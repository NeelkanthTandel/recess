# Contributing to Recess

Thanks for helping! Bug reports, ideas, themes and code are all welcome.

## Getting started

Requirements: macOS 14 or later, and Xcode 16 or later (or the matching Swift toolchain).

```bash
git clone https://github.com/NeelkanthTandel/recess.git
cd recess
make build     # debug build
make test      # unit tests
make app       # release build → build/Recess.app
make install   # copy to ~/Applications and launch
```

You can also open `Package.swift` in Xcode and run the `Recess` scheme.

### Try changes without touching your real data

```bash
make run-dev   # launches a debug build with RECESS_DATA_DIR=./.devdata
```

With `RECESS_DATA_DIR` set, Recess reads and writes only that folder. Debug builds also accept `recess://snapshot` and `recess://testnotify` (see [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md#testing-hooks)), which are handy for checking UI from scripts.

## Before you open a pull request

- `make lint` passes (it runs `swift format lint` with the repo's `.swift-format` config). `make format` fixes most issues.
- `make test` passes, and new logic in `Model/` or `Engine/` has a test.
- The app builds without warnings (`make app`).
- UI changes include a screenshot in the PR.
- One topic per PR. Small PRs get reviewed faster.

## Code guidelines

- Read [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) first: it explains where things live and how a break flows through the scheduler.
- UI state lives on the main actor. `AppModel` and `Scheduler` are `@MainActor`.
- Don't write to `AppModel.breaks` unless a value really changed (see the architecture doc).
- New settings fields need a default value so older JSON files still load: `Store.decodeMerging` fills missing keys from the defaults.
- Never run user-supplied strings through a shell. Scripts are executed by path, with arguments passed separately.
- Keep wording friendly and plain in the UI, and in your own words. **Don't copy text, images or code from proprietary apps.** Recess is an independent project.

## Themes

Themes are folders: `Name.recesstheme/Info.json` plus `Name.recesstheme/Documents/index.html`. Copy one from `Resources/Themes` to get started, and see [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md#themes) for how a theme can get the break name and time remaining. To contribute a theme, add it under `Resources/Themes`. It must be your own work, or carry a compatible licence that you name in the PR. It shouldn't load remote scripts.

## The website

The landing page lives in [`site/`](site/): plain HTML and CSS, one small script (`app.js`) for the break demo, and a self-hosted font (Atkinson Hyperlegible Next, under the SIL Open Font License in `site/fonts/OFL.txt`). There's no build step: open `site/index.html` in a browser to preview it. Changes to `site/` are published to GitHub Pages when they're merged into `main`. Please keep it free of trackers, third-party scripts and requests to other sites.

## Releases

Maintainers cut releases by pushing a version tag; see [docs/RELEASING.md](docs/RELEASING.md).

## Reporting bugs and security issues

Use the issue templates for bugs and feature requests. For security problems, follow [SECURITY.md](SECURITY.md) instead of opening a public issue.

By contributing, you agree that your contributions are licensed under the [MIT License](LICENSE).
