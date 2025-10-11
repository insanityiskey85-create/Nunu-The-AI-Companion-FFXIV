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
    /// Streams assistant text from an Ollama-compatible endpoint.
    /// Consumes SSE/JSONL lines like: data: {"message":{"content":"..."},"done":false}
    /// Emits ONLY the human-readable text (message.content or response).
    /// No try/catch here (callers handle errors).
    /// </summary>
    public async IAsyncEnumerable<string> StreamChatAsync(
        string url,
        List<(string role, string content)> history,
        [EnumeratorCancellation] CancellationToken token)
    {
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

        var buf = new byte[8192];
        var acc = new StringBuilder();   // raw incoming chars (SSE lines)
        var obj = new StringBuilder();   // current JSON object
        var depth = 0;                   // brace depth
        var inStr = false;               // inside JSON string
        var esc = false;                 // previous char was '\'

        static string? ExtractText(string json)
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Prefer "message.content" (Ollama chat)
            if (root.TryGetProperty("message", out var msg) &&
                msg.ValueKind == JsonValueKind.Object &&
                msg.TryGetProperty("content", out var content) &&
                content.ValueKind == JsonValueKind.String)
                return content.GetString();

            // Fallback "response" (some backends)
            if (root.TryGetProperty("response", out var resp) &&
                resp.ValueKind == JsonValueKind.String)
                return resp.GetString();

            // Ignore objects that don't contain text (done/telemetry)
            return null;
        }

        while (!token.IsCancellationRequested)
        {
            var n = await s.ReadAsync(buf, 0, buf.Length, token).ConfigureAwait(false);
            if (n <= 0) break;

            acc.Append(Encoding.UTF8.GetString(buf, 0, n));

            for (int i = 0; i < acc.Length; i++)
            {
                var ch = acc[i];

                // Strip 'data:' prefix when not inside an object
                if (depth == 0 && !inStr)
                {
                    if (char.IsWhiteSpace(ch)) continue;

                    if (acc.Length - i >= 5 &&
                        acc[i] == 'd' && acc[i + 1] == 'a' && acc[i + 2] == 't' && acc[i + 3] == 'a' && acc[i + 4] == ':')
                    {
                        i += 4;
                        continue;
                    }
                }

                // handle JSON string/escape state
                if (inStr)
                {
                    obj.Append(ch);
                    if (esc) { esc = false; continue; }
                    if (ch == '\\') { esc = true; continue; }
                    if (ch == '"') inStr = false;
                    continue;
                }

                if (ch == '"') { inStr = true; obj.Append(ch); continue; }

                if (ch == '{')
                {
                    depth++;
                    obj.Append(ch);
                    continue;
                }

                if (ch == '}')
                {
                    if (depth > 0) depth--;
                    obj.Append(ch);

                    if (depth == 0)
                    {
                        var text = ExtractText(obj.ToString());
                        if (!string.IsNullOrEmpty(text))
                            yield return text!;
                        obj.Clear();
                    }
                    continue;
                }

                if (depth > 0)
                    obj.Append(ch);
            }

            // all characters consumed into obj or skipped; clear accumulator
            acc.Clear();
        }

        // flush any final balanced object
        if (depth == 0 && obj.Length > 0)
        {
            var text = ExtractText(obj.ToString());
            if (!string.IsNullOrEmpty(text))
                yield return text!;
        }
    }
}
