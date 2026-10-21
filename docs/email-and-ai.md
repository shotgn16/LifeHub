# Email & AI

LifeHub reads your mailboxes and sends each new email to **your own Ollama server**. Nothing goes to
a cloud AI. The model classifies the email and suggests actions. LifeHub checks each suggestion,
then either applies it or waits for you.

```
Mailbox ──► LifeHub ──► Ollama (JSON schema) ──► checks & junk filter ──► auto-apply  ─► History (undo)
 (IMAP / Graph / EWS)                                                   └► AI Inbox (approve / dismiss)
```

## 1. Set up Ollama

1. Install [Ollama](https://ollama.com) on a machine with spare RAM. A GPU helps but isn't required.
   An Ubuntu VM with 8 GB or more RAM on the same host as Home Assistant is fine.
2. Let it listen on the network. Run `systemctl edit ollama`, add the lines below, then
   `systemctl restart ollama`.
   ```ini
   [Service]
   Environment="OLLAMA_HOST=0.0.0.0"
   ```
3. Pull a model: `ollama pull qwen2.5:7b` (the default, and good with dates and JSON).
   `llama3.1:8b` and `mistral-nemo` also work. You need **Ollama 0.5 or later** for structured
   output.
4. In LifeHub, open **Settings → AI**:
   - Enter `http://<ollama-ip>:11434`.
   - Press **List**, pick a model, then press **Try it**.
   - In the add-on, `localhost` means the LifeHub container itself, so always use the IP.

## 2. Add mailboxes

Open **Settings → Email → Add mailbox**.

### Gmail and other IMAP (iCloud, Fastmail, Yahoo…)
1. Turn on 2-Step Verification. Then go to **Google Account → Security → App passwords** and
   create one.
2. Choose the **IMAP** type and the **Gmail** preset. Enter your address and the app password, not
   your normal password.

### Microsoft 365, Outlook.com and Exchange Online
This needs a free, one-time app registration:
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

## Tuning

| Setting | Where | Notes |
|---|---|---|
| Extra rules | Settings → AI | Free text added to the prompt, for example "Emails from school are Personal, high priority". |
| Poll interval | Settings → Email | Default 10 minutes, up to 20 emails per poll. The first sync looks back 2 days. |
| Ignore senders | Each mailbox | Addresses or domains to skip completely. |
| Timeout | Settings → AI | Raise it for large models running on CPU (default 180 seconds). |

## Other things the local AI does

- [Bible studies](bible.md): reads handwritten notes, breaks studies down, finds connections, and
  compares your notes with StudyLight.
- [Kitchen photos](kitchen-and-meals.md#add-from-photo): lists your groceries and reads their dates.
- [Meal planner](kitchen-and-meals.md#meal-planner): moves meals around your week, adapts recipes
  for dietary needs, and uses up food.

Two optional models help with these. Set them in **Settings → AI**:

| Model | For | Example |
|---|---|---|
| Vision model | photos and scans | `ollama pull qwen2.5vl:7b` |
| Embedding model | linking studies by meaning | `ollama pull nomic-embed-text` |

## Privacy
- Email bodies are sent to **your** Ollama server only, and are never stored. LifeHub keeps the
  sender, subject, a short snippet, the category and the AI summary.
- Mailbox passwords and tokens are encrypted at rest. See [security](security.md).
