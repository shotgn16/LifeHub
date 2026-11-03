# Problems?

## Installing

| Problem | Fix |
|---|---|
| Windows: "Windows protected your PC" | **More info → Run anyway**. LifeHub isn't signed with a paid certificate yet. |
| Mac: "LifeHub can't be opened" / "Apple could not verify…" | **System Settings → Privacy & Security → Open Anyway** (once). See [Mac](mac.md). |
| Linux: the window is blank or doesn't open | Install WebKitGTK: `sudo apt install libwebkit2gtk-4.1-0` (Fedora: `sudo dnf install webkit2gtk4.1`). |
| Windows: the window is blank | Install [Microsoft Edge WebView2](https://developer.microsoft.com/microsoft-edge/webview2/) (the installer normally does this). |

## Starting

| Problem | Fix |
|---|---|
| "LifeHub couldn't start" | Run the setup assistant (Windows: Start menu → **LifeHub setup assistant**; Mac: `open -a LifeHub --args --setup`; Linux: `lifehub --setup`) and choose again. Details are in `lifehub.log` in your data folder. |
| "The background service didn't start" | Choose the option again in the setup assistant; or pick **This computer + my phone** with *Keep LifeHub running* unticked (LifeHub then runs while the app is open). |
| "Port 8099 is already in use" | Something else uses that port. Choose another, e.g. 8100, in the setup assistant. |
| Docker: "LifeHub in Docker isn't answering" | Start Docker Desktop, wait until it says *running*, then open LifeHub again. |

## Phones

| Problem | Fix |
|---|---|
| The phone app can't reach LifeHub | Phone on the same Wi-Fi? Address exactly as shown in **Settings → This computer**? Allow LifeHub through the computer's firewall (Windows: *Private networks*; Linux: `sudo ufw allow 8099/tcp`). |
| It works at home but not outside | You need an https address: [Remote access](../remote-access.md). |
| The camera scanner doesn't start | It needs https. Use **Take photo** instead, or set up https. |
| iPhone app stopped opening after a week | Free Apple IDs last 7 days: install it again with Sideloadly (your settings are kept). |

## AI

| Problem | Fix |
|---|---|
| Ollama install failed | Install it from [ollama.com/download](https://ollama.com/download), then in Settings → AI press **List** to check LifeHub can see it. |
| The AI is very slow | Choose a smaller model in Settings → AI (e.g. `qwen2.5:3b`). |

## Your data

Your data folder: Windows `%LOCALAPPDATA%\LifeHub` · Mac `~/Library/Application Support/LifeHub` ·
Linux `~/.local/share/lifehub` · Docker the `lifehub-data` volume · Home Assistant inside the
add-on (included in Home Assistant's own backups). Restore a backup in **Settings → Backup**.

More fixes: [troubleshooting.md](../troubleshooting.md).
