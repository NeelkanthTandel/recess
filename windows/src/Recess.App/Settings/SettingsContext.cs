using Recess.App.Actions;
using Recess.App.Breaks;
using Recess.App.Platform;
using Recess.Core;

namespace Recess.App.Settings;

/// <summary>Everything the settings pages work with.</summary>
internal sealed class SettingsContext
{
    public required RecessModel Model { get; init; }

    public required Scheduler Scheduler { get; init; }

    public required IClock Clock { get; init; }

    public required Store Store { get; init; }

    public required ThemeCatalog Themes { get; init; }

    public required HotKeys HotKeys { get; init; }

    public required ActionRunner Actions { get; init; }

    public required ActivityRecorder Activity { get; init; }

    public required CalendarFetcher Calendars { get; init; }

    public required AppCommands Commands { get; init; }

    /// <summary>Called when the shortcuts change, so they're registered again.</summary>
    public required Action ShortcutsChanged { get; init; }

    /// <summary>Called when the tray icon is turned on or off.</summary>
    public required Action PresentationChanged { get; init; }

    public required Action OpenSetupAssistant { get; init; }

    public DateTime Now => Scheduler.Now;
}
