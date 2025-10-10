using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Numerics;
using System.Text;
using System.Text.Json;
// ImGui bindings
using ImGui = Dalamud.Bindings.ImGui.ImGui;
using ImGuiCol = Dalamud.Bindings.ImGui.ImGuiCol;

namespace NunuTheAICompanion.UI;

public sealed class ConfigWindow : Window
{
    private readonly Configuration _config;

    // Backend
    private string _backend;
    private string _backendMode; // "jsonl" | "sse" | "plaintext"
    private string _model;
    private float _temperature;
    private string _systemPrompt;
    private float _opacity;
    private bool _strictPersona;

    // Listening
    private bool _listenEnabled;
    private bool _requireCallsign;
    private string _callsign;
    private string _whitelistCsv;

    // Channels
    private bool _say, _tell, _party, _alliance, _fc, _shout, _yell;

    // Diagnostics
    private bool _debugListen;
    private bool _debugMirror;

    // Chat display
    private string _chatDisplayName;

    private static readonly HttpClient _http = new();
    private string _testStatus = "";
    private bool _testing = false;

    public ConfigWindow(Configuration config) : base("Nunu — Configuration")
    {
        _config = config;

        // backend
        _backend = _config.BackendUrl;
        _backendMode = _config.BackendMode;
        _model = _config.ModelName;
        _temperature = _config.Temperature;
        _systemPrompt = _config.SystemPrompt ?? "";
        _opacity = _config.WindowOpacity;
        _strictPersona = _config.StrictPersona;

        // listening
        _listenEnabled = _config.ListenEnabled;
        _requireCallsign = _config.RequireCallsign;
        _callsign = _config.Callsign ?? "@nunu";
        _whitelistCsv = string.Join(", ", _config.Whitelist ?? new());

        // channels
        _say = _config.ListenSay;
        _tell = _config.ListenTell;
        _party = _config.ListenParty;
        _alliance = _config.ListenAlliance;
        _fc = _config.ListenFreeCompany;
        _shout = _config.ListenShout;
        _yell = _config.ListenYell;

        // diagnostics
        _debugListen = _config.DebugListen;
        _debugMirror = _config.DebugMirrorToWindow;

        // chat display
        _chatDisplayName = _config.ChatDisplayName;

        RespectCloseHotkey = true;
        Flags |= ImGuiWindowFlags.AlwaysAutoResize;
        IsOpen = false;
    }

