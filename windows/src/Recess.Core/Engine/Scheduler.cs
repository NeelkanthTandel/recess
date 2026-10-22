namespace Recess.Core;

/// <summary>
/// Decides when breaks happen. Each one-second <see cref="Cycle"/> handles idle detection, time
/// away, sleep, rules, priorities and availability, then starts any break that's due. The
/// behaviour matches the Mac app's scheduler; docs/ARCHITECTURE.md describes the rules.
/// </summary>
public sealed class Scheduler
{
    /// <summary>"Wait for a pause in my work": start after this many seconds without input…</summary>
    public const double PauseLength = 15;

    /// <summary>…or after this long, whichever comes first.</summary>
    public const double LongestWait = 60;

    private readonly RecessModel model;
    private readonly IClock clock;
    private readonly ISystemMonitor system;
    private readonly IAskPresenter ask;
    private readonly IBreakPresenter presenter;
    private readonly IActionRunner actions;
    private readonly ICalendarSource calendar;
    private readonly RulesEngine rules;
    private readonly ActivityRecorder activity;
    private readonly Func<(string Id, string Name)?> frontApp;

    private DateTime lastCycle;

    // Time away
    private DateTime? idleStart;
    private readonly HashSet<Guid> naturalBreakIds = [];

    // Due breaks
    private readonly Dictionary<Guid, DateTime> dueSince = [];
    private Guid? askingBreakId;

    /// <summary>Which "ahead of the break" actions have run, and for which due date.</summary>
    private readonly Dictionary<Guid, DateTime> firedBeforeActions = [];

    /// <summary>When sleep was last handled, so a wake isn't counted twice.</summary>
    private DateTime lastSleepHandled = LocalTime.DistantPast;

    /// <summary>Idle time that built up during a break doesn't count as time away.</summary>
    private DateTime? lastSessionEnd;

    private DateTime lastCalendarRefresh = LocalTime.DistantPast;

    public Scheduler(
        RecessModel model, IClock clock, ISystemMonitor system, IAskPresenter ask, IBreakPresenter presenter, IActionRunner actions,
        ICalendarSource calendar, IScriptConditions scripts, ActivityRecorder activity, Func<(string Id, string Name)?>? frontApp = null)
    {
        this.model = model;
        this.clock = clock;
        this.system = system;
        this.ask = ask;
        this.presenter = presenter;
        this.actions = actions;
        this.calendar = calendar;
        this.activity = activity;
        this.frontApp = frontApp ?? (() => null);
        rules = new RulesEngine(calendar, scripts);
        lastCycle = clock.UtcNow;
        Now = lastCycle;
    }

    public BreakSession? Session { get; private set; }

    /// <summary>The time of the last cycle.</summary>
    public DateTime Now { get; private set; }

    /// <summary>Seconds without input, as of the last cycle.</summary>
    public double IdleSeconds { get; private set; }

    /// <summary>True while the user is away long enough for it to count as a break.</summary>
    public bool IsNaturalBreak { get; private set; }

    public RulesEngine Rules => rules;

    /// <summary>Gives every break a due time, then runs the first cycle. Call once at launch.</summary>
    public void Start()
    {
        var now = clock.UtcNow;
        lastCycle = now;
        foreach (var b in model.Breaks)
        {
            if (b.State.NextDue is not { } due || due < now.AddHours(-12))
            {
                Reschedule(b, now, DueDates.Reason.Reset);
            }
            else if (due < now && b.Schedule.Adjustment != DueAdjustment.Calendar)
            {
                // Overdue from before launch: a fresh start rather than a break the moment Recess opens.
                Reschedule(b, now, DueDates.Reason.Reset);
                b.State.Status = "restarted after Recess was closed";
            }
        }
        model.MarkChanged();
        Cycle();
    }

    // MARK: - Cycle

