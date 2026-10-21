import Foundation

/// Pure due-date maths for non-calendar breaks. Kept free of app state so it can be unit tested.
enum DueDates {
    enum Reason { case done, skip, reset }

    /// When `b` should next be due, measured from `now`.
    static func next(for b: Break, from now: Date, reason: Reason, calendar cal: Calendar = .current) -> Date {
        let due: Date
        switch b.schedule.adjustment {
        case .calendar:
            return b.state.nextDue ?? .distantFuture
        case .lastDue:
            if reason == .done, let started = b.state.lastStarted {
                // Start to start, but leave at least a little work time after a long or late break.
                due = max(started.addingTimeInterval(b.frequency), now.addingTimeInterval(min(60, b.frequency / 2)))
            } else {
                due = now.addingTimeInterval(b.frequency)
            }
        case .lastDone:
            due = now.addingTimeInterval(b.frequency)
        case .fixedTime:
            let after = now.addingTimeInterval(reason == .reset ? 30 : b.frequency / 2)
            due = nextFixed(after: after, minutesPastHour: b.schedule.fixedMinutesPastHour, frequency: b.frequency, calendar: cal)
        }
        return adjustForAvailability(due, schedule: b.schedule, frequency: b.frequency, calendar: cal)
    }

    /// The next wall-clock slot after `date` on a grid of `frequency` that passes through
    /// `minutesPastHour` (e.g. :50 with a 60-minute frequency gives 9:50, 10:50, …).
    /// Uses clock time, so slots stay put across daylight-saving changes.
    static func nextFixed(after date: Date, minutesPastHour: Int, frequency: TimeInterval, calendar cal: Calendar = .current) -> Date {
        let step = max(1, min(1440, Int((frequency / 60).rounded())))
        let offset = ((minutesPastHour % 60) + 60) % 60 % step
        var day = cal.startOfDay(for: date)
        for _ in 0..<3 {
            var minute = offset
            while minute < 1440 {
                if let slot = cal.date(bySettingHour: minute / 60, minute: minute % 60, second: 0, of: day), slot > date {
                    return slot
                }
                minute += step
            }
            day = cal.date(byAdding: .day, value: 1, to: day) ?? day.addingTimeInterval(86400)
        }
        return date.addingTimeInterval(frequency)
    }

    /// Moves `date` into the break's allowed hours and weekdays. Each day's first break comes one
    /// frequency after the window opens. A window whose end is before its start runs past midnight.
    static func adjustForAvailability(
        _ date: Date, schedule s: BreakSchedule, frequency: TimeInterval, calendar cal: Calendar = .current
    ) -> Date {
        let restrictsDays = !s.weekdays.isEmpty && s.weekdays.count < 7
        guard s.availability == .between || restrictsDays else { return date }

        func allowed(_ day: Date) -> Bool { !restrictsDays || s.weekdays.contains(cal.component(.weekday, from: day)) }
        func addDays(_ n: Int, _ day: Date) -> Date {
            cal.date(byAdding: .day, value: n, to: day) ?? day.addingTimeInterval(Double(n) * 86400)
        }

        /// The availability window that opens on `day`.
        func window(_ day: Date) -> (start: Date, end: Date) {
            guard s.availability == .between else { return (day, addDays(1, day)) }
            let start = s.fromTime.date(on: day, calendar: cal)
            var end = s.toTime.date(on: day, calendar: cal)
            if end <= start { end = s.toTime.date(on: addDays(1, day), calendar: cal) }
            return (start, end)
        }

        /// The first break time in the window that opens on `day`, kept inside the window.
        func firstSlot(_ day: Date) -> Date {
            let w = window(day)
            let latest = max(w.start, w.end.addingTimeInterval(-60))
            return min(w.start.addingTimeInterval(frequency), latest)
        }

        var day = cal.startOfDay(for: date)
        // A window that opened yesterday may still be running (overnight windows).
        let yesterday = addDays(-1, day)
        if allowed(yesterday) {
            let w = window(yesterday)
            if date >= w.start && date < w.end { return date }
        }
        for _ in 0..<15 {
            if allowed(day) {
                let w = window(day)
                if date < w.start { return firstSlot(day) }
                if date < w.end { return date }
            }
            day = addDays(1, day)
            if allowed(day) { return firstSlot(day) }
        }
        return date
    }
}
