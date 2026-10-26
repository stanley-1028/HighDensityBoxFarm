namespace HDBF.Core.Config
{
    /// <summary>
    /// Named clamp ranges for every configurable value, taken from chapter 18 "可调参数清单".
    ///
    /// Public so the blueprint/spec validator, docs generation and UI tooltips can reference the
    /// same numbers as <see cref="HdbfConfigCodec"/> instead of duplicating literals.
    ///
    /// Two values are tighter than the document's suggested range because a hard requirement
    /// wins over a tuning hint:
    /// * work range is capped at 27 cells (REQ-BAL-18 forbids configs that reach the whole map);
    /// * plot capacity keeps chapter 18's 30–1000 window; the save codec uses the resolved
    ///   per-tier capacity as its hard cap (chapter 10.5) — see the comment on
    ///   <see cref="PlotCapacityMin"/>.
    ///
    /// Unit tests deliberately assert literal chapter-18 numbers instead of these constants, so
    /// a typo here cannot make a test pass by comparing the constant with itself.
    /// </summary>
    public static class HdbfConfigRanges
    {
        public const int SchemaVersionMin = 0;
        public const int SchemaVersionMax = HdbfConfig.CurrentSchemaVersion;

        // chapter 18.2 — maximum managed plots. Three independent constraints coexist here; do
        // not collapse them into one number:
        //   1. chapter 5 tier defaults: 60 / 150 / 300 / 600 (TierCatalog, acceptance baseline);
        //   2. chapter 18.2 config override window: 30..1000 (the clamp below — a player may
        //      tune within the documented window, up to 1000 for the v0.2 density experiment);
        //   3. REQ-IMPL-02 blueprint validation: the four shipped blueprints must keep their
        //      defaults inside 60..600 (Gameplay-side validator, not a config clamp).
        public const int PlotCapacityMin = 30;
        public const int PlotCapacityMax = 1000;

        // chapter 18.2 — square work-range edge. The document suggests 7..35, but REQ-BAL-18
        // hard-caps the range at 27x27 so a config can never scan the whole map.
        public const int WorkRangeMin = 7;
        public const int WorkRangeMax = 27;

        // chapter 18.2 — range scan budget per frame (table 10-5).
        public const int ScanBudgetPerFrameMin = 32;
        public const int ScanBudgetPerFrameMax = 512;

        // chapter 18.3
        public const int MaxWorkersMin = 1;
        public const int MaxWorkersMax = 40;

        // chapter 18.4
        public const float MinIrrigationMin = 0.30f;
        public const float MinIrrigationMax = 0.95f;
        public const float WaterPerPlotPerDayMin = 0.005f;
        public const float WaterPerPlotPerDayMax = 0.08f;
        public const int TankCapacityDaysMin = 1;
        public const int TankCapacityDaysMax = 10;

        // chapter 18.5
        public const float SoilFatigueFactorMin = 0.5f;
        public const float SoilFatigueFactorMax = 2.0f;

        // chapter 18.1
        public const int ScienceCostMin = 100;
        public const int ScienceCostMax = 5000;

        // chapter 18.7 balance multipliers
        public const float DenseYieldMin = 1.10f;
        public const float DenseYieldMax = 1.60f;
        public const float DenseWaterMin = 1.10f;
        public const float DenseWaterMax = 2.00f;
        public const float DenseFatigueMin = 1.00f;
        public const float DenseFatigueMax = 2.00f;
        public const float DenseWorkMin = 1.00f;
        public const float DenseWorkMax = 1.50f;
        public const float FallowRecoveryMin = 0.01f;
        public const float FallowRecoveryMax = 0.10f;
        public const float FatiguePenaltyMin = 0.20f;
        public const float FatiguePenaltyMax = 0.90f;
        public const float RotationFactorMin = 0.10f;
        public const float RotationFactorMax = 1.00f;
        public const float RotationRecoveryMin = 0.00f;
        public const float RotationRecoveryMax = 0.20f;

        // chapter 18.7 performance
        public const float ScanIntervalSecondsMin = 1f;
        public const float ScanIntervalSecondsMax = 5f;
        public const float IrrigationSampleIntervalMin = 0.25f;
        public const float IrrigationSampleIntervalMax = 2f;
        public const int UiRefreshHzMin = 1;
        public const int UiRefreshHzMax = 10;

        // chapter 18.8 performance
        public const int ReducedBuildingThresholdMin = 20;
        public const int ReducedBuildingThresholdMax = 300;
        public const float SteadyFrameBudgetMsMin = 0.2f;
        public const float SteadyFrameBudgetMsMax = 2.0f;
        public const float WarnFrameMsMin = 1f;
        public const float WarnFrameMsMax = 5f;
        public const float PauseFrameMsMin = 3f;
        public const float PauseFrameMsMax = 10f;

        // chapter 14.2/14.6 UI (world warning icons). The requirement calls the 40-cell distance,
        // the 0.5 s icon interval and the ≤1 Hz blink first-version values ("初版可调"), so each is
        // read from here instead of being a hard-coded literal in gameplay.
        public const int WorldIconMaxDistanceMin = 10;
        public const int WorldIconMaxDistanceMax = 120;
        public const float WorldIconIntervalSecondsMin = 0.25f;
        public const float WorldIconIntervalSecondsMax = 2f;
        public const int WorldIconFarmsPerTickMin = 1;
        public const int WorldIconFarmsPerTickMax = 200;
        public const float WarningAggregateTtlSecondsMin = 1f;
        public const float WarningAggregateTtlSecondsMax = 30f;

        // BUILD-05 managed-crop visual refresh. The interval matches the chapter-18 performance
        // policy for the plot scan (1-5 s) and is widened to 10 s because the crop refresh is only
        // discovery latency, not farm state: a worker-planted crop may stay visible for at most one
        // interval plus one sliced sweep. The per-frame cell budget is capped at 512 so a single
        // frame can still never walk a whole 600-plot top tier.
        public const float CropVisibilityIntervalSecondsMin = 1f;
        public const float CropVisibilityIntervalSecondsMax = 10f;
        public const int CropVisibilityCellsPerFrameMin = 8;
        public const int CropVisibilityCellsPerFrameMax = 512;
    }
}