    public void Cycle()
    {
        var now = clock.UtcNow;
        var dt = (now - lastCycle).TotalSeconds;
        lastCycle = now;
        Now = now;

        var changed = false;
        foreach (var b in model.Breaks)
        {
            changed |= b.State.Counts.RollIfNeeded(now, clock.Zone);
        }
        if (changed)
        {
            model.MarkChanged();
        }
        ResumeExpiredPauses(now);

        // Time asleep counts as time away. The wake signal is the main source; a long gap
        // between cycles counts only if the system idle time agrees, so a busy app isn't
        // mistaken for sleep. Each wake is handled once.
        double sleepInterval = 0;
        if (system.TakeSleepInterval() is { } pending)
        {
            if ((now - lastSleepHandled).TotalSeconds > 120)
            {
                sleepInterval = pending;
            }
        }
        else if (dt > 30 && system.SystemIdleSeconds >= dt - 5)
        {
            sleepInterval = dt;
        }
        if (dt is > 30 or < 0)
        {
            dt = 1;
        }
        if (sleepInterval > 0)
        {
            lastSleepHandled = now;
            HandleSleep(sleepInterval, now);
        }

        if (Session is { } session)
        {
            session.Tick(now);
            activity.Tick(now, idle: true, frontApp);
            return;
        }

        var settings = model.Settings;
        var env = system.Snapshot();
        var idle = system.IdleSeconds(settings.IdleDetector);
        if (lastSessionEnd is { } end)
        {
            idle = Math.Min(idle, (now - end).TotalSeconds);
        }
        if (rules.IgnoreIdle(settings.Rules, env, now) is not null)
        {
            idle = 0;
        }
        else if (rules.TreatAsIdle(settings.Rules, env, now) is not null)
        {
            idle = Math.Max(idle, (now - (idleStart ?? now)).TotalSeconds + settings.NaturalBreakThreshold);
        }
        IdleSeconds = idle;

        var officiallyIdle = idle >= Math.Max(5, settings.NaturalBreakThreshold);
        RefreshCalendarBreaks(now);
        HandleNaturalBreak(officiallyIdle, idle, dt, now);
        activity.Tick(now, officiallyIdle, frontApp);

        // No point warning someone who isn't there.
        if (!officiallyIdle)
        {
            PerformBeforeDueActions(now);
        }
        AutoSkipForHigherPriority(now);

        if (!officiallyIdle)
        {
            StartDueBreak(idle, now, env);
        }
    }

    private void ResumeExpiredPauses(DateTime now)
    {
        foreach (var b in model.Breaks.Where(b => b.State.PausedUntil is { } until && until <= now).ToList())
        {
            b.State.PausedUntil = null;
            ResetBreak(b.Id, "resumed");
        }
    }

    private void HandleSleep(double interval, DateTime now)
    {
        foreach (var b in model.Breaks.Where(b => b.IsActive(now) && b.Schedule.Adjustment != DueAdjustment.Calendar))
        {
            var threshold = b.Schedule.ResetWhenIdleInterval?.TotalSeconds ?? b.Duration;
            if (b.Schedule.ResetWhenIdle && interval >= threshold)
            {
                Reschedule(b, now, DueDates.Reason.Reset);
                b.State.Status = "rested while the computer slept";
                b.State.Counts.BreakPostpone = 0;
                b.State.Counts.BreakSkip = 0;
                ClearDueState(b.Id);
            }
            else if (b.Schedule.IdleHandling == IdleHandling.PauseCountdown && b.State.NextDue is { } due)
            {
                b.State.NextDue = due.AddSeconds(interval);
            }
        }
        model.MarkChanged();
    }

