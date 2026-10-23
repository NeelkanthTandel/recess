using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Recess.App.Actions;
using Recess.App.Platform;
using Recess.Core;

namespace Recess.App.Settings;

/// <summary>Rules change what happens when a break comes due, or whether time counts as being away. The first match wins.</summary>
internal sealed class RulesPage : ISettingsPage
{
    // Video-call apps on Windows, by program file name.
    private static readonly string[] CallApps = ["Zoom.exe", "ms-teams.exe", "Teams.exe", "CiscoCollabHost.exe", "webexmta.exe", "Discord.exe", "Slack.exe"];

    private readonly SettingsContext ctx;
    private readonly StackPanel list = new();
    private readonly List<Form> forms = [];
    private string key = "";

    public RulesPage(SettingsContext ctx)
    {
        this.ctx = ctx;
        var root = new StackPanel { Margin = new Thickness(24, 16, 24, 24) };
        root.Children.Add(new TextBlock { Text = "Rules", FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(4, 0, 0, 8) });
        root.Children.Add(new TextBlock
        {
            Text = "Rules change what happens when a break comes due, or whether time counts as being away. The first rule that matches is used; use the arrows to reorder.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.7,
            Margin = new Thickness(4, 0, 0, 12),
        });
        root.Children.Add(list);

        var add = new Button { Content = "Add rule", Padding = new Thickness(12, 4, 12, 4), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 12, 0, 0) };
        add.Click += (_, _) => PresetMenu(add).IsOpen = true;
        root.Children.Add(add);

