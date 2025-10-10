using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace NunuTheAICompanion.Services;

/// <summary>
/// Streams plain-text responses from your local proxy.
/// Reads by bytes (not lines), so it works whether the server inserts newlines or not.
/// </summary>
public sealed class OllamaClient
{
    private readonly HttpClient _http;

    public OllamaClient(HttpClient http)
    {
        _http = http;
        _http.Timeout = TimeSpan.FromSeconds(120);
    }

    public async IAsyncEnumerable<string> StreamChatAsync(
        string backendUrl,
        List<(string role, string content)> messages,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var body = JsonSerializer.Serialize(new
        {
            messages = messages.Select(m => new { role = m.role, content = m.content }).ToArray()
        });

        using var req = new HttpRequestMessage(HttpMethod.Post, backendUrl)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct)
                                    .ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var buffer = new byte[4096];

        while (!ct.IsCancellationRequested)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false);
            if (read <= 0) break;

            var chunk = Encoding.UTF8.GetString(buffer, 0, read);
            if (!string.IsNullOrEmpty(chunk))
                yield return chunk;
        }
    }
}
