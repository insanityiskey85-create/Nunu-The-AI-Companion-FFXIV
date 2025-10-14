using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;

namespace NunuTheAICompanion.UI
{
    /// <summary>
    /// Settings window for Little Nunu using Dalamud.Bindings.ImGui (no ImGuiNET).
    /// Covers backend, memory & Soul Threads, Songcraft, voice, listening, broadcast, IPC,
    /// UI/window, images, search, and debug.
    /// </summary>
    public sealed class ConfigWindow : Window
    {
        private readonly Configuration _cfg;

        // Reusable UTF-8 buffers per field label for InputText*
        private readonly Dictionary<string, byte[]> _buffers = new(StringComparer.Ordinal);

        public ConfigWindow(Configuration cfg)
            : base("Nunu — Configuration", ImGuiWindowFlags.None, true)
        {
            _cfg = cfg;

            SizeConstraints = new WindowSizeConstraints
            {
                MinimumSize = new Vector2(540, 440),
                MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
            };

            IsOpen = _cfg.StartOpen;
        }

        public override void Draw()
        {
            if (ImGui.BeginTabBar("nunu_cfg_tabs"))
            {
                if (ImGui.BeginTabItem("Backend")) { DrawBackend(); ImGui.EndTabItem(); }
                if (ImGui.BeginTabItem("Memory")) { DrawMemory(); ImGui.EndTabItem(); }
                if (ImGui.BeginTabItem("Songcraft")) { DrawSongcraft(); ImGui.EndTabItem(); }
                if (ImGui.BeginTabItem("Voice")) { DrawVoice(); ImGui.EndTabItem(); }
                if (ImGui.BeginTabItem("Listen")) { DrawListen(); ImGui.EndTabItem(); }
                if (ImGui.BeginTabItem("Broadcast")) { DrawBroadcastIpc(); ImGui.EndTabItem(); }
                if (ImGui.BeginTabItem("UI")) { DrawUi(); ImGui.EndTabItem(); }
                if (ImGui.BeginTabItem("Images")) { DrawImages(); ImGui.EndTabItem(); }
                if (ImGui.BeginTabItem("Search")) { DrawSearch(); ImGui.EndTabItem(); }
                if (ImGui.BeginTabItem("Debug")) { DrawDebug(); ImGui.EndTabItem(); }
                ImGui.EndTabBar();
            }

            ImGui.Separator();
            if (ImGui.Button("Save & Apply", new Vector2(160, 0)))
            {
                _cfg.Save();
                PluginMain.Instance.RehookListener(); // apply listen changes immediately
            }
            ImGui.SameLine();
            ImGui.TextDisabled("Changes are saved to disk; some settings take effect instantly.");
        }

        // ---------- Tabs ----------

        private void DrawBackend()
        {
            ImGui.Text("Chat Backend");
            InputTextProp("Mode (label)", _cfg.BackendMode, v => _cfg.BackendMode = v);
            InputTextProp("Endpoint URL", _cfg.BackendUrl, v => _cfg.BackendUrl = v);
            InputTextProp("Model", _cfg.ModelName, v => _cfg.ModelName = v);
            SliderFloatProp("Temperature", _cfg.Temperature, v => _cfg.Temperature = v, 0.0f, 2.0f);
            InputTextMultilineProp("System Prompt", _cfg.SystemPrompt, v => _cfg.SystemPrompt = v, new Vector2(-1, 120));

            ImGui.Separator();
            InputTextProp("Chat Display Name", _cfg.ChatDisplayName, v => _cfg.ChatDisplayName = v);
        }

