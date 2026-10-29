# Changelog

## 2.0.0
**Several people in one LifeHub.**
- Each person has their own tasks, email, AI inbox, Bible tracking, studies, notes, phones and API
  key.
- You choose whether food stock & shopping, meal planning and renewals are shared.
- In Home Assistant, LifeHub asks "Who's using LifeHub?" (optional PIN). Add people in
  Settings → People.
- Upgrading changes nothing until you add someone.

**Also new:**
- **Notes**: drop in bits of text; the AI files them into categories. Tidy up, tags and sharing.
- **Bible: start a study from a verse**: choose a level and what it's for (home group, preaching,
  teaching, youth, word study, one-to-one, personal) to get starter notes.
- **Deals** (Meals → Deals, can be switched off): offers at supermarkets near your town or
  postcode, matched to your shopping list and recipes, with meal ideas on days that suit and when
  to shop.

## 1.5.0
- New **Go deeper** button on Bible studies: StudyLight commentaries turned into a deeper explanation
  — interpretations, background, word studies, what it could look like and how to apply it. Depth
  (Overview / Detailed / In-depth) is set in Settings → Bible.
- Fix: "The model did not return JSON" on Ask for insights (the answer was cut off on small machines).

## 1.4.0
- Share verses from YouVersion to LifeHub to log exactly what you read (Android share menu, or an
  iPhone shortcut).
- Time in the Bible app is logged automatically and shown in Recently read.
- Optional "What did you read?" page and prompt (off by default) to record which passage.
- More accurate app time: rebuilt from HA history every minute, stops when the screen is locked
  (Interactive sensor), ignores quick glances. You can mark a session as "Wasn't reading".

## 1.3.2
- Model advice: use `qwen3-vl:4b-instruct`. The plain `qwen3-vl:4b` is the slower "thinking" version.

## 1.3.1
- Fix: no answer from "thinking" models such as qwen3-vl. Thinking is now off by default (you can
  turn it on in Settings → AI). Answers are length-capped, errors are clearer, and Try it shows a timer.

## 1.3.0
**New: Bible**
- Verse of the day, the same as the YouVersion app (free YouVersion key), or typed in yourself.
- Time in the YouVersion app is tracked: Android through the Home Assistant "Last used app" sensor,
  iPhone through a Shortcuts automation. A few minutes in the app ticks off the verse.
- A missed verse moves to the next day as a catch-up, and unfinished readings carry over.
  Optional reading plan, evening reminder, streak, heatmap and book coverage.
- Bible studies with notes plus photos/PDFs as proof. The local AI reads handwriting, breaks the
  study down, links it to your other studies, and on request compares it with StudyLight. AI text
  is purple and never changes your notes.

**New: meal planner**
- Plan up to 2 weeks around your calendar and energy, see what to buy (scaled and checked
  against your kitchen), and set dietary needs (gluten free etc.).
- AI suggestions to move meals, use up food, adapt recipes for your needs or improve them.
  Nothing changes until you press Apply.

**Kitchen & shopping**
- Add from photo: the vision model lists the groceries and reads printed dates. For missing ones,
  take a close-up, type them in or use a typical estimate.
- Use-by vs best-before: eggs, cereal, pasta and tins fade on a scale (past best → check first)
  instead of "expired". Opened items go stale sooner. Staples can override this.
- The shopping list only changes when you add things (typed, scanned, your chosen HA list, or the
  meal planner). Low staples are suggestions. Items LifeHub had added itself are removed once.

**Notifications**
- Choose Home Assistant, LifeHub's own web push (works on any phone, even without HA), or both.

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
