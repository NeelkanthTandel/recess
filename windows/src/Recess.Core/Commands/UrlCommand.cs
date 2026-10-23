using System.Globalization;

namespace Recess.Core;

/// <summary>A parsed recess:// link, e.g. recess://postpone?break=Normal&amp;minutes=10.</summary>
public sealed class UrlCommand
{
    /// <summary>Longer links are ignored; nothing legitimate needs more.</summary>
    public const int MaxLength = 2048;

    private readonly Dictionary<string, string> query;

    private UrlCommand(string name, Dictionary<string, string> query)
    {
        Name = name;
        this.query = query;
    }

    /// <summary>Lower-case command name, e.g. "start" or "settings".</summary>
    public string Name { get; }

    public string? this[string key] => query.TryGetValue(key, out var v) ? v : null;

    /// <summary>
    /// Parses a link. Accepts recess://start, recess://start/ (browsers add the slash) and
    /// recess:start. Query keys are case-insensitive; the first of a repeated key wins.
    /// </summary>
    public static UrlCommand? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaxLength)
        {
            return null;
        }
        text = text.Trim();
        if (!text.StartsWith("recess:", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        var rest = text["recess:".Length..];
        if (rest.StartsWith("//", StringComparison.Ordinal))
        {
            rest = rest[2..];
        }
        var queryStart = rest.IndexOf('?');
        var path = queryStart >= 0 ? rest[..queryStart] : rest;
        var queryText = queryStart >= 0 ? rest[(queryStart + 1)..] : "";
        var fragment = queryText.IndexOf('#');
        if (fragment >= 0)
        {
            queryText = queryText[..fragment];
        }
        var name = Uri.UnescapeDataString(path.Trim('/')).ToLowerInvariant();
        if (name.Length == 0 || !name.All(char.IsAsciiLetter))
        {
            return null;
        }

        var query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in queryText.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            var key = Decode(eq >= 0 ? pair[..eq] : pair);
            var value = eq >= 0 ? Decode(pair[(eq + 1)..]) : "";
            query.TryAdd(key, value);
        }
        return new UrlCommand(name, query);
    }

    private static string Decode(string s) => Uri.UnescapeDataString(s.Replace('+', ' '));

    /// <summary>A finite number clamped into a range, or null if missing or not a number.</summary>
    public double? Number(string key, double min, double max)
    {
        if (this[key] is not { } s || !double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) || !double.IsFinite(v))
        {
            return null;
        }
        return Math.Min(Math.Max(v, min), max);
    }
}

/// <summary>What a link can ask the app itself to do (the rest goes to the scheduler).</summary>
public interface IUrlCommandHost
{
    void StopSound();

    /// <summary>Opens Settings at a page ("general", "shortcuts", "rules", "activity", "advanced") or a break.</summary>
    void OpenSettings(string? page, Guid? breakId);
}

public static class UrlCommands
{
    private static readonly HashSet<string> Pages = ["general", "shortcuts", "rules", "activity", "advanced"];

    /// <summary>
    /// Runs a link. Any app or web page can open recess:// links, so they can be turned off in
    /// Settings, and every number is checked. Returns false if the link was ignored.
    /// </summary>
    public static bool Run(UrlCommand url, RecessModel model, Scheduler scheduler, IUrlCommandHost host, DateTime now)
    {
        if (!model.Settings.AllowUrlCommands)
        {
            return false;
        }
        var target = url["break"] is { } name ? model.FindByName(name) : null;
        var minutes = url.Number("minutes", 0, 1440);
        switch (url.Name)
        {
            case "start":
                if (target is not null)
                {
                    scheduler.StartBreak(target.Id, manual: true);
                }
                else
                {
                    scheduler.StartNextBreak();
                }
                break;
            case "postpone":
                if (target is not null)
                {
                    scheduler.Postpone(target.Id, minutes ?? target.Controls.PostponeMinutes1);
                }
                else if (minutes is { } m)
                {
                    scheduler.PostponeCurrentOrNext(m);
                }
                else
                {
                    scheduler.PostponeNext(1);
                }
                break;
            case "skip":
                if (target is not null)
                {
                    scheduler.Skip(target.Id);
                }
                else
                {
                    scheduler.SkipNext();
                }
                break;
            case "pause":
                var seconds = minutes * 60;
                if (target is not null)
                {
                    scheduler.Pause(target.Id, seconds);
                }
                else
                {
                    scheduler.PauseAll(seconds);
                }
                break;
            case "resume":
                if (target is not null)
                {
                    scheduler.Resume(target.Id);
                }
                else
                {
                    scheduler.ResumeAll();
                }
                break;
            case "makedue":
                var secs = url.Number("seconds", 0, 86400) ?? (minutes ?? 1) * 60;
                if ((target ?? model.NextBreakAnyLength(now)) is { } b)
                {
                    scheduler.MakeDue(b.Id, secs);
                }
                break;
            case "reset":
                if (target is not null)
                {
                    scheduler.ResetBreak(target.Id);
                }
                else
                {
                    scheduler.ResetAll();
                }
                break;
            case "stopsound":
                host.StopSound();
                break;
            case "settings" or "preferences":
                var page = url["page"]?.ToLowerInvariant();
                host.OpenSettings(page is not null && Pages.Contains(page) ? page : null, target?.Id);
                break;
            default:
                return false;
        }
        return true;
    }
}
