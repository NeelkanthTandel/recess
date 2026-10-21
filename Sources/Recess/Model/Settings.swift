import Foundation

enum StatusIcon: String, Codable, CaseIterable, Identifiable {
    case none, appIcon, palmTree, beachUmbrella, sunHorizon, cup, waves, cardio, strength, mindBody, lastDoneLabel, nextDueLabel
    var id: String { rawValue }
    var title: String {
        switch self {
        case .none: "No Icon"
        case .appIcon: "App Icon"
        case .palmTree: "Palm Tree"
        case .beachUmbrella: "Beach Umbrella"
        case .sunHorizon: "Sun on Horizon"
        case .cup: "Cup and Saucer"
        case .waves: "Water Waves"
        case .cardio: "Mixed Cardio"
        case .strength: "Strength Training"
        case .mindBody: "Mind and Body"
        case .lastDoneLabel: "Colour of the last break"
        case .nextDueLabel: "Colour of the next break"
        }
    }
    var symbol: String? {
        switch self {
        case .none, .lastDoneLabel, .nextDueLabel: nil
        case .appIcon: "pause.circle"
        case .palmTree: "tree"
        case .beachUmbrella: "beach.umbrella"
        case .sunHorizon: "sun.horizon"
        case .cup: "cup.and.saucer"
        case .waves: "water.waves"
        case .cardio: "figure.mixed.cardio"
        case .strength: "figure.strengthtraining.traditional"
        case .mindBody: "figure.mind.and.body"
        }
    }
}

enum StatusTitle: String, Codable, CaseIterable, Identifiable {
    case onlyIcon, briefSinceLast, fullSinceLast, doneTimeOfLast, durationOfLast, briefCountdown, fullCountdown, dueTimeOfNext,
        finishTimeOfNext, durationOfNext
    var id: String { rawValue }
    var title: String {
        switch self {
        case .onlyIcon: "Nothing (icon only)"
        case .briefSinceLast: "Since last break (short)"
        case .fullSinceLast: "Since last break (long)"
        case .doneTimeOfLast: "When the last break ended"
        case .durationOfLast: "Length of the last break"
        case .briefCountdown: "Until next break (short)"
        case .fullCountdown: "Until next break (mm:ss)"
        case .dueTimeOfNext: "When the next break starts"
        case .finishTimeOfNext: "When the next break ends"
        case .durationOfNext: "Length of the next break"
        }
    }
}

enum IdleDetector: String, Codable, CaseIterable, Identifiable {
    case eventSource, eventMonitor, none
    var id: String { rawValue }
    var title: String {
        switch self {
        case .eventSource: "System idle timer"
        case .eventMonitor: "Input monitoring"
        case .none: "None"
        }
    }
}

struct Shortcuts: Codable, Hashable {
    var startNext: KeyCombo? = nil
    var postpone1: KeyCombo? = nil
    var postpone2: KeyCombo? = nil
    var skip: KeyCombo? = nil
    var pauseIndefinitely: KeyCombo? = nil
    var resume: KeyCombo? = nil
    var reset: KeyCombo? = nil
    var stopSound: KeyCombo? = nil
}

struct AppSettings: Codable, Hashable {
    // General / Options
    var showDockIcon = false
    var showStatusMenu = true
    var statusIcon: StatusIcon = .cup
    var statusTitle: StatusTitle = .briefCountdown
    var statusOnlyLong = false
    var relativeDates = true
    var launchAtLogin = false
    var hasCompletedSetup = false

    // Shortcuts
    var shortcuts = Shortcuts()

    // Rules
    var rules: [Rule] = []

    // Activity
    var trackBreaks = true
    var trackApps = false

    // Advanced
    var idleDetector: IdleDetector = .eventSource
    var naturalBreakThreshold: Double = 60
    var hidePause = false
    var hideMakeDue = false
    var hideReset = false
    var hideDisable = false
    var hideDelete = false
    var hideQuit = false
    var disableAppSwitching = false
    var showMenuBarDuringBreak = false
    /// Accept recess:// links from other apps and web pages.
    var allowURLCommands = true
}
