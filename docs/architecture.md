# Architecture

LifeHub is a single ASP.NET Core 8 process. The UI is **Blazor Server** (interactive server
rendering), so the pages are Razor components in C#. The browser holds a SignalR connection and
there's no JavaScript framework. The only JavaScript is the barcode scanner, the vendored ZXing
library, web push and a two-line bridge to the desktop window.

The **desktop apps** are the same program built with `-p:LifeHubDesktop=true`
(`DESKTOP` defined): `Desktop/DesktopHost.cs` adds a native window (Photino: WebView2 on Windows,
WKWebView on macOS, WebKitGTK on Linux) and decides where LifeHub runs — inside the app on
`127.0.0.1`, as a background service (`--server`; a scheduled task or Windows service, a
LaunchAgent/LaunchDaemon, or a systemd user service — `ServiceManager.cs`), in Docker
(`DockerSetup.cs`) or on another machine. The setup assistant (`/welcome`, `WelcomePage.razor`)
writes `desktop.json` and asks the window to look again; `OllamaSetup.cs` installs Ollama and pulls
a model.

The **phone app** (`mobile/maui`, .NET MAUI Blazor Hybrid, Android + iPhone/iPad) compiles the same code
(`LifeHubShared.props` links `lifehub/src/LifeHub`, minus the web-server parts; `#if MOBILE` and
`AppHost.IsPhoneApp` mark the few differences) and runs it two ways:

- **On its own**: `MauiProgram` calls the same `AddLifeHubCore()` as the server, with the data folder in the app's
  storage, the data key in the Keystore/Keychain (`SecureStorageKeyStore`), one person signed in
  (`DeviceAuthStateProvider`, `UserScope.Default`), notifications shown by the phone (`IDeviceNotifier`), the
  on-device AI (`IOnDeviceAi` → WebLLM in the web view) and background checks (`PhoneJobs`: Android JobScheduler,
  iOS BGAppRefresh). The usual background services run while the app is open.
- **Connected**: a WebView signed in with the API key (`POST /auth/app`). The offline screen,
  `mobile/shared/offline.html`, shows a snapshot from `GET /api/v1/offline` and queues changes, replayed through
  `POST /api/v1/sync` (idempotent via `SyncReceipt`s); `ServerClient` keeps both encrypted on the phone and shows
  `GET /api/v1/notices` entries flagged `phone`.

```
                ┌───────────────────────── LifeHub (one container) ─────────────────────────┐
 Browser/Phone ─┤ Blazor Server UI (Razor components)                                        │
 HA sidebar ────┤   Pages ─► Services (Tasks, Renewals, Kitchen, Schedule, Planner, AI…)     │
 (ingress)      │                 │                         │                                │
 HA / Shortcuts ┤ REST /api/v1 ───┘                 IRepository<T>  ──► /data/db/*.json      │
 (API key)      │                                   SettingsService ──► /data/settings.json  │
                │ Background: EmailSync · Automation · HaListSync (1 min)                   │
                └──────┬───────────────────┬──────────────────────┬──────────────────────────┘
                       │                   │                      │
            Home Assistant REST     Ollama /api/chat        IMAP · Microsoft Graph · EWS
     (states, services, templates,  (JSON schema, images,
      calendars, history, to-do,     /api/embed)            Web Push services (VAPID)
      notify; publishes sensor.lifehub_*)
```

## Source layout (`lifehub/src/LifeHub`)

