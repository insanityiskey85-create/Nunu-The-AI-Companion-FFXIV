using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using Nunu_The_AI_Companion;
using NunuTheAICompanion.Services;
using System;
using System.Numerics;

namespace NunuTheAICompanion.UI;

public sealed class MemoryWindow : Window
{
    private readonly Configuration _config;
    private readonly MemoryService? _memory;
    private readonly IPluginLog? _log;

    public MemoryWindow(Configuration config, MemoryService? memory, IPluginLog? log = null)
        : base("Nunu – Memory", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _memory = memory;
        _log = log;
        Size = new Vector2(600, 420);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        if (_memory is null)
        {
            ImGui.TextDisabled("Memory service not available.");
            return;
        }

        var enabled = _config.MemoryEnabled;
        if (ImGui.Checkbox("Enable Memory", ref enabled))
            _config.MemoryEnabled = enabled;

        ImGui.SameLine();
        if (ImGui.Button("Clear"))
        {
            _memory.Clear();
            _log?.Information("[MemoryWindow] Cleared.");
        }

        ImGui.Separator();
        var list = _memory.Snapshot();
        if (list.Count == 0)
        {
            ImGui.TextDisabled("No memories yet.");
            return;
        }

        ImGui.BeginChild("mem-list", new Vector2(0, -ImGui.GetFrameHeightWithSpacing()), true);
        foreach (var m in list)
            ImGui.TextWrapped($"{m.Timestamp:HH:mm:ss} [{m.Topic}] {m.Role}: {m.Content}");
        ImGui.EndChild();
    }
}
