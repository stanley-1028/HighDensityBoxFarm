// ============================================================================
// HDBF v2 | Layer: Core/Indoor | Owner: V2-CORE-01
// Purpose: The single source of every indoor balance number (INTERFACES section 1,
//          section 19.5-19.8). Formulas must reference these names, never literals.
// Requirements: REQ-V2-READ-02, REQ-V2-BAL-01, REQ-V2-EXT-01
// ============================================================================
namespace HDBF.Core.Indoor
{
    /// <summary>
    /// Compiled-in indoor balance constants (INTERFACES section 1). Values marked v2.1 follow the
    /// 2026-10-26 revision; tier-dependent values stay in <see cref="HDBF.Core.Balance.TierCatalog"/>.
    /// </summary>
    public static class IndoorConstants
    {
        /// <summary>Slots one farmer can tend: P_eff = min(SlotCapacity, ActiveWorkers x 38).</summary>
        public const int SlotsPerFarmer = 38;

        /// <summary>Farm actions (plant or harvest) one on-duty farmer can perform per game day.</summary>
        public const int ActionsPerFarmerDay = 13;

        /// <summary>Workdays one farm action costs in normal/fallow mode (0.08, no path term).</summary>
        public const float WorkDaysPerAction = 0.08f;

        /// <summary>Dense mode yield multiplier (G_mode = 1.35).</summary>
        public const float DenseYieldMultiplier = 1.35f;

        /// <summary>Dense mode water demand multiplier.</summary>
        public const float DenseWaterMultiplier = 1.50f;

        /// <summary>Dense mode fatigue gain multiplier.</summary>
        public const float DenseFatigueMultiplier = 1.50f;

        /// <summary>Dense mode work-cost multiplier (0.08 x 1.20 = 0.096 workdays per action).</summary>
        public const float DenseWorkMultiplier = 1.20f;

        /// <summary>Fallow mode water demand multiplier (30%).</summary>
        public const float FallowWaterMultiplier = 0.30f;

        /// <summary>Fallow mode work-cost multiplier: v2 has no path/fallow work discount (1.0).</summary>
        public const float FallowWorkMultiplier = 1f;

        /// <summary>Fatigue recovered per game day while the farm is in fallow mode.</summary>
        public const float FallowRecoveryPerDay = 0.03f;

        /// <summary>Multiplier used by normal mode (and by every non-dense/non-fallow switch).</summary>
        public const float NormalModeMultiplier = 1f;

        /// <summary>Base fatigue gained by harvesting the same crop again.</summary>
        public const float FatiguePerHarvest = 0.08f;

        /// <summary>G_fat = 1 - 0.60 x F (fatigue may reduce yield by at most 60%).</summary>
        public const float FatigueYieldPenalty = 0.60f;

        /// <summary>Fatigue increment multiplier when the harvested crop differs from the previous one.</summary>
        public const float RotationFatigueFactor = 0.40f;

        /// <summary>Fatigue recovered right after a rotated harvest.</summary>
        public const float RotationRecovery = 0.05f;

        /// <summary>Highest representable fatigue value (F is clamped to [0,1]).</summary>
        public const float MaxFatigue = 1f;

        /// <summary>G_power when a power-needing tier has no grid connection.</summary>
        public const float OffGridEfficiency = 0.60f;

        /// <summary>G_maint when exactly one maintenance cycle was missed.</summary>
        public const float OneCycleMissedEfficiency = 0.50f;

        /// <summary>Missed cycles from which the farm stalls (farm actions only; growth continues).</summary>
        public const int StalledAfterCycles = 2;

        /// <summary>
        /// Growth multiplier used instead of G_maint = 0 while the farm is stalled. REQ-V2-BAL-02
        /// explicitly keeps crops growing during a maintenance stall, so the zero maintenance layer
        /// is replaced by the v1 minimum growth rate (ADR-009, 20x slowdown) for the growth channel
        /// only; farm actions stay disabled.
        /// </summary>
        public const float StalledGrowthMultiplier = 0.05f;

        /// <summary>Material maintenance multiplier when the grid power of a power tier is cut (x2).</summary>
        public const float PowerCutMaintenanceMultiplier = 2f;

        /// <summary>Farm actions consumed by one manual water refill trip (worker fetch, no teleport).</summary>
        public const int ManualRefillActions = 2;

