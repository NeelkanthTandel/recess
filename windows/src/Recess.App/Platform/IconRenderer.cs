using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Win32;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Recess.App.Platform;

/// <summary>Turns a WPF drawing into a Windows icon handle, for the tray.</summary>
internal static class IconRenderer
{
    /// <summary>Draws at <paramref name="size"/> pixels square. The caller destroys the icon with <see cref="Destroy"/>.</summary>
    public static unsafe HICON Render(int size, Action<DrawingContext, double> draw)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            draw(dc, size);
        }
        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var pixels = new byte[size * size * 4];
        bitmap.CopyPixels(pixels, size * 4, 0);

        // WPF gives premultiplied alpha; icons want it straight.
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var a = pixels[i + 3];
            if (a is > 0 and < 255)
            {
                pixels[i] = (byte)Math.Min(255, pixels[i] * 255 / a);
                pixels[i + 1] = (byte)Math.Min(255, pixels[i + 1] * 255 / a);
                pixels[i + 2] = (byte)Math.Min(255, pixels[i + 2] * 255 / a);
            }
        }

        fixed (byte* p = pixels)
        {
            var color = PInvoke.CreateBitmap(size, size, 1, 32, p);
            var mask = PInvoke.CreateBitmap(size, size, 1, 1, null);
            try
            {
                var info = new ICONINFO { fIcon = true, hbmColor = color, hbmMask = mask };
                // The raw call: the caller owns the icon and destroys it when it's replaced.
                return PInvoke.CreateIconIndirect(&info);
            }
            finally
            {
                PInvoke.DeleteObject(color);
                PInvoke.DeleteObject(mask);
            }
        }
    }

    public static void Destroy(HICON icon)
    {
        if (!icon.IsNull)
        {
            PInvoke.DestroyIcon(icon);
        }
    }

    /// <summary>The small-icon size in pixels at the given DPI (16 at 100%, 24 at 150%, 32 at 200%).</summary>
    public static int SmallIconSize(uint dpi) => PInvoke.GetSystemMetricsForDpi(SYSTEM_METRICS_INDEX.SM_CXSMICON, dpi);

    /// <summary>Loads a PNG or ICO file as a WPF image, for drawing into icons.</summary>
    public static ImageSource? LoadImage(string path)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(path);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UriFormatException)
        {
            return null;
        }
    }

    public static Rect Square(double size, double inset) => new(inset, inset, size - 2 * inset, size - 2 * inset);
}
