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
                │ Background: EmailSync · Automation (every minute) · HaListSync             │
                └──────┬───────────────────┬──────────────────────┬──────────────────────────┘
                       │                   │                      │
            Home Assistant REST     Ollama /api/chat        IMAP · Microsoft Graph · EWS
     (states, services, templates,  (JSON-schema output)
      calendars, history, to-do,
      notify; publishes sensor.lifehub_*)
```

## Source layout (`lifehub/src/LifeHub`)

| Folder | Contents |
|---|---|
| `Domain/` | Plain entity classes: `TaskItem`, `Renewal`, `PantryItem`, `StapleItem`, `ShoppingItem`, `ProductInfo`, `EmailRecord`, `Promotion`, `AiSuggestion`, … |
| `Data/` | `IRepository<T>` and `JsonRepository<T>`, which keeps an in-memory copy, writes the file atomically and raises a change event |
| `Services/` | Business logic, plus the background jobs `AutomationService` and `EmailSyncService` |
| `Integrations/HomeAssistant/` | REST client, entity catalogue (names, areas, devices), automatic setup, two-way to-do sync |
| `Integrations/Email/` | `IEmailProvider` implementations: IMAP (MailKit), Microsoft Graph, EWS (SOAP) |
| `Integrations/Ollama/` | `/api/chat` client with JSON-schema structured output |
| `Ai/` | `AiService` (prompt, schema, checks, apply, undo) and `Handlers/`, with one class per action type |
| `Api/` | Login/setup endpoints and the REST API |
| `Infrastructure/` | Settings (with encrypted `[Secret]` fields), auth, HA ingress middleware, clock and time zone, data paths |
| `Components/` | Pages, layout, settings tabs and shared components |
| `wwwroot/` | `app.css`, the scanner script, ZXing, the manifest and icons |

## Key design points

- **Storage.** Each entity type is one JSON file (`data/db/TaskItem.json`, …). Reads come from
  memory. Writes are serialised and go to a temp file that is then renamed. That's plenty for a
  household, and storage can be swapped (see [extending.md](extending.md)).
- **Live UI.** Pages derive from `LiveComponent` and `Watch()` repositories, so changes made by
  background jobs, the API or another tab show up straight away.
- **Settings.** All settings live in one `settings.json`. Properties marked `[Secret]` are
  encrypted with ASP.NET Core Data Protection, with keys in `data/keys`. Small migrations run on
  load.
- **Auth.** There are three schemes:
  - **Cookie**: LifeHub's own login.
  - **Ingress**: requests from the HA Supervisor proxy (`172.30.32.2`) that carry the ingress
    headers are trusted.
  - **API key**: for `/api/v1`.

  The ingress middleware runs *before* forwarded-header processing so that the real remote IP is
  checked.
- **Background jobs.**
  - `AutomationService` ticks every minute. It handles reminders, the digest, expiry, auto-renewal
    roll-forward, nightly planning, presence and sensor publishing.
  - `EmailSyncService` polls the mailboxes.
  - `HaListSyncService` syncs the lists with HA's to-do lists.
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
- [ZXing-js](https://github.com/zxing-js/library) and the native `BarcodeDetector` for scanning
- [Open Food Facts](https://world.openfoodfacts.org) for product lookup
- [Ollama](https://ollama.com) for local LLMs
- Docker images: `mcr.microsoft.com/dotnet/sdk:8.0` → `mcr.microsoft.com/dotnet/aspnet:8.0`
