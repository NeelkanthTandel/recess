namespace Recess.Core;

/// <summary>Due-date maths for breaks that aren't timed from a calendar. No app state, so it's easy to test.</summary>
public static class DueDates
{
    public enum Reason
    {
        Done,
        Skip,
        Reset,
    }

    /// <summary>When <paramref name="b"/> should next be due, measured from <paramref name="now"/>.</summary>
    public static DateTime Next(Break b, DateTime now, Reason reason, TimeZoneInfo zone)
    {
        DateTime due;
        switch (b.Schedule.Adjustment)
        {
            case DueAdjustment.Calendar:
                return b.State.NextDue ?? LocalTime.DistantFuture;
            case DueAdjustment.LastDue:
                if (reason == Reason.Done && b.State.LastStarted is { } started)
                {
                    // Start to start, but leave at least a little work time after a long or late break.
                    due = LocalTime.Max(started.AddSeconds(b.Frequency), now.AddSeconds(Math.Min(60, b.Frequency / 2)));
                }
                else
                {
                    due = now.AddSeconds(b.Frequency);
                }
                break;
            case DueAdjustment.LastDone:
                due = now.AddSeconds(b.Frequency);
                break;
            default:
                var after = now.AddSeconds(reason == Reason.Reset ? 30 : b.Frequency / 2);
                due = NextFixed(after, b.Schedule.FixedMinutesPastHour, b.Frequency, zone);
                break;
        }
        return AdjustForAvailability(due, b.Schedule, b.Frequency, zone);
    }

    /// <summary>
    /// The next clock-time slot after <paramref name="date"/> on a grid of <paramref name="frequency"/>
    /// that passes through <paramref name="minutesPastHour"/> (e.g. :50 every 60 minutes gives
    /// 9:50, 10:50, …). Uses the wall clock, so slots stay put across daylight-saving changes.
    /// </summary>
    public static DateTime NextFixed(DateTime date, int minutesPastHour, double frequency, TimeZoneInfo zone)
    {
        var step = (int)Math.Max(1, Math.Min(1440, Format.Round(frequency / 60)));
        var offset = (minutesPastHour % 60 + 60) % 60 % step;
        var day = LocalTime.Date(date, zone);
        for (var i = 0; i < 3; i++)
        {
            for (var minute = offset; minute < 1440; minute += step)
            {
                var slot = LocalTime.At(day, minute / 60, minute % 60, zone);
                if (slot > date)
                {
                    return slot;
                }
            }
            day = day.AddDays(1);
        }
        return date.AddSeconds(frequency);
    }

    /// <summary>
    /// Moves <paramref name="date"/> into the break's allowed hours and weekdays. Each day's first
    /// break comes one frequency after the window opens. A window that ends before it starts
    /// runs past midnight.
    /// </summary>
    public static DateTime AdjustForAvailability(DateTime date, BreakSchedule s, double frequency, TimeZoneInfo zone)
    {
        var restrictsDays = s.Weekdays.Count > 0 && s.Weekdays.Count < 7;
        if (s.Availability != Availability.Between && !restrictsDays)
        {
            return date;
        }

        bool Allowed(DateOnly day) => !restrictsDays || s.Weekdays.Contains(LocalTime.Weekday(day));

        // The availability window that opens on a given day.
        (DateTime Start, DateTime End) Window(DateOnly day)
        {
            if (s.Availability != Availability.Between)
            {
                return (LocalTime.At(day, 0, 0, zone), LocalTime.At(day.AddDays(1), 0, 0, zone));
            }
            var start = LocalTime.At(day, s.FromTime.Hour, s.FromTime.Minute, zone);
            var end = LocalTime.At(day, s.ToTime.Hour, s.ToTime.Minute, zone);
            if (end <= start)
            {
                end = LocalTime.At(day.AddDays(1), s.ToTime.Hour, s.ToTime.Minute, zone);
            }
            return (start, end);
        }

        // The first break time in the window that opens on a day, kept inside the window.
        DateTime FirstSlot(DateOnly day)
        {
            var w = Window(day);
            var latest = LocalTime.Max(w.Start, w.End.AddSeconds(-60));
            return LocalTime.Min(w.Start.AddSeconds(frequency), latest);
        }

        var day = LocalTime.Date(date, zone);
        // A window that opened yesterday may still be running (overnight windows).
        var yesterday = day.AddDays(-1);
        if (Allowed(yesterday))
        {
            var w = Window(yesterday);
            if (date >= w.Start && date < w.End)
            {
                return date;
            }
        }
        for (var i = 0; i < 15; i++)
        {
            if (Allowed(day))
            {
                var w = Window(day);
                if (date < w.Start)
                {
                    return FirstSlot(day);
                }
                if (date < w.End)
                {
                    return date;
                }
            }
            day = day.AddDays(1);
            if (Allowed(day))
            {
                return FirstSlot(day);
            }
        }
        return date;
    }
}
