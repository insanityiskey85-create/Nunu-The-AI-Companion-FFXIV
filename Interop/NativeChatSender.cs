using System;
using System.Reflection;
using System.Text;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace NunuTheAICompanion.Interop;

public static class NativeChatSender
{
    private static IPluginLog? _log;
    private static MethodInfo? _apiInitialize;
    private static MethodInfo? _chatSend;

    public static void Initialize(object pluginInstance, IDalamudPluginInterface pluginInterface, IPluginLog log)
    {
        _log = log;

        try
        {
            // Optional: Dalamud.api.Initialize(plugin, pi)
            var apiType = FindType("Dalamud.api");
            _apiInitialize = apiType?.GetMethod("Initialize", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (_apiInitialize != null)
            {
                try { _apiInitialize.Invoke(null, new object?[] { pluginInstance, pluginInterface }); }
                catch (Exception ex) { _log.Warning(ex, "[NativeChatSender] Initialize call failed; continuing."); }
            }

            // Optional: MidiBard.Util.Chat.SendMessage(string)
            var chatType = FindType("MidiBard.Util.Chat");
            _chatSend = chatType?.GetMethod("SendMessage", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(string) }, null);

            _log.Information("[NativeChatSender] MidiBard Chat.SendMessage present = {Has}", _chatSend != null);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "[NativeChatSender] Initialize failed.");
        }
    }

    public static bool IsAvailable => _chatSend is not null;

    public static bool TrySendFull(string fullLine)
    {
        if (_chatSend is null || string.IsNullOrWhiteSpace(fullLine)) return false;

        try
        {
            var space = fullLine.IndexOf(' ');
            var prefix = space > 0 ? fullLine[..space] : "/say";
            var body = space > 0 ? fullLine[(space + 1)..] : fullLine;

            foreach (var part in ChunkForChat(body, 500, 6))
                _chatSend.Invoke(null, new object[] { $"{prefix} {part}" });

            return true;
        }
        catch (Exception ex)
        {
            _log?.Error(ex, "[NativeChatSender] TrySendFull failed for: {Line}", fullLine);
            return false;
        }
    }

    public static System.Collections.Generic.IEnumerable<string> ChunkForChat(string s, int maxBytes = 500, int prefixHeadroom = 6)
    {
        s = s.Replace("\r\n", " ").Replace('\n', ' ');
        var words = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var sb = new StringBuilder();
        foreach (var w in words)
        {
            var test = sb.Length == 0 ? w : $"{sb} {w}";
            if (Encoding.UTF8.GetByteCount(test) > (maxBytes - prefixHeadroom))
            {
                if (sb.Length > 0) { yield return sb.ToString(); sb.Clear(); }
                sb.Append(w);
            }
            else
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(w);
            }
        }
        if (sb.Length > 0) yield return sb.ToString();
    }

    private static Type? FindType(string fullName)
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try { var t = asm.GetType(fullName, false, false); if (t != null) return t; }
            catch { }
        }
        return null;
    }
}