    public override void Draw()
    {
        ImGui.TextUnformatted("Nunu The AI Companion — Settings");
        ImGui.Separator();

        // ---------- Backend ----------
        ImGui.TextUnformatted("Backend Mode");
        var modes = new[] { "jsonl", "sse", "plaintext" };
        int idx = Array.IndexOf(modes, _backendMode);
        if (idx < 0) idx = 0;
        if (ImGui.BeginCombo("##mode", modes[idx]))
        {
            for (int i = 0; i < modes.Length; i++)
            {
                bool sel = (i == idx);
                if (ImGui.Selectable(modes[i], sel))
                {
                    _backendMode = modes[i];
                    idx = i;
                }
            }
            ImGui.EndCombo();
        }
        ImGui.SameLine(); ImGui.TextDisabled("(jsonl = direct Ollama /api/chat, sse/plaintext = proxy)");

        ImGui.TextUnformatted("Backend URL");
        ImGui.InputText("##backend", ref _backend, 1024);

        if (_backendMode == "jsonl")
        {
            ImGui.TextUnformatted("Model Name");
            ImGui.InputText("##model", ref _model, 256);
        }

        ImGui.TextUnformatted("System Prompt (optional)");
        ImGui.InputTextMultiline("##sys", ref _systemPrompt, 8000, new Vector2(520, 100));

        ImGui.TextUnformatted("Temperature");
        ImGui.SliderFloat("##temp", ref _temperature, 0.1f, 1.5f, "%.2f");

        ImGui.Checkbox("Strict Persona (stay in-character)", ref _strictPersona);

        ImGui.Separator();

        // Backend test
        if (ImGui.Button(_testing ? "Testing..." : "Test Backend", new Vector2(130 * ImGuiHelpers.GlobalScale, 28 * ImGuiHelpers.GlobalScale)) && !_testing)
        {
            _ = RunTestAsync();
        }
        ImGui.SameLine();
        if (!string.IsNullOrEmpty(_testStatus))
        {
            var ok = _testStatus.StartsWith("OK");
            var color = ok ? new Vector4(0.6f, 1.0f, 0.6f, 1f) : new Vector4(1.0f, 0.6f, 0.6f, 1f);
            ImGui.PushStyleColor(ImGuiCol.Text, color);
            ImGui.TextWrapped(_testStatus);
            ImGui.PopStyleColor();
        }

        ImGui.Separator();

        // ---------- Chat Listening ----------
        ImGui.TextUnformatted("Chat Listening");
        ImGui.Checkbox("Enable listening", ref _listenEnabled);

        ImGui.Checkbox("Require callsign", ref _requireCallsign);
        ImGui.SameLine();
        ImGui.InputText("Callsign", ref _callsign, 64);
        ImGui.TextDisabled("Example: @nunu (case-insensitive)");

        ImGui.TextUnformatted("Whitelist (comma-separated names, optional)");
        ImGui.InputTextMultiline("##wl", ref _whitelistCsv, 8000, new Vector2(520, 60));
        ImGui.TextDisabled("Accepted: Name or Name@World. Empty = allow anyone.");

        ImGui.TextUnformatted("Channels to listen");
        ImGui.Checkbox("Say", ref _say); ImGui.SameLine();
        ImGui.Checkbox("Tell", ref _tell); ImGui.SameLine();
        ImGui.Checkbox("Party", ref _party); ImGui.SameLine();
        ImGui.Checkbox("Alliance", ref _alliance); ImGui.SameLine();
        ImGui.Checkbox("FC", ref _fc);
        ImGui.Checkbox("Shout", ref _shout); ImGui.SameLine();
        ImGui.Checkbox("Yell", ref _yell);

        ImGui.Separator();

        // ---------- Chat Display ----------
        ImGui.TextUnformatted("Your display name (shown instead of \"You\")");
        ImGui.InputText("##displayname", ref _chatDisplayName, 128);
        ImGui.TextDisabled("Set this to: Real Nunu");

        ImGui.Separator();

        // ---------- Diagnostics ----------
        ImGui.Checkbox("Debug: log all heard chat events", ref _debugListen);
        ImGui.SameLine();
        ImGui.Checkbox("Mirror debug lines into Nunu window", ref _debugMirror);

        ImGui.Separator();

        ImGui.TextUnformatted("Window Opacity");
        ImGui.SliderFloat("##opacity", ref _opacity, 0.30f, 1.00f, "%.2f");

        ImGui.Separator();
        if (ImGui.Button("Save & Close", new Vector2(140 * ImGuiHelpers.GlobalScale, 28 * ImGuiHelpers.GlobalScale)))
        {
            _config.BackendUrl = _backend.Trim();
            _config.BackendMode = _backendMode.Trim().ToLowerInvariant();
            _config.ModelName = _model.Trim();
            _config.Temperature = _temperature;
            _config.SystemPrompt = string.IsNullOrWhiteSpace(_systemPrompt) ? null : _systemPrompt;
            _config.StrictPersona = _strictPersona;

            _config.ListenEnabled = _listenEnabled;
            _config.RequireCallsign = _requireCallsign;
            _config.Callsign = string.IsNullOrWhiteSpace(_callsign) ? "@nunu" : _callsign.Trim();

            var list = new List<string>();
            foreach (var raw in (_whitelistCsv ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var s = raw.Trim();
                if (s.Length > 0) list.Add(s);
            }
            _config.Whitelist = list;

            _config.ListenSay = _say;
            _config.ListenTell = _tell;
            _config.ListenParty = _party;
            _config.ListenAlliance = _alliance;
            _config.ListenFreeCompany = _fc;
            _config.ListenShout = _shout;
            _config.ListenYell = _yell;

            _config.DebugListen = _debugListen;
            _config.DebugMirrorToWindow = _debugMirror;

            _config.ChatDisplayName = string.IsNullOrWhiteSpace(_chatDisplayName) ? "You" : _chatDisplayName.Trim();

            _config.WindowOpacity = _opacity;

            _config.Save();
            IsOpen = false;
        }
        ImGui.SameLine();
        if (ImGui.Button("Cancel", new Vector2(100 * ImGuiHelpers.GlobalScale, 28 * ImGuiHelpers.GlobalScale)))
        {
            IsOpen = false;
        }
    }

    private async System.Threading.Tasks.Task RunTestAsync()
    {
        _testing = true; _testStatus = "Testing…";
        try
        {
            var mode = (_backendMode ?? "jsonl").ToLowerInvariant();
            var url = _backend;

            string bodyJson;
            if (mode == "jsonl")
            {
                bodyJson = JsonSerializer.Serialize(new
                {
                    model = string.IsNullOrWhiteSpace(_model) ? "nunu-8b" : _model,
                    stream = false,
                    options = new { temperature = _temperature },
                    messages = new object[]
                    {
                        string.IsNullOrWhiteSpace(_systemPrompt) ? null : new { role = "system", content = _systemPrompt },
                        new { role = "user", content = "ping" }
                    }.Where(x => x is not null).ToArray()
                });
            }
            else
            {
                bodyJson = JsonSerializer.Serialize(new
                {
                    model = _model,
                    temperature = _temperature,
                    system = _systemPrompt,
                    messages = new[] { new { role = "user", content = "ping" } }
                });
            }

            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(bodyJson, Encoding.UTF8, "application/json")
            };
            if (mode == "sse")
                req.Headers.TryAddWithoutValidation("Accept", "text/event-stream");

            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            _testStatus = resp.IsSuccessStatusCode ? "OK: backend reachable." : $"ERR: HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}";
        }
        catch (Exception ex)
        {
            _testStatus = $"ERR: {ex.Message}";
        }
        finally
        {
            _testing = false;
        }
    }
}
