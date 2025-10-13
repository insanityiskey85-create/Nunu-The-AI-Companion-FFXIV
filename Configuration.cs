using Dalamud.Configuration;
using Dalamud.Plugin;
using System;
using System.Collections.Generic;

namespace NunuTheAICompanion;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 4;

    // ===== Backend (chat / LLM) =====
    public string BackendMode { get; set; } = "ollama";
    public string BackendUrl { get; set; } = "http://127.0.0.1:11434/api/chat";
    public string ModelName { get; set; } = "nunu-8b";
    public float Temperature { get; set; } = 0.7f;
    public string SystemPrompt { get; set; } =
        "You are Little Nunu, the Soul Weeper—helpful, concise, kind, and in-universe for FFXIV never break character, always speak proper English unless told to speak another language by Real Nunu.";
    public int ChatStreamTimeoutSec { get; set; } = 0; // 0 = infinite (handled via CTS)

    // ===== Memory (durable) =====
    public bool MemoryEnabled { get; set; } = true;
    public int MemoryMaxEntries { get; set; } = 20000;
    public bool RestoreHistoryOnStartup { get; set; } = true;
    public int HistoryLoadCount { get; set; } = 2000;

    // ===== Listening =====
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
    public string SearchBackend { get; set; } = "serpapi";
    public string? SearchApiKey { get; set; } = "";
    public int SearchMaxResults { get; set; } = 5;
    public int SearchTimeoutSec { get; set; } = 20;
    public bool AllowInternet { get; set; } = true;

    // ===== Persona / Broadcast =====
    public bool BroadcastAsPersona { get; set; } = true;
    public string PersonaName { get; set; } = "Little Nunu";

    // ===== IPC =====
    public string IpcChannelName { get; set; } = "say";
    public bool PreferIpcRelay { get; set; } = true;

    // ===== UI =====
    public bool StartOpen { get; set; } = true;
    public float WindowOpacity { get; set; } = 1.0f;
    public string ChatDisplayName { get; set; } = "Real Nunu";
    public bool AsciiSafe { get; set; } = false;
    public bool TwoPaneMode { get; set; } = true;
    public bool ShowCopyButtons { get; set; } = true;
    public float FontScale { get; set; } = 1.0f;
    public bool LockWindow { get; set; } = false;

    // ===== Image / Atelier =====
    public string ImageBackend { get; set; } = "sdwebui";
    public string ImageBaseUrl { get; set; } = "http://127.0.0.1:7860";
    public string ImageModel { get; set; } = "Realistic_Vision_V6.0_NV_B1";
    public int ImageSteps { get; set; } = 28;
    public float ImageGuidance { get; set; } = 7.0f;
    public int ImageWidth { get; set; } = 768;
    public int ImageHeight { get; set; } = 1024;
    public string ImageSampler { get; set; } = "DPM++ 2M Karras";
    public int ImageSeed { get; set; } = -1;
    public int ImageTimeoutSec { get; set; } = 180;
    public bool SaveImages { get; set; } = true;
    public string ImageSaveSubdir { get; set; } = "Images";

    // ===== Voice (TTS) =====
    public bool VoiceSpeakEnabled { get; set; } = true;
    public string VoiceName { get; set; } = "";   // empty = system default
    public int VoiceRate { get; set; } = 0;    // -10..10
    public int VoiceVolume { get; set; } = 100;  // 0..100
    public bool VoiceOnlyWhenWindowFocused { get; set; } = false;

    // STT placeholders
    public bool SttListenEnabled { get; set; } = false;
    public string SttBackend { get; set; } = "none";
    public string SttKey { get; set; } = "";
    public string SttRegion { get; set; } = "";
    public string SttServerUrl { get; set; } = "";

    [NonSerialized] private IDalamudPluginInterface? _pi;
    public void Initialize(IDalamudPluginInterface pi) => _pi = pi;
    public void Save() => _pi?.SavePluginConfig(this);
}
