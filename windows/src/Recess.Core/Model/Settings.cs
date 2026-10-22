namespace Recess.Core;

/// <summary>The tray icon's picture.</summary>
public enum StatusIcon
{
    None,
    AppIcon,
    PalmTree,
    BeachUmbrella,
    SunHorizon,
    Cup,
    Waves,
    Cardio,
    Strength,
    MindBody,
    LastDoneLabel,
    NextDueLabel,
}

/// <summary>What the tray icon tells you about your breaks.</summary>
public enum StatusTitle
{
    OnlyIcon,
    BriefSinceLast,
    FullSinceLast,
    DoneTimeOfLast,
    DurationOfLast,
    BriefCountdown,
    FullCountdown,
    DueTimeOfNext,
    FinishTimeOfNext,
    DurationOfNext,
}

public enum IdleDetector
{
    /// <summary>The system's last-input time.</summary>
    EventSource,

    /// <summary>Kept so settings files stay compatible; on Windows it uses the same source.</summary>
    EventMonitor,
    None,
}

public static class SettingsTitles
{
    public static string Title(this StatusIcon v) => v switch
    {
        StatusIcon.None => "No icon",
        StatusIcon.AppIcon => "App icon",
        StatusIcon.PalmTree => "Palm tree",
        StatusIcon.BeachUmbrella => "Beach umbrella",
        StatusIcon.SunHorizon => "Sun on the horizon",
        StatusIcon.Cup => "Cup and saucer",
        StatusIcon.Waves => "Water waves",
        StatusIcon.Cardio => "Running figure",
        StatusIcon.Strength => "Weights",
        StatusIcon.MindBody => "Meditating figure",
        StatusIcon.LastDoneLabel => "Colour of the last break",
        _ => "Colour of the next break",
    };

    public static string Title(this StatusTitle v) => v switch
    {
        StatusTitle.OnlyIcon => "Nothing (icon only)",
        StatusTitle.BriefSinceLast => "Since last break (short)",
        StatusTitle.FullSinceLast => "Since last break (long)",
        StatusTitle.DoneTimeOfLast => "When the last break ended",
        StatusTitle.DurationOfLast => "Length of the last break",
        StatusTitle.BriefCountdown => "Until next break (short)",
        StatusTitle.FullCountdown => "Until next break (mm:ss)",
        StatusTitle.DueTimeOfNext => "When the next break starts",
        StatusTitle.FinishTimeOfNext => "When the next break ends",
        _ => "Length of the next break",
    };

    public static string Title(this IdleDetector v) => v switch
    {
        IdleDetector.None => "None",
        _ => "System idle time",
    };
}

/// <summary>A global keyboard shortcut: a Windows virtual-key code plus MOD_ALT/CONTROL/SHIFT/WIN flags.</summary>
public sealed record KeyCombo(uint Key, uint Modifiers);

public sealed class Shortcuts
{
    public KeyCombo? StartNext { get; set; }
    public KeyCombo? Postpone1 { get; set; }
    public KeyCombo? Postpone2 { get; set; }
    public KeyCombo? Skip { get; set; }
    public KeyCombo? PauseIndefinitely { get; set; }
    public KeyCombo? Resume { get; set; }
    public KeyCombo? Reset { get; set; }
    public KeyCombo? StopSound { get; set; }
}

/// <summary>A calendar Recess reads events from: a private iCal address or an .ics file.</summary>
public sealed class CalendarFeed
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "Calendar";

    /// <summary>The iCal address, encrypted for this Windows user (it works like a password).</summary>
    public string ProtectedUrl { get; set; } = "";

    /// <summary>A local .ics file, used when there's no address.</summary>
    public string FilePath { get; set; } = "";

    public bool Enabled { get; set; } = true;
}

public sealed class AppSettings
{
    // General
    public bool ShowDockIcon { get; set; }
    public bool ShowStatusMenu { get; set; } = true;
    public StatusIcon StatusIcon { get; set; } = StatusIcon.Cup;
    public StatusTitle StatusTitle { get; set; } = StatusTitle.BriefCountdown;
    public bool StatusOnlyLong { get; set; }
    public bool RelativeDates { get; set; } = true;
    public bool LaunchAtLogin { get; set; }
    public bool HasCompletedSetup { get; set; }

    // Shortcuts
    public Shortcuts Shortcuts { get; set; } = new();

    // Rules
    public List<Rule> Rules { get; set; } = [];

    // Calendars
    public List<CalendarFeed> Calendars { get; set; } = [];

    // Activity
    public bool TrackBreaks { get; set; } = true;
    public bool TrackApps { get; set; }

    // Advanced
    public IdleDetector IdleDetector { get; set; } = IdleDetector.EventSource;
    public double NaturalBreakThreshold { get; set; } = 60;
    public bool HidePause { get; set; }
    public bool HideMakeDue { get; set; }
    public bool HideReset { get; set; }
    public bool HideDisable { get; set; }
    public bool HideDelete { get; set; }
    public bool HideQuit { get; set; }
    public bool DisableAppSwitching { get; set; }

    /// <summary>Keep the taskbar visible during breaks (the Mac keeps its menu bar).</summary>
    public bool ShowMenuBarDuringBreak { get; set; }

    /// <summary>Accept recess:// links from other apps and web pages.</summary>
    public bool AllowUrlCommands { get; set; } = true;
}
