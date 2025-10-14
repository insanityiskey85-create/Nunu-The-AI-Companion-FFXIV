using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin;

namespace NunuTheAICompanion;

public sealed class Configuration : Dalamud.Configuration.IPluginConfiguration
{
    public int Version { get; set; } = 4;

    // -------- Backend --------
    public string BackendMode { get; set; } = "ollama";
    public string BackendUrl { get; set; } = "http://127.0.0.1:11434/api/chat";
    public string ModelName { get; set; } = "nunu";
    public float Temperature { get; set; } = 0.7f;
    public string SystemPrompt { get; set; } =
        "You are Little Nunu, the Soul Weeper. Stay in FFXIV voice; be helpful and kind.";
    public string ChatDisplayName { get; set; } = "You";

    // --- Endpoints (compat with older services) ---
    public string ChatEndpointUrl { get; set; } = "http://127.0.0.1:11434/api/chat";
    public string EmbeddingEndpointUrl { get; set; } = "http://127.0.0.1:11434/api/embeddings";

    // -------- Memory / Soul Threads --------
    public bool SoulThreadsEnabled { get; set; } = true;
    public int ContextTurns { get; set; } = 12;
    public string? EmbeddingModel { get; set; } = "nomic-embed-text";
    public float ThreadSimilarityThreshold { get; set; } = 0.72f;
    public int ThreadContextMaxFromThread { get; set; } = 8;
    public int ThreadContextMaxRecent { get; set; } = 4;

    // -------- Songcraft --------
    public bool SongcraftEnabled { get; set; } = true;
    public string SongcraftBardCallTrigger { get; set; } = "/song";
    public string SongcraftKey { get; set; } = "C4";
    public int SongcraftTempoBpm { get; set; } = 96;
    public int SongcraftBars { get; set; } = 8;
    public int SongcraftProgram { get; set; } = 0; // GM program 0..127
    public string? SongcraftSaveDir { get; set; } = null;

    // -------- Voice --------
    public bool VoiceSpeakEnabled { get; set; } = false;
    public string? VoiceName { get; set; }
    public int VoiceRate { get; set; } = 0;     // -10..10
    public int VoiceVolume { get; set; } = 100; // 0..100
    public bool VoiceOnlyWhenWindowFocused { get; set; } = false;

    // -------- Listen (inbound chat) --------
    public bool ListenEnabled { get; set; } = true;
    public bool ListenSelf { get; set; } = false;
    public bool ListenSay { get; set; } = true;
    public bool ListenTell { get; set; } = true;
    public bool ListenParty { get; set; } = true;
    public bool ListenAlliance { get; set; } = false;
    public bool ListenFreeCompany { get; set; } = false;
    public bool ListenShout { get; set; } = false;
    public bool ListenYell { get; set; } = false;
    public bool RequireCallsign { get; set; } = false;
    public string Callsign { get; set; } = "@nunu";

    // ---- Whitelist for allowed authors (by player name) ----
    // Empty list = allow all (subject to other listen rules).
    // Comparison is case-insensitive; you can fill via UI or /nunu set.
    public List<string> Whitelist { get; set; } = new List<string>();

    // -------- Broadcast & IPC --------
    public bool BroadcastAsPersona { get; set; } = true;
    public string PersonaName { get; set; } = "Little Nunu";
    public string? IpcChannelName { get; set; } = string.Empty;
    public bool PreferIpcRelay { get; set; } = true;

    // Preferred outgoing channel for broadcasts
    // Values: Say, Party, Shout, Yell, Alliance, FreeCompany, Echo
    public string EchoChannel { get; set; } = "Say";

    // -------- UI --------
    public bool StartOpen { get; set; } = true;
    public float WindowOpacity { get; set; } = 1.0f; // 0.25..1.0
    public bool AsciiSafe { get; set; } = false;
    public bool TwoPaneMode { get; set; } = true;
    public bool ShowCopyButtons { get; set; } = true;
    public float FontScale { get; set; } = 1.0f; // 0.75..1.5
    public bool LockWindow { get; set; } = false;

    // -------- Images --------
    public string ImageBackend { get; set; } = "none";
    public string ImageBaseUrl { get; set; } = "http://127.0.0.1:7860";
    public string ImageModel { get; set; } = "stable-diffusion-1.5";
    public int ImageSteps { get; set; } = 30;
    public float ImageGuidance { get; set; } = 7.0f;
    public int ImageWidth { get; set; } = 768;
    public int ImageHeight { get; set; } = 512;
    public string ImageSampler { get; set; } = "Euler a";
    public int ImageSeed { get; set; } = -1;
    public int ImageTimeoutSec { get; set; } = 60;
    public bool SaveImages { get; set; } = true;
    public string ImageSaveSubdir { get; set; } = "Images";

    // -------- Search --------
    public bool AllowInternet { get; set; } = false;
    public string SearchBackend { get; set; } = "serpapi";
    public string SearchApiKey { get; set; } = "";
    public int SearchMaxResults { get; set; } = 5;
    public int SearchTimeoutSec { get; set; } = 20;

    // -------- Debug --------
    public bool DebugMirrorToWindow { get; set; } = false;
    public bool DebugListen { get; set; } = false;

    [NonSerialized] private IDalamudPluginInterface? _pi;

    public void Initialize(IDalamudPluginInterface pi)
    {
        _pi = pi;
        _pi.UiBuilder.DisableUserUiHide = false;
        // Normalize whitelist to a consistent form
        NormalizeWhitelist();
    }

    public void Save()
    {
        NormalizeWhitelist();
        _pi?.SavePluginConfig(this);
    }

    // --- Helpers ---
    private void NormalizeWhitelist()
    {
        if (Whitelist == null) { Whitelist = new List<string>(); return; }
        // trim empties and normalize casing
        Whitelist = Whitelist
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public bool IsWhitelisted(string author)
    {
        if (Whitelist == null || Whitelist.Count == 0) return true; // empty = allow all
        return Whitelist.Contains(author?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase);
    }
}
