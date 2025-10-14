using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace NunuTheAICompanion.UI
{
    public sealed class ChatWindow : Window
    {
        private readonly Configuration _cfg;

        private string _input = string.Empty;
        private readonly List<(string role, string content)> _history = new();

        // Streaming buffer
        private bool _isStreaming;
        private readonly StringBuilder _streamBuf = new();

        private bool _scrollLeftToBottom;
        private bool _scrollRightToBottom;

        public event Action<string>? OnSend;

        public ChatWindow(Configuration cfg)
            : base("Nunu — Chat", ImGuiWindowFlags.None, true)
        {
            _cfg = cfg;

            SizeConstraints = new WindowSizeConstraints
            {
                MinimumSize = new Vector2(560, 420),
                MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
            };

            RespectCloseHotkey = true;
            IsOpen = _cfg.StartOpen;
        }

        public override void Draw()
        {
            DrawToolbar();
            ImGui.Separator();

            if (_cfg.TwoPaneMode) DrawTwoPane();
            else DrawOnePane();

            ImGui.Separator();
            DrawInput();
        }

        // ==================== Toolbar ====================
        private void DrawToolbar()
        {
            ImGui.TextDisabled("Speak, and I shall sing it back.");
            var avail = ImGui.GetContentRegionAvail();
            ImGui.SameLine(Math.Max(0, avail.X - 280));

            bool tp = _cfg.TwoPaneMode;
            if (ImGui.Checkbox("Two-Pane", ref tp)) { _cfg.TwoPaneMode = tp; _cfg.Save(); }

            ImGui.SameLine();
            bool copy = _cfg.ShowCopyButtons;
            if (ImGui.Checkbox("Copy Buttons", ref copy)) { _cfg.ShowCopyButtons = copy; _cfg.Save(); }

            ImGui.SameLine();
            float op = _cfg.WindowOpacity;
            if (ImGui.SliderFloat("Opacity", ref op, 0.25f, 1.0f)) { _cfg.WindowOpacity = op; _cfg.Save(); }
        }

        // ==================== Layouts ====================
        private void DrawTwoPane()
        {
            var full = ImGui.GetContentRegionAvail();
            var gap = 10f;
            var colWidth = (full.X - gap) * 0.5f;
            var height = MathF.Max(100f, full.Y - 80f);

            // Left (user)
            ImGui.BeginChild("pane_user", new Vector2(colWidth, height), true, ImGuiWindowFlags.HorizontalScrollbar);
            foreach (var (role, content) in _history)
                if (role == "user") DrawBubble(role, content);
            if (_scrollLeftToBottom) { ImGui.SetScrollHereY(1.0f); _scrollLeftToBottom = false; }
            ImGui.EndChild();

            ImGui.SameLine(); ImGui.Dummy(new Vector2(gap, 1)); ImGui.SameLine();

            // Right (assistant + system + stream)
            ImGui.BeginChild("pane_assistant", new Vector2(colWidth, height), true, ImGuiWindowFlags.HorizontalScrollbar);
            foreach (var (role, content) in _history)
                if (role is "assistant" or "system") DrawBubble(role, content);

            if (_isStreaming)
                DrawBubble("assistant", _streamBuf.ToString(), streaming: true);

            if (_scrollRightToBottom) { ImGui.SetScrollHereY(1.0f); _scrollRightToBottom = false; }
            ImGui.EndChild();
        }

        private void DrawOnePane()
        {
            var full = ImGui.GetContentRegionAvail();
            var height = MathF.Max(120f, full.Y - 80f);

            ImGui.BeginChild("pane_single", new Vector2(-1, height), true, ImGuiWindowFlags.HorizontalScrollbar);
            foreach (var (role, content) in _history) DrawBubble(role, content);
            if (_isStreaming)
                DrawBubble("assistant", _streamBuf.ToString(), streaming: true);

            if (_isStreaming || _scrollRightToBottom) { ImGui.SetScrollHereY(1.0f); _scrollRightToBottom = false; }
            ImGui.EndChild();
        }

        // ==================== Input ====================
        private void DrawInput()
        {
            var avail = ImGui.GetContentRegionAvail();
            var btnW = 100f;
            var inputW = MathF.Max(200f, avail.X - btnW - 8f);

            ImGui.PushItemWidth(inputW);
            if (InputTextLineUtf8("##nunu_input", ref _input)) { /* live update ok */ }
            ImGui.PopItemWidth();

            ImGui.SameLine();
            if (ImGui.Button("Send", new Vector2(btnW, 0)))
                TrySend();

            if (ImGui.IsKeyPressed(ImGuiKey.Enter) && !ImGui.IsKeyDown(ImGuiKey.ModShift))
                TrySend();
        }

        private void TrySend()
        {
            var text = (_input ?? string.Empty).Trim();
            if (text.Length == 0) return;

            _history.Add(("user", text));
            _scrollLeftToBottom = true;
            OnSend?.Invoke(text);
            _input = string.Empty;
        }

        // ==================== Bubbles ====================
        private void DrawBubble(string role, string content, bool streaming = false)
        {
            var who = role switch
            {
                "user" => "You",
                "assistant" => "Nunu",
                "system" => "System",
                _ => role
            };

            ImGui.PushID($"{role}:{content.GetHashCode()}:{(streaming ? 1 : 0)}");

            var color = role == "system" ? new Vector4(0.7f, 0.7f, 0.85f, 1f)
                      : role == "assistant" ? new Vector4(0.9f, 0.8f, 0.95f, 1f)
                                            : new Vector4(0.8f, 0.9f, 1f, 1f);

            ImGui.TextColored(color, who);

            if (_cfg.ShowCopyButtons)
            {
                if (ImGui.SmallButton("Copy"))
                {
                    var toCopy = _cfg.AsciiSafe ? StripNonAscii(content) : content;
                    ImGui.SetClipboardText(toCopy);
                }
                ImGui.SameLine();
            }

            ImGui.BeginGroup();
            ImGui.PushTextWrapPos(0);
            ImGui.TextUnformatted(content ?? string.Empty);
            ImGui.PopTextWrapPos();
            ImGui.EndGroup();

            if (streaming)
            {
                ImGui.SameLine();
                ImGui.TextDisabled("♪");
            }

            ImGui.Spacing();
            ImGui.PopID();
        }

        // ==================== Public API ====================
        public void AppendAssistant(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            _history.Add(("assistant", text));
            _scrollRightToBottom = true;
        }

        public void AppendSystem(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            _history.Add(("system", text));
            _scrollRightToBottom = true;
        }

        public void AddSystemLine(string text) => AppendSystem(text);

        public void BeginAssistantStream()
        {
            _isStreaming = true;
            _streamBuf.Clear();
            _scrollRightToBottom = true;
        }

        public void AppendAssistantDelta(string chunk)
        {
            if (!_isStreaming || string.IsNullOrEmpty(chunk)) return;
            _streamBuf.Append(chunk);
            _scrollRightToBottom = true;
        }

        public void EndAssistantStream()
        {
            if (!_isStreaming) return;
            _isStreaming = false;

            var final = _streamBuf.ToString();
            _streamBuf.Clear();
            if (!string.IsNullOrEmpty(final))
            {
                _history.Add(("assistant", final));
                _scrollRightToBottom = true;
            }
        }

        // ==================== Helpers ====================
        private static string StripNonAscii(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var sb = new StringBuilder(s.Length);
            foreach (var ch in s) if (ch <= 0x7F) sb.Append(ch);
            return sb.ToString();
        }

        // Dalamud.Bindings.ImGui -> UTF-8 buffer single-line input
        private static readonly int LineBufMin = 1024;

        private static bool InputTextLineUtf8(string label, ref string text)
        {
            var bytes = EncodingUtf8WithNul(text, Math.Max(LineBufMin, (text?.Length ?? 0) * 4 + 32));
            var changed = ImGui.InputText(label, bytes, ImGuiInputTextFlags.EnterReturnsTrue);
            text = BytesToUtf8String(bytes);
            return changed;
        }

        private static byte[] EncodingUtf8WithNul(string? s, int size)
        {
            var buf = new byte[size];
            if (!string.IsNullOrEmpty(s))
            {
                var src = Encoding.UTF8.GetBytes(s);
                var len = Math.Min(src.Length, size - 1);
                Array.Copy(src, buf, len);
                buf[len] = 0;
            }
            else buf[0] = 0;
            return buf;
        }

        private static string BytesToUtf8String(byte[] buf)
        {
            var len = Array.IndexOf(buf, (byte)0);
            if (len < 0) len = buf.Length;
            return Encoding.UTF8.GetString(buf, 0, len);
        }
    }
}
