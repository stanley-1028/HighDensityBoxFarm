using System;
using System.Collections.Generic;
using HDBF.Core.Balance;

namespace HDBF.Core.Config
{
    /// <summary>
    /// Optional per-tier balance override (chapters 9.3, 18). Every property is nullable:
    /// <c>null</c> keeps the compiled-in <see cref="TierCatalog"/> value, so a partial config
    /// file can never silently import values a player did not write (chapter 10.3).
    /// </summary>
    public sealed record TierOverride
    {
        /// <summary><c>balance.tier[n].maxPlots</c>; null keeps the tier default.</summary>
        public int? PlotCapacity { get; init; }

        /// <summary><c>balance.tier[n].maxWorkers</c>; null keeps the tier default.</summary>
        public int? MaxWorkers { get; init; }

        /// <summary><c>balance.tier[n].range</c> square work-range edge; null keeps the tier default.</summary>
        public int? WorkRange { get; init; }

        /// <summary><c>balance.tier[n].minIrrigation</c>; null keeps the tier default.</summary>
        public float? MinIrrigationCoverage { get; init; }

        /// <summary><c>balance.tier[n].waterPerPlotDay</c>; null keeps the tier default.</summary>
        public float? WaterPerPlotPerDay { get; init; }

        /// <summary><c>balance.tier[n].tankDays</c>; null keeps the tier default.</summary>
        public int? TankCapacityDays { get; init; }

        /// <summary><c>balance.tier[n].fatigueRate</c>; null keeps the tier default.</summary>
        public float? SoilFatigueFactor { get; init; }

        /// <summary><c>balance.tier[n].scienceCost</c>; null keeps the tier default.</summary>
        public int? ScienceCost { get; init; }
    }

    /// <summary>
    /// Feature toggles from the chapter 9.3 table. Defaults are the shipped (balanced) values:
    /// only <c>LogisticsOutput</c> is off, because it requires the Conveyor Belts V2 adapter
    /// (table 10-3, REQ-SAFE-10).
    ///
    /// Toggles must never delete saved fields: disabling then re-enabling a feature restores the
    /// same behaviour (REQ-ARCH-09).
    /// </summary>
    public sealed record FeatureFlags
    {
        /// <summary>Dense planting mode (chapter 6.7 / chapter 9.3).</summary>
        public bool DensePlanting { get; init; } = true;

        /// <summary>Crop rotation fatigue relief (chapter 6.4).</summary>
        public bool Rotation { get; init; } = true;

        /// <summary>Maintenance cycles and shutdown (chapter 6.5).</summary>
        public bool Maintenance { get; init; } = true;

        /// <summary>Top-tier power connection with off-grid fallback (chapter 6.5).</summary>
        public bool PowerConnection { get; init; } = true;

        /// <summary>Timber Pipes water adapter (chapter 8.3).</summary>
        public bool PipeConnection { get; init; } = true;

        /// <summary>Conveyor Belts V2 output; off by default (table 10-3).</summary>
        public bool LogisticsOutput { get; init; } = false;

        /// <summary>Soil fatigue accumulation (chapter 6.4).</summary>
        public bool SoilFatigue { get; init; } = true;

        /// <summary>
        /// The one approved Harmony Postfix of ADR-009 / deviation D-2: makes
        /// <c>G_irr × G_fat × G_maint × G_power</c> stretch the growth time of crops on
        /// HDBF-managed plots. Default on; when off the patch is never installed and the mod runs
        /// exactly the pre-patch D-2 behaviour (dispatch gates + the existing growth channel).
        /// </summary>
        public bool GrowthMultiplierPatch { get; init; } = true;
    }

    /// <summary>
    /// Performance budget knobs, defaults from chapters 18.7/18.8 and table 10-5.
    /// All budgets are configurable (REQ-SAFE-18); exceeding one defers work instead of blocking
    /// the main thread (REQ-SAFE-17).
    /// </summary>
    public sealed record PerfSettings
    {
        /// <summary>Cells scanned per frame by the plot scan (chapter 18.2).</summary>
        public int ScanBudgetPerFrame { get; init; } = 128;

