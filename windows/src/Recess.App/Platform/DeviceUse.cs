using Microsoft.Win32;

namespace Recess.App.Platform;

/// <summary>
/// Whether any app is using the microphone or camera right now, which usually means a call.
/// Windows records this per app (it's what lights the microphone icon in the taskbar). The
/// registry layout isn't documented, so this is best effort.
/// </summary>
internal static class DeviceUse
{
    private const string Store = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore";

    public static bool MicOrCameraInUse()
    {
        try
        {
            return InUse("microphone") || InUse("webcam");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return false;
        }
    }

    private static bool InUse(string device)
    {
        using var root = Registry.CurrentUser.OpenSubKey($@"{Store}\{device}");
        if (root is null)
        {
            return false;
        }
        // Store apps (like the new Teams) are listed directly; desktop apps under NonPackaged.
        foreach (var name in root.GetSubKeyNames())
        {
            if (name == "NonPackaged")
            {
                using var desktop = root.OpenSubKey(name);
                foreach (var app in desktop?.GetSubKeyNames() ?? [])
                {
                    using var key = desktop!.OpenSubKey(app);
                    // The key name is the program's path with # for \; a program that crashed
                    // mid-call leaves "in use" behind, so it must still be running.
                    if (Active(key) && Processes.Running.Contains(Path.GetFileName(app.Replace('#', '\\'))))
                    {
                        return true;
                    }
                }
            }
            else
            {
                using var key = root.OpenSubKey(name);
                if (Active(key))
                {
                    return true;
                }
            }
        }
        return false;
    }

    // Started, and not yet stopped.
    private static bool Active(RegistryKey? key) =>
        key?.GetValue("LastUsedTimeStart") is long start && start > 0 && key.GetValue("LastUsedTimeStop") is long stop && stop == 0;
}
