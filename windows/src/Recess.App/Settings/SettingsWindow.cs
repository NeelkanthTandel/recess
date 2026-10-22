using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Recess.App.Platform;
using Recess.Core;

namespace Recess.App.Settings;

/// <summary>A settings page: its content, refreshed when the model changes.</summary>
internal interface ISettingsPage
{
    UIElement View { get; }

    void Refresh();
}

/// <summary>
/// The Settings window: breaks (in priority order) and option pages in a sidebar, the selected
/// one on the right. Drag breaks to change their priority.
/// </summary>
internal sealed class SettingsWindow : Window
{
    private static SettingsWindow? current;

    private readonly SettingsContext ctx;
    private readonly ListBox sidebar = new() { BorderThickness = new Thickness(0), Background = Brushes.Transparent };
    private readonly ContentControl detail = new();
    private readonly DispatcherTimer refresh = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private ISettingsPage? page;
    private string selected = "";
    private string sidebarKey = "";
    private bool refreshPending;
    private Point dragStart;

    private SettingsWindow(SettingsContext ctx)
    {
        this.ctx = ctx;
        Title = "Recess";
        Width = 980;
        Height = 680;
        MinWidth = 820;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        if (IconRenderer.LoadImage(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "icon.png")) is { } icon)
        {
            Icon = icon;
        }

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(270) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var side = new DockPanel { Background = new SolidColorBrush(Color.FromArgb(14, 128, 128, 128)) };
        var bar = BottomBar();
        DockPanel.SetDock(bar, Dock.Bottom);
        side.Children.Add(bar);
        side.Children.Add(sidebar);
        grid.Children.Add(side);
        Grid.SetColumn(detail, 1);
        grid.Children.Add(detail);
        Content = grid;

        sidebar.SelectionChanged += (_, _) =>
        {
            if (sidebar.SelectedItem is ListBoxItem { Tag: string key } && key != selected)
            {
                Show(key);
            }
        };
        sidebar.PreviewMouseLeftButtonDown += (_, e) => dragStart = e.GetPosition(null);
        sidebar.PreviewMouseMove += OnDragMove;
        sidebar.AllowDrop = true;
        sidebar.Drop += OnDrop;

