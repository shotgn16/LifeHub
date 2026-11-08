# The AI assistant

LifeHub's AI is **optional**. It sorts notes, reads your email into tasks, plans meals and reads photos of
groceries. Anything the AI writes is shown in **purple**, and it never changes your own words.

## Choose who runs it

**Settings → AI → Which AI**:

| | Private? | Cost | Good for |
|---|---|---|---|
| **Ollama** | Yes — on your computer or network | Free | Desktop and server LifeHub; the setup assistant can install it |
| **On this device** | Yes — inside the phone app, offline | Free | The phone app on its own (needs a recent phone) |
| **ChatGPT (OpenAI)** | No — text goes to OpenAI | Pay-as-you-go API key | Best results with no hardware |
| **Google Gemini** | No — text goes to Google | API key (free tier available) | Fast and cheap |
| **Claude (Anthropic)** | No — text goes to Anthropic | Pay-as-you-go API key | Careful, accurate answers |
| **OpenAI-compatible server** | Depends on the server | — | LM Studio, llama.cpp, vLLM, OpenRouter, Groq… |

With ChatGPT, Gemini or Claude, the emails, notes and lists LifeHub asks about are sent to that company to be
answered. Your API key is stored encrypted.

## Ollama (private, on your computer)

**From the desktop app:** the setup assistant checks for Ollama. If it isn't installed, press **Yes, install
Ollama** and LifeHub installs it and downloads a model that suits your computer's memory:

- 16 GB or more: `qwen2.5:7b` (about 4.7 GB) — the best results
- 8 GB: `qwen2.5:3b` (about 1.9 GB)
- less: `qwen2.5:1.5b` (about 1 GB)

Say **No thanks** and set it up later here.

**Yourself:** install [Ollama](https://ollama.com/download), run `ollama pull qwen2.5:7b`, then in **Settings → AI**
choose **Ollama**, set the address (`http://localhost:11434`, or `http://<that-computer>:11434` — start Ollama there
with `OLLAMA_HOST=0.0.0.0`), press **List**, pick the model and **Save AI settings**.

Photos need a vision model, e.g. `ollama pull qwen3-vl:4b-instruct`, set as **Vision model**.

## ChatGPT, Gemini or Claude

1. Get an API key: [OpenAI](https://platform.openai.com/api-keys) ·
   [Google AI Studio](https://aistudio.google.com/apikey) · [Anthropic](https://console.anthropic.com/settings/keys).
2. **Settings → AI**: choose the service, paste the key, press **List** and pick a model (a good default is filled
   in), then **Save AI settings**. Try it with **Analyse** further down the page.

Their models read photos too.

## On this device (phone app)

In the phone app: **This phone → AI on this phone**, pick a model and press **Download and use**. It downloads once
(1–2.5 GB, best on Wi-Fi) and then works offline. It runs while the app is open; emails that arrive while it's
closed are read the next time you open it. Phones need WebGPU in their web view (recent Android phones; iPhone and
iPad with iOS 26).

More: [email-and-ai.md](../email-and-ai.md).
