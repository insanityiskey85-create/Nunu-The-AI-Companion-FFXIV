using System;
using System.IO;
using System.Text.Json;
using Dalamud.Plugin.Services;
// bring the NunuEmotion enum into scope (it lives in the root plugin namespace)
using NunuTheAICompanion;

namespace NunuTheAICompanion.Services
{
    public enum AffinityTier
    {
        Stranger = 0,
        Acquaintance = 1,
        Friend = 2,
        Confidant = 3,
        Bonded = 4,
        Eternal = 5,
    }

    public sealed class AffinityService
    {
        private readonly Configuration _cfg;
        private readonly IPluginLog _log;
        private readonly string _savePath;

        private float _score;          // running bond score
        private int _streak;           // positive-streak helper (optional flavor)
        private DateTime _lastDecayUtc = DateTime.UtcNow.Date;

        private NunuEmotion _lastEmotion = NunuEmotion.Neutral;

        public event Action<AffinityTier, AffinityTier, string>? OnTierChanged;

        public AffinityService(string storageRoot, Configuration cfg, IPluginLog log)
        {
            _cfg = cfg;
            _log = log;
            _savePath = Path.Combine(storageRoot, "affinity.json");
            Load();
        }

        // ---------- Public API used by PluginMain ----------

        public void OnUserUtterance(string text)
        {
            if (!_cfg.AffinityEnabled) return;

            float delta = _cfg.AffinityPerInteraction;
            if (_cfg.AffinityAutoClassify)
                delta *= ClassifyPolarity(text);

            AddScore(delta, "user utterance");
        }

        public void OnAssistantReply(string reply)
        {
            if (!_cfg.AffinityEnabled) return;

            float delta = _cfg.AffinityPerAssistantReply;
            if (_cfg.AffinityAutoClassify)
                delta *= ClassifyPolarity(reply);

            AddScore(delta, "assistant reply");
        }

        /// <summary>
        /// Called by PluginMain when the EmotionManager emits a new emotion.
        /// Applies a small, configurable-friendly nudge to affinity.
        /// </summary>
        public void OnEmotionChanged(NunuEmotion emo)
        {
            _lastEmotion = emo;
            if (!_cfg.AffinityEnabled) return;

            // Gentle defaults; pass through weighting in AddScore.
            float delta = emo switch
            {
                NunuEmotion.Happy => 0.6f,
                NunuEmotion.Curious => 0.3f,
                NunuEmotion.Playful => 0.4f,
                NunuEmotion.Mournful => 0.0f,  // reflective, neutral to bond
                NunuEmotion.Sad => -0.2f,
                NunuEmotion.Angry => -0.4f,
                NunuEmotion.Tired => -0.1f,
                _ => 0.0f,  // Neutral or unknown
            };

            if (Math.Abs(delta) > 0.0001f)
                AddScore(delta, $"emotion:{emo}");
        }

        public void AddScore(float delta, string reason)
        {
            if (!_cfg.AffinityEnabled) return;

            var beforeTier = TierFor(_score);
            _score += Weighted(delta);
            _score = MathF.Max(0f, _score); // clamp lower bound
            var afterTier = TierFor(_score);

            if (delta >= 0.001f) _streak++;
            else if (delta <= -0.001f) _streak = Math.Max(0, _streak - 1);

            if (afterTier != beforeTier)
                OnTierChanged?.Invoke(afterTier, beforeTier, reason);

            Save();
        }

        public void SetScore(float value, string reason)
        {
            value = MathF.Max(0f, value);
            var before = TierFor(_score);
            _score = value;
            var after = TierFor(_score);
            if (after != before)
                OnTierChanged?.Invoke(after, before, reason);
            Save();
        }

        public void Reset()
        {
            var before = TierFor(_score);
            _score = 0f;
            _streak = 0;
            var after = TierFor(_score);
            if (after != before)
                OnTierChanged?.Invoke(after, before, "reset");
            Save();
        }