    private void HandleNaturalBreak(bool officiallyIdle, double idle, double dt, DateTime now)
    {
        if (officiallyIdle)
        {
            idleStart ??= now.AddSeconds(-idle);
            IsNaturalBreak = true;
            var idleLength = (now - idleStart.Value).TotalSeconds;
            foreach (var b in model.Breaks.Where(b => b.IsActive(now) && b.Schedule.Adjustment != DueAdjustment.Calendar))
            {
                if (b.State.NextDue is not { } due)
                {
                    continue;
                }
                string status;
                switch (b.Schedule.IdleHandling)
                {
                    case IdleHandling.ContinueCountdown:
                        status = "away; still counting down";
                        break;
                    case IdleHandling.PauseCountdown:
                        due = due.AddSeconds(dt);
                        status = "away; clock stopped";
                        break;
                    default:
                        // Winding back never brings a break forward.
                        due = LocalTime.Max(due, LocalTime.Min(due.AddSeconds(2 * dt), now.AddSeconds(b.Frequency)));
                        status = "away; winding the clock back";
                        break;
                }
                var threshold = b.Schedule.ResetWhenIdleInterval?.TotalSeconds ?? b.Duration;
                if (b.Schedule.ResetWhenIdle && idleLength >= threshold)
                {
                    naturalBreakIds.Add(b.Id);
                    due = LocalTime.Max(due, now.AddSeconds(b.Frequency));
                    status = "rested while away";
                }
                // Never let a break start while the user is away.
                if (due < now)
                {
                    due = now;
                }
                if (b.State.NextDue != due || b.State.Status != status)
                {
                    b.State.NextDue = due;
                    b.State.Status = status;
                    model.MarkChanged();
                }
            }
        }
        else if (idleStart is { } start)
        {
            // Back from time away.
            idleStart = null;
            IsNaturalBreak = false;
            var length = (now - start).TotalSeconds;
            foreach (var id in naturalBreakIds)
            {
                if (model.Find(id) is not { } b)
                {
                    continue;
                }
                Reschedule(b, now, DueDates.Reason.Reset);
                b.State.LastDone = now;
                b.State.LastDuration = length;
                b.State.Counts.BreakPostpone = 0;
                b.State.Counts.BreakSkip = 0;
                b.State.Status = "rested while away";
                ClearDueState(id);
                activity.Log(b, BreakEventKind.NaturalBreak, length);
            }
            naturalBreakIds.Clear();
            model.MarkChanged();
        }
    }

    private void PerformBeforeDueActions(DateTime now)
    {
        foreach (var b in model.Breaks.Where(b => b.IsActive(now)))
        {
            if (b.State.NextDue is not { } due || due <= now)
            {
                continue;
            }
            foreach (var a in b.Actions.Where(a => a.Enabled && a.Stage == ActionStage.BeforeStart))
            {
                // Small shifts of the due time (e.g. a stopped clock) don't make it a new break.
                var alreadyFired = firedBeforeActions.TryGetValue(a.Id, out var firedFor) && Math.Abs((firedFor - due).TotalSeconds) < 30;
                if (now >= due.AddSeconds(-a.Offset) && !alreadyFired)
                {
                    firedBeforeActions[a.Id] = due;
                    actions.Perform(a, b);
                }
            }
        }
    }

    /// <summary>Skips a break when a more important one is due soon.</summary>
    private void AutoSkipForHigherPriority(DateTime now)
    {
        var breaks = model.Breaks.ToList();
        for (var index = 0; index < breaks.Count; index++)
        {
            var b = breaks[index];
            if (!b.IsActive(now) || !b.Schedule.SkipForHigherPriority || b.State.NextDue is not { } due || due > now.AddSeconds(1))
            {
                continue;
            }
            var window = b.Schedule.SkipForHigherPriorityInterval.TotalSeconds;
            var higher = breaks.Take(index).FirstOrDefault(h =>
                h.IsActive(now) && h.State.NextDue is { } hDue && (hDue - now).TotalSeconds <= window);
            if (higher is not null)
            {
                Reschedule(b, now, DueDates.Reason.Skip);
                b.State.Status = $"skipped for higher priority ({higher.Name})";
                ClearDueState(b.Id);
                model.MarkChanged();
            }
        }
    }

