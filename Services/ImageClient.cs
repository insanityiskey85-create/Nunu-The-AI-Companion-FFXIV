using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NunuTheAICompanion.Services;

public sealed class ImageClient
{
    private readonly HttpClient _http;
    private readonly Configuration _config;
    private readonly string _baseSave;

    public ImageClient(HttpClient http, Configuration config, string defaultSaveDir)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _config = config ?? throw new ArgumentNullException(nameof(config));

        // Remove global timeout so we control it per request.
        _http.Timeout = System.Threading.Timeout.InfiniteTimeSpan;

        _baseSave = string.IsNullOrWhiteSpace(_config.ImageSaveDir)
            ? defaultSaveDir
            : _config.ImageSaveDir;

        Directory.CreateDirectory(_baseSave);
    }

    public async Task<List<string>> GenerateAsync(
        string prompt,
        string? negative,
        int? width,
        int? height,
        int? steps,
        float? cfg,
        string? sampler,
        int? seed,
        CancellationToken token = default)
    {
        var mode = (_config.ImageBackendMode ?? "auto1111").ToLowerInvariant();
        return mode switch
        {
            "auto1111" => await GenerateAuto1111Async(prompt, negative, width, height, steps, cfg, sampler, seed, token),
            _ => throw new NotSupportedException($"Image backend '{mode}' not supported yet.")
        };
    }

    private async Task<List<string>> GenerateAuto1111Async(
        string prompt,
        string? negative,
        int? width,
        int? height,
        int? steps,
        float? cfg,
        string? sampler,
        int? seed,
        CancellationToken externalToken)
    {
        var url = $"{_config.ImageBackendUrl?.TrimEnd('/')}/sdapi/v1/txt2img";

        var body = new
        {
            prompt = prompt,
            negative_prompt = string.IsNullOrWhiteSpace(negative) ? _config.ImgNegative : negative,
            width = width ?? _config.ImgWidth,
            height = height ?? _config.ImgHeight,
            steps = steps ?? _config.ImgSteps,
            cfg_scale = cfg ?? _config.ImgCfgScale,
            sampler_name = string.IsNullOrWhiteSpace(sampler) ? _config.ImgSampler : sampler,
            seed = seed ?? -1,
            enable_hr = false,
        };

        var json = JsonSerializer.Serialize(body);
        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        // Apply per-request timeout from config but still honor external cancellation
        using var ctsTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(
            Math.Clamp(_config.ImageRequestTimeoutSec, 30, 36000))); // 30s .. 10h
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(externalToken, ctsTimeout.Token);

        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, linked.Token)
                                    .ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        var payload = await resp.Content.ReadAsStringAsync(linked.Token).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;

        var list = new List<string>();
        if (root.TryGetProperty("images", out var images) && images.ValueKind == JsonValueKind.Array)
        {
            int i = 0;
            foreach (var imgEl in images.EnumerateArray())
            {
                var b64 = imgEl.GetString() ?? string.Empty;
                if (string.IsNullOrEmpty(b64)) continue;
                byte[] data = Convert.FromBase64String(b64);

                var file = Path.Combine(_baseSave, MakeFileName(i));
                await File.WriteAllBytesAsync(file, data, linked.Token).ConfigureAwait(false);
                list.Add(file);
                i++;
            }
        }

        return list;
    }

    private static string MakeFileName(int index)
    {
        var ts = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        return $"nunu_{ts}_{index:D2}.png";
    }
}
