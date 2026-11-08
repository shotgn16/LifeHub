# Email & AI

LifeHub reads your mailboxes and sends each new email to **the AI you choose**: your own Ollama server
(private, the default), a model inside the phone app, or ChatGPT, Gemini or Claude with your own API key. The model
classifies the email and suggests actions. LifeHub checks each suggestion, then either applies it or waits for you.

```
Mailbox ──► LifeHub ──► AI (JSON answer) ──► checks & junk filter ──► auto-apply  ─► History (undo)
 (IMAP / Graph / EWS)    Ollama · on-device ·                         └► AI Inbox (approve / dismiss)
                         ChatGPT · Gemini · Claude · OpenAI-compatible
```

The easy, step-by-step version: [manual/email.md](manual/email.md) and [manual/ai.md](manual/ai.md).

## 1. Choose the AI

**Settings → AI → Which AI.** Ollama keeps everything on your network. With ChatGPT, Gemini or Claude,
emails and notes are sent to that company; paste your API key and press **List** to pick a model. In the
phone app on its own, **On this device** runs a model on the phone. Developers: the request formats are in
`Integrations/Ollama/AiProviders.cs` and `AiClient` routes every AI call through the chosen provider.

### Ollama

1. Install [Ollama](https://ollama.com) on a machine with spare RAM. A GPU helps but isn't required.
   An Ubuntu VM with 8 GB or more RAM on the same host as Home Assistant is fine.
2. Let it listen on the network. Run `systemctl edit ollama`, add the lines below, then
   `systemctl restart ollama`.
   ```ini
   [Service]
   Environment="OLLAMA_HOST=0.0.0.0"
   ```
3. Pull a model. On a small machine (8 GB, e.g. Mac mini M-series) use **`qwen3-vl:4b-instruct`** for both the
   main model and the vision model, so only one model sits in memory.
   Otherwise pull a model: `ollama pull qwen2.5:7b` (the default, and good with dates and JSON).
   `llama3.1:8b` and `mistral-nemo` also work. You need **Ollama 0.5 or later** for structured
   output.
4. In LifeHub, open **Settings → AI**:
   - Enter `http://<ollama-ip>:11434`.
   - Press **List**, pick a model, then press **Try it**.
   - In the add-on, `localhost` means the LifeHub container itself, so always use the IP.

## 2. Add mailboxes

Open **Settings → Email → Add mailbox** and pick your provider: **Outlook/Hotmail/Microsoft 365** sign in with
Microsoft; **Gmail, iCloud, Yahoo, AOL and Fastmail** connect with an app password (LifeHub opens the right page);
**anything else** is found from your address (Thunderbird's provider database, the domain's autoconfig, then
`imap.<domain>`). LifeHub tests the connection before saving. Add as many as you like, including several of the
same kind. **Add manually** keeps every setting in your hands. Each person adds their own mailboxes (see
[People](people.md)). Emails, AI suggestions, promotions and the tasks made from them belong to
the person whose mailbox it is. The AI settings below are set once for the household by an
admin.

### Gmail and other IMAP (iCloud, Fastmail, Yahoo…)
**Add mailbox → Gmail** (or your provider) → **Open Gmail settings** → create an app password (Google needs
2-Step Verification) → paste it → **Connect**.

### Microsoft 365, Outlook.com and Exchange Online
**Add mailbox → Outlook, Hotmail or Microsoft 365 → Open Microsoft sign-in**, enter the code, sign in, **Accept**.

This uses one free app registration for your copy of LifeHub, made once by whoever publishes or runs it
(see [manual/email.md](manual/email.md#setting-up-microsoft-sign-in-once-for-whoever-publishes-lifehub)); its ID
is built in with the `MS_CLIENT_ID` repository variable, set in **Settings → Email → Microsoft sign-in**, or
given as `LIFEHUB_MS_CLIENT_ID`. To use your own registration for one mailbox instead (**Add manually**):
1. Go to [entra.microsoft.com](https://entra.microsoft.com), then **App registrations → New
   registration**. Choose *Accounts in any organizational directory and personal Microsoft
   accounts*. Leave the redirect URI empty.
2. Go to **Authentication → Advanced settings**, set **Allow public client flows** to **Yes**, and
   save.
3. Go to **API permissions → Add → Microsoft Graph → Delegated** and add `Mail.ReadWrite`,
   `Calendars.ReadWrite`, `User.Read` and `offline_access`.
4. In LifeHub add a **Microsoft 365** mailbox and paste the **Application (client) ID**. Set the
   tenant to one of these:
   - `common` for work or school accounts
   - `consumers` for Outlook.com and Hotmail
   - your tenant ID
5. Press **Save & sign in with Microsoft**. Open the link it shows and enter the code. Tokens then
   refresh automatically.
6. Optionally tick **Also read this calendar** to use it for your schedule.

### On-premises Exchange (EWS)
Add an **Exchange on-premises** mailbox:
- **URL:** `https://mail.yourdomain.com/EWS/Exchange.asmx`
- your username, and the domain if needed
- your password

NTLM, Negotiate and Basic authentication are supported.

## 3. What the AI does with each email

### Classification
Every email gets a category:

| Category | Can create |
|---|---|
| Action needed, Work, Personal | tasks, events (never shopping items) |
| Bill | tasks, renewal updates |
| Receipt | renewal updates, and kitchen stock for grocery orders |
| Delivery, Booking | events, tasks |
| Account / security, Notification | tasks, only if you need to act |
| **Promotion, Newsletter** | **promotions only**: never tasks, shopping, events, renewals or stock |

### Action types

| Type | Default |
|---|---|
| **Task** | Applied automatically. Repeat issues stack onto one task (×N). |
| **Shopping** | **Only from notes you type** (quick capture), never from emails. Applied automatically. |
| **Promotion** | Applied automatically, to the Promotions list. Clears itself after the offer ends. |
| **Event** | Waits for approval, then goes to your calendar. |
| **Renewal** | Waits for approval. Needs a real price. |
| **Pantry (kitchen stock)** | Waits for approval. Only taken from grocery deliveries and receipts. |

Change these defaults with the **Auto-apply** checkboxes in **Settings → AI**. Suggestions below the confidence
threshold (default **0.85**) always wait for approval.

### Safety checks
LifeHub doesn't trust the model blindly. It also:

- drops boilerplate and nonsense titles (unsubscribe text, legal footers, slogans)
- ignores currency strings it can't parse
- turns marketing that the model mislabelled into a promotion
- skips senders you've chosen to ignore

### Undo and clean-up
- **AI Inbox → History** lists everything that was applied automatically. Each entry has an
  **Undo** button.
- **Clean up** removes everything the AI added from email, by type. Use it after changing
  settings, or if a model misbehaved.

### Email ↔ task link
Both directions are on by default, and both can be turned off in Settings → Email:

- **Deleting a task** that came from an email moves the email to the mailbox's **Bin / Deleted
  Items**.
- **Deleting the email**, in LifeHub or in your mail client, removes its open tasks.
- **Archived** email (moved to another folder) is left alone. LifeHub never permanently deletes
  anything.

This is the only change LifeHub makes to your mailbox. It never marks mail as read.

### Quick capture
The **Capture** button (and `POST /api/v1/capture`) takes plain English, for example
*"dentist Tuesday 2:30, renew example.co.uk by 3 Nov, need coffee"*, and turns it into the right
actions.

## Checking and notifications

LifeHub checks every mailbox when someone opens LifeHub or the phone app (at most every 2 minutes) and every
**10 minutes** (Settings → Email or **Settings → Background tasks**). What the AI applies from one check is told in
**one** notification ("From your email: 2 tasks, 1 renewal"); offers saved to Promotions are never notified
(see [manual/notifications.md](manual/notifications.md)).

## Tuning

| Setting | Where | Notes |
|---|---|---|
| Extra rules | Settings → AI | Free text added to the prompt, for example "Emails from school are Personal, high priority". |
| Poll interval | Settings → Email | Default 10 minutes, up to 20 emails per poll. The first sync looks back 2 days. |
| Ignore senders | Each mailbox | Addresses or domains to skip completely. |
| Timeout | Settings → AI | Raise it for large models running on CPU (default 180 seconds). |

## Other things the local AI does

- [Notes](notes.md): files each note into a category with tags.
- [Kitchen photos](kitchen-and-meals.md#add-from-photo): lists your groceries and reads their dates.
- [Meal planner](kitchen-and-meals.md#meal-planner): moves meals around your week, adapts recipes
  for dietary needs, and uses up food.

An optional vision model helps with photos. Set them in **Settings → AI**:

| Model | For | Example |
|---|---|---|
| Vision model | photos and scans | `ollama pull qwen3-vl:4b-instruct` |

## Privacy
- Email bodies are sent only to the AI you chose — **your** Ollama server or the phone itself by default, or
  OpenAI, Google or Anthropic if you pick them — and are never stored. LifeHub keeps the
  sender, subject, a short snippet, the category and the AI summary.
- Mailbox passwords and tokens are encrypted at rest. See [security](security.md).
