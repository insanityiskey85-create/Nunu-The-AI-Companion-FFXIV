using System;
using System.Collections.Generic;

namespace NunuTheAICompanion.Services;

public sealed class MemoryService
{
    public sealed record Entry(DateTime Timestamp, string Role, string Content, string Topic);

    private readonly string _dir;
    private readonly int _max;
    public bool Enabled { get; set; }

    private readonly List<Entry> _items = new();

    public MemoryService(string configDir, int maxEntries, bool enabled)
    {
        _dir = configDir;
        _max = Math.Max(100, maxEntries);
        Enabled = enabled;
    }

    public void Append(string role, string content, string? topic = null)
    {
        if (!Enabled) return;
        var e = new Entry(DateTime.Now, role, content, topic ?? "");
        _items.Add(e);
        if (_items.Count > _max) _items.RemoveRange(0, _items.Count - _max);
    }

    public void Clear() => _items.Clear();

    public List<Entry> Snapshot() => new(_items);
}
