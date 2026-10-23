using static Recess.Core.Tests.Zones;

namespace Recess.Core.Tests;

public class StatusTextTests
{
    [Fact]
    public void TheIconCountsMinutesToTheNextBreak()
    {
        using var h = new Harness();
        h.Scheduler.Start();
        Assert.Equal("15", StatusText.IconText(h.Model, h.Scheduler));
        h.Run(14 * 60 + 1);
        Assert.Equal("<1", StatusText.IconText(h.Model, h.Scheduler));
    }

    [Fact]
    public void TheIconShowsHoursWhenTheNextBreakIsFarOff()
    {
        var b = Break.MakeNormal();
        b.Schedule.Frequency = Interval.FromHours(3);
        using var h = new Harness(b);
        h.Scheduler.Start();
        Assert.Equal("3h", StatusText.IconText(h.Model, h.Scheduler));
        h.Run(60); // whole hours only, so 2 h 59 m shows as 2h
        Assert.Equal("2h", StatusText.IconText(h.Model, h.Scheduler));
    }

    [Fact]
    public void TheIconCountsDownDuringABreak()
    {
        using var h = new Harness();
        h.Scheduler.Start();
        h.Scheduler.StartBreak(h.Normal.Id, manual: true);
        h.Run(30);
        Assert.Equal("10", StatusText.IconText(h.Model, h.Scheduler));
        Assert.StartsWith("Recess: Normal 9:3", StatusText.Tooltip(h.Model, h.Scheduler, NewYork), StringComparison.Ordinal);
    }

    [Fact]
    public void TitleFollowsTheChosenStyle()
    {
        using var h = new Harness();
        h.Scheduler.Start();
        Assert.Equal("15m", StatusText.Title(h.Model, h.Scheduler, NewYork));
        h.Model.Settings.StatusTitle = StatusTitle.FullCountdown;
        Assert.Equal("15:00", StatusText.Title(h.Model, h.Scheduler, NewYork));
        h.Model.Settings.StatusTitle = StatusTitle.DurationOfNext;
        Assert.Equal("15s", StatusText.Title(h.Model, h.Scheduler, NewYork));
        h.Model.Settings.StatusOnlyLong = true;
        Assert.Equal("10m", StatusText.Title(h.Model, h.Scheduler, NewYork));
    }

    [Fact]
    public void PausedAndWaitingAreSaidPlainly()
    {
        var micro = Break.MakeMicro();
        micro.Schedule.DueBehavior = DueBehavior.WaitForNaturalBreak;
        using var h = new Harness(micro);
        h.Scheduler.Start();
        h.Run(15 * 60 + 2);
        Assert.Equal("waiting", StatusText.Title(h.Model, h.Scheduler, NewYork));
        Assert.Equal("…", StatusText.IconText(h.Model, h.Scheduler));
        h.Scheduler.PauseAll(null);
        Assert.Equal("Paused", StatusText.Title(h.Model, h.Scheduler, NewYork));
        Assert.Equal("paused", StatusText.BreakStatus(micro, h.Model, h.Scheduler, NewYork));
    }

    [Fact]
    public void BreakStatusInTheMenu()
    {
        using var h = new Harness();
        h.Scheduler.Start();
        Assert.Equal("due in 15 minutes", StatusText.BreakStatus(h.Micro, h.Model, h.Scheduler, NewYork));
        h.Scheduler.SetEnabled(h.Micro.Id, false);
        Assert.Equal("disabled", StatusText.BreakStatus(h.Micro, h.Model, h.Scheduler, NewYork));
    }

    [Fact]
    public void LimitTextSaysWhatIsLeft()
    {
        var b = new Break();
        Assert.Null(LimitText.Remaining(b));
        b.Controls.LimitPostpone = true;
        b.Controls.PostponeLimit = 2;
        b.Controls.LimitSkip = true;
        b.Controls.SkipLimit = 1;
        b.Controls.SkipLimitKind = LimitKind.PerDay;
        Assert.Equal("2 postpones left this break · 1 skip left today", LimitText.Remaining(b));
    }
}
