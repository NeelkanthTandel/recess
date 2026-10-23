using System.Windows;
using System.Windows.Threading;
using Recess.App.Actions;
using Recess.App.Breaks;
using Recess.App.Platform;
using Recess.App.Settings;
using Recess.App.Tray;
using Recess.Core;

namespace Recess.App;

public partial class App : Application, IUrlCommandHost
{
    private SingleInstance? instance;
    private SystemMonitor? system;
    private RecessModel? model;
    private Scheduler? scheduler;
    private ActivityRecorder? activity;
    private ActionRunner? actions;
    private TrayController? tray;
    private HotKeys? hotKeys;
    private readonly KeyboardLock keyboardLock = new();
    private BreakSession? lockedSession;
    private SettingsContext? settings;
    private string shortcutsKey = "";
    private DispatcherTimer? timer;
    private readonly IClock clock = new SystemClock();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // A recess:// link wins over anything else on the command line, which is then ignored.
        var link = e.Args.FirstOrDefault(a => a.StartsWith("recess:", StringComparison.OrdinalIgnoreCase));

        instance = SingleInstance.TryClaim();
        if (instance is null)
        {
            // Recess is already running: hand it the link (or ask it to show Settings) and leave.
            SingleInstance.Send(link is not null ? "url " + link : SingleInstance.Reopen);
            Shutdown();
            return;
        }

        try
        {
            Toasts.Register();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            // Notifications fall back to Recess's own banner.
        }
        try
        {
            Registration.EnsureProtocol();
            Registration.RepairLaunchAtLogin();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            // Links and starting at sign-in just won't work; nothing else depends on them.
        }

        var store = new Store(DataFolder.Path);
        model = RecessModel.Load(store);
        system = new SystemMonitor();
        activity = new ActivityRecorder(store.ActivityFolder, () => model.Settings, clock);
        actions = new ActionRunner(clock, store);
        SampleScripts.InstallOnce(store.ScriptsFolder);
        var themes = new ThemeCatalog(store.ThemesFolder);
        var presenter = new BreakPresenter(clock, themes);
        presenter.Shown += session =>
        {
            if (session.LocksAppSwitching)
            {
                lockedSession = session;
                keyboardLock.Start();
            }
        };
        presenter.Hidden += session =>
        {
            keyboardLock.Stop();
            lockedSession = null;
            // "Loop until the break ends."
            actions.StopLoopingSounds();
        };
        scheduler = new Scheduler(model, clock, system, new AskPanel(clock), presenter, actions, new NoCalendar(),
            new ScriptConditions(store.ScriptsFolder), activity, () => Processes.Front() is { } f ? (f.Id, f.Name) : null);

        var commands = new AppCommands
        {
            OpenSettings = OpenSettings,
            Quit = Quit,
            StopSound = actions.StopAllSound,
            IsSoundPlaying = () => actions.IsSoundPlaying,
        };
        tray = new TrayController(model, scheduler, clock, commands);
        tray.SetVisible(model.Settings.ShowStatusMenu);

        hotKeys = new HotKeys();
        settings = new SettingsContext
        {
            Model = model,
            Scheduler = scheduler,
            Clock = clock,
            Store = store,
            Themes = themes,
            HotKeys = hotKeys,
            Actions = actions,
            Activity = activity,
            Commands = commands,
            ShortcutsChanged = RegisterShortcuts,
            PresentationChanged = () => tray.SetVisible(model.Settings.ShowStatusMenu),
            OpenSetupAssistant = () => SetupAssistant.Open(settings!),
        };
        RegisterShortcuts();
        model.Changed += () =>
        {
            // Per-break shortcuts follow the breaks (added, deleted, or given a new shortcut).
            if (ShortcutsKey() != shortcutsKey)
            {
                RegisterShortcuts();
            }
        };

        scheduler.Start();
        timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => Cycle();
        timer.Start();

        instance.Listen(message => Dispatcher.BeginInvoke(() => Received(message)));
        if (link is not null)
        {
            RunLink(link);
        }
        else if (!model.Settings.HasCompletedSetup)
        {
            SetupAssistant.Open(settings);
        }
        else if (!model.Settings.ShowStatusMenu)
        {
            // Without a tray icon, opening Recess is the way to reach Settings.
            OpenSettings(null, null);
        }
    }

    /// <summary>Quits, except during a break that locks the computer.</summary>
    private void Quit()
    {
        if (lockedSession is not null)
        {
            actions?.Beep();
            return;
        }
        Shutdown();
    }

    private void Cycle()
    {
        scheduler!.Cycle();
        model!.SaveIfChanged();
        tray!.Refresh();
    }

    private void Received(string message)
    {
        if (message.StartsWith("url ", StringComparison.Ordinal))
        {
            RunLink(message[4..]);
        }
        else if (message == SingleInstance.Reopen)
        {
            OpenSettings(null, null);
        }
    }

    private void RunLink(string link)
    {
        if (UrlCommand.Parse(link) is { } command)
        {
            UrlCommands.Run(command, model!, scheduler!, this, clock.UtcNow);
            tray?.Refresh();
        }
    }

    public void StopSound() => actions?.StopAllSound();

    public void OpenSettings(string? page, Guid? breakId) => SettingsWindow.Open(settings!, page, breakId);

    private string ShortcutsKey()
    {
        var s = model!.Settings.Shortcuts;
        return string.Join('|', new[] { s.StartNext, s.Postpone1, s.Postpone2, s.Skip, s.PauseIndefinitely, s.Resume, s.Reset, s.StopSound }
            .Concat(model.Breaks.Select(b => b.StartShortcut)).Select(k => k?.ToString() ?? "-"))
            + string.Join(',', model.Breaks.Select(b => b.Id));
    }

    private void RegisterShortcuts()
    {
        shortcutsKey = ShortcutsKey();
        var s = model!.Settings.Shortcuts;
        var sched = scheduler!;
        var list = new List<(KeyCombo, Action)>();
        void Add(KeyCombo? combo, Action run)
        {
            if (combo is not null)
            {
                list.Add((combo, run));
            }
        }
        Add(s.StartNext, sched.StartNextBreak);
        Add(s.Postpone1, () => sched.PostponeNext(1));
        Add(s.Postpone2, () => sched.PostponeNext(2));
        Add(s.Skip, sched.SkipNext);
        Add(s.PauseIndefinitely, () => sched.PauseAll(null));
        Add(s.Resume, sched.ResumeAll);
        Add(s.Reset, sched.ResetAll);
        Add(s.StopSound, () => actions!.StopAllSound());
        foreach (var b in model.Breaks)
        {
            var id = b.Id;
            Add(b.StartShortcut, () => sched.StartBreak(id, manual: true));
        }
        hotKeys!.Set(list);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        timer?.Stop();
        model?.SaveNow();
        activity?.Save();
        tray?.Dispose();
        hotKeys?.Dispose();
        keyboardLock.Dispose();
        system?.Dispose();
        instance?.Dispose();
        base.OnExit(e);
    }
}
