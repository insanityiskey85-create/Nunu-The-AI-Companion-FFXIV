using System;
using System.Collections.Generic;

namespace NunuTheAICompanion.Services
{
    public enum NunuEmotion
    {
        Neutral = 0,
        Happy,
        Curious,
        Playful,
        Mournful,
        Sad,
        Angry,
        Tired
    }

    /// <summary>
    /// Central emotion state with change notifications and simple emote lines.
    /// </summary>
    public sealed class EmotionManager
    {
        public NunuEmotion Current { get; private set; } = NunuEmotion.Neutral;
        public bool Locked { get; private set; } = false;

        public event Action<NunuEmotion>? OnEmotionChanged;

        private readonly Dictionary<NunuEmotion, string> _emotes = new Dictionary<NunuEmotion, string>
        {
            { NunuEmotion.Neutral,  "/em stands calmly." },
            { NunuEmotion.Happy,    "/em smiles brightly." },
            { NunuEmotion.Curious,  "/em tilts her head in wonder." },
            { NunuEmotion.Playful,  "/em twirls her voidbound lute mischievously." },
            { NunuEmotion.Mournful, "/em lowers her gaze; a soft lament escapes." },
            { NunuEmotion.Sad,      "/em sighs, a shadow crossing her face." },
            { NunuEmotion.Angry,    "/em glares with fiery resolve." },
            { NunuEmotion.Tired,    "/em rubs her eyes, weary but willing." },
        };

        public string EmoteFor(NunuEmotion e)
            => _emotes.TryGetValue(e, out var s) ? s : "/em stands calmly.";

        public void SetLocked(bool locked)
        {
            Locked = locked;
        }

        public void Set(NunuEmotion next)
        {
            if (Locked) return;
            if (next == Current) return;
            Current = next;
            try { OnEmotionChanged?.Invoke(next); } catch { /* swallow */ }
        }

        public static bool TryParse(string name, out NunuEmotion emo)
        {
            // allow friendly names
            switch ((name ?? "").Trim().ToLowerInvariant())
            {
                case "neutral": emo = NunuEmotion.Neutral; return true;
                case "happy": emo = NunuEmotion.Happy; return true;
                case "curious": emo = NunuEmotion.Curious; return true;
                case "playful": emo = NunuEmotion.Playful; return true;
                case "mournful": emo = NunuEmotion.Mournful; return true;
                case "sad": emo = NunuEmotion.Sad; return true;
                case "angry": emo = NunuEmotion.Angry; return true;
                case "tired": emo = NunuEmotion.Tired; return true;
                default: emo = NunuEmotion.Neutral; return false;
            }
        }
    }
}
