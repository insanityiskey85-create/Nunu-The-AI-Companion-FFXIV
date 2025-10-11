using Nunu_The_AI_Companion;
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

    public WebSearchClient(HttpClient http, Configuration cfg) { _http = http; _cfg = cfg; }

    public sealed record Hit(string Title, string Url, string Snippet);

    public async Task<List<Hit>> SearchAsync(string query, CancellationToken token)
    {
        var hits = new List<Hit>();
        if (!_cfg.AllowInternet) return hits;

        // very small SerpAPI-ish fetch; user supplies key in config
        if (_cfg.SearchBackend == "serpapi" && !string.IsNullOrWhiteSpace(_cfg.SearchApiKey))
        {
            var u = $"https://serpapi.com/search.json?q={System.Web.HttpUtility.UrlEncode(query)}&num={_cfg.SearchMaxResults}&api_key={_cfg.SearchApiKey}";
            using var res = await _http.GetAsync(u, token).ConfigureAwait(false);
            res.EnsureSuccessStatusCode();
            var json = await res.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("organic_results", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                int i = 0;
                foreach (var e in arr.EnumerateArray())
                {
                    if (i++ >= _cfg.SearchMaxResults) break;
                    var title = e.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                    var link = e.TryGetProperty("link", out var l) ? l.GetString() ?? "" : "";
                    var snip = e.TryGetProperty("snippet", out var s) ? s.GetString() ?? "" : "";
                    if (!string.IsNullOrEmpty(title) && !string.IsNullOrEmpty(link))
                        hits.Add(new Hit(title, link, snip));
                }
            }
        }
        // else: could add Bing here similarly; leaving minimal for compile

        return hits;
    }

    public static string FormatForContext(List<Hit> hits)
    {
        if (hits.Count == 0) return "(no results)";
        var sb = new System.Text.StringBuilder();
        int i = 1;
        foreach (var h in hits)
        {
            sb.AppendLine($"{i++}. {h.Title}");
            if (!string.IsNullOrWhiteSpace(h.Snippet))
                sb.AppendLine($"   {h.Snippet}");
            sb.AppendLine($"   {h.Url}");
        }
        return sb.ToString();
    }
}
