using System;
using System.Collections.Concurrent;
using System.Text;
using System.Threading;
using Dalamud.Game.Command;
using Dalamud.Plugin.Services;

namespace NunuTheAICompanion.Services;

/// <summary>
/// Safely posts Little Nunu's messages into in-game channels (/say, /p, /fc, etc.)
/// Uses CommandManager.ProcessCommand on the Framework thread.
/// </summary>
public sealed class ChatBroadcaster : IDisposable
{
    private readonly IPluginLog _log;
    private readonly ICommandManager _cmd;
    private readonly IFramework _framework;

    private readonly ConcurrentQueue<(NunuChannel ch, string text)> _queue = new();
    private readonly AutoResetEvent _wake = new(false);
    private readonly Thread _worker;
    private volatile bool _running = true;

    // Rate limit: N messages per minute
    private int _sentThisMinute = 0;
    private DateTime _windowStart = DateTime.UtcNow;

    public ChatBroadcaster(ICommandManager cmd, IFramework framework, IPluginLog log)
    {
        _cmd = cmd ?? throw new ArgumentNullException(nameof(cmd));
        _framework = framework ?? throw new ArgumentNullException(nameof(framework));
        _log = log ?? throw new ArgumentNullException(nameof(log));

        _worker = new Thread(Work) { IsBackground = true, Name = "Nunu.ChatBroadcaster" };
        _worker.Start();
    }

    public void Dispose()
    {
        try
        {
            _running = false;
            _wake.Set();
        }
        catch { }
    }

    public enum NunuChannel { Say, Party, FreeCompany, Shout, Yell /*, Tell (needs target) */ }

    public bool Enabled { get; set; } = false;          // master toggle
    public int MaxPerMinute { get; set; } = 6;          // conservative default
    public int DelayBetweenLinesMs { get; set; } = 1500;
    public int MaxChunkLen { get; set; } = 430;         // leave room for prefix

    /// <summary>Enqueue a message for a channel. Decoupled from any "listening" flags.</summary>
    public void Enqueue(NunuChannel ch, string text)
    {
        if (!Enabled) { _log.Information("[Broadcaster] Ignored enqueue (disabled)."); return; }
        if (string.IsNullOrWhiteSpace(text)) return;

        foreach (var part in Chunk(text, MaxChunkLen))
        {
            _queue.Enqueue((ch, part));
            _log.Information("[Broadcaster] queued {Channel}: \"{Part}\"", ch, part);
        }

        _wake.Set();
    }

    private void Work()
    {
        _log.Information("[Broadcaster] worker started");
        while (_running)
        {
            try
            {
                while (_queue.TryDequeue(out var item))
                {
                    // Sliding window rate limit
                    ResetWindowIfNeeded();
                    if (_sentThisMinute >= Math.Max(1, MaxPerMinute))
                    {
                        var wait = 60_000 - (int)(DateTime.UtcNow - _windowStart).TotalMilliseconds + 250;
                        _log.Information("[Broadcaster] rate limit hit; sleeping {Wait} ms", wait);
                        if (wait > 0) Thread.Sleep(wait);
                        ResetWindowIfNeeded();
                    }

                    var prefix = ChannelPrefix(item.ch);
                    var line = $"{prefix} {item.text}".TrimEnd();
                    _log.Information("[Broadcaster] sending: {Line}", line);

                    // IMPORTANT: send on Framework thread
                    try
                    {
                        _framework.RunOnFrameworkThread(() =>
                        {
                            try { _cmd.ProcessCommand(line); }
                            catch (Exception ex) { _log.Error(ex, "[Broadcaster] ProcessCommand failed for: {Line}", line); }
                        });
                        _sentThisMinute++;
                    }
                    catch (Exception ex)
                    {
                        _log.Error(ex, "[Broadcaster] scheduling command failed");
                    }

                    Thread.Sleep(Math.Max(250, DelayBetweenLinesMs));
                }

                _wake.WaitOne();
            }
            catch (Exception ex)
            {
                _log.Error(ex, "[Broadcaster] worker error");
                Thread.Sleep(1000);
            }
        }
        _log.Information("[Broadcaster] worker ended");
    }

    private void ResetWindowIfNeeded()
    {
        var now = DateTime.UtcNow;
        if ((now - _windowStart).TotalSeconds >= 60)
        {
            _windowStart = now;
            _sentThisMinute = 0;
        }
    }

    private static string ChannelPrefix(NunuChannel ch) => ch switch
    {
        NunuChannel.Say => "/say",
        NunuChannel.Party => "/p",
        NunuChannel.FreeCompany => "/fc",
        NunuChannel.Shout => "/sh",
        NunuChannel.Yell => "/y",
        _ => "/say"
    };

    private static string[] Chunk(string s, int maxLen)
    {
        var list = new System.Collections.Generic.List<string>();
        var sb = new StringBuilder(maxLen);

        // Normalize newlines to spaces (commands ignore hard newlines)
        s = s.Replace("\r\n", " ").Replace('\n', ' ');

        foreach (var word in s.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (sb.Length + word.Length + 1 > maxLen)
            {
                if (sb.Length > 0) { list.Add(sb.ToString()); sb.Clear(); }
            }
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(word);
        }
        if (sb.Length > 0) list.Add(sb.ToString());
        return list.ToArray();
    }
}
