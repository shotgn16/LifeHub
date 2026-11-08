# Phone apps (Android, iPhone and iPad)

The LifeHub app works two ways — you choose the first time you open it:

| | **Use LifeHub on this phone** | **Connect to my LifeHub** |
|---|---|---|
| Who it's for | Just you, no computer or server needed | Your household's LifeHub on a computer, server, Docker or Home Assistant |
| Where your data is | On the phone, encrypted | On your LifeHub, with an encrypted offline copy on the phone |
| AI | On the phone itself, or Ollama, ChatGPT, Gemini or Claude | Whatever your LifeHub uses |
| Email | Checked by the phone (when you open the app, and in the background) | Checked by your LifeHub |
| Works offline | Yes, completely | Tasks, shopping, notes and meals; changes are sent later |

You can switch later in **This phone → Change how the app runs**.

## Install it

### Android

1. On the phone, open the [Releases page](https://github.com/shotgn16/LifeHub/releases/latest) and download
   **`LifeHub-<version>-Android.apk`**.
2. Open the download. If asked, allow your browser to **install unknown apps**, then press **Install**.
   - *"App from unverified developer"*? See [mobile-apps.md](../mobile-apps.md#installing-on-android-without-the-play-store)
     for the one-time setting, or install from a computer with `adb install`.
   - Had the older LifeHub app (1.x)? Uninstall it first.

### iPhone and iPad

Apple only allows apps signed for your device, so installing outside the App Store takes a few more steps. The same
app works on iPhone and iPad.

1. Download **`LifeHub-<version>-iPhone-iPad-unsigned.ipa`** from the
   [Releases page](https://github.com/shotgn16/LifeHub/releases/latest).
2. Install [Sideloadly](https://sideloadly.io) on a Windows PC or Mac (on Windows also iTunes and iCloud from
   Apple's website).
3. Connect the iPhone/iPad with a cable, drag the `.ipa` into Sideloadly, enter your Apple ID and press **Start**.
4. On the device: **Settings → Privacy & Security → Developer Mode → On** (it restarts), then **Settings → General →
   VPN & Device Management →** your Apple ID **→ Trust**.

With a free Apple ID the app needs re-installing every **7 days** (AltStore/SideStore can do it automatically; your
data is kept). A paid Apple Developer account lasts a year, or lets you use TestFlight. More:
[mobile-apps.md](../mobile-apps.md).

## First start

**Use LifeHub on this phone:** enter your name and press **Start**, then allow notifications. Next:

- **Email:** Settings → Email → **Add mailbox** ([Your email](email.md)).
- **AI:** **This phone → AI on this phone → Download and use**, or choose Ollama, ChatGPT, Gemini or Claude in
  Settings → AI ([The AI assistant](ai.md)).
- **Home Assistant** (optional): Settings → Home Assistant, as on a computer.

**Connect to my LifeHub:** enter your LifeHub's address (shown in **Settings → This computer** on the computer
running it — for Home Assistant, the add-on's port) and your **API key** (**Settings → Security & API → Show**), then
**Check & connect**. Each person uses their own key.

## In the background

**This phone → In the background:**

- **Check whenever I open the app** — email (or, connected, your LifeHub's news) straight away.
- **When the app is closed, check** every 15 minutes, 30 minutes, hour, 3 hours, or never.
  - **Android** runs it at most every 15 minutes and may wait a little to save battery. If checks stop, set
    LifeHub's battery use to **Unrestricted** (Settings → Apps → LifeHub → App battery usage).
  - **iPhone and iPad** decide when to run it themselves (they learn when you use the app), so it's roughly that
    often at best. Keep **Background App Refresh** on for LifeHub.
- On its own, what runs **while the app is open** (email every 10 minutes, reminders, Home Assistant lists) is in
  **Settings → Background tasks**, the same as on a computer.

## Notifications

Reminders, renewals, food to use up, a summary of what came from your email and the morning summary — each kind is
its own channel, so you can silence one in the phone's settings. Choose what you're told about in **Settings →
Notifications** ([Notifications](notifications.md)). Tap a notification to open the right page.

## Your data on the phone

On its own, everything is stored in the app's private storage, **encrypted** with a key kept in the phone's secure
keystore. **This phone → Back up and share** makes an encrypted backup you can save to Files, Drive or iCloud; it opens
with the recovery key from **Settings → Backup**. Uninstalling the app deletes its data.

## Using the app

- **Share** text or a link from any app to LifeHub to save it as a note, task or shopping item.
- **Away from home** (connected): your phone needs an https address for your LifeHub — see
  [Remote access](../remote-access.md).

Want it in Google Play or the App Store? See [publishing-apps.md](../publishing-apps.md).
