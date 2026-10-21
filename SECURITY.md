# Security policy

## Reporting a vulnerability

Please **don't open a public issue** for security problems. Report them privately via GitHub: **Security → Report a vulnerability** on this repository. That opens a private advisory only the maintainers can see.

Please include what you found, how to reproduce it, and the macOS and Recess versions. You'll get a reply within a week. Fixes are released before details are made public.

## Supported versions

Only the latest release on `main` gets security fixes.

## What Recess can do on your Mac

Knowing this helps when judging a report:

- Recess is **not sandboxed**. It runs scripts that *you* put in `~/Library/Application Support/Recess/Scripts` (as break actions or rules), with your user's permissions. It never downloads or runs scripts from anywhere else. Script names can't point outside that folder (no `../` or symlinks out), and a script is stopped after 30 seconds.
- **Think before granting Full Disk Access.** Focus rules need it, but any program running as you can write to Recess's settings and Scripts folder. If Recess has Full Disk Access, a script added that way would run with it. Grant it only if you trust everything that runs as your user. Calendar access carries the same trade-off on a smaller scale.
- The app is signed with the **hardened runtime**, so libraries can't be injected into it via `DYLD_INSERT_LIBRARIES`.
- Any app or web page can open `recess://` links. Those can only start, postpone, skip, pause, resume or reset breaks, or open Settings. They can't run scripts, read files or change other settings. A browser asks you before opening an app link, and you can turn link handling off entirely under Settings › Advanced. Numbers in links are range-checked.
- HTML themes run in a WebKit view. Local themes can read only their own folder. Website themes are ordinary web pages and get no information from Recess.
- Recess makes no network requests of its own: no analytics, no update checks. The only network traffic comes from website or YouTube themes you choose.
- Data stays in `~/Library/Application Support/Recess` as plain JSON.
