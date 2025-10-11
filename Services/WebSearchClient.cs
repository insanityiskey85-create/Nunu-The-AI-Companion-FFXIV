using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NunuTheAICompanion.Services;

public sealed class WebSearchClient
{
    private readonly HttpClient _http;
    private readonly Configuration _cfg;

    public WebSearchClient(HttpClient http, Configuration cfg)
    {
        _http = http;
        _cfg = cfg;
    }

    public async Task<List<(string title, string url, string snippet)>> SearchAsync(string query, CancellationToken token)
    {
        var results = new List<(string, string, string)>();

        var backend = (_cfg.SearchBackend ?? "none").Trim().ToLowerInvariant();
        if (backend is "none" or "" || !_cfg.AllowInternet)
            return results;

        if (backend == "serpapi")
        {
            var key = _cfg.SearchApiKey?.Trim();
            if (string.IsNullOrEmpty(key))
                return results; // no key -> silently no results

            var uri = $"https://serpapi.com/search.json?q={Uri.EscapeDataString(query)}&engine=google&num={Math.Clamp(_cfg.SearchMaxResults, 1, 10)}&api_key={Uri.EscapeDataString(key)}";
            using var req = new HttpRequestMessage(HttpMethod.Get, uri);
            using var res = await _http.SendAsync(req, token).ConfigureAwait(false);
            res.EnsureSuccessStatusCode();
            var json = await res.Content.ReadAsStringAsync(token).ConfigureAwait(false);

            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("organic_results", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var it in arr.EnumerateArray())
                {
                    var title = it.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                    var link = it.TryGetProperty("link", out var l) ? l.GetString() ?? "" : "";
                    var snip = it.TryGetProperty("snippet", out var s) ? s.GetString() ?? "" : "";
                    if (!string.IsNullOrEmpty(link))
                        results.Add((title, link, snip));
                }
            }
            return results;
        }

        // Unknown backend -> nothing
        return results;
    }

    public static string FormatForContext(List<(string title, string url, string snippet)> hits)
    {
        if (hits.Count == 0) return "(no results)";
        var sb = new System.Text.StringBuilder();
        int i = 1;
        foreach (var h in hits)
        {
            sb.Append(i++).Append(". ").Append(string.IsNullOrEmpty(h.title) ? h.url : h.title).Append('\n');
            if (!string.IsNullOrEmpty(h.snippet)) sb.Append("   ").Append(h.snippet).Append('\n');
            sb.Append("   ").Append(h.url).Append('\n');
        }
        return sb.ToString();
    }
}
