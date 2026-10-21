# Releasing

Releases are built by GitHub Actions ([`.github/workflows/release.yml`](../.github/workflows/release.yml)) when a version tag is pushed. Only maintainers can push `v*` tags; a repository ruleset protects them.

## Cutting a release

1. Make sure `main` is green and has everything you want to ship.
2. Tag the commit and push the tag:

   ```bash
   git checkout main && git pull
   git tag -a v1.2.0 -m "Recess 1.2.0"
   git push origin v1.2.0
   ```

3. The **Release** workflow runs the tests, then:
   - builds a universal (Apple Silicon + Intel) `Recess.app` with version `1.2.0`;
   - packs it into `Recess.dmg`, with a shortcut to Applications;
   - creates the GitHub Release, uploads `Recess.dmg` and `Recess.dmg.sha256`, and writes release notes with the changes since the last tag.

The download link `https://github.com/NeelkanthTandel/recess/releases/latest/download/Recess.dmg`, used by the README and the website, always points at the newest release, so nothing else needs updating.

To try the packaging locally:

```bash
VERSION=1.2.0 scripts/build_app.sh --universal
scripts/make_dmg.sh            # → build/Recess.dmg
```

## Signing and notarization (optional)

Without signing secrets, releases are signed ad hoc and people see a one-time Gatekeeper prompt; the release notes and website explain how to open the app. To remove the prompt, join the Apple Developer Program and add these **repository secrets** (Settings › Secrets and variables › Actions). The workflow then signs with your Developer ID, notarizes and staples the DMG automatically.

| Secret | What it is |
| --- | --- |
| `MACOS_CERTIFICATE_P12` | Your **Developer ID Application** certificate and private key, exported from Keychain Access as a `.p12`, then base64-encoded: `base64 -i cert.p12 \| pbcopy` |
| `MACOS_CERTIFICATE_PASSWORD` | The password you chose when exporting the `.p12` |
| `APPLE_ID` | The Apple ID email of your developer account |
| `APPLE_TEAM_ID` | Your 10-character Team ID (developer.apple.com › Membership) |
| `APPLE_APP_PASSWORD` | An app-specific password for that Apple ID (appleid.apple.com › Sign-In and Security) |

Signing runs only in the release job, which only runs for tags you push. Pull requests, including ones from forks, never see these secrets.

Once releases are notarized, remove the "First launch" sections from `README.md` and `site/index.html`.
