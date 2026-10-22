using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Recess.App.Platform;

/// <summary>
/// "Lock the computer into the break": while a real break is on screen, swallows the keys that
/// leave it (Alt+Tab, Alt+Esc, Ctrl+Esc, the Windows key, Alt+F4). Only installed for the length
/// of a break, on its own thread so a busy app can't make Windows drop it. Ctrl+Alt+Del and
/// Win+L can't be blocked by any app, and aren't.
/// </summary>
internal sealed class KeyboardLock : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const uint AltDown = 0x20; // LLKHF_ALTDOWN

    private Thread? thread;
    private uint threadId;
    private HOOKPROC? proc;

    public bool IsActive => thread is not null;

    public void Start()
    {
        if (thread is not null)
        {
            return;
        }
        var ready = new ManualResetEventSlim();
        thread = new Thread(() =>
        {
            threadId = PInvoke.GetCurrentThreadId();
            proc = HookProc;
            using var hook = PInvoke.SetWindowsHookEx((WINDOWS_HOOK_ID)WhKeyboardLl, proc, PInvoke.GetModuleHandle((string?)null), 0);
            ready.Set();
            // The hook only works while this thread pumps messages.
            while (PInvoke.GetMessage(out var msg, HWND.Null, 0, 0).Value > 0)
            {
                _ = msg;
            }
        })
        {
            IsBackground = true,
            Name = "Recess keyboard lock",
        };
        thread.Start();
        ready.Wait(TimeSpan.FromSeconds(2));
    }

    public void Stop()
    {
        if (thread is null)
        {
            return;
        }
        PInvoke.PostThreadMessage(threadId, PInvoke.WM_QUIT, 0, 0);
        thread.Join(TimeSpan.FromSeconds(2));
        thread = null;
        proc = null;
    }

    private static unsafe LRESULT HookProc(int code, WPARAM wParam, LPARAM lParam)
    {
        if (code >= 0)
        {
            var key = (KBDLLHOOKSTRUCT*)lParam.Value;
            var vk = (VIRTUAL_KEY)key->vkCode;
            var alt = (key->flags & (KBDLLHOOKSTRUCT_FLAGS)AltDown) != 0;
            var ctrl = (PInvoke.GetAsyncKeyState((int)VIRTUAL_KEY.VK_CONTROL) & 0x8000) != 0;
            var block = vk switch
            {
                VIRTUAL_KEY.VK_LWIN or VIRTUAL_KEY.VK_RWIN => true,
                VIRTUAL_KEY.VK_TAB => alt,
                VIRTUAL_KEY.VK_ESCAPE => alt || ctrl,
                VIRTUAL_KEY.VK_F4 => alt,
                _ => false,
            };
            if (block)
            {
                return new LRESULT(1);
            }
        }
        return PInvoke.CallNextHookEx(HHOOK.Null, code, wParam, lParam);
    }

    public void Dispose() => Stop();
}
