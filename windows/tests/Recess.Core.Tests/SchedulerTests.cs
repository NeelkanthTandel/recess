using static Recess.Core.Tests.Zones;

namespace Recess.Core.Tests;

// The harness starts at 09:00 on Monday 1 June 2026 (New York) with the default Normal
// (10 minutes every hour) and Micro (15 seconds every 15 minutes) breaks.
public class SchedulerTests
{
    [Fact]
    public void StartGivesEveryBreakADueTime()
    {
        using var h = new Harness();
        h.Scheduler.Start();
        Assert.Equal("06/01 10:00", Hm(h.Normal.State.NextDue!.Value));
        Assert.Equal("06/01 09:15", Hm(h.Micro.State.NextDue!.Value));
    }

    [Fact]
    public void ABreakStartsWhenItIsDue()
    {
        using var h = new Harness();
        h.Scheduler.Start();
        var session = h.RunUntilBreak();
        Assert.Equal("Micro", session.Break.Name);
        Assert.Equal("06/01 09:15", Hm(h.Clock.UtcNow));
        Assert.Equal(1, h.Micro.State.Counts.TodayStart);
        Assert.Equal(h.Micro.Id, h.Model.ActiveBreakId);
    }

    [Fact]
    public void AFinishedBreakIsTimedStartToStart()
    {
        using var h = new Harness();
        h.Scheduler.Start();
        h.RunUntilBreak();
        h.Run(20);
        Assert.Null(h.Scheduler.Session);
        Assert.Equal("06/01 09:30", Hm(h.Micro.State.NextDue!.Value));
        Assert.Equal(1, h.Micro.State.Counts.TodayDone);
        Assert.Equal(15, h.Micro.State.LastDuration);
        Assert.Equal("completed", h.Micro.State.Status);
        var kinds = h.Activity.Record(LocalTime.Date(h.Clock.UtcNow, NewYork)).Events.Select(e => e.Kind);
        Assert.Equal([BreakEventKind.Started, BreakEventKind.Done], kinds);
    }

    [Fact]
    public void PostponingFromTheBreakScreen()
    {
        using var h = new Harness();
        h.Scheduler.Start();
        var session = h.RunUntilBreak();
        h.Run(2); // past the one-second fade-in, when the buttons work
        session.Postpone(1);
        Assert.Null(h.Scheduler.Session);
        Assert.Equal(h.Clock.UtcNow.AddMinutes(1), h.Micro.State.NextDue);
        Assert.Equal(1, h.Micro.State.Counts.BreakPostpone);
        Assert.Equal(1, h.Micro.State.Counts.TodayPostpone);
    }

    [Fact]
    public void ButtonsDoNothingDuringTheFadeInAndLockDelay()
    {
        var micro = Break.MakeMicro();
        micro.Controls.DisableButtonsSeconds = 5;
        using var h = new Harness(Break.MakeNormal(), micro);
        h.Scheduler.Start();
        var session = h.RunUntilBreak();
        h.Run(3);
        session.Skip();
        Assert.Equal(1, h.Actions.Beeps);
        Assert.NotNull(h.Scheduler.Session);
        h.Run(4);
        session.Skip();
        Assert.Null(h.Scheduler.Session);
        Assert.Equal(1, h.Micro.State.Counts.TodaySkip);
    }

    [Fact]
    public void PostponeLimitsApplyToTheMenuAndLinksToo()
    {
        var normal = Break.MakeNormal();
        normal.Controls.LimitPostpone = true;
        normal.Controls.PostponeLimit = 1;
        using var h = new Harness(normal);
        h.Scheduler.Start();
        h.Scheduler.Postpone(normal.Id, 5);
        var due = normal.State.NextDue;
        h.Scheduler.Postpone(normal.Id, 5);
        Assert.Equal(due, normal.State.NextDue);
        Assert.Equal(1, h.Actions.Beeps);
        Assert.Equal("postpone limit reached", normal.State.Status);
    }

