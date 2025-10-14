using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using NunuTheAICompanion.Interop;
using NunuTheAICompanion.Services;
using NunuTheAICompanion.Services.Songcraft;
using NunuTheAICompanion.UI;

namespace NunuTheAICompanion;

public sealed partial class PluginMain : IDalamudPlugin
{
    public string Name => "Nunu The AI Companion";

    // ===== Dalamud services =====
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;

    public static PluginMain Instance { get; private set; } = null!;

    // ===== Core state =====
    internal Configuration Config { get; private set; } = null!;
    internal WindowSystem WindowSystem { get; } = new("NunuTheAICompanion");

    internal ChatWindow ChatWindow { get; private set; } = null!;
    internal ConfigWindow ConfigWindow { get; private set; } = null!;
    internal MemoryWindow? MemoryWindow { get; private set; }
    internal ImageWindow? ImageWindow { get; private set; }

    internal MemoryService Memory { get; private set; } = null!;
    internal VoiceService? Voice { get; private set; }

    private readonly HttpClient _http = new();
    private readonly List<(string role, string content)> _history = new();

    private ChatBroadcaster? _broadcaster;
    private IpcChatRelay? _ipcRelay;
    private ChatListener? _listener;

    private bool _isStreaming;

    // Broadcast echo channel
    internal ChatBroadcaster.NunuChannel _echoChannel = ChatBroadcaster.NunuChannel.Party;

    // Commands
    private const string Command = "/nunu";

    // Soul Threads & Songcraft
    private EmbeddingClient? _embedding;
    private SoulThreadService? _threads;
    private SongcraftService? _songcraft;
    private IPluginLog? _songLog;

    // Bard-call moods
    private static readonly HashSet<string> _songMoods = new(StringComparer.OrdinalIgnoreCase)
    { "sorrow", "light", "playful", "mystic", "battle", "triumph" };

    // UI draw hooks
    private bool _uiHooksBound;

    public PluginMain()
    {
        Instance = this;

        // HTTP timeout: we cancel via CTS during streaming
        _http.Timeout = Timeout.InfiniteTimeSpan;

        // ---- Config
        Config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Config.Initialize(PluginInterface);

        // ---- Memory
        var storageRoot = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NunuTheAICompanion");
        try { System.IO.Directory.CreateDirectory(storageRoot); } catch { }
        Memory = new MemoryService(storageRoot, maxEntries: 1024, enabled: true);
        Memory.Load();

        // ---- Voice (optional)
        try { Voice = new VoiceService(Config, Log); }
        catch (Exception ex) { Log.Error(ex, "[Voice] init failed"); }

        // ---- Windows
        ChatWindow = new ChatWindow(Config);
        ChatWindow.OnSend += HandleUserSend;
        WindowSystem.AddWindow(ChatWindow);

        ConfigWindow = new ConfigWindow(Config);
        WindowSystem.AddWindow(ConfigWindow);

        try
        {
            MemoryWindow = new MemoryWindow(Config, Memory, Log);
            WindowSystem.AddWindow(MemoryWindow);
        }
        catch { /* optional window; ignore if ctor differs */ }

        // ---- UiBuilder hooks (REQUIRED for windows to render)
        if (!_uiHooksBound)
        {
            PluginInterface.UiBuilder.Draw += DrawUi;
            PluginInterface.UiBuilder.OpenConfigUi += OpenConfigUi;
            PluginInterface.UiBuilder.OpenMainUi += OpenMainUi;
            _uiHooksBound = true;
        }

        if (Config.StartOpen)
            ChatWindow.IsOpen = true;

        // ---- Broadcaster (chat output)
        _broadcaster = new ChatBroadcaster(CommandManager, Framework, Log)
        {
            Enabled = true
        };
        NativeChatSender.Initialize(this, PluginInterface, Log);
        _broadcaster.SetNativeSender(full => NativeChatSender.TrySendFull(full));

        // ---- IPC relay (optional)
        _ipcRelay = new IpcChatRelay(PluginInterface, Log);
        if (!string.IsNullOrWhiteSpace(Config.IpcChannelName))
        {
            var ok = _ipcRelay.Bind(Config.IpcChannelName!);
            ChatWindow.AppendSystem(ok
                ? $"[ipc] bound to '{Config.IpcChannelName}'"
                : $"[ipc] failed to bind '{Config.IpcChannelName}'");
        }
        _broadcaster.SetIpcRelay(_ipcRelay, Config.PreferIpcRelay);

        // ---- Commands
        CommandManager.AddHandler(Command, new CommandInfo(OnCommand)
        {
            HelpMessage = "Toggle Nunu. '/nunu config' opens settings. '/nunu memory' opens memories."
        });

        // ---- Listener (inbound chat)
        RehookListener();

        // ---- Soul Threads + Songcraft
        InitializeSoulThreads(_http, Log);
        InitializeSongcraft(Log);

        // ---- Greeting
        ChatWindow.AppendAssistant("Nunu is awake. Ask me anything—or play me a memory.");
    }

