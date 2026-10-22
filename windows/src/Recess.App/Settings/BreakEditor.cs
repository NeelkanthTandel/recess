using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Recess.App.Breaks;
using Recess.Core;

namespace Recess.App.Settings;

/// <summary>One break's settings, on five tabs: Name, Schedule, Appearance, Postpone &amp; Skip, Actions.</summary>
internal sealed class BreakEditor : ISettingsPage
{
    private static string lastTab = "Schedule";

    private readonly SettingsContext ctx;
    private readonly Guid id;
    private readonly TabControl tabs = new() { Margin = new Thickness(12, 0, 12, 0), BorderThickness = new Thickness(0) };
    private readonly List<Form> forms = [];
    private readonly TextBlock title = new() { FontSize = 20, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    private readonly Ellipse dot = new() { Width = 14, Height = 14, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly Button tryIt;
    private ActionsTab? actionsTab;
    private string timing;

    public BreakEditor(SettingsContext ctx, Guid id)
    {
        this.ctx = ctx;
        this.id = id;
        timing = B.Schedule.TimingKey;

        var header = new DockPanel { Margin = new Thickness(20, 14, 20, 6) };
        tryIt = Form.Button("Try it", () => ctx.Scheduler.StartBreak(id, manual: true, preview: true), "Preview the break to try the settings; it isn't counted.");
        tryIt.Margin = new Thickness(0);
        DockPanel.SetDock(tryIt, Dock.Right);
        header.Children.Add(tryIt);
        header.Children.Add(Form.Line(dot, title));

        AddTab("Name", NameTab());
        AddTab("Schedule", ScheduleTab());
        AddTab("Appearance", AppearanceTab());
        AddTab("Postpone & Skip", ControlsTab());
        actionsTab = new ActionsTab(ctx, id);
        tabs.Items.Add(new TabItem { Header = "Actions", Content = actionsTab.View });
        tabs.SelectedIndex = Math.Max(0, tabs.Items.OfType<TabItem>().ToList().FindIndex(t => (string)t.Header == lastTab));
        tabs.SelectionChanged += (_, e) =>
        {
            if (e.Source == tabs && tabs.SelectedItem is TabItem t)
            {
                lastTab = (string)t.Header;
            }
        };

        var root = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        root.Children.Add(tabs);
        View = root;
        Refresh();
    }

    public UIElement View { get; }

    private Recess.Core.Break B => ctx.Model.Find(id) ?? new Recess.Core.Break();

    public void Refresh()
    {
        title.Text = B.Name;
        dot.Fill = new SolidColorBrush(Tray.TrayGlyphs.ToColor(B.Color));
        tryIt.IsEnabled = ctx.Model.ActiveBreakId is null;
        foreach (var f in forms)
        {
            f.Refresh();
        }
        actionsTab?.Refresh();
    }

    private void AddTab(string header, Form form) =>
        tabs.Items.Add(new TabItem { Header = header, Content = Form.Scroll(form.Root) });

    private Form NewForm()
    {
        var form = new Form(Changed);
        forms.Add(form);
        return form;
    }

    // Saves the edit; a change to when the break happens re-times it.
    private void Changed()
    {
        ctx.Model.MarkChanged();
        var key = B.Schedule.TimingKey;
        if (key != timing)
        {
            timing = key;
            ctx.Scheduler.ScheduleChanged(id);
        }
    }

    // MARK: - Name

    private Form NameTab()
    {
        var f = NewForm();
        var s = f.Section();
        f.Row(s, "Name", f.TextField(() => B.Name, v => B.Name = v), "What this break is called in menus and on the break screen.");
        f.Row(s, "Label colour", f.ColorField(() => B.Color, v => B.Color = v with { A = 1 }, opacity: false),
            "Colour used for this break in Settings, the tray and the break screen.");
        f.Full(s, f.Toggle("Use a pie of today's postpones, skips and completions as the label", () => B.LabelAsPie, v => B.LabelAsPie = v,
            "Replaces the colour dot with a tiny pie chart of today's results for this break."));
        f.Full(s, f.Toggle("Enabled", () => B.Enabled, v => ctx.Scheduler.SetEnabled(id, v)));

        var shortcut = f.Section("Start shortcut");
        f.Row(shortcut, "Start this break from any app", new ShortcutRecorder(ctx.HotKeys, () => B.StartShortcut, v =>
        {
            B.StartShortcut = v;
            ctx.Model.MarkChanged();
            ctx.ShortcutsChanged();
        }));
        f.Hint(shortcut, "A keyboard shortcut that starts this break straight away, whichever app is in front.");

        var comments = f.Section("Comments");
        var box = f.Full(comments, f.TextField(() => B.Comments, v => B.Comments = v, width: double.NaN, multiline: true));
        box.HorizontalAlignment = HorizontalAlignment.Stretch;

        var today = f.Section("Today");
        f.Row(today, "Status", f.Value(() => B.State.Status));
        f.Row(today, "Postponed / skipped / done", f.Value(() => $"{B.State.Counts.TodayPostpone} / {B.State.Counts.TodaySkip} / {B.State.Counts.TodayDone}"));
        f.Row(today, "Time on break today", f.Value(() => Format.Full(B.State.Counts.TodayDuration)));
        f.Row(today, "All time", f.Value(() => $"{B.State.Counts.TotalDone} breaks, {Format.Full(B.State.Counts.TotalDuration)}"));
        return f;
    }

    // MARK: - Schedule

    private Form ScheduleTab()
    {
        var f = NewForm();
        var s = f.Section();
        var duration = f.Row(s, "Break for", f.IntervalField(() => B.Schedule.Duration, v => B.Schedule.Duration = v, minimum: 1), "How long the break lasts.");
        var work = new TextBlock { Opacity = 0.65, VerticalAlignment = VerticalAlignment.Center };
        f.OnRefresh(() => work.Text = $"(work for {Format.Full(Math.Max(0, B.Frequency - B.Schedule.Duration.TotalSeconds))})");
        var every = f.Row(s, "Every", Form.Line(f.IntervalField(() => B.Schedule.Frequency, v => B.Schedule.Frequency = v, [TimeUnit.Minutes, TimeUnit.Hours], 10), work),
            "Time from the start of one break to the start of the next (the break itself is included).");
        f.EnabledWhen(duration, () => B.Schedule.Adjustment != DueAdjustment.Calendar);
        f.EnabledWhen(every, () => B.Schedule.Adjustment != DueAdjustment.Calendar);

        f.Row(s, "Next due from", f.EnumPicker(v => v.Title(), () => B.Schedule.Adjustment, v => B.Schedule.Adjustment = v),
            "What the next break is timed from: when the last one was due, when it finished, a set minute of each hour, or your calendar.");
        var fixedRow = f.Row(s, "At", f.IntField(() => B.Schedule.FixedMinutesPastHour, v => B.Schedule.FixedMinutesPastHour = v, 0, 59, "minutes past the hour"));
        f.VisibleWhen(fixedRow, () => B.Schedule.Adjustment == DueAdjustment.FixedTime);
        var calendar = CalendarControls(f, s);
        f.VisibleWhen(calendar, () => B.Schedule.Adjustment == DueAdjustment.Calendar);
        f.Row(s, "Next due", f.Value(() => ctx.Scheduler.WaitingDescription(B) ?? ctx.Model.DateString(B.State.NextDue, ctx.Now, ctx.Clock.Zone)),
            "When this break will next happen. Use Make due in the break's menu to move it.");

        var avail = f.Section("Availability");
        f.Row(avail, "Time of day", f.EnumPicker(v => v.Title(), () => B.Schedule.Availability, v => B.Schedule.Availability = v),
            "Restrict the hours this break can happen in.");
        var from = f.Row(avail, "From", f.TimeField(() => B.Schedule.FromTime, v => B.Schedule.FromTime = v), "The day's first break comes one interval after this time.");
        var to = f.Row(avail, "To", f.TimeField(() => B.Schedule.ToTime, v => B.Schedule.ToTime = v), "No break of this kind starts at or after this time.");
        f.VisibleWhen(from, () => B.Schedule.Availability == Availability.Between);
        f.VisibleWhen(to, () => B.Schedule.Availability == Availability.Between);
        f.Row(avail, "Days", Weekdays(f), "Click a day to turn this break on or off for it.");

        var away = f.Section("Time away");
        f.Row(away, "While you're away from the computer", f.EnumPicker(v => v.Title(), () => B.Schedule.IdleHandling, v => B.Schedule.IdleHandling = v));
        f.Full(away, f.ToggleLine(() => B.Schedule.ResetWhenIdle, v => B.Schedule.ResetWhenIdle = v, null,
            Form.Text("Start the timer over after being away for at least"),
            f.IntervalField(() => B.Schedule.ResetWhenIdleInterval ?? B.Schedule.Duration, v => B.Schedule.ResetWhenIdleInterval = v, minimum: 1)));
        f.Full(away, f.Toggle("Start the timer over when a more important break finishes", () => B.Schedule.ResetWhenFinishingHigherPriority,
            v => B.Schedule.ResetWhenFinishingHigherPriority = v));
        f.Full(away, f.ToggleLine(() => B.Schedule.SkipForHigherPriority, v => B.Schedule.SkipForHigherPriority = v, null,
            Form.Text("Don't bother with this break when a more important one is coming within"),
            f.IntervalField(() => B.Schedule.SkipForHigherPriorityInterval, v => B.Schedule.SkipForHigherPriorityInterval = v, [TimeUnit.Seconds, TimeUnit.Minutes])));
        f.Hint(away, "Breaks nearer the top of the list in Settings are more important. Drag them to reorder.");

        var when = f.Section("When it's time");
        f.Row(when, "Start", f.EnumPicker(v => v.Title(), () => B.Schedule.DueBehavior, v => B.Schedule.DueBehavior = v));
        f.LiveHint(when, () => B.Schedule.DueBehavior == DueBehavior.Ask
            ? "A small panel lets you begin when ready; if you ignore it, the break begins on its own after this long."
            : B.Schedule.DueBehavior.Explanation());
        var auto = f.Row(when, "Start anyway after", f.IntervalField(() => B.Schedule.AskAutoStart, v => B.Schedule.AskAutoStart = v, [TimeUnit.Seconds, TimeUnit.Minutes], 5));
        f.VisibleWhen(auto, () => B.Schedule.DueBehavior == DueBehavior.Ask);
        return f;
    }

    private StackPanel Weekdays(Form f)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        var names = System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat.AbbreviatedDayNames;
        for (var day = 1; day <= 7; day++)
        {
            var d = day;
            var toggle = new System.Windows.Controls.Primitives.ToggleButton { Content = names[d - 1], MinWidth = 40, Margin = new Thickness(0, 0, 4, 0), Padding = new Thickness(6, 3, 6, 3) };
            f.OnRefresh(() => toggle.IsChecked = B.Schedule.Weekdays.Contains(d));
            toggle.Click += (_, _) =>
            {
                // At least one day has to stay on.
                if (B.Schedule.Weekdays.Contains(d))
                {
                    if (B.Schedule.Weekdays.Count > 1)
                    {
                        B.Schedule.Weekdays.Remove(d);
                    }
                }
                else
                {
                    B.Schedule.Weekdays.Add(d);
                }
                f.Changed();
                f.Refresh();
            };
            line.Children.Add(toggle);
        }
        return line;
    }

    private StackPanel CalendarControls(Form f, StackPanel section)
    {
        var panel = new StackPanel();
        section.Children.Add(panel);
        var calendars = ctx.Model.Settings.Calendars;
        if (calendars.Count == 0)
        {
            f.Hint(panel, "Add a calendar under Settings → General → Calendars to time breaks from its events.");
            return panel;
        }
        f.Row(panel, "Calendar", f.Picker(new (string?, string)[] { (null, "All calendars") }.Concat(calendars.Select(c => ((string?)c.Id, c.Name))),
            () => B.Schedule.CalendarIdentifier, v => B.Schedule.CalendarIdentifier = v), "Which calendar's events become breaks.");
        f.Row(panel, "Matching", f.TextField(() => B.Schedule.CalendarEventFilter, v => B.Schedule.CalendarEventFilter = v, placeholder: "any event"),
            "Only events whose title, location or notes contain one of these comma-separated words. Leave empty for every event.");
        f.Row(panel, "Next event", Form.Line(f.Value(() => B.State.NextEventStart is { } start && B.State.NextEventEnd is { } end
            ? $"{Format.Absolute(start, ctx.Now, ctx.Clock.Zone)} – {Format.Time(end, ctx.Clock.Zone)}"
            : "No upcoming matching events"), Form.Button("Reload", () => ctx.Scheduler.ScheduleChanged(id))));
        return panel;
    }

    // MARK: - Appearance

    private Form AppearanceTab()
    {
        var f = NewForm();
        var s = f.Section("Theme");
        Theme Selected() => ctx.Themes.Find(B.Appearance.ThemeId);
        ComboBox? picker = null;
        void BuildPicker()
        {
            var themes = ctx.Themes.Themes;
            picker = f.Picker(themes.Select(t => (t.Id, (t.Source == ThemeSource.User ? "My themes: " : "") + t.Name)), () => B.Appearance.ThemeId,
                v => B.Appearance.ThemeId = v, 240);
        }
        BuildPicker();
        var pickerRow = new ContentControl { Content = picker };
        f.Row(s, "Show", pickerRow, "What fills the screen during the break.");
        void ThemesChanged()
        {
            BuildPicker();
            pickerRow.Content = picker;
            f.Refresh();
        }
        // Listen only while this editor is on screen.
        pickerRow.Loaded += (_, _) => ctx.Themes.Changed += ThemesChanged;
        pickerRow.Unloaded += (_, _) => ctx.Themes.Changed -= ThemesChanged;
        f.LiveHint(s, () => Selected().Comments);
        f.LiveHint(s, () => Selected().Author.Length > 0 || Selected().CreditsName.Length > 0
            ? $"By {Selected().Author}{(Selected().CreditsName.Length > 0 ? " · Source: " + Selected().CreditsName : "")}"
            : "");
        var delete = Form.Button("Delete theme", () =>
        {
            var t = Selected();
            if (t.Editable && t.Folder is not null && MessageBox.Show($"Move the {t.Name} theme to the Recycle Bin?", "Delete theme",
                MessageBoxButton.OKCancel) == MessageBoxResult.OK)
            {
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(t.Folder, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                B.Appearance.ThemeId = ThemeIds.Default;
                ctx.Model.MarkChanged();
                ctx.Themes.Reload();
            }
        });
        f.VisibleWhen(delete, () => Selected().Editable);
        f.Full(s, Form.Line(
            Form.Button("New theme…", () =>
            {
                if (ThemeEditorWindow.Create(ctx.Themes, Window.GetWindow(View)) is { } theme)
                {
                    B.Appearance.ThemeId = theme.Id;
                    ctx.Model.MarkChanged();
                    f.Refresh();
                }
            }),
            delete,
            Form.Button("Open themes folder", () => System.Diagnostics.Process.Start("explorer.exe", ctx.Themes.UserFolder)),
            Form.Button("Reload", ctx.Themes.Reload)));
        var message = f.Row(s, "Message", f.TextField(() => B.Appearance.Message, v => B.Appearance.Message = v, placeholder: "A random tip"));
        var messageHint = f.Hint(s, "Shown below the countdown. Use {break} for the break's name. Leave empty for a random tip.");
        f.VisibleWhen(message, () => B.Appearance.ThemeId is ThemeIds.Default or ThemeIds.None);
        f.VisibleWhen(messageHint, () => B.Appearance.ThemeId is ThemeIds.Default or ThemeIds.None);

        var screens = f.Section("Screens");
        f.Row(screens, "On screen", f.EnumPicker(v => v.Title(), () => B.Appearance.MainScreen, v => B.Appearance.MainScreen = v));
        f.Row(screens, "Background colour", f.ColorField(() => B.Appearance.BackgroundColor, v => B.Appearance.BackgroundColor = v, opacity: true),
            "The colour behind the theme, and how see-through it is.");
        f.Row(screens, "Other screens", f.EnumPicker(v => v.Title(), () => B.Appearance.OtherScreens, v => B.Appearance.OtherScreens = v));
        var other = f.Row(screens, "Other screens colour", f.ColorField(() => B.Appearance.OtherScreensColor, v => B.Appearance.OtherScreensColor = v, opacity: true));
        f.VisibleWhen(other, () => B.Appearance.OtherScreens == OtherScreensMode.Dim);
        f.Row(screens, "Fade in over", f.NumberField(() => B.Controls.FadeInSeconds, v => B.Controls.FadeInSeconds = v, 0, 30, "seconds"));
        f.Row(screens, "Fade out over", f.NumberField(() => B.Controls.FadeOutSeconds, v => B.Controls.FadeOutSeconds = v, 0, 30, "seconds"));
        return f;
    }

    // MARK: - Postpone & Skip

    private Form ControlsTab()
    {
        var f = NewForm();
        var c = new Func<BreakControls>(() => B.Controls);
        var buttons = f.Section("Buttons");
        f.Full(buttons, f.ToggleLine(() => c().ShowPostpone1, v => c().ShowPostpone1 = v, "Lets you push the break back. There's a keyboard shortcut for this under Shortcuts.",
            Form.Text("Offer a postpone button for"), f.NumberField(() => c().PostponeMinutes1, v => c().PostponeMinutes1 = v, 1, 240, "minutes")));
        f.Full(buttons, f.ToggleLine(() => c().ShowPostpone2, v => c().ShowPostpone2 = v, null,
            Form.Text("Offer a second postpone button for"), f.NumberField(() => c().PostponeMinutes2, v => c().PostponeMinutes2 = v, 1, 240, "minutes")));
        f.Full(buttons, f.Toggle("Offer a skip button", () => c().ShowSkip, v => c().ShowSkip = v,
            "Lets you skip this break entirely. There's a keyboard shortcut for this under Shortcuts."));
        f.Full(buttons, f.ToggleLine(() => c().ShowEndEarly, v => c().ShowEndEarly = v, "For when you've rested enough before the time is up.",
            Form.Text("Offer an end-now button once"), f.IntervalField(() => c().EndEarlyAfter, v => c().EndEarlyAfter = v, [TimeUnit.Seconds, TimeUnit.Minutes]),
            Form.Text("have passed")));
        f.Full(buttons, f.Toggle("Stay on the break screen until I dismiss it", () => c().EndWhenDone, v => c().EndWhenDone = v,
            "When time is up, the screen stays until you click End break."));
        f.Full(buttons, f.Toggle("Show today's postpone, skip and completion counts", () => c().ShowCounters, v => c().ShowCounters = v));

        var limits = f.Section("Limits");
        f.Row(limits, "Lock the buttons for", f.NumberField(() => c().DisableButtonsSeconds, v => c().DisableButtonsSeconds = v, 0, 600, "seconds after start"),
            "Postpone and skip can't be used until this many seconds after the break begins (on top of the fade in).");
        f.Full(limits, f.ToggleLine(() => c().LimitPostpone, v => c().LimitPostpone = v, null,
            Form.Text("Allow at most"), f.IntField(() => c().PostponeLimit, v => c().PostponeLimit = v, 0, 100, "postpones per"),
            f.EnumPicker(v => v.Title(), () => c().PostponeLimitKind, v => c().PostponeLimitKind = v)));
        f.Full(limits, f.ToggleLine(() => c().LimitSkip, v => c().LimitSkip = v, null,
            Form.Text("Allow at most"), f.IntField(() => c().SkipLimit, v => c().SkipLimit = v, 0, 100, "skips per"),
            f.EnumPicker(v => v.Title(), () => c().SkipLimitKind, v => c().SkipLimitKind = v)));

        var today = f.Section("Today");
        var counts = new Func<BreakCounts>(() => B.State.Counts);
        f.Row(today, "Postponed", f.Value(() => $"{counts().TodayPostpone} today, {counts().HourPostpone} this hour, {counts().BreakPostpone} this break"));
        f.Row(today, "Skipped", f.Value(() => $"{counts().TodaySkip} today, {counts().HourSkip} this hour, {counts().BreakSkip} this break"));
        f.LiveHint(today, () => LimitText.Remaining(B) ?? "");
        f.Hint(today, "Limits apply everywhere: the break screen, the tray menu, keyboard shortcuts and recess:// links. Rules can still postpone or skip.");
        return f;
    }
}
