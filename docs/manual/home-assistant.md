# Running LifeHub in Home Assistant

LifeHub is a Home Assistant **add-on** (an "app" in newer Home Assistant versions). It needs
**Home Assistant OS** or a **Supervised** install — not the plain Container or Core versions (use
[Docker](docker.md) for those).

## Install the add-on

[![Add repository to my Home Assistant](https://my.home-assistant.io/badges/supervisor_add_addon_repository.svg)](https://my.home-assistant.io/redirect/supervisor_add_addon_repository/?repository_url=https%3A%2F%2Fgithub.com%2Fshotgn16%2FLifeHub)

1. Press the button above (or in the LifeHub desktop app's setup assistant choose **Home Assistant
   → Open in Home Assistant**). It adds LifeHub to your add-on store.
   - By hand: **Settings → Add-ons → Add-on store → ⋮ → Repositories**, add
     `https://github.com/shotgn16/LifeHub`.
2. Find **LifeHub** in the store and press **Install** (a ready-made download — about a minute).
3. Turn on **Show in sidebar** and press **Start**.
4. Open **LifeHub** in the sidebar. Home Assistant has already signed you in, and LifeHub connects
   to Home Assistant by itself.

## Using it from the desktop app

In the LifeHub app's setup assistant choose **Home Assistant** and enter your Home Assistant
address (usually `http://homeassistant.local:8123`). The app then opens Home Assistant — LifeHub
is in the sidebar.

Prefer LifeHub on its own, without Home Assistant around it? In the add-on's **Configuration →
Network**, set a port for `8099/tcp` (e.g. 8099), then enter `http://homeassistant.local:8099` in
the assistant instead. You sign in with a LifeHub username and password (create one in LifeHub's
**Settings → People**).

## Phones

The LifeHub phone apps need that direct port too (they can't sign in to Home Assistant):
see [Phone apps](phone-apps.md). The Home Assistant Companion app shows LifeHub in its sidebar as
well.

## More

Everything LifeHub does with Home Assistant (rooms, shared shopping list, notifications,
sensors): [home-assistant.md](../home-assistant.md).
