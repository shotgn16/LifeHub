#!/bin/bash
# Builds a .deb (Debian, Ubuntu, Mint, Pop!_OS, Raspberry Pi OS…) and a .tar.gz (any distribution)
# from a self-contained Linux publish folder.
#   build-deb.sh <publish-dir> <version> <deb-arch: amd64|arm64> <out-dir>
set -euo pipefail
PUB="$1"; VERSION="$2"; ARCH="$3"; OUT="$4"
HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$HERE/../.."
WORK="$(mktemp -d)"
PKG="$WORK/lifehub_${VERSION}_${ARCH}"
mkdir -p "$PKG/DEBIAN" "$PKG/opt/lifehub" "$PKG/usr/bin" "$PKG/usr/share/applications" "$PKG/usr/share/icons/hicolor/256x256/apps" "$OUT"
cp -R "$PUB"/. "$PKG/opt/lifehub/"
chmod 755 "$PKG/opt/lifehub/LifeHub"
cat > "$PKG/usr/bin/lifehub" <<'SH'
#!/bin/sh
exec /opt/lifehub/LifeHub "$@"
SH
chmod 755 "$PKG/usr/bin/lifehub"
sed "s#__EXEC__#lifehub#g" "$HERE/lifehub.desktop" > "$PKG/usr/share/applications/lifehub.desktop"
cp "$ROOT/lifehub/src/LifeHub/Desktop/lifehub.png" "$PKG/usr/share/icons/hicolor/256x256/apps/lifehub.png"
SIZE=$(du -sk "$PKG/opt" | cut -f1)
cat > "$PKG/DEBIAN/control" <<CTL
Package: lifehub
Version: $VERSION
Section: utils
Priority: optional
Architecture: $ARCH
Installed-Size: $SIZE
Depends: libwebkit2gtk-4.1-0, libgtk-3-0, libnotify4, libicu76 | libicu74 | libicu72 | libicu70 | libicu67 | libicu-dev, ca-certificates
Recommends: curl
Maintainer: LifeHub <noreply@github.com>
Homepage: https://github.com/shotgn16/LifeHub
Description: Household organiser - tasks, kitchen, meals, renewals and notes
 LifeHub runs on this computer, as a server for the household, or as a
 window onto LifeHub in Docker or Home Assistant. A setup assistant opens on
 first start and can install the local AI (Ollama).
CTL
cat > "$PKG/DEBIAN/prerm" <<'SH'
#!/bin/sh
# Removing the package: stop the signed-in user's background service if it's running.
[ "$1" = remove ] || exit 0
for home in /home/*; do
  u=$(basename "$home")
  if [ -f "$home/.config/systemd/user/lifehub.service" ]; then
    su - "$u" -c "systemctl --user disable --now lifehub.service" >/dev/null 2>&1 || true
    rm -f "$home/.config/systemd/user/lifehub.service"
  fi
done
exit 0
SH
chmod 755 "$PKG/DEBIAN/prerm"
cat > "$PKG/DEBIAN/postinst" <<'SH'
#!/bin/sh
command -v update-desktop-database >/dev/null && update-desktop-database -q /usr/share/applications || true
command -v gtk-update-icon-cache >/dev/null && gtk-update-icon-cache -q /usr/share/icons/hicolor || true
exit 0
SH
chmod 755 "$PKG/DEBIAN/postinst"
dpkg-deb --root-owner-group --build "$PKG" "$OUT/LifeHub-$VERSION-Linux-$ARCH.deb"

# Portable tar.gz with an installer script for other distributions (Fedora, Arch, openSUSE…).
TAR="$WORK/LifeHub-$VERSION-Linux-$ARCH"
mkdir -p "$TAR/app"
cp -R "$PUB"/. "$TAR/app/"
cp "$HERE/install.sh" "$HERE/uninstall.sh" "$TAR/"
cp "$HERE/lifehub.desktop" "$TAR/lifehub.desktop"
cp "$ROOT/lifehub/src/LifeHub/Desktop/lifehub.png" "$TAR/lifehub.png"
cat > "$TAR/README.txt" <<'TXT'
LifeHub for Linux
=================
Install:    ./install.sh        (installs for your user; asks for your password only to add missing system libraries)
Uninstall:  ./uninstall.sh
Then open LifeHub from your applications menu, or run:  lifehub
Manual: https://github.com/shotgn16/LifeHub/tree/main/docs/manual
TXT
chmod +x "$TAR/install.sh" "$TAR/uninstall.sh" "$TAR/app/LifeHub"
tar -C "$WORK" -czf "$OUT/LifeHub-$VERSION-Linux-$ARCH.tar.gz" "$(basename "$TAR")"
echo "Built $OUT/LifeHub-$VERSION-Linux-$ARCH.deb and .tar.gz"
