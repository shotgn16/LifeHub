# Installation

> **Looking for the easy, step-by-step version?** See the [manual](manual/README.md). This page
> covers every option in more depth, including servers and building LifeHub yourself.

LifeHub is one ASP.NET Core 8 app, so it runs anywhere .NET 8 or Docker runs. Pick one:

- [Desktop apps (Windows, macOS, Linux)](#desktop-apps-windows-macos-linux). Installers with
  everything included and a setup assistant; also the easiest way to run a household server or
  set up Docker.
- [Home Assistant OS (add-on)](#home-assistant-os-add-on). The least setup if you already run
  Home Assistant.
- [Plesk Obsidian](#plesk-obsidian): Docker, .NET Toolkit or Windows/IIS.
- [Plain Docker](#plain-docker).
- [Portable server build](#portable-server-build) (any OS with .NET 8, no Docker).
- [Development](#development) and [building the installers](#building-the-installers).

Whichever you choose, LifeHub stores **all** of its data in one folder: database files, settings,
encryption keys and backups. See [Data, backups & security](security.md).

---

## Desktop apps (Windows, macOS, Linux)

Download from the [Releases page](https://github.com/shotgn16/LifeHub/releases/latest):

| System | File | Notes |
|---|---|---|
| Windows 10 (1809+) / 11, x64 | `LifeHub-<v>-Windows-x64-Setup.exe` | Per-user install (no admin). Adds WebView2 if missing. Start-menu entries for LifeHub and the setup assistant. |
| macOS 12+ Apple Silicon | `LifeHub-<v>-macOS-AppleSilicon.dmg` | Drag to Applications. Unsigned: allow once in Privacy & Security. |
| macOS 12+ Intel | `LifeHub-<v>-macOS-Intel.dmg` | as above |
| Debian/Ubuntu family, amd64/arm64 | `LifeHub-<v>-Linux-<arch>.deb` | Installs to `/opt/lifehub`, pulls in WebKitGTK. |
| Other Linux, amd64/arm64 | `LifeHub-<v>-Linux-<arch>.tar.gz` | `./install.sh` installs for your user. |

Each one contains LifeHub with its own .NET runtime. On first start, the **setup assistant** asks how
LifeHub should run:

| Mode | What happens | Listens on |
|---|---|---|
| This computer only | LifeHub runs inside the app while it's open | `127.0.0.1` (a free port) |
| This computer + my phone | A background service starts when you sign in (or, unticked, LifeHub runs while the app is open) | `0.0.0.0:<port>` |
| A server for my household | A background service that starts with the computer (or when you sign in) | `0.0.0.0:<port>` |
| Docker | Pulls `ghcr.io/shotgn16/lifehub`, runs container `lifehub` with volume `lifehub-data`; the app shows it | Docker's port mapping |
| Home Assistant / somewhere else | The app is a window onto that address | — |

The background service is the same program started with `--server`:

| | "When I sign in" | "When the computer starts" |
|---|---|---|
| Windows | Scheduled task `LifeHub` (at log on) | Windows service `LifeHub` + firewall rule (asks for admin once) |
| macOS | LaunchAgent `~/Library/LaunchAgents/io.github.shotgn16.lifehub.plist` | LaunchDaemon in `/Library/LaunchDaemons` running as you (asks for your password) |
| Linux | systemd user service `~/.config/systemd/user/lifehub.service` | the same, plus `loginctl enable-linger` (asks for your password) |

Command line (the installed program is `LifeHub.exe`, `LifeHub.app/Contents/MacOS/LifeHub` or
`lifehub`):

```
LifeHub                        open the app (first run: the setup assistant)
LifeHub --setup                open the setup assistant
LifeHub --server               run without a window (what the service runs)
LifeHub --uninstall-service    remove the background service
  --data <folder>   --port <n>
```

Data folders: `%LOCALAPPDATA%\LifeHub`, `~/Library/Application Support/LifeHub`,
`~/.local/share/lifehub` (or `LIFEHUB_DATA` / `--data`). How LifeHub runs is saved in
`desktop.json` there. The app window signs you in with a secret in `keys/desktop.token` that only
works from the same computer.

---

## Home Assistant OS (add-on)

This works on HA OS and HA Supervised installs, including HA OS running as a VM on ESXi, Proxmox
or Hyper-V.

### From this GitHub repository (recommended)

[![Add repository to my Home Assistant](https://my.home-assistant.io/badges/supervisor_add_addon_repository.svg)](https://my.home-assistant.io/redirect/supervisor_add_addon_repository/?repository_url=https%3A%2F%2Fgithub.com%2FShotgn16%2FLifeHub)

1. In Home Assistant go to **Settings → Add-ons → Add-on store**.
2. Open the **⋮** menu (top right), then **Repositories**. Add
   `https://github.com/shotgn16/LifeHub` and close the dialog.
3. Scroll to **LifeHub add-ons**, open **LifeHub** and press **Install**. HA downloads a ready-made
   image for your machine (`ghcr.io/shotgn16/lifehub-amd64` or `-aarch64`), which takes a minute
   or two. Nothing is compiled on your HA machine.
4. Press **Start**, and turn on **Show in sidebar**.
5. Open **LifeHub** from the sidebar. Home Assistant has already signed you in, and LifeHub sets up
   the HA connection automatically.

**Updates.** When a new version is published here, HA shows an update for the add-on and downloads
the new image. Your data is kept across updates.

### As a local add-on (no GitHub)

Use this if you want to change the code and run your own copy.

1. Copy the [`lifehub/`](../lifehub) folder from this repository to **`/addons/lifehub`** on the HA
   host. You can do this through the *Samba share* add-on (`\\homeassistant\addons`) or the
   *Advanced SSH & Web Terminal* add-on.
   - The result must be `/addons/lifehub/config.yaml`. A nested `/addons/lifehub/lifehub/` folder
     won't work.
2. Go to **Settings → Add-ons → Add-on store → ⋮ → Check for updates** (or run `ha store reload`
   over SSH).
3. **Delete the `image:` line from `config.yaml`.** Otherwise HA downloads the published image
   instead of building your copy.
4. LifeHub now appears under **Local add-ons**. Install and start it as in the steps above. A
   local copy is compiled on the HA machine, which needs about 2 GB of free RAM and 5–15 minutes.

After changing the code, bump `version:` in `config.yaml` and HA offers it as an update. You can
also use **Rebuild** on the add-on page.

### After installing

- **Direct access (optional).** On the add-on's **Configuration → Network** section, set a host
  port for `8099/tcp`. This lets phones and PCs, and the [phone apps](mobile-apps.md), reach
  LifeHub without going through HA. Give yourself a username and password first under
  **Settings → Security & API**; each person has their own (see [People](people.md)). See
  [Remote access](remote-access.md).
- **Ollama.** HA OS isn't a good place to run an LLM. Run Ollama on another VM or PC, then enter
  its address in **Settings → AI**. See [Email & AI](email-and-ai.md). For the photo features
  (groceries, handwriting), also `ollama pull qwen3-vl:4b-instruct` and set it as the vision model.

---

## Plesk Obsidian

### Option A: Plesk on Linux with the Docker extension (recommended)

1. Install **Extensions → Docker**.
2. Build the image on the server over SSH:
   ```bash
   git clone https://github.com/shotgn16/LifeHub.git /root/LifeHub
   cd /root/LifeHub && docker build -t lifehub:latest ./lifehub
   ```
3. In **Docker → Images**, run `lifehub:latest` with these settings:
   - **Manual port mapping:** container `8099` → host `8099` (untick *automatic*).
   - **Volume:** host `/var/lib/lifehub` → container `/data`.
   - **Environment:** `TZ=Europe/London` (or your zone).
   - **Restart policy:** *always*.
4. Go to **Websites & Domains → your (sub)domain → Docker Proxy Rules → Add rule**. Set URL `/` →
   container `lifehub`, port `8099`. Turn on **Let's Encrypt** for the domain.
5. Blazor needs WebSockets. If you see "Reconnecting…", replace the proxy rule with these
   directives under **Apache & nginx Settings → Additional nginx directives**:
   ```nginx
   location / {
       proxy_pass http://127.0.0.1:8099;
       proxy_http_version 1.1;
       proxy_set_header Upgrade $http_upgrade;
       proxy_set_header Connection "upgrade";
       proxy_set_header Host $host;
       proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
       proxy_set_header X-Forwarded-Proto $scheme;
       proxy_read_timeout 3600s;
   }
   ```
6. Browse to the domain and create your account (the first person, an admin) on the first-run
   screen. Add the rest of the household in **Settings → People**.

`docker compose up -d --build` with the included [`docker-compose.yml`](../docker-compose.yml) also
works.

### Option B: Plesk on Linux with the .NET Toolkit

1. Install **Extensions → .NET Toolkit**, then install the **ASP.NET Core 8** runtime from it.
2. On your PC, run `tools/publish.sh`, or
   `dotnet publish lifehub/src/LifeHub/LifeHub.csproj -c Release -o publish`. Zip the `publish/`
   folder.
3. Create a .NET site, then upload and extract the zip into `httpdocs`. In the domain's
   **.NET Core** settings:
   - Set **Application startup file** to `LifeHub.dll`.
   - Add `LIFEHUB_DATA=/var/www/vhosts/<domain>/lifehub-data`. Keep this outside `httpdocs`.
4. Add the WebSocket directives from Option A if needed.

### Option C: Plesk on Windows (IIS)

1. Install the **ASP.NET Core 8 Hosting Bundle** and the IIS **WebSocket Protocol** feature on the
   server.
2. Run `tools/publish-iis.ps1` and upload the contents of `publish/` to `httpdocs`.
3. Keep data outside the web root. Add this inside `<aspNetCore …>` in `web.config`:
   ```xml
   <environmentVariables>
     <environmentVariable name="LIFEHUB_DATA" value="C:\Inetpub\vhosts\example.com\lifehub-data" />
   </environmentVariables>
   ```
   Then give the application-pool user *Modify* permission on that folder.
4. Browse to the site and create your account (the first person, an admin).

---

## Plain Docker

The ready-made image (amd64 and arm64):

```bash
docker run -d --name lifehub --restart unless-stopped -p 8099:8099 \
  -v lifehub-data:/data -e TZ=Europe/London ghcr.io/shotgn16/lifehub:latest
```

Or build it yourself with Compose:

```bash
git clone https://github.com/shotgn16/LifeHub.git && cd LifeHub
docker compose up -d --build
```

Open `http://<host>:8099`. With Compose, data is kept in `./data`. To build the image without Compose:

```bash
docker build -t lifehub ./lifehub
docker run -d --name lifehub -p 8099:8099 -v lifehub-data:/data -e TZ=Europe/London lifehub
```

---

## Portable server build

For a server without Docker (any OS with the ASP.NET Core 8 runtime): download
`LifeHub-<v>-server-portable.zip` from the release, unzip it and run

```bash
LIFEHUB_DATA=/srv/lifehub ASPNETCORE_URLS=http://0.0.0.0:8099 dotnet LifeHub.dll
```

Run it as a service with your system's tools (systemd, NSSM, launchd). `dotnet LifeHub.dll --server
--data <folder> --port 8099` uses the desktop layout instead (data in your user folder by default).

---

## Development

You need the **.NET 8 SDK**, plus Visual Studio 2022, Rider or VS Code with the C# Dev Kit.

```bash
cd lifehub/src/LifeHub
dotnet run                     # http://localhost:5080, data in lifehub/src/LifeHub/data
```

Or open `LifeHub.sln` in Visual Studio.

LifeHub looks for its data folder in this order:

1. the `LIFEHUB_DATA` environment variable
2. `LifeHub:DataPath` in `appsettings.json`
3. `/data` when running as an HA add-on
4. `./data`

To test HA, Ollama and email without the real services, point the settings at your own instances.
Every integration can be enabled or disabled on its own.

## Building the installers

The **Desktop apps** workflow (`.github/workflows/desktop.yml`) builds everything on GitHub's
Windows, macOS and Linux machines. To build one yourself:

```bash
# any OS — the desktop app for one platform (win-x64, osx-arm64, osx-x64, linux-x64, linux-arm64)
dotnet publish lifehub/src/LifeHub/LifeHub.csproj -c Release -r linux-x64 --self-contained \
  -p:LifeHubDesktop=true -o out/linux-x64

# then package it
bash installer/linux/build-deb.sh out/linux-x64 3.0.0 amd64 dist              # Linux .deb + .tar.gz
bash installer/macos/make-app.sh out/osx-arm64 3.0.0 arm64 dist              # macOS .app + .dmg (on a Mac)
iscc /DAppVersion=3.0.0 /DSourceDir=%CD%\out\win-x64 installer\windows\LifeHub.iss   # Windows (Inno Setup 6)
```

`-p:LifeHubDesktop=true` adds the app window (Photino.NET) and Windows-service support; without it
you get the normal server build. The Windows script expects `MicrosoftEdgeWebview2Setup.exe` next
to it ([download](https://go.microsoft.com/fwlink/p/?LinkId=2124703)).

**Signing (later).** The installers are unsigned. To sign them: Windows — add `SignTool=` to the
Inno script with your code-signing certificate; macOS — replace the ad-hoc `codesign --sign -` in
`make-app.sh` with your *Developer ID Application* identity plus `--options runtime`, then
`xcrun notarytool submit … --wait` and `xcrun stapler staple` on the `.dmg`.
