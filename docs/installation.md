# Installation

LifeHub is one ASP.NET Core 8 app, so it runs anywhere .NET 8 or Docker runs. Pick one:

- [Home Assistant OS (add-on)](#home-assistant-os-add-on). This is the recommended option and
  needs the least setup.
- [Plesk Obsidian](#plesk-obsidian): Docker, .NET Toolkit or Windows/IIS.
- [Plain Docker](#plain-docker).
- [Development](#development).

Whichever you choose, LifeHub stores **all** of its data in one folder: database files, settings,
encryption keys and backups. See [Data, backups & security](security.md).

---

## Home Assistant OS (add-on)

This works on HA OS and HA Supervised installs, including HA OS running as a VM on ESXi, Proxmox
or Hyper-V.

### From this GitHub repository (recommended)

[![Add repository to my Home Assistant](https://my.home-assistant.io/badges/supervisor_add_addon_repository.svg)](https://my.home-assistant.io/redirect/supervisor_add_addon_repository/?repository_url=https%3A%2F%2Fgithub.com%2FShotgn16%2FLifeHub)

1. In Home Assistant go to **Settings → Add-ons → Add-on store**.
2. Open the **⋮** menu (top right), then **Repositories**. Add
   `https://github.com/Shotgn16/LifeHub` and close the dialog.
3. Scroll to **LifeHub add-ons**, open **LifeHub** and press **Install**. HA builds the image on
   your machine, which takes about 3–10 minutes the first time.
4. Press **Start**, and turn on **Show in sidebar**.
5. Open **LifeHub** from the sidebar. Home Assistant has already signed you in, and LifeHub sets up
   the HA connection automatically.

**Updates.** When a new version is published here, HA shows an update for the add-on. Your data is
kept across updates.

### As a local add-on (no GitHub)

Use this if you want to change the code and run your own copy.

1. Copy the [`lifehub/`](../lifehub) folder from this repository to **`/addons/lifehub`** on the HA
   host. You can do this through the *Samba share* add-on (`\\homeassistant\addons`) or the
   *Advanced SSH & Web Terminal* add-on.
   - The result must be `/addons/lifehub/config.yaml`. A nested `/addons/lifehub/lifehub/` folder
     won't work.
2. Go to **Settings → Add-ons → Add-on store → ⋮ → Check for updates** (or run `ha store reload`
   over SSH).
3. LifeHub now appears under **Local add-ons**. Install and start it as in the steps above.

After changing the code, bump `version:` in `config.yaml` and HA offers it as an update. You can
also use **Rebuild** on the add-on page.

### After installing

- **Direct access (optional).** On the add-on's **Configuration → Network** section, set a host
  port for `8099/tcp`. This lets phones and PCs reach LifeHub without going through HA. Set a
  LifeHub login first under **Settings → Security**. See [Remote access](remote-access.md).
- **Ollama.** HA OS isn't a good place to run an LLM. Run Ollama on another VM or PC, then enter
  its address in **Settings → AI**. See [Email & AI](email-and-ai.md). For the photo features
  (study notes, groceries), also `ollama pull qwen3-vl:4b-instruct` and set it as the vision model.

---

## Plesk Obsidian

### Option A: Plesk on Linux with the Docker extension (recommended)

1. Install **Extensions → Docker**.
2. Build the image on the server over SSH:
   ```bash
   git clone https://github.com/Shotgn16/LifeHub.git /root/LifeHub
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
6. Browse to the domain and create your login on the first-run screen.

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
4. Browse to the site and create your login.

---

## Plain Docker

```bash
git clone https://github.com/Shotgn16/LifeHub.git && cd LifeHub
docker compose up -d --build
```

Open `http://<host>:8099`. Data is kept in `./data`. To build the image without Compose:

```bash
docker build -t lifehub ./lifehub
docker run -d --name lifehub -p 8099:8099 -v lifehub-data:/data -e TZ=Europe/London lifehub
```

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
