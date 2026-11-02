# Publishing the apps to Google Play and the App Store

This is optional: you can use the apps without publishing them (see
[mobile-apps.md](mobile-apps.md)). Publishing makes installing and updating easy for family or
anyone else running LifeHub.

## Before either store

- **Change the app ID to one you own.** Both apps use `io.github.shotgn16.lifehub`. Keep it if
  this is your GitHub account; otherwise use a reverse domain you control, for example
  `uk.yourname.lifehub`. It **can't be changed after publishing**. Set it in:
  - `mobile/android/app/build.gradle.kts` (`applicationId`)
  - `mobile/ios/project.yml` (`PRODUCT_BUNDLE_IDENTIFIER`)
- **Privacy policy URL.** Both stores require one. A short page is enough, for example a
  `PRIVACY.md` in this repository. Say that:
  - the app connects only to the LifeHub server you enter
  - it stores your server address and API key on the phone
  - the developer collects no data
- **Store listing assets**:
  - the icon: `mobile/store-icon-512.png` for Play; the 1024 px icon is already in the iOS project
  - screenshots from your phones
  - a short and a long description
- **Reviewers need a server.** LifeHub is self-hosted, so both stores' reviewers need something to
  sign in to. Run a **demo LifeHub** reachable over https, for example a second Docker container
  behind your Cloudflare Tunnel, with made-up data. Give the reviewers its address and an API
  key, and say clearly in the review notes that the app is a client for a self-hosted server.
- **Versions.** Bump `versionName` / `MARKETING_VERSION` for each release. The build number is
  set automatically from the GitHub run number.

## Google Play (Pixel)

**Cost:** a one-off US$25 registration.

1. **Create a developer account** at [play.google.com/console](https://play.google.com/console).
   Choose a **personal** account and verify your identity: ID, address, and a phone number on an
   Android device.
2. **Create a signing key** and add the four GitHub secrets (see
   [mobile-apps.md](mobile-apps.md#updates-keep-the-same-signature-only-with-your-own-key)).
   Then run the **Android app** workflow and download `app-release.aab`. Keep the key safe: with
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
   - **App access**: "All or some functionality is restricted". Add the demo server address and
     API key, and explain how to sign in.
   - **Ads**: no.
   - **Content rating**: complete the questionnaire (a utility app, no user-generated content
     shared with others).
   - **Target audience**: 18+, which avoids the Families policy.
   - **Data safety**:
     - the app collects no data for the developer
     - data is sent only to the user's own server
     - data in transit is encrypted when the server uses https
   - **Sensitive permissions**: the optional **Usage access** (`PACKAGE_USAGE_STATS`) feature may
     be queried. Explain that it's off by default, user-enabled, used only to measure time in the
     Bible app, and sent only to the user's own server. If Play rejects it, remove that
     permission from the manifest for the Play build; the Home Assistant sensor still works.
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
4. **Build and upload** from the Mac:
   1. `brew install xcodegen`, then run `xcodegen` in `mobile/ios`, and open `LifeHub.xcodeproj`.
   2. Under **Signing & Capabilities**, choose your paid **Team**.
   3. Set the destination to **Any iOS Device**, then go to **Product → Archive**.
   4. In the Organizer, choose **Distribute App → App Store Connect → Upload**.
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
     client. List what the app adds beyond the website:
     - Siri and Shortcuts actions
     - background notifications
     - Bible-app timing through Shortcuts
     - camera scanning
     - Keychain storage of the key

     Apple's guideline 4.2 (minimum functionality) rejects apps that are "just a website", so the
     native features matter.
8. **Submit for review.** It usually takes 1–3 days. If it's rejected under 4.2, reply in
   Resolution Center with the list of native features, or add more (for example widgets) and
   resubmit.

**Encryption export question.** The app only uses standard https, so `ITSAppUsesNonExemptEncryption`
is already set to **NO** in Info.plist, and you won't be asked each upload.

## After publishing

- To release an update:
  1. Bump the version.
  2. Run the workflows, or archive in Xcode.
  3. Upload to the same app, with release notes.
- Keep the demo server running; reviewers check it on every update.
- Play and Apple both email policy updates, such as new target-SDK requirements. Google requires
  a recent `targetSdk` each year; change `targetSdk`/`compileSdk` in `app/build.gradle.kts`.
