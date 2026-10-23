using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using Recess.App.Platform;
using Recess.Core;

namespace Recess.App.Settings;

/// <summary>The first-run assistant: what Recess does, which breaks to have, how they start, and starting at sign-in.</summary>
internal sealed class SetupAssistant : Window
{
    private static SetupAssistant? current;

    private readonly SettingsContext ctx;
    private readonly ContentControl page = new();
    private readonly Button back = new() { Content = "Back", Padding = new Thickness(14, 4, 14, 4), Margin = new Thickness(0, 0, 8, 0) };
    private readonly Button next = new() { Content = "Continue", IsDefault = true, Padding = new Thickness(14, 4, 14, 4) };
    private readonly Form form;
    private int step;

    private bool includeNormal = true;
    private Interval normalDuration = Interval.FromMinutes(10);
    private Interval normalFrequency = Interval.FromMinutes(60);
    private bool includeMicro = true;
    private Interval microDuration = Interval.FromSeconds(15);
    private Interval microFrequency = Interval.FromMinutes(15);
    private DueBehavior dueBehavior = DueBehavior.StartImmediately;
    private bool launchAtLogin = true;

    private SetupAssistant(SettingsContext ctx)
    {
        this.ctx = ctx;
        Title = "Welcome to Recess";
        Width = 600;
        Height = 480;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        if (IconRenderer.LoadImage(Path.Combine(AppContext.BaseDirectory, "Assets", "icon.png")) is { } icon)
        {
            Icon = icon;
        }
        form = new Form(() => { });
        if (ctx.Model.FindByName("Normal") is { } n)
        {
            normalDuration = n.Schedule.Duration;
            normalFrequency = n.Schedule.Frequency;
            dueBehavior = n.Schedule.DueBehavior;
        }
        if (ctx.Model.FindByName("Micro") is { } m)
        {
            microDuration = m.Schedule.Duration;
            microFrequency = m.Schedule.Frequency;
        }

        var buttons = new DockPanel { Margin = new Thickness(28, 0, 28, 24), LastChildFill = false };
        DockPanel.SetDock(next, Dock.Right);
        DockPanel.SetDock(back, Dock.Left);
        buttons.Children.Add(next);
        buttons.Children.Add(back);
        back.Click += (_, _) => Go(step - 1);
        next.Click += (_, _) =>
        {
            if (step == 3)
            {
                Apply();
                Close();
            }
            else
            {
                Go(step + 1);
            }
        };
        var root = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(page);
        Content = root;
        Closed += (_, _) =>
        {
            ctx.Model.Settings.HasCompletedSetup = true;
            ctx.Model.MarkChanged();
            current = null;
        };
        Go(0);
    }

    public static void Open(SettingsContext ctx)
    {
        current ??= new SetupAssistant(ctx);
        current.Show();
        current.Activate();
    }

    private void Go(int to)
    {
        step = Math.Clamp(to, 0, 3);
        back.Visibility = step > 0 ? Visibility.Visible : Visibility.Hidden;
        next.Content = step == 3 ? "Done" : "Continue";
        var body = new StackPanel { Margin = new Thickness(28, 24, 28, 12) };
        switch (step)
        {
            case 0:
                Welcome(body);
                break;
            case 1:
                Breaks(body);
                break;
            case 2:
                Starting(body);
                break;
            default:
                Finish(body);
                break;
        }
        page.Content = body;
        form.Refresh();
    }

