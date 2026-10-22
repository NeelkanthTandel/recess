namespace Recess.Core;

/// <summary>Where Recess keeps its settings, breaks, activity, themes and scripts.</summary>
public static class DataFolder
{
    /// <summary>
    /// %APPDATA%\Recess, or RECESS_DATA_DIR in debug builds so development never touches real data.
    /// </summary>
    public static string Path => Resolve(Environment.GetEnvironmentVariable("RECESS_DATA_DIR"));

    internal static string Resolve(string? overrideDirectory)
    {
#if DEBUG
        if (!string.IsNullOrWhiteSpace(overrideDirectory))
        {
            return System.IO.Path.GetFullPath(overrideDirectory);
        }
#endif
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return System.IO.Path.Combine(appData, "Recess");
    }

    /// <summary>True when debug-only hooks may run: a debug build pointed at a separate data folder.</summary>
    public static bool IsDevelopment
    {
        get
        {
#if DEBUG
            return !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("RECESS_DATA_DIR"));
#else
            return false;
#endif
        }
    }
}