        public void Save()
        {
            try
            {
                var dto = new SaveDto
                {
                    score = _score,
                    streak = _streak,
                    lastDecayDate = _lastDecayUtc.Date,
                    lastEmotion = _lastEmotion.ToString(),
                };
                var json = JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_savePath, json);
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "[Affinity] save failed");
            }
        }

        public void DecayTick()
        {
            if (!_cfg.AffinityEnabled) return;

            var today = DateTime.UtcNow.Date;
            if (_lastDecayUtc == today) return;

            var days = (today - _lastDecayUtc).Days;
            if (days <= 0) { _lastDecayUtc = today; return; }

            float perDay = MathF.Max(0f, _cfg.AffinityDecayPerDay);
            if (perDay > 0f && _score > 0f)
            {
                var beforeTier = TierFor(_score);
                _score = MathF.Max(0f, _score - perDay * days);
                var afterTier = TierFor(_score);
                if (afterTier != beforeTier)
                    OnTierChanged?.Invoke(afterTier, beforeTier, "decay");
                Save();
            }

            _lastDecayUtc = today;
        }

        /// <summary>
        /// Returns (score, tier, streak) snapshot for UI.
        /// </summary>
        public (float score, AffinityTier tier, int streak) Snapshot()
            => (_score, TierFor(_score), _streak);

        // ---------- Internals ----------

        private void Load()
        {
            try
            {
                if (!File.Exists(_savePath))
                {
                    _score = 0f;
                    _streak = 0;
                    _lastDecayUtc = DateTime.UtcNow.Date;
                    _lastEmotion = NunuEmotion.Neutral;
                    Save();
                    return;
                }

                var json = File.ReadAllText(_savePath);
                var dto = JsonSerializer.Deserialize<SaveDto>(json);
                if (dto is null)
                {
                    _score = 0f;
                    _streak = 0;
                    _lastDecayUtc = DateTime.UtcNow.Date;
                    _lastEmotion = NunuEmotion.Neutral;
                    return;
                }

                _score = MathF.Max(0f, dto.score);
                _streak = Math.Max(0, dto.streak);
                _lastDecayUtc = dto.lastDecayDate == default ? DateTime.UtcNow.Date : dto.lastDecayDate.Date;

                // try parse last emotion for flavor continuity
                if (!Enum.TryParse<NunuEmotion>(dto.lastEmotion ?? "Neutral", out _lastEmotion))
                    _lastEmotion = NunuEmotion.Neutral;
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "[Affinity] load failed; resetting.");
                _score = 0f;
                _streak = 0;
                _lastDecayUtc = DateTime.UtcNow.Date;
                _lastEmotion = NunuEmotion.Neutral;
            }
        }

        private static float ClassifyPolarity(string text)
        {
            // ultra-light heuristic sentiment: +1 positive, -1 negative, default ~neutral
            if (string.IsNullOrWhiteSpace(text)) return 0.5f;

            var s = text.ToLowerInvariant();

            // negative cues
            int neg = 0;
            if (s.Contains("hate")) neg++;
            if (s.Contains("angry") || s.Contains("mad")) neg++;
            if (s.Contains("annoy") || s.Contains("annoying")) neg++;
            if (s.Contains("bad") || s.Contains("terrible") || s.Contains("awful")) neg++;

            // positive cues
            int pos = 0;
            if (s.Contains("love")) pos++;
            if (s.Contains("thanks") || s.Contains("thank you")) pos++;
            if (s.Contains("great") || s.Contains("awesome") || s.Contains("nice")) pos++;
            if (s.Contains("sweet") || s.Contains("cute") || s.Contains("yay")) pos++;

            int score = pos - neg;
            if (score > 1) return 1.0f;
            if (score == 1) return 0.75f;
            if (score == 0) return 0.5f;
            if (score == -1) return 0.35f;
            return 0.2f;
        }

        private float Weighted(float rawDelta)
        {
            if (rawDelta >= 0f)
                return rawDelta * MathF.Max(0f, _cfg.AffinityPositiveWeight);
            else
                return rawDelta * MathF.Max(0f, _cfg.AffinityNegativeWeight);
        }

        private static AffinityTier TierFor(float score)
        {
            // Simple thresholds; tune as desired
            if (score >= 120f) return AffinityTier.Eternal;
            if (score >= 80f) return AffinityTier.Bonded;
            if (score >= 50f) return AffinityTier.Confidant;
            if (score >= 25f) return AffinityTier.Friend;
            if (score >= 10f) return AffinityTier.Acquaintance;
            return AffinityTier.Stranger;
        }

        private sealed class SaveDto
        {
            public float score { get; set; }
            public int streak { get; set; }
            public DateTime lastDecayDate { get; set; }
            public string? lastEmotion { get; set; }
        }
    }
}
