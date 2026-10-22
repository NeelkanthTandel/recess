using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Recess.App.Break;

/// <summary>How a break window covers its display.</summary>
internal enum OverlayMode
{
    /// <summary>The display you're using: theme, countdown and buttons.</summary>
    Main,

    /// <summary>Another display, showing the same theme without buttons.</summary>
    Mirror,

    /// <summary>Another display, only darkened.</summary>
    Dim,
}

/// <summary>
/// One borderless, topmost window covering one display during a break. It is placed in
/// physical pixels so it fits displays with different scaling exactly.
/// </summary>
internal sealed class OverlayWindow : Window
{
    private static readonly HWND HwndTopmost = new(-1);

    private readonly Platform.Monitor monitor;
    private readonly OverlayMode mode;
    private bool allowClose;

    public OverlayWindow(Platform.Monitor monitor, OverlayMode mode, Color dim, UIElement? theme, UIElement? controls)
    {
        this.monitor = monitor;
        this.mode = mode;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = mode == OverlayMode.Main;
        Title = "Recess break";

        var root = new Grid();
        root.Children.Add(new Rectangle { Fill = new SolidColorBrush(dim) });
        if (theme is not null)
        {
            root.Children.Add(theme);
        }
        if (controls is not null)
        {
            root.Children.Add(controls);
        }
        Content = root;
        Opacity = 0;
    }

    public OverlayMode Mode => mode;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new HWND(new WindowInteropHelper(this).Handle);
        // A tool window stays out of Alt+Tab and the taskbar.
        var style = PInvoke.GetWindowLong(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        PInvoke.SetWindowLong(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, style | (int)WINDOW_EX_STYLE.WS_EX_TOOLWINDOW);
        PlaceOnMonitor();
        HwndSource.FromHwnd((IntPtr)hwnd).AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // WPF moves the window to the rectangle Windows suggests for the new scale; put it back
        // over the whole display once WPF has rescaled the content.
        if (msg == PInvoke.WM_DPICHANGED)
        {
            Dispatcher.BeginInvoke(PlaceOnMonitor, DispatcherPriority.Background);
        }
        return IntPtr.Zero;
    }

    private void PlaceOnMonitor()
    {
        var hwnd = new HWND(new WindowInteropHelper(this).Handle);
        var r = monitor.Bounds;
        PInvoke.SetWindowPos(hwnd, HwndTopmost, r.left, r.top, r.right - r.left, r.bottom - r.top,
            SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
    }

    public IntPtr Handle => new WindowInteropHelper(this).Handle;

    /// <summary>Fades in or out over the given time.</summary>
    public void Fade(double to, TimeSpan duration, Action? done = null)
    {
        var animation = new System.Windows.Media.Animation.DoubleAnimation(to, duration);
        if (done is not null)
        {
            animation.Completed += (_, _) => done();
        }
        BeginAnimation(OpacityProperty, animation);
    }

    /// <summary>Closes for real; Alt+F4 and other close requests are ignored during a break.</summary>
    public void CloseNow()
    {
        allowClose = true;
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        // Release the theme's browser, if it has one, along with the window.
        if (Content is Panel root)
        {
            foreach (var child in root.Children.OfType<IDisposable>())
            {
                child.Dispose();
            }
        }
        base.OnClosed(e);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        e.Cancel = !allowClose;
        base.OnClosing(e);
    }
}
