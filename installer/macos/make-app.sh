#!/bin/bash
# Builds LifeHub.app and a .dmg from a self-contained macOS publish folder.
#   make-app.sh <publish-dir> <version> <arch: arm64|x64> <out-dir>
set -euo pipefail
PUB="$1"; VERSION="$2"; ARCH="$3"; OUT="$4"
HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$HERE/../.."
WORK="$(mktemp -d)"
APP="$WORK/dmg/LifeHub.app"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources" "$OUT"
cp -R "$PUB"/. "$APP/Contents/MacOS/"
sed "s/__VERSION__/$VERSION/g" "$HERE/Info.plist" > "$APP/Contents/Info.plist"
printf 'APPL????' > "$APP/Contents/PkgInfo"

# Icon: LifeHub.icns from the 1024px-ish PNG
ICONSET="$WORK/LifeHub.iconset"; mkdir -p "$ICONSET"
SRC="$ROOT/lifehub/src/LifeHub/Desktop/lifehub.png"
for s in 16 32 128 256 512; do
  sips -z $s $s "$SRC" --out "$ICONSET/icon_${s}x${s}.png" >/dev/null
  d=$((s*2)); sips -z $d $d "$SRC" --out "$ICONSET/icon_${s}x${s}@2x.png" >/dev/null
done
iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/LifeHub.icns"

chmod +x "$APP/Contents/MacOS/LifeHub"
# Not notarised yet: an ad-hoc signature lets it run (Apple Silicon needs at least that).
codesign --force --deep --sign - "$APP"
codesign --verify --deep "$APP"

ln -s /Applications "$WORK/dmg/Applications"
cp "$HERE/Read me first.txt" "$WORK/dmg/Read me first.txt"
NAME="LifeHub-$VERSION-macOS-$([ "$ARCH" = arm64 ] && echo "AppleSilicon" || echo "Intel")"
hdiutil create -volname "LifeHub $VERSION" -srcfolder "$WORK/dmg" -ov -format UDZO "$OUT/$NAME.dmg"
(cd "$WORK/dmg" && ditto -c -k --keepParent LifeHub.app "$OUT/$NAME.zip")
echo "Built $OUT/$NAME.dmg"
