using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
// ImGui (Dalamud.Bindings.ImGui.dll)
using ImGui = Dalamud.Bindings.ImGui.ImGui;
using ImGuiCol = Dalamud.Bindings.ImGui.ImGuiCol;
using ImGuiWindowFlags = Dalamud.Bindings.ImGui.ImGuiWindowFlags;

namespace Nunu_The_AI_Companion.UI;

public sealed class ConfigWindow : Window
{
    private readonly Configuration _config;

    // Backend
    private string _backend;
    private string _backendMode;
    private string _model;
    private float _temperature;
    private string _systemPrompt;
    private bool _strictPersona;

    // Display
    private float _opacity;
    private string _chatDisplayName;

    // Listening
    private bool _listenEnabled, _requireCallsign, _listenSelf;
    private string _callsign;
    private string _whitelistCsv;
    private bool _say, _tell, _party, _alliance, _fc, _shout, _yell;

    // Diagnostics
    private bool _debugListen, _debugMirror;

    // Internet search
    private bool _allowInternet;
    private string _searchBackend;
    private string _searchApiKey;
    private int _searchMaxResults;
    private int _searchTimeout;

    // HTTP test
    private static readonly HttpClient _http = new();
    private string _testStatus = "";
    private bool _testing;

    public ConfigWindow(Configuration config) : base("Nunu — Configuration")
    {
        _config = config;

        // backend
        _backend = config.BackendUrl;
        _backendMode = config.BackendMode;
        _model = config.ModelName;
        _temperature = config.Temperature;
        _systemPrompt = config.SystemPrompt ?? "";
        _strictPersona = config.StrictPersona;

        // display
        _opacity = config.WindowOpacity;
        _chatDisplayName = config.ChatDisplayName;

        // listening
        _listenEnabled = config.ListenEnabled;
        _requireCallsign = config.RequireCallsign;
        _listenSelf = config.ListenSelf;
        _callsign = config.Callsign ?? "@nunu";
        _whitelistCsv = string.Join(", ", config.Whitelist ?? new());
        _say = config.ListenSay; _tell = config.ListenTell; _party = config.ListenParty;
        _alliance = config.ListenAlliance; _fc = config.ListenFreeCompany;
        _shout = config.ListenShout; _yell = config.ListenYell;

        // diagnostics
        _debugListen = config.DebugListen; _debugMirror = config.DebugMirrorToWindow;

        // internet search
        _allowInternet = config.AllowInternet;
        _searchBackend = config.SearchBackend ?? "serpapi";
        _searchApiKey = config.SearchApiKey ?? "";
        _searchMaxResults = config.SearchMaxResults;
        _searchTimeout = config.SearchTimeoutSec;

        RespectCloseHotkey = true;
        Flags |= ImGuiWindowFlags.AlwaysAutoResize;
        IsOpen = false;
    }

