using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Recess.App.Breaks;

namespace Recess.App.Actions;

/// <summary>Recess's own notification, for when Windows notifications are turned off for it.</summary>
internal sealed class Banner : FloatingPanel
{
    private static Banner? current;

    private Banner(string title, string message)
    {
        Title = title;
        var stack = new StackPanel { Width = 320 };
        stack.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, FontSize = 14, TextWrapping = TextWrapping.Wrap });
        if (message.Length > 0)
        {
            stack.Children.Add(new TextBlock { Text = message, Opacity = 0.8, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
        }
        Content = Card(stack);
        Loaded += (_, _) => PlaceInCorner(top: false);
        MouseLeftButtonUp += (_, _) => Close();
    }

    public static void Show(string title, string message)
    {
        current?.Close();
        current = new Banner(title, message);
        current.Show();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        var shown = current;
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            shown.Close();
        };
        timer.Start();
    }
}
