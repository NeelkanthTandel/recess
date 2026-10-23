using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Recess.App.Platform;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Recess.App.Actions;

/// <summary>The Flash Screen action: every display briefly turns white and fades back. Clicks go straight through it.</summary>
internal sealed class FlashWindow : Window
{
    private static readonly HWND HwndTopmost = new(-1);
    private readonly Windows.Win32.Foundation.RECT bounds;

    private FlashWindow(Windows.Win32.Foundation.RECT bounds)
    {
        this.bounds = bounds;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.White;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Opacity = 0.85;
    }

    public static void FlashAll()
    {
        foreach (var monitor in Monitors.All())
        {
            var window = new FlashWindow(monitor.Bounds);
            window.Show();
            var fade = new DoubleAnimation(0.85, 0, TimeSpan.FromMilliseconds(700)) { BeginTime = TimeSpan.FromMilliseconds(120) };
            fade.Completed += (_, _) => window.Close();
            window.BeginAnimation(OpacityProperty, fade);
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new HWND(new WindowInteropHelper(this).Handle);
        var style = PInvoke.GetWindowLong(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        PInvoke.SetWindowLong(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, style | (int)(WINDOW_EX_STYLE.WS_EX_TRANSPARENT
            | WINDOW_EX_STYLE.WS_EX_LAYERED | WINDOW_EX_STYLE.WS_EX_NOACTIVATE | WINDOW_EX_STYLE.WS_EX_TOOLWINDOW));
        PInvoke.SetWindowPos(hwnd, HwndTopmost, bounds.left, bounds.top, bounds.right - bounds.left, bounds.bottom - bounds.top,
            SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
    }
}
