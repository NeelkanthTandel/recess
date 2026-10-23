using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Recess.App.Break;
using Recess.App.Platform;
using Recess.Core;
using Windows.Win32;

namespace Recess.App;

public partial class App : Application
{
    private TrayIcon? tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            Toasts.Register();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.IO.IOException)
        {
            // Notifications fall back to Recess's own banner later on; nothing else depends on this.
        }

        tray = new TrayIcon("Recess");
        tray.MenuOpening += BuildMenu;
        tray.Show(Icons.LoadAppIcon(PInvoke.GetDpiForSystem()));
    }

    protected override void OnExit(ExitEventArgs e)
    {
        tray?.Dispose();
        base.OnExit(e);
    }

    // Milestone 1 menu: the checks to run on a real Windows PC before the engine goes in.
    private void BuildMenu(ContextMenu menu, bool extended)
    {
        menu.Items.Add(Item("Preview the Default break screen", () => PreviewSession.Start(null)));

        var themes = new MenuItem { Header = "Preview an HTML theme" };
        foreach (var (name, folder, root) in BundledThemes())
        {
            themes.Items.Add(Item(name, () => PreviewSession.Start(HtmlTheme.Local(folder, root))));
        }
        menu.Items.Add(themes);
        menu.Items.Add(Item("Preview a YouTube theme", () => PreviewSession.Start(YouTubeTheme())));
        menu.Items.Add(Item("Focus test: preview in 5 seconds", () =>
        {
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                PreviewSession.Start(null);
            };
            timer.Start();
        }));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Send a test notification", () =>
        {
            if (Toasts.Enabled)
            {
                Toasts.Show("Break coming up", "Your Normal break starts in a minute.");
            }
            else
            {
                MessageBox.Show("Notifications are turned off for Recess in Windows Settings.", "Recess");
            }
        }));
        if (extended)
        {
            menu.Items.Add(Item("Open data folder", () => System.Diagnostics.Process.Start("explorer.exe", DataFolder.Path)));
        }
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Quit Recess", Shutdown));
    }

    private static MenuItem Item(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    // Themes shipped next to the exe, each with an Info.json naming its root document.
    private static IEnumerable<(string Name, string Folder, string Root)> BundledThemes()
    {
        var themes = Path.Combine(AppContext.BaseDirectory, "Themes");
        if (!Directory.Exists(themes))
        {
            yield break;
        }
        foreach (var folder in Directory.GetDirectories(themes).Order())
        {
            var infoPath = Path.Combine(folder, "Info.json");
            if (!File.Exists(infoPath))
            {
                continue;
            }
            using var info = JsonDocument.Parse(File.ReadAllText(infoPath));
            var name = info.RootElement.TryGetProperty("name", out var n) ? n.GetString() : null;
            var root = info.RootElement.TryGetProperty("rootDocument", out var r) ? r.GetString() : null;
            if (name is not null && root is not null)
            {
                yield return (name, Path.Combine(folder, "Documents"), root);
            }
        }
    }

    // A generated theme like the Mac's YouTube themes, for checking that video plays smoothly.
    private static HtmlTheme YouTubeTheme()
    {
        const string id = "jfKfPfyJRdk";
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Recess", "Spike", "YouTube");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "index.html"), $$"""
            <!DOCTYPE html><html><head><meta charset="utf-8"><style>html,body{margin:0;height:100%;background:transparent;overflow:hidden}
            iframe{position:absolute;inset:8vh 8vw;width:84vw;height:84vh;border:0;border-radius:16px}</style></head>
            <body><iframe src="https://www.youtube-nocookie.com/embed/{{id}}?autoplay=1&loop=1&playlist={{id}}&mute=0" allow="autoplay; encrypted-media" allowfullscreen></iframe></body></html>
            """);
        return HtmlTheme.Local(folder, "index.html");
    }
}
