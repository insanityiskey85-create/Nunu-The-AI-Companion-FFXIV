using System;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;

namespace NunuTheAICompanion.Services;

public sealed class IpcChatRelay : IDisposable
{
    private readonly IPluginLog _log;
    private readonly IDalamudPluginInterface _pi;

    private ICallGateSubscriber<string, bool>? _boolResult;
    private ICallGateSubscriber<string, object>? _objResult;

    public IpcChatRelay(IDalamudPluginInterface pi, IPluginLog log)
    {
        _pi = pi ?? throw new ArgumentNullException(nameof(pi));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public bool Bind(string channelName)
    {
        _boolResult = null;
        _objResult = null;
        if (string.IsNullOrWhiteSpace(channelName)) return false;

        var ok = false;
        try
        {
            try { _boolResult = _pi.GetIpcSubscriber<string, bool>(channelName); ok = true; _log.Information("[IpcChatRelay] bool-subscriber bound: {Channel}", channelName); } catch { }
            try { _objResult = _pi.GetIpcSubscriber<string, object>(channelName); ok = true; _log.Information("[IpcChatRelay] obj-subscriber  bound: {Channel}", channelName); } catch { }

            if (!ok) _log.Warning("[IpcChatRelay] No supported subscriber signatures for: {Channel}", channelName);
            return ok;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "[IpcChatRelay] Bind failed");
            return false;
        }
    }

    public bool TrySend(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return false;

        try
        {
            if (_boolResult is not null)
            {
                var ok = _boolResult.InvokeFunc(line);
                _log.Information("[IpcChatRelay] bool sent: {Line} (OK={OK})", line, ok);
                return ok;
            }
            if (_objResult is not null)
            {
                var obj = _objResult.InvokeFunc(line);
                _log.Information("[IpcChatRelay] obj sent: {Line} (Obj={Obj})", line, obj);
                return obj is not null;
            }
            _log.Warning("[IpcChatRelay] No subscriber bound; cannot send.");
            return false;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "[IpcChatRelay] Error while sending IPC line");
            return false;
        }
    }

    public void Dispose() { _boolResult = null; _objResult = null; }
}
