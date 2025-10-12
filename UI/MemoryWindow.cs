using System;
using System.Numerics;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using NunuTheAICompanion.Services;
using Dalamud.Plugin.Services;

namespace NunuTheAICompanion.UI;

public sealed class MemoryWindow : Window
{
    private readonly Configuration _cfg;
    private readonly MemoryService? _mem;
    private readonly IPluginLog _log;

    private string _importPath = string.Empty;
    private string _exportPathShown = string.Empty;
    private Vector2 _tableSize = new(0, 360);

    public MemoryWindow(Configuration cfg, MemoryService? mem, IPluginLog log)
        : base("Nunu – Memories", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        _cfg = cfg;
        _mem = mem;
        _log = log;
        Size = new Vector2(680, 520);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        if (_mem is null)
        {
            ImGui.TextColored(new System.Numerics.Vector4(1, 0.6f, 0.6f, 1), "Memory service unavailable in this build.");
            return;
        }

        ImGui.TextUnformatted($"Durable Memory: {(_mem.Enabled ? "ENABLED" : "DISABLED")}  ({_mem.StorageFile})");
        ImGui.Separator();

        if (ImGui.Button("Refresh"))
        {
            try { _mem.Load(); } catch (Exception ex) { _log.Error(ex, "Memory refresh error"); }
        }
        ImGui.SameLine();
        if (ImGui.Button("Export"))
        {
            try { _exportPathShown = _mem.ExportTo(); }
            catch (Exception ex) { _exportPathShown = $"[export error] {ex.Message}"; _log.Error(ex, "Memory export error"); }
        }
        ImGui.SameLine();
        if (ImGui.Button("Clear All"))
        {
            if (_mem.Enabled)
            {
                try { _mem.ClearAll(); } catch (Exception ex) { _log.Error(ex, "Memory clear error"); }
            }
        }

        if (!string.IsNullOrEmpty(_exportPathShown))
        {
            ImGui.SameLine();
            ImGui.TextDisabled(_exportPathShown);
        }

        ImGui.Spacing();
        ImGui.InputText("Import .jsonl path", ref _importPath, 512);
        ImGui.SameLine();
        if (ImGui.Button("Import (append)"))
        {
            try { var count = _mem.ImportFrom(_importPath, keepExisting: true); _exportPathShown = $"Imported {count} lines."; }
            catch (Exception ex) { _exportPathShown = $"[import error] {ex.Message}"; _log.Error(ex, "Memory import error"); }
        }
        ImGui.SameLine();
        if (ImGui.Button("Import (replace)"))
        {
            try { var count = _mem.ImportFrom(_importPath, keepExisting: false); _exportPathShown = $"Replaced with {count} lines."; }
            catch (Exception ex) { _exportPathShown = $"[import error] {ex.Message}"; _log.Error(ex, "Memory import error"); }
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.TextUnformatted("Recent entries (tail):");

        var entries = _mem.Snapshot();
        if (ImGui.BeginTable("mem_table", 4, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY, _tableSize))
        {
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableSetupColumn("Time (UTC)", 0, 0.18f);
            ImGui.TableSetupColumn("Role", 0, 0.12f);
            ImGui.TableSetupColumn("Topic", 0, 0.18f);
            ImGui.TableSetupColumn("Content", 0, 0.52f);
            ImGui.TableHeadersRow();

            foreach (var e in entries)
            {
                ImGui.TableNextRow();

                ImGui.TableSetColumnIndex(0);
                ImGui.TextUnformatted(e.TimestampUtc.ToString("yyyy-MM-dd HH:mm:ss"));

                ImGui.TableSetColumnIndex(1);
                ImGui.TextUnformatted(e.Role ?? "");

                ImGui.TableSetColumnIndex(2);
                ImGui.TextUnformatted(e.Topic ?? "");

                ImGui.TableSetColumnIndex(3);
                ImGui.TextWrapped(e.Content ?? "");
            }

            ImGui.EndTable();
        }

        ImGui.Spacing();
        ImGui.SliderFloat("Table Height", ref _tableSize.Y, 200f, 800f, "%.0f px");
    }
}
