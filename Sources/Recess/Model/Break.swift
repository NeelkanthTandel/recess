import Foundation

// MARK: - Schedule

/// What the next due date is measured from.
enum DueAdjustment: String, Codable, CaseIterable, Identifiable {
    case lastDue, lastDone, fixedTime, calendar
    var id: String { rawValue }
    var title: String {
        switch self {
        case .lastDue: "When the last one was due"
        case .lastDone: "When the last one ended"
        case .fixedTime: "A set minute each hour"
        case .calendar: "Calendar events"
        }
    }
}

/// What happens to the countdown while the user is away from the Mac.
enum IdleHandling: String, Codable, CaseIterable, Identifiable {
    case continueCountdown, pauseCountdown, countBackwards
    var id: String { rawValue }
    var title: String {
        switch self {
        case .continueCountdown: "Keep counting down"
        case .pauseCountdown: "Stop the clock"
        case .countBackwards: "Wind the clock back"
        }
    }
}

/// How a break begins once it is due.
enum DueBehavior: String, Codable, CaseIterable, Identifiable {
    case startImmediately, waitForNaturalBreak, ask
    var id: String { rawValue }
    var title: String {
        switch self {
        case .startImmediately: "Start right away"
        case .waitForNaturalBreak: "Wait for a pause in my work"
        case .ask: "Ask me first"
        }
    }
}

enum Availability: String, Codable, CaseIterable, Identifiable {
    case allDay, between
    var id: String { rawValue }
    var title: String { self == .allDay ? "Any time" : "Only between" }
}

struct BreakSchedule: Codable, Hashable {
    var duration: Interval = .minutes(10)
    var frequency: Interval = .minutes(60)
    var adjustment: DueAdjustment = .lastDue
    var fixedMinutesPastHour: Int = 0
    var calendarIdentifier: String? = nil
    var calendarEventFilter: String = ""
    var availability: Availability = .allDay
    var fromTime = TimeOfDay(hour: 9, minute: 0)
    var toTime = TimeOfDay(hour: 17, minute: 0)
    /// Calendar weekday numbers (1 = Sunday … 7 = Saturday) on which the break may occur.
    var weekdays: Set<Int> = Set(1...7)
    var idleHandling: IdleHandling = .continueCountdown
    var resetWhenIdle: Bool = true
    /// nil = use the break's own length.
    var resetWhenIdleInterval: Interval? = nil
    var resetWhenFinishingHigherPriority: Bool = true
    var skipForHigherPriority: Bool = true
    var skipForHigherPriorityInterval: Interval = .minutes(5)
    var dueBehavior: DueBehavior = .waitForNaturalBreak
    var askAutoStart: Interval = .minutes(1)

    /// The fields that decide when the break happens; editing any of them re-times the break.
    struct Timing: Hashable {
        var duration: Interval, frequency: Interval, adjustment: DueAdjustment, fixedMinutes: Int
        var calendarIdentifier: String?, calendarEventFilter: String
        var availability: Availability, fromTime: TimeOfDay, toTime: TimeOfDay, weekdays: Set<Int>
    }

    var timing: Timing {
        Timing(
            duration: duration, frequency: frequency, adjustment: adjustment, fixedMinutes: fixedMinutesPastHour,
            calendarIdentifier: calendarIdentifier, calendarEventFilter: calendarEventFilter,
            availability: availability, fromTime: fromTime, toTime: toTime, weekdays: weekdays)
    }
}

// MARK: - Controls

enum LimitKind: String, Codable, CaseIterable, Identifiable {
    case perBreak, perHour, perDay
    var id: String { rawValue }
    var title: String {
        switch self {
        case .perBreak: "break"
        case .perHour: "hour"
        case .perDay: "day"
        }
    }
}

