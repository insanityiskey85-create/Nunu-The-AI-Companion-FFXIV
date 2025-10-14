using System;
using System.IO;
using System.Numerics;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using Dalamud.Bindings.ImGui;
using NunuTheAICompanion.Services;

namespace NunuTheAICompanion.UI
{
    /// <summary>
    /// UI for browsing and managing persistent memory.
    /// Constructor matches PluginMain usage: (Configuration, MemoryService?, IPluginLog)
    /// </summary>
    public sealed class MemoryWindow : Window
    {
        private readonly Configuration _cfg;
        private readonly MemoryService? _mem;
        private readonly IPluginLog _log;

        // UI state
        private int _previewCount = 64;
        private string _exportPath = string.Empty;
        private string _importPath = string.Empty;
        private bool _autoFlush = true;

        public MemoryWindow(Configuration cfg, MemoryService? mem, IPluginLog log)
            // IMPORTANT: use a Window constructor that takes title + flags (and optional forceMainWindow)
            : base("Nunu Memories", ImGuiWindowFlags.None, true)
        {
            _cfg = cfg;
            _mem = mem;
            _log = log;

            SizeConstraints = new WindowSizeConstraints
            {
                MinimumSize = new Vector2(480, 320),
                MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
            };

            try
            {
                var dir = _mem?.StorageDirectory
                          ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "NunuMemories");
                Directory.CreateDirectory(dir);
                _exportPath = Path.Combine(dir, "memory_export.jsonl");
                _importPath = _exportPath;
            }
            catch
            {
                _exportPath = "memory_export.jsonl";
                _importPath = "memory_export.jsonl";
            }
        }

        public override void Draw()
        {
            if (_mem is null)
            {
                ImGui.TextColored(new Vector4(1f, 0.5f, 0.5f, 1f), "Memory service not available.");
                return;
            }

            // Header
            ImGui.Text($"Enabled: {(_mem.Enabled ? "yes" : "no")}");
            ImGui.SameLine();
            ImGui.Checkbox("Auto-flush on actions", ref _autoFlush);

            // Actions row
            if (ImGui.Button("Refresh"))
            {
                Try(() => { _mem.Load(); if (_autoFlush) _mem.Flush(); }, "[Memory] refresh failed");
            }
            ImGui.SameLine();
            if (ImGui.Button("Clear All"))
            {
                Try(() => { _mem.ClearAll(); if (_autoFlush) _mem.Flush(); }, "[Memory] clear failed");
            }
            ImGui.SameLine();
            if (ImGui.Button("Flush"))
            {
                Try(() => _mem.Flush(), "[Memory] flush failed");
            }

            ImGui.Separator();

            // Export
            ImGui.Text("Export to:");
            ImGui.SameLine();
            ImGui.InputText("##export_path", ref _exportPath, 2048);
            ImGui.SameLine();
            if (ImGui.Button("Export"))
            {
                Try(() =>
                {
                    var p = string.IsNullOrWhiteSpace(_exportPath) ? "memory_export.jsonl" : _exportPath;
                    var dir = Path.GetDirectoryName(p);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                        Directory.CreateDirectory(dir);
                    _mem.ExportTo(p);
                }, "[Memory] export failed");
            }

            // Import
            ImGui.Text("Import from:");
            ImGui.SameLine();
            ImGui.InputText("##import_path", ref _importPath, 2048);
            ImGui.SameLine();
            if (ImGui.Button("Import (append)"))
            {
                Try(() =>
                {
                    var p = string.IsNullOrWhiteSpace(_importPath) ? "memory_export.jsonl" : _importPath;
                    _mem.ImportFrom(p, keepExisting: true);
                    if (_autoFlush) _mem.Flush();
                }, "[Memory] import (append) failed");
            }
            ImGui.SameLine();
            if (ImGui.Button("Import (replace)"))
            {
                Try(() =>
                {
                    var p = string.IsNullOrWhiteSpace(_importPath) ? "memory_export.jsonl" : _importPath;
                    _mem.ImportFrom(p, keepExisting: false);
                    if (_autoFlush) _mem.Flush();
                }, "[Memory] import (replace) failed");
            }

            ImGui.Separator();

            // Preview slider
            ImGui.SliderInt("Preview count", ref _previewCount, 4, 512);

            // Preview list
            Try(() =>
            {
                var all = _mem.Snapshot(); // assumes entries with Role + Content
                var start = Math.Max(0, all.Count - _previewCount);

                ImGui.BeginChild("mem_scroll", new Vector2(0, -8), true);

                for (int i = start; i < all.Count; i++)
                {
                    var entry = all[i];
                    var role = entry.Role ?? "unknown";
                    var content = entry.Content ?? string.Empty;

                    var color = role.Equals("assistant", StringComparison.OrdinalIgnoreCase)
                        ? new Vector4(0.70f, 0.90f, 1f, 1f)
                        : new Vector4(0.95f, 0.95f, 0.95f, 1f);

                    ImGui.TextColored(color, $"[{i}] {role}:");
                    ImGui.TextWrapped(content);
                    ImGui.Separator();
                }

                ImGui.EndChild();
            }, "[Memory] preview failed");
        }

        private void Try(Action act, string warnTag)
        {
            try { act(); }
            catch (Exception ex) { _log.Warning(ex, warnTag); }
        }
    }
}
