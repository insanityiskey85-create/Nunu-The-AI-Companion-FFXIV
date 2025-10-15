#nullable enable
using System;
using System.Numerics;
using System.Text;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace NunuTheAICompanion.UI
{
    public sealed class ConfigWindow : Window
    {
        private static Configuration C => PluginMain.Instance.Configuration;

        // --- text buffers (sizes chosen generously) ---
        private static byte[] _bufName = NewBuf(C.ChatDisplayName ?? "Little Nunu", 256);
        private static byte[] _bufMode = NewBuf(C.BackendMode ?? "ollama", 64);
        private static byte[] _bufUrl = NewBuf(C.BackendUrl ?? "http://127.0.0.1:11434", 256);
        private static byte[] _bufModel = NewBuf(C.ModelName ?? "nunu-8b", 128);
        private static byte[] _bufSys = NewBuf(C.SystemPrompt ?? string.Empty, 4096);
        private static byte[] _bufCall = NewBuf(C.Callsign ?? "@nunu", 64);
        private static byte[] _bufWl = NewBuf(C.Whitelist ?? string.Empty, 4096);
        private static byte[] _bufVName = NewBuf(C.VoiceName ?? "Female", 128);

        public ConfigWindow()
            : base("Nunu — Configuration", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
        {
            SizeCondition = ImGuiCond.FirstUseEver;
            RespectCloseHotkey = true;
        }

        public ConfigWindow(Configuration config)
        {
        }

        public override void Draw()
        {
            if (ImGui.BeginTabBar("nunu_cfg_tabs"))
            {
                if (ImGui.BeginTabItem("General")) { DrawGeneral(); ImGui.EndTabItem(); }
                if (ImGui.BeginTabItem("Chat")) { DrawChat(); ImGui.EndTabItem(); }
                if (ImGui.BeginTabItem("Listen")) { DrawListen(); ImGui.EndTabItem(); }
                if (ImGui.BeginTabItem("Voice")) { DrawVoice(); ImGui.EndTabItem(); }
                ImGui.EndTabBar();
            }
        }

        private static void DrawGeneral()
        {
            var startOpen = C.StartOpen;
            if (ImGui.Checkbox("Open chat window on login", ref startOpen))
            {
                if (startOpen != C.StartOpen) { C.StartOpen = startOpen; C.Save(); }
            }

            var opacity = C.WindowOpacity;
            if (ImGui.SliderFloat("Window opacity", ref opacity, 0.3f, 1.0f, "%.2f"))
            {
                if (Math.Abs(opacity - C.WindowOpacity) > 0.0001f) { C.WindowOpacity = opacity; C.Save(); }
            }

            var ascii = C.AsciiSafe;
            if (ImGui.Checkbox("ASCII-safe rendering", ref ascii))
            {
                if (ascii != C.AsciiSafe) { C.AsciiSafe = ascii; C.Save(); }
            }
        }

        private static void DrawChat()
        {
            ImGui.TextUnformatted("Assistant display name");
            ImGui.PushID("dispname");
            if (ImGui.InputTextMultiline("##disp",
                _bufName, (uint)_bufName.Length,
                new Vector2(-1, ImGui.GetTextLineHeight() * 2)))
            {
                SyncIfChanged(_bufName, ref C.ChatDisplayName);
            }
            ImGui.PopID();

            ImGui.Separator();
            ImGui.TextDisabled("Backend");

            ImGui.TextUnformatted("Mode (ollama/openai/http)");
            ImGui.PushID("backmode");
            if (ImGui.InputTextMultiline("##mode",
                _bufMode, (uint)_bufMode.Length,
                new Vector2(-1, ImGui.GetTextLineHeight() * 2)))
            {
                SyncIfChanged(_bufMode, ref C.BackendMode);
            }
            ImGui.PopID();

            ImGui.TextUnformatted("Backend URL");
            ImGui.PushID("backurl");
            if (ImGui.InputTextMultiline("##url",
                _bufUrl, (uint)_bufUrl.Length,
                new Vector2(-1, ImGui.GetTextLineHeight() * 2)))
            {
                SyncIfChanged(_bufUrl, ref C.BackendUrl);
            }
            ImGui.PopID();

            ImGui.TextUnformatted("Model name");
            ImGui.PushID("model");
            if (ImGui.InputTextMultiline("##model",
                _bufModel, (uint)_bufModel.Length,
                new Vector2(-1, ImGui.GetTextLineHeight() * 2)))
            {
                SyncIfChanged(_bufModel, ref C.ModelName);
            }
            ImGui.PopID();

            var temp = C.Temperature;
            if (ImGui.SliderFloat("Temperature", ref temp, 0.0f, 1.5f, "%.2f"))
            {
                if (Math.Abs(temp - C.Temperature) > 0.0001f) { C.Temperature = temp; C.Save(); }
            }

            ImGui.Spacing();
            ImGui.TextDisabled("System Prompt");
            ImGui.PushID("sysprompt");
            if (ImGui.InputTextMultiline("##sysprompt_text",
                _bufSys, (uint)_bufSys.Length,
                new Vector2(-1, 180)))
            {
                SyncIfChanged(_bufSys, ref C.SystemPrompt);
            }
            ImGui.PopID();
        }

        private static void DrawListen()
        {
            var enabled = C.ListenEnabled;
            if (ImGui.Checkbox("Enable listening to game chat", ref enabled))
            {
                if (enabled != C.ListenEnabled) { C.ListenEnabled = enabled; C.Save(); }
            }

            ImGui.Separator();

            var req = C.RequireCallsign;
            if (ImGui.Checkbox("Require callsign", ref req))
            {
                if (req != C.RequireCallsign) { C.RequireCallsign = req; C.Save(); }
            }

            ImGui.TextUnformatted("Callsign (e.g. @nunu)");
            ImGui.PushID("callsign");
            if (ImGui.InputTextMultiline("##callsign_text",
                _bufCall, (uint)_bufCall.Length,
                new Vector2(-1, ImGui.GetTextLineHeight() * 2)))
            {
                SyncIfChanged(_bufCall, ref C.Callsign);
            }
            ImGui.PopID();

            var self = C.ListenSelf;
            if (ImGui.Checkbox("React to my own messages", ref self))
            {
                if (self != C.ListenSelf) { C.ListenSelf = self; C.Save(); }
            }

            ImGui.Spacing();
            ImGui.TextDisabled("Whitelist (one name per line; blank = anyone)");
            ImGui.PushID("whitelist");
            if (ImGui.InputTextMultiline("##wl_text",
                _bufWl, (uint)_bufWl.Length,
                new Vector2(-1, 140)))
            {
                SyncIfChanged(_bufWl, ref C.Whitelist);
            }
            ImGui.PopID();

            ImGui.Separator();
            ImGui.TextDisabled("Channels to listen");

            Toggle("Say", C.ListenSay, v => C.ListenSay = v);
            Toggle("Tell", C.ListenTell, v => C.ListenTell = v);
            Toggle("Party", C.ListenParty, v => C.ListenParty = v);
            Toggle("Alliance", C.ListenAlliance, v => C.ListenAlliance = v);
            Toggle("FreeCompany", C.ListenFreeCompany, v => C.ListenFreeCompany = v);
            Toggle("Shout", C.ListenShout, v => C.ListenShout = v);
            Toggle("Yell", C.ListenYell, v => C.ListenYell = v);

            ImGui.Separator();
            var dbg = C.DebugListen;
            if (ImGui.Checkbox("Debug: log every heard line", ref dbg))
            {
                if (dbg != C.DebugListen) { C.DebugListen = dbg; C.Save(); }
            }
        }

        private static void DrawVoice()
        {
            var speak = C.VoiceSpeakEnabled;
            if (ImGui.Checkbox("Speak assistant replies", ref speak))
            {
                if (speak != C.VoiceSpeakEnabled) { C.VoiceSpeakEnabled = speak; C.Save(); }
            }

            var focus = C.VoiceOnlyWhenWindowFocused;
            if (ImGui.Checkbox("Only speak when chat window focused", ref focus))
            {
                if (focus != C.VoiceOnlyWhenWindowFocused) { C.VoiceOnlyWhenWindowFocused = focus; C.Save(); }
            }

            ImGui.TextUnformatted("Voice name");
            ImGui.PushID("vname");
            if (ImGui.InputTextMultiline("##vname_text",
                _bufVName, (uint)_bufVName.Length,
                new Vector2(-1, ImGui.GetTextLineHeight() * 2)))
            {
                SyncIfChanged(_bufVName, ref C.VoiceName);
            }
            ImGui.PopID();

            var rate = C.VoiceRate;
            if (ImGui.SliderInt("Voice rate (percent)", ref rate, -50, 50))
            {
                if (rate != C.VoiceRate) { C.VoiceRate = rate; C.Save(); }
            }

            var vol = C.VoiceVolume;
            if (ImGui.SliderInt("Voice volume (percent)", ref vol, 0, 200))
            {
                if (vol != C.VoiceVolume) { C.VoiceVolume = vol; C.Save(); }
            }

            ImGui.TextDisabled("Note: your synthesis backend should read these values.");
        }

        // --- helpers ---
        private static byte[] NewBuf(string s, int capacity)
        {
            // Ensure space for null-terminator; ImGui expects zero-terminated buffers.
            var buf = new byte[Math.Max(capacity, 16)];
            var bytes = Encoding.UTF8.GetBytes(s ?? string.Empty);
            var n = Math.Min(bytes.Length, buf.Length - 1);
            Array.Copy(bytes, buf, n);
            buf[n] = 0;
            return buf;
        }

        private static string FromBuf(byte[] buf)
        {
            var len = Array.IndexOf<byte>(buf, 0);
            if (len < 0) len = buf.Length;
            return Encoding.UTF8.GetString(buf, 0, len);
        }

        private static void SyncIfChanged(byte[] buf, ref string? target)
        {
            var s = FromBuf(buf);
            if (!string.Equals(s, target, StringComparison.Ordinal))
            {
                target = s;
                C.Save();
            }
        }

        private static void Toggle(string label, bool value, Action<bool> setter)
        {
            var v = value;
            if (ImGui.Checkbox(label, ref v) && v != value)
            {
                setter(v);
                C.Save();
            }
        }
    }
}
