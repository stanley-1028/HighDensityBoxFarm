// ============================================================================
// HDBF v2 | Layer: Core/Indoor | Owner: V2-CORE-01 / V2-GAMEPLAY-02
// Purpose: Private scheduling helpers of IndoorLaborMath (harvest, plant, rotation choice,
//          harvest fatigue, faction crop table). Split from IndoorLaborMath.cs so both
//          files stay within the 300-line readability gate.
// Requirements: REQ-V2-CORE-05, REQ-V2-LABOR-01, REQ-V2-BAL-03
// ============================================================================
using System;
using System.Collections.Generic;
using HDBF.Core.Balance;

namespace HDBF.Core.Indoor
{
    public static partial class IndoorLaborMath
    {
        /// <summary>Harvests one mature slot; output-full and missing-spec cases are not fatal.</summary>
        private static bool TryHarvest(IndoorFarmRecord record, TierProfile tier, IndoorSlot slot,
            ICropCatalog catalog, IInventorySink output, float yieldScale, out bool blocked)
        {
            blocked = false;
            if (!FallbackCropCatalog.TryResolve(catalog, record.Faction, slot.CropId, out var crop))
            {
                // Missing spec in both catalogs: keep the mature slot; the validator/logs repair it.
                return false;
            }

            var amount = IndoorGrowthMath.HarvestItems(crop.BaseYield, slot.Fatigue, record.Mode, yieldScale);
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