    private void StartDueBreak(double idle, DateTime now, IRuleEnvironment env)
    {
        var due = model.Breaks.Where(b => b.IsActive(now) && (b.State.NextDue ?? LocalTime.DistantFuture) <= now).ToList();
        // Forget "due since" times for breaks that are no longer due.
        foreach (var id in dueSince.Keys.Where(id => due.All(b => b.Id != id)).ToList())
        {
            dueSince.Remove(id);
        }
        if (due.Count == 0)
        {
            return;
        }
        var b = due[0];

        // A calendar break whose event is already over (e.g. the computer was asleep) moves on.
        if (b.Schedule.Adjustment == DueAdjustment.Calendar && b.State.NextEventEnd is { } end && end <= now.AddSeconds(5))
        {
            Reschedule(b, now, DueDates.Reason.Skip);
            b.State.Status = "missed a calendar break that has ended";
            model.MarkChanged();
            return;
        }

        if (askingBreakId is { } asking)
        {
            if (ask.IsShowing(asking))
            {
                return;
            }
            askingBreakId = null;
        }

        if (rules.DueRule(b, model.Settings.Rules, env, now) is { } rule)
        {
            if (rule.Action.PostponeMinutes() is { } minutes)
            {
                b.State.NextDue = now.AddMinutes(minutes);
                b.State.Status = $"postponed {Format.Int(minutes)} minutes by rule";
            }
            else
            {
                Reschedule(b, now, DueDates.Reason.Skip);
                b.State.Status = "skipped by rule";
                activity.Log(b, BreakEventKind.Skipped, detail: "rule");
            }
            dueSince.Remove(b.Id);
            model.MarkChanged();
            return;
        }

        if (system.IsMouseButtonDown)
        {
            SetStatus(b, "waiting while a mouse button is pressed");
            return;
        }

        if (!dueSince.TryGetValue(b.Id, out var since))
        {
            since = now;
            dueSince[b.Id] = since;
        }

        switch (b.Schedule.DueBehavior)
        {
            case DueBehavior.StartImmediately:
                StartBreak(b.Id, manual: false);
                break;
            case DueBehavior.WaitForNaturalBreak:
                if (idle >= PauseLength || (now - since).TotalSeconds >= LongestWait)
                {
                    StartBreak(b.Id, manual: false);
                }
                else
                {
                    SetStatus(b, "waiting for a pause in your work");
                }
                break;
            default:
                askingBreakId = b.Id;
                SetStatus(b, "asking when to start");
                var id = b.Id;
                ask.Show(b, choice =>
                {
                    askingBreakId = null;
                    dueSince.Remove(id);
                    switch (choice.Kind)
                    {
                        case AskChoiceKind.Start:
                            StartBreak(id, manual: false);
                            break;
                        case AskChoiceKind.Postpone:
                            Postpone(id, choice.Minutes);
                            break;
                        default:
                            Skip(id);
                            break;
                    }
                });
                break;
        }
    }

    private void SetStatus(Break b, string status)
    {
        if (b.State.Status != status)
        {
            b.State.Status = status;
            model.MarkChanged();
        }
    }

    /// <summary>What a due break is waiting for before it starts, or null if it isn't waiting. Shown so a late break doesn't look stuck.</summary>
    public string? WaitingDescription(Break b)
    {
        var now = Now;
        if (!b.IsActive(now) || Session?.Break.Id == b.Id || b.State.NextDue is not { } due || due > now)
        {
            return null;
        }
        if (askingBreakId == b.Id)
        {
            return "waiting for your answer";
        }
        if (b.Schedule.DueBehavior == DueBehavior.WaitForNaturalBreak && dueSince.TryGetValue(b.Id, out var since))
        {
            var left = Math.Max(1, LongestWait - (now - since).TotalSeconds);
            return $"waiting for a pause in your work (starts within {Format.Brief(left)})";
        }
        return b.State.Status;
    }

    // MARK: - Commands

