using System.Text.Json.Serialization;

namespace Recess.Core;

public enum BreakEventKind
{
    Started,
    Done,
    EndedEarly,
    Postponed,
    Skipped,
    NaturalBreak,
}

public static class ActivityTitles
{
    public static string Title(this BreakEventKind v) => v switch
    {
        BreakEventKind.Started => "Started",
        BreakEventKind.Done => "Done",
        BreakEventKind.EndedEarly => "Ended early",
        BreakEventKind.Postponed => "Postponed",
        BreakEventKind.Skipped => "Skipped",
        _ => "Natural break",
    };
}

public sealed class BreakEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime Date { get; set; }
    public Guid BreakId { get; set; }
    public string BreakName { get; set; } = "";
    public BreakEventKind Kind { get; set; }
    public double Duration { get; set; }
    public string Detail { get; set; } = "";
}

/// <summary>A stretch of time with one app in front (or idle, when <see cref="AppId"/> is empty).</summary>
public sealed class AppSpan
{
    /// <summary>The program's file name, e.g. "Code.exe".</summary>
    public string AppId { get; set; } = "";

    public string Name { get; set; } = "";
    public DateTime Start { get; set; }
    public DateTime End { get; set; }

    [JsonIgnore]
    public double Duration => (End - Start).TotalSeconds;
}

public sealed class ActivityDayRecord
{
    /// <summary>yyyy-MM-dd</summary>
    public string Day { get; set; } = "";

    public List<BreakEvent> Events { get; set; } = [];
    public List<AppSpan> Apps { get; set; } = [];
}