        /// <summary>Fallback plot re-scan interval in seconds (table 10-5).</summary>
        public float ScanIntervalSeconds { get; init; } = 2f;

        /// <summary>Irrigation coverage sample interval in seconds (table 10-5).</summary>
        public float IrrigationSampleInterval { get; init; } = 0.5f;

        /// <summary>UI panel refresh rate; bounded for REQ-UI-12.</summary>
        public int UiRefreshHz { get; init; } = 4;

        /// <summary>Building count that switches the mod to Reduced mode (REQ-SAFE-19).</summary>
        public int ReducedBuildingThreshold { get; init; } = 100;

        /// <summary>Steady-state per-frame budget target in milliseconds (chapter 18.8).</summary>
        public float SteadyFrameBudgetMs { get; init; } = 0.5f;

        /// <summary>Frame time that triggers a reduced-frequency warning (chapter 18.8).</summary>
        public float WarnFrameMs { get; init; } = 2f;

        /// <summary>Frame time that pauses non-core systems (chapter 18.8).</summary>
        public float PauseFrameMs { get; init; } = 5f;

        /// <summary>
        /// <c>perf.cropVisibilityIntervalSeconds</c>: how often (in seconds) a full sweep over the
        /// farm's known managed-plot cells starts to discover worker-planted crops (BUILD-05).
        /// Workers plant through the vanilla planting executor without notifying the mod, so the
        /// controller polls its own registered cells; the sweep is sliced by
        /// <see cref="CropVisibilityCellsPerFrame"/>, never run in one pass.
        /// </summary>
        public float CropVisibilityIntervalSeconds { get; init; } = 2f;

        /// <summary>
        /// <c>perf.cropVisibilityCellsPerFrame</c>: maximum number of registered plot cells whose
        /// crop is inspected in one frame by the BUILD-05 refresh (chapter 10.5/14.6 frame
        /// budget). A 600-plot farm therefore takes at most 10 frames per sweep at the default 64.
        /// </summary>
        public int CropVisibilityCellsPerFrame { get; init; } = 64;
    }

    /// <summary>
    /// Presentation switches and budgets of the chapter-14 UI (14.2 head-top warning icons,
    /// 14.6 performance). Defaults are the requirement's first-version values: icons on, P0 blink
    /// on, hide beyond 40 cells, aggregate every 0.5 s. Chapter 14.2 explicitly allows turning the
    /// blink off ("可在设置中关闭"); the shipped switch is this file section, not a second in-game
    /// settings screen.
    ///
    /// These keys are additive: existing <c>perf.*</c> keys keep their meaning, and a file without
    /// a <c>ui</c> section parses to exactly these defaults.
    /// </summary>
    public sealed record UiSettings
    {
        /// <summary><c>ui.worldIcons</c>: master switch for the world-space P0/P1 icons.</summary>
        public bool WorldIcons { get; init; } = true;

        /// <summary><c>ui.worldIconBlink</c>: chapter 14.5 P0 blink (≤1 Hz) on/off.</summary>
        public bool WorldIconBlink { get; init; } = true;

        /// <summary><c>ui.worldIconMaxDistance</c>: hide the icon beyond this many cells.</summary>
        public int WorldIconMaxDistance { get; init; } = 40;

        /// <summary><c>ui.worldIconIntervalSeconds</c>: head-top icon aggregation interval.</summary>
        public float WorldIconIntervalSeconds { get; init; } = 0.5f;

        /// <summary>
        /// <c>ui.worldIconFarmsPerTick</c>: farm snapshots rebuilt per icon pass (round-robin), so
        /// a screen full of farms stays inside the chapter-14.6 warning budget.
        /// </summary>
        public int WorldIconFarmsPerTick { get; init; } = 24;

