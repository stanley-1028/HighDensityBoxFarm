// ============================================================================
// HDBF v2 | Layer: Core/Indoor | Owner: V2-CORE-01
// Purpose: Immutable panel snapshot DTO (INTERFACES section 9) that UI reads instead
//          of game objects. Pure data record; no builder and no game references here.
// Requirements: REQ-V2-READ-04, REQ-V2-WATER-03, REQ-V2-UI-01
// ============================================================================
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
        string OutputInventoryPortState);
}
