using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Recess.Core;

namespace Recess.App.Breaks;

/// <summary>The "None" theme: instead of dimming the screen, a small panel in the corner with the countdown.</summary>
internal sealed class CompactBreakPanel : FloatingPanel
{
    private readonly BreakSession session;
    private readonly TextBlock time = new() { FontSize = 22, FontWeight = FontWeights.Medium };
    private readonly ProgressBar progress = new() { Height = 4, Maximum = 1, Margin = new Thickness(0, 8, 0, 8) };
    private readonly WrapPanel buttons = new();
    private string lastButtons = "";

    public CompactBreakPanel(BreakSession session, string message)
    {
        this.session = session;
        Title = $"{session.Break.Name} break";
        var color = new SolidColorBrush(Tray.TrayGlyphs.ToColor(session.Break.Color));
        progress.Foreground = color;

        var stack = new StackPanel { Width = 330 };
        var header = new DockPanel();
        var name = new StackPanel { Orientation = Orientation.Horizontal };
        name.Children.Add(new Ellipse { Width = 10, Height = 10, Fill = color, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
        name.Children.Add(new TextBlock { Text = session.Break.Name + " break", FontWeight = FontWeights.SemiBold, FontSize = 15, VerticalAlignment = VerticalAlignment.Center });
        DockPanel.SetDock(time, Dock.Right);
        header.Children.Add(time);
        header.Children.Add(name);
        stack.Children.Add(header);
        stack.Children.Add(progress);
        stack.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Opacity = 0.75, Margin = new Thickness(0, 0, 0, 10) });
        stack.Children.Add(buttons);
        Content = Card(stack);
        Loaded += (_, _) => PlaceInCorner(top: false);
        Update();
    }

    public void Update()
    {
        time.Text = session.CurrentPhase == BreakSession.Phase.WaitingForEnd ? "Done" : Format.Countdown(session.Remaining);
        progress.Value = session.Progress;

        var c = session.Break.Controls;
        var wanted = new List<(string, Action)>();
        if (session.CurrentPhase == BreakSession.Phase.WaitingForEnd)
        {
            wanted.Add(("End break", session.EndWhenDone));
        }
        if (c.ShowPostpone1 && session.CanPostpone)
        {
            wanted.Add(($"+{Format.Int(c.PostponeMinutes1)}m", () => session.Postpone(1)));
        }
        if (c.ShowPostpone2 && session.CanPostpone)
        {
            wanted.Add(($"+{Format.Int(c.PostponeMinutes2)}m", () => session.Postpone(2)));
        }
        if (c.ShowSkip && session.CanSkip)
        {
            wanted.Add(("Skip", session.Skip));
        }
        if (session.ShowEndEarly || session.IsPreview)
        {
            wanted.Add(("End", session.EndEarly));
        }
        var key = string.Join('|', wanted.Select(w => w.Item1));
        if (key != lastButtons)
        {
            lastButtons = key;
            buttons.Children.Clear();
            foreach (var (title, click) in wanted)
            {
                var button = new Button { Content = title, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(10, 3, 10, 3) };
                button.Click += (_, _) => click();
                buttons.Children.Add(button);
            }
        }
        buttons.IsEnabled = session.ButtonsEnabled;
    }
}
