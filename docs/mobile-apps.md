# Phone apps (Android, iPhone & iPad)

> Just want to install them? The short, easy version is in the manual: [Phone apps](manual/phone-apps.md).

LifeHub 3.1 has **one app** for Android, iPhone and iPad (`mobile/maui`, .NET MAUI). It runs two ways:

- **On this phone, on its own** — the app runs LifeHub's own code (the same services and pages as the server, compiled
  into the app), for one person, with everything stored encrypted on the phone. Email is checked by the phone; the AI
  runs on the phone (WebLLM on the GPU) or is Ollama, ChatGPT, Gemini or Claude.
- **Connected to your LifeHub** — LifeHub full screen, signed in with your API key, with an encrypted offline copy
  (tasks, shopping, notes, meals) and notifications fetched from the server.

| | On its own | Connected |
|---|---|---|
| Sign-in | none (one person) | your API key (no password) |
| Notifications | shown by the phone, one channel per kind | the server's, fetched when you open the app and in the background |
| Background | email + reminders every 15/30/60/180 min (Android JobScheduler; iOS Background App Refresh when iOS allows) | the same schedule, for the server's news |
| Offline | completely | saved copy + changes sent later |
| Barcode scanner, photos, uploads, share menu | ✓ | ✓ (camera needs an https address) |

> **Connected: your phone must be able to reach LifeHub.**
> - At home, use the direct port, for example `http://192.168.1.10:8099`. On the desktop app,
>   choose **This computer + my phone** or **A server for my household**; the address is shown in
>   **Settings → This computer**. In Home Assistant, map the add-on's port under the add-on's
>   **Configuration → Network**.
> - When you're out, use an https address, for example a Cloudflare Tunnel (see
>   [remote-access.md](remote-access.md)).
> - The Home Assistant ingress URL doesn't work in the app, because it needs a Home Assistant sign-in.

## Getting the app

