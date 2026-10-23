using System.Text.Json;

namespace Recess.Core;

/// <summary>What the next due date is measured from.</summary>
public enum DueAdjustment
{
    LastDue,
    LastDone,
    FixedTime,
    Calendar,
}

/// <summary>What happens to the countdown while the user is away.</summary>
public enum IdleHandling
{
    ContinueCountdown,
    PauseCountdown,
    CountBackwards,
}

/// <summary>How a break begins once it is due.</summary>
public enum DueBehavior
{
    StartImmediately,
    WaitForNaturalBreak,
    Ask,
}

public enum Availability
{
    AllDay,
    Between,
}

public enum LimitKind
{
    PerBreak,
    PerHour,
    PerDay,
}

public enum OtherScreensMode
{
    Theme,
    Dim,
    None,
}

public enum MainScreenChoice
{
    /// <summary>The display the user is working on (the Mac calls it the one with the active menu bar).</summary>
    ActiveMenubar,
    Primary,
}

public static class BreakTitles
{
    public static string Title(this DueAdjustment v) => v switch
    {
        DueAdjustment.LastDue => "When the last one was due",
        DueAdjustment.LastDone => "When the last one ended",
        DueAdjustment.FixedTime => "A set minute each hour",
        _ => "Calendar events",
    };

    public static string Title(this IdleHandling v) => v switch
    {
        IdleHandling.ContinueCountdown => "Keep counting down",
        IdleHandling.PauseCountdown => "Stop the clock",
        _ => "Wind the clock back",
    };

    public static string Title(this DueBehavior v) => v switch
    {
        DueBehavior.StartImmediately => "Start right away",
        DueBehavior.WaitForNaturalBreak => "Wait for a pause in my work",
        _ => "Ask me first",
    };

    /// <summary>What this choice means in practice; shown in the setup assistant and on the Schedule tab.</summary>
    public static string Explanation(this DueBehavior v) => v switch
    {
        DueBehavior.StartImmediately =>
            "The screen dims exactly on time, even mid-sentence. Predictable, and the best way to build the habit.",
        DueBehavior.WaitForNaturalBreak =>
            $"Recess waits until you stop typing and using the mouse for {Scheduler.PauseLength:0} seconds, so you're not interrupted mid-thought. If you never pause, the break starts anyway after {Scheduler.LongestWait:0} seconds, so it can arrive up to a minute late.",
        _ => "A small panel appears with Start, Postpone and Skip buttons. If you don't answer, the break starts on its own after a minute.",
    };

    public static string Title(this Availability v) => v == Availability.AllDay ? "Any time" : "Only between";

    public static string Title(this LimitKind v) => v switch
    {
        LimitKind.PerBreak => "break",
        LimitKind.PerHour => "hour",
        _ => "day",
    };

    public static string Title(this OtherScreensMode v) => v switch
    {
        OtherScreensMode.Theme => "Same as the main screen",
        OtherScreensMode.Dim => "Darken only",
        _ => "Leave alone",
    };

    public static string Title(this MainScreenChoice v) => v == MainScreenChoice.ActiveMenubar ? "Screen I'm using" : "Main display";
}

public sealed class BreakSchedule
{
    public Interval Duration { get; set; } = Interval.FromMinutes(10);
    public Interval Frequency { get; set; } = Interval.FromMinutes(60);
    public DueAdjustment Adjustment { get; set; } = DueAdjustment.LastDue;
    public int FixedMinutesPastHour { get; set; }
    public string? CalendarIdentifier { get; set; }
    public string CalendarEventFilter { get; set; } = "";
    public Availability Availability { get; set; } = Availability.AllDay;
    public TimeOfDay FromTime { get; set; } = new(9, 0);
    public TimeOfDay ToTime { get; set; } = new(17, 0);

    /// <summary>Weekdays (1 = Sunday … 7 = Saturday) on which the break may happen.</summary>
    public SortedSet<int> Weekdays { get; set; } = [1, 2, 3, 4, 5, 6, 7];

    public IdleHandling IdleHandling { get; set; } = IdleHandling.ContinueCountdown;
    public bool ResetWhenIdle { get; set; } = true;

    /// <summary>null = the break's own length.</summary>
    public Interval? ResetWhenIdleInterval { get; set; }

    public bool ResetWhenFinishingHigherPriority { get; set; } = true;
    public bool SkipForHigherPriority { get; set; } = true;
    public Interval SkipForHigherPriorityInterval { get; set; } = Interval.FromMinutes(5);
    public DueBehavior DueBehavior { get; set; } = DueBehavior.StartImmediately;
    public Interval AskAutoStart { get; set; } = Interval.FromMinutes(1);

