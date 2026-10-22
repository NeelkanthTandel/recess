namespace Recess.Core.Tests;

public class ThemeGeneratorTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "recess-themes-" + Guid.NewGuid());

    public ThemeGeneratorTests() => Directory.CreateDirectory(folder);

    public void Dispose()
    {
        Directory.Delete(folder, recursive: true);
        GC.SuppressFinalize(this);
    }

    private static string Page(string theme) => File.ReadAllText(Path.Combine(theme, "Documents", "index.html"));

    [Fact]
    public void TextIsEscapedAndFontNamesCleaned()
    {
        var theme = ThemeGenerator.Create(folder, new GeneratedTheme
        {
            Name = "Note",
            Text = "</div><script>alert(1)</script> & breathe",
            FontName = "x';}</style><script>",
        });
        var html = Page(theme);
        Assert.DoesNotContain("<script>alert", html, StringComparison.Ordinal);
        Assert.Contains("&lt;/div&gt;&lt;script&gt;", html, StringComparison.Ordinal);
        Assert.Contains("&amp; breathe", html, StringComparison.Ordinal);
        Assert.Contains("font-family:'xstylescript'", html, StringComparison.Ordinal);
    }

    [Fact]
    public void ImageNamesAreWrittenAsSafeStrings()
    {
        var images = Path.Combine(folder, "pictures");
        Directory.CreateDirectory(images);
        // "</script>" can't be in a file name ("/" isn't allowed anywhere), but quotes and brackets can
        // (double quotes everywhere but Windows).
        var quote = OperatingSystem.IsWindows() ? "'" : "\"";
        var risky = $"it{quote}];alert(1);x.png";
        File.WriteAllText(Path.Combine(images, risky), "");
        File.WriteAllText(Path.Combine(images, "notes.txt"), "");
        var theme = ThemeGenerator.Create(folder, new GeneratedTheme { Name = "Slides", Kind = GeneratedThemeKind.Images, ImagesFolder = images });
        var html = Page(theme);
        Assert.Equal(2, html.Split("<script>").Length); // only the theme's own script
        Assert.DoesNotContain(quote + "];alert", html, StringComparison.Ordinal);
        Assert.Contains(OperatingSystem.IsWindows() ? "\\u0027];alert" : "\\u0022];alert", html, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(theme, "Documents", risky)));
        Assert.False(File.Exists(Path.Combine(theme, "Documents", "notes.txt")));
    }

    [Fact]
    public void YouTubeLinksAreChecked()
    {
        Assert.Throws<ThemeException>(() => ThemeGenerator.Create(folder, new GeneratedTheme { Kind = GeneratedThemeKind.YouTube, Url = "https://example.com" }));
        var theme = ThemeGenerator.Create(folder, new GeneratedTheme { Kind = GeneratedThemeKind.YouTube, Url = "https://youtu.be/dQw4w9WgXcQ" });
        Assert.Contains("youtube-nocookie.com/embed/dQw4w9WgXcQ", Page(theme), StringComparison.Ordinal);
    }

    [Fact]
    public void WebsitesMustBeWebAddresses()
    {
        Assert.Throws<ThemeException>(() => ThemeGenerator.Create(folder, new GeneratedTheme { Kind = GeneratedThemeKind.Website, Url = "file:///C:/Windows" }));
        Assert.Throws<ThemeException>(() => ThemeGenerator.Create(folder, new GeneratedTheme { Kind = GeneratedThemeKind.Website, Url = "javascript:alert(1)" }));
        var theme = ThemeGenerator.Create(folder, new GeneratedTheme { Kind = GeneratedThemeKind.Website, Url = "https://example.com/calm" });
        Assert.Contains("\"url\": \"https://example.com/calm\"", File.ReadAllText(Path.Combine(theme, "Info.json")), StringComparison.Ordinal);
    }

    [Fact]
    public void ExistingThemesAreNeverOverwritten()
    {
        var first = ThemeGenerator.Create(folder, new GeneratedTheme { Name = "Calm" });
        var second = ThemeGenerator.Create(folder, new GeneratedTheme { Name = "Calm" });
        var odd = ThemeGenerator.Create(folder, new GeneratedTheme { Name = "../a:b" });
        Assert.EndsWith("Calm.recesstheme", first, StringComparison.Ordinal);
        Assert.EndsWith("Calm 2.recesstheme", second, StringComparison.Ordinal);
        Assert.Equal(folder, Path.GetDirectoryName(odd));
    }
}
