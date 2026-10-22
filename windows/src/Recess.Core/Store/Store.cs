using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Recess.Core;

/// <summary>
/// JSON files in the data folder: Settings.json, Breaks.json (in priority order), and one
/// file per day under Activity. Missing keys fall back to defaults so files from older
/// versions keep loading, and a file that can't be read is copied aside before anything
/// can overwrite it.
/// </summary>
public sealed class Store
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        // Keeps characters like < and & escaped, so names are safe wherever the JSON ends up.
        Encoder = JavaScriptEncoder.Default,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    public Store(string folder)
    {
        Folder = folder;
    }

    public string Folder { get; }

    public string SettingsPath => Path.Combine(Folder, "Settings.json");

    public string BreaksPath => Path.Combine(Folder, "Breaks.json");

    public string ActivityFolder => Subfolder("Activity");

    public string ThemesFolder => Subfolder("Themes");

    public string ScriptsFolder => Subfolder("Scripts");

    public string SoundsFolder => Subfolder("Sounds");

    public string Subfolder(string name)
    {
        var path = Path.Combine(Folder, name);
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>Writes atomically: to a temporary file first, then over the old one.</summary>
    public static void Write<T>(T value, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(value, Json));
        File.Move(temp, path, overwrite: true);
    }

    public static T? Read<T>(string path)
        where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json);
        }
        catch (Exception ex) when (ex is JsonException or IOException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>Settings, or null if there are none yet or they couldn't be read (a copy is kept).</summary>
    public AppSettings? LoadSettings()
    {
        if (!File.Exists(SettingsPath))
        {
            return null;
        }
        try
        {
            var node = JsonNode.Parse(File.ReadAllText(SettingsPath)) as JsonObject;
            if (node is null)
            {
                throw new JsonException("Settings.json isn't a JSON object.");
            }
            // One rule this version doesn't understand shouldn't cost every other setting.
            if (node["rules"] is JsonArray rules)
            {
                var kept = new JsonArray();
                foreach (var rule in rules)
                {
                    if (rule is not null && TryDeserialize<Rule>(rule) is not null)
                    {
                        kept.Add(rule.DeepClone());
                    }
                }
                node["rules"] = kept;
            }
            return node.Deserialize<AppSettings>(Json) ?? throw new JsonException("Settings.json is empty.");
        }
        catch (Exception ex) when (ex is JsonException or IOException or InvalidOperationException)
        {
            BackUpUnreadable(SettingsPath);
            return null;
        }
    }

    /// <summary>
    /// The saved breaks, or null if there are none yet or the file couldn't be read. An empty
    /// list is kept: the user deleted every break.
    /// </summary>
    public List<Break>? LoadBreaks()
    {
        if (!File.Exists(BreaksPath))
        {
            return null;
        }
        JsonArray? array;
        try
        {
            array = JsonNode.Parse(File.ReadAllText(BreaksPath)) as JsonArray;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            array = null;
        }
        if (array is null)
        {
            BackUpUnreadable(BreaksPath);
            return null;
        }

        var lostSomething = false;
        var breaks = new List<Break>();
        foreach (var item in array)
        {
            if (item is JsonObject obj && obj["actions"] is JsonArray actions)
            {
                // Drop only the actions this version doesn't know, not the whole break.
                var kept = new JsonArray();
                foreach (var action in actions)
                {
                    if (action is not null && TryDeserialize<BreakAction>(action) is not null)
                    {
                        kept.Add(action.DeepClone());
                    }
                    else
                    {
                        lostSomething = true;
                    }
                }
                obj["actions"] = kept;
            }
            if (item is not null && TryDeserialize<Break>(item) is { } b)
            {
                breaks.Add(b);
            }
            else
            {
                lostSomething = true;
            }
        }
        // Keep a copy if anything was left out, since the next save would lose it.
        if (lostSomething)
        {
            BackUpUnreadable(BreaksPath);
        }
        return breaks;
    }

    public void Save(AppSettings settings, IEnumerable<Break> breaks)
    {
        Write(settings, SettingsPath);
        Write(breaks.ToList(), BreaksPath);
    }

    /// <summary>Decodes JSON, filling keys it lacks from the type's defaults. Null if it doesn't fit the type.</summary>
    public static T? DecodeMerging<T>(string json)
        where T : class
    {
        try
        {
            var node = JsonNode.Parse(json);
            return node is null ? null : TryDeserialize<T>(node);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static T? TryDeserialize<T>(JsonNode node)
        where T : class
    {
        // Properties start at their defaults and only keys present in the file replace them,
        // so missing keys (at any depth) keep their default values.
        try
        {
            return node.Deserialize<T>(Json);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or NotSupportedException or FormatException)
        {
            return null;
        }
    }

    /// <summary>Copies a file that failed to load to Name.unreadable-date.json before it can be overwritten.</summary>
    public static void BackUpUnreadable(string path)
    {
        try
        {
            var stamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH-mm-ssZ", CultureInfo.InvariantCulture);
            var backup = Path.Combine(Path.GetDirectoryName(path)!, $"{Path.GetFileNameWithoutExtension(path)}.unreadable-{stamp}.json");
            File.Copy(path, backup, overwrite: false);
        }
        catch (IOException)
        {
            // A backup with the same stamp already exists, or the disk is unavailable.
        }
    }
}
