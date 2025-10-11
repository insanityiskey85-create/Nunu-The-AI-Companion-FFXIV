using Dalamud.Configuration;
using Dalamud.Plugin;
using System;
using System.Collections.Generic;

namespace NunuTheAICompanion;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    // ===== Backend (chat) =====
    public string BackendUrl { get; set; } = "http://127.0.0.1:11434/api/chat";
    public string ModelName { get; set; } = "nunu-8b";
    public float Temperature { get; set; } = 0.7f;
    public string SystemPrompt { get; set; } =
        "You are Little Nunu, the Soul Weeper—be helpful, concise, kind, and in-universe.";

    // Some UIs expect a free-form backend mode string (e.g., 'ollama', 'openai', 'proxy')
    public string BackendMode { get; set; } = "ollama";

    // ===== Memory =====
    public bool MemoryEnabled { get; set; } = true;
    public int MemoryMaxEntries { get; set; } = 200;

    // ===== Listening (game chat) =====
    public bool ListenEnabled { get; set; } = true;
    public bool RequireCallsign { get; set; } = true;
    public string Callsign { get; set; } = "@nunu";
    public bool ListenSelf { get; set; } = true;
    public bool ListenSay { get; set; } = true;
    public bool ListenTell { get; set; } = true;
    public bool ListenParty { get; set; } = true;
    public bool ListenAlliance { get; set; } = false;
    public bool ListenFreeCompany { get; set; } = true;
    public bool ListenShout { get; set; } = false;
    public bool ListenYell { get; set; } = false;
    public List<string>? Whitelist { get; set; } = new();

    // ===== Debug =====
    public bool DebugListen { get; set; } = false;
    public bool DebugMirrorToWindow { get; set; } = true;

    // ===== Web Search =====
    // Backend selector (e.g., 'serpapi', 'none'); UI already has SearchBackend
    public string SearchBackend { get; set; } = "serpapi";
    public int SearchMaxResults { get; set; } = 5;
    public int SearchTimeoutSec { get; set; } = 20;
    public bool AllowInternet { get; set; } = true;

    // API key for search providers that require it (e.g., SerpAPI)
    public string? SearchApiKey { get; set; } = "";

    // ===== Broadcast persona in real chat =====
    public bool BroadcastAsPersona { get; set; } = true;
    public string PersonaName { get; set; } = "Little Nunu";

    // ===== IPC =====
    public string IpcChannelName { get; set; } = "say";
    public bool PreferIpcRelay { get; set; } = true;

    // ===== UI niceties expected by ChatWindow =====
    public bool StartOpen { get; set; } = true;         // open chat window on load
    public float WindowOpacity { get; set; } = 1.0f;    // 0..1 alpha for window
    public string ChatDisplayName { get; set; } = "Real Nunu"; // label for your side in UI
    public bool AsciiSafe { get; set; } = false;        // strip non-ASCII if desired

    [NonSerialized] private IDalamudPluginInterface? _pi;
    public void Initialize(IDalamudPluginInterface pi) => _pi = pi;
    public void Save() => _pi?.SavePluginConfig(this);
}
