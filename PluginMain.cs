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
        public string Name => "NunuTheAICompanion";

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

        // ===== Affinity =====
        private AffinityService? _affinity;

        // ===== Personality =====
        private PersonalityService? _persona;

        // ===== Aliases map =====
        private static readonly Dictionary<string, string> _aliases = CreateAliases();

        public PluginMain()
        {
            Instance = this;

            // No timeout—streaming cancels via CTS.
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

            // ---- Affinity
            InitializeAffinity(storageRoot);

            // ---- Personality
            _persona = new PersonalityService(Config, Log);

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
            catch { /* optional window */ }

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
            try { (_dreamTimer as IDisposable)?.Dispose(); } catch { }
            try { (Voice as IDisposable)?.Dispose(); } catch { } // tolerate if not IDisposable

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
            try { _affinity?.Save(); } catch { }
        }

        // ===== UiBuilder handlers =====
        private void DrawUi()
        {
            _affinity?.DecayTick();
            _persona?.DecayTick();

            // Emotion decay (drift to Neutral)
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

        // ========================= Listener boot / rehook =========================
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

        // ========================= Soul Threads =========================
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
            _affinity?.OnUserUtterance(text);

            if (_threads is null) { Memory.Append("user", text, topic: author); return; }
            _threads.AppendAndThread("user", text, author, token);
        }

        private void OnAssistantReply(string author, string reply, CancellationToken token)
        {
            _lastUserInteractionUtc = DateTime.UtcNow; // dreaming idle reset
            _affinity?.OnAssistantReply(reply);

            if (_threads is null) { Memory.Append("assistant", reply, topic: author); return; }
            _threads.AppendAndThread("assistant", reply, author, token);
        }

        // ========================= Songcraft =========================
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

        // ========================= Emotion Engine =========================
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

                // Affinity coupling
                try { _affinity?.OnEmotionChanged(emo); } catch { }

                // Personality coupling
                try { _persona?.OnEmotion(emo); } catch { }

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

        // ========================= Dreaming Mode =========================
        private void InitializeDreaming()
        {
            try { _dreamTimer?.Dispose(); } catch { }
            if (!Config.DreamingEnabled)
            {
                _dreamTimer = null;
                return;
            }

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
            var recent = Memory.GetRecentForContext(Math.Max(4, Config.ThreadContextMaxRecent));
            var lines = new List<string>();
            foreach (var (role, content) in recent)
            {
                if (string.IsNullOrWhiteSpace(content)) continue;
                var trimmed = content.Length > 200 ? content.Substring(0, 200) + "…" : content;
                lines.Add($"{role}: {trimmed}");
                if (lines.Count >= 6) break;
            }

            var mood = _emotions.Current.ToString();

            var sys = "You are Little Nunu, the Soul Weeper—a void-touched bard of Eorzea. " +
                      "You are dreaming while the player is idle. " +
                      "Write 1–2 short poetic lines (<=180 chars total), reflective and metaphorical, inspired by the seeds. " +
                      "No code fences or markdown. Keep it diegetic to FFXIV.";

            var user = $"Emotion: {mood}\nSeeds:\n- " + string.Join("\n- ", lines) +
                       "\nDream now in one or two short lines.";

            var chat = new List<(string role, string content)>
            {
                ("system", sys),
                ("user", user),
            };

            var client = new OllamaClient(_http, Config);

            string dream = "";
            await foreach (var raw in client.StreamChatAsync(Config.BackendUrl, chat, CancellationToken.None))
            {
                if (!string.IsNullOrEmpty(raw))
                    dream += raw;
            }

            if (!string.IsNullOrWhiteSpace(dream))
            {
                dream = EmotionMarker.Replace(dream, "");
                dream = dream.Trim();
                if (dream.Length > 240) dream = dream.Substring(0, 240);
            }

            return dream ?? "";
        }

        // ========================= Inbound path =========================
        private void OnHeardFromChat(string author, string text)
        {
            _lastUserInteractionUtc = DateTime.UtcNow; // dreaming idle reset

            // Bard-Call short-circuit before LLM
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

        // ========================= Streaming model loop =========================
        private async Task StreamAssistantAsync(List<(string role, string content)> history)
        {
            if (_isStreaming) return;
            _isStreaming = true;

            var cts = new CancellationTokenSource();
            string reply = "";
            try
            {
                var chat = new List<(string role, string content)>(history);

                // Personality guidance
                if (_persona is not null)
                    chat.Insert(0, ("system", _persona.BuildSystemLine()));

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

                if (Config.EmotionEnabled && Config.EmotionPromptMarkersEnabled && !string.IsNullOrEmpty(reply))
                    reply = EmotionMarker.Replace(reply, "");

                ChatWindow.EndAssistantStream();

                if (!string.IsNullOrEmpty(reply))
                {
                    try { OnAssistantReply("Nunu", reply, CancellationToken.None); }
                    catch { if (Memory.Enabled) Memory.Append("assistant", reply, topic: "chat"); }

                    try { Voice?.Speak(reply); } catch { }

                    if (_broadcaster?.Enabled == true)
                        _broadcaster.Enqueue(_echoChannel, reply);

                    try
                    {
                        var path = TriggerSongcraft(reply, mood: null);
                        if (!string.IsNullOrEmpty(path))
                            ChatWindow.AppendAssistant($"[song] Saved: {path}");
                    }
                    catch { }
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
                StopTypingIndicator();
                _isStreaming = false;
                _lastUserInteractionUtc = DateTime.UtcNow;
                try { cts.Dispose(); } catch { }
            }
        }

        // ========================= Typing Indicator =========================
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

        // ========================= Affinity =========================
        private void InitializeAffinity(string storageRoot)
        {
            try
            {
                _affinity = new AffinityService(storageRoot, Config, Log);
                _affinity.OnTierChanged += HandleAffinityTierChanged;
                Log.Info("[Affinity] initialized.");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[Affinity] init failed; continuing without.");
            }
        }

        private void HandleAffinityTierChanged(AffinityTier newTier, AffinityTier oldTier, string reason)
        {
            var msg = newTier switch
            {
                AffinityTier.Acquaintance => "The strings tremble—we are no longer strangers.",
                AffinityTier.Friend => "A warm counterpoint forms—friendship in harmony.",
                AffinityTier.Confidant => "Your secrets fall like gentle rain—I will keep them.",
                AffinityTier.Bonded => "Our melodies entwine—two threads, one song.",
                AffinityTier.Eternal => "Eclipse Aria awakens—our chorus spans the void.",
                _ => "A new measure begins."
            };

            ChatWindow.AddSystemLine($"[Affinity] Tier up → {newTier} (from {oldTier}, reason: {reason})");
            if (_broadcaster?.Enabled == true)
                _broadcaster.Enqueue(_echoChannel, $"[Affinity] {msg}");

            if (newTier >= AffinityTier.Friend && !_emotions.Locked)
                SafeSetEmotion(NunuEmotion.Happy, "tier-up");

            try { _persona?.OnAffinityTier(newTier); } catch { }
        }

        // ========================= Slash Command Router =========================

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
            "/nunu affinity                          -> show score, tier, streak",
            "/nunu affinity set <score>              -> set score directly",
            "/nunu affinity add <delta>              -> add to score",
            "/nunu affinity reset                    -> reset all stats",
            "/nunu persona                           -> show persona state",
            "/nunu persona auto <true|false>",
            "/nunu persona decay <float/day>",
            "/nunu persona set <w|c|f|p|g> <val>",
            "/nunu persona base <w0|c0|f0|p0|g0> <val>",
            "/nunu persona reset",
        });

        private void HandleSlashCommand(string raw)
        {
            var args = (raw ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(args))
            {
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
                    ChatWindow.AppendSystem(_help); return;

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
                case "affinity": CmdAffinity(parts); return;
                case "persona": CmdPersona(parts); return;

                case "config":
                    ConfigWindow.IsOpen = true; return;

                case "memory":
                    if (MemoryWindow is { } mw) mw.IsOpen = true; return;

                default:
                    ChatWindow.AppendSystem($"Unknown subcommand '{head}'. Type '/nunu help' for usage.");
                    return;
            }
        }

        // --------- Subcommand implementations ---------

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
            if (!Config.EmotionEnabled) { ChatWindow.AppendSystem("Emotion system disabled."); return; }

            if (parts.Count == 1)
            {
                ChatWindow.AppendSystem($"Emotion: current={_emotions.Current}, locked={_emotions.Locked}, emitEmote={Config.EmotionEmitEmote}, markers={Config.EmotionPromptMarkersEnabled}, decay={Config.EmotionDecaySeconds}s");
                return;
            }

            var sub = parts[1].ToLowerInvariant();

            if (sub == "set" && parts.Count >= 3)
            {
                var name = parts[2];
                if (EmotionManager.TryParse(name, out var emo))
                {
                    SafeSetEmotion(emo, "manual");
                    ChatWindow.AppendSystem($"Emotion set -> {emo}");
                }
                else ChatWindow.AppendSystem("emotion set <neutral|happy|curious|playful|mournful|sad|angry|tired>");
                return;
            }

            if (sub == "lock" && parts.Count >= 3 && bool.TryParse(parts[2], out var locked))
            {
                _emotions.SetLocked(locked);
                Config.EmotionLock = locked; Config.Save();
                ChatWindow.AppendSystem($"Emotion lock = {locked}");
                return;
            }

            if (sub == "emote" && parts.Count >= 3 && bool.TryParse(parts[2], out var emit))
            {
                Config.EmotionEmitEmote = emit; Config.Save();
                ChatWindow.AppendSystem($"Emotion emit emote = {emit}");
                return;
            }

            ChatWindow.AppendSystem("emotion | emotion set <name> | emotion lock <true|false> | emotion emote <true|false>");
        }

        private void CmdDream(List<string> parts)
        {
            if (parts.Count == 1)
            {
                ChatWindow.AppendSystem($"Dreaming: enabled={Config.DreamingEnabled}, idle={Config.DreamingIdleMinutes}m, show={Config.DreamingShowInChat}");
                return;
            }

            var sub = parts[1].ToLowerInvariant();

            if (sub is "on" or "off")
            {
                Config.DreamingEnabled = sub == "on"; Config.Save();
                InitializeDreaming();
                ChatWindow.AppendSystem($"Dreaming = {Config.DreamingEnabled}");
                return;
            }

            if (sub == "idle" && parts.Count >= 3 && int.TryParse(parts[2], out var mins))
            {
                if (mins < 1) mins = 1;
                Config.DreamingIdleMinutes = mins; Config.Save();
                InitializeDreaming();
                ChatWindow.AppendSystem($"Dreaming idle = {mins} minutes");
                return;
            }

            if (sub == "show" && parts.Count >= 3 && bool.TryParse(parts[2], out var show))
            {
                Config.DreamingShowInChat = show; Config.Save();
                ChatWindow.AppendSystem($"Dreaming show-in-chat = {show}");
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
                            ChatWindow.AppendAssistant($"[Dream] {dream.Trim()}");
                        }
                        else ChatWindow.AppendSystem("[Dream] (no output)");
                    }
                    catch (Exception ex) { Log.Warning(ex, "[Dream] now failed"); }
                });
                return;
            }

            ChatWindow.AppendSystem("dream | dream on|off | dream idle <minutes> | dream show <true|false> | dream now");
        }

        private void CmdAffinity(List<string> parts)
        {
            if (_affinity is null || !Config.AffinityEnabled)
            {
                ChatWindow.AppendSystem("Affinity disabled.");
                return;
            }

            if (parts.Count == 1)
            {
                var (score, tier, streak) = _affinity.Snapshot();
                ChatWindow.AppendSystem($"Affinity: score={score:0.##}, tier={tier}, streak={streak}");
                return;
            }

            var sub = parts[1].ToLowerInvariant();

            if (sub == "set" && parts.Count >= 3 && float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var set))
            {
                _affinity.SetScore(set, "manual");
                var (score, tier, _) = _affinity.Snapshot();
                ChatWindow.AppendSystem($"Affinity set -> score={score:0.##}, tier={tier}");
                return;
            }

            if (sub == "add" && parts.Count >= 3 && float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var add))
            {
                _affinity.AddScore(add, "manual add");
                var (score, tier, _) = _affinity.Snapshot();
                ChatWindow.AppendSystem($"Affinity add -> score={score:0.##}, tier={tier}");
                return;
            }

            if (sub == "reset")
            {
                _affinity.Reset();
                ChatWindow.AppendSystem("Affinity reset.");
                return;
            }

            ChatWindow.AppendSystem("affinity | affinity set <score> | affinity add <delta> | affinity reset");
        }

        // --- persona command (adaptive gradients) ---
        private void CmdPersona(List<string> parts)
        {
            if (_persona is null) { ChatWindow.AppendSystem("[Persona] not initialized."); return; }

            if (parts.Count == 1)
            {
                ChatWindow.AppendSystem(
                    $"Persona: auto={(Config.PersonaAutoEnabled ? "on" : "off")} decay={Config.PersonaBaselineDecayPerDay:0.00}\n" +
                    $"W={_persona.Warmth:0.00} C={_persona.Curiosity:0.00} F={_persona.Formality:0.00} P={_persona.Playfulness:0.00} G={_persona.Gravitas:0.00}\n" +
                    $"Baseline W={Config.PersonaBaselineWarmth:0.00} C={Config.PersonaBaselineCuriosity:0.00} F={Config.PersonaBaselineFormality:0.00} P={Config.PersonaBaselinePlayfulness:0.00} G={Config.PersonaBaselineGravitas:0.00}");
                return;
            }

            var sub = parts[1].ToLowerInvariant();
            if (sub == "auto" && parts.Count >= 3 && bool.TryParse(parts[2], out var on))
            {
                Config.PersonaAutoEnabled = on; Config.Save();
                ChatWindow.AppendSystem($"persona.auto = {on}");
                return;
            }

            if (sub == "decay" && parts.Count >= 3 && float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
            {
                Config.PersonaBaselineDecayPerDay = MathF.Max(0f, d); Config.Save();
                ChatWindow.AppendSystem($"persona.decay = {Config.PersonaBaselineDecayPerDay:0.00}");
                return;
            }

            if (sub == "set" && parts.Count >= 4)
            {
                var which = parts[2].ToLowerInvariant();
                if (!float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                { ChatWindow.AppendSystem("persona set <w|c|f|p|g> <float -1..1>"); return; }
                v = MathF.Max(-1f, MathF.Min(1f, v));
                switch (which)
                {
                    case "w": _persona.Nudge(PersonalityTrait.Warmth, v - _persona.Warmth, "manual"); break;
                    case "c": _persona.Nudge(PersonalityTrait.Curiosity, v - _persona.Curiosity, "manual"); break;
                    case "f": _persona.Nudge(PersonalityTrait.Formality, v - _persona.Formality, "manual"); break;
                    case "p": _persona.Nudge(PersonalityTrait.Playfulness, v - _persona.Playfulness, "manual"); break;
                    case "g": _persona.Nudge(PersonalityTrait.Gravitas, v - _persona.Gravitas, "manual"); break;
                    default: ChatWindow.AppendSystem("persona set <w|c|f|p|g> <float -1..1>"); return;
                }
                ChatWindow.AppendSystem($"Persona set {which} -> {v:0.00}");
                return;
            }

            if (sub == "base" && parts.Count >= 4)
            {
                var which = parts[2].ToLowerInvariant();
                if (!float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                { ChatWindow.AppendSystem("persona base <w0|c0|f0|p0|g0> <float -1..1>"); return; }
                v = MathF.Max(-1f, MathF.Min(1f, v));
                switch (which)
                {
                    case "w0": Config.PersonaBaselineWarmth = v; break;
                    case "c0": Config.PersonaBaselineCuriosity = v; break;
                    case "f0": Config.PersonaBaselineFormality = v; break;
                    case "p0": Config.PersonaBaselinePlayfulness = v; break;
                    case "g0": Config.PersonaBaselineGravitas = v; break;
                    default: ChatWindow.AppendSystem("persona base <w0|c0|f0|p0|g0> <float -1..1>"); return;
                }
                Config.Save();
                ChatWindow.AppendSystem($"Baseline updated: {which} = {v:0.00}");
                return;
            }

            if (sub == "reset")
            {
                _persona = new PersonalityService(Config, Log);
                _persona.SaveToConfig();
                ChatWindow.AppendSystem("Persona reset to baselines.");
                return;
            }

            ChatWindow.AppendSystem("persona | persona auto <true|false> | persona decay <float/day> | persona set <w|c|f|p|g> <val> | persona base <w0|c0|f0|p0|g0> <val> | persona reset");
        }

        // ========================= Utilities =========================

        private static List<string> SplitArgs(string s)
        {
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
                var u = Nullable.GetUnderlyingType(target);
                if (u != null)
                {
                    if (string.Equals(text, "null", StringComparison.OrdinalIgnoreCase))
                    { boxed = null; error = ""; return true; }
                    if (TryConvert(text, u, out var inner, out error))
                    { boxed = inner; return true; }
                    boxed = null; return false;
                }
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
            if (key.StartsWith("Listen", StringComparison.OrdinalIgnoreCase)
                || key.Equals(nameof(Configuration.RequireCallsign), StringComparison.OrdinalIgnoreCase)
                || key.Equals(nameof(Configuration.Callsign), StringComparison.OrdinalIgnoreCase)
                || key.Equals(nameof(Configuration.DebugListen), StringComparison.OrdinalIgnoreCase))
            {
                RehookListener();
                ChatWindow.AppendSystem("[listener] rehooked.");
            }

            if (key.Equals(nameof(Configuration.IpcChannelName), StringComparison.OrdinalIgnoreCase))
            {
                try { _ipcRelay?.Dispose(); } catch { }
                _ipcRelay = new IpcChatRelay(PluginInterface, Log);

                var name = Config.IpcChannelName;
                if (!string.IsNullOrWhiteSpace(name))
                {
                    var ok = _ipcRelay.Bind(name!);
                    ChatWindow.AppendSystem(ok ? $"[ipc] bound to '{name}'" : $"[ipc] failed to bind '{name}'");
                }

                _broadcaster?.SetIpcRelay(_ipcRelay, Config.PreferIpcRelay);
                return;
            }

            if (key.Equals(nameof(Configuration.PreferIpcRelay), StringComparison.OrdinalIgnoreCase))
            {
                _broadcaster?.SetIpcRelay(_ipcRelay, Config.PreferIpcRelay);
                ChatWindow.AppendSystem($"[ipc] prefer relay = {Config.PreferIpcRelay}");
            }

            if (key.Equals(nameof(Configuration.EchoChannel), StringComparison.OrdinalIgnoreCase))
            {
                _echoChannel = ParseChannel(Config.EchoChannel);
                ChatWindow.AppendSystem($"[echo] channel set to {Config.EchoChannel}");
                return;
            }

            if (key.StartsWith("Dreaming", StringComparison.OrdinalIgnoreCase))
            {
                InitializeDreaming();
            }

            if (key.Equals(nameof(Configuration.StartOpen), StringComparison.OrdinalIgnoreCase) && after is bool b)
                ChatWindow.IsOpen = b;
        }

        // ===== Aliases map =====
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

            // Affinity
            d.Add("affinity", nameof(Configuration.AffinityEnabled));
            d.Add("bond.decay", nameof(Configuration.AffinityDecayPerDay));
            d.Add("bond.pos", nameof(Configuration.AffinityPositiveWeight));
            d.Add("bond.neg", nameof(Configuration.AffinityNegativeWeight));
            d.Add("bond.auto", nameof(Configuration.AffinityAutoClassify));
            d.Add("bond.interact", nameof(Configuration.AffinityPerInteraction));
            d.Add("bond.assist", nameof(Configuration.AffinityPerAssistantReply));

            // Persona (Adaptive Gradients)
            d.Add("persona.auto", nameof(Configuration.PersonaAutoEnabled));
            d.Add("persona.decay", nameof(Configuration.PersonaBaselineDecayPerDay));
            d.Add("persona.w", nameof(Configuration.PersonaWarmth));
            d.Add("persona.c", nameof(Configuration.PersonaCuriosity));
            d.Add("persona.f", nameof(Configuration.PersonaFormality));
            d.Add("persona.p", nameof(Configuration.PersonaPlayfulness));
            d.Add("persona.g", nameof(Configuration.PersonaGravitas));
            d.Add("persona.w0", nameof(Configuration.PersonaBaselineWarmth));
            d.Add("persona.c0", nameof(Configuration.PersonaBaselineCuriosity));
            d.Add("persona.f0", nameof(Configuration.PersonaBaselineFormality));
            d.Add("persona.p0", nameof(Configuration.PersonaBaselinePlayfulness));
            d.Add("persona.g0", nameof(Configuration.PersonaBaselineGravitas));

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
                case "alliance": return ChatBroadcaster.NunuChannel.Party; // stable fallback
                case "freecompany":
                case "fc": return ChatBroadcaster.NunuChannel.FreeCompany;
                case "echo": return ChatBroadcaster.NunuChannel.Echo;
                default: return ChatBroadcaster.NunuChannel.Party;
            }
        }
    }
}
