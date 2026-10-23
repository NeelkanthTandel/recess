using static Recess.Core.Tests.Zones;

namespace Recess.Core.Tests;

public class CalendarTests
{
    private static string Ics(params string[] events) =>
        "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//Recess tests//EN\r\n" + string.Concat(events) + "END:VCALENDAR\r\n";

    private static string Event(string uid, string start, string end, string summary, string extra = "") =>
        $"BEGIN:VEVENT\r\nUID:{uid}\r\nDTSTAMP:20260101T000000Z\r\nDTSTART:{start}\r\nDTEND:{end}\r\nSUMMARY:{summary}\r\n{extra}END:VEVENT\r\n";

    private static CalendarFeeds Feeds(string ics)
    {
        var feeds = new CalendarFeeds();
        feeds.Update("work", ics);
        return feeds;
    }

    [Fact]
    public void FindsTheNextMatchingEvent()
    {
        var feeds = Feeds(Ics(
            Event("1", "20260601T140000Z", "20260601T143000Z", "Standup"),
            Event("2", "20260601T160000Z", "20260601T170000Z", "Design review", "LOCATION:Room 4\r\n")));
        var now = Date(2026, 6, 1, 9, 0);
        Assert.Equal((Date(2026, 6, 1, 10, 0), Date(2026, 6, 1, 10, 30)), feeds.NextEvent(null, "", now));
        // Filters match the title, location or notes, ignoring case; any of several words.
        Assert.Equal(Date(2026, 6, 1, 12, 0), feeds.NextEvent("work", "room 4, lunch", now)?.Start);
        Assert.Null(feeds.NextEvent("other", "", now));
    }

    [Fact]
    public void AllDayAndCancelledEventsAreIgnored()
    {
        var feeds = Feeds(Ics(
            "BEGIN:VEVENT\r\nUID:a\r\nDTSTAMP:20260101T000000Z\r\nDTSTART;VALUE=DATE:20260601\r\nDTEND;VALUE=DATE:20260602\r\nSUMMARY:Holiday\r\nEND:VEVENT\r\n",
            Event("c", "20260601T140000Z", "20260601T150000Z", "Cancelled", "STATUS:CANCELLED\r\n")));
        Assert.Null(feeds.NextEvent(null, "", Date(2026, 6, 1, 0, 0)));
    }

    [Fact]
    public void BusyOnlyDuringBusyEvents()
    {
        var feeds = Feeds(Ics(
            Event("1", "20260601T140000Z", "20260601T150000Z", "Planning"),
            Event("2", "20260601T160000Z", "20260601T170000Z", "Focus time", "TRANSP:TRANSPARENT\r\n")));
        Assert.True(feeds.IsBusy(null, "", Date(2026, 6, 1, 10, 15)));
        Assert.False(feeds.IsBusy(null, "", Date(2026, 6, 1, 11, 15)));
        // Free ("show as available") events don't count as busy.
        Assert.False(feeds.IsBusy(null, "", Date(2026, 6, 1, 12, 15)));
    }

    [Fact]
    public void RecurringEventsWithExceptions()
    {
        // Weekdays at 9:30 New York time, except Wednesday 3 June, and Thursday moved to 11:00.
        var feeds = Feeds(Ics(
            "BEGIN:VTIMEZONE\r\nTZID:America/New_York\r\nBEGIN:DAYLIGHT\r\nTZOFFSETFROM:-0500\r\nTZOFFSETTO:-0400\r\nDTSTART:20070311T020000\r\nRRULE:FREQ=YEARLY;BYMONTH=3;BYDAY=2SU\r\nTZNAME:EDT\r\nEND:DAYLIGHT\r\nBEGIN:STANDARD\r\nTZOFFSETFROM:-0400\r\nTZOFFSETTO:-0500\r\nDTSTART:20071104T020000\r\nRRULE:FREQ=YEARLY;BYMONTH=11;BYDAY=1SU\r\nTZNAME:EST\r\nEND:STANDARD\r\nEND:VTIMEZONE\r\n",
            "BEGIN:VEVENT\r\nUID:standup\r\nDTSTAMP:20260101T000000Z\r\nDTSTART;TZID=America/New_York:20260601T093000\r\nDTEND;TZID=America/New_York:20260601T094500\r\nRRULE:FREQ=WEEKLY;BYDAY=MO,TU,WE,TH,FR\r\nEXDATE;TZID=America/New_York:20260603T093000\r\nSUMMARY:Standup\r\nEND:VEVENT\r\n",
            "BEGIN:VEVENT\r\nUID:standup\r\nDTSTAMP:20260101T000000Z\r\nRECURRENCE-ID;TZID=America/New_York:20260604T093000\r\nDTSTART;TZID=America/New_York:20260604T110000\r\nDTEND;TZID=America/New_York:20260604T111500\r\nSUMMARY:Standup (moved)\r\nEND:VEVENT\r\n"));
        var starts = feeds.Events(null, Date(2026, 6, 1, 0), Date(2026, 6, 6, 0)).Select(e => Hm(e.Start)).ToList();
        Assert.Equal(["06/01 09:30", "06/02 09:30", "06/04 11:00", "06/05 09:30"], starts);
    }

