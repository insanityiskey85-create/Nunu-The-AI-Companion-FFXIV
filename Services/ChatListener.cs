using Dalamud.Plugin.Services;
using Nunu_The_AI_Companion;
using System;

namespace NunuTheAICompanion.Services;

/// <summary>
/// Minimal listener scaffold: compiles and provides hooks; you can wire IChatGui events later.
/// </summary>
public sealed class ChatListener : IDisposable
{
    private readonly IChatGui _chat;
    private readonly IPluginLog _log;
    private readonly Configuration _cfg;
    private readonly Action<string, string> _onEligible;
    private readonly Action<string>? _mirror;

    public ChatListener(IChatGui chat, IPluginLog log, Configuration cfg, Action<string, string> onEligibleChatHeard, Action<string>? mirrorDebugToWindow = null)
    {
        _chat = chat; _log = log; _cfg = cfg;
        _onEligible = onEligibleChatHeard;
        _mirror = mirrorDebugToWindow;

        // Hooking actual OnMessage event differs across Dalamud versions; to avoid signature mismatch,
        // keep this as a no-op that can be extended later.
        _log.Information("[ChatListener] initialized (no-op).");
    }

    public void Dispose()
    {
        // Unhook if you later hook events
    }

    // You can call this from dev commands to simulate a heard message:
    public void SimulateHeard(string author, string text)
    {
        if (!_cfg.ListenEnabled) return;
        _mirror?.Invoke($"[heard] {author}: {text}");
        _onEligible(author, text);
    }
}
