using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.IO;

namespace NunuTheAICompanion.Services;

public sealed class OllamaClient
{
    private readonly HttpClient _http;

    public OllamaClient(HttpClient http)
    {
        _http = http;
        _http.Timeout = TimeSpan.FromSeconds(120);
    }

    public async IAsyncEnumerable<string> StreamChatAsync(string backendUrl, List<(string role, string content)> messages, CancellationToken ct = default)
    {
        var body = JsonSerializer.Serialize(new
        {
            messages = messages.Select(m => new { role = m.role, content = m.content }).ToArray()
        });

        using var req = new HttpRequestMessage(HttpMethod.Post, backendUrl)
        {
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
            if (line.Length == 0) continue;
            yield return line;
        }
    }
}
