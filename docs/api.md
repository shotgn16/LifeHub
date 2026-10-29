# REST API

LifeHub has a small REST API for Home Assistant automations, Assist, iOS Shortcuts, NFC tags and
hardware barcode scanners.

## Authentication

Create or copy **your** key in **Settings → Security & API**. Each person has their own key, and
everything done with it is done as that person: their tasks, their notes, their Bible reading.
Send it with every request, either way:

```
X-Api-Key: <key>
Authorization: Bearer <key>
```

Use the direct port (`http://<lifehub-host>:8099`) or your HTTPS hostname as the base URL. HA
ingress URLs need a Home Assistant session, so they don't suit API calls.

## Endpoints

| Method | Path | Body / query | Returns |
|---|---|---|---|
| GET | `/health` | | `{status, version}`. No key needed. |
| GET | `/api/v1/summary` | | Period, tasks due, overdue count, renewals due, monthly spend, food expiring, shopping list, AI pending |
| GET | `/api/v1/period` | | Current period, until, next period, current event |
| GET | `/api/v1/tasks` | `?filter=today` | Open tasks |
| POST | `/api/v1/tasks` | `{"title","category","due":"2026-10-20","time":"14:00","priority":"high","notes"}` | The new task. Only `title` is required. |
| POST | `/api/v1/tasks/{id}/complete` | | Completes the task. A recurring task creates its next occurrence. |
| GET | `/api/v1/shopping` | | Items to buy |
| POST | `/api/v1/shopping` | `{"name":"Milk","quantity":2,"unit":"pints"}` | Adds the item, merging duplicates |
| POST | `/api/v1/capture` | `{"text":"dentist Tuesday 2:30 and buy coffee"}` | AI quick capture. The actions follow your auto-apply settings. |
| POST | `/api/v1/scan` | `{"barcode":"5000112637922","mode":"add\|use\|shopping","quantity":1}` | The scan result. Unknown barcodes return 404; scan them once in the app to name them. |
| POST | `/api/v1/notes` | `{"text":"Door code 4471","shared":false}` | Saves a note; the AI files it into a category. Returns `category` (a first guess while the AI works). |
| GET | `/api/v1/notes` | `?category=Gift Ideas&q=scarf` | Your notes (and shared ones), newest first |
| GET | `/api/v1/meals` | `?days=1` (1–14) | Planned meals: date, slot, title, servings, cooked |
| POST | `/api/v1/bible/app` | `{"event":"opened\|closed","device":"iPhone"}` | Times the Bible app (for the iPhone Shortcuts automation). Returns `minutesToday`. |
| POST | `/api/v1/bible/read` | `{"reference":"Ps 23","minutes":10}` | Logs a reading. `reference` can be typed, YouVersion share text or a bible.com link; matching list items and the verse are ticked. Leave it out to mark today's verse as read. |
| GET | `/api/v1/bible/today` | | Verse of the day (and text), whether you've read today, the streak, minutes in the app, and what's still to read |

## Examples

### curl

```bash
curl -H "X-Api-Key: $KEY" -H "Content-Type: application/json" \
     -d '{"title":"Change the furnace filter","category":"Home DIY","due":"2026-11-01"}' \
     http://lifehub.local:8099/api/v1/tasks
```

### Home Assistant `rest_command`

```yaml
# configuration.yaml
rest_command:
  lifehub_shopping:
    url: "http://192.168.1.10:8099/api/v1/shopping"
    method: POST
    headers:
      X-Api-Key: !secret lifehub_api_key
    content_type: "application/json"
    payload: '{"name": "{{ item }}"}'

  lifehub_task:
    url: "http://192.168.1.10:8099/api/v1/tasks"
    method: POST
    headers:
      X-Api-Key: !secret lifehub_api_key
    content_type: "application/json"
    payload: '{"title": "{{ title }}", "category": "{{ category | default(''Home DIY'') }}"}'
```

Since 1.2.0 the shopping list already syncs with HA's Shopping List, so "add milk to the shopping
list" works through Assist without any of this. The REST commands are for scripts, NFC tags and
custom intents.

### Assist sentence for tasks

```yaml
# configuration.yaml
conversation:
  intents:
    LifeHubTask:
      - "remind me to {title}"
intent_script:
  LifeHubTask:
    action:
      - action: rest_command.lifehub_task
        data:
          title: "{{ title }}"
    speech:
      text: "Added {{ title }} to LifeHub"
```

### Bible

The iPhone Shortcuts setup for timing the YouVersion app is in [bible.md](bible.md#iphone-shortcuts).
To log a reading from an NFC tag on your Bible:

```yaml
rest_command:
  lifehub_bible_read:
    url: "http://192.168.1.10:8099/api/v1/bible/read"
    method: POST
    headers:
      X-Api-Key: !secret lifehub_api_key
    content_type: "application/json"
    payload: '{}'          # marks today's verse of the day as read
```

### Drop a note from Assist

```yaml
rest_command:
  lifehub_note:
    url: "http://192.168.1.10:8099/api/v1/notes"
    method: POST
    headers:
      X-Api-Key: !secret lifehub_api_key
    content_type: "application/json"
    payload: '{"text": {{ text | tojson }}}'
```

### What's for dinner (Assist)

```yaml
conversation:
  intents:
    LifeHubDinner:
      - "what's for dinner"
rest:
  - resource: http://192.168.1.10:8099/api/v1/meals?days=1
    headers:
      X-Api-Key: !secret lifehub_api_key
    scan_interval: 900
    sensor:
      - name: LifeHub dinner
        value_template: "{{ (value_json | selectattr('slot', 'eq', 'Dinner') | map(attribute='title') | first) | default('nothing planned') }}"
intent_script:
  LifeHubDinner:
    speech:
      text: "Tonight it's {{ states('sensor.lifehub_dinner') }}"
```

### Sensor from the summary

```yaml
rest:
  - resource: http://192.168.1.10:8099/api/v1/summary
    headers:
      X-Api-Key: !secret lifehub_api_key
    scan_interval: 300
    sensor:
      - name: LifeHub monthly spend
        value_template: "{{ value_json.monthlySpend }}"
```

LifeHub also publishes its own `sensor.lifehub_*` entities. See
[home-assistant.md](home-assistant.md#sensors-lifehub-publishes).
