<img src="site/assets/icon.png" alt="Recess app icon" width="96" height="96">

# Recess

**A free, open-source break reminder for macOS.** Recess dims your screen when it's time to rest your eyes, stretch or get up. You can postpone or skip when you're in the middle of something, and it notices when you've already stepped away.

**[Download Recess.dmg](https://github.com/NeelkanthTandel/recess/releases/latest/download/Recess.dmg)** · [Website](https://recess.always17.com/) · macOS 14+, Apple Silicon and Intel

[![CI](https://github.com/NeelkanthTandel/recess/actions/workflows/ci.yml/badge.svg)](https://github.com/NeelkanthTandel/recess/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
![macOS 14+](https://img.shields.io/badge/macOS-14%2B-lightgrey)

## Features

- **As many breaks as you like.** It starts with a 10-minute *Normal* break every hour and a 15-second *Micro* break every 15 minutes.
- **Flexible timing.** The next break can be timed from when the last one was due, from when it ended, at a set minute each hour, or from your calendar events. Breaks can be limited to certain hours (including overnight windows) and days of the week.
- **Notices when you're away.** Time away from the keyboard counts as a break, and so does sleep. Each break chooses whether its clock keeps running, stops or winds back while you're away.
- **Priorities.** Breaks higher in the list win; a smaller break steps aside when a bigger one is about to start.
- **Gentle or strict.** A break can start right away, wait for a pause in your typing, or ask first. On the break screen you can postpone (two buttons), skip, end early, or require a click to finish.
- **Postpone & skip limits.** Allow only N postpones or skips per break, hour or day. The limits apply everywhere: break screen, menu bar, shortcuts and URL commands.
- **Themes.** A countdown ring, a breathing guide, clocks, eye-rest and desk-stretch routines, or your own text, website, YouTube video or photo folder.
- **Actions** before, during and after breaks: notifications, sounds, speech, a screen flash, or your own AppleScript, shell or Automator scripts.
- **Rules.** Postpone or skip breaks while a given app is in front or open, during a fullscreen app, Focus, screensaver or calendar event, or whenever a script you write says so.
- **Menu bar item** with a countdown and quick commands, **global keyboard shortcuts**, and **`recess://` links** for automation.
- **Activity.** A log of breaks taken, postponed and skipped, a statistics chart, and an optional per-app usage timeline.
- **Private.** No accounts, analytics or network calls of its own; your settings are plain JSON on your Mac.

## Install

1. Download **[Recess.dmg](https://github.com/NeelkanthTandel/recess/releases/latest/download/Recess.dmg)** from the [latest release](https://github.com/NeelkanthTandel/recess/releases/latest).
2. Open it and drag **Recess** into **Applications**.

Requires macOS 14 Sonoma or later, on Apple Silicon or Intel.

### First launch

Recess isn't notarized by Apple yet, so the first time you open it macOS says it can't verify the app. You only need to allow it once:

1. Open Recess. When macOS says it can't verify it, click **Done**.
2. Open **System Settings › Privacy & Security**, scroll down to **Security**, and click **Open Anyway** next to Recess.
3. Confirm with your password or Touch ID, then click **Open**.

Or, in Terminal: `xattr -dr com.apple.quarantine /Applications/Recess.app`

Each release lists the DMG's SHA-256 checksum, so you can check your download with `shasum -a 256 Recess.dmg`.

### Build from source

Prefer to build it yourself? It takes about a minute with Xcode 16 or later:

```bash
git clone https://github.com/NeelkanthTandel/recess.git
cd recess
make install        # builds a release app, copies it to ~/Applications and opens it
```

On first launch a short setup assistant helps you choose your breaks. Recess then lives in the menu bar.

### Permissions (all optional)

| Permission | Used for |
| --- | --- |
| Notifications | "Break coming up" warnings. Without it, Recess shows its own banner. |
| Calendars | Calendar-timed breaks and "during a meeting" rules |
| Accessibility | Only for the *Input monitoring* idle detector (the default needs nothing) |
| Full Disk Access | Only for Focus rules, which read macOS's Do Not Disturb state. Read [SECURITY.md](SECURITY.md) before granting it. |

## Automation

```text
recess://start                     start the next break (or ?break=Micro)
recess://postpone?minutes=5        postpone the current or next break
recess://skip                      skip the current or next break
recess://makedue?break=Normal&seconds=30
recess://pause?minutes=30          pause all breaks (omit minutes for "until resumed")
recess://resume   recess://reset   recess://stopsound   recess://settings
```

You can turn these off under **Settings › Advanced**.

Scripts for actions and rules live in `~/Library/Application Support/Recess/Scripts`. The samples there show how to detect Zoom, Teams and FaceTime calls.

## Contributing

Contributions are welcome! See [CONTRIBUTING.md](CONTRIBUTING.md) for setup, and [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for how the app fits together. Please report security issues privately as described in [SECURITY.md](SECURITY.md).

## License

[MIT](LICENSE).
