# LifeHub

[![Build](https://github.com/Shotgn16/LifeHub/actions/workflows/build.yml/badge.svg)](https://github.com/Shotgn16/LifeHub/actions/workflows/build.yml)
[![Release](https://img.shields.io/github/v/release/Shotgn16/LifeHub)](https://github.com/Shotgn16/LifeHub/releases)
[![Licence: MIT](https://img.shields.io/badge/licence-MIT-green.svg)](LICENSE)
![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4)
![Home Assistant add-on](https://img.shields.io/badge/Home%20Assistant-add--on-41BDF5)

LifeHub is a self-hosted personal organiser. It brings together things that usually live in a dozen
different places:

- **renewals and subscriptions**
- **tasks**
- **kitchen stock and shopping**
- your **schedule**
- **Home Assistant**
- a **local-AI email assistant**, powered by [Ollama](https://ollama.com), that turns emails into
  tasks, calendar events, renewal updates and shopping items without your mail leaving the house

It's written in **C# / ASP.NET Core 8 + Blazor Server**, with no JavaScript framework. Data is
stored as JSON files in one folder. It ships as one Docker image that runs as a **Home Assistant
add-on**, on **Plesk Obsidian**, or anywhere Docker or .NET 8 runs.

<p align="center">
  <img src="docs/images/dashboard.png" alt="LifeHub dashboard" width="72%">
  &nbsp;
  <img src="docs/images/scanner.png" alt="Barcode scanner on a phone" width="22%">
</p>

---

## Features

| Area | What it does |
|---|---|
| **Today** | Shows the current period (work, free or busy), tasks due, your agenda, upcoming renewals, food to use up, and who's home. |
| **Tasks** | Categories, priorities, recurrence, checklists and tags. Repeat issues stack onto one task with a ×N count. |
| **Schedule** | A weekly template of work and free time. Calendar events and holidays override it. **Plan my day** slots tasks into free gaps, and runs automatically every evening. |
| **Renewals** | Domains, hosting, subscriptions, insurance and utilities: cost, cycle, usage, auto-renew. It records price-rise history and reminds you before each date. |
| **Kitchen** | Fridge, freezer and cupboard stock with use-by dates, freeze and defrost, and waste tracking. **Staples** have minimum stock, and LifeHub predicts when each one will run out. |
| **Scan** | Phone barcode scanner with three modes: stock in, use up, and add to the shopping list. It looks products up on Open Food Facts, learns them after the first scan, and lets you undo. |
| **Shopping** | Grouped by aisle. Items come from low stock, expiry, predictions or AI. Ticked items go into stock when you put them away. Two-way sync with HA's Shopping List, so Assist voice works. |
| **Home** | Home Assistant runs the house; LifeHub gives you one place to see and nudge it. Rooms, lights, climate, scenes, who's home and energy, all shown with friendly names. Setup is automatic. |
| **AI Inbox** | Reads Gmail/IMAP, Microsoft 365 and on-prem Exchange. A local Ollama model classifies each email and proposes actions. Safe actions are applied automatically and can be undone. |
| **Promotions** | Adverts and newsletters go into their own list instead of becoming tasks or shopping items, and clear themselves when they expire. |
| **Email ↔ task link** | Delete a task and its email moves to the Bin. Delete the email and its task goes. |
| **Notifications** | In-app bell plus phone push through HA: a morning digest, renewal and task reminders, food expiry, and a "you've left home" shopping list. |
| **REST API** | API-key endpoints for HA automations, Assist, iOS Shortcuts and USB barcode scanners. |

## Quick start

### Home Assistant OS (recommended)

[![Add repository to my Home Assistant](https://my.home-assistant.io/badges/supervisor_add_addon_repository.svg)](https://my.home-assistant.io/redirect/supervisor_add_addon_repository/?repository_url=https%3A%2F%2Fgithub.com%2FShotgn16%2FLifeHub)

1. **Settings → Add-ons → Add-on store → ⋮ → Repositories**, then add `https://github.com/Shotgn16/LifeHub`.
2. Find **LifeHub**, then **Install** (the first build takes a few minutes) and **Start**. Turn on **Show in sidebar**.
3. Open LifeHub from the sidebar. Home Assistant is detected and configured automatically.

### Docker

```bash
git clone https://github.com/Shotgn16/LifeHub.git && cd LifeHub
docker compose up -d --build        # then open http://localhost:8099 and create your login
```

For Plesk, IIS, Cloudflare Tunnel and development setups, see the **[installation guide](docs/installation.md)**.

## Documentation

| Guide | |
|---|---|
| [Installation](docs/installation.md) | Home Assistant, Plesk (Docker, .NET Toolkit, IIS), Docker, development |
| [Remote access & HTTPS](docs/remote-access.md) | Direct port, Cloudflare Tunnel, reverse proxies, HTTPS for the camera |
| [Home Assistant](docs/home-assistant.md) | What's automatic, rooms, shared lists, notifications, sensors, automations |
| [Email & AI](docs/email-and-ai.md) | Ollama, Gmail/IMAP, Microsoft 365, Exchange, classification, auto-apply and undo |
| [Barcode scanner](docs/scanner.md) | Phone camera, photo, USB scanners, product learning |
| [REST API](docs/api.md) | Endpoints and Home Assistant `rest_command` examples |
| [Architecture](docs/architecture.md) | How the code is organised |
| [Extending LifeHub](docs/extending.md) | New AI actions, mail providers, pages, storage |
| [Data, backups & security](docs/security.md) | Where data lives, encryption, backups |
| [Troubleshooting](docs/troubleshooting.md) | Common problems and fixes |
| [Changelog](CHANGELOG.md) | Version history |

## Project layout

```
.
├── repository.yaml          # makes this repo a Home Assistant add-on repository
├── lifehub/                 # the add-on (also the Docker build context)
│   ├── config.yaml          # add-on manifest (version, ingress, ports)
│   ├── Dockerfile
│   ├── DOCS.md, CHANGELOG.md, icon.png, logo.png
│   └── src/LifeHub/         # the ASP.NET Core / Blazor app
├── docs/                    # documentation
├── tools/                   # publish scripts for Plesk/IIS/Linux
├── docker-compose.yml
└── LifeHub.sln
```

## Requirements

- **To run:** Docker, or the ASP.NET Core 8 runtime. The add-on supports amd64 and aarch64.
- **For AI:** an [Ollama](https://ollama.com) server (0.5 or later) on your network. A 7–8B model
  such as `qwen2.5:7b` works well.
- **To build:** the .NET 8 SDK.

## Contributing

Issues and pull requests are welcome. See [CONTRIBUTING.md](CONTRIBUTING.md).

## Licence

[MIT](LICENSE). Bundled third-party components are listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
