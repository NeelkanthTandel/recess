using System.Windows;
using System.Windows.Controls;
using Recess.App.Platform;
using Recess.Core;

namespace Recess.App.Settings;

/// <summary>A page built from one form.</summary>
internal abstract class FormPage : ISettingsPage
{
    protected FormPage(SettingsContext ctx, string title)
    {
        Ctx = ctx;
        F = new Form(Saved);
        F.Root.Children.Add(new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(4, 0, 0, 12) });
    }

    protected SettingsContext Ctx { get; }

    protected Form F { get; }

    protected AppSettings S => Ctx.Model.Settings;

    public UIElement View => Form.Scroll(F.Root);

    public virtual void Refresh() => F.Refresh();

    protected virtual void Saved() => Ctx.Model.MarkChanged();

    protected static void Open(string target) =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = true });
}

internal sealed class GeneralPage : FormPage
{
    public GeneralPage(SettingsContext ctx)
        : base(ctx, "General")
    {
        var start = F.Section();
        F.Full(start, F.Toggle("Start Recess when I sign in to Windows", () => Registration.LaunchAtLogin, v =>
        {
            Registration.LaunchAtLogin = v;
            S.LaunchAtLogin = v;
        }, "So breaks keep happening after a restart."));
        F.LiveHint(start, () => Registration.LaunchAtLogin && Registration.LaunchAtLoginBlockedByUser
            ? "Recess is turned off under Startup apps in Windows Settings, which wins over this. Turn it on there."
            : "");

        var tray = F.Section("Notification area");
        F.Full(tray, F.Toggle("Show Recess in the notification area", () => S.ShowStatusMenu, v =>
        {
            S.ShowStatusMenu = v;
            ctx.PresentationChanged();
        }, "The icon shows when your next break is, and its menu has every break command."));
        F.LiveHint(tray, () => S.ShowStatusMenu ? "" : "With no icon, open Recess again from the Start menu to get back to these settings.");
        F.Row(tray, "Icon", F.EnumPicker(v => v.Title(), () => S.StatusIcon, v => S.StatusIcon = v,
            Enum.GetValues<StatusIcon>().Where(i => i != StatusIcon.None).OrderBy(i => i == StatusIcon.Countdown ? 0 : 1)),
            "The picture in the notification area.");
        F.Row(tray, "Tooltip shows", F.EnumPicker(v => v.Title(), () => S.StatusTitle, v => S.StatusTitle = v),
            "What the icon's tooltip leads with when you point at it.");
        F.Full(tray, F.Toggle("Ignore breaks shorter than a minute", () => S.StatusOnlyLong, v => S.StatusOnlyLong = v,
            "Keeps short micro-breaks out of the icon's countdown."));
        F.Hint(tray, "Windows tucks new icons under the ^ arrow. To keep Recess in view, turn it on under Settings › Personalization › Taskbar › Other system tray icons.");
        F.Full(tray, Form.Line(Form.Button("Open taskbar settings", () => Open("ms-settings:taskbar"))));

        var dates = F.Section("Dates");
        F.Row(dates, "Show break times as", F.Picker([(true, "Time from now (in 20 minutes)"), (false, "Clock time (at 15:40)")],
            () => S.RelativeDates, v => S.RelativeDates = v));

        CalendarSection.Add(F, ctx);

        var data = F.Section("Data");
        F.Row(data, "Your settings and activity", Form.Line(
            new TextBlock { Text = ctx.Store.Folder, Opacity = 0.65, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) },
            Form.Button("Open folder", () => System.Diagnostics.Process.Start("explorer.exe", ctx.Store.Folder))));
        F.Row(data, "Notifications", Form.Line(F.Value(() => Toasts.Enabled ? "On" : "Off for Recess (it shows its own banner)"),
            Form.Button("Open notification settings", () => Open("ms-settings:notifications"))));
        F.Full(data, Form.Line(Form.Button("Setup assistant…", ctx.OpenSetupAssistant)));
    }
}

