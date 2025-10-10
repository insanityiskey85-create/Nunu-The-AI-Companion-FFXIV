using System.Numerics;
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

    private readonly List<(string role, string content)> _messages = new();
    private string _input = string.Empty;
    private bool _rememberNext = true;
    private bool _isStreaming = false;
    private CancellationTokenSource? _cts;

    public ChatWindow(Configuration config, MemoryService? memory = null) : base("Little Nunu — Soul Chat")
    {
        _config = config;
        _memory = memory;
        _client = new OllamaClient(new HttpClient());

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(520, 360),
            MaximumSize = new Vector2(9999, 9999)
        };

        RespectCloseHotkey = true;
        IsOpen = _config.StartOpen;

        var hello = "WAH! Little Nunu listens by the campfire. What tale shall we stitch?";
        _messages.Add(("assistant", hello));
        _memory?.Append("assistant", hello, topic: "greeting");
    }

    public override void Draw()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.Alpha, _config.WindowOpacity);
        DrawToolbar();
        ImGui.Separator();
        DrawSplitTranscript();
        ImGui.Separator();
        DrawComposer();
        ImGui.PopStyleVar();
    }

    private void DrawToolbar()
    {
        if (ImGui.BeginChild("toolbar", new Vector2(0, 28 * ImGuiHelpers.GlobalScale), false))
        {
            ImGui.TextUnformatted("Nunu The AI Companion — ToS-safe chat only");
            ImGui.SameLine();
            ImGui.TextDisabled(" | ");
            ImGui.SameLine();
            ImGui.TextUnformatted($"Backend: {_config.BackendUrl}");
            if (_memory is not null)
            {
                ImGui.SameLine();
                ImGui.TextDisabled(" | ");
                ImGui.SameLine();
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
        }
        ImGui.EndChild();
    }

    private void DrawSplitTranscript()
    {
        // available vertical space for panes
        float paneHeight = ImGui.GetContentRegionAvail().Y - (110 * ImGuiHelpers.GlobalScale);
        if (paneHeight < 120) paneHeight = 120;

        // width split
        float gap = 8 * ImGuiHelpers.GlobalScale;
        float totalW = ImGui.GetContentRegionAvail().X;
        float halfW = (totalW - gap) / 2f;

        // left: USER
        if (ImGui.BeginChild("pane_user", new Vector2(halfW, paneHeight), true))
        {
            ImGui.TextDisabled("You");
            ImGui.Separator();

            foreach (var (role, content) in _messages)
            {
                if (role != "user") continue;
                if (string.IsNullOrWhiteSpace(content)) continue;

                ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.10f, 0.12f, 0.16f, 0.65f));
                ImGui.BeginChild($"user_{GetHashCode()}_{content.GetHashCode()}", new Vector2(0, 0), true);
                ImGui.TextWrapped(content);
                ImGui.EndChild();
                ImGui.PopStyleColor();
                ImGui.Spacing();
            }

            // auto-scroll to bottom when new content arrives
            if (ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - 5f)
                ImGui.SetScrollHereY(1.0f);
        }
        ImGui.EndChild();

        ImGui.SameLine();

        // right: ASSISTANT (Nunu)
        if (ImGui.BeginChild("pane_assistant", new Vector2(halfW, paneHeight), true))
        {
            ImGui.TextDisabled("Little Nunu");
            ImGui.Separator();

            foreach (var (role, content) in _messages)
            {
                if (role != "assistant") continue;
                if (string.IsNullOrWhiteSpace(content)) continue;

                ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.20f, 0.00f, 0.20f, 0.50f));
                ImGui.BeginChild($"assistant_{GetHashCode()}_{content.GetHashCode()}", new Vector2(0, 0), true);
                ImGui.TextWrapped(content);
                ImGui.EndChild();
                ImGui.PopStyleColor();
                ImGui.Spacing();
            }

            if (_isStreaming)
                ImGui.TextDisabled("Nunu is tuning the voidstrings…");

            if (ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - 5f)
                ImGui.SetScrollHereY(1.0f);
        }
        ImGui.EndChild();
    }

    private void DrawComposer()
    {
        if (ImGui.BeginChild("composer", new Vector2(0, 0), false))
        {
            ImGui.Checkbox("Remember", ref _rememberNext);
            ImGui.SameLine();

            // make the input field take the remaining width minus the button
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
        }
        ImGui.EndChild();
    }

    private void Send()
    {
        var text = _input.Trim();
        if (string.IsNullOrEmpty(text)) return;

        _messages.Add(("user", text));
        if (_rememberNext && _memory?.Enabled == true)
            _memory.Append("user", text, topic: "chat");

        _input = string.Empty;
        StreamAsync();
    }

    private async void StreamAsync()
    {
        if (_isStreaming) return;
        _isStreaming = true;
        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        // Assistant placeholder (right pane). We'll append into this slot.
        _messages.Add(("assistant", string.Empty));
        int idx = _messages.Count - 1;

        try
        {
            await foreach (var chunk in _client.StreamChatAsync(_config.BackendUrl, _messages, _cts.Token))
            {
                if (string.IsNullOrEmpty(chunk)) continue;
                _messages[idx] = ("assistant", _messages[idx].content + chunk);
            }
        }
        catch (Exception ex)
        {
            _messages[idx] = ("assistant", $"The strings tangled: {ex.Message}");
        }
        finally
        {
            _isStreaming = false;

            var reply = _messages[idx].content.Trim();
            if (!string.IsNullOrEmpty(reply) && _memory?.Enabled == true)
                _memory.Append("assistant", reply, topic: "chat");
        }
    }
}
