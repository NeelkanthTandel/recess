using System.Windows;
using System.Windows.Controls;
using Recess.App.Platform;
using Recess.Core;

namespace Recess.App.Settings;

/// <summary>The Calendars section of General: the iCal feeds and files breaks and rules can use.</summary>
internal static class CalendarSection
{
    public static void Add(Form f, SettingsContext ctx)
    {
        var s = f.Section("Calendars");
        f.Hint(s, "Time breaks from calendar events, or postpone them during meetings. Add your calendar's private iCal address (Google Calendar, Outlook.com, iCloud and Exchange all offer one) or an .ics file.");
        var list = new StackPanel();
        s.Children.Add(list);

        void Rebuild()
        {
            list.Children.Clear();
            foreach (var feed in ctx.Model.Settings.Calendars.ToList())
            {
                var id = feed.Id;
                var row = new DockPanel { Margin = new Thickness(0, 6, 0, 6) };
                var buttons = Form.Line(
                    Form.Button("Reload", () => ctx.Calendars.RefreshNow(id)),
                    Form.Button("Remove", () =>
                    {
                        ctx.Model.Settings.Calendars.RemoveAll(c => c.Id == id);
                        ctx.Model.MarkChanged();
                        ctx.Calendars.RefreshNow();
                        Rebuild();
                    }));
                DockPanel.SetDock(buttons, Dock.Right);
                row.Children.Add(buttons);
                var enabled = new CheckBox { IsChecked = feed.Enabled, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 8, 0) };
                enabled.Click += (_, _) =>
                {
                    feed.Enabled = enabled.IsChecked == true;
                    ctx.Model.MarkChanged();
                    ctx.Calendars.RefreshNow();
                };
                row.Children.Add(enabled);
                var text = new StackPanel();
                text.Children.Add(new TextBlock { Text = feed.Name, FontWeight = FontWeights.Medium });
                text.Children.Add(new TextBlock
                {
                    Text = feed.FilePath.Length > 0 ? feed.FilePath : "iCal address (stored encrypted)",
                    FontSize = 12,
                    Opacity = 0.6,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                });
                var status = new TextBlock { FontSize = 12, Opacity = 0.75, TextWrapping = TextWrapping.Wrap };
                f.OnRefresh(() => status.Text = ctx.Calendars.Status(id));
                text.Children.Add(status);
                row.Children.Add(text);
                list.Children.Add(row);
            }
            if (ctx.Model.Settings.Calendars.Count == 0)
            {
                list.Children.Add(new TextBlock { Text = "No calendars yet.", Opacity = 0.6, Margin = new Thickness(0, 4, 0, 4) });
            }
        }

        Rebuild();
        ctx.Calendars.StatusChanged += f.Refresh;
        list.Unloaded += (_, _) => ctx.Calendars.StatusChanged -= f.Refresh;
        f.Full(s, Form.Line(Form.Button("Add calendar…", () =>
        {
            if (AddCalendarWindow.Ask(Window.GetWindow(list)) is { } feed)
            {
                ctx.Model.Settings.Calendars.Add(feed);
                ctx.Model.MarkChanged();
                ctx.Calendars.RefreshNow(feed.Id);
                Rebuild();
            }
        })));
        f.Hint(s, "Recess downloads an address every 10 minutes. It's the only time Recess goes online, and only for calendars you add here. The address is kept encrypted for your Windows account, because anyone with it can read your calendar.");
    }
}

/// <summary>Asks for a calendar: a name, and an iCal address or an .ics file.</summary>
internal sealed class AddCalendarWindow : Window
{
    private readonly TextBox name = new() { Text = "Work", Width = 300 };
    private readonly PasswordBox address = new() { Width = 300 };
    private readonly TextBlock file = new() { Opacity = 0.7, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, MaxWidth = 220, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock error = new() { Foreground = System.Windows.Media.Brushes.OrangeRed, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private string? filePath;
    private CalendarFeed? result;

    private AddCalendarWindow()
    {
        Title = "Add calendar";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        var f = new Form(() => { });
        var s = f.Section();
        f.Row(s, "Name", name);
        f.Row(s, "Private iCal address", address);
        f.Hint(s, "Google Calendar: Settings → your calendar → Secret address in iCal format. Outlook.com: Settings → Calendar → Shared calendars → Publish a calendar → ICS. iCloud: share the calendar as Public and copy the link.");
        file.Text = "or choose an .ics file";
        f.Row(s, "File", Form.Line(file, Form.Button("Choose…", () =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Calendar files|*.ics" };
            if (dialog.ShowDialog(this) == true)
            {
                filePath = dialog.FileName;
                file.Text = Path.GetFileName(filePath);
                address.Clear();
            }
        })));
        f.Root.Children.Add(error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Padding = new Thickness(14, 4, 14, 4), Margin = new Thickness(0, 0, 8, 0) };
        var add = new Button { Content = "Add", IsDefault = true, Padding = new Thickness(14, 4, 14, 4) };
        add.Click += (_, _) => Add();
        buttons.Children.Add(cancel);
        buttons.Children.Add(add);
        f.Root.Children.Add(buttons);
        Content = f.Root;
    }

    public static CalendarFeed? Ask(Window? owner)
    {
        var window = new AddCalendarWindow { Owner = owner };
        window.ShowDialog();
        return window.result;
    }

    private void Add()
    {
        var feed = new CalendarFeed { Name = name.Text.Trim().Length > 0 ? name.Text.Trim() : "Calendar" };
        if (address.Password.Trim().Length > 0)
        {
            if (CalendarFetcher.ParseAddress(address.Password) is not { } url)
            {
                error.Text = "That doesn't look like an iCal address. It should start with https:// or webcal://.";
                return;
            }
            feed.ProtectedUrl = CalendarFetcher.Protect(url.AbsoluteUri);
        }
        else if (filePath is not null)
        {
            feed.FilePath = filePath;
        }
        else
        {
            error.Text = "Paste an address or choose a file.";
            return;
        }
        result = feed;
        DialogResult = true;
    }
}
