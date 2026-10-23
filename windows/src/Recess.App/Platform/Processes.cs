using System.Diagnostics;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Threading;

namespace Recess.App.Platform;

/// <summary>Which apps are running and which one is in front, by program file name (e.g. "Zoom.exe").</summary>
internal static class Processes
{
    private static readonly object Gate = new();
    private static HashSet<string> running = new(StringComparer.OrdinalIgnoreCase);
    private static DateTime runningAt = DateTime.MinValue;

    /// <summary>File names of running programs, refreshed at most every few seconds.</summary>
    public static IReadOnlySet<string> Running
    {
        get
        {
            lock (Gate)
            {
                if ((DateTime.UtcNow - runningAt).TotalSeconds > 3)
                {
                    var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var p in Process.GetProcesses())
                    {
                        names.Add(p.ProcessName + ".exe");
                        p.Dispose();
                    }
                    running = names;
                    runningAt = DateTime.UtcNow;
                }
                return running;
            }
        }
    }

    /// <summary>The front window's program, or null for the desktop and when nothing is in front.</summary>
    public static (string Id, string Name, uint ProcessId)? Front()
    {
        var hwnd = PInvoke.GetForegroundWindow();
        if (hwnd.IsNull || hwnd == PInvoke.GetShellWindow())
        {
            return null;
        }
        var pid = ProcessId(hwnd);
        var path = ImagePath(pid);
        // Store apps run inside ApplicationFrameHost; the real app owns one of its child windows.
        if (path is not null && Path.GetFileName(path).Equals("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase))
        {
            var host = pid;
            PInvoke.EnumChildWindows(hwnd, (child, _) =>
            {
                var childPid = ProcessId(child);
                if (childPid != host)
                {
                    pid = childPid;
                    return false;
                }
                return true;
            }, 0);
            path = ImagePath(pid) ?? path;
        }
        if (path is null)
        {
            return null;
        }
        return (Path.GetFileName(path), DisplayName(path), pid);
    }

    public static uint ProcessId(HWND hwnd)
    {
        uint pid;
        unsafe
        {
            PInvoke.GetWindowThreadProcessId(hwnd, &pid);
        }
        return pid;
    }

    /// <summary>The full path of a process's program, using the limited access that works for most processes.</summary>
    public static unsafe string? ImagePath(uint pid)
    {
        if (pid == 0)
        {
            return null;
        }
        var handle = PInvoke.OpenProcess(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (handle.IsNull)
        {
            return null;
        }
        try
        {
            Span<char> buffer = stackalloc char[1024];
            var size = (uint)buffer.Length;
            fixed (char* p = buffer)
            {
                return PInvoke.QueryFullProcessImageName(handle, PROCESS_NAME_FORMAT.PROCESS_NAME_WIN32, p, &size)
                    ? new string(p, 0, (int)size)
                    : null;
            }
        }
        finally
        {
            PInvoke.CloseHandle(handle);
        }
    }

    /// <summary>A program's friendly name from its version info, e.g. "Microsoft Teams" for ms-teams.exe.</summary>
    public static string DisplayName(string path)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            if (!string.IsNullOrWhiteSpace(info.FileDescription))
            {
                return info.FileDescription.Trim();
            }
        }
        catch (FileNotFoundException)
        {
        }
        return Path.GetFileNameWithoutExtension(path);
    }
}
