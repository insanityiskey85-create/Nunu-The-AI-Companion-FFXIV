using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace NunuTheAICompanion.Services;

public sealed class MemoryService
{
    public sealed class Entry
    {
        public DateTime TimestampUtc { get; set; }
        public string Role { get; set; } = "";
        public string Content { get; set; } = "";
        public string? Topic { get; set; }
    }

    private readonly string _dir;
    private readonly string _filePath;
    private readonly int _maxEntries;
    private readonly bool _enabled;

    private readonly List<Entry> _entries = new();
    private readonly object _gate = new();

    public bool Enabled => _enabled;

    public MemoryService(string configDir, int maxEntries, bool enabled)
    {
        _enabled = enabled;
        _maxEntries = Math.Max(10, maxEntries);

        _dir = Path.Combine(configDir, "Memories");
        Directory.CreateDirectory(_dir);
        _filePath = Path.Combine(_dir, "memory.jsonl");
    }

    public string StorageDirectory => _dir;
    public string StorageFile => _filePath;

    public void Load()
    {
        if (!_enabled) return;
        lock (_gate)
        {
            _entries.Clear();
            if (!File.Exists(_filePath)) return;

            foreach (var line in File.ReadLines(_filePath, Encoding.UTF8))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var e = JsonSerializer.Deserialize<Entry>(line);
                    if (e != null && !string.IsNullOrWhiteSpace(e.Content))
                        _entries.Add(e);
                }
                catch { /* skip bad line */ }
            }

            if (_entries.Count > _maxEntries)
                _entries.RemoveRange(0, _entries.Count - _maxEntries);
        }
    }

    public void Append(string role, string content, string? topic = null)
    {
        if (!_enabled) return;
        var e = new Entry
        {
            TimestampUtc = DateTime.UtcNow,
            Role = role,
            Content = content,
            Topic = topic
        };

        lock (_gate)
        {
            _entries.Add(e);
            if (_entries.Count > _maxEntries)
                _entries.RemoveRange(0, _entries.Count - _maxEntries);

            var json = JsonSerializer.Serialize(e);
            File.AppendAllText(_filePath, json + "\n", Encoding.UTF8);

            try
            {
                var fi = new FileInfo(_filePath);
                if (fi.Exists && fi.Length > 10 * 1024 * 1024)
                    RewriteTailNoLock();
            }
            catch { /* ignore IO errors */ }
        }
    }

    public void Flush()
    {
        if (!_enabled) return;
        lock (_gate)
        {
            RewriteTailNoLock();
        }
    }

    /// <summary>Dangerous: wipes all persisted memories and in-memory cache.</summary>
    public void ClearAll()
    {
        if (!_enabled) return;
        lock (_gate)
        {
            _entries.Clear();
            try
            {
                if (File.Exists(_filePath))
                {
                    var tmp = _filePath + ".wipe";
                    using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                    { /* create empty file */ }
                    File.Copy(tmp, _filePath, overwrite: true);
                    File.Delete(tmp);
                }
            }
            catch { /* best-effort */ }
        }
    }

    private void RewriteTailNoLock()
    {
        var tmp = _filePath + ".tmp";
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var sw = new StreamWriter(fs, Encoding.UTF8))
        {
            int start = Math.Max(0, _entries.Count - _maxEntries);
            for (int i = start; i < _entries.Count; i++)
                sw.WriteLine(JsonSerializer.Serialize(_entries[i]));
        }
        File.Copy(tmp, _filePath, overwrite: true);
        File.Delete(tmp);
    }

    public List<(string role, string content)> GetRecentForContext(int count)
    {
        var list = new List<(string role, string content)>();
        if (!_enabled) return list;
        lock (_gate)
        {
            int take = Math.Min(count, _entries.Count);
            for (int i = _entries.Count - take; i < _entries.Count; i++)
            {
                var e = _entries[i];
                list.Add((e.Role, e.Content));
            }
        }
        return list;
    }

    public List<Entry> Snapshot()
    {
        lock (_gate) return new List<Entry>(_entries);
    }

    public string ExportTo(string? filePath = null)
    {
        var path = filePath ?? Path.Combine(_dir, $"export-{DateTime.UtcNow:yyyyMMdd-HHmmss}.jsonl");
        lock (_gate)
        {
            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            using var sw = new StreamWriter(fs, Encoding.UTF8);
            foreach (var e in _entries)
                sw.WriteLine(JsonSerializer.Serialize(e));
        }
        return path;
    }

    public int ImportFrom(string importPath, bool keepExisting = true)
    {
        if (!File.Exists(importPath)) return 0;
        var imported = new List<Entry>();
        foreach (var line in File.ReadLines(importPath, Encoding.UTF8))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var e = JsonSerializer.Deserialize<Entry>(line);
                if (e != null && !string.IsNullOrWhiteSpace(e.Content))
                    imported.Add(e);
            }
            catch { }
        }

        lock (_gate)
        {
            if (!keepExisting) _entries.Clear();
            _entries.AddRange(imported);
            if (_entries.Count > _maxEntries)
                _entries.RemoveRange(0, _entries.Count - _maxEntries);
            RewriteTailNoLock();
            return imported.Count;
        }
    }
}
