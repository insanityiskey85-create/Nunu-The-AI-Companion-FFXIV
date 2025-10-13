using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using Dalamud.Plugin.Services;
using NunuTheAICompanion.Services;

namespace NunuTheAICompanion
{
    public partial class PluginMain
    {
        private EmbeddingClient? _embedding;
        private SoulThreadService? _threads;

        private void InitializeSoulThreads(HttpClient http, IPluginLog log)
        {
            try
            {
                _embedding = new EmbeddingClient(http, Config);
                _threads = new SoulThreadService(Memory, _embedding, Config, log);
                log.Info("[SoulThreads] initialized.");
            }
            catch { /* continue without Soul Threads */ }
        }

        private void OnUserUtterance(string author, string text, CancellationToken token)
        {
            if (_threads is null) { Memory.Append("user", text, topic: author); return; }
            _threads.AppendAndThread("user", text, author, token);
        }

        private List<(string role, string content)> BuildContext(string userText, CancellationToken token)
        {
            if (_threads is null) return Memory.GetRecentForContext(Config.ContextTurns);
            return _threads.GetContextFor(userText, Config.ThreadContextMaxFromThread, Config.ThreadContextMaxRecent, token);
        }

        private void OnAssistantReply(string author, string reply, CancellationToken token)
        {
            if (_threads is null) { Memory.Append("assistant", reply, topic: author); return; }
            _threads.AppendAndThread("assistant", reply, author, token);
        }
    }
}
