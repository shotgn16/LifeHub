#!/bin/bash
# Removes LifeHub installed with install.sh (asks before deleting your data).
DEST="${XDG_DATA_HOME:-$HOME/.local/share}/lifehub-app"
[ -x "$DEST/LifeHub" ] && "$DEST/LifeHub" --uninstall-service || true
rm -rf "$DEST" "$HOME/.local/bin/lifehub" "$HOME/.local/share/applications/lifehub.desktop" "$HOME/.local/share/icons/hicolor/256x256/apps/lifehub.png"
DATA="${XDG_DATA_HOME:-$HOME/.local/share}/lifehub"
if [ -d "$DATA" ]; then
  read -r -p "Also delete your LifeHub data in $DATA? [y/N] " a
  [ "$a" = y ] || [ "$a" = Y ] && rm -rf "$DATA" && echo "Data deleted." || echo "Data kept."
fi
echo "LifeHub removed."
