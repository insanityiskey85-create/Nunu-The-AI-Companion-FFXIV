using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace NunuTheAICompanion.UI
{
    public sealed class ChatWindow : Window
    {
        private readonly Configuration _cfg;

        // Chat buffers
        private readonly List<string> _you = new();
        private readonly List<string> _nunu = new();
        private readonly List<string> _system = new();

        // Streaming state
        private bool _isStreaming;
        private string _streamBuf = string.Empty;

        // Input
        private string _input = string.Empty;

        public event Action<string>? OnSend;

        public ChatWindow(Configuration cfg)
            : base("Little Nunu — Soul-Weeper Chat", ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar)
        {
            _cfg = cfg;

            Size = new Vector2(920, 560);
            SizeCondition = ImGuiCond.FirstUseEver;

            // prevent resizing into oblivion
            SizeConstraints = new WindowSizeConstraints
            {
                MinimumSize = new Vector2(600, 360),
                MaximumSize = new Vector2(4096, 4096),
            };
        }

        // ===== API used by PluginMain =====

        public void AppendSystem(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            foreach (var line in SplitLines(text))
                _system.Add(line);
        }

        public void AddSystemLine(string line)
        {
            if (!string.IsNullOrEmpty(line))
                _system.Add(line);
        }

        public void AppendAssistant(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            _nunu.Add(text);
        }

        public void BeginAssistantStream()
        {
            _isStreaming = true;
            _streamBuf = string.Empty;
        }

        public void AppendAssistantDelta(string delta)
        {
            if (!_isStreaming || string.IsNullOrEmpty(delta)) return;
            _streamBuf += delta;
        }

        public void EndAssistantStream()
        {
            if (!_isStreaming) return;
            _isStreaming = false;
            if (!string.IsNullOrEmpty(_streamBuf))
                _nunu.Add(_streamBuf);
            _streamBuf = string.Empty;
        }

        // ===== Window lifecycle =====

        public override bool DrawConditions()
        {
            if (PluginMain.IsShuttingDown) return false;
            return base.DrawConditions();
        }

        public override void Draw()
        {
            if (PluginMain.IsShuttingDown) return;

            // Respect UI prefs
            var alpha = Math.Clamp(_cfg.WindowOpacity, 0.3f, 1.0f);
            ImGui.PushStyleVar(ImGuiStyleVar.Alpha, alpha);

            var prevScale = ImGui.GetFontSize();
            ImGui.SetWindowFontScale(Math.Clamp(_cfg.FontScale, 0.8f, 1.8f));

            DrawSystemStrip();
            ImGui.Separator();

            if (_cfg.TwoPaneMode)
                DrawTwoPane();
            else
                DrawSinglePane();

            ImGui.Separator();
            DrawComposer();

            // Footer utilities
            if (_cfg.ShowCopyButtons)
            {
                if (ImGui.Button("Copy last Nunu"))
                {
                    var last = _isStreaming ? _streamBuf : _nunu.LastOrDefault() ?? "";
                    ImGui.SetClipboardText(last);
                }
                ImGui.SameLine();
                if (ImGui.Button("Copy last You"))
                {
                    var last = _you.LastOrDefault() ?? "";
                    ImGui.SetClipboardText(last);
                }
            }

            // Restore UI vars
            ImGui.SetWindowFontScale(1f);
            ImGui.PopStyleVar();
        }

        // ===== Sections =====

        private void DrawSystemStrip()
        {
            // Magenta header area for system lines
            var avail = ImGui.GetContentRegionAvail();
            var height = MathF.Min(120f, MathF.Max(24f, avail.Y * 0.25f));
            if (ImGui.BeginChild("##sys_strip", new Vector2(-1, height), true))
            {
                // luminous magenta-ish title
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 0.65f, 0.95f, 1f));
                ImGui.TextUnformatted("— Nunu System —");
                ImGui.PopStyleColor();

                ImGui.PushTextWrapPos(0);
                foreach (var line in _system.TakeLast(200)) // cap to avoid megaspam
                    ImGui.TextUnformatted(line);
                if (ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - 4f)
                    ImGui.SetScrollHereY(1.0f);
                ImGui.PopTextWrapPos();
            }
            ImGui.EndChild();
        }

        private void DrawTwoPane()
        {
            var avail = ImGui.GetContentRegionAvail();
            float split = MathF.Floor(avail.X * 0.5f) - 6f;

            // Left: You
            if (ImGui.BeginChild("##you_pane", new Vector2(split, -1), true))
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.7f, 0.7f, 1f, 1f));
                ImGui.TextUnformatted((_cfg.ChatDisplayName?.Length ?? 0) > 0 ? _cfg.ChatDisplayName! : "You");
                ImGui.PopStyleColor();
                ImGui.Separator();

                ImGui.PushTextWrapPos(0);
                foreach (var line in _you.TakeLast(500))
                    ImGui.TextUnformatted(line);
                if (ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - 4f)
                    ImGui.SetScrollHereY(1.0f);
                ImGui.PopTextWrapPos();
            }
            ImGui.EndChild();

            ImGui.SameLine();

            // Right: Nunu
            if (ImGui.BeginChild("##nunu_pane", new Vector2(0, -1), true))
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 0.35f, 0.85f, 1f)); // luminous magenta
                ImGui.TextUnformatted("Nunu");
                ImGui.PopStyleColor();
                ImGui.Separator();

                ImGui.PushTextWrapPos(0);
                foreach (var line in _nunu.TakeLast(500))
                    ImGui.TextUnformatted(line);

                if (_isStreaming && !string.IsNullOrEmpty(_streamBuf))
                {
                    ImGui.Separator();
                    ImGui.TextUnformatted(_streamBuf);
                }

                if (ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - 4f)
                    ImGui.SetScrollHereY(1.0f);
                ImGui.PopTextWrapPos();
            }
            ImGui.EndChild();
        }

        private void DrawSinglePane()
        {
            if (ImGui.BeginChild("##single_pane", new Vector2(-1, -1), true))
            {
                ImGui.PushTextWrapPos(0);

                // Show as transcript, tagging speakers
                int u = Math.Max(0, _you.Count - 500);
                int n = Math.Max(0, _nunu.Count - 500);

                // Interleave naive (You lines first then Nunu lines); this mode is just a compact dump
                for (int i = u; i < _you.Count; i++)
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.7f, 0.7f, 1f, 1f));
                    ImGui.TextUnformatted($"You: {_you[i]}");
                    ImGui.PopStyleColor();
                }
                for (int j = n; j < _nunu.Count; j++)
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 0.35f, 0.85f, 1f));
                    ImGui.TextUnformatted($"Nunu: {_nunu[j]}");
                    ImGui.PopStyleColor();
                }

                if (_isStreaming && !string.IsNullOrEmpty(_streamBuf))
                {
                    ImGui.Separator();
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 0.35f, 0.85f, 1f));
                    ImGui.TextUnformatted($"Nunu (typing…): {_streamBuf}");
                    ImGui.PopStyleColor();
                }

                if (ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - 4f)
                    ImGui.SetScrollHereY(1.0f);

                ImGui.PopTextWrapPos();
            }
            ImGui.EndChild();
        }

        private void DrawComposer()
        {
            // Big multiline input area + Send button
            var height = 120f;
            if (ImGui.InputTextMultiline("##compose", ref _input, 4000, new Vector2(-160, height)))
            {
                // just edit
            }
            ImGui.SameLine();

            if (ImGui.Button("Send", new Vector2(140, height)))
            {
                Submit();
            }

            // Also send with Ctrl+Enter to avoid conflicting with multiline Enter
            // (Enter is kept for newlines in multiline)
            if (ImGui.IsItemFocused() || ImGui.IsItemHovered())
            {
                // no-op
            }

            // Keyboard shortcut: Ctrl+Enter anywhere
            bool ctrl = ImGui.GetIO().KeyCtrl;
            if (ctrl && ImGui.IsKeyPressed(ImGuiKey.Enter))
                Submit();
        }

        private void Submit()
        {
            var text = (_input ?? string.Empty).Trim();
            if (text.Length == 0) return;

            _you.Add(text);
            try { OnSend?.Invoke(text); }
            catch { /* UI must never throw */ }

            _input = string.Empty;
        }

        // ===== helpers =====

        private static IEnumerable<string> SplitLines(string s)
        {
            if (string.IsNullOrEmpty(s)) yield break;

            // split CRLF/CR/LF
            int i = 0;
            while (i < s.Length)
            {
                int j = s.IndexOfAny(new[] { '\r', '\n' }, i);
                if (j < 0)
                {
                    yield return s[i..];
                    yield break;
                }

                yield return s[i..j];

                // consume possible CRLF
                if (j + 1 < s.Length && s[j] == '\r' && s[j + 1] == '\n') i = j + 2;
                else i = j + 1;
            }
        }
    }
}