    public void StartBreak(Guid id, bool manual, bool preview = false)
    {
        if (Session is not null || model.Find(id) is not { } b)
        {
            return;
        }
        ask.Dismiss();
        askingBreakId = null;
        dueSince.Remove(id);
        var now = clock.UtcNow;
        if (!preview)
        {
            b.State.LastStarted = now;
            b.State.Counts.TodayStart += 1;
            b.State.Counts.TotalStart += 1;
            b.State.Status = "having the break";
            activity.Log(b, BreakEventKind.Started);
        }
        model.ActiveBreakId = id;
        var session = new BreakSession(b.Clone(), preview, model.Settings, clock, actions, result => SessionEnded(id, result, preview));
        Session = session;
        model.MarkChanged();
        session.Begin();
        presenter.Present(session);
    }

    private void SessionEnded(Guid id, BreakResult result, bool preview)
    {
        Session = null;
        model.ActiveBreakId = null;
        var now = clock.UtcNow;
        lastCycle = now;
        lastSessionEnd = now;
        idleStart = null;
        IsNaturalBreak = false;
        naturalBreakIds.Clear();
        model.MarkChanged();
        if (preview || model.Find(id) is not { } b)
        {
            return;
        }
        switch (result)
        {
            case BreakResult.Done or BreakResult.EndedEarly:
                var elapsed = result is BreakResult.Done d ? d.Elapsed : ((BreakResult.EndedEarly)result).Elapsed;
                var early = result is BreakResult.EndedEarly;
                b.State.LastDone = now;
                b.State.LastDuration = elapsed;
                b.State.Counts.TodayDone += 1;
                b.State.Counts.TotalDone += 1;
                b.State.Counts.TodayDuration += elapsed;
                b.State.Counts.TotalDuration += elapsed;
                b.State.Counts.BreakPostpone = 0;
                b.State.Counts.BreakSkip = 0;
                Reschedule(b, now, DueDates.Reason.Done);
                b.State.Status = early ? "ended early" : "completed";
                activity.Log(b, early ? BreakEventKind.EndedEarly : BreakEventKind.Done, elapsed);
                ResetLowerPriorityAfterFinishing(id);
                break;
            case BreakResult.Postponed p:
                Postpone(id, p.Minutes, fromBreak: true);
                break;
            case BreakResult.Skipped:
                Skip(id, fromBreak: true);
                break;
        }
    }

    /// <summary>Lower-priority breaks restart their timers after a more important break finishes.</summary>
    private void ResetLowerPriorityAfterFinishing(Guid id)
    {
        var index = model.IndexOf(id);
        if (index < 0)
        {
            return;
        }
        var finished = model.Breaks[index];
        var now = clock.UtcNow;
        foreach (var b in model.Breaks.Skip(index + 1))
        {
            if (!b.IsActive(now) || !b.Schedule.ResetWhenFinishingHigherPriority || b.Duration > finished.Duration)
            {
                continue;
            }
            Reschedule(b, now, DueDates.Reason.Reset);
            b.State.Status = $"reset for higher priority ({finished.Name})";
            ClearDueState(b.Id);
        }
        model.MarkChanged();
    }

    public void StartNextBreak()
    {
        if (model.NextBreakAnyLength(clock.UtcNow) is { } b)
        {
            StartBreak(b.Id, manual: true);
        }
    }

    public void Postpone(Guid id, double minutes, bool fromBreak = false)
    {
        if (model.Find(id) is not { } b)
        {
            return;
        }
        if (!fromBreak && Session?.Break.Id == id)
        {
            Session.Postpone(minutes);
            return;
        }
        if (!fromBreak && !b.CanPostpone)
        {
            LimitReached(b, "postpone");
            return;
        }
        ask.Dismiss(id);
        var now = clock.UtcNow;
        b.State.NextDue = now.AddMinutes(minutes);
        b.State.LastDeferred = now;
        b.State.Counts.BreakPostpone += 1;
        b.State.Counts.HourPostpone += 1;
        b.State.Counts.TodayPostpone += 1;
        b.State.Counts.TotalPostpone += 1;
        b.State.Status = $"postponed {Format.Int(minutes)} minute{(minutes == 1 ? "" : "s")}";
        dueSince.Remove(id);
        model.MarkChanged();
        activity.Log(b, BreakEventKind.Postponed, detail: $"{Format.Int(minutes)} min");
        PerformActions(b, ActionStage.AfterPostponeOrSkip);
    }

