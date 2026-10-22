using System.Text.Json;
using static Recess.Core.Tests.Zones;

namespace Recess.Core.Tests;

// Ported from the Mac app's ModelTests.
public class FormatTests
{
    [Fact]
    public void IntNeverFails()
    {
        Assert.Equal(0, Format.Int(double.NaN));
        Assert.Equal(0, Format.Int(double.PositiveInfinity));
        Assert.Equal(1_000_000_000_000_000, Format.Int(1e300));
        Assert.Equal(42, Format.Int(42.9));
    }

    [Fact]
    public void Countdown()
    {
        Assert.Equal("0:09", Format.Countdown(9));
        Assert.Equal("1:05", Format.Countdown(65));
        Assert.Equal("1:02:05", Format.Countdown(3725));
        Assert.Equal("0:00", Format.Countdown(-5));
        Assert.Equal("0:00", Format.Countdown(double.NaN));
        Assert.Equal("0:10", Format.Countdown(9.2)); // rounds up, so 0:00 only at the very end
    }

    [Fact]
    public void BriefAndFull()
    {
        Assert.Equal("30s", Format.Brief(30));
        Assert.Equal("5m", Format.Brief(5 * 60));
        Assert.Equal("1h 5m", Format.Brief(3900));
        Assert.Equal("1 minute", Format.Full(60));
        Assert.Equal("1 hour, 5 minutes", Format.Full(3900));
        Assert.Equal("3s", Format.Brief(2.5)); // halves round up, not to even
    }
}

public class BreakTests
{
    [Fact]
    public void PostponeLimitPerHour()
    {
        var b = new Break();
        b.Controls.LimitPostpone = true;
        b.Controls.PostponeLimit = 2;
        b.Controls.PostponeLimitKind = LimitKind.PerHour;
        Assert.True(b.CanPostpone);
        b.State.Counts.HourPostpone = 2;
        Assert.False(b.CanPostpone);
        Assert.Equal(0, b.PostponesRemaining);
    }

    [Fact]
    public void SkipLimitOfZeroBlocksSkipping()
    {
        var b = new Break();
        b.Controls.LimitSkip = true;
        b.Controls.SkipLimit = 0;
        Assert.False(b.CanSkip);
    }

    [Fact]
    public void NoLimitsMeansAlwaysAllowed()
    {
        var b = new Break();
        b.State.Counts.TodayPostpone = 100;
        Assert.True(b.CanPostpone);
        Assert.Null(b.PostponesRemaining);
    }

    [Fact]
    public void CountersRollOverByHourAndDay()
    {
        var now = Date(2026, 6, 3, 12, 0);
        var c = new BreakCounts
        {
            HourPostpone = 3,
            TodayPostpone = 5,
            TotalPostpone = 9,
            HourStamp = now.AddHours(-2),
            DayStamp = now.AddDays(-2),
        };
        Assert.True(c.RollIfNeeded(now, NewYork));
        Assert.Equal(0, c.HourPostpone);
        Assert.Equal(0, c.TodayPostpone);
        Assert.Equal(9, c.TotalPostpone);
        Assert.False(c.RollIfNeeded(now.AddMinutes(1), NewYork));
    }

    [Fact]
    public void NewBreaksStartOnTimeByDefault()
    {
        // Waiting for a pause can make a break up to a minute late, so it has to be chosen explicitly.
        Assert.Equal(DueBehavior.StartImmediately, new BreakSchedule().DueBehavior);
        Assert.Equal(DueBehavior.StartImmediately, Break.MakeNormal().Schedule.DueBehavior);
        Assert.Equal(DueBehavior.StartImmediately, Break.MakeMicro().Schedule.DueBehavior);
    }

    [Fact]
    public void IntervalsFromHandEditedFilesAreClamped()
    {
        var b = new Break();
        b.Schedule.Frequency = new Interval(double.NaN, TimeUnit.Minutes);
        b.Schedule.Duration = new Interval(1e12, TimeUnit.Hours);
        Assert.Equal(10, b.Frequency);
        Assert.Equal(24 * 3600, b.Duration);
    }

    [Fact]
    public void DefaultBreaksMatchTheMacApp()
    {
        var normal = Break.MakeNormal();
        Assert.Equal(600, normal.Duration);
        Assert.Equal(3600, normal.Frequency);
        var headsUp = Assert.Single(normal.Actions);
        Assert.Equal(ActionKind.Notification, headsUp.Kind);
        Assert.Equal(ActionStage.BeforeStart, headsUp.Stage);
        Assert.Equal(60, headsUp.Offset);

        var micro = Break.MakeMicro();
        Assert.Equal(15, micro.Duration);
        Assert.Equal(900, micro.Frequency);
        Assert.False(micro.Controls.ShowPostpone2);
        Assert.Empty(micro.Actions);
    }

