using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Numerics;
using System.Reflection;
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

namespace NunuTheAICompanion
{
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

        private ChatBroadcaster? _broadcaster;
        private IpcChatRelay? _ipcRelay;
        private ChatListener? _listener;

        // Broadcast echo channel (default overridden by Config on load)
        internal ChatBroadcaster.NunuChannel _echoChannel = ChatBroadcaster.NunuChannel.Party;

        // Commands
        private const string Command = "/nunu";

        // Soul Threads & Songcraft
        private EmbeddingClient? _embedding;
        private SoulThreadService? _threads;
        private SongcraftService? _songcraft;
        private IPluginLog? _songLog;

        // UI draw hooks
        private bool _uiHooksBound;
        private bool _isStreaming;

        public PluginMain()
        {
            Instance = this;

            // No client timeout; we cancel via CTS while streaming.
            _http.Timeout = Timeout.InfiniteTimeSpan;

            // ---- Config
            Config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
            Config.Initialize(PluginInterface);

            // Echo channel from config
            _echoChannel = ParseChannel(Config.EchoChannel);

            // ---- Memory
            var storageRoot = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "NunuTheAICompanion");
            try { System.IO.Directory.CreateDirectory(storageRoot); } catch { }
            Memory = new MemoryService(storageRoot, maxEntries: 1024, enabled: true);
            Memory.Load();

            // ---- Voice (optional)
            try { Voice = new VoiceService(Config, Log); } catch (Exception ex) { Log.Error(ex, "[Voice] init failed"); }

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
            catch { /* optional window differences are fine */ }

            // ---- UiBuilder hooks
            if (!_uiHooksBound)
            {
                PluginInterface.UiBuilder.Draw += DrawUi;
                PluginInterface.UiBuilder.OpenConfigUi += OpenConfigUi;
                PluginInterface.UiBuilder.OpenMainUi += OpenMainUi;
                _uiHooksBound = true;
            }

            if (Config.StartOpen)
                ChatWindow.IsOpen = true;

            // ---- Broadcaster (native + IPC)
            _broadcaster = new ChatBroadcaster(CommandManager, Framework, Log) { Enabled = true };
            NativeChatSender.Initialize(this, PluginInterface, Log);
            _broadcaster.SetNativeSender(full => NativeChatSender.TrySendFull(full));

            // ---- IPC relay (create & bind if configured)
            _ipcRelay = new IpcChatRelay(PluginInterface, Log);
            if (!string.IsNullOrWhiteSpace(Config.IpcChannelName))
            {
                var ok = _ipcRelay.Bind(Config.IpcChannelName!);
                ChatWindow.AppendSystem(ok
                    ? $"[ipc] bound to '{Config.IpcChannelName}'"
                    : $"[ipc] failed to bind '{Config.IpcChannelName}'");
            }
            _broadcaster.SetIpcRelay(_ipcRelay, Config.PreferIpcRelay);