    public void PostponeNext(int slot)
    {
        if (Session is { } s)
        {
            s.Postpone(slot);
            return;
        }
        if (model.NextBreakAnyLength(clock.UtcNow) is { } b)
        {
            Postpone(b.Id, slot == 1 ? b.Controls.PostponeMinutes1 : b.Controls.PostponeMinutes2);
        }
    }

    /// <summary>Postpones the break in progress, or else the next one, by a number of minutes.</summary>
    public void PostponeCurrentOrNext(double minutes)
    {
        if (Session is { } s)
        {
            s.Postpone(minutes);
            return;
        }
        if (model.NextBreakAnyLength(clock.UtcNow) is { } b)
        {
            Postpone(b.Id, minutes);
        }
    }

    public void Skip(Guid id, bool fromBreak = false)
    {
        if (model.Find(id) is not { } b)
        {
            return;
        }
        if (!fromBreak && Session?.Break.Id == id)
        {
            Session.Skip();
            return;
        }
        if (!fromBreak && !b.CanSkip)
        {
            LimitReached(b, "skip");
            return;
        }
        ask.Dismiss(id);
        var now = clock.UtcNow;
        Reschedule(b, now, DueDates.Reason.Skip);
        b.State.LastDeferred = now;
        b.State.Counts.BreakSkip += 1;
        b.State.Counts.HourSkip += 1;
        b.State.Counts.TodaySkip += 1;
        b.State.Counts.TotalSkip += 1;
        b.State.Status = "skipped";
        dueSince.Remove(id);
        model.MarkChanged();
        activity.Log(b, BreakEventKind.Skipped);
        PerformActions(b, ActionStage.AfterPostponeOrSkip);
    }

    public void SkipNext()
    {
        if (Session is { } s)
        {
            s.Skip();
            return;
        }
        if (model.NextBreakAnyLength(clock.UtcNow) is { } b)
        {
            Skip(b.Id);
        }
    }

    /// <summary>Forgets waiting and asking state for a break whose due time just changed.</summary>
    private void ClearDueState(Guid id)
    {
        dueSince.Remove(id);
        if (askingBreakId == id)
        {
            ask.Dismiss(id);
            askingBreakId = null;
        }
    }

    /// <summary>Feedback when a postpone or skip limit blocks a request from the menu, a shortcut or a link.</summary>
    private void LimitReached(Break b, string what)
    {
        actions.Beep();
        SetStatus(b, $"{what} limit reached");
    }

    public void MakeDue(Guid id, double seconds)
    {
        model.Update(id, b =>
        {
            b.State.NextDue = clock.UtcNow.AddSeconds(seconds);
            b.State.Status = "made due";
        });
        ClearDueState(id);
    }

    public void ResetBreak(Guid id, string reason = "reset")
    {
        model.Update(id, b =>
        {
            Reschedule(b, clock.UtcNow, DueDates.Reason.Reset);
            b.State.Counts.BreakPostpone = 0;
            b.State.Counts.BreakSkip = 0;
            b.State.Status = reason;
        });
        ClearDueState(id);
    }

    public void ResetAll()
    {
        foreach (var b in model.Breaks.ToList())
        {
            ResetBreak(b.Id);
        }
    }

