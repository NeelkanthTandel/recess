namespace Recess.Core;

/// <summary>How a break ended.</summary>
public abstract record BreakResult
{
    private BreakResult()
    {
    }

    public sealed record Done(double Elapsed) : BreakResult;

    public sealed record EndedEarly(double Elapsed) : BreakResult;

    public sealed record Postponed(double Minutes) : BreakResult;

    public sealed record Skipped : BreakResult;
}

/// <summary>
/// One break in progress: countdown, phases, stage actions, and the buttons' rules. The screen
/// side (windows, fades, focus) belongs to the app, which watches this and calls
/// <see cref="Complete"/> once it has faded out.
/// </summary>
public sealed class BreakSession
{
    public enum Phase
    {
        FadingIn,
        Running,
        WaitingForEnd,
        FadingOut,
    }

    private readonly IClock clock;
    private readonly IActionRunner actions;
    private readonly Action<BreakResult> completion;
    private readonly DateTime startDate;
    private readonly HashSet<Guid> firedBeforeEnd = [];
    private bool firedAfterFadeIn;
    private BreakResult? result;
    private bool completed;

    public BreakSession(Break b, bool isPreview, AppSettings settings, IClock clock, IActionRunner actions, Action<BreakResult> completion)
    {
        Break = b;
        IsPreview = isPreview;
        Settings = settings;
        this.clock = clock;
        this.actions = actions;
        this.completion = completion;
        startDate = clock.UtcNow;
        // A calendar break lasts until its event ends, even if it started late.
        if (b.Schedule.Adjustment == DueAdjustment.Calendar && b.State.NextEventEnd is { } end && !isPreview)
        {
            Duration = Math.Max(5, Math.Min(b.Duration, (end - startDate).TotalSeconds));
        }
        else
        {
            Duration = b.Duration;
        }
    }

    /// <summary>A snapshot of the break as it was when the session started.</summary>
    public Break Break { get; }

    public bool IsPreview { get; }

    public AppSettings Settings { get; }

    public double Duration { get; }

    public double Elapsed { get; private set; }

    public Phase CurrentPhase { get; private set; } = Phase.FadingIn;

    public double Remaining => Math.Max(0, Duration - Elapsed);

    public double Progress => Duration > 0 ? Math.Min(1, Elapsed / Duration) : 1;

    public bool IsFinished => result is not null;

    /// <summary>The buttons stay disabled for the fade-in plus the break's "disable buttons" delay.</summary>
    public bool ButtonsEnabled => IsPreview || Elapsed >= Break.Controls.DisableButtonsSeconds + Break.Controls.FadeInSeconds;

    public bool ShowEndEarly =>
        Break.Controls.ShowEndEarly && Elapsed >= Break.Controls.EndEarlyAfter.TotalSeconds && CurrentPhase == Phase.Running;

    public bool CanPostpone => IsPreview || Break.CanPostpone;

    public bool CanSkip => IsPreview || Break.CanSkip;

    /// <summary>Whether to block Alt+Tab and the Windows key: only for a real break that dims the screen.</summary>
    public bool LocksAppSwitching => Settings.DisableAppSwitching && !IsPreview && Break.Appearance.ThemeId != ThemeIds.None;

    /// <summary>Raised whenever the countdown or phase changes.</summary>
    public event Action? Changed;

    /// <summary>Raised once when the break ends; the app fades out, then calls <see cref="Complete"/>.</summary>
    public event Action<BreakResult>? Finishing;

    public void Begin()
    {
        if (!IsPreview)
        {
            Perform(ActionStage.AtStart);
        }
    }

    public void Tick(DateTime now)
    {
        if (IsFinished)
        {
            return;
        }
        Elapsed = Math.Max(0, (now - startDate).TotalSeconds);
        var c = Break.Controls;
        if (CurrentPhase == Phase.FadingIn && Elapsed >= c.FadeInSeconds)
        {
            CurrentPhase = Phase.Running;
        }
        if (!firedAfterFadeIn && Elapsed >= c.FadeInSeconds)
        {
            firedAfterFadeIn = true;
            if (!IsPreview)
            {
                Perform(ActionStage.AfterFadeIn);
            }
        }
        if (!IsPreview)
        {
            foreach (var a in Break.Actions.Where(a => a.Enabled && a.Stage == ActionStage.BeforeEnd && !firedBeforeEnd.Contains(a.Id)))
            {
                if (Remaining <= a.Offset)
                {
                    firedBeforeEnd.Add(a.Id);
                    actions.Perform(a, Break);
                }
            }
        }
        if (Elapsed >= Duration && CurrentPhase is not Phase.WaitingForEnd and not Phase.FadingOut)
        {
            if (c.EndWhenDone && !IsPreview)
            {
                CurrentPhase = Phase.WaitingForEnd;
                actions.Beep();
            }
            else
            {
                Finish(new BreakResult.Done(Duration));
                return;
            }
        }
        // Runaway guard: a break left waiting for hours ends by itself.
        if (Elapsed > Duration + 4 * 3600)
        {
            Finish(new BreakResult.Done(Duration));
            return;
        }
        Changed?.Invoke();
    }

    public void Finish(BreakResult how)
    {
        if (IsFinished)
        {
            return;
        }
        result = how;
        CurrentPhase = Phase.FadingOut;
        if (how is BreakResult.Done or BreakResult.EndedEarly && !IsPreview)
        {
            Perform(ActionStage.AtEnd);
        }
        Changed?.Invoke();
        Finishing?.Invoke(how);
    }

    /// <summary>Called by the app once the break screen has faded out.</summary>
    public void Complete()
    {
        if (result is null || completed)
        {
            return;
        }
        completed = true;
        completion(result);
    }

    public void Postpone(int slot) =>
        Postpone(slot == 1 ? Break.Controls.PostponeMinutes1 : Break.Controls.PostponeMinutes2);

    /// <summary>
    /// Postpone and skip honour the break's limits and the button delay, whether they come
    /// from the break screen, the tray menu, a shortcut or a link.
    /// </summary>
    public void Postpone(double minutes)
    {
        if (!CanPostpone || !ButtonsEnabled)
        {
            actions.Beep();
            return;
        }
        Finish(new BreakResult.Postponed(minutes));
    }

    public void Skip()
    {
        if (!CanSkip || !ButtonsEnabled)
        {
            actions.Beep();
            return;
        }
        Finish(new BreakResult.Skipped());
    }

    public void EndEarly() => Finish(new BreakResult.EndedEarly(Elapsed));

    public void EndWhenDone() => Finish(new BreakResult.Done(Elapsed));

    private void Perform(ActionStage stage)
    {
        foreach (var a in Break.Actions.Where(a => a.Enabled && a.Stage == stage))
        {
            actions.Perform(a, Break);
        }
    }
}
