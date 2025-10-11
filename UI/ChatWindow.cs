using System;
using System.Numerics;
using System.Text;
using Dalamud.Interface.Windowing;

// ImGui bindings (Dalamud.Bindings.ImGui.dll)
using ImGui = Dalamud.Bindings.ImGui.ImGui;
using ImGuiCol = Dalamud.Bindings.ImGui.ImGuiCol;
using ImGuiStyleVar = Dalamud.Bindings.ImGui.ImGuiStyleVar;
using ImGuiWindowFlags = Dalamud.Bindings.ImGui.ImGuiWindowFlags;
using ImGuiCond = Dalamud.Bindings.ImGui.ImGuiCond;

namespace NunuTheAICompanion.UI;

/// <summary>
/// Two-pane chat:
///  - Left pane: "Real Nunu" (user) continuous transcript (no bubbles).
///  - Right pane: "Little Nunu" reply transcript (streams inline).
/// Keeps copy buttons for assistant (copy last reply / copy all).
/// </summary>
public sealed class ChatWindow : Window
{
    private readonly Configuration _config;

    // Continuous transcripts
    private readonly StringBuilder _userTranscript = new();
    private readonly StringBuilder _assistantTranscript = new();

    // Streaming buffer for current assistant reply
    private readonly StringBuilder _assistantStream = new();

    // Last sealed assistant reply (for "Copy last reply")
    private string _lastAssistantReply = string.Empty;

    // Composer
    private string _userInput = string.Empty;

    // Auto-scroll flags
    private bool _autoScrollUser = true;
    private bool _autoScrollAssistant = true;

    // First-draw size application
    public new Vector2 Size { get => _initialSize; set => _initialSize = value; }
    private Vector2 _initialSize = new(740, 480);
    private bool _didFirstSizeApply;

    /// <summary>Raised when player clicks Send with non-empty input.</summary>
    public event Action<string>? OnSend;

