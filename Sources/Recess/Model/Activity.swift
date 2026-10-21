import Foundation

enum BreakEventKind: String, Codable {
    case started, done, endedEarly, postponed, skipped, naturalBreak
    var title: String {
        switch self {
        case .started: "Started"
        case .done: "Done"
        case .endedEarly: "Ended early"
        case .postponed: "Postponed"
        case .skipped: "Skipped"
        case .naturalBreak: "Natural break"
        }
    }
}

struct BreakEvent: Codable, Hashable, Identifiable {
    var id = UUID()
    var date: Date
    var breakID: UUID
    var breakName: String
    var kind: BreakEventKind
    var duration: TimeInterval = 0
    var detail: String = ""
}

/// A contiguous span of time with one app frontmost (or idle when `bundleID` is empty).
struct AppSpan: Codable, Hashable {
    var bundleID: String
    var name: String
    var start: Date
    var end: Date
    var duration: TimeInterval { end.timeIntervalSince(start) }
}

struct ActivityDayRecord: Codable {
    var day: String  // yyyy-MM-dd
    var events: [BreakEvent] = []
    var apps: [AppSpan] = []
}
