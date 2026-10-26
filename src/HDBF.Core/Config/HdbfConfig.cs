using System;
using System.Collections.Generic;
using HDBF.Core.Balance;

namespace HDBF.Core.Config
{
    /// <summary>
    /// Optional per-tier balance override (chapter 9.3, 18). Every property is nullable:
    /// <c>null</c> keeps the compiled-in <see cref="TierCatalog"/> value, so a partial config
    /// file can never silently import values a player did not write (chapter 10.3).
    ///
    /// Schema 2 uses the v2 slot terminology (<c>slotCapacity</c>, <c>waterPerSlotPerDay</c>);
    /// a schema-1 file's plot-named keys are renamed during migration (REQ-V2-EXT-04).
    /// </summary>
    public sealed record TierOverride
    {
        /// <summary><c>balance.tier[n].slotCapacity</c>; null keeps the tier default.</summary>
        public int? SlotCapacity { get; init; }

        /// <summary><c>balance.tier[n].maxWorkers</c>; null keeps the tier default.</summary>
        public int? MaxWorkers { get; init; }

        /// <summary><c>balance.tier[n].minIrrigationCoverage</c>; null keeps the tier default.</summary>
        public float? MinIrrigationCoverage { get; init; }

        /// <summary><c>balance.tier[n].waterPerSlotPerDay</c>; null keeps the tier default.</summary>
        public float? WaterPerSlotPerDay { get; init; }

        /// <summary><c>balance.tier[n].tankCapacityDays</c>; null keeps the tier default.</summary>
        public int? TankCapacityDays { get; init; }

        /// <summary><c>balance.tier[n].soilFatigueFactor</c>; null keeps the tier default.</summary>
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
    /// same behaviour (REQ-ARCH-09). The v1 <c>growthMultiplierPatch</c> toggle was deleted with
    /// the ADR-009 patch (section 19.18.3-B5: v2 ships 0 Harmony patches).
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
    }

    /// <summary>
    /// Performance budget knobs of the v2 mod (chapters 18.7/18.8 and table 10-5). The v1
    /// scan/irrigation/crop-visibility budgets were deleted with the world-plot chain (section
    /// 19.2); the remaining keys are the general frame/building-count budgets.
    /// </summary>
    public sealed record PerfSettings
    {
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
    }

    /// <summary>
    /// Presentation switches of the four v2 world warning icons (section 19.11/19.13). Defaults
    /// are the requirement's first-version values: icons on, P0 blink on, hide beyond 40 cells,
    /// refresh at most once per second. Section 14.2 explicitly allows turning the blink off
    /// ("可在设置中关闭"); the shipped switch is this file section, not an in-game settings screen.
    ///
    /// The v1 managed-crop visibility switch and the cross-building warning TTL were deleted with
    /// their subsystems (sections 19.2/19.18.1).
    /// </summary>
    public sealed record UiSettings
    {
        /// <summary><c>ui.worldIcons</c>: master switch for the world-space P0/P1 icons.</summary>
        public bool WorldIcons { get; init; } = true;

        /// <summary><c>ui.worldIconBlink</c>: chapter 14.5 P0 blink (&lt;=1 Hz) on/off.</summary>
        public bool WorldIconBlink { get; init; } = true;

        /// <summary><c>ui.worldIconMaxDistance</c>: hide the icon beyond this many cells.</summary>
        public int WorldIconMaxDistance { get; init; } = 40;

        /// <summary><c>ui.worldIconIntervalSeconds</c>: head-top icon refresh interval (capped at 1 s).</summary>
        public float WorldIconIntervalSeconds { get; init; } = 0.5f;

        /// <summary>
        /// <c>ui.worldIconFarmsPerTick</c>: farm snapshots rebuilt per icon pass (round-robin), so
        /// a screen full of farms stays inside the warning budget.
        /// </summary>
        public int WorldIconFarmsPerTick { get; init; } = 24;
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
        /// <summary>Current configuration schema version (chapter 9.4 / REQ-V2-EXT-04).</summary>
        public const int CurrentSchemaVersion = 2;

        /// <summary>Legacy schema version this build can migrate from (REQ-V2-SAFE-06).</summary>
        public const int MigratableSchemaVersion = 1;

        /// <summary>Config schema version stored in the file; independent of mod and save versions.</summary>
        public int ConfigSchemaVersion { get; init; } = CurrentSchemaVersion;

        /// <summary>Per-tier overrides; index is <see cref="HdbfTier"/>. Four entries, null = default.</summary>
        public IReadOnlyList<TierOverride?> TierOverrides { get; init; } = new TierOverride?[] { null, null, null, null };

        /// <summary>Feature toggles (chapter 9.3).</summary>
        public FeatureFlags Features { get; init; } = new FeatureFlags();

        /// <summary>Performance budgets (chapters 18.7/18.8).</summary>
        public PerfSettings Perf { get; init; } = new PerfSettings();

        /// <summary>World warning icon switches (section 19.11); additive section.</summary>
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
