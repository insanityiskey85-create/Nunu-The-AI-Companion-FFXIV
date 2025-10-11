using System;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;

namespace NunuTheAICompanion.Services;

public sealed class ChatListener : IDisposable
{
    private readonly IChatGui _chat;
    private readonly IPluginLog _log;
    private readonly Configuration _cfg;
    private readonly Action<string, string> _onHeard; // (author, text)
    private readonly Action<string>? _mirror;

    public ChatListener(IChatGui chat, IPluginLog log, Configuration cfg,
                        Action<string, string> onHeard, Action<string>? mirrorDebugToWindow = null)
    {
        _chat = chat;
        _log = log;
        _cfg = cfg;
        _onHeard = onHeard;
        _mirror = mirrorDebugToWindow;
        _chat.ChatMessage += OnChatMessage;
        _log.Information("[Listener] Hooked.");
    }

    public void Dispose()
    {
        _chat.ChatMessage -= OnChatMessage;
        _log.Information("[Listener] Unhooked.");
    }

    private bool ChannelAllowed(XivChatType type) => type switch
    {
        XivChatType.Say => _cfg.ListenSay,
        XivChatType.TellIncoming => _cfg.ListenTell,
        XivChatType.TellOutgoing => _cfg.ListenTell,
        XivChatType.Party => _cfg.ListenParty,
        XivChatType.Alliance => _cfg.ListenAlliance,
        XivChatType.FreeCompany => _cfg.ListenFreeCompany,
        XivChatType.Shout => _cfg.ListenShout,
        XivChatType.Yell => _cfg.ListenYell,
        _ => false
    };

    private bool PassesCallsign(string text)
    {
        if (!_cfg.RequireCallsign) return true;
        var cs = _cfg.Callsign ?? "@nunu";
        return text.IndexOf(cs, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private bool AuthorAllowed(string author)
    {
        if (_cfg.ListenSelf) return true;
        if (_cfg.Whitelist is { Count: > 0 })
            return _cfg.Whitelist.Contains(author, StringComparer.OrdinalIgnoreCase);
        return true;
    }

    private void OnChatMessage(XivChatType type, int senderId, ref SeString sender, ref SeString message, ref bool isHandled)
    {
        try
        {
            if (!_cfg.ListenEnabled) return;
            if (!ChannelAllowed(type)) return;

            var author = sender.TextValue?.Trim() ?? "";
            var text = message.TextValue?.Trim() ?? "";
            if (string.IsNullOrEmpty(text)) return;

            if (!AuthorAllowed(author)) return;
            if (!PassesCallsign(text)) return;

            _log.Information("[heard] ts=0 type={Type} author='{Author}' text='{Text}'", (int)type, author, text);
            if (_cfg.DebugListen) _mirror?.Invoke($"[heard {type}] {author}: {text}");

            // Strip callsign before handing to model
            var cs = _cfg.Callsign ?? "@nunu";
            var idx = text.IndexOf(cs, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0) text = text.Remove(idx, cs.Length).Trim();

            _onHeard(author, text);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "[Listener] OnChatMessage error");
        }
    }
}
