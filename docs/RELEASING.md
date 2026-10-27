# Releasing

Releases are built by GitHub Actions ([`.github/workflows/release.yml`](../.github/workflows/release.yml)) when a version tag is pushed. Only maintainers can push `v*` tags; a repository ruleset protects them. One release carries both the Mac and the Windows app.

## Cutting a release

1. Make sure `main` is green and has everything you want to ship.
2. Tag the commit and push the tag:

   ```bash
   git checkout main && git pull
   git tag -a v1.2.0 -m "Recess 1.2.0"
   git push origin v1.2.0
   ```

3. The **Release** workflow checks the tag, then builds both apps at the same time:
   - **Mac:** runs the tests, builds a universal (Apple Silicon + Intel) `Recess.app` with version `1.2.0`, and packs it into `Recess.dmg` with a shortcut to Applications.
   - **Windows:** runs the tests, publishes a self-contained x64 build with version `1.2.0` (no .NET install needed; it also runs on Windows on Arm), and packs it two ways: `Recess-Setup.exe`, a per-user installer made with Inno Setup ([`windows/installer/Recess.iss`](../windows/installer/Recess.iss)), and `Recess-windows.zip`, a portable copy.
4. Once both have built, a last job writes a `.sha256` file for each download and creates the GitHub Release with everything at once, so a release is never half there. The notes explain the first-launch prompts and list the checksums, followed by the changes since the last tag.

These links always point at the newest release, so the download links never need updating.

- `https://github.com/NeelkanthTandel/recess/releases/latest/download/Recess.dmg`
- `https://github.com/NeelkanthTandel/recess/releases/latest/download/Recess-Setup.exe`
- `https://github.com/NeelkanthTandel/recess/releases/latest/download/Recess-windows.zip`

5. Once the release is out, update the version on the website in a small PR: `softwareVersion` and `datePublished` in the JSON-LD near the top of `site/index.html`, "Current version" in `site/llms.txt`, and `lastmod` in `site/sitemap.xml`. Search engines and AI assistants read these; see [SEO.md](SEO.md).

To try the packaging locally, on a Mac:

```bash
VERSION=1.2.0 scripts/build_app.sh --universal
scripts/make_dmg.sh            # → build/Recess.dmg
```

and on Windows, with the [.NET 10 SDK](https://dotnet.microsoft.com/download) and [Inno Setup 6](https://jrsoftware.org/isinfo.php):

```powershell
cd windows
dotnet publish src/Recess.App/Recess.App.csproj -c Release -r win-x64 --self-contained -p:Version=1.2.0 -p:DebugType=none -o publish/Recess
& "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe" /DAppVersion=1.2.0 installer/Recess.iss   # → publish/Recess-Setup.exe
```

Every pull request's CI also builds the Windows zip and installer, as the **Recess-windows** artifact.

## Signing and notarization (optional)

### Mac

Without signing secrets, releases are signed ad hoc and people see a one-time Gatekeeper prompt; the release notes and website explain how to open the app. To remove the prompt, join the Apple Developer Program and add these **repository secrets** (Settings › Secrets and variables › Actions). The workflow then signs with your Developer ID, notarizes and staples the DMG automatically.

| Secret | What it is |
| --- | --- |
| `MACOS_CERTIFICATE_P12` | Your **Developer ID Application** certificate and private key, exported from Keychain Access as a `.p12`, then base64-encoded: `base64 -i cert.p12 \| pbcopy` |
| `MACOS_CERTIFICATE_PASSWORD` | The password you chose when exporting the `.p12` |
| `APPLE_ID` | The Apple ID email of your developer account |
| `APPLE_TEAM_ID` | Your 10-character Team ID (developer.apple.com › Membership) |
| `APPLE_APP_PASSWORD` | An app-specific password for that Apple ID (appleid.apple.com › Sign-In and Security) |

Once releases are notarized, remove the Mac "First launch" sections from `README.md` and `site/index.html`, and update the "Why does my computer warn me" answer on the website and in `site/llms.txt`.

### Windows

The Windows downloads aren't signed yet, so Windows SmartScreen shows **Windows protected your PC** the first time (More info → Run anyway), and on a new PC with Smart App Control turned on, Windows won't run them at all. Signing fixes both. Two routes suit an open-source project:

- **[SignPath Foundation](https://signpath.org/)** signs open-source projects for free. Apply with the repository; once accepted, add their GitHub action to the `windows` job.
- **[Azure Trusted Signing](https://learn.microsoft.com/azure/trusted-signing/)** is a low-cost Microsoft service, open to individuals in some countries. Add `azure/trusted-signing-action` to the `windows` job, with the account details as secrets.

Either way, sign twice in the `windows` job: `publish/Recess/Recess.exe` after **Publish** (so the installed app is signed too), and `publish/Recess-Setup.exe` after **Build installer**. The zip is made after the first signing. The checksums are computed afterwards, in the publish job, so they match the signed files. Once Windows releases are signed, remove the SmartScreen notes from `README.md`, `site/index.html`, `site/llms.txt` and the release notes in `release.yml`.

Signing runs only in the release jobs, which only run for tags you push. Pull requests, including ones from forks, never see these secrets.
