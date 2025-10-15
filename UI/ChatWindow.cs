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

        // --- model for lines ---
        private enum LineKind { User, Assistant, System }

        private readonly List<(LineKind kind, string text)> _left = new();   // user pane
        private readonly List<(LineKind kind, string text)> _right = new();  // assistant pane
        private readonly List<string> _system = new();                       // banner/system log

        // --- composer/input state ---
        private string _input = string.Empty;
        private bool _focusInputNext = false;

        // --- streaming state ---
        private bool _isStreaming = false;
        private readonly StringBuilder _streamBuf = new();
        private readonly float _panePad = 8f;

        // --- scroll state ---
        private bool _scrollLeftToEnd = false;
        private bool _scrollRightToEnd = false;

        // --- colors (luminous magenta theme) ---
        private readonly Vector4 _magenta = new(1.00f, 0.20f, 0.90f, 1.00f); // accent
        private readonly Vector4 _magentaDim = new(0.90f, 0.35f, 0.85f, 1.00f);
        private readonly Vector4 _assistantText = new(0.96f, 0.88f, 0.98f, 1.00f);
        private readonly Vector4 _userText = new(0.95f, 0.95f, 0.95f, 1.00f);
        private readonly Vector4 _systemText = new(0.95f, 0.80f, 0.98f, 1.00f);

        public event Action<string>? OnSend;

        public ChatWindow(Configuration cfg)
            : base("Little Nunu — Soul-Weeper Chat", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoCollapse)
        {
            _cfg = cfg;

            this.SizeConstraints = new WindowSizeConstraints
            {
                MinimumSize = new Vector2(680, 460),
                MaximumSize = new Vector2(9999, 9999)
            };
        }

        public override bool DrawConditions()
        {
            // apply global look each frame
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 12f);
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 10f);

            // font scale & opacity
            if (_cfg.FontScale > 0f)
                ImGui.SetWindowFontScale(_cfg.FontScale);

            if (_cfg.WindowOpacity > 0f && _cfg.WindowOpacity < 1f)
                ImGui.PushStyleVar(ImGuiStyleVar.Alpha, _cfg.WindowOpacity);

            // lock window
            if (_cfg.LockWindow)
                Flags |= ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize;
            else
                Flags &= ~(ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize);

            return base.DrawConditions();
        }

        public override void PostDraw()
        {
            // pop style vars pushed in DrawConditions
            // order matters (LIFO):
            if (_cfg.WindowOpacity > 0f && _cfg.WindowOpacity < 1f) ImGui.PopStyleVar(); // Alpha
            ImGui.PopStyleVar(); // FrameRounding
            ImGui.PopStyleVar(); // WindowRounding
        }

        public override void Draw()
        {
            // header bar
            DrawHeader();

            // panes
            if (_cfg.TwoPaneMode)
                DrawTwoPane();
            else
                DrawSinglePane();

            // composer
            ImGui.Separator();
            DrawComposer();
        }

        // ========================= Header =========================

        private void DrawHeader()
        {
            ImGui.PushStyleColor(ImGuiCol.Text, _magenta);
            ImGui.TextUnformatted("♪ Nunu’s Luminous Thread ♪");
            ImGui.PopStyleColor();

            if (_system.Count > 0)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, _systemText);
                foreach (var s in _system)
                {
                    // subtle bullet
                    ImGui.TextUnformatted($"› {s}");
                }
                ImGui.PopStyleColor();
            }

            ImGui.Dummy(new Vector2(0, 2));
        }

        // ========================= Panes =========================

        private void DrawTwoPane()
        {
            var avail = ImGui.GetContentRegionAvail();
            var half = new Vector2(avail.X * 0.5f - _panePad * 0.5f, avail.Y - 120f);

            // left: user
            ImGui.BeginGroup();
            DrawPaneBox("You", _userText, _left, ref _scrollLeftToEnd, half);
            ImGui.EndGroup();

            ImGui.SameLine(0, _panePad);

            // right: nunu
            ImGui.BeginGroup();
            DrawPaneBox("Nunu", _assistantText, _right, ref _scrollRightToEnd, half, isRight: true);
            ImGui.EndGroup();
        }

        private void DrawSinglePane()
        {
            var avail = ImGui.GetContentRegionAvail();
            var size = new Vector2(avail.X, avail.Y - 120f);

            ImGui.PushStyleColor(ImGuiCol.ChildBg, _magentaDim with { W = 0.10f });
            ImGui.BeginChild("##nunu_single_pane", size, true);

            // merge lines in order-ish: render system, then user/assistant interleaved by storage index
            // simplest: display user then assistant chronologically in their own blocks
            ImGui.PushStyleColor(ImGuiCol.Text, _userText);
            foreach (var (kind, text) in _left)
            {
                if (kind == LineKind.User)
                    RenderBubble("You", text, left: true);
            }
            ImGui.PopStyleColor();

            ImGui.Separator();

            ImGui.PushStyleColor(ImGuiCol.Text, _assistantText);
            foreach (var (kind, text) in _right)
            {
                if (kind == LineKind.Assistant)
                    RenderBubble("Nunu", text, left: false);
            }
            ImGui.PopStyleColor();

            if (_isStreaming && _streamBuf.Length > 0)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, _assistantText);
                RenderBubble("Nunu (typing…)", _streamBuf.ToString(), left: false, streaming: true);
                ImGui.PopStyleColor();
            }

            if (ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - 2)
                ImGui.SetScrollHereY();

            ImGui.EndChild();
            ImGui.PopStyleColor();
        }

        private void DrawPaneBox(string title, Vector4 textColor, List<(LineKind, string)> lines,
            ref bool scrollEnd, Vector2 size, bool isRight = false)
        {
            // title
            ImGui.PushStyleColor(ImGuiCol.Text, _magenta);
            ImGui.TextUnformatted(title);
            ImGui.PopStyleColor();

            ImGui.PushStyleColor(ImGuiCol.ChildBg, _magentaDim with { W = 0.08f });
            ImGui.BeginChild($"##pane_{title}", size, true);

            ImGui.PushStyleColor(ImGuiCol.Text, textColor);

            foreach (var (kind, text) in lines)
            {
                switch (kind)
                {
                    case LineKind.User:
                        RenderBubble(_cfg.ChatDisplayName?.Length > 0 ? _cfg.ChatDisplayName! : "You", text, left: true);
                        break;
                    case LineKind.Assistant:
                        RenderBubble("Nunu", text, left: false);
                        break;
                    case LineKind.System:
                        ImGui.PushStyleColor(ImGuiCol.Text, _systemText);
                        RenderSystemLine(text);
                        ImGui.PopStyleColor();
                        break;
                }
            }

            // streaming ghost on assistant pane
            if (isRight && _isStreaming && _streamBuf.Length > 0)
                RenderBubble("Nunu (writing…)", _streamBuf.ToString(), left: false, streaming: true);

            if (scrollEnd || ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - 2)
            {
                ImGui.SetScrollHereY(1.0f);
                scrollEnd = false;
            }

            ImGui.PopStyleColor(); // text
            ImGui.EndChild();
            ImGui.PopStyleColor(); // child bg
        }

        // ========================= Composer =========================

        private void DrawComposer()
        {
            var availX = ImGui.GetContentRegionAvail().X;

            // input
            if (_focusInputNext)
            {
                ImGui.SetKeyboardFocusHere();
                _focusInputNext = false;
            }

            var inputId = "##nunu_input";
            var height = 3 * ImGui.GetTextLineHeightWithSpacing() + 6f;
            ImGui.PushStyleColor(ImGuiCol.FrameBg, _magentaDim with { W = 0.15f });
            ImGui.InputTextMultiline(inputId, ref _input, 4000, new Vector2(availX - 140f, height), ImGuiInputTextFlags.None);
            ImGui.PopStyleColor();

            ImGui.SameLine();

            // send button
            ImGui.PushStyleColor(ImGuiCol.Button, _magenta with { W = 0.45f });
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, _magenta with { W = 0.65f });
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, _magenta with { W = 0.85f });
            if (ImGui.Button("Send", new Vector2(120f, height)))
            {
                SubmitInput();
            }
            ImGui.PopStyleColor(3);

            // submit on Ctrl+Enter
            if (ImGui.IsItemFocused() || ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows))
            {
                bool ctrl = ImGui.GetIO().KeyCtrl;
                if (ctrl && ImGui.IsKeyPressed((ImGuiKey)(int)ImGuiKey.Enter, false))
                    SubmitInput();
            }

            // quick copy buttons (optional)
            if (_cfg.ShowCopyButtons)
            {
                ImGui.Dummy(new Vector2(0, 4));
                if (ImGui.Button("Copy last Nunu"))
                {
                    var last = GetLast(LineKind.Assistant);
                    if (!string.IsNullOrEmpty(last)) ImGui.SetClipboardText(last);
                }
                ImGui.SameLine();
                if (ImGui.Button("Copy last You"))
                {
                    var last = GetLast(LineKind.User);
                    if (!string.IsNullOrEmpty(last)) ImGui.SetClipboardText(last);
                }
            }
        }

        private void SubmitInput()
        {
            var text = (_input ?? string.Empty).Trim();
            if (text.Length == 0) return;

            AddUserLine(text);
            OnSend?.Invoke(text);

            _input = string.Empty;
            _focusInputNext = true;
        }

        // ========================= Rendering helpers =========================

        private void RenderBubble(string speaker, string text, bool left, bool streaming = false)
        {
            // name
            ImGui.PushStyleColor(ImGuiCol.Text, _magenta);
            ImGui.TextUnformatted(speaker);
            ImGui.PopStyleColor();

            // body
            var wrap = ImGui.GetContentRegionAvail().X;
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + MathF.Max(240f, wrap));
            var shown = _cfg.AsciiSafe ? AsciiSafe(text) : text;

            // slightly different tint for streaming
            var bg = _magentaDim with { W = streaming ? 0.12f : 0.18f };
            ImGui.PushStyleColor(ImGuiCol.ChildBg, bg);
            ImGui.BeginChild($"##bubble_{speaker}_{ImGui.GetCursorPosY():0}", new Vector2(0, 0), true);
            ImGui.TextUnformatted(shown);
            ImGui.EndChild();
            ImGui.PopStyleColor();

            ImGui.PopTextWrapPos();
            ImGui.Dummy(new Vector2(0, 4));
        }

        private void RenderSystemLine(string text)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, _systemText);
            ImGui.TextUnformatted(text);
            ImGui.PopStyleColor();
        }

        private static string AsciiSafe(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (var ch in s)
                sb.Append(ch <= 127 ? ch : '?');
            return sb.ToString();
        }

        private string? GetLast(LineKind k)
        {
            var list = k == LineKind.Assistant ? _right : _left;
            for (int i = list.Count - 1; i >= 0; --i)
                if (list[i].Item1 == k)
                    return list[i].Item2;
            return null;
        }

        // ========================= Public API (called by PluginMain) =========================

        public void AppendSystem(string text)
        {
            _system.Add(text);
            // also mirror into both panes as faint system lines
            _left.Add((LineKind.System, text));
            _right.Add((LineKind.System, text));
            _scrollLeftToEnd = _scrollRightToEnd = true;
        }

        public void AddSystemLine(string text) => AppendSystem(text);

        public void AppendAssistant(string text)
        {
            _right.Add((LineKind.Assistant, text));
            _scrollRightToEnd = true;
        }

        public void BeginAssistantStream()
        {
            _isStreaming = true;
            _streamBuf.Clear();
        }

        public void AppendAssistantDelta(string delta)
        {
            if (!_isStreaming) { BeginAssistantStream(); }
            _streamBuf.Append(delta);
            _scrollRightToEnd = true;
        }

        public void EndAssistantStream()
        {
            if (!_isStreaming) return;
            _isStreaming = false;
            var final = _streamBuf.ToString();
            _streamBuf.Clear();
            if (!string.IsNullOrEmpty(final))
            {
                _right.Add((LineKind.Assistant, final));
                _scrollRightToEnd = true;
            }
        }

        public void AddUserLine(string text)
        {
            var who = _cfg.ChatDisplayName?.Length > 0 ? _cfg.ChatDisplayName! : "You";
            _left.Add((LineKind.User, text));
            _scrollLeftToEnd = true;

            // also show a typing indicator in system banner & right pane if streaming is about to start
            // (PluginMain starts streaming immediately after calling HandleUserSend)
        }

        internal void SetMoodColor(Vector4 vector4)
        {
            throw new NotImplementedException();
        }
    }
}
