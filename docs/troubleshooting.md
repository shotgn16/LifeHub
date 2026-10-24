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
| The AI never answers / "Waiting for the AI" goes on for minutes | Update LifeHub to 1.3.1 or later: "thinking" models such as qwen3-vl now answer straight away. Leave *Let thinking models reason first* off in Settings → AI on small machines. The first request after a while also loads the model (up to a minute). |
| Ollama errors or timeouts | Check that `http://<ip>:11434/api/tags` opens from another machine. Set `OLLAMA_HOST=0.0.0.0`, use Ollama 0.5 or later, and raise the timeout for large models on CPU. |
| `localhost` doesn't work for Ollama | Inside the add-on, `localhost` is the container itself. Use the Ollama machine's IP. |
| Gmail "authentication failed" | Use an **App password**, and make sure IMAP is enabled in Gmail. |
| Microsoft sign-in fails with *AADSTS7000218* | Turn on **Allow public client flows** on the app registration. |
| Microsoft can't move emails to Deleted Items | Since 1.2.0 the app needs `Mail.ReadWrite`. Add the permission and sign in again. |
| Junk was added from old emails | Use **AI Inbox → History → Clean up**, and **Merge duplicates** on the Tasks page. |

## Bible, kitchen photos & meals

| Symptom | Fix |
|---|---|
| No verse of the day | Add a YouVersion app key in Settings → Bible, or type the verse on the Bible page. |
| Verse shows without text | That Bible version isn't licensed for apps like LifeHub. Try BSB (3034) or KJV (1). |
| App time not counted (Android) | Turn on the Companion app's **Last used app** sensor and tick it in Settings → Bible. Set *Sensor update frequency* to **Fast always**. |
| App time not counted (iPhone) | Check the Shortcuts automations are set to **Run Immediately** and the URL and API key are right. Your phone must reach LifeHub at that address. |
| Handwriting not read / "model can't see images" | Set a **vision model** in Settings → AI (`ollama pull qwen3-vl:4b` on 8 GB machines, `qwen3-vl:8b` with 16 GB or more). |
| "Biblia Graphia: page didn't contain readable text" | That site builds its pages with JavaScript. Use the link in **Read more**, or turn it off in Settings → Bible. |
| Photo finds no food | Get closer, use good light, and spread items out with labels facing the camera. One bag per photo. |
| Dates read wrongly | Check the day/month order follows Settings → General → Culture. You can always correct the date before adding. |
| Meal suggestions are odd | Add your requirements and preferences on Meals → Dietary needs. Bigger models give better plans. |

## Notifications

| Symptom | Fix |
|---|---|
| "Turn on for this device" missing | Open LifeHub over HTTPS. On iPhone, add it to the home screen first and open it from there. |
| "Blocked in browser settings" | Allow notifications for the LifeHub site in the browser or phone settings, then try again. |
| Device shows "error" | Remove it and turn it on again on that device. Expired devices are removed automatically. |

## General

| Symptom | Fix |
|---|---|
| Times are wrong | Set the time zone in **Settings → General**. The container's `TZ` isn't used for scheduling. |
| Secrets are lost after a restore | `keys/` wasn't restored with `settings.json`. Enter the passwords and tokens again. |
| Logs | **HA:** the add-on's **Log** tab. **Docker:** `docker logs lifehub`. **IIS:** set `stdoutLogEnabled="true"` in `web.config`. |

Still stuck? [Open an issue](https://github.com/Shotgn16/LifeHub/issues) with the log output, with
any personal details removed.
