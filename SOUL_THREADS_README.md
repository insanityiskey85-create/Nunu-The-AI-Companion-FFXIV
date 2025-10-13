# Soul Threads (Topic-Aware Persistent Memory)

Adds topic-aware memory using Ollama embeddings.

## Files
- Services/EmbeddingClient.cs
- Services/SoulThreadService.cs
- Configuration.SoulThreads.partial.cs
- PluginMain.SoulThreads.partial.cs

## Integrate
1. Call `InitializeSoulThreads(_http, _log)` after MemoryService setup.
2. Use `OnUserUtterance`, `BuildContext`, `OnAssistantReply` in your message pipeline.
3. Configure thresholds in config.

Graceful fallback if embeddings fail or disabled.
