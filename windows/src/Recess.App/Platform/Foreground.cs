using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace Recess.App.Platform;

/// <summary>
/// Brings a break window to the front so typing goes to it, not the document underneath.
/// Windows normally refuses SetForegroundWindow from a background app and only flashes
/// its taskbar button, so this uses the two usual, documented-in-practice workarounds.
/// </summary>
internal static class Foreground
{
    public static bool IsForeground(IntPtr hwnd) => PInvoke.GetForegroundWindow() == new HWND(hwnd);

    public static void Force(IntPtr handle)
    {
        var hwnd = new HWND(handle);
        if (IsForeground(handle))
        {
            return;
        }

        // 1. Share input state with the current foreground thread, which lets us take focus.
        var foreground = PInvoke.GetForegroundWindow();
        uint foregroundThread;
        unsafe
        {
            foregroundThread = PInvoke.GetWindowThreadProcessId(foreground, null);
        }
        var thisThread = PInvoke.GetCurrentThreadId();
        var attached = foregroundThread != 0 && foregroundThread != thisThread
            && PInvoke.AttachThreadInput(thisThread, foregroundThread, true);
        PInvoke.BringWindowToTop(hwnd);
        PInvoke.SetForegroundWindow(hwnd);
        if (attached)
        {
            PInvoke.AttachThreadInput(thisThread, foregroundThread, false);
        }
        if (IsForeground(handle))
        {
            return;
        }

        // 2. A synthetic Alt press counts as input to this process, which also unlocks it.
        Span<INPUT> inputs = stackalloc INPUT[2];
        inputs[0].type = INPUT_TYPE.INPUT_KEYBOARD;
        inputs[0].Anonymous.ki.wVk = VIRTUAL_KEY.VK_MENU;
        inputs[1].type = INPUT_TYPE.INPUT_KEYBOARD;
        inputs[1].Anonymous.ki.wVk = VIRTUAL_KEY.VK_MENU;
        inputs[1].Anonymous.ki.dwFlags = KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP;
        PInvoke.SendInput(inputs[..1], Marshal.SizeOf<INPUT>());
        PInvoke.SetForegroundWindow(hwnd);
        PInvoke.SendInput(inputs[1..], Marshal.SizeOf<INPUT>());
    }
}