| Folder | Contents |
|---|---|
| `Domain/` | Plain entity classes: `TaskItem`, `Renewal`, `PantryItem`, `StapleItem`, `ShoppingItem`, `ProductInfo`, `EmailRecord`, `Promotion`, `AiSuggestion`, `Note`, `Recipe`, `MealPlanEntry`, `PushDevice`, `SchedulePlan` (time off and plans), `MemoryFact`, … |
| `Data/` | `IRepository<T>` and `JsonRepository<T>`, which keeps an in-memory copy, writes the file atomically and raises a change event. `Ownership.cs` holds `UserScope`, `[Owned]` and the visibility rules for several people |
| `Services/` | Business logic and background jobs: `TaskService`, `RenewalService`, `KitchenService`, `MealService`, `PeopleService`, `NotesService`, `DealsService` (nearby stores, offers, ideas), `KitchenPhotoService`, `Freshness` (use-by vs best-before), `WebPushService` (RFC 8291/8292), `ScheduleService` (usual week + time off and plans + calendar → periods), `PlannerService`, `MemoryService`, `SyncService` (offline changes), `BibleArchiveService` (exports and removes LifeHub 2 Bible data) |
| `Modules/` | `LifeHubModule` and `ModuleRegistry`: every section (menu entry, home-screen card, badge, bottom bar) is registered in `AddLifeHubModules()` |
| `Desktop/` | Desktop app: window, setup decisions, background services, Ollama and Docker setup |
| `Data/DataCipher.cs` | AES-256-GCM encryption of every data file (key from `IDataKeyStore`) |
| `Infrastructure/LifeHubServices.cs` | `AddLifeHubCore()`: every service, shared by the server and the phone app |
| `Integrations/HomeAssistant/` | REST client, entity catalogue (names, areas, devices), automatic setup, two-way to-do sync |
| `Integrations/Email/` | `IEmailProvider` implementations: IMAP (MailKit), Microsoft Graph, EWS (SOAP) |
| `Integrations/Ollama/` | `AiClient` (one front door for every AI call) and `AiProviders` (Ollama `/api/chat` with JSON schema; OpenAI, Gemini, Claude, OpenAI-compatible; on-device) |
| `Ai/` | `AiService` (prompt, schema, checks, apply, undo), `AskService` (Ask LifeHub: context + answer + suggestions) and `Handlers/`, with one class per action type (`IUserOnlyAction` marks ones emails can't trigger) |
| `Integrations/Telegram/` | `TelegramService`: the household bot (long polling, pairing codes, confirm buttons) and the Telegram notification channel (`IChatNotifier`) |
| `Api/` | Login/setup endpoints and the REST API |
| `mobile/maui`, `mobile/shared` | The phone app and its offline screen. Built by GitHub Actions; see [mobile-apps.md](mobile-apps.md) |
| `Infrastructure/` | Settings (with encrypted `[Secret]` fields), auth, HA ingress middleware, clock and time zone, data paths |
| `Components/` | Pages, layout, settings tabs, home-screen cards (`Widgets/`) and shared components |
| `wwwroot/` | `css/theme.css` (design tokens, light/dark), `app.css`, the scanner script, ZXing, `sw.js` (service worker for push), `js/push.js`, the manifest and icons |

## Key design points

- **Storage.** Each entity type is one JSON file (`data/db/TaskItem.json`, …). Reads come from
  memory. Writes are serialised and go to a temp file that is then renamed. That's plenty for a
  household, and storage can be swapped (see [extending.md](extending.md)).
- **Several people (2.0).** Every entity has an `OwnerId`, and its class is marked
  `[Owned(OwnScope.Personal | Kitchen | Meals | Renewals | Global)]`. `UserScope`, an `AsyncLocal`,
  holds the person LifeHub is working for:
  - It is set per web request by `UserScopeMiddleware`.
  - It is set for every Blazor event and render by `UserCircuitHandler`, a circuit inbound-activity
    handler.
  - Background jobs run as *system* and switch to each person for per-person work.

  `JsonRepository` filters reads by `UserScope.CanSee` and stamps new records with their owner.
  Shared areas stay household-owned; records with no owner (from before 2.0) belong to the main
  person. Change events are raised with no scope, and `LiveComponent` reloads as its own person,
  so one person's background work can never render another person's data.

  `SettingsService.Current` overlays the person's own settings: phones, calendars, schedule,
  appearance and mailboxes. `UpdateAsync` splits them back into their `UserProfile`.
- **Live UI.** Pages derive from `LiveComponent` and `Watch()` repositories, so changes made by
  background jobs, the API or another tab show up straight away.
- **Settings.** All settings live in one `settings.json`. Properties marked `[Secret]` are
  encrypted with ASP.NET Core Data Protection, with keys in `data/keys`. Small migrations run on
  load.
- **Auth.** There are three schemes:
  - **Cookie**: each person's login, or their pick on the HA "Who's using LifeHub?" screen. A pick
    on that screen only counts inside ingress.
  - **Ingress**: requests from the HA Supervisor proxy (`172.30.32.2`) that carry the ingress
    headers are trusted.
  - **API key**: for `/api/v1`. There is one key per person.

  The ingress middleware runs *before* forwarded-header processing so that the real remote IP is
  checked.
- **Background jobs.**
  - `AutomationService` ticks every minute. It handles reminders, the digest, expiry, auto-renewal
    roll-forward, nightly planning, presence and sensor publishing.
  - `EmailSyncService` polls the mailboxes.
  - `HaListSyncService` syncs the lists with HA's to-do lists.
- **AI never edits your content.** What the AI adds (note categories, email summaries, meal ideas)
  lives in its own fields and is shown in purple. Meal suggestions and
  adapted recipes are stored as pending `MealSuggestion`s, or as new recipe versions, until you
  apply them.
- **AI pipeline.**
  1. The email text is trimmed to `MaxEmailChars`.
  2. The chosen AI is called with a JSON schema built from the registered `IAiActionHandler`s.
  3. LifeHub applies its own validation, junk filter and marketing guard.
  4. Each suggestion is auto-applied (storing undo data) or queued in the AI Inbox.
- **Time.** All scheduling uses the time zone set in Settings → General (`Clock`), not the
  container's.

## Tech

- .NET 8 / ASP.NET Core / Blazor Server
- [MailKit](https://github.com/jstedfast/MailKit) for IMAP
- [Photino.NET](https://github.com/tryphotino/photino.NET) for the desktop window; Inno Setup,
  `hdiutil` and `dpkg-deb` for the installers
- [ZXing-js](https://github.com/zxing-js/library) and the native `BarcodeDetector` for scanning
- [Open Food Facts](https://world.openfoodfacts.org) for product lookup
- [Ollama](https://ollama.com) for local LLMs
- Docker images: `mcr.microsoft.com/dotnet/sdk:8.0` → `mcr.microsoft.com/dotnet/aspnet:8.0`
- Phone app: .NET MAUI 10 (Blazor Hybrid), [WebLLM](https://github.com/mlc-ai/web-llm) for the on-device AI
- Cloud AI (optional): OpenAI, Google Gemini and Anthropic Claude APIs (`AiProviders.cs`)

## Builds (GitHub Actions)

| Workflow | What it does |
|---|---|
| **Build** | Builds the .NET solution, builds and smoke-tests the Docker image, and checks the add-on and app versions match |
| **Add-on image** | Publishes the ready-made Home Assistant images `ghcr.io/shotgn16/lifehub-{amd64,aarch64}` that the add-on downloads, and the multi-arch Docker image `ghcr.io/shotgn16/lifehub` |
| **Desktop apps** | Self-contained builds for Windows x64, macOS (Apple Silicon, Intel) and Linux (amd64, arm64), each started once headless as a check, then packaged: Inno Setup `.exe`, `.dmg`, `.deb` and `.tar.gz` |
| **Phone apps** | Builds the Android APK/AAB and the unsigned iPhone/iPad `.ipa` from `mobile/maui`, then opens the APK on an Android emulator and walks through the first start |
| **Release** | On a `v*` tag, runs the desktop and phone builds and attaches every installer, the APK, the IPA and a portable server zip to the GitHub release |
