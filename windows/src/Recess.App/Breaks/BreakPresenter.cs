using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using Recess.App.Platform;
using Recess.App.Tray;
using Recess.Core;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace Recess.App.Breaks;

/// <summary>
/// Puts a break on screen: a window on each display (or the small panel for the None theme),
/// fades in, keeps the countdown and buttons current, keeps keyboard focus, and fades out
/// when the session finishes.
/// </summary>
internal sealed class BreakPresenter : IBreakPresenter
{
    private readonly IClock clock;
    private readonly ThemeCatalog themes;
    private readonly string appVersion;

    private BreakSession? session;
    private readonly List<OverlayWindow> windows = [];
    private readonly List<DefaultThemeView> defaultViews = [];
    private BreakControlsView? controls;
    private CompactBreakPanel? panel;
    private DispatcherTimer? timer;
    private HWND previousForeground;
    private Recess.App.Breaks.Theme? theme;
    private string message = "";
    private int ticks;

    public BreakPresenter(IClock clock, ThemeCatalog themes)
    {
        this.clock = clock;
        this.themes = themes;
        appVersion = typeof(BreakPresenter).Assembly.GetName().Version?.ToString(3) ?? "";
    }

    /// <summary>Raised when a break appears and when it has gone (used to lock app switching).</summary>
    public event Action<BreakSession>? Shown;

    public event Action<BreakSession>? Hidden;

    public void Present(BreakSession s)
    {
        session = s;
        previousForeground = PInvoke.GetForegroundWindow();
        theme = themes.Find(s.Break.Appearance.ThemeId);
        var custom = s.Break.Appearance.Message;
        message = custom.Length > 0 ? BreakAction.Expand(custom, s.Break, clock.UtcNow, clock.Zone) : "";

        Build(fadeIn: true);
        s.Changed += Update;
        s.Finishing += OnFinishing;
        SystemEvents.DisplaySettingsChanged += OnDisplaysChanged;

        timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += (_, _) => Tick();
        timer.Start();
        Shown?.Invoke(s);
    }

    private void Build(bool fadeIn)
    {
        var s = session!;
        var fade = TimeSpan.FromSeconds(Math.Max(0.05, s.Break.Controls.FadeInSeconds));
        if (theme!.Id == ThemeIds.None)
        {
            panel = new CompactBreakPanel(s, message.Length > 0 ? message : "Look away from the screen and relax.");
            panel.Opacity = 0;
            panel.Show();
            panel.BeginAnimation(UIElement.OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(1, fade));
            return;
        }

        var appearance = s.Break.Appearance;
        var monitors = Monitors.All();
        // With no display attached (e.g. while switching monitors) the break just counts down.
        if (monitors.Count == 0)
        {
            return;
        }
        var primary = monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];
        var main = appearance.MainScreen == MainScreenChoice.Primary
            ? primary
            : monitors.FirstOrDefault(m => m.Handle == Monitors.UnderCursor()) ?? primary;
        var keepTaskbar = s.Settings.ShowMenuBarDuringBreak;
        var tip = message.Length > 0 ? message : Tips.Random();

