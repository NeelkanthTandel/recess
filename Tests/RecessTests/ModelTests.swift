import XCTest

@testable import Recess

final class FormatTests: XCTestCase {
    func testIntNeverTraps() {
        XCTAssertEqual(Format.int(.nan), 0)
        XCTAssertEqual(Format.int(.infinity), 0)
        XCTAssertEqual(Format.int(1e300), Int(1e15))
        XCTAssertEqual(Format.int(42.9), 42)
    }

    func testCountdown() {
        XCTAssertEqual(Format.countdown(9), "0:09")
        XCTAssertEqual(Format.countdown(65), "1:05")
        XCTAssertEqual(Format.countdown(3725), "1:02:05")
        XCTAssertEqual(Format.countdown(-5), "0:00")
        XCTAssertEqual(Format.countdown(.nan), "0:00")
    }

    func testBriefAndFull() {
        XCTAssertEqual(Format.brief(30), "30s")
        XCTAssertEqual(Format.brief(5 * 60), "5m")
        XCTAssertEqual(Format.brief(3900), "1h 5m")
        XCTAssertEqual(Format.full(60), "1 minute")
        XCTAssertEqual(Format.full(3900), "1 hour, 5 minutes")
    }
}

final class BreakTests: XCTestCase {
    func testPostponeLimitPerHour() {
        var b = Break()
        b.controls.limitPostpone = true
        b.controls.postponeLimit = 2
        b.controls.postponeLimitKind = .perHour
        XCTAssertTrue(b.canPostpone)
        b.state.counts.hourPostpone = 2
        XCTAssertFalse(b.canPostpone)
        XCTAssertEqual(b.postponesRemaining, 0)
    }

    func testSkipLimitOfZeroBlocksSkipping() {
        var b = Break()
        b.controls.limitSkip = true
        b.controls.skipLimit = 0
        XCTAssertFalse(b.canSkip)
    }

    func testNoLimitsMeansAlwaysAllowed() {
        var b = Break()
        b.state.counts.todayPostpone = 100
        XCTAssertTrue(b.canPostpone)
        XCTAssertNil(b.postponesRemaining)
    }

    func testCountersRollOverByHourAndDay() {
        var c = BreakCounts()
        c.hourPostpone = 3
        c.todayPostpone = 5
        c.totalPostpone = 9
        c.hourStamp = Date().addingTimeInterval(-2 * 3600)
        c.dayStamp = Date().addingTimeInterval(-2 * 86400)
        c.rollIfNeeded()
        XCTAssertEqual(c.hourPostpone, 0)
        XCTAssertEqual(c.todayPostpone, 0)
        XCTAssertEqual(c.totalPostpone, 9)
    }

    func testIntervalsFromHandEditedFilesAreClamped() {
        var b = Break()
        b.schedule.frequency = Interval(.nan, .minutes)
        b.schedule.duration = Interval(1e12, .hours)
        XCTAssertEqual(b.frequency, 10)
        XCTAssertEqual(b.duration, 24 * 3600)
    }
}

final class StoreTests: XCTestCase {
    func testMissingKeysAreFilledFromDefaults() throws {
        // An older settings file that predates most options.
        let json = #"{"showDockIcon": true, "statusTitle": "onlyIcon"}"#
        let s = try XCTUnwrap(Store.decodeMerging(Data(json.utf8), defaults: AppSettings()))
        XCTAssertTrue(s.showDockIcon)
        XCTAssertEqual(s.statusTitle, .onlyIcon)
        XCTAssertEqual(s.naturalBreakThreshold, AppSettings().naturalBreakThreshold)
        XCTAssertTrue(s.allowURLCommands)
    }

    func testNestedKeysAreFilledToo() throws {
        let json = #"{"name": "Stretch", "schedule": {"duration": {"amount": 3, "units": "minutes"}}}"#
        let b = try XCTUnwrap(Store.decodeMerging(Data(json.utf8), defaults: Break()))
        XCTAssertEqual(b.name, "Stretch")
        XCTAssertEqual(b.schedule.duration, .minutes(3))
        XCTAssertEqual(b.schedule.frequency, BreakSchedule().frequency)
    }

    func testRoundTrip() throws {
        var b = Break.makeMicro()
        b.state.nextDue = Date(timeIntervalSince1970: 1_800_000_000)
        let data = try Store.encoder.encode(b)
        let back = try XCTUnwrap(Store.decodeMerging(data, defaults: Break()))
        XCTAssertEqual(back.name, b.name)
        XCTAssertEqual(back.schedule, b.schedule)
        XCTAssertEqual(back.state.nextDue, b.state.nextDue)
    }
}

final class SafetyTests: XCTestCase {
    func testYouTubeIDsAreValidated() {
        XCTAssertEqual(ThemeManager.youTubeID("https://www.youtube.com/watch?v=dQw4w9WgXcQ"), "dQw4w9WgXcQ")
        XCTAssertEqual(ThemeManager.youTubeID("https://youtu.be/dQw4w9WgXcQ"), "dQw4w9WgXcQ")
        XCTAssertEqual(ThemeManager.youTubeID("dQw4w9WgXcQ"), "dQw4w9WgXcQ")
        XCTAssertNil(ThemeManager.youTubeID("https://youtu.be/x%22%3E%3Cscript%3Ealert(1)%3C/script%3E"))
        XCTAssertNil(ThemeManager.youTubeID("https://example.com/watch?v=\"><script>"))
        XCTAssertNil(ThemeManager.youTubeID(""))
    }

    func testFontNamesCantBreakOutOfCSS() {
        XCTAssertEqual(ThemeManager.safeFontName("Helvetica Neue"), "Helvetica Neue")
        XCTAssertEqual(ThemeManager.safeFontName("x';}</style><script>"), "xstylescript")
        XCTAssertEqual(ThemeManager.safeFontName("';"), "Helvetica Neue")
    }

    func testScriptNamesCantLeaveTheScriptsFolder() throws {
        let folder = FileManager.default.temporaryDirectory.appendingPathComponent("recess-tests-\(UUID().uuidString)")
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: folder) }
        try "echo true".write(to: folder.appendingPathComponent("ok.sh"), atomically: true, encoding: .utf8)
        // A symlink inside the folder that points outside it.
        try FileManager.default.createSymbolicLink(
            at: folder.appendingPathComponent("link.sh"), withDestinationURL: URL(fileURLWithPath: "/bin/echo"))

        XCTAssertNotNil(ScriptRunner.resolve("ok.sh", in: folder))
        XCTAssertNil(ScriptRunner.resolve("../ok.sh", in: folder))
        XCTAssertNil(ScriptRunner.resolve("sub/ok.sh", in: folder))
        XCTAssertNil(ScriptRunner.resolve("..", in: folder))
        XCTAssertNil(ScriptRunner.resolve("", in: folder))
        XCTAssertNil(ScriptRunner.resolve("missing.sh", in: folder))
        XCTAssertNil(ScriptRunner.resolve("link.sh", in: folder))
    }

    func testKeyNamesForOutOfRangeCodes() {
        XCTAssertEqual(KeyCombo.keyName(UInt32.max), "?")
    }
}
