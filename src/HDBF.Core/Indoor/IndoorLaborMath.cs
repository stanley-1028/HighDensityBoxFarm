// ============================================================================
// HDBF v2 | Layer: Core/Indoor | Owner: V2-CORE-01 (scheduler) / V2-GAMEPLAY-02 (budget)
// Purpose: Labour capacity, action budget, per-action work cost and the deterministic
//          harvest-before-plant action scheduler (INTERFACES section 5, section 19.7).
//          The mode-transition yield scale and the reserved-action budget are the
//          backward-compatible additions of V2-GAMEPLAY-02 (REQ-V2-WATER-04/LABOR-01);
//          the water-fed auto-fill planting mode is the 2026-10-28 player request.
// Requirements: REQ-V2-LABOR-01, REQ-V2-LABOR-02, REQ-V2-CORE-01, REQ-V2-CORE-05,
//               REQ-V2-WATER-04, REQ-V2-BAL-03
// ============================================================================
using System;
using System.Collections.Generic;
using HDBF.Core.Balance;

namespace HDBF.Core.Indoor
{
    /// <summary>
    /// Indoor labour mathematics and the daily action scheduler (INTERFACES section 5).
    /// Deterministic by construction: ascending slot index, no randomness, no dictionary order.
    /// </summary>
    public static partial class IndoorLaborMath
    {
        /// <summary>
        /// P_eff = min(SlotCapacity, ActiveWorkers x 38); only these leading slots are scheduled
        /// (REQ-V2-CORE-01).
        /// </summary>
        /// <param name="tier">Tier profile that supplies the slot capacity.</param>
        /// <param name="activeWorkers">On-duty workers of the building.</param>
        /// <returns>Effective slot count; 0 without tier or workers.</returns>
        public static int EffectiveSlots(TierProfile tier, int activeWorkers)
        {
            if (tier is null || activeWorkers <= 0) return 0;
            var capacity = tier.SlotCapacity;
            if (capacity <= 0) return 0;
            var allowed = (long)activeWorkers * IndoorConstants.SlotsPerFarmer;
            return (int)Math.Min(capacity, allowed);
        }

        /// <summary>Daily action budget = ActiveWorkers x 13 (REQ-V2-LABOR-01).</summary>
        /// <param name="activeWorkers">On-duty workers of the building.</param>
        /// <returns>Plant+harvest actions available today; 0 without workers.</returns>
        public static int ActionBudget(int activeWorkers)
        {
            if (activeWorkers <= 0) return 0;
            var total = (long)activeWorkers * IndoorConstants.ActionsPerFarmerDay;
            return total > int.MaxValue ? int.MaxValue : (int)total;
        }

        /// <summary>
        /// Daily action budget minus actions reserved by another subsystem of the same day (for
        /// example the 2 actions of a manual water refill, REQ-V2-WATER-04). This is the only place
        /// that splits the daily budget, so a farm can never overspend it across subsystems.
        /// </summary>
        /// <param name="activeWorkers">On-duty workers of the building.</param>
        /// <param name="reservedActions">Actions already reserved/consumed today; negative means 0.</param>
        /// <returns>Remaining plant+harvest actions for this day, never negative.</returns>
        public static int ActionBudget(int activeWorkers, int reservedActions)
        {
            var budget = ActionBudget(activeWorkers);
            var reserved = reservedActions > 0 ? reservedActions : 0;
            return budget <= reserved ? 0 : budget - reserved;
        }

        /// <summary>
        /// Remaining actions of the current day after <paramref name="actionsUsed"/> were already
        /// performed. Used by the command layer to validate a manual refill before reserving it
        /// (REQ-V2-LABOR-01: no cross-day overspend).
        /// </summary>
        /// <param name="activeWorkers">On-duty workers of the building.</param>
        /// <param name="actionsUsed">Actions already performed today; negative means 0.</param>
        /// <returns>Actions still available today, never negative.</returns>
        public static int RemainingActions(int activeWorkers, int actionsUsed)
        {
            var budget = ActionBudget(activeWorkers);
            var used = actionsUsed > 0 ? actionsUsed : 0;
            return budget <= used ? 0 : budget - used;
        }