    public override void Draw()
    {
        ImGui.TextUnformatted("Nunu The AI Companion — Settings");
        ImGui.Separator();

        // ===== Backend =====
        ImGui.TextUnformatted("Backend Mode");
        var modes = new[] { "jsonl", "sse", "plaintext" };
        int idx = Array.IndexOf(modes, _backendMode); if (idx < 0) idx = 0;
        if (ImGui.BeginCombo("##mode", modes[idx]))
        {
            for (int i = 0; i < modes.Length; i++)
            {
                bool sel = (i == idx);
                if (ImGui.Selectable(modes[i], sel)) { _backendMode = modes[i]; idx = i; }
            }
            ImGui.EndCombo();
        }
        ImGui.SameLine(); ImGui.TextDisabled("(jsonl = direct Ollama /api/chat)");

        ImGui.TextUnformatted("Backend URL");
        ImGui.InputText("##backend", ref _backend, 1024);

        ImGui.TextUnformatted("Model Name");
        ImGui.InputText("##model", ref _model, 256);

        ImGui.TextUnformatted("System Prompt (optional)");
        ImGui.InputTextMultiline("##sys", ref _systemPrompt, 8000, new Vector2(520, 100));

        ImGui.TextUnformatted("Temperature");
        ImGui.SliderFloat("##temp", ref _temperature, 0.1f, 1.5f, "%.2f");

        ImGui.Checkbox("Strict Persona (stay in-character)", ref _strictPersona);

        ImGui.Separator();

        if (ImGui.Button(_testing ? "Testing..." : "Test Backend", new Vector2(130 * ImGuiHelpers.GlobalScale, 28 * ImGuiHelpers.GlobalScale)) && !_testing)
            _ = RunTestAsync();
        ImGui.SameLine();
        if (!string.IsNullOrEmpty(_testStatus))
        {
            var ok = _testStatus.StartsWith("OK");
            ImGui.PushStyleColor(ImGuiCol.Text, ok ? new Vector4(0.6f, 1f, 0.6f, 1f) : new Vector4(1f, 0.6f, 0.6f, 1f));
            ImGui.TextWrapped(_testStatus);
            ImGui.PopStyleColor();
        }

        ImGui.Separator();

        // ===== Listening =====
        ImGui.TextUnformatted("Chat Listening");
        ImGui.Checkbox("Enable listening", ref _listenEnabled);

        ImGui.Checkbox("Require callsign", ref _requireCallsign);
        ImGui.SameLine(); ImGui.InputText("Callsign", ref _callsign, 64);
        ImGui.TextDisabled("Example: @nunu (case-insensitive)");

        ImGui.Checkbox("Listen to my own messages (\"You\")", ref _listenSelf);
        ImGui.TextDisabled("Enable to trigger Nunu when you speak in /say, /party, etc.");

        ImGui.TextUnformatted("Whitelist (comma-separated names)");
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

        // ===== Internet Search =====
        ImGui.TextUnformatted("Internet Search");
        ImGui.Checkbox("Allow Little Nunu to search the web", ref _allowInternet);

        ImGui.TextUnformatted("Engine");
        if (ImGui.BeginCombo("##engine", _searchBackend))
        {
            foreach (var opt in new[] { "serpapi", "bing" })
            {
                bool sel = opt == _searchBackend;
                if (ImGui.Selectable(opt, sel)) _searchBackend = opt;
            }
            ImGui.EndCombo();
        }

        ImGui.InputText("API Key", ref _searchApiKey, 256);
        ImGui.SliderInt("Max Results", ref _searchMaxResults, 1, 5);
        ImGui.SliderInt("Timeout (sec)", ref _searchTimeout, 5, 60);
        ImGui.TextDisabled("SerpAPI (Google) or Bing Web Search API. Keys are stored locally.");

        ImGui.Separator();

        // ===== Display =====
        ImGui.TextUnformatted("Your display name (shown instead of \"You\")");
        ImGui.InputText("##displayname", ref _chatDisplayName, 128);

        ImGui.TextUnformatted("Window Opacity");
        ImGui.SliderFloat("##opacity", ref _opacity, 0.30f, 1.00f, "%.2f");

        ImGui.Separator();

        // ===== Diagnostics =====
        ImGui.Checkbox("Debug: log all heard chat events", ref _debugListen);
        ImGui.SameLine();
        ImGui.Checkbox("Mirror debug lines into Nunu window", ref _debugMirror);

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
            _config.ListenSelf = _listenSelf;
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

            _config.AllowInternet = _allowInternet;
            _config.SearchBackend = _searchBackend;
            _config.SearchApiKey = _searchApiKey.Trim();
            _config.SearchMaxResults = _searchMaxResults;
            _config.SearchTimeoutSec = _searchTimeout;

            _config.ChatDisplayName = string.IsNullOrWhiteSpace(_chatDisplayName) ? "You" : _chatDisplayName.Trim();
            _config.WindowOpacity = _opacity;

            _config.DebugListen = _debugListen;
            _config.DebugMirrorToWindow = _debugMirror;

            _config.Save();
            IsOpen = false;
        }
        ImGui.SameLine();
        if (ImGui.Button("Cancel", new Vector2(100 * ImGuiHelpers.GlobalScale, 28 * ImGuiHelpers.GlobalScale)))
            IsOpen = false;
    }

    private async System.Threading.Tasks.Task RunTestAsync()
    {
        _testing = true; _testStatus = "Testing…";
        try
        {
            var mode = (_backendMode ?? "jsonl").ToLowerInvariant();
            var url = _backend;

            string bodyJson = JsonSerializer.Serialize(new
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

            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(bodyJson, Encoding.UTF8, "application/json")
            };
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
