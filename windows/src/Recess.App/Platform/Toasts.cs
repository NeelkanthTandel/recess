using Microsoft.Win32;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;
using Windows.Win32;

namespace Recess.App.Platform;

/// <summary>
/// Windows notifications. Recess registers its own app ID in the registry, so toasts show
/// its name and icon even when it runs from the portable zip, without a Start menu shortcut.
/// </summary>
internal static class Toasts
{
    public const string AppId = "Recess.BreakReminder";

    public static void Register()
    {
        using (var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\AppUserModelId\{AppId}"))
        {
            key.SetValue("DisplayName", "Recess");
            key.SetValue("IconUri", Path.Combine(AppContext.BaseDirectory, "Assets", "icon.png"));
        }
        PInvoke.SetCurrentProcessExplicitAppUserModelID(AppId);
    }

    /// <summary>False when the user has turned off notifications for Recess, or for everything.</summary>
    public static bool Enabled => Notifier.Setting == NotificationSetting.Enabled;

    public static void Show(string title, string body, bool silent = false)
    {
        // Built with the XML APIs, never by pasting strings, because break names go in here.
        var xml = new XmlDocument();
        var toast = xml.CreateElement("toast");
        xml.AppendChild(toast);
        var visual = xml.CreateElement("visual");
        toast.AppendChild(visual);
        var binding = xml.CreateElement("binding");
        binding.SetAttribute("template", "ToastGeneric");
        visual.AppendChild(binding);
        foreach (var line in new[] { title, body })
        {
            var text = xml.CreateElement("text");
            text.AppendChild(xml.CreateTextNode(line));
            binding.AppendChild(text);
        }
        if (silent)
        {
            var audio = xml.CreateElement("audio");
            audio.SetAttribute("silent", "true");
            toast.AppendChild(audio);
        }
        Notifier.Show(new ToastNotification(xml));
    }

    private static ToastNotifier Notifier => ToastNotificationManager.CreateToastNotifier(AppId);
}
