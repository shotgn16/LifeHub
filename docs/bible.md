# Bible

The Bible section keeps your daily reading on track and keeps your studies in one place. It has
three tabs:

- **Today**: the verse of the day, today's reading list (including anything you missed), your
  streak and what you've read recently.
- **Studies**: your study notes, with photos, scans or PDFs as proof of study, plus AI help that's
  always kept separate from your own words.
- **Progress**: chapters and books read across the Bible.

You can turn the section on or off in **Settings → Bible**.

<img src="images/bible.png" alt="Bible page" width="80%">

## Verse of the day

LifeHub can show **the same verse of the day as the YouVersion app**:

1. Sign in at [platform.youversion.com](https://platform.youversion.com), create an app and copy
   its **app key**. It's free.
2. Paste it into **Settings → Bible → YouVersion app key**, choose a Bible version, and press
   **Save & fetch today's verse**.

Without a key you can type the day's verse on the Bible page. Either way, **Open in YouVersion**
opens the passage in the app on your phone.

Some versions aren't licensed for apps like LifeHub. In that case you get the reference and link
but no verse text, so try BSB or KJV.

## Tracking time in the YouVersion app

LifeHub can notice when you've been reading in the YouVersion app and tick off the verse of the
day for you. A few minutes in the app (2 by default) counts as reading.

### Android (through Home Assistant)

1. In the **Home Assistant Companion app**, go to Settings → Companion app → Manage sensors.
2. Turn on **Last used app**.
3. For more accurate timing, set *Sensor update frequency* to **Fast always** on the same screen.
4. In LifeHub, go to **Settings → Bible** and tick your phone's *Last used app* sensor.

LifeHub checks every minute. After a restart it rebuilds the last day's sessions from Home
Assistant's history. How precise the timing is depends on how often the phone updates the sensor.

### iPhone (Shortcuts)

iOS doesn't share app usage with Home Assistant, so use two small Shortcuts automations:

1. Open **Shortcuts → Automation → + → App**, choose **Bible** (that's YouVersion), pick **Is
   Opened**, then **Run Immediately**.
2. Add the action **Get Contents of URL**:
   - **URL:** `https://<your LifeHub address>/api/v1/bible/app`
   - **Method:** POST
   - **Header:** `X-Api-Key` set to your key from Settings → Security & API
   - **Request Body:** JSON, with `event` = `opened`
3. Make a second automation for **Is Closed**, with `event` = `closed`.

Your phone has to be able to reach LifeHub at that address, for example through your
[Cloudflare Tunnel](remote-access.md).

## Staying on track

- **Carry-over.** If you don't read the verse of the day, it's added to tomorrow's list as a
  **Catch up** item. Unfinished plan readings move forward too, and each shows how many times it
  has been carried.
- **Reading plan (optional).** Choose a book and how many chapters a day. LifeHub adds the next
  chapters each day and moves on to the next book when one ends. Missed chapters don't pile up:
  new ones are only added once you've caught up.
- **Reminder.** If you haven't read by the time you set (20:30 by default), you get a notification
  with today's verse and anything left on your list.
- **Streak and heatmap.** A day counts as read if you read the verse, logged a reading, ticked a
  plan item, studied, or spent the minimum time in the app.
- **Recently read** lists every passage logged, wherever it came from: verse of the day, plan,
  catch-up, study, typed in, or the API.

## Bible studies

Press **New study** and add:

- a title
- the passages you studied (any common format works: `Rom 8:28-39; 12:1-2`, `1 Cor 13`, `Ps 23`)
- your notes
- **proof of study**: photos of handwritten notes (**Take photo** on a phone), scans, PDFs, Word
  documents or text files

### What the local AI does (in purple)

Everything the AI produces is shown in **purple**, labelled as AI-generated, and stored separately
from your notes. **It never changes your notes.**

- **Reads your uploads.** A vision model transcribes handwriting and scans. PDFs with a text layer
  are read directly. You can expand **AI-read text from your uploads** to check it.
- **Breaks the study down** into:
  - a summary and key points
  - the passages mentioned (one tap adds them to your passages)
  - themes and Bible people
  - questions and applications or prayer points
- **Connected studies.** Other studies are linked when they share passages, themes or people. With
  an embedding model, studies with similar ideas are linked too. You'll also see when you've read
  the same passage before.

### Ask for insights (StudyLight & Biblia Graphia)

**Ask for insights** only runs when you press it:

1. LifeHub fetches the pages for your passages from the reference sites you've enabled in
   Settings → Bible.
2. It sends them, your notes and your related studies to your local AI.
3. You get 4–8 short insights:

| Kind | What it is |
|---|---|
| Connection | A link between your notes and a source, passage or other study |
| Go deeper | A point you could expand, and how |
| Background | Historical, cultural or literary context |
| Cross-references | Other passages that shed light on this one |
| Question to explore | Something to study or pray about next |
| Link to another study | A specific connection to one of your studies |

Each insight shows its source, linked where possible. **Pin** the ones you want to keep (pinned
insights survive a refresh) and dismiss the rest.

**Sites:**

- **StudyLight.org** works out of the box. LifeHub reads the commentary page for the first verse of
  each passage.
- **Biblia Graphia** builds its pages in the browser with JavaScript, so LifeHub usually can't read
  them. You'll see a note saying so, and **Read more** still links you there.

Both URL patterns are editable in Settings → Bible. You can use the placeholders `{book}`,
`{name}`, `{chapter}`, `{verse}` and `{usfm}`.

### Models

| Purpose | Setting | Suggested |
|---|---|---|
| Breakdown and insights | Settings → AI → Model | `qwen2.5:7b` |
| Reading handwriting and scans | Settings → AI → **Vision model** | `qwen2.5vl:7b`, `llama3.2-vision`, `minicpm-v` |
| Linking studies by meaning (optional) | Settings → AI → **Embedding model** | `nomic-embed-text` |

## Home Assistant

`sensor.lifehub_bible_streak` has the streak as its state. Its attributes are `read_today`,
`verse`, `verse_read`, `app_minutes_today` and `to_read`. Example: light a lamp at 9pm if you
haven't read yet.

```yaml
automation:
  - alias: Bible reading nudge
    triggers:
      - trigger: time
        at: "21:00:00"
    conditions:
      - condition: template
        value_template: "{{ not state_attr('sensor.lifehub_bible_streak', 'read_today') }}"
    actions:
      - action: light.turn_on
        target: { entity_id: light.reading_lamp }
```

## API

See [api.md](api.md#bible). The endpoints are `POST /api/v1/bible/app`,
`POST /api/v1/bible/read` and `GET /api/v1/bible/today`.
