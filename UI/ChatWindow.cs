using System.Numerics;
using System.Threading;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using NunuTheAICompanion.Services;
// ImGui bindings
using ImGui = Dalamud.Bindings.ImGui.ImGui;
using ImGuiCol = Dalamud.Bindings.ImGui.ImGuiCol;
using ImGuiStyleVar = Dalamud.Bindings.ImGui.ImGuiStyleVar;

namespace NunuTheAICompanion.UI;

public sealed class ChatWindow : Window
{
    private readonly Configuration _config;
    private readonly OllamaClient _client;
    private readonly MemoryService? _memory;

    private readonly object _gate = new();
    private readonly List<(string role, string content)> _messages = new();

    private string _input = string.Empty;
    private bool _rememberNext = true;
    private bool _isStreaming = false;
    private bool _autoScroll = true;
    private CancellationTokenSource? _cts;

    public ChatWindow(Configuration config, MemoryService? memory = null)
        : base("Little Nunu — Soul Chat")
    {
        _config = config;
        _memory = memory;
        _client = new OllamaClient(new HttpClient(), _config);

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(520, 380),
            MaximumSize = new Vector2(9999, 9999)
        };

        RespectCloseHotkey = true;
        IsOpen = _config.StartOpen;

        var hello = "WAH! Little Nunu listens by the campfire. What tale shall we stitch?";
        lock (_gate) _messages.Add(("assistant", hello));
        _memory?.Append("assistant", hello, topic: "greeting");
    }

    public override void Draw()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.Alpha, _config.WindowOpacity);
        DrawToolbar();
        ImGui.Separator();
        DrawTranscript();
        ImGui.Separator();
        DrawComposer();
        ImGui.PopStyleVar();
    }

    private void DrawToolbar()
    {
        if (!ImGui.BeginChild("toolbar", new Vector2(0, 28 * ImGuiHelpers.GlobalScale), false))
        { ImGui.EndChild(); return; }

        ImGui.TextUnformatted("Nunu The AI Companion — ToS-safe chat only");
        ImGui.SameLine(); ImGui.TextDisabled(" | "); ImGui.SameLine();
        ImGui.TextUnformatted($"Backend: {_config.BackendMode}@{_config.BackendUrl}");

        if (_memory is not null)
        {
            ImGui.SameLine(); ImGui.TextDisabled(" | "); ImGui.SameLine();
            bool enabled = _memory.Enabled;
            if (ImGui.Checkbox("Memory", ref enabled))
            {
                _memory.Enabled = enabled;
                _config.MemoryEnabled = enabled;
                _config.Save();
            }
        }

        ImGui.SameLine();
        if (ImGui.SmallButton("Opacity+"))
        {
            _config.WindowOpacity = Math.Clamp(_config.WindowOpacity + 0.05f, 0.3f, 1.0f);
            _config.Save();
        }

        ImGui.EndChild();
    }

    private void DrawTranscript()
    {
        // Reserve most of the window height for the transcript
        float availY = ImGui.GetContentRegionAvail().Y - (110 * ImGuiHelpers.GlobalScale);
        if (availY < 140) availY = 140;

        if (!ImGui.BeginChild("transcript", new Vector2(0, availY), true))
        { ImGui.EndChild(); return; }

        // Take a snapshot so draw isn't racing the stream thread
        List<(string role, string content)> snapshot;
        lock (_gate) snapshot = _messages.ToList();

        // Detect if user scrolled up; only autoscroll if at bottom
        bool atBottom = ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - 5f;

        for (int i = 0; i < snapshot.Count; i++)
        {
            var (role, content) = snapshot[i];
            bool isUser = role == "user";

            // Bubble background
            ImGui.PushStyleColor(ImGuiCol.ChildBg,
                isUser ? new Vector4(0.10f, 0.12f, 0.16f, 0.65f)
                       : new Vector4(0.20f, 0.00f, 0.20f, 0.50f));

            // One bubble per message; no nested windows-per-line
            ImGui.BeginChild($"msg_{i}", new Vector2(0, 0), true);

            // Small header
            ImGui.PushStyleColor(ImGuiCol.Text, isUser
                ? new Vector4(0.8f, 0.85f, 1.0f, 0.9f)
                : new Vector4(1.0f, 0.8f, 1.0f, 0.9f));
            ImGui.TextUnformatted(isUser ? "You" : "Little Nunu");
            ImGui.PopStyleColor();

            // Content (streaming may still be mid-flight)
            if (string.IsNullOrEmpty(content))
            {
                // Show a soft typing indicator if assistant bubble is still empty
                if (!isUser)
                    ImGui.TextDisabled("…tuning the voidstrings");
            }
            else
            {
                ImGui.TextWrapped(content);
            }

            ImGui.EndChild();
            ImGui.PopStyleColor();
            ImGui.Spacing();
        }

        // Auto-scroll if near bottom or a new chunk arrived and _autoScroll wants it
        if (_autoScroll || atBottom)
            ImGui.SetScrollHereY(1.0f);

        ImGui.EndChild();
    }

    private void DrawComposer()
    {
        if (!ImGui.BeginChild("composer", new Vector2(0, 0), false))
        { ImGui.EndChild(); return; }

        ImGui.Checkbox("Remember", ref _rememberNext);
        ImGui.SameLine();

        float btnW = 120 * ImGuiHelpers.GlobalScale;
        var inputW = ImGui.GetContentRegionAvail().X - (btnW + 8 * ImGuiHelpers.GlobalScale);
        if (inputW < 120) inputW = 120;

        ImGui.InputTextMultiline("##input", ref _input, 8000, new Vector2(inputW, 80 * ImGuiHelpers.GlobalScale));
        ImGui.SameLine();

        var canSend = !_isStreaming && !string.IsNullOrWhiteSpace(_input);
        if (ImGui.Button("Sing", new Vector2(btnW, 80 * ImGuiHelpers.GlobalScale)) && canSend)
        {
            Send();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Send to Nunu");

        ImGui.EndChild();
    }

    private void Send()
    {
        var text = _input.Trim();
        if (string.IsNullOrEmpty(text)) return;

        lock (_gate) _messages.Add(("user", text));
        if (_rememberNext && _memory?.Enabled == true)
            _memory.Append("user", text, topic: "chat");

        _input = string.Empty;
        _autoScroll = true;
        StreamAsync();
    }

    private async void StreamAsync()
    {
        if (_isStreaming) return;
        _isStreaming = true;
        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        // Add local assistant placeholder (will be filled by stream)
        int idx;
        lock (_gate)
        {
            _messages.Add(("assistant", string.Empty));
            idx = _messages.Count - 1;
        }

        // Build history for backend WITHOUT the trailing empty assistant
        List<(string role, string content)> history;
        lock (_gate)
        {
            history = _messages
                .Where((m, i) => !(i == idx && m.role == "assistant"))
                .ToList();
        }

        try
        {
            await foreach (var chunk in _client.StreamChatAsync(_config.BackendUrl, history, _cts.Token))
            {
                if (string.IsNullOrEmpty(chunk)) continue;

                lock (_gate)
                {
                    var current = _messages[idx].content;
                    _messages[idx] = ("assistant", current + chunk);
                }
                _autoScroll = true; // keep transcript pinned to latest during stream
            }
        }
        catch (Exception ex)
        {
            lock (_gate) _messages[idx] = ("assistant", $"The strings tangled: {ex.Message}");
        }
        finally
        {
            _isStreaming = false;

            // Persist assistant reply when finished
            string reply;
            lock (_gate) reply = _messages[idx].content.Trim();

            if (!string.IsNullOrEmpty(reply) && _memory?.Enabled == true)
                _memory.Append("assistant", reply, topic: "chat");
        }
    }
}
