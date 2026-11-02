# Phone apps (Android & iPhone)

The LifeHub apps are companions to **your own LifeHub server**. They show LifeHub full screen,
signed in as you, and add things a browser can't do.

| | Android (`mobile/android`) | iPhone (`mobile/ios`) |
|---|---|---|
| LifeHub signed in as you (with your API key, no password) | ✓ | ✓ |
| Barcode scanner, photos and uploads | ✓ (camera needs an https address) | ✓ (camera needs an https address) |
| Notifications from LifeHub | ✓ checks every 15 minutes | ✓ when you open the app, and when iOS allows a background refresh |
| **Time in the Bible app**, measured on the phone (no Home Assistant needed) | ✓ Usage access | Shortcuts automation with the app's **Bible app opened / closed** actions |
| Share text or links to LifeHub (passages are logged; anything else becomes a note) | ✓ share menu | Shortcuts actions |
| Siri and Shortcuts: add a note, task or shopping item, log a reading, open a page | — | ✓ |
| Home-screen shortcuts (long-press the icon): Scan, Shopping, Notes, What did I read? | ✓ | ✓ (Open LifeHub → page) |
| **Phone app settings** link in LifeHub's menu | ✓ | ✓ |
| **Works offline**, then syncs when LifeHub is back | ✓ | ✓ |

> **Your phone must be able to reach LifeHub.**
> - At home, use the direct port, for example `http://192.168.1.10:8099`. In Home Assistant, map
>   the add-on's port under the add-on's **Configuration → Network**.
> - When you're out, use an https address, for example a Cloudflare Tunnel (see
>   [remote-access.md](remote-access.md)).
> - The camera scanner only works over **https**. Over http, use **Take photo** instead.
> - The Home Assistant ingress URL doesn't work in the apps, because it needs a Home Assistant
>   sign-in.

## Getting the apps

The apps are built automatically by GitHub Actions whenever `mobile/` changes:

1. Go to **github.com/shotgn16/LifeHub → Actions**.
2. Open **Android app** or **iPhone app**, then the latest green run.
3. Download it from **Artifacts** at the bottom:
   - **LifeHub-android-N**: `app-release.apk` (to install) and `app-release.aab` (for Google Play)
   - **LifeHub-ios-N**: `LifeHub-unsigned.ipa` (to sideload) and `LifeHub-ios-project.zip` (the
     Xcode project)
4. To build again, press **Run workflow** on either workflow.

## First run (both apps)

1. In LifeHub in a browser, go to **Settings → Security & API → Your API key → Show**, and copy
   the key. Each person has their own key, so the app is signed in as **you**.
2. Open the app, enter your **LifeHub address** and paste the **API key**, then press **Check &
   save**. It should say "Signed in as …".
3. Allow notifications when asked.
4. To change these later, open LifeHub's menu → **Phone app settings**.

## Offline mode

LifeHub's screens are drawn live by your server, so the apps keep their own copy for when it
can't be reached. Examples: no signal, away from home without an https address, or LifeHub
restarting for an update.

**What's kept on the phone.** The copy is refreshed whenever the app is online (Android also
every 15 minutes in the background):

- your open tasks
- the shopping list
- your recent notes
- this week's meals
- today's Bible verse, reading list and streak

**When the app switches.** If the app can't reach LifeHub, or LifeHub's live connection stays
down for 12 seconds, it shows the **offline screen**. It has Today, Tasks, Shopping, Notes and
Meals tabs. On that screen you can:

- add tasks (with a due date), shopping items and notes
- tick tasks done and tick items off the shopping list
- tick the verse of the day and your reading plan, or log what you read

**Waiting changes.** Each change is marked **waiting to send** and kept on the phone, even if you
close the app or restart the phone. They're sent automatically and in order:

- as soon as LifeHub can be reached
- when you open the app, or press **Try again**
- in the background: Android checks every 15 minutes once there's a connection; iPhone when iOS
  allows a background refresh

**What happens on the server.** Each change carries its own id, so a change is never applied
twice, even if the connection drops mid-send.

- A task added offline gets filed and planned as usual.
- A note is sorted into a category once it arrives.
- If something changed meanwhile, the change is skipped rather than causing an error. For
  example, someone else already ticked that shopping item, or the task was deleted.

