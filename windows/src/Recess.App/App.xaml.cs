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
        actions = new ActionRunner(clock);
        var themes = new ThemeCatalog(store.ThemesFolder);
        var presenter = new BreakPresenter(clock, themes);
        scheduler = new Scheduler(model, clock, system, new AskPanel(clock), presenter, actions, new NoCalendar(), new NoScripts(), activity,
            () => Processes.Front() is { } f ? (f.Id, f.Name) : null);

        var commands = new AppCommands
        {
            OpenSettings = OpenSettings,
            Quit = Shutdown,
            StopSound = actions.StopAllSound,
            IsSoundPlaying = () => actions.IsSoundPlaying,
        };
        tray = new TrayController(model, scheduler, clock, commands);
        tray.SetVisible(model.Settings.ShowStatusMenu);

        scheduler.Start();
        timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => Cycle();
        timer.Start();

        instance.Listen(message => Dispatcher.BeginInvoke(() => Received(message)));
        if (link is not null)
        {
            RunLink(link);
        }
        else if (!model.Settings.ShowStatusMenu)
        {
            // Without a tray icon, opening Recess is the way to reach Settings.
            OpenSettings(null, null);
        }
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

    public void OpenSettings(string? page, Guid? breakId) => SettingsWindow.Open(model!, scheduler!, clock);

    protected override void OnExit(ExitEventArgs e)
    {
        timer?.Stop();
        model?.SaveNow();
        activity?.Save();
        tray?.Dispose();
        system?.Dispose();
        instance?.Dispose();
        base.OnExit(e);
    }

    /// <summary>Script rules arrive with the next milestone.</summary>
    private sealed class NoScripts : IScriptConditions
    {
        public bool Result(string scriptFilename) => false;
    }
}
