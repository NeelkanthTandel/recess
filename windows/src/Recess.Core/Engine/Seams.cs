namespace Recess.Core;

// The engine's view of the outside world. The Windows app implements these with system
// APIs; the tests use fakes, so every scheduling rule can be checked with a fake clock.

public interface IClock
{
    DateTime UtcNow { get; }

    TimeZoneInfo Zone { get; }
}

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;

    public TimeZoneInfo Zone => TimeZoneInfo.Local;
}

/// <summary>Idle time, sleep and mouse state.</summary>
public interface ISystemMonitor
{
    /// <summary>Seconds since the last keyboard or mouse input, by the chosen detector (0 for none).</summary>
    double IdleSeconds(IdleDetector detector);

    /// <summary>Seconds since the last input as the system counts it, whatever the detector.</summary>
    double SystemIdleSeconds { get; }

    bool IsMouseButtonDown { get; }

    /// <summary>How long the computer slept, once per wake; null if it hasn't slept since the last call.</summary>
    double? TakeSleepInterval();

    /// <summary>The things rules test. Values should be looked up lazily, when a rule first asks.</summary>
    IRuleEnvironment Snapshot();
}

public interface IRuleEnvironment
{
    /// <summary>The front app's program file name, e.g. "Zoom.exe".</summary>
    string? FrontApp { get; }

    /// <summary>Program file names of running apps.</summary>
    IReadOnlySet<string> RunningApps { get; }

    bool IsFullscreen { get; }

    bool IsScreensaver { get; }

    bool IsDisplayAsleep { get; }

    bool IsFocusOn { get; }

    bool IsMicOrCameraInUse { get; }
}

public interface ICalendarSource
{
    /// <summary>The next event (start and end) after a moment, in the given calendar (null = any) whose title contains the filter.</summary>
    (DateTime Start, DateTime End)? NextEvent(string? calendarId, string filter, DateTime after);

    /// <summary>True while a matching event is happening.</summary>
    bool IsBusy(string? calendarId, string filter, DateTime now);
}

/// <summary>Script-result rule conditions. Scripts run in the background; this returns the latest result.</summary>
public interface IScriptConditions
{
    bool Result(string scriptFilename);
}

/// <summary>Runs break actions (notifications, sounds, speech, flashes, scripts).</summary>
public interface IActionRunner
{
    void Perform(BreakAction action, Break b);

    /// <summary>Feedback when something isn't allowed, e.g. a postpone over the limit.</summary>
    void Beep();
}

public enum AskChoiceKind
{
    Start,
    Postpone,
    Skip,
}

public readonly record struct AskChoice(AskChoiceKind Kind, double Minutes = 0);

/// <summary>The "Ask me first" panel.</summary>
public interface IAskPresenter
{
    bool IsShowing(Guid breakId);

    void Show(Break b, Action<AskChoice> choose);

    void Dismiss();

    void Dismiss(Guid ifShowing);
}

/// <summary>Shows a break session on screen. When the session finishes, the presenter fades out and calls <see cref="BreakSession.Complete"/>.</summary>
public interface IBreakPresenter
{
    void Present(BreakSession session);
}

/// <summary>Does nothing; for tests and for parts of the app that haven't been wired up.</summary>
public sealed class NoCalendar : ICalendarSource
{
    public (DateTime Start, DateTime End)? NextEvent(string? calendarId, string filter, DateTime after) => null;

    public bool IsBusy(string? calendarId, string filter, DateTime now) => false;
}
