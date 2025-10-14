using Dalamud.Configuration;
using Dalamud.Plugin;
using System;
using System.Collections.Generic;

namespace NunuTheAICompanion;

/// <summary>
/// Central plugin configuration. Includes all knobs required by
/// Soul Threads, Songcraft, chat listening/broadcast, search, voice, and IPC.
/// </summary>
[Serializable]
public partial class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 4;

    // ===== Backend (chat / LLM) =====
    /// <summary>Backend mode label (e.g., "ollama"). Currently informational.</summary>
    public string BackendMode { get; set; } = "ollama";

    /// <summary>Primary chat endpoint (Ollama-compatible /api/chat, or any adapter you use).</summary>
    public string BackendUrl { get; set; } = "http://127.0.0.1:11434/api/chat";

    /// <summary>Model identifier for the Ollama backend.</summary>
    public string ModelName { get; set; } = "nunu-8b";

    /// <summary>Sampling temperature for the model (0..2 typical).</summary>
    public float Temperature { get; set; } = 0.7f;

    /// <summary>Optional system prompt injected at the start of messages.</summary>
    public string SystemPrompt { get; set; } =
        "You are Little Nunu, the void-touched Lalafell bard—mischievous, empathetic, and lore-bound to Eorzea.";

    /// <summary>
    /// Back-compat alias used by EmbeddingClient; maps to BackendUrl so older code compiles.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string ChatEndpointUrl
    {
        get => BackendUrl;
        set => BackendUrl = value;
    }

    // ===== Memory & Soul Threads =====
    /// <summary>How many recent turns to include alongside the best thread context.</summary>
    public int ContextTurns { get; set; } = 12;

    /// <summary>Enable topic-aware memory (embeddings + clustering).</summary>
    public bool SoulThreadsEnabled { get; set; } = true;

    /// <summary>Embedding model name for Ollama /api/embeddings.</summary>
    public string? EmbeddingModel { get; set; } = "nomic-embed-text";

    /// <summary>Cosine similarity threshold to start a new thread.</summary>
    public float ThreadSimilarityThreshold { get; set; } = 0.78f;

    /// <summary>Max entries to pull from the best-matching thread.</summary>
    public int ThreadContextMaxFromThread { get; set; } = 6;

    /// <summary>Max recent entries to always add for recency balance.</summary>
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
    public string Callsign { get; set; } = "@nunu";

    /// <summary>Optional whitelist of authors permitted to trigger Nunu.</summary>
    public List<string>? Whitelist { get; set; } = new();

    // ===== Chat Broadcast (output) =====
    /// <summary>Prefix outgoing lines with a persona tag like [Little Nunu].</summary>
    public bool BroadcastAsPersona { get; set; } = true;

    /// <summary>Name used in the persona tag when broadcasting.</summary>
    public string PersonaName { get; set; } = "Little Nunu";

    // ===== IPC Relay =====
    /// <summary>IPC channel name for sending lines via another plugin (optional).</summary>
    public string? IpcChannelName { get; set; } = "";

    /// <summary>Prefer IPC relay over native send / command processing when available.</summary>
    public bool PreferIpcRelay { get; set; } = false;

    // ===== UI & Debug =====
    public string ChatDisplayName { get; set; } = "You";
    public bool DebugListen { get; set; } = false;
    public bool DebugMirrorToWindow { get; set; } = true;

    // ===== Web Search (optional) =====
    public bool AllowInternet { get; set; } = false;
    public string SearchBackend { get; set; } = "serpapi";
    public string? SearchApiKey { get; set; } = "";
    public int SearchMaxResults { get; set; } = 5;

    // ===== Persistence =====
    [NonSerialized] private IDalamudPluginInterface? _pi;

    public void Initialize(IDalamudPluginInterface pi) => _pi = pi;

    public void Save() => _pi?.SavePluginConfig(this);
}
