import AppKit
import Combine

/// Observable app state shared by the scheduler, status item and settings UI.
@MainActor
final class AppModel: ObservableObject {
    static let shared = AppModel()

    @Published var breaks: [Break] {
        didSet { scheduleSave() }
    }
    @Published var settings: AppSettings {
        didSet {
            scheduleSave()
            if oldValue.shortcuts != settings.shortcuts { NotificationCenter.default.post(name: .shortcutsChanged, object: nil) }
            if oldValue.showDockIcon != settings.showDockIcon || oldValue.showStatusMenu != settings.showStatusMenu {
                NotificationCenter.default.post(name: .presentationChanged, object: nil)
            }
        }
    }
    /// The current time, as of the last scheduler cycle. Views that show relative times
    /// observe `Ticker` so the rest of the UI isn't rebuilt every second.
    var now: Date { Ticker.shared.now }
    /// Identifier of the break currently being shown, if any.
    @Published var activeBreakID: UUID?

    private var saveWork: DispatchWorkItem?

    private init() {
        settings = Store.loadSettings() ?? AppSettings()
        // An empty list is respected (the user deleted every break); only a missing or
        // unreadable file falls back to the defaults.
        if let saved = Store.loadBreaks() {
            breaks = saved
        } else {
            breaks = [Break.makeNormal(), Break.makeMicro()]
        }
    }

    func index(of id: UUID) -> Int? { breaks.firstIndex { $0.id == id } }

    func breakWith(id: UUID) -> Break? { breaks.first { $0.id == id } }

    func binding(for id: UUID) -> Break? { breakWith(id: id) }

    func update(_ id: UUID, _ change: (inout Break) -> Void) {
        guard let i = index(of: id) else { return }
        var b = breaks[i]
        change(&b)
        if b != breaks[i] { breaks[i] = b }
    }

    /// The break due soonest (ignoring paused/disabled ones).
    var nextBreak: Break? {
        breaks.filter { $0.isActive && $0.state.nextDue != nil }
            .filter { !settings.statusOnlyLong || $0.isLong }
            .min { ($0.state.nextDue ?? .distantFuture) < ($1.state.nextDue ?? .distantFuture) }
    }

    var nextBreakAnyLength: Break? {
        breaks.filter { $0.isActive && $0.state.nextDue != nil }
            .min { ($0.state.nextDue ?? .distantFuture) < ($1.state.nextDue ?? .distantFuture) }
    }

    var lastDoneBreak: Break? {
        breaks.filter { $0.state.lastDone != nil }
            .filter { !settings.statusOnlyLong || $0.isLong }
            .max { ($0.state.lastDone ?? .distantPast) < ($1.state.lastDone ?? .distantPast) }
    }

    var allPaused: Bool { !breaks.isEmpty && breaks.allSatisfy { !$0.enabled || $0.isPaused } }

    func dateString(_ date: Date?) -> String {
        guard let date else { return "never" }
        if date == .distantFuture { return "no upcoming events" }
        return settings.relativeDates ? Format.relative(date, from: now) : Format.absolute(date)
    }

    // MARK: - Persistence

    /// Throttled rather than debounced, so constant updates (e.g. during idle) still get saved.
    private func scheduleSave() {
        guard saveWork == nil else { return }
        let work = DispatchWorkItem { [weak self] in self?.saveNow() }
        saveWork = work
        DispatchQueue.main.asyncAfter(deadline: .now() + 1, execute: work)
    }

    func saveNow() {
        saveWork?.cancel()
        saveWork = nil
        Store.write(settings, to: Store.settingsURL)
        Store.write(breaks, to: Store.breaksURL)
    }
}

/// Per-second values (clock and idle time), kept apart from `AppModel` so that observing
/// the model doesn't re-render the settings window every second.
@MainActor
final class Ticker: ObservableObject {
    static let shared = Ticker()
    @Published var now = Date()
    /// Seconds the Mac has been idle (for display/debugging).
    @Published var idleSeconds: TimeInterval = 0
    @Published var isNaturalBreak = false
}

extension Notification.Name {
    static let shortcutsChanged = Notification.Name("RecessShortcutsChanged")
    static let presentationChanged = Notification.Name("RecessPresentationChanged")
}
