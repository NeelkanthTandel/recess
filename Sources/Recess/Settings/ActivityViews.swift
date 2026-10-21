import Charts
import SwiftUI

struct ActivitySettingsView: View {
    enum Page: String, CaseIterable, Identifiable {
        case activity = "Activity", statistics = "Statistics", log = "Log"; var id: String { rawValue }
    }

    @EnvironmentObject var model: AppModel
    @ObservedObject private var recorder = ActivityRecorder.shared
    @State private var page: Page = .statistics

    var body: some View {
        VStack(spacing: 0) {
            HStack {
                Picker("", selection: $page) { ForEach(Page.allCases) { Text($0.rawValue).tag($0) } }
                    .pickerStyle(.segmented).labelsHidden().frame(width: 300)
                Spacer()
                Toggle("Track Breaks", isOn: $model.settings.trackBreaks)
                    .help("Log each break you take, postpone or skip, so you can see how you're doing.")
                Toggle("Track Apps", isOn: $model.settings.trackApps)
                    .help("Log which apps are in front and for how long.")
            }
            .padding(14)
            Divider()
            switch page {
            case .activity: AppActivityPage()
            case .statistics: StatisticsPage()
            case .log: LogPage()
            }
        }
        .navigationTitle("Activity")
    }
}

private struct StatisticsPage: View {
    enum Metric: String, CaseIterable, Identifiable {
        case postponed = "Postpones per day", skipped = "Skips per day", done = "Breaks taken per day", duration =
            "Minutes on break per day", all = "Everything"
        var id: String { rawValue }
    }

    @EnvironmentObject var model: AppModel
    @ObservedObject private var recorder = ActivityRecorder.shared
    @State private var metric: Metric = .all
    @State private var breakID: UUID?
    @State private var days = 14

    var body: some View {
        let stats = recorder.stats(days: days, breakID: breakID)
        let _ = recorder.revision
        VStack(alignment: .leading, spacing: 14) {
            HStack {
                Picker("Show", selection: $metric) { ForEach(Metric.allCases) { Text($0.rawValue).tag($0) } }.fixedSize()
                Picker("for", selection: $breakID) {
                    Text("All Breaks").tag(UUID?.none)
                    ForEach(model.breaks) { Text($0.name).tag(Optional($0.id)) }
                }
                .fixedSize()
                Picker("over", selection: $days) {
                    Text("7 days").tag(7); Text("14 days").tag(14); Text("30 days").tag(30); Text("90 days").tag(90)
                }
                .fixedSize()
            }
            if !model.settings.trackBreaks {
                ContentUnavailableView(
                    "No Statistics", systemImage: "chart.bar", description: Text("Turn on Track Breaks to record statistics."))
            } else {
                chart(stats)
                    .frame(minHeight: 260)
                summary(stats)
            }
            Spacer()
        }
        .padding(20)
    }

    @ViewBuilder private func chart(_ stats: [ActivityRecorder.DayStats]) -> some View {
        Chart {
            ForEach(stats) { s in
                switch metric {
                case .postponed: BarMark(x: .value("Day", s.day, unit: .day), y: .value("Postponed", s.postponed)).foregroundStyle(.orange)
                case .skipped: BarMark(x: .value("Day", s.day, unit: .day), y: .value("Skipped", s.skipped)).foregroundStyle(.red)
                case .done: BarMark(x: .value("Day", s.day, unit: .day), y: .value("Done", s.done)).foregroundStyle(.green)
                case .duration: BarMark(x: .value("Day", s.day, unit: .day), y: .value("Minutes", s.duration / 60)).foregroundStyle(.blue)
                case .all:
                    BarMark(x: .value("Day", s.day, unit: .day), y: .value("Count", s.done)).foregroundStyle(by: .value("Kind", "Done"))
                    BarMark(x: .value("Day", s.day, unit: .day), y: .value("Count", s.postponed)).foregroundStyle(
                        by: .value("Kind", "Postponed"))
                    BarMark(x: .value("Day", s.day, unit: .day), y: .value("Count", s.skipped)).foregroundStyle(
                        by: .value("Kind", "Skipped"))
                }
            }
        }
        .chartForegroundStyleScale(["Done": Color.green, "Postponed": Color.orange, "Skipped": Color.red])
        .chartYAxisLabel(metric == .duration ? "minutes" : "count")
    }

    private func summary(_ stats: [ActivityRecorder.DayStats]) -> some View {
        let p = stats.reduce(0) { $0 + $1.postponed }
        let s = stats.reduce(0) { $0 + $1.skipped }
        let d = stats.reduce(0) { $0 + $1.done }
        let t = stats.reduce(0) { $0 + $1.duration }
        return HStack(spacing: 30) {
            stat("Done", "\(d)", .green)
            stat("Postponed", "\(p)", .orange)
            stat("Skipped", "\(s)", .red)
            stat("Time on break", Format.full(t), .blue)
            stat("Taken", d + s > 0 ? "\(Int(Double(d) / Double(d + s) * 100))%" : "–", .primary)
        }
    }

    private func stat(_ title: String, _ value: String, _ color: Color) -> some View {
        VStack(alignment: .leading) {
            Text(value).font(.title2.weight(.semibold)).foregroundStyle(color)
            Text(title).font(.caption).foregroundStyle(.secondary)
        }
    }
}

