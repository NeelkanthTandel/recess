# Architecture

Recess is two native apps that behave the same way:

- **Mac:** a single SwiftPM executable target (`Sources/Recess`) built with AppKit and SwiftUI, with no third-party dependencies. Most of this document describes it.
- **Windows:** a C# and WPF (.NET 10) solution in `windows/`. Its engine is a port of the Mac one; see [Windows](#windows) at the end.

The scheduling rules below apply to both.

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

## Windows

```
windows/
├── src/Recess.Core/        the engine: no Windows APIs, so it builds and tests anywhere
│   ├── Model/              Break, BreakAction, Rule, AppSettings, Interval/Format, LocalTime, StatusText
│   ├── Store/              Store (JSON in %APPDATA%\Recess) and RecessModel
│   ├── Engine/             Scheduler, DueDates, BreakSession, RulesEngine, and Seams.cs
│   ├── Activity/           ActivityRecorder
│   ├── Calendar/           CalendarFeeds (iCal, via Ical.Net)
│   ├── Commands/           recess:// parsing and running
│   ├── Themes/             ThemeGenerator (text, website, YouTube, image themes)
│   └── Safety/             ScriptPaths, ThemeText
├── src/Recess.App/         the Windows app (WPF)
│   ├── Platform/           tray icon, monitors, idle/sleep/lock, processes, single instance, registry,
│   │                       hot keys, keyboard lock, toasts, calendar downloads, mic/camera use
│   ├── Breaks/             BreakPresenter, OverlayWindow, ThemeView (WebView2), Default theme, ask panel
│   ├── Actions/            ActionRunner (sound, speech, flash, scripts), ScriptRunner, samples, banner
│   ├── Tray/               TrayController and the icon's glyphs
│   └── Settings/           settings window, pages, setup assistant, shortcut recorder
├── tests/Recess.Core.Tests
└── installer/Recess.iss    Inno Setup
```

**The engine is the Mac's, with seams.** `Scheduler`, `DueDates`, `BreakSession` and `RulesEngine` follow the Swift code rule for rule. The difference is that the Windows scheduler reaches the outside world only through small interfaces in `Engine/Seams.cs`: `IClock`, `ISystemMonitor`, `IRuleEnvironment`, `ICalendarSource`, `IScriptConditions`, `IActionRunner`, `IAskPresenter` and `IBreakPresenter`. The app implements them with Windows APIs; the tests implement them with fakes, so the whole one-second cycle is tested with a fake clock. Dates are stored in UTC; days, hours and weekdays are worked out in the user's time zone by `LocalTime`, which, like Foundation, moves a time skipped by daylight saving forward by the length of the skip.

**A break on screen.** `Scheduler.StartBreak` creates a `BreakSession` (countdown, phases, button rules, stage actions) and hands it to `BreakPresenter`, which opens an `OverlayWindow` per display: borderless, topmost, placed in physical pixels. HTML themes run in a `WebView2CompositionControl`, which draws as ordinary WPF content, so the dim colour and the buttons layer over the page. A small script maps the Mac's `window.webkit.messageHandlers.getRecessInfo` onto WebView2's messaging, so the shared themes run unchanged. When the session finishes, the presenter fades out and calls `BreakSession.Complete`, and the scheduler records the result.

**Windows equivalents of Mac features:**

| Mac | Windows |
| --- | --- |
| Menu bar item | Notification-area icon (`Platform/TrayIcon` over `Shell_NotifyIcon`); the title goes in the tooltip, and the Countdown icon style draws the minutes in the icon |
| Idle time (`CGEventSource`) | `GetLastInputInfo`; a locked screen counts as away |
| Sleep and wake | the gap between cycles (reliable with Modern Standby), plus power events |
| EventKit calendars | private iCal addresses or `.ics` files (`CalendarFeeds`, `Platform/CalendarFetcher`) |
| Focus / Do Not Disturb | not readable on Windows; *Microphone or camera in use* covers calls instead |
| `presentationOptions` lock | a low-level keyboard hook, only during a locked break |
| Carbon hot keys | `RegisterHotKey` |
| `UNUserNotificationCenter` | toast notifications under an app ID Recess registers, or its own banner |
| AppleScript / shell scripts | PowerShell, batch files and programs |
| `SMAppService` login item | the per-user `Run` key |
| URL scheme in Info.plist | `HKCU\Software\Classes\recess`, plus a named pipe to the running copy |

The settings file has the same shape on both systems (camelCase keys and enum names, weekdays 1 = Sunday), but files aren't meant to move between them: shortcuts are stored as Windows virtual-key codes, and apps by program file name (`Zoom.exe`) rather than bundle ID.
