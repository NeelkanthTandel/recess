using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Recess.Core;

namespace Recess.App.Actions;

/// <summary>
/// Runs scripts from the user's Scripts folder: PowerShell, batch files and programs. Only ever
/// by name from that folder (see <see cref="ScriptPaths"/>), with arguments passed as a list
/// rather than a command line, no window, and a time limit.
/// </summary>
internal static class ScriptRunner
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private const int MaxOutput = 64 * 1024;

    /// <summary>Runs a script in the background. The task gives its output and exit code (−1 if it couldn't run).</summary>
    public static Task<(string Output, int ExitCode)> Run(string name, string scriptsFolder, IReadOnlyDictionary<string, string>? environment = null)
    {
        if (ScriptPaths.Resolve(name, scriptsFolder) is not { } path)
        {
            return Task.FromResult(("", -1));
        }
        return Task.Run(async () =>
        {
            var start = StartInfo(path, scriptsFolder);
            foreach (var (key, value) in environment ?? new Dictionary<string, string>())
            {
                start.Environment[key] = value;
            }
            using var process = new Process { StartInfo = start };
            var output = new StringBuilder();
            process.OutputDataReceived += (_, e) =>
            {
                // Keep reading so the script never blocks on a full pipe, but keep only the start.
                if (e.Data is not null && output.Length < MaxOutput)
                {
                    output.AppendLine(e.Data);
                }
            };
            process.ErrorDataReceived += (_, _) => { };
            try
            {
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                using var cancel = new CancellationTokenSource(Timeout);
                try
                {
                    await process.WaitForExitAsync(cancel.Token);
                }
                catch (OperationCanceledException)
                {
                    process.Kill(entireProcessTree: true);
                    return (output.ToString(), -1);
                }
                return (output.ToString(), process.ExitCode);
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
            {
                Debug.WriteLine($"Script {name} failed: {ex.Message}");
                return ("", -1);
            }
        });
    }

    private static ProcessStartInfo StartInfo(string path, string folder)
    {
        var system = Environment.SystemDirectory;
        var start = new ProcessStartInfo
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            WorkingDirectory = folder,
            StandardOutputEncoding = Encoding.UTF8,
        };
        switch (Path.GetExtension(path).ToLowerInvariant())
        {
            case ".ps1":
                start.FileName = Path.Combine(system, "WindowsPowerShell", "v1.0", "powershell.exe");
                foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", path })
                {
                    start.ArgumentList.Add(arg);
                }
                break;
            case ".bat" or ".cmd":
                // Safe script names have no characters cmd.exe treats specially (see ScriptPaths).
                start.FileName = Path.Combine(system, "cmd.exe");
                foreach (var arg in new[] { "/d", "/c", path })
                {
                    start.ArgumentList.Add(arg);
                }
                break;
            default:
                start.FileName = path;
                break;
        }
        return start;
    }
}

/// <summary>Script-result rule conditions: each script runs at most every 20 seconds, in the background.</summary>
internal sealed class ScriptConditions(string scriptsFolder) : IScriptConditions
{
    private static readonly TimeSpan Refresh = TimeSpan.FromSeconds(20);
    private readonly ConcurrentDictionary<string, bool> results = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTime> checkedAt = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, bool> running = new(StringComparer.OrdinalIgnoreCase);

    public bool Result(string scriptFilename)
    {
        var stale = !checkedAt.TryGetValue(scriptFilename, out var at) || DateTime.UtcNow - at > Refresh;
        if (stale && running.TryAdd(scriptFilename, true))
        {
            _ = ScriptRunner.Run(scriptFilename, scriptsFolder).ContinueWith(t =>
            {
                var value = t.IsCompletedSuccessfully ? t.Result.Output.Trim().ToLowerInvariant() : "";
                results[scriptFilename] = value is "true" or "yes" or "1";
                checkedAt[scriptFilename] = DateTime.UtcNow;
                running.TryRemove(scriptFilename, out _);
            }, TaskScheduler.Default);
        }
        return results.TryGetValue(scriptFilename, out var result) && result;
    }
}
