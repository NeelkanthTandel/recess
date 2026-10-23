# Recess for Windows

The Windows version of Recess: C# and WPF on .NET 10. It's being built in milestones toward the same features as the Mac app; until it ships, the releases only contain the Mac app.

## Layout

| Folder | What's in it |
|---|---|
| `src/Recess.Core` | The scheduling engine and data model. No Windows APIs, so it builds and its tests run on any OS. |
| `src/Recess.App` | The Windows app: tray icon, break screens, settings, and everything that talks to Windows. |
| `tests/Recess.Core.Tests` | Tests for the engine (xUnit). |

The HTML break themes are shared with the Mac app: the build copies them from `Resources/Themes`.

## Building

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download). On Windows 10 or 11:

```powershell
cd windows
dotnet build Recess.slnx
dotnet test --solution Recess.slnx
dotnet run --project src/Recess.App
```

Visual Studio 2022, Rider and VS Code (with C# Dev Kit) all open `Recess.slnx`.

On macOS or Linux the whole solution compiles too (`EnableWindowsTargeting`), so you can work on the engine and check that the app builds, but the app itself only runs on Windows.

Before opening a pull request, run `dotnet format Recess.slnx --verify-no-changes`; CI runs it along with the build and the tests.

To keep your real settings safe while developing, set `RECESS_DATA_DIR` to another folder. Debug builds then keep everything there.
