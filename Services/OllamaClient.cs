using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace NunuTheAICompanion.Services;

public sealed class OllamaClient
{
    private readonly HttpClient _http;
    private readonly Configuration _config;

    public OllamaClient(HttpClient http, Configuration config)
    {
        _http = http;
        _http.Timeout = TimeSpan.FromSeconds(120);
        _config = config;
    }

    public async IAsyncEnumerable<string> StreamChatAsync(
        string backendUrl,
        List<(string role, string content)> messages,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var mode = (_config.BackendMode ?? "jsonl").ToLowerInvariant();

        if (mode == "jsonl")
        {
            await foreach (var chunk in StreamJsonlAsync(backendUrl, messages, ct))
                yield return chunk;
        }
        else if (mode == "sse")
        {
            await foreach (var chunk in StreamSseAsync(backendUrl, messages, ct))
                yield return chunk;
        }
        else // "plaintext"
        {
            await foreach (var chunk in StreamPlainTextAsync(backendUrl, messages, ct))
                yield return chunk;
        }
    }

    // ---- Direct Ollama JSONL (/api/chat) ----
    private async IAsyncEnumerable<string> StreamJsonlAsync(
        string backendUrl,
        List<(string role, string content)> messages,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var msgList = new List<object>();
        if (!string.IsNullOrWhiteSpace(_config.SystemPrompt))
            msgList.Add(new { role = "system", content = _config.SystemPrompt });

        foreach (var (role, content) in messages)
            msgList.Add(new { role, content });

        var bodyObj = new
        {
            model = _config.ModelName,
            stream = true,
            options = new { temperature = _config.Temperature },
            messages = msgList
        };
        var body = JsonSerializer.Serialize(bodyObj);

        using var req = new HttpRequestMessage(HttpMethod.Post, backendUrl)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var sb = new StringBuilder(8192);
        var buf = new byte[4096];

        while (true)
        {
            var read = await stream.ReadAsync(buf.AsMemory(0, buf.Length), ct).ConfigureAwait(false);
            if (read <= 0) break;

            sb.Append(Encoding.UTF8.GetString(buf, 0, read));

            var text = sb.ToString();
            int lineStart = 0;
            while (true)
            {
                int nl = text.IndexOf('\n', lineStart);
                if (nl < 0) break;
                var line = text.AsSpan(lineStart, nl - lineStart).Trim().ToString();
                lineStart = nl + 1;

                if (line.Length == 0) continue;

                // Extract content safely (no yield inside try/catch)
                var (hasContent, content, done) = TryExtractOllamaContent(line);
                if (hasContent && !string.IsNullOrEmpty(content))
                    yield return content;

                if (done) yield break;
            }

            if (lineStart > 0)
                sb.Remove(0, lineStart);
        }
    }

    // Helper avoids yielding inside try/catch (fixes CS1626)
    private static (bool hasContent, string? content, bool done) TryExtractOllamaContent(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            string? content = null;
            if (root.TryGetProperty("message", out var msgEl) &&
                msgEl.TryGetProperty("content", out var contentEl))
            {
                content = contentEl.GetString();
            }

            bool done = root.TryGetProperty("done", out var doneEl) && doneEl.ValueKind == JsonValueKind.True;
            return (content is not null, content, done);
        }
        catch
        {
            // Malformed/partial line; ignore
            return (false, null, false);
        }
    }

    // ---- SSE proxy (text/event-stream) ----
    private async IAsyncEnumerable<string> StreamSseAsync(
        string backendUrl,
        List<(string role, string content)> messages,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var body = JsonSerializer.Serialize(new
        {
            messages = messages.Select(m => new { role = m.role, content = m.content }).ToArray(),
            system = _config.SystemPrompt,
            temperature = _config.Temperature,
            model = _config.ModelName
        });

        using var req = new HttpRequestMessage(HttpMethod.Post, backendUrl)
        {
            Headers = { { "Accept", "text/event-stream" } },
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        while (!reader.EndOfStream && !ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync().ConfigureAwait(false);
            if (line is null) break;

            if (line.StartsWith("data:"))
            {
                var data = line.Substring(5).TrimStart();
                if (data == "[DONE]") yield break;
                if (!string.IsNullOrEmpty(data)) yield return data;
            }
        }
    }

    // ---- Plain text proxy (bytes) ----
    private async IAsyncEnumerable<string> StreamPlainTextAsync(
        string backendUrl,
        List<(string role, string content)> messages,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var body = JsonSerializer.Serialize(new
        {
            messages = messages.Select(m => new { role = m.role, content = m.content }).ToArray(),
            system = _config.SystemPrompt,
            temperature = _config.Temperature,
            model = _config.ModelName
        });

        using var req = new HttpRequestMessage(HttpMethod.Post, backendUrl)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var buf = new byte[4096];

        while (!ct.IsCancellationRequested)
        {
            var read = await stream.ReadAsync(buf.AsMemory(0, buf.Length), ct).ConfigureAwait(false);
            if (read <= 0) break;
            var chunk = Encoding.UTF8.GetString(buf, 0, read);
            if (!string.IsNullOrEmpty(chunk)) yield return chunk;
        }
    }
}
