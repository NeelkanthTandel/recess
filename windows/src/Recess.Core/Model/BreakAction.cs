using System.Text.Json.Serialization;

namespace Recess.Core;

public enum ActionKind
{
    Notification,
    Sound,
    FadeSound,
    Speech,
    FlashScreen,
    Script,
}

/// <summary>When during a break's life an action happens. <see cref="BreakAction.Offset"/> applies to the "before" stages.</summary>
public enum ActionStage
{
    BeforeStart,
    AtStart,
    AfterFadeIn,
    BeforeEnd,
    AtEnd,
    AfterPostponeOrSkip,
}

public static class ActionTitles
{
    public static string DisplayName(this ActionKind v) => v switch
    {
        ActionKind.Notification => "Notification",
        ActionKind.Sound => "Play Sound",
        ActionKind.FadeSound => "Fade Out Sound",
        ActionKind.Speech => "Speak",
        ActionKind.FlashScreen => "Flash Screen",
        _ => "Run Script",
    };

    public static string Title(this ActionStage v) => v switch
    {
        ActionStage.BeforeStart => "ahead of the break",
        ActionStage.AtStart => "at start",
        ActionStage.AfterFadeIn => "after fade in",
        ActionStage.BeforeEnd => "before end",
        ActionStage.AtEnd => "when it ends",
        _ => "when postponed or skipped",
    };

    public static bool UsesOffset(this ActionStage v) => v is ActionStage.BeforeStart or ActionStage.BeforeEnd;
}

public sealed class BreakAction
{
    [JsonConstructor]
    public BreakAction()
    {
    }

    public BreakAction(ActionKind kind, ActionStage stage, double offset = 0)
    {
        Kind = kind;
        Stage = stage;
        Offset = offset;
        switch (kind)
        {
            case ActionKind.Notification:
                Title = "Break time";
                Message = "Time for a {break} break.";
                break;
            case ActionKind.Speech:
                Message = "Time for a {break} break.";
                break;
            case ActionKind.Sound:
                SoundName = "Windows Notify Calendar";
                break;
        }
    }

    public Guid Id { get; set; } = Guid.NewGuid();
    public ActionKind Kind { get; set; }
    public ActionStage Stage { get; set; } = ActionStage.AtStart;

    /// <summary>Seconds before the stage, for <see cref="ActionStage.BeforeStart"/> and <see cref="ActionStage.BeforeEnd"/>.</summary>
    public double Offset { get; set; }

    public bool Enabled { get; set; } = true;
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    public string SoundName { get; set; } = "";
    public string SoundPath { get; set; } = "";
    public double Volume { get; set; } = 0.8;
    public bool Loops { get; set; }
    public double FadeSeconds { get; set; } = 3;
    public string VoiceIdentifier { get; set; } = "";
    public double Rate { get; set; } = 0.5;
    public double Pitch { get; set; } = 1;
    public string ScriptFilename { get; set; } = "";

    [JsonIgnore]
    public string Summary => Kind switch
    {
        ActionKind.Notification => Title.Length == 0 ? Message : Title,
        ActionKind.Sound => SoundName.Length == 0 ? Path.GetFileName(SoundPath) : SoundName,
        ActionKind.FadeSound => $"over {Format.Int(FadeSeconds)} seconds",
        ActionKind.Speech => Message,
        ActionKind.FlashScreen => "flash all screens",
        _ => ScriptFilename.Length == 0 ? "No script chosen" : ScriptFilename,
    };

    /// <summary>Fills in {break}, {duration} and {time}.</summary>
    public static string Expand(string text, Break b, DateTime now, TimeZoneInfo zone) => text
        .Replace("{break}", b.Name, StringComparison.Ordinal)
        .Replace("{duration}", Format.Full(b.Duration), StringComparison.Ordinal)
        .Replace("{time}", Format.Time(now, zone), StringComparison.Ordinal);
}
