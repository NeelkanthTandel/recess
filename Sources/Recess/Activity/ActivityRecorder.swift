import AppKit

/// Records breaks taken and app usage per day, and summarises them for the statistics chart.
@MainActor
final class ActivityRecorder: ObservableObject {
    static let shared = ActivityRecorder()

    @Published private(set) var revision = 0
    private var days: [String: ActivityDayRecord] = [:]
    private var dirty: Set<String> = []
    private var lastSave = Date()
    private var lastTick: Date?

    static let dayFormatter: DateFormatter = {
        let f = DateFormatter()
        f.dateFormat = "yyyy-MM-dd"
        f.locale = Locale(identifier: "en_US_POSIX")
        return f
    }()

    static func key(_ date: Date) -> String { dayFormatter.string(from: date) }

    private var settings: AppSettings { AppModel.shared.settings }

    func record(for date: Date) -> ActivityDayRecord {
        let k = Self.key(date)
        if let r = days[k] { return r }
        let url = Store.activityFolder.appendingPathComponent("\(k).json")
        var r = ActivityDayRecord(day: k)
        if FileManager.default.fileExists(atPath: url.path) {
            if let saved = Store.read(ActivityDayRecord.self, from: url) {
                r = saved
            } else {
                // Don't let a later save overwrite a day we couldn't read.
                Store.backUpUnreadable(url)
            }
        }
        days[k] = r
        return r
    }

    /// `notify` is false for the per-second app-usage ticks, so open Activity pages aren't
    /// redrawn every second; they refresh when breaks are logged or the page is reopened.
    private func mutate(_ date: Date, notify: Bool = true, _ change: (inout ActivityDayRecord) -> Void) {
        var r = record(for: date)
        change(&r)
        days[r.day] = r
        dirty.insert(r.day)
        if notify { revision += 1 }
    }

    func log(_ b: Break, _ kind: BreakEventKind, duration: TimeInterval = 0, detail: String = "") {
        guard settings.trackBreaks else { return }
        let now = Date()
        mutate(now) {
            $0.events.append(BreakEvent(date: now, breakID: b.id, breakName: b.name, kind: kind, duration: duration, detail: detail))
        }
        saveIfNeeded(force: true)
    }

    func tick(now: Date, idle: Bool) {
        defer { lastTick = now }
        guard settings.trackApps else { return }
        let gap = lastTick.map { now.timeIntervalSince($0) } ?? 1
        guard gap < 10 else { return }
        let front = NSWorkspace.shared.frontmostApplication
        let id = idle ? "" : (front?.bundleIdentifier ?? "")
        let name = idle ? "Idle" : (front?.localizedName ?? "Unknown")
        mutate(now, notify: false) { r in
            if var last = r.apps.last, last.bundleID == id, now.timeIntervalSince(last.end) < 10 {
                last.end = now
                r.apps[r.apps.count - 1] = last
            } else {
                r.apps.append(AppSpan(bundleID: id, name: name, start: now.addingTimeInterval(-gap), end: now))
            }
        }
        saveIfNeeded(force: false)
    }

    func saveIfNeeded(force: Bool) {
        guard force || Date().timeIntervalSince(lastSave) > 60 else { return }
        save()
    }

    func save() {
        lastSave = Date()
        for k in dirty {
            if let r = days[k] { Store.write(r, to: Store.activityFolder.appendingPathComponent("\(k).json")) }
        }
        dirty.removeAll()
        // Keep memory small: drop cached days other than today.
        let today = Self.key(Date())
        days = days.filter { $0.key == today }
    }

    func availableDays() -> [Date] {
        let files = (try? FileManager.default.contentsOfDirectory(atPath: Store.activityFolder.path)) ?? []
        var keys = Set(files.filter { $0.hasSuffix(".json") }.map { String($0.dropLast(5)) })
        keys.insert(Self.key(Date()))
        return keys.compactMap { Self.dayFormatter.date(from: $0) }.sorted(by: >)
    }

    func clearAll() {
        days.removeAll()
        dirty.removeAll()
        if let files = try? FileManager.default.contentsOfDirectory(at: Store.activityFolder, includingPropertiesForKeys: nil) {
            files.forEach { try? FileManager.default.removeItem(at: $0) }
        }
        revision += 1
    }

    /// Per-day totals for the statistics chart.
    struct DayStats: Identifiable {
        var day: Date
        var postponed = 0, skipped = 0, done = 0
        var duration: TimeInterval = 0
        var id: Date { day }
    }

    func stats(days count: Int, breakID: UUID?) -> [DayStats] {
        let cal = Calendar.current
        let today = cal.startOfDay(for: Date())
        return (0..<count).reversed().map { offset in
            let day = cal.date(byAdding: .day, value: -offset, to: today)!
            var s = DayStats(day: day)
            for e in record(for: day).events where breakID == nil || e.breakID == breakID {
                switch e.kind {
                case .postponed: s.postponed += 1
                case .skipped: s.skipped += 1
                case .done, .endedEarly:
                    s.done += 1
                    s.duration += e.duration
                case .naturalBreak, .started: break
                }
            }
            return s
        }
    }
}
