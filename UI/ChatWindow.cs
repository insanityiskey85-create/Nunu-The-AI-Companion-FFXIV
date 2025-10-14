using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace NunuTheAICompanion.UI
{
    public sealed class ChatWindow : Window
    {
        private readonly Configuration _config;

        // streaming buffer
        private bool _isStreaming;
        private string _streamBuf = "";

        // user input buffer (FIX: was a static local; now a field)
        private string _inputBuf = string.Empty;

        // mood tint
        private Vector4 _moodColor = new Vector4(1f, 1f, 1f, 1f);

        public event Action<string>? OnSend;

        public ChatWindow(Configuration config)
            : base("Little Nunu — The Soul Weeper")
        {
            _config = config;
            RespectCloseHotkey = true;
            Size = new Vector2(700, 500);
            SizeCondition = ImGuiCond.FirstUseEver;
        }

        // ===== Streaming helpers =====
        public void BeginAssistantStream()
        {
            _isStreaming = true;
            _streamBuf = string.Empty;
        }

        public void AppendAssistantDelta(string delta)
        {
            _streamBuf += delta ?? string.Empty;
        }

        public void EndAssistantStream()
        {
            _isStreaming = false;
        }

        public void AppendAssistant(string text)
        {
            _streamBuf = text ?? string.Empty;
            _isStreaming = false;
        }

        public void AppendSystem(string text)
        {
            _streamBuf = text ?? string.Empty;
        }

        public void AddSystemLine(string text) => AppendSystem(text);

        public void SetMoodColor(Vector4 color) => _moodColor = color;

        // ===== Draw =====
        public override void Draw()
        {
            try
            {
                // Header with mood tint block
                var tint = _moodColor;
                var sz = new Vector2(12, 12);
                var cursor = ImGui.GetCursorScreenPos();
                var dl = ImGui.GetWindowDrawList();
                dl.AddRectFilled(cursor, cursor + sz, ImGui.ColorConvertFloat4ToU32(tint));
                ImGui.Dummy(new Vector2(16, 14));
                ImGui.SameLine();
                ImGui.TextUnformatted("Little Nunu");

                ImGui.Separator();

                // Two-pane layout
                ImGui.Columns(2, "nunuCols", true);

                // LEFT: input
                ImGui.TextUnformatted(_config.ChatDisplayName ?? "You");
                ImGui.Separator();

                ImGui.InputTextMultiline("##n_input", ref _inputBuf, 4096, new Vector2(-1, 140));
                if (ImGui.Button("Send"))
                {
                    var t = _inputBuf?.Trim();
                    if (!string.IsNullOrEmpty(t))
                    {
                        try { OnSend?.Invoke(t); } catch { /* swallow UI errors */ }
                        _inputBuf = string.Empty;
                    }
                }

                ImGui.NextColumn();

                // RIGHT: assistant/output
                ImGui.TextUnformatted("Little Nunu");
                ImGui.Separator();
                var text = _isStreaming ? _streamBuf + "▌" : _streamBuf;
                ImGui.TextWrapped(text ?? "");

                ImGui.Columns(1);
            }
            catch
            {
                // never throw inside Draw
            }
        }
    }
}
