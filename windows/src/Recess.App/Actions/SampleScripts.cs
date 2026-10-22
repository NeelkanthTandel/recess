namespace Recess.App.Actions;

/// <summary>Example scripts put in the Scripts folder once, so there's something to start from.</summary>
internal static class SampleScripts
{
    private const string Marker = ".installed-v1";

    private static readonly (string Name, string Body)[] Samples =
    [
        ("In a call.ps1", """
            # Prints "true" while an app is using the microphone, which usually means you're in a call.
            # Use it with a "Script result" rule. (Recess also has a built-in "Microphone or camera in use" rule.)
            $store = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone'
            $inUse = Get-ChildItem $store -Recurse -ErrorAction SilentlyContinue |
                Where-Object { $_.GetValue('LastUsedTimeStart') -gt 0 -and $_.GetValue('LastUsedTimeStop') -eq 0 }
            if ($inUse) { 'true' } else { 'false' }
            """),
        ("Play or pause media.ps1", """
            # Presses the play/pause media key: it pauses whatever is playing, or resumes it if paused.
            (New-Object -ComObject WScript.Shell).SendKeys([char]179)
            """),
        ("Speak the time.ps1", """
            # Says the time out loud.
            Add-Type -AssemblyName System.Speech
            $voice = New-Object System.Speech.Synthesis.SpeechSynthesizer
            $voice.Speak("It's " + (Get-Date -Format t))
            """),
        ("Start screensaver.ps1", """
            # Starts the screensaver chosen in Windows Settings, if there is one.
            $saver = (Get-ItemProperty 'HKCU:\Control Panel\Desktop' -ErrorAction SilentlyContinue).'SCRNSAVE.EXE'
            if ($saver) { Start-Process $saver -ArgumentList '/s' }
            """),
        ("Lock screen.cmd", """
            @echo off
            rem Locks the computer, like pressing Win+L.
            rundll32.exe user32.dll,LockWorkStation
            """),
    ];

    public static void InstallOnce(string scriptsFolder)
    {
        var marker = Path.Combine(scriptsFolder, Marker);
        if (File.Exists(marker))
        {
            return;
        }
        try
        {
            foreach (var (name, body) in Samples)
            {
                var path = Path.Combine(scriptsFolder, name);
                if (!File.Exists(path))
                {
                    // Batch files need Windows line endings.
                    File.WriteAllText(path, name.EndsWith(".cmd", StringComparison.Ordinal) ? body.ReplaceLineEndings("\r\n") : body);
                }
            }
            File.WriteAllText(marker, "");
            File.SetAttributes(marker, FileAttributes.Hidden);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Samples are a nicety; try again next launch.
        }
    }
}
