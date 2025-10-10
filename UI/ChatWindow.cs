using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using Dalamud.Interface.Windowing;

namespace NunuTheAICompanion.UI
{
    /// <summary>
    /// Continuous chat log with streaming helpers, like your reference.
    /// Uses fully-qualified Dalamud ImGui to avoid alias issues.
    /// </summary>
    public sealed class ChatWindow : Window
    {
        private readonly Configuration _config;

        private readonly List<(string role, string text)> _lines = new();
        private readonly StringBuilder _streamSb = new();

        private string _userInput = string.Empty;
        private bool _autoScroll = true;

        // First-draw size application
        public new Vector2 Size { get => _initialSize; set => _initialSize = value; }
        private Vector2 _initialSize = new(640, 420);
        private bool _didFirstSizeApply;

        /// <summary>Raised when player clicks Send with non-empty input.</summary>
        public event Action<string>? OnSend;

        public ChatWindow(Configuration config)
            : base("Little Nunu — Soul Chat##NunuTheAICompanion")
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));

            SizeConstraints = new WindowSizeConstraints
            {
                MinimumSize = new Vector2(420, 240),
                MaximumSize = new Vector2(4096, 4096),
            };

            RespectCloseHotkey = true;
            IsOpen = _config.StartOpen;
        }

        public override void PreDraw()
        {
            if (!_didFirstSizeApply)
            {
                Dalamud.Bindings.ImGui.ImGui.SetNextWindowSize(
                    _initialSize,
                    Dalamud.Bindings.ImGui.ImGuiCond.FirstUseEver
                );
                _didFirstSizeApply = true;
            }
        }

        public override void Draw()
        {
            // Transcript (fills most of the window height; input row is ~60px)
            Dalamud.Bindings.ImGui.ImGui.BeginChild(
                "nunu_log",
                new Vector2(0, -60),
                true,
                Dalamud.Bindings.ImGui.ImGuiWindowFlags.HorizontalScrollbar
            );

            foreach (var (role, text) in _lines)
            {
                Dalamud.Bindings.ImGui.ImGui.PushTextWrapPos();
                if (role.Equals("assistant", StringComparison.OrdinalIgnoreCase))
                    Dalamud.Bindings.ImGui.ImGui.TextWrapped($"> {Sanitize(text)}");
                else
                    Dalamud.Bindings.ImGui.ImGui.TextWrapped($"{role}: {Sanitize(text)}");
                Dalamud.Bindings.ImGui.ImGui.PopTextWrapPos();
            }

            // Streaming line (if a reply is in progress)
            if (_streamSb.Length > 0)
            {
                Dalamud.Bindings.ImGui.ImGui.PushTextWrapPos();
                Dalamud.Bindings.ImGui.ImGui.TextWrapped($"> {Sanitize(_streamSb.ToString())}_");
                Dalamud.Bindings.ImGui.ImGui.PopTextWrapPos();
            }

            if (_autoScroll &&
                Dalamud.Bindings.ImGui.ImGui.GetScrollY() >= Dalamud.Bindings.ImGui.ImGui.GetScrollMaxY() - 4)
            {
                Dalamud.Bindings.ImGui.ImGui.SetScrollHereY(1.0f);
            }

            Dalamud.Bindings.ImGui.ImGui.EndChild();

            // Composer row
            Dalamud.Bindings.ImGui.ImGui.PushItemWidth(-120);
            Dalamud.Bindings.ImGui.ImGui.InputText("##nunu_input", ref _userInput, 4000);
            Dalamud.Bindings.ImGui.ImGui.PopItemWidth();
            Dalamud.Bindings.ImGui.ImGui.SameLine();

            bool send = Dalamud.Bindings.ImGui.ImGui.Button("Send");
            Dalamud.Bindings.ImGui.ImGui.SameLine();
            Dalamud.Bindings.ImGui.ImGui.Checkbox("Auto-scroll", ref _autoScroll);

            if (send && !string.IsNullOrWhiteSpace(_userInput))
            {
                var text = _userInput.Trim();
                AppendUser(text);
                _userInput = string.Empty;

                // Let PluginMain forward to backend
                OnSend?.Invoke(text);
            }
        }

        // ---------- Public helpers for streaming ----------

        public void AppendAssistant(string text)
        {
            if (!string.IsNullOrEmpty(text))
                _lines.Add(("assistant", text));
        }

        public void BeginAssistantStream()
        {
            _streamSb.Clear();
        }

        public void AppendAssistantDelta(string delta)
        {
            if (!string.IsNullOrEmpty(delta))
                _streamSb.Append(delta);
        }

        public void EndAssistantStream()
        {
            if (_streamSb.Length > 0)
            {
                _lines.Add(("assistant", _streamSb.ToString()));
                _streamSb.Clear();
            }
        }

        public void AppendSystem(string text)
        {
            if (!string.IsNullOrEmpty(text))
                _lines.Add(("system", text));
        }

        // ---------- Internals ----------

        private void AppendUser(string text)
        {
            _lines.Add(("you", text));
        }

        private string Sanitize(string s)
        {
            if (!_config.AsciiSafe || string.IsNullOrEmpty(s))
                return s ?? string.Empty;

            var sb = new StringBuilder(s.Length);
            foreach (var ch in s)
                sb.Append(ch is >= (char)0x20 and <= (char)0x7E ? ch : '?');
            return sb.ToString();
        }
    }
}