    [Fact]
    public void PostponeLimitsAlsoBlockTheBreakScreenButton()
    {
        var micro = Break.MakeMicro();
        micro.Controls.LimitPostpone = true;
        micro.Controls.PostponeLimit = 0;
        using var h = new Harness(micro);
        h.Scheduler.Start();
        var session = h.RunUntilBreak();
        h.Run(2);
        Assert.False(session.CanPostpone);
        session.Postpone(1);
        Assert.Equal(1, h.Actions.Beeps);
        Assert.NotNull(h.Scheduler.Session);
    }

    [Fact]
    public void WaitForAPauseStartsOnceInputStops()
    {
        var micro = Break.MakeMicro();
        micro.Schedule.DueBehavior = DueBehavior.WaitForNaturalBreak;
        using var h = new Harness(micro);
        h.Scheduler.Start();
        h.Run(15 * 60 + 5);
        Assert.Null(h.Scheduler.Session);
        Assert.Equal("waiting for a pause in your work", micro.State.Status);
        Assert.StartsWith("waiting for a pause in your work (starts within", h.Scheduler.WaitingDescription(micro), StringComparison.Ordinal);
        h.System.Idle = 15;
        h.Run(1);
        Assert.NotNull(h.Scheduler.Session);
    }

    [Fact]
    public void WaitForAPauseGivesUpAfterAMinute()
    {
        var micro = Break.MakeMicro();
        micro.Schedule.DueBehavior = DueBehavior.WaitForNaturalBreak;
        using var h = new Harness(micro);
        h.Scheduler.Start();
        h.Run(15 * 60 + 58);
        Assert.Null(h.Scheduler.Session);
        h.Run(3);
        Assert.NotNull(h.Scheduler.Session);
    }

    [Fact]
    public void AskMeFirstWaitsForAnAnswer()
    {
        var micro = Break.MakeMicro();
        micro.Schedule.DueBehavior = DueBehavior.Ask;
        using var h = new Harness(micro);
        h.Scheduler.Start();
        h.Run(15 * 60 + 30);
        Assert.Equal(micro.Id, h.Ask.Showing);
        Assert.Null(h.Scheduler.Session);
        Assert.Equal("waiting for your answer", h.Scheduler.WaitingDescription(micro));

        h.Ask.Choose!(new AskChoice(AskChoiceKind.Postpone, 5));
        Assert.Null(h.Ask.Showing);
        Assert.Equal(h.Clock.UtcNow.AddMinutes(5), micro.State.NextDue);
    }

    [Fact]
    public void AskMeFirstCanStartTheBreak()
    {
        var micro = Break.MakeMicro();
        micro.Schedule.DueBehavior = DueBehavior.Ask;
        using var h = new Harness(micro);
        h.Scheduler.Start();
        h.Run(15 * 60 + 1);
        h.Ask.Choose!(new AskChoice(AskChoiceKind.Start));
        Assert.Equal("Micro", h.Scheduler.Session?.Break.Name);
    }

    [Fact]
    public void ABreakWaitsWhileAMouseButtonIsHeld()
    {
        using var h = new Harness(Break.MakeMicro());
        h.Scheduler.Start();
        h.System.MouseDown = true;
        h.Run(15 * 60 + 10);
        Assert.Null(h.Scheduler.Session);
        Assert.Equal("waiting while a mouse button is pressed", h.Micro.State.Status);
        h.System.MouseDown = false;
        h.Run(1);
        Assert.NotNull(h.Scheduler.Session);
    }

    [Fact]
    public void TimeAwayCountsAsABreak()
    {
        using var h = new Harness();
        h.Scheduler.Start();
        h.Run(30);
        // Two minutes without input: long enough for Micro (15 s), not for Normal (10 min).
        for (var i = 0; i < 120; i++)
        {
            h.Clock.Advance(1);
            h.System.Idle += 1;
            h.Scheduler.Cycle();
        }
        Assert.True(h.Scheduler.IsNaturalBreak);
        h.System.Idle = 0;
        h.Run(1);
        Assert.False(h.Scheduler.IsNaturalBreak);
        Assert.Equal(h.Clock.UtcNow.AddMinutes(15), h.Micro.State.NextDue);
        Assert.Equal(h.Clock.UtcNow, h.Micro.State.LastDone);
        Assert.Equal("06/01 10:00", Hm(h.Normal.State.NextDue!.Value));
        var events = h.Activity.Record(LocalTime.Date(h.Clock.UtcNow, NewYork)).Events;
        Assert.Equal("Micro", Assert.Single(events, e => e.Kind == BreakEventKind.NaturalBreak).BreakName);
    }

