using System;
using System.Collections.Generic;
using System.Text;
using System.Numerics;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;

namespace NunuTheAICompanion.UI
{
    public sealed class ChatWindow : Window
    {
        private readonly Configuration _cfg;

        // Basic state
        private string _input = string.Empty;

        // History store (role: "user" | "assistant" | "system")
        private readonly List<(string role, string content)> _history = new();

        // Streaming buffer for assistant
        private bool _isStreaming;
        private readonly StringBuilder _streamBuf = new();

        // Auto-scroll flags
        private bool _scrollLeftToBottom;
        private bool _scrollRightToBottom;

        // Events
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
            // header toolbar
            DrawToolbar();

            ImGui.Separator();

            if (_cfg.TwoPaneMode)
                DrawTwoPaneLayout();
            else
                DrawSinglePaneLayout();

            ImGui.Separator();

            DrawInputBar();
        }

        // ============================= UI Pieces =============================

        private void DrawToolbar()
        {
            // left side: title-ish
            ImGui.TextDisabled("Speak, and I shall sing it back.");

            // right side: quick toggles
            var avail = ImGui.GetContentRegionAvail();
            ImGui.SameLine(avail.X - 280);

            bool tp = _cfg.TwoPaneMode;
            if (ImGui.Checkbox("Two-Pane", ref tp))
            {
                _cfg.TwoPaneMode = tp;
                _cfg.Save();
            }

            ImGui.SameLine();
            bool copyBtn = _cfg.ShowCopyButtons;
            if (ImGui.Checkbox("Copy Buttons", ref copyBtn))
            {
                _cfg.ShowCopyButtons = copyBtn;
                _cfg.Save();
            }

            ImGui.SameLine();
            float op = _cfg.WindowOpacity;
            if (ImGui.SliderFloat("Opacity", ref op, 0.25f, 1.0f))
            {
                _cfg.WindowOpacity = op;
                _cfg.Save();
            }
        }

        private void DrawTwoPaneLayout()
        {
            var full = ImGui.GetContentRegionAvail();
            var colGap = 10f;
            var colWidth = (full.X - colGap) * 0.5f;
            var height = full.Y - 80f; // leave room for input

            if (height < 100f) height = 100f;

            // Left pane – User
            ImGui.BeginChild("pane_user", new Vector2(colWidth, height), true, ImGuiWindowFlags.HorizontalScrollbar);
            {
                foreach (var (role, content) in _history)
                {
                    if (role != "user") continue;
                    DrawBubble(role, content, leftPane: true);
                }
                // Ensure bottom on new user lines
                if (_scrollLeftToBottom)
                {
                    ImGui.SetScrollHereY(1.0f);
                    _scrollLeftToBottom = false;
                }
            }
            ImGui.EndChild();

            ImGui.SameLine();
            ImGui.Dummy(new Vector2(colGap, 1));
            ImGui.SameLine();

            // Right pane – Assistant + System (including stream)
            ImGui.BeginChild("pane_assistant", new Vector2(colWidth, height), true, ImGuiWindowFlags.HorizontalScrollbar);
            {
                foreach (var (role, content) in _history)
                {
                    if (role == "assistant" || role == "system")
                        DrawBubble(role, content, leftPane: false);
                }

                if (_isStreaming)
                {
                    DrawBubble("assistant", _streamBuf.ToString(), leftPane: false, streaming: true);
                }

                if (_scrollRightToBottom)
                {
                    ImGui.SetScrollHereY(1.0f);
                    _scrollRightToBottom = false;
                }
            }
            ImGui.EndChild();
        }

        private void DrawSinglePaneLayout()
        {
            var full = ImGui.GetContentRegionAvail();
            var height = full.Y - 80f;
            if (height < 120f) height = 120f;

            ImGui.BeginChild("pane_single", new Vector2(-1, height), true, ImGuiWindowFlags.HorizontalScrollbar);
            {
                foreach (var (role, content) in _history)
                {
                    DrawBubble(role, content, leftPane: false);
                }
                if (_isStreaming)
                {
                    DrawBubble("assistant", _streamBuf.ToString(), leftPane: false, streaming: true);
                }
                // In single pane we follow assistant stream
                if (_isStreaming || _scrollRightToBottom)
                {
                    ImGui.SetScrollHereY(1.0f);
                    _scrollRightToBottom = false;
                }
            }
            ImGui.EndChild();
        }