    /// <summary>The fields that decide when the break happens; editing any of them re-times the break.</summary>
    public string TimingKey => string.Join('|', Duration, Frequency, Adjustment, FixedMinutesPastHour, CalendarIdentifier,
        CalendarEventFilter, Availability, FromTime, ToTime, string.Join(',', Weekdays));
}

public sealed class BreakControls
{
    public double FadeInSeconds { get; set; } = 2;
    public double FadeOutSeconds { get; set; } = 2;
    public bool ShowPostpone1 { get; set; } = true;
    public double PostponeMinutes1 { get; set; } = 1;
    public bool ShowPostpone2 { get; set; } = true;
    public double PostponeMinutes2 { get; set; } = 5;
    public bool ShowSkip { get; set; } = true;
    public bool ShowEndEarly { get; set; }
    public Interval EndEarlyAfter { get; set; } = Interval.FromMinutes(5);
    public bool EndWhenDone { get; set; }
    public double DisableButtonsSeconds { get; set; }
    public bool ShowCounters { get; set; }
    public bool LimitPostpone { get; set; }
    public int PostponeLimit { get; set; } = 3;
    public LimitKind PostponeLimitKind { get; set; } = LimitKind.PerBreak;
    public bool LimitSkip { get; set; }
    public int SkipLimit { get; set; } = 1;
    public LimitKind SkipLimitKind { get; set; } = LimitKind.PerHour;
}

public static class ThemeIds
{
    public const string Default = "builtin.default";
    public const string Blank = "builtin.blank";
    public const string None = "builtin.none";
}

/// <summary>A colour with components from 0 to 1.</summary>
public readonly record struct Rgba(double R, double G, double B, double A = 1)
{
    public string Hex => $"#{Channel(R):X2}{Channel(G):X2}{Channel(B):X2}";

    private static int Channel(double v) => (int)Math.Round(Math.Clamp(double.IsFinite(v) ? v : 0, 0, 1) * 255);
}

public sealed class BreakAppearance
{
    public string ThemeId { get; set; } = ThemeIds.Default;
    public Rgba BackgroundColor { get; set; } = new(0.08, 0.1, 0.14, 0.88);
    public MainScreenChoice MainScreen { get; set; } = MainScreenChoice.ActiveMenubar;
    public OtherScreensMode OtherScreens { get; set; } = OtherScreensMode.Dim;
    public Rgba OtherScreensColor { get; set; } = new(0, 0, 0, 0.75);
    public string Message { get; set; } = "";
}

