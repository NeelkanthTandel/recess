import Foundation

enum TimeUnit: String, Codable, CaseIterable, Identifiable {
    case seconds, minutes, hours, days

    var id: String { rawValue }

    var seconds: TimeInterval {
        switch self {
        case .seconds: 1
        case .minutes: 60
        case .hours: 3600
        case .days: 86400
        }
    }

    func name(plural: Bool) -> String {
        switch self {
        case .seconds: plural ? "seconds" : "second"
        case .minutes: plural ? "minutes" : "minute"
        case .hours: plural ? "hours" : "hour"
        case .days: plural ? "days" : "day"
        }
    }

    var brief: String {
        switch self {
        case .seconds: "s"
        case .minutes: "m"
        case .hours: "h"
        case .days: "d"
        }
    }
}

/// An amount plus units, e.g. 10 minutes.
struct Interval: Codable, Hashable {
    var amount: Double
    var units: TimeUnit

    init(_ amount: Double, _ units: TimeUnit) {
        self.amount = amount
        self.units = units
    }

    static func seconds(_ v: Double) -> Interval { Interval(v, .seconds) }
    static func minutes(_ v: Double) -> Interval { Interval(v, .minutes) }
    static func hours(_ v: Double) -> Interval { Interval(v, .hours) }

    var timeInterval: TimeInterval { amount * units.seconds }

    var fullDescription: String {
        let n = amount == amount.rounded() ? String(Format.int(amount)) : String(format: "%.1f", amount)
        return "\(n) \(units.name(plural: amount != 1))"
    }
}

enum Format {
    /// Converts to Int without trapping on NaN, infinity or out-of-range values.
    static func int(_ value: Double) -> Int {
        guard value.isFinite else { return 0 }
        return Int(max(-1e15, min(1e15, value)))
    }

    /// "1:05:09", "4:09", "0:09"
    static func countdown(_ seconds: TimeInterval) -> String {
        let s = max(0, int(seconds.rounded(.up)))
        let h = s / 3600, m = (s % 3600) / 60, sec = s % 60
        return h > 0 ? String(format: "%d:%02d:%02d", h, m, sec) : String(format: "%d:%02d", m, sec)
    }

    /// "5m", "1h 5m", "30s"
    static func brief(_ seconds: TimeInterval) -> String {
        let s = max(0, int(seconds.rounded()))
        if s < 60 { return "\(s)s" }
        let h = s / 3600, m = (s % 3600) / 60
        if h == 0 { return "\(m)m" }
        if h >= 24 { return "\(h / 24)d \(h % 24)h" }
        return m == 0 ? "\(h)h" : "\(h)h \(m)m"
    }

    /// "5 minutes", "1 hour, 5 minutes"
    static func full(_ seconds: TimeInterval) -> String {
        let s = max(0, int(seconds.rounded()))
        func part(_ v: Int, _ unit: String) -> String { "\(v) \(unit)\(v == 1 ? "" : "s")" }
        if s < 60 { return part(s, "second") }
        let d = s / 86400, h = (s % 86400) / 3600, m = (s % 3600) / 60
        var parts: [String] = []
        if d > 0 { parts.append(part(d, "day")) }
        if h > 0 { parts.append(part(h, "hour")) }
        if m > 0 && d == 0 { parts.append(part(m, "minute")) }
        return parts.joined(separator: ", ")
    }

    static func time(_ date: Date) -> String {
        date.formatted(date: .omitted, time: .shortened)
    }

    static func relative(_ date: Date, from now: Date = Date()) -> String {
        let diff = date.timeIntervalSince(now)
        if abs(diff) < 1 { return "now" }
        return diff > 0 ? "in \(full(diff))" : "\(full(-diff)) ago"
    }

    static func absolute(_ date: Date) -> String {
        if Calendar.current.isDateInToday(date) { return "today at \(time(date))" }
        if Calendar.current.isDateInTomorrow(date) { return "tomorrow at \(time(date))" }
        if Calendar.current.isDateInYesterday(date) { return "yesterday at \(time(date))" }
        return date.formatted(date: .abbreviated, time: .shortened)
    }
}

/// A time of day (hour and minute).
struct TimeOfDay: Codable, Hashable {
    var hour: Int
    var minute: Int

    func date(on day: Date, calendar: Calendar = .current) -> Date {
        calendar.date(bySettingHour: hour, minute: minute, second: 0, of: day) ?? day
    }

    var asDate: Date {
        get { date(on: Date()) }
        set {
            let c = Calendar.current.dateComponents([.hour, .minute], from: newValue)
            hour = c.hour ?? 0
            minute = c.minute ?? 0
        }
    }
}