struct BreakControls: Codable, Hashable {
    var fadeInSeconds: Double = 2
    var fadeOutSeconds: Double = 2
    var showPostpone1 = true
    var postponeMinutes1: Double = 1
    var showPostpone2 = true
    var postponeMinutes2: Double = 5
    var showSkip = true
    var showEndEarly = false
    var endEarlyAfter: Interval = .minutes(5)
    var endWhenDone = false
    var disableButtonsSeconds: Double = 0
    var showCounters = false
    var limitPostpone = false
    var postponeLimit = 3
    var postponeLimitKind: LimitKind = .perBreak
    var limitSkip = false
    var skipLimit = 1
    var skipLimitKind: LimitKind = .perHour
}

// MARK: - Appearance

enum OtherScreensMode: String, Codable, CaseIterable, Identifiable {
    case theme, dim, none
    var id: String { rawValue }
    var title: String {
        switch self {
        case .theme: "Same as the main screen"
        case .dim: "Darken only"
        case .none: "Leave alone"
        }
    }
}

enum MainScreenChoice: String, Codable, CaseIterable, Identifiable {
    case activeMenubar, primary
    var id: String { rawValue }
    var title: String {
        switch self {
        case .activeMenubar: "Screen I'm using"
        case .primary: "Main display"
        }
    }
}

struct BreakAppearance: Codable, Hashable {
    var themeID: String = ThemeID.defaultTheme
    var backgroundColor: RGBA = RGBA(r: 0.08, g: 0.1, b: 0.14, a: 0.88)
    var mainScreen: MainScreenChoice = .activeMenubar
    var otherScreens: OtherScreensMode = .dim
    var otherScreensColor: RGBA = RGBA(r: 0, g: 0, b: 0, a: 0.75)
    var message: String = ""
}

enum ThemeID {
    static let defaultTheme = "builtin.default"
    static let blank = "builtin.blank"
    static let none = "builtin.none"
}

struct RGBA: Codable, Hashable {
    var r: Double, g: Double, b: Double, a: Double = 1
}

// MARK: - Break

struct Break: Codable, Identifiable, Hashable {
    var id: UUID = UUID()
    var name: String = "Normal"
    var comments: String = ""
    var color: RGBA = RGBA(r: 0.2, g: 0.6, b: 0.4)
    var labelAsPie = false
    var startShortcut: KeyCombo? = nil
    var enabled = true
    var schedule = BreakSchedule()
    var controls = BreakControls()
    var appearance = BreakAppearance()
    var actions: [BreakAction] = []
    var state = BreakState()

    var duration: TimeInterval {
        if schedule.adjustment == .calendar, let start = state.nextEventStart, let end = state.nextEventEnd {
            return max(5, end.timeIntervalSince(start))
        }
        return Self.clamp(schedule.duration.timeInterval, 1...(24 * 3600))
    }

    var frequency: TimeInterval { Self.clamp(schedule.frequency.timeInterval, 10...(30 * 86400)) }

    /// Keeps intervals from hand-edited JSON (NaN, huge, negative) within sane bounds.
    static func clamp(_ v: TimeInterval, _ range: ClosedRange<TimeInterval>) -> TimeInterval {
        v.isFinite ? min(max(v, range.lowerBound), range.upperBound) : range.lowerBound
    }

    var isLong: Bool { schedule.duration.timeInterval >= 60 }

    var isPaused: Bool {
        guard let until = state.pausedUntil else { return false }
        return until > Date()
    }

    var isActive: Bool { enabled && !isPaused }

    var postponesRemaining: Int? {
        guard controls.limitPostpone else { return nil }
        let used: Int
        switch controls.postponeLimitKind {
        case .perBreak: used = state.counts.breakPostpone
        case .perHour: used = state.counts.hourPostpone
        case .perDay: used = state.counts.todayPostpone
        }
        return max(0, controls.postponeLimit - used)
    }

    var skipsRemaining: Int? {
        guard controls.limitSkip else { return nil }
        let used: Int
        switch controls.skipLimitKind {
        case .perBreak: used = state.counts.breakSkip
        case .perHour: used = state.counts.hourSkip
        case .perDay: used = state.counts.todaySkip
        }
        return max(0, controls.skipLimit - used)
    }

