using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Recess.App.Break;

/// <summary>What an HTML theme shows: a local theme folder, or a web page.</summary>
internal sealed record HtmlTheme(string? Folder, string? RootDocument, Uri? Website)
{
    public static HtmlTheme Local(string folder, string rootDocument) => new(folder, rootDocument, null);

    public static HtmlTheme Web(Uri url) => new(null, null, url);

    public bool IsLocal => Folder is not null;
}

/// <summary>
/// Hosts an HTML theme in WebView2. The composition control draws as ordinary WPF content,
/// so the dim colour shows through the page and the break buttons sit on top of it.
/// </summary>
internal sealed class ThemeView : WebView2CompositionControl
{
    // Local themes call this while loading, exactly as on the Mac, so the same theme files work.
    private const string Shim = """
        (() => {
          if (window.top !== window || !window.chrome || !window.chrome.webview) return;
          const webview = window.chrome.webview;
          window.webkit = window.webkit || {};
          window.webkit.messageHandlers = window.webkit.messageHandlers || {};
          window.webkit.messageHandlers.getRecessInfo = {
            postMessage: (level) => webview.postMessage({ getRecessInfo: String(level ?? 'summary') })
          };
          webview.addEventListener('message', (e) => {
            if (typeof window.recessInfo === 'function') window.recessInfo(JSON.stringify(e.data));
          });
        })();
        """;

    private static Task<CoreWebView2Environment>? environment;

    private readonly HtmlTheme theme;
    private readonly Func<string, object> info;
    private string? host;

    /// <param name="info">Builds the break details a local theme asks for, given the requested level.</param>
    public ThemeView(HtmlTheme theme, Func<string, object> info)
    {
        this.theme = theme;
        this.info = info;
        DefaultBackgroundColor = System.Drawing.Color.Transparent;
        Loaded += async (_, _) =>
        {
            try
            {
                await StartAsync();
            }
            catch (Exception ex) when (ex is WebView2RuntimeNotFoundException or System.Runtime.InteropServices.COMException
                or InvalidOperationException)
            {
                // The break still runs; the theme area just stays empty over the dim colour.
                System.Diagnostics.Debug.WriteLine($"Theme failed to load: {ex.Message}");
            }
        };
    }

    /// <summary>False when the WebView2 Runtime isn't installed (some LTSC and locked-down PCs).</summary>
    public static bool RuntimeAvailable
    {
        get
        {
            try
            {
                return !string.IsNullOrEmpty(CoreWebView2Environment.GetAvailableBrowserVersionString());
            }
            catch (WebView2RuntimeNotFoundException)
            {
                return false;
            }
        }
    }

    // One browser environment for every break window; its data stays out of the roaming profile.
    private static Task<CoreWebView2Environment> Environment => environment ??= CoreWebView2Environment.CreateAsync(
        null,
        Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "Recess", "WebView2"),
        new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required"));

    private async Task StartAsync()
    {
        await EnsureCoreWebView2Async(await Environment);
        var core = CoreWebView2;
        LockDown(core);

        if (theme.IsLocal)
        {
            // Each theme gets its own made-up host name, so its localStorage is its own.
            host = HostFor(theme.Folder!);
            core.SetVirtualHostNameToFolderMapping(host, theme.Folder!, CoreWebView2HostResourceAccessKind.DenyCors);
            await core.AddScriptToExecuteOnDocumentCreatedAsync(Shim);
            core.WebMessageReceived += OnWebMessage;
            var path = string.Join('/', theme.RootDocument!.Split('/', '\\').Select(Uri.EscapeDataString));
            core.Navigate($"https://{host}/{path}");
        }
        else
        {
            core.Navigate(theme.Website!.AbsoluteUri);
        }
    }

    private void LockDown(CoreWebView2 core)
    {
        var s = core.Settings;
#if !DEBUG
        s.AreDevToolsEnabled = false;
#endif
        s.AreDefaultContextMenusEnabled = false;
        s.AreBrowserAcceleratorKeysEnabled = false;
        s.IsStatusBarEnabled = false;
        s.AreHostObjectsAllowed = false;
        s.IsGeneralAutofillEnabled = false;
        s.IsPasswordAutosaveEnabled = false;
        s.IsSwipeNavigationEnabled = false;
        s.IsZoomControlEnabled = false;
        s.IsWebMessageEnabled = theme.IsLocal;

        core.NavigationStarting += (_, e) => e.Cancel = !Allowed(e.Uri);
        core.FrameNavigationStarting += (_, e) => e.Cancel = !Uri.TryCreate(e.Uri, UriKind.Absolute, out var u)
            || (u.Scheme != Uri.UriSchemeHttps && u.Scheme != Uri.UriSchemeHttp && u.Scheme != "about");
        core.NewWindowRequested += (_, e) => e.Handled = true;
        core.DownloadStarting += (_, e) => e.Cancel = true;
        core.LaunchingExternalUriScheme += (_, e) => e.Cancel = true;
        core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
    }

    // A local theme stays inside its own folder; a website theme may go anywhere on the web.
    private bool Allowed(string uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var u))
        {
            return false;
        }
        if (theme.IsLocal)
        {
            return u.Scheme == Uri.UriSchemeHttps && string.Equals(u.Host, host, StringComparison.OrdinalIgnoreCase);
        }
        return u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp;
    }

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        // Only the theme's own top-level page gets break details.
        if (!Uri.TryCreate(e.Source, UriKind.Absolute, out var source)
            || !string.Equals(source.Host, host, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        string level;
        try
        {
            using var message = JsonDocument.Parse(e.WebMessageAsJson);
            level = message.RootElement.TryGetProperty("getRecessInfo", out var l) && l.ValueKind == JsonValueKind.String
                ? l.GetString()!
                : "summary";
        }
        catch (JsonException)
        {
            return;
        }
        CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(info(level)));
    }

    private static string HostFor(string folder)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(folder).ToUpperInvariant()));
        return Convert.ToHexStringLower(hash.AsSpan(0, 8)) + ".theme.example";
    }
}
