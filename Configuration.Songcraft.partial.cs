namespace NunuTheAICompanion
{
    public partial class Configuration
    {
        // Songcraft flags and defaults
        public bool SongcraftEnabled { get; set; } = true;
        public string? SongcraftKey { get; set; } = "C4";
        public int    SongcraftTempoBpm { get; set; } = 96;
        public int    SongcraftBars { get; set; } = 8;
        public int    SongcraftProgram { get; set; } = 24; // default voice if mood not mapped
        public string? SongcraftSaveDir { get; set; } = null; // null => use Memories dir
    }
}
