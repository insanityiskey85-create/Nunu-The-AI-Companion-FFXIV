using System.Numerics;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.IoC;
using NunuTheAICompanion.Services;
using Dalamud.Plugin.Services;

namespace NunuTheAICompanion;

public sealed class PluginMain : IDalamudPlugin
{
    public string Name => "Nunu The AI Companion";

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;

    internal Configuration Config { get; private set; } = null!;
    internal WindowSystem WindowSystem { get; } = new("NunuTheAICompanion");

    internal UI.ChatWindow ChatWindow { get; private set; } = null!;
    internal UI.ConfigWindow ConfigWindow { get; private set; } = null!;
    internal UI.MemoryWindow? MemoryWindow { get; private set; }
    internal MemoryService? Memory { get; private set; }

    private readonly List<(string role, string content)> _history = new();
    private readonly HttpClient _http = new();
    private OllamaClient _client = null!;
    private CancellationTokenSource? _cts;
    private bool _isStreaming;

    private const string Command = "/nunu";

    public PluginMain()
    {
        // Load config
        Config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Config.Initialize(PluginInterface);

        // Client for backend (uses Config for mode)
        _client = new OllamaClient(_http, Config);

        // Optional memory
        var cfgDir = PluginInterface.GetPluginConfigDirectory(); // string path in your API
        Memory = new MemoryService(cfgDir, Config.MemoryMaxEntries, Config.MemoryEnabled);

        // Windows
        ChatWindow = new UI.ChatWindow(Config);
        ChatWindow.Size = new Vector2(680, 460);
        ChatWindow.OnSend += HandleUserSend;
        WindowSystem.AddWindow(ChatWindow);

        ConfigWindow = new UI.ConfigWindow(Config);
        WindowSystem.AddWindow(ConfigWindow);

        MemoryWindow = new UI.MemoryWindow(Config, Memory);
        WindowSystem.AddWindow(MemoryWindow);

        // Greeting in view + history
        const string hello = "WAH! Little Nunu listens by the campfire. What tale shall we stitch?";
        ChatWindow.AppendAssistant(hello);
        _history.Add(("assistant", hello));
        Memory?.Append("assistant", hello, topic: "greeting");

        // Commands
        CommandManager.AddHandler(Command, new CommandInfo(OnCommand)
        {
            HelpMessage = "Toggle Nunu. '/nunu config' settings. '/nunu memory' memories."
        });

        // UI events
        PluginInterface.UiBuilder.Draw += DrawUI;
        PluginInterface.UiBuilder.OpenConfigUi += () => ConfigWindow.IsOpen = true;
        PluginInterface.UiBuilder.OpenMainUi += () => ChatWindow.IsOpen = true;
    }

    private void OnCommand(string command, string args)
    {
        var trimmed = args?.Trim() ?? string.Empty;

        if (trimmed.Equals("config", StringComparison.OrdinalIgnoreCase))
        {
            ConfigWindow.IsOpen = true;
            return;
        }
        if (trimmed.Equals("memory", StringComparison.OrdinalIgnoreCase))
        {
            MemoryWindow!.IsOpen = true;
            return;
        }
        ChatWindow.IsOpen = !ChatWindow.IsOpen;
    }

    private void DrawUI() => WindowSystem.Draw();

    public void Dispose()
    {
        _cts?.Cancel();
        WindowSystem.RemoveAllWindows();
        CommandManager.RemoveHandler(Command);
        _http.Dispose();
    }

    // ---------- Chat pipeline ----------

    private void HandleUserSend(string text)
    {
        // Keep one record in the durable memory/history
        _history.Add(("user", text));
        if (Memory?.Enabled == true) Memory.Append("user", text, topic: "chat");

        // Begin a streaming assistant line in the UI
        ChatWindow.BeginAssistantStream();

        // Build request history WITHOUT a trailing empty assistant
        var request = _history.ToList();

        StreamAssistantAsync(request);
    }

    private async void StreamAssistantAsync(List<(string role, string content)> request)
    {
        if (_isStreaming) _cts?.Cancel();
        _isStreaming = true;
        _cts = new CancellationTokenSource();

        try
        {
            await foreach (var delta in _client.StreamChatAsync(Config.BackendUrl, request, _cts.Token))
            {
                if (string.IsNullOrEmpty(delta)) continue;
                ChatWindow.AppendAssistantDelta(delta);
            }
        }
        catch (Exception ex)
        {
            ChatWindow.AppendAssistantDelta($"[error: {ex.Message}]");
        }
        finally
        {
            _isStreaming = false;
            // Seal the line in the view and add to history/memory
            ChatWindow.EndAssistantStream();

            // Capture the last assistant line we just finished
            var last = GetLastAssistantLineForHistory();
            if (!string.IsNullOrWhiteSpace(last))
            {
                _history.Add(("assistant", last));
                if (Memory?.Enabled == true)
                    Memory.Append("assistant", last, topic: "chat");
            }
        }
    }

    private string GetLastAssistantLineForHistory()
    {
        // The ChatWindow sealed the streaming buffer into a new assistant line
        // We don't have direct access to its internal list, so best-effort:
        // store a shadow of last delta locally in future if you want perfect accuracy.
        // For now, nothing to do; EndAssistantStream just added full text to view.
        // If you want exact text, reflect deltas here instead of reading back.
        return ""; // optional: track last built text during streaming and return it here
    }
}