        /// <summary>
        /// Workdays per action in the given mode: 0.08 normal/fallow, 0.096 dense; v2 has no path
        /// term (REQ-V2-LABOR-02).
        /// </summary>
        /// <param name="mode">Operating mode of the farm.</param>
        /// <returns>Workdays one plant or harvest action costs.</returns>
        public static float WorkDaysPerAction(IndoorMode mode)
        {
            if (mode == IndoorMode.Dense)
            {
                return IndoorConstants.WorkDaysPerAction * IndoorConstants.DenseWorkMultiplier;
            }

            if (mode == IndoorMode.Fallow)
            {
                return IndoorConstants.WorkDaysPerAction * IndoorConstants.FallowWorkMultiplier;
            }

            return IndoorConstants.WorkDaysPerAction * IndoorConstants.NormalModeMultiplier;
        }

        /// <summary>
        /// Runs one day of farm actions: mature slots are harvested first (ascending index), then
        /// the lowest empty slots are planted, until the action budget is spent (section 19.7).
        /// A harvest whose output cannot be stored leaves the slot mature and reports a backlog;
        /// one broken slot never stops the others (REQ-V2-CORE-05).
        /// </summary>
        /// <param name="record">Farm record mutated in place.</param>
        /// <param name="tier">Tier profile of the building.</param>
        /// <param name="catalog">Crop catalog; the built-in fallback covers missing specs.</param>
        /// <param name="activeWorkers">On-duty workers, source of the action budget.</param>
        /// <param name="output">Output inventory sink; null means "nowhere to put produce".</param>
        /// <param name="farmActionsAllowed">False for stalled/fallow/blocked-entrance days.</param>
        /// <returns>What happened today; never null and never throws.</returns>
        public static IndoorActionStats RunActions(IndoorFarmRecord record, TierProfile tier,
            ICropCatalog catalog, int activeWorkers, IInventorySink output, bool farmActionsAllowed)
        {
            return RunActions(record, tier, catalog, activeWorkers, output, farmActionsAllowed, 0, 1f);
        }

        /// <summary>
        /// Runs one day of farm actions with a reserved-action deduction and the optional one-day
        /// mode-transition yield scale. Both additions are backward compatible: the original
        /// six-argument overload forwards here with 0 reserved actions and scale 1.
        /// </summary>
        /// <param name="record">Farm record mutated in place.</param>
        /// <param name="tier">Tier profile of the building.</param>
        /// <param name="catalog">Crop catalog; the built-in fallback covers missing specs.</param>
        /// <param name="activeWorkers">On-duty workers, source of the action budget.</param>
        /// <param name="output">Output inventory sink; null means "nowhere to put produce".</param>
        /// <param name="farmActionsAllowed">False for stalled/fallow/blocked-entrance days.</param>
        /// <param name="reservedActions">Actions already reserved/consumed today (refill, jobs).</param>
        /// <param name="yieldScale">Harvest yield scale in [0,1]; 1 means no mode transition.</param>
        /// <returns>What happened today; never null and never throws.</returns>
        public static IndoorActionStats RunActions(IndoorFarmRecord record, TierProfile tier,
            ICropCatalog catalog, int activeWorkers, IInventorySink output, bool farmActionsAllowed,
            int reservedActions, float yieldScale)
        {
            return RunActionsCore(record, tier, catalog, activeWorkers, output, farmActionsAllowed,
                reservedActions, yieldScale, plantAllEmptySlots: false, waterAvailable: true);
        }

