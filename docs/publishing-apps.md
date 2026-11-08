# Publishing the apps to Google Play and the App Store

This is optional: you can use the apps without publishing them (see
[mobile-apps.md](mobile-apps.md)). Publishing makes installing and updating easy for family or
anyone else running LifeHub.

## Before either store

- **Change the app ID to one you own.** The app uses `io.github.shotgn16.lifehub`. Keep it if
  this is your GitHub account; otherwise use a reverse domain you control, for example
  `uk.yourname.lifehub`. It **can't be changed after publishing**. Set `ApplicationId` in
  `mobile/maui/LifeHub.Mobile/LifeHub.Mobile.csproj`.
- **Privacy policy URL.** Both stores require one. A short page is enough, for example a
  `PRIVACY.md` in this repository. Say that:
  - on its own, everything stays on the phone (encrypted); connected, the app talks only to the LifeHub server you enter
  - email is read from the mailboxes you add, and only by the AI you choose (on the phone, your own Ollama, or — if
    you pick them — OpenAI, Google or Anthropic with your own API key)
  - the developer collects no data
- **Store listing assets**:
  - the icon: `mobile/store-icon-512.png` for Play; the iOS icons are generated from
    `mobile/maui/LifeHub.Mobile/Resources/AppIcon`
  - screenshots from your phones
  - a short and a long description
- **Reviewers can use it without a server.** "Use LifeHub on this phone" works straight away, so reviewers
  can try everything except email and server mode. For those, run a **demo LifeHub** reachable over https (for
  example a second Docker container behind your Cloudflare Tunnel, with made-up data) and give them its address and
  an API key.
- **Versions.** Bump `ApplicationDisplayVersion` in the `.csproj` for each release. The build number is
  set automatically from the GitHub run number.

## Google Play (Android)

**Cost:** a one-off US$25 registration.

1. **Create a developer account** at [play.google.com/console](https://play.google.com/console).
   Choose a **personal** account and verify your identity: ID, address, and a phone number on an
   Android device.
2. **Create a signing key** and add the four GitHub secrets (see
   [mobile-apps.md](mobile-apps.md#updates-keep-the-same-signature-only-with-your-own-key)).
   Then run the **Phone apps** workflow and download `app-release.aab`. Keep the key safe: with
   **Play App Signing** (recommended, on by default), it is your *upload key*, and Google can
   reset it if you lose it.
3. **Create app** in Play Console:
   - name: LifeHub
   - default language
   - App (not a game)
   - Free
   - accept the declarations
4. **Set up your app** (the dashboard checklist):
   - **Privacy policy**: your URL.
   - **App access**: "All functionality is available without special access" (on this phone needs no
     account). Mention the demo server address and API key for server mode.
   - **Ads**: no.
   - **Content rating**: complete the questionnaire (a utility app, no user-generated content
     shared with others).
   - **Target audience**: 18+, which avoids the Families policy.
   - **Data safety**:
     - the app collects no data for the developer
     - data stays on the phone (encrypted) or goes only to the user's own server and the services the user chooses
       (their mailboxes; OpenAI/Google/Anthropic only if they pick them, with their own key)
     - data in transit is encrypted when the server uses https
   - **Sensitive permissions**: none. The app asks only for the camera (barcode scanning) and
     notifications.
5. **Closed testing** (required for personal accounts created after 13 November 2023): you must
   run a closed test with **at least 12 testers opted in for 14 days in a row** before you can
   apply for production.
   1. Go to **Testing → Closed testing → Create track**.
   2. Upload the `.aab`.
   3. Add the testers' Google accounts (family and friends count) and share the opt-in link.
      Testers must stay opted in for the full 14 days.
6. **Apply for production access** after the 14 days (Dashboard → Apply for production). Answer
   the questions about your testing.
7. **Production release**: go to **Production → Create new release**, upload the `.aab`, add
   release notes and **Send for review**. Reviews usually take from a few hours to a few days.

**Private alternative.** If it's only for your household, stay on **Internal testing** (up to 100
testers, no 14-day rule, no full review). Testers install it from the Play Store with
automatic updates.

## Apple App Store (iPhone)

**Cost:** Apple Developer Program, £79 / US$99 a year. You need a **Mac** with Xcode, or a cloud
Mac.

1. **Enrol** at [developer.apple.com/programs](https://developer.apple.com/programs/) with your
   Apple ID. It's an individual membership, and approval takes up to about two days.
2. **Register the bundle ID**: go to Certificates, Identifiers & Profiles → Identifiers → + → App
   IDs, and enter your bundle ID.
3. **Create the app** in [App Store Connect](https://appstoreconnect.apple.com): **Apps → + → New
   App**. Choose iOS, the name LifeHub, your bundle ID and an SKU (any text, for example
   `lifehub1`).
4. **Build and upload** from the Mac (Xcode, .NET 10 SDK, `dotnet workload install maui-ios`):
   1. In Xcode → Settings → Accounts, add your Apple ID and create an **Apple Distribution** certificate; in the
      developer portal create an **App Store** provisioning profile for the bundle ID and download it (double-click).
   2. `bash mobile/maui/copy-web.sh`, then:

      ```bash
      dotnet publish mobile/maui/LifeHub.Mobile/LifeHub.Mobile.csproj -f net10.0-ios -p:TargetFrameworks=net10.0-ios -c Release \
        -p:RuntimeIdentifier=ios-arm64 -p:ArchiveOnBuild=true \
        -p:CodesignKey="Apple Distribution: Your Name (TEAMID)" -p:CodesignProvision="LifeHub App Store"
      ```
   3. Upload the `.ipa` with Apple's **Transporter** app (or `xcrun altool --upload-app`).
5. **TestFlight** (optional, recommended). Once the build has processed, add yourself and your
   family under **Internal Testing** (up to 100 people, no review). They install it with the
   **TestFlight** app. Builds expire after 90 days; upload a new one to continue.
6. **App information**, in App Store Connect:
   - the privacy policy URL
   - category: Productivity
   - age rating questionnaire
   - **App Privacy**: "Data not collected" (the app only talks to the user's own server)
   - screenshots for the 6.9" iPhone size (the iPhone Air's are fine) and, if offered on iPad, the
     13" iPad size
7. **App Review Information**:
   - **Sign-in required**: give the demo server address and API key. The reviewer enters these
     on the first screen.
   - **Notes**: explain that LifeHub is a self-hosted household organiser and the app is its
     client, but that it also works fully on its own. List what the app does natively:
     - runs LifeHub on the phone with encrypted storage (key in the Keychain)
     - an on-device AI model
     - background refresh and local notifications
     - camera scanning and the share sheet

     Apple's guideline 4.2 (minimum functionality) rejects apps that are "just a website"; the standalone mode
     answers that.
8. **Submit for review.** It usually takes 1–3 days. If it's rejected under 4.2, reply in
   Resolution Center with the list of native features, or add more (for example widgets) and
   resubmit.

**Encryption export question.** The app only uses standard https, so `ITSAppUsesNonExemptEncryption`
is already set to **NO** in Info.plist, and you won't be asked each upload.

## After publishing

- To release an update:
  1. Bump the version.
  2. Run the workflows, or `dotnet publish` as above.
  3. Upload to the same app, with release notes.
- Keep the demo server running; reviewers check it on every update.
- Play and Apple both email policy updates, such as new target-SDK requirements. Google requires
  a recent `targetSdk` each year; a newer .NET MAUI release brings it (update the SDK in the workflow).
