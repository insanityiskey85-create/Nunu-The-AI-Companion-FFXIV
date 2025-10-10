using System.Numerics;
using System.IO; // for future file ops
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.IoC;
using Dalamud.Interface;
using NunuTheAICompanion.Services;
using Dalamud.Plugin.Services;

namespace NunuTheAICompanion;

public sealed class PluginMain : IDalamudPlugin
{
    public string Name => "Nunu The AI Companion";

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;

    internal Configuration Config { get; private set; }
    internal WindowSystem WindowSystem { get; } = new("NunuTheAICompanion");
    internal UI.ChatWindow ChatWindow { get; private set; }
    internal UI.ConfigWindow ConfigWindow { get; private set; }
    internal UI.MemoryWindow MemoryWindow { get; private set; }

    internal MemoryService Memory { get; private set; }

    private const string Command = "/nunu";

    public PluginMain()
    {
        // Load or init config
        Config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Config.Initialize(PluginInterface);

        // Resolve config directory for persistence (returns string in your API)
        var cfgDir = PluginInterface.GetPluginConfigDirectory(); // <-- no .FullName

        // Memory service
        Memory = new MemoryService(cfgDir, Config.MemoryMaxEntries, Config.MemoryEnabled);

        // Windows
        ChatWindow = new UI.ChatWindow(Config, Memory);
        WindowSystem.AddWindow(ChatWindow);

        ConfigWindow = new UI.ConfigWindow(Config);
        WindowSystem.AddWindow(ConfigWindow);

        MemoryWindow = new UI.MemoryWindow(Config, Memory);
        WindowSystem.AddWindow(MemoryWindow);

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
            MemoryWindow.IsOpen = true;
            return;
        }

        ChatWindow.IsOpen = !ChatWindow.IsOpen;
    }

    private void DrawUI()
    {
        WindowSystem.Draw();
    }

    public void Dispose()
    {
        WindowSystem.RemoveAllWindows();
        CommandManager.RemoveHandler(Command);
    }
}