    [Fact]
    public void StopTheClockWhileAway()
    {
        var normal = Break.MakeNormal();
        normal.Schedule.IdleHandling = IdleHandling.PauseCountdown;
        using var h = new Harness(normal);
        h.Scheduler.Start();
        var due = normal.State.NextDue!.Value;
        for (var i = 0; i < 120; i++)
        {
            h.Clock.Advance(1);
            h.System.Idle += 1;
            h.Scheduler.Cycle();
        }
        // The clock stopped for each second spent officially away (from 60 s on).
        Assert.Equal(due.AddSeconds(61), normal.State.NextDue);
        Assert.Equal("away; clock stopped", normal.State.Status);
    }

    [Fact]
    public void WindTheClockBackWhileAway()
    {
        var normal = Break.MakeNormal();
        normal.Schedule.IdleHandling = IdleHandling.CountBackwards;
        using var h = new Harness(normal);
        h.Scheduler.Start();
        var due = normal.State.NextDue!.Value;
        void Away(int seconds)
        {
            for (var i = 0; i < seconds; i++)
            {
                h.Clock.Advance(1);
                h.System.Idle += 1;
                h.Scheduler.Cycle();
            }
        }
        // Two seconds back for each second officially away (from 60 s on)…
        Away(70);
        Assert.Equal(due.AddSeconds(2 * 11), normal.State.NextDue);
        // …but never further than one full interval from now.
        Away(50);
        Assert.Equal(h.Clock.UtcNow.AddSeconds(normal.Frequency), normal.State.NextDue);
    }

    [Fact]
    public void SleepLongerThanABreakCountsAsThatBreak()
    {
        using var h = new Harness();
        h.Scheduler.Start();
        h.Run(10);
        h.System.PendingSleep = 3600;
        h.Run(1);
        Assert.Equal(h.Clock.UtcNow.AddHours(1), h.Normal.State.NextDue);
        Assert.Equal("rested while the computer slept", h.Normal.State.Status);
    }

    [Fact]
    public void AGapBetweenCyclesIsSleepOnlyIfTheSystemWasIdle()
    {
        using var h = new Harness();
        h.Scheduler.Start();
        var microDue = h.Micro.State.NextDue;

        // Busy: the app was just slow, nobody slept.
        h.Clock.Advance(120);
        h.Scheduler.Cycle();
        Assert.Equal(microDue, h.Micro.State.NextDue);

        // Idle for the whole gap: the computer slept.
        h.Clock.Advance(120);
        h.System.Idle = 120;
        h.Scheduler.Cycle();
        Assert.Equal(h.Clock.UtcNow.AddMinutes(15), h.Micro.State.NextDue);
    }

    [Fact]
    public void ALowerBreakIsSkippedWhenAHigherOneIsDueSoon()
    {
        using var h = new Harness();
        h.Scheduler.Start();
        h.Normal.State.NextDue = Date(2026, 6, 1, 9, 17);
        h.Run(15 * 60);
        Assert.Equal("skipped for higher priority (Normal)", h.Micro.State.Status);
        // Skipped a second ahead of its 9:15 due time, as on the Mac, so it's next due at 9:29:59.
        Assert.Equal(Date(2026, 6, 1, 9, 14).AddSeconds(59).AddMinutes(15), h.Micro.State.NextDue);
        Assert.Null(h.Scheduler.Session);
    }

