using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Recess.Core;

namespace Recess.App.Settings;

/// <summary>Activity: app use through the day, statistics of breaks taken, and the log.</summary>
internal sealed class ActivityPage : ISettingsPage
{
    private static readonly Color Done = Color.FromRgb(48, 180, 88);
    private static readonly Color Postponed = Color.FromRgb(255, 159, 10);
    private static readonly Color Skipped = Color.FromRgb(235, 69, 58);
    private static readonly Color Minutes = Color.FromRgb(58, 140, 217);

    private readonly SettingsContext ctx;
    private readonly Form controls;
    private readonly TabControl tabs = new() { BorderThickness = new Thickness(0) };
    private readonly ContentControl statistics = new();
    private readonly ContentControl apps = new();
    private readonly DataGrid log = new()
    {
        AutoGenerateColumns = false,
        IsReadOnly = true,
        HeadersVisibility = DataGridHeadersVisibility.Column,
        GridLinesVisibility = DataGridGridLinesVisibility.None,
        Margin = new Thickness(16),
    };

    private string metric = "all";
    private Guid? breakId;
    private int days = 14;
    private DateOnly day;
    private bool dirty = true;

    public ActivityPage(SettingsContext ctx)
    {
        this.ctx = ctx;
        day = LocalTime.Date(ctx.Clock.UtcNow, ctx.Clock.Zone);
        controls = new Form(() => ctx.Model.MarkChanged());

        var header = new DockPanel { Margin = new Thickness(24, 16, 24, 8) };
        var toggles = Form.Line(
            controls.Toggle("Track breaks", () => ctx.Model.Settings.TrackBreaks, v => ctx.Model.Settings.TrackBreaks = v,
                "Log each break you take, postpone or skip, so you can see how you're doing."),
            controls.Toggle("Track apps", () => ctx.Model.Settings.TrackApps, v => ctx.Model.Settings.TrackApps = v,
                "Log which app is in front and for how long. Stays on this computer."));
        DockPanel.SetDock(toggles, Dock.Right);
        header.Children.Add(toggles);
        header.Children.Add(new TextBlock { Text = "Activity", FontSize = 20, FontWeight = FontWeights.SemiBold });

        log.Columns.Add(new DataGridTextColumn { Header = "Time", Binding = new System.Windows.Data.Binding("Time"), Width = 170 });
        log.Columns.Add(new DataGridTextColumn { Header = "Break", Binding = new System.Windows.Data.Binding("Break"), Width = 120 });
        log.Columns.Add(new DataGridTextColumn { Header = "Event", Binding = new System.Windows.Data.Binding("Event"), Width = 110 });
        log.Columns.Add(new DataGridTextColumn { Header = "Duration", Binding = new System.Windows.Data.Binding("Duration"), Width = 130 });
        log.Columns.Add(new DataGridTextColumn { Header = "Detail", Binding = new System.Windows.Data.Binding("Detail"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });

        tabs.Items.Add(new TabItem { Header = "Statistics", Content = Form.Scroll(statistics) });
        tabs.Items.Add(new TabItem { Header = "Apps", Content = Form.Scroll(apps) });
        tabs.Items.Add(new TabItem { Header = "Log", Content = log });
        tabs.Margin = new Thickness(12, 0, 12, 0);

        var root = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        root.Children.Add(tabs);
        View = root;

        ctx.Activity.Changed += () => dirty = true;
        Rebuild();
    }

    public UIElement View { get; }

    public void Refresh()
    {
        controls.Refresh();
        // Activity changes when breaks are logged; app time builds up every second but needn't redraw that often.
        if (dirty)
        {
            Rebuild();
        }
    }

    private void Rebuild()
    {
        dirty = false;
        statistics.Content = StatisticsView();
        apps.Content = AppsView();
        log.ItemsSource = ctx.Activity.AvailableDays().Take(14)
            .SelectMany(d => ctx.Activity.Record(d).Events)
            .OrderByDescending(e => e.Date)
            .Select(e => new
            {
                Time = LocalTime.ToLocal(e.Date, ctx.Clock.Zone).ToString("g", CultureInfo.CurrentCulture),
                Break = e.BreakName,
                Event = e.Kind.Title(),
                Duration = e.Duration > 0 ? Format.Full(e.Duration) : "",
                e.Detail,
            })
            .ToList();
    }

    // MARK: - Statistics

    private UIElement StatisticsView()
    {
        var root = new StackPanel { Margin = new Thickness(16) };
        var f = new Form(() =>
        {
            dirty = true;
            Rebuild();
        });
        root.Children.Add(Form.Line(
            Form.Text("Show"),
            f.Picker([("all", "Everything"), ("done", "Breaks taken per day"), ("postponed", "Postpones per day"), ("skipped", "Skips per day"), ("minutes", "Minutes on break per day")],
                () => metric, v => metric = v),
            Form.Text("for"),
            f.Picker(new (Guid?, string)[] { (null, "All breaks") }.Concat(ctx.Model.Breaks.Select(b => ((Guid?)b.Id, b.Name))), () => breakId, v => breakId = v),
            Form.Text("over"),
            f.Picker([(7, "7 days"), (14, "14 days"), (30, "30 days"), (90, "90 days")], () => days, v => days = v)));

        if (!ctx.Model.Settings.TrackBreaks)
        {
            root.Children.Add(new TextBlock { Text = "Turn on Track breaks to record statistics.", Opacity = 0.6, Margin = new Thickness(0, 24, 0, 0) });
            return root;
        }
        var stats = ctx.Activity.Stats(days, breakId);
        root.Children.Add(Chart(stats));

        int done = stats.Sum(s => s.Done), postponed = stats.Sum(s => s.Postponed), skipped = stats.Sum(s => s.Skipped);
        var total = stats.Sum(s => s.Duration);
        var summary = new WrapPanel { Margin = new Thickness(0, 16, 0, 0) };
        void Stat(string title, string value, Color color)
        {
            var s = new StackPanel { Margin = new Thickness(0, 0, 32, 0) };
            s.Children.Add(new TextBlock { Text = value, FontSize = 22, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(color) });
            s.Children.Add(new TextBlock { Text = title, FontSize = 12, Opacity = 0.65 });
            summary.Children.Add(s);
        }
        Stat("Done", done.ToString(CultureInfo.CurrentCulture), Done);
        Stat("Postponed", postponed.ToString(CultureInfo.CurrentCulture), Postponed);
        Stat("Skipped", skipped.ToString(CultureInfo.CurrentCulture), Skipped);
        Stat("Time on break", Format.Full(total), Minutes);
        Stat("Taken", done + skipped > 0 ? $"{done * 100 / (done + skipped)}%" : "–", Colors.Gray);
        root.Children.Add(summary);
        return root;
    }

    /// <summary>A bar chart, one column per day, with the bars stacked for "Everything".</summary>
    private UIElement Chart(List<DayStats> stats)
    {
        const double height = 240;
        var series = metric switch
        {
            "done" => [(Done, (Func<DayStats, double>)(s => s.Done))],
            "postponed" => [(Postponed, s => s.Postponed)],
            "skipped" => [(Skipped, s => s.Skipped)],
            "minutes" => [(Minutes, s => s.Duration / 60)],
            _ => new List<(Color, Func<DayStats, double>)> { (Done, s => s.Done), (Postponed, s => s.Postponed), (Skipped, s => s.Skipped) },
        };
        var max = Math.Max(1, stats.Max(s => series.Sum(x => x.Item2(s))));
        var grid = new Grid { Height = height + 24, Margin = new Thickness(0, 16, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        var scale = new Grid();
        foreach (var fraction in new[] { 0.0, 0.5, 1.0 })
        {
            scale.Children.Add(new TextBlock
            {
                Text = Format.Int(max * fraction).ToString(CultureInfo.CurrentCulture),
                FontSize = 11,
                Opacity = 0.6,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, height * (1 - fraction) - 7, 6, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
            });
        }
        grid.Children.Add(scale);
        for (var i = 0; i < stats.Count; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var s = stats[i];
            var column = new DockPanel();
            var label = new TextBlock
            {
                Text = stats.Count <= 14 || i % (stats.Count / 7) == 0 ? s.Day.ToString("d MMM", CultureInfo.CurrentCulture) : "",
                FontSize = 10,
                Opacity = 0.6,
                HorizontalAlignment = HorizontalAlignment.Center,
                Height = 24,
            };
            DockPanel.SetDock(label, Dock.Bottom);
            column.Children.Add(label);
            var bars = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(2, 0, 2, 0) };
            foreach (var (color, value) in series.AsEnumerable().Reverse())
            {
                var v = value(s);
                if (v > 0)
                {
                    bars.Children.Add(new Rectangle { Height = height * v / max, Fill = new SolidColorBrush(color), RadiusX = 2, RadiusY = 2, ToolTip = $"{s.Day:d}: {Format.Int(v)}" });
                }
            }
            column.Children.Add(bars);
            Grid.SetColumn(column, i + 1);
            grid.Children.Add(column);
        }
        return grid;
    }

    // MARK: - Apps

    private UIElement AppsView()
    {
        var root = new StackPanel { Margin = new Thickness(16) };
        var today = LocalTime.Date(ctx.Clock.UtcNow, ctx.Clock.Zone);
        var back = Form.Button("‹", () =>
        {
            day = day.AddDays(-1);
            Rebuild();
        });
        var forward = Form.Button("›", () =>
        {
            day = day.AddDays(1);
            Rebuild();
        });
        forward.IsEnabled = day < today;
        var clear = Form.Button("Delete all activity…", () =>
        {
            if (MessageBox.Show("Delete every recorded break and app-use entry? This can't be undone.", "Delete activity", MessageBoxButton.OKCancel,
                MessageBoxImage.Warning) == MessageBoxResult.OK)
            {
                ctx.Activity.ClearAll();
            }
        });
        var header = new DockPanel();
        DockPanel.SetDock(clear, Dock.Right);
        header.Children.Add(clear);
        header.Children.Add(Form.Line(back, new TextBlock { Text = day.ToString("D", CultureInfo.CurrentCulture), FontWeight = FontWeights.SemiBold, MinWidth = 240 }, forward));
        root.Children.Add(header);

        var record = ctx.Activity.Record(day);
        if (!ctx.Model.Settings.TrackApps && record.Apps.Count == 0)
        {
            root.Children.Add(new TextBlock { Text = "Turn on Track apps above to see which apps you use, and when you took breaks.", Opacity = 0.6, Margin = new Thickness(0, 24, 0, 0), TextWrapping = TextWrapping.Wrap });
            return root;
        }
        root.Children.Add(Timeline(record));
        var totals = record.Apps.Where(a => a.AppId.Length > 0)
            .GroupBy(a => a.AppId, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Name: g.First().Name, Total: g.Sum(a => a.Duration)))
            .OrderByDescending(t => t.Total)
            .ToList();
        var active = totals.Sum(t => t.Total);
        var taken = record.Events.Count(e => e.Kind is BreakEventKind.Done or BreakEventKind.EndedEarly);
        root.Children.Add(new TextBlock { Text = $"Active for {Format.Full(active)}; {taken} breaks taken", Opacity = 0.7, Margin = new Thickness(0, 8, 0, 8) });
        foreach (var (name, total) in totals)
        {
            var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
            var time = new TextBlock { Text = Format.Brief(total), Width = 70, TextAlignment = TextAlignment.Right };
            DockPanel.SetDock(time, Dock.Right);
            row.Children.Add(time);
            var bar = new ProgressBar { Width = 160, Height = 6, Maximum = 1, Value = active > 0 ? total / active : 0, Margin = new Thickness(8, 0, 8, 0) };
            DockPanel.SetDock(bar, Dock.Right);
            row.Children.Add(bar);
            row.Children.Add(new TextBlock { Text = name, TextTrimming = TextTrimming.CharacterEllipsis });
            root.Children.Add(row);
        }
        return root;
    }

    /// <summary>The day as a 24-hour strip: a colour per app, grey when idle, dots for breaks.</summary>
    private UIElement Timeline(ActivityDayRecord record)
    {
        var canvas = new Canvas { Height = 46, Margin = new Thickness(0, 12, 0, 0), ClipToBounds = true };
        var start = LocalTime.At(DateOnly.ParseExact(record.Day, "yyyy-MM-dd", CultureInfo.InvariantCulture), 0, 0, ctx.Clock.Zone);
        void Draw()
        {
            canvas.Children.Clear();
            var width = canvas.ActualWidth;
            if (width <= 0)
            {
                return;
            }
            var scale = width / 86400;
            canvas.Children.Add(new Rectangle { Width = width, Height = 28, RadiusX = 6, RadiusY = 6, Fill = new SolidColorBrush(Color.FromArgb(30, 128, 128, 128)) });
            foreach (var span in record.Apps)
            {
                var rect = new Rectangle
                {
                    Width = Math.Max(1, span.Duration * scale),
                    Height = 28,
                    Fill = new SolidColorBrush(span.AppId.Length == 0 ? Color.FromArgb(60, 128, 128, 128) : ColorFor(span.AppId)),
                    ToolTip = $"{span.Name}: {Format.Time(span.Start, ctx.Clock.Zone)}–{Format.Time(span.End, ctx.Clock.Zone)}",
                };
                Canvas.SetLeft(rect, (span.Start - start).TotalSeconds * scale);
                canvas.Children.Add(rect);
            }
            foreach (var e in record.Events.Where(e => e.Kind != BreakEventKind.Started))
            {
                var dot = new Ellipse
                {
                    Width = 8,
                    Height = 8,
                    Fill = new SolidColorBrush(e.Kind == BreakEventKind.Skipped ? Skipped : e.Kind == BreakEventKind.Postponed ? Postponed : Done),
                    ToolTip = $"{e.BreakName} {e.Kind.Title().ToLowerInvariant()} at {Format.Time(e.Date, ctx.Clock.Zone)}",
                };
                Canvas.SetLeft(dot, (e.Date - start).TotalSeconds * scale - 4);
                Canvas.SetTop(dot, 34);
                canvas.Children.Add(dot);
            }
            foreach (var h in new[] { 0, 6, 12, 18 })
            {
                var label = new TextBlock { Text = $"{h}:00", FontSize = 9, Opacity = 0.6 };
                Canvas.SetLeft(label, h * 3600 * scale + 3);
                Canvas.SetTop(label, 7);
                canvas.Children.Add(label);
            }
        }
        canvas.SizeChanged += (_, _) => Draw();
        return canvas;
    }

    private static Color ColorFor(string id)
    {
        ulong hash = 5381;
        foreach (var b in System.Text.Encoding.UTF8.GetBytes(id.ToLowerInvariant()))
        {
            hash = unchecked(hash * 33 + b);
        }
        return FromHsv(hash % 360, 0.55, 0.85);
    }

    private static Color FromHsv(double hue, double s, double v)
    {
        var c = v * s;
        var x = c * (1 - Math.Abs(hue / 60 % 2 - 1));
        var m = v - c;
        var (r, g, b) = (hue / 60) switch
        {
            < 1 => (c, x, 0.0),
            < 2 => (x, c, 0.0),
            < 3 => (0.0, c, x),
            < 4 => (0.0, x, c),
            < 5 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        return Color.FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }
}
