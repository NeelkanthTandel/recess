using Windows.Win32;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Recess.App.Platform;

internal static class Icons
{
    /// <summary>The app icon at the small-icon size for the given DPI, from Recess.ico next to the exe.</summary>
    public static unsafe HICON LoadAppIcon(uint dpi)
    {
        var size = PInvoke.GetSystemMetricsForDpi(SYSTEM_METRICS_INDEX.SM_CXSMICON, dpi);
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Recess.ico");
        // The raw overload: the friendly one wraps the icon in a SafeFileHandle, which would
        // eventually call CloseHandle on it.
        fixed (char* p = path)
        {
            var handle = PInvoke.LoadImage(default, p, GDI_IMAGE_TYPE.IMAGE_ICON, size, size, IMAGE_FLAGS.LR_LOADFROMFILE);
            return new HICON(handle.Value);
        }
    }
}
