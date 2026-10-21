# Architecture

Recess is a single SwiftPM executable target (`Sources/Recess`) built with AppKit and SwiftUI. It has no third-party dependencies.

```
Sources/Recess/
├── App/          main.swift, AppDelegate (launch, main menu, global shortcuts, recess:// URLs)
├── Model/        Codable value types: Break, BreakAction, Rule, AppSettings, Interval, activity records
├── Store/        JSON persistence (Store) and the observable app state (AppModel, Ticker)
├── Engine/       Scheduler (the 1-second cycle), EnvironmentMonitor, RulesEngine, CalendarManager
├── Break/        BreakSession (one break in progress), overlay views, themes, the "ask me first" panel
├── Actions/      ActionPerformer (notification, sound, speech, flash, script) and the fallback banner
├── Activity/     ActivityRecorder (break log, app usage, statistics)
├── Status/       StatusItemController (menu bar item)
├── Settings/     SwiftUI settings window
├── Setup/        First-run assistant
├── Shortcuts/    Carbon global hot keys and the KeyCombo type
└── Util/         Small helpers
```

## Data flow

1. **`AppModel`** (main actor, `ObservableObject`) owns `breaks: [Break]` and `settings: AppSettings`. Any change schedules a throttled save to `~/Library/Application Support/Recess/{Breaks,Settings}.json`.
2. **`Ticker`** publishes the values that change every second (current time, idle seconds). They live outside `AppModel` so that views observing the model are not rebuilt every second. Only views that show a countdown observe `Ticker`.
3. **`Scheduler.cycle()`** runs once a second:
   - rolls the hourly and daily postpone/skip counters, and resumes pauses that have expired;
   - handles sleep: on wake, or after a long gap, the time asleep counts as time away;
   - measures idle time (`EnvironmentMonitor`) and applies "count as away" / "don't count as away" rules;
   - handles time away: per break, the clock either keeps running, stops or winds back, and the break restarts after a long enough absence;
   - fires "ahead of the break" actions;
   - skips lower-priority breaks when a more important one is due soon;
   - if a break is due: checks postpone/skip rules, waits while a mouse button is down, then starts it now, waits for a pause in your work, or asks first.
4. **`BreakSession`** opens one borderless window per screen, counts down, runs actions for each stage, and reports `done / endedEarly / postponed / skipped` back to the scheduler, which updates counters, the activity log and the next due date.

Writes to `model.breaks` only happen when a value actually changes. Every write re-renders the UI and queues a save, so code in the cycle compares before assigning.

## Scheduling rules

- **Frequency** is measured start to start, so it includes the break's own length.
- **Next due from**: when the last break was due (start + frequency), when it ended (end + frequency), a set minute each hour (a grid anchored at midnight + N minutes, stepping by the frequency), or the next matching calendar event.
- **Availability**: an optional From/To window plus allowed weekdays. A time after "To" moves to the next allowed day at From + frequency.
- **Priority** is the order of breaks in the sidebar. A break is skipped when a more important one is due within its window, and can restart its timer after a more important one finishes.

## Themes

A theme is a folder named `Something.recesstheme` containing `Info.json` and `Documents/`. `Info.json` has a `name`, an `identifier`, and either a `rootDocument` (a local HTML file in `Documents/`) or a `url` (a remote page). Themes load from two places:

1. `Recess.app/Contents/Resources/Themes` (bundled)
2. `~/Library/Application Support/Recess/Themes` (yours; the theme editor writes here)

HTML themes run in a `WKWebView`. A theme can ask for break details by calling `window.webkit.messageHandlers.getRecessInfo.postMessage('summary')`. Recess then calls the theme's `recessInfo(json)` function. The JSON contains `breakName`, `breakIdentifier`, `breakDuration`, `breakRemaining` and `breakColor`. Only local (file) themes get this information.

## Testing hooks

Debug builds (`swift build`) support two extra `recess://` commands for automated checks. They work only when the app is launched with the `RECESS_DATA_DIR` environment variable, which also redirects all data to a throwaway folder:

- `recess://snapshot?name=x` writes PNGs of Recess's own windows to `$RECESS_DATA_DIR/Snapshots`.
- `recess://testnotify?break=Name` performs that break's notification action.

Neither command exists in release builds.
