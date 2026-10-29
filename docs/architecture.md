# Architecture

LifeHub is a single ASP.NET Core 8 process. The UI is **Blazor Server** (interactive server
rendering), so the pages are Razor components in C#. The browser holds a SignalR connection and
there's no JavaScript framework. The only JavaScript is the barcode scanner and the vendored ZXing
library.

```
                ┌───────────────────────── LifeHub (one container) ─────────────────────────┐
 Browser/Phone ─┤ Blazor Server UI (Razor components)                                        │
 HA sidebar ────┤   Pages ─► Services (Tasks, Renewals, Kitchen, Schedule, Planner, AI…)     │
 (ingress)      │                 │                         │                                │
 HA / Shortcuts ┤ REST /api/v1 ───┘                 IRepository<T>  ──► /data/db/*.json      │
 (API key)      │                                   SettingsService ──► /data/settings.json  │
                │ Background: EmailSync · Automation · HaListSync · BibleTracking (1 min)    │
                └──────┬───────────────────┬──────────────────────┬──────────────────────────┘
                       │                   │                      │
            Home Assistant REST     Ollama /api/chat        IMAP · Microsoft Graph · EWS
     (states, services, templates,  (JSON schema, images,
      calendars, history, to-do,     /api/embed)            Web Push services (VAPID)
      notify; publishes sensor.lifehub_*)                   YouVersion API · StudyLight (on request)
```

## Source layout (`lifehub/src/LifeHub`)

| Folder | Contents |
|---|---|
| `Domain/` | Plain entity classes: `TaskItem`, `Renewal`, `PantryItem`, `StapleItem`, `ShoppingItem`, `ProductInfo`, `EmailRecord`, `Promotion`, `AiSuggestion`, `BibleStudy`, `DailyVerse`, `Recipe`, `MealPlanEntry`, `PushDevice`, … plus `BibleRef`, the Bible reference parser (66 books, abbreviations, USFM) |
| `Data/` | `IRepository<T>` and `JsonRepository<T>`, which keeps an in-memory copy, writes the file atomically and raises a change event. `Ownership.cs` holds `UserScope`, `[Owned]` and the visibility rules for several people |
| `Services/` | Business logic and background jobs. New in 2.0: `PeopleService`, `NotesService`, `DealsService` (nearby stores, offers, ideas), starter notes in `BibleStudyService`. New in 1.3: `BibleService`, `BibleTrackingService`, `BibleStudyService` (uploads, OCR, PdfPig, insights), `MealService`, `KitchenPhotoService`, `Freshness` (use-by vs best-before), `WebPushService` (RFC 8291/8292) |
| `Integrations/HomeAssistant/` | REST client, entity catalogue (names, areas, devices), automatic setup, two-way to-do sync |
| `Integrations/Email/` | `IEmailProvider` implementations: IMAP (MailKit), Microsoft Graph, EWS (SOAP) |
| `Integrations/Ollama/` | `/api/chat` client with JSON-schema structured output |
| `Ai/` | `AiService` (prompt, schema, checks, apply, undo) and `Handlers/`, with one class per action type |
| `Api/` | Login/setup endpoints and the REST API |
| `Infrastructure/` | Settings (with encrypted `[Secret]` fields), auth, HA ingress middleware, clock and time zone, data paths |
| `Components/` | Pages, layout, settings tabs and shared components |
| `wwwroot/` | `app.css`, the scanner script, ZXing, `sw.js` (service worker for push), `js/push.js`, the manifest and icons |

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

  `SettingsService.Current` overlays the person's own settings: phones, calendars, schedule, Bible
  settings and mailboxes. `UpdateAsync` splits them back into their `UserProfile`.
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
- **AI never edits your content.** Bible study insights, breakdowns and OCR text live in their own
  fields (`ExtractedText`, `Breakdown`, `Insights`) and are shown in purple. Meal suggestions and
  adapted recipes are stored as pending `MealSuggestion`s, or as new recipe versions, until you
  apply them.
- **AI pipeline.**
  1. The email text is trimmed to `MaxEmailChars`.
  2. Ollama is called with a JSON schema built from the registered `IAiActionHandler`s.
  3. LifeHub applies its own validation, junk filter and marketing guard.
  4. Each suggestion is auto-applied (storing undo data) or queued in the AI Inbox.
- **Time.** All scheduling uses the time zone set in Settings → General (`Clock`), not the
  container's.

## Tech

- .NET 8 / ASP.NET Core / Blazor Server
- [MailKit](https://github.com/jstedfast/MailKit) for IMAP
- [PdfPig](https://github.com/UglyToad/PdfPig) for PDF text and scanned-page images
- [ZXing-js](https://github.com/zxing-js/library) and the native `BarcodeDetector` for scanning
- [Open Food Facts](https://world.openfoodfacts.org) for product lookup
- [Ollama](https://ollama.com) for local LLMs
- Docker images: `mcr.microsoft.com/dotnet/sdk:8.0` → `mcr.microsoft.com/dotnet/aspnet:8.0`
