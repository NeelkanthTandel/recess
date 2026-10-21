import Foundation

enum RuleCondition: String, Codable, CaseIterable, Identifiable {
    case frontApp, openApp, focus, fullscreen, screensaver, displayAsleep, calendarEvent, script
    var id: String { rawValue }
    var title: String {
        switch self {
        case .frontApp: "Front app"
        case .openApp: "Open app"
        case .focus: "Focus / Do Not Disturb"
        case .fullscreen: "Fullscreen app"
        case .screensaver: "Screensaver"
        case .displayAsleep: "Display asleep"
        case .calendarEvent: "Calendar event"
        case .script: "Script result"
        }
    }
    var needsApps: Bool { self == .frontApp || self == .openApp }
    var symbol: String {
        switch self {
        case .frontApp: "macwindow"
        case .openApp: "app.badge"
        case .focus: "moon.circle.fill"
        case .fullscreen: "arrow.up.left.and.arrow.down.right"
        case .screensaver: "sparkles.tv"
        case .displayAsleep: "display"
        case .calendarEvent: "calendar"
        case .script: "applescript"
        }
    }
}

enum RuleAction: String, Codable, CaseIterable, Identifiable {
    case postpone1, postpone2, postpone5, postpone10, postpone20, postpone30, skip, treatAsNaturalBreak, ignoreNaturalBreak
    var id: String { rawValue }
    var title: String {
        switch self {
        case .postpone1: "postpone 1 minute"
        case .postpone2: "postpone 2 minutes"
        case .postpone5: "postpone 5 minutes"
        case .postpone10: "postpone 10 minutes"
        case .postpone20: "postpone 20 minutes"
        case .postpone30: "postpone 30 minutes"
        case .skip: "skip the break"
        case .treatAsNaturalBreak: "count it as time away"
        case .ignoreNaturalBreak: "don't count it as time away"
        }
    }
    var postponeMinutes: Double? {
        switch self {
        case .postpone1: 1
        case .postpone2: 2
        case .postpone5: 5
        case .postpone10: 10
        case .postpone20: 20
        case .postpone30: 30
        default: nil
        }
    }
    var isDueAction: Bool { postponeMinutes != nil || self == .skip }
}

struct Rule: Codable, Identifiable, Hashable {
    var id = UUID()
    var enabled = true
    var condition: RuleCondition = .frontApp
    /// "is not" instead of "is"
    var negate = false
    /// Bundle identifiers for app conditions.
    var apps: [String] = []
    /// Calendar identifier (nil = any) for calendar conditions.
    var calendarIdentifier: String? = nil
    var calendarFilter = ""
    var scriptFilename = ""
    var action: RuleAction = .postpone5
    /// Empty = applies to all breaks.
    var breakIDs: [UUID] = []

    func applies(to b: Break) -> Bool { breakIDs.isEmpty || breakIDs.contains(b.id) }
}
