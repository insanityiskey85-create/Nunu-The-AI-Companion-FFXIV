using System;
using System.Numerics;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;

namespace NunuTheAICompanion.UI;

public sealed class ConfigWindow : Window
{
    private readonly Configuration _c;

    // Editable buffers (kept separate so we only Save() when user clicks)
    private string _backendMode;
    private string _backendUrl;
    private string _modelName;
    private float _temperature;
    private string _systemPrompt;

    private bool _listenEnabled;
    private bool _requireCallsign;
    private string _callsign;
    private bool _listenSelf;
    private bool _say, _tell, _party, _alliance, _fc, _shout, _yell;

    private bool _broadcastAsPersona;
    private string _personaName;

    private string _searchBackend;
    private string _searchApiKey;
    private int _searchMax;
    private int _searchTimeout;
    private bool _allowInternet;

    private string _ipcChannel;
    private bool _preferIpc;

    // UI niceties
    private bool _startOpen;
    private float _opacity;
    private string _chatDisplayName;
    private bool _asciiSafe;
    private bool _twoPane;
    private bool _showCopy;
    private float _fontScale;
    private bool _lockWindow;

    // Image / Atelier
    private string _imageBackend;
    private string _imageBaseUrl;
    private string _imageModel;
    private int _imageSteps;
    private float _imageGuidance;
    private int _imageW;
    private int _imageH;
    private string _imageSampler;
    private int _imageSeed;
    private int _imageTimeout;
    private bool _saveImages;
    private string _imageSubdir;

    public ConfigWindow(Configuration config)
        : base("Nunu – Configuration", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        _c = config;

        Size = new Vector2(720, 640);
        SizeCondition = ImGuiCond.FirstUseEver;

        // Prime buffers from config
        _backendMode = _c.BackendMode;
        _backendUrl = _c.BackendUrl;
        _modelName = _c.ModelName;
        _temperature = _c.Temperature;
        _systemPrompt = _c.SystemPrompt;

        _listenEnabled = _c.ListenEnabled;
        _requireCallsign = _c.RequireCallsign;
        _callsign = _c.Callsign;
        _listenSelf = _c.ListenSelf;
        _say = _c.ListenSay;
        _tell = _c.ListenTell;
        _party = _c.ListenParty;
        _alliance = _c.ListenAlliance;
        _fc = _c.ListenFreeCompany;
        _shout = _c.ListenShout;
        _yell = _c.ListenYell;

        _broadcastAsPersona = _c.BroadcastAsPersona;
        _personaName = _c.PersonaName;

        _searchBackend = _c.SearchBackend;
        _searchApiKey = _c.SearchApiKey ?? string.Empty;
        _searchMax = _c.SearchMaxResults;
        _searchTimeout = _c.SearchTimeoutSec;
        _allowInternet = _c.AllowInternet;

        _ipcChannel = _c.IpcChannelName;
        _preferIpc = _c.PreferIpcRelay;

        _startOpen = _c.StartOpen;
        _opacity = _c.WindowOpacity;
        _chatDisplayName = _c.ChatDisplayName;
        _asciiSafe = _c.AsciiSafe;
        _twoPane = _c.TwoPaneMode;
        _showCopy = _c.ShowCopyButtons;
        _fontScale = _c.FontScale;
        _lockWindow = _c.LockWindow;

        _imageBackend = _c.ImageBackend;
        _imageBaseUrl = _c.ImageBaseUrl;
        _imageModel = _c.ImageModel;
        _imageSteps = _c.ImageSteps;
        _imageGuidance = _c.ImageGuidance;
        _imageW = _c.ImageWidth;
        _imageH = _c.ImageHeight;
        _imageSampler = _c.ImageSampler;
        _imageSeed = _c.ImageSeed;
        _imageTimeout = _c.ImageTimeoutSec;
        _saveImages = _c.SaveImages;
        _imageSubdir = _c.ImageSaveSubdir;
    }

