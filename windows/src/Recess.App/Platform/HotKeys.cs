using System.Windows.Input;
using System.Windows.Interop;
using Recess.Core;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace Recess.App.Platform;

/// <summary>Keyboard shortcuts that work in every app, via RegisterHotKey.</summary>
internal sealed class HotKeys : IDisposable
{
    public const uint ModAlt = 0x1;
    public const uint ModControl = 0x2;
    public const uint ModShift = 0x4;
    public const uint ModWin = 0x8;
    private const uint ModNoRepeat = 0x4000;

    private readonly HwndSource window;
    private readonly Dictionary<int, Action> actions = [];
    private readonly HashSet<KeyCombo> failed = [];
    private List<(KeyCombo Combo, Action Run)> wanted = [];
    private bool suspended;

    public HotKeys()
    {
        window = new HwndSource(new HwndSourceParameters("RecessHotKeys") { Width = 0, Height = 0, WindowStyle = 0 });
        window.AddHook(WndProc);
    }

    /// <summary>Shortcuts another app already uses, so Settings can say so.</summary>
    public IReadOnlySet<KeyCombo> Failed => failed;

    public event Action? FailedChanged;

    /// <summary>Replaces every registered shortcut.</summary>
    public void Set(IEnumerable<(KeyCombo Combo, Action Run)> shortcuts)
    {
        wanted = shortcuts.DistinctBy(s => s.Combo).ToList();
        if (!suspended)
        {
            RegisterAll();
        }
    }

    /// <summary>While recording a new shortcut, the existing ones are released so the keys reach the recorder.</summary>
    public bool Suspended
    {
        get => suspended;
        set
        {
            suspended = value;
            if (value)
            {
                UnregisterAll();
            }
            else
            {
                RegisterAll();
            }
        }
    }

    private void RegisterAll()
    {
        UnregisterAll();
        var before = failed.Count;
        failed.Clear();
        var id = 1;
        foreach (var (combo, run) in wanted)
        {
            if (PInvoke.RegisterHotKey(new HWND(window.Handle), id, (HOT_KEY_MODIFIERS)(combo.Modifiers | ModNoRepeat), combo.Key))
            {
                actions[id] = run;
            }
            else
            {
                failed.Add(combo);
            }
            id++;
        }
        if (failed.Count > 0 || before > 0)
        {
            FailedChanged?.Invoke();
        }
    }

    private void UnregisterAll()
    {
        foreach (var id in actions.Keys)
        {
            PInvoke.UnregisterHotKey(new HWND(window.Handle), id);
        }
        actions.Clear();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == PInvoke.WM_HOTKEY && actions.TryGetValue((int)wParam, out var run))
        {
            handled = true;
            run();
        }
        return IntPtr.Zero;
    }

    /// <summary>"Ctrl+Alt+B", for showing a shortcut.</summary>
    public static string Describe(KeyCombo combo)
    {
        var parts = new List<string>();
        if ((combo.Modifiers & ModControl) != 0)
        {
            parts.Add("Ctrl");
        }
        if ((combo.Modifiers & ModAlt) != 0)
        {
            parts.Add("Alt");
        }
        if ((combo.Modifiers & ModShift) != 0)
        {
            parts.Add("Shift");
        }
        if ((combo.Modifiers & ModWin) != 0)
        {
            parts.Add("Win");
        }
        parts.Add(KeyName(combo.Key));
        return string.Join('+', parts);
    }

    public static string KeyName(uint vk)
    {
        if (vk > 0xFE)
        {
            return "?";
        }
        var key = KeyInterop.KeyFromVirtualKey((int)vk);
        return key switch
        {
            >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
            >= Key.NumPad0 and <= Key.NumPad9 => "Num " + (key - Key.NumPad0),
            Key.OemComma => ",",
            Key.OemPeriod => ".",
            Key.OemMinus => "-",
            Key.OemPlus => "=",
            Key.OemQuestion => "/",
            Key.OemSemicolon => ";",
            Key.OemQuotes => "'",
            Key.OemOpenBrackets => "[",
            Key.OemCloseBrackets => "]",
            Key.OemPipe => "\\",
            Key.OemTilde => "`",
            Key.None => "?",
            _ => key.ToString(),
        };
    }

    public void Dispose()
    {
        UnregisterAll();
        window.Dispose();
    }
}
