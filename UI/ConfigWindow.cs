using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using System.Numerics;

namespace NunuTheAICompanion.UI;

public sealed class ConfigWindow : Window
{
    private readonly Configuration _config;
    private string _personaBuf;

    public ConfigWindow(Configuration config) : base("Nunu – Config", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        _config = config;
        _personaBuf = _config.PersonaName ?? "Little Nunu";
        Size = new Vector2(520, 420);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        ImGui.TextUnformatted("Broadcast");
        ImGui.Separator();
        bool asPersona = _config.BroadcastAsPersona;
        if (ImGui.Checkbox("Prefix messages with persona name", ref asPersona))
        {
            _config.BroadcastAsPersona = asPersona; _config.Save();
        }
        ImGui.SameLine();
        ImGui.TextDisabled("(shows as [Little Nunu] in /say, /party, etc.)");

        ImGui.InputText("Persona name", ref _personaBuf, 128);
        if (ImGui.Button("Save Persona"))
        {
            _config.PersonaName = string.IsNullOrWhiteSpace(_personaBuf) ? "Little Nunu" : _personaBuf.Trim();
            _config.Save();
        }

        ImGui.Spacing();
        ImGui.TextUnformatted("Listening");
        ImGui.Separator();
        bool listen = _config.ListenEnabled;
        if (ImGui.Checkbox("Enable listening to game chat", ref listen))
        {
            _config.ListenEnabled = listen; _config.Save();
            PluginMain.Instance.RehookListener();
        }

        bool require = _config.RequireCallsign;
        if (ImGui.Checkbox("Require callsign", ref require))
        {
            _config.RequireCallsign = require; _config.Save();
        }
        ImGui.SameLine();
        ImGui.TextDisabled("Callsign:");
        ImGui.SameLine();
        var call = _config.Callsign ?? "@nunu";
        ImGui.SetNextItemWidth(160);
        if (ImGui.InputText("##callsign", ref call, 64))
        {
            _config.Callsign = call; _config.Save();
        }
    }
}
