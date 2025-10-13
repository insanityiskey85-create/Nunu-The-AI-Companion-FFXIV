using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Dalamud.Plugin.Services;

namespace NunuTheAICompanion.Services
{
    /// <summary>
    /// Topic-aware "Soul Threads" built on top of MemoryService entries.
    /// Performs lightweight online clustering with embeddings from EmbeddingClient.
    /// Persists threads to threads.json in the Memories directory.
    /// </summary>
    public sealed class SoulThreadService
    {
        public sealed class ThreadInfo
        {
            public string Id { get; set; } = Guid.NewGuid().ToString("N");
            public string Label { get; set; } = "untitled";
            public float[] Centroid { get; set; } = Array.Empty<float>();
            public List<int> EntryIndices { get; set; } = new();
            public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
        }

        private readonly MemoryService _mem;
        private readonly EmbeddingClient _embed;
        private readonly Configuration _cfg;
        private readonly IPluginLog _log;
        private readonly string _filePath;
        private readonly object _gate = new();
        private List<ThreadInfo> _threads = new();

        public IReadOnlyList<ThreadInfo> Threads { get { lock (_gate) return _threads.ToList(); } }
        public string StorageFile => _filePath;

        public SoulThreadService(MemoryService mem, EmbeddingClient embed, Configuration cfg, IPluginLog log)
        {
            _mem = mem;
            _embed = embed;
            _cfg = cfg;
            _log = log;
            _filePath = Path.Combine(mem.StorageDirectory, "threads.json");
            Load();
        }

        public void Load()
        {
            try
            {
                if (File.Exists(_filePath))
                {
                    var json = File.ReadAllText(_filePath);
                    var list = System.Text.Json.JsonSerializer.Deserialize<List<ThreadInfo>>(json);
                    if (list != null) _threads = list;
                }
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "[SoulThreads] load failed");
            }
        }

        private void SaveNoThrow()
        {
            try
            {
                var json = System.Text.Json.JsonSerializer.Serialize(_threads, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_filePath, json);
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "[SoulThreads] save failed");
            }
        }

        public string? AppendAndThread(string role, string content, string? authorOrTopic = null, CancellationToken token = default)
        {
            if (!_cfg.SoulThreadsEnabled)
            {
                _mem.Append(role, content, topic: authorOrTopic);
                return null;
            }

            float[] vec = Array.Empty<float>();
            try { vec = _embed.EmbedAsync(content, token).GetAwaiter().GetResult(); }
            catch (Exception ex) { _log.Warning(ex, "[SoulThreads] embed failed; falling back to plain memory"); }

            _mem.Append(role, content, topic: authorOrTopic);
            var snapshot = _mem.Snapshot();
            int idx = snapshot.Count - 1;

            lock (_gate)
            {
                var now = DateTime.UtcNow;
                var threshold = Math.Clamp(_cfg.ThreadSimilarityThreshold, 0.1f, 0.95f);

                NunuTheAICompanion.Services.SoulThreadService.ThreadInfo? best = null;
                double bestSim = -1;
                foreach (var t in _threads)
                {
                    var sim = EmbeddingClient.Cosine(vec, t.Centroid);
                    if (sim > bestSim) { bestSim = sim; best = t; }
                }

                if (best == null || bestSim < threshold || best.Centroid.Length == 0)
                {
                    var label = GuessLabel(authorOrTopic, content);
                    var t = new ThreadInfo
                    {
                        Label = label,
                        Centroid = vec,
                        EntryIndices = new List<int> { idx },
                        UpdatedUtc = now
                    };
                    _threads.Add(t);
                    SaveNoThrow();
                    return t.Id;
                }
                else
                {
                    best.EntryIndices.Add(idx);
                    best.Centroid = EmbeddingClient.Average(best.Centroid, vec);
                    best.UpdatedUtc = now;
                    SaveNoThrow();
                    return best.Id;
                }
            }
        }

        public List<(string role, string content)> GetContextFor(string userPrompt, int maxFromThread, int maxRecent, CancellationToken token = default)
        {
            var recent = _mem.GetRecentForContext(maxRecent);
            if (!_cfg.SoulThreadsEnabled) return recent;

            float[] q = Array.Empty<float>();
            try { q = _embed.EmbedAsync(userPrompt, token).GetAwaiter().GetResult(); } catch { }

            ThreadInfo? best = null;
            double bestSim = -1;
            lock (_gate)
            {
                foreach (var t in _threads)
                {
                    var sim = EmbeddingClient.Cosine(q, t.Centroid);
                    if (sim > bestSim) { bestSim = sim; best = t; }
                }
            }

            var ctx = new List<(string role, string content)>(recent);
            if (best != null && best.EntryIndices.Count > 0)
            {
                var snap = _mem.Snapshot();
                var take = Math.Min(maxFromThread, best.EntryIndices.Count);
                foreach (var idx in best.EntryIndices.Skip(Math.Max(0, best.EntryIndices.Count - take)))
                {
                    if (idx >= 0 && idx < snap.Count)
                        ctx.Add((snap[idx].Role, snap[idx].Content));
                }
            }
            return ctx;
        }

        public List<(string id, string label, int count, DateTime updated)> GetThreadSummaries()
        {
            lock (_gate)
            {
                return _threads
                    .OrderByDescending(t => t.UpdatedUtc)
                    .Select(t => (t.Id, t.Label, t.EntryIndices.Count, t.UpdatedUtc))
                    .ToList();
            }
        }

        private static string GuessLabel(string? topic, string content)
        {
            if (!string.IsNullOrWhiteSpace(topic)) return topic!;
            var words = (content ?? "").Replace('\n', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return string.Join(' ', words.Take(5)).Trim();
        }
    }
}
