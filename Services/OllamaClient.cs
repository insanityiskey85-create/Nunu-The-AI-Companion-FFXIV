using Nunu_The_AI_Companion;
using System.Collections.Generic;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace NunuTheAICompanion.Services;

public sealed class OllamaClient
{
    private readonly HttpClient _http;
    private readonly Configuration _cfg;

    public OllamaClient(HttpClient http, Configuration cfg)
    {
        _http = http;
        _cfg = cfg;
    }

    /// <summary>
    /// Minimal, safe streaming reader with ZERO catch blocks (so we can yield legally).
    /// We do not parse JSON to avoid parse exceptions; we just emit text lines as they arrive.
    /// Caller (PluginMain.StreamAssistantAsync) already has try/catch to surface errors.
    /// </summary>
    public async IAsyncEnumerable<string> StreamChatAsync(
        string url,
        List<(string role, string content)> history,
        [EnumeratorCancellation] CancellationToken token)
    {
        // Build request payload
        var payload = new
        {
            model = _cfg.ModelName,
            stream = true,
            temperature = _cfg.Temperature,
            messages = history.ConvertAll(m => new { role = m.role, content = m.content }),
            system = _cfg.SystemPrompt
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };

        using var res = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        res.EnsureSuccessStatusCode();

        using var s = await res.Content.ReadAsStreamAsync(token).ConfigureAwait(false);

        var buf = new byte[4096];
        var sb = new StringBuilder();

        while (!token.IsCancellationRequested)
        {
            var n = await s.ReadAsync(buf, 0, buf.Length, token).ConfigureAwait(false);
            if (n <= 0) break;

            sb.Append(Encoding.UTF8.GetString(buf, 0, n));

            // emit complete lines
            while (true)
            {
                var all = sb.ToString();
                var nl = all.IndexOf('\n');
                if (nl < 0) break;

                var line = all[..nl].Trim();
                sb.Remove(0, nl + 1);

                if (line.Length == 0) continue;

                // If the backend uses SSE like "data: {...}" strip the prefix.
                const string dataPrefix = "data:";
                if (line.StartsWith(dataPrefix))
                    line = line[dataPrefix.Length..].Trim();

                // If it's a simple JSON object with "response"/"message.content", we *attempt*
                // a safe extraction WITHOUT try/catch by doing cheap checks; otherwise emit raw.
                if (line.Length > 0 && line[0] == '{' && line[^1] == '}')
                {
                    // Heuristic extraction without throwing: look for quoted field names.
                    // If not found, just emit the raw line.
                    // (We avoid JsonDocument.Parse to keep this try/catch-free.)
                    var lower = line.ToLowerInvariant();
                    var kMsg = "\"message\"";
                    var kCont = "\"content\"";
                    var kResp = "\"response\"";

                    if (lower.Contains(kResp))
                    {
                        // Very loose extraction: "response":"...".
                        var idx = lower.IndexOf(kResp);
                        var colon = line.IndexOf(':', idx);
                        if (colon > 0)
                        {
                            var val = line[(colon + 1)..].Trim().TrimStart('"');
                            var end = val.LastIndexOf('"');
                            if (end > 0) val = val[..end];
                            if (!string.IsNullOrEmpty(val)) { yield return val; continue; }
                        }
                    }
                    else if (lower.Contains(kMsg) && lower.Contains(kCont))
                    {
                        // Loose extraction for message.content
                        var contIdx = lower.IndexOf(kCont);
                        var colon = line.IndexOf(':', contIdx);
                        if (colon > 0)
                        {
                            var val = line[(colon + 1)..].Trim().TrimStart('"');
                            var end = val.LastIndexOf('"');
                            if (end > 0) val = val[..end];
                            if (!string.IsNullOrEmpty(val)) { yield return val; continue; }
                        }
                    }
                }

                // Fallback: emit raw line/chunk
                yield return line;
            }
        }
    }
}
