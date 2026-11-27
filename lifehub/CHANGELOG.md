# Changelog

## 3.2.2
- **Memory** is now in the menu: add things and see everything LifeHub knows.
- A **Remember** button in the Ask box, and `/remember` on Telegram, save things straight to memory.

## 3.2.1
- Reopening LifeHub after the phone or tab was asleep no longer freezes: it reconnects (or reloads
  itself) straight away.
- Phone app: nothing hides behind the camera cut-out on Android 15 any more.
- Ask LifeHub, Memory and Telegram can be switched off in **Settings → Modules**.

## 3.2.0
- **Ask LifeHub**: ask questions about your tasks, week, kitchen and renewals, or tell it what to
  do ("add milk", "I'm out Thursday 2–4"). It always asks before changing anything.
- **Memory** (AI Inbox → Memory): facts the AI keeps in mind, like diets and routines.
- **Telegram**: chat with LifeHub and get notifications there (Settings → Telegram).
- **Schedule**: set the days and hours you work, add holidays, time out and time you keep free —
  even if they're not in a calendar — and see the whole week at a glance.
- Phone app 2.1.0 with Ask LifeHub, memory and the new Schedule.

## 3.1.0
- **Easier email set-up**: Settings → Email → **Add mailbox**. Sign in with Microsoft, or follow
  the steps for a Gmail, iCloud, Yahoo, AOL or Fastmail app password. Add as many mailboxes as
  you like. Email is also checked whenever you open LifeHub.
- **Tidier notifications**: one notification per email check instead of one per email, no
  notifications for promotions, and a choice per kind (phone, LifeHub only, off) with quiet hours.
- **Use any AI**: Ollama, ChatGPT, Gemini, Claude or an OpenAI-compatible server.
- **Everything is stored encrypted.** Write down the recovery key in **Settings → Backup**.
- **Settings → Background tasks** lets you switch off or reschedule anything LifeHub does by
  itself.
- New phone app 2.0.0, which can also run on its own without LifeHub.

## 3.0.0
- **Bible features have moved to their own app.** Before anything is deleted, go to
  **Settings → Backup → Bible data from LifeHub 2** to export it to a zip, then delete it.
- **Cleaner, modular design**: switch sections on or off in **Settings → Modules**, arrange your
  home screen with **Customise**, and pick your own theme and accent colour in
  **Settings → Appearance**.
- New desktop apps for Windows, macOS and Linux (they can also set LifeHub up in Docker or point
  at this add-on), and phone apps 1.2.0.
- Security update for email (MailKit 4.16.0).

## 2.2.0
- The phone apps now **work offline**: your tasks, shopping list, notes, meals and Bible reading
  are kept on the phone. Anything you add or tick off is sent to LifeHub automatically when it's
  reachable again (update the apps to 1.1.0).
- Documentation updated throughout.

## 2.1.0
- **Phone apps for Android and iPhone**: LifeHub signed in as you, with notifications, the share
  menu, Siri & Shortcuts (iPhone) and Bible-app timing straight from your Android phone. See the
  Phone apps guide in the docs.

## 2.0.1
- Updates now download a ready-made image instead of compiling LifeHub on your Home Assistant
  machine. This fixes updates that took a very long time or never finished.

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
