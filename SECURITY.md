# Security policy

## Reporting a vulnerability

Please **don't open a public issue** for security problems. Report them privately via GitHub: **Security → Report a vulnerability** on this repository. That opens a private advisory only the maintainers can see.

Please include what you found, how to reproduce it, and the Mac or Windows version and Recess version. You'll get a reply within a week. Fixes are released before details are made public.

## Supported versions

Only the latest release on `main` gets security fixes.

## What Recess can do on your Mac

Knowing this helps when judging a report. (Windows is covered below.)

- Recess is **not sandboxed**. It runs scripts that *you* put in `~/Library/Application Support/Recess/Scripts` (as break actions or rules), with your user's permissions. It never downloads or runs scripts from anywhere else. Script names can't point outside that folder (no `../` or symlinks out), and a script is stopped after 30 seconds.
- **Think before granting Full Disk Access.** Focus rules need it, but any program running as you can write to Recess's settings and Scripts folder. If Recess has Full Disk Access, a script added that way would run with it. Grant it only if you trust everything that runs as your user. Calendar access carries the same trade-off on a smaller scale.
- The app is signed with the **hardened runtime**, so libraries can't be injected into it via `DYLD_INSERT_LIBRARIES`.
- Any app or web page can open `recess://` links. Those can only start, postpone, skip, pause, resume or reset breaks, or open Settings. They can't run scripts, read files or change other settings. A browser asks you before opening an app link, and you can turn link handling off entirely under Settings › Advanced. Numbers in links are range-checked.
- HTML themes run in a WebKit view. Local themes can read only their own folder. Website themes are ordinary web pages and get no information from Recess.
- Recess makes no network requests of its own: no analytics, no update checks. The only network traffic comes from website or YouTube themes you choose.
- Data stays in `~/Library/Application Support/Recess` as plain JSON.

## What Recess can do on Windows

- Recess runs as your user, with no admin rights, and installs per user. It's **not sandboxed**. It runs scripts that *you* put in `%APPDATA%\Recess\Scripts`: PowerShell (with `-NoProfile -ExecutionPolicy Bypass`, which a Group Policy can still override), batch files and programs. It never downloads or runs scripts from anywhere else.
  - A script is named, not given as a path. Names with path separators, `:` (alternate data streams), reserved device names, trailing dots or spaces, or characters `cmd.exe` treats specially are refused, and links pointing outside the folder are refused.
  - Arguments are passed as a list, never as a command line. A script gets no window and is stopped, with anything it started, after 30 seconds.
- **Lock the computer into the break** uses a low-level keyboard hook to swallow Alt+Tab, Alt+Esc, Ctrl+Esc, the Windows key and Alt+F4. The hook exists only while a real break is on screen (never for previews), on its own thread, and only blocks those keys; it doesn't record anything. Ctrl+Alt+Del and Win+L always work, and shutting down is never blocked.
- `recess://` links work as on the Mac, and can be turned off the same way. A second copy of Recess (which is how links arrive) passes the link to the running one over a named pipe that only your user, on this computer, can open; nothing else on the command line is used.
- HTML themes run in Microsoft Edge WebView2. Each local theme is served from its own made-up address and can only reach its own folder. Downloads, pop-ups, permission requests (camera, location and so on) and links to other apps are refused, and developer tools are off. Website themes are ordinary web pages and get no information from Recess.
- **Calendars** are the only network requests Recess makes, and only for calendars you add. They use HTTPS only (a redirect away from HTTPS is refused), with a size limit and timeouts. A private iCal address works like a password, so it's stored encrypted with Windows DPAPI for your account and never written to logs.
- *Microphone or camera in use* rules read the per-app usage Windows keeps for its privacy indicator, in your own registry. Nothing is recorded or sent anywhere.
- Data stays in `%APPDATA%\Recess` as plain JSON, and the browser cache for themes in `%LOCALAPPDATA%\Recess\WebView2`. Uninstalling leaves your data in place.
