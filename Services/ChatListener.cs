using System;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;

namespace NunuTheAICompanion.Services;

/// <summary>
/// Listens to game chat and forwards eligible messages to the AI pipeline (API 13).
/// </summary>
public sealed class ChatListener : IDisposable
{
    private readonly IChatGui _chatGui;
    private readonly IPluginLog _log;
    private readonly Configuration _config;
    private readonly Action<string /*author*/, string /*text*/> _onEligibleMessage;
    private readonly Action<string>? _mirrorDebug;

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

        // API 13 signature: (XivChatType, int timestamp, ref SeString sender, ref SeString message, ref bool isHandled)
        _chatGui.ChatMessage += OnChatMessage;
        _log.Information("[ChatListener] Subscribed to ChatMessage (API13).");
    }

    public void Dispose()
    {
        _chatGui.ChatMessage -= OnChatMessage;
        _log.Information("[ChatListener] Unsubscribed from ChatMessage.");
    }

    // ---- API 13 exact signature ----
    private void OnChatMessage(XivChatType type, int timestamp, ref SeString sender, ref SeString message, ref bool isHandled)
    {
        try
        {
            var author = (sender.TextValue ?? string.Empty).Trim();
            var text = (message.TextValue ?? string.Empty).Trim();

            if (_config.DebugListen)
            {
                var dbg = $"[heard] ts={timestamp} type={type} author='{author}' text='{text}'";
                _log.Information(dbg);
                if (_config.DebugMirrorToWindow) _mirrorDebug?.Invoke(dbg);
            }

            if (!_config.ListenEnabled) return;
            if (!IsChannelAllowed(type)) return;

            if (author.Length == 0 || text.Length == 0) return;

            // Accept "You" iff ListenSelf is enabled
            var isSelf = author.Equals("You", StringComparison.OrdinalIgnoreCase);
            if (isSelf && !_config.ListenSelf) return;

            // If not self, enforce whitelist if present
            if (!isSelf && !IsAuthorAllowed(author)) return;

            // Callsign filter
            if (_config.RequireCallsign)
            {
                var cs = (_config.Callsign ?? string.Empty).Trim();
                if (cs.Length == 0) return;

                var idx = text.IndexOf(cs, StringComparison.OrdinalIgnoreCase);
                if (idx < 0) return;

                text = text.Remove(idx, cs.Length).Trim();
                if (text.Length == 0) return;
            }

            _onEligibleMessage(author, text);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "ChatListener exception in OnChatMessage.");
        }
    }

    private bool IsChannelAllowed(XivChatType type) => type switch
    {
        XivChatType.Say => _config.ListenSay,
        XivChatType.Shout => _config.ListenShout,
        XivChatType.Yell => _config.ListenYell,
        XivChatType.TellIncoming => _config.ListenTell,
        XivChatType.TellOutgoing => _config.ListenTell, // optional: your own /tell
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
