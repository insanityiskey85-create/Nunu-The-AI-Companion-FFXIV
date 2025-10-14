using Dalamud.Configuration;
using Dalamud.Plugin;
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace NunuTheAICompanion;

/// <summary>
/// Central plugin configuration for Little Nunu.
/// Includes all fields used by PluginMain, ConfigWindow, Soul Threads, Songcraft, Search, Voice, IPC, and Image UI.
/// </summary>
[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 4;

    // ===== Backend (chat / LLM) =====
    /// <summary>Backend mode label (e.g., "ollama").</summary>
    public string BackendMode { get; set; } = "ollama";

    /// <summary>Ollama-compatible chat endpoint (e.g., http://127.0.0.1:11434/api/chat).</summary>
    public string BackendUrl { get; set; } = "http://127.0.0.1:11434/api/chat";

    /// <summary>Model name for the backend.</summary>
    public string ModelName { get; set; } = "nunu-8b";

    /// <summary>Sampling temperature (0..2 typical).</summary>
    public float Temperature { get; set; } = 0.7f;

    /// <summary>Optional system prompt injected before history.</summary>
    public string SystemPrompt { get; set; } =
        "You are Little Nunu, the void-touched Lalafell bard—mischievous, empathetic, and lore-bound to Eorzea.";

    /// <summary>
    /// Back-compat alias used by some helpers (e.g., EmbeddingClient).
    /// Maps to BackendUrl so older code compiles without refactor.
    /// </summary>
    [JsonIgnore]
    public string ChatEndpointUrl
    {
        get => BackendUrl;
        set => BackendUrl = value;
    }

    // ===== Memory & Soul Threads =====
    /// <summary>How many recent turns to include alongside thread context.</summary>
    public int ContextTurns { get; set; } = 12;

    /// <summary>Enable topic-aware memory (embeddings + clustering).</summary>
    public bool SoulThreadsEnabled { get; set; } = true;

    /// <summary>Embedding model name for Ollama /api/embeddings.</summary>
    public string? EmbeddingModel { get; set; } = "nomic-embed-text";

    /// <summary>Cosine similarity threshold for creating new threads.</summary>
    public float ThreadSimilarityThreshold { get; set; } = 0.78f;

    /// <summary>Max entries pulled from the best-matching thread.</summary>
    public int ThreadContextMaxFromThread { get; set; } = 6;

    /// <summary>Max recent entries always included for recency balance.</summary>
    public int ThreadContextMaxRecent { get; set; } = 8;

    // ===== Songcraft (MIDI generation) =====
    public bool SongcraftEnabled { get; set; } = true;
    public string? SongcraftKey { get; set; } = "C4";
    public int SongcraftTempoBpm { get; set; } = 96;
    public int SongcraftBars { get; set; } = 8;
    public int SongcraftProgram { get; set; } = 24; // Nylon Guitar
    public string? SongcraftSaveDir { get; set; } = null; // null => Memories dir
    public string? SongcraftBardCallTrigger { get; set; } = "/song";

    // ===== Voice (TTS) =====
    public bool VoiceSpeakEnabled { get; set; } = true;
    public string? VoiceName { get; set; } = "";
    public int VoiceRate { get; set; } = 0;       // -10..10
    public int VoiceVolume { get; set; } = 100;   // 0..100
    public bool VoiceOnlyWhenWindowFocused { get; set; } = false;

    // ===== Chat Listening (input) =====
    public bool ListenEnabled { get; set; } = true;
    public bool ListenSelf { get; set; } = true;
    public bool ListenSay { get; set; } = true;
    public bool ListenTell { get; set; } = true;
    public bool ListenParty { get; set; } = true;
    public bool ListenAlliance { get; set; } = false;
    public bool ListenFreeCompany { get; set; } = true;
    public bool ListenShout { get; set; } = false;
    public bool ListenYell { get; set; } = false;

    /// <summary>If true, messages must contain Callsign to be heard.</summary>
    public bool RequireCallsign { get; set; } = false;

    /// <summary>Callsign to trigger Nunu in chat (e.g., "@nunu").</summary>
    public string Callsign { get; set; } = "@Little Nunu";

    /// <summary>Optional whitelist of authors permitted to trigger Nunu.</summary>
    public List<string>? Whitelist { get; set; } = new();

    // ===== Chat Broadcast (output) =====
    public bool BroadcastAsPersona { get; set; } = true;
    public string PersonaName { get; set; } = "Little Nunu";

    // ===== IPC Relay =====
    public string? IpcChannelName { get; set; } = "";
    public bool PreferIpcRelay { get; set; } = false;

    // ===== Window / UI =====
    public bool StartOpen { get; set; } = true;       // open ChatWindow on startup
    public float WindowOpacity { get; set; } = 1.0f;   // 0..1
    public bool AsciiSafe { get; set; } = false;
    public bool TwoPaneMode { get; set; } = false;
    public bool ShowCopyButtons { get; set; } = true;
    public float FontScale { get; set; } = 1.0f;
    public bool LockWindow { get; set; } = false;
    public string ChatDisplayName { get; set; } = "You";

    // ===== Debug =====
    public bool DebugListen { get; set; } = false;
    public bool DebugMirrorToWindow { get; set; } = true;

    // ===== Web Search (optional) =====
    public bool AllowInternet { get; set; } = false;
    public string SearchBackend { get; set; } = "serpapi";
    public string? SearchApiKey { get; set; } = "";
    public int SearchMaxResults { get; set; } = 5;
    public int SearchTimeoutSec { get; set; } = 15;

    // ===== Image (text-to-image) =====
    public string ImageBackend { get; set; } = "none";           // e.g., "a1111", "comfy", "none"
    public string ImageBaseUrl { get; set; } = "http://127.0.0.1:7860";
    public string ImageModel { get; set; } = "stable-diffusion-1.5";
    public int ImageSteps { get; set; } = 30;
    public float ImageGuidance { get; set; } = 7.5f;             // CFG
    public int ImageWidth { get; set; } = 768;
    public int ImageHeight { get; set; } = 768;
    public string ImageSampler { get; set; } = "Euler a";
    public int ImageSeed { get; set; } = -1;                     // -1 => random
    public int ImageTimeoutSec { get; set; } = 60;
    public bool SaveImages { get; set; } = true;
    public string ImageSaveSubdir { get; set; } = "Images";

    // ===== Persistence =====
    [NonSerialized] private IDalamudPluginInterface? _pi;

    /// <summary>Dalamud calls this to hand the plugin interface back to us.</summary>
    public void Initialize(IDalamudPluginInterface pi) => _pi = pi;

    /// <summary>Persist configuration to disk.</summary>
    public void Save() => _pi?.SavePluginConfig(this);
}
