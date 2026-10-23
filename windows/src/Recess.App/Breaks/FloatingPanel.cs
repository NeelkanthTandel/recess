using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Recess.App.Breaks;

/// <summary>
/// A small always-on-top panel that never takes keyboard focus from what you're doing: used for
/// "Ask me first", the None theme, and Recess's own notification banner.
/// </summary>
internal class FloatingPanel : Window
{
    public FloatingPanel()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new HWND(new WindowInteropHelper(this).Handle);
        var style = PInvoke.GetWindowLong(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        PInvoke.SetWindowLong(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE,
            style | (int)(WINDOW_EX_STYLE.WS_EX_NOACTIVATE | WINDOW_EX_STYLE.WS_EX_TOOLWINDOW));
    }

    /// <summary>A rounded card in the system's light or dark colours.</summary>
    protected static System.Windows.Controls.Border Card(UIElement content) => new()
    {
        CornerRadius = new CornerRadius(12),
        Padding = new Thickness(18),
        Margin = new Thickness(12),
        Child = content,
        Background = (Brush)Application.Current.FindResource(SystemColors.WindowBrushKey),
        BorderBrush = new SolidColorBrush(Color.FromArgb(50, 128, 128, 128)),
        BorderThickness = new Thickness(1),
        Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 16, ShadowDepth = 2, Opacity = 0.25 },
    };

    /// <summary>Places the panel in a corner of the work area of the display under the mouse.</summary>
    protected void PlaceInCorner(bool top)
    {
        var monitor = Platform.Monitors.All().FirstOrDefault(m => m.Handle == Platform.Monitors.UnderCursor())
            ?? Platform.Monitors.All().First();
        var source = PresentationSource.FromVisual(this);
        var scale = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var work = monitor.WorkArea;
        var bottomRight = scale.Transform(new Point(work.right, work.bottom));
        var topLeft = scale.Transform(new Point(work.left, work.top));
        Left = bottomRight.X - ActualWidth - 8;
        Top = top ? topLeft.Y + 8 : bottomRight.Y - ActualHeight - 8;
    }
}
