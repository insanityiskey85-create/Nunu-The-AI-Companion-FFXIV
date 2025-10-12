using System;
using System.Numerics;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using NunuTheAICompanion.Services;

namespace NunuTheAICompanion.UI;

public sealed class ConfigWindow : Window
{
    private readonly Configuration _c;

    public ConfigWindow(Configuration config)
        : base("Nunu – Configuration", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        _c = config;
        Size = new Vector2(720, 640);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        ImGui.PushItemWidth(260);

        if (ImGui.BeginTabBar("nunu_cfg_tabs"))
        {
            if (ImGui.BeginTabItem("Backend")) { SectionBackend(); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Listening")) { SectionListening(); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Broadcast")) { SectionBroadcast(); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Web Search")) { SectionSearch(); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("IPC")) { SectionIpc(); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("UI")) { SectionUi(); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Images")) { SectionImages(); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Voice")) { SectionVoice(); ImGui.EndTabItem(); }
        }

        ImGui.Separator();
        if (ImGui.Button("Save All##nunu_cfg")) _c.Save();
        ImGui.SameLine();
        if (ImGui.Button("Rehook Listener")) PluginMain.Instance.RehookListener();
        ImGui.SameLine();
        if (ImGui.Button("Close")) IsOpen = false;

        ImGui.PopItemWidth();
    }

    private void SectionBackend()
    {
        ImGui.TextUnformatted("Chat Backend (LLM)");
        ImGui.Separator();

        string[] modes = { "ollama", "openai", "proxy", "custom" };
        int sel = Array.IndexOf(modes, _c.BackendMode);
        if (sel < 0) sel = 0;
        if (ImGui.Combo("Mode", ref sel, modes, modes.Length))
        { _c.BackendMode = modes[sel]; _c.Save(); }

        { var tmp = _c.BackendUrl ?? string.Empty; if (ImGui.InputText("Backend URL", ref tmp, 512)) { _c.BackendUrl = tmp; _c.Save(); } }
        { var tmp = _c.ModelName ?? string.Empty; if (ImGui.InputText("Model Name", ref tmp, 128)) { _c.ModelName = tmp; _c.Save(); } }

        { float t = _c.Temperature; if (ImGui.SliderFloat("Temperature", ref t, 0.0f, 1.5f, "%.2f")) { _c.Temperature = Math.Clamp(t, 0f, 2f); _c.Save(); } }

        ImGui.TextUnformatted("System Prompt");
        { var tmp = _c.SystemPrompt ?? string.Empty; if (ImGui.InputTextMultiline("##systemPrompt", ref tmp, 4000, new Vector2(-1, 120))) { _c.SystemPrompt = tmp; _c.Save(); } }
    }

    private void SectionListening()
    {
        ImGui.TextUnformatted("Listen to Game Chat");
        ImGui.Separator();

        { bool b = _c.ListenEnabled; if (ImGui.Checkbox("Enable listening", ref b)) { _c.ListenEnabled = b; _c.Save(); } }
        ImGui.SameLine(); Help("(If off, Nunu only answers in her own window)");

        { bool b = _c.RequireCallsign; if (ImGui.Checkbox("Require callsign", ref b)) { _c.RequireCallsign = b; _c.Save(); } }
        ImGui.SameLine();
        ImGui.SetNextItemWidth(160);
        { var tmp = _c.Callsign ?? string.Empty; if (ImGui.InputText("Callsign", ref tmp, 64)) { _c.Callsign = tmp; _c.Save(); } }

        { bool b = _c.ListenSelf; if (ImGui.Checkbox("Listen to my own messages", ref b)) { _c.ListenSelf = b; _c.Save(); } }

        ImGui.Separator();
        ImGui.TextUnformatted("Channels:");
        { bool b = _c.ListenSay; if (ImGui.Checkbox("Say", ref b)) { _c.ListenSay = b; _c.Save(); } }
        ImGui.SameLine();
        { bool b = _c.ListenTell; if (ImGui.Checkbox("Tell", ref b)) { _c.ListenTell = b; _c.Save(); } }
        ImGui.SameLine();
        { bool b = _c.ListenParty; if (ImGui.Checkbox("Party", ref b)) { _c.ListenParty = b; _c.Save(); } }
        ImGui.SameLine();
        { bool b = _c.ListenAlliance; if (ImGui.Checkbox("Alliance", ref b)) { _c.ListenAlliance = b; _c.Save(); } }
        ImGui.SameLine();
        { bool b = _c.ListenFreeCompany; if (ImGui.Checkbox("Free Company", ref b)) { _c.ListenFreeCompany = b; _c.Save(); } }
        ImGui.SameLine();
        { bool b = _c.ListenShout; if (ImGui.Checkbox("Shout", ref b)) { _c.ListenShout = b; _c.Save(); } }
        ImGui.SameLine();
        { bool b = _c.ListenYell; if (ImGui.Checkbox("Yell", ref b)) { _c.ListenYell = b; _c.Save(); } }

        ImGui.Separator();
        { bool b = _c.DebugListen; if (ImGui.Checkbox("Mirror heard lines to Nunu window (debug)", ref b)) { _c.DebugListen = b; _c.Save(); } }
    }

    private void SectionBroadcast()
    {
        ImGui.TextUnformatted("Broadcast to Real Chat");
        ImGui.Separator();

        { bool b = _c.BroadcastAsPersona; if (ImGui.Checkbox("Prefix outgoing lines with persona name", ref b)) { _c.BroadcastAsPersona = b; _c.Save(); } }
        { var tmp = _c.PersonaName ?? string.Empty; if (ImGui.InputText("Persona Name", ref tmp, 128)) { _c.PersonaName = tmp; _c.Save(); } }

        ImGui.Spacing();
        ImGui.TextDisabled("Tip: /nunuspeak on  &  /nunuchan say|party|fc|shout|yell");
    }

    private void SectionSearch()
    {
        ImGui.TextUnformatted("Web Search Tool");
        ImGui.Separator();

        string[] engines = { "serpapi", "none" };
        int sel = Array.IndexOf(engines, _c.SearchBackend);
        if (sel < 0) sel = 0;
        if (ImGui.Combo("Backend", ref sel, engines, engines.Length))
        { _c.SearchBackend = engines[sel]; _c.Save(); }

        { bool b = _c.AllowInternet; if (ImGui.Checkbox("Allow internet lookups", ref b)) { _c.AllowInternet = b; _c.Save(); } }

        { var tmp = _c.SearchApiKey ?? string.Empty; if (ImGui.InputText("API Key", ref tmp, 256)) { _c.SearchApiKey = tmp; _c.Save(); } }

        { int v = _c.SearchMaxResults; if (ImGui.SliderInt("Max Results", ref v, 1, 10)) { _c.SearchMaxResults = Math.Clamp(v, 1, 10); _c.Save(); } }
        { int v = _c.SearchTimeoutSec; if (ImGui.SliderInt("Timeout (sec)", ref v, 5, 120)) { _c.SearchTimeoutSec = Math.Clamp(v, 5, 600); _c.Save(); } }
    }

    private void SectionIpc()
    {
        ImGui.TextUnformatted("IPC / External Relay");
        ImGui.Separator();

        { var tmp = _c.IpcChannelName ?? string.Empty; if (ImGui.InputText("Channel Name", ref tmp, 64)) { _c.IpcChannelName = tmp; _c.Save(); } }

        { bool b = _c.PreferIpcRelay; if (ImGui.Checkbox("Prefer IPC relay over ProcessCommand", ref b)) { _c.PreferIpcRelay = b; _c.Save(); } }

        ImGui.Spacing();
        if (ImGui.Button("Bind IPC Now"))
        {
            var ok = new IpcChatRelay(PluginMain.PluginInterface, PluginMain.Log).Bind(_c.IpcChannelName);
            PluginMain.Instance?.ChatWindow?.AppendAssistant(ok
                ? $"[ipc] bound to '{_c.IpcChannelName}'"
                : $"[ipc] failed to bind '{_c.IpcChannelName}'");
        }
    }

    private void SectionUi()
    {
        ImGui.TextUnformatted("UI Preferences");
        ImGui.Separator();

        { bool b = _c.StartOpen; if (ImGui.Checkbox("Open Nunu window on load", ref b)) { _c.StartOpen = b; _c.Save(); } }

        { float f = _c.WindowOpacity; if (ImGui.SliderFloat("Window Opacity", ref f, 0.25f, 1.0f, "%.2f")) { _c.WindowOpacity = MathF.Round(Math.Clamp(f, 0.25f, 1f), 2); _c.Save(); } }

        { var tmp = _c.ChatDisplayName ?? string.Empty; if (ImGui.InputText("Your Display Name (UI only)", ref tmp, 128)) { _c.ChatDisplayName = tmp; _c.Save(); } }

        { bool b = _c.AsciiSafe; if (ImGui.Checkbox("ASCII-safe output (strip non-ASCII)", ref b)) { _c.AsciiSafe = b; _c.Save(); } }
        { bool b = _c.TwoPaneMode; if (ImGui.Checkbox("Two-pane mode (you vs assistant)", ref b)) { _c.TwoPaneMode = b; _c.Save(); } }
        { bool b = _c.ShowCopyButtons; if (ImGui.Checkbox("Show copy buttons on assistant messages", ref b)) { _c.ShowCopyButtons = b; _c.Save(); } }

        { float fs = _c.FontScale; if (ImGui.SliderFloat("Font Scale", ref fs, 0.8f, 1.6f, "%.2f")) { _c.FontScale = MathF.Round(Math.Clamp(fs, 0.6f, 2f), 2); _c.Save(); } }

        { bool b = _c.LockWindow; if (ImGui.Checkbox("Lock window", ref b)) { _c.LockWindow = b; _c.Save(); } }
    }

    private void SectionImages()
    {
        ImGui.TextUnformatted("Image / Atelier");
        ImGui.Separator();

        string[] backends = { "sdwebui", "none" };
        int sel = Array.IndexOf(backends, _c.ImageBackend);
        if (sel < 0) sel = 0;
        if (ImGui.Combo("Image Backend", ref sel, backends, backends.Length))
        { _c.ImageBackend = backends[sel]; _c.Save(); }

        { var tmp = _c.ImageBaseUrl ?? string.Empty; if (ImGui.InputText("Base URL", ref tmp, 256)) { _c.ImageBaseUrl = tmp; _c.Save(); } }
        { var tmp = _c.ImageModel ?? string.Empty; if (ImGui.InputText("Model", ref tmp, 128)) { _c.ImageModel = tmp; _c.Save(); } }

        { int v = _c.ImageSteps; if (ImGui.SliderInt("Steps", ref v, 5, 150)) { _c.ImageSteps = Math.Clamp(v, 1, 300); _c.Save(); } }
        { float f = _c.ImageGuidance; if (ImGui.SliderFloat("Guidance (CFG)", ref f, 1.0f, 20.0f, "%.1f")) { _c.ImageGuidance = Math.Clamp(f, 0.1f, 50f); _c.Save(); } }

        { int w = _c.ImageWidth; if (ImGui.InputInt("Width", ref w)) { _c.ImageWidth = Math.Clamp(w, 64, 2048); _c.Save(); } }
        ImGui.SameLine();
        { int h = _c.ImageHeight; if (ImGui.InputInt("Height", ref h)) { _c.ImageHeight = Math.Clamp(h, 64, 2048); _c.Save(); } }

        { var tmp = _c.ImageSampler ?? string.Empty; if (ImGui.InputText("Sampler", ref tmp, 128)) { _c.ImageSampler = tmp; _c.Save(); } }
        { int seed = _c.ImageSeed; if (ImGui.InputInt("Seed (-1 random)", ref seed)) { _c.ImageSeed = seed; _c.Save(); } }
        { int to = _c.ImageTimeoutSec; if (ImGui.SliderInt("Timeout (sec)", ref to, 10, 600)) { _c.ImageTimeoutSec = Math.Clamp(to, 5, 3600); _c.Save(); } }

        { bool b = _c.SaveImages; if (ImGui.Checkbox("Save generated images", ref b)) { _c.SaveImages = b; _c.Save(); } }
        { var tmp = _c.ImageSaveSubdir ?? string.Empty; if (ImGui.InputText("Save subfolder (under plugin config dir)", ref tmp, 128)) { _c.ImageSaveSubdir = tmp; _c.Save(); } }
    }

    private void SectionVoice()
    {
        ImGui.TextUnformatted("Text-to-Speech (TTS)");
        ImGui.Separator();

        { bool b = _c.VoiceSpeakEnabled; if (ImGui.Checkbox("Speak Nunu's replies", ref b)) { _c.VoiceSpeakEnabled = b; _c.Save(); } }
        { bool b = _c.VoiceOnlyWhenWindowFocused; if (ImGui.Checkbox("Only speak when Nunu window is open", ref b)) { _c.VoiceOnlyWhenWindowFocused = b; _c.Save(); } }

        { int v = _c.VoiceVolume; if (ImGui.SliderInt("Volume", ref v, 0, 100)) { _c.VoiceVolume = Math.Clamp(v, 0, 100); _c.Save(); } }
        { int v = _c.VoiceRate; if (ImGui.SliderInt("Rate", ref v, -10, 10)) { _c.VoiceRate = Math.Clamp(v, -10, 10); _c.Save(); } }

        if (ImGui.Button("Save Voice Settings")) _c.Save();

        ImGui.Spacing();
        ImGui.TextUnformatted("Installed Voices (Windows)");
        if (ImGui.Button("List Voices"))
        {
            var list = PluginMain.Instance.Voice?.ListVoices() ?? Array.Empty<string>();
            var joined = list.Count == 0 ? "(none detected)" : string.Join(", ", list);
            PluginMain.Instance.ChatWindow.AppendAssistant($"[voices] {joined}");
        }

        { var tmp = _c.VoiceName ?? string.Empty; if (ImGui.InputText("Preferred Voice Name", ref tmp, 128)) { _c.VoiceName = tmp; _c.Save(); } }
        ImGui.SameLine();
        if (ImGui.Button("Try Select"))
        {
            var ok = PluginMain.Instance.Voice?.TrySelectVoice(_c.VoiceName) ?? false;
            PluginMain.Instance.ChatWindow.AppendAssistant(ok ? $"[voice] selected '{_c.VoiceName}'" : $"[voice] failed for '{_c.VoiceName}'");
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.TextUnformatted("Speech-to-Text (planned)");
        ImGui.TextDisabled("Backends: Azure / Whisper local / Windows speech. (Next patch.)");
    }

    private static void Help(string text)
    {
        ImGui.SameLine(); ImGui.TextDisabled("(?)");
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 32f);
            ImGui.TextUnformatted(text);
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
        }
    }
}
