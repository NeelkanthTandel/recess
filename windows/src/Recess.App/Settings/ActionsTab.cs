using System.Windows;
using System.Windows.Controls;
using Recess.App.Actions;
using Recess.Core;

namespace Recess.App.Settings;

/// <summary>A break's actions: notifications, sounds, speech, screen flashes and scripts, before, during and after it.</summary>
internal sealed class ActionsTab
{
    private readonly SettingsContext ctx;
    private readonly Guid breakId;
    private readonly ListBox list = new() { MinWidth = 250 };
    private readonly ContentControl detail = new();
    private Form? form;
    private Guid? selected;
    private string listKey = "";

    public ActionsTab(SettingsContext ctx, Guid breakId)
    {
        this.ctx = ctx;
        this.breakId = breakId;
        var grid = new Grid { Margin = new Thickness(12) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(270) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var left = new DockPanel();
        var add = new Button { Content = "+", Width = 30, ToolTip = "Add something to happen around this break." };
        add.Click += (_, _) =>
        {
            var menu = new ContextMenu { PlacementTarget = add };
            foreach (var kind in Enum.GetValues<ActionKind>())
            {
                var item = new MenuItem { Header = kind.DisplayName() };
                item.Click += (_, _) => Add(kind);
                menu.Items.Add(item);
            }
            menu.IsOpen = true;
        };
        var remove = new Button { Content = "−", Width = 30, Margin = new Thickness(6, 0, 0, 0), ToolTip = "Remove the selected action." };
        remove.Click += (_, _) =>
        {
            if (selected is { } id)
            {
                B.Actions.RemoveAll(a => a.Id == id);
                selected = null;
                ctx.Model.MarkChanged();
                Refresh();
            }
        };
        var bar = Form.Line(add, remove);
        bar.Margin = new Thickness(0, 8, 0, 0);
        DockPanel.SetDock(bar, Dock.Bottom);
        left.Children.Add(bar);
        left.Children.Add(list);
        grid.Children.Add(left);
        var scroll = Form.Scroll(detail);
        Grid.SetColumn(scroll, 1);
        grid.Children.Add(scroll);
        View = grid;

        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is ListBoxItem { Tag: Guid id } && id != selected)
            {
                selected = id;
                ShowDetail();
            }
        };
        Refresh();
    }

    public UIElement View { get; }

    private Recess.Core.Break B => ctx.Model.Find(breakId) ?? new Recess.Core.Break();

    private BreakAction? Selected => B.Actions.Find(a => a.Id == selected);

    public void Refresh()
    {
        var key = string.Join('|', B.Actions.Select(a => $"{a.Id}{a.Stage}{a.Kind}{a.Summary}{a.Enabled}"));
        if (key != listKey)
        {
            listKey = key;
            list.Items.Clear();
            foreach (var stage in Enum.GetValues<ActionStage>())
            {
                var items = B.Actions.Where(a => a.Stage == stage).ToList();
                if (items.Count == 0)
                {
                    continue;
                }
                var title = stage.Title();
                list.Items.Add(new ListBoxItem
                {
                    Content = new TextBlock { Text = char.ToUpperInvariant(title[0]) + title[1..], FontWeight = FontWeights.SemiBold, Opacity = 0.6 },
                    IsEnabled = false,
                });
                foreach (var a in items)
                {
                    var text = new StackPanel();
                    text.Children.Add(new TextBlock { Text = a.Kind.DisplayName() + (a.Enabled ? "" : " (off)") });
                    text.Children.Add(new TextBlock { Text = a.Summary, FontSize = 12, Opacity = 0.65, TextTrimming = TextTrimming.CharacterEllipsis });
                    list.Items.Add(new ListBoxItem { Content = text, Tag = a.Id, Padding = new Thickness(8, 3, 8, 3) });
                }
            }
            if (B.Actions.Count == 0)
            {
                list.Items.Add(new ListBoxItem { Content = new TextBlock { Text = "No actions. Click + to add one.", Opacity = 0.6, TextWrapping = TextWrapping.Wrap }, IsEnabled = false });
            }
            list.SelectedItem = list.Items.OfType<ListBoxItem>().FirstOrDefault(i => i.Tag is Guid g && g == selected);
            if (Selected is null)
            {
                selected = null;
                ShowDetail();
            }
        }
        form?.Refresh();
    }

    private void Add(ActionKind kind)
    {
        var stage = kind switch
        {
            ActionKind.FadeSound => ActionStage.BeforeEnd,
            ActionKind.Notification => ActionStage.BeforeStart,
            _ => ActionStage.AtStart,
        };
        var a = new BreakAction(kind, stage, stage.UsesOffset() ? 30 : 0);
        B.Actions.Add(a);
        selected = a.Id;
        ctx.Model.MarkChanged();
        Refresh();
        ShowDetail();
    }

    private void ShowDetail()
    {
        if (Selected is not { } a)
        {
            form = null;
            detail.Content = new TextBlock
            {
                Text = "Select an action. Actions run before, during and after breaks: notifications, sounds, speech, screen flashes and scripts.",
                Opacity = 0.6,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(24),
            };
            return;
        }
        var id = a.Id;
        BreakAction A() => B.Actions.Find(x => x.Id == id) ?? a;
        var f = new Form(() =>
        {
            ctx.Model.MarkChanged();
            Refresh();
        });
        form = f;
        var s = f.Section();
        f.Full(s, f.Toggle("Enabled", () => A().Enabled, v => A().Enabled = v));
        f.Row(s, "When", f.EnumPicker(v => v.Title(), () => A().Stage, v => A().Stage = v));
        var offset = f.Row(s, "Seconds ahead", f.NumberField(() => A().Offset, v => A().Offset = v, 0, 3600));
        f.VisibleWhen(offset, () => A().Stage.UsesOffset());

        var k = f.Section(a.Kind.DisplayName());
        switch (a.Kind)
        {
            case ActionKind.Notification:
                f.Row(k, "Title", f.TextField(() => A().Title, v => A().Title = v));
                f.Row(k, "Message", f.TextField(() => A().Message, v => A().Message = v));
                f.Row(k, "Notifications", f.Value(() => Platform.Toasts.Enabled ? "On" : "Off for Recess: Recess shows its own banner instead"));
                f.Full(k, Form.Button("Open notification settings", () => Open("ms-settings:notifications")));
                break;
            case ActionKind.Sound:
                f.Row(k, "Sound", SoundPicker(f, A));
                f.Row(k, "Volume", f.SliderField(() => A().Volume, v => A().Volume = v, 0, 1));
                f.Full(k, f.Toggle("Loop until the break ends", () => A().Loops, v => A().Loops = v));
                var file = new TextBlock { Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
                f.OnRefresh(() => file.Text = A().SoundPath.Length == 0 ? "Or choose a file…" : Path.GetFileName(A().SoundPath));
                var clear = Form.Button("Clear", () =>
                {
                    A().SoundPath = "";
                    f.Changed();
                    f.Refresh();
                });
                f.VisibleWhen(clear, () => A().SoundPath.Length > 0);
                f.Full(k, Form.Line(file, Form.Button("Choose file…", () =>
                {
                    var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Sounds|*.wav;*.mp3;*.wma;*.m4a;*.aac;*.flac" };
                    if (dialog.ShowDialog() == true)
                    {
                        A().SoundPath = dialog.FileName;
                        f.Changed();
                        f.Refresh();
                    }
                }), clear));
                break;
            case ActionKind.FadeSound:
                f.Row(k, "Fade out over", f.NumberField(() => A().FadeSeconds, v => A().FadeSeconds = v, 0.5, 60, "seconds"));
                f.Hint(k, "Fades out any sound started by a Play sound action, for example a looping one.");
                break;
            case ActionKind.Speech:
                f.Row(k, "Message", f.TextField(() => A().Message, v => A().Message = v));
                f.Row(k, "Voice", f.Picker(new[] { ("", "Windows default") }.Concat(Library.Voices()), () => A().VoiceIdentifier, v => A().VoiceIdentifier = v, 240));
                f.Row(k, "Rate", f.SliderField(() => A().Rate, v => A().Rate = v, 0, 1));
                f.Row(k, "Pitch", f.SliderField(() => A().Pitch, v => A().Pitch = v, 0.5, 2));
                f.Row(k, "Volume", f.SliderField(() => A().Volume, v => A().Volume = v, 0, 1));
                break;
            case ActionKind.FlashScreen:
                f.Hint(k, "Briefly flashes every screen white.");
                break;
            case ActionKind.Script:
                f.Row(k, "Script", ScriptPicker(f, A));
                f.Full(k, Form.Line(Form.Button("Open scripts folder", () => Open(ctx.Store.ScriptsFolder))));
                f.Hint(k, "PowerShell (.ps1), batch (.bat, .cmd) and programs (.exe) in the Scripts folder. They can read RECESS_BREAK_NAME, RECESS_BREAK_ID, RECESS_BREAK_DURATION and RECESS_STAGE.");
                break;
        }
        f.Hint(k, "Use {break}, {duration} or {time} in messages.");
        var tryIt = f.Section();
        f.Full(tryIt, Form.Line(Form.Button("Try it", () => ctx.Actions.Perform(A(), B))));
        detail.Content = f.Root;
    }

    private ContentControl SoundPicker(Form f, Func<BreakAction> a)
    {
        var host = new ContentControl();
        void Build() => host.Content = f.Picker(Library.Sounds(ctx.Store).Select(s => (s.Name, s.Name)), () => a().SoundName, v => a().SoundName = v, 240);
        Build();
        return host;
    }

    private ComboBox ScriptPicker(Form f, Func<BreakAction> a) =>
        f.Picker(new[] { ("", "None") }.Concat(Library.Scripts(ctx.Store).Select(s => (s, s))), () => a().ScriptFilename, v => a().ScriptFilename = v, 240);

    private static void Open(string target) =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = true });
}
