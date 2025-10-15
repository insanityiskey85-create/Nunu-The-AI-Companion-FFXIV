using System;
using System.Globalization;
using System.Text;
using Dalamud.Plugin.Services;

namespace NunuTheAICompanion.Services
{
    public enum PersonalityTrait { Warmth, Curiosity, Formality, Playfulness, Gravitas }

    public sealed class PersonalityService
    {
        private readonly Configuration _cfg;
        private readonly IPluginLog _log;

        // Live state (clamped -1..+1)
        public float Warmth { get; private set; }
        public float Curiosity { get; private set; }
        public float Formality { get; private set; }
        public float Playfulness { get; private set; }
        public float Gravitas { get; private set; }

        private DateTime _lastDecayUtc = DateTime.UtcNow.Date;

        public PersonalityService(Configuration cfg, IPluginLog log)
        {
            _cfg = cfg; _log = log;
            // Start from saved current, or fall back to baseline if empty
            Warmth = Coalesce(_cfg.PersonaWarmth, _cfg.PersonaBaselineWarmth);
            Curiosity = Coalesce(_cfg.PersonaCuriosity, _cfg.PersonaBaselineCuriosity);
            Formality = Coalesce(_cfg.PersonaFormality, _cfg.PersonaBaselineFormality);
            Playfulness = Coalesce(_cfg.PersonaPlayfulness, _cfg.PersonaBaselinePlayfulness);
            Gravitas = Coalesce(_cfg.PersonaGravitas, _cfg.PersonaBaselineGravitas);
            ClampAll();
        }

        private static float Coalesce(float? current, float baseline) => current ?? baseline;

        public void SaveToConfig()
        {
            _cfg.PersonaWarmth = Warmth;
            _cfg.PersonaCuriosity = Curiosity;
            _cfg.PersonaFormality = Formality;
            _cfg.PersonaPlayfulness = Playfulness;
            _cfg.PersonaGravitas = Gravitas;
            _cfg.Save();
        }

        private static float Clamp(float v) => MathF.Max(-1f, MathF.Min(1f, v));
        private void ClampAll()
        {
            Warmth = Clamp(Warmth);
            Curiosity = Clamp(Curiosity);
            Formality = Clamp(Formality);
            Playfulness = Clamp(Playfulness);
            Gravitas = Clamp(Gravitas);
        }

        public void Nudge(PersonalityTrait t, float delta, string reason)
        {
            if (!_cfg.PersonaAutoEnabled) return;
            switch (t)
            {
                case PersonalityTrait.Warmth: Warmth += delta; break;
                case PersonalityTrait.Curiosity: Curiosity += delta; break;
                case PersonalityTrait.Formality: Formality += delta; break;
                case PersonalityTrait.Playfulness: Playfulness += delta; break;
                case PersonalityTrait.Gravitas: Gravitas += delta; break;
            }
            ClampAll();
            _log.Debug($"[Persona] {t:+0.00;-0.00} ({reason}) -> W{Warmth:0.00} C{Curiosity:0.00} F{Formality:0.00} P{Playfulness:0.00} G{Gravitas:0.00}");
            SaveToConfig();
        }

        public void DecayTick()
        {
            // daily micro-drift back toward baselines
            var today = DateTime.UtcNow.Date;
            if (_lastDecayUtc == today) return;
            var days = (today - _lastDecayUtc).Days;
            if (days <= 0) { _lastDecayUtc = today; return; }

            float amt = MathF.Max(0f, _cfg.PersonaBaselineDecayPerDay);
            for (int i = 0; i < days; i++)
            {
                Warmth = DriftToward(Warmth, _cfg.PersonaBaselineWarmth, amt);
                Curiosity = DriftToward(Curiosity, _cfg.PersonaBaselineCuriosity, amt);
                Formality = DriftToward(Formality, _cfg.PersonaBaselineFormality, amt);
                Playfulness = DriftToward(Playfulness, _cfg.PersonaBaselinePlayfulness, amt);
                Gravitas = DriftToward(Gravitas, _cfg.PersonaBaselineGravitas, amt);
            }
            ClampAll();
            _lastDecayUtc = today;
            SaveToConfig();
        }