    [Fact]
    public void FinishingAHigherBreakResetsShorterLowerOnes()
    {
        using var h = new Harness();
        h.Scheduler.Start();
        h.Run(60);
        var started = h.Clock.UtcNow;
        h.Scheduler.StartBreak(h.Normal.Id, manual: true);
        h.Run(605);
        Assert.Null(h.Scheduler.Session);
        Assert.Equal("reset for higher priority (Normal)", h.Micro.State.Status);
        // Restarted when Normal ended, ten minutes after it began.
        Assert.Equal(started.AddMinutes(10).AddMinutes(15), h.Micro.State.NextDue);
    }

    [Fact]
    public void RulesCanPostponeADueBreak()
    {
        using var h = new Harness(Break.MakeMicro());
        h.Model.Settings.Rules.Add(new Rule { Condition = RuleCondition.FrontApp, Apps = ["zoom.exe"], Action = RuleAction.Postpone5 });
        h.System.Environment.FrontApp = "Zoom.exe";
        h.Scheduler.Start();
        h.Run(15 * 60);
        Assert.Null(h.Scheduler.Session);
        Assert.Equal(h.Clock.UtcNow.AddMinutes(5), h.Micro.State.NextDue);
        Assert.Equal("postponed 5 minutes by rule", h.Micro.State.Status);
    }

    [Fact]
    public void RulesCanSkipADueBreakDuringACall()
    {
        using var h = new Harness(Break.MakeMicro());
        h.Model.Settings.Rules.Add(new Rule { Condition = RuleCondition.MicOrCamera, Action = RuleAction.Skip });
        h.System.Environment.IsMicOrCameraInUse = true;
        h.Scheduler.Start();
        h.Run(15 * 60);
        Assert.Null(h.Scheduler.Session);
        Assert.Equal("skipped by rule", h.Micro.State.Status);
    }

    [Fact]
    public void ARuleCanCountTimeAsAway()
    {
        using var h = new Harness(Break.MakeMicro());
        h.Model.Settings.Rules.Add(new Rule { Condition = RuleCondition.Screensaver, Action = RuleAction.TreatAsNaturalBreak });
        h.Scheduler.Start();
        h.System.Environment.IsScreensaver = true;
        h.Run(20);
        Assert.True(h.Scheduler.IsNaturalBreak);
    }

    [Fact]
    public void TheHeadsUpNotificationComesOnceAMinuteAhead()
    {
        using var h = new Harness(Break.MakeNormal());
        h.Scheduler.Start();
        h.Run(58 * 60 + 59);
        Assert.Empty(h.Actions.Performed);
        h.Run(2);
        Assert.Single(h.Actions.Performed);
        h.Run(30);
        Assert.Single(h.Actions.Performed);
    }

    [Fact]
    public void NoHeadsUpForSomeoneWhoIsAway()
    {
        using var h = new Harness(Break.MakeNormal());
        h.Scheduler.Start();
        h.System.Idle = 3000;
        h.Run(59 * 60 + 10);
        Assert.Empty(h.Actions.Performed);
    }

    [Fact]
    public void APauseEndsByItself()
    {
        using var h = new Harness(Break.MakeNormal());
        h.Scheduler.Start();
        h.Scheduler.Pause(h.Normal.Id, 60);
        Assert.False(h.Normal.IsActive(h.Clock.UtcNow));
        h.Run(61);
        Assert.Null(h.Normal.State.PausedUntil);
        Assert.Equal("resumed", h.Normal.State.Status);
        Assert.True(h.Normal.IsActive(h.Clock.UtcNow));
    }

    [Fact]
    public void PausingUntilResumed()
    {
        using var h = new Harness();
        h.Scheduler.Start();
        h.Scheduler.PauseAll(null);
        Assert.True(h.Model.AllPaused(h.Clock.UtcNow));
        h.Run(3 * 3600);
        Assert.Null(h.Scheduler.Session);
        h.Scheduler.ResumeAll();
        Assert.False(h.Model.AllPaused(h.Clock.UtcNow));
        Assert.Equal(h.Clock.UtcNow.AddMinutes(15), h.Micro.State.NextDue);
    }

