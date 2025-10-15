namespace NunuTheAICompanion
{
    public sealed partial class Configuration
    {
        // -------- Adaptive Personality Gradients --------
        public bool PersonaAutoEnabled { get; set; } = true;
        public float PersonaBaselineDecayPerDay { get; set; } = 0.03f;

        // Baselines (-1..+1) — starting flavor
        public float PersonaBaselineWarmth { get; set; } = 0.20f;
        public float PersonaBaselineCuriosity { get; set; } = 0.15f;
        public float PersonaBaselineFormality { get; set; } = -0.10f;
        public float PersonaBaselinePlayfulness { get; set; } = 0.25f;
        public float PersonaBaselineGravitas { get; set; } = 0.10f;

        // Current live values (nullable => fall back to baseline on first run)
        public float? PersonaWarmth { get; set; } = null;
        public float? PersonaCuriosity { get; set; } = null;
        public float? PersonaFormality { get; set; } = null;
        public float? PersonaPlayfulness { get; set; } = null;
        public float? PersonaGravitas { get; set; } = null;
    }
}
