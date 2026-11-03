# A LifeHub for the whole household

Run LifeHub on one computer that stays on, and everyone uses it — in a browser or the phone app,
each with their own account.

## 1. Set up the server

On the computer that will host LifeHub (a desktop that stays on, a mini PC, a Mac mini, a
Linux box…):

1. Install LifeHub: [Windows](windows.md) · [Mac](mac.md) · [Linux](linux.md). (Or use
   [Docker](docker.md) or [Home Assistant](home-assistant.md) — they're household servers too.)
2. In the setup assistant choose **A server for my household** and **When the computer starts**.
3. Stop the computer from sleeping:
   - Windows: **Settings → System → Power → Screen and sleep → When plugged in, put my device to
     sleep after: Never**.
   - Mac: **System Settings → Energy → Prevent automatic sleeping when the display is off**.
   - Linux: your desktop's power settings → **Automatic suspend: Off**.
4. Give the computer a fixed address on your network, so phones always find it: in your router's
   settings look for **DHCP reservation** (or "static IP") and reserve its current address.

## 2. Add everyone

**Settings → People → Add person** (you need to be an admin):

- Name and colour.
- A **username and password**, so they can sign in from a browser.
- Tick **Admin** only for people who should change household settings.

Each person then signs in once in a browser and finds their **API key** in **Settings → Security
& API**, for their [phone app](phone-apps.md).

## 3. Choose what's shared

**Settings → People → What's shared**: the food stock and shopping list, meal planning, and
renewals can be one shared list or separate per person. Tasks, email, notes and notifications are
always each person's own (a note can be shared one at a time).

More: [people.md](../people.md).

## From outside the house

At home, phones use `http://<server-address>:8099`. To use LifeHub when you're out, and for the
phone camera scanner, give it an https address — a free Cloudflare Tunnel is the easiest. See
[Remote access](../remote-access.md).
