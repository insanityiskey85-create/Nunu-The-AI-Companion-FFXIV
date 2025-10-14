using System;
using Dalamud.Plugin.Services;

namespace NunuTheAICompanion.Services
{
    public sealed class VoiceService : IDisposable
    {
        private readonly Configuration _config;
        private readonly IPluginLog _log;
        private bool _disposed;

        public VoiceService(Configuration config, IPluginLog log)
        {
            _config = config;
            _log = log;
        }

        public void Speak(string text)
        {
            if (!_config.VoiceSpeakEnabled) return;
            // Your TTS implementation.
        }

        public void ApplyEmotionPreset(NunuEmotion emo)
        {
            int rate = _config.VoiceRate;
            int vol = _config.VoiceVolume;

            switch (emo)
            {
                case NunuEmotion.Happy: rate += 2; break;
                case NunuEmotion.Curious: rate += 1; break;
                case NunuEmotion.Playful: rate += 2; break;
                case NunuEmotion.Mournful: rate -= 2; break;
                case NunuEmotion.Sad: rate -= 1; break;
                case NunuEmotion.Angry: rate += 1; vol += 5; break;
                case NunuEmotion.Tired: rate -= 2; vol -= 5; break;
                default: break;
            }

            if (rate < -10) rate = -10; if (rate > 10) rate = 10;
            if (vol < 0) vol = 0; if (vol > 100) vol = 100;

            _log.Debug($"[Voice] Emotion preset applied: {emo} (rate={rate}, vol={vol})");
            // Apply to backend if supported.
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            // cleanup if needed (close TTS handles, etc.)
        }
    }
}
