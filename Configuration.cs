using Dalamud.Configuration;
using Dalamud.Plugin;

namespace NunuTheAICompanion;

public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 3;

    // Backend
    public string BackendUrl { get; set; } = "http://localhost:11434/api/chat"; // default: direct Ollama
    public string BackendMode { get; set; } = "jsonl"; // "jsonl" | "sse" | "plaintext"
    public string ModelName { get; set; } = "nunu-8b";
    public float Temperature { get; set; } = 0.7f;
    public string? SystemPrompt { get; set; } =
        "You are Little Nunu — The Soul Weeper — a void-touched Lalafell Bard in FFXIV. Stay in-lore. Mischief: WAH! Serious: \"Every note is a tether… every soul, a string.\" No real world, Stay in character.";

    // Persona toggles
    public bool StrictPersona { get; set; } = true;

    // Window
    public bool StartOpen { get; set; } = true;
    public float WindowOpacity { get; set; } = 0.95f;

    // Memory
    public bool MemoryEnabled { get; set; } = true;
    public int MemoryMaxEntries { get; set; } = 1000;

    [NonSerialized] private IDalamudPluginInterface? _pluginInterface;

    public void Initialize(IDalamudPluginInterface pluginInterface) => _pluginInterface = pluginInterface;

    public void Save() => _pluginInterface!.SavePluginConfig(this);
}