            // ---- Slash commands
            CommandManager.AddHandler(Command, new CommandInfo(OnCommand)
            {
                HelpMessage = "Toggle Nunu. '/nunu help' for usage."
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
        private void OnCommand(string cmd, string args) => HandleSlashCommand(args ?? string.Empty);

        // ===== Listener boot / rehook =====
        public void RehookListener()
        {
            try { _listener?.Dispose(); } catch { }
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
            // Bard-Call (/song …) short-circuit before LLM
            if (TryHandleBardCall(author, text))
                return;

            HandleUserSend(text);
        }

        private void HandleUserSend(string text)
        {
            // Persist user line (threaded if available)
            try
            {
                var who = string.IsNullOrWhiteSpace(Config.ChatDisplayName) ? "You" : Config.ChatDisplayName;
                OnUserUtterance(who, text, CancellationToken.None);
            }
            catch { /* best-effort */ }

            // Begin UI streaming
            ChatWindow.BeginAssistantStream();

            // Build context (thread + recents) then add the current turn
            var request = BuildContext(text, CancellationToken.None);
            request.Add(("user", text));

            _ = StreamAssistantAsync(request);
        }

        // --- typing indicator (added) ---
        private void BroadcastTypingIndicator()
        {
            const string indicator = "Little Nunu is writing…";
            try
            {
                // Show in the plugin chat window
                ChatWindow.AddSystemLine(indicator);

                // Echo to the configured in-game channel
                if (_broadcaster?.Enabled == true)
                    _broadcaster.Enqueue(_echoChannel, indicator);
            }
            catch
            {
                // best-effort; never crash on chat send
            }
        }

        // ===== Streaming model loop =====
        private async Task StreamAssistantAsync(List<(string role, string content)> history)
        {
            if (_isStreaming) return;
            _isStreaming = true;

            // NEW: announce typing before we begin streaming
            BroadcastTypingIndicator();

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
                    if (string.IsNullOrEmpty(chunk)) continue;
                    reply += chunk;
                    ChatWindow.AppendAssistantDelta(chunk); // stream deltas to UI
                }

                ChatWindow.EndAssistantStream();

                if (!string.IsNullOrEmpty(reply))
                {
                    // Persist assistant line (threaded if available)
                    try { OnAssistantReply("Nunu", reply, CancellationToken.None); }
                    catch { if (Memory.Enabled) Memory.Append("assistant", reply, topic: "chat"); }

                    // TTS
                    try { Voice?.Speak(reply); } catch { }

                    // Broadcast to chat
                    if (_broadcaster?.Enabled == true)
                        _broadcaster.Enqueue(_echoChannel, reply);

                    // Songcraft composition (optional, fire-and-forget)
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

        // ===== Bard-Call (/song … in chat text) =====
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
                if (parts.Length >= 1 && parts[0].Length <= 12) // simple mood capture
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

        // ========================= Slash Command Router =========================

        private static readonly StringComparer Ci = StringComparer.OrdinalIgnoreCase;

        // alias -> Configuration property name
        private static readonly Dictionary<string, string> _aliases = new(StringComparer.OrdinalIgnoreCase)
        {
            // Backend
            ["mode"] = nameof(Configuration.BackendMode),
            ["endpoint"] = nameof(Configuration.BackendUrl),
            ["url"] = nameof(Configuration.BackendUrl),
            ["model"] = nameof(Configuration.ModelName),
            ["temp"] = nameof(Configuration.Temperature),
            ["system"] = nameof(Configuration.SystemPrompt),

            // Memory / Soul Threads
            ["context"] = nameof(Configuration.ContextTurns),
            ["threads"] = nameof(Configuration.SoulThreadsEnabled),
            ["embed"] = nameof(Configuration.EmbeddingModel),
            ["thread.threshold"] = nameof(Configuration.ThreadSimilarityThreshold),
            ["thread.max"] = nameof(Configuration.ThreadContextMaxFromThread),
            ["recent.max"] = nameof(Configuration.ThreadContextMaxRecent),

            // Songcraft
            ["song"] = nameof(Configuration.SongcraftEnabled),
            ["song.key"] = nameof(Configuration.SongcraftKey),
            ["song.tempo"] = nameof(Configuration.SongcraftTempoBpm),
            ["song.bars"] = nameof(Configuration.SongcraftBars),
            ["song.program"] = nameof(Configuration.SongcraftProgram),
            ["song.dir"] = nameof(Configuration.SongcraftSaveDir),
            ["song.trigger"] = nameof(Configuration.SongcraftBardCallTrigger),

            // Voice
            ["voice"] = nameof(Configuration.VoiceSpeakEnabled),
            ["voice.name"] = nameof(Configuration.VoiceName),
            ["voice.rate"] = nameof(Configuration.VoiceRate),
            ["voice.vol"] = nameof(Configuration.VoiceVolume),
            ["voice.focus"] = nameof(Configuration.VoiceOnlyWhenWindowFocused),

            // Listen
            ["listen"] = nameof(Configuration.ListenEnabled),
            ["listen.self"] = nameof(Configuration.ListenSelf),
            ["listen.say"] = nameof(Configuration.ListenSay),
            ["listen.tell"] = nameof(Configuration.ListenTell),
            ["listen.party"] = nameof(Configuration.ListenParty),
            ["listen.alliance"] = nameof(Configuration.ListenAlliance),
            ["listen.fc"] = nameof(Configuration.ListenFreeCompany),
            ["listen.shout"] = nameof(Configuration.ListenShout),
            ["listen.yell"] = nameof(Configuration.ListenYell),
            ["callsign"] = nameof(Configuration.Callsign),
            ["require.callsign"] = nameof(Configuration.RequireCallsign),

            // Broadcast & IPC
            ["persona"] = nameof(Configuration.BroadcastAsPersona),
            ["persona.name"] = nameof(Configuration.PersonaName),
            ["ipc.channel"] = nameof(Configuration.IpcChannelName),
            ["ipc.prefer"] = nameof(Configuration.PreferIpcRelay),

            // Echo channel (broadcast target)
            ["echo"] = nameof(Configuration.EchoChannel),

            // UI
            ["startopen"] = nameof(Configuration.StartOpen),
            ["opacity"] = nameof(Configuration.WindowOpacity),
            ["ascii"] = nameof(Configuration.AsciiSafe),
            ["twopane"] = nameof(Configuration.TwoPaneMode),
            ["copybuttons"] = nameof(Configuration.ShowCopyButtons),
            ["fontscale"] = nameof(Configuration.FontScale),
            ["lock"] = nameof(Configuration.LockWindow),
            ["displayname"] = nameof(Configuration.ChatDisplayName),

            // Search
            ["net"] = nameof(Configuration.AllowInternet),
            ["search.backend"] = nameof(Configuration.SearchBackend),
            ["search.key"] = nameof(Configuration.SearchApiKey),
            ["search.max"] = nameof(Configuration.SearchMaxResults),
            ["search.timeout"] = nameof(Configuration.SearchTimeoutSec),

            // Images
            ["img.backend"] = nameof(Configuration.ImageBackend),
            ["img.url"] = nameof(Configuration.ImageBaseUrl),
            ["img.model"] = nameof(Configuration.ImageModel),
            ["img.steps"] = nameof(Configuration.ImageSteps),
            ["img.cfg"] = nameof(Configuration.ImageGuidance),
            ["img.w"] = nameof(Configuration.ImageWidth),
            ["img.h"] = nameof(Configuration.ImageHeight),
            ["img.sampler"] = nameof(Configuration.ImageSampler),
            ["img.seed"] = nameof(Configuration.ImageSeed),
            ["img.timeout"] = nameof(Configuration.ImageTimeoutSec),
            ["img.save"] = nameof(Configuration.SaveImages),
            ["img.dir"] = nameof(Configuration.ImageSaveSubdir),

            // Debug
            ["debug.listen"] = nameof(Configuration.DebugListen),
            ["debug.mirror"] = nameof(Configuration.DebugMirrorToWindow),
        };

        // slash usage summary
        private static readonly string _help = string.Join("\n", new[]
        {
            "Usage:",
            "/nunu                                    -> toggle chat window",
            "/nunu open chat|config|memory           -> open a window",
            "/nunu get <key|alias>                   -> show a config value",
            "/nunu set <key|alias> <value...>        -> set a config value (bool/int/float/string)",
            "/nunu toggle <key|alias>                -> toggle a bool config",
            "/nunu list                              -> list common aliases",
            "/nunu rehook                            -> rehook chat listener",
            "/nunu ipc bind <channel>                -> bind IPC channel",
            "/nunu ipc unbind                        -> unbind IPC channel",
            "/nunu echo <say|party|shout|yell|fc|echo> -> set broadcast channel",
            "/nunu song [mood] <lyric...>            -> compose via Songcraft immediately",
        });

        private void HandleSlashCommand(string raw)
        {
            var args = (raw ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(args))
            {
                // default: toggle chat window
                ChatWindow.IsOpen = !ChatWindow.IsOpen;
                return;
            }

            var parts = SplitArgs(args);
            if (parts.Count == 0) return;

            var head = parts[0].ToLowerInvariant();

            switch (head)
            {
                case "help":
                case "?":
                    ChatWindow.AppendSystem(_help);
                    return;

                case "open":
                    CmdOpen(parts);
                    return;

                case "get":
                    CmdGet(parts);
                    return;

                case "set":
                    CmdSet(parts);
                    return;

                case "toggle":
                    CmdToggle(parts);
                    return;

                case "list":
                    CmdListAliases();
                    return;

                case "rehook":
                    RehookListener();
                    ChatWindow.AppendSystem("[listener] rehooked.");
                    return;

                case "ipc":
                    CmdIpc(parts);
                    return;

                case "echo":
                    CmdEcho(parts);
                    return;

                case "song":
                    CmdSong(parts);
                    return;

                case "config":
                    ConfigWindow.IsOpen = true;
                    return;

                case "memory":
                    if (MemoryWindow is { } mw) mw.IsOpen = true;
                    return;

                default:
                    ChatWindow.AppendSystem($"Unknown subcommand '{head}'. Type '/nunu help' for usage.");
                    return;
            }
        }

        // --- subcommand implementations ---

        private void CmdOpen(List<string> parts)
        {
            if (parts.Count < 2)
            {
                ChatWindow.AppendSystem("open what? chat|config|memory");
                return;
            }
            var target = parts[1].ToLowerInvariant();
            if (target is "chat" or "main") { ChatWindow.IsOpen = true; return; }
            if (target is "config" or "settings") { ConfigWindow.IsOpen = true; return; }
            if (target is "mem" or "memory") { if (MemoryWindow is { } mw) mw.IsOpen = true; return; }
            ChatWindow.AppendSystem($"Unknown window '{target}'.");
        }

        private void CmdGet(List<string> parts)
        {
            if (parts.Count < 2) { ChatWindow.AppendSystem("get <key|alias>"); return; }
            var key = ResolveKey(parts[1]);
            if (key is null) { ChatWindow.AppendSystem($"Unknown key/alias '{parts[1]}'. Use '/nunu list'."); return; }

            var pi = typeof(Configuration).GetProperty(key, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (pi is null) { ChatWindow.AppendSystem($"No property '{key}' on Configuration."); return; }

            var val = pi.GetValue(Config);
            ChatWindow.AppendSystem($"{key} = {(val is null ? "(null)" : val.ToString())}");
        }

        private void CmdSet(List<string> parts)
        {
            if (parts.Count < 3) { ChatWindow.AppendSystem("set <key|alias> <value...>"); return; }
            var key = ResolveKey(parts[1]);
            if (key is null) { ChatWindow.AppendSystem($"Unknown key/alias '{parts[1]}'. Use '/nunu list'."); return; }

            var pi = typeof(Configuration).GetProperty(key, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (pi is null || !pi.CanWrite) { ChatWindow.AppendSystem($"Cannot set '{key}'."); return; }

            var valueText = string.Join(' ', parts.GetRange(2, parts.Count - 2));
            if (!TryConvert(valueText, pi.PropertyType, out var boxed, out var error))
            {
                ChatWindow.AppendSystem($"set {key}: {error}");
                return;
            }

            var before = pi.GetValue(Config);
            pi.SetValue(Config, boxed);

            Config.Save();
            ChatWindow.AppendSystem($"set {key} = {boxed}");

            ApplySideEffectsOnConfigChange(key, before, boxed);
        }

        private void CmdToggle(List<string> parts)
        {
            if (parts.Count < 2) { ChatWindow.AppendSystem("toggle <key|alias>"); return; }
            var key = ResolveKey(parts[1]);
            if (key is null) { ChatWindow.AppendSystem($"Unknown key/alias '{parts[1]}'. Use '/nunu list'."); return; }

            var pi = typeof(Configuration).GetProperty(key, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (pi is null || pi.PropertyType != typeof(bool) || !pi.CanWrite)
            {
                ChatWindow.AppendSystem($"'{key}' is not a writable bool.");
                return;
            }

            var current = (bool)(pi.GetValue(Config) ?? false);
            var next = !current;
            pi.SetValue(Config, next);
            Config.Save();
            ChatWindow.AppendSystem($"toggle {key} -> {next}");

            ApplySideEffectsOnConfigChange(key, current, next);
        }

        private void CmdListAliases()
        {
            var lines = new List<string> { "Common keys/aliases (you can also use real property names):" };
            foreach (var kv in _aliases)
                lines.Add($"- {kv.Key} -> {kv.Value}");
            ChatWindow.AppendSystem(string.Join("\n", lines));
        }

        private void CmdIpc(List<string> parts)
        {
            if (parts.Count < 2) { ChatWindow.AppendSystem("ipc bind <channel> | ipc unbind"); return; }
            var act = parts[1].ToLowerInvariant();

            if (act == "bind")
            {
                if (parts.Count < 3) { ChatWindow.AppendSystem("ipc bind <channel>"); return; }
                var name = parts[2];

                // Recreate relay and bind (dispose is our unbind)
                try { _ipcRelay?.Dispose(); } catch { }
                _ipcRelay = new IpcChatRelay(PluginInterface, Log);

                var ok = _ipcRelay.Bind(name);
                Config.IpcChannelName = ok ? name : string.Empty;
                Config.Save();

                _broadcaster?.SetIpcRelay(_ipcRelay, Config.PreferIpcRelay);
                ChatWindow.AppendSystem(ok ? $"[ipc] bound to '{name}'" : $"[ipc] failed to bind '{name}'");
                return;
            }

            if (act == "unbind")
            {
                try { _ipcRelay?.Dispose(); } catch { }
                _ipcRelay = new IpcChatRelay(PluginInterface, Log);
                Config.IpcChannelName = string.Empty;
                Config.Save();

                _broadcaster?.SetIpcRelay(_ipcRelay, Config.PreferIpcRelay);
                ChatWindow.AppendSystem("[ipc] unbound.");
                return;
            }

            ChatWindow.AppendSystem("ipc bind <channel> | ipc unbind");
        }

        private void CmdEcho(List<string> parts)
        {
            if (parts.Count < 2)
            {
                ChatWindow.AppendSystem("echo <say|party|shout|yell|fc|echo>");
                return;
            }
            var chosen = parts[1];
            _echoChannel = ParseChannel(chosen);
            Config.EchoChannel = chosen;
            Config.Save();
            ChatWindow.AppendSystem($"[echo] channel set to {Config.EchoChannel}");
        }

        private void CmdSong(List<string> parts)
        {
            // `/nunu song [mood] <lyric...>`
            if (!Config.SongcraftEnabled || _songcraft is null)
            {
                ChatWindow.AddSystemLine("[Songcraft] disabled.");
                return;
            }

            string? mood = null;
            string lyric = "";

            if (parts.Count >= 2)
            {
                if (parts[1].Length <= 12)
                {
                    mood = parts[1];
                    lyric = parts.Count >= 3 ? string.Join(' ', parts.GetRange(2, parts.Count - 2)) : "";
                }
                else
                {
                    lyric = string.Join(' ', parts.GetRange(1, parts.Count - 1));
                }
            }

            var text = string.IsNullOrWhiteSpace(lyric) ? "(no text)" : lyric;

            try
            {
                var path = TriggerSongcraft(text, mood);
                ChatWindow.AddSystemLine(!string.IsNullOrEmpty(path)
                    ? $"[Songcraft] Saved: {path}"
                    : "[Songcraft] Failed to compose.");
            }
            catch (Exception ex)
            {
                ChatWindow.AddSystemLine("[Songcraft] Error while composing (see logs).");
                try { _songLog?.Error(ex, "[Songcraft] /nunu song failed"); } catch { }
            }
        }

        // --- utilities ---

        private static List<string> SplitArgs(string s)
        {
            // simple splitter that respects quotes
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(s)) return list;
            var i = 0;
            while (i < s.Length)
            {
                while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
                if (i >= s.Length) break;

                if (s[i] == '"' || s[i] == '“' || s[i] == '”')
                {
                    var q = s[i];
                    i++;
                    var start = i;
                    while (i < s.Length && s[i] != q) i++;
                    list.Add(s[start..Math.Min(i, s.Length)]);
                    if (i < s.Length && s[i] == q) i++;
                }
                else
                {
                    var start = i;
                    while (i < s.Length && !char.IsWhiteSpace(s[i])) i++;
                    list.Add(s[start..i]);
                }
            }
            return list;
        }

        private string? ResolveKey(string input)
        {
            if (_aliases.TryGetValue(input, out var real))
                return real;

            // allow direct property names (case-insensitive)
            var pi = typeof(Configuration).GetProperty(input,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            return pi?.Name;
        }

        private static bool TryConvert(string text, Type target, out object? boxed, out string error)
        {
            try
            {
                if (target == typeof(string)) { boxed = text; error = ""; return true; }
                if (target == typeof(bool))
                {
                    if (bool.TryParse(text, out var b)) { boxed = b; error = ""; return true; }
                    if (text.Equals("on", StringComparison.OrdinalIgnoreCase)) { boxed = true; error = ""; return true; }
                    if (text.Equals("off", StringComparison.OrdinalIgnoreCase)) { boxed = false; error = ""; return true; }
                    if (text == "1") { boxed = true; error = ""; return true; }
                    if (text == "0") { boxed = false; error = ""; return true; }
                    boxed = null; error = "expected bool (true/false/on/off/1/0)"; return false;
                }
                if (target == typeof(int))
                {
                    if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
                    { boxed = i; error = ""; return true; }
                    boxed = null; error = "expected int"; return false;
                }
                if (target == typeof(float))
                {
                    if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var f))
                    { boxed = f; error = ""; return true; }
                    boxed = null; error = "expected float"; return false;
                }
                // nullable support
                var u = Nullable.GetUnderlyingType(target);
                if (u != null)
                {
                    if (string.Equals(text, "null", StringComparison.OrdinalIgnoreCase))
                    { boxed = null; error = ""; return true; }
                    if (TryConvert(text, u, out var inner, out error))
                    { boxed = inner; return true; }
                    boxed = null; return false;
                }

                // last resort
                boxed = Convert.ChangeType(text, target, CultureInfo.InvariantCulture);
                error = "";
                return true;
            }
            catch (Exception ex)
            {
                boxed = null;
                error = $"cannot convert '{text}' to {target.Name}: {ex.Message}";
                return false;
            }
        }

        private void ApplySideEffectsOnConfigChange(string key, object? before, object? after)
        {
            // Listener-affecting keys -> rehook immediately
            if (key.StartsWith("Listen", StringComparison.OrdinalIgnoreCase)
                || key.Equals(nameof(Configuration.RequireCallsign), StringComparison.OrdinalIgnoreCase)
                || key.Equals(nameof(Configuration.Callsign), StringComparison.OrdinalIgnoreCase)
                || key.Equals(nameof(Configuration.DebugListen), StringComparison.OrdinalIgnoreCase))
            {
                RehookListener();
                ChatWindow.AppendSystem("[listener] rehooked.");
            }

            // IPC rebind when channel changes
            if (key.Equals(nameof(Configuration.IpcChannelName), StringComparison.OrdinalIgnoreCase))
            {
                // Recreate relay (acts like unbind+bind)
                try { _ipcRelay?.Dispose(); } catch { }
                _ipcRelay = new IpcChatRelay(PluginInterface, Log);

                var name = Config.IpcChannelName;
                if (!string.IsNullOrWhiteSpace(name))
                {
                    var ok = _ipcRelay.Bind(name!);
                    ChatWindow.AppendSystem(ok ? $"[ipc] bound to '{name}'" : $"[ipc] failed to bind '{name}'");
                }

                // refresh broadcaster preference wiring
                _broadcaster?.SetIpcRelay(_ipcRelay, Config.PreferIpcRelay);
                return;
            }

            // IPC prefer toggle
            if (key.Equals(nameof(Configuration.PreferIpcRelay), StringComparison.OrdinalIgnoreCase))
            {
                _broadcaster?.SetIpcRelay(_ipcRelay, Config.PreferIpcRelay);
                ChatWindow.AppendSystem($"[ipc] prefer relay = {Config.PreferIpcRelay}");
            }

            // Echo channel
            if (key.Equals(nameof(Configuration.EchoChannel), StringComparison.OrdinalIgnoreCase))
            {
                _echoChannel = ParseChannel(Config.EchoChannel);
                ChatWindow.AppendSystem($"[echo] channel set to {Config.EchoChannel}");
                return;
            }

            // UI niceties
            if (key.Equals(nameof(Configuration.StartOpen), StringComparison.OrdinalIgnoreCase) && after is bool b)
                ChatWindow.IsOpen = b;
        }

        // ===== Echo channel =====
        private static ChatBroadcaster.NunuChannel ParseChannel(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return ChatBroadcaster.NunuChannel.Party;

            switch (name.Trim().ToLowerInvariant())
            {
                case "say": return ChatBroadcaster.NunuChannel.Say;
                case "party": return ChatBroadcaster.NunuChannel.Party;
                case "shout": return ChatBroadcaster.NunuChannel.Shout;
                case "yell": return ChatBroadcaster.NunuChannel.Yell;
                // Many builds don't expose Alliance; use Party as a stable fallback
                case "alliance": return ChatBroadcaster.NunuChannel.Party;
                case "freecompany":
                case "fc": return ChatBroadcaster.NunuChannel.FreeCompany;
                case "echo": return ChatBroadcaster.NunuChannel.Echo;
                default: return ChatBroadcaster.NunuChannel.Party;
            }
        }
    }
}