        private void DrawMemory()
        {
            CheckboxProp("Enable Soul Threads (topic-aware memory)", _cfg.SoulThreadsEnabled, v => _cfg.SoulThreadsEnabled = v);
            SliderIntProp("Recent Context (ContextTurns)", _cfg.ContextTurns, v => _cfg.ContextTurns = v, 0, 64);
            InputTextProp("Embedding Model", _cfg.EmbeddingModel ?? string.Empty, v => _cfg.EmbeddingModel = v);
            SliderFloatProp("Thread Similarity Threshold", _cfg.ThreadSimilarityThreshold, v => _cfg.ThreadSimilarityThreshold = v, 0.3f, 0.95f);
            SliderIntProp("Max from Matching Thread", _cfg.ThreadContextMaxFromThread, v => _cfg.ThreadContextMaxFromThread = v, 0, 16);
            SliderIntProp("Max Recent (always include)", _cfg.ThreadContextMaxRecent, v => _cfg.ThreadContextMaxRecent = v, 0, 32);

            ImGui.Separator();
            ImGui.TextWrapped("Soul Threads weaves older, thematically similar memories into the prompt alongside recent turns.");
        }

        private void DrawSongcraft()
        {
            CheckboxProp("Enable Songcraft (MIDI generation)", _cfg.SongcraftEnabled, v => _cfg.SongcraftEnabled = v);
            InputTextProp("Bard-Call Trigger", _cfg.SongcraftBardCallTrigger ?? "/song", v => _cfg.SongcraftBardCallTrigger = v);
            InputTextProp("Key (e.g., C4, D#3)", _cfg.SongcraftKey ?? "C4", v => _cfg.SongcraftKey = v);
            SliderIntProp("Tempo (BPM)", _cfg.SongcraftTempoBpm, v => _cfg.SongcraftTempoBpm = v, 40, 200);
            SliderIntProp("Bars", _cfg.SongcraftBars, v => _cfg.SongcraftBars = v, 2, 64);
            SliderIntProp("Program (GM 0..127)", _cfg.SongcraftProgram, v => _cfg.SongcraftProgram = v, 0, 127);

            var saveDir = _cfg.SongcraftSaveDir ?? string.Empty;
            if (InputTextProp("Save Directory (blank = Memories)", saveDir, v => saveDir = v))
                _cfg.SongcraftSaveDir = string.IsNullOrWhiteSpace(saveDir) ? null : saveDir;

            ImGui.Separator();
            ImGui.TextWrapped("Type '@nunu /song sorrow hush the storm' to compose on demand.");
        }

        private void DrawVoice()
        {
            CheckboxProp("Speak replies (TTS)", _cfg.VoiceSpeakEnabled, v => _cfg.VoiceSpeakEnabled = v);
            InputTextProp("Voice Name (optional)", _cfg.VoiceName ?? string.Empty, v => _cfg.VoiceName = v);
            SliderIntProp("Rate", _cfg.VoiceRate, v => _cfg.VoiceRate = v, -10, 10);
            SliderIntProp("Volume", _cfg.VoiceVolume, v => _cfg.VoiceVolume = v, 0, 100);
            CheckboxProp("Speak only when Chat window focused", _cfg.VoiceOnlyWhenWindowFocused, v => _cfg.VoiceOnlyWhenWindowFocused = v);
        }

        private void DrawListen()
        {
            bool changed = false;
            changed |= CheckboxProp("Enable Listening", _cfg.ListenEnabled, v => _cfg.ListenEnabled = v);
            changed |= CheckboxProp("Listen to Self", _cfg.ListenSelf, v => _cfg.ListenSelf = v);
            changed |= CheckboxProp("Listen: /say", _cfg.ListenSay, v => _cfg.ListenSay = v);
            changed |= CheckboxProp("Listen: /tell", _cfg.ListenTell, v => _cfg.ListenTell = v);
            changed |= CheckboxProp("Listen: /party", _cfg.ListenParty, v => _cfg.ListenParty = v);
            changed |= CheckboxProp("Listen: /alliance", _cfg.ListenAlliance, v => _cfg.ListenAlliance = v);
            changed |= CheckboxProp("Listen: /fc", _cfg.ListenFreeCompany, v => _cfg.ListenFreeCompany = v);
            changed |= CheckboxProp("Listen: /shout", _cfg.ListenShout, v => _cfg.ListenShout = v);
            changed |= CheckboxProp("Listen: /yell", _cfg.ListenYell, v => _cfg.ListenYell = v);

            ImGui.Separator();
            changed |= CheckboxProp("Require Callsign in message", _cfg.RequireCallsign, v => _cfg.RequireCallsign = v);
            InputTextProp("Callsign (e.g., @nunu)", _cfg.Callsign ?? "@nunu", v => _cfg.Callsign = v);
            ImGui.TextDisabled("When required, Nunu only responds if the message contains this callsign.");

            if (changed)
                PluginMain.Instance.RehookListener();
        }