        var scripts = new TextBlock
        {
            Text = "Script rules run a script from the Scripts folder every 20 seconds; it should print \"true\" when the rule should match. A sample that checks for a call is included.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.7,
            Margin = new Thickness(4, 20, 0, 6),
        };
        root.Children.Add(scripts);
        root.Children.Add(Form.Line(Form.Button("Open scripts folder", () =>
            System.Diagnostics.Process.Start("explorer.exe", ctx.Store.ScriptsFolder))));
        View = Form.Scroll(root);
        Refresh();
    }

    public UIElement View { get; }

    private List<Rule> Rules => ctx.Model.Settings.Rules;

    public void Refresh()
    {
        var k = string.Join('|', Rules.Select(r => $"{r.Id}{r.Condition}{r.Action}"));
        if (k != key)
        {
            key = k;
            Rebuild();
        }
        foreach (var f in forms)
        {
            f.Refresh();
        }
    }

    private void Changed()
    {
        ctx.Model.MarkChanged();
        Refresh();
    }

    private void Rebuild()
    {
        list.Children.Clear();
        forms.Clear();
        if (Rules.Count == 0)
        {
            list.Children.Add(new TextBlock
            {
                Text = "No rules. Add one to postpone breaks during calls, games, presentations and more.",
                Opacity = 0.6,
                Margin = new Thickness(4, 8, 0, 8),
            });
        }
        for (var i = 0; i < Rules.Count; i++)
        {
            list.Children.Add(Row(Rules[i], i));
        }
    }

    private UIElement Row(Rule rule, int index)
    {
        var f = new Form(Changed);
        forms.Add(f);
        Rule R() => Rules.Find(r => r.Id == rule.Id) ?? rule;
        // Each rule is its own card: built in a scratch panel, then moved into the list.
        var section = f.Section(parent: new StackPanel());
        var card = (Border)section.Parent;

        var enabled = new CheckBox { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        f.OnRefresh(() => enabled.IsChecked = R().Enabled);
        enabled.Click += (_, _) =>
        {
            R().Enabled = enabled.IsChecked == true;
            f.Changed();
        };
        var condition = f.EnumPicker(v => v.Title(), () => R().Condition, v => R().Condition = v);
        var negate = f.Picker([(false, "is active"), (true, "is not active")], () => R().Negate, v => R().Negate = v);
        f.OnRefresh(() =>
        {
            if (negate.Items.Count == 2)
            {
                negate.Items[0] = R().Condition == RuleCondition.Script ? "returns true" : "is active";
                negate.Items[1] = R().Condition == RuleCondition.Script ? "returns false" : "is not active";
            }
        });
        var action = f.EnumPicker(v => v.Title(), () => R().Action, v => R().Action = v);
        var up = Form.Button("↑", () => MoveRule(index, -1), "Move up (checked first)");
        var down = Form.Button("↓", () => MoveRule(index, 1), "Move down");
        var delete = Form.Button("Delete", () =>
        {
            Rules.RemoveAll(r => r.Id == rule.Id);
            Changed();
        });
        up.IsEnabled = index > 0;
        down.IsEnabled = index < Rules.Count - 1;
        var first = new WrapPanel();
        foreach (var e in new UIElement[] { enabled, Form.Text("When "), condition, negate, Form.Text(" then "), action })
        {
            if (e is FrameworkElement fe)
            {
                fe.Margin = new Thickness(0, 2, 6, 2);
                fe.VerticalAlignment = VerticalAlignment.Center;
            }
            first.Children.Add(e);
        }
        section.Children.Add(new DockPanel
        {
            Children =
            {
                WithDock(Form.Line(up, down, delete), Dock.Right),
                first,
            },
        });

        // Apps
        var apps = new WrapPanel { Margin = new Thickness(28, 4, 0, 4) };
        f.OnRefresh(() =>
        {
            apps.Children.Clear();
            foreach (var app in R().Apps)
            {
                var chip = new Border
                {
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(8, 2, 4, 2),
                    Margin = new Thickness(0, 2, 6, 2),
                    Background = new SolidColorBrush(Color.FromArgb(40, 128, 128, 128)),
                };
                var remove = new Button { Content = "✕", Padding = new Thickness(4, 0, 4, 0), Margin = new Thickness(4, 0, 0, 0), BorderThickness = new Thickness(0), Background = Brushes.Transparent };
                var name = app;
                remove.Click += (_, _) =>
                {
                    R().Apps.Remove(name);
                    f.Changed();
                };
                chip.Child = Form.Line(Form.Text(AppName(app)), remove);
                apps.Children.Add(chip);
            }
            var add = Form.Button("Add apps…", () =>
            {
                foreach (var chosen in AppPicker.Choose(Window.GetWindow(View)))
                {
                    if (!R().Apps.Contains(chosen, StringComparer.OrdinalIgnoreCase))
                    {
                        R().Apps.Add(chosen);
                    }
                }
                f.Changed();
            });
            apps.Children.Add(add);
        });
        f.VisibleWhen(apps, () => R().Condition.NeedsApps());
        section.Children.Add(apps);

        // Script
        var script = f.Picker(new[] { ("", "None") }.Concat(Library.Scripts(ctx.Store).Select(s => (s, s))), () => R().ScriptFilename, v => R().ScriptFilename = v, 220);
        var scriptLine = Form.Line(Form.Text("Script"), script);
        scriptLine.Margin = new Thickness(28, 4, 0, 4);
        f.VisibleWhen(scriptLine, () => R().Condition == RuleCondition.Script);
        section.Children.Add(scriptLine);

        // Calendar
        var calendars = ctx.Model.Settings.Calendars;
        var calendarLine = calendars.Count == 0
            ? Form.Line(new TextBlock { Text = "Add a calendar under General → Calendars first.", Opacity = 0.65 })
            : Form.Line(
                f.Picker(new (string?, string)[] { (null, "All calendars") }.Concat(calendars.Select(c => ((string?)c.Id, c.Name))),
                    () => R().CalendarIdentifier, v => R().CalendarIdentifier = v),
                Form.Text("matching"),
                f.TextField(() => R().CalendarFilter, v => R().CalendarFilter = v, 180, "any event"));
        calendarLine.Margin = new Thickness(28, 4, 0, 4);
        f.VisibleWhen(calendarLine, () => R().Condition == RuleCondition.CalendarEvent);
        section.Children.Add(calendarLine);

        // Explanations for conditions that work differently on Windows.
        var note = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0.65, FontSize = 12, Margin = new Thickness(28, 2, 0, 4) };
        f.OnRefresh(() =>
        {
            note.Text = R().Condition switch
            {
                RuleCondition.Focus => "Windows doesn't let apps read Do not disturb, so this condition never matches. Try Microphone or camera in use, or Fullscreen app.",
                RuleCondition.MicOrCamera => "Matches while any app uses the microphone or camera, which usually means a call. Best effort: it reads what Windows records for its privacy indicator.",
                RuleCondition.Screensaver => "Matches while the screensaver runs or the screen is locked.",
                _ => "",
            };
            note.Visibility = note.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        });
        section.Children.Add(note);

        // Which breaks
        var applies = new Button { Padding = new Thickness(10, 2, 10, 2) };
        f.OnRefresh(() => applies.Content = R().BreakIds.Count == 0
            ? "All breaks"
            : string.Join(", ", ctx.Model.Breaks.Where(b => R().BreakIds.Contains(b.Id)).Select(b => b.Name)));
        applies.Click += (_, _) =>
        {
            var menu = new ContextMenu { PlacementTarget = applies };
            var all = new MenuItem { Header = "All breaks", IsCheckable = true, IsChecked = R().BreakIds.Count == 0 };
            all.Click += (_, _) =>
            {
                R().BreakIds.Clear();
                f.Changed();
            };
            menu.Items.Add(all);
            menu.Items.Add(new Separator());
            foreach (var b in ctx.Model.Breaks)
            {
                var id = b.Id;
                var item = new MenuItem { Header = b.Name, IsCheckable = true, IsChecked = R().BreakIds.Contains(id) };
                item.Click += (_, _) =>
                {
                    if (!R().BreakIds.Remove(id))
                    {
                        R().BreakIds.Add(id);
                    }
                    f.Changed();
                };
                menu.Items.Add(item);
            }
            menu.IsOpen = true;
        };
        var appliesLine = Form.Line(new TextBlock { Text = "Applies to", Opacity = 0.65 }, applies);
        appliesLine.Margin = new Thickness(28, 4, 0, 4);
        f.VisibleWhen(appliesLine, () => R().Action.IsDueAction());
        section.Children.Add(appliesLine);

        f.OnRefresh(() => card.Opacity = R().Enabled ? 1 : 0.55);
        card.Margin = new Thickness(0, 0, 0, 10);
        var holder = (Panel)card.Parent;
        holder.Children.Remove(card);
        return card;
    }

    private static T WithDock<T>(T element, Dock dock)
        where T : UIElement
    {
        DockPanel.SetDock(element, dock);
        return element;
    }

    private void MoveRule(int index, int by)
    {
        var to = index + by;
        if (to < 0 || to >= Rules.Count)
        {
            return;
        }
        (Rules[index], Rules[to]) = (Rules[to], Rules[index]);
        key = "";
        Changed();
    }

    private static string AppName(string exe)
    {
        var running = System.Diagnostics.Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exe));
        try
        {
            foreach (var p in running)
            {
                if (Processes.ImagePath((uint)p.Id) is { } path)
                {
                    return Processes.DisplayName(path);
                }
            }
        }
        finally
        {
            foreach (var p in running)
            {
                p.Dispose();
            }
        }
        return Path.GetFileNameWithoutExtension(exe);
    }

    private ContextMenu PresetMenu(UIElement target)
    {
        var menu = new ContextMenu { PlacementTarget = target };
        void Add(string title, Func<Rule> make)
        {
            var item = new MenuItem { Header = title };
            item.Click += (_, _) =>
            {
                Rules.Add(make());
                Changed();
            };
            menu.Items.Add(item);
        }
        Add("Postpone during a call (microphone or camera in use)", () => new Rule { Condition = RuleCondition.MicOrCamera, Action = RuleAction.Postpone5 });
        Add("Postpone while a video-call app is in front (Zoom, Teams, Webex…)", () => new Rule { Condition = RuleCondition.FrontApp, Apps = [.. CallApps], Action = RuleAction.Postpone5 });
        Add("Postpone while a fullscreen app is active", () => new Rule { Condition = RuleCondition.Fullscreen, Action = RuleAction.Postpone5 });
        Add("Skip while presenting in PowerPoint", () => new Rule { Condition = RuleCondition.FrontApp, Apps = ["POWERPNT.EXE"], Action = RuleAction.Skip });
        Add("Treat the screensaver or lock screen as a natural break", () => new Rule { Condition = RuleCondition.Screensaver, Action = RuleAction.TreatAsNaturalBreak });
        Add("Postpone during busy calendar events", () => new Rule { Condition = RuleCondition.CalendarEvent, Action = RuleAction.Postpone10 });
        Add("Don't count watching a video as time away (VLC, Media Player)", () => new Rule
        {
            Condition = RuleCondition.FrontApp,
            Apps = ["vlc.exe", "Microsoft.Media.Player.exe", "mpc-hc64.exe"],
            Action = RuleAction.IgnoreNaturalBreak,
        });
        menu.Items.Add(new Separator());
        Add("Custom rule", () => new Rule());
        return menu;
    }
}

