using System;
using System.IO;
using Dalamud.Plugin.Services;
using NunuTheAICompanion.Services.Songcraft;

namespace NunuTheAICompanion
{
    public partial class PluginMain
    {
        private SongcraftService? _songcraft;

        private void InitializeSongcraft(IPluginLog log)
        {
            try
            {
                _songcraft = new SongcraftService(Config, log);
                log.Info("[Songcraft] initialized.");
            }
            catch { /* continue without Songcraft */ }
        }

        /// <summary>
        /// Compose a melody for the given text/mood and return the MIDI file path.
        /// </summary>
        public string? TriggerSongcraft(string text, string? mood = null)
        {
            if (!Config.SongcraftEnabled || _songcraft is null) return null;
            var dir = string.IsNullOrWhiteSpace(Config.SongcraftSaveDir) ? Memory.StorageDirectory : Config.SongcraftSaveDir!;
            try
            {
                return _songcraft.ComposeToFile(text, mood, dir, "nunu_song");
            }
            catch (Exception ex)
            {
                _log.Error(ex, "[Songcraft] compose failed");
                return null;
            }
        }
    }
}
