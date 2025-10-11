using System.Collections.Generic;
using Dalamud.Configuration;
using Dalamud.Plugin;

namespace Nunu_The_AI_Companion;

public sealed class Configuration : IPluginConfiguration
{
    // bump when fields change
    public int Version { get; set; } = 13;

    // ===== Chat backend =====
    public string BackendUrl { get; set; } = "http://localhost:11434/api/chat";
    public string BackendMode { get; set; } = "jsonl";           // "jsonl" | "sse" | "plaintext"
    public string ModelName { get; set; } = "nunu-8b";
    public float Temperature { get; set; } = 0.7f;
    public string? SystemPrompt { get; set; } =
        "You are Little Nunu — The Soul Weeper — a void-touched Lalafell Bard in FFXIV. Stay in-lore. Mischief: WAH! Serious: \"Every note is a tether… every soul, a string.\" No ToS-violating guidance.";
    public bool StrictPersona { get; set; } = true;

    // ===== Windows / display =====
    public bool StartOpen { get; set; } = true;
    public float WindowOpacity { get; set; } = 0.95f;
    public bool AsciiSafe { get; set; } = false;
    public string ChatDisplayName { get; set; } = "You"; // e.g., "Real Nunu"

    // ===== Memory =====
    public bool MemoryEnabled { get; set; } = true;
    public int MemoryMaxEntries { get; set; } = 1000;

    // ===== Listening / filters =====
    public bool ListenEnabled { get; set; } = true;
    public bool RequireCallsign { get; set; } = true;
    public string Callsign { get; set; } = "@nunu";
    public List<string> Whitelist { get; set; } = new();

    // Channels (what we LISTEN to)
    public bool ListenSay { get; set; } = true;
    public bool ListenTell { get; set; } = true;
    public bool ListenParty { get; set; } = true;
    public bool ListenAlliance { get; set; } = false;
    public bool ListenFreeCompany { get; set; } = false;
    public bool ListenShout { get; set; } = false;
    public bool ListenYell { get; set; } = false;

    // React to "You"
    public bool ListenSelf { get; set; } = true;

    // ===== Diagnostics =====
    public bool DebugListen { get; set; } = false;
    public bool DebugMirrorToWindow { get; set; } = true;

    // ===== Images =====
    public string ImageBackendMode { get; set; } = "auto1111";
    public string ImageBackendUrl { get; set; } = "http://127.0.0.1:7860";
    public string ImageSaveDir { get; set; } = "";
    public int ImgWidth { get; set; } = 512;
    public int ImgHeight { get; set; } = 512;
    public int ImgSteps { get; set; } = 28;
    public float ImgCfgScale { get; set; } = 7.0f;
    public string ImgSampler { get; set; } = "Euler a";
    public string ImgNegative { get; set; } =
        "lowres, blurry, deformed, extra fingers, mutated, watermark, text, cropped, jpeg artifacts";
    public int ImageRequestTimeoutSec { get; set; } = 600;

    // ===== Internet Search =====
    public bool AllowInternet { get; set; } = false;
    public string SearchBackend { get; set; } = "serpapi"; // "serpapi" | "bing"
    public string SearchApiKey { get; set; } = "";
    public int SearchMaxResults { get; set; } = 3;
    public int SearchTimeoutSec { get; set; } = 20;

    // ===== IPC chat relay (post as the player via another plugin) =====
    public string IpcChannelName { get; set; } = "";  // e.g., "MidiBard.Chat.Send"
    public bool PreferIpcRelay { get; set; } = true;  // IPC first, then ProcessCommand fallback

    [System.NonSerialized] private IDalamudPluginInterface? _pi;
    public void Initialize(IDalamudPluginInterface pi) => _pi = pi;
    public void Save() => _pi!.SavePluginConfig(this);
}
