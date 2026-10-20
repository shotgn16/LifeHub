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
| `backups/` | Nightly zip backups (the latest 14 are kept) |

## Backups

- LifeHub zips `db/`, `settings.json` and `keys/` every night. **Settings → Backup** can create
  and download a backup on demand.
- The HA add-on's `/data` is also included in **Home Assistant backups**, which are hot backups.
- **Always back up `keys/` together with `settings.json`.** Without the keys, saved passwords and
  tokens can't be decrypted after a restore. You would have to enter them again; nothing else is
  lost.

## What's stored about email

LifeHub stores the sender, subject, date, a short snippet, the category, the AI summary and a
link back to the message. **It doesn't store email bodies.** They're only sent to your own Ollama
server while the email is being processed.

## Authentication

- **One account.** The password is hashed with PBKDF2. Sign-in uses a 30-day sliding cookie, and
  repeated failures lock the account out.
- **HA ingress.** Requests from the Supervisor's proxy that carry ingress headers are trusted,
  because Home Assistant has already authenticated the user. You can turn this off in Settings →
  Security & API.
- **API key.** Needed for `/api/v1/*`. You can regenerate it at any time.
- **Secrets.** Mailbox passwords, OAuth refresh tokens, the HA token and the API key are encrypted
  at rest with ASP.NET Core Data Protection.

## Exposing LifeHub to the internet

- Always use **HTTPS**, through Cloudflare Tunnel, Let's Encrypt or a reverse proxy, and set a
  strong password.
- Consider putting Cloudflare Access, or your proxy's authentication, in front.
- Forwarded headers are honoured, so redirects and secure cookies work behind proxies.

## Reporting a vulnerability

See [SECURITY.md](../SECURITY.md).
