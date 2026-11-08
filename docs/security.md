# Data, backups & security

## Where data lives

Everything is in one folder:

- `/data` in Docker and the HA add-on
- the desktop apps: `%LOCALAPPDATA%\LifeHub` (Windows), `~/Library/Application Support/LifeHub`
  (macOS), `~/.local/share/lifehub` (Linux)
- `LIFEHUB_DATA` (or `--data <folder>`) elsewhere

| Path | Contents |
|---|---|
| `db/*.json` | One file per entity type: tasks, renewals, pantry, shopping, emails (metadata only), suggestions, … **Encrypted.** |
| `settings.json` | All settings. **Encrypted**, and passwords, tokens and API keys inside it are encrypted again. |
| `keys/data.key` | The data key (AES-256), itself protected by the Data Protection key ring |
| `keys/key-*.xml` | Data Protection key ring (protected by DPAPI on Windows) |
| `desktop.json` | Desktop app only: how LifeHub runs on this computer (mode, port) |
| `custom.css` | Optional: your own styles (see [customise](manual/customise.md)) |
| `backups/` | Nightly zip backups (the latest 14 are kept) |

## Encryption at rest

Since 3.1, **everything LifeHub stores is encrypted** with AES-256-GCM: every `db/*.json` file and
`settings.json` (format: `LHE1` + 12-byte nonce + 16-byte tag + ciphertext; see `Data/DataCipher.cs`). Files from
earlier versions are encrypted on the first start.

| Where | The key is kept |
|---|---|
| Windows (desktop app, service) | `keys/data.key`, wrapped by Data Protection, whose keys are protected by **Windows DPAPI** (machine scope) |
| macOS, Linux, Docker, Home Assistant | `keys/data.key`, wrapped by Data Protection; the `keys` folder is readable only by LifeHub's user |
| Phone app (on its own) | the phone's secure storage (**Android Keystore**, **iOS Keychain**); never in a file |
| Anywhere, optionally | the `LIFEHUB_DATA_KEY` environment variable (your recovery key), e.g. to keep the key out of a Docker volume |

**Recovery key.** **Settings → Backup → Show recovery key** shows the key as text. Keep it in a password manager:
it's what opens your backups on another computer. To move LifeHub, copy the data folder and save the recovery key as
`keys/recovery.key` there (or set `LIFEHUB_DATA_KEY`); LifeHub adopts it on start. If the key is missing while the
data is encrypted, LifeHub refuses to start rather than starting empty.

## Backups

- LifeHub zips `db/` and `settings.json` every night — already encrypted — and keeps the latest 14 (both
  adjustable in **Settings → Background tasks**). **Settings → Backup** can create and download one on demand.
- **Backups don't contain the key**, so a copied backup can't be read on its own. The same LifeHub opens its own
  backups; elsewhere you need the recovery key.
- The HA add-on's `/data` is also included in **Home Assistant backups** (with its key).

## What's stored about email

LifeHub stores the sender, subject, date, a short snippet, the category, the AI summary and a
link back to the message (all encrypted). **It doesn't store email bodies.** They're only sent to the AI you
chose while the email is being processed.

## What leaves your network

LifeHub only connects to outside services you've set up:

| Service | When | What's sent |
|---|---|---|
| Open Food Facts | unknown barcode scanned | the barcode number |
| postcodes.io / OpenStreetMap (Nominatim, Overpass) | when Deals is on and you set your area | the town, city or postcode you typed |
| Supermarket offers pages | when Deals is on, every 24 hours by default | an ordinary page request (no personal data) |
| Web push services (Google, Apple, Mozilla) | when LifeHub's own notifications are on | the encrypted message, which only your device can read |
| Microsoft Graph / your mail servers | email sync | your sign-in |
| OpenAI, Google (Gemini) or Anthropic (Claude) | only if you choose one in Settings → AI | the emails, notes, photos and lists LifeHub asks the AI about, with your API key |
| Hugging Face (model files) | phone app, when you download the on-device AI | a normal download |
| Thunderbird's provider database / your mail domain | adding a mailbox with "Another provider" | the domain part of the address |

With **Ollama** or the **on-device AI** (the defaults), your notes, photos and emails never leave your network or
phone.

## Authentication

- **Desktop app.** The app window signs the main person in with a random secret stored in
  `keys/desktop.token` (readable only by your user), and only accepts it from the same computer
  (`127.0.0.1`). Before an account exists, the setup assistant only opens from this computer. In
  "this computer only" mode LifeHub listens on `127.0.0.1` alone; the phone and server modes listen
  on your network (Windows adds a firewall rule for the chosen port when it runs as a service).

- **People.** Each person has their own login, with the password hashed with PBKDF2. Sign-in uses
  a 30-day sliding cookie, and repeated failures lock the account out. Each person's tasks, email
  and notes are only returned to them (see [people.md](people.md)). Admins manage
  household settings and people.
- **"Who's using LifeHub?" in Home Assistant.** Optional per-person PINs, also hashed and
  rate-limited. A choice made on this screen is only accepted for requests coming through HA
  ingress, never on the direct port.
- **HA ingress.** Requests from the Supervisor's proxy that carry ingress headers are trusted,
  because Home Assistant has already authenticated the user. You can turn this off in Settings →
  Security & API.
- **Phone apps.** They store your LifeHub address and your API key on the phone: the iPhone app
  in the Keychain, the Android app in app-private storage. For **offline use** they also keep a
  copy of your open tasks, shopping list, recent notes and this week's meals, plus any changes waiting to be sent. This copy is in the app's private storage and is
  protected by the phone's own encryption (iOS file protection; Android's app sandbox).
  Uninstalling the app deletes it. They sign in with `POST /auth/app`,
  which is rate-limited like the login form. Removing a person or regenerating their key signs
  their phones out.
- **API keys.** Needed for `/api/v1/*`. There is one key per person, it acts as that person, and
  you can regenerate it at any time. Removing a person revokes their key.
- **Secrets.** Mailbox passwords, OAuth refresh tokens, the HA token, the API key and the web-push
  signing key are encrypted at rest with ASP.NET Core Data Protection.

## Exposing LifeHub to the internet

- Always use **HTTPS**, through Cloudflare Tunnel, Let's Encrypt or a reverse proxy, and set a
  strong password.
- Consider putting Cloudflare Access, or your proxy's authentication, in front.
- Forwarded headers are honoured, so redirects and secure cookies work behind proxies.

## Reporting a vulnerability

See [SECURITY.md](../SECURITY.md).
