using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace NunuTheAICompanion.UI;

/// <summary>
/// Minimal chat UI window with streaming helpers.
/// Includes back-compat shims used by older patches
/// (AppendSystem, AddSystemLine, AppendAssistantStream).
/// </summary>
public sealed class ChatWindow : Window
{
    private readonly Configuration _cfg;

    // Simple transcript buffer; replace with a richer log model if desired.
    private readonly List<string> _lines = new();

    // Streaming state
    private bool _streaming;
    private string _streamBuffer = string.Empty;

    // Input buffer (basic, not persistent between frames)
    private string _input = string.Empty;

    public event Action<string>? OnSend;

    public ChatWindow(Configuration cfg)
        : base("Little Nunu — The Soul Weeper", ImGuiWindowFlags.None, true)
    {
        _cfg = cfg;

        // Default window constraints; caller can still set Window.Size outside.
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(520, 320),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };
    }

    // ========= Public helpers used by PluginMain =========

    /// <summary>Add a completed assistant line to the transcript.</summary>
    public void AppendAssistant(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        _lines.Add(text);
    }

    /// <summary>Begin a streaming assistant response.</summary>
    public void BeginAssistantStream()
    {
        _streaming = true;
        _streamBuffer = string.Empty;
    }

    /// <summary>Append a delta chunk during streaming.</summary>
    public void AppendAssistantDelta(string delta)
    {
        if (!_streaming || string.IsNullOrEmpty(delta)) return;
        _streamBuffer += delta;
    }

    /// <summary>End streaming and commit the buffered text.</summary>
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

    // ========= Back-compat shims (used by some patches) =========

    /// <summary>Treat “system” lines the same as assistant lines in UI.</summary>
    public void AppendSystem(string text) => AppendAssistant(text);

    /// <summary>Alias used by some patches; same as AppendAssistant.</summary>
    public void AddSystemLine(string text) => AppendAssistant(text);

    /// <summary>Alias used by some patches for streaming deltas.</summary>
    public void AppendAssistantStream(string delta) => AppendAssistantDelta(delta);

    // ========= Drawing =========

    public override void Draw()
    {
        // Optional opacity (simple multiplier on style alpha)
        var alpha = ImGui.GetStyle().Alpha;
        if (_cfg.WindowOpacity >= 0f && _cfg.WindowOpacity <= 1f)
            ImGui.GetStyle().Alpha = _cfg.WindowOpacity;

        try
        {
            // Transcript area
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

            // Input row
            ImGui.PushItemWidth(-130);
            ImGui.InputText("##nunu_input", ref _input, 2048);
            ImGui.SameLine();
            if (ImGui.Button("Send", new Vector2(120, 0)))
            {
                var text = _input?.Trim();
                if (!string.IsNullOrEmpty(text))
                {
                    OnSend?.Invoke(text);
                    _input = string.Empty;
                }
            }
            ImGui.PopItemWidth();
        }
        finally
        {
            // restore alpha
            ImGui.GetStyle().Alpha = alpha;
        }
    }
}