        private void DrawInputBar()
        {
            // Input text (we use a UTF-8 buffer for Dalamud.Bindings.ImGui)
            var avail = ImGui.GetContentRegionAvail();
            var btnWidth = 100f;
            var inputWidth = MathF.Max(200f, avail.X - btnWidth - 8f);

            ImGui.PushItemWidth(inputWidth);
            if (InputTextLineUtf8("##nunu_input", ref _input))
            {
                // no-op on change
            }
            ImGui.PopItemWidth();

            ImGui.SameLine();
            if (ImGui.Button("Send", new Vector2(btnWidth, 0)))
                TrySend();

            // Enter to send
            if (ImGui.IsItemFocused() || ImGui.IsItemClicked())
            {
                // handled below
            }
            else if (ImGui.IsKeyPressed(ImGuiKey.Enter) && !ImGui.IsKeyDown(ImGuiKey.ModShift))
            {
                TrySend();
            }
        }

        private void TrySend()
        {
            var text = (_input ?? string.Empty).Trim();
            if (text.Length == 0) return;

            // Append to history as user
            _history.Add(("user", text));
            _scrollLeftToBottom = true;

            OnSend?.Invoke(text);

            _input = string.Empty;
        }

        private void DrawBubble(string role, string content, bool leftPane, bool streaming = false)
        {
            // color header
            var header = role switch
            {
                "user" => "You",
                "assistant" => "Nunu",
                "system" => "System",
                _ => role
            };

            ImGui.PushID($"{role}:{content.GetHashCode()}:{streaming}");

            // subtle header
            ImGui.TextColored(role == "system" ? new Vector4(0.7f, 0.7f, 0.85f, 1f)
                                               : role == "assistant" ? new Vector4(0.9f, 0.8f, 0.95f, 1f)
                                                                     : new Vector4(0.8f, 0.9f, 1f, 1f),
                              header);

            // body as wrapped text, with optional copy
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
                ImGui.TextDisabled(" …");
            }

            ImGui.Spacing();
            ImGui.PopID();
        }

        // =========================== Public API ============================

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
            if (!_isStreaming) return;
            if (!string.IsNullOrEmpty(chunk))
            {
                _streamBuf.Append(chunk);
                // keep view hugging the bottom
                _scrollRightToBottom = true;
            }
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

        // ============================ Utilities ============================

        private static string StripNonAscii(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var sb = new StringBuilder(s.Length);
            foreach (var ch in s)
            {
                if (ch <= 0x7F) sb.Append(ch);
            }
            return sb.ToString();
        }

        // Dalamud.Bindings.ImGui InputText wants UTF-8 byte buffer
        // Provide a simple line-edit helper that grows buffer as needed and round-trips to C# string.
        private static readonly int LineBufMin = 1024;

        private static bool InputTextLineUtf8(string label, ref string text)
        {
            var bytes = EncodingUtf8WithNul(text, Math.Max(LineBufMin, (text?.Length ?? 0) * 4 + 32));
            var changed = ImGui.InputText(label, bytes, ImGuiInputTextFlags.EnterReturnsTrue);
            if (changed)
            {
                text = BytesToUtf8String(bytes);
            }
            else
            {
                // Even when not “changed”, the user may have edited but not committed; still read back
                // to keep ref in sync with the buffer (ImGui returns true on validation-style, so do this always).
                text = BytesToUtf8String(bytes);
            }
            return changed;
        }

        private static byte[] EncodingUtf8WithNul(string? s, int size)
        {
            var buf = new byte[size];
            if (!string.IsNullOrEmpty(s))
            {
                var src = System.Text.Encoding.UTF8.GetBytes(s);
                var len = Math.Min(src.Length, size - 1);
                Array.Copy(src, buf, len);
                buf[len] = 0;
            }
            else
            {
                buf[0] = 0;
            }
            return buf;
        }

        private static string BytesToUtf8String(byte[] buf)
        {
            var len = Array.IndexOf(buf, (byte)0);
            if (len < 0) len = buf.Length;
            return System.Text.Encoding.UTF8.GetString(buf, 0, len);
        }
    }
}
