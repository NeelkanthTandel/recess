using static Recess.Core.Tests.Zones;

namespace Recess.Core.Tests;

public class UrlCommandTests
{
    private sealed class Host : IUrlCommandHost
    {
        public int Stops { get; private set; }

        public (string? Page, Guid? Break)? Opened { get; private set; }

        public void StopSound() => Stops++;

        public void OpenSettings(string? page, Guid? breakId) => Opened = (page, breakId);
    }

    [Theory]
    [InlineData("recess://start", "start")]
    [InlineData("recess://start/", "start")]
    [InlineData("recess:start", "start")]
    [InlineData("RECESS://Start?break=Normal", "start")]
    [InlineData("recess://makedue/?seconds=5", "makedue")]
    public void ParsesTheForms(string url, string name) => Assert.Equal(name, UrlCommand.Parse(url)?.Name);

    [Theory]
    [InlineData("https://example.com/start")]
    [InlineData("recess://")]
    [InlineData("recess://st art")]
    [InlineData("recess://../settings")]
    [InlineData("")]
    [InlineData(null)]
    public void RefusesAnythingElse(string? url) => Assert.Null(UrlCommand.Parse(url));

    [Fact]
    public void RefusesOverlongLinks() => Assert.Null(UrlCommand.Parse("recess://start?break=" + new string('x', 3000)));

    [Fact]
    public void DecodesValuesAndClampsNumbers()
    {
        var url = UrlCommand.Parse("recess://postpone?Break=Eye%20Rest&minutes=1e9&break=second")!;
        Assert.Equal("Eye Rest", url["break"]);
        Assert.Equal(1440, url.Number("minutes", 0, 1440));
        Assert.Null(UrlCommand.Parse("recess://pause?minutes=NaN")!.Number("minutes", 0, 1440));
        Assert.Equal(0, UrlCommand.Parse("recess://pause?minutes=-5")!.Number("minutes", 0, 1440));
        Assert.Equal("a b", UrlCommand.Parse("recess://start?break=a+b")!["break"]);
    }

    [Fact]
    public void RunsCommandsByBreakName()
    {
        using var h = new Harness();
        h.Scheduler.Start();
        var host = new Host();
        Assert.True(UrlCommands.Run(UrlCommand.Parse("recess://postpone?break=normal&minutes=10")!, h.Model, h.Scheduler, host, h.Clock.UtcNow));
        Assert.Equal(h.Clock.UtcNow.AddMinutes(10), h.Normal.State.NextDue);

        UrlCommands.Run(UrlCommand.Parse("recess://makedue?break=Micro&seconds=3")!, h.Model, h.Scheduler, host, h.Clock.UtcNow);
        h.Run(4);
        Assert.Equal("Micro", h.Scheduler.Session?.Break.Name);
    }

    [Fact]
    public void SettingsLinksOnlyOpenKnownPages()
    {
        using var h = new Harness();
        var host = new Host();
        UrlCommands.Run(UrlCommand.Parse("recess://settings?page=rules")!, h.Model, h.Scheduler, host, h.Clock.UtcNow);
        Assert.Equal(("rules", (Guid?)null), host.Opened);
        UrlCommands.Run(UrlCommand.Parse("recess://preferences?page=../../evil&break=Micro")!, h.Model, h.Scheduler, host, h.Clock.UtcNow);
        Assert.Equal((null, h.Micro.Id), host.Opened);
    }

    [Fact]
    public void LinksCanBeTurnedOff()
    {
        using var h = new Harness();
        h.Scheduler.Start();
        h.Model.Settings.AllowUrlCommands = false;
        var host = new Host();
        var due = h.Normal.State.NextDue;
        Assert.False(UrlCommands.Run(UrlCommand.Parse("recess://skip?break=Normal")!, h.Model, h.Scheduler, host, h.Clock.UtcNow));
        Assert.False(UrlCommands.Run(UrlCommand.Parse("recess://stopsound")!, h.Model, h.Scheduler, host, h.Clock.UtcNow));
        Assert.Equal(due, h.Normal.State.NextDue);
        Assert.Equal(0, host.Stops);
    }

    [Fact]
    public void UnknownCommandsAreIgnored()
    {
        using var h = new Harness();
        Assert.False(UrlCommands.Run(UrlCommand.Parse("recess://format")!, h.Model, h.Scheduler, new Host(), h.Clock.UtcNow));
    }
}

public class RulesTests
{
    private static readonly FakeEnvironment Env = new() { FrontApp = "Teams.exe" };

    private static RulesEngine Engine => new(new NoCalendar(), new FakeScripts());

    [Fact]
    public void AppNamesMatchWhateverTheCase()
    {
        var rule = new Rule { Condition = RuleCondition.FrontApp, Apps = ["teams.EXE"] };
        Assert.Same(rule, Engine.DueRule(new Break(), [rule], Env, DateTime.UtcNow));
    }

