# Changelog

All notable changes to LifeHub are recorded here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses
[Semantic Versioning](https://semver.org/).

Home Assistant shows the add-on's own changelog ([`lifehub/CHANGELOG.md`](lifehub/CHANGELOG.md))
on the add-on page. That file is a shorter version of this one.

## [Unreleased]

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

[Unreleased]: https://github.com/Shotgn16/LifeHub/compare/v1.2.1...HEAD
[1.2.1]: https://github.com/Shotgn16/LifeHub/releases/tag/v1.2.1

Versions before 1.2.1 were developed privately and are recorded here for history. The first public
release on GitHub is 1.2.1.
