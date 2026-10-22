using System.Media;
using Recess.App.Platform;
using Recess.Core;

namespace Recess.App.Actions;

/// <summary>Performs break actions. Sounds, speech, screen flashes and scripts arrive in the next milestone.</summary>
internal sealed class ActionRunner(IClock clock) : IActionRunner
{
    public void Perform(BreakAction action, Recess.Core.Break b)
    {
        switch (action.Kind)
        {
            case ActionKind.Notification:
                Notify(Expand(action.Title, b), Expand(action.Message, b));
                break;
        }
    }

    public void Beep() => SystemSounds.Beep.Play();

    public bool IsSoundPlaying => false;

    public void StopAllSound()
    {
    }

    private string Expand(string text, Recess.Core.Break b) => BreakAction.Expand(text, b, clock.UtcNow, clock.Zone);

    /// <summary>A Windows notification, or Recess's own banner when notifications are off.</summary>
    public static void Notify(string title, string message)
    {
        try
        {
            if (Toasts.Enabled)
            {
                Toasts.Show(title, message);
                return;
            }
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            // Falls through to the banner.
        }
        Banner.Show(title, message);
    }
}
