namespace Recess.Core.Tests;

/// <summary>Fixed time zone so results don't depend on the machine running the tests.</summary>
internal static class Zones
{
    public static readonly TimeZoneInfo NewYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    /// <summary>A New York wall-clock time as UTC.</summary>
    public static DateTime Date(int y, int m, int d, int h, int min = 0) => LocalTime.At(new DateOnly(y, m, d), h, min, NewYork);

    /// <summary>"06/01 10:00" in New York time.</summary>
    public static string Hm(DateTime utc) => LocalTime.ToLocal(utc, NewYork).ToString("MM/dd HH:mm", System.Globalization.CultureInfo.InvariantCulture);
}

internal sealed class FakeClock(DateTime start) : IClock
{
    public DateTime UtcNow { get; set; } = start;

    public TimeZoneInfo Zone => Zones.NewYork;

    public void Advance(double seconds) => UtcNow = UtcNow.AddSeconds(seconds);
}

internal sealed class FakeEnvironment : IRuleEnvironment
{
    public string? FrontApp { get; set; }

    public HashSet<string> Running { get; } = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlySet<string> RunningApps => Running;

    public bool IsFullscreen { get; set; }

    public bool IsScreensaver { get; set; }

    public bool IsDisplayAsleep { get; set; }

    public bool IsFocusOn { get; set; }

    public bool IsMicOrCameraInUse { get; set; }
}

internal sealed class FakeSystem : ISystemMonitor
{
    public double Idle { get; set; }

    public double? PendingSleep { get; set; }

    public bool MouseDown { get; set; }

    public FakeEnvironment Environment { get; } = new();

    public double IdleSeconds(IdleDetector detector) => detector == IdleDetector.None ? 0 : Idle;

    public double SystemIdleSeconds => Idle;

    public bool IsMouseButtonDown => MouseDown;

    public double? TakeSleepInterval()
    {
        var s = PendingSleep;
        PendingSleep = null;
        return s;
    }

    public IRuleEnvironment Snapshot() => Environment;
}

internal sealed class FakeAsk : IAskPresenter
{
    public Guid? Showing { get; private set; }

    public Action<AskChoice>? Choose { get; private set; }

    public bool IsShowing(Guid breakId) => Showing == breakId;

    public void Show(Break b, Action<AskChoice> choose)
    {
        Showing = b.Id;
        Choose = c =>
        {
            Showing = null;
            choose(c);
        };
    }

    public void Dismiss() => Showing = null;

    public void Dismiss(Guid ifShowing)
    {
        if (Showing == ifShowing)
        {
            Showing = null;
        }
    }
}

/// <summary>Shows nothing; when a session finishes it completes straight away (no fade).</summary>
internal sealed class FakePresenter : IBreakPresenter
{
    public List<BreakSession> Sessions { get; } = [];

    public BreakSession? Current => Sessions.Count > 0 && !Sessions[^1].IsFinished ? Sessions[^1] : null;

    public void Present(BreakSession session)
    {
        Sessions.Add(session);
        session.Finishing += _ => session.Complete();
    }
}

internal sealed class FakeActions : IActionRunner
{
    public List<(BreakAction Action, string Break)> Performed { get; } = [];

    public int Beeps { get; private set; }

    public void Perform(BreakAction action, Break b) => Performed.Add((action, b.Name));

    public void Beep() => Beeps++;
}

internal sealed class FakeScripts : IScriptConditions
{
    public HashSet<string> True { get; } = [];

    public bool Result(string scriptFilename) => True.Contains(scriptFilename);
}

internal sealed class FakeCalendar : ICalendarSource
{
    public List<(DateTime Start, DateTime End)> Events { get; } = [];

    public (DateTime Start, DateTime End)? NextEvent(string? calendarId, string filter, DateTime after) =>
        Events.Where(e => e.Start >= after).OrderBy(e => e.Start).Cast<(DateTime, DateTime)?>().FirstOrDefault();

    public bool IsBusy(string? calendarId, string filter, DateTime now) => Events.Any(e => e.Start <= now && now < e.End);
}

/// <summary>A scheduler wired to fakes, with the default Normal and Micro breaks.</summary>
internal sealed class Harness : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "recess-tests-" + Guid.NewGuid());

    public Harness(params Break[] breaks)
    {
        Clock = new FakeClock(Zones.Date(2026, 6, 1, 9, 0));
        Model = new RecessModel(breaks.Length > 0 ? [.. breaks] : [Break.MakeNormal(), Break.MakeMicro()], new AppSettings());
        Activity = new ActivityRecorder(Path.Combine(folder, "Activity"), () => Model.Settings, Clock);
        Scheduler = new Scheduler(Model, Clock, System, Ask, Presenter, Actions, Calendar, Scripts, Activity);
    }

    public FakeClock Clock { get; }

    public FakeSystem System { get; } = new();

    public FakeAsk Ask { get; } = new();

    public FakePresenter Presenter { get; } = new();

    public FakeActions Actions { get; } = new();

    public FakeCalendar Calendar { get; } = new();

    public FakeScripts Scripts { get; } = new();

    public RecessModel Model { get; }

    public ActivityRecorder Activity { get; }

    public Scheduler Scheduler { get; }

    public Break Normal => Model.FindByName("Normal")!;

    public Break Micro => Model.FindByName("Micro")!;

    /// <summary>Runs one-second cycles for a while, as the app's timer would.</summary>
    public void Run(double seconds)
    {
        for (var i = 0; i < seconds; i++)
        {
            Clock.Advance(1);
            Scheduler.Cycle();
        }
    }

    /// <summary>Runs cycles until a break is on screen, or gives up after a limit.</summary>
    public BreakSession RunUntilBreak(double limit = 7200)
    {
        for (var i = 0; i < limit && Presenter.Current is null; i++)
        {
            Clock.Advance(1);
            Scheduler.Cycle();
        }
        return Presenter.Current ?? throw new InvalidOperationException("No break started.");
    }

    public void Dispose()
    {
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
