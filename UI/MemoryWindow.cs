using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using NunuTheAICompanion.Services;
using Dalamud.Plugin.Services;

namespace NunuTheAICompanion.UI
{
    public sealed class MemoryWindow : Window
    {
        private readonly Configuration _cfg;
        private readonly MemoryService _memory;
        private readonly IPluginLog _log;

        public MemoryWindow(Configuration cfg, MemoryService memory, IPluginLog log)
            : base("Nunu — Memory", ImGuiWindowFlags.NoCollapse)
        {
            _cfg = cfg;
            _memory = memory;
            _log = log;
            SizeConstraints = new WindowSizeConstraints
            {
                MinimumSize = new Vector2(520, 360),
                MaximumSize = new Vector2(4096, 4096),
            };
        }

        public override bool DrawConditions()
        {
            if (PluginMain.IsShuttingDown) return false;
            return base.DrawConditions();
        }

        public override void Draw()
        {
            if (PluginMain.IsShuttingDown) return;

            if (ImGui.Button("Flush to Disk"))
            {
                try { _memory.Flush(); } catch { }
            }
            ImGui.SameLine();
            if (ImGui.Button("Reload from Disk"))
            {
                try { _memory.Load(); } catch { }
            }

            ImGui.SameLine();
            ImGui.TextUnformatted($"Storage: {_memory.StorageDirectory}");

            ImGui.Separator();
            ImGui.TextUnformatted("Recent Context (for preview):");

            // Show whatever the chat context would normally pull
            var recent = _memory.GetRecentForContext(_cfg.ContextTurns);
            ImGui.BeginChild("##mem_scroll", new Vector2(-1, -1), true);
            foreach (var (role, content) in recent)
            {
                ImGui.TextUnformatted($"[{role}] {content}");
            }
            ImGui.EndChild();
        }
    }
}
