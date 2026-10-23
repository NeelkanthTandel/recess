using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Recess.App.Platform;

/// <summary>
/// The notification-area icon: Recess's counterpart to the Mac menu bar item.
/// A thin wrapper over Shell_NotifyIcon that opens a WPF context menu.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private const uint CallbackMessage = 0x8000 + 1; // WM_APP + 1
    private const uint IconId = 1;
    private const uint NinSelect = 0x400; // WM_USER: a left click
    private const uint NinKeySelect = 0x401; // NIN_SELECT | NINF_KEY: Enter or Space on the icon
    private const uint WmContextMenu = 0x7B;

    private readonly HwndSource window;
    private readonly uint taskbarCreated;
    private HICON icon;
    private string tooltip;
    private bool added;

    /// <summary>Raised on a left click, or Enter on the focused icon.</summary>
    public event Action? Activated;

    /// <summary>Raised before the menu opens; the bool is true when Shift is held.</summary>
    public event Action<ContextMenu, bool>? MenuOpening;

    public TrayIcon(string tooltip)
    {
        this.tooltip = tooltip;
        // A hidden top-level window (not message-only) so it also hears TaskbarCreated,
        // which Explorer broadcasts when it restarts and every tray icon has to be added again.
        var parameters = new HwndSourceParameters("RecessTray")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0,
            ExtendedWindowStyle = (int)WINDOW_EX_STYLE.WS_EX_TOOLWINDOW,
        };
        window = new HwndSource(parameters);
        window.AddHook(WndProc);
        taskbarCreated = PInvoke.RegisterWindowMessage("TaskbarCreated");
    }

    public void Show(HICON newIcon)
    {
        icon = newIcon;
        Update(add: !added);
    }

    public void SetTooltip(string text)
    {
        tooltip = text;
        if (added)
        {
            Update(add: false);
        }
    }

    private unsafe void Update(bool add)
    {
        var data = NewData();
        data.uFlags = NOTIFY_ICON_DATA_FLAGS.NIF_MESSAGE | NOTIFY_ICON_DATA_FLAGS.NIF_ICON | NOTIFY_ICON_DATA_FLAGS.NIF_TIP
            | NOTIFY_ICON_DATA_FLAGS.NIF_SHOWTIP;
        data.uCallbackMessage = CallbackMessage;
        data.hIcon = icon;
        // The tooltip holds at most 127 characters.
        data.szTip = tooltip.Length > 127 ? tooltip[..127] : tooltip;
        if (add)
        {
            added = PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_ADD, &data);
            data.Anonymous.uVersion = PInvoke.NOTIFYICON_VERSION_4;
            PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_SETVERSION, &data);
        }
        else
        {
            PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_MODIFY, &data);
        }
    }

    private NOTIFYICONDATAW NewData() => new()
    {
        cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
        hWnd = new HWND(window.Handle),
        uID = IconId,
    };

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == taskbarCreated)
        {
            added = false;
            Update(add: true);
        }
        else if (msg == CallbackMessage)
        {
            // With NOTIFYICON_VERSION_4 the event is in the low word of lParam.
            var evt = (uint)(lParam.ToInt64() & 0xFFFF);
            switch (evt)
            {
                case NinSelect:
                case NinKeySelect:
                    Activated?.Invoke();
                    handled = true;
                    break;
                case WmContextMenu:
                    OpenMenu();
                    handled = true;
                    break;
            }
        }
        return IntPtr.Zero;
    }

    /// <summary>Opens the menu at the mouse pointer.</summary>
    public void ShowMenu() => OpenMenu();

    private void OpenMenu()
    {
        var menu = new ContextMenu { Placement = PlacementMode.MousePoint };
        var shift = (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Shift) != 0;
        MenuOpening?.Invoke(menu, shift);
        if (menu.Items.Count == 0)
        {
            return;
        }
        // The menu only closes on an outside click if its own window is in the foreground.
        menu.Opened += (_, _) =>
        {
            if (PresentationSource.FromVisual(menu) is HwndSource source)
            {
                PInvoke.SetForegroundWindow(new HWND(source.Handle));
            }
        };
        menu.IsOpen = true;
    }

    public unsafe void Dispose()
    {
        if (added)
        {
            var data = NewData();
            PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_DELETE, &data);
            added = false;
        }
        window.Dispose();
    }
}
