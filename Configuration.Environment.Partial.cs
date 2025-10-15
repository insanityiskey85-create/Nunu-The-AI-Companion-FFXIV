namespace NunuTheAICompanion
{
    public sealed partial class Configuration
    {
        // Master switch
        public bool EnvironmentEnabled { get; set; } = true;

        // Context content
        public bool EnvIncludeZone { get; set; } = true;
        public bool EnvIncludeTime { get; set; } = true;
        public bool EnvIncludeDuty { get; set; } = true;
        public bool EnvIncludeCoords { get; set; } = false;

        // Behavior
        public int EnvTickSeconds { get; set; } = 2;
        public bool EnvAnnounceOnChange { get; set; } = true;
    }
}