    public ChatWindow(Configuration config)
        : base("Little Nunu — Soul Chat##NunuTheAICompanion")
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(520, 320),
            MaximumSize = new Vector2(4096, 4096),
        };

        RespectCloseHotkey = true;
        IsOpen = _config.StartOpen;

        // Soft greeting into assistant pane
        AppendAssistantLine("WAH! Little Nunu listens by the campfire. What tale shall we stitch?");
    }

    public override void PreDraw()
    {
        if (!_didFirstSizeApply)
        {
            ImGui.SetNextWindowSize(_initialSize, ImGuiCond.FirstUseEver);
            _didFirstSizeApply = true;
        }
    }

    public override void Draw()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.Alpha, _config.WindowOpacity);

        // Top toolbar (optional mini status)
        DrawToolbar();

        ImGui.Separator();

        // Transcripts area: two panes side-by-side
        DrawTwoPaneTranscripts();

        ImGui.Separator();

        // Composer row
        DrawComposer();

        ImGui.PopStyleVar();
    }

    // =========================
    //        Toolbar
    // =========================
    private void DrawToolbar()
    {
        if (!ImGui.BeginChild("toolbar", new Vector2(0, 26), false))
        { ImGui.EndChild(); return; }

        var me = string.IsNullOrWhiteSpace(_config.ChatDisplayName) ? "You" : _config.ChatDisplayName;
        ImGui.TextUnformatted($"Real Nunu: {me}");
        ImGui.SameLine(); ImGui.TextDisabled(" | "); ImGui.SameLine();
        ImGui.TextUnformatted($"Little Nunu: connected to {_config.BackendMode ?? "jsonl"} @ {_config.BackendUrl}");

        ImGui.EndChild();
    }

    // =========================
    //      Two Pane View
    // =========================
    private void DrawTwoPaneTranscripts()
    {
        // Height reserved for transcripts: take all but composer (~70 px)
        float availY = ImGui.GetContentRegionAvail().Y - 78f;
        if (availY < 160) availY = 160;

        var fullWidth = ImGui.GetContentRegionAvail().X;
        var gap = 8f;
        var half = (fullWidth - gap) * 0.5f;
        if (half < 120f) half = Math.Max(120f, fullWidth - gap - 120f);

        // Left: Real Nunu (user)
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.08f, 0.10f, 0.14f, 0.65f));
        ImGui.BeginChild("pane_user", new Vector2(half, availY), true, ImGuiWindowFlags.HorizontalScrollbar);
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.85f, 0.90f, 1.0f, 1f));
            var me = string.IsNullOrWhiteSpace(_config.ChatDisplayName) ? "You" : _config.ChatDisplayName;
            ImGui.TextUnformatted($"{me} ▸");
            ImGui.PopStyleColor();

            ImGui.Separator();

            // content
            var utext = _userTranscript.ToString();
            if (string.IsNullOrEmpty(utext))
                ImGui.TextDisabled("(your words will flow here)");
            else
            {
                ImGui.PushTextWrapPos();
                ImGui.TextWrapped(Sanitize(utext));
                ImGui.PopTextWrapPos();
            }

            // Auto-scroll
            if (_autoScrollUser && ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - 4f)
                ImGui.SetScrollHereY(1.0f);
        }
        ImGui.EndChild();
        ImGui.PopStyleColor();

        // Gap
        ImGui.SameLine();
        ImGui.Dummy(new Vector2(gap, 1));
        ImGui.SameLine();

        // Right: Little Nunu (assistant)
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.20f, 0.00f, 0.20f, 0.50f));
        ImGui.BeginChild("pane_assistant", new Vector2(0, availY), true, ImGuiWindowFlags.HorizontalScrollbar);
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 0.85f, 1.0f, 1f));
            ImGui.TextUnformatted("Little Nunu ▸");
            ImGui.PopStyleColor();

            ImGui.SameLine();
            // Copy controls for assistant pane
            if (ImGui.SmallButton("Copy last"))
            {
                var copy = string.IsNullOrEmpty(_assistantStream.ToString())
                    ? _lastAssistantReply
                    : _assistantStream.ToString();
                ImGui.SetClipboardText(copy ?? string.Empty);
            }
            ImGui.SameLine();
            if (ImGui.SmallButton("Copy all")) ImGui.SetClipboardText(_assistantTranscript.ToString() + _assistantStream.ToString() ?? string.Empty);
            ImGui.SameLine();
            ImGui.Checkbox("Auto-scroll", ref _autoScrollAssistant);

            ImGui.Separator();

            // content (sealed transcript)
            var atext = _assistantTranscript.ToString();
            if (!string.IsNullOrEmpty(atext))
            {
                ImGui.PushTextWrapPos();
                ImGui.TextWrapped(Sanitize(atext));
                ImGui.PopTextWrapPos();
            }

            // streaming line (cursor)
            if (_assistantStream.Length > 0)
            {
                ImGui.PushTextWrapPos();
                ImGui.TextWrapped(Sanitize(_assistantStream.ToString()) + "_");
                ImGui.PopTextWrapPos();
            }
            else if (string.IsNullOrEmpty(atext))
            {
                ImGui.TextDisabled("(my song appears here)");
            }

            // Auto-scroll
            if (_autoScrollAssistant && ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - 4f)
                ImGui.SetScrollHereY(1.0f);
        }
        ImGui.EndChild();
        ImGui.PopStyleColor();
    }

    // =========================
    //        Composer
    // =========================
    private void DrawComposer()
    {
        if (!ImGui.BeginChild("composer", new Vector2(0, 0), false))
        { ImGui.EndChild(); return; }

        // Auto-scroll toggles for each pane
        ImGui.Checkbox("Auto-scroll (you)", ref _autoScrollUser);
        ImGui.SameLine();
        // Right pane checkbox lives above; repeat here for convenience:
        ImGui.Checkbox("Auto-scroll (Nunu)", ref _autoScrollAssistant);

        // Input + Send
        float btnW = 120f;
        float inputW = ImGui.GetContentRegionAvail().X - (btnW + 8f);
        if (inputW < 120f) inputW = 120f;

        ImGui.PushItemWidth(inputW);
        ImGui.InputText("##nunu_input", ref _userInput, 4000);
        ImGui.PopItemWidth();
        ImGui.SameLine();

        if (ImGui.Button("Send", new Vector2(btnW, 0)))
        {
            Send();
        }

        ImGui.EndChild();
    }

    private void Send()
    {
        var text = _userInput?.Trim();
        if (string.IsNullOrEmpty(text)) return;

        AppendUserLine(text);
        _userInput = string.Empty;

        // Raise to PluginMain so it can stream via backend
        OnSend?.Invoke(text);
    }

    // =========================
    //     Public hooks
    // =========================

    /// <summary>Append a complete assistant line (non-streamed).</summary>
    public void AppendAssistant(string text)
    {
        AppendAssistantLine(text);
    }

    /// <summary>Begin streaming the assistant's reply.</summary>
    public void BeginAssistantStream()
    {
        _assistantStream.Clear();
        _autoScrollAssistant = true; // stick to bottom while streaming
    }

    /// <summary>Append a streaming chunk for the assistant's reply.</summary>
    public void AppendAssistantDelta(string delta)
    {
        if (string.IsNullOrEmpty(delta)) return;

        // Ensure lines break nicely when backend doesn't include \n
        _assistantStream.Append(delta);
    }

    /// <summary>Seal the current streaming reply into the transcript.</summary>
    public void EndAssistantStream()
    {
        var final = _assistantStream.ToString().TrimEnd();
        if (final.Length > 0)
        {
            if (_assistantTranscript.Length > 0 && !EndsWithNewline(_assistantTranscript))
                _assistantTranscript.AppendLine();
            _assistantTranscript.AppendLine(final);
            _lastAssistantReply = final;
        }
        _assistantStream.Clear();
    }

    // =========================
    //     Internals
    // =========================

    private void AppendUserLine(string text)
    {
        var me = string.IsNullOrWhiteSpace(_config.ChatDisplayName) ? "You" : _config.ChatDisplayName;
        if (_userTranscript.Length > 0 && !EndsWithNewline(_userTranscript))
            _userTranscript.AppendLine();
        // Prefix with your display name to keep context clear
        _userTranscript.AppendLine($"{me}: {text}");
        _autoScrollUser = true;
    }

    private void AppendAssistantLine(string text)
    {
        if (_assistantTranscript.Length > 0 && !EndsWithNewline(_assistantTranscript))
            _assistantTranscript.AppendLine();
        _assistantTranscript.AppendLine(text);
        _lastAssistantReply = text;
        _autoScrollAssistant = true;
    }

    private static bool EndsWithNewline(StringBuilder sb)
    {
        if (sb.Length == 0) return true;
        char c = sb[^1];
        return c == '\n' || c == '\r';
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