    [Fact]
    public void NegatedRulesMatchTheOpposite()
    {
        var rule = new Rule { Condition = RuleCondition.FrontApp, Apps = ["Zoom.exe"], Negate = true };
        Assert.Same(rule, Engine.DueRule(new Break(), [rule], Env, DateTime.UtcNow));
    }

    [Fact]
    public void RulesCanApplyToSomeBreaksOnly()
    {
        var target = new Break();
        var rule = new Rule { Condition = RuleCondition.FrontApp, Apps = ["Teams.exe"], BreakIds = [target.Id] };
        Assert.Null(Engine.DueRule(new Break(), [rule], Env, DateTime.UtcNow));
        Assert.Same(rule, Engine.DueRule(target, [rule], Env, DateTime.UtcNow));
    }

    [Fact]
    public void DisabledRulesAndOtherActionsDontMatch()
    {
        var disabled = new Rule { Condition = RuleCondition.FrontApp, Apps = ["Teams.exe"], Enabled = false };
        var idle = new Rule { Condition = RuleCondition.FrontApp, Apps = ["Teams.exe"], Action = RuleAction.TreatAsNaturalBreak };
        Assert.Null(Engine.DueRule(new Break(), [disabled, idle], Env, DateTime.UtcNow));
        Assert.Same(idle, Engine.TreatAsIdle([disabled, idle], Env, DateTime.UtcNow));
    }

    [Fact]
    public void ScriptRulesUseTheLatestResult()
    {
        var scripts = new FakeScripts();
        var engine = new RulesEngine(new NoCalendar(), scripts);
        var rule = new Rule { Condition = RuleCondition.Script, ScriptFilename = "in-call.ps1" };
        Assert.Null(engine.DueRule(new Break(), [rule], Env, DateTime.UtcNow));
        scripts.True.Add("in-call.ps1");
        Assert.Same(rule, engine.DueRule(new Break(), [rule], Env, DateTime.UtcNow));
    }

    [Fact]
    public void Describe() => Assert.Equal(
        "Front app is Zoom → postpone 5 minutes",
        RulesEngine.Describe(new Rule { Apps = ["Zoom.exe"] }, app => Path.GetFileNameWithoutExtension(app)));
}

public class ActivityTests
{
    [Fact]
    public void StatisticsAddUpEachDay()
    {
        using var h = new Harness();
        var b = h.Normal;
        h.Activity.Log(b, BreakEventKind.Done, 600);
        h.Activity.Log(b, BreakEventKind.Postponed);
        h.Clock.UtcNow = h.Clock.UtcNow.AddDays(1);
        h.Activity.Log(b, BreakEventKind.Skipped);
        h.Activity.Log(h.Micro, BreakEventKind.EndedEarly, 10);

        var all = h.Activity.Stats(2, null);
        Assert.Equal([new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 2)], all.Select(s => s.Day));
        Assert.Equal((1, 1, 0, 600.0), (all[0].Done, all[0].Postponed, all[0].Skipped, all[0].Duration));
        Assert.Equal((1, 0, 1, 10.0), (all[1].Done, all[1].Postponed, all[1].Skipped, all[1].Duration));
        Assert.Equal(0, h.Activity.Stats(2, b.Id)[1].Done);
    }

    [Fact]
    public void DaysAreSavedAndReadBack()
    {
        using var h = new Harness();
        h.Activity.Log(h.Normal, BreakEventKind.Done, 600);
        h.Clock.UtcNow = h.Clock.UtcNow.AddDays(1);
        h.Activity.Save();
        Assert.Equal([new DateOnly(2026, 6, 2), new DateOnly(2026, 6, 1)], h.Activity.AvailableDays());
        Assert.Single(h.Activity.Record(new DateOnly(2026, 6, 1)).Events);
    }

    [Fact]
    public void NothingIsLoggedWhenTrackingIsOff()
    {
        using var h = new Harness();
        h.Model.Settings.TrackBreaks = false;
        h.Activity.Log(h.Normal, BreakEventKind.Done, 600);
        Assert.Empty(h.Activity.Record(LocalTime.Date(h.Clock.UtcNow, NewYork)).Events);
    }

    [Fact]
    public void AppTimeIsJoinedIntoSpans()
    {
        using var h = new Harness();
        h.Model.Settings.TrackApps = true;
        (string, string)? front = ("Code.exe", "Visual Studio Code");
        for (var i = 0; i < 5; i++)
        {
            h.Clock.Advance(1);
            h.Activity.Tick(h.Clock.UtcNow, idle: false, () => front);
        }
        front = ("msedge.exe", "Microsoft Edge");
        h.Clock.Advance(1);
        h.Activity.Tick(h.Clock.UtcNow, idle: false, () => front);
        var apps = h.Activity.Record(LocalTime.Date(h.Clock.UtcNow, NewYork)).Apps;
        Assert.Equal(["Code.exe", "msedge.exe"], apps.Select(a => a.AppId));
        Assert.Equal(5, apps[0].Duration);
    }
}
