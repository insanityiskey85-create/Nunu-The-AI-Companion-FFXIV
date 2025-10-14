using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace NunuTheAICompanion.UI;

/// <summary>
/// Minimal chat UI window with streaming helpers.
/// Includes back-compat shims used by earlier patches
/// (AppendSystem, AddSystemLine, AppendAssistantStream).
/// </summary>
public sealed class ChatWindow : Window
{
    private readonly Configuration _cfg;

    // Simple transcript buffer; you can replace with a richer log model
    private readonly List<string> _lines = new();

    // Streaming state
    private bool _streaming;
    private string _streamBuffer = string.Empty;

    public event Action<string>? OnSend;

    public ChatWindow(Configuration cfg)
        : base("Little Nunu — The Soul Weeper", ImGuiWindowFlags.None, true)
    {
        _cfg = cfg;
        // reasonable default size; caller can overwrite via property
        this.SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(520, 320),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };
    }

    /// <summary>External code may set an initial size.</summary>
    public Vector2 Size
    {
        set { this.SizeCondition = ImGuiCond.Appearing; this.Size = value; }
    }

    // ========= Public helpers used by PluginMain =========

    public void AppendAssistant(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        _lines.Add(text);
    }

    public void BeginAssistantStream()
    {
        _streaming = true;
        _streamBuffer = string.Empty;
    }

    public void AppendAssistantDelta(string delta)
    {
        if (!_streaming || string.IsNullOrEmpty(delta)) return;
        _streamBuffer += delta;
    }

    public void EndAssistantStream()
    {
        if (!_streaming) return;
        _streaming = false;

        if (!string.IsNullOrEmpty(_streamBuffer))
        {
            _lines.Add(_streamBuffer);
            _streamBuffer = string.Empty;
        }
    }

    // ========= Back-compat shims (called by earlier patches) =========

    /// <summary>Treat “system” lines the same as assistant lines in UI.</summary>
    public void AppendSystem(string text) => AppendAssistant(text);

    /// <summary>Alias used by some patches; same as AppendAssistant.</summary>
    public void AddSystemLine(string text) => AppendAssistant(text);

    /// <summary>Alias used by some patches for streaming deltas.</summary>
    public void AppendAssistantStream(string delta) => AppendAssistantDelta(delta);

    public override void Draw()
    {
        // Render transcript
        ImGui.BeginChild("nunu_chat_scroll", new Vector2(0, -40), true);
        foreach (var line in _lines)
        {
            ImGui.TextWrapped(line);
            ImGui.Separator();
        }

        if (_streaming && !string.IsNullOrEmpty(_streamBuffer))
        {
            ImGui.TextWrapped(_streamBuffer + "█");
        }
        ImGui.EndChild();

        // Input row (very simple)
        ImGui.PushItemWidth(-130);
        string input = string.Empty;
        ImGui.InputText("##nunu_input", ref input, 1024);
        ImGui.SameLine();
        if (ImGui.Button("Send", new Vector2(120, 0)))
        {
            if (!string.IsNullOrWhiteSpace(input))
                OnSend?.Invoke(input.Trim());
        }
        ImGui.PopItemWidth();
    }
}
