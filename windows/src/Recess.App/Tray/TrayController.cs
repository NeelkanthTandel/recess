using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Recess.App.Platform;
using Recess.Core;
using Windows.Win32;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Recess.App.Tray;

/// <summary>The notification-area icon and its menu: Recess's counterpart to the Mac menu bar item.</summary>
internal sealed class TrayController : IDisposable
{
    private readonly RecessModel model;
    private readonly Scheduler scheduler;
    private readonly IClock clock;
    private readonly AppCommands commands;
    private TrayIcon? icon;
    private HICON current;
    private string lastKey = "";
    private bool extended;

    public TrayController(RecessModel model, Scheduler scheduler, IClock clock, AppCommands commands)
    {
        this.model = model;
        this.scheduler = scheduler;
        this.clock = clock;
        this.commands = commands;
    }

    /// <summary>Shows or hides the icon (Settings → General → Show in the notification area).</summary>
    public void SetVisible(bool visible)
    {
        if (visible && icon is null)
        {
            icon = new TrayIcon("Recess");
            icon.MenuOpening += BuildMenu;
            icon.Activated += () => icon.ShowMenu();
            lastKey = "";
            Refresh();
        }
        else if (!visible && icon is not null)
        {
            icon.Dispose();
            icon = null;
        }
    }

