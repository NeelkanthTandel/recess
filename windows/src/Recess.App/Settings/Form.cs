using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Recess.Core;

namespace Recess.App.Settings;

/// <summary>
/// Builds settings pages out of sections and rows, like the Mac's grouped forms. Controls read
/// and write through small get/set functions; <see cref="Changed"/> runs after every edit, and
/// <see cref="Refresh"/> brings live labels and enabled states up to date.
/// </summary>
internal sealed class Form
{
    private readonly List<Action> refreshers = [];

    public Form(Action changed)
    {
        Changed = changed;
    }

    public StackPanel Root { get; } = new() { Margin = new Thickness(24, 16, 24, 24) };

    /// <summary>Called after any control changes a value.</summary>
    public Action Changed { get; }

    public void Refresh()
    {
        foreach (var r in refreshers)
        {
            r();
        }
    }

    /// <summary>Adds something that updates itself on <see cref="Refresh"/>.</summary>
    public void OnRefresh(Action refresh)
    {
        refreshers.Add(refresh);
        refresh();
    }

    public static ScrollViewer Scroll(UIElement content) => new()
    {
        Content = content,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
    };

    // MARK: - Layout

    public StackPanel Section(string? title = null, StackPanel? parent = null)
    {
        var target = parent ?? Root;
        if (title is not null)
        {
            target.Children.Add(new TextBlock
            {
                Text = title,
                FontWeight = FontWeights.SemiBold,
                FontSize = 14,
                Margin = new Thickness(4, target.Children.Count == 0 ? 0 : 18, 0, 6),
            });
        }
        var body = new StackPanel();
        target.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 6, 14, 6),
            Margin = new Thickness(0, title is null && target.Children.Count > 0 ? 12 : 0, 0, 0),
            Background = new SolidColorBrush(Color.FromArgb(20, 128, 128, 128)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(36, 128, 128, 128)),
            BorderThickness = new Thickness(1),
            Child = body,
        });
        return body;
    }

    /// <summary>A labelled row: label on the left, control on the right.</summary>
    public FrameworkElement Row(StackPanel section, string label, UIElement control, string? help = null)
    {
        var grid = new Grid { Margin = new Thickness(0, 6, 0, 6), ToolTip = help };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 160 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 16, 0) };
        grid.Children.Add(text);
        Grid.SetColumn(control, 1);
        if (control is FrameworkElement fe)
        {
            fe.VerticalAlignment = VerticalAlignment.Center;
        }
        grid.Children.Add(control);
        section.Children.Add(grid);
        return grid;
    }

    /// <summary>A control that takes the whole row.</summary>
    public T Full<T>(StackPanel section, T control)
        where T : FrameworkElement
    {
        control.Margin = new Thickness(0, 6, 0, 6);
        section.Children.Add(control);
        return control;
    }

    public TextBlock Hint(StackPanel section, string text)
    {
        var hint = new TextBlock { Text = text, Opacity = 0.65, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 6) };
        section.Children.Add(hint);
        return hint;
    }

    /// <summary>A hint whose text is worked out again on every refresh.</summary>
    public TextBlock LiveHint(StackPanel section, Func<string> text)
    {
        var hint = Hint(section, "");
        OnRefresh(() =>
        {
            var t = text();
            hint.Text = t;
            hint.Visibility = t.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        });
        return hint;
    }

    public TextBlock Value(Func<string> text)
    {
        var block = new TextBlock { Opacity = 0.75, TextWrapping = TextWrapping.Wrap, MaxWidth = 420, TextAlignment = TextAlignment.Right };
        OnRefresh(() => block.Text = text());
        return block;
    }

    public static StackPanel Line(params UIElement[] items)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var item in items)
        {
            if (item is FrameworkElement fe)
            {
                fe.VerticalAlignment = VerticalAlignment.Center;
                if (fe.Margin == default)
                {
                    fe.Margin = new Thickness(0, 0, 8, 0);
                }
            }
            line.Children.Add(item);
        }
        return line;
    }

    public static TextBlock Text(string text) => new() { Text = text, VerticalAlignment = VerticalAlignment.Center };

    /// <summary>Keeps a control enabled only while a condition holds.</summary>
    public void EnabledWhen(UIElement element, Func<bool> condition) => OnRefresh(() => element.IsEnabled = condition());

    /// <summary>Shows a control only while a condition holds.</summary>
    public void VisibleWhen(UIElement element, Func<bool> condition) =>
        OnRefresh(() => element.Visibility = condition() ? Visibility.Visible : Visibility.Collapsed);

    // MARK: - Controls

    public CheckBox Toggle(string label, Func<bool> get, Action<bool> set, string? help = null)
    {
        var box = new CheckBox { Content = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap }, ToolTip = help, Margin = new Thickness(0, 6, 0, 6) };
        var updating = false;
        OnRefresh(() =>
        {
            updating = true;
            box.IsChecked = get();
            updating = false;
        });
        box.Click += (_, _) =>
        {
            if (!updating)
            {
                set(box.IsChecked == true);
                Changed();
            }
        };
        return box;
    }

    /// <summary>A check box followed by other controls on the same line, e.g. "Allow at most [3] postpones per [hour]".</summary>
    public StackPanel ToggleLine(Func<bool> get, Action<bool> set, string? help, params UIElement[] rest)
    {
        var box = new CheckBox { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        OnRefresh(() => box.IsChecked = get());
        box.Click += (_, _) =>
        {
            set(box.IsChecked == true);
            Changed();
        };
        var line = Line([box, .. rest]);
        line.Margin = new Thickness(0, 6, 0, 6);
        line.ToolTip = help;
        foreach (var item in rest)
        {
            if (item is Control or Panel)
            {
                EnabledWhen(item, get);
            }
        }
        return line;
    }

    public ComboBox Picker<T>(IEnumerable<(T Value, string Title)> options, Func<T> get, Action<T> set, double minWidth = 0)
    {
        var list = options.ToList();
        var combo = new ComboBox { MinWidth = minWidth };
        foreach (var (_, title) in list)
        {
            combo.Items.Add(title);
        }
        var updating = false;
        OnRefresh(() =>
        {
            updating = true;
            combo.SelectedIndex = list.FindIndex(o => EqualityComparer<T>.Default.Equals(o.Value, get()));
            updating = false;
        });
        combo.SelectionChanged += (_, _) =>
        {
            if (!updating && combo.SelectedIndex >= 0)
            {
                set(list[combo.SelectedIndex].Value);
                Changed();
            }
        };
        return combo;
    }

    public ComboBox EnumPicker<T>(Func<T, string> title, Func<T> get, Action<T> set, IEnumerable<T>? values = null)
        where T : struct, Enum => Picker((values ?? Enum.GetValues<T>()).Select(v => (v, title(v))), get, set);

    public TextBox TextField(Func<string> get, Action<string> set, double width = 260, string? placeholder = null, bool multiline = false)
    {
        var box = new TextBox { Width = width, AcceptsReturn = multiline, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap };
        if (multiline)
        {
            box.MinHeight = 80;
            box.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        }
        if (placeholder is not null)
        {
            box.ToolTip = placeholder;
        }
        OnRefresh(() =>
        {
            if (!box.IsKeyboardFocused)
            {
                box.Text = get();
            }
        });
        box.TextChanged += (_, _) =>
        {
            if (box.IsKeyboardFocused && box.Text != get())
            {
                set(box.Text);
                Changed();
            }
        };
        return box;
    }

    /// <summary>A number box that accepts values within a range when you leave it or press Enter.</summary>
    public StackPanel NumberField(Func<double> get, Action<double> set, double min, double max, string? suffix = null, double width = 64)
    {
        var box = new TextBox { Width = width, TextAlignment = TextAlignment.Right };
        OnRefresh(() =>
        {
            if (!box.IsKeyboardFocused)
            {
                box.Text = get().ToString("0.##", CultureInfo.CurrentCulture);
            }
        });
        void Commit()
        {
            if (double.TryParse(box.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var v) && double.IsFinite(v))
            {
                v = Math.Clamp(v, min, max);
                if (v != get())
                {
                    set(v);
                    Changed();
                }
            }
            box.Text = get().ToString("0.##", CultureInfo.CurrentCulture);
        }
        box.LostKeyboardFocus += (_, _) => Commit();
        box.KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                Commit();
            }
        };
        return suffix is null ? Line(box) : Line(box, Text(suffix));
    }

    public StackPanel IntField(Func<int> get, Action<int> set, int min, int max, string? suffix = null) =>
        NumberField(() => get(), v => set((int)Math.Round(v)), min, max, suffix, 52);

    /// <summary>An amount plus units, e.g. [10] [minutes].</summary>
    public StackPanel IntervalField(Func<Interval> get, Action<Interval> set, TimeUnit[]? units = null, double minimum = 0)
    {
        units ??= [TimeUnit.Seconds, TimeUnit.Minutes, TimeUnit.Hours];
        var amount = NumberField(() => get().Amount, v => set(get() with { Amount = v }), 0, 100000);
        var unit = Picker(units.Select(u => (u, u.Name(plural: true))), () => get().Units, u => set(get() with { Units = u }));
        unit.Margin = new Thickness(0);
        var line = Line(amount, unit);
        // Keep the total at or above the minimum, whatever the units.
        var guard = Changed;
        line.LostKeyboardFocus += (_, _) =>
        {
            if (get().TotalSeconds < minimum)
            {
                set(Interval.FromSeconds(minimum));
                guard();
                Refresh();
            }
        };
        return line;
    }

    /// <summary>A time of day as hours and minutes.</summary>
    public StackPanel TimeField(Func<TimeOfDay> get, Action<TimeOfDay> set)
    {
        var hours = Picker(Enumerable.Range(0, 24).Select(h => (h, h.ToString("00", CultureInfo.InvariantCulture))), () => get().Hour, h => set(get() with { Hour = h }));
        var minutes = Picker(Enumerable.Range(0, 12).Select(m => (m * 5, (m * 5).ToString("00", CultureInfo.InvariantCulture))),
            () => get().Minute - get().Minute % 5, m => set(get() with { Minute = m }));
        minutes.Margin = new Thickness(0);
        return Line(hours, Text(":"), minutes);
    }

    public Slider SliderField(Func<double> get, Action<double> set, double min, double max)
    {
        var slider = new Slider { Minimum = min, Maximum = max, Width = 200 };
        var updating = false;
        OnRefresh(() =>
        {
            updating = true;
            slider.Value = get();
            updating = false;
        });
        slider.ValueChanged += (_, _) =>
        {
            if (!updating)
            {
                set(slider.Value);
                Changed();
            }
        };
        return slider;
    }

    public static Button Button(string title, Action click, string? help = null)
    {
        var button = new Button { Content = title, Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 8, 0), ToolTip = help };
        button.Click += (_, _) => click();
        return button;
    }

    /// <summary>A colour swatch that opens a palette with a hex box (and opacity, if wanted).</summary>
    public Button ColorField(Func<Rgba> get, Action<Rgba> set, bool opacity)
    {
        var swatch = new Border { Width = 28, Height = 16, CornerRadius = new CornerRadius(3), BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1) };
        var button = new Button { Content = swatch, Padding = new Thickness(6, 3, 6, 3) };
        OnRefresh(() => swatch.Background = new SolidColorBrush(Tray.TrayGlyphs.ToColor(get())));
        button.Click += (_, _) =>
        {
            var popup = new Popup { PlacementTarget = button, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true };
            popup.Child = ColorPalette(get, v =>
            {
                set(v);
                Changed();
                Refresh();
            }, opacity, () => popup.IsOpen = false);
            popup.IsOpen = true;
        };
        return button;
    }

    private static readonly Rgba[] Palette =
    [
        new(0.22, 0.55, 0.85), new(0.95, 0.6, 0.2), new(0.2, 0.6, 0.4), new(0.85, 0.3, 0.35), new(0.6, 0.4, 0.8), new(0.3, 0.7, 0.75),
        new(0.95, 0.75, 0.2), new(0.55, 0.55, 0.6), new(0.08, 0.1, 0.14), new(0, 0, 0), new(0.15, 0.2, 0.35), new(1, 1, 1),
    ];

    private static Border ColorPalette(Func<Rgba> get, Action<Rgba> set, bool opacity, Action close)
    {
        var stack = new StackPanel();
        var grid = new UniformGrid { Columns = 6 };
        foreach (var c in Palette)
        {
            var b = new Button
            {
                Width = 26,
                Height = 26,
                Margin = new Thickness(3),
                Background = new SolidColorBrush(Tray.TrayGlyphs.ToColor(c)),
                ToolTip = c.Hex,
            };
            b.Click += (_, _) =>
            {
                set(c with { A = get().A });
                close();
            };
            grid.Children.Add(b);
        }
        stack.Children.Add(grid);
        var hex = new TextBox { Text = get().Hex, Width = 90, Margin = new Thickness(3, 8, 3, 0) };
        hex.KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter && ParseHex(hex.Text) is { } c)
            {
                set(c with { A = get().A });
                close();
            }
        };
        stack.Children.Add(Line(Text("Hex"), hex));
        if (opacity)
        {
            var slider = new Slider { Minimum = 0, Maximum = 1, Value = get().A, Width = 150 };
            slider.ValueChanged += (_, _) => set(get() with { A = slider.Value });
            stack.Children.Add(Line(Text("Opacity"), slider));
        }
        return new Border
        {
            Padding = new Thickness(10),
            CornerRadius = new CornerRadius(8),
            Background = (Brush)Application.Current.FindResource(SystemColors.WindowBrushKey),
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(1),
            Child = stack,
        };
    }

    public static Rgba? ParseHex(string text)
    {
        var s = text.Trim().TrimStart('#');
        if (s.Length == 6 && int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v))
        {
            return new Rgba(((v >> 16) & 0xFF) / 255.0, ((v >> 8) & 0xFF) / 255.0, (v & 0xFF) / 255.0);
        }
        return null;
    }
}
