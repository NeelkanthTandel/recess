namespace Recess.Core;

/// <summary>
/// Decides which script files may run. Scripts only ever run from the user's Scripts folder,
/// by name, and the name has to be a plain file name that Windows and cmd.exe can't
/// reinterpret.
/// </summary>
public static class ScriptPaths
{
    public static readonly string[] Extensions = [".ps1", ".bat", ".cmd", ".exe"];

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    // Path separators, alternate data streams (':'), wildcards, and characters cmd.exe treats
    // specially even inside quotes.
    private static readonly char[] Forbidden = ['/', '\\', ':', '*', '?', '"', '<', '>', '|', '&', '^', '%', '!', '\0'];

    /// <summary>True if the name is a plain, safe file name with a script extension.</summary>
    public static bool IsSafeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200 || name is "." or "..")
        {
            return false;
        }
        if (name.IndexOfAny(Forbidden) >= 0 || name.Any(char.IsControl))
        {
            return false;
        }
        // Windows quietly drops trailing dots and spaces, so "run.ps1." isn't "run.ps1".
        if (name.EndsWith('.') || name.EndsWith(' ') || name.StartsWith(' '))
        {
            return false;
        }
        var stem = name.Split('.')[0];
        if (ReservedNames.Contains(stem))
        {
            return false;
        }
        return Extensions.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The full path of a script in the Scripts folder, or null if the name isn't safe, the file
    /// doesn't exist, or it's a link to somewhere outside the folder.
    /// </summary>
    public static string? Resolve(string name, string scriptsFolder)
    {
        if (!IsSafeName(name))
        {
            return null;
        }
        var folder = Path.GetFullPath(scriptsFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var path = Path.Combine(folder, name);
        var file = new FileInfo(path);
        if (!file.Exists)
        {
            return null;
        }
        if (file.LinkTarget is not null)
        {
            // A link may only point at another file in the same folder.
            var target = file.ResolveLinkTarget(returnFinalTarget: true);
            if (target is null || !target.Exists || !SameFolder(Path.GetDirectoryName(target.FullName), folder))
            {
                return null;
            }
        }
        return SameFolder(Path.GetDirectoryName(Path.GetFullPath(path)), folder) ? path : null;
    }

    private static bool SameFolder(string? a, string b) =>
        a is not null && string.Equals(
            a.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), b,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
