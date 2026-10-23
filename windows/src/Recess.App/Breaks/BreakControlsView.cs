using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Recess.Core;

namespace Recess.App.Breaks;

/// <summary>
/// The bottom of the main break screen: a countdown card (unless the theme shows its own) and
/// the Postpone, Skip and End buttons, following the session's rules for when each appears.
/// </summary>
internal sealed class BreakControlsView : StackPanel
{
    private readonly BreakSession session;
    private readonly bool compact;
    private readonly TextBlock? time;
    private readonly ProgressBar? progress;
    private readonly StackPanel buttons = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock info = new()
    {
        FontSize = 13,
        Foreground = new SolidColorBrush(Color.FromArgb(166, 255, 255, 255)),
        HorizontalAlignment = HorizontalAlignment.Center,
        Margin = new Thickness(0, 12, 0, 0),
    };

    private string lastButtons = "";

    /// <param name="compact">When the theme already shows the countdown, only the buttons appear.</param>
    public BreakControlsView(BreakSession session, bool compact)
    {
        this.session = session;
        this.compact = compact;
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Bottom;
        Margin = new Thickness(0, 0, 0, 60);

        if (!compact)
        {
            var card = new Border
            {
                CornerRadius = new CornerRadius(18),
                Background = new SolidColorBrush(Color.FromArgb(70, 30, 34, 44)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(28, 16, 28, 16),
                Margin = new Thickness(0, 0, 0, 14),
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = session.Break.Name,
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(191, 255, 255, 255)),
                HorizontalAlignment = HorizontalAlignment.Center,
            });
            time = new TextBlock
            {
                FontSize = 44,
                FontWeight = FontWeights.Light,
                FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI"),
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            stack.Children.Add(time);
            progress = new ProgressBar
            {
                Width = 320,
                Height = 4,
                Maximum = 1,
                Foreground = new SolidColorBrush(Tray.TrayGlyphs.ToColor(session.Break.Color)),
                Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                BorderThickness = new Thickness(0),
                Margin = new Thickness(0, 6, 0, 0),
            };
            stack.Children.Add(progress);
            card.Child = stack;
            Children.Add(card);
        }
        Children.Add(buttons);
        Children.Add(info);
        Update();
    }

    public void Update()
    {
        if (time is not null && progress is not null)
        {
            time.Text = session.CurrentPhase == BreakSession.Phase.WaitingForEnd ? "Break complete" : Format.Countdown(session.Remaining);
            progress.Value = session.Progress;
        }

        var c = session.Break.Controls;
        var wanted = new List<(string Title, Action Click, bool Prominent)>();
        if (session.CurrentPhase == BreakSession.Phase.WaitingForEnd)
        {
            wanted.Add(("End break", session.EndWhenDone, true));
        }
        if (c.ShowPostpone1 && session.CanPostpone)
        {
            wanted.Add((PostponeTitle(c.PostponeMinutes1), () => session.Postpone(1), false));
        }
        if (c.ShowPostpone2 && session.CanPostpone)
        {
            wanted.Add((PostponeTitle(c.PostponeMinutes2), () => session.Postpone(2), false));
        }
        if (c.ShowSkip && session.CanSkip)
        {
            wanted.Add(("Skip this break", session.Skip, false));
        }
        if (session.ShowEndEarly)
        {
            wanted.Add(("End break early", session.EndEarly, false));
        }
        if (session.IsPreview)
        {
            wanted.Add(("End preview", session.EndEarly, false));
        }

        // Rebuild the buttons only when the set changes, so keyboard focus isn't lost every tick.
        var key = string.Join('|', wanted.Select(w => w.Title));
        if (key != lastButtons)
        {
            lastButtons = key;
            buttons.Children.Clear();
            foreach (var (title, click, prominent) in wanted)
            {
                var button = new Button { Content = title, Style = (Style)Application.Current.FindResource("OverlayButton") };
                if (prominent)
                {
                    button.Background = new SolidColorBrush(Tray.TrayGlyphs.ToColor(session.Break.Color));
                }
                button.Click += (_, _) => click();
                buttons.Children.Add(button);
            }
        }
        buttons.IsEnabled = session.ButtonsEnabled;

        var lines = new List<string>();
        if (c.ShowCounters)
        {
            lines.Add(session.Break.CountersString);
        }
        if (!session.IsPreview && LimitText.Remaining(session.Break) is { } limits)
        {
            lines.Add(limits);
        }
        info.Text = string.Join("\n", lines);
        info.Visibility = lines.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>The first enabled button, to take keyboard focus.</summary>
    public Button? FirstButton => buttons.Children.OfType<Button>().FirstOrDefault();

    private static string PostponeTitle(double minutes)
    {
        var m = Format.Int(minutes);
        return $"Postpone {m} minute{(m == 1 ? "" : "s")}";
    }

    public bool IsCompact => compact;
}
