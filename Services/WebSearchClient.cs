using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NunuTheAICompanion.Services;

public sealed class WebSearchClient
{
    private readonly HttpClient _http;
    private readonly Configuration _config;

    public record SearchResult(string Title, string Url, string Snippet);

    public WebSearchClient(HttpClient http, Configuration config)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _http.Timeout = TimeSpan.FromSeconds(Math.Clamp(_config.SearchTimeoutSec, 5, 60));
    }

    public async Task<List<SearchResult>> SearchAsync(string query, CancellationToken token = default)
    {
        if (!_config.AllowInternet)
            throw new InvalidOperationException("Internet search disabled in config.");
        if (string.IsNullOrWhiteSpace(query))
            return new List<SearchResult>();

        var backend = (_config.SearchBackend ?? "serpapi").ToLowerInvariant();
        return backend switch
        {
            "bing" => await BingAsync(query, token).ConfigureAwait(false),
            _ => await SerpApiAsync(query, token).ConfigureAwait(false),
        };
    }

    private async Task<List<SearchResult>> SerpApiAsync(string q, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(_config.SearchApiKey))
            throw new InvalidOperationException("SerpAPI key not set.");
        var url = $"https://serpapi.com/search.json?engine=google&q={Uri.EscapeDataString(q)}&num={Math.Clamp(_config.SearchMaxResults, 1, 10)}&api_key={Uri.EscapeDataString(_config.SearchApiKey)}";
        using var resp = await _http.GetAsync(url, token).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync(token).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var list = new List<SearchResult>();
        if (root.TryGetProperty("organic_results", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var it in arr.EnumerateArray())
            {
                var title = it.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                var link = it.TryGetProperty("link", out var l) ? l.GetString() ?? "" : "";
                var snip = it.TryGetProperty("snippet", out var s) ? s.GetString() ?? "" : "";
                if (!string.IsNullOrEmpty(link))
                    list.Add(new SearchResult(title, link, snip));
                if (list.Count >= _config.SearchMaxResults) break;
            }
        }
        return list;
    }

    private async Task<List<SearchResult>> BingAsync(string q, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(_config.SearchApiKey))
            throw new InvalidOperationException("Bing Search key not set.");

        var uri = $"https://api.bing.microsoft.com/v7.0/search?q={Uri.EscapeDataString(q)}&count={Math.Clamp(_config.SearchMaxResults, 1, 10)}";
        using var req = new HttpRequestMessage(HttpMethod.Get, uri);
        req.Headers.TryAddWithoutValidation("Ocp-Apim-Subscription-Key", _config.SearchApiKey);
        using var resp = await _http.SendAsync(req, token).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync(token).ConfigureAwait(false);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var list = new List<SearchResult>();
        if (root.TryGetProperty("webPages", out var wp) && wp.TryGetProperty("value", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var it in arr.EnumerateArray())
            {
                var name = it.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                var url = it.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
                var snip = it.TryGetProperty("snippet", out var s) ? s.GetString() ?? "" : "";
                if (!string.IsNullOrEmpty(url))
                    list.Add(new SearchResult(name, url, snip));
                if (list.Count >= _config.SearchMaxResults) break;
            }
        }
        return list;
    }

    public static string FormatForContext(IEnumerable<SearchResult> results)
    {
        var sb = new StringBuilder();
        int i = 1;
        foreach (var r in results)
        {
            sb.Append(i++).Append(". ").AppendLine(string.IsNullOrWhiteSpace(r.Title) ? "(no title)" : r.Title);
            if (!string.IsNullOrWhiteSpace(r.Snippet))
                sb.Append("   ").AppendLine(r.Snippet);
            sb.Append("   ").AppendLine(r.Url);
        }
        return sb.ToString();
    }
}
