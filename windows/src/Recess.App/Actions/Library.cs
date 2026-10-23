using Recess.Core;

namespace Recess.App.Actions;

/// <summary>The sounds, voices and scripts the Actions and Rules pages offer.</summary>
internal static class Library
{
    private static readonly string[] SoundExtensions = [".wav", ".mp3", ".wma", ".m4a", ".aac", ".flac"];

    /// <summary>Sounds by name: Windows' own (C:\Windows\Media) and any in Recess's Sounds folder, which win on a clash.</summary>
    public static List<(string Name, string Path)> Sounds(Store store)
    {
        var found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in new[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media"), store.SoundsFolder })
        {
            if (!Directory.Exists(folder))
            {
                continue;
            }
            foreach (var file in Directory.EnumerateFiles(folder))
            {
                if (SoundExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                {
                    found[Path.GetFileNameWithoutExtension(file)] = file;
                }
            }
        }
        return found.OrderBy(p => p.Key, StringComparer.CurrentCultureIgnoreCase).Select(p => (p.Key, p.Value)).ToList();
    }

    /// <summary>The file for a sound action: its chosen file, or the named sound.</summary>
    public static string? SoundFile(BreakAction action, Store store)
    {
        if (action.SoundPath.Length > 0 && File.Exists(action.SoundPath))
        {
            return action.SoundPath;
        }
        return action.SoundName.Length == 0
            ? null
            : Sounds(store).FirstOrDefault(s => string.Equals(s.Name, action.SoundName, StringComparison.OrdinalIgnoreCase)).Path;
    }

    /// <summary>Speech voices installed on this PC, as (id, display name).</summary>
    public static List<(string Id, string Name)> Voices()
    {
        try
        {
            return Windows.Media.SpeechSynthesis.SpeechSynthesizer.AllVoices
                .Select(v => (v.Id, $"{v.DisplayName} ({v.Language})"))
                .ToList();
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>Scripts in the Scripts folder that Recess is allowed to run.</summary>
    public static List<string> Scripts(Store store) => Directory.EnumerateFiles(store.ScriptsFolder)
        .Select(Path.GetFileName)
        .OfType<string>()
        .Where(ScriptPaths.IsSafeName)
        .Order(StringComparer.CurrentCultureIgnoreCase)
        .ToList();
}
