using System.Windows;
using System.Windows.Controls;
using Recess.App.Breaks;
using Recess.Core;

namespace Recess.App.Settings;

/// <summary>"New theme": text, a website, a YouTube video, or a folder of images.</summary>
internal sealed class ThemeEditorWindow : Window
{
    private readonly ThemeCatalog themes;
    private readonly GeneratedTheme config = new();
    private readonly TextBlock error = new() { Foreground = System.Windows.Media.Brushes.OrangeRed, TextWrapping = TextWrapping.Wrap };
    private Theme? created;

    private ThemeEditorWindow(ThemeCatalog themes)
    {
        this.themes = themes;
        Title = "New theme";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var f = new Form(() => { });
        var s = f.Section();
        f.Row(s, "Name", f.TextField(() => config.Name, v => config.Name = v));
        f.Row(s, "Type", f.EnumPicker(v => v.Title(), () => config.Kind, v =>
        {
            config.Kind = v;
            f.Refresh();
        }));

        var text = f.Full(s, f.TextField(() => config.Text, v => config.Text = v, double.NaN, multiline: true));
        var font = f.Row(s, "Font", f.TextField(() => config.FontName, v => config.FontName = v));
        var size = f.Row(s, "Size", f.NumberField(() => config.FontSize, v => config.FontSize = v, 12, 200, "pixels"));
        var colour = f.Row(s, "Colour", f.ColorField(() => config.Foreground, v => config.Foreground = v, opacity: false));
        foreach (var e in new UIElement[] { text, font, size, colour })
        {
            f.VisibleWhen(e, () => config.Kind == GeneratedThemeKind.Text);
        }
        var url = f.Row(s, "Address", f.TextField(() => config.Url, v => config.Url = v));
        f.VisibleWhen(url, () => config.Kind is GeneratedThemeKind.Website or GeneratedThemeKind.YouTube);

        var folderName = new TextBlock { Opacity = 0.7, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        f.OnRefresh(() => folderName.Text = config.ImagesFolder is { } p ? Path.GetFileName(p) : "None");
        var folder = f.Row(s, "Folder", Form.Line(folderName, Form.Button("Choose…", () =>
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Choose a folder of images" };
            if (dialog.ShowDialog(this) == true)
            {
                config.ImagesFolder = dialog.FolderName;
                f.Refresh();
            }
        })));
        var cycle = f.Row(s, "Change every", f.NumberField(() => config.CycleSeconds, v => config.CycleSeconds = v, 2, 600, "seconds"));
        var random = f.Full(s, f.Toggle("Random order", () => config.RandomOrder, v => config.RandomOrder = v));
        foreach (var e in new UIElement[] { folder, cycle, random })
        {
            f.VisibleWhen(e, () => config.Kind == GeneratedThemeKind.Images);
        }
        f.Root.Children.Add(error);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Padding = new Thickness(14, 4, 14, 4), Margin = new Thickness(0, 0, 8, 0) };
        var create = new Button { Content = "Create", IsDefault = true, Padding = new Thickness(14, 4, 14, 4) };
        create.Click += (_, _) => Create();
        buttons.Children.Add(cancel);
        buttons.Children.Add(create);
        f.Root.Children.Add(buttons);
        Content = f.Root;
    }

    /// <summary>Shows the window; returns the new theme, or null if cancelled.</summary>
    public static Theme? Create(ThemeCatalog themes, Window? owner)
    {
        var window = new ThemeEditorWindow(themes) { Owner = owner };
        window.ShowDialog();
        return window.created;
    }

    private void Create()
    {
        if (string.IsNullOrWhiteSpace(config.Name))
        {
            error.Text = "Give the theme a name.";
            return;
        }
        try
        {
            var folder = ThemeGenerator.Create(themes.UserFolder, config);
            themes.Reload();
            created = themes.Themes.FirstOrDefault(t => string.Equals(t.Folder, folder, StringComparison.OrdinalIgnoreCase));
            DialogResult = true;
        }
        catch (ThemeException ex)
        {
            error.Text = ex.Message;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error.Text = "Couldn't create the theme: " + ex.Message;
        }
    }
}
