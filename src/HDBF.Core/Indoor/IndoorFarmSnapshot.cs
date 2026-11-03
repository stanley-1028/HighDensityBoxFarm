// ============================================================================
// HDBF v2 | Layer: Core/Indoor | Owner: V2-CORE-01
// Purpose: Immutable panel snapshot DTO (INTERFACES section 9) that UI reads instead
//          of game objects. Pure data record; no builder and no game references here.
// Requirements: REQ-V2-READ-04, REQ-V2-WATER-03, REQ-V2-UI-01, REQ-V2-UI-05
// ============================================================================
using System;
using System.Collections.Generic;
using HDBF.Core.Balance;

namespace HDBF.Core.Indoor
{
    /// <summary>
    /// Immutable panel snapshot of one indoor farm (INTERFACES section 9). UI and tests read only
    /// this DTO, never a game object; empty data is null/NaN and renders as "—" (REQ-V2-READ-04).
    /// </summary>
    /// <param name="Tier">Tier of the building.</param>
    /// <param name="Faction">Faction of the building.</param>
    /// <param name="Workers">Workers currently on duty.</param>
    /// <param name="MaxWorkers">Worker cap of the tier.</param>
    /// <param name="Mode">Current operating mode.</param>
    /// <param name="ModeTransitioning">True while the 1-day mode transition runs.</param>
    /// <param name="Capacity">Slot capacity of the tier.</param>
    /// <param name="ConfiguredSlots">Slots the player configured (0 when unknown).</param>
    /// <param name="EffectiveSlots">P_eff = min(Capacity, Workers x 38).</param>
    /// <param name="EmptySlots">Slots with no crop.</param>
    /// <param name="GrowingSlots">Slots with a growing crop.</param>
    /// <param name="MatureSlots">Slots waiting for harvest.</param>
    /// <param name="BacklogActions">Pending actions the current crew cannot reach.</param>
    /// <param name="Tank">Tank content in water units.</param>
    /// <param name="TankCapacity">Tank capacity of the tier.</param>
    /// <param name="TankDaysRemaining">Full-load days the tank can support (infinity renders "—").</param>
    /// <param name="Coverage">Coverage C in [0,1].</param>
    /// <param name="MinCoverage">Tier minimum coverage C_min.</param>
    /// <param name="WaterSupply">Where water comes from (none/pipe/manual).</param>
    /// <param name="MaintenanceDueDay">Next material maintenance due day.</param>
    /// <param name="MaintenanceCyclesMissed">Missed maintenance cycles.</param>
    /// <param name="MaintenanceMaterialMissing">True when the upkeep material is missing.</param>
    /// <param name="Power">Power contract of the building.</param>
    /// <param name="PowerConnected">True while grid power is up.</param>
    /// <param name="CropPolicy">Rotation or fixed crop policy.</param>
    /// <param name="FixedCropId">Fixed crop id when the policy is fixed.</param>
    /// <param name="RotationSequence">Human readable rotation sequence for the tooltip.</param>
    /// <param name="OutputOccupied">Items currently held by the output inventory.</param>
    /// <param name="OutputPortConnected">True when the logistics output port is connected.</param>
    /// <param name="PipePort">True when the pipe port exists.</param>
    /// <param name="LogisticsPort">True when the logistics port exists.</param>
    /// <param name="PowerPort">True when the power port exists.</param>
    /// <param name="Warnings">Aggregated problem flags of the last tick.</param>
    /// <param name="OutputInventoryPortState">Adapter state string of the output port.</param>
    public sealed record IndoorFarmSnapshot(
        HdbfTier Tier,
        HdbfFaction Faction,
        int Workers,
        int MaxWorkers,
        IndoorMode Mode,
        bool ModeTransitioning,
        int Capacity,
        int ConfiguredSlots,
        int EffectiveSlots,
        int EmptySlots,
        int GrowingSlots,
        int MatureSlots,
        int BacklogActions,
        float Tank,
        float TankCapacity,
        float TankDaysRemaining,
        float Coverage,
        float MinCoverage,
        WaterSupplyKind WaterSupply,
        float MaintenanceDueDay,
        int MaintenanceCyclesMissed,
        bool MaintenanceMaterialMissing,
        PowerConnection Power,
        bool PowerConnected,
        CropPolicy CropPolicy,
        string FixedCropId,
        string RotationSequence,
        int OutputOccupied,
        bool OutputPortConnected,
        bool PipePort,
        bool LogisticsPort,
        bool PowerPort,
        IndoorWarningFlags Warnings,
        string OutputInventoryPortState)
    {
        /// <summary>
        /// Live per-crop status of the slots (REQ-V2-UI-05): one row per crop that currently holds
        /// at least one slot, ordered by the faction crop table. Empty when nothing is planted, so
        /// the panel card can show its "nothing planted" state instead of disappearing. Kept as an
        /// init property rather than a positional parameter so existing constructors/tests keep
        /// compiling; gameplay fills it through <see cref="IndoorCropReport.Build"/>.
        /// </summary>
        public IReadOnlyList<IndoorCropStatus> Crops { get; init; } = Array.Empty<IndoorCropStatus>();

        /// <summary>True while the player paused the building (WP-04: tick, drain and icons stop).</summary>
        public bool Paused { get; init; }

        /// <summary>True when the farm pays material maintenance automatically (WP-02).</summary>
        public bool AutoMaintenance { get; init; } = true;

        /// <summary>Maintenance cycles one click/automatic pass would settle right now (WP-01).</summary>
        public int MaintenanceCyclesToCatchUp { get; init; }

        /// <summary>Material count of the catch-up bill shown to the player (0 when nothing is due).</summary>
        public int MaintenanceCatchUpMaterialAmount { get; init; }

        /// <summary>Single highest-priority reason the farm is not doing farm work (WP-06).</summary>
        public IndoorIdleReason IdleReason { get; init; } = IndoorIdleReason.None;

        /// <summary>True when a manual refill source is reachable within the manual radius (WP-06/L-03).</summary>
        public bool ManualRefillSourceAvailable { get; init; }

        /// <summary>Read-only pipe probe result when a pipe source is present; Unknown otherwise (WP-05).</summary>
        public PipeProbe PipeProbeState { get; init; } = PipeProbe.Unknown;

        /// <summary>
        /// Config-resolved tier profile the building actually runs with (L-15). Gameplay fills the
        /// same profile the daily simulation uses, so JSON overrides of <c>balance.tier</c> reach the
        /// panel; when it is null (older snapshots/tests) the panel falls back to the compiled
        /// <see cref="TierCatalog.Get"/> table and keeps its previous behaviour.
        /// </summary>
        public TierProfile? Profile { get; init; }

        /// <summary>
        /// Live crop list of the building faction (L-12). Gameplay fills it from the game-backed
        /// crop catalog, so a crop added by a crop mod is a valid fixed-crop choice and shows its
        /// own localized name. Empty when the live catalog is unavailable; the panel then falls back
        /// to <see cref="FallbackCropCatalog"/> and its built-in name table.
        /// </summary>
        public IReadOnlyList<IndoorCropChoice> CropChoices { get; init; } =
            Array.Empty<IndoorCropChoice>();
    }
}
