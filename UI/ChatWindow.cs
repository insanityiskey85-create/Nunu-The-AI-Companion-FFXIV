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
    private readonly MemoryService _memory;

    private readonly List<(string role, string content)> _messages = new();
    private string _input = string.Empty;
    private bool _rememberNext = true;     // toggle next user message
    private bool _isStreaming = false;
    private CancellationTokenSource? _cts;

    public ChatWindow(Configuration config, MemoryService memory) : base("Little Nunu — Soul Chat")
    {
        _config = config;
        _memory = memory;
        _client = new OllamaClient(new HttpClient());

        this.SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(360, 320),
            MaximumSize = new Vector2(9999, 9999)
        };

        this.RespectCloseHotkey = true;
        this.IsOpen = _config.StartOpen;

        var hello = "WAH! Little Nunu listens by the campfire. What tale shall we stitch?";
        _messages.Add(("assistant", hello));
        if (_memory.Enabled) _memory.Append("assistant", hello, topic: "greeting");
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
        if (ImGui.BeginChild("toolbar", new Vector2(0, 28 * ImGuiHelpers.GlobalScale), false))
        {
            ImGui.TextUnformatted("Nunu The AI Companion — ToS-safe chat only");
            ImGui.SameLine();
            ImGui.TextDisabled(" | ");
            ImGui.SameLine();
            ImGui.TextUnformatted($"Backend: {_config.BackendUrl}");
            ImGui.SameLine();
            ImGui.TextDisabled(" | ");
            ImGui.SameLine();
            var mem = _memory.Enabled;
            if (ImGui.Checkbox("Memory", ref mem))
            {
                _memory.Enabled = mem;
                _config.MemoryEnabled = mem;
                _config.Save();
            }
        }
        ImGui.EndChild();
    }

    private void DrawTranscript()
    {
        var frameHeight = ImGui.GetContentRegionAvail().Y - 120 * ImGuiHelpers.GlobalScale;
        if (frameHeight < 100) frameHeight = 100;

        if (ImGui.BeginChild("transcript", new Vector2(0, frameHeight), true))
        {
            foreach (var (role, content) in _messages)
            {
                var isUser = role == "user";
                if (isUser)
                    ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.10f, 0.12f, 0.16f, 0.65f));
                else
                    ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.20f, 0.00f, 0.20f, 0.50f));

                ImGui.BeginChild($"msg_{GetHashCode()}_{_messages.IndexOf((role, content))}", new Vector2(0, 0), true);
                ImGui.TextWrapped(content);
                ImGui.EndChild();
                ImGui.PopStyleColor();
                ImGui.Spacing();
            }
            if (_isStreaming)
            {
                ImGui.TextDisabled("Nunu is tuning the voidstrings…");
            }
            if (ImGui.GetScrollY() >= ImGui.GetScrollMaxY())
            {
                ImGui.SetScrollHereY(1.0f);
            }
        }
        ImGui.EndChild();
    }

    private void DrawComposer()
    {
        if (ImGui.BeginChild("composer", new Vector2(0, 0), false))
        {
            ImGui.Checkbox("Remember", ref _rememberNext);
            ImGui.SameLine();
            ImGui.InputTextMultiline("##input", ref _input, 4000, new Vector2(-140 * ImGuiHelpers.GlobalScale, 80 * ImGuiHelpers.GlobalScale));
            ImGui.SameLine();

            var canSend = !_isStreaming && !string.IsNullOrWhiteSpace(_input);
            if (ImGui.Button("Sing", new Vector2(120 * ImGuiHelpers.GlobalScale, 80 * ImGuiHelpers.GlobalScale)) && canSend)
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
        if (_rememberNext && _memory.Enabled) _memory.Append("user", text, topic: "chat");
        _input = string.Empty;
        StreamAsync();
    }

    private async void StreamAsync()
    {
        if (_isStreaming) return;
        _isStreaming = true;
        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        _messages.Add(("assistant", string.Empty));
        var idx = _messages.Count - 1;

        try
        {
            await foreach (var chunk in _client.StreamChatAsync(_config.BackendUrl, _messages, _cts.Token))
            {
                var newText = _messages[idx].content + chunk;
                _messages[idx] = ("assistant", newText);
            }
        }
        catch (Exception ex)
        {
            _messages[idx] = ("assistant", $"The strings tangled: {ex.Message}");
        }
        finally
        {
            _isStreaming = false;

            // After stream completes, persist assistant reply
            if (_memory.Enabled)
            {
                var text = _messages[idx].content.Trim();
                if (!string.IsNullOrEmpty(text))
                    _memory.Append("assistant", text, topic: "chat");
            }
        }
    }
}
