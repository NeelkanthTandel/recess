using System.Globalization;
using System.Net;
using System.Text.Json;

namespace Recess.Core;

public enum GeneratedThemeKind
{
    Text,
    Website,
    YouTube,
    Images,
}

/// <summary>What the New Theme window asks for.</summary>
public sealed class GeneratedTheme
{
    public string Name { get; set; } = "My Theme";
    public GeneratedThemeKind Kind { get; set; } = GeneratedThemeKind.Text;
    public string Text { get; set; } = "Stand up, stretch, and look into the distance.";
    public string FontName { get; set; } = ThemeText.DefaultFont;
    public double FontSize { get; set; } = 64;
    public Rgba Foreground { get; set; } = new(1, 1, 1);
    public string Url { get; set; } = "https://";
    public string? ImagesFolder { get; set; }
    public double CycleSeconds { get; set; } = 10;
    public bool RandomOrder { get; set; } = true;
}

public sealed class ThemeException(string message) : Exception(message);

/// <summary>
/// Makes simple themes (text, a website, a YouTube video, or a slideshow of images) as
/// .recesstheme folders. Everything the user typed is escaped before it goes into the page.
/// </summary>
public static class ThemeGenerator
{
    private static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp"];

    public static string Title(this GeneratedThemeKind kind) => kind switch
    {
        GeneratedThemeKind.Text => "Text",
        GeneratedThemeKind.Website => "Website",
        GeneratedThemeKind.YouTube => "YouTube video",
        _ => "Folder of images",
    };

    /// <summary>Creates the theme in <paramref name="themesFolder"/> and returns its folder. Never overwrites an existing theme.</summary>
    public static string Create(string themesFolder, GeneratedTheme c)
    {
        var info = new Dictionary<string, object>
        {
            ["name"] = c.Name,
            ["identifier"] = "user-" + Guid.NewGuid().ToString().ToUpperInvariant(),
            ["version"] = 1,
            ["comments"] = $"Created with Recess ({c.Kind.Title()}).",
            ["created"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
        };
        string? html = null;
        switch (c.Kind)
        {
            case GeneratedThemeKind.Website:
                if (!Uri.TryCreate(c.Url.Trim(), UriKind.Absolute, out var url) || (url.Scheme != Uri.UriSchemeHttps && url.Scheme != Uri.UriSchemeHttp))
                {
                    throw new ThemeException("That doesn't look like a web address. It should start with https://.");
                }
                info["url"] = url.AbsoluteUri;
                break;
            case GeneratedThemeKind.YouTube:
                var id = ThemeText.YouTubeId(c.Url) ?? throw new ThemeException("That doesn't look like a YouTube video link.");
                html = $$"""
                    <!DOCTYPE html><html><head><meta charset="utf-8"><style>html,body{margin:0;height:100%;background:transparent;overflow:hidden}
                    iframe{position:absolute;inset:8vh 8vw;width:84vw;height:84vh;border:0;border-radius:16px}</style></head>
                    <body><iframe src="https://www.youtube-nocookie.com/embed/{{id}}?autoplay=1&loop=1&playlist={{id}}&mute=0" allow="autoplay; encrypted-media" allowfullscreen></iframe></body></html>
                    """;
                break;
            case GeneratedThemeKind.Text:
                html = $$"""
                    <!DOCTYPE html><html><head><meta charset="utf-8"><style>html,body{margin:0;height:100%;background:transparent;overflow:hidden}
                    body{display:flex;align-items:center;justify-content:center}
                    #text{max-width:80vw;text-align:center;white-space:pre-wrap;color:{{c.Foreground.Hex}};font-family:'{{ThemeText.SafeFontName(c.FontName)}}','Segoe UI',sans-serif;font-size:{{(int)Math.Clamp(c.FontSize, 8, 400)}}px;font-weight:600;line-height:1.25;text-shadow:0 2px 12px rgba(0,0,0,.35)}
                    </style></head><body><div id="text">{{WebUtility.HtmlEncode(c.Text)}}</div></body></html>
                    """;
                break;
        }

        var folder = FreeFolder(themesFolder, c.Name);
        var docs = Path.Combine(folder, "Documents");
        Directory.CreateDirectory(docs);
        if (c.Kind == GeneratedThemeKind.Images)
        {
            var names = new List<string>();
            if (c.ImagesFolder is { } source && Directory.Exists(source))
            {
                foreach (var file in Directory.EnumerateFiles(source))
                {
                    if (ImageExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                    {
                        File.Copy(file, Path.Combine(docs, Path.GetFileName(file)), overwrite: false);
                        names.Add(Path.GetFileName(file));
                    }
                }
            }
            // The default JSON encoder escapes < and >, so a file name can't end the script.
            var list = JsonSerializer.Serialize(names);
            var interval = (int)(Math.Clamp(c.CycleSeconds, 2, 600) * 1000);
            html = $$"""
                <!DOCTYPE html><html><head><meta charset="utf-8"><style>html,body{margin:0;height:100%;background:transparent;overflow:hidden}
                img{position:absolute;inset:0;width:100%;height:100%;object-fit:cover;opacity:0;transition:opacity 1.5s}img.on{opacity:1}</style></head>
                <body><img id="a"><img id="b"><script>
                const items={{list}};const random={{(c.RandomOrder ? "true" : "false")}};let i=-1,front='a';
                function next(){if(!items.length)return;i=random?Math.floor(Math.random()*items.length):(i+1)%items.length;
                const back=front==='a'?'b':'a';const el=document.getElementById(back);el.onload=()=>{el.classList.add('on');document.getElementById(front).classList.remove('on');front=back};el.src=encodeURIComponent(items[i]);}
                next();setInterval(next,{{interval}});
                </script></body></html>
                """;
        }
        if (html is not null)
        {
            info["rootDocument"] = "index.html";
            File.WriteAllText(Path.Combine(docs, "index.html"), html);
        }
        File.WriteAllText(Path.Combine(folder, "Info.json"), JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true }));
        return folder;
    }

    /// <summary>A folder name from the theme's name that isn't taken yet: "Calm.recesstheme", "Calm 2.recesstheme"…</summary>
    private static string FreeFolder(string themesFolder, string name)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(['/', '\\', ':']).ToHashSet();
        var cleaned = new string(name.Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray()).Trim().Trim('.');
        if (cleaned.Length == 0)
        {
            cleaned = "Theme";
        }
        var folder = Path.Combine(themesFolder, cleaned + ".recesstheme");
        for (var n = 2; Directory.Exists(folder); n++)
        {
            folder = Path.Combine(themesFolder, $"{cleaned} {n}.recesstheme");
        }
        return folder;
    }
}