        private void DrawBroadcastIpc()
        {
            ImGui.Text("Broadcast");
            CheckboxProp("Prefix outgoing lines with persona", _cfg.BroadcastAsPersona, v => _cfg.BroadcastAsPersona = v);
            InputTextProp("Persona Name", _cfg.PersonaName ?? "Little Nunu", v => _cfg.PersonaName = v);

            ImGui.Separator();
            ImGui.Text("IPC Relay");
            InputTextProp("Channel Name", _cfg.IpcChannelName ?? string.Empty, v => _cfg.IpcChannelName = v);
            CheckboxProp("Prefer IPC Relay (when bound)", _cfg.PreferIpcRelay, v => _cfg.PreferIpcRelay = v);

            ImGui.Separator();
            if (ImGui.Button("Rebind IPC"))
                PluginMain.Instance.RehookListener();
        }

        private void DrawUi()
        {
            CheckboxProp("Open Chat Window on start", _cfg.StartOpen, v => _cfg.StartOpen = v);
            SliderFloatProp("Window Opacity", _cfg.WindowOpacity, v => _cfg.WindowOpacity = v, 0.25f, 1.0f);
            CheckboxProp("ASCII-safe broadcast (strip non-ASCII)", _cfg.AsciiSafe, v => _cfg.AsciiSafe = v);
            CheckboxProp("Two-pane mode", _cfg.TwoPaneMode, v => _cfg.TwoPaneMode = v);
            CheckboxProp("Show copy buttons", _cfg.ShowCopyButtons, v => _cfg.ShowCopyButtons = v);
            SliderFloatProp("Font Scale", _cfg.FontScale, v => _cfg.FontScale = v, 0.75f, 1.5f);
            CheckboxProp("Lock Chat Window", _cfg.LockWindow, v => _cfg.LockWindow = v);
        }

        private void DrawImages()
        {
            InputTextProp("Image Backend (e.g., none, a1111, comfy)", _cfg.ImageBackend ?? "none", v => _cfg.ImageBackend = v);
            InputTextProp("Base URL", _cfg.ImageBaseUrl ?? "http://127.0.0.1:7860", v => _cfg.ImageBaseUrl = v);
            InputTextProp("Model", _cfg.ImageModel ?? "stable-diffusion-1.5", v => _cfg.ImageModel = v);
            SliderIntProp("Steps", _cfg.ImageSteps, v => _cfg.ImageSteps = v, 1, 150);
            SliderFloatProp("Guidance (CFG)", _cfg.ImageGuidance, v => _cfg.ImageGuidance = v, 1.0f, 20.0f);
            SliderIntProp("Width", _cfg.ImageWidth, v => _cfg.ImageWidth = v, 64, 2048);
            SliderIntProp("Height", _cfg.ImageHeight, v => _cfg.ImageHeight = v, 64, 2048);
            InputTextProp("Sampler", _cfg.ImageSampler ?? "Euler a", v => _cfg.ImageSampler = v);
            SliderIntProp("Seed (-1=random)", _cfg.ImageSeed, v => _cfg.ImageSeed = v, -1, int.MaxValue);
            SliderIntProp("Request Timeout (s)", _cfg.ImageTimeoutSec, v => _cfg.ImageTimeoutSec = v, 5, 300);
            CheckboxProp("Save Images", _cfg.SaveImages, v => _cfg.SaveImages = v);
            InputTextProp("Save Subfolder", _cfg.ImageSaveSubdir ?? "Images", v => _cfg.ImageSaveSubdir = v);
        }

