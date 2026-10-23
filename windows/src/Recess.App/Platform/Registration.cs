using Microsoft.Win32;

namespace Recess.App.Platform;

/// <summary>Per-user registry entries: the recess:// link handler and starting at sign-in. Nothing needs admin rights.</summary>
internal static class Registration
{
    private const string ProtocolKey = @"Software\Classes\recess";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ValueName = "Recess";

    private static string Exe => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Recess.exe");

    /// <summary>
    /// Registers recess:// links if nothing handles them yet, or if the registered program no
    /// longer exists. An installed copy's registration is left alone by a portable copy.
    /// </summary>
    public static void EnsureProtocol()
    {
        using var existing = Registry.CurrentUser.OpenSubKey($@"{ProtocolKey}\shell\open\command");
        if (existing?.GetValue(null) is string command && File.Exists(ExeFromCommand(command)))
        {
            return;
        }
        using var key = Registry.CurrentUser.CreateSubKey(ProtocolKey);
        key.SetValue(null, "URL:Recess");
        key.SetValue("URL Protocol", "");
        using (var icon = key.CreateSubKey("DefaultIcon"))
        {
            icon.SetValue(null, $"\"{Exe}\",0");
        }
        using var open = key.CreateSubKey(@"shell\open\command");
        open.SetValue(null, $"\"{Exe}\" \"%1\"");
    }

    private static string ExeFromCommand(string command)
    {
        var trimmed = command.TrimStart();
        if (trimmed.StartsWith('"'))
        {
            var end = trimmed.IndexOf('"', 1);
            return end > 0 ? trimmed[1..end] : "";
        }
        var space = trimmed.IndexOf(' ');
        return space > 0 ? trimmed[..space] : trimmed;
    }

    /// <summary>Whether Recess starts when you sign in.</summary>
    public static bool LaunchAtLogin
    {
        get
        {
            using var run = Registry.CurrentUser.OpenSubKey(RunKey);
            return run?.GetValue(ValueName) is string;
        }
        set
        {
            using var run = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value)
            {
                run.SetValue(ValueName, $"\"{Exe}\"");
            }
            else if (run.GetValue(ValueName) is not null)
            {
                run.DeleteValue(ValueName);
            }
        }
    }

    /// <summary>True if the user turned Recess off under Startup apps (Settings or Task Manager), which overrides the Run entry.</summary>
    public static bool LaunchAtLoginBlockedByUser
    {
        get
        {
            using var approved = Registry.CurrentUser.OpenSubKey(StartupApprovedKey);
            // The first byte is 2 (or 6) when enabled and 3 (or 7) when the user disabled it.
            return approved?.GetValue(ValueName) is byte[] { Length: > 0 } data && (data[0] & 1) == 1;
        }
    }

    /// <summary>If Recess starts at sign-in but its program was moved or deleted, points the entry at this copy.</summary>
    public static void RepairLaunchAtLogin()
    {
        using var run = Registry.CurrentUser.OpenSubKey(RunKey);
        if (run?.GetValue(ValueName) is string command && !File.Exists(ExeFromCommand(command)))
        {
            LaunchAtLogin = true;
        }
    }
}
