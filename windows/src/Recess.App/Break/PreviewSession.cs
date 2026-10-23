using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Recess.App.Platform;
using Windows.Win32;

namespace Recess.App.Break;

/// <summary>
/// A break preview across every display. Milestone 1 uses it to prove the break screen works
/// on real hardware (layering, scaling, focus); milestone 3 replaces it with real break sessions.
/// </summary>
internal sealed class PreviewSession
{
    private static readonly Color DimMain = Color.FromArgb(224, 20, 26, 36);
    private static readonly Color DimOther = Color.FromArgb(191, 0, 0, 0);
    private static readonly Color BreakColor = Color.FromRgb(74, 144, 217);
    private static readonly TimeSpan Fade = TimeSpan.FromSeconds(2);
    private static PreviewSession? current;

    private readonly List<OverlayWindow> windows = [];
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Guid id = Guid.NewGuid();
    private readonly int length;
    private readonly IntPtr previousForeground;
    private DefaultThemeView? defaultTheme;
    private int remaining;

    private PreviewSession(int seconds)
    {
        length = seconds;
        remaining = seconds;
        previousForeground = PInvoke.GetForegroundWindow();
    }

    /// <param name="theme">An HTML theme, or null for the Default theme.</param>
    public static void Start(HtmlTheme? theme, int seconds = 20)
    {
        if (current is not null)
        {
            return;
        }
        current = new PreviewSession(seconds);
        current.Show(theme);
    }

    private void Show(HtmlTheme? theme)
    {
        var main = Monitors.UnderCursor();
        foreach (var monitor in Monitors.All())
        {
            OverlayWindow window;
            if (monitor.Handle == main)
            {
                UIElement view;
                if (theme is not null && ThemeView.RuntimeAvailable)
                {
                    view = new ThemeView(theme, Info);
                }
                else
                {
                    defaultTheme = new DefaultThemeView("Preview", BreakColor, "Look at something at least 20 feet away for 20 seconds.");
                    defaultTheme.Update(Countdown(remaining), 1);
                    view = defaultTheme;
                }
                window = new OverlayWindow(monitor, OverlayMode.Main, DimMain, view, Buttons());
            }
            else
            {
                window = new OverlayWindow(monitor, OverlayMode.Dim, DimOther, null, null);
            }
            windows.Add(window);
            window.Show();
            window.Fade(1, Fade);
        }
        timer.Tick += (_, _) => Tick();
        timer.Start();
        KeepFocus();
    }

    private UIElement Buttons()
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 60),
        };
        foreach (var title in new[] { "Postpone 1 Minute", "Skip This Break", "End Preview" })
        {
            var button = new Button { Content = title, Style = (Style)Application.Current.FindResource("OverlayButton") };
            button.Click += (_, _) => End();
            row.Children.Add(button);
        }
        return row;
    }

    private void Tick()
    {
        remaining -= 1;
        defaultTheme?.Update(Countdown(remaining), (double)remaining / length);
        if (remaining <= 0)
        {
            End();
            return;
        }
        KeepFocus();
    }

    // Typing during a break should land on the break screen, never in the app underneath.
    private void KeepFocus()
    {
        var main = windows.FirstOrDefault(w => w.Mode == OverlayMode.Main);
        if (main is not null && !Foreground.IsForeground(main.Handle))
        {
            Foreground.Force(main.Handle);
        }
    }

    private void End()
    {
        if (!timer.IsEnabled)
        {
            return;
        }
        timer.Stop();
        var left = windows.Count;
        foreach (var window in windows)
        {
            window.Fade(0, Fade, () =>
            {
                window.CloseNow();
                if (--left == 0)
                {
                    PInvoke.SetForegroundWindow(new Windows.Win32.Foundation.HWND(previousForeground));
                    current = null;
                }
            });
        }
    }

    // The details a local HTML theme receives, in the same shape as on the Mac.
    private object Info(string level) => new
    {
        format = 1,
        level,
        appName = "Recess",
        appVersion = typeof(PreviewSession).Assembly.GetName().Version?.ToString(3) ?? "",
        breakName = "Preview",
        breakIdentifier = id.ToString().ToUpperInvariant(),
        breakDuration = (double)length,
        breakRemaining = (double)remaining,
        breakColor = $"#{BreakColor.R:X2}{BreakColor.G:X2}{BreakColor.B:X2}",
    };

    private static string Countdown(int seconds) => $"{seconds / 60}:{seconds % 60:00}";
}