    [Fact]
    public void OverdueBreaksRestartAfterLaunch()
    {
        var normal = Break.MakeNormal();
        var stale = Break.MakeMicro();
        using var h = new Harness(normal, stale);
        normal.State.NextDue = h.Clock.UtcNow.AddHours(-1);
        stale.State.NextDue = h.Clock.UtcNow.AddHours(-13);
        h.Scheduler.Start();
        Assert.Equal(h.Clock.UtcNow.AddHours(1), normal.State.NextDue);
        Assert.Equal("restarted after Recess was closed", normal.State.Status);
        Assert.Equal(h.Clock.UtcNow.AddMinutes(15), stale.State.NextDue);
        Assert.Null(h.Scheduler.Session);
    }

    [Fact]
    public void StayUntilDismissedWaitsAtTheEnd()
    {
        var micro = Break.MakeMicro();
        micro.Controls.EndWhenDone = true;
        using var h = new Harness(micro);
        h.Scheduler.Start();
        var session = h.RunUntilBreak();
        h.Run(30);
        Assert.Equal(BreakSession.Phase.WaitingForEnd, session.CurrentPhase);
        Assert.Equal(1, h.Actions.Beeps);
        session.EndWhenDone();
        Assert.Null(h.Scheduler.Session);
        Assert.Equal(1, micro.State.Counts.TodayDone);
    }

    [Fact]
    public void APreviewDoesNotCount()
    {
        using var h = new Harness(Break.MakeMicro());
        h.Scheduler.Start();
        var due = h.Micro.State.NextDue;
        h.Scheduler.StartBreak(h.Micro.Id, manual: true, preview: true);
        Assert.True(h.Scheduler.Session!.IsPreview);
        Assert.False(h.Scheduler.Session.LocksAppSwitching);
        h.Run(20);
        Assert.Null(h.Scheduler.Session);
        Assert.Equal(0, h.Micro.State.Counts.TodayStart);
        Assert.Equal(0, h.Micro.State.Counts.TodayDone);
        Assert.Equal(due, h.Micro.State.NextDue);
    }

    [Fact]
    public void MakeDueStartsTheBreakSoon()
    {
        using var h = new Harness();
        h.Scheduler.Start();
        h.Scheduler.MakeDue(h.Normal.Id, 5);
        h.Run(6);
        Assert.Equal("Normal", h.Scheduler.Session?.Break.Name);
    }

    [Fact]
    public void ActionsRunAtEachStageOfABreak()
    {
        var micro = Break.MakeMicro();
        micro.Actions.Add(new BreakAction(ActionKind.Sound, ActionStage.AtStart));
        micro.Actions.Add(new BreakAction(ActionKind.FlashScreen, ActionStage.AfterFadeIn));
        micro.Actions.Add(new BreakAction(ActionKind.Speech, ActionStage.BeforeEnd, 5));
        micro.Actions.Add(new BreakAction(ActionKind.FadeSound, ActionStage.AtEnd));
        using var h = new Harness(micro);
        h.Scheduler.Start();
        h.RunUntilBreak();
        h.Run(20);
        Assert.Equal(
            [ActionKind.Sound, ActionKind.FlashScreen, ActionKind.Speech, ActionKind.FadeSound],
            h.Actions.Performed.Select(p => p.Action.Kind));
    }

    [Fact]
    public void ACalendarBreakLastsAsLongAsItsEvent()
    {
        var meeting = Break.MakeNormal();
        meeting.Name = "Meeting rest";
        meeting.Actions.Clear();
        meeting.Schedule.Adjustment = DueAdjustment.Calendar;
        using var h = new Harness(meeting);
        h.Calendar.Events.Add((Date(2026, 6, 1, 9, 30), Date(2026, 6, 1, 9, 45)));
        h.Scheduler.Start();
        Assert.Equal("06/01 09:30", Hm(meeting.State.NextDue!.Value));
        var session = h.RunUntilBreak();
        Assert.Equal(15 * 60, session.Duration, precision: 0);
        h.Run(15 * 60 + 5);
        Assert.Null(h.Scheduler.Session);
        Assert.Equal(LocalTime.DistantFuture, meeting.State.NextDue);
        Assert.Equal("no upcoming events", h.Model.DateString(meeting.State.NextDue, h.Clock.UtcNow, NewYork));
    }
}
