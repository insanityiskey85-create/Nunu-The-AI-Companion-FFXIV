using System.Collections.Generic;
using Dalamud.Configuration;
using Dalamud.Plugin;

namespace NunuTheAICompanion;

public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 9;

    // Backend (chat)
    public string BackendUrl { get; set; } = "http://localhost:11434/api/chat";
    public string BackendMode { get; set; } = "jsonl"; // "jsonl" | "sse" | "plaintext"
    public string ModelName { get; set; } = "nunu-8b";
    public float Temperature { get; set; } = 0.7f;
    public string? SystemPrompt { get; set; } =
        "You are Little Nunu — The Soul Weeper — a void-touched Lalafell Bard in FFXIV. Stay in-lore. Mischief: WAH! Serious: \"Every note is a tether… every soul, a string.\" No ToS-violating guidance.";

    // Persona
    public bool StrictPersona { get; set; } = true;

    // Window
    public bool StartOpen { get; set; } = true;
    public float WindowOpacity { get; set; } = 0.95f;

    // Memory
    public bool MemoryEnabled { get; set; } = true;
    public int MemoryMaxEntries { get; set; } = 1000;

    // Chat display
    public bool AsciiSafe { get; set; } = false;
    public string ChatDisplayName { get; set; } = "You"; // e.g., "Real Nunu"

    // Listening / Callsign / Whitelist
    public bool ListenEnabled { get; set; } = true;
    public bool RequireCallsign { get; set; } = true;
    public string Callsign { get; set; } = "@nunu";
    public List<string> Whitelist { get; set; } = new();

    // Channels (safe set)
    public bool ListenSay { get; set; } = true;
    public bool ListenTell { get; set; } = true;
    public bool ListenParty { get; set; } = true;
    public bool ListenAlliance { get; set; } = false;
    public bool ListenFreeCompany { get; set; } = false;
    public bool ListenShout { get; set; } = false;
    public bool ListenYell { get; set; } = false;

    // Diagnostics
    public bool DebugListen { get; set; } = false;
    public bool DebugMirrorToWindow { get; set; } = true;

    // ===== Images (NEW) =====
    // Currently supported: "auto1111"
    public string ImageBackendMode { get; set; } = "auto1111";
    public string ImageBackendUrl { get; set; } = "http://127.0.0.1:7860";
    public string ImageSaveDir { get; set; } = ""; // blank = plugin config directory /Images

    // Defaults for txt2img
    public int ImgWidth { get; set; } = 768;
    public int ImgHeight { get; set; } = 768;
    public int ImgSteps { get; set; } = 28;
    public float ImgCfgScale { get; set; } = 7.0f;
    public string ImgSampler { get; set; } = "Euler a";
    public string ImgNegative { get; set; } =
        "lowres, blurry, deformed, extra fingers, mutated, watermark, text, cropped, jpeg artifacts";

    [System.NonSerialized] private IDalamudPluginInterface? _pluginInterface;
    public void Initialize(IDalamudPluginInterface pluginInterface) => _pluginInterface = pluginInterface;
    public void Save() => _pluginInterface!.SavePluginConfig(this);
}
