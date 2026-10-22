namespace Recess.Core;

public enum RuleCondition
{
    FrontApp,
    OpenApp,
    Focus,
    Fullscreen,
    Screensaver,
    DisplayAsleep,
    CalendarEvent,
    Script,

    /// <summary>Windows only: an app is using the microphone or camera (usually a call).</summary>
    MicOrCamera,
}

public enum RuleAction
{
    Postpone1,
    Postpone2,
    Postpone5,
    Postpone10,
    Postpone20,
    Postpone30,
    Skip,
    TreatAsNaturalBreak,
    IgnoreNaturalBreak,
}

public static class RuleTitles
{
    public static string Title(this RuleCondition v) => v switch
    {
        RuleCondition.FrontApp => "Front app",
        RuleCondition.OpenApp => "Open app",
        RuleCondition.Focus => "Do not disturb",
        RuleCondition.Fullscreen => "Fullscreen app",
        RuleCondition.Screensaver => "Screensaver or locked screen",
        RuleCondition.DisplayAsleep => "Display off",
        RuleCondition.CalendarEvent => "Calendar event",
        RuleCondition.Script => "Script result",
        _ => "Microphone or camera in use",
    };

    public static bool NeedsApps(this RuleCondition v) => v is RuleCondition.FrontApp or RuleCondition.OpenApp;

    public static string Title(this RuleAction v) => v switch
    {
        RuleAction.Postpone1 => "postpone 1 minute",
        RuleAction.Postpone2 => "postpone 2 minutes",
        RuleAction.Postpone5 => "postpone 5 minutes",
        RuleAction.Postpone10 => "postpone 10 minutes",
        RuleAction.Postpone20 => "postpone 20 minutes",
        RuleAction.Postpone30 => "postpone 30 minutes",
        RuleAction.Skip => "skip the break",
        RuleAction.TreatAsNaturalBreak => "count it as time away",
        _ => "don't count it as time away",
    };

    public static double? PostponeMinutes(this RuleAction v) => v switch
    {
        RuleAction.Postpone1 => 1,
        RuleAction.Postpone2 => 2,
        RuleAction.Postpone5 => 5,
        RuleAction.Postpone10 => 10,
        RuleAction.Postpone20 => 20,
        RuleAction.Postpone30 => 30,
        _ => null,
    };

    public static bool IsDueAction(this RuleAction v) => v.PostponeMinutes() is not null || v == RuleAction.Skip;
}

public sealed class Rule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public bool Enabled { get; set; } = true;
    public RuleCondition Condition { get; set; } = RuleCondition.FrontApp;

    /// <summary>"is not" instead of "is".</summary>
    public bool Negate { get; set; }

    /// <summary>Program file names for app conditions, e.g. "Zoom.exe".</summary>
    public List<string> Apps { get; set; } = [];

    /// <summary>Calendar (null = any) for calendar conditions.</summary>
    public string? CalendarIdentifier { get; set; }

    public string CalendarFilter { get; set; } = "";
    public string ScriptFilename { get; set; } = "";
    public RuleAction Action { get; set; } = RuleAction.Postpone5;

    /// <summary>Empty = applies to every break.</summary>
    public List<Guid> BreakIds { get; set; } = [];

    public bool AppliesTo(Break b) => BreakIds.Count == 0 || BreakIds.Contains(b.Id);
}
