# Changelog

All notable changes to LifeHub are recorded here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses
[Semantic Versioning](https://semver.org/).

Home Assistant shows the add-on's own changelog ([`lifehub/CHANGELOG.md`](lifehub/CHANGELOG.md))
on the add-on page. That file is a shorter version of this one.

## [Unreleased]

## [3.2.2] – Unreleased

### Added
- **Memory has its own page in the menu**: add things (one per line) and see everything LifeHub
  knows, with search, edit, share and forget. Old links to AI Inbox → Memory go there.
- **Remember** button in the Ask / Add box saves what you typed straight to memory — no AI needed.
- Telegram: `/remember …`. API: `GET`/`POST /api/v1/memory`, `DELETE /api/v1/memory/{id}`.

- **Heads-ups**: when a calendar event mentions someone or something LifeHub remembers ("Sam over"
  + "Sam is vegan"), you're reminded before it — on the days you choose, from a week before to the
  day itself — with what to keep in mind. A new **Heads-ups** notification kind and background task.
- **Meals for guests**: for dietary needs, LifeHub suggests meals for that day in Meals (you apply
  them), and the meal planner's own suggestions know who's coming when. New "Add a meal" suggestion.
- **Remind me** on any memory fact turns it into a repeating reminder (e.g. bins every Tuesday at
  19:00), guessed from the wording and editable.

### Changed
- Passwords, PINs and account numbers are refused however they're added to memory.

## [3.2.1] – 2026-11-27

### Added
- **Ask LifeHub, Memory and Telegram are modules**: switch each off in **Settings → Modules**
  (now grouped like the menu). Switched off, Ask becomes the plain Add button, Memory isn't given
  to the AI, and the Telegram bot stops.

### Fixed
- **Reopening LifeHub after it's been in the background** no longer leaves it frozen: it reconnects
  straight away, and reloads itself when the server has let the old session go. The message shown
  meanwhile is a small bar near the bottom of the screen with a **Try again** button.
- **Phone app (Android 15)**: LifeHub no longer draws under the status bar and camera cut-out (or
  under the keyboard), so buttons at the top of the screen can be reached. Reopening the app
  reconnects to your LifeHub, or shows the offline screen if it can't be reached.

## [3.2.0] – 2026-11-27

Ask LifeHub anything, chat with it on Telegram, give its AI a memory, and set up your week properly.

### Added
- **Ask LifeHub**: the **Ask** button (and the box in the AI Inbox) takes questions (*"what's due
  this week?"*, *"am I free Thursday afternoon?"*) and requests (*"add milk"*, *"I'm out Thursday
  2–4"*, *"I paid the council tax"*). It answers from your own lists, schedule, kitchen, meals and
  renewals, and lists any change with **Do it** / **Skip** — nothing changes until you confirm.
  Also `POST /api/v1/ask`, `/api/v1/suggestions/{id}/apply` and `/skip`.
- **Memory** (AI Inbox → Memory): facts the AI keeps in mind — diets, routines, people. Add them
  yourself or confirm ones LifeHub suggests when you say *"remember…"*; share them with the
  household or keep them to yourself. Used when reading email, answering and planning meals.
- **Telegram**: connect a bot once in **Settings → Telegram**; each person connects their own chat
  with a 6-digit code (or an admin approves it). Ask and tell LifeHub things from Telegram, with
  buttons to confirm changes; `/today`, `/shopping`, `/stop`. Notifications can go there too.
  Works without opening LifeHub to the internet. Switch it off in Background tasks.
- **Schedule: time off and plans** — holidays, days off, time out, time you're keeping free and
  extra work, even when they're not in a calendar. Add them from **Time off or plans** or by
  asking LifeHub. Nothing is ever planned into time you're out or keeping free.
- **Schedule: week view** with each day's hours, time out, events and planned tasks.
- New AI actions for things you say (never from email): schedule changes, remembering, and
  ticking off tasks and shopping. All can be undone from AI Inbox → History.
- **Phone app 2.1.0** with Ask LifeHub, memory and the new Schedule (Telegram needs a LifeHub
  server, so it isn't in the phone app on its own).
- **Send a test message** in Settings → Telegram; the test notification in Settings →
  Notifications now always goes to your phone.

### Changed
- **Schedule → My usual week** replaces the weekly template table: switch on the days you work
  and set each day's hours, when your day starts and ends, and regular times to keep free (gym,
  school run). The detailed period editor is still there under **Edit every period in detail**.
- **Capture** is now **Ask** (or **Add** when no AI is set up). Quick capture from Home Assistant
  (`/api/v1/capture`) works as before.
- Day-off calendar words live under **My usual week → Planning**.

## [3.1.0] – 2026-11-08

Easier email set-up, a phone app that runs on its own, any AI, tidier notifications, and
everything stored encrypted.

### Added
- **Add mailbox wizard** (Settings → Email): **Sign in with Microsoft** (Outlook.com, Hotmail,
  Microsoft 365) with a one-time code, and guided app-password steps for Gmail, iCloud, Yahoo,
  AOL and Fastmail. Other providers' servers are found automatically. Add as many mailboxes of
  each kind as you like ("Gmail 2"); manual set-up is still there.
- **Email is checked whenever LifeHub is opened** (at most every 2 minutes), as well as on its
  schedule. `POST /api/v1/email/check` lets the phone app ask for a check.
- **Any AI**: Ollama, ChatGPT (OpenAI), Google Gemini, Claude, any OpenAI-compatible server
  (LM Studio, OpenRouter…) and, in the phone app, an AI that runs on the phone itself (WebLLM).
  API keys are stored encrypted, and a warning explains what cloud providers see.
- **Settings → Background tasks**: switch each job (email, reminders, auto-plan, deals, backup,
  Home Assistant lists and sensors, presence) on or off, change how often it runs, see when it
  last ran, and run it now.
- **Encryption at rest**: every data file and the settings file are encrypted with AES-256-GCM.
  The key is created and stored automatically (`keys/`, protected by the operating system where
  possible, or the `LIFEHUB_DATA_KEY` environment variable). Existing data is encrypted on first
  start. A **recovery key** is shown in Settings → Backup; backups don't contain the key.
- **Phone app 2.0.0** (one .NET MAUI app for Android, iPhone and iPad), replacing the separate
  Android and iOS apps:
  - **On its own**: runs LifeHub's own code on the phone for one person, encrypted with a key
    kept in the phone's secure storage. Checks email, sends reminders and runs the AI without a
    server.
  - **Connected**: LifeHub full screen with your API key, an encrypted offline copy, and
    notifications fetched from the server.
  - Background checks every 15 minutes by default (adjustable; Android JobScheduler, iOS
    Background App Refresh when iOS allows) and whenever the app opens.
  - Android notification channels per kind, grouped; iOS notification threads.

### Changed
- **Notifications cleaned up**: emails turned into items now give one short notification per
  check ("From your email: 2 tasks, 1 renewal") instead of one per email, and promotions never
  notify. Each kind (reminders, renewals, food, email, daily summary, deals, shopping, system) can
  go to your phone, stay in LifeHub only, or be switched off, with quiet hours. Titles no longer
  start with emojis.
- Dark mode: the browser's address bar and form controls follow the theme; disabled buttons are
  easier to tell apart.
- The AI client (`OllamaClient`) is now `AiClient`, with provider request formats in
  `AiProviders`. Core services are registered with `AddLifeHubCore()`, shared with the phone app.

### Removed
- `mobile/android` and `mobile/ios` (replaced by `mobile/maui`), and their workflows (replaced by
  `mobile.yml`).

## [3.0.0] – 2026-11-04

LifeHub 3 is a clean-up for publishing: Bible features have moved to their own app, the app is
modular and restyleable, and it installs on Windows, macOS and Linux with a setup assistant.

### Added
- **Desktop apps with installers**, with everything included (their own .NET runtime; WebView2 on
  Windows if missing; WebKitGTK pulled in by the Linux package):
  - Windows 10/11: `LifeHub-<v>-Windows-x64-Setup.exe` (per-user, no admin; Start-menu entries for
    LifeHub and the setup assistant; uninstaller removes the service and offers to delete data)
  - macOS 12+: `.dmg` for Apple Silicon and Intel (unsigned, ad-hoc signed)
  - Linux: `.deb` and `.tar.gz` with `install.sh`, for amd64 and arm64
- **Setup assistant** on first launch (and `--setup` / Settings → This computer later). Choose:
  - **This computer only**: LifeHub runs inside the app on `127.0.0.1`
  - **This computer + my phone**: a background service when you sign in
  - **A server for my household**: a background service that starts with the computer
    (Windows service / LaunchDaemon / systemd with lingering) or when you sign in
  - **Docker**: pulls and runs `ghcr.io/shotgn16/lifehub`, then shows it
  - **Home Assistant** or **LifeHub somewhere else**: the app becomes a window onto it

  It then creates your account, **checks for Ollama and offers to install it** with a model sized
  for your computer's memory (or skip and set it up in Settings → AI), and shows your phone's
  address and API key.
- **Modules**: every section (menu entry, home-screen card, badge, bottom-bar button) is a module
  registered in one place. **Settings → Modules** switches sections off for the household.
  Developers add a section with one registration (see `docs/extending.md`).
- **Make it yours**: per-person theme (auto/light/dark), accent colour, density and home-screen
  layout (**Customise** on Today); admins can add **Custom CSS** (or `custom.css` in the data folder)
  on top of design tokens in `wwwroot/css/theme.css`.
- **Share page** (`/share`): text shared from the phone becomes a note, a task or shopping items.
- General multi-arch Docker image `ghcr.io/shotgn16/lifehub:<version>` / `:latest`.
- Releases now attach every installer, the Android APK, the unsigned iPhone/iPad IPA and a
  portable server zip, with SHA-256 checksums.
- **Manual** (`docs/manual/`): easy step-by-step guides for each platform, phones, a household
  server, customising, the AI and common problems.

### Changed
- Redesigned home screen built from module cards, and a cleaner menu grouped by section.
- Phone apps 1.2.0: Bible features removed; the share menu opens the new share page; the Android
  app no longer asks for Usage access; new **Tasks** home-screen shortcut.
- MailKit 4.16.0 (security fix for STARTTLS handling).

### Removed
- **Bible tracking, verse of the day, reading plans, studies, starter notes and Bible insights** —
  now in their own app. On upgrade, **Settings → Backup → Bible data from LifeHub 2** exports
  everything to a zip and then deletes it. The `/api/v1/bible/*` endpoints and
  `sensor.lifehub_bible_streak*` are gone; older phone apps' Bible offline changes are skipped.
- PdfPig dependency (only used for study notes).

## [2.2.0] – 2026-11-02

### Added
- **The phone apps work offline** (Android & iPhone app 1.1.0).
  - The apps keep a copy of your open tasks, shopping list, recent notes, this week's meals and
    today's Bible reading on the phone, refreshed whenever they're online.
  - When LifeHub can't be reached, the apps switch to an **offline screen**. They also switch if
    the live connection stays down for 12 seconds. On it you can:
    - add tasks, shopping items and notes
    - tick tasks and shopping
    - tick or log your Bible reading
  - Changes are kept on the phone and sent automatically, in order, when LifeHub is back (on
    open, on **Try again**, or in the background). Each change is applied only once, even if it's
    sent again, and changes that no longer make sense are skipped instead of failing.
- API: `GET /api/v1/offline` (the snapshot) and `POST /api/v1/sync` (replay offline changes).

### Changed
- Documentation brought up to date for the ready-made add-on image, people, phone apps and offline
  mode:
  - installation
  - remote access
  - Home Assistant
  - email
  - kitchen
  - scanner
  - Bible
  - notes
  - architecture (including the build workflows)
  - extending
  - security
  - troubleshooting (new phone-app section)
  - contributing

## [2.1.0] – 2026-11-02

### Added
- **LifeHub phone apps** for Android and iPhone (`mobile/`). They are built by GitHub Actions as
  an installable APK and Play bundle, and an unsigned iPhone app plus Xcode project.
  - Both apps:
    - sign in with your own API key, with no password
    - support the camera scanner and uploads
    - show LifeHub notifications
    - add a **Phone app settings** link in LifeHub's menu
  - **Android** also has:
    - the share menu (Bible passages are logged; anything else can become a note)
    - home-screen shortcuts
    - **time in the Bible app measured on the phone** (Usage access, no Home Assistant needed)
  - **iPhone** also has **Siri & Shortcuts** actions: add a note, task or shopping item, log a
    Bible reading, open a page, and **Bible app opened / closed** for timing reading with a
    Shortcuts automation.
- New guides:
  - [Phone apps](docs/mobile-apps.md), including installing without the stores on a Pixel and an
    iPhone
  - [Publishing to Google Play and the App Store](docs/publishing-apps.md)
  - [Running AI models on your phones](docs/local-ai-on-phones.md)
- API: `GET /api/v1/me`, `GET /api/v1/notices`, `POST /api/v1/bible/sessions`, and `POST
  /auth/app` (app sign-in with an API key).

## [2.0.1] – 2026-10-29

### Fixed
- **Updating from Home Assistant took ages or never finished.** Home Assistant used to compile
  LifeHub on the HA machine for every update, which needs a lot of memory and time (about 2 GB of
  free RAM). GitHub now builds ready-made images for amd64 and aarch64
  (`ghcr.io/shotgn16/lifehub-{arch}`), and the add-on simply downloads one. Updates take a minute
  or two, and the HA machine no longer needs build tools or spare memory.

## [2.0.0] – 2026-10-29

**Several people can now use one LifeHub.** Upgrading changes nothing until you add someone. Your
login, API key, phones, calendars, schedule, Bible settings and data become the first person's,
and existing Shortcuts and automations keep working.

### Added
- **People** (Settings → People, admins only).
  - **Each person's own:**
    - tasks and schedule
    - email accounts, AI inbox and promotions
    - Bible reading, verse, reading plan, app time, streak and reminder
    - studies
    - notes
    - phones and devices
    - calendars
    - HA tasks list
    - reminders and digest
    - API key
  - **Household sharing switches** for:
    - food stock, shopping list and purchase history
    - meal planning (recipes, plan, dietary needs, deals)
    - renewals and subscriptions

    Turning sharing off keeps the existing list with the main person; turning it back on merges
    everyone's lists.
  - **Signing in.**
    - In Home Assistant, **"Who's using LifeHub?"**, with an optional PIN per person.
    - On the direct port, each person has their own username and password.
    - Each person has their own **API key**, which acts as them.
  - **Switch person** in the menu.
  - Admins manage household settings; everyone else sees their personal settings tabs.
  - Shared meal planning takes everyone's calendars into account ("Sam: football 18:00").
  - New sensors for each extra person: `sensor.lifehub_tasks_due_<name>` and
    `sensor.lifehub_bible_streak_<name>`.
  - Removing a person deletes their personal data and keeps what they added to shared areas.
- **Notes.**
  - Drop in bits of text from:
    - the Notes page
    - Quick capture
    - the phone share sheet
    - `POST /api/v1/notes`
  - The local AI files each note into a category (reusing yours where they fit), adds tags, and
    gives long notes a short heading. It never changes the text.
  - **Tidy up** files everything again and merges near-duplicate categories.
  - You can:
    - move notes yourself (they then stay put)
    - rename or merge categories
    - use `#tags`
    - pin, archive and search
  - Notes are private by default; share one at a time.
- **Bible: start a study from a verse.**
  - Type a passage and choose:
    - **how deep**: Getting started, Going further or In depth
    - **what it's for**: personal study, **home group**, **preaching**, teaching, youth & children,
      word & theme study, or one-to-one
  - LifeHub reads the passage text and StudyLight's commentaries, then writes **starter notes** in
    purple, shaped for that purpose. For example:
    - a home group gets an icebreaker, leader's notes and observation / interpretation /
      application questions
    - preaching gets a big idea, exegetical notes and an outline with illustration ideas
  - Studies have a **What it's for** type, can be filtered by it, and Go deeper tailors its
    applications to it.
- **Deals & offers** (Meals → Deals, off by default).
  - Set a town, city or postcode. LifeHub finds the nearest branch of each store using
    postcodes.io / OpenStreetMap.
  - It checks offers periodically, every 24 hours by default. Sources:
    - store offers pages (structured product data or the local AI)
    - supermarket emails you already get
    - pasted offers
    - leaflet photos
  - Deals are matched to your shopping list, staples and (planned) recipes. Dietary clashes are
    flagged.
  - The AI suggests meals on days that suit (quick meals on busy days), ingredients worth picking
    up, and **when to shop** in a free slot before offers end.
  - **Add to Thursday** plans the meal and lists its deal items.
  - Default stores: Tesco, Sainsbury's, Asda, Morrisons, Aldi, Lidl, Co-op, M&S, Waitrose and
    Iceland, all editable. Meal suggestions also consider nearby deals.
- Notes and per-person keys in the REST API; new guides: [People & sharing](docs/people.md) and
  [Notes](docs/notes.md).

### Changed
- Reminders (tasks, renewals, food), the morning digest and auto-planning now run per person, to
  their own phones and devices.
- Household-wide notices go to everyone.
- The HA shopping list stays the household's. When food isn't shared, it syncs with the main
  person's list.

## [1.5.0] – 2026-10-25

### Added
- **Go deeper** button on Bible studies.
  - It reads StudyLight's commentaries on your passage, spread through it (for example verses 28
    and 39 of Romans 8:28-39).
  - It explains the passage at a deeper level:
    - how it can be understood, and who holds each view
    - background
    - original-language word studies
    - cross-references and themes
    - **what this could look like** in everyday situations
    - **how to apply it**
    - reflection questions and a prayer
  - Shown in purple and kept separate from your notes; sources and commentators are credited.
  - It works on small machines: each commentary piece is summarised on its own first, then the
    study is written from those summaries.
- **Settings → Bible → Go deeper**:
  - How deep: **Overview** (quick), **Detailed** or **In-depth study** (more commentary pages and
    commentators, where traditions differ, more scenarios)
  - word studies on or off
  - questions and prayer on or off
  - an optional focus for the applications ("family life, my small group")
- Settings → AI: **Context** and **Longest answer** sizes.

### Fixed
- **"The model did not return JSON" on Ask for insights.** Your notes plus long StudyLight pages
  filled the model's working memory, so its answer was cut off part-way. LifeHub now:
  - keeps each request within the context size, leaving room for the answer
  - rescues a cut-off answer by keeping every complete item
  - shows a friendly message instead of the raw model output

## [1.4.0] – 2026-10-24

### Added
- **Share from YouVersion to log exactly what you read.**
  - On Android, LifeHub appears in the share menu once installed to the home screen.
  - On iPhone, a "Log in LifeHub" shortcut does the same.
  - The shared bible.com link or text ("John 3:16-18 NIV") is logged as that passage, and it ticks
    off the verse of the day and any matching readings on today's list.
  - `POST /api/v1/bible/read` accepts shared links and text as well.
- **"What did you read?" page** (`/bible/log`) with one-tap choices:
  - the verse of the day
  - today's plan and catch-up readings
  - "carry on" suggestions, such as the next chapter after the last one you read
  - today's sessions in the app, each with a **Wasn't reading** button
- **Time in the app is logged automatically** and now shows in **Recently read**, for example
  "Time in the Bible app · 12 min · 08:02–08:14 · logged automatically". There's nothing to tap;
  sessions count towards your streak and the verse of the day by themselves.
- Optional **"Which passage?" prompt:** after a reading session, one notification that links to the
  quick-log page. It's off by default; turn it on in Settings → Bible.

### Changed
- **Much more accurate time in the Bible app (Android):**
  - Sessions are rebuilt every minute from Home Assistant's history, instead of sampling the
    current state, so short sessions aren't missed and missing data corrects itself.
  - The phone's **Interactive** (screen on/off) sensor is used automatically when it exists. Time
    stops counting when the phone is locked, even though "Last used app" still says Bible.
    Without it, sessions are capped at 30 minutes.
  - Visits shorter than 45 seconds are ignored (configurable), and brief switches away (up to
    2 minutes, e.g. replying to a message) are merged into one session.
  - Marking a session as "Wasn't reading" removes its time. If that was the only reason the verse
    was ticked, the tick is removed too.
- iPhone Shortcut sessions are capped at 45 minutes, and a missed "closed" event no longer leaves a
  session running.
- The Bible page shows today's sessions in the app.

## [1.3.2] – 2026-10-24

### Changed
- Model advice now points to **`qwen3-vl:4b-instruct`** (and `8b-instruct`). The plain `qwen3-vl:4b`
  tag is the *thinking* version, which reasons before every answer even when asked not to, and is
  slow on small machines. The instruct version is the same size and answers straight away.

## [1.3.1] – 2026-10-24

### Fixed
- **The AI never answered with "thinking" models such as `qwen3-vl` and `qwen3`.** These models
  could spend many minutes reasoning before answering, especially on small machines. LifeHub now
  asks Ollama to skip the thinking step. A new setting in Settings → AI turns it back on for big
  machines. Older Ollama versions that don't understand the option are retried without it.
- Answers are capped in length (3072 tokens by default) so a model can't run on for minutes, and
  the context window is set to 8192 tokens.
- Clearer errors: "Ollama didn't answer within N seconds…" and "Can't reach Ollama at…", instead
  of a silent failure.
- Reasoning text that leaks into an answer (`<think>…</think>`) is removed before it's read.

### Changed
- **Try it** in Settings → AI shows a running timer while it waits, and how long the answer took.

## [1.3.0] – 2026-10-21

### Added
- **Bible section** (Today, Studies, Progress). See [docs/bible.md](docs/bible.md).
  - **Verse of the day**: the same as the YouVersion app, through the YouVersion Platform API with
    a free app key. You can also type it in yourself. Includes an "Open in YouVersion" link.
  - **Time in the YouVersion app** is tracked:
    - Android: the Home Assistant Companion *Last used app* sensor. It's polled every minute, and
      sessions are rebuilt from HA history after a restart.
    - iPhone: a Shortcuts automation calls the new `POST /api/v1/bible/app`.
    - A few minutes in the app (configurable) ticks off the verse of the day.
  - **Keeping on track**:
    - A missed verse becomes a *Catch up* reading the next day, and unfinished readings carry over.
    - Optional reading plan (book, chapters a day; it moves on to the next book).
    - Evening reminder, streak, 16-week heatmap, and chapters/books read.
  - **Reading log**: every passage read (verse, plan, catch-up, study, typed, API), with references
    normalised from any common format (`Rom 8:28-39; 12:1-2`, `1 Cor 13`, `Ps 23`).
  - **Bible studies**: your notes, passages, tags and uploaded proof of study (photos, scans,
    PDFs, Word or text files).
    - The local AI reads handwriting (vision model) and PDF text (PdfPig).
    - It breaks the study down into summary, key points, passages, themes, people, questions and
      applications.
    - It links studies by shared passages, themes and people, and optionally by meaning (embedding
      model).
  - **Ask for insights** (only on request): fetches StudyLight.org (and Biblia Graphia where
    readable) for your passages. Your local AI then suggests connections, ways to go deeper,
    background, cross-references, questions and links to your other studies, each with its source.
    Insights are shown in purple, can be pinned or dismissed, and never change your notes.
  - `sensor.lifehub_bible_streak` in Home Assistant, and `GET /api/v1/bible/today` and
    `POST /api/v1/bible/read`.
- **Meal planner** (up to two weeks). See [docs/kitchen-and-meals.md](docs/kitchen-and-meals.md).
  - Each day shows what your calendar leaves you (work, events, free evening) and an energy level
    you can override.
  - Recipes with ingredients, method, effort and time. You can draft a recipe from a meal name with
    the AI.
  - **What to buy**: ingredients scaled to the servings, checked against your kitchen (ignoring
    food that will be past it) and the shopping list. Added to the list only when you choose.
  - **Dietary needs and preferences**: requirements such as gluten free, dairy free, vegetarian,
    vegan, nut free, no pork or low carb. Recipes that may clash are flagged.
  - **AI suggestions**: move meals to better days, use up food, adapt recipes for your needs, or
    improve them. Each needs **Apply**, and adapted recipes are saved as new versions (the
    original is kept).
  - Mark a meal as cooked to take its ingredients out of the kitchen.
  - `GET /api/v1/meals`, and today's meals in the morning digest and on the Today page.
- **Kitchen: Add from photo**.
  - A vision model lists the groceries in your photos and reads clearly printed use-by and
    best-before dates.
  - For items without a date: take a close-up of their labels, type the date, or use a typical
    estimate.
  - Understands `12 NOV`, `24/10/26`, `03/2027` (end of month) and more.
- **Use-by vs best-before**.
  - Best-before food (eggs, cereal, pasta, tins, bread, crisps, jars, fruit and veg…) fades
    gradually on a scale (*best before soon → past best, usually fine → check before using*)
    instead of a hard "expired".
  - Opened items go stale sooner.
  - Built-in profiles for common foods; staples can override the date type, opened life and grace
    days.
  - Estimated dates are marked `~`.
- **LifeHub's own notifications** (Web Push, RFC 8291/8292).
  - Settings → Notifications lets you choose Home Assistant, LifeHub, or both.
  - Turn it on per device; it works on Android, desktop and iPhone home-screen apps.
  - Devices can belong to a person, for "while you're out" messages.
  - Expired devices are removed automatically.
  - The server's signing key is generated locally and encrypted.
- App icons for installing LifeHub to the home screen (PNG, maskable, Apple touch icon).
- Settings → AI: **vision model** and **embedding model**.

### Changed
- **The shopping list only changes when you add something**: typed, scanned, the Home Assistant
  list you chose, the meal planner, or the API. Emails can no longer add shopping items, even when
  the model suggests them.
  - Low or predicted staples are shown as **suggestions** you tap to add.
  - On upgrade, items LifeHub had added by itself (low stock, predictions, emails) are removed
    once, with a notice.
- Food reminders only nag about use-by food, and about best-before food that now needs checking.
- The morning digest includes today's meals and the verse of the day.
- Backups include uploaded study notes.

### Fixed
- The Kitchen "add item" form now fits on phones, and item rows wrap instead of squashing.
- The Gmail help text now explains that the only change LifeHub makes to your mailbox is moving
  an email to the Bin when you delete its task.

## [1.2.1] – 2026-10-20

### Changed
- Phone notifications are rewritten for the lock screen. Each has a short heading, one item per
  line and "+N more" instead of long comma-separated sentences.
- The morning digest is now split into sections: 📅 today, ✅ tasks due, 🔁 renewals, 🥛 use up
  and 🛒 to buy.
- The "while you're out" shopping list goes only to the phone of the person who left, and uses
  their first name.
- Notifications of the same kind replace each other instead of piling up, and are grouped in the
  notification shade.
- Tapping a notification opens LifeHub. It uses the public address set in Settings → General, or
  the Home Assistant ingress panel when no address is set.

## [1.2.0] – 2026-10-19

### Added
- **Email classification.** Every email gets a category: action needed, bill, receipt, delivery,
  booking, account/security, promotion, newsletter, social or personal.
- **Promotions list.** Deals, sales and product launches are saved with the merchant, price and
  offer end date instead of becoming shopping items. They clear themselves after they expire, and
  you can save one or add it to the shopping list.
- **Stacking.** Repeat emails or captures about the same issue update one task with a ×N count
  and a dated log. **Merge duplicates** on the Tasks page stacks existing duplicates.
- **Email ↔ task link.**
  - Deleting a task moves its email(s) to the mailbox's Bin or Deleted Items.
  - Deleting the email, in LifeHub or in your mail app, removes its open tasks.
  - Archived mail is left alone, and nothing is ever permanently deleted.
- **AI clean-up.** AI Inbox → History can remove everything the AI added from email, by type, in
  one tap.
- **Home Assistant, rebuilt around names:**
  - Friendly names, rooms (areas) and devices everywhere instead of entity IDs.
  - The Home page is grouped by room, with scenes and scripts as buttons, climate,
    temperature/humidity readings and open-door alerts. **Customise** lets you hide tiles.
- **Automatic HA setup.** People, calendars (including one that can take new events), phones,
  energy sensors and to-do lists are detected on first start.
- **Two-way list sync.** The shopping list syncs with HA's Shopping List, so Assist voice and the
  HA app work with it. Tasks can optionally sync with an HA to-do list.
- Reminders can go to several phones.

### Changed
- Adverts and newsletters never create shopping items, tasks, renewals or kitchen stock.
- AI output is checked more strictly:
  - Boilerplate or nonsense titles are dropped.
  - Renewals need a real price and a valid currency.
  - Kitchen stock is only taken from grocery deliveries and receipts, and now waits for approval.
- Microsoft 365 now asks for `Mail.ReadWrite` so it can move emails to Deleted Items. Sign in
  again once after upgrading.
- Microsoft Graph uses immutable message IDs, and EWS links by Internet Message-ID, so links
  survive emails being moved between folders.

### Fixed
- Junk items from marketing emails, such as advert lines on the shopping list, devotional text in
  the pantry and renewals with garbled currency strings.

## [1.1.2] – 2026-10-18

### Fixed
- The barcode scanner could fail to load behind Cloudflare Tunnel or other HTTPS proxies
  (mixed-content script import).
- Forwarded HTTPS headers are now honoured through several proxies in a row
  (Cloudflare → cloudflared → LifeHub).

## [1.1.1] – 2026-10-18

### Fixed
- Opening LifeHub from the Home Assistant sidebar showed "404: Not Found". Ingress detection now
  runs before forwarded-header processing.

## [1.1.0] – 2026-10-18

### Added
- **Barcode scanner**, using the phone camera, a photo, a typed number or a USB/Bluetooth scanner.
  - Modes: stock in, use up and add to the shopping list, each with undo.
  - Open Food Facts lookup and learned products, and products can be linked to staples.
  - New `POST /api/v1/scan` endpoint.
- **Automatic planning.** Every evening LifeHub plans tomorrow, and each morning it tops up today
  with anything new. You get a notification, and can optionally have the plan written to your
  calendar.
- **Auto-apply with undo.** Tasks, shopping items and kitchen stock suggested by the AI are applied
  automatically by default. Each one can be undone from AI Inbox → History, and you get an in-app
  notice for anything done from email.

## [1.0.0] – 2026-10-18

### Added
- First release:
  - Renewals and subscriptions with price history.
  - Tasks with categories and recurrence.
  - Kitchen stock, staples and predictions.
  - Shopping list.
  - Weekly schedule and day planner.
  - Home Assistant integration (presence, lights, energy, calendars, notifications, published
    sensors).
  - AI Inbox (Ollama) for IMAP, Microsoft 365 and Exchange (EWS).
  - Quick capture.
  - REST API.
  - Nightly backups.
- Runs as a Home Assistant add-on (ingress), with Docker, or on Plesk (Docker, .NET Toolkit or IIS).

[Unreleased]: https://github.com/Shotgn16/LifeHub/compare/v3.2.1...HEAD
[3.2.1]: https://github.com/Shotgn16/LifeHub/compare/v3.2.0...v3.2.1
[3.2.0]: https://github.com/Shotgn16/LifeHub/compare/v3.1.0...v3.2.0
[3.1.0]: https://github.com/Shotgn16/LifeHub/compare/v3.0.0...v3.1.0
[3.0.0]: https://github.com/Shotgn16/LifeHub/releases/tag/v3.0.0
[1.5.0]: https://github.com/Shotgn16/LifeHub/compare/v1.4.0...v1.5.0
[1.4.0]: https://github.com/Shotgn16/LifeHub/compare/v1.3.2...v1.4.0
[1.3.2]: https://github.com/Shotgn16/LifeHub/compare/v1.3.1...v1.3.2
[1.3.1]: https://github.com/Shotgn16/LifeHub/compare/v1.3.0...v1.3.1
[1.3.0]: https://github.com/Shotgn16/LifeHub/releases/tag/v1.3.0
[1.2.1]: https://github.com/Shotgn16/LifeHub/releases/tag/v1.2.1

Versions before 1.2.1 were developed privately and are recorded here for history. The first public
release on GitHub is 1.2.1.