        /// <summary>
        /// <c>ui.warningAggregateTtlSeconds</c>: how long an on-screen farm remains in the
        /// cross-building aggregation cache after its last evaluation (chapter 14.6 processes
        /// on-screen buildings only, so entries expire instead of covering the whole map).
        /// </summary>
        public float WarningAggregateTtlSeconds { get; init; } = 3f;

        /// <summary>
        /// <c>ui.hideManagedCrops</c>: BUILD-05 user design decision (2026-10-25) — the crops on
        /// this mod's managed plots are grown and harvested by the real vanilla crop entities but
        /// their models are hidden, so only the building's internal slots are visible from the
        /// ground. Default <c>true</c> (the design); <c>false</c> restores every managed crop
        /// visual and the mod looks exactly like the pre-BUILD-05 build. Only visibility is
        /// affected: growth, yield, harvesting, irrigation, fatigue and power are untouched.
        /// </summary>
        public bool HideManagedCrops { get; init; } = true;
    }

    /// <summary>
    /// Fully resolved mod configuration (chapters 9.3, 18). Immutable value object: the loader
    /// produces one instance and every subsystem reads it.
    ///
    /// <see cref="Default"/> is the compiled-in fallback used whenever the file is missing,
    /// unreadable or partially invalid (table 10-2, REQ-SAFE-10); it is also the exact balance
    /// state the acceptance tests are calibrated against.
    /// </summary>
    public sealed record HdbfConfig
    {
        /// <summary>Current configuration schema version (chapter 9.4 / REQ-ARCH-10).</summary>
        public const int CurrentSchemaVersion = 1;

        /// <summary>Config schema version stored in the file; independent of mod and save versions.</summary>
        public int ConfigSchemaVersion { get; init; } = CurrentSchemaVersion;

        /// <summary>Per-tier overrides; index is <see cref="HdbfTier"/>. Four entries, null = default.</summary>
        public IReadOnlyList<TierOverride?> TierOverrides { get; init; } = new TierOverride?[] { null, null, null, null };

        /// <summary>Feature toggles (chapter 9.3).</summary>
        public FeatureFlags Features { get; init; } = new FeatureFlags();

        /// <summary>Performance budgets (chapters 18.7/18.8).</summary>
        public PerfSettings Perf { get; init; } = new PerfSettings();

        /// <summary>UI presentation switches/budgets (chapter 14.2/14.6); additive section.</summary>
        public UiSettings Ui { get; init; } = new UiSettings();

        /// <summary>Dense mode yield multiplier (chapter 6.7).</summary>
        public float DenseYieldMultiplier { get; init; } = TierCatalog.DenseYieldMultiplier;

        /// <summary>Dense mode water multiplier (chapter 6.7).</summary>
        public float DenseWaterMultiplier { get; init; } = TierCatalog.DenseWaterMultiplier;

        /// <summary>Dense mode fatigue multiplier (chapter 6.7).</summary>
        public float DenseFatigueMultiplier { get; init; } = TierCatalog.DenseFatigueMultiplier;

        /// <summary>Dense mode work multiplier (chapter 6.7).</summary>
        public float DenseWorkMultiplier { get; init; } = TierCatalog.DenseWorkMultiplier;

        /// <summary>Fallow mode fatigue recovery per game day (chapter 6.4).</summary>
        public float FallowRecoveryPerDay { get; init; } = TierCatalog.FallowRecoveryPerDay;

        /// <summary>Yield penalty coefficient in <c>G_fat = 1 − penalty × F</c> (chapter 6.4).</summary>
        public float FatiguePenalty { get; init; } = TierCatalog.FatiguePenalty;

        /// <summary>Rotation increment factor applied to the fatigue gain (chapter 6.4).</summary>
        public float RotationFactor { get; init; } = TierCatalog.RotationFactor;

        /// <summary>Extra fatigue removed after a rotated harvest (chapter 6.4).</summary>
        public float RotationRecovery { get; init; } = TierCatalog.RotationRecovery;

        /// <summary>Conservative, fully balanced defaults; safe for any consumer (REQ-SAFE-10).</summary>
        public static HdbfConfig Default { get; } = new HdbfConfig();
    }
}
