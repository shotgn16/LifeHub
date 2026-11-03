#!/bin/bash
# Installs LifeHub for the current user (no root needed, except to add missing system libraries).
set -e
HERE="$(cd "$(dirname "$0")" && pwd)"
DEST="${XDG_DATA_HOME:-$HOME/.local/share}/lifehub-app"
BIN="$HOME/.local/bin"

say() { printf '\033[1;36m%s\033[0m\n' "$*"; }

has_webkit() {
  ldconfig -p 2>/dev/null | grep -qE 'libwebkit2gtk-4\.(1|0)\.so' && return 0
  ls /usr/lib*/libwebkit2gtk-4.* /usr/lib/*/libwebkit2gtk-4.* >/dev/null 2>&1
}

if ! has_webkit; then
  say "LifeHub needs WebKitGTK (draws the app window). Installing it — you may be asked for your password."
  if command -v apt-get >/dev/null; then sudo apt-get update && (sudo apt-get install -y libwebkit2gtk-4.1-0 || sudo apt-get install -y libwebkit2gtk-4.0-37)
  elif command -v dnf >/dev/null; then sudo dnf install -y webkit2gtk4.1 || sudo dnf install -y webkit2gtk4.0
  elif command -v pacman >/dev/null; then sudo pacman -S --needed --noconfirm webkit2gtk-4.1
  elif command -v zypper >/dev/null; then sudo zypper install -y libwebkit2gtk-4_1-0
  else echo "Please install WebKitGTK 4.1 (or 4.0) with your package manager, then run this again."; exit 1
  fi
fi

say "Installing LifeHub to $DEST"
rm -rf "$DEST"; mkdir -p "$DEST" "$BIN" "$HOME/.local/share/applications" "$HOME/.local/share/icons/hicolor/256x256/apps"
cp -R "$HERE/app/." "$DEST/"
chmod +x "$DEST/LifeHub"
ln -sf "$DEST/LifeHub" "$BIN/lifehub"
sed "s#__EXEC__#$DEST/LifeHub#g" "$HERE/lifehub.desktop" > "$HOME/.local/share/applications/lifehub.desktop"
cp "$HERE/lifehub.png" "$HOME/.local/share/icons/hicolor/256x256/apps/lifehub.png"
command -v update-desktop-database >/dev/null && update-desktop-database -q "$HOME/.local/share/applications" || true

say "Done. Starting LifeHub — its setup assistant will guide you."
nohup "$DEST/LifeHub" >/dev/null 2>&1 &
