#!/bin/bash
# Smoke test on an emulator (used by CI): install, open, first start, look around, check for crashes.
set +e
APK="$1"; PKG=io.github.shotgn16.lifehub
shot() {   # small JPEG as a base64 notice, so it can be seen without downloading anything
  adb exec-out screencap -p > "/tmp/$1.png"
  python3 - "$1" <<'PY'
import base64, io, sys
from PIL import Image
im = Image.open(f"/tmp/{sys.argv[1]}.png").convert("RGB")
im.thumbnail((300, 640))
b = io.BytesIO(); im.save(b, "JPEG", quality=45)
print(f"::notice title=screenshot {sys.argv[1]}::" + base64.b64encode(b.getvalue()).decode())
PY
}
texts() { adb shell uiautomator dump /sdcard/ui.xml >/dev/null 2>&1; adb shell cat /sdcard/ui.xml | grep -o 'text="[^"]*"' | grep -v 'text=""' | head -40 | tr '\n' ' '; }
tap_text() {   # taps the first element whose text contains $1
  adb shell uiautomator dump /sdcard/ui.xml >/dev/null 2>&1
  b=$(adb shell cat /sdcard/ui.xml | tr '>' '\n' | grep -F "$1" | grep -o 'bounds="[^"]*"' | head -1 | grep -oE '[0-9]+')
  set -- $b
  [ -n "$4" ] && adb shell input tap $(( ($1 + $3) / 2 )) $(( ($2 + $4) / 2 ))
}
adb install -r "$APK" || { echo "::error::APK didn't install"; exit 1; }
adb logcat -c
adb shell monkey -p $PKG -c android.intent.category.LAUNCHER 1 >/dev/null
sleep 40
shot 1-start
echo "::notice title=screen 1::$(texts)"
tap_text "Use LifeHub on this phone"; sleep 4
adb shell input text "Alex"; sleep 1
tap_text "Start"; sleep 6
# Android asks to allow notifications
tap_text "Allow"; sleep 12
shot 2-today
echo "::notice title=screen 2::$(texts)"
crash=$(adb logcat -d | grep -E "FATAL EXCEPTION|Unhandled Exception|Unhandled managed exception|System\.[A-Za-z.]*Exception" | head -15)
running=$(adb shell pidof $PKG)
if [ -n "$crash" ]; then echo "$crash" | while read -r l; do echo "::warning::$l"; done; fi
adb logcat -d | grep -iE "lifehub|blazor|chromium.*console" | grep -iE "error|exception|fail" | head -15 | while read -r l; do echo "::warning::$l"; done
if [ -z "$running" ]; then echo "::error::LifeHub isn't running any more (crashed)"; exit 1; fi
echo "::notice::LifeHub opened and is still running"