        private void DrawSearch()
        {
            CheckboxProp("Allow Internet Access", _cfg.AllowInternet, v => _cfg.AllowInternet = v);
            InputTextProp("Search Backend", _cfg.SearchBackend ?? "serpapi", v => _cfg.SearchBackend = v);
            InputTextProp("Search API Key", _cfg.SearchApiKey ?? string.Empty, v => _cfg.SearchApiKey = v);
            SliderIntProp("Max Results", _cfg.SearchMaxResults, v => _cfg.SearchMaxResults = v, 1, 50);
            SliderIntProp("Timeout (s)", _cfg.SearchTimeoutSec, v => _cfg.SearchTimeoutSec = v, 5, 120);
        }

        private void DrawDebug()
        {
            CheckboxProp("Mirror listener debug to Chat window", _cfg.DebugMirrorToWindow, v => _cfg.DebugMirrorToWindow = v);
            CheckboxProp("Verbose listen debug", _cfg.DebugListen, v => _cfg.DebugListen = v);
            ImGui.Separator();
            if (ImGui.Button("Rehook Listener Now"))
                PluginMain.Instance.RehookListener();
        }

        // ---------- Helper wrappers (Dalamud.Bindings.ImGui with UTF-8 buffers) ----------

        private bool InputTextProp(string label, string value, Action<string> setter, int maxBytes = 2048)
        {
            var buf = GetBuffer(label, value, maxBytes);
            var changed = ImGui.InputText(label, buf, ImGuiInputTextFlags.None);
            if (changed)
            {
                var text = BytesToString(buf);
                setter(text);
            }
            return changed;
        }

        private bool InputTextMultilineProp(string label, string value, Action<string> setter, Vector2 size, int maxBytes = 4096)
        {
            var buf = GetBuffer(label, value, maxBytes);
            var changed = ImGui.InputTextMultiline(label, buf, size, ImGuiInputTextFlags.None);
            if (changed)
            {
                var text = BytesToString(buf);
                setter(text);
            }
            return changed;
        }

        private static bool CheckboxProp(string label, bool value, Action<bool> setter)
        {
            var v = value;
            var changed = ImGui.Checkbox(label, ref v);
            if (changed) setter(v);
            return changed;
        }

        private static bool SliderIntProp(string label, int value, Action<int> setter, int min, int max)
        {
            var v = value;
            var changed = ImGui.SliderInt(label, ref v, min, max);
            if (changed) setter(v);
            return changed;
        }

        private static bool SliderFloatProp(string label, float value, Action<float> setter, float min, float max)
        {
            var v = value;
            var changed = ImGui.SliderFloat(label, ref v, min, max);
            if (changed) setter(v);
            return changed;
        }

        // ---------- UTF-8 buffer helpers ----------

        private byte[] GetBuffer(string key, string text, int size)
        {
            if (!_buffers.TryGetValue(key, out var buf) || buf.Length != size)
            {
                buf = new byte[size];
                _buffers[key] = buf;
            }

            // write UTF-8 into buffer with proper NUL termination
            Array.Clear(buf, 0, buf.Length);
            if (!string.IsNullOrEmpty(text))
            {
                var maxPayload = size - 1; // last byte reserved for NUL
                var bytes = Encoding.UTF8.GetBytes(text);
                var len = Math.Min(bytes.Length, maxPayload);
                Array.Copy(bytes, buf, len);
                buf[len] = 0; // NUL
            }
            else
            {
                buf[0] = 0;
            }

            return buf;
        }

        private static string BytesToString(byte[] buf)
        {
            // find first NUL
            var len = Array.IndexOf(buf, (byte)0);
            if (len < 0) len = buf.Length;
            return Encoding.UTF8.GetString(buf, 0, len);
        }
    }
}