        /// <summary>Fraction of the tank filled by one manual refill trip (1/3 tank).</summary>
        public const float ManualRefillTankFraction = 1f / 3f;

        /// <summary>Maximum walk distance (cells) to a manual water source (section 19.6).</summary>
        public const int ManualRefillRadiusCells = 12;

        /// <summary>
        /// Vanilla good id of water. IL-verified against Timberborn 1.1.2.8:
        /// <c>WaterWorkshops.WaterGoodToWaterAmountConverter</c> static ctor stores the literal
        /// <c>"Water"</c> (see docs/API_FACTS.md evidence in this card's report).
        /// </summary>
        public const string WaterGoodId = "Water";

        /// <summary>
        /// Water volume of one HDBF water unit in m3. IL-verified: the same static ctor stores
        /// <c>WaterAmountConversion = 0.2</c>, i.e. one vanilla water good (one HDBF unit) is 0.2 m3,
        /// which is also the Timber Pipes unit mapping used by the v2 pipe source.
        /// </summary>
        public const float WaterVolumeCubicMetersPerUnit = 0.2f;

        /// <summary>Game days the mode switch transition lasts (lower yield during it; reserved).</summary>
        public const float ModeTransitionDays = 1f;

        /// <summary>Lower end of the G_irr ramp, as a fraction of C_min (G_irr = 0 below it).</summary>
        public const float IrrigationZeroPointRatio = 0.25f;

        /// <summary>Width of the G_irr ramp, as a fraction of C_min (0.75 x C_min).</summary>
        public const float IrrigationRampSpanRatio = 0.75f;

        /// <summary>Game days advanced by one daily tick (the controller ticks once per game day).</summary>
        public const float DayTickGameDays = 1f;

        /// <summary>Half-up offset used to convert a float yield into whole inventory items.</summary>
        public const float RoundHalfUp = 0.5f;

        /// <summary>
        /// Upper bound of the whole-item harvest of one slot: the highest vanilla crop base yield
        /// (3) x the dense yield multiplier (1.35), rounded half up = 4. Fatigue and a mode-transition
        /// scale can only reduce the yield, so no slot can ever produce more than this.
        /// </summary>
        public const int MaxHarvestItemsPerSlot = 4;

        /// <summary>
        /// Output-inventory capacity of one tier: the whole-box harvest bound
        /// <c>SlotCapacity x MaxHarvestItemsPerSlot</c> (60 -> 240, 150 -> 600, 300 -> 1200, 600 ->
        /// 2400). The vanilla output inventory is the only place harvested goods can go before
        /// haulers move them out, and the farm harvests in daily batches. A fixed vanilla-farmhouse
        /// capacity (50) therefore filled up after a few slots, blocked every further harvest and
        /// left a 600-slot farm transferring only ~50 items — the 2026-10-29 player bug report. The
        /// buffer is sized so one whole-box harvest always fits, which makes hauling (not the
        /// building buffer) the only rate limit; the blueprint data and tools/validate pin the same
        /// number per tier (TestOutputInventoryMatchesHarvestCapacityPerTier).
        /// </summary>
        /// <param name="slotCapacity">Slot capacity of the tier (PlotCapacity).</param>
        /// <returns>Per-good output inventory capacity; 0 for a non-positive slot capacity.</returns>
        public static int OutputCapacityFor(int slotCapacity)
        {
            if (slotCapacity <= 0)
            {
                return 0;
            }

            var capacity = (long)slotCapacity * MaxHarvestItemsPerSlot;
            return capacity > int.MaxValue ? int.MaxValue : (int)capacity;
        }

        /// <summary>Consecutive failures after which a subsystem is disabled (REQ-V2-SAFE-02, reserved).</summary>
        public const int SlotFaultDisableThreshold = 3;

        /// <summary>Slot flag bit 0: the slot is reserved by a worker (INTERFACES section 3).</summary>
        public const byte SlotFlagReservedByWorker = 1;

        /// <summary>Vanilla good id of the intermediate/advanced tier maintenance material.</summary>
        public const string MaintenanceItemPlank = "Plank";

        /// <summary>Vanilla good id of the advanced/top tier maintenance material.</summary>
        public const string MaintenanceItemMetal = "MetalBlock";

        /// <summary>Adapter state value written by a farm that never talked to the adapter.</summary>
        public const string AdapterStateNone = "None";
    }
}
