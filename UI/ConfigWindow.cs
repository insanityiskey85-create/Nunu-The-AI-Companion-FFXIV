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

        public override void Draw()
        {
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

        // ================= Core =================
        private void DrawCore()
        {
            ImGui.TextUnformatted("Backend");
            ImGui.Separator();

            if (InputTextRef("Backend URL", _cfg.BackendUrl ?? string.Empty, out var backendNext))
            { _cfg.BackendUrl = backendNext; _cfg.Save(); }

            if (InputTextRef("Model Name", _cfg.ModelName ?? string.Empty, out var modelNext))
            { _cfg.ModelName = modelNext; _cfg.Save(); }

            float temp = _cfg.Temperature;
            if (ImGui.SliderFloat("Temperature", ref temp, 0f, 2f))
            { _cfg.Temperature = temp; _cfg.Save(); }

            if (InputTextMultilineRef("System Prompt", _cfg.SystemPrompt ?? string.Empty, 6, out var sysNext))
            { _cfg.SystemPrompt = sysNext; _cfg.Save(); }

            if (InputTextRef("Display Name", _cfg.ChatDisplayName ?? string.Empty, out var dnNext))
            { _cfg.ChatDisplayName = dnNext; _cfg.Save(); }
        }

        // ============ Memory / Threads ============
        private void DrawMemory()
        {
            ImGui.TextUnformatted("Context & Soul Threads");
            ImGui.Separator();

            IntSlider("Recent Context Turns", _cfg.ContextTurns, 0, 64, v => _cfg.ContextTurns = v);
            BoolCheckbox("Enable Soul Threads", _cfg.SoulThreadsEnabled, v => _cfg.SoulThreadsEnabled = v);

            if (InputTextRef("Embedding Model", _cfg.EmbeddingModel ?? string.Empty, out var embedNext))
            { _cfg.EmbeddingModel = embedNext; _cfg.Save(); }

            FloatSlider("Thread Similarity Threshold", _cfg.ThreadSimilarityThreshold, 0.0f, 1.0f, v => _cfg.ThreadSimilarityThreshold = v);
            IntSlider("Max Lines From Thread", _cfg.ThreadContextMaxFromThread, 0, 64, v => _cfg.ThreadContextMaxFromThread = v);
            IntSlider("Max Recent Lines", _cfg.ThreadContextMaxRecent, 0, 64, v => _cfg.ThreadContextMaxRecent = v);
        }

        // ================= Songcraft =================
        private void DrawSongcraft()
        {
            ImGui.TextUnformatted("Songcraft");
            ImGui.Separator();

            BoolCheckbox("Enable Songcraft", _cfg.SongcraftEnabled, v => _cfg.SongcraftEnabled = v);

            if (InputTextRef("Bard-Call Trigger", _cfg.SongcraftBardCallTrigger ?? "/song", out var trigNext))
            { _cfg.SongcraftBardCallTrigger = trigNext; _cfg.Save(); }

            if (InputTextRef("Save Directory", _cfg.SongcraftSaveDir ?? string.Empty, out var dirNext))
            { _cfg.SongcraftSaveDir = dirNext; _cfg.Save(); }

            // Search: numeric sliders must use ints (no ?? "string")
            int maxResults = _cfg.SearchMaxResults;
            if (ImGui.SliderInt("Max Results", ref maxResults, 1, 20))
            {
                _cfg.SearchMaxResults = maxResults;
                _cfg.Save();
            }

            int timeout = _cfg.SearchTimeoutSec;
            if (ImGui.SliderInt("Timeout (sec)", ref timeout, 5, 120))
            {
                _cfg.SearchTimeoutSec = timeout;
                _cfg.Save();
            }
        }


        // ================= Voice =================
        private void DrawVoice()
        {
            ImGui.TextUnformatted("Voice (TTS)");
            ImGui.Separator();

            BoolCheckbox("Speak replies", _cfg.VoiceSpeakEnabled, v => _cfg.VoiceSpeakEnabled = v);

            if (InputTextRef("Voice Name", _cfg.VoiceName ?? string.Empty, out var voiceNext))
            { _cfg.VoiceName = voiceNext; _cfg.Save(); }

            FloatSlider("Rate", _cfg.VoiceRate, 0.25f, 2.0f, v => _cfg.VoiceRate = (int)v);
            FloatSlider("Volume", _cfg.VoiceVolume, 0.0f, 1.0f, v => _cfg.VoiceVolume = (int)v);

            BoolCheckbox("Only when chat window focused", _cfg.VoiceOnlyWhenWindowFocused, v => _cfg.VoiceOnlyWhenWindowFocused = v);
        }

        // ================= Listen =================
        private void DrawListen()
        {
            ImGui.TextUnformatted("Listening");
            ImGui.Separator();

            BoolCheckbox("Enable inbound listening", _cfg.ListenEnabled, v => _cfg.ListenEnabled = v);

            if (InputTextRef("Callsign (e.g. nunu:)", _cfg.Callsign ?? string.Empty, out var csNext))
            { _cfg.Callsign = csNext; _cfg.Save(); }

            BoolCheckbox("Require callsign", _cfg.RequireCallsign, v => _cfg.RequireCallsign = v);

            ImGui.Separator();

            BoolCheckbox("Self", _cfg.ListenSelf, v => _cfg.ListenSelf = v);
            BoolCheckbox("Say", _cfg.ListenSay, v => _cfg.ListenSay = v);
            BoolCheckbox("Tell", _cfg.ListenTell, v => _cfg.ListenTell = v);
            BoolCheckbox("Party", _cfg.ListenParty, v => _cfg.ListenParty = v);
            BoolCheckbox("Alliance", _cfg.ListenAlliance, v => _cfg.ListenAlliance = v);
            BoolCheckbox("Free Company", _cfg.ListenFreeCompany, v => _cfg.ListenFreeCompany = v);
            BoolCheckbox("Shout", _cfg.ListenShout, v => _cfg.ListenShout = v);
            BoolCheckbox("Yell", _cfg.ListenYell, v => _cfg.ListenYell = v);
        }

        // ================= Broadcast / IPC =================
        private void DrawBroadcast()
        {
            ImGui.TextUnformatted("Broadcast & IPC");
            ImGui.Separator();

            BoolCheckbox("Broadcast as Persona Name", _cfg.BroadcastAsPersona, v => _cfg.BroadcastAsPersona = v);

            if (InputTextRef("Persona Name", _cfg.PersonaName ?? "Nunu", out var pnameNext))
            { _cfg.PersonaName = pnameNext; _cfg.Save(); }

            if (InputTextRef("Echo Channel (say/party/shout/yell/fc/echo)", _cfg.EchoChannel ?? "party", out var echoNext))
            { _cfg.EchoChannel = echoNext; _cfg.Save(); }

            ImGui.Separator();

            if (InputTextRef("IPC Channel", _cfg.IpcChannelName ?? string.Empty, out var ipcNext))
            { _cfg.IpcChannelName = ipcNext; _cfg.Save(); }

            BoolCheckbox("Prefer IPC relay over native send", _cfg.PreferIpcRelay, v => _cfg.PreferIpcRelay = v);
        }

        // ================= Search =================
        private void DrawSearch()
        {
            ImGui.TextUnformatted("Search / Web");
            ImGui.Separator();

            BoolCheckbox("Allow Internet", _cfg.AllowInternet, v => _cfg.AllowInternet = v);

            if (InputTextRef("Search Backend", _cfg.SearchBackend ?? string.Empty, out var backendNext))
            { _cfg.SearchBackend = backendNext; _cfg.Save(); }

            if (InputTextRef("Search API Key", _cfg.SearchApiKey ?? string.Empty, out var keyNext))
            { _cfg.SearchApiKey = keyNext; _cfg.Save(); }

            int maxResults = _cfg.SearchMaxResults;
            if (ImGui.SliderInt("Max Results", ref maxResults, 1, 20))
            { _cfg.SearchMaxResults = maxResults; _cfg.Save(); }

            int timeout = _cfg.SearchTimeoutSec;
            if (ImGui.SliderInt("Timeout (sec)", ref timeout, 5, 120))
            { _cfg.SearchTimeoutSec = timeout; _cfg.Save(); }
        }

        // ================= Images =================
        private void DrawImages()
        {
            ImGui.TextUnformatted("Images");
            ImGui.Separator();

            if (InputTextRef("Image Backend", _cfg.ImageBackend ?? string.Empty, out var ibNext))
            { _cfg.ImageBackend = ibNext; _cfg.Save(); }

            if (InputTextRef("Image Base URL", _cfg.ImageBaseUrl ?? string.Empty, out var urlNext))
            { _cfg.ImageBaseUrl = urlNext; _cfg.Save(); }

            if (InputTextRef("Image Model", _cfg.ImageModel ?? string.Empty, out var modelNext))
            { _cfg.ImageModel = modelNext; _cfg.Save(); }

            int steps = _cfg.ImageSteps;
            if (ImGui.SliderInt("Steps", ref steps, 1, 200))
            { _cfg.ImageSteps = steps; _cfg.Save(); }

            FloatSlider("CFG", _cfg.ImageGuidance, 0.0f, 30.0f, v => _cfg.ImageGuidance = v);

            int w = _cfg.ImageWidth;
            if (ImGui.SliderInt("Width", ref w, 64, 2048))
            { _cfg.ImageWidth = w; _cfg.Save(); }

            int h = _cfg.ImageHeight;
            if (ImGui.SliderInt("Height", ref h, 64, 2048))
            { _cfg.ImageHeight = h; _cfg.Save(); }

            if (InputTextRef("Sampler", _cfg.ImageSampler ?? string.Empty, out var samplerNext))
            { _cfg.ImageSampler = samplerNext; _cfg.Save(); }

            int seedLocal = _cfg.ImageSeed;
            if (ImGui.InputInt("Seed", ref seedLocal))
            { _cfg.ImageSeed = seedLocal; _cfg.Save(); }

            int itime = _cfg.ImageTimeoutSec;
            if (ImGui.SliderInt("Timeout (sec)", ref itime, 5, 300))
            { _cfg.ImageTimeoutSec = itime; _cfg.Save(); }

            BoolCheckbox("Save images to disk", _cfg.SaveImages, v => _cfg.SaveImages = v);

            if (InputTextRef("Save Subdir", _cfg.ImageSaveSubdir ?? "images", out var subdirNext))
            { _cfg.ImageSaveSubdir = subdirNext; _cfg.Save(); }
        }

        // ================= UI prefs =================
        private void DrawUiPrefs()
        {
            ImGui.TextUnformatted("Window & Layout");
            ImGui.Separator();

            BoolCheckbox("Open chat on login", _cfg.StartOpen, v => _cfg.StartOpen = v);
            FloatSlider("Window Opacity", _cfg.WindowOpacity, 0.3f, 1.0f, v => _cfg.WindowOpacity = v);
            BoolCheckbox("ASCII-safe output", _cfg.AsciiSafe, v => _cfg.AsciiSafe = v);
            BoolCheckbox("Two-pane chat", _cfg.TwoPaneMode, v => _cfg.TwoPaneMode = v);
            BoolCheckbox("Show copy buttons", _cfg.ShowCopyButtons, v => _cfg.ShowCopyButtons = v);
            FloatSlider("Font Scale", _cfg.FontScale, 0.8f, 1.6f, v => _cfg.FontScale = v);
            BoolCheckbox("Lock window position", _cfg.LockWindow, v => _cfg.LockWindow = v);
        }

        // ================= Environment =================
        private void DrawEnvironment()
        {
            ImGui.TextUnformatted("Environment Awareness");
            ImGui.Separator();

            bool enabled = _cfg.EnvironmentEnabled;
            if (ImGui.Checkbox("Enable", ref enabled))
            { _cfg.EnvironmentEnabled = enabled; _cfg.Save(); }

            int tick = _cfg.EnvTickSeconds;
            if (ImGui.SliderInt("Tick (sec)", ref tick, 1, 10))
            { _cfg.EnvTickSeconds = tick; _cfg.Save(); }

            BoolCheckbox("Announce zone/duty changes", _cfg.EnvAnnounceOnChange, v => _cfg.EnvAnnounceOnChange = v);

            ImGui.Separator();

            BoolCheckbox("Include Zone", _cfg.EnvIncludeZone, v => _cfg.EnvIncludeZone = v);
            BoolCheckbox("Include Time", _cfg.EnvIncludeTime, v => _cfg.EnvIncludeTime = v);
            BoolCheckbox("Include Duty/Combat", _cfg.EnvIncludeDuty, v => _cfg.EnvIncludeDuty = v);
            BoolCheckbox("Include Coords", _cfg.EnvIncludeCoords, v => _cfg.EnvIncludeCoords = v);
        }

        // ================= Debug =================
        private void DrawDebug()
        {
            ImGui.TextUnformatted("Debug");
            ImGui.Separator();

            BoolCheckbox("Mirror listener to window", _cfg.DebugMirrorToWindow, v => _cfg.DebugMirrorToWindow = v);
            BoolCheckbox("Debug listen log", _cfg.DebugListen, v => _cfg.DebugListen = v);
        }

        // ================= Helpers (non-generic, ref-string, int maxLen) =================

        private static bool InputTextRef(string label, string current, out string result, int max = 512)
        {
            var buf = current ?? string.Empty;
            var changed = ImGui.InputText(label, ref buf, max, ImGuiInputTextFlags.None);
            result = buf ?? string.Empty;
            return changed;
        }

        private static bool InputTextMultilineRef(string label, string current, int lines, out string result, int max = 4096)
        {
            var buf = current ?? string.Empty;
            var size = new Vector2(-1f, lines * ImGui.GetTextLineHeightWithSpacing() + 8f);
            var changed = ImGui.InputTextMultiline(label, ref buf, max, size, ImGuiInputTextFlags.None);
            result = buf ?? string.Empty;
            return changed;
        }

        private void BoolCheckbox(string label, bool current, System.Action<bool> setter)
        {
            var v = current;
            if (ImGui.Checkbox(label, ref v)) { setter(v); _cfg.Save(); }
        }

        private void IntSlider(string label, int current, int min, int max, System.Action<int> setter)
        {
            int v = current;
            if (ImGui.SliderInt(label, ref v, min, max)) { setter(v); _cfg.Save(); }
        }

        private void FloatSlider(string label, float current, float min, float max, System.Action<float> setter)
        {
            float v = current;
            if (ImGui.SliderFloat(label, ref v, min, max)) { setter(v); _cfg.Save(); }
        }
    }
}
