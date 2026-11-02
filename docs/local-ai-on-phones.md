# Running AI models on your phones

There are two separate things you might want:

1. **A private AI chat on the phone itself**, offline and with nothing leaving the phone. This is
   easy on both phones.
2. **The phone acting as LifeHub's AI server**, instead of the Mac mini. This is possible, but
   it's an experiment: phones throttle when hot, sleep, and kill background apps. The Mac mini
   (Ollama) remains the right server for LifeHub. Use a phone as a backup or for trying things out.

Your phones:

- **Pixel 9 Pro XL**: Tensor G4 and 16 GB of RAM. It comfortably runs 2–4B models, and 7–8B models
  slowly.
- **iPhone Air**: A19 Pro and 12 GB of RAM, of which about 8 GB is usable for models. It runs 3–4B
  models well, and 7B models at 4-bit.

## 1. AI chat on the phone

### Pixel 9 Pro XL

| App | Why | Models to try |
|---|---|---|
| **Google AI Edge Gallery** (Play Store, free) | Google's own app. Chat, photos ("Ask Image"), audio transcription, PDF summaries, all offline. No account needed. | **Gemma 4 E4B** (best on your phone), E2B (faster) |
| **PocketPal AI** (Play Store, free, open source) | Runs any GGUF model from Hugging Face; good controls | Qwen 2.5/3 3–4B Instruct Q4_K_M, Llama 3.2 3B, Phi-4 mini |

Tips:

- Download models on Wi-Fi; they're 1.5–4 GB each.
- The phone warms up during long answers, which is normal. Plugging it in keeps it fast.

### iPhone Air

| App | Why | Models to try |
|---|---|---|
| **Locally AI** (App Store, free, from the LM Studio team) | Fast (Apple's MLX), many models, includes a **Shortcuts action**, and can use Apple's built-in model | Gemma 4 E4B, Qwen 3 4B, Llama 3.2 3B |
| **PocketPal AI** (App Store, free) | Any GGUF model | Qwen 2.5 3B / 7B (Q4_K_M), Phi-4 mini |
| **Google AI Edge Gallery** (App Store, free) | Gemma 4 with image and audio input | Gemma 4 E2B / E4B |
| **Apple Intelligence** (built in) | No download. Shortcuts has a **Use Model** action that runs Apple's on-device model. | Apple's ~3B on-device model |

### Using phone AI with LifeHub, without a server

The iPhone app's Shortcuts actions combine well with on-device models. For example, make a
Shortcut that:

1. receives text from the **share sheet** (a recipe web page, an email)
2. uses **Use Model → On-Device** with the prompt "Make a shopping list from this, one item per
   line"
3. repeats with each line → **Add to the shopping list (LifeHub)**

Or: **Dictate text** → **Use Model** ("Tidy this into a short note") → **Add a note (LifeHub)**.
Everything stays on the phone and your LifeHub.

## 2. A phone as LifeHub's AI server (experimental)

LifeHub talks to anything that speaks Ollama's API. Point **Settings → AI → Ollama URL** at the
phone's address on your Wi-Fi.

### Pixel 9 Pro XL: Ollama in Termux

1. Install **Termux** from [F-Droid](https://f-droid.org/packages/com.termux/), not the Play
   Store, whose version is outdated.
2. In Termux, run:

   ```bash
   pkg update && pkg upgrade
   pkg install ollama
   termux-wake-lock                       # keeps it running with the screen off
   OLLAMA_HOST=0.0.0.0:11434 ollama serve &
   ollama pull qwen2.5:3b                 # main model (about 2 GB)
   ollama pull qwen2.5vl:3b               # optional: photos & handwriting
   ```

3. Find the phone's IP address (**Settings → About phone → IP address**). Give it a fixed address
   in your router so it doesn't change.
4. In LifeHub, go to **Settings → AI** and set:
   - **URL** `http://<phone-ip>:11434`
   - **Model** `qwen2.5:3b`
   - **Vision model** `qwen2.5vl:3b`
   - **Timeout** 300 s
   - **Context** 4096

   Then press **Try it**.
5. Keep it alive:
   - **Settings → Apps → Termux → App battery usage → Unrestricted**
   - keep the phone on charge and on Wi-Fi

What to expect:

- Termux runs on the CPU only, at roughly 5–10 words a second for a 3B model.
- Email sorting, notes, meal ideas and starter notes work, just slowly.
- **Go deeper "In-depth"** and big meal plans are better left to the Mac mini.

### iPhone Air

iOS pauses apps in the background, so an iPhone can't be a dependable always-on server. If you
want to experiment anyway:

- **Local LLM Server Pro** (App Store, paid, iOS 26 with Apple Intelligence) serves Apple's
  on-device model with an **Ollama-compatible API** on your Wi-Fi.
- Turn on its **Keep Screen Awake** option, keep the app open and on charge, and set LifeHub's
  **Settings → AI → URL** to the address it shows.
- This hasn't been tested with LifeHub. Apple's small model may not follow LifeHub's structured
  answers reliably, so treat it as a toy.

For everyday use, keep **Settings → AI** pointing at the Mac mini:

| Purpose | Model on the Mac mini (8 GB) |
|---|---|
| Main | `qwen2.5:7b`, or `qwen3-vl:4b-instruct` if it should also see images |
| Vision | `qwen3-vl:4b-instruct` |

The phone apps use whatever LifeHub is set to; nothing changes on the phones.

## Privacy

All of the apps above run models **on the device**. Prompts aren't sent anywhere, unless you choose
a cloud option yourself, such as Shortcuts' "Use Model → ChatGPT" or "Private Cloud Compute".
