using System.Text.Json;
using Recess.Core;

namespace Recess.App.Breaks;

/// <summary>A break theme: built in, shipped with Recess, or one of the user's own (a .recesstheme folder).</summary>
internal sealed record Theme(string Id, string Name, string Comments, ThemeSource Source)
{
    public string Author { get; init; } = "";

    public string CreditsName { get; init; } = "";

    public string CreditsUrl { get; init; } = "";

    /// <summary>The .recesstheme folder, for themes that have one.</summary>
    public string? Folder { get; init; }

    /// <summary>The page to load, relative to the folder's Documents folder.</summary>
    public string? RootDocument { get; init; }

    /// <summary>A web page to show instead of a local document.</summary>
    public Uri? Website { get; init; }

    public bool Editable => Source == ThemeSource.User;

    public HtmlTheme? Html => Website is not null
        ? HtmlTheme.Web(Website)
        : Folder is not null && RootDocument is not null ? HtmlTheme.Local(Path.Combine(Folder, "Documents"), RootDocument) : null;
}

internal enum ThemeSource
{
    Builtin,
    Bundled,
    User,
}

/// <summary>Finds themes: the built-in ones, those shipped next to Recess.exe, and the user's own.</summary>
internal sealed class ThemeCatalog(string userFolder)
{
    public const string FileExtension = ".recesstheme";

    public static readonly Theme[] Builtins =
    [
        new(ThemeIds.Default, "Default", "Break name, countdown ring, and a gentle suggestion.", ThemeSource.Builtin),
        new(ThemeIds.Blank, "Blank", "Dim the screen with no theme content.", ThemeSource.Builtin),
        new(ThemeIds.None, "None", "Don't dim the screen; show a small panel with the countdown instead.", ThemeSource.Builtin),
    ];

    private List<Theme> themes = [];

    public string UserFolder => userFolder;

    public IReadOnlyList<Theme> Themes
    {
        get
        {
            if (themes.Count == 0)
            {
                Reload();
            }
            return themes;
        }
    }

    public event Action? Changed;

    public void Reload()
    {
        var list = new List<Theme>(Builtins);
        list.AddRange(Load(Path.Combine(AppContext.BaseDirectory, "Themes"), ThemeSource.Bundled));
        list.AddRange(Load(userFolder, ThemeSource.User));
        themes = list.DistinctBy(t => t.Id).ToList();
        Changed?.Invoke();
    }

    /// <summary>The theme with this ID, or Default if it's gone.</summary>
    public Theme Find(string id) => Themes.FirstOrDefault(t => t.Id == id) ?? Builtins[0];

    private static IEnumerable<Theme> Load(string folder, ThemeSource source)
    {
        if (!Directory.Exists(folder))
        {
            return [];
        }
        var result = new List<Theme>();
        foreach (var dir in Directory.EnumerateDirectories(folder, "*" + FileExtension))
        {
            try
            {
                using var info = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "Info.json")));
                var root = info.RootElement;
                string? Text(JsonElement e, string key) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v)
                    && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                var name = Text(root, "name") ?? Path.GetFileNameWithoutExtension(dir);
                var ident = Text(root, "identifier") ?? name;
                root.TryGetProperty("author", out var author);
                root.TryGetProperty("credits", out var credits);
                var document = Text(root, "rootDocument");
                Uri? website = null;
                if (document is null && Text(root, "url") is { } url && Uri.TryCreate(url, UriKind.Absolute, out var u)
                    && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp))
                {
                    website = u;
                }
                if (document is null && website is null)
                {
                    continue;
                }
                // Root documents stay inside the theme's Documents folder.
                if (document is not null && (Path.IsPathRooted(document) || document.Split('/', '\\').Contains("..")))
                {
                    continue;
                }
                var id = $"{source.ToString().ToLowerInvariant()}.{ident}";
                result.Add(new Theme(id, name, Text(root, "comments") ?? "", source)
                {
                    Author = Text(author, "name") ?? "",
                    CreditsName = Text(credits, "name") ?? "",
                    CreditsUrl = Text(credits, "url") ?? "",
                    Folder = dir,
                    RootDocument = document,
                    Website = website,
                });
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                // A theme without a readable Info.json is skipped.
            }
        }
        return result.OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase);
    }
}
