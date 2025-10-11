using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
// ImGui bindings
using ImGui = Dalamud.Bindings.ImGui.ImGui;
using ImGuiCol = Dalamud.Bindings.ImGui.ImGuiCol;

namespace NunuTheAICompanion.UI;

public sealed class ImageWindow : Window
{
    private readonly Configuration _config;
    private readonly Services.ImageClient _client;
    private readonly List<string> _outputs = new();

    private string _prompt = "little nunu, lalafell bard, void-touched, horns, tattoos, fishnets, leather & fur, dramatic lighting, eorzea tavern";
    private string _negative;
    private int _w;
    private int _h;
    private int _steps;
    private float _cfg;
    private string _sampler;
    private int _seed = -1;

    private string _status = "";
    private bool _busy = false;
    private CancellationTokenSource? _cts;

    public ImageWindow(Configuration config, Services.ImageClient client)
        : base("Nunu — Image Atelier")
    {
        _config = config;
        _client = client;

        _negative = config.ImgNegative;
        _w = config.ImgWidth;
        _h = config.ImgHeight;
        _steps = config.ImgSteps;
        _cfg = config.ImgCfgScale;
        _sampler = config.ImgSampler;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(560, 380),
            MaximumSize = new Vector2(4096, 4096)
        };

        RespectCloseHotkey = true;
        IsOpen = false;
    }

    public override void Draw()
    {
        ImGui.TextUnformatted($"Backend: {(_config.ImageBackendMode ?? "auto1111")} @ {(_config.ImageBackendUrl ?? "")}");
        ImGui.SameLine();
        ImGui.TextDisabled($"  |  Timeout: {_config.ImageRequestTimeoutSec}s");
        ImGui.Separator();

        ImGui.TextUnformatted("Prompt");
        ImGui.InputTextMultiline("##prompt", ref _prompt, 12000, new Vector2(0, 90 * ImGuiHelpers.GlobalScale));

        ImGui.TextUnformatted("Negative (optional)");
        ImGui.InputTextMultiline("##negative", ref _negative, 8000, new Vector2(0, 60 * ImGuiHelpers.GlobalScale));

        ImGui.Separator();

        ImGui.InputInt("Width", ref _w);
        ImGui.SameLine(); ImGui.InputInt("Height", ref _h);
        ImGui.InputInt("Steps", ref _steps);
        ImGui.SameLine(); ImGui.SliderFloat("CFG", ref _cfg, 1.0f, 15.0f, "%.1f");
        ImGui.InputInt("Seed (-1 random)", ref _seed);
        ImGui.InputText("Sampler", ref _sampler, 64);
        ImGui.TextDisabled("Tip: 512×512, 20–28 steps are fast. Large sizes or Hires fix will take much longer.");

        ImGui.Separator();

        if (!_busy)
        {
            if (ImGui.Button("Generate", new Vector2(120, 28)))
                _ = GenerateAsync();
        }
        else
        {
            if (ImGui.Button("Cancel", new Vector2(120, 28)))
                _cts?.Cancel();
        }

        ImGui.SameLine();
        if (ImGui.Button("Open Folder", new Vector2(120, 28)))
            TryOpenFolder();

        ImGui.SameLine();
        if (!string.IsNullOrEmpty(_status))
        {
            var ok = _status.StartsWith("OK");
            ImGui.PushStyleColor(ImGuiCol.Text, ok ? new Vector4(0.6f, 1f, 0.6f, 1f) : new Vector4(1f, 0.6f, 0.6f, 1f));
            ImGui.TextWrapped(_status);
            ImGui.PopStyleColor();
        }

        ImGui.Separator();
        ImGui.TextUnformatted("Recent Outputs");
        if (_outputs.Count == 0)
            ImGui.TextDisabled("(none yet)");
        else
        {
            foreach (var file in _outputs)
            {
                ImGui.TextWrapped(file);
                ImGui.SameLine();
                if (ImGui.SmallButton($"Copy##{file.GetHashCode()}"))
                    ImGui.SetClipboardText(file);
                ImGui.SameLine();
                if (ImGui.SmallButton($"Open##{file.GetHashCode()}"))
                    TryOpenFile(file);
            }
        }
    }

    private async Task GenerateAsync()
    {
        if (string.IsNullOrWhiteSpace(_prompt))
        {
            _status = "ERR: Prompt is empty.";
            return;
        }

        // Quick guard against accidental huge requests
        if ((_w * _h) > (1024 * 1024) && _steps > 40)
        {
            _status = "ERR: Very large request. Try smaller size or fewer steps first.";
            return;
        }

        _busy = true;
        _status = "Working…";
        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        try
        {
            var list = await _client.GenerateAsync(
                prompt: _prompt.Trim(),
                negative: string.IsNullOrWhiteSpace(_negative) ? null : _negative.Trim(),
                width: _w, height: _h,
                steps: _steps, cfg: _cfg,
                sampler: string.IsNullOrWhiteSpace(_sampler) ? null : _sampler.Trim(),
                seed: _seed,
                token: _cts.Token);

            if (list.Count == 0)
            {
                _status = "ERR: Backend returned no images.";
            }
            else
            {
                _outputs.InsertRange(0, list);
                _status = $"OK: Saved {list.Count} image(s).";
            }
        }
        catch (OperationCanceledException)
        {
            _status = "ERR: Generation cancelled (timeout or user cancel).";
        }
        catch (Exception ex)
        {
            _status = $"ERR: {ex.Message}";
        }
        finally
        {
            _busy = false;
        }
    }

    private void TryOpenFolder()
    {
        try
        {
            var dir = string.IsNullOrWhiteSpace(_config.ImageSaveDir)
                ? NunuTheAICompanion.PluginMain.PluginInterface.GetPluginConfigDirectory() + "/Images"
                : _config.ImageSaveDir;

            if (!System.IO.Directory.Exists(dir)) return;
            Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
        }
        catch { }
    }

    private void TryOpenFile(string file)
    {
        try
        {
            if (!System.IO.File.Exists(file)) return;
            Process.Start(new ProcessStartInfo { FileName = file, UseShellExecute = true });
        }
        catch { }
    }
}
