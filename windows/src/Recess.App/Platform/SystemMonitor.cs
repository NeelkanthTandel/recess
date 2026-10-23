using System.Runtime.InteropServices;
using System.Windows.Interop;
using Microsoft.Win32;
using Recess.Core;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Power;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Recess.App.Platform;

/// <summary>
/// Idle time, sleep, the lock screen, display power and the front app: what the scheduler
/// needs to know about the computer. Must be created on the UI thread.
/// </summary>
internal sealed class SystemMonitor : ISystemMonitor, IDisposable
{
    private readonly HwndSource window;
    private readonly HPOWERNOTIFY displayNotification;
    private DateTime? suspendedAt;
    private double? pendingSleep;
    private DateTime? lockedAt;

    public SystemMonitor()
    {
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;

        // A hidden window to hear when the display turns off and on.
        window = new HwndSource(new HwndSourceParameters("RecessPower") { Width = 0, Height = 0, WindowStyle = 0 });
        window.AddHook(WndProc);
        var guid = PInvoke.GUID_CONSOLE_DISPLAY_STATE;
        unsafe
        {
            displayNotification = PInvoke.RegisterPowerSettingNotification(new HANDLE(window.Handle), &guid,
                REGISTER_NOTIFICATION_FLAGS.DEVICE_NOTIFY_WINDOW_HANDLE);
        }
    }

    public bool IsDisplayOff { get; private set; }

    public bool IsLocked => lockedAt is not null;

    /// <summary>Seconds since the last keyboard or mouse input in this session.</summary>
    public double SystemIdleSeconds
    {
        get
        {
            var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
            if (!PInvoke.GetLastInputInfo(ref info))
            {
                return 0;
            }
            // Both counters wrap after 49.7 days; unsigned subtraction still gives the right gap.
            var idle = unchecked((uint)Environment.TickCount - info.dwTime) / 1000.0;
            // While the screen is locked, the person isn't at their desk, whatever the counter says.
            if (lockedAt is { } locked)
            {
                idle = Math.Max(idle, (DateTime.UtcNow - locked).TotalSeconds);
            }
            return idle;
        }
    }

    public double IdleSeconds(IdleDetector detector) => detector == IdleDetector.None ? 0 : SystemIdleSeconds;

    public bool IsMouseButtonDown =>
        Down(VIRTUAL_KEY.VK_LBUTTON) || Down(VIRTUAL_KEY.VK_RBUTTON) || Down(VIRTUAL_KEY.VK_MBUTTON);

    private static bool Down(VIRTUAL_KEY key) => (PInvoke.GetAsyncKeyState((int)key) & 0x8000) != 0;

    public double? TakeSleepInterval()
    {
        var s = pendingSleep;
        pendingSleep = null;
        return s;
    }

    public IRuleEnvironment Snapshot() => new RuleSnapshot(this);

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend)
        {
            suspendedAt = DateTime.UtcNow;
        }
        else if (e.Mode == PowerModes.Resume && suspendedAt is { } at)
        {
            pendingSleep = (DateTime.UtcNow - at).TotalSeconds;
            suspendedAt = null;
        }
    }

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.ConsoleDisconnect or SessionSwitchReason.RemoteDisconnect)
        {
            lockedAt ??= DateTime.UtcNow;
        }
        else if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.ConsoleConnect or SessionSwitchReason.RemoteConnect
            or SessionSwitchReason.SessionLogon)
        {
            lockedAt = null;
        }
    }

    private unsafe IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == PInvoke.WM_POWERBROADCAST && (uint)wParam == PInvoke.PBT_POWERSETTINGCHANGE && lParam != IntPtr.Zero)
        {
            var setting = (POWERBROADCAST_SETTING*)lParam;
            if (setting->PowerSetting == PInvoke.GUID_CONSOLE_DISPLAY_STATE && setting->DataLength >= 4)
            {
                // 0 = off, 1 = on, 2 = dimmed.
                IsDisplayOff = *(uint*)&setting->Data == 0;
            }
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        if (!displayNotification.IsNull)
        {
            PInvoke.UnregisterPowerSettingNotification(displayNotification);
        }
        window.Dispose();
    }

    /// <summary>Rule inputs, each looked up only when a rule first asks for it.</summary>
    private sealed class RuleSnapshot(SystemMonitor monitor) : IRuleEnvironment
    {
        private readonly Lazy<(string Id, string Name, uint ProcessId)?> front = new(Processes.Front);
        private readonly Lazy<bool> fullscreen = new(IsFrontFullscreen);
        private readonly Lazy<bool> screensaver = new(() => ScreensaverRunning() || monitor.IsLocked);

        public string? FrontApp => front.Value?.Id;

        public IReadOnlySet<string> RunningApps => Processes.Running;

        public bool IsFullscreen => fullscreen.Value;

        public bool IsScreensaver => screensaver.Value;

        public bool IsDisplayAsleep => monitor.IsDisplayOff;

        /// <summary>Windows has no public way to read Do Not Disturb; the rule says so in Settings.</summary>
        public bool IsFocusOn => false;

        public bool IsMicOrCameraInUse => DeviceUse.MicOrCameraInUse();
    }

    private static unsafe bool ScreensaverRunning()
    {
        BOOL running = false;
        PInvoke.SystemParametersInfo(SYSTEM_PARAMETERS_INFO_ACTION.SPI_GETSCREENSAVERRUNNING, 0, &running, 0);
        return running;
    }

    /// <summary>A full-screen game, video, or presentation in front.</summary>
    private static bool IsFrontFullscreen()
    {
        if (PInvoke.SHQueryUserNotificationState(out var state).Succeeded
            && state is QUERY_USER_NOTIFICATION_STATE.QUNS_RUNNING_D3D_FULL_SCREEN or QUERY_USER_NOTIFICATION_STATE.QUNS_PRESENTATION_MODE
                or QUERY_USER_NOTIFICATION_STATE.QUNS_BUSY)
        {
            return true;
        }
        var hwnd = PInvoke.GetForegroundWindow();
        if (hwnd.IsNull || hwnd == PInvoke.GetShellWindow() || Processes.ProcessId(hwnd) == (uint)Environment.ProcessId)
        {
            return false;
        }
        Span<char> name = stackalloc char[64];
        var length = PInvoke.GetClassName(hwnd, name);
        var cls = name[..Math.Max(0, length)].ToString();
        if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd")
        {
            return false;
        }
        if (!PInvoke.GetWindowRect(hwnd, out var rect))
        {
            return false;
        }
        var monitor = Monitors.All().FirstOrDefault(m => m.Handle == (IntPtr)PInvoke.MonitorFromWindow(hwnd, Windows.Win32.Graphics.Gdi.MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST));
        return monitor is not null && rect.left <= monitor.Bounds.left && rect.top <= monitor.Bounds.top
            && rect.right >= monitor.Bounds.right && rect.bottom >= monitor.Bounds.bottom;
    }
}
