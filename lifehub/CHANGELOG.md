# Changelog

## 1.2.1
- Phone notifications rewritten for the lock screen: short headings, one item per line, "+N more".
- Morning digest in sections (📅 today, ✅ tasks due, 🔁 renewals, 🥛 use up, 🛒 to buy).
- "While you're out" shopping list goes only to the phone of the person who left, using their first name.
- Notifications of the same kind replace each other instead of piling up, and are grouped together.
- Tapping a notification opens LifeHub (set your LifeHub web address in Settings → General, otherwise
  it opens inside the Home Assistant app).

## 1.2.0
**Email & AI**
- Every email is classified (action needed, bill, receipt, delivery, booking, promotion, newsletter…).
- Adverts and newsletters never become shopping items, tasks, renewals or kitchen stock — deals go to a
  new **Promotions** list (auto-cleared when they expire; save or add to shopping yourself).
- Repeat emails about the same issue are **stacked** onto one task with a ×N count and a dated log.
  **Merge duplicates** on the Tasks page stacks existing ones.
- Emails ↔ tasks: deleting a task moves its email(s) to the mailbox bin; deleting an email (in LifeHub
  or your mail app) removes its open tasks. Archived emails are left alone. Never a permanent delete.
- Stricter checks: nonsense/boilerplate titles dropped, renewals need a real price, currency validated,
  kitchen stock only from grocery deliveries/receipts and now waits for approval.
- AI Inbox → History: one-tap **clean-up** of everything the AI added from emails, by type.
- Microsoft 365 now asks for Mail.ReadWrite (to move emails to Deleted Items) — sign in again once.

**Home Assistant**
- Names, rooms and devices everywhere instead of entity IDs; Home page grouped by room with scenes and
  scripts as buttons, climate, temperature/humidity and open-door alerts; Customise to hide tiles.
- Automatic setup: people, calendars (and one that can take events), phones, energy sensors and lists.
- Two-way sync of the shopping list with HA's Shopping List (Assist voice, HA app) and, optionally,
  tasks with an HA to-do list.
- Reminders go to every phone you tick.

## 1.1.2
- Fix: barcode scanner could fail to load behind Cloudflare Tunnel / HTTPS proxies (mixed content).
- Honour forwarded HTTPS headers through multiple proxies (Cloudflare → cloudflared).

## 1.1.1
- Fix: opening LifeHub from the Home Assistant sidebar showed "404: Not Found".

## 1.1.0
- Barcode scanner (phone camera, photo, typed or USB scanner): stock in, use up, add to shopping list,
  with undo. Open Food Facts lookup, learned products, link products to staples. `POST /api/v1/scan`.
- Automatic planning: tomorrow every evening, top-up each morning, with notification and calendar option.
- AI: tasks, shopping items and kitchen stock are applied automatically by default, with undo and an
  in-app notice for anything done from email.

## 1.0.0
- First release: renewals, tasks, kitchen & shopping, schedule & day planner, Home Assistant
  integration, AI inbox (Ollama) for IMAP / Microsoft 365 / Exchange, REST API.
