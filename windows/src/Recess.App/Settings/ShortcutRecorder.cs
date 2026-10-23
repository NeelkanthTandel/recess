using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Recess.App.Platform;
using Recess.Core;

namespace Recess.App.Settings;

/// <summary>
/// Click, then press a key combination to set a shortcut. Esc cancels; Backspace or Delete
/// clears it. A shortcut needs Ctrl, Alt or Win (Shift alone isn't enough), or a function key.
/// </summary>
internal sealed class ShortcutRecorder : StackPanel
{
    private readonly HotKeys hotKeys;
    private readonly Func<KeyCombo?> get;
    private readonly Action<KeyCombo?> set;
    private readonly Button button = new() { MinWidth = 150, Padding = new Thickness(10, 4, 10, 4) };
    private readonly TextBlock warning = new() { Foreground = System.Windows.Media.Brushes.OrangeRed, FontSize = 12, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    private bool recording;

    public ShortcutRecorder(HotKeys hotKeys, Func<KeyCombo?> get, Action<KeyCombo?> set)
    {
        this.hotKeys = hotKeys;
        this.get = get;
        this.set = set;
        Orientation = Orientation.Horizontal;
        Children.Add(button);
        var clear = new Button { Content = "✕", Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(4, 0, 0, 0), ToolTip = "Remove this shortcut" };
        clear.Click += (_, _) =>
        {
            set(null);
            Update();
        };
        Children.Add(clear);
        Children.Add(warning);
        button.Click += (_, _) => Start();
        button.PreviewKeyDown += OnKey;
        button.LostKeyboardFocus += (_, _) => Stop();
        hotKeys.FailedChanged += Update;
        Unloaded += (_, _) => hotKeys.FailedChanged -= Update;
        Update();
    }

    public void Update()
    {
        var combo = get();
        button.Content = recording ? "Type a shortcut…" : combo is null ? "Record shortcut" : HotKeys.Describe(combo);
        warning.Text = combo is not null && hotKeys.Failed.Contains(combo) ? "Used by another app" : "";
    }

    private void Start()
    {
        recording = true;
        hotKeys.Suspended = true;
        button.Focus();
        Update();
    }

    private void Stop()
    {
        if (!recording)
        {
            return;
        }
        recording = false;
        hotKeys.Suspended = false;
        Update();
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (!recording)
        {
            return;
        }
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            return;
        }
        if (key == Key.Escape)
        {
            Stop();
            return;
        }
        var mods = Keyboard.Modifiers;
        if (key is Key.Back or Key.Delete && mods == ModifierKeys.None)
        {
            set(null);
            Stop();
            return;
        }
        uint flags = 0;
        if (mods.HasFlag(ModifierKeys.Control))
        {
            flags |= HotKeys.ModControl;
        }
        if (mods.HasFlag(ModifierKeys.Alt))
        {
            flags |= HotKeys.ModAlt;
        }
        if (mods.HasFlag(ModifierKeys.Shift))
        {
            flags |= HotKeys.ModShift;
        }
        if (Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin))
        {
            flags |= HotKeys.ModWin;
        }
        var isFunctionKey = key is >= Key.F1 and <= Key.F24;
        if ((flags & (HotKeys.ModControl | HotKeys.ModAlt | HotKeys.ModWin)) == 0 && !isFunctionKey)
        {
            return;
        }
        set(new KeyCombo((uint)KeyInterop.VirtualKeyFromKey(key), flags));
        Stop();
    }
}
