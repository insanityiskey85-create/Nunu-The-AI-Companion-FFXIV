using System;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;

namespace NunuTheAICompanion.Services;

/// <summary>
/// Listens to FFXIV chat via Dalamud IChatGui and forwards eligible messages
/// (callsign + whitelist + channel filter) to the provided callback.
/// Replies are NOT sent to game chat; PluginMain routes to UI.
/// </summary>
public sealed class ChatListener : IDisposable
{
    private readonly IChatGui _chatGui;
    private readonly Configuration _config;
    private readonly Action<string /*sender*/, string /*text*/> _onEligibleMessage;

    public ChatListener(IChatGui chatGui, Configuration config, Action<string, string> onEligibleMessage)
    {
        _chatGui = chatGui ?? throw new ArgumentNullException(nameof(chatGui));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _onEligibleMessage = onEligibleMessage ?? throw new ArgumentNullException(nameof(onEligibleMessage));

        // API 13: subscribe to ChatMessage (public on IChatGui)
        _chatGui.ChatMessage += OnChatMessage;
    }

    private void OnChatMessage(XivChatType type, int timestamp, ref SeString sender, ref SeString message, ref bool isHandled)
    {
        throw new NotImplementedException();
    }

    public void Dispose()
    {
        _chatGui.ChatMessage -= OnChatMessage;
    }

    // Matches IChatGui.ChatMessage delegate
    private void OnChatMessage(
        XivChatType type,
        uint senderId,
        ref SeString sender,
        ref SeString message,
        ref bool isHandled)
    {
        try
        {
            if (!_config.ListenEnabled)
                return;

            if (!IsChannelAllowed(type))
                return;

            var author = (sender.TextValue ?? string.Empty).Trim();
            if (author.Length == 0 || author.Equals("You", StringComparison.OrdinalIgnoreCase))
                return;

            if (!IsAuthorAllowed(author))
                return;

            var text = (message.TextValue ?? string.Empty).Trim();
            if (text.Length == 0)
                return;

            if (_config.RequireCallsign)
            {
                var cs = (_config.Callsign ?? string.Empty).Trim();
                if (cs.Length == 0)
                    return;

                var idx = text.IndexOf(cs, StringComparison.OrdinalIgnoreCase);
                if (idx < 0)
                    return;

                var cleaned = text.Remove(idx, cs.Length).Trim();
                if (cleaned.Length == 0)
                    return;

                _onEligibleMessage(author, cleaned);
                return;
            }

            _onEligibleMessage(author, text);
        }
        catch
        {
            // never break chat loop
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
        if (wl is null || wl.Count == 0)
            return true;

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