    public void Dispose()
    {
        try { _listener?.Dispose(); } catch { }
        try { _broadcaster?.Dispose(); } catch { }
        try { _ipcRelay?.Dispose(); } catch { }
        try { Voice?.Dispose(); } catch { }

        try
        {
            if (_uiHooksBound)
            {
                PluginInterface.UiBuilder.Draw -= DrawUi;
                PluginInterface.UiBuilder.OpenConfigUi -= OpenConfigUi;
                PluginInterface.UiBuilder.OpenMainUi -= OpenMainUi;
                _uiHooksBound = false;
            }
        }
        catch { }

        try { CommandManager.RemoveHandler(Command); } catch { }
        try { WindowSystem.RemoveAllWindows(); } catch { }

        try { Memory.Flush(); } catch { }
    }

    // ===== UiBuilder handlers =====
    private void DrawUi() => WindowSystem.Draw();
    private void OpenConfigUi() => ConfigWindow.IsOpen = true;
    private void OpenMainUi() => ChatWindow.IsOpen = true;

    // ===== Command handler =====
    private void OnCommand(string cmd, string args)
    {
        args = (args ?? string.Empty).Trim().ToLowerInvariant();
        if (args == "config") { ConfigWindow.IsOpen = true; return; }
        if (args == "memory") { if (MemoryWindow is { } mw) mw.IsOpen = true; return; }

        ChatWindow.IsOpen = !ChatWindow.IsOpen;
    }

    // ===== Listener boot / rehook =====
    /// <summary>Recreate the chat listener using the current configuration.</summary>
    public void RehookListener()
    {
        try { _listener?.Dispose(); } catch { /* ignore */ }
        _listener = new ChatListener(
            ChatGui, Log, Config,
            onHeard: OnHeardFromChat,
            mirrorDebugToWindow: s =>
            {
                if (Config.DebugMirrorToWindow)
                    ChatWindow.AppendSystem(s);
            });
        Log.Info("[Listener] rehooked with latest configuration.");
    }

    // ===== Inbound path =====
    private void OnHeardFromChat(string author, string text)
    {
        // Bard-Call early exit (/song …) before LLM
        if (TryHandleBardCall(author, text))
            return;

        HandleUserSend(text);
    }

    private void HandleUserSend(string text)
    {
        var author = string.IsNullOrWhiteSpace(Config.ChatDisplayName) ? "You" : Config.ChatDisplayName;

        // Persist the user line (threaded if available)
        try { OnUserUtterance(author, text, CancellationToken.None); } catch { /* fallback later */ }

        // Begin UI streaming
        ChatWindow.BeginAssistantStream();

        // Build context (thread + recents) then add the current turn
        var request = BuildContext(text, CancellationToken.None);
        request.Add(("user", text));

        _ = StreamAssistantAsync(request);
    }

    // ===== Streaming model loop =====
    private async Task StreamAssistantAsync(List<(string role, string content)> history)
    {
        if (_isStreaming) return;
        _isStreaming = true;

        var cts = new CancellationTokenSource();
        string reply = "";
        try
        {
            var chat = new List<(string role, string content)>(history);

            if (!string.IsNullOrWhiteSpace(Config.SystemPrompt))
                chat.Insert(0, ("system", Config.SystemPrompt));

            var client = new OllamaClient(_http, Config);
            await foreach (var chunk in client.StreamChatAsync(Config.BackendUrl, chat, cts.Token))
            {
                reply += chunk;
                ChatWindow.AppendAssistantDelta(chunk); // stream deltas to UI
            }

            ChatWindow.EndAssistantStream();

            if (!string.IsNullOrEmpty(reply))
            {
                _history.Add(("assistant", reply));

                // Persist assistant line (threaded if available)
                try { OnAssistantReply("Nunu", reply, CancellationToken.None); }
                catch { if (Memory.Enabled) Memory.Append("assistant", reply, topic: "chat"); }

                // TTS
                try { Voice?.Speak(reply); } catch { }

                // Broadcast to chat
                if (_broadcaster?.Enabled == true)
                    _broadcaster.Enqueue(_echoChannel, reply);

                // Songcraft composition (fire-and-forget)
                try
                {
                    var path = TriggerSongcraft(reply, mood: null);
                    if (!string.IsNullOrEmpty(path))
                        ChatWindow.AppendAssistant($"[song] Saved: {path}");
                }
                catch { /* non-fatal */ }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[model] stream failed");
            ChatWindow.AppendSystem("[error] model stream failed; see logs.");
            ChatWindow.EndAssistantStream();
        }
        finally
        {
            _isStreaming = false;
            try { cts.Dispose(); } catch { }
        }
    }

    // ===== Soul Threads =====
    private void InitializeSoulThreads(HttpClient http, IPluginLog log)
    {
        try
        {
            _embedding = new EmbeddingClient(http, Config);
            _threads = new SoulThreadService(Memory, _embedding, Config, log);
            log.Info("[SoulThreads] initialized.");
        }
        catch (Exception ex)
        {
            log.Warning(ex, "[SoulThreads] failed; continuing without.");
        }
    }

    private List<(string role, string content)> BuildContext(string userText, CancellationToken token)
    {
        if (_threads is null)
            return Memory.GetRecentForContext(Config.ContextTurns);

        return _threads.GetContextFor(
            userText,
            Config.ThreadContextMaxFromThread,
            Config.ThreadContextMaxRecent,
            token);
    }

    private void OnUserUtterance(string author, string text, CancellationToken token)
    {
        if (_threads is null) { Memory.Append("user", text, topic: author); return; }
        _threads.AppendAndThread("user", text, author, token);
    }

    private void OnAssistantReply(string author, string reply, CancellationToken token)
    {
        if (_threads is null) { Memory.Append("assistant", reply, topic: author); return; }
        _threads.AppendAndThread("assistant", reply, author, token);
    }

    // ===== Songcraft =====
    private void InitializeSongcraft(IPluginLog log)
    {
        _songLog = log;
        try
        {
            _songcraft = new SongcraftService(Config, log);
            log.Info("[Songcraft] initialized.");
        }
        catch (Exception ex)
        {
            log.Warning(ex, "[Songcraft] init failed; continuing without.");
        }
    }

    public string? TriggerSongcraft(string text, string? mood = null)
    {
        if (!Config.SongcraftEnabled || _songcraft is null) return null;
        var dir = string.IsNullOrWhiteSpace(Config.SongcraftSaveDir)
            ? Memory.StorageDirectory
            : Config.SongcraftSaveDir!;
        try
        {
            return _songcraft.ComposeToFile(text, mood, dir, "nunu_song");
        }
        catch (Exception ex)
        {
            _songLog?.Error(ex, "[Songcraft] compose failed");
            return null;
        }
    }

    // ===== Bard-Call (/song …) =====
    public bool TryHandleBardCall(string author, string text)
    {
        if (!Config.SongcraftEnabled || _songcraft is null) return false;

        var trig = string.IsNullOrWhiteSpace(Config.SongcraftBardCallTrigger)
            ? "/song"
            : Config.SongcraftBardCallTrigger!;

        var idx = text.IndexOf(trig, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return false;

        string after = text[(idx + trig.Length)..].Trim();
        string? mood = null;
        string lyric = text;

        if (!string.IsNullOrEmpty(after))
        {
            var parts = after.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 1 && _songMoods.Contains(parts[0]))
            {
                mood = parts[0];
                lyric = parts.Length > 1 ? parts[1] : "";
            }
            else
            {
                lyric = after;
            }
        }

        string payload = string.IsNullOrWhiteSpace(lyric) ? "(no text)" : lyric;

        try
        {
            var path = TriggerSongcraft(payload, mood);
            if (!string.IsNullOrEmpty(path))
            {
                ChatWindow.AddSystemLine($"[Songcraft] Saved: {path}");
                if (_broadcaster?.Enabled == true)
                    _broadcaster.Enqueue(_echoChannel, $"[Songcraft] Saved: {path}");
            }
            else
            {
                ChatWindow.AddSystemLine("[Songcraft] Failed to compose.");
            }
        }
        catch (Exception ex)
        {
            ChatWindow.AddSystemLine("[Songcraft] Error while composing (see logs).");
            try { _songLog?.Error(ex, "[Songcraft] bard-call failed"); } catch { }
        }

        // Persist the command as a memory "moment"
        try { OnUserUtterance(author, $"{trig} {mood ?? ""} {payload}".Trim(), CancellationToken.None); } catch { }
        return true;
    }
}
