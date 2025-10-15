using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;

namespace NunuTheAICompanion.UI
{
    public sealed class ImageWindow : Window
    {
        private readonly Configuration _cfg;
        private readonly IPluginLog _log;

        public ImageWindow(Configuration cfg, IPluginLog log)
            : base("Nunu — Images", ImGuiWindowFlags.NoCollapse)
        {
            _cfg = cfg;
            _log = log;
            SizeConstraints = new WindowSizeConstraints
            {
                MinimumSize = new Vector2(520, 360),
                MaximumSize = new Vector2(4096, 4096),
            };
        }

        public override bool DrawConditions()
        {
            if (PluginMain.IsShuttingDown) return false; // <<< guard
            return base.DrawConditions();
        }

        public override void Draw()
        {
            if (PluginMain.IsShuttingDown) return;

            ImGui.TextUnformatted("Image generation / history (wire your preview or controls here).");
            ImGui.Separator();
            ImGui.TextUnformatted($"Backend: {_cfg.ImageBackend ?? "(none)"}  Model: {_cfg.ImageModel ?? "(none)"}");
        }
    }
}
