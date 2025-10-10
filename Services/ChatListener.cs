using System;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;

namespace NunuTheAICompanion.Services;

/// <summary>
/// Listens to FFXIV chat via Dalamud IChatGui and forwards eligible messages
/// (callsign + whitelist + channel filter) to the provided callback.
/// Compatible with SDKs that pass SeString by ref or by value.
/// </summary>
public sealed class ChatListener : IDisposable
{
    private readonly IChatGui _chatGui;
    private readonly IPluginLog _log;
    private readonly Configuration _config;
    private readonly Action<string /*sender*/, string /*text*/> _onEligibleMessage;
    private readonly Action<string /*debug line*/>? _mirrorDebug;

    public ChatListener(
        IChatGui chatGui,
        IPluginLog log,
        Configuration config,
        Action<string, string> onEligibleMessage,
        Action<string>? mirrorDebugToWindow = null)
    {
        _chatGui = chatGui ?? throw new ArgumentNullException(nameof(chatGui));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _onEligibleMessage = onEligibleMessage ?? throw new ArgumentNullException(nameof(onEligibleMessage));
        _mirrorDebug = mirrorDebugToWindow;

        _chatGui.ChatMessage += OnChatMessage; // compiler binds to a matching overload below
        _log.Information("[ChatListener] Subscribed to ChatMessage.");
    }

    private void OnChatMessage(XivChatType type, int timestamp, ref SeString sender, ref SeString message, ref bool isHandled)
    {
        throw new NotImplementedException();
    }

    public void Dispose()
    {
        _chatGui.ChatMessage -= OnChatMessage;
        _log.Information("[ChatListener] Unsubscribed from ChatMessage.");
    }

    // ---- Overload A: SeString by ref (common) ----
    private void OnChatMessage(
        XivChatType type,
        uint senderId,
        ref SeString sender,
        ref SeString message,
        ref bool isHandled)
    {
        HandleMessage(type, senderId, sender.TextValue, message.TextValue, ref isHandled);
    }

    // ---- Overload B: SeString by value (some SDKs) ----
    private void OnChatMessage(
        XivChatType type,
        uint senderId,
        SeString sender,
        SeString message,
        ref bool isHandled)
    {
        HandleMessage(type, senderId, sender.TextValue, message.TextValue, ref isHandled);
    }

    private void HandleMessage(
        XivChatType type,
        uint senderId,
        string? senderText,
        string? messageText,
        ref bool isHandled)
    {
        try
        {
            var author = (senderText ?? string.Empty).Trim();
            var text = (messageText ?? string.Empty).Trim();

            if (_config.DebugListen)
            {
                var dbg = $"[heard] type={type} senderId={senderId} author='{author}' text='{text}'";
                _log.Information(dbg);
                if (_config.DebugMirrorToWindow && _mirrorDebug is not null)
                    _mirrorDebug(dbg);
            }

            if (!_config.ListenEnabled) return;
            if (!IsChannelAllowed(type)) return;

            if (author.Length == 0 || author.Equals("You", StringComparison.OrdinalIgnoreCase)) return;
            if (!IsAuthorAllowed(author)) return;
            if (text.Length == 0) return;

            if (_config.RequireCallsign)
            {
                var cs = (_config.Callsign ?? string.Empty).Trim();
                if (cs.Length == 0) return;

                var idx = text.IndexOf(cs, StringComparison.OrdinalIgnoreCase);
                if (idx < 0) return;

                var cleaned = text.Remove(idx, cs.Length).Trim();
                if (cleaned.Length == 0) return;

                _onEligibleMessage(author, cleaned);
                return;
            }

            _onEligibleMessage(author, text);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "ChatListener exception in HandleMessage.");
        }
    }

    private bool IsChannelAllowed(XivChatType type) => type switch
    {
        XivChatType.Say => _config.ListenSay,
        XivChatType.Shout => _config.ListenShout,
        XivChatType.Yell => _config.ListenYell,
        XivChatType.TellIncoming => _config.ListenTell,
        XivChatType.TellOutgoing => false, // ignore our own tells
        XivChatType.Party => _config.ListenParty,
        XivChatType.CrossParty => _config.ListenParty,
        XivChatType.Alliance => _config.ListenAlliance,
        XivChatType.FreeCompany => _config.ListenFreeCompany,
        _ => false
    };

    private bool IsAuthorAllowed(string author)
    {
        var wl = _config.Whitelist;
        if (wl is null || wl.Count == 0) return true;

        static string BaseName(string a)
        {
            var at = a.IndexOf('@');
            return at >= 0 ? a[..at] : a;
        }

        var baseAuthor = BaseName(author);

        foreach (var raw in wl)
        {
            var entry = (raw ?? string.Empty).Trim();
            if (entry.Length == 0) continue;

            if (entry.Equals(author, StringComparison.OrdinalIgnoreCase)) return true;
            if (BaseName(entry).Equals(baseAuthor, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }
}
