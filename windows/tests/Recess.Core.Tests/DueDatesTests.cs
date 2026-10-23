using static Recess.Core.Tests.Zones;

namespace Recess.Core.Tests;

// Ported from the Mac app's DueDatesTests, with the same dates and expectations.
public class DueDatesTests
{
    private static Break MakeBreak(double frequency = 60, double duration = 10)
    {
        var b = new Break();
        b.Schedule.Frequency = Interval.FromMinutes(frequency);
        b.Schedule.Duration = Interval.FromMinutes(duration);
        return b;
    }

    [Fact]
    public void LastDueIsMeasuredFromTheStartOfTheBreak()
    {
        var b = MakeBreak();
        b.State.LastStarted = Date(2026, 6, 1, 10, 0);
        var due = DueDates.Next(b, Date(2026, 6, 1, 10, 10), DueDates.Reason.Done, NewYork);
        Assert.Equal("06/01 11:00", Hm(due));
    }

    [Fact]
    public void LastDueLeavesSomeWorkTimeAfterALateBreak()
    {
        var b = MakeBreak();
        b.State.LastStarted = Date(2026, 6, 1, 10, 0);
        var now = Date(2026, 6, 1, 11, 30); // the break ran very long
        Assert.Equal(now.AddSeconds(60), DueDates.Next(b, now, DueDates.Reason.Done, NewYork));
    }

    [Fact]
    public void LastDoneIsMeasuredFromNow()
    {
        var b = MakeBreak();
        b.Schedule.Adjustment = DueAdjustment.LastDone;
        var now = Date(2026, 6, 1, 10, 10);
        Assert.Equal(now.AddSeconds(3600), DueDates.Next(b, now, DueDates.Reason.Done, NewYork));
    }

    [Fact]
    public void FixedTimeHourlyAtFiftyPast() =>
        Assert.Equal("06/01 09:50", Hm(DueDates.NextFixed(Date(2026, 6, 1, 9, 12), 50, 3600, NewYork)));

    [Fact]
    public void FixedTimeHalfHourGridIncludesEarlierOffsets() =>
        Assert.Equal("06/01 10:20", Hm(DueDates.NextFixed(Date(2026, 6, 1, 9, 51), 50, 1800, NewYork)));

    [Fact]
    public void FixedTimeRollsOverMidnight() =>
        Assert.Equal("06/02 00:00", Hm(DueDates.NextFixed(Date(2026, 6, 1, 23, 30), 0, 3600, NewYork)));

    [Fact]
    public void FixedTimeKeepsWallClockAcrossDaylightSaving() =>
        // Clocks jump from 2:00 to 3:00 on 8 March 2026 in New York.
        Assert.Equal("03/08 04:00", Hm(DueDates.NextFixed(Date(2026, 3, 8, 3, 10), 0, 2 * 3600, NewYork)));

    [Fact]
    public void FixedTimeInTheHourThatRepeatsInAutumn()
    {
        // Clocks go back from 2:00 to 1:00 on 1 November 2026; slots are still an hour apart on the clock.
        // 1:30 happens twice; the slot is the first one (still daylight time, UTC−4).
        var slot = DueDates.NextFixed(Date(2026, 11, 1, 0, 40), 30, 3600, NewYork);
        Assert.Equal("11/01 01:30", Hm(slot));
        Assert.Equal(new DateTime(2026, 11, 1, 5, 30, 0, DateTimeKind.Utc), slot);
    }

    private static BreakSchedule Schedule(int from, int to, IEnumerable<int>? weekdays = null) => new()
    {
        Availability = Availability.Between,
        FromTime = new TimeOfDay(from, 0),
        ToTime = new TimeOfDay(to, 0),
        Weekdays = new SortedSet<int>(weekdays ?? [1, 2, 3, 4, 5, 6, 7]),
    };

    [Fact]
    public void InsideTheWindowIsUnchanged()
    {
        var t = Date(2026, 6, 1, 12, 0);
        Assert.Equal(t, DueDates.AdjustForAvailability(t, Schedule(9, 17), 3600, NewYork));
    }

    [Fact]
    public void BeforeTheWindowMovesToFirstBreakOfTheDay() =>
        Assert.Equal("06/01 10:00", Hm(DueDates.AdjustForAvailability(Date(2026, 6, 1, 7, 0), Schedule(9, 17), 3600, NewYork)));

    [Fact]
    public void AfterTheWindowMovesToTomorrow() =>
        Assert.Equal("06/02 10:00", Hm(DueDates.AdjustForAvailability(Date(2026, 6, 1, 18, 0), Schedule(9, 17), 3600, NewYork)));

    [Fact]
    public void FirstBreakStaysInsideAShortWindow()
    {
        var s = Schedule(16, 17);
        s.FromTime = new TimeOfDay(16, 30);
        Assert.Equal("06/01 16:59", Hm(DueDates.AdjustForAvailability(Date(2026, 6, 1, 8, 0), s, 3600, NewYork)));
    }

    [Fact]
    public void OvernightWindow()
    {
        var s = Schedule(22, 6);
        Assert.Equal("06/01 23:00", Hm(DueDates.AdjustForAvailability(Date(2026, 6, 1, 23, 0), s, 3600, NewYork)));
        Assert.Equal("06/02 02:00", Hm(DueDates.AdjustForAvailability(Date(2026, 6, 2, 2, 0), s, 3600, NewYork)));
        Assert.Equal("06/02 23:00", Hm(DueDates.AdjustForAvailability(Date(2026, 6, 2, 7, 0), s, 3600, NewYork)));
    }

    [Fact]
    public void ExcludedWeekdaysAreSkipped()
    {
        // 6 June 2026 is a Saturday; allow Monday–Friday only (weekdays 2–6).
        var s = new BreakSchedule { Weekdays = [2, 3, 4, 5, 6] };
        Assert.Equal("06/08 01:00", Hm(DueDates.AdjustForAvailability(Date(2026, 6, 6, 11, 0), s, 3600, NewYork)));
    }

    [Fact]
    public void NoRestrictionsLeavesTheDateAlone()
    {
        var t = Date(2026, 6, 6, 11, 0);
        Assert.Equal(t, DueDates.AdjustForAvailability(t, new BreakSchedule(), 3600, NewYork));
    }

    [Fact]
    public void ATimeSkippedByDaylightSavingMovesForward() =>
        // 2:30 doesn't exist on 8 March 2026 in New York; it becomes 3:30.
        Assert.Equal("03/08 03:30", Hm(Date(2026, 3, 8, 2, 30)));
}