Every [release](https://github.com/shotgn16/LifeHub/releases/latest) includes `LifeHub-<version>-Android.apk` and
`LifeHub-<version>-iPhone-iPad-unsigned.ipa`. Newer builds come from **Actions → Phone apps** (artifacts
`LifeHub-android-N` and `LifeHub-ios-N`); each Android build is also opened on an emulator as a check.

## How it's built

```
mobile/maui/
  LifeHubShared.props        compiles lifehub/src/LifeHub (services, AI, email, Razor pages) into the app, minus the
                             web-server parts; differences are marked #if MOBILE / AppHost.IsPhoneApp
  copy-web.sh                copies LifeHub's CSS, scripts and the offline screen into the app before a build
  LifeHub.Mobile/
    MauiProgram.cs           the same AddLifeHubCore() as the server, plus the phone's key store, sign-in, notifier
    Mobile/                  first-start choice, This phone page, server mode, background jobs, on-device AI
    Platforms/Android|iOS/   notifications, background scheduling, camera permission, app entry points
    wwwroot/                 host page, phone styles, ondevice.js and WebLLM (vendor/web-llm, Apache-2.0)
```

Build it yourself (needs the .NET 10 SDK; Android Studio's SDK for Android, Xcode on a Mac for iOS):

```bash
bash mobile/maui/copy-web.sh
dotnet workload install maui-android            # or maui-ios on a Mac
dotnet publish mobile/maui/LifeHub.Mobile/LifeHub.Mobile.csproj -f net10.0-android -p:TargetFrameworks=net10.0-android -c Release
```

Add `-p:MicrosoftClientId=<your Entra app ID>` for the "Sign in with Microsoft" button
(see [manual/email.md](manual/email.md#setting-up-microsoft-sign-in-once-for-whoever-publishes-lifehub)).

## Installing on Android (without the Play Store)

### Option A: download and tap (simplest)

1. Download `app-release.apk` to the phone. You can open the GitHub artifact on the phone, or
   unzip it on a computer and copy the APK over.
2. Tap the file. When asked, allow your browser or Files app to **install unknown apps**.
3. Tap **Install**.

**About Google's 2026 rules.** Google is introducing **developer verification** for apps installed
outside the Play Store:

- It started on 30 September 2026 in Brazil, Indonesia, Singapore and Thailand, and Google plans
  to expand it worldwide in 2027.
- Before that, Google is rolling out an **"advanced flow"** for installing apps from unverified
  developers.

If your phone refuses with a message about an **unverified developer**, either:

- Turn it on once in **Settings → System → Developer options → Allow apps from unverified
  developers**. Read the warnings and wait out the one-time **24-hour** delay. Then install as
  above. You can choose 7 days or indefinitely, and you can turn Developer options off again
  afterwards.
- Or use Option B, because **adb installs aren't affected**.
- Or register yourself as a developer for free in the Android Developer Console (hobbyist /
  limited distribution), and sign the app with your own key.

### Option B: from a computer with adb

1. On the phone, turn on Developer options: **Settings → About phone →** tap **Build number** seven
   times. Then turn on **Settings → System → Developer options → USB debugging**.
2. On the computer, install
   [Android platform-tools](https://developer.android.com/tools/releases/platform-tools).
3. Connect the phone by USB, accept the prompt on the phone, and run:

   ```bash
   adb install -r app-release.apk
   ```

### Turning on the extras

- **Background checks:** in the app, **This phone → In the background**.
- **Battery.** If notifications arrive late, go to **Settings → Apps → LifeHub → App battery
  usage** and choose **Unrestricted**.

### Updates keep the same signature only with your own key

Without a signing key, each build is signed with a temporary key, so you must uninstall the old
app before installing a newer one. To make updates install over the top, create a key once:

```bash
keytool -genkeypair -v -keystore lifehub.jks -alias lifehub -keyalg RSA -keysize 4096 -validity 10000
base64 -w0 lifehub.jks > lifehub.jks.b64        # macOS: base64 -i lifehub.jks -o lifehub.jks.b64
```

Then add four **repository secrets** (GitHub → Settings → Secrets and variables → Actions):

| Secret | Value |
|---|---|
| `ANDROID_KEYSTORE_BASE64` | the contents of `lifehub.jks.b64` |
| `ANDROID_KEYSTORE_PASSWORD` | the keystore password |
| `ANDROID_KEY_ALIAS` | `lifehub` |
| `ANDROID_KEY_PASSWORD` | the key password |

**Keep `lifehub.jks` and its passwords safe and private.** You need the same key for every
future update, including on Google Play if you don't use Play App Signing.

### Building it yourself

See [How it's built](#how-its-built) above.

---

## Installing on iPhone and iPad (without the App Store)

The same app runs on iPhone and iPad.

Apple only lets you install apps that are signed for your device. Outside the EU there are no
alternative app marketplaces, so these are your options:

| Way | Needs | Lasts |
|---|---|---|
| **Your Mac with a free Apple ID** | a Mac with Xcode | 7 days, then build again; max 3 apps |
| **Sideloadly or AltStore/SideStore** with a free Apple ID | a Mac or a Windows PC | 7 days; AltStore/SideStore can refresh it automatically over Wi-Fi |
| **Apple Developer Program** (£79/US$99 a year) | a Mac (or a cloud Mac) | 1 year; installs from Xcode, or **TestFlight** for you and your family |

### From a Mac with your Apple ID

1. Install **Xcode** from the Mac App Store and the .NET 10 SDK, then `dotnet workload install maui-ios`.
2. Clone the repository and run `bash mobile/maui/copy-web.sh`.
3. Connect the iPhone/iPad, turn on **Developer Mode** on it, and run:

   ```bash
   dotnet build mobile/maui/LifeHub.Mobile/LifeHub.Mobile.csproj -f net10.0-ios -p:TargetFrameworks=net10.0-ios \
     -t:Run -p:RuntimeIdentifier=ios-arm64 -p:CodesignKey="Apple Development: you@example.com"
   ```

   With a free Apple ID, Xcode first needs to create your "Personal Team" signing certificate: open Xcode →
   Settings → Accounts → add your Apple ID → **Manage Certificates → + → Apple Development**. If the bundle ID
   `io.github.shotgn16.lifehub` is taken, add `-p:ApplicationId=uk.yourname.lifehub`.
4. Trust the certificate on the device: **Settings → General → VPN & Device Management**.

### With Sideloadly (Windows or Mac)

1. Install [Sideloadly](https://sideloadly.io) and, on Windows, iTunes and iCloud from Apple's
   website (not the Microsoft Store).
2. Download `LifeHub-<version>-iPhone-iPad-unsigned.ipa` from the latest release (or
   `LifeHub-unsigned.ipa` from the **Phone apps** workflow's artifacts).
3. Connect the iPhone, drag the `.ipa` into Sideloadly, enter your Apple ID and press **Start**.
4. On the iPhone, turn on **Developer Mode**, then trust the certificate under **Settings →
   General → VPN & Device Management**.
5. Repeat every 7 days. AltStore or SideStore can do this automatically while the computer is on
   the same Wi-Fi.

Sideloadly and AltStore are third-party tools, so read their privacy notes. Consider using a
spare Apple ID with them.

### Turning on the extras

- **Notifications**: allow them on first start. iOS decides how often apps refresh in the
  background, so for instant reminders when connected to a server, keep LifeHub's Home Assistant
  notifications on as well (Settings → Notifications → **Both**).
- **Background App Refresh**: keep it on for LifeHub (iOS Settings → General → Background App Refresh).

---

Publishing to the stores: see [publishing-apps.md](publishing-apps.md). Running AI models on the
phones: see [local-ai-on-phones.md](local-ai-on-phones.md).
