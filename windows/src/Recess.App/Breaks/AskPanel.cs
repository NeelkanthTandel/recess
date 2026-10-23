using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Recess.Core;

namespace Recess.App.Breaks;

/// <summary>"Ask me first": a small panel offering to start the break, which starts it on its own after a while.</summary>
internal sealed class AskPanel : IAskPresenter
{
    private readonly IClock clock;
    private Window? window;
    private Guid? breakId;
    private Action<AskChoice>? handler;
    private DispatcherTimer? timer;

    public AskPanel(IClock clock)
    {
        this.clock = clock;
    }

    public bool IsShowing(Guid id) => breakId == id && window is not null;

    public void Show(Recess.Core.Break b, Action<AskChoice> choose)
    {
        Dismiss();
        breakId = b.Id;
        handler = choose;
        var deadline = clock.UtcNow.AddSeconds(b.Schedule.AskAutoStart.TotalSeconds);
        var panel = new Panel(b, Choose);
        window = panel;
        timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) =>
        {
            var left = (deadline - clock.UtcNow).TotalSeconds;
            if (left <= 0)
            {
                Choose(new AskChoice(AskChoiceKind.Start));
            }
            else
            {
                panel.SetCountdown(left);
            }
        };
        panel.SetCountdown((deadline - clock.UtcNow).TotalSeconds);
        panel.Show();
        timer.Start();
    }

    private void Choose(AskChoice choice)
    {
        var h = handler;
        Dismiss();
        h?.Invoke(choice);
    }

    public void Dismiss()
    {
        timer?.Stop();
        timer = null;
        window?.Close();
        window = null;
        breakId = null;
        handler = null;
    }

    public void Dismiss(Guid ifShowing)
    {
        if (breakId == ifShowing)
        {
            Dismiss();
        }
    }

    private sealed class Panel : FloatingPanel
    {
        private readonly TextBlock countdown = new() { Opacity = 0.75, TextWrapping = TextWrapping.Wrap, MaxWidth = 400, Margin = new Thickness(0, 6, 0, 12) };

        public Panel(Recess.Core.Break b, Action<AskChoice> choose)
        {
            Title = $"{b.Name} break is due";
            var stack = new StackPanel { Width = 420 };
            var header = new StackPanel { Orientation = Orientation.Horizontal };
            header.Children.Add(new Ellipse
            {
                Width = 10,
                Height = 10,
                Fill = new SolidColorBrush(Tray.TrayGlyphs.ToColor(b.Color)),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
            });
            header.Children.Add(new TextBlock { Text = $"{b.Name} break is due", FontWeight = FontWeights.SemiBold, FontSize = 15 });
            stack.Children.Add(header);
            stack.Children.Add(countdown);
            var buttons = new WrapPanel();
            void Add(string title, AskChoice choice, bool isDefault = false)
            {
                var button = new Button { Content = title, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(12, 4, 12, 4), IsDefault = isDefault };
                button.Click += (_, _) => choose(choice);
                buttons.Children.Add(button);
            }
            Add("Start break", new AskChoice(AskChoiceKind.Start), isDefault: true);
            var c = b.Controls;
            if (c.ShowPostpone1 && b.CanPostpone)
            {
                Add($"Postpone {Format.Int(c.PostponeMinutes1)}m", new AskChoice(AskChoiceKind.Postpone, c.PostponeMinutes1));
            }
            if (c.ShowPostpone2 && b.CanPostpone)
            {
                Add($"Postpone {Format.Int(c.PostponeMinutes2)}m", new AskChoice(AskChoiceKind.Postpone, c.PostponeMinutes2));
            }
            if (c.ShowSkip && b.CanSkip)
            {
                Add("Skip", new AskChoice(AskChoiceKind.Skip));
            }
            stack.Children.Add(buttons);
            Content = Card(stack);
            Loaded += (_, _) => PlaceInCorner(top: true);
        }

        public void SetCountdown(double seconds) =>
            countdown.Text = $"Starts automatically in {Format.Countdown(seconds)}, or start it when you're ready.";
    }
}
