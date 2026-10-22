using System.Media;
using System.Windows.Threading;
using Recess.App.Platform;
using Recess.Core;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Media.SpeechSynthesis;

namespace Recess.App.Actions;

/// <summary>Performs break actions: notifications, sounds, fades, speech, screen flashes and scripts.</summary>
internal sealed class ActionRunner(IClock clock, Store store) : IActionRunner
{
    private readonly List<(MediaPlayer Player, bool Loops)> players = [];
    private readonly List<DispatcherTimer> fades = [];

    /// <summary>True while a sound or speech is playing (players leave the list when they end).</summary>
    public bool IsSoundPlaying => players.Count > 0;

    public void Perform(BreakAction action, Recess.Core.Break b)
    {
        try
        {
            switch (action.Kind)
            {
                case ActionKind.Notification:
                    var sound = action.SoundName.Length > 0 || action.SoundPath.Length > 0;
                    Notify(Expand(action.Title, b), Expand(action.Message, b), silent: sound);
                    if (sound)
                    {
                        PlaySound(action, loops: false);
                    }
                    break;
                case ActionKind.Sound:
                    PlaySound(action, action.Loops);
                    break;
                case ActionKind.FadeSound:
                    FadeOut(action.FadeSeconds);
                    break;
                case ActionKind.Speech:
                    _ = Speak(Expand(action.Message, b), action);
                    break;
                case ActionKind.FlashScreen:
                    FlashWindow.FlashAll();
                    break;
                case ActionKind.Script:
                    ScriptRunner.Run(action.ScriptFilename, store.ScriptsFolder, Environment(b, action.Stage));
                    break;
            }
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or IOException or UnauthorizedAccessException
            or ArgumentException or InvalidOperationException)
        {
            // An action that can't run (a missing sound file, no audio device) mustn't stop the break.
            System.Diagnostics.Debug.WriteLine($"Action {action.Kind} failed: {ex.Message}");
        }
    }

    public void Beep() => SystemSounds.Beep.Play();

    /// <summary>"Loop until the break ends": stops looping sounds when a break is over.</summary>
    public void StopLoopingSounds()
    {
        foreach (var (player, loops) in players.ToList())
        {
            if (loops)
            {
                Stop(player);
            }
        }
    }

    public void StopAllSound()
    {
        foreach (var timer in fades)
        {
            timer.Stop();
        }
        fades.Clear();
        foreach (var (player, _) in players.ToList())
        {
            Stop(player);
        }
    }

    private string Expand(string text, Recess.Core.Break b) => BreakAction.Expand(text, b, clock.UtcNow, clock.Zone);

    private static Dictionary<string, string> Environment(Recess.Core.Break b, ActionStage stage) => new()
    {
        ["RECESS_BREAK_NAME"] = b.Name,
        ["RECESS_BREAK_ID"] = b.Id.ToString().ToUpperInvariant(),
        ["RECESS_BREAK_DURATION"] = Format.Int(b.Duration).ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["RECESS_STAGE"] = stage.ToString(),
    };

    /// <summary>A Windows notification, or Recess's own banner when notifications are off.</summary>
    public static void Notify(string title, string message, bool silent = false)
    {
        try
        {
            if (Toasts.Enabled)
            {
                Toasts.Show(title, message, silent);
                return;
            }
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            // Falls through to the banner.
        }
        Banner.Show(title, message);
    }

    // MARK: - Sound

    private void PlaySound(BreakAction action, bool loops)
    {
        if (Library.SoundFile(action, store) is not { } file)
        {
            return;
        }
        Play(MediaSource.CreateFromUri(new Uri(file)), action.Volume, loops);
    }

    private void Play(MediaSource source, double volume, bool loops)
    {
        var player = new MediaPlayer
        {
            Source = source,
            Volume = Math.Clamp(volume, 0, 1),
            IsLoopingEnabled = loops,
            AutoPlay = false,
        };
        // Otherwise Windows shows an "Unknown app" card in its volume pop-up.
        player.CommandManager.IsEnabled = false;
        player.MediaEnded += (_, _) => System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            if (!player.IsLoopingEnabled)
            {
                Stop(player);
            }
        });
        player.MediaFailed += (_, _) => System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => Stop(player));
        players.Add((player, loops));
        player.Play();
    }

    private void Stop(MediaPlayer player)
    {
        players.RemoveAll(p => p.Player == player);
        try
        {
            player.Pause();
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // Already gone.
        }
        player.Dispose();
    }

    /// <summary>Fades every playing sound (and speech) to silence, then stops it.</summary>
    private void FadeOut(double seconds)
    {
        var playing = players.Select(p => (p.Player, Start: p.Player.Volume)).ToList();
        if (playing.Count == 0)
        {
            return;
        }
        var length = Math.Max(0.1, seconds);
        var started = DateTime.UtcNow;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        timer.Tick += (_, _) =>
        {
            var t = (DateTime.UtcNow - started).TotalSeconds / length;
            foreach (var (player, start) in playing)
            {
                player.Volume = Math.Max(0, start * (1 - t));
            }
            if (t >= 1)
            {
                timer.Stop();
                fades.Remove(timer);
                foreach (var (player, _) in playing)
                {
                    Stop(player);
                }
            }
        };
        fades.Add(timer);
        timer.Start();
    }

    // MARK: - Speech

    private async Task Speak(string text, BreakAction action)
    {
        using var synth = new SpeechSynthesizer();
        if (action.VoiceIdentifier.Length > 0 && SpeechSynthesizer.AllVoices.FirstOrDefault(v => v.Id == action.VoiceIdentifier) is { } voice)
        {
            synth.Voice = voice;
        }
        // The Mac's rate runs 0–1 with 0.5 as normal; Windows uses a multiplier with 1 as normal.
        synth.Options.SpeakingRate = Math.Clamp(Math.Pow(2, (action.Rate - 0.5) * 4), 0.5, 6);
        synth.Options.AudioPitch = Math.Clamp(action.Pitch, 0, 2);
        synth.Options.AudioVolume = Math.Clamp(action.Volume, 0, 1);
        var stream = await synth.SynthesizeTextToStreamAsync(text);
        Play(MediaSource.CreateFromStream(stream, stream.ContentType), 1, loops: false);
    }
}
