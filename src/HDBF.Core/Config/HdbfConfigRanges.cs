namespace HDBF.Core.Config
{
    /// <summary>
    /// Named clamp ranges for every configurable value of config schema 2, taken from chapter 18
    /// "可调参数清单".
    ///
    /// Public so the blueprint/spec validator, docs generation and UI tooltips can reference the
    /// same numbers as <see cref="HdbfConfigCodec"/> instead of duplicating literals.
    ///
    /// The v1 scan/irrigation/crop-visibility ranges were deleted with their keys (section 19.2);
    /// the tier slot range keeps chapter 18's 30–1000 window, and the parameter values are
    /// deliberately literals in the unit tests so a typo here cannot make a test pass by
    /// comparing a constant with itself.
    /// </summary>
    public static class HdbfConfigRanges
    {
        /// <summary>Lowest accepted schema version (0 is treated as legacy garbage).</summary>
        public const int SchemaVersionMin = 0;

        /// <summary>Highest accepted schema version (the current one).</summary>
        public const int SchemaVersionMax = HdbfConfig.CurrentSchemaVersion;

        // chapter 5 tier defaults: 60 / 150 / 300 / 600 (TierCatalog, acceptance baseline);
        // chapter 18.2 allows a player override inside the 30..1000 window.
        /// <summary>Lowest accepted per-tier slot override.</summary>
        public const int SlotCapacityMin = 30;

        /// <summary>Highest accepted per-tier slot override.</summary>
        public const int SlotCapacityMax = 1000;

        /// <summary>Lowest accepted worker cap override.</summary>
        public const int MaxWorkersMin = 1;

        /// <summary>Highest accepted worker cap override.</summary>
        public const int MaxWorkersMax = 40;

        /// <summary>Lowest accepted minimum coverage C_min.</summary>
        public const float MinIrrigationMin = 0.30f;

        /// <summary>Highest accepted minimum coverage C_min.</summary>
        public const float MinIrrigationMax = 0.95f;

        /// <summary>Lowest accepted water use per slot per day.</summary>
        public const float WaterPerSlotPerDayMin = 0.005f;

        /// <summary>Highest accepted water use per slot per day.</summary>
        public const float WaterPerSlotPerDayMax = 0.08f;

        /// <summary>Lowest accepted tank capacity in full-load days.</summary>
        public const int TankCapacityDaysMin = 1;

        /// <summary>Highest accepted tank capacity in full-load days.</summary>
        public const int TankCapacityDaysMax = 10;

        /// <summary>Lowest accepted tier fatigue factor.</summary>
        public const float SoilFatigueFactorMin = 0.5f;

        /// <summary>Highest accepted tier fatigue factor.</summary>
        public const float SoilFatigueFactorMax = 2.0f;

        /// <summary>Lowest accepted science cost override.</summary>
        public const int ScienceCostMin = 100;

        /// <summary>Highest accepted science cost override.</summary>
        public const int ScienceCostMax = 5000;

        // chapter 18.7 balance multipliers
        /// <summary>Lowest accepted dense yield multiplier.</summary>
        public const float DenseYieldMin = 1.10f;

        /// <summary>Highest accepted dense yield multiplier.</summary>
        public const float DenseYieldMax = 1.60f;

        /// <summary>Lowest accepted dense water multiplier.</summary>
        public const float DenseWaterMin = 1.10f;

        /// <summary>Highest accepted dense water multiplier.</summary>
        public const float DenseWaterMax = 2.00f;

        /// <summary>Lowest accepted dense fatigue multiplier.</summary>
        public const float DenseFatigueMin = 1.00f;

        /// <summary>Highest accepted dense fatigue multiplier.</summary>
        public const float DenseFatigueMax = 2.00f;

        /// <summary>Lowest accepted dense work multiplier.</summary>
        public const float DenseWorkMin = 1.00f;

        /// <summary>Highest accepted dense work multiplier.</summary>
        public const float DenseWorkMax = 1.50f;

        /// <summary>Lowest accepted fallow recovery per day.</summary>
        public const float FallowRecoveryMin = 0.01f;

        /// <summary>Highest accepted fallow recovery per day.</summary>
        public const float FallowRecoveryMax = 0.10f;

        /// <summary>Lowest accepted fatigue penalty coefficient.</summary>
        public const float FatiguePenaltyMin = 0.20f;

        /// <summary>Highest accepted fatigue penalty coefficient.</summary>
        public const float FatiguePenaltyMax = 0.90f;

        /// <summary>Lowest accepted rotation factor.</summary>
        public const float RotationFactorMin = 0.10f;

        /// <summary>Highest accepted rotation factor.</summary>
        public const float RotationFactorMax = 1.00f;

        /// <summary>Lowest accepted rotation recovery.</summary>
        public const float RotationRecoveryMin = 0.00f;

        /// <summary>Highest accepted rotation recovery.</summary>
        public const float RotationRecoveryMax = 0.20f;

        // chapter 18.7 performance
        /// <summary>Lowest accepted UI refresh rate.</summary>
        public const int UiRefreshHzMin = 1;

        /// <summary>Highest accepted UI refresh rate.</summary>
        public const int UiRefreshHzMax = 10;

        // chapter 18.8 performance
        /// <summary>Lowest accepted reduced-mode building threshold.</summary>
        public const int ReducedBuildingThresholdMin = 20;

        /// <summary>Highest accepted reduced-mode building threshold.</summary>
        public const int ReducedBuildingThresholdMax = 300;

        /// <summary>Lowest accepted steady-state frame budget.</summary>
        public const float SteadyFrameBudgetMsMin = 0.2f;

        /// <summary>Highest accepted steady-state frame budget.</summary>
        public const float SteadyFrameBudgetMsMax = 2.0f;

        /// <summary>Lowest accepted warning frame time.</summary>
        public const float WarnFrameMsMin = 1f;

        /// <summary>Highest accepted warning frame time.</summary>
        public const float WarnFrameMsMax = 5f;

        /// <summary>Lowest accepted pause frame time.</summary>
        public const float PauseFrameMsMin = 3f;

        /// <summary>Highest accepted pause frame time.</summary>
        public const float PauseFrameMsMax = 10f;

        // section 19.11/19.13 world warning icons; the requirement calls the 40-cell distance and
        // the <=1 Hz blink first-version values ("初版可调"), so gameplay reads them from config.
        /// <summary>Lowest accepted icon culling distance in cells.</summary>
        public const int WorldIconMaxDistanceMin = 10;

        /// <summary>Highest accepted icon culling distance in cells.</summary>
        public const int WorldIconMaxDistanceMax = 120;

        /// <summary>Lowest accepted icon refresh interval (the pass caps at 1 s).</summary>
        public const float WorldIconIntervalSecondsMin = 0.25f;

        /// <summary>Highest accepted icon refresh interval.</summary>
        public const float WorldIconIntervalSecondsMax = 2f;

        /// <summary>Lowest accepted farms-per-icon-pass budget.</summary>
        public const int WorldIconFarmsPerTickMin = 1;

        /// <summary>Highest accepted farms-per-icon-pass budget.</summary>
        public const int WorldIconFarmsPerTickMax = 200;
    }
}
