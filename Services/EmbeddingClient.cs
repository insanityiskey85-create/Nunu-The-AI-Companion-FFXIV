using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NunuTheAICompanion.Services
{
    /// <summary>
    /// Minimal embeddings client for Ollama-compatible /api/embeddings.
    /// Returns a dense float vector for given text. If disabled, returns an empty array.
    /// </summary>
    public sealed class EmbeddingClient
    {
        private readonly HttpClient _http;
        private readonly Configuration _cfg;

        public EmbeddingClient(HttpClient http, Configuration cfg)
        {
            _http = http;
            _cfg = cfg;
        }

        public async Task<float[]> EmbedAsync(string text, CancellationToken token = default)
        {
            if (!_cfg.SoulThreadsEnabled) return Array.Empty<float>();
            var model = string.IsNullOrWhiteSpace(_cfg.EmbeddingModel) ? "nomic-embed-text" : _cfg.EmbeddingModel!;
            var url = (_cfg.ChatEndpointUrl ?? "http://localhost:11434")!.TrimEnd('/') + "/api/embeddings";

            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { model, prompt = text ?? "" }), Encoding.UTF8, "application/json")
            };
            using var res = await _http.SendAsync(req, token).ConfigureAwait(false);
            res.EnsureSuccessStatusCode();
            var json = await res.Content.ReadAsStringAsync(token).ConfigureAwait(false);

            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("embedding", out var emb) && emb.ValueKind == JsonValueKind.Array)
            {
                var vec = new List<float>(emb.GetArrayLength());
                foreach (var v in emb.EnumerateArray())
                    vec.Add(v.GetSingle());
                return vec.ToArray();
            }
            return Array.Empty<float>();
        }

        public static double Cosine(float[] a, float[] b)
        {
            if (a.Length == 0 || b.Length == 0 || a.Length != b.Length) return 0;
            double dot = 0, na = 0, nb = 0;
            for (int i = 0; i < a.Length; i++)
            {
                dot += a[i] * b[i];
                na += a[i] * a[i];
                nb += b[i] * b[i];
            }
            if (na == 0 || nb == 0) return 0;
            return dot / (Math.Sqrt(na) * Math.Sqrt(nb));
        }

        public static float[] Average(params float[][] vectors)
        {
            if (vectors == null || vectors.Length == 0) return Array.Empty<float>();
            int d = vectors[0].Length;
            if (d == 0) return Array.Empty<float>();
            var sum = new double[d];
            int count = 0;
            foreach (var v in vectors)
            {
                if (v.Length != d) continue;
                for (int i = 0; i < d; i++) sum[i] += v[i];
                count++;
            }
            if (count == 0) return Array.Empty<float>();
            var avg = new float[d];
            for (int i = 0; i < d; i++) avg[i] = (float)(sum[i] / count);
            return avg;
        }
    }
}
