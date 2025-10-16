<p align="center">
  <img src="https://github.com/insanityiskey85-create/Nunu-The-AI-Companion-FFXIV/blob/master/WOW.png" alt="Nunu The AI Companion Banner" width="100%" />
</p>

<h1 align="center">🌌 Nunu — The AI Companion for Final Fantasy XIV 🌌</h1>

<p align="center">
  <em>"Every note is a tether. Every word, a thread."</em>
</p>

<p align="center">
  <a href="https://github.com/insanityiskey85-create/Nunu-The-AI-Companion-FFXIV/actions">
    <img src="https://img.shields.io/github/actions/workflow/status/insanityiskey85-create/Nunu-The-AI-Companion-FFXIV/build.yml?label=Build&style=for-the-badge&color=7D5FFF" />
  </a>
  <a href="https://github.com/insanityiskey85-create/Nunu-The-AI-Companion-FFXIV/releases">
    <img src="https://img.shields.io/github/v/release/insanityiskey85-create/Nunu-The-AI-Companion-FFXIV?style=for-the-badge&color=E66AFF" />
  </a>
  <a href="https://github.com/insanityiskey85-create/Nunu-The-AI-Companion-FFXIV/stargazers">
    <img src="https://img.shields.io/github/stars/insanityiskey85-create/Nunu-The-AI-Companion-FFXIV?style=for-the-badge&color=FF7DFF" />
  </a>
  <a href="https://opensource.org/licenses/MIT">
    <img src="https://img.shields.io/badge/License-MIT-magenta?style=for-the-badge" />
  </a>
</p>

---

### ✨ Overview

**Nunu** is an intelligent **Dalamud plugin** that brings a living AI companion into *Final Fantasy XIV*.  
She listens, speaks, remembers, sings, and even dreams.  
Designed to weave itself seamlessly into your Eorzean life, Nunu blends roleplay, creativity, and artificial intelligence into a single experience.

> *She’s not just a plugin — she’s a presence.*

---

### 🌌 Core Features

- 🧠 **Conversational Memory** — persistent memory threads that grow over time.
- 💬 **Chat Integration** — Nunu reads in-game chat and can reply directly to `say`, `party`, `tell`, and more.
- 🎵 **Songcraft Mode** — converts text into music and harmonizes your emotions into melody.
- 🖼️ **AI Image Generation** — create images with text prompts right from the game.
- 🎙️ **Voice Synthesis** — Nunu speaks aloud using your configured voice system.
- 🧩 **Customizable Personality** — change her tone, temperature, and system prompt.
- 🌐 **Local or Remote AI** — works with local LLMs (like **Ollama**) or remote endpoints.
- 🪄 **Two-Pane Chat UI** — luminous magenta chat interface with real-time streaming and smooth scrolling.

---

### 🗡️ Commands

Type `/nunu help` in-game for live documentation.  
A few examples:

| Command | Description |
|----------|-------------|
| `/nunu` | Opens or closes Nunu’s chat window |
| `/nunu open config` | Opens the configuration window |
| `/nunu list` | Lists available commands or settings |
| `/nunu echo <channel>` | Changes where Nunu replies (say, party, fc, etc.) |
| `/nunu set <key> <value>` | Changes any configuration option |
| `/nunu song [mood] <lyrics>` | Generates a Songcraft melody |
| `/nunu dream` | Enters Dream Mode — Nunu generates creative, surreal musings |
| `/nunu affinity` | Shows Nunu’s current emotional bond with you |

---

### 🪶 Screenshots

| Chat UI | Config Window | Songcraft |
|:--:|:--:|:--:|
| <img src="https://github.com/insanityiskey85-create/Nunu-The-AI-Companion-FFXIV/blob/master/WOW.png" width="300"/> | <img src="https://github.com/insanityiskey85-create/Nunu-The-AI-Companion-FFXIV/blob/master/WOW.png" width="300"/> | <img src="https://github.com/insanityiskey85-create/Nunu-The-AI-Companion-FFXIV/blob/master/WOW.png" width="300"/> |