private struct AppActivityPage: View {
    @EnvironmentObject var model: AppModel
    @ObservedObject private var recorder = ActivityRecorder.shared
    @State private var day = Calendar.current.startOfDay(for: Date())
    @State private var confirmClear = false

    var body: some View {
        let _ = recorder.revision
        let record = recorder.record(for: day)
        let totals = Dictionary(grouping: record.apps.filter { !$0.bundleID.isEmpty }, by: \.bundleID)
            .map { (id: $0.key, name: $0.value.first?.name ?? $0.key, total: $0.value.reduce(0) { $0 + $1.duration }) }
            .sorted { $0.total > $1.total }
        let active = totals.reduce(0) { $0 + $1.total }

        VStack(alignment: .leading, spacing: 12) {
            HStack {
                Button {
                    day = Calendar.current.date(byAdding: .day, value: -1, to: day)!
                } label: {
                    Image(systemName: "chevron.left")
                }
                Text(day.formatted(date: .complete, time: .omitted)).font(.headline).frame(minWidth: 240)
                Button {
                    day = Calendar.current.date(byAdding: .day, value: 1, to: day)!
                } label: {
                    Image(systemName: "chevron.right")
                }
                .disabled(Calendar.current.isDateInToday(day))
                Spacer()
                Button("Delete All Activity…") { confirmClear = true }
                    .help("Delete every recorded break and app-usage entry.")
            }
            if !model.settings.trackApps && record.apps.isEmpty {
                ContentUnavailableView(
                    "No Activity", systemImage: "clock",
                    description: Text("Recess can track app usage and breaks taken. Turn on Track Apps above to enable."))
            } else {
                Timeline(record: record)
                    .frame(height: 46)
                Text(
                    "Active for \(Format.full(active)); \(record.events.filter { $0.kind == .done || $0.kind == .endedEarly }.count) breaks taken"
                )
                .foregroundStyle(.secondary)
                List(totals, id: \.id) { t in
                    HStack {
                        Image(nsImage: AppInfo.icon(forBundleID: t.id)).resizable().frame(width: 20, height: 20)
                        Text(t.name)
                        Spacer()
                        ProgressView(value: active > 0 ? t.total / active : 0).frame(width: 160)
                        Text(Format.brief(t.total)).monospacedDigit().frame(width: 70, alignment: .trailing)
                    }
                }
            }
        }
        .padding(20)
        .alert("Delete all recorded activity?", isPresented: $confirmClear) {
            Button("Clear", role: .destructive) { recorder.clearAll() }
            Button("Cancel", role: .cancel) {}
        } message: {
            Text("This can't be undone.")
        }
    }
}

/// A 24-hour bar showing app usage colours and break markers.
private struct Timeline: View {
    let record: ActivityDayRecord

    var body: some View {
        GeometryReader { geo in
            let dayStart = ActivityRecorder.dayFormatter.date(from: record.day) ?? Calendar.current.startOfDay(for: Date())
            let scale = geo.size.width / 86400
            ZStack(alignment: .topLeading) {
                RoundedRectangle(cornerRadius: 6).fill(.quaternary)
                ForEach(Array(record.apps.enumerated()), id: \.offset) { _, span in
                    Rectangle()
                        .fill(span.bundleID.isEmpty ? Color.gray.opacity(0.25) : Self.color(for: span.bundleID))
                        .frame(width: max(1, span.duration * scale), height: 28)
                        .offset(x: span.start.timeIntervalSince(dayStart) * scale, y: 0)
                        .help("\(span.name): \(Format.time(span.start))–\(Format.time(span.end))")
                }
                ForEach(record.events.filter { $0.kind != .started }) { e in
                    Circle()
                        .fill(e.kind == .skipped ? Color.red : (e.kind == .postponed ? Color.orange : Color.green))
                        .frame(width: 8, height: 8)
                        .offset(x: e.date.timeIntervalSince(dayStart) * scale - 4, y: 34)
                        .help("\(e.breakName) \(e.kind.title.lowercased()) at \(Format.time(e.date))")
                }
                ForEach([0, 6, 12, 18], id: \.self) { h in
                    Text("\(h):00").font(.system(size: 9)).foregroundStyle(.secondary)
                        .offset(x: CGFloat(h) * 3600 * scale + 2, y: 6)
                }
            }
        }
    }

    static func color(for id: String) -> Color {
        var hash: UInt64 = 5381
        for b in id.utf8 { hash = (hash &* 33) &+ UInt64(b) }
        return Color(hue: Double(hash % 360) / 360, saturation: 0.55, brightness: 0.85)
    }
}

private struct LogPage: View {
    @ObservedObject private var recorder = ActivityRecorder.shared

    var body: some View {
        let _ = recorder.revision
        let events = recorder.availableDays().prefix(14).flatMap { recorder.record(for: $0).events }.sorted { $0.date > $1.date }
        Table(events) {
            TableColumn("Time") { Text($0.date.formatted(date: .abbreviated, time: .shortened)) }
            TableColumn("Break", value: \.breakName)
            TableColumn("Event") { Text($0.kind.title) }
            TableColumn("Duration") { Text($0.duration > 0 ? Format.full($0.duration) : "") }
            TableColumn("Detail", value: \.detail)
        }
        .overlay {
            if events.isEmpty {
                ContentUnavailableView(
                    "No Events", systemImage: "list.bullet", description: Text("Breaks you take, postpone and skip will be listed here."))
            }
        }
    }
}