    /// <summary>Pauses a break; null seconds = until resumed.</summary>
    public void Pause(Guid id, double? seconds)
    {
        var now = clock.UtcNow;
        model.Update(id, b =>
        {
            b.State.PausedUntil = seconds is { } s ? now.AddSeconds(s) : LocalTime.DistantFuture;
            b.State.Status = seconds is { } secs ? $"paused until {Format.Time(now.AddSeconds(secs), clock.Zone)}" : "paused until resumed";
        });
        ClearDueState(id);
    }

    public void PauseAll(double? seconds)
    {
        foreach (var b in model.Breaks.ToList())
        {
            Pause(b.Id, seconds);
        }
    }

    public void Resume(Guid id)
    {
        model.Update(id, b => b.State.PausedUntil = null);
        ResetBreak(id, "resumed");
    }

    public void ResumeAll()
    {
        foreach (var b in model.Breaks.Where(b => b.State.PausedUntil is not null).ToList())
        {
            Resume(b.Id);
        }
    }

    public void SetEnabled(Guid id, bool enabled)
    {
        model.Update(id, b => b.Enabled = enabled);
        if (enabled)
        {
            ResetBreak(id);
        }
    }

    public Guid AddBreak()
    {
        var b = Break.MakeNormal();
        b.Name = $"Break {model.Breaks.Count + 1}";
        b.Actions.Clear();
        Reschedule(b, clock.UtcNow, DueDates.Reason.Reset);
        model.Breaks.Add(b);
        model.MarkChanged();
        return b.Id;
    }

    public Guid? DuplicateBreak(Guid id)
    {
        var index = model.IndexOf(id);
        if (index < 0)
        {
            return null;
        }
        var copy = model.Breaks[index].Duplicate();
        Reschedule(copy, clock.UtcNow, DueDates.Reason.Reset);
        model.Breaks.Insert(index + 1, copy);
        model.MarkChanged();
        return copy.Id;
    }

    public void DeleteBreak(Guid id)
    {
        if (Session?.Break.Id == id)
        {
            Session.Finish(new BreakResult.Skipped());
        }
        model.Breaks.RemoveAll(b => b.Id == id);
        model.MarkChanged();
    }

    /// <summary>
    /// Re-times a break after its schedule was edited. Postpone and skip counts are left alone,
    /// so editing settings can't be used to get around the limits.
    /// </summary>
    public void ScheduleChanged(Guid id)
    {
        model.Update(id, b =>
        {
            Reschedule(b, clock.UtcNow, DueDates.Reason.Reset);
            b.State.Status = "schedule edited";
        });
        ClearDueState(id);
    }

    public void PerformActions(Break b, ActionStage stage)
    {
        foreach (var a in b.Actions.Where(a => a.Enabled && a.Stage == stage))
        {
            actions.Perform(a, b);
        }
    }

    // MARK: - Due dates

    public void Reschedule(Break b, DateTime now, DueDates.Reason reason)
    {
        if (b.Schedule.Adjustment == DueAdjustment.Calendar)
        {
            var s = b.Schedule;
            var next = calendar.NextEvent(s.CalendarIdentifier, s.CalendarEventFilter, now.AddSeconds(1));
            b.State.NextEventStart = next?.Start;
            b.State.NextEventEnd = next?.End;
            b.State.NextDue = next?.Start ?? LocalTime.DistantFuture;
        }
        else
        {
            b.State.NextDue = DueDates.Next(b, now, reason, clock.Zone);
        }
    }

    /// <summary>Picks up new or moved calendar events every couple of minutes.</summary>
    private void RefreshCalendarBreaks(DateTime now)
    {
        if ((now - lastCalendarRefresh).TotalSeconds <= 120)
        {
            return;
        }
        lastCalendarRefresh = now;
        foreach (var b in model.Breaks.Where(b => b.Schedule.Adjustment == DueAdjustment.Calendar && b.IsActive(now)))
        {
            var due = b.State.NextDue ?? LocalTime.DistantFuture;
            if (due > now.AddSeconds(5))
            {
                Reschedule(b, now, DueDates.Reason.Reset);
                model.MarkChanged();
            }
        }
    }
}