        /// <summary>
        /// Player-requested "auto-fill" work mode (2026-10-28): when the water tank holds water the
        /// planting pass fills every empty slot in the worker-tended range and does not spend the
        /// daily action budget; a tank without water plants nothing. Harvests keep the daily budget
        /// and still run strictly before planting (REQ-V2-LABOR-01). The older overloads keep the
        /// previous budget-limited planting so existing callers/tests stay valid.
        /// </summary>
        /// <param name="record">Farm record mutated in place.</param>
        /// <param name="tier">Tier profile of the building.</param>
        /// <param name="catalog">Crop catalog; the built-in fallback covers missing specs.</param>
        /// <param name="activeWorkers">On-duty workers, source of the action budget.</param>
        /// <param name="output">Output inventory sink; null means "nowhere to put produce".</param>
        /// <param name="farmActionsAllowed">False for stalled/fallow/blocked-entrance days.</param>
        /// <param name="reservedActions">Actions already reserved/consumed today (refill, jobs).</param>
        /// <param name="yieldScale">Harvest yield scale in [0,1]; 1 means no mode transition.</param>
        /// <param name="waterAvailable">True when the tank holds water; false plants nothing.</param>
        /// <returns>What happened today; never null and never throws.</returns>
        public static IndoorActionStats RunActions(IndoorFarmRecord record, TierProfile tier,
            ICropCatalog catalog, int activeWorkers, IInventorySink output, bool farmActionsAllowed,
            int reservedActions, float yieldScale, bool waterAvailable)
        {
            return RunActionsCore(record, tier, catalog, activeWorkers, output, farmActionsAllowed,
                reservedActions, yieldScale, plantAllEmptySlots: waterAvailable, waterAvailable: waterAvailable);
        }

        private static IndoorActionStats RunActionsCore(IndoorFarmRecord record, TierProfile tier,
            ICropCatalog catalog, int activeWorkers, IInventorySink output, bool farmActionsAllowed,
            int reservedActions, float yieldScale, bool plantAllEmptySlots, bool waterAvailable)
        {
            var stats = new IndoorActionStats();
            if (record is null || tier is null || !farmActionsAllowed) return stats;
            var slots = record.Slots;
            if (slots is null) return stats;
            var budget = ActionBudget(activeWorkers, reservedActions);
            if (budget <= 0) return stats;
            var limit = EffectiveSlots(tier, activeWorkers);
            if (limit > slots.Length) limit = slots.Length;

            // REQ-V2-LABOR-01: the harvest pass runs strictly before the planting pass.
            for (var index = 0; index < limit && stats.ActionsUsed < budget; index++)
            {
                var slot = slots[index];
                if (slot is null || slot.Stage != SlotStage.Mature || string.IsNullOrEmpty(slot.CropId)) continue;
                try
                {
                    if (TryHarvest(record, tier, slot, catalog, output, yieldScale, out var blocked))
                    {
                        stats.Harvested++;
                        stats.ActionsUsed++;
                    }
                    else if (blocked)
                    {
                        stats.BlockedByOutput++;
                        stats.Backlog = true;
                    }
                }
                catch
                {
                    // REQ-V2-SAFE-02: a throwing sink/catalog must not abort the other slots.
                    stats.BlockedByOutput++;
                    stats.Backlog = true;
                }
            }

            // The auto-fill mode never plants into a dry tank: the crops would only stall.
            if (!waterAvailable) return stats;

            var factionCrops = ResolveFactionCrops(catalog, record.Faction);
            for (var index = 0; index < limit; index++)
            {
                if (!plantAllEmptySlots && stats.ActionsUsed >= budget) break;
                var slot = slots[index];
                if (slot is null || slot.Stage != SlotStage.Empty) continue;
                try
                {
                    if (!TryPlant(record, slot, catalog, factionCrops)) continue;
                    stats.Planted++;
                    // Auto-fill planting is free of the daily budget; the old modes keep paying.
                    if (!plantAllEmptySlots) stats.ActionsUsed++;
                }
                catch
                {
                    // REQ-V2-SAFE-02: leave the slot empty and continue with the next one.
                }
            }

            return stats;
        }
    }
}
