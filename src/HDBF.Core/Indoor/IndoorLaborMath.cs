// ============================================================================
// HDBF v2 | Layer: Core/Indoor | Owner: V2-CORE-01
// Purpose: Labour capacity, action budget, per-action work cost and the deterministic
//          harvest-before-plant action scheduler (INTERFACES section 5, section 19.7).
// Requirements: REQ-V2-LABOR-01, REQ-V2-LABOR-02, REQ-V2-CORE-01, REQ-V2-CORE-05
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
    public static class IndoorLaborMath
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
            var stats = new IndoorActionStats();
            if (record is null || tier is null || !farmActionsAllowed) return stats;
            var slots = record.Slots;
            if (slots is null) return stats;
            var budget = ActionBudget(activeWorkers);
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
                    if (TryHarvest(record, tier, slot, catalog, output, out var blocked))
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

            var factionCrops = ResolveFactionCrops(catalog, record.Faction);
            for (var index = 0; index < limit && stats.ActionsUsed < budget; index++)
            {
                var slot = slots[index];
                if (slot is null || slot.Stage != SlotStage.Empty) continue;
                try
                {
                    if (!TryPlant(record, slot, catalog, factionCrops)) continue;
                    stats.Planted++;
                    stats.ActionsUsed++;
                }
                catch
                {
                    // REQ-V2-SAFE-02: leave the slot empty and continue with the next one.
                }
            }

            return stats;
        }

        /// <summary>Harvests one mature slot; output-full and missing-spec cases are not fatal.</summary>
        private static bool TryHarvest(IndoorFarmRecord record, TierProfile tier, IndoorSlot slot,
            ICropCatalog catalog, IInventorySink output, out bool blocked)
        {
            blocked = false;
            if (!FallbackCropCatalog.TryResolve(catalog, record.Faction, slot.CropId, out var crop))
            {
                // Missing spec in both catalogs: keep the mature slot; the validator/logs repair it.
                return false;
            }

            var amount = IndoorGrowthMath.HarvestItems(crop.BaseYield, slot.Fatigue, record.Mode);
            if (amount > 0)
            {
                if (output is null)
                {
                    blocked = true;
                    return false;
                }

                try
                {
                    if (!output.TryAdd(crop.YieldItemId, amount))
                    {
                        // REQ-V2-CORE-01/section 19.5: output full -> do not harvest, keep it mature.
                        blocked = true;
                        return false;
                    }
                }
                catch
                {
                    // A throwing sink means "not stored": keep the slot mature (never double add).
                    blocked = true;
                    return false;
                }
            }

            // Harvest succeeded: apply fatigue/rotation bookkeeping and clear the slot.
            var sameCrop = string.Equals(slot.CropId, slot.LastCropId, StringComparison.Ordinal);
            ApplyHarvestFatigue(slot, tier.SoilFatigueFactor, record.Mode, sameCrop);
            slot.LastCropId = slot.CropId;
            slot.CropId = string.Empty;
            slot.GrowthDays = 0f;
            slot.Stage = SlotStage.Empty;
            slot.Flags = 0;
            return true;
        }

        /// <summary>Plants one empty slot with the policy-selected crop.</summary>
        private static bool TryPlant(IndoorFarmRecord record, IndoorSlot slot, ICropCatalog catalog,
            IReadOnlyList<CropDefinition> factionCrops)
        {
            if (!TryChooseCrop(record, slot.LastCropId, catalog, factionCrops, out var crop)) return false;
            slot.CropId = crop.Id;
            slot.GrowthDays = 0f;
            slot.Stage = SlotStage.Growing;
            slot.Flags = 0;
            return true;
        }

        /// <summary>Policy-selected crop: fixed crop, or the next crop after the previous one.</summary>
        private static bool TryChooseCrop(IndoorFarmRecord record, string lastCropId, ICropCatalog catalog,
            IReadOnlyList<CropDefinition> factionCrops, out CropDefinition crop)
        {
            crop = null!;
            if (record.CropPolicy == CropPolicy.Fixed)
            {
                // An empty/invalid fixed crop plants nothing until the player picks a valid one.
                return FallbackCropCatalog.TryResolve(catalog, record.Faction, record.FixedCropId, out crop);
            }

            if (factionCrops is null || factionCrops.Count == 0) return false;
            var lastIndex = -1;
            for (var index = 0; index < factionCrops.Count; index++)
            {
                var candidate = factionCrops[index];
                if (candidate is not null && string.Equals(candidate.Id, lastCropId, StringComparison.Ordinal))
                {
                    lastIndex = index;
                    break;
                }
            }

            // Rotation: the crop after the previous one in the faction table, wrapping around.
            var start = lastIndex >= 0 ? (lastIndex + 1) % factionCrops.Count : 0;
            for (var step = 0; step < factionCrops.Count; step++)
            {
                var candidate = factionCrops[(start + step) % factionCrops.Count];
                if (candidate is null || string.IsNullOrEmpty(candidate.Id)) continue;
                crop = candidate;
                return true;
            }

            return false;
        }

        /// <summary>Applies one harvest worth of fatigue, rotated or repeated crop (section 19.5).</summary>
        private static void ApplyHarvestFatigue(IndoorSlot slot, float tierFatigueFactor, IndoorMode mode,
            bool sameCrop)
        {
            var tierFactor = IndoorMath.IsFinite(tierFatigueFactor) && tierFatigueFactor > 0f
                ? tierFatigueFactor
                : 0f;
            var delta = IndoorConstants.FatiguePerHarvest * tierFactor;
            if (mode == IndoorMode.Dense) delta *= IndoorConstants.DenseFatigueMultiplier;
            if (!sameCrop) delta *= IndoorConstants.RotationFatigueFactor;

            var fatigue = IndoorMath.Clamp01(slot.Fatigue) + delta;
            if (!sameCrop) fatigue -= IndoorConstants.RotationRecovery;
            slot.Fatigue = IndoorMath.Clamp01(fatigue);
        }

        /// <summary>Reads the faction crop list once per day; a broken catalog degrades to the fallback.</summary>
        private static IReadOnlyList<CropDefinition> ResolveFactionCrops(ICropCatalog catalog, HdbfFaction faction)
        {
            if (catalog is not null)
            {
                try
                {
                    var crops = catalog.ForFaction(faction);
                    if (crops is not null && crops.Count > 0) return crops;
                }
                catch
                {
                    // Missing/broken catalog: fall through to the built-in table (REQ-V2-CORE-05).
                }
            }

            return FallbackCropCatalog.Default.ForFaction(faction);
        }
    }
}
