using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace NunuTheAICompanion.UI
{
    public sealed class ConfigWindow : Window
    {
        private readonly Configuration _cfg;

        public ConfigWindow(Configuration cfg)
            : base("Nunu — Configuration", ImGuiWindowFlags.NoCollapse)
        {
            _cfg = cfg;
            SizeConstraints = new WindowSizeConstraints
            {
                MinimumSize = new Vector2(560, 440),
                MaximumSize = new Vector2(4096, 4096),
            };
        }

        public override bool DrawConditions()
        {
            if (PluginMain.IsShuttingDown) return false;
            return base.DrawConditions();
        }

        public override void Draw()
        {
            if (PluginMain.IsShuttingDown) return;

            if (!ImGui.BeginTabBar("##nunu_cfg_tabs"))
                return;

            if (ImGui.BeginTabItem("Core")) { DrawCore(); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Memory / Threads")) { DrawMemory(); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Songcraft")) { DrawSongcraft(); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Voice")) { DrawVoice(); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Listen")) { DrawListen(); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Broadcast / IPC")) { DrawBroadcast(); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Search")) { DrawSearch(); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Images")) { DrawImages(); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("UI")) { DrawUiPrefs(); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Environment")) { DrawEnvironment(); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Debug")) { DrawDebug(); ImGui.EndTabItem(); }

            ImGui.EndTabBar();
        }

        // ===== Tabs =====

        private void DrawCore()
        {
            ImGui.TextUnformatted("Model Backend");
            ImGui.Separator();

            string backendUrl = _cfg.BackendUrl ?? string.Empty;
            if (ImGui.InputText("Backend URL", ref backendUrl, 1024))
            {
                _cfg.BackendUrl = backendUrl;
                _cfg.Save();
            }

            string model = _cfg.ModelName ?? string.Empty;
            if (ImGui.InputText("Model", ref model, 256))
            {
                _cfg.ModelName = model;
                _cfg.Save();
            }

            float temp = _cfg.Temperature;
            if (ImGui.SliderFloat("Temperature", ref temp, 0f, 2.0f))
            {
                _cfg.Temperature = temp;
                _cfg.Save();
            }

            string sys = _cfg.SystemPrompt ?? string.Empty;
            if (ImGui.InputTextMultiline("System Prompt", ref sys, 4000, new Vector2(-1, 140)))
            {
                _cfg.SystemPrompt = sys;
                _cfg.Save();
            }
        }

        private void DrawMemory()
        {
            ImGui.TextUnformatted("Context & Soul Threads");
            ImGui.Separator();

            int turns = _cfg.ContextTurns;
            if (ImGui.SliderInt("Context Turns", ref turns, 2, 64))
            {
                _cfg.ContextTurns = turns;
                _cfg.Save();
            }

            bool threads = _cfg.SoulThreadsEnabled;
            if (ImGui.Checkbox("Enable Soul Threads", ref threads))
            {
                _cfg.SoulThreadsEnabled = threads;
                _cfg.Save();
            }

            string embed = _cfg.EmbeddingModel ?? string.Empty;
            if (ImGui.InputText("Embedding Model", ref embed, 256))
            {
                _cfg.EmbeddingModel = embed;
                _cfg.Save();
            }

            float thr = _cfg.ThreadSimilarityThreshold;
            if (ImGui.SliderFloat("Thread Similarity Threshold", ref thr, 0f, 1f))
            {
                _cfg.ThreadSimilarityThreshold = thr;
                _cfg.Save();
            }

            int fromThread = _cfg.ThreadContextMaxFromThread;
            if (ImGui.SliderInt("Max from Thread", ref fromThread, 0, 32))
            {
                _cfg.ThreadContextMaxFromThread = fromThread;
                _cfg.Save();
            }

            int maxRecent = _cfg.ThreadContextMaxRecent;
            if (ImGui.SliderInt("Max Recent", ref maxRecent, 0, 32))
            {
                _cfg.ThreadContextMaxRecent = maxRecent;
                _cfg.Save();
            }
        }

        private void DrawSongcraft()
        {
            ImGui.TextUnformatted("Songcraft");
            ImGui.Separator();

            bool enabled = _cfg.SongcraftEnabled;
            if (ImGui.Checkbox("Enable Songcraft", ref enabled))
            {
                _cfg.SongcraftEnabled = enabled;
                _cfg.Save();
            }

            string trig = _cfg.SongcraftBardCallTrigger ?? "/song";
            if (ImGui.InputText("Bard-Call Trigger", ref trig, 64))
            {
                _cfg.SongcraftBardCallTrigger = trig;
                _cfg.Save();
            }

            int bars = _cfg.SongcraftBars;
            if (ImGui.SliderInt("Bars", ref bars, 4, 128))
            {
                _cfg.SongcraftBars = bars;
                _cfg.Save();
            }

            int tempo = _cfg.SongcraftTempoBpm;
            if (ImGui.SliderInt("Tempo BPM", ref tempo, 40, 220))
            {
                _cfg.SongcraftTempoBpm = tempo;
                _cfg.Save();
            }

            int program = _cfg.SongcraftProgram;
            if (ImGui.SliderInt("MIDI Program", ref program, 0, 127))
            {
                _cfg.SongcraftProgram = program;
                _cfg.Save();
            }

            string dir = _cfg.SongcraftSaveDir ?? string.Empty;
            if (ImGui.InputText("Save Directory", ref dir, 512))
            {
                _cfg.SongcraftSaveDir = dir;
                _cfg.Save();
            }
        }

        private void DrawVoice()
        {
            ImGui.TextUnformatted("Voice (TTS)");
            ImGui.Separator();

            bool on = _cfg.VoiceSpeakEnabled;
            if (ImGui.Checkbox("Enable Voice", ref on))
            {
                _cfg.VoiceSpeakEnabled = on;
                _cfg.Save();
            }

            string vname = _cfg.VoiceName ?? string.Empty;
            if (ImGui.InputText("Voice Name", ref vname, 128))
            {
                _cfg.VoiceName = vname;
                _cfg.Save();
            }

            float rate = _cfg.VoiceRate;
            if (ImGui.SliderFloat("Rate", ref rate, 0.25f, 2.0f))
            {
                _cfg.VoiceRate = (int)rate;
                _cfg.Save();
            }

            float vol = _cfg.VoiceVolume;
            if (ImGui.SliderFloat("Volume", ref vol, 0f, 1f))
            {
                _cfg.VoiceVolume = (int)vol;
                _cfg.Save();
            }

            bool focus = _cfg.VoiceOnlyWhenWindowFocused;
            if (ImGui.Checkbox("Only Speak When Window Focused", ref focus))
            {
                _cfg.VoiceOnlyWhenWindowFocused = focus;
                _cfg.Save();
            }
        }

        private void DrawListen()
        {
            ImGui.TextUnformatted("Listen (inbound chat filter)");
            ImGui.Separator();

            void Bool(string label, ref bool v, Action<bool> set)
            {
                if (ImGui.Checkbox(label, ref v)) { set(v); _cfg.Save(); }
            }

            bool listen = _cfg.ListenEnabled; Bool("Enabled", ref listen, v => _cfg.ListenEnabled = v);
            bool self = _cfg.ListenSelf; Bool("Self", ref self, v => _cfg.ListenSelf = v);
            bool say = _cfg.ListenSay; Bool("Say", ref say, v => _cfg.ListenSay = v);
            bool tell = _cfg.ListenTell; Bool("Tell", ref tell, v => _cfg.ListenTell = v);
            bool party = _cfg.ListenParty; Bool("Party", ref party, v => _cfg.ListenParty = v);
            bool alliance = _cfg.ListenAlliance; Bool("Alliance", ref alliance, v => _cfg.ListenAlliance = v);
            bool fc = _cfg.ListenFreeCompany; Bool("Free Company", ref fc, v => _cfg.ListenFreeCompany = v);
            bool shout = _cfg.ListenShout; Bool("Shout", ref shout, v => _cfg.ListenShout = v);
            bool yell = _cfg.ListenYell; Bool("Yell", ref yell, v => _cfg.ListenYell = v);

            string callsign = _cfg.Callsign ?? string.Empty;
            if (ImGui.InputText("Callsign (optional)", ref callsign, 64)) { _cfg.Callsign = callsign; _cfg.Save(); }

            bool req = _cfg.RequireCallsign;
            if (ImGui.Checkbox("Require Callsign", ref req)) { _cfg.RequireCallsign = req; _cfg.Save(); }
        }

        private void DrawBroadcast()
        {
            ImGui.TextUnformatted("Broadcast & IPC");
            ImGui.Separator();

            string echo = _cfg.EchoChannel ?? "party";
            if (ImGui.InputText("Echo Channel (say/party/shout/yell/fc/echo)", ref echo, 32))
            {
                _cfg.EchoChannel = echo;
                _cfg.Save();
            }

            bool persona = _cfg.BroadcastAsPersona;
            if (ImGui.Checkbox("Broadcast as Persona", ref persona))
            {
                _cfg.BroadcastAsPersona = persona;
                _cfg.Save();
            }

            string pname = _cfg.PersonaName ?? string.Empty;
            if (ImGui.InputText("Persona Name", ref pname, 128))
            {
                _cfg.PersonaName = pname;
                _cfg.Save();
            }

            string ipc = _cfg.IpcChannelName ?? string.Empty;
            if (ImGui.InputText("IPC Channel", ref ipc, 128))
            {
                _cfg.IpcChannelName = ipc;
                _cfg.Save();
            }

            bool prefer = _cfg.PreferIpcRelay;
            if (ImGui.Checkbox("Prefer IPC Relay", ref prefer))
            {
                _cfg.PreferIpcRelay = prefer;
                _cfg.Save();
            }
        }

        private void DrawSearch()
        {
            ImGui.TextUnformatted("Search / Web");
            ImGui.Separator();

            bool allow = _cfg.AllowInternet;
            if (ImGui.Checkbox("Allow Internet Access", ref allow)) { _cfg.AllowInternet = allow; _cfg.Save(); }

            string backend = _cfg.SearchBackend ?? string.Empty;
            if (ImGui.InputText("Search Backend", ref backend, 128)) { _cfg.SearchBackend = backend; _cfg.Save(); }

            string key = _cfg.SearchApiKey ?? string.Empty;
            if (ImGui.InputText("Search API Key", ref key, 256)) { _cfg.SearchApiKey = key; _cfg.Save(); }

            int max = _cfg.SearchMaxResults;
            if (ImGui.SliderInt("Max Results", ref max, 1, 20)) { _cfg.SearchMaxResults = max; _cfg.Save(); }

            int tsec = _cfg.SearchTimeoutSec;
            if (ImGui.SliderInt("Timeout (sec)", ref tsec, 5, 120)) { _cfg.SearchTimeoutSec = tsec; _cfg.Save(); }
        }

        private void DrawImages()
        {
            ImGui.TextUnformatted("Images");
            ImGui.Separator();

            string ib = _cfg.ImageBackend ?? string.Empty;
            if (ImGui.InputText("Backend", ref ib, 128)) { _cfg.ImageBackend = ib; _cfg.Save(); }

            string url = _cfg.ImageBaseUrl ?? string.Empty;
            if (ImGui.InputText("Base URL", ref url, 512)) { _cfg.ImageBaseUrl = url; _cfg.Save(); }

            string mdl = _cfg.ImageModel ?? string.Empty;
            if (ImGui.InputText("Model", ref mdl, 128)) { _cfg.ImageModel = mdl; _cfg.Save(); }

            int steps = _cfg.ImageSteps;
            if (ImGui.SliderInt("Steps", ref steps, 1, 150)) { _cfg.ImageSteps = steps; _cfg.Save(); }

            float guidance = _cfg.ImageGuidance;
            if (ImGui.SliderFloat("CFG", ref guidance, 0f, 30f)) { _cfg.ImageGuidance = guidance; _cfg.Save(); }

            int w = _cfg.ImageWidth;
            if (ImGui.SliderInt("Width", ref w, 64, 2048)) { _cfg.ImageWidth = w; _cfg.Save(); }

            int h = _cfg.ImageHeight;
            if (ImGui.SliderInt("Height", ref h, 64, 2048)) { _cfg.ImageHeight = h; _cfg.Save(); }

            string sampler = _cfg.ImageSampler ?? string.Empty;
            if (ImGui.InputText("Sampler", ref sampler, 64)) { _cfg.ImageSampler = sampler; _cfg.Save(); }

            // IMPORTANT: ImageSeed is an int -> use InputInt NOT InputText to avoid ref/flags errors.
            int seed = _cfg.ImageSeed;
            if (ImGui.InputInt("Seed", ref seed)) { _cfg.ImageSeed = seed; _cfg.Save(); }

            int itime = _cfg.ImageTimeoutSec;
            if (ImGui.SliderInt("Timeout (sec)", ref itime, 5, 180)) { _cfg.ImageTimeoutSec = itime; _cfg.Save(); }

            bool save = _cfg.SaveImages;
            if (ImGui.Checkbox("Save Images", ref save)) { _cfg.SaveImages = save; _cfg.Save(); }

            string dir = _cfg.ImageSaveSubdir ?? string.Empty;
            if (ImGui.InputText("Save Subdir", ref dir, 128)) { _cfg.ImageSaveSubdir = dir; _cfg.Save(); }
        }

        private void DrawUiPrefs()
        {
            ImGui.TextUnformatted("Chat Window");
            ImGui.Separator();

            bool start = _cfg.StartOpen;
            if (ImGui.Checkbox("Open on Start", ref start)) { _cfg.StartOpen = start; _cfg.Save(); }

            float op = _cfg.WindowOpacity;
            if (ImGui.SliderFloat("Window Opacity", ref op, 0.3f, 1.0f)) { _cfg.WindowOpacity = op; _cfg.Save(); }

            float fs = _cfg.FontScale;
            if (ImGui.SliderFloat("Font Scale", ref fs, 0.8f, 1.6f)) { _cfg.FontScale = fs; _cfg.Save(); }

            bool ascii = _cfg.AsciiSafe;
            if (ImGui.Checkbox("ASCII Safe Output", ref ascii)) { _cfg.AsciiSafe = ascii; _cfg.Save(); }

            bool two = _cfg.TwoPaneMode;
            if (ImGui.Checkbox("Two Pane Mode", ref two)) { _cfg.TwoPaneMode = two; _cfg.Save(); }

            bool copy = _cfg.ShowCopyButtons;
            if (ImGui.Checkbox("Show Copy Buttons", ref copy)) { _cfg.ShowCopyButtons = copy; _cfg.Save(); }

            bool lockw = _cfg.LockWindow;
            if (ImGui.Checkbox("Lock Window", ref lockw)) { _cfg.LockWindow = lockw; _cfg.Save(); }

            string name = _cfg.ChatDisplayName ?? string.Empty;
            if (ImGui.InputText("Display Name", ref name, 64)) { _cfg.ChatDisplayName = name; _cfg.Save(); }
        }

        private void DrawEnvironment()
        {
            ImGui.TextUnformatted("Environment Awareness");
            ImGui.Separator();
            ImGui.TextWrapped("Environment toggles can also be changed via /nunu set/toggle. This tab intentionally avoids direct sheet bindings to keep compatibility.");
        }

        private void DrawDebug()
        {
            ImGui.TextUnformatted("Debug");
            ImGui.Separator();

            bool dbgListen = _cfg.DebugListen;
            if (ImGui.Checkbox("Debug Listen", ref dbgListen)) { _cfg.DebugListen = dbgListen; _cfg.Save(); }

            bool mirror = _cfg.DebugMirrorToWindow;
            if (ImGui.Checkbox("Mirror Debug to Chat Window", ref mirror)) { _cfg.DebugMirrorToWindow = mirror; _cfg.Save(); }
        }
    }
}
