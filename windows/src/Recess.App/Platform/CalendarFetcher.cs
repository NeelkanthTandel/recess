using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Threading;
using Recess.Core;

namespace Recess.App.Platform;

/// <summary>
/// Keeps the user's calendars current: downloads each iCal address every 10 minutes (HTTPS
/// only, size- and time-limited, using ETags so unchanged calendars aren't downloaded again)
/// or re-reads an .ics file when it changes. This is the only network request Recess makes,
/// and only for calendars the user added. Addresses are stored encrypted for the Windows user.
/// </summary>
internal sealed class CalendarFetcher : IDisposable
{
    private const long MaxBytes = 10 * 1024 * 1024;
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Recess calendar address");

    private readonly RecessModel model;
    private readonly CalendarFeeds feeds;
    private readonly HttpClient http;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMinutes(1) };
    private readonly Dictionary<string, (string? ETag, DateTimeOffset? Modified, DateTime Fetched, DateTime FileTime)> state = [];
    private readonly Dictionary<string, string> status = [];
    private readonly HashSet<string> busy = [];

    public CalendarFetcher(RecessModel model, CalendarFeeds feeds)
    {
        this.model = model;
        this.feeds = feeds;
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5,
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(15),
            UseCookies = false,
        };
        http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30), MaxResponseContentBufferSize = MaxBytes };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Recess", typeof(CalendarFetcher).Assembly.GetName().Version?.ToString(3) ?? "1"));
        timer.Tick += (_, _) => RefreshDue();
        timer.Start();
        RefreshDue();
    }

    /// <summary>Raised when a calendar's status line changes, for Settings.</summary>
    public event Action? StatusChanged;

    public string Status(string feedId) => status.TryGetValue(feedId, out var s) ? s : "Not loaded yet";

    /// <summary>Encrypts an iCal address for this Windows user. It works like a password, so it's never stored in plain text.</summary>
    public static string Protect(string url) =>
        Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(url), Entropy, DataProtectionScope.CurrentUser));

    public static string? Unprotect(string stored)
    {
        try
        {
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(stored), Entropy, DataProtectionScope.CurrentUser));
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return null;
        }
    }

    /// <summary>Normalises what the user pasted: webcal:// becomes https://; anything but HTTPS is refused.</summary>
    public static Uri? ParseAddress(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("webcals://", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = "https://" + trimmed[(trimmed.IndexOf("://", StringComparison.Ordinal) + 3)..];
        }
        return Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps ? uri : null;
    }

    /// <summary>Loads every calendar now (the Reload button), or one.</summary>
    public void RefreshNow(string? feedId = null)
    {
        foreach (var feed in model.Settings.Calendars.Where(f => feedId is null || f.Id == feedId))
        {
            state.Remove(feed.Id);
        }
        RefreshDue();
    }

    private void RefreshDue()
    {
        var wanted = model.Settings.Calendars.Where(f => f.Enabled).ToList();
        // Calendars that were removed or turned off stop counting.
        foreach (var id in state.Keys.Concat(status.Keys).Distinct().Where(id => wanted.All(f => f.Id != id)).ToList())
        {
            state.Remove(id);
            status.Remove(id);
            feeds.Remove(id);
        }
        foreach (var feed in wanted)
        {
            if (busy.Contains(feed.Id))
            {
                continue;
            }
            if (feed.FilePath.Length > 0)
            {
                LoadFile(feed);
            }
            else if (!state.TryGetValue(feed.Id, out var s) || DateTime.UtcNow - s.Fetched >= Interval)
            {
                _ = Download(feed);
            }
        }
    }

    private void LoadFile(CalendarFeed feed)
    {
        try
        {
            var written = File.GetLastWriteTimeUtc(feed.FilePath);
            if (state.TryGetValue(feed.Id, out var s) && s.FileTime == written && feeds.Has(feed.Id))
            {
                return;
            }
            var info = new FileInfo(feed.FilePath);
            if (info.Length > MaxBytes)
            {
                SetStatus(feed.Id, "The file is too large to be a calendar.");
                return;
            }
            feeds.Update(feed.Id, File.ReadAllText(feed.FilePath));
            state[feed.Id] = (null, null, DateTime.UtcNow, written);
            SetStatus(feed.Id, $"Read {DateTime.Now:t}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        {
            SetStatus(feed.Id, ex is FormatException ? ex.Message : "Couldn't read the file: " + ex.Message);
        }
    }

    private async Task Download(CalendarFeed feed)
    {
        if (Unprotect(feed.ProtectedUrl) is not { } text || ParseAddress(text) is not { } url)
        {
            SetStatus(feed.Id, "The address can't be read on this account. Remove the calendar and add it again.");
            return;
        }
        busy.Add(feed.Id);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            state.TryGetValue(feed.Id, out var previous);
            if (previous.ETag is { } etag && feeds.Has(feed.Id))
            {
                request.Headers.IfNoneMatch.ParseAdd(etag);
            }
            if (previous.Modified is { } modified && feeds.Has(feed.Id))
            {
                request.Headers.IfModifiedSince = modified;
            }
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            // A redirect must not leave HTTPS.
            if (response.RequestMessage?.RequestUri?.Scheme != Uri.UriSchemeHttps)
            {
                SetStatus(feed.Id, "The calendar redirected to an insecure address, so it wasn't loaded.");
                return;
            }
            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                state[feed.Id] = previous with { Fetched = DateTime.UtcNow };
                SetStatus(feed.Id, $"Up to date at {DateTime.Now:t}");
                return;
            }
            if (!response.IsSuccessStatusCode)
            {
                state[feed.Id] = previous with { Fetched = DateTime.UtcNow };
                SetStatus(feed.Id, $"The calendar server said {(int)response.StatusCode} {response.ReasonPhrase}. Check the address.");
                return;
            }
            if (response.Content.Headers.ContentLength is > MaxBytes)
            {
                SetStatus(feed.Id, "The calendar is too large to load.");
                return;
            }
            var body = await response.Content.ReadAsStringAsync();
            feeds.Update(feed.Id, body);
            state[feed.Id] = (response.Headers.ETag?.ToString(), response.Content.Headers.LastModified, DateTime.UtcNow, default);
            SetStatus(feed.Id, $"Updated {DateTime.Now:t}");
        }
        catch (FormatException ex)
        {
            state[feed.Id] = (null, null, DateTime.UtcNow, default);
            SetStatus(feed.Id, ex.Message + " The address may be for a web page rather than the iCal feed.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            // Try again at the next interval; the events already loaded stay.
            state[feed.Id] = state.TryGetValue(feed.Id, out var kept) ? kept with { Fetched = DateTime.UtcNow } : (null, null, DateTime.UtcNow, default);
            SetStatus(feed.Id, "Couldn't reach the calendar; trying again in 10 minutes.");
        }
        finally
        {
            busy.Remove(feed.Id);
        }
    }

    private void SetStatus(string feedId, string text)
    {
        status[feedId] = text;
        StatusChanged?.Invoke();
    }

    public void Dispose()
    {
        timer.Stop();
        http.Dispose();
    }
}