    [Fact]
    public void DuplicatesGetNewIdsAndAFreshState()
    {
        var b = Break.MakeNormal();
        b.State.Counts.TotalDone = 4;
        var copy = b.Duplicate();
        Assert.NotEqual(b.Id, copy.Id);
        Assert.NotEqual(b.Actions[0].Id, copy.Actions[0].Id);
        Assert.Equal("Normal copy", copy.Name);
        Assert.Equal(0, copy.State.Counts.TotalDone);
    }
}

public class StoreTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "recess-store-" + Guid.NewGuid());

    public void Dispose()
    {
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void MissingKeysAreFilledFromDefaults()
    {
        // An older settings file that predates most options.
        var s = Store.DecodeMerging<AppSettings>("""{"showDockIcon": true, "statusTitle": "onlyIcon"}""")!;
        Assert.True(s.ShowDockIcon);
        Assert.Equal(StatusTitle.OnlyIcon, s.StatusTitle);
        Assert.Equal(new AppSettings().NaturalBreakThreshold, s.NaturalBreakThreshold);
        Assert.True(s.AllowUrlCommands);
    }

    [Fact]
    public void NestedKeysAreFilledToo()
    {
        var b = Store.DecodeMerging<Break>("""{"name": "Stretch", "schedule": {"duration": {"amount": 3, "units": "minutes"}}}""")!;
        Assert.Equal("Stretch", b.Name);
        Assert.Equal(Interval.FromMinutes(3), b.Schedule.Duration);
        Assert.Equal(new BreakSchedule().Frequency, b.Schedule.Frequency);
    }

    [Fact]
    public void RoundTrip()
    {
        var b = Break.MakeMicro();
        b.State.NextDue = new DateTime(2027, 1, 15, 8, 0, 0, DateTimeKind.Utc);
        var back = Store.DecodeMerging<Break>(JsonSerializer.Serialize(b, Store.Json))!;
        Assert.Equal(b.Name, back.Name);
        Assert.Equal(b.Schedule.TimingKey, back.Schedule.TimingKey);
        Assert.Equal(b.State.NextDue, back.State.NextDue);
    }

    [Fact]
    public void EnumsAreWrittenAsCamelCaseNames()
    {
        var json = JsonSerializer.Serialize(new BreakSchedule(), Store.Json);
        Assert.Contains("\"adjustment\": \"lastDue\"", json, StringComparison.Ordinal);
        Assert.Contains("\"dueBehavior\": \"startImmediately\"", json, StringComparison.Ordinal);
        Assert.Contains("\"units\": \"minutes\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnknownActionIsDroppedButItsBreakIsKept()
    {
        var store = new Store(folder);
        Directory.CreateDirectory(folder);
        File.WriteAllText(store.BreaksPath, """
            [{"name": "Stretch", "actions": [{"kind": "teleport"}, {"kind": "sound", "soundName": "Chime"}]}]
            """);
        var breaks = store.LoadBreaks()!;
        var b = Assert.Single(breaks);
        Assert.Equal("Stretch", b.Name);
        Assert.Equal("Chime", Assert.Single(b.Actions).SoundName);
        // The original was kept, since the next save drops the unknown action.
        Assert.Single(Directory.GetFiles(folder, "Breaks.unreadable-*.json"));
    }

    [Fact]
    public void AnUnknownRuleIsDroppedButOtherSettingsAreKept()
    {
        var store = new Store(folder);
        Directory.CreateDirectory(folder);
        File.WriteAllText(store.SettingsPath, """
            {"naturalBreakThreshold": 90, "rules": [{"condition": "moonPhase"}, {"condition": "fullscreen", "action": "skip"}]}
            """);
        var s = store.LoadSettings()!;
        Assert.Equal(90, s.NaturalBreakThreshold);
        Assert.Equal(RuleCondition.Fullscreen, Assert.Single(s.Rules).Condition);
    }

    [Fact]
    public void AnUnreadableFileIsBackedUpAndDefaultsAreUsed()
    {
        var store = new Store(folder);
        Directory.CreateDirectory(folder);
        File.WriteAllText(store.SettingsPath, "{ not json");
        Assert.Null(store.LoadSettings());
        Assert.Single(Directory.GetFiles(folder, "Settings.unreadable-*.json"));
        var model = RecessModel.Load(store);
        Assert.Equal(["Normal", "Micro"], model.Breaks.Select(b => b.Name));
    }

    [Fact]
    public void AnEmptyBreakListIsRespected()
    {
        var store = new Store(folder);
        Directory.CreateDirectory(folder);
        File.WriteAllText(store.BreaksPath, "[]");
        Assert.Empty(RecessModel.Load(store).Breaks);
    }

    [Fact]
    public void SavingAndLoadingKeepsEverything()
    {
        var store = new Store(folder);
        var model = new RecessModel([Break.MakeNormal()], new AppSettings { StatusTitle = StatusTitle.FullCountdown }, store);
        model.MarkChanged();
        model.SaveIfChanged();
        var loaded = RecessModel.Load(store);
        Assert.Equal(StatusTitle.FullCountdown, loaded.Settings.StatusTitle);
        Assert.Equal(model.Breaks[0].Id, loaded.Breaks[0].Id);
        Assert.Equal("Your {break} break starts in a minute.", loaded.Breaks[0].Actions[0].Message);
    }
}

public class SafetyTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "recess-scripts-" + Guid.NewGuid());

    public void Dispose()
    {
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void YouTubeIdsAreValidated()
    {
        Assert.Equal("dQw4w9WgXcQ", ThemeText.YouTubeId("https://www.youtube.com/watch?v=dQw4w9WgXcQ"));
        Assert.Equal("dQw4w9WgXcQ", ThemeText.YouTubeId("https://youtu.be/dQw4w9WgXcQ"));
        Assert.Equal("dQw4w9WgXcQ", ThemeText.YouTubeId("https://www.youtube.com/embed/dQw4w9WgXcQ"));
        Assert.Equal("dQw4w9WgXcQ", ThemeText.YouTubeId("dQw4w9WgXcQ"));
        Assert.Null(ThemeText.YouTubeId("https://youtu.be/x%22%3E%3Cscript%3Ealert(1)%3C/script%3E"));
        Assert.Null(ThemeText.YouTubeId("https://example.com/watch?v=\"><script>"));
        Assert.Null(ThemeText.YouTubeId(""));
    }

    [Fact]
    public void FontNamesCantBreakOutOfCss()
    {
        Assert.Equal("Segoe UI", ThemeText.SafeFontName("Segoe UI"));
        Assert.Equal("xstylescript", ThemeText.SafeFontName("x';}</style><script>"));
        Assert.Equal("Segoe UI", ThemeText.SafeFontName("';"));
    }

    [Fact]
    public void ScriptNamesCantLeaveTheScriptsFolder()
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "ok.ps1"), "'true'");
        File.WriteAllText(Path.Combine(folder, "notes.txt"), "not a script");

        Assert.NotNull(ScriptPaths.Resolve("ok.ps1", folder));
        Assert.Null(ScriptPaths.Resolve("../ok.ps1", folder));
        Assert.Null(ScriptPaths.Resolve("..\\ok.ps1", folder));
        Assert.Null(ScriptPaths.Resolve("sub/ok.ps1", folder));
        Assert.Null(ScriptPaths.Resolve("..", folder));
        Assert.Null(ScriptPaths.Resolve("", folder));
        Assert.Null(ScriptPaths.Resolve("missing.ps1", folder));
        Assert.Null(ScriptPaths.Resolve("notes.txt", folder));
        Assert.Null(ScriptPaths.Resolve("C:\\Windows\\System32\\cmd.exe", folder));
        Assert.Null(ScriptPaths.Resolve("\\\\server\\share\\x.ps1", folder));
    }

    [Fact]
    public void ScriptLinksToOtherFoldersAreRefused()
    {
        Directory.CreateDirectory(folder);
        var outside = Path.Combine(Path.GetTempPath(), "recess-outside-" + Guid.NewGuid() + ".ps1");
        File.WriteAllText(outside, "'true'");
        try
        {
            File.CreateSymbolicLink(Path.Combine(folder, "link.ps1"), outside);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            // Creating links needs Developer Mode or admin rights on Windows; nothing to test then.
            File.Delete(outside);
            return;
        }
        Assert.Null(ScriptPaths.Resolve("link.ps1", folder));
        File.Delete(outside);
    }

    [Theory]
    [InlineData("run.ps1", true)]
    [InlineData("Stop Music.cmd", true)]
    [InlineData("check-call.bat", true)]
    [InlineData("tool.exe", true)]
    [InlineData("a&calc.bat", false)]
    [InlineData("100%.bat", false)]
    [InlineData("hi!.cmd", false)]
    [InlineData("x.ps1:stream", false)]
    [InlineData("run.ps1.", false)]
    [InlineData("run.ps1 ", false)]
    [InlineData("CON.ps1", false)]
    [InlineData("nul.bat", false)]
    [InlineData("script.vbs", false)]
    [InlineData("script.sh", false)]
    public void ScriptNamesWindowsCouldReinterpretAreRefused(string name, bool safe) => Assert.Equal(safe, ScriptPaths.IsSafeName(name));
}
