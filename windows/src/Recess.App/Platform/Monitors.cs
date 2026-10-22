using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;

namespace Recess.App.Platform;

/// <summary>A display, in physical pixels.</summary>
internal sealed record Monitor(IntPtr Handle, RECT Bounds, RECT WorkArea, bool IsPrimary);

internal static class Monitors
{
    public static unsafe List<Monitor> All()
    {
        var result = new List<Monitor>();
        PInvoke.EnumDisplayMonitors(HDC.Null, (RECT?)null, (monitor, _, _, _) =>
        {
            var info = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
            if (PInvoke.GetMonitorInfo(monitor, ref info))
            {
                result.Add(new Monitor(monitor, info.rcMonitor, info.rcWork, (info.dwFlags & PInvoke.MONITORINFOF_PRIMARY) != 0));
            }
            return true;
        }, 0);
        return result;
    }

    /// <summary>The display under the mouse pointer: the one the user is working on.</summary>
    public static IntPtr UnderCursor()
    {
        PInvoke.GetCursorPos(out var point);
        return PInvoke.MonitorFromPoint(point, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTOPRIMARY);
    }
}
