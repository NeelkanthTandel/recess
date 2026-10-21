import XCTest

@testable import Recess

final class DueDatesTests: XCTestCase {
    /// A fixed calendar so results don't depend on the machine running the tests.
    private var cal: Calendar = {
        var c = Calendar(identifier: .gregorian)
        c.timeZone = TimeZone(identifier: "America/New_York")!
        return c
    }()

    private func date(_ y: Int, _ m: Int, _ d: Int, _ h: Int, _ min: Int = 0) -> Date {
        cal.date(from: DateComponents(year: y, month: m, day: d, hour: h, minute: min))!
    }

    private func hm(_ date: Date) -> String {
        let c = cal.dateComponents([.month, .day, .hour, .minute], from: date)
        return String(format: "%02d/%02d %02d:%02d", c.month!, c.day!, c.hour!, c.minute!)
    }

    private func makeBreak(frequency: Double = 60, duration: Double = 10) -> Break {
        var b = Break()
        b.schedule.frequency = .minutes(frequency)
        b.schedule.duration = .minutes(duration)
        return b
    }

    // MARK: Next due

    func testLastDueIsMeasuredFromTheStartOfTheBreak() {
        var b = makeBreak()
        let started = date(2026, 6, 1, 10, 0)
        b.state.lastStarted = started
        let due = DueDates.next(for: b, from: date(2026, 6, 1, 10, 10), reason: .done, calendar: cal)
        XCTAssertEqual(hm(due), "06/01 11:00")
    }

    func testLastDueLeavesSomeWorkTimeAfterALateBreak() {
        var b = makeBreak()
        b.state.lastStarted = date(2026, 6, 1, 10, 0)
        let now = date(2026, 6, 1, 11, 30)  // the break ran very long
        let due = DueDates.next(for: b, from: now, reason: .done, calendar: cal)
        XCTAssertEqual(due, now.addingTimeInterval(60))
    }

    func testLastDoneIsMeasuredFromNow() {
        let b = makeBreak()
        let now = date(2026, 6, 1, 10, 10)
        XCTAssertEqual(DueDates.next(for: b, from: now, reason: .done, calendar: cal), now.addingTimeInterval(3600))
    }

    // MARK: Fixed time

    func testFixedTimeHourlyAtFiftyPast() {
        let slot = DueDates.nextFixed(after: date(2026, 6, 1, 9, 12), minutesPastHour: 50, frequency: 3600, calendar: cal)
        XCTAssertEqual(hm(slot), "06/01 09:50")
    }

    func testFixedTimeHalfHourGridIncludesEarlierOffsets() {
        let slot = DueDates.nextFixed(after: date(2026, 6, 1, 9, 51), minutesPastHour: 50, frequency: 1800, calendar: cal)
        XCTAssertEqual(hm(slot), "06/01 10:20")
    }

    func testFixedTimeRollsOverMidnight() {
        let slot = DueDates.nextFixed(after: date(2026, 6, 1, 23, 30), minutesPastHour: 0, frequency: 3600, calendar: cal)
        XCTAssertEqual(hm(slot), "06/02 00:00")
    }

    func testFixedTimeKeepsWallClockAcrossDaylightSaving() {
        // Clocks jump from 2:00 to 3:00 on 8 March 2026 in New York.
        let slot = DueDates.nextFixed(after: date(2026, 3, 8, 3, 10), minutesPastHour: 0, frequency: 2 * 3600, calendar: cal)
        XCTAssertEqual(hm(slot), "03/08 04:00")
    }

    // MARK: Availability

    private func schedule(from: Int, to: Int, weekdays: Set<Int> = Set(1...7)) -> BreakSchedule {
        var s = BreakSchedule()
        s.availability = .between
        s.fromTime = TimeOfDay(hour: from, minute: 0)
        s.toTime = TimeOfDay(hour: to, minute: 0)
        s.weekdays = weekdays
        return s
    }

    func testInsideTheWindowIsUnchanged() {
        let t = date(2026, 6, 1, 12, 0)
        XCTAssertEqual(DueDates.adjustForAvailability(t, schedule: schedule(from: 9, to: 17), frequency: 3600, calendar: cal), t)
    }

    func testBeforeTheWindowMovesToFirstBreakOfTheDay() {
        let t = DueDates.adjustForAvailability(date(2026, 6, 1, 7, 0), schedule: schedule(from: 9, to: 17), frequency: 3600, calendar: cal)
        XCTAssertEqual(hm(t), "06/01 10:00")
    }

    func testAfterTheWindowMovesToTomorrow() {
        let t = DueDates.adjustForAvailability(date(2026, 6, 1, 18, 0), schedule: schedule(from: 9, to: 17), frequency: 3600, calendar: cal)
        XCTAssertEqual(hm(t), "06/02 10:00")
    }

    func testFirstBreakStaysInsideAShortWindow() {
        var s = schedule(from: 16, to: 17)
        s.fromTime = TimeOfDay(hour: 16, minute: 30)
        let t = DueDates.adjustForAvailability(date(2026, 6, 1, 8, 0), schedule: s, frequency: 3600, calendar: cal)
        XCTAssertEqual(hm(t), "06/01 16:59")
    }

    func testOvernightWindow() {
        let s = schedule(from: 22, to: 6)
        XCTAssertEqual(
            hm(DueDates.adjustForAvailability(date(2026, 6, 1, 23, 0), schedule: s, frequency: 3600, calendar: cal)), "06/01 23:00")
        XCTAssertEqual(
            hm(DueDates.adjustForAvailability(date(2026, 6, 2, 2, 0), schedule: s, frequency: 3600, calendar: cal)), "06/02 02:00")
        XCTAssertEqual(
            hm(DueDates.adjustForAvailability(date(2026, 6, 2, 7, 0), schedule: s, frequency: 3600, calendar: cal)), "06/02 23:00")
    }

    func testExcludedWeekdaysAreSkipped() {
        // 6 June 2026 is a Saturday; allow Monday–Friday only (weekday 2...6).
        var s = BreakSchedule()
        s.weekdays = Set(2...6)
        let t = DueDates.adjustForAvailability(date(2026, 6, 6, 11, 0), schedule: s, frequency: 3600, calendar: cal)
        XCTAssertEqual(hm(t), "06/08 01:00")
    }

    func testNoRestrictionsLeavesTheDateAlone() {
        let t = date(2026, 6, 6, 11, 0)
        XCTAssertEqual(DueDates.adjustForAvailability(t, schedule: BreakSchedule(), frequency: 3600, calendar: cal), t)
    }
}
