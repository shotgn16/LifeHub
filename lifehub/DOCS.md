# LifeHub

Open **LifeHub** from the Home Assistant sidebar. You're signed in automatically because Home
Assistant has already authenticated you.

Full documentation: <https://github.com/Shotgn16/LifeHub/tree/main/docs>

## First steps

1. **Home Assistant is set up automatically.** On first start LifeHub finds your people,
   calendars, phones, energy sensors and to-do lists. No token is needed. Review or change them in
   **Settings → Home Assistant**.
2. **Settings → AI.** Point LifeHub at your Ollama server (for example
   `http://192.168.1.50:11434`) and pick a model. `localhost` means the add-on container itself,
   so use the Ollama machine's IP.
3. **Settings → Email.** Add Gmail/IMAP, Microsoft 365 or Exchange mailboxes.
4. **Settings → Security & API** (optional). Set a LifeHub username and password if you also want
   to use the direct port (8099) or your own hostname, and copy the API key for `rest_command`s.

## Access from other devices

Under **Configuration → Network**, map `8099/tcp` to a host port. Then use
`http://<ha-ip>:8099`, or point a Cloudflare Tunnel or reverse proxy at it for HTTPS. Set
**Settings → General → LifeHub web address** so that tapping a phone notification opens LifeHub.

## Barcode scanner

Open **Scan** from the phone menu. The live camera only works on HTTPS pages: the HA app over
HTTPS (Nabu Casa or your own certificate), or your own HTTPS hostname. Over plain HTTP, use
**Take photo** instead.

## Sensors published to Home Assistant

These refresh every 5 minutes, and their attributes include the item lists:

- `sensor.lifehub_current_period`
- `sensor.lifehub_tasks_due`
- `sensor.lifehub_renewals_due`
- `sensor.lifehub_food_expiring`
- `sensor.lifehub_shopping_list`
- `sensor.lifehub_ai_pending`

## Data

Everything is stored in the add-on's `/data` folder, which is included in Home Assistant backups.
LifeHub also keeps 14 nightly zip backups (**Settings → Backup**).