    private static TextBlock Heading(string text) => new() { Text = text, FontSize = 26, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 14) };

    private static TextBlock Para(string text, double opacity = 1) =>
        new() { Text = text, TextWrapping = TextWrapping.Wrap, Opacity = opacity, Margin = new Thickness(0, 0, 0, 12), FontSize = 14 };

    private static void Welcome(StackPanel body)
    {
        if (IconRenderer.LoadImage(Path.Combine(AppContext.BaseDirectory, "Assets", "icon.png")) is { } icon)
        {
            body.Children.Add(new Image { Source = icon, Width = 56, Height = 56, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 12) });
        }
        body.Children.Add(Heading("Welcome to Recess"));
        body.Children.Add(Para("Recess reminds you to take regular breaks from the computer. It gently dims the screen when a break is due; you can postpone or skip if you're in the middle of something."));
        var kinds = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12), FontSize = 14 };
        kinds.Inlines.Add("There are two kinds of break to start with: a ");
        kinds.Inlines.Add(new Bold(new Run("Normal")));
        kinds.Inlines.Add(" break for a few minutes every hour or so, and very brief, frequent ");
        kinds.Inlines.Add(new Bold(new Run("Micro")));
        kinds.Inlines.Add(" breaks to relax your eyes and muscles. You can add as many breaks as you like later.");
        body.Children.Add(kinds);
        body.Children.Add(Para("If you step away from the computer, Recess notices the natural break and resets the timers.", 0.7));
    }

    private void Breaks(StackPanel body)
    {
        body.Children.Add(Heading("Choose your breaks"));
        void Kind(string name, Func<bool> include, Action<bool> setInclude, Func<Interval> duration, Action<Interval> setDuration, Func<Interval> frequency,
            Action<Interval> setFrequency)
        {
            var s = form.Section(parent: body);
            s.Children.Add(form.Toggle(name + " break", include, setInclude));
            var line = Form.Line(Form.Text("Break for"), form.IntervalField(duration, setDuration, minimum: 1), Form.Text("every"),
                form.IntervalField(frequency, setFrequency, [TimeUnit.Minutes, TimeUnit.Hours], 10));
            form.EnabledWhen(line, include);
            s.Children.Add(line);
            ((FrameworkElement)s.Parent).Margin = new Thickness(0, 0, 0, 12);
        }
        Kind("Normal", () => includeNormal, v => includeNormal = v, () => normalDuration, v => normalDuration = v, () => normalFrequency, v => normalFrequency = v);
        Kind("Micro", () => includeMicro, v => includeMicro = v, () => microDuration, v => microDuration = v, () => microFrequency, v => microFrequency = v);
    }

    /// <summary>An explicit choice, so nobody is surprised by a break that waits (or one that doesn't).</summary>
    private void Starting(StackPanel body)
    {
        body.Children.Add(Heading("When a break is due"));
        body.Children.Add(Para("Choose how a break begins. You can change this for each break later, on its Schedule tab.", 0.7));
        var explanation = Para("");
        foreach (var behavior in Enum.GetValues<DueBehavior>())
        {
            var radio = new RadioButton { Content = behavior.Title(), GroupName = "due", IsChecked = behavior == dueBehavior, Margin = new Thickness(0, 4, 0, 4), FontSize = 14 };
            var b = behavior;
            radio.Checked += (_, _) =>
            {
                dueBehavior = b;
                explanation.Text = b.Explanation();
            };
            body.Children.Add(radio);
        }
        explanation.Text = dueBehavior.Explanation();
        explanation.Margin = new Thickness(0, 12, 0, 0);
        body.Children.Add(new Border
        {
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(8),
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(24, 128, 128, 128)),
            Child = explanation,
            Margin = new Thickness(0, 12, 0, 0),
        });
    }

    private void Finish(StackPanel body)
    {
        body.Children.Add(Heading("Almost done"));
        body.Children.Add(form.Toggle("Start Recess automatically when you sign in", () => launchAtLogin, v => launchAtLogin = v));
        body.Children.Add(Para("Recess lives in the notification area by the clock. Click its icon to see when breaks are due, start one now, or pause them; hold Shift for extra commands."));
        body.Children.Add(Para("Windows may tuck the icon under the ^ arrow at first. To keep it in view, turn Recess on under Settings › Personalization › Taskbar › Other system tray icons.", 0.7));
        var preview = Form.Button("Preview a break", () =>
        {
            // The shortest break makes the quickest preview; it isn't counted.
            if (ctx.Model.Breaks.MinBy(b => b.Duration) is { } b)
            {
                ctx.Scheduler.StartBreak(b.Id, manual: true, preview: true);
            }
        });
        preview.HorizontalAlignment = HorizontalAlignment.Left;
        body.Children.Add(preview);
    }

    private void Apply()
    {
        var now = ctx.Clock.UtcNow;
        void Upsert(string name, bool include, Interval duration, Interval frequency, Func<Recess.Core.Break> make)
        {
            if (ctx.Model.FindByName(name) is { } b)
            {
                b.Enabled = include;
                b.Schedule.Duration = duration;
                b.Schedule.Frequency = frequency;
                b.Schedule.DueBehavior = dueBehavior;
                ctx.Scheduler.ResetBreak(b.Id);
            }
            else if (include)
            {
                var created = make();
                created.Schedule.Duration = duration;
                created.Schedule.Frequency = frequency;
                created.Schedule.DueBehavior = dueBehavior;
                ctx.Scheduler.Reschedule(created, now, DueDates.Reason.Reset);
                ctx.Model.Breaks.Add(created);
            }
        }
        Upsert("Normal", includeNormal, normalDuration, normalFrequency, Recess.Core.Break.MakeNormal);
        Upsert("Micro", includeMicro, microDuration, microFrequency, Recess.Core.Break.MakeMicro);
        try
        {
            Registration.LaunchAtLogin = launchAtLogin;
            ctx.Model.Settings.LaunchAtLogin = launchAtLogin;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            // Starting at sign-in can be turned on later in Settings.
        }
        ctx.Model.Settings.HasCompletedSetup = true;
        ctx.Model.MarkChanged();
    }
}
