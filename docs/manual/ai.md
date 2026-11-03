# The AI assistant

LifeHub's AI is **optional** and runs **on your own computer** with [Ollama](https://ollama.com)
(free). Nothing you write is sent to an AI company.

## What it does

- **Notes**: files each note into a category with tags.
- **Email**: reads your inbox and suggests tasks, calendar events, renewal changes and shopping
  items — safe ones are applied for you and can be undone.
- **Kitchen and meals**: lists groceries from a photo, plans meals, suggests ideas that use up
  food.
- **Tasks**: understands "dentist next Friday at 3".

Anything the AI writes is shown in **purple**, and it never changes your own words.

## Set it up from the desktop app (easiest)

The setup assistant checks whether Ollama is installed:

- **Not installed**: press **Yes, install Ollama**. LifeHub downloads and installs it, starts it,
  and then downloads a model that suits your computer's memory:
  - 16 GB or more: `qwen2.5:7b` (about 4.7 GB) — the best results
  - 8 GB: `qwen2.5:3b` (about 1.9 GB)
  - less: `qwen2.5:1.5b` (about 1 GB)
- **Already installed**: pick a model and press **Download and use** (or **Use this model**).
- **No thanks**: skip it. Set it up any time as below.

## Set it up yourself

1. Install Ollama from [ollama.com/download](https://ollama.com/download) (on the same computer, or
   any computer on your network).
2. Download a model: in a terminal, `ollama pull qwen2.5:7b`.
3. In LifeHub: **Settings → AI** → tick **Enabled**, set the **Ollama URL** (`http://localhost:11434`
   on the same computer, or `http://<that-computer's-IP>:11434`), press **List** to pick the
   **Model**, then **Save AI settings**. Try it with **Analyse** further down the page.

Ollama on another computer must accept network connections: start it with
`OLLAMA_HOST=0.0.0.0` (see Ollama's FAQ).

**Photos and handwriting** need a vision model, e.g. `ollama pull qwen3-vl:4b-instruct`, set as
**Vision model** in Settings → AI.

More: [email-and-ai.md](../email-and-ai.md).
