using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using System.Numerics;
// ImGui bindings
using ImGui = Dalamud.Bindings.ImGui.ImGui;
using ImGuiCol = Dalamud.Bindings.ImGui.ImGuiCol;
using ImGuiStyleVar = Dalamud.Bindings.ImGui.ImGuiStyleVar;

namespace NunuTheAICompanion.UI;

public sealed class ConfigWindow : Window
{
    private readonly Configuration _config;
    private string _backend;
    private float _opacity;
    private float _temperature;
    private bool _strictPersona;

    public ConfigWindow(Configuration config) : base("Nunu — Configuration")
    {
        _config = config;
        _backend = config.BackendUrl;
        _opacity = config.WindowOpacity;
        _temperature = config.Temperature;
        _strictPersona = config.StrictPersona;
        RespectCloseHotkey = true;
        Flags |= ImGuiWindowFlags.AlwaysAutoResize;
        IsOpen = false;
    }

    public override void Draw()
    {
        ImGui.TextUnformatted("Nunu The AI Companion — Settings");
        ImGui.Separator();

        ImGui.TextUnformatted("Backend URL");
        ImGui.InputText("##backend", ref _backend, 1024);

        ImGui.TextUnformatted("Window Opacity");
        ImGui.SliderFloat("##opacity", ref _opacity, 0.30f, 1.00f, "%.2f");

        ImGui.TextUnformatted("Temperature");
        ImGui.SliderFloat("##temp", ref _temperature, 0.1f, 1.5f, "%.2f");

        ImGui.Checkbox("Strict Persona (stay in-character)", ref _strictPersona);

        ImGui.Separator();
        if (ImGui.Button("Save & Close", new Vector2(140 * ImGuiHelpers.GlobalScale, 28 * ImGuiHelpers.GlobalScale)))
        {
            _config.BackendUrl = _backend;
            _config.WindowOpacity = _opacity;
            _config.Temperature = _temperature;
            _config.StrictPersona = _strictPersona;
            _config.Save();
            IsOpen = false;
        }
        ImGui.SameLine();
        if (ImGui.Button("Cancel", new Vector2(100 * ImGuiHelpers.GlobalScale, 28 * ImGuiHelpers.GlobalScale)))
        {
            IsOpen = false;
        }

        ImGui.Separator();
        ImGui.TextDisabled("Tips: /nunu toggles chat, '/nunu config' opens this window.");
    }
}
