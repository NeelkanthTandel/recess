using System.Text.RegularExpressions;

namespace Recess.Core;

/// <summary>Checks for text that ends up inside generated theme HTML.</summary>
public static partial class ThemeText
{
    public const string DefaultFont = "Segoe UI";

    /// <summary>
    /// The video ID from a YouTube link or a bare ID. Only well-formed IDs come back, because
    /// the ID is written into generated HTML.
    /// </summary>
    public static string? YouTubeId(string text)
    {
        var trimmed = text.Trim();
        string? candidate;
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var url) && url.Host.Length > 0)
        {
            var v = QueryValue(url.Query, "v");
            if (v is not null)
            {
                candidate = v;
            }
            else if (url.Host.EndsWith("youtu.be", StringComparison.OrdinalIgnoreCase))
            {
                candidate = url.AbsolutePath.TrimStart('/');
            }
            else if (url.AbsolutePath.Contains("/embed/", StringComparison.Ordinal) || url.AbsolutePath.Contains("/shorts/", StringComparison.Ordinal))
            {
                candidate = url.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
            }
            else
            {
                candidate = null;
            }
        }
        else
        {
            candidate = trimmed;
        }
        return candidate is not null && IdPattern().IsMatch(candidate) ? candidate : null;
    }

    /// <summary>Font names go into CSS, so only characters that can appear in a font name are kept.</summary>
    public static string SafeFontName(string text)
    {
        var cleaned = new string(text.Where(c => char.IsLetterOrDigit(c) || c == ' ' || c == '-').ToArray());
        return cleaned.Length == 0 ? DefaultFont : cleaned[..Math.Min(64, cleaned.Length)];
    }

    private static string? QueryValue(string query, string key)
    {
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            if (eq > 0 && pair[..eq] == key)
            {
                return Uri.UnescapeDataString(pair[(eq + 1)..]);
            }
        }
        return null;
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{11}$")]
    private static partial Regex IdPattern();
}
