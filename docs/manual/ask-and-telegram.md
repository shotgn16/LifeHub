# Ask LifeHub, memory and Telegram

Don't need one of these? Switch it off in **Settings → Modules** (under **AI**), like any other
part of LifeHub.

## Ask LifeHub

Press **Ask** at the top of any page (the **+** button on a phone), or use the box at the top of
the **AI Inbox**. Type a question or tell LifeHub what to do — in the same box:

- *What's due this week?* · *Am I free on Thursday afternoon?* · *What can I cook with what's in the fridge?*
- *Add milk and eggs* · *Remind me to call the plumber tomorrow*
- *I'm out Thursday 2 till 4* · *On holiday next week*
- *I paid the council tax* (ticks it off) · *Remember the bins go out Tuesday night*

<img src="../images/ask.png" alt="Ask LifeHub" width="60%">

LifeHub answers in purple, and lists anything it would change. **Nothing changes until you press
Do it** (or **Do all of these**). Press **Skip** for anything it got wrong. Anything you leave waits
in **AI Inbox → To review**, and anything you did can be undone from **AI Inbox → History**.

Ask needs an AI (**Settings → AI**). Without one, the same box adds tasks, shopping items and notes.

## Memory

**Memory** (in the menu) shows everything LifeHub's AI keeps in mind, and is where you add to it — a short list of things: routines, diets,
people, regular arrangements — *"Sam is vegetarian"*, *"bins go out Tuesday night"*,
*"I work from home on Fridays"*. The AI uses them when it reads your email, answers questions and
suggests meals.

- Add them on the **Memory** page (one per line), type them in the **Ask** box and press
  **Remember** (no AI needed), send `/remember …` on Telegram, or tell LifeHub *"remember…"* and confirm.
- Each is just for you unless you press **Share**, so everyone's AI in the household uses it.
- Edit or forget any of them at any time. LifeHub never adds one without you confirming, and
  refuses things that look like passwords or bank details.
- Emails can't add to your memory or change your schedule — only you can.

### Reminders from memory

Next to each fact on the **Memory** page is **Remind me** (highlighted when a fact sounds like a
routine, such as *"bins go out Tuesday night"*). It suggests when — here every Tuesday at 19:00 —
and you can change the wording, how often and the time. It becomes a repeating task in **Tasks**,
so you get a notification when it's due. Remove it with the × next to ⏰.

### Heads-ups

When something in your calendar mentions someone or something LifeHub knows about — an event
called *"Sam over for dinner"* and the fact *"Sam is vegan"* — LifeHub reminds you before it:
*"Sam over for dinner — Monday (in 3 days). Remember: Sam is vegan."*

- On the **Memory** page, under **Heads-ups**, choose which days before to be reminded — any of 7
  days down to the day itself (6, 3 and 1 days before and on the day to start with) — and the
  time of day. **Coming up** shows what it has found.
- When the fact is about food (vegan, vegetarian, allergies, diabetic, doesn't eat…) and an AI is
  set up, LifeHub also suggests meals for that day in **Meals**, the first time it tells you. Apply
  the one you like. **Meal ideas** next to an event asks for them straight away.
- Names work best: *"Sam is vegan"* matches *"Sam over"* and *"Sam's birthday"*. Days of the week
  and ordinary words don't count as names.
- Heads-ups come as notifications (choose where in **Settings → Notifications → Heads-ups**, so they
  can go to Telegram too). Switch them off with the switch on the card, or in
  **Settings → Background tasks**.

When you ask the meal planner for suggestions, it also knows who's coming on which day.

## Telegram

Chat with LifeHub from Telegram: ask the same questions and tell it the same things. It replies
with buttons to confirm any change. It can also send you your notifications.

**Set up the bot (once, by whoever manages LifeHub)**

1. In Telegram, open **@BotFather** and send `/newbot`.
2. Give it a name (e.g. *Smith Home*) and a username ending in `bot`.
3. BotFather sends a token (a long code like `123456789:AAE…`). In LifeHub, open
   **Settings → Telegram**, paste it and press **Connect**.

<img src="../images/telegram.png" alt="Settings → Telegram" width="80%">

**Connect your own chat (everyone)**

1. In **Settings → Telegram**, open the bot's link and press **Start** in Telegram.
2. The bot sends a 6-digit code. Type it in **Your Telegram → Connect**.

Or the admin can connect it for you: people who messaged the bot are listed under **Waiting to be
connected** — choose who it is and press **Connect**. Only connect chats you recognise.

Once connected, switch **Send my notifications here too** on or off. In the chat:
`/today` shows today at a glance, `/shopping` the shopping list, `/remember …` saves something
to memory, and `/stop` disconnects.

Good to know:
- LifeHub only answers chats someone connected. Anyone else just gets a code, which only works
  for an hour and only once someone enters it in LifeHub.
- Only one-to-one chats work, not groups.
- Messages pass through Telegram's servers, and your questions go to the AI you chose in
  **Settings → AI**. LifeHub itself doesn't need to be reachable from the internet.
- Switch it off any time in **Settings → Background tasks → Telegram chat**.
