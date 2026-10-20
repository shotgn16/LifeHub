# Troubleshooting

## Installing

| Symptom | Fix |
|---|---|
| The add-on doesn't appear after adding the repository | Go to **Add-on store → ⋮ → Check for updates**, then refresh the browser. |
| The local add-on doesn't appear | The path must be `/addons/lifehub/config.yaml`, not `/addons/lifehub/lifehub/…`. Run `ha store reload`. |
| The build fails or takes a long time | The first build compiles .NET and needs about 2 GB of free RAM and internet access to `mcr.microsoft.com` and `nuget.org`. Check the Supervisor log. |
| **404: Not Found** from the HA sidebar | Update to 1.1.1 or later. |

## Access

| Symptom | Fix |
|---|---|
| A "Reconnecting…" banner, or a slow UI behind a proxy | WebSockets aren't getting through. Enable the WebSocket upgrade (nginx directives in [installation.md](installation.md)) or the IIS WebSocket Protocol feature. |
| Cloudflare shows **502 Bad Gateway** | Point the tunnel route at `http://<ha-ip>:8099` (not `localhost`), and make sure the port is mapped in the add-on's Network settings. |
| The hostname doesn't answer `ping` | That's normal behind Cloudflare. Test in a browser. |
| The scanner says the camera needs HTTPS | Open LifeHub over HTTPS (see [remote-access.md](remote-access.md)), or use **Take photo**. |
| The scanner script fails to load behind a proxy | Update to 1.1.2 or later. |
| Camera permission was refused | Go to the phone's settings → browser or HA app → Camera → Allow, then reload. |

## Home Assistant

| Symptom | Fix |
|---|---|
| The test fails with 401 | In the add-on, leave the token empty. Elsewhere, create a new long-lived token and check the URL. |
| No people, calendars or phones are listed | Press **Detect again** in Settings → Home Assistant. |
| Calendar events can't be created | Choose a writable calendar (Local Calendar, Google or CalDAV). |
| Notifications don't arrive | Tick the phones in Settings → Home Assistant. Check that the Companion app's `notify.mobile_app_*` service works from HA's Developer tools. |
| The shopping list doesn't sync | Choose the list in Settings → Home Assistant, then press **Sync now** on the Home page. |

## Email & AI

| Symptom | Fix |
|---|---|
| Ollama errors or timeouts | Check that `http://<ip>:11434/api/tags` opens from another machine. Set `OLLAMA_HOST=0.0.0.0`, use Ollama 0.5 or later, and raise the timeout for large models on CPU. |
| `localhost` doesn't work for Ollama | Inside the add-on, `localhost` is the container itself. Use the Ollama machine's IP. |
| Gmail "authentication failed" | Use an **App password**, and make sure IMAP is enabled in Gmail. |
| Microsoft sign-in fails with *AADSTS7000218* | Turn on **Allow public client flows** on the app registration. |
| Microsoft can't move emails to Deleted Items | Since 1.2.0 the app needs `Mail.ReadWrite`. Add the permission and sign in again. |
| Junk was added from old emails | Use **AI Inbox → History → Clean up**, and **Merge duplicates** on the Tasks page. |

## General

| Symptom | Fix |
|---|---|
| Times are wrong | Set the time zone in **Settings → General**. The container's `TZ` isn't used for scheduling. |
| Secrets are lost after a restore | `keys/` wasn't restored with `settings.json`. Enter the passwords and tokens again. |
| Logs | **HA:** the add-on's **Log** tab. **Docker:** `docker logs lifehub`. **IIS:** set `stdoutLogEnabled="true"` in `web.config`. |

Still stuck? [Open an issue](https://github.com/Shotgn16/LifeHub/issues) with the log output, with
any personal details removed.