    public override void Draw()
    {
        ImGui.PushItemWidth(260);

        if (ImGui.BeginTabBar("nunu_cfg_tabs"))
        {
            if (ImGui.BeginTabItem("Backend"))
            {
                SectionBackend();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Listening"))
            {
                SectionListening();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Broadcast"))
            {
                SectionBroadcast();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Web Search"))
            {
                SectionSearch();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("IPC"))
            {
                SectionIpc();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("UI"))
            {
                SectionUi();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Images"))
            {
                SectionImages();
                ImGui.EndTabItem();
            }
        }

        ImGui.Separator();
        if (ImGui.Button("Save All##nunu_cfg"))
        {
            ApplyAndSave();
        }
        ImGui.SameLine();
        if (ImGui.Button("Rehook Listener"))
        {
            ApplyAndSave(); // ensure we persist first
            PluginMain.Instance.RehookListener();
        }
        ImGui.SameLine();
        if (ImGui.Button("Close"))
        {
            IsOpen = false;
        }

        ImGui.PopItemWidth();
    }

    private void SectionBackend()
    {
        ImGui.TextUnformatted("Chat Backend (LLM)");
        ImGui.Separator();

        // BackendMode combo for friendly values
        string[] modes = { "ollama", "openai", "proxy", "custom" };
        int sel = Array.IndexOf(modes, _backendMode);
        if (sel < 0) sel = 0;
        if (ImGui.Combo("Mode", ref sel, modes, modes.Length))
            _backendMode = modes[sel];

        ImGui.InputText("Backend URL", ref _backendUrl, 512);
        ImGui.InputText("Model Name", ref _modelName, 128);
        ImGui.SliderFloat("Temperature", ref _temperature, 0.0f, 1.5f, "%.2f");
        ImGui.TextUnformatted("System Prompt");
        ImGui.InputTextMultiline("##systemPrompt", ref _systemPrompt, 4000, new Vector2(-1, 120));
    }

    private void SectionListening()
    {
        ImGui.TextUnformatted("Listen to Game Chat");
        ImGui.Separator();

        ImGui.Checkbox("Enable listening", ref _listenEnabled);
        ImGui.SameLine(); Help("(If off, Nunu only answers in her own window)");

        ImGui.Checkbox("Require callsign", ref _requireCallsign);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(160);
        ImGui.InputText("Callsign", ref _callsign, 64);
        Help("Example: @nunu — Nunu only replies to lines containing this.");

        ImGui.Checkbox("Listen to my own messages", ref _listenSelf);

        ImGui.Separator();
        ImGui.TextUnformatted("Channels:");
        ImGui.Checkbox("Say", ref _say); ImGui.SameLine();
        ImGui.Checkbox("Tell", ref _tell); ImGui.SameLine();
        ImGui.Checkbox("Party", ref _party); ImGui.SameLine();
        ImGui.Checkbox("Alliance", ref _alliance); ImGui.SameLine();
        ImGui.Checkbox("Free Company", ref _fc); ImGui.SameLine();
        ImGui.Checkbox("Shout", ref _shout); ImGui.SameLine();
        ImGui.Checkbox("Yell", ref _yell);

        ImGui.Separator();
        bool dbg = _c.DebugListen;
        if (ImGui.Checkbox("Mirror heard lines to Nunu window (debug)", ref dbg))
        {
            _c.DebugListen = dbg; _c.Save();
        }
    }

    private void SectionBroadcast()
    {
        ImGui.TextUnformatted("Broadcast to Real Chat");
        ImGui.Separator();
        ImGui.Checkbox("Prefix outgoing lines with persona name", ref _broadcastAsPersona);
        ImGui.SameLine(); Help("Your account sends the message; this adds [Little Nunu] to the text.");
        ImGui.InputText("Persona Name", ref _personaName, 128);
        ImGui.Spacing();
        ImGui.TextDisabled("Tip: Use /nunuspeak on and /nunuchan say|party|fc|shout|yell to send replies to a channel.");
    }

    private void SectionSearch()
    {
        ImGui.TextUnformatted("Web Search Tool");
        ImGui.Separator();

        string[] engines = { "serpapi", "none" };
        int sel = Array.IndexOf(engines, _searchBackend);
        if (sel < 0) sel = 0;
        if (ImGui.Combo("Backend", ref sel, engines, engines.Length))
            _searchBackend = engines[sel];

        ImGui.Checkbox("Allow internet lookups", ref _allowInternet);

        ImGui.InputText("API Key", ref _searchApiKey, 256);
        ImGui.SameLine(); Help("Needed for SerpAPI.");
        ImGui.SliderInt("Max Results", ref _searchMax, 1, 10);
        ImGui.SliderInt("Timeout (sec)", ref _searchTimeout, 5, 120);
    }

    private void SectionIpc()
    {
        ImGui.TextUnformatted("IPC / External Relay");
        ImGui.Separator();

        ImGui.InputText("Channel Name", ref _ipcChannel, 64);
        ImGui.Checkbox("Prefer IPC relay over ProcessCommand", ref _preferIpc);

        ImGui.Spacing();
        if (ImGui.Button("Bind IPC Now"))
        {
            ApplyAndSave();
            var ok = new Services.IpcChatRelay(PluginMain.PluginInterface, PluginMain.Log).Bind(_ipcChannel);
            PluginMain.Instance?.ChatWindow?.AppendAssistant(ok ? $"[ipc] bound to '{_ipcChannel}'" : $"[ipc] failed to bind '{_ipcChannel}'");
        }
    }

    private void SectionUi()
    {
        ImGui.TextUnformatted("UI Preferences");
        ImGui.Separator();

        ImGui.Checkbox("Open Nunu window on load", ref _startOpen);
        ImGui.SliderFloat("Window Opacity", ref _opacity, 0.25f, 1.0f, "%.2f");
        ImGui.InputText("Your Display Name (UI only)", ref _chatDisplayName, 128);
        ImGui.Checkbox("ASCII-safe output (strip non-ASCII)", ref _asciiSafe);
        ImGui.Checkbox("Two-pane mode (you vs assistant)", ref _twoPane);
        ImGui.Checkbox("Show copy buttons on assistant messages", ref _showCopy);
        ImGui.SliderFloat("Font Scale", ref _fontScale, 0.8f, 1.6f, "%.2f");
        ImGui.Checkbox("Lock window", ref _lockWindow);
    }

    private void SectionImages()
    {
        ImGui.TextUnformatted("Image / Atelier");
        ImGui.Separator();

        string[] backends = { "sdwebui", "none" };
        int sel = Array.IndexOf(backends, _imageBackend);
        if (sel < 0) sel = 0;
        if (ImGui.Combo("Image Backend", ref sel, backends, backends.Length))
            _imageBackend = backends[sel];

        ImGui.InputText("Base URL", ref _imageBaseUrl, 256);
        ImGui.InputText("Model", ref _imageModel, 128);
        ImGui.SliderInt("Steps", ref _imageSteps, 5, 150);
        ImGui.SliderFloat("Guidance (CFG)", ref _imageGuidance, 1.0f, 20.0f, "%.1f");
        ImGui.InputInt("Width", ref _imageW);
        ImGui.SameLine();
        ImGui.InputInt("Height", ref _imageH);
        ImGui.InputText("Sampler", ref _imageSampler, 128);
        ImGui.InputInt("Seed (-1 random)", ref _imageSeed);
        ImGui.SliderInt("Timeout (sec)", ref _imageTimeout, 10, 600);
        ImGui.Checkbox("Save generated images", ref _saveImages);
        ImGui.InputText("Save subfolder (under plugin config dir)", ref _imageSubdir, 128);
    }

    private void ApplyAndSave()
    {
        // Backend
        _c.BackendMode = _backendMode.Trim();
        _c.BackendUrl = _backendUrl.Trim();
        _c.ModelName = _modelName.Trim();
        _c.Temperature = Math.Clamp(_temperature, 0f, 2f);
        _c.SystemPrompt = _systemPrompt;

        // Listening
        _c.ListenEnabled = _listenEnabled;
        _c.RequireCallsign = _requireCallsign;
        _c.Callsign = string.IsNullOrWhiteSpace(_callsign) ? "@nunu" : _callsign.Trim();
        _c.ListenSelf = _listenSelf;
        _c.ListenSay = _say;
        _c.ListenTell = _tell;
        _c.ListenParty = _party;
        _c.ListenAlliance = _alliance;
        _c.ListenFreeCompany = _fc;
        _c.ListenShout = _shout;
        _c.ListenYell = _yell;

        // Broadcast
        _c.BroadcastAsPersona = _broadcastAsPersona;
        _c.PersonaName = string.IsNullOrWhiteSpace(_personaName) ? "Little Nunu" : _personaName.Trim();

        // Search
        _c.SearchBackend = _searchBackend.Trim();
        _c.SearchApiKey = _searchApiKey?.Trim();
        _c.SearchMaxResults = Math.Clamp(_searchMax, 1, 10);
        _c.SearchTimeoutSec = Math.Clamp(_searchTimeout, 5, 600);
        _c.AllowInternet = _allowInternet;

        // IPC
        _c.IpcChannelName = _ipcChannel.Trim();
        _c.PreferIpcRelay = _preferIpc;

        // UI niceties
        _c.StartOpen = _startOpen;
        _c.WindowOpacity = Math.Clamp(_opacity, 0.25f, 1.0f);
        _c.ChatDisplayName = string.IsNullOrWhiteSpace(_chatDisplayName) ? "Real Nunu" : _chatDisplayName.Trim();
        _c.AsciiSafe = _asciiSafe;
        _c.TwoPaneMode = _twoPane;
        _c.ShowCopyButtons = _showCopy;
        _c.FontScale = Math.Clamp(_fontScale, 0.6f, 2.0f);
        _c.LockWindow = _lockWindow;

        // Images
        _c.ImageBackend = _imageBackend.Trim();
        _c.ImageBaseUrl = _imageBaseUrl.Trim().TrimEnd('/');
        _c.ImageModel = _imageModel.Trim();
        _c.ImageSteps = Math.Clamp(_imageSteps, 1, 300);
        _c.ImageGuidance = Math.Clamp(_imageGuidance, 0.1f, 50f);
        _c.ImageWidth = Math.Clamp(_imageW, 64, 2048);
        _c.ImageHeight = Math.Clamp(_imageH, 64, 2048);
        _c.ImageSampler = _imageSampler.Trim();
        _c.ImageSeed = _imageSeed;
        _c.ImageTimeoutSec = Math.Clamp(_imageTimeout, 5, 3600);
        _c.SaveImages = _saveImages;
        _c.ImageSaveSubdir = string.IsNullOrWhiteSpace(_imageSubdir) ? "Images" : _imageSubdir.Trim();

        _c.Save();
    }

    private static void Help(string text)
    {
        ImGui.SameLine();
        ImGui.TextDisabled("(?)");
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
