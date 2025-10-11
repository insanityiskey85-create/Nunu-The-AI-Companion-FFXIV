using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using NunuTheAICompanion.Services;
using System;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace NunuTheAICompanion.UI;

public sealed class ImageWindow : Window
{
    private readonly Configuration _config;
    private readonly ImageClient _images;
    private readonly IPluginLog? _log;

    private string _prompt = "";
    private string _lastPath = "";
    private bool _busy;

    public ImageWindow(Configuration config, ImageClient images, IPluginLog? log = null)
        : base("Nunu – Image Atelier", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _images = images ?? throw new ArgumentNullException(nameof(images));
        _log = log;
        Size = new Vector2(720, 300);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        ImGui.InputTextMultiline("Prompt", ref _prompt, 4096, new Vector2(-1, 120));
        if (!_busy)
        {
            if (ImGui.Button("Create Image"))
            {
                _ = RunAsync(_prompt);
            }
        }
        else
        {
            ImGui.TextDisabled("Working…");
        }

        if (!string.IsNullOrEmpty(_lastPath))
        {
            ImGui.Separator();
            ImGui.TextWrapped($"Saved: {_lastPath}");
        }
    }

    private async Task RunAsync(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt)) return;
        _busy = true;
        try
        {
            // Use a default 120s timeout unless you add a Config field later.
            var timeoutSec = 120;
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSec));
            var path = await _images.Txt2ImgAsync(prompt, cts.Token).ConfigureAwait(false);
            _lastPath = path;
        }
        catch (Exception ex)
        {
            _log?.Error(ex, "[ImageWindow] generation failed");
            _lastPath = $"[error] {ex.Message}";
        }
        finally
        {
            _busy = false;
        }
    }
}