public sealed class Break
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Normal";
    public string Comments { get; set; } = "";
    public Rgba Color { get; set; } = new(0.2, 0.6, 0.4);
    public bool LabelAsPie { get; set; }
    public KeyCombo? StartShortcut { get; set; }
    public bool Enabled { get; set; } = true;
    public BreakSchedule Schedule { get; set; } = new();
    public BreakControls Controls { get; set; } = new();
    public BreakAppearance Appearance { get; set; } = new();
    public List<BreakAction> Actions { get; set; } = [];
    public BreakState State { get; set; } = new();

    public double Duration
    {
        get
        {
            if (Schedule.Adjustment == DueAdjustment.Calendar && State.NextEventStart is { } start && State.NextEventEnd is { } end)
            {
                return Math.Max(5, (end - start).TotalSeconds);
            }
            return Clamp(Schedule.Duration.TotalSeconds, 1, 24 * 3600);
        }
    }

    public double Frequency => Clamp(Schedule.Frequency.TotalSeconds, 10, 30 * 86400);

    /// <summary>Keeps intervals from hand-edited files (NaN, huge, negative) within sane bounds.</summary>
    public static double Clamp(double v, double min, double max) => double.IsFinite(v) ? Math.Min(Math.Max(v, min), max) : min;

    public bool IsLong => Schedule.Duration.TotalSeconds >= 60;

    public bool IsPaused(DateTime now) => State.PausedUntil is { } until && until > now;

    public bool IsActive(DateTime now) => Enabled && !IsPaused(now);

    public int? PostponesRemaining
    {
        get
        {
            if (!Controls.LimitPostpone)
            {
                return null;
            }
            var used = Controls.PostponeLimitKind switch
            {
                LimitKind.PerBreak => State.Counts.BreakPostpone,
                LimitKind.PerHour => State.Counts.HourPostpone,
                _ => State.Counts.TodayPostpone,
            };
            return Math.Max(0, Controls.PostponeLimit - used);
        }
    }

    public int? SkipsRemaining
    {
        get
        {
            if (!Controls.LimitSkip)
            {
                return null;
            }
            var used = Controls.SkipLimitKind switch
            {
                LimitKind.PerBreak => State.Counts.BreakSkip,
                LimitKind.PerHour => State.Counts.HourSkip,
                _ => State.Counts.TodaySkip,
            };
            return Math.Max(0, Controls.SkipLimit - used);
        }
    }

    public bool CanPostpone => (PostponesRemaining ?? 1) > 0;

    public bool CanSkip => (SkipsRemaining ?? 1) > 0;

    public string CountersString
    {
        get
        {
            var c = State.Counts;
            var parts = new List<string> { $"Postponed {c.TodayPostpone}×", $"skipped {c.TodaySkip}×", $"done {c.TodayDone}× today" };
            if (c.TodayDuration > 0)
            {
                parts.Add($"{Format.Full(c.TodayDuration)} in all");
            }
            return string.Join(", ", parts);
        }
    }

    public static Break MakeNormal()
    {
        var b = new Break
        {
            Name = "Normal",
            Comments = "Your longer rest each hour. Get up, walk around, drink some water, and give your eyes something far away to look at.",
            Color = new Rgba(0.22, 0.55, 0.85),
        };
        b.Schedule.Duration = Interval.FromMinutes(10);
        b.Schedule.Frequency = Interval.FromMinutes(60);
        b.Controls.PostponeMinutes1 = 1;
        b.Controls.PostponeMinutes2 = 5;
        var headsUp = new BreakAction(ActionKind.Notification, ActionStage.BeforeStart, 60)
        {
            Title = "Break coming up",
            Message = "Your {break} break starts in a minute.",
        };
        b.Actions.Add(headsUp);
        return b;
    }

    public static Break MakeMicro()
    {
        var b = new Break
        {
            Name = "Micro",
            Comments = "A few seconds every so often: lean back, drop your shoulders, and look across the room.",
            Color = new Rgba(0.95, 0.6, 0.2),
        };
        b.Schedule.Duration = Interval.FromSeconds(15);
        b.Schedule.Frequency = Interval.FromMinutes(15);
        b.Schedule.SkipForHigherPriorityInterval = Interval.FromMinutes(5);
        b.Controls.FadeInSeconds = 1;
        b.Controls.FadeOutSeconds = 1;
        b.Controls.ShowPostpone2 = false;
        b.Controls.PostponeMinutes1 = 1;
        return b;
    }

    /// <summary>An independent copy (breaks are edited in place, so sessions keep their own).</summary>
    public Break Clone() => JsonSerializer.Deserialize<Break>(JsonSerializer.Serialize(this, Store.Json), Store.Json)!;

    public Break Duplicate()
    {
        var b = Clone();
        b.Id = Guid.NewGuid();
        b.Name = Name + " copy";
        b.State = new BreakState();
        b.StartShortcut = null;
        foreach (var a in b.Actions)
        {
            a.Id = Guid.NewGuid();
        }
        return b;
    }
}

public sealed class BreakCounts
{
    public int BreakPostpone { get; set; }
    public int HourPostpone { get; set; }
    public int TodayPostpone { get; set; }
    public int TotalPostpone { get; set; }
    public int BreakSkip { get; set; }
    public int HourSkip { get; set; }
    public int TodaySkip { get; set; }
    public int TotalSkip { get; set; }
    public int TodayStart { get; set; }
    public int TotalStart { get; set; }
    public int TodayDone { get; set; }
    public int TotalDone { get; set; }
    public double TodayDuration { get; set; }
    public double TotalDuration { get; set; }
    public DateTime HourStamp { get; set; } = DateTime.UtcNow;
    public DateTime DayStamp { get; set; } = DateTime.UtcNow;

    /// <summary>Starts new hourly and daily counts when the hour or day has changed. True if anything changed.</summary>
    public bool RollIfNeeded(DateTime now, TimeZoneInfo zone)
    {
        var changed = false;
        var local = LocalTime.ToLocal(now, zone);
        var day = LocalTime.ToLocal(DayStamp, zone);
        if (day.Date != local.Date)
        {
            TodayPostpone = 0;
            TodaySkip = 0;
            TodayStart = 0;
            TodayDone = 0;
            TodayDuration = 0;
            DayStamp = now;
            changed = true;
        }
        var hour = LocalTime.ToLocal(HourStamp, zone);
        if (hour.Date != local.Date || hour.Hour != local.Hour)
        {
            HourPostpone = 0;
            HourSkip = 0;
            HourStamp = now;
            changed = true;
        }
        return changed;
    }
}

public sealed class BreakState
{
    public DateTime? NextDue { get; set; }
    public DateTime? LastStarted { get; set; }
    public DateTime? LastDone { get; set; }
    public DateTime? LastDeferred { get; set; }
    public double LastDuration { get; set; }

    /// <summary><see cref="LocalTime.DistantFuture"/> = paused until resumed.</summary>
    public DateTime? PausedUntil { get; set; }

    public DateTime? NextEventStart { get; set; }
    public DateTime? NextEventEnd { get; set; }
    public string Status { get; set; } = "scheduled";
    public BreakCounts Counts { get; set; } = new();
}
