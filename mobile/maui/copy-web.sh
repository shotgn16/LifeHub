#!/bin/bash
# Copies LifeHub's styles, scripts and icons, and the offline screen, into the phone app before building it.
# (The app compiles LifeHub's code directly; these are the web files its pages use.)
set -e
HERE="$(cd "$(dirname "$0")" && pwd)"
SRC="$HERE/../../lifehub/src/LifeHub/wwwroot"
DST="$HERE/LifeHub.Mobile/wwwroot"
mkdir -p "$DST/css" "$DST/js" "$HERE/LifeHub.Mobile/Resources/Raw"
cp "$SRC/app.css" "$SRC/favicon.svg" "$SRC/icon.svg" "$DST/"
cp "$SRC/css/"*.css "$DST/css/"
cp "$SRC/js/"*.js "$SRC/js/"*.txt "$DST/js/"
cp "$HERE/../shared/offline.html" "$HERE/LifeHub.Mobile/Resources/Raw/offline.html"
echo "Copied LifeHub's web files into the phone app."
