using System;
using System.Collections.Generic;
using HDBF.Core;
using HDBF.Core.Abstractions;
using HDBF.Core.Balance;
using HDBF.Core.Config;

namespace HDBF.Gameplay.Components
{
    /// <summary>
    /// Read-only state source published by one live box farm to
    /// <see cref="HdbfFarmQueryService"/>. The only consumer is gameplay itself (the query
    /// service plus the plot claim registry), so UI never sees this type; UI talks to
    /// <see cref="IBoxFarmQuery"/> only (chapter 9.2 / REQ-ARCH-04).
    /// </summary>
    public interface IHdbfBoxFarmSnapshotSource
    {
        /// <summary>Runtime entity id used as the UI lookup key.</summary>
        Guid EntityId { get; }

        /// <summary>Save-file identity (the long stored in <c>HdbfBuildingRecord</c>).</summary>
        long SaveEntityId { get; }

        /// <summary>Builds a fresh immutable snapshot. Must not throw; the caller isolates it.</summary>
        BoxFarmSnapshot CreateSnapshot();

        /// <summary>
        /// Builds the warning evaluation input for an already-created snapshot, adding the data
        /// that deliberately does not live in <see cref="BoxFarmSnapshot"/> (adapter health,
        /// configured hysteresis window).
        /// </summary>
        HdbfWarningInputs CreateWarningInputs(BoxFarmSnapshot snapshot);

        /// <summary>
        /// Growth-rate multiplier (<c>G_irr × G_fat × G_maint × G_power</c>, mapped through
        /// <see cref="HDBF.Core.Balance.BalanceFormulas.GrowthRateMultiplier"/>) for one registered
        /// plot. Consumed by the ADR-009 Harmony patch through
        /// <see cref="HDBF.Core.Growth.IGrowthMultiplierSource"/>; returns false for a plot this
        /// farm does not manage, and never throws.
        /// </summary>
        bool TryGetPlotGrowthRate(int plotId, out float growthRate);
    }

    /// <summary>
    /// Session-wide plot ownership and farm lookup index (REQ-BLD-04 first-come-first-served,
    /// REQ-BLD-05 release on demolition). One cell belongs to at most one box farm.
    ///
    /// Implemented by the game-context singleton; all calls are main-thread only.
    /// </summary>
    public interface IHdbfFarmRegistry
    {
        /// <summary>Makes a farm queryable by UI and diagnostics; replaces any earlier source.</summary>
        void RegisterSource(IHdbfBoxFarmSnapshotSource source);

        /// <summary>Removes a farm and releases every plot it owned (demolition, chapter 5.5).</summary>
        void UnregisterSource(Guid entityId);

        /// <summary>Number of live farms; drives the Reduced-mode threshold (REQ-SAFE-19).</summary>
        int RegisteredFarmCount { get; }

        /// <summary>True when the plot belongs to a different farm (scan skips it).</summary>
        bool IsPlotClaimedByOther(int plotId, Guid ownerEntityId);

        /// <summary>
        /// Resolves the farm that currently owns a managed plot. Used by the ADR-009 growth patch
        /// lookup (cell → farm) and by diagnostics; returns false for unclaimed plots.
        /// </summary>
        bool TryGetPlotOwner(int plotId, out Guid ownerEntityId);

        /// <summary>
        /// Replaces the owner's claimed set with the plots it can actually hold, dropping claims
        /// it no longer uses and refusing plots another farm registered first. Returns the
        /// granted ids in the requested order.
        /// </summary>
        int[] ApplyPlotSelection(Guid ownerEntityId, IReadOnlyList<int> desiredPlotIds);

        /// <summary>Releases every plot owned by the farm without unregistering it.</summary>
        void ReleasePlots(Guid ownerEntityId);
    }

    /// <summary>
    /// Resolved balance data for gameplay. Implemented by
    /// <see cref="HdbfSystemRegistrar"/>: it loads <c>HDBF_Config.json</c> once, applies config
    /// overrides on top of <see cref="TierCatalog"/> and exposes the resulting table
    /// (REQ-IMPL-03, chapter 18). Gameplay reads balance values from here, never from a tier
    /// constant embedded in a branch (REQ-ARCH-07).
    /// </summary>
    public interface IHdbfGameConfigProvider
    {
        /// <summary>Fully resolved configuration (defaults when the file is absent or broken).</summary>
        HdbfConfig Config { get; }

        /// <summary>Config-resolved tier table, four entries indexed by <see cref="HdbfTier"/>.</summary>
        IReadOnlyList<TierProfile> Profiles { get; }

        /// <summary>
        /// Looks a profile up by blueprint id. Returns false when the blueprint is unknown; the
        /// <paramref name="profile"/> is then the tier fallback (never throws).
        /// </summary>
        bool TryGetTierProfile(string? blueprintId, HdbfTier fallbackTier, out TierProfile profile);
    }

    /// <summary>
    /// Inputs for one warning evaluation pass (chapter 14.2). Deliberately a plain value type:
    /// it is derived from <see cref="BoxFarmSnapshot"/> plus the few fields the snapshot does
    /// not expose, which keeps <see cref="HdbWarningEvaluator"/> free of game objects.
    /// </summary>
    public readonly struct HdbfWarningInputs
    {
        /// <summary>P0: maintenance shutdown is active for this farm.</summary>
        public bool IsShutdown { get; init; }

        /// <summary>False when nothing is registered; suppresses coverage warnings.</summary>
        public bool HasRegisteredPlots { get; init; }

        /// <summary>Latest cached irrigation coverage (0..1).</summary>
        public float IrrigationCoverage { get; init; }

        /// <summary>Tier minimum coverage C_min (0..1); 0 disables irrigation warnings.</summary>
        public float MinIrrigationCoverage { get; init; }

        /// <summary>True for the tier that needs power (chapter 6.5).</summary>
        public bool RequiresPower { get; init; }

        /// <summary>True when the farm is on the power network right now.</summary>
        public bool PowerConnected { get; init; }

        /// <summary>Consecutive missed maintenance cycles (0 = on schedule).</summary>
        public int CyclesMissed { get; init; }

        /// <summary>Highest fatigue over the registered plots (0..1).</summary>
        public float FatigueMax { get; init; }

        /// <summary>Days the tank can still supply at the current draw (0 when it cannot).</summary>
        public float TankDaysLeft { get; init; }

        /// <summary>
        /// Plots currently fed by the tank. The P4 reserve warning is only meaningful while the
        /// tank is actually supplying dry plots (chapter 6.3).
        /// </summary>
        public int TankFedPlots { get; init; }

        /// <summary>Full-load tank capacity in days, the P4 reference value.</summary>
        public int TankCapacityDays { get; init; }

        /// <summary>Adapters that reached the failure threshold (REQ-COMPAT-07).</summary>
        public int AdapterFailedCount { get; init; }

        /// <summary>Adapters disabled because their mod is missing (REQ-COMPAT-09).</summary>
        public int AdapterDisabledCount { get; init; }

        /// <summary>
        /// Wall-clock seconds a warning must persist before it appears or disappears (chapter 14.2
        /// two-second hysteresis, default <see cref="HdbfWarningRules.HysteresisSeconds"/>). The
        /// gate is time-based, not sample-based, so the panel's 4 Hz refresh and the world icon's
        /// 2 Hz pass observe the same window.
        /// </summary>
        public float HysteresisSeconds { get; init; }
    }
}