    [Fact]
    public void OutlookStyleWindowsTimeZoneNames()
    {
        var feeds = Feeds(Ics(
            "BEGIN:VEVENT\r\nUID:x\r\nDTSTAMP:20260101T000000Z\r\nDTSTART;TZID=Eastern Standard Time:20260601T150000\r\nDTEND;TZID=Eastern Standard Time:20260601T153000\r\nSUMMARY:Call\r\nEND:VEVENT\r\n"));
        Assert.Equal("06/01 15:00", Hm(feeds.NextEvent(null, "", Date(2026, 6, 1, 9))!.Value.Start));
    }

    [Fact]
    public void ARuleThatNeverMatchesDoesNotHang()
    {
        // February 30th never happens.
        var feeds = Feeds(Ics(Event("never", "20260130T100000Z", "20260130T110000Z", "Never", "RRULE:FREQ=MONTHLY;BYMONTHDAY=30;BYMONTH=2\r\n")));
        Assert.Null(feeds.NextEvent(null, "", Date(2026, 6, 1, 9)));
    }

    [Fact]
    public void ARuleEverySecondIsCapped()
    {
        var feeds = Feeds(Ics(Event("spam", "20260601T000000Z", "20260601T000001Z", "Spam", "RRULE:FREQ=SECONDLY\r\n")));
        Assert.True(feeds.Events(null, Date(2026, 6, 1, 0), Date(2026, 6, 2, 0)).Count <= 5000);
    }

    [Fact]
    public void NotICalIsRefused()
    {
        var feeds = new CalendarFeeds();
        Assert.Throws<FormatException>(() => feeds.Update("x", "<html>Sign in</html>"));
        Assert.False(feeds.Has("x"));
    }

    [Fact]
    public void CalendarBreaksFollowTheFeed()
    {
        var meeting = Break.MakeNormal();
        meeting.Name = "After meetings";
        meeting.Actions.Clear();
        meeting.Schedule.Adjustment = DueAdjustment.Calendar;
        meeting.Schedule.CalendarEventFilter = "rest";
        var feeds = Feeds(Ics(Event("r", "20260601T150000Z", "20260601T151000Z", "Rest your eyes")));
        using var h = new HarnessWithCalendar(feeds, meeting);
        h.Scheduler.Start();
        Assert.Equal("06/01 11:00", Hm(meeting.State.NextDue!.Value));
        Assert.Equal(600, meeting.Duration);
    }

    /// <summary>The usual harness, but with real calendar feeds.</summary>
    private sealed class HarnessWithCalendar : IDisposable
    {
        private readonly string folder = Path.Combine(Path.GetTempPath(), "recess-cal-" + Guid.NewGuid());

        public HarnessWithCalendar(CalendarFeeds feeds, params Break[] breaks)
        {
            var clock = new FakeClock(Date(2026, 6, 1, 9, 0));
            var model = new RecessModel([.. breaks], new AppSettings());
            Scheduler = new Scheduler(model, clock, new FakeSystem(), new FakeAsk(), new FakePresenter(), new FakeActions(), feeds, new FakeScripts(),
                new ActivityRecorder(folder, () => model.Settings, clock));
        }

        public Scheduler Scheduler { get; }

        public void Dispose()
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }
}