    var canPostpone: Bool { (postponesRemaining ?? 1) > 0 }
    var canSkip: Bool { (skipsRemaining ?? 1) > 0 }

    var countersString: String {
        let c = state.counts
        var parts: [String] = []
        parts.append("Postponed \(c.todayPostpone)×")
        parts.append("skipped \(c.todaySkip)×")
        parts.append("done \(c.todayDone)× today")
        if c.todayDuration > 0 { parts.append("\(Format.full(c.todayDuration)) in all") }
        return parts.joined(separator: ", ")
    }

    static func makeNormal() -> Break {
        var b = Break()
        b.name = "Normal"
        b.comments = "Your longer rest each hour. Get up, walk around, drink some water, and give your eyes something far away to look at."
        b.color = RGBA(r: 0.22, g: 0.55, b: 0.85)
        b.schedule.duration = .minutes(10)
        b.schedule.frequency = .minutes(60)
        b.controls.postponeMinutes1 = 1
        b.controls.postponeMinutes2 = 5
        b.actions = [
            BreakAction(kind: .notification, stage: .beforeStart, offset: 60)
        ]
        b.actions[0].title = "Break coming up"
        b.actions[0].message = "Your {break} break starts in a minute."
        return b
    }

    static func makeMicro() -> Break {
        var b = Break()
        b.name = "Micro"
        b.comments = "A few seconds every so often: lean back, drop your shoulders, and look across the room."
        b.color = RGBA(r: 0.95, g: 0.6, b: 0.2)
        b.schedule.duration = .seconds(15)
        b.schedule.frequency = .minutes(15)
        b.schedule.skipForHigherPriorityInterval = .minutes(5)
        b.controls.fadeInSeconds = 1
        b.controls.fadeOutSeconds = 1
        b.controls.showPostpone2 = false
        b.controls.postponeMinutes1 = 1
        return b
    }

    func duplicate() -> Break {
        var b = self
        b.id = UUID()
        b.name = name + " copy"
        b.state = BreakState()
        b.startShortcut = nil
        b.actions = actions.map {
            var a = $0; a.id = UUID(); return a
        }
        return b
    }
}

// MARK: - State

enum BreakPhase: String, Codable {
    case idle, waitingForNatural, asking, preparing, running, ending
}

struct BreakCounts: Codable, Hashable {
    var breakPostpone = 0, hourPostpone = 0, todayPostpone = 0, totalPostpone = 0
    var breakSkip = 0, hourSkip = 0, todaySkip = 0, totalSkip = 0
    var todayStart = 0, totalStart = 0
    var todayDone = 0, totalDone = 0
    var todayDuration: TimeInterval = 0, totalDuration: TimeInterval = 0
    var hourStamp: Date = Date()
    var dayStamp: Date = Date()

    mutating func rollIfNeeded(now: Date = Date()) {
        let cal = Calendar.current
        if !cal.isDate(dayStamp, inSameDayAs: now) {
            todayPostpone = 0; todaySkip = 0; todayStart = 0; todayDone = 0; todayDuration = 0
            dayStamp = now
        }
        if !cal.isDate(hourStamp, equalTo: now, toGranularity: .hour) {
            hourPostpone = 0; hourSkip = 0
            hourStamp = now
        }
    }
}

struct BreakState: Codable, Hashable {
    var nextDue: Date? = nil
    var lastStarted: Date? = nil
    var lastDone: Date? = nil
    var lastDeferred: Date? = nil
    var lastDuration: TimeInterval = 0
    /// `Date.distantFuture` = paused indefinitely.
    var pausedUntil: Date? = nil
    var nextEventStart: Date? = nil
    var nextEventEnd: Date? = nil
    var status: String = "scheduled"
    var counts = BreakCounts()
}