        foreach (var monitor in monitors.OrderBy(m => m.Handle == main.Handle ? 0 : 1))
        {
            OverlayMode mode;
            if (monitor.Handle == main.Handle)
            {
                mode = OverlayMode.Main;
            }
            else if (appearance.OtherScreens == OtherScreensMode.None)
            {
                continue;
            }
            else
            {
                mode = appearance.OtherScreens == OtherScreensMode.Theme ? OverlayMode.Mirror : OverlayMode.Dim;
            }

            UIElement? content = null;
            var showsCountdown = false;
            if (mode != OverlayMode.Dim)
            {
                if (theme.Html is { } html && ThemeView.RuntimeAvailable)
                {
                    content = new ThemeView(html, Info);
                }
                else if (theme.Id != ThemeIds.Blank)
                {
                    // The Default theme, and the fallback when an HTML theme can't be shown.
                    var view = new DefaultThemeView(s.Break.Name, TrayGlyphs.ToColor(s.Break.Color), tip);
                    view.Update(Format.Countdown(s.Remaining), 1 - s.Progress);
                    defaultViews.Add(view);
                    content = view;
                    showsCountdown = true;
                }
            }
            UIElement? bar = null;
            if (mode == OverlayMode.Main)
            {
                controls = new BreakControlsView(s, compact: showsCountdown);
                bar = controls;
            }
            var dim = mode == OverlayMode.Dim ? appearance.OtherScreensColor : appearance.BackgroundColor;
            var window = new OverlayWindow(keepTaskbar ? monitor.WorkArea : monitor.Bounds, mode, TrayGlyphs.ToColor(dim), content, bar);
            windows.Add(window);
            window.Show();
            if (fadeIn)
            {
                window.Fade(1, fade);
            }
            else
            {
                window.Opacity = 1;
            }
        }
        KeepFocus();
    }

    private void Tick()
    {
        if (session is not { } s)
        {
            return;
        }
        s.Tick(clock.UtcNow);
        // Every two seconds, take keyboard focus back if something else grabbed it.
        if (++ticks % 8 == 0 && s.CurrentPhase != BreakSession.Phase.FadingOut)
        {
            KeepFocus();
        }
    }

    private void Update()
    {
        if (session is not { } s)
        {
            return;
        }
        foreach (var view in defaultViews)
        {
            view.Update(s.CurrentPhase == BreakSession.Phase.WaitingForEnd ? "Done" : Format.Countdown(s.Remaining), 1 - s.Progress);
        }
        controls?.Update();
        panel?.Update();
    }

    private void KeepFocus()
    {
        var main = windows.FirstOrDefault(w => w.Mode == OverlayMode.Main);
        if (main is not null && main.IsLoaded && !Foreground.IsForeground(main.Handle))
        {
            Foreground.Force(main.Handle);
        }
    }

    private void OnDisplaysChanged(object? sender, EventArgs e) => Application.Current.Dispatcher.BeginInvoke(() =>
    {
        // Displays came or went mid-break: start over on the new layout, without fading.
        if (session is null || session.IsFinished)
        {
            return;
        }
        CloseAll();
        Build(fadeIn: false);
    });

    private void OnFinishing(BreakResult result)
    {
        var s = session!;
        timer?.Stop();
        SystemEvents.DisplaySettingsChanged -= OnDisplaysChanged;
        var fade = TimeSpan.FromSeconds(Math.Max(0.05, s.Break.Controls.FadeOutSeconds));
        var pending = windows.Count + (panel is null ? 0 : 1);
        if (pending == 0)
        {
            Done();
            return;
        }
        void One()
        {
            if (--pending == 0)
            {
                Done();
            }
        }
        foreach (var w in windows)
        {
            w.Fade(0, fade, One);
        }
        if (panel is not null)
        {
            var animation = new System.Windows.Media.Animation.DoubleAnimation(0, fade);
            animation.Completed += (_, _) => One();
            panel.BeginAnimation(UIElement.OpacityProperty, animation);
        }
    }

    private void Done()
    {
        var s = session!;
        CloseAll();
        s.Changed -= Update;
        s.Finishing -= OnFinishing;
        session = null;
        // Give focus back to whatever was in front before the break.
        if (!previousForeground.IsNull && Processes.ProcessId(previousForeground) != (uint)Environment.ProcessId)
        {
            PInvoke.SetForegroundWindow(previousForeground);
        }
        Hidden?.Invoke(s);
        s.Complete();
    }

    private void CloseAll()
    {
        foreach (var w in windows)
        {
            w.CloseNow();
        }
        windows.Clear();
        defaultViews.Clear();
        controls = null;
        panel?.Close();
        panel = null;
    }

    // The details a local HTML theme receives, in the same shape as on the Mac.
    private object Info(string level)
    {
        var s = session;
        var b = s?.Break;
        return new
        {
            format = 1,
            level,
            appName = "Recess",
            appVersion,
            breakName = b?.Name ?? "",
            breakIdentifier = b?.Id.ToString().ToUpperInvariant() ?? "",
            breakDuration = s?.Duration ?? 0,
            breakRemaining = s?.Remaining ?? 0,
            breakColor = b?.Color.Hex ?? "#3A8CD9",
        };
    }
}
