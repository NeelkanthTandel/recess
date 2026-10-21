#!/bin/sh
# Packs build/Recess.app into build/Recess.dmg: a compressed disk image with the app and a
# shortcut to /Applications, so people can drag it across. Run scripts/build_app.sh first.
#
# Environment (optional):
#   SIGN_IDENTITY   if set to a real identity (not "-"), the disk image is signed too
set -e
cd "$(dirname "$0")/.."
APP="build/Recess.app"
DMG="build/Recess.dmg"
STAGING="build/dmg"

[ -d "$APP" ] || { echo "Missing $APP; run scripts/build_app.sh first." >&2; exit 1; }

rm -rf "$STAGING" "$DMG"
mkdir -p "$STAGING"
cp -R "$APP" "$STAGING/"
ln -s /Applications "$STAGING/Applications"

hdiutil create -volname "Recess" -srcfolder "$STAGING" -fs HFS+ -format UDZO -imagekey zlib-level=9 -ov "$DMG" >/dev/null
rm -rf "$STAGING"

if [ -n "$SIGN_IDENTITY" ] && [ "$SIGN_IDENTITY" != "-" ]; then
  codesign --force --timestamp --sign "$SIGN_IDENTITY" "$DMG"
fi

hdiutil verify "$DMG" >/dev/null
echo "Built $DMG ($(du -h "$DMG" | cut -f1))"
