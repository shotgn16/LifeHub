# REST API

LifeHub has a small REST API for Home Assistant automations, Assist, iOS Shortcuts, NFC tags and
hardware barcode scanners.

## Authentication

Create or copy the key in **Settings → Security & API**. Send it with every request, either way:

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
