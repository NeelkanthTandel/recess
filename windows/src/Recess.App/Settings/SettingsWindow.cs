using System.Windows;
using System.Windows.Controls;
using Recess.App.Platform;
using Recess.Core;

namespace Recess.App.Settings;

/// <summary>
/// A stand-in Settings window for milestone 3: try each break, start at sign-in, and the data
/// folder. The full settings window replaces it in the next milestone.
/// </summary>
internal sealed class SettingsWindow : Window
{
    private static SettingsWindow? current;

    private readonly RecessModel model;
    private readonly Scheduler scheduler;
    private readonly IClock clock;
    private readonly StackPanel list = new();

    private SettingsWindow(RecessModel model, Scheduler scheduler, IClock clock)
    {
        this.model = model;
        this.scheduler = scheduler;
        this.clock = clock;
        Title = "Recess Settings";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.CanMinimize;
        var icon = IconRenderer.LoadImage(Path.Combine(AppContext.BaseDirectory, "Assets", "icon.png"));
        if (icon is not null)
        {
            Icon = icon;
        }

        var root = new StackPanel { Margin = new Thickness(24) };
        root.Children.Add(new TextBlock { Text = "Breaks", FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        root.Children.Add(list);

        var login = new CheckBox
        {
            Content = "Start Recess when I sign in to Windows",
            IsChecked = Registration.LaunchAtLogin,
            Margin = new Thickness(0, 20, 0, 0),
        };
        login.Click += (_, _) =>
        {
            Registration.LaunchAtLogin = login.IsChecked == true;
            model.Settings.LaunchAtLogin = login.IsChecked == true;
            model.MarkChanged();
        };
        root.Children.Add(login);
        if (Registration.LaunchAtLoginBlockedByUser)
        {
            root.Children.Add(new TextBlock
            {
                Text = "Recess is turned off under Startup apps in Windows Settings, which overrides this.",
                Opacity = 0.7,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0),
            });
        }

        var folder = new Button { Content = "Open data folder", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 16, 0, 0), Padding = new Thickness(12, 4, 12, 4) };
        folder.Click += (_, _) => System.Diagnostics.Process.Start("explorer.exe", DataFolder.Path);
        root.Children.Add(folder);
        root.Children.Add(new TextBlock
        {
            Text = "The full settings window arrives in the next update. Until then, every break command is in the tray icon's menu.",
            Opacity = 0.7,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 20, 0, 0),
        });
        Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

        Rebuild();
        model.Changed += Rebuild;
        Closed += (_, _) =>
        {
            model.Changed -= Rebuild;
            current = null;
        };
    }

    public static void Open(RecessModel model, Scheduler scheduler, IClock clock)
    {
        current ??= new SettingsWindow(model, scheduler, clock);
        current.Show();
        if (current.WindowState == WindowState.Minimized)
        {
            current.WindowState = WindowState.Normal;
        }
        current.Activate();
    }

    private readonly List<(Guid Id, TextBlock Title, TextBlock Status)> rows = [];

    // Rows are rebuilt only when breaks are added or removed, so a click is never lost to a refresh.
    private void Rebuild()
    {
        if (!rows.Select(r => r.Id).SequenceEqual(model.Breaks.Select(b => b.Id)))
        {
            list.Children.Clear();
            rows.Clear();
            foreach (var b in model.Breaks)
            {
                var row = new DockPanel { Margin = new Thickness(0, 6, 0, 6) };
                var tryIt = new Button { Content = "Try it", Padding = new Thickness(12, 4, 12, 4), VerticalAlignment = VerticalAlignment.Center };
                var id = b.Id;
                tryIt.Click += (_, _) => scheduler.StartBreak(id, manual: true, preview: true);
                DockPanel.SetDock(tryIt, Dock.Right);
                row.Children.Add(tryIt);
                var text = new StackPanel();
                var title = new TextBlock { FontWeight = FontWeights.Medium };
                var status = new TextBlock { Opacity = 0.7 };
                text.Children.Add(title);
                text.Children.Add(status);
                row.Children.Add(text);
                list.Children.Add(row);
                rows.Add((id, title, status));
            }
        }
        foreach (var (id, title, status) in rows)
        {
            if (model.Find(id) is { } b)
            {
                title.Text = $"{b.Name}: {Format.Full(b.Duration)} every {Format.Full(b.Frequency)}";
                status.Text = StatusText.BreakStatus(b, model, scheduler, clock.Zone);
            }
        }
    }
}