        ctx.Model.Changed += ModelChanged;
        refresh.Tick += (_, _) =>
        {
            if (refreshPending)
            {
                refreshPending = false;
                RefreshAll();
            }
        };
        refresh.Start();
        Closed += (_, _) =>
        {
            ctx.Model.Changed -= ModelChanged;
            refresh.Stop();
            ctx.Model.SaveNow();
            current = null;
        };
        BuildSidebar();
    }

    /// <summary>Opens Settings at a page ("general", "shortcuts", "rules", "activity", "advanced") or a break.</summary>
    public static void Open(SettingsContext ctx, string? pageName = null, Guid? breakId = null)
    {
        current ??= new SettingsWindow(ctx);
        var key = breakId is { } id ? "break:" + id : pageName ?? (current.selected.Length > 0 ? current.selected : null);
        key ??= ctx.Model.Breaks.FirstOrDefault() is { } first ? "break:" + first.Id : "general";
        current.Select(key);
        current.Show();
        if (current.WindowState == WindowState.Minimized)
        {
            current.WindowState = WindowState.Normal;
        }
        current.Activate();
    }

    private void ModelChanged() => refreshPending = true;

    private void RefreshAll()
    {
        // A break was added, removed, renamed or moved: rebuild the sidebar.
        if (SidebarKey() != sidebarKey)
        {
            BuildSidebar();
            if (selected.StartsWith("break:", StringComparison.Ordinal) && ctx.Model.Find(Guid.Parse(selected[6..])) is null)
            {
                Select(ctx.Model.Breaks.FirstOrDefault() is { } b ? "break:" + b.Id : "general");
                return;
            }
        }
        foreach (var item in sidebar.Items.OfType<ListBoxItem>())
        {
            if (item.Content is BreakRow row)
            {
                row.Update();
            }
        }
        page?.Refresh();
    }

    private string SidebarKey() => string.Join('|', ctx.Model.Breaks.Select(b => $"{b.Id}{b.Name}{b.Enabled}{b.Color}{b.LabelAsPie}"));

    private void BuildSidebar()
    {
        sidebarKey = SidebarKey();
        sidebar.Items.Clear();
        sidebar.Items.Add(Header("Breaks"));
        foreach (var b in ctx.Model.Breaks)
        {
            var item = new ListBoxItem { Tag = "break:" + b.Id, Content = new BreakRow(ctx, b), Padding = new Thickness(8, 4, 8, 4) };
            item.ContextMenu = BreakMenu(b);
            sidebar.Items.Add(item);
        }
        sidebar.Items.Add(Header("Options"));
        foreach (var (key, title) in new[] { ("general", "General"), ("shortcuts", "Shortcuts"), ("rules", "Rules"), ("activity", "Activity"), ("advanced", "Advanced") })
        {
            sidebar.Items.Add(new ListBoxItem { Tag = key, Content = title, Padding = new Thickness(12, 6, 8, 6) });
        }
        HighlightSelected();
    }

    private static ListBoxItem Header(string text) => new()
    {
        Content = new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeights.SemiBold, Opacity = 0.6, Margin = new Thickness(4, 10, 0, 2) },
        IsEnabled = false,
        Focusable = false,
    };

    private void Select(string key)
    {
        Show(key);
        HighlightSelected();
    }

    private void HighlightSelected()
    {
        var item = sidebar.Items.OfType<ListBoxItem>().FirstOrDefault(i => i.Tag as string == selected);
        if (item is not null && sidebar.SelectedItem != item)
        {
            sidebar.SelectedItem = item;
        }
    }

    private void Show(string key)
    {
        selected = key;
        page = key switch
        {
            "general" => new GeneralPage(ctx),
            "shortcuts" => new ShortcutsPage(ctx),
            "rules" => new RulesPage(ctx),
            "activity" => new ActivityPage(ctx),
            "advanced" => new AdvancedPage(ctx),
            _ when key.StartsWith("break:", StringComparison.Ordinal) && Guid.TryParse(key[6..], out var id) && ctx.Model.Find(id) is not null
                => new BreakEditor(ctx, id),
            _ => null,
        };
        detail.Content = page?.View ?? new TextBlock { Text = "Select a break or option", Opacity = 0.6, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    }

    // MARK: - Menus

    private ContextMenu BreakMenu(Recess.Core.Break b)
    {
        var sched = ctx.Scheduler;
        var menu = new ContextMenu();
        menu.Items.Add(Item("Start break", () => sched.StartBreak(b.Id, manual: true)));
        var due = new MenuItem { Header = "Make due in" };
        foreach (var (title, seconds) in Choices.MakeDue)
        {
            due.Items.Add(Item(title, () => sched.MakeDue(b.Id, seconds)));
        }
        menu.Items.Add(due);
        menu.Opened += (_, _) => UpdateBreakMenu(menu, b);
        var pause = new MenuItem { Header = "Pause break", Tag = "pause" };
        foreach (var (title, seconds) in Choices.Pause)
        {
            double? length = seconds == 0 ? null : seconds;
            pause.Items.Add(Item(title, () => sched.Pause(b.Id, length)));
        }
        menu.Items.Add(pause);
        menu.Items.Add(Item("Resume break", () => sched.Resume(b.Id), "resume"));
        menu.Items.Add(Item("Reset break", () => sched.ResetBreak(b.Id), "reset"));
        menu.Items.Add(Item("Disable break", () => sched.SetEnabled(b.Id, !b.Enabled), "enable"));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Move up", () => Move(b.Id, -1)));
        menu.Items.Add(Item("Move down", () => Move(b.Id, 1)));
        menu.Items.Add(Item("Duplicate break", () =>
        {
            if (sched.DuplicateBreak(b.Id) is { } copy)
            {
                Select("break:" + copy);
            }
        }));
        menu.Items.Add(Item("Delete break…", () => Delete(b), "delete"));
        return menu;
    }

    private void UpdateBreakMenu(ContextMenu menu, Recess.Core.Break b)
    {
        var s = ctx.Model.Settings;
        foreach (var item in menu.Items.OfType<MenuItem>())
        {
            switch (item.Tag as string)
            {
                case "pause":
                    item.Visibility = b.State.PausedUntil is null && !s.HidePause ? Visibility.Visible : Visibility.Collapsed;
                    break;
                case "resume":
                    item.Visibility = b.State.PausedUntil is not null ? Visibility.Visible : Visibility.Collapsed;
                    break;
                case "reset":
                    item.Visibility = s.HideReset ? Visibility.Collapsed : Visibility.Visible;
                    break;
                case "enable":
                    item.Header = b.Enabled ? "Disable break" : "Enable break";
                    item.Visibility = s.HideDisable ? Visibility.Collapsed : Visibility.Visible;
                    break;
                case "delete":
                    item.Visibility = s.HideDelete ? Visibility.Collapsed : Visibility.Visible;
                    break;
            }
        }
    }

    private void Delete(Recess.Core.Break b)
    {
        if (MessageBox.Show(this, $"Delete the {b.Name} break? This can't be undone.", "Delete break", MessageBoxButton.OKCancel,
            MessageBoxImage.Warning) == MessageBoxResult.OK)
        {
            ctx.Scheduler.DeleteBreak(b.Id);
        }
    }

    private void Move(Guid id, int by)
    {
        var breaks = ctx.Model.Breaks;
        var from = breaks.FindIndex(b => b.Id == id);
        var to = from + by;
        if (from < 0 || to < 0 || to >= breaks.Count)
        {
            return;
        }
        var b = breaks[from];
        breaks.RemoveAt(from);
        breaks.Insert(to, b);
        ctx.Model.MarkChanged();
    }

    private UIElement BottomBar()
    {
        var bar = new DockPanel { Margin = new Thickness(8), LastChildFill = false };
        var add = new Button { Content = "+", Width = 30, ToolTip = "Create a break, or copy the selected one.", Margin = new Thickness(0, 0, 6, 0) };
        add.Click += (_, _) =>
        {
            var menu = new ContextMenu { PlacementTarget = add, Placement = PlacementMode.Top };
            menu.Items.Add(Item("New break", () => Select("break:" + ctx.Scheduler.AddBreak())));
            if (selected.StartsWith("break:", StringComparison.Ordinal))
            {
                var id = Guid.Parse(selected[6..]);
                menu.Items.Add(Item("Duplicate break", () =>
                {
                    if (ctx.Scheduler.DuplicateBreak(id) is { } copy)
                    {
                        Select("break:" + copy);
                    }
                }));
            }
            menu.IsOpen = true;
        };
        var pause = new Button { Width = 30, ToolTip = "Pause every break, or resume them." };
        void UpdatePause() => pause.Content = ctx.Model.AllPaused(ctx.Now) ? "▶" : "❚❚";
        pause.Click += (_, _) =>
        {
            if (ctx.Model.AllPaused(ctx.Now))
            {
                ctx.Scheduler.ResumeAll();
            }
            else
            {
                ctx.Scheduler.PauseAll(null);
            }
            UpdatePause();
        };
        UpdatePause();
        ctx.Model.Changed += UpdatePause;
        Closed += (_, _) => ctx.Model.Changed -= UpdatePause;
        var more = new Button { Content = "…", Width = 30, ToolTip = "More commands." };
        more.Click += (_, _) =>
        {
            var menu = new ContextMenu { PlacementTarget = more, Placement = PlacementMode.Top };
            menu.Items.Add(Item("Start next break", ctx.Scheduler.StartNextBreak));
            menu.Items.Add(Item("Postpone next break", () => ctx.Scheduler.PostponeNext(1)));
            menu.Items.Add(Item("Skip next break", ctx.Scheduler.SkipNext));
            menu.Items.Add(Item("Reset all breaks", ctx.Scheduler.ResetAll));
            menu.Items.Add(Item("Stop sound", ctx.Commands.StopSound));
            menu.Items.Add(new Separator());
            menu.Items.Add(Item("Open data folder", () => System.Diagnostics.Process.Start("explorer.exe", ctx.Store.Folder)));
            menu.Items.Add(Item("Setup assistant…", ctx.OpenSetupAssistant));
            menu.Items.Add(new Separator());
            menu.Items.Add(Item("Quit Recess", ctx.Commands.Quit));
            menu.IsOpen = true;
        };
        bar.Children.Add(add);
        bar.Children.Add(pause);
        DockPanel.SetDock(more, Dock.Right);
        bar.Children.Add(more);
        return bar;
    }

    private static MenuItem Item(string header, Action action, string? tag = null)
    {
        var item = new MenuItem { Header = header, Tag = tag };
        item.Click += (_, _) => action();
        return item;
    }

    // MARK: - Drag to reorder

    private void OnDragMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }
        var delta = dragStart - e.GetPosition(null);
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }
        if (ItemAt(e.OriginalSource) is { Tag: string key } && key.StartsWith("break:", StringComparison.Ordinal))
        {
            DragDrop.DoDragDrop(sidebar, key, DragDropEffects.Move);
        }
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(string)) is not string key || !key.StartsWith("break:", StringComparison.Ordinal))
        {
            return;
        }
        var breaks = ctx.Model.Breaks;
        var id = Guid.Parse(key[6..]);
        var from = breaks.FindIndex(b => b.Id == id);
        var target = ItemAt(e.OriginalSource);
        int to;
        if (target?.Tag is string t && t.StartsWith("break:", StringComparison.Ordinal))
        {
            to = breaks.FindIndex(b => b.Id == Guid.Parse(t[6..]));
        }
        else
        {
            // Dropped below the breaks: last place.
            to = breaks.Count - 1;
        }
        if (from < 0 || to < 0 || from == to)
        {
            return;
        }
        var moved = breaks[from];
        breaks.RemoveAt(from);
        breaks.Insert(to, moved);
        ctx.Model.MarkChanged();
    }

    private static ListBoxItem? ItemAt(object source)
    {
        var d = source as DependencyObject;
        while (d is not null and not ListBoxItem)
        {
            d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        }
        return d as ListBoxItem;
    }

    /// <summary>A break in the sidebar: colour, name, when it's next due, and how often.</summary>
    private sealed class BreakRow : DockPanel
    {
        private readonly SettingsContext ctx;
        private readonly Recess.Core.Break b;
        private readonly TextBlock subtitle = new() { FontSize = 12, Opacity = 0.7, TextTrimming = TextTrimming.CharacterEllipsis };
        private readonly TextBlock every = new() { FontSize = 11, Opacity = 0.5, TextTrimming = TextTrimming.CharacterEllipsis };

        public BreakRow(SettingsContext ctx, Recess.Core.Break b)
        {
            this.ctx = ctx;
            this.b = b;
            var dot = new Ellipse
            {
                Width = 12,
                Height = 12,
                Margin = new Thickness(0, 4, 10, 0),
                VerticalAlignment = VerticalAlignment.Top,
                Fill = new SolidColorBrush(b.Enabled ? Tray.TrayGlyphs.ToColor(b.Color) : Color.FromArgb(100, 128, 128, 128)),
            };
            Children.Add(dot);
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = b.Name, FontWeight = FontWeights.Medium });
            text.Children.Add(subtitle);
            text.Children.Add(every);
            Children.Add(text);
            Update();
        }

        public void Update()
        {
            var now = ctx.Now;
            subtitle.Text = !b.Enabled ? "Disabled"
                : ctx.Model.ActiveBreakId == b.Id ? "Having the break now"
                : b.IsPaused(now) ? (b.State.PausedUntil >= LocalTime.DistantFuture ? "Paused" : $"Paused until {Format.Time(b.State.PausedUntil!.Value, ctx.Clock.Zone)}")
                : ctx.Scheduler.WaitingDescription(b) is { } waiting ? "Due now; " + waiting
                : "Next: " + ctx.Model.DateString(b.State.NextDue, now, ctx.Clock.Zone);
            every.Text = $"Every {b.Schedule.Frequency.FullDescription} for {b.Schedule.Duration.FullDescription}";
        }
    }
}

/// <summary>Time choices shared by the tray menu and Settings.</summary>
internal static class Choices
{
    public static readonly (string Title, int Seconds)[] MakeDue =
    [
        ("30 seconds", 30), ("1 minute", 60), ("2 minutes", 120), ("3 minutes", 180), ("4 minutes", 240), ("5 minutes", 300),
        ("10 minutes", 600), ("15 minutes", 900), ("20 minutes", 1200), ("30 minutes", 1800), ("45 minutes", 2700), ("1 hour", 3600),
    ];

    public static readonly (string Title, int Seconds)[] Pause =
    [
        ("1 minute", 60), ("2 minutes", 120), ("5 minutes", 300), ("10 minutes", 600), ("15 minutes", 900), ("30 minutes", 1800),
        ("1 hour", 3600), ("2 hours", 7200), ("3 hours", 10800), ("4 hours", 14400), ("5 hours", 18000), ("8 hours", 28800),
        ("12 hours", 43200), ("24 hours", 86400), ("Until I resume", 0),
    ];
}