**Getting back.** When LifeHub is reachable again, the banner turns green. Press **Open LifeHub**
to go back to the full app. Features that need the server stay online-only: the AI, the scanner,
Bible studies, deals and settings.

---

## Installing on your Pixel 9 Pro XL (unofficially, no Play Store)

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

If your Pixel refuses with a message about an **unverified developer**, either:

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

- **Time in the Bible app**:
  1. In the app, go to **Phone app settings → Time in the Bible app**.
  2. Press **Open Usage access**, find **LifeHub** and switch it on.
  3. Sessions are sent every 15 minutes and appear on the Bible page as "LifeHub app". If you
     also use the Home Assistant sensor, untick that phone in Settings → Bible to avoid counting
     twice.
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

Open `mobile/android` in **Android Studio** (Ladybug or later), then press **Run** with the phone
connected.

---

## Installing on your iPhone Air (unofficially, no App Store)

Apple only lets you install apps that are signed for your iPhone. In the UK there are no
alternative app marketplaces (those are EU-only), so these are your options:

| Way | Needs | Lasts |
|---|---|---|
| **Xcode with a free Apple ID** | a Mac | 7 days, then re-install from Xcode; max 3 apps |
| **Sideloadly or AltStore/SideStore** with a free Apple ID | a Mac or a Windows PC | 7 days; AltStore/SideStore can refresh it automatically over Wi-Fi |
| **Apple Developer Program** (£79/US$99 a year) | a Mac (or a cloud Mac) | 1 year; installs from Xcode, or **TestFlight** for you and your family |

### With Xcode (Mac)

1. Install **Xcode** from the Mac App Store, and XcodeGen with `brew install xcodegen`.
2. Get the project: either clone the repo, or download `LifeHub-ios-project.zip` from the
   **iPhone app** workflow's artifacts.
3. In `mobile/ios`, run `xcodegen`, then open `LifeHub.xcodeproj`.
4. Go to **Xcode → Settings → Accounts** and add your Apple ID.
5. Select the **LifeHub** target → **Signing & Capabilities** and choose your **Team** (your name,
   "Personal Team").
   - If Xcode says the bundle ID is taken, change it to something unique, for example
     `uk.yourname.lifehub`.
6. Connect the iPhone by cable and choose it as the run destination. Then:
   1. On the iPhone, turn on **Settings → Privacy & Security → Developer Mode** and restart.
   2. In Xcode, press **Run**.
   3. The first time, go to **Settings → General → VPN & Device Management** on the iPhone and
      **trust** your developer certificate.
7. With a free Apple ID the app stops opening after 7 days. Press **Run** in Xcode again to renew
   it. Your settings are kept.

### With Sideloadly (Windows or Mac)

1. Install [Sideloadly](https://sideloadly.io) and, on Windows, iTunes and iCloud from Apple's
   website (not the Microsoft Store).
2. Download `LifeHub-unsigned.ipa` from the **iPhone app** workflow's artifacts.
3. Connect the iPhone, drag the `.ipa` into Sideloadly, enter your Apple ID and press **Start**.
4. On the iPhone, turn on **Developer Mode**, then trust the certificate under **Settings →
   General → VPN & Device Management**.
5. Repeat every 7 days. AltStore or SideStore can do this automatically while the computer is on
   the same Wi-Fi.

Sideloadly and AltStore are third-party tools, so read their privacy notes. Consider using a
spare Apple ID with them.

### Turning on the extras

- **Notifications**: allow them on first run. iOS decides how often apps refresh in the
  background, so for instant reminders keep LifeHub's Home Assistant notifications on as well
  (Settings → Notifications → **Both**).
- **Time in the Bible app**:
  1. Open **Shortcuts → Automation → + → App**, choose **Bible**, tick **Is Opened**, and choose
     **Run Immediately**.
  2. Add the action **Bible app opened** (from LifeHub).
  3. Make a second automation with **Is Closed** → **Bible app closed**.
- **Siri**: say "Add a note in LifeHub", "Add to my LifeHub shopping list", "Log my Bible reading
  in LifeHub" or "Open LifeHub". All the actions are also in the Shortcuts app; for example, add
  "Add a note" to a share-sheet shortcut.

---

Publishing to the stores: see [publishing-apps.md](publishing-apps.md). Running AI models on the
phones: see [local-ai-on-phones.md](local-ai-on-phones.md).
