namespace Recess.Core;

/// <summary>The words the tray icon and its menu use, worked out the same way as the Mac's menu bar item.</summary>
public static class StatusText
{
    /// <summary>The text the Mac shows next to its menu bar icon; on Windows it heads the tooltip.</summary>
    public static string Title(RecessModel model, Scheduler scheduler, TimeZoneInfo zone)
    {
        var s = model.Settings;
        var now = scheduler.Now;
        var next = model.NextBreak(now);
        var last = model.LastDoneBreak;
        if (scheduler.Session is { } session)
        {
            return $"{session.Break.Name} {Format.Countdown(session.Remaining)}";
        }
        if (model.AllPaused(now))
        {
            return "Paused";
        }
        if (next is not null && scheduler.WaitingDescription(next) is not null
            && s.StatusTitle is StatusTitle.BriefCountdown or StatusTitle.FullCountdown)
        {
            // A due break that's waiting to start: say so rather than "0s".
            return "waiting";
        }
        return s.StatusTitle switch
        {
            StatusTitle.OnlyIcon => "",
            StatusTitle.BriefSinceLast => last?.State.LastDone is { } d ? Format.Brief((now - d).TotalSeconds) : "",
            StatusTitle.FullSinceLast => last?.State.LastDone is { } d ? Format.Full((now - d).TotalSeconds) : "",
            StatusTitle.DoneTimeOfLast => last?.State.LastDone is { } d ? Format.Time(d, zone) : "",
            StatusTitle.DurationOfLast => last is not null ? Format.Brief(last.State.LastDuration) : "",
            StatusTitle.BriefCountdown => DueDate(next, now) is { } due ? Format.Brief((due - now).TotalSeconds) : "",
            StatusTitle.FullCountdown => DueDate(next, now) is { } due ? Format.Countdown((due - now).TotalSeconds) : "",
            StatusTitle.DueTimeOfNext => DueDate(next, now) is { } due ? Format.Time(due, zone) : "",
            StatusTitle.FinishTimeOfNext => next is not null && DueDate(next, now) is { } due ? Format.Time(due.AddSeconds(next.Duration), zone) : "",
            _ => next is not null ? Format.Brief(next.Duration) : "",
        };
    }

    /// <summary>The tray tooltip: the title, then what's coming next.</summary>
    public static string Tooltip(RecessModel model, Scheduler scheduler, TimeZoneInfo zone)
    {
        var now = scheduler.Now;
        var title = Title(model, scheduler, zone);
        string detail;
        if (scheduler.Session is not null)
        {
            detail = "Break in progress";
        }
        else if (model.NextBreak(now) is not { } next || DueDate(next, now) is not { } due)
        {
            detail = "No breaks scheduled";
        }
        else if (scheduler.WaitingDescription(next) is { } waiting)
        {
            detail = $"{next.Name} break is {waiting}";
        }
        else
        {
            detail = $"{next.Name} break {Format.Relative(due, now)}";
        }
        var text = title.Length > 0 && !detail.Contains(title, StringComparison.Ordinal) ? $"Recess: {title}\n{detail}" : $"Recess\n{detail}";
        return text.Length > 127 ? text[..127] : text;
    }

    /// <summary>
    /// Two or three characters for the countdown icon: minutes to the next break ("12"), hours
    /// when it's far off ("2h"), "&lt;1" in the last minute, and the minutes left during a break.
    /// </summary>
    public static string IconText(RecessModel model, Scheduler scheduler)
    {
        var now = scheduler.Now;
        double seconds;
        if (scheduler.Session is { } session)
        {
            seconds = session.Remaining;
        }
        else if (model.NextBreak(now) is { } next && DueDate(next, now) is { } due)
        {
            if (scheduler.WaitingDescription(next) is not null)
            {
                return "…";
            }
            seconds = (due - now).TotalSeconds;
        }
        else
        {
            return "–";
        }
        if (seconds < 60)
        {
            return "<1";
        }
        var minutes = Math.Ceiling(seconds / 60);
        if (minutes < 100)
        {
            return Format.Int(minutes).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        var hours = Math.Floor(seconds / 3600);
        return hours < 100 ? $"{Format.Int(hours)}h" : "99+";
    }

    /// <summary>The grey text after a break's name in the menu: "due in 12 minutes", "paused", "disabled"…</summary>
    public static string BreakStatus(Break b, RecessModel model, Scheduler scheduler, TimeZoneInfo zone)
    {
        var now = scheduler.Now;
        if (!b.Enabled)
        {
            return "disabled";
        }
        if (b.IsPaused(now))
        {
            return b.State.PausedUntil >= LocalTime.DistantFuture ? "paused" : $"paused until {Format.Time(b.State.PausedUntil!.Value, zone)}";
        }
        if (model.ActiveBreakId == b.Id)
        {
            return "now";
        }
        if (b.State.NextDue is not { } due)
        {
            return "";
        }
        if (due >= LocalTime.DistantFuture)
        {
            return "no upcoming events";
        }
        return scheduler.WaitingDescription(b) ?? "due " + model.DateString(due, now, zone);
    }

    private static DateTime? DueDate(Break? b, DateTime now) =>
        b?.State.NextDue is { } d && d < LocalTime.DistantFuture ? LocalTime.Max(d, now) : null;
}