    /// <summary>Redraws the icon and tooltip; called every second. The icon is only rebuilt when it changes.</summary>
    public void Refresh()
    {
        if (icon is null)
        {
            return;
        }
        var s = model.Settings;
        var now = scheduler.Now;
        var next = model.NextBreak(now);
        var last = model.LastDoneBreak;
        var dpi = PInvoke.GetDpiForSystem();
        var size = IconRenderer.SmallIconSize(dpi);
        var light = TrayGlyphs.LightTaskbar;
        var ink = TrayGlyphs.Ink;

        string key;
        Action<DrawingContext, double> draw;
        if (model.AllPaused(now) && scheduler.Session is null)
        {
            key = "paused";
            draw = (dc, px) => TrayGlyphs.DrawPaused(dc, px, ink);
        }
        else if (s.StatusIcon == StatusIcon.NextDueLabel && next is { LabelAsPie: true })
        {
            var c = next.State.Counts;
            key = $"pie{next.Id}{c.TodayPostpone}/{c.TodaySkip}/{c.TodayDone}{next.Color}";
            draw = (dc, px) => TrayGlyphs.DrawPie(dc, px, next);
        }
        else
        {
            switch (s.StatusIcon)
            {
                case StatusIcon.Countdown:
                    var text = StatusText.IconText(model, scheduler);
                    key = "text" + text;
                    draw = (dc, px) => TrayGlyphs.DrawText(dc, px, text, ink, dpi / 96.0);
                    break;
                case StatusIcon.LastDoneLabel or StatusIcon.NextDueLabel:
                    var color = (s.StatusIcon == StatusIcon.LastDoneLabel ? last : next)?.Color is { } c2 ? TrayGlyphs.ToColor(c2) : Colors.Gray;
                    key = "dot" + color;
                    draw = (dc, px) => TrayGlyphs.DrawDot(dc, px, color);
                    break;
                case StatusIcon.AppIcon or StatusIcon.None:
                    // A tray icon can't be empty, so "no icon" shows the app icon too.
                    key = "app";
                    var image = IconRenderer.LoadImage(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Recess.ico"));
                    draw = (dc, px) =>
                    {
                        if (image is not null)
                        {
                            dc.DrawImage(image, new Rect(0, 0, px, px));
                        }
                    };
                    break;
                default:
                    var glyph = s.StatusIcon;
                    key = "glyph" + glyph;
                    draw = (dc, px) => TrayGlyphs.DrawGlyph(dc, px, glyph, ink);
                    break;
            }
        }
        key += $"|{size}|{light}";

        if (key != lastKey)
        {
            var old = current;
            current = IconRenderer.Render(size, draw);
            icon.Show(current);
            IconRenderer.Destroy(old);
            lastKey = key;
        }
        icon.SetTooltip(StatusText.Tooltip(model, scheduler, clock.Zone));
    }

    // MARK: - Menu

    private void BuildMenu(ContextMenu menu, bool shift)
    {
        extended = shift;
        var s = model.Settings;
        var now = scheduler.Now;

        if (scheduler.Session is { } session)
        {
            menu.Items.Add(Header($"{session.Break.Name} break: {Format.Countdown(session.Remaining)} left"));
            menu.Items.Add(Limited(Item($"Postpone {Format.Int(session.Break.Controls.PostponeMinutes1)} minutes", () => session.Postpone(1)),
                session.CanPostpone));
            menu.Items.Add(Limited(Item("Skip this break", session.Skip), session.CanSkip));
            menu.Items.Add(new Separator());
        }

        if (model.Breaks.Count == 0)
        {
            menu.Items.Add(Header("No breaks"));
        }
        foreach (var b in model.Breaks)
        {
            var item = new MenuItem
            {
                Header = BreakHeader(b),
                Icon = new Ellipse { Width = 10, Height = 10, Fill = new SolidColorBrush(b.Enabled ? TrayGlyphs.ToColor(b.Color) : Colors.Gray) },
            };
            AddBreakSubmenu(item, b);
            menu.Items.Add(item);
        }

        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Start next break", scheduler.StartNextBreak));
        if (model.NextBreakAnyLength(now) is { } next)
        {
            menu.Items.Add(Limited(Item($"Postpone {next.Name} {Format.Int(next.Controls.PostponeMinutes1)} minutes", () => scheduler.PostponeNext(1)),
                next.CanPostpone));
            menu.Items.Add(Limited(Item($"Skip {next.Name} break", scheduler.SkipNext), next.CanSkip));
        }
        menu.Items.Add(new Separator());
        if (!s.HidePause || extended)
        {
            var pause = new MenuItem { Header = "Pause all breaks" };
            AddPauseChoices(pause, null);
            menu.Items.Add(pause);
        }
        if (model.Breaks.Any(b => b.State.PausedUntil is not null))
        {
            menu.Items.Add(Item("Resume all breaks", scheduler.ResumeAll));
        }
        if (!s.HideReset || extended)
        {
            menu.Items.Add(Item("Reset all breaks", scheduler.ResetAll));
        }
        if (commands.IsSoundPlaying())
        {
            menu.Items.Add(Item("Stop sound", commands.StopSound));
        }
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Settings…", () => commands.OpenSettings(null, null)));
        if (!s.HideQuit || extended)
        {
            menu.Items.Add(Item("Quit Recess", commands.Quit));
        }
    }

    private UIElement BreakHeader(Break b)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new TextBlock { Text = b.Name });
        panel.Children.Add(new TextBlock
        {
            Text = "   " + StatusText.BreakStatus(b, model, scheduler, clock.Zone),
            Opacity = 0.6,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
        });
        return panel;
    }

    private void AddBreakSubmenu(MenuItem parent, Break b)
    {
        var s = model.Settings;
        var now = scheduler.Now;
        parent.Items.Add(Header($"Last done: {model.DateString(b.State.LastDone, now, clock.Zone)}"));
        parent.Items.Add(Header($"Status: {b.State.Status}"));
        parent.Items.Add(new Separator());
        parent.Items.Add(Item($"Start {b.Name} break", () => scheduler.StartBreak(b.Id, manual: true)));
        parent.Items.Add(Limited(Item($"Postpone {Format.Int(b.Controls.PostponeMinutes1)} minutes",
            () => scheduler.Postpone(b.Id, b.Controls.PostponeMinutes1)), b.CanPostpone));
        parent.Items.Add(Limited(Item($"Postpone {Format.Int(b.Controls.PostponeMinutes2)} minutes",
            () => scheduler.Postpone(b.Id, b.Controls.PostponeMinutes2)), b.CanPostpone));
        parent.Items.Add(Limited(Item("Skip break", () => scheduler.Skip(b.Id)), b.CanSkip));
        if (LimitText.Remaining(b) is { } limits)
        {
            parent.Items.Add(Header(limits));
        }
        if (!s.HideMakeDue || extended)
        {
            var due = new MenuItem { Header = "Make due in" };
            foreach (var (title, seconds) in Settings.Choices.MakeDue)
            {
                due.Items.Add(Item(title, () => scheduler.MakeDue(b.Id, seconds)));
            }
            parent.Items.Add(due);
        }
        parent.Items.Add(new Separator());
        if (b.State.PausedUntil is not null)
        {
            parent.Items.Add(Item("Resume break", () => scheduler.Resume(b.Id)));
        }
        else if (!s.HidePause || extended)
        {
            var pause = new MenuItem { Header = "Pause break" };
            AddPauseChoices(pause, b.Id);
            parent.Items.Add(pause);
        }
        if (!s.HideReset || extended)
        {
            parent.Items.Add(Item("Reset break", () => scheduler.ResetBreak(b.Id)));
        }
        if (!s.HideDisable || extended)
        {
            parent.Items.Add(Item(b.Enabled ? "Disable break" : "Enable break", () => scheduler.SetEnabled(b.Id, !b.Enabled)));
        }
        parent.Items.Add(new Separator());
        parent.Items.Add(Item("Edit break…", () => commands.OpenSettings(null, b.Id)));
    }

    private void AddPauseChoices(MenuItem parent, Guid? id)
    {
        foreach (var (title, seconds) in Settings.Choices.Pause)
        {
            if (seconds == 0)
            {
                parent.Items.Add(new Separator());
            }
            double? length = seconds == 0 ? null : seconds;
            parent.Items.Add(Item(title, () =>
            {
                if (id is { } breakId)
                {
                    scheduler.Pause(breakId, length);
                }
                else
                {
                    scheduler.PauseAll(length);
                }
            }));
        }
    }

    private static MenuItem Item(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    private static MenuItem Header(string text) => new() { Header = text, IsEnabled = false };

    /// <summary>Disables a postpone or skip item once the break's limit is used up.</summary>
    private static MenuItem Limited(MenuItem item, bool allowed)
    {
        if (!allowed)
        {
            item.IsEnabled = false;
            item.Header += " (limit reached)";
        }
        return item;
    }

    public void Dispose()
    {
        icon?.Dispose();
        IconRenderer.Destroy(current);
    }
}
