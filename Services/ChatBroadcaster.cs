using System;
using System.Collections.Concurrent;
using System.Text;
using System.Threading;
using Dalamud.Game.Command;
using Dalamud.Plugin.Services;
using NunuTheAICompanion.Interop;

namespace NunuTheAICompanion.Services;

public sealed class ChatBroadcaster : IDisposable
{
    private readonly IPluginLog _log;
    private readonly ICommandManager _cmd;
    private readonly IFramework _framework;

    private readonly ConcurrentQueue<(NunuChannel ch, string text)> _queue = new();
    private readonly AutoResetEvent _wake = new(false);
    private readonly Thread _worker;
    private volatile bool _running = true;

    private IpcChatRelay? _ipc;
    private bool _preferIpc = false;
    private Func<string, bool>? _nativeSend;

    private int _sentThisMinute = 0;
    private DateTime _windowStart = DateTime.UtcNow;

    public ChatBroadcaster(ICommandManager cmd, IFramework framework, IPluginLog log)
    {
        _cmd = cmd;
        _framework = framework;
        _log = log;
        _worker = new Thread(Work) { IsBackground = true, Name = "Nunu.ChatBroadcaster" };
        _worker.Start();
    }

    public void Dispose() { try { _running = false; _wake.Set(); } catch { } }

    public enum NunuChannel { Say, Party, FreeCompany, Shout, Yell, Echo }

    public bool Enabled { get; set; } = false;
    public int MaxPerMinute { get; set; } = 6;
    public int DelayBetweenLinesMs { get; set; } = 1500;
    public int MaxChunkLen { get; set; } = 430;

    public void SetIpcRelay(IpcChatRelay? relay, bool preferIpc) { _ipc = relay; _preferIpc = preferIpc; }
    public void SetNativeSender(Func<string, bool>? nativeSend) { _nativeSend = nativeSend; }

    public void Enqueue(NunuChannel ch, string text)
    {
        if (!Enabled) { _log.Information("[Broadcaster] Ignored enqueue (disabled)."); return; }
        if (string.IsNullOrWhiteSpace(text)) return;

        foreach (var part in Chunk(text, MaxChunkLen))
            _queue.Enqueue((ch, part));
        _wake.Set();
    }

    private string FormatOutgoing(NunuChannel ch, string body)
    {
        var cfg = PluginMain.Instance.Config;
        var persona = cfg.BroadcastAsPersona ? (cfg.PersonaName ?? "Little Nunu") : null;
        if (!string.IsNullOrWhiteSpace(persona))
        {
            var label = $"[{persona}] ";
            var budget = Math.Max(1, MaxChunkLen - label.Length);
            if (body.Length > budget) body = body[..budget];
            body = label + body;
        }
        return $"{ChannelPrefix(ch)} {body}".TrimEnd();
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
                    ResetWindowIfNeeded();
                    if (_sentThisMinute >= Math.Max(1, MaxPerMinute))
                    {
                        var wait = 60_000 - (int)(DateTime.UtcNow - _windowStart).TotalMilliseconds + 250;
                        if (wait > 0) Thread.Sleep(wait);
                        ResetWindowIfNeeded();
                    }

                    var line = FormatOutgoing(item.ch, item.text);
                    _log.Information("[Broadcaster] sending: {Line}", line);

                    bool delivered = false;
                    try
                    {
                        if (_nativeSend is not null) delivered = _nativeSend(line);
                        if (!delivered && _preferIpc && _ipc is not null) delivered = _ipc.TrySend(line);
                        if (!delivered)
                        {
                            _framework.RunOnFrameworkThread(() =>
                            {
                                try { _cmd.ProcessCommand(line); } catch (Exception ex) { _log.Error(ex, "[Broadcaster] ProcessCommand failed"); }
                            });
                            delivered = true;
                        }
                        if (delivered) _sentThisMinute++;
                    }
                    catch (Exception ex) { _log.Error(ex, "[Broadcaster] send failed"); }

                    Thread.Sleep(Math.Max(250, DelayBetweenLinesMs));
                }
                _wake.WaitOne();
            }
            catch (Exception ex) { _log.Error(ex, "[Broadcaster] worker error"); Thread.Sleep(500); }
        }
        _log.Information("[Broadcaster] worker ended");
    }

    private void ResetWindowIfNeeded()
    {
        var now = DateTime.UtcNow;
        if ((now - _windowStart).TotalSeconds >= 60) { _windowStart = now; _sentThisMinute = 0; }
    }

    private static string ChannelPrefix(NunuChannel ch) => ch switch
    {
        NunuChannel.Say         => "/say",
        NunuChannel.Party       => "/p",
        NunuChannel.FreeCompany => "/fc",
        NunuChannel.Shout       => "/sh",
        NunuChannel.Yell        => "/y",
        NunuChannel.Echo        => "/echo",
        _ => "/say"
    };

    private static string[] Chunk(string s, int maxLen)
    {
        var list = new System.Collections.Generic.List<string>();
        var sb = new StringBuilder(maxLen);
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