internal sealed class ShortcutsPage : FormPage
{
    public ShortcutsPage(SettingsContext ctx)
        : base(ctx, "Shortcuts")
    {
        var all = F.Section();
        F.Hint(all, "These shortcuts work in every app. A shortcut needs Ctrl, Alt or the Windows key, or a function key on its own.");
        Row(all, "Start next break", () => S.Shortcuts.StartNext, v => S.Shortcuts.StartNext = v);
        Row(all, "Postpone next break (first button)", () => S.Shortcuts.Postpone1, v => S.Shortcuts.Postpone1 = v);
        Row(all, "Postpone next break (second button)", () => S.Shortcuts.Postpone2, v => S.Shortcuts.Postpone2 = v);
        Row(all, "Skip current or next break", () => S.Shortcuts.Skip, v => S.Shortcuts.Skip = v);
        Row(all, "Pause everything", () => S.Shortcuts.PauseIndefinitely, v => S.Shortcuts.PauseIndefinitely = v);
        Row(all, "Resume all breaks", () => S.Shortcuts.Resume, v => S.Shortcuts.Resume = v);
        Row(all, "Reset all breaks", () => S.Shortcuts.Reset, v => S.Shortcuts.Reset = v);
        Row(all, "Stop sound", () => S.Shortcuts.StopSound, v => S.Shortcuts.StopSound = v);

        var each = F.Section("Start a specific break");
        foreach (var b in ctx.Model.Breaks)
        {
            var id = b.Id;
            Row(each, $"Start {b.Name} break", () => ctx.Model.Find(id)?.StartShortcut, v =>
            {
                if (ctx.Model.Find(id) is { } found)
                {
                    found.StartShortcut = v;
                }
            });
        }
    }

    private void Row(StackPanel section, string title, Func<KeyCombo?> get, Action<KeyCombo?> set) =>
        F.Row(section, title, new ShortcutRecorder(Ctx.HotKeys, get, v =>
        {
            set(v);
            Ctx.Model.MarkChanged();
            Ctx.ShortcutsChanged();
        }));
}

internal sealed class AdvancedPage : FormPage
{
    private static readonly (double, string)[] Thresholds =
    [
        (30, "30 seconds"), (60, "1 minute"), (120, "2 minutes"), (180, "3 minutes"), (300, "5 minutes"), (600, "10 minutes"),
        (900, "15 minutes"), (1200, "20 minutes"), (1800, "30 minutes"), (2700, "45 minutes"), (3600, "1 hour"),
    ];

    public AdvancedPage(SettingsContext ctx)
        : base(ctx, "Advanced")
    {
        var away = F.Section("Time away");
        F.Row(away, "Notice when I'm away", F.Picker([(IdleDetector.EventSource, "Yes, from keyboard and mouse use"), (IdleDetector.None, "No")],
            () => S.IdleDetector == IdleDetector.None ? IdleDetector.None : IdleDetector.EventSource, v => S.IdleDetector = v));
        F.Hint(away, "Recess uses the time since your last keyboard or mouse input, which Windows keeps anyway; nothing is recorded. Choose No to turn off time-away detection.");
        F.Row(away, "Count as away after", F.Picker(Thresholds, () => S.NaturalBreakThreshold, v => S.NaturalBreakThreshold = v));
        F.Hint(away, "No keyboard or mouse input for this long counts as a natural break.");
        F.Row(away, "Currently idle for", F.Value(() => Format.Full(ctx.Scheduler.IdleSeconds) + (ctx.Scheduler.IsNaturalBreak ? " (natural break)" : "")));

        var temptations = F.Section("Remove temptations");
        F.Full(temptations, F.Toggle("Hide Pause", () => S.HidePause, v => S.HidePause = v));
        F.Full(temptations, F.Toggle("Hide Make due", () => S.HideMakeDue, v => S.HideMakeDue = v));
        F.Full(temptations, F.Toggle("Hide Reset", () => S.HideReset, v => S.HideReset = v));
        F.Full(temptations, F.Toggle("Hide Disable", () => S.HideDisable, v => S.HideDisable = v));
        F.Full(temptations, F.Toggle("Hide Delete", () => S.HideDelete, v => S.HideDelete = v));
        F.Full(temptations, F.Toggle("Hide Quit", () => S.HideQuit, v => S.HideQuit = v));
        F.Hint(temptations, "Hidden commands are still there if you hold Shift while opening the tray icon's menu.");
        F.Full(temptations, F.Toggle("Lock the computer into the break (no Alt+Tab, Windows key or Alt+F4)", () => S.DisableAppSwitching,
            v => S.DisableAppSwitching = v));
        F.Hint(temptations, "During a break, switching apps and the Start menu are blocked and Recess can't be quit. Ctrl+Alt+Del and Win+L always work, and shutting down is never blocked.");
        F.Full(temptations, F.Toggle("Keep the taskbar visible during breaks", () => S.ShowMenuBarDuringBreak, v => S.ShowMenuBarDuringBreak = v));

        var links = F.Section("Links");
        F.Full(links, F.Toggle("Accept recess:// links from other apps and web pages", () => S.AllowUrlCommands, v => S.AllowUrlCommands = v));
        F.Hint(links, "Control Recess from scripts, shortcuts or a browser: recess://start, recess://start?break=Micro, recess://postpone?minutes=5, recess://skip, recess://makedue?break=Normal&seconds=30, recess://pause?minutes=30, recess://resume, recess://reset, recess://settings");
    }
}
