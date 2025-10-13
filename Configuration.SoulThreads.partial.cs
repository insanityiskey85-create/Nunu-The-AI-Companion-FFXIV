namespace NunuTheAICompanion
{
    public partial class Configuration
    {
        public bool SoulThreadsEnabled { get; set; } = true;
        public string? EmbeddingModel { get; set; } = "nomic-embed-text";
        public float ThreadSimilarityThreshold { get; set; } = 0.78f;
        public int ThreadContextMaxFromThread { get; set; } = 6;
        public int ThreadContextMaxRecent { get; set; } = 8;
    }
        public string? ChatEndpointUrl { get; set; } = "http://localhost:11434";
    }
        public int ContextTurns { get; set; } = 12;
    }
}
