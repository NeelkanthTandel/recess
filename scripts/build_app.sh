#!/bin/sh
# Builds Recess.app (release) into build/Recess.app.
#
#   scripts/build_app.sh               build for this Mac's architecture
#   scripts/build_app.sh --universal   build for Apple Silicon and Intel (used for releases)
#   scripts/build_app.sh --install     also copy to ~/Applications and launch
#
# Environment (all optional; used by the release workflow):
#   VERSION         marketing version, e.g. 1.2.0 (default: the one in Resources/Info.plist)
#   BUILD_NUMBER    CFBundleVersion (default: VERSION)
#   SIGN_IDENTITY   codesigning identity, e.g. "Developer ID Application: …" (default: "-", ad hoc)
set -e
cd "$(dirname "$0")/.."
ROOT=$(pwd)
APP="$ROOT/build/Recess.app"

UNIVERSAL=0
INSTALL=0
for arg in "$@"; do
  case "$arg" in
    --universal) UNIVERSAL=1 ;;
    --install) INSTALL=1 ;;
    *) echo "Unknown option: $arg" >&2; exit 2 ;;
  esac
done

if [ "$UNIVERSAL" = 1 ]; then
  swift build -c release --arch arm64 --arch x86_64
  BIN=$(swift build -c release --arch arm64 --arch x86_64 --show-bin-path)/Recess
else
  swift build -c release
  BIN=$(swift build -c release --show-bin-path)/Recess
fi

rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "$BIN" "$APP/Contents/MacOS/Recess"
cp Resources/Info.plist "$APP/Contents/Info.plist"
cp -R Resources/Themes "$APP/Contents/Resources/Themes"

if [ -n "$VERSION" ]; then
  /usr/libexec/PlistBuddy -c "Set :CFBundleShortVersionString $VERSION" "$APP/Contents/Info.plist"
  /usr/libexec/PlistBuddy -c "Set :CFBundleVersion ${BUILD_NUMBER:-$VERSION}" "$APP/Contents/Info.plist"
fi

# App icon
ICONSET="$ROOT/build/AppIcon.iconset"
if [ ! -f "$ROOT/build/AppIcon.icns" ]; then
  rm -rf "$ICONSET" && mkdir -p "$ICONSET"
  swift scripts/make_icon.swift "$ROOT/build/icon_1024.png"
  for s in 16 32 128 256 512; do
    sips -z $s $s "$ROOT/build/icon_1024.png" --out "$ICONSET/icon_${s}x${s}.png" >/dev/null
    d=$((s * 2))
    sips -z $d $d "$ROOT/build/icon_1024.png" --out "$ICONSET/icon_${s}x${s}@2x.png" >/dev/null
  done
  iconutil -c icns "$ICONSET" -o "$ROOT/build/AppIcon.icns"
fi
cp "$ROOT/build/AppIcon.icns" "$APP/Contents/Resources/AppIcon.icns"

# Signed with the hardened runtime, which blocks injected libraries (DYLD_INSERT_LIBRARIES).
# Ad hoc by default; a real identity also gets a secure timestamp, which notarization requires.
SIGN_IDENTITY="${SIGN_IDENTITY:--}"
if [ "$SIGN_IDENTITY" = "-" ]; then
  codesign --force --options runtime --entitlements Resources/Recess.entitlements --sign - "$APP"
else
  codesign --force --options runtime --timestamp --entitlements Resources/Recess.entitlements --sign "$SIGN_IDENTITY" "$APP"
fi
codesign --verify --strict "$APP"
echo "Built $APP ($(lipo -archs "$APP/Contents/MacOS/Recess"))"

if [ "$INSTALL" = 1 ]; then
  DEST="$HOME/Applications/Recess.app"
  mkdir -p "$HOME/Applications"
  pkill -x Recess 2>/dev/null || true
  sleep 0.5
  rm -rf "$DEST"
  cp -R "$APP" "$DEST"
  /System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister -f "$DEST"
  open "$DEST"
  echo "Installed and launched $DEST"
fi
