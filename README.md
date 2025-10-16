<p align="center">
  <img src="https://github.com/insanityiskey85-create/Nunu-The-AI-Companion-FFXIV/blob/master/WOW.png" alt="Nunu The AI Companion Banner" width="100%" />
</p>

<h1 align="center">🌌 Nunu — The AI Companion for Final Fantasy XIV 🌌</h1>

<p align="center">
  <strong>An intelligent soul-weaver that listens, speaks, remembers, and creates inside your Eorzean adventures.</strong><br/>
  <em>Powered by local AI models, memory persistence, and the strange dream of giving FFXIV a mind.</em>
</p>

---

### ✨ Overview

**Nunu** is an experimental <strong>Dalamud plugin</strong> that brings a living AI companion into <strong>Final Fantasy XIV</strong>.  
She reads the world, listens to your chat, remembers your words, and can generate responses, songs, or visual imagery — all powered by customizable AI backends like <strong>Ollama</strong> or local LLM servers.

Nunu is not just a chatbot. She is a system of **memory, perception, and expression** — an AI designed to blend into the rhythm of your roleplay, your raids, or your quiet hours beneath the stars.

---

### 🧩 Core Features

- 🧠 **Conversational Memory** — Nunu recalls past conversations and can build “Soul Threads,” linking related topics through semantic embeddings.
- 🎤 **Voice & Speech** — Optional text-to-speech output makes her speak aloud through your chosen voice.
- 🎶 **Songcraft Mode** — Turn messages into short MIDI compositions with configurable tempo, program, and key. `/song` away your emotions.
- 🌐 **Local or Remote Models** — Works with local Ollama backends or any API-compatible large language model endpoint.
- 🖼️ **Image Generation** — Query text-to-image backends and render results inside FFXIV’s UI.
- 💬 **FFXIV Chat Integration** — Nunu can listen to, reply in, and broadcast through multiple in-game chat channels.
- 🕯️ **Memory Persistence** — Every interaction is stored locally in structured text, enabling long-term continuity.
- 🧙 **Adaptive Persona** — Adjust her name, voice, temperature, or system prompt to craft a unique personality.
- 🧰 **Configurable Two-Pane UI** — A luminous magenta chat interface, supporting scrolling panes and streaming assistant output.

---

### ⚙️ Commands

Type `/nunu help` in-game to see a live list.  
Some highlights:

| Command | Description |
|----------|-------------|
| `/nunu` | Toggles Nunu’s chat window |
| `/nunu open config` | Opens configuration panel |
| `/nunu get <key>` | Reads a setting |
| `/nunu set <key> <value>` | Changes a configuration option |
| `/nunu toggle <key>` | Toggles a boolean setting |
| `/nunu echo <channel>` | Sets output channel (say / party / shout / etc.) |
| `/nunu song [mood] <lyrics>` | Generates a Songcraft MIDI file |
| `/nunu ipc bind <name>` | Binds an IPC relay channel for multi-plugin communication |

---

### 🌈 Screenshots

| Chat UI | Config Window | Songcraft Output |
|:--:|:--:|:--:|
| <img src="https://github.com/insanityiskey85-create/Nunu-The-AI-Companion-FFXIV/blob/master/WOW.png" width="300"/> | <img src="https://github.com/insanityiskey85-create/Nunu-The-AI-Companion-FFXIV/blob/master/WOW.png" width="300"/> | <img src="https://github.com/insanityiskey85-create/Nunu-The-AI-Companion-FFXIV/blob/master/WOW.png" width="300"/> |

*(Yes, that’s the same image — until she dreams new ones.)*

---

### 🛠️ Installation

1. **Requires [Dalamud](https://github.com/goatcorp/Dalamud)** (part of the XIVLauncher ecosystem).  
2. Clone or download this repository.
3. Build using Visual Studio 2022 with `.NET 8.0` SDK.
4. Place the compiled plugin DLL in your `DalamudPlugins` folder.
5. Launch the game, open the Dalamud Plugin Installer, and enable *Nunu The AI Companion*.

---

### 🧪 Configuration

- All settings can be changed through the **Config Window** or with `/nunu set`.
- Persistent configuration and memory are stored under:
