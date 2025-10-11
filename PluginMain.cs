using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Net.Http;
using System.Text;
using System.Threading;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using NunuTheAICompanion.Interop;
using NunuTheAICompanion.Services;
using NunuTheAICompanion.UI;

namespace NunuTheAICompanion;

public sealed class PluginMain : IDalamudPlugin
{
    public string Name => "Nunu The AI Companion";

    // ---------- Dalamud services ----------
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;

    // ---------- Singleton ----------
    public static PluginMain Instance { get; private set; } = null!;

    // ---------- Config + UI ----------
    internal Configuration Config { get; private set; } = null!;
    internal WindowSystem WindowSystem { get; } = new("NunuTheAICompanion");
    internal ChatWindow ChatWindow { get; private set; } = null!;
    internal ConfigWindow ConfigWindow { get; private set; } = null!;
    internal MemoryWindow? MemoryWindow { get; private set; }
    internal ImageWindow? ImageWindow { get; private set; }
    internal MemoryService? Memory { get; private set; }

    // ---------- Runtime ----------
    private readonly List<(string role, string content)> _history = new();
    private readonly HttpClient _http = new();
    private OllamaClient _client = null!;
    private WebSearchClient _search = null!;
    private ChatBroadcaster? _broadcaster;
    private IpcChatRelay? _ipcRelay;
    private ChatListener? _listener;

    private CancellationTokenSource? _cts;
    private bool _isStreaming;

    private ChatBroadcaster.NunuChannel _echoChannel = ChatBroadcaster.NunuChannel.Party;

    // ---------- Commands ----------
    private const string Command = "/nunu";
    private const string DiagCommand = "/nunudiag";
    private const string DebugCommand = "/nunudebug";
    private const string ImgCommand = "/nunuimg";
    private const string SearchCommand = "/nunusearch";
    private const string SpeakCommand = "/nunuspeak";
    private const string ChanCommand = "/nunuchan";
    private const string SendCommand = "/nunusend";
    private const string RawCommand = "/nunucmd";
    private const string IpcBindCommand = "/nunuipcbind";
    private const string IpcPrefCommand = "/nunuipcmode";
    private const string RehookCommand = "/nunurehook";

    public PluginMain()
    {
        Instance = this;

        // Infinite HTTP timeout; we control lifetime via CancellationTokenSource.
        _http.Timeout = System.Threading.Timeout.InfiniteTimeSpan;

        // Config
        Config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Config.Initialize(PluginInterface);

        // Optional native chat via reflection (MidiBard)
        NativeChatSender.Initialize(this, PluginInterface, Log);

        // Services
        _client = new OllamaClient(_http, Config);
        _search = new WebSearchClient(_http, Config);

        // Memory
        var cfgDir = PluginInterface.GetPluginConfigDirectory();
        Memory = new MemoryService(cfgDir, Config.MemoryMaxEntries, Config.MemoryEnabled);

        // Windows
        ChatWindow = new ChatWindow(Config) { Size = new Vector2(740, 480) };
        ChatWindow.OnSend += HandleUserSend;
        WindowSystem.AddWindow(ChatWindow);

        ConfigWindow = new ConfigWindow(Config);
        WindowSystem.AddWindow(ConfigWindow);

        try
        {
            MemoryWindow = new MemoryWindow(Config, Memory, Log);
            if (MemoryWindow is not null) WindowSystem.AddWindow(MemoryWindow);
        }
        catch { /* optional window */ }

        try
        {
            var imageSaveBase = System.IO.Path.Combine(cfgDir, "Images");
            var imageClient = new ImageClient(_http, Config, imageSaveBase);
            ImageWindow = new ImageWindow(Config, imageClient, Log);
            if (ImageWindow is not null) WindowSystem.AddWindow(ImageWindow);
        }
        catch { /* optional window */ }

        // Greeting
        const string hello = "WAH! Little Nunu listens by the campfire. Call me with @nunu; I answer here, not in game chat.";
        ChatWindow.AppendAssistant(hello);
        _history.Add(("assistant", hello));
        Memory?.Append("assistant", hello, topic: "greeting");

        // Listener
        _listener = new ChatListener(
            ChatGui,
            Log,
            Config,
            OnEligibleChatHeard,
            mirrorDebugToWindow: s => { if (Config.DebugMirrorToWindow) ChatWindow.AppendAssistant(s); });

        // Broadcaster
        _broadcaster = new ChatBroadcaster(CommandManager, Framework, Log)
        {
            Enabled = false,
            MaxPerMinute = 6,
            DelayBetweenLinesMs = 1500
        };
        _broadcaster.SetNativeSender(fullLine => NativeChatSender.TrySendFull(fullLine));

        // IPC relay (optional)
        _ipcRelay = new IpcChatRelay(PluginInterface, Log);
        if (!string.IsNullOrWhiteSpace(Config.IpcChannelName))
        {
            var ok = _ipcRelay.Bind(Config.IpcChannelName);
            ChatWindow.AppendAssistant(ok
                ? $"[ipc] bound to '{Config.IpcChannelName}'."
                : $"[ipc] failed to bind '{Config.IpcChannelName}'.");
        }
        _broadcaster.SetIpcRelay(_ipcRelay, Config.PreferIpcRelay);

        // Commands
        CommandManager.AddHandler(Command, new CommandInfo(OnCommand) { HelpMessage = "Toggle Nunu. '/nunu config' settings. '/nunu memory' memories." });
        CommandManager.AddHandler(DiagCommand, new CommandInfo(OnDiag) { HelpMessage = "Diagnostics for Nunu chat listening." });
        CommandManager.AddHandler(DebugCommand, new CommandInfo(OnDebugCommand) { HelpMessage = "Toggle debug listening: /nunudebug on|off|mirror [on|off]" });
        CommandManager.AddHandler(ImgCommand, new CommandInfo(OnImgCommand) { HelpMessage = "Open Nunu's image atelier." });
        CommandManager.AddHandler(SearchCommand, new CommandInfo(OnSearchCommand) { HelpMessage = "Search the web: /nunusearch <query>" });
        CommandManager.AddHandler(SpeakCommand, new CommandInfo(OnSpeakCommand) { HelpMessage = "Broadcast replies to game chat: /nunuspeak on|off" });
        CommandManager.AddHandler(ChanCommand, new CommandInfo(OnChanCommand) { HelpMessage = "Set broadcast channel: /nunuchan say|party|fc|shout|yell" });
        CommandManager.AddHandler(SendCommand, new CommandInfo(OnSendCommand) { HelpMessage = "Force-send: /nunusend <say|party|fc|shout|yell|echo> <text>" });
        CommandManager.AddHandler(RawCommand, new CommandInfo(OnRawCommand) { HelpMessage = "Send a raw game command: /nunucmd <text>" });
        CommandManager.AddHandler(IpcBindCommand, new CommandInfo(OnIpcBind) { HelpMessage = "Bind an IPC sender: /nunuipcbind <channelName>" });
        CommandManager.AddHandler(IpcPrefCommand, new CommandInfo(OnIpcPref) { HelpMessage = "Prefer IPC over ProcessCommand: /nunuipcmode on|off" });
        CommandManager.AddHandler(RehookCommand, new CommandInfo((_, __) => RehookListener()) { HelpMessage = "Rehook chat listener" });

        // UI hooks
        PluginInterface.UiBuilder.Draw += DrawUI;
        PluginInterface.UiBuilder.OpenConfigUi += () => ConfigWindow.IsOpen = true;
        PluginInterface.UiBuilder.OpenMainUi += () => ChatWindow.IsOpen = true;

        Log.Information("[Nunu] Plugin initialized. NativeChat available={Avail}", NativeChatSender.IsAvailable);
    }

    // ---------- Public helpers ----------
    public void RehookListener()
    {
        try { _listener?.Dispose(); } catch { }
        _listener = new ChatListener(
            ChatGui, Log, Config,
            OnEligibleChatHeard,
            mirrorDebugToWindow: s => { if (Config.DebugMirrorToWindow) ChatWindow.AppendAssistant(s); });
        Log.Information("[Nunu] Listener rehooked.");
    }

    // ---------- Command handlers ----------
    private void OnImgCommand(string command, string args)
    {
        if (ImageWindow is null)
        {
            ChatGui.PrintError("Image window not available in this build.");
            return;
        }
        ImageWindow.IsOpen = !ImageWindow.IsOpen;
    }

    private async void OnSearchCommand(string cmd, string args)
    {
        var q = (args ?? "").Trim();
        if (string.IsNullOrWhiteSpace(q))
        {
            ChatWindow.AppendAssistant("Usage: /nunusearch <query>");
            return;
        }

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Clamp(Config.SearchTimeoutSec, 5, 60)));
            var hits = await _search.SearchAsync(q, cts.Token);
            if (hits.Count == 0)
            {
                ChatWindow.AppendAssistant($"No results for: {q}");
                return;
            }

            var text = WebSearchClient.FormatForContext(hits);
            ChatWindow.AppendAssistant($"Web echoes for “{q}”:\n{text}");

            if (Memory?.Enabled == true) Memory.Append("tool:web.search", text, topic: $"search:{q}");
            _history.Add(("tool:web.search", text));
        }
        catch (Exception ex)
        {
            ChatWindow.AppendAssistant($"[search error] {ex.Message}");
            Log.Error(ex, "Search failed.");
        }
    }

    private void OnSpeakCommand(string cmd, string args)
    {
        if (_broadcaster is null) { ChatWindow.AppendAssistant("Broadcaster not available."); return; }
        var a = (args ?? "").Trim().ToLowerInvariant();
        if (a is "on" or "1" or "true")
        {
            _broadcaster.Enabled = true;
            ChatWindow.AppendAssistant("Broadcast to game chat: ON");
        }
        else if (a is "off" or "0" or "false")
        {
            _broadcaster.Enabled = false;
            ChatWindow.AppendAssistant("Broadcast to game chat: OFF");
        }
        else
        {
            ChatWindow.AppendAssistant("Usage: /nunuspeak on|off");
        }
    }

    private void OnChanCommand(string cmd, string args)
    {
        var a = (args ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(a))
        {
            ChatWindow.AppendAssistant($"Current channel: {_echoChannel}. Usage: /nunuchan say|party|fc|shout|yell");
            return;
        }

        switch (a)
        {
            case "say": _echoChannel = ChatBroadcaster.NunuChannel.Say; break;
            case "party":
            case "p": _echoChannel = ChatBroadcaster.NunuChannel.Party; break;
            case "fc": _echoChannel = ChatBroadcaster.NunuChannel.FreeCompany; break;
            case "shout":
            case "sh": _echoChannel = ChatBroadcaster.NunuChannel.Shout; break;
            case "yell":
            case "y": _echoChannel = ChatBroadcaster.NunuChannel.Yell; break;
            case "echo": _echoChannel = ChatBroadcaster.NunuChannel.Echo; break;
            default:
                ChatWindow.AppendAssistant("Unknown channel. Use: say | party | fc | shout | yell | echo");
                return;
        }

        ChatWindow.AppendAssistant($"Broadcast channel set to: {_echoChannel}");
    }

    private void OnSendCommand(string cmd, string args)
    {
        if (_broadcaster is null) { ChatWindow.AppendAssistant("Broadcaster not available."); return; }
        var a = (args ?? "").Trim();
        var sp = a.IndexOf(' ');
        if (sp <= 0) { ChatWindow.AppendAssistant("Usage: /nunusend <say|party|fc|shout|yell|echo> <text>"); return; }

        var chan = a[..sp].ToLowerInvariant();
        var text = a[(sp + 1)..].Trim();
        var c = chan switch
        {
            "say" => ChatBroadcaster.NunuChannel.Say,
            "party" or "p" => ChatBroadcaster.NunuChannel.Party,
            "fc" => ChatBroadcaster.NunuChannel.FreeCompany,
            "shout" or "sh" => ChatBroadcaster.NunuChannel.Shout,
            "yell" or "y" => ChatBroadcaster.NunuChannel.Yell,
            "echo" => ChatBroadcaster.NunuChannel.Echo,
            _ => ChatBroadcaster.NunuChannel.Say
        };

        if (!_broadcaster.Enabled) ChatWindow.AppendAssistant("[note] Broadcaster is OFF. Use /nunuspeak on");
        _broadcaster.Enqueue(c, text);
    }

    private void OnRawCommand(string cmd, string args)
    {
        var text = (args ?? "").Trim();
        if (string.IsNullOrEmpty(text))
        {
            ChatWindow.AppendAssistant("Usage: /nunucmd <game command>, e.g. /nunucmd /say raw test");
            return;
        }
        try
        {
            Framework.RunOnFrameworkThread(() =>
            {
                try { CommandManager.ProcessCommand(text); }
                catch (Exception ex) { Log.Error(ex, "[Raw] ProcessCommand failed for: {Text}", text); }
            });
            ChatWindow.AppendAssistant($"[raw] sent: {text}");
        }
        catch (Exception ex)
        {
            ChatWindow.AppendAssistant($"[raw] error: {ex.Message}");
            Log.Error(ex, "[Raw] scheduling failed");
        }
    }

    private void OnIpcBind(string cmd, string args)
    {
        var name = (args ?? "").Trim();
        if (_ipcRelay is null || string.IsNullOrEmpty(name))
        {
            ChatWindow.AppendAssistant("Usage: /nunuipcbind <channelName>");
            return;
        }
        var ok = _ipcRelay.Bind(name);
        Config.IpcChannelName = name; Config.Save();
        _broadcaster?.SetIpcRelay(_ipcRelay, Config.PreferIpcRelay);
        ChatWindow.AppendAssistant(ok ? $"[ipc] bound to '{name}'" : $"[ipc] failed to bind '{name}'");
    }

    private void OnIpcPref(string cmd, string args)
    {
        var a = (args ?? "").Trim().ToLowerInvariant();
        bool on = a is "on" or "1" or "true";
        bool off = a is "off" or "0" or "false";
        if (!on && !off)
        {
            ChatWindow.AppendAssistant($"[ipc] PreferIpcRelay={(Config.PreferIpcRelay ? "ON" : "OFF")}. Usage: /nunuipcmode on|off");
            return;
        }
        Config.PreferIpcRelay = on; Config.Save();
        _broadcaster?.SetIpcRelay(_ipcRelay, Config.PreferIpcRelay);
        ChatWindow.AppendAssistant($"[ipc] PreferIpcRelay {(on ? "ON" : "OFF")}");
    }

    private void OnCommand(string command, string args)
    {
        var trimmed = args?.Trim() ?? string.Empty;
        if (trimmed.Equals("config", StringComparison.OrdinalIgnoreCase)) { ConfigWindow.IsOpen = true; return; }
        if (trimmed.Equals("memory", StringComparison.OrdinalIgnoreCase)) { if (MemoryWindow is not null) MemoryWindow.IsOpen = true; return; }
        if (trimmed.Equals("image", StringComparison.OrdinalIgnoreCase)) { if (ImageWindow is not null) ImageWindow.IsOpen = true; return; }
        ChatWindow.IsOpen = !ChatWindow.IsOpen;
    }

    private void OnDiag(string command, string args)
    {
        var s = $"[diag] ListenEnabled={Config.ListenEnabled}, RequireCallsign={Config.RequireCallsign}, ListenSelf={Config.ListenSelf}, Callsign='{Config.Callsign}', " +
                $"Say={Config.ListenSay}, Tell={Config.ListenTell}, Party={Config.ListenParty}, Alliance={Config.ListenAlliance}, FC={Config.ListenFreeCompany}, " +
                $"Shout={Config.ListenShout}, Yell={Config.ListenYell}, WhitelistCount={(Config.Whitelist?.Count ?? 0)}, DebugListen={Config.DebugListen}, Mirror={Config.DebugMirrorToWindow}, " +
                $"AllowInternet={Config.AllowInternet}, SearchBackend={Config.SearchBackend}, MaxRes={Config.SearchMaxResults}, " +
                $"SpeakEnabled={_broadcaster?.Enabled}, EchoChannel={_echoChannel}, IpcChannel='{Config.IpcChannelName}', PreferIpc={Config.PreferIpcRelay}, NativeAvail={NativeChatSender.IsAvailable}";
        Log.Information(s);
        ChatWindow.AppendAssistant(s);
        ChatGui.Print("Nunu diag printed in window.");
    }

    private void OnDebugCommand(string cmd, string args)
    {
        var a = (args ?? "").Trim().ToLowerInvariant();
        if (a is "on" or "1" or "true")
        {
            Config.DebugListen = true; Config.Save();
            ChatWindow.AppendAssistant("[diag] DebugListen ON"); Log.Information("DebugListen ON");
        }
        else if (a is "off" or "0" or "false")
        {
            Config.DebugListen = false; Config.Save();
            ChatWindow.AppendAssistant("[diag] DebugListen OFF"); Log.Information("DebugListen OFF");
        }
        else if (a.StartsWith("mirror"))
        {
            var parts = a.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            bool set = parts.Length < 2 || parts[1] is "on" or "1" or "true";
            Config.DebugMirrorToWindow = set; Config.Save();
            ChatWindow.AppendAssistant($"[diag] DebugMirrorToWindow {(set ? "ON" : "OFF")}");
            Log.Information("DebugMirrorToWindow {State}", set ? "ON" : "OFF");
        }
        else
        {
            ChatWindow.AppendAssistant("[diag] Usage: /nunudebug on | off | mirror [on|off]");
        }
    }

    // ---------- Chat pipeline ----------
    private void HandleUserSend(string text)
    {
        _history.Add(("user", text));
        if (Memory?.Enabled == true) Memory.Append("user", text, topic: "chat");

        ChatWindow.BeginAssistantStream();
        var request = _history.ToList();
        StreamAssistantAsync(request);
    }

    private async void StreamAssistantAsync(List<(string role, string content)> request)
    {
        if (_isStreaming) _cts?.Cancel();
        _isStreaming = true;

        // Infinite stream; cancel manually when needed.
        _cts = new CancellationTokenSource();

        var full = new StringBuilder();

        try
        {
            await foreach (var delta in _client.StreamChatAsync(Config.BackendUrl, request, _cts.Token))
            {
                if (string.IsNullOrEmpty(delta)) continue;

                var chunk = delta;

                // simple inline tool-call: <<search: query>>
                int markerStart = chunk.IndexOf("<<search:", StringComparison.OrdinalIgnoreCase);
                if (markerStart >= 0)
                {
                    int markerEnd = chunk.IndexOf(">>", markerStart + 9);
                    if (markerEnd > markerStart)
                    {
                        var q = chunk.Substring(markerStart + 9, markerEnd - (markerStart + 9)).Trim();
                        ChatWindow.AppendAssistantDelta("\n[searching…]\n");
                        try
                        {
                            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Clamp(Config.SearchTimeoutSec, 5, 60)));
                            var hits = await _search.SearchAsync(q, cts.Token);
                            var brief = WebSearchClient.FormatForContext(hits);
                            _history.Add(("tool:web.search", brief));
                            ChatWindow.AppendAssistantDelta($"\n[search results]\n{brief}\n");
                        }
                        catch (Exception ex)
                        {
                            ChatWindow.AppendAssistantDelta($"\n[search error] {ex.Message}\n");
                        }
                        chunk = chunk.Remove(markerStart, (markerEnd + 2) - markerStart);
                    }
                }

                full.Append(chunk);
                ChatWindow.AppendAssistantDelta(chunk);
            }
        }
        catch (Exception ex)
        {
            ChatWindow.AppendAssistantDelta($" [error: {ex.Message}]");
            Log.Error(ex, "Streaming failed.");
        }
        finally
        {
            _isStreaming = false;
            ChatWindow.EndAssistantStream();

            var reply = full.ToString().Trim();
            if (!string.IsNullOrEmpty(reply))
            {
                _history.Add(("assistant", reply));
                if (Memory?.Enabled == true)
                    Memory.Append("assistant", reply, topic: "chat");

                if (_broadcaster?.Enabled == true)
                {
                    _broadcaster.Enqueue(_echoChannel, reply);
                }
            }
        }
    }

    private void OnEligibleChatHeard(string author, string text)
    {
        ChatWindow.AppendAssistant($"“{text}” you say, {author}? Very well…");
        HandleUserSend(text);
    }

    // ---------- UI ----------
    private void DrawUI() => WindowSystem.Draw();

    public void Dispose()
    {
        _cts?.Cancel();
        _listener?.Dispose();
        _broadcaster?.Dispose();
        _ipcRelay?.Dispose();

        WindowSystem.RemoveAllWindows();

        CommandManager.RemoveHandler(Command);
        CommandManager.RemoveHandler(DiagCommand);
        CommandManager.RemoveHandler(DebugCommand);
        CommandManager.RemoveHandler(ImgCommand);
        CommandManager.RemoveHandler(SearchCommand);
        CommandManager.RemoveHandler(SpeakCommand);
        CommandManager.RemoveHandler(ChanCommand);
        CommandManager.RemoveHandler(SendCommand);
        CommandManager.RemoveHandler(RawCommand);
        CommandManager.RemoveHandler(IpcBindCommand);
        CommandManager.RemoveHandler(IpcPrefCommand);
        CommandManager.RemoveHandler(RehookCommand);

        _http.Dispose();
        Log.Information("[Nunu] Plugin disposed.");
    }
}
