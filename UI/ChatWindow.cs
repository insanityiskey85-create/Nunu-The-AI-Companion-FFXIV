using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using Dalamud.Interface.Windowing;

// ImGui bindings
using ImGui = Dalamud.Bindings.ImGui.ImGui;
using ImGuiCol = Dalamud.Bindings.ImGui.ImGuiCol;
using ImGuiStyleVar = Dalamud.Bindings.ImGui.ImGuiStyleVar;

namespace NunuTheAICompanion.UI;

/// <summary>
/// Continuous chat log with streaming helpers. 
/// Shows user's configurable display name and provides Copy buttons for assistant messages.
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
            ImGui.SetNextWindowSize(_initialSize, Dalamud.Bindings.ImGui.ImGuiCond.FirstUseEver);
            _didFirstSizeApply = true;
        }
    }

    public override void Draw()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.Alpha, _config.WindowOpacity);

        // Transcript (fills most of the window height; input row is ~60px)
        ImGui.BeginChild("nunu_log", new Vector2(0, -60), true, Dalamud.Bindings.ImGui.ImGuiWindowFlags.HorizontalScrollbar);

        for (int i = 0; i < _lines.Count; i++)
        {
            var (role, text) = _lines[i];
            DrawMessageBubble(i, role, text);
        }

        // Streaming line (if a reply is in progress)
        if (_streamSb.Length > 0)
        {
            DrawMessageBubble(-1, "assistant_stream", _streamSb.ToString(), isStreaming: true);
        }

        // Keep scrolled to bottom if user was there
        if (_autoScroll && ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - 4)
            ImGui.SetScrollHereY(1.0f);

        ImGui.EndChild();

        // Composer row
        ImGui.PushItemWidth(-120);
        ImGui.InputText("##nunu_input", ref _userInput, 4000);
        ImGui.PopItemWidth();
        ImGui.SameLine();

        bool send = ImGui.Button("Send");
        ImGui.SameLine();
        ImGui.Checkbox("Auto-scroll", ref _autoScroll);

        if (send && !string.IsNullOrWhiteSpace(_userInput))
        {
            var text = _userInput.Trim();
            AppendUser(text);
            _userInput = string.Empty;
            OnSend?.Invoke(text);
        }

        ImGui.PopStyleVar();
    }

    private void DrawMessageBubble(int id, string role, string text, bool isStreaming = false)
    {
        bool isUser = role.Equals("you", StringComparison.OrdinalIgnoreCase);
        bool isAssistant = role.StartsWith("assistant", StringComparison.OrdinalIgnoreCase);
        bool isSystem = role.Equals("system", StringComparison.OrdinalIgnoreCase);

        // Background color by role
        var bg = isUser ? new Vector4(0.10f, 0.12f, 0.16f, 0.65f)
                        : isAssistant ? new Vector4(0.20f, 0.00f, 0.20f, 0.50f)
                                      : new Vector4(0.12f, 0.12f, 0.12f, 0.50f);

        ImGui.PushStyleColor(ImGuiCol.ChildBg, bg);
        ImGui.BeginChild($"msg_{role}_{id}", new Vector2(0, 0), true);

        // Header (name)
        ImGui.PushStyleColor(ImGuiCol.Text,
            isUser ? new Vector4(0.8f, 0.85f, 1.0f, 0.9f)
                   : isAssistant ? new Vector4(1.0f, 0.8f, 1.0f, 0.9f)
                                 : new Vector4(0.9f, 0.9f, 0.9f, 0.9f));

        var displayName = isUser
            ? (string.IsNullOrWhiteSpace(_config.ChatDisplayName) ? "You" : _config.ChatDisplayName)
            : isAssistant ? "Little Nunu"
                          : "System";

        ImGui.TextUnformatted(displayName);
        ImGui.PopStyleColor();

        // Content
        if (string.IsNullOrEmpty(text))
        {
            if (isAssistant) ImGui.TextDisabled("…tuning the voidstrings");
        }
        else
        {
            ImGui.PushTextWrapPos();
            ImGui.TextWrapped(Sanitize(text) + (isStreaming ? "_" : string.Empty));
            ImGui.PopTextWrapPos();
        }

        // Copy controls (assistant messages only; include streaming)
        if (isAssistant)
        {
            ImGui.Spacing();
            ImGui.PushID(id);
            if (ImGui.Button(isStreaming ? "Copy (streaming)" : "Copy"))
            {
                var toCopy = text ?? string.Empty;
                ImGui.SetClipboardText(toCopy);
            }
            // Context menu too
            if (ImGui.BeginPopupContextItem("assistant_context"))
            {
                if (ImGui.MenuItem("Copy"))
                    ImGui.SetClipboardText(text ?? string.Empty);
                ImGui.EndPopup();
            }
            ImGui.PopID();
        }

        ImGui.EndChild();
        ImGui.PopStyleColor();
        ImGui.Spacing();
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
