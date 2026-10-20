# Home Assistant

The two apps have separate jobs:

- **Home Assistant runs the house.** Devices, automations, voice and presence all stay in HA.
- **LifeHub is the organiser on top.** It reads HA to show what's going on, sends phone
  notifications through HA, and shares its lists with HA so that both apps work on the same data.

## What's set up automatically

As an add-on, LifeHub talks to HA through the Supervisor API, so no token is needed. On first
start it detects:

| Thing | Used for |
|---|---|
| `person.*` entities | Who's home, and the "you've left home" shopping reminder |
| Calendars | Your schedule. It also picks one writable calendar for new events and the daily plan. |
| `notify.mobile_app_*` phones | Push notifications. Untick any you don't want in **Settings → Home Assistant**. |
| Power and energy sensors | Energy tiles and 24-hour charts |
| `todo.shopping_list` and other to-do lists | Two-way sync of the shopping list (and optionally tasks) |

You can review or change any of these in **Settings → Home Assistant**. Running outside HA (Docker
or Plesk)? Enter the HA URL and a **long-lived access token** there instead. To create a token, go
to your HA profile → **Security** → **Long-lived access tokens**.

## The Home page

- **Rooms** come from HA areas. Each shows lights, switches, fans, covers, climate and
  temperature/humidity readings, using HA's friendly names.
- **Scenes and scripts** appear as one-tap buttons.
- **Open doors and windows** show as an alert.
- **Who's home** lists each person and how long they've been there or away.
- **Energy** shows current power and 24-hour charts.
- **Customise** hides tiles you don't care about. Hidden entities are only hidden in LifeHub.

## Shared lists

- **Shopping list ↔ HA Shopping List.** Items added with Assist ("add milk to the shopping list"),
  the HA app or LifeHub appear in both places, and ticking an item off in either app ticks it off
  in both. LifeHub sends its changes straight away and checks HA for changes every minute, or press **Sync now** on the Home page.
- **Tasks ↔ an HA to-do list** is optional. Pick the list in **Settings → Home Assistant**.

## Notifications

LifeHub sends push notifications through the HA Companion app to the phones you tick:

- a **morning digest** with sections for today, tasks due, renewals, food to use up and things to buy
- **renewal reminders**: within the warning window, the day before, and on the day
- **task reminders**, 15 minutes before a timed task
- **food expiry**
- **while you're out**: when someone leaves home and the shopping list isn't empty, that person's
  phone gets the list
- the **evening plan** for tomorrow

Notifications of the same kind replace each other instead of stacking up. Tapping one opens
LifeHub: at the **LifeHub web address** if one is set in Settings → General, otherwise at the HA
ingress panel.

## Sensors LifeHub publishes

These update every 5 minutes. Their attributes contain the item lists.

| Entity | State | Attributes |
|---|---|---|
| `sensor.lifehub_current_period` | Work / Free / Sleep / … | `label`, `until`, `next`, `next_start`, `in_event`, `event` |
| `sensor.lifehub_tasks_due` | count | `tasks`, `overdue` |
| `sensor.lifehub_renewals_due` | count | `items`, `monthly_total` |
| `sensor.lifehub_food_expiring` | count | `items` |
| `sensor.lifehub_shopping_list` | count | `items` |
| `sensor.lifehub_ai_pending` | count | |

### Example automations

Remind me about food when I get home:

```yaml
automation:
  - alias: Use-up reminder when I get home
    triggers:
      - trigger: state
        entity_id: person.me
        to: home
    conditions:
      - condition: template
        value_template: "{{ states('sensor.lifehub_food_expiring') | int > 0 }}"
    actions:
      - action: notify.mobile_app_my_phone
        data:
          title: "Use up soon"
          message: "{{ state_attr('sensor.lifehub_food_expiring', 'items') | join('\n') }}"
```

Keep the office quiet during meetings:

```yaml
automation:
  - alias: Do not disturb in meetings
    triggers:
      - trigger: state
        entity_id: sensor.lifehub_current_period
        attribute: in_event
        to: true
    actions:
      - action: light.turn_on
        target: { entity_id: light.office_door_sign }
        data: { color_name: red }
```

To add tasks or shopping items from HA (scripts, Assist, NFC tags), see the [REST API](api.md).

## Troubleshooting

- **"404: Not Found" from the sidebar.** Update to 1.1.1 or later.
- **No people, calendars or phones listed.** Press **Detect again** in Settings → Home Assistant. In Docker or Plesk, check the URL and token.
- **Events can't be created.** The target calendar must support creating events (Local Calendar,
  Google or CalDAV). Read-only ICS calendars can't take events.
