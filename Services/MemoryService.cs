using System.Text;
using System.Text.Json;

namespace NunuTheAICompanion.Services;

public sealed class MemoryService
{
    public sealed record MemoryEntry(
        DateTimeOffset At,
        string Role,           // "user" | "assistant" | "system"
        string Content,
        string? Topic,         // optional tag
        string? Source         // e.g., "chat"
    );

    private readonly string _memoryDir;
    private readonly string _memoryFile;
    private readonly JsonSerializerOptions _json = new() { WriteIndented = false };

    public int MaxEntries { get; set; } = 1000;
    public bool Enabled { get; set; } = true;

    private readonly object _gate = new();
    private List<MemoryEntry> _cache = new();

    public MemoryService(string baseConfigDir, int maxEntries, bool enabled)
    {
        _memoryDir = Path.Combine(baseConfigDir, "memory");
        Directory.CreateDirectory(_memoryDir);
        _memoryFile = Path.Combine(_memoryDir, "memories.jsonl");

        MaxEntries = Math.Max(100, maxEntries);
        Enabled = enabled;

        Load();
    }

    public void Load()
    {
        lock (_gate)
        {
            _cache.Clear();
            if (!File.Exists(_memoryFile))
                return;

            foreach (var line in File.ReadLines(_memoryFile, Encoding.UTF8))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var entry = JsonSerializer.Deserialize<MemoryEntry>(line, _json);
                    if (entry is not null) _cache.Add(entry);
                }
                catch
                {
                    // Skip malformed lines
                }
            }
        }
    }

    public void SaveSnapshot()
    {
        lock (_gate)
        {
            using var fs = new FileStream(_memoryFile, FileMode.Create, FileAccess.Write, FileShare.Read);
            using var sw = new StreamWriter(fs, Encoding.UTF8);
            foreach (var m in _cache)
            {
                sw.WriteLine(JsonSerializer.Serialize(m, _json));
            }
        }
    }

    public void Append(string role, string content, string? topic = null, string? source = "chat")
    {
        if (!Enabled) return;
        var entry = new MemoryEntry(DateTimeOffset.UtcNow, role, content, topic, source);

        lock (_gate)
        {
            _cache.Add(entry);
            if (_cache.Count > MaxEntries)
            {
                var excess = _cache.Count - MaxEntries;
                _cache.RemoveRange(0, excess);
            }

            // Append-only line write for durability
            using var fs = new FileStream(_memoryFile, FileMode.Append, FileAccess.Write, FileShare.Read);
            using var sw = new StreamWriter(fs, Encoding.UTF8);
            sw.WriteLine(JsonSerializer.Serialize(entry, _json));
        }
    }

    public IReadOnlyList<MemoryEntry> GetAll() =>
        _cache.OrderBy(m => m.At).ToList();

    public IReadOnlyList<MemoryEntry> GetRecent(int count = 50) =>
        _cache.OrderByDescending(m => m.At).Take(count).OrderBy(m => m.At).ToList();

    public IReadOnlyList<MemoryEntry> Search(string query, string? role = null, string? topic = null)
    {
        query = query?.Trim() ?? "";
        return _cache.Where(m =>
                (string.IsNullOrEmpty(role) || string.Equals(m.Role, role, StringComparison.OrdinalIgnoreCase)) &&
                (string.IsNullOrEmpty(topic) || string.Equals(m.Topic, topic, StringComparison.OrdinalIgnoreCase)) &&
                (string.IsNullOrEmpty(query) || m.Content.Contains(query, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(m => m.At)
            .ToList();
    }

    public int RemoveWhere(Func<MemoryEntry, bool> predicate)
    {
        lock (_gate)
        {
            var before = _cache.Count;
            _cache = _cache.Where(m => !predicate(m)).ToList();
            SaveSnapshot();
            return before - _cache.Count;
        }
    }

    public string ExportPath()
    {
        var name = $"memories_{DateTime.UtcNow:yyyyMMdd_HHmmss}.jsonl";
        return Path.Combine(_memoryDir, name);
    }

    public void ExportTo(string path)
    {
        lock (_gate)
        {
            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
            using var sw = new StreamWriter(fs, Encoding.UTF8);
            foreach (var m in _cache.OrderBy(m => m.At))
            {
                sw.WriteLine(JsonSerializer.Serialize(m, _json));
            }
        }
    }
}
