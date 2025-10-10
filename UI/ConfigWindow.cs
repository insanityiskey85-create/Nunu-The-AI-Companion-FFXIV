using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using System.Net.Http;
using System.Numerics;
using System.Text;
using System.Text.Json;
// ImGui bindings
using ImGui = Dalamud.Bindings.ImGui.ImGui;
using ImGuiCol = Dalamud.Bindings.ImGui.ImGuiCol;
using ImGuiStyleVar = Dalamud.Bindings.ImGui.ImGuiStyleVar;

namespace NunuTheAICompanion.UI;

public sealed class ConfigWindow : Window
{
    private readonly Configuration _config;

    private string _backend;
    private string _backendMode; // "jsonl" | "sse" | "plaintext"
    private string _model;
    private float _opacity;
    private float _temperature;
    private bool _strictPersona;
    private string _systemPrompt;

    private static readonly HttpClient _http = new();
    private string _testStatus = "";
    private bool _testing = false;

    public ConfigWindow(Configuration config) : base("Nunu — Configuration")
    {
        _config = config;

        _backend = config.BackendUrl;
        _backendMode = config.BackendMode;
        _model = config.ModelName;
        _opacity = config.WindowOpacity;
        _temperature = config.Temperature;
        _strictPersona = config.StrictPersona;
        _systemPrompt = config.SystemPrompt ?? "";

        RespectCloseHotkey = true;
        Flags |= ImGuiWindowFlags.AlwaysAutoResize;
        IsOpen = false;
    }

    public override void Draw()
    {
        ImGui.TextUnformatted("Nunu The AI Companion — Settings");
        ImGui.Separator();

        ImGui.TextUnformatted("Backend Mode");
        var modes = new[] { "jsonl", "sse", "plaintext" };
        int idx = System.Array.IndexOf(modes, _backendMode);
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
        ImGui.SameLine();
        ImGui.TextDisabled("(jsonl = direct Ollama /api/chat, sse/plaintext = proxy)");

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

        ImGui.TextUnformatted("Window Opacity");
        ImGui.SliderFloat("##opacity", ref _opacity, 0.30f, 1.00f, "%.2f");

        ImGui.Separator();
        if (ImGui.Button("Save & Close", new Vector2(140 * ImGuiHelpers.GlobalScale, 28 * ImGuiHelpers.GlobalScale)))
        {
            _config.BackendUrl = _backend.Trim();
            _config.BackendMode = _backendMode.Trim().ToLowerInvariant();
            _config.ModelName = _model.Trim();
            _config.WindowOpacity = _opacity;
            _config.Temperature = _temperature;
            _config.StrictPersona = _strictPersona;
            _config.SystemPrompt = string.IsNullOrWhiteSpace(_systemPrompt) ? null : _systemPrompt;
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
        _testing = true;
        _testStatus = "Testing…";
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
            _testStatus = resp.IsSuccessStatusCode
                ? "OK: backend reachable."
                : $"ERR: HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}";
        }
        catch (System.Exception ex)
        {
            _testStatus = $"ERR: {ex.Message}";
        }
        finally
        {
            _testing = false;
        }
    }
}
