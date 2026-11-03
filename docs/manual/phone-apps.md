# Phone apps (Android, iPhone and iPad)

The phone apps show **your** LifeHub full screen, signed in as you, with notifications, the share
menu, shortcuts and an **offline mode** (keep using your lists without a connection; changes are
sent later).

> **Before you start**, LifeHub must be reachable from your phone. In the desktop app choose
> **This computer + my phone** or **A server for my household** (or use Docker / Home Assistant
> with the port open). The setup assistant's last step, and **Settings → This computer**, show the
> address to use — something like `http://192.168.1.10:8099`.

## You'll need two things

1. **The address** of your LifeHub (above).
2. **Your API key**: in LifeHub, **Settings → Security & API → Your API key → Show**. Everyone in
   the household has their own.

## Android

1. On the phone, open the [Releases page](https://github.com/shotgn16/LifeHub/releases/latest)
   and download **`LifeHub-<version>-Android.apk`**.
2. Open the download. If asked, allow your browser to **install unknown apps**, then press
   **Install**.
   - *"App from unverified developer"* (newer Android versions)? See
     [mobile-apps.md](../mobile-apps.md#installing-on-android-without-the-play-store)
     for the one-time setting, or install from a computer with `adb install`.
3. Open **LifeHub**, enter the address and API key, press **Check & save**. It says
   *"Signed in as …"*.
4. Allow notifications when asked. If they arrive late: **Settings → Apps → LifeHub → App battery
   usage → Unrestricted**.

## iPhone and iPad

Apple only allows apps that are signed for your device, so installing outside the App Store takes
a few more steps. The same app works on iPhone and iPad.

**Easiest (Windows or Mac): Sideloadly**

1. Download **`LifeHub-<version>-iPhone-iPad-unsigned.ipa`** from the
   [Releases page](https://github.com/shotgn16/LifeHub/releases/latest).
2. Install [Sideloadly](https://sideloadly.io) (on Windows also iTunes and iCloud from Apple's
   website).
3. Connect the iPhone/iPad with a cable, drag the `.ipa` into Sideloadly, enter your Apple ID and
   press **Start**.
4. On the device: **Settings → Privacy & Security → Developer Mode → On** (it restarts), then
   **Settings → General → VPN & Device Management →** your Apple ID **→ Trust**.
5. Open **LifeHub**, enter the address and API key, press **Check & save**.

With a free Apple ID the app needs re-installing every **7 days** (AltStore/SideStore can do this
automatically over Wi-Fi). A paid Apple Developer account (£79/US$99 a year) lasts a year, or lets
you use TestFlight. All the options, including Xcode: [mobile-apps.md](../mobile-apps.md).

## Using the apps

- **Offline**: if LifeHub can't be reached, the app shows your saved tasks, shopping list, notes
  and meals. Add and tick things off as usual — they're sent when LifeHub is back.
- **Share** text or a link from any app to LifeHub to save it as a note, task or shopping item.
- **Shortcuts**: long-press the app icon for Scan, Shopping, Notes and Tasks. On iPhone, Siri
  understands "Add a note in LifeHub" and "Add to my LifeHub shopping list".
- **Away from home**: your phone needs an https address for LifeHub — see
  [Remote access](../remote-access.md). The camera barcode scanner also needs https; otherwise use
  **Take photo**.

Want them in the Google Play Store or App Store? See [publishing-apps.md](../publishing-apps.md).
