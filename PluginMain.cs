using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Numerics;
using System.Reflection;
using System.Text.RegularExpressions;
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

        // Broadcast echo (overridden by Config)
        internal ChatBroadcaster.NunuChannel _echoChannel = ChatBroadcaster.NunuChannel.Party;

        // Commands
        private const string Command = "/nunu";

        // Soul Threads & Songcraft
        private EmbeddingClient? _embedding;
        private SoulThreadService? _threads;
        private SongcraftService? _songcraft;
        private IPluginLog? _songLog;

        // UiBuilder hooks & streaming flag
        private bool _uiHooksBound;
        private bool _isStreaming;

        // Typing Indicator
        private bool _typingActive;

        // ===== Emotion Engine =====
        private EmotionManager _emotions = new EmotionManager();
        private DateTime _lastEmotionChangeUtc = DateTime.UtcNow;

        // Stream-safe emotion marker parsing
        private string _emotionScanTail = "";
        private static readonly Regex EmotionMarker = new Regex(
            @"[\(\[\{\<]\s*emotion\s*:\s*([a-zA-Z]+)\s*[\)\]\}\>]",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // ===== Dreaming Mode =====
        private System.Threading.Timer? _dreamTimer;
        private DateTime _lastUserInteractionUtc = DateTime.UtcNow;

        public PluginMain()
        {
            Instance = this;

            // No timeout; we cancel via CTS while streaming.
            _http.Timeout = Timeout.InfiniteTimeSpan;

            // ---- Config
            Config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
            Config.Initialize(PluginInterface);

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

            // ---- Emotion Engine
            InitializeEmotionEngine();

            // ---- Dreaming Mode
            InitializeDreaming();

            // ---- Greeting
            ChatWindow.AppendAssistant("Nunu is awake. Ask me anything—or play me a memory.");
        }

        public void Dispose()
        {
            try { _listener?.Dispose(); } catch { }
            try { _broadcaster?.Dispose(); } catch { }
            try { _ipcRelay?.Dispose(); } catch { }
            try { Voice?.Dispose(); } catch { }
            try { _dreamTimer?.Dispose(); } catch { }

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
        private void DrawUi()
        {
            // Emotion decay (neutral drift on idle)
            if (Config.EmotionEnabled && Config.EmotionDecaySeconds > 0 && !_emotions.Locked)
            {
                var idle = (DateTime.UtcNow - _lastEmotionChangeUtc).TotalSeconds;
                if (idle >= Config.EmotionDecaySeconds && _emotions.Current != NunuEmotion.Neutral)
                    SafeSetEmotion(NunuEmotion.Neutral, reason: "decay");
            }

            WindowSystem.Draw();
        }

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
            _lastUserInteractionUtc = DateTime.UtcNow; // dreaming idle reset

            if (_threads is null) { Memory.Append("user", text, topic: author); return; }
            _threads.AppendAndThread("user", text, author, token);
        }

        private void OnAssistantReply(string author, string reply, CancellationToken token)
        {
            _lastUserInteractionUtc = DateTime.UtcNow; // dreaming idle reset

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

        public bool TryHandleBardCall(string author, string text)
        {
            if (!Config.SongcraftEnabled || _songcraft is null) return false;

            var trig = string.IsNullOrWhiteSpace(Config.SongcraftBardCallTrigger) ? "/song" : Config.SongcraftBardCallTrigger!;
            var idx = text.IndexOf(trig, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return false;

            string after = text[(idx + trig.Length)..].Trim();
            string? mood = null;
            string lyric = text;

            if (!string.IsNullOrEmpty(after))
            {
                var parts = after.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 1 && parts[0].Length <= 12)
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

            try { OnUserUtterance(author, $"{trig} {mood ?? ""} {payload}".Trim(), CancellationToken.None); } catch { }
            return true;
        }

        // ===== Emotion Engine wiring =====
        private void InitializeEmotionEngine()
        {
            if (!Config.EmotionEnabled) return;

            _emotions.SetLocked(Config.EmotionLock);
            if (EmotionManager.TryParse(Config.EmotionDefault, out var start))
                _emotions.Set(start);

            _emotions.OnEmotionChanged += emo =>
            {
                _lastEmotionChangeUtc = DateTime.UtcNow;

                // UI tint
                ChatWindow.SetMoodColor(ColorFor(emo));

                // Voice tone
                try { Voice?.ApplyEmotionPreset(emo); } catch { }

                // Emote line (optional)
                if (Config.EmotionEmitEmote && _broadcaster?.Enabled == true)
                {
                    var line = _emotions.EmoteFor(emo);
                    _broadcaster.Enqueue(_echoChannel, line);
                }
            };

            // apply initial tint/voice
            ChatWindow.SetMoodColor(ColorFor(_emotions.Current));
            try { Voice?.ApplyEmotionPreset(_emotions.Current); } catch { }
        }

        private static Vector4 ColorFor(NunuEmotion emo)
        {
            // soft UI tints
            switch (emo)
            {
                case NunuEmotion.Happy: return new Vector4(0.98f, 0.90f, 0.65f, 1f);
                case NunuEmotion.Curious: return new Vector4(0.80f, 0.90f, 1.00f, 1f);
                case NunuEmotion.Playful: return new Vector4(1.00f, 0.80f, 0.95f, 1f);
                case NunuEmotion.Mournful: return new Vector4(0.70f, 0.78f, 1.00f, 1f);
                case NunuEmotion.Sad: return new Vector4(0.75f, 0.80f, 0.95f, 1f);
                case NunuEmotion.Angry: return new Vector4(1.00f, 0.75f, 0.75f, 1f);
                case NunuEmotion.Tired: return new Vector4(0.85f, 0.85f, 0.85f, 1f);
                default: return new Vector4(1f, 1f, 1f, 1f);
            }
        }

        private void SafeSetEmotion(NunuEmotion emo, string reason)
        {
            if (!Config.EmotionEnabled) return;
            try { _emotions.Set(emo); Log.Debug($"[Emotion] -> {emo} ({reason})"); } catch { }
        }

        // ===== Dreaming Mode =====
        private void InitializeDreaming()
        {
            try { _dreamTimer?.Dispose(); } catch { }
            if (!Config.DreamingEnabled)
            {
                _dreamTimer = null;
                return;
            }

            // check every minute
            _dreamTimer = new System.Threading.Timer(CheckDreamCycle, null,
                dueTime: TimeSpan.FromMinutes(1),
                period: TimeSpan.FromMinutes(1));
        }

        private void CheckDreamCycle(object? state)
        {
            try
            {
                if (!Config.DreamingEnabled) return;
                if (_isStreaming) return;

                var idleMins = (DateTime.UtcNow - _lastUserInteractionUtc).TotalMinutes;
                if (idleMins < Config.DreamingIdleMinutes) return;

                // fire-and-forget dream task
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var dream = await ComposeDreamAsync();
                        if (!string.IsNullOrWhiteSpace(dream))
                        {
                            Memory.Append("dream", dream.Trim(), topic: "subconscious");
                            if (Config.DreamingShowInChat)
                                ChatWindow.AppendAssistant($"[Dream] {dream.Trim()}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "[Dreaming] compose failed");
                    }
                    finally
                    {
                        // reset idle so we don't immediately dream again
                        _lastUserInteractionUtc = DateTime.UtcNow;
                    }
                });
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[Dreaming] timer tick failed");
            }
        }

        private async Task<string> ComposeDreamAsync()
        {
            // Build seeds from recent context (role-tagged)
            var recent = Memory.GetRecentForContext(Math.Max(4, Config.ThreadContextMaxRecent));
            var lines = new List<string>();
            foreach (var (role, content) in recent)
            {
                if (string.IsNullOrWhiteSpace(content)) continue;
                // Keep short, avoid blowing up prompt
                var trimmed = content.Length > 200 ? content.Substring(0, 200) + "…" : content;
                lines.Add($"{role}: {trimmed}");
                if (lines.Count >= 6) break;
            }

            var mood = _emotions.Current.ToString();

            var sys = "You are Little Nunu, the Soul Weeper—a void-touched bard of Eorzea. " +
                      "You are dreaming while the player is idle. " +
                      "Write 1–2 short poetic lines (no more than 180 characters total), reflective and metaphorical, inspired by the seeds. " +
                      "Do not include code fences or markdown. Keep it diegetic to FFXIV.";

            var user = $"Emotion: {mood}\nSeeds:\n- " + string.Join("\n- ", lines) +
                       "\nDream now in one or two short lines.";

            var chat = new List<(string role, string content)>
            {
                ("system", sys),
                ("user", user),
            };

            var client = new OllamaClient(_http, Config);

            // aggregate via streaming API (so we don't add new client method)
            string dream = "";
            await foreach (var raw in client.StreamChatAsync(Config.BackendUrl, chat, CancellationToken.None))
            {
                if (!string.IsNullOrEmpty(raw))
                    dream += raw;
            }

            // Trim emotion markers if the model added any
            if (!string.IsNullOrWhiteSpace(dream))
            {
                dream = EmotionMarker.Replace(dream, "");
                dream = dream.Trim();
                // Be safe: keep very short
                if (dream.Length > 240) dream = dream.Substring(0, 240);
            }

            return dream ?? "";
        }

        // ===== Inbound path =====
        private void OnHeardFromChat(string author, string text)
        {
            _lastUserInteractionUtc = DateTime.UtcNow; // dreaming idle reset

            if (TryHandleBardCall(author, text))
                return;

            HandleUserSend(text);
        }

        private void HandleUserSend(string text)
        {
            _lastUserInteractionUtc = DateTime.UtcNow; // dreaming idle reset

            try
            {
                var who = string.IsNullOrWhiteSpace(Config.ChatDisplayName) ? "You" : Config.ChatDisplayName;
                OnUserUtterance(who, text, CancellationToken.None);
            }
            catch { /* best-effort */ }

            // Typing indicator
            StartTypingIndicator();

            // Begin UI streaming
            ChatWindow.BeginAssistantStream();

            // Build context then add the current turn
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

                // Emotion context + marker guidance
                if (Config.EmotionEnabled)
                    chat.Insert(0, ("system", $"Current emotional tone: {_emotions.Current}. If your tone changes, include a short inline marker like (emotion: happy)."));

                if (!string.IsNullOrWhiteSpace(Config.SystemPrompt))
                    chat.Insert(0, ("system", Config.SystemPrompt));

                var client = new OllamaClient(_http, Config);

                await foreach (var raw in client.StreamChatAsync(Config.BackendUrl, chat, cts.Token))
                {
                    var incoming = raw ?? "";
                    var visibleChunk = incoming;

                    // stream-safe emotion markers
                    if (Config.EmotionEnabled && Config.EmotionPromptMarkersEnabled)
                    {
                        var scan = _emotionScanTail + incoming;

                        var matches = EmotionMarker.Matches(scan);
                        foreach (Match m in matches)
                        {
                            var name = m.Groups[1].Value;
                            if (EmotionManager.TryParse(name, out var emo))
                                SafeSetEmotion(emo, "marker");
                        }

                        var cleaned = EmotionMarker.Replace(scan, "");
                        if (cleaned.Length >= _emotionScanTail.Length)
                            visibleChunk = cleaned.Substring(_emotionScanTail.Length);
                        else
                            visibleChunk = "";

                        const int TailKeep = 24;
                        _emotionScanTail = scan.Length > TailKeep ? scan.Substring(scan.Length - TailKeep) : scan;
                    }

                    if (string.IsNullOrEmpty(visibleChunk)) continue;

                    reply += visibleChunk;
                    ChatWindow.AppendAssistantDelta(visibleChunk); // stream deltas to UI
                }

                // final sweep
                if (Config.EmotionEnabled && Config.EmotionPromptMarkersEnabled && !string.IsNullOrEmpty(reply))
                    reply = EmotionMarker.Replace(reply, "");

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

                    // Optional: Songcraft composition (fire-and-forget)
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
                StopTypingIndicator(); // always stop
                _isStreaming = false;
                _lastUserInteractionUtc = DateTime.UtcNow; // dreaming idle reset after assistant speaks
                try { cts.Dispose(); } catch { }
            }
        }

        // ===== Typing Indicator =====
        private void StartTypingIndicator()
        {
            if (_typingActive) return;
            _typingActive = true;

            if (!Config.TypingIndicatorEnabled) return;
            if (_broadcaster == null || !_broadcaster.Enabled) return;

            var note = string.IsNullOrWhiteSpace(Config.TypingIndicatorMessage)
                ? "Little Nunu is writing ...."
                : Config.TypingIndicatorMessage;

            _broadcaster.Enqueue(_echoChannel, note);
        }

        private void StopTypingIndicator()
        {
            if (!_typingActive) return;
            _typingActive = false;

            if (!Config.TypingIndicatorEnabled) return;
            if (!Config.TypingIndicatorSendDone) return;
            if (_broadcaster == null || !_broadcaster.Enabled) return;

            var done = string.IsNullOrWhiteSpace(Config.TypingIndicatorDoneMessage)
                ? "…done."
                : Config.TypingIndicatorDoneMessage;

            _broadcaster.Enqueue(_echoChannel, done);
        }

        // ========================= Slash Command Router =========================

        private static readonly Dictionary<string, string> _aliases = CreateAliases();

        private static Dictionary<string, string> CreateAliases()
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // Backend
            d.Add("mode", nameof(Configuration.BackendMode));
            d.Add("endpoint", nameof(Configuration.BackendUrl));
            d.Add("url", nameof(Configuration.BackendUrl));
            d.Add("model", nameof(Configuration.ModelName));
            d.Add("temp", nameof(Configuration.Temperature));
            d.Add("system", nameof(Configuration.SystemPrompt));

            // Memory / Soul Threads
            d.Add("context", nameof(Configuration.ContextTurns));
            d.Add("threads", nameof(Configuration.SoulThreadsEnabled));
            d.Add("embed", nameof(Configuration.EmbeddingModel));
            d.Add("thread.threshold", nameof(Configuration.ThreadSimilarityThreshold));
            d.Add("thread.max", nameof(Configuration.ThreadContextMaxFromThread));
            d.Add("recent.max", nameof(Configuration.ThreadContextMaxRecent));

            // Songcraft
            d.Add("song", nameof(Configuration.SongcraftEnabled));
            d.Add("song.key", nameof(Configuration.SongcraftKey));
            d.Add("song.tempo", nameof(Configuration.SongcraftTempoBpm));
            d.Add("song.bars", nameof(Configuration.SongcraftBars));
            d.Add("song.program", nameof(Configuration.SongcraftProgram));
            d.Add("song.dir", nameof(Configuration.SongcraftSaveDir));
            d.Add("song.trigger", nameof(Configuration.SongcraftBardCallTrigger));

            // Voice
            d.Add("voice", nameof(Configuration.VoiceSpeakEnabled));
            d.Add("voice.name", nameof(Configuration.VoiceName));
            d.Add("voice.rate", nameof(Configuration.VoiceRate));
            d.Add("voice.vol", nameof(Configuration.VoiceVolume));
            d.Add("voice.focus", nameof(Configuration.VoiceOnlyWhenWindowFocused));

            // Listen
            d.Add("listen", nameof(Configuration.ListenEnabled));
            d.Add("listen.self", nameof(Configuration.ListenSelf));
            d.Add("listen.say", nameof(Configuration.ListenSay));
            d.Add("listen.tell", nameof(Configuration.ListenTell));
            d.Add("listen.party", nameof(Configuration.ListenParty));
            d.Add("listen.alliance", nameof(Configuration.ListenAlliance));
            d.Add("listen.fc", nameof(Configuration.ListenFreeCompany));
            d.Add("listen.shout", nameof(Configuration.ListenShout));
            d.Add("listen.yell", nameof(Configuration.ListenYell));
            d.Add("callsign", nameof(Configuration.Callsign));
            d.Add("require.callsign", nameof(Configuration.RequireCallsign));

            // Broadcast & IPC
            d.Add("persona", nameof(Configuration.BroadcastAsPersona));
            d.Add("persona.name", nameof(Configuration.PersonaName));
            d.Add("ipc.channel", nameof(Configuration.IpcChannelName));
            d.Add("ipc.prefer", nameof(Configuration.PreferIpcRelay));

            // Echo channel
            d.Add("echo", nameof(Configuration.EchoChannel));

            // Typing Indicator
            d.Add("typing", nameof(Configuration.TypingIndicatorEnabled));
            d.Add("typing.msg", nameof(Configuration.TypingIndicatorMessage));
            d.Add("typing.done", nameof(Configuration.TypingIndicatorSendDone));
            d.Add("typing.done.msg", nameof(Configuration.TypingIndicatorDoneMessage));

            // Emotion
            d.Add("emotion.enabled", nameof(Configuration.EmotionEnabled));
            d.Add("emotion.emote", nameof(Configuration.EmotionEmitEmote));
            d.Add("emotion.markers", nameof(Configuration.EmotionPromptMarkersEnabled));
            d.Add("emotion.decay", nameof(Configuration.EmotionDecaySeconds));
            d.Add("emotion.default", nameof(Configuration.EmotionDefault));
            d.Add("emotion.lock", nameof(Configuration.EmotionLock));

            // Dreaming
            d.Add("dream", nameof(Configuration.DreamingEnabled));
            d.Add("dream.idle", nameof(Configuration.DreamingIdleMinutes));
            d.Add("dream.show", nameof(Configuration.DreamingShowInChat));

            // UI
            d.Add("startopen", nameof(Configuration.StartOpen));
            d.Add("opacity", nameof(Configuration.WindowOpacity));
            d.Add("ascii", nameof(Configuration.AsciiSafe));
            d.Add("twopane", nameof(Configuration.TwoPaneMode));
            d.Add("copybuttons", nameof(Configuration.ShowCopyButtons));
            d.Add("fontscale", nameof(Configuration.FontScale));
            d.Add("lock", nameof(Configuration.LockWindow));
            d.Add("displayname", nameof(Configuration.ChatDisplayName));

            // Search
            d.Add("net", nameof(Configuration.AllowInternet));
            d.Add("search.backend", nameof(Configuration.SearchBackend));
            d.Add("search.key", nameof(Configuration.SearchApiKey));
            d.Add("search.max", nameof(Configuration.SearchMaxResults));
            d.Add("search.timeout", nameof(Configuration.SearchTimeoutSec));

            // Images
            d.Add("img.backend", nameof(Configuration.ImageBackend));
            d.Add("img.url", nameof(Configuration.ImageBaseUrl));
            d.Add("img.model", nameof(Configuration.ImageModel));
            d.Add("img.steps", nameof(Configuration.ImageSteps));
            d.Add("img.cfg", nameof(Configuration.ImageGuidance));
            d.Add("img.w", nameof(Configuration.ImageWidth));
            d.Add("img.h", nameof(Configuration.ImageHeight));
            d.Add("img.sampler", nameof(Configuration.ImageSampler));
            d.Add("img.seed", nameof(Configuration.ImageSeed));
            d.Add("img.timeout", nameof(Configuration.ImageTimeoutSec));
            d.Add("img.save", nameof(Configuration.SaveImages));
            d.Add("img.dir", nameof(Configuration.ImageSaveSubdir));

            // Debug
            d.Add("debug.listen", nameof(Configuration.DebugListen));
            d.Add("debug.mirror", nameof(Configuration.DebugMirrorToWindow));

            return d;
        }

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
            "/nunu emotion                           -> show current emotion",
            "/nunu emotion set <neutral|happy|curious|playful|mournful|sad|angry|tired>",
            "/nunu emotion lock <true|false>",
            "/nunu emotion emote <true|false>",
            "/nunu dream                             -> show dreaming status",
            "/nunu dream on|off                      -> enable/disable dreaming",
            "/nunu dream idle <minutes>              -> set idle minutes before dreaming",
            "/nunu dream show <true|false>           -> echo dreams into chat window",
            "/nunu dream now                         -> force a dream once",
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

                case "open": CmdOpen(parts); return;
                case "get": CmdGet(parts); return;
                case "set": CmdSet(parts); return;
                case "toggle": CmdToggle(parts); return;
                case "list": CmdListAliases(); return;
                case "rehook": RehookListener(); ChatWindow.AppendSystem("[listener] rehooked."); return;
                case "ipc": CmdIpc(parts); return;
                case "echo": CmdEcho(parts); return;
                case "song": CmdSong(parts); return;
                case "emotion": CmdEmotion(parts); return;
                case "dream": CmdDream(parts); return;

                case "config":
                    ConfigWindow.IsOpen = true; return;

                case "memory":
                    if (MemoryWindow is { } mw) mw.IsOpen = true; return;

                default:
                    ChatWindow.AppendSystem($"Unknown subcommand '{head}'. Type '/nunu help' for usage.");
                    return;
            }
        }

        private void CmdDream(List<string> parts)
        {
            if (parts.Count == 1)
            {
                ChatWindow.AppendSystem($"Dreaming: {(Config.DreamingEnabled ? "on" : "off")} | idle={Config.DreamingIdleMinutes}m | show={(Config.DreamingShowInChat ? "on" : "off")}");
                return;
            }

            var sub = parts[1].ToLowerInvariant();

            if (sub is "on" or "off")
            {
                Config.DreamingEnabled = sub == "on";
                Config.Save();
                InitializeDreaming();
                ChatWindow.AppendSystem($"Dreaming {(Config.DreamingEnabled ? "enabled" : "disabled")}.");
                return;
            }

            if (sub == "idle")
            {
                if (parts.Count < 3) { ChatWindow.AppendSystem("dream idle <minutes>"); return; }
                if (int.TryParse(parts[2], out var mins) && mins >= 1 && mins <= 240)
                {
                    Config.DreamingIdleMinutes = mins;
                    Config.Save();
                    ChatWindow.AppendSystem($"Dreaming idle minutes = {mins}");
                }
                else ChatWindow.AppendSystem("Please provide minutes between 1 and 240.");
                return;
            }

            if (sub == "show")
            {
                if (parts.Count < 3) { ChatWindow.AppendSystem("dream show <true|false>"); return; }
                if (bool.TryParse(parts[2], out var b))
                {
                    Config.DreamingShowInChat = b;
                    Config.Save();
                    ChatWindow.AppendSystem($"Dreaming show in chat = {b}");
                }
                else ChatWindow.AppendSystem("dream show expects true/false");
                return;
            }

            if (sub == "now")
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var dream = await ComposeDreamAsync();
                        if (!string.IsNullOrWhiteSpace(dream))
                        {
                            Memory.Append("dream", dream.Trim(), topic: "subconscious");
                            if (Config.DreamingShowInChat)
                                ChatWindow.AppendAssistant($"[Dream] {dream.Trim()}");
                            ChatWindow.AppendSystem("[Dreaming] Composed.");
                        }
                        else
                        {
                            ChatWindow.AppendSystem("[Dreaming] No dream produced.");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "[Dreaming] now failed");
                        ChatWindow.AppendSystem("[Dreaming] Error (see logs).");
                    }
                });
                return;
            }

            ChatWindow.AppendSystem("dream | dream on|off | dream idle <minutes> | dream show <true|false> | dream now");
        }

        // ===== Slash subcommand implementations (existing) =====

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

        private void CmdEmotion(List<string> parts)
        {
            if (parts.Count == 1)
            {
                ChatWindow.AppendSystem($"Emotion: {_emotions.Current} | locked={_emotions.Locked} | emote={(Config.EmotionEmitEmote ? "on" : "off")}");
                return;
            }

            var sub = parts[1].ToLowerInvariant();
            if (sub == "set")
            {
                if (parts.Count < 3) { ChatWindow.AppendSystem("emotion set <name>"); return; }
                var name = parts[2];
                if (EmotionManager.TryParse(name, out var emo))
                {
                    SafeSetEmotion(emo, "slash");
                    ChatWindow.AppendSystem($"Emotion -> {emo}");
                }
                else ChatWindow.AppendSystem($"Unknown emotion '{name}'.");
                return;
            }

            if (sub == "lock")
            {
                if (parts.Count < 3) { ChatWindow.AppendSystem("emotion lock <true|false>"); return; }
                if (bool.TryParse(parts[2], out var b))
                {
                    _emotions.SetLocked(b);
                    Config.EmotionLock = b; Config.Save();
                    ChatWindow.AppendSystem($"Emotion lock = {b}");
                }
                else ChatWindow.AppendSystem("emotion lock expects true/false");
                return;
            }

            if (sub == "emote")
            {
                if (parts.Count < 3) { ChatWindow.AppendSystem("emotion emote <true|false>"); return; }
                if (bool.TryParse(parts[2], out var b))
                {
                    Config.EmotionEmitEmote = b; Config.Save();
                    ChatWindow.AppendSystem($"Emotion emote = {b}");
                }
                else ChatWindow.AppendSystem("emotion emote expects true/false");
                return;
            }

            ChatWindow.AppendSystem("emotion | emotion set <name> | emotion lock <true|false> | emotion emote <true|false>");
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

            // Dreaming toggles
            if (key.StartsWith("Dreaming", StringComparison.OrdinalIgnoreCase))
            {
                InitializeDreaming();
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