        private static float DriftToward(float v, float target, float step)
        {
            if (step <= 0f) return v;
            if (Math.Abs(v - target) <= step) return target;
            return v + MathF.Sign(target - v) * step;
        }

        // Affinity coupling: higher tiers warm/playful; low tiers formal/gravity
        public void OnAffinityTier(AffinityTier tier)
        {
            if (!_cfg.PersonaAutoEnabled) return;
            switch (tier)
            {
                case AffinityTier.Stranger:
                    Nudge(PersonalityTrait.Warmth, -0.15f, "affinity");
                    Nudge(PersonalityTrait.Formality, +0.15f, "affinity");
                    Nudge(PersonalityTrait.Gravitas, +0.05f, "affinity");
                    break;
                case AffinityTier.Acquaintance:
                    Nudge(PersonalityTrait.Formality, -0.05f, "affinity");
                    Nudge(PersonalityTrait.Curiosity, +0.05f, "affinity");
                    break;
                case AffinityTier.Friend:
                    Nudge(PersonalityTrait.Warmth, +0.10f, "affinity");
                    Nudge(PersonalityTrait.Playfulness, +0.08f, "affinity");
                    break;
                case AffinityTier.Confidant:
                    Nudge(PersonalityTrait.Warmth, +0.12f, "affinity");
                    Nudge(PersonalityTrait.Gravitas, +0.05f, "affinity");
                    break;
                case AffinityTier.Bonded:
                    Nudge(PersonalityTrait.Warmth, +0.15f, "affinity");
                    Nudge(PersonalityTrait.Playfulness, +0.10f, "affinity");
                    break;
                case AffinityTier.Eternal:
                    Nudge(PersonalityTrait.Warmth, +0.20f, "affinity");
                    Nudge(PersonalityTrait.Gravitas, +0.10f, "affinity");
                    break;
            }
        }

        // Emotion coupling (gentle)
        public void OnEmotion(NunuEmotion e)
        {
            if (!_cfg.PersonaAutoEnabled) return;
            switch (e)
            {
                case NunuEmotion.Happy:
                    Nudge(PersonalityTrait.Warmth, +0.04f, "emotion");
                    Nudge(PersonalityTrait.Playfulness, +0.04f, "emotion");
                    break;
                case NunuEmotion.Curious:
                    Nudge(PersonalityTrait.Curiosity, +0.05f, "emotion");
                    break;
                case NunuEmotion.Mournful:
                    Nudge(PersonalityTrait.Gravitas, +0.05f, "emotion");
                    Nudge(PersonalityTrait.Playfulness, -0.03f, "emotion");
                    break;
                case NunuEmotion.Sad:
                    Nudge(PersonalityTrait.Warmth, -0.03f, "emotion");
                    Nudge(PersonalityTrait.Gravitas, +0.04f, "emotion");
                    break;
                case NunuEmotion.Angry:
                    Nudge(PersonalityTrait.Warmth, -0.05f, "emotion");
                    Nudge(PersonalityTrait.Gravitas, +0.06f, "emotion");
                    break;
                case NunuEmotion.Playful:
                    Nudge(PersonalityTrait.Playfulness, +0.05f, "emotion");
                    break;
                case NunuEmotion.Tired:
                    Nudge(PersonalityTrait.Curiosity, -0.03f, "emotion");
                    break;
            }
        }

        public string BuildSystemLine()
        {
            // concise, model-friendly guidance
            var sb = new StringBuilder();
            sb.Append("Adaptive personality (range -1..+1): ");
            sb.Append($"Warmth={Warmth:0.00}, Curiosity={Curiosity:0.00}, Formality={Formality:0.00}, Playfulness={Playfulness:0.00}, Gravitas={Gravitas:0.00}. ");
            sb.Append("Interpretation: Higher Warmth -> kinder tone; Higher Curiosity -> more questions/details; Higher Formality -> fewer slang/emotes; ");
            sb.Append("Higher Playfulness -> more whimsy; Higher Gravitas -> solemn/poetic economy. Keep replies in FFXIV voice.");
            return sb.ToString();
        }
    }
}
