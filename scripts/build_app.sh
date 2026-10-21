#!/bin/sh
# Builds Recess.app (release), signs it ad hoc, and optionally installs it to ~/Applications.
#   scripts/build_app.sh            -> build/Recess.app
#   scripts/build_app.sh --install  -> also copy to ~/Applications and launch
set -e
cd "$(dirname "$0")/.."
ROOT=$(pwd)
APP="$ROOT/build/Recess.app"

swift build -c release
BIN=$(swift build -c release --show-bin-path)/Recess

rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "$BIN" "$APP/Contents/MacOS/Recess"
cp Resources/Info.plist "$APP/Contents/Info.plist"
cp -R Resources/Themes "$APP/Contents/Resources/Themes"

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

# Ad-hoc signature with the hardened runtime, which blocks injected libraries (DYLD_INSERT_LIBRARIES).
codesign --force --options runtime --entitlements Resources/Recess.entitlements --sign - "$APP"
echo "Built $APP"

if [ "$1" = "--install" ]; then
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