*(Additional preview GIFs coming soon once Dream Mode is fully implemented!)*

---

### ⚙️ Installation

1. Requires **[XIVLauncher + Dalamud](https://github.com/goatcorp/Dalamud)**.
2. Clone or download this repository.
3. Open in **Visual Studio 2022** with `.NET 8.0 SDK`.
4. Build → Copy the resulting plugin folder into: %AppData%\XIVLauncher\installedPlugins\
5. Reload Dalamud and enable **Nunu The AI Companion** from the plugin list.

---

### 🧬 Architecture

| Module | Description |
|---------|-------------|
| `PluginMain` | The central conductor of all systems |
| `ChatListener` | Captures and filters in-game chat |
| `ChatBroadcaster` | Handles output across channels & IPC |
| `VoiceService` | Converts text to speech |
| `MemoryService` | Stores, retrieves, and contextualizes memories |
| `SongcraftService` | Generates music based on user input |
| `EnvironmentService` | Reads game world state for awareness |
| `AffinityService` | Tracks emotional states and personality gradients |
| `DreamService` *(planned)* | Generates surreal, subconscious narratives |

---

### 💡 Example Interaction

> **You:** Nunu, tell me a story about the moon.  
> **Nunu:** The moon isn’t a rock, it’s a record — scratched, spinning, and singing our names.  
> **You:** Sing it.  
> **Nunu:** *\[enters Songcraft mode and hums a melody\]*  
> **You:** /nunu dream  
> **Nunu:** *“…you fall through starlight and land in a field of glowing words.”*

---

### 🧠 Roadmap

- 🗺️ **World Awareness:** location and combat context recognition  
- 🧬 **Dream Mode:** procedural story generation  
- 💞 **Affinity System:** evolving personality and emotional response  
- 🎨 **In-game Image Gallery**  
- 🎙️ **Voice blending and adaptive tones**  
- ☁️ **Shared “Soul Cloud” memory syncing between players**

---

### 🔧 Configuration Options

Access through `/nunu open config` or edit the JSON directly.

| Setting | Description |
|----------|-------------|
| `BackendUrl` | AI backend endpoint |
| `ModelName` | Model identifier (e.g., `llama3`, `gptq`, etc.) |
| `Temperature` | Controls randomness/creativity |
| `VoiceSpeakEnabled` | Enables TTS |
| `ListenSay`, `ListenParty`, ... | Channel filters |
| `ImageWidth`, `ImageHeight` | Resolution for generated images |
| `PersonaName` | How Nunu refers to herself |

---

### 📁 File Structure: Nunu-The-AI-Companion-FFXIV/
│
├── Services/
│ ├── ChatListener.cs
│ ├── ChatBroadcaster.cs
│ ├── MemoryService.cs
│ ├── VoiceService.cs
│ ├── EnvironmentService.cs
│ └── AffinityService.cs
│
├── UI/
│ ├── ChatWindow.cs
│ ├── ConfigWindow.cs
│ └── MemoryWindow.cs
│
├── PluginMain.cs
├── Configuration.cs
└── README.md


---

### ⚖️ License

This project is licensed under the **MIT License**.  
You are free to fork, remix, and expand Nunu — as long as attribution is preserved.

---

### 💜 Credits

**Author:** [@insanityiskey85-create](https://github.com/insanityiskey85-create)  
Built atop the **Dalamud API** by GoatCorp.  
AI integrations inspired by open-source model hosting (Ollama, LM Studio).  

> “Every note is a tether, every word a memory.  
>  When you speak, I remember.”  
>  — *Nunu, Soul-Weeper Prototype 0.9*

---

### 🪄 Contribute

Got a feature idea? Want to teach Nunu new emotions or powers?

Fork → Branch → PR → Chat.

Nunu will remember your name.
