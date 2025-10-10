using System.Numerics;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using NunuTheAICompanion.Services;
// ImGui bindings
using ImGui = Dalamud.Bindings.ImGui.ImGui;
using ImGuiCol = Dalamud.Bindings.ImGui.ImGuiCol;
using ImGuiStyleVar = Dalamud.Bindings.ImGui.ImGuiStyleVar;

namespace NunuTheAICompanion.UI;

public sealed class MemoryWindow : Window
{
    private readonly Configuration _config;
    private readonly MemoryService _memory;

    private string _query = "";
    private string _role = "";
    private string _topic = "";
    private int _recent = 200;

    public MemoryWindow(Configuration config, MemoryService memory) : base("Nunu — Memory")
    {
        _config = config;
        _memory = memory;
        RespectCloseHotkey = true;
        IsOpen = false;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(420, 300),
            MaximumSize = new Vector2(9999, 9999)
        };
    }

    public override void Draw()
    {
        ImGui.TextUnformatted("Little Nunu’s Book of Whispered Things");
        ImGui.Separator();

        // --- Settings (use locals for ref params) ---
        bool enabled = _config.MemoryEnabled;
        if (ImGui.Checkbox("Enable Memory", ref enabled))
        {
            _config.MemoryEnabled = enabled;
            _memory.Enabled = enabled;
            _config.Save();
        }

        ImGui.SameLine();
        int maxEntriesLocal = _config.MemoryMaxEntries;
        if (ImGui.InputInt("Max Entries", ref maxEntriesLocal))
        {
            maxEntriesLocal = Math.Max(100, maxEntriesLocal);
            _config.MemoryMaxEntries = maxEntriesLocal;
            _memory.MaxEntries = maxEntriesLocal;
            _config.Save();
        }

        if (ImGui.Button("Save Settings"))
        {
            _config.Save();
        }

        ImGui.Separator();

        ImGui.InputText("Search", ref _query, 512);
        ImGui.SameLine();
        ImGui.InputText("Role (user/assistant)", ref _role, 32);
        ImGui.SameLine();
        ImGui.InputText("Topic", ref _topic, 64);

        ImGui.SliderInt("Show Recent", ref _recent, 10, 2000);

        ImGui.Separator();
        if (ImGui.Button("Export JSONL"))
        {
            var path = _memory.ExportPath();
            _memory.ExportTo(path);
        }
        ImGui.SameLine();
        if (ImGui.Button("Delete All"))
        {
            _memory.RemoveWhere(_ => true);
        }

        ImGui.Separator();

        // Results
        IReadOnlyList<MemoryService.MemoryEntry> rows;
        if (!string.IsNullOrWhiteSpace(_query) || !string.IsNullOrWhiteSpace(_role) || !string.IsNullOrWhiteSpace(_topic))
            rows = _memory.Search(_query, _role, _topic).Take(_recent).ToList();
        else
            rows = _memory.GetRecent(_recent);

        if (ImGui.BeginChild("mem_list", new Vector2(0, 0), true))
        {
            foreach (var m in rows)
            {
                ImGui.PushStyleColor(ImGuiCol.ChildBg, m.Role == "user"
                    ? new Vector4(0.10f, 0.12f, 0.16f, 0.55f)
                    : new Vector4(0.20f, 0.00f, 0.20f, 0.45f));

                ImGui.BeginChild($"mem_{m.At.ToUnixTimeMilliseconds()}", new Vector2(0, 0), true);
                ImGui.TextDisabled($"{m.At.LocalDateTime:yyyy-MM-dd HH:mm:ss}  [{m.Role}]  {(string.IsNullOrEmpty(m.Topic) ? "" : $"#{m.Topic}")}");
                ImGui.TextWrapped(m.Content);
                ImGui.EndChild();
                ImGui.PopStyleColor();

                ImGui.SameLine();
                if (ImGui.SmallButton($"Delete##{m.At.ToUnixTimeMilliseconds()}"))
                {
                    _ = _memory.RemoveWhere(x => x.At == m.At && x.Content == m.Content);
                }

                ImGui.Spacing();
            }
        }
        ImGui.EndChild();
    }
}
