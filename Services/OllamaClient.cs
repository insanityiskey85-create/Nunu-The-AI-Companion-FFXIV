using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NunuTheAICompanion.Services
{
    /// <summary>
    /// Minimal Ollama chat client with robust streaming.
    /// Supports both Ollama's NDJSON and SSE-style "data:" lines.
    /// </summary>
    public sealed class OllamaClient
    {
        private readonly HttpClient _http;
        private readonly string _model;
        private readonly float _temperature;

        public OllamaClient(HttpClient http, Configuration config)
        {
            _http = http ?? throw new ArgumentNullException(nameof(http));
            _model = string.IsNullOrWhiteSpace(config.ModelName) ? "llama3" : config.ModelName!;
            _temperature = config.Temperature <= 0 ? 0.7f : config.Temperature;
        }

        /// <summary>
        /// Stream a chat completion from Ollama. 
        /// history: list of (role, content) tuples. baseUrl should point to /api/chat (or its base; we append if needed).
        /// </summary>
        public async IAsyncEnumerable<string> StreamChatAsync(
            string baseUrl,
            List<(string role, string content)> history,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
                throw new ArgumentException("Base URL is empty.", nameof(baseUrl));

            var url = baseUrl.EndsWith("/api/chat", StringComparison.OrdinalIgnoreCase)
                ? baseUrl
                : $"{baseUrl.TrimEnd('/')}/api/chat";

            // Build messages payload
            var msgs = new List<object>(history.Count);
            foreach (var (role, content) in history)
                msgs.Add(new { role, content });

            var payload = new
            {
                model = _model,
                temperature = _temperature,
                messages = msgs,
                stream = true
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };

            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();

            await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 8192, leaveOpen: true);

            while (!reader.EndOfStream && !ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync().ConfigureAwait(false);
                if (line is null) break;
                if (line.Length == 0) continue;

                // SSR prefix like "data: ..."
                if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                    line = line.Substring(5).Trim();

                // Ollama end marker
                if (string.Equals(line, "[DONE]", StringComparison.Ordinal))
                    yield break;

                foreach (var chunk in ExtractChunksFromLine(line))
                    yield return chunk;
            }
        }

        /// <summary>
        /// Optional: non-streaming helper, returns full text.
        /// </summary>
        public async Task<string> CompleteAsync(string baseUrl, List<(string role, string content)> history, CancellationToken ct)
        {
            var sb = new StringBuilder();
            await foreach (var chunk in StreamChatAsync(baseUrl, history, ct))
                sb.Append(chunk);
            return sb.ToString();
        }

        /// <summary>
        /// Parses one NDJSON/SSE line into 0..N text chunks.
        /// Try/catch is contained in this helper (no yield in try/catch in caller).
        /// </summary>
        private static IEnumerable<string> ExtractChunksFromLine(string line)
        {
            var results = new List<string>(2);
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;

                // Done flag?
                if (root.TryGetProperty("done", out var doneProp) && doneProp.ValueKind == JsonValueKind.True)
                    return results; // empty

                // Shape 1: Ollama: { message: { content }, ... }
                if (root.TryGetProperty("message", out var message) &&
                    message.ValueKind == JsonValueKind.Object &&
                    message.TryGetProperty("content", out var content1) &&
                    content1.ValueKind == JsonValueKind.String)
                {
                    var text = content1.GetString();
                    if (!string.IsNullOrEmpty(text))
                        results.Add(text!);
                    return results;
                }

                // Shape 2: OpenAI-like: { choices: [{ delta: { content } }] }
                if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array)
                {
                    foreach (var ch in choices.EnumerateArray())
                    {
                        if (ch.TryGetProperty("delta", out var delta) &&
                            delta.ValueKind == JsonValueKind.Object &&
                            delta.TryGetProperty("content", out var content2) &&
                            content2.ValueKind == JsonValueKind.String)
                        {
                            var text = content2.GetString();
                            if (!string.IsNullOrEmpty(text))
                                results.Add(text!);
                        }
                    }
                    return results;
                }

                // Fallback: raw content
                if (root.TryGetProperty("content", out var content3) &&
                    content3.ValueKind == JsonValueKind.String)
                {
                    var text = content3.GetString();
                    if (!string.IsNullOrEmpty(text))
                        results.Add(text!);
                }
            }
            catch
            {
                // Malformed line; ignore.
            }
            return results;
        }
    }
}
