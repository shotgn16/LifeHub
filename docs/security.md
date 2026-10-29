# Data, backups & security

## Where data lives

Everything is in one folder:

- `/data` in Docker and the HA add-on
- `LIFEHUB_DATA` elsewhere

| Path | Contents |
|---|---|
| `db/*.json` | One file per entity type: tasks, renewals, pantry, shopping, emails (metadata only), suggestions, … |
| `settings.json` | All settings. Secrets are encrypted. |
| `keys/` | Data Protection key ring, which decrypts the secrets in `settings.json` |
| `bible/files/` | Uploaded study notes (photos, PDFs, documents), served only to signed-in users |
| `backups/` | Nightly zip backups (the latest 14 are kept) |

## Backups

- LifeHub zips `db/`, `settings.json`, `keys/` and `bible/files/` every night. **Settings → Backup** can create
  and download a backup on demand.
- The HA add-on's `/data` is also included in **Home Assistant backups**, which are hot backups.
- **Always back up `keys/` together with `settings.json`.** Without the keys, saved passwords and
  tokens can't be decrypted after a restore. You would have to enter them again; nothing else is
  lost.

## What's stored about email

LifeHub stores the sender, subject, date, a short snippet, the category, the AI summary and a
link back to the message. **It doesn't store email bodies.** They're only sent to your own Ollama
server while the email is being processed.

## What leaves your network

LifeHub only connects to outside services you've set up:

| Service | When | What's sent |
|---|---|---|
| Open Food Facts | unknown barcode scanned | the barcode number |
| YouVersion Platform API | once a day, if you added a key | the day of the year, and your app key |
| StudyLight / Biblia Graphia | only when you press **Ask for insights**, **Go deeper** or **Starter notes** | a page request for the passage (no notes) |
| postcodes.io / OpenStreetMap (Nominatim, Overpass) | when Deals is on and you set your area | the town, city or postcode you typed |
| Supermarket offers pages | when Deals is on, every 24 hours by default | an ordinary page request (no personal data) |
| Web push services (Google, Apple, Mozilla) | when LifeHub's own notifications are on | the encrypted message, which only your device can read |
| Microsoft Graph / your mail servers | email sync | your sign-in |

Your notes, photos and emails are only ever sent to **your own** Ollama server.

## Authentication

- **People.** Each person has their own login, with the password hashed with PBKDF2. Sign-in uses
  a 30-day sliding cookie, and repeated failures lock the account out. Each person's tasks, email,
  Bible data and notes are only returned to them (see [people.md](people.md)). Admins manage
  household settings and people.
- **"Who's using LifeHub?" in Home Assistant.** Optional per-person PINs, also hashed and
  rate-limited. A choice made on this screen is only accepted for requests coming through HA
  ingress, never on the direct port.
- **HA ingress.** Requests from the Supervisor's proxy that carry ingress headers are trusted,
  because Home Assistant has already authenticated the user. You can turn this off in Settings →
  Security & API.
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