/// <summary>Pick apps for a rule: from what's running, or any program file.</summary>
internal sealed class AppPicker : Window
{
    private readonly ListBox list = new() { SelectionMode = SelectionMode.Multiple, Height = 320 };
    private readonly List<string> chosen = [];

    private AppPicker()
    {
        Title = "Add apps";
        Width = 440;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(new TextBlock { Text = "Apps with windows open now:", Margin = new Thickness(0, 0, 0, 6) });
        var apps = System.Diagnostics.Process.GetProcesses()
            .Where(p =>
            {
                try
                {
                    return p.MainWindowHandle != IntPtr.Zero && p.Id != Environment.ProcessId;
                }
                catch (InvalidOperationException)
                {
                    return false;
                }
            })
            .Select(p => (Exe: p.ProcessName + ".exe", Path: Processes.ImagePath((uint)p.Id)))
            .DistinctBy(a => a.Exe, StringComparer.OrdinalIgnoreCase)
            .Select(a => (a.Exe, Name: a.Path is { } path ? Processes.DisplayName(path) : a.Exe))
            .OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase);
        foreach (var (exe, name) in apps)
        {
            list.Items.Add(new ListBoxItem { Content = $"{name}  ({exe})", Tag = exe });
        }
        root.Children.Add(list);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var browse = Form.Button("Choose a program…", () =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Programs|*.exe", Multiselect = true };
            if (dialog.ShowDialog(this) == true)
            {
                chosen.AddRange(dialog.FileNames.Select(Path.GetFileName).OfType<string>());
                DialogResult = true;
            }
        });
        var cancel = new Button { Content = "Cancel", IsCancel = true, Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 8, 0) };
        var add = new Button { Content = "Add", IsDefault = true, Padding = new Thickness(12, 4, 12, 4) };
        add.Click += (_, _) =>
        {
            chosen.AddRange(list.SelectedItems.OfType<ListBoxItem>().Select(i => (string)i.Tag));
            DialogResult = true;
        };
        buttons.Children.Add(browse);
        buttons.Children.Add(cancel);
        buttons.Children.Add(add);
        root.Children.Add(buttons);
        Content = root;
    }

    public static List<string> Choose(Window? owner)
    {
        var picker = new AppPicker { Owner = owner };
        return picker.ShowDialog() == true ? picker.chosen : [];
    }
}
