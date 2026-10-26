// ============================================================================
// HDBF v2 | Layer: Core/Indoor | Owner: V2-CORE-02
// Purpose: The mandatory pre-write validation/repair pass of one indoor farm record
//          (INTERFACES section 8, section 19.18.3-A2): orchestrates identity, enum, port,
//          numeric, crop and slot repairs and returns the locatable repair summary.
//          Field-level rules live in IndoorSaveFieldRepair, slot rules in IndoorSlotRepair.
// Requirements: REQ-V2-SAVE-04, REQ-V2-SAVE-06, REQ-V2-CORE-02, REQ-V2-SAFE-01
// ============================================================================
// Namespace: HDBF.Core.Indoor (frozen INTERFACES section 8 contract; no .Save sub-namespace).
using System;
using System.Globalization;
using HDBF.Core.Balance;

namespace HDBF.Core.Indoor
{
    /// <summary>
    /// Validates and repairs one record against its resolved tier profile (REQ-V2-SAVE-04: every
    /// entry of section 19.18.3-A2 must pass before a record may be written). The repair is
    /// conservative: it clamps, clears and pads, it never invents crops, water or maintenance
    /// credit. The record is repaired <b>in place</b> (the interface returns only the summary);
    /// a caller on the write path must keep the previous good snapshot and skip the write when
    /// <see cref="IndoorRepairResult.Repaired"/> is true. The pass never throws: a broken catalog
    /// or a null argument degrades to "not writable" (REQ-V2-SAVE-06).
    /// </summary>
    public static class IndoorSaveValidator
    {
        /// <summary>
        /// Runs the full pre-write validation/repair of <paramref name="record"/> (section
        /// 19.18.3-A2). Missing/null arguments are reported as not writable instead of throwing.
        /// </summary>
        /// <param name="record">Record to validate and repair in place.</param>
        /// <param name="tier">Resolved tier profile; supplies capacity, tank capacity and power contract.</param>
        /// <param name="catalog">Live crop catalog; the built-in fallback table covers missing specs.</param>
        /// <param name="expectedEntityId">Entity key the record is stored under, or null when unknown.</param>
        /// <returns>What was repaired; <see cref="IndoorRepairResult.Repaired"/> forbids the write.</returns>
        public static IndoorRepairResult ValidateAndRepair(IndoorFarmRecord record, TierProfile tier,
            ICropCatalog catalog, string? expectedEntityId)
        {
            // REQ-V2-SAVE-04: every section-19.18.3-A2 rule runs before any write or keep-old decision.
            var accumulator = new IndoorRepairAccumulator();
            string? entityId = null;
            try
            {
                if (record is null)
                {
                    accumulator.Fail("record-missing");
                    return accumulator.Build(null);
                }

                entityId = record.EntityId;
                if (tier is null)
                {
                    accumulator.Fail("tier-missing");
                    return accumulator.Build(entityId);
                }

                if (!Enum.IsDefined(typeof(HdbfTier), tier.Tier))
                {
                    accumulator.Fail("tier-profile-invalid:" +
                        ((int)tier.Tier).ToString(CultureInfo.InvariantCulture));
                    return accumulator.Build(entityId);
                }

                if (tier.SlotCapacity <= 0)
                {
                    accumulator.Fail("capacity-invalid:" + tier.SlotCapacity.ToString(CultureInfo.InvariantCulture));
                    return accumulator.Build(entityId);
                }

                IndoorSaveFieldRepair.RepairIdentity(record, accumulator, expectedEntityId);
                RepairEnums(record, tier, accumulator);
                IndoorSaveFieldRepair.RepairNumbers(record, tier, accumulator);
                IndoorSaveFieldRepair.RepairCropPolicy(record, catalog, accumulator);
                IndoorSaveFieldRepair.RepairAdapter(record, accumulator);
                IndoorSlotRepair.Repair(record, tier, catalog, accumulator);
                return accumulator.Build(record.EntityId);
            }
            catch (Exception exception)
            {
                // REQ-V2-SAVE-06 / REQ-V2-SAFE-01: a damaged building is isolated, the save loads.
                accumulator.Fail("validator-exception:" + exception.GetType().Name);
                return accumulator.Build(entityId);
            }
        }

        // ---------------------------------------------------------------- enums + ports + power

        private static void RepairEnums(IndoorFarmRecord record, TierProfile tier,
            IndoorRepairAccumulator accumulator)
        {
            if (record.Tier != tier.Tier)
            {
                accumulator.Clamp("tier-mismatch:" + IndoorSaveEnums.TierName(record.Tier) + "->" +
                    IndoorSaveEnums.TierName(tier.Tier));
                record.Tier = tier.Tier;
            }

            if (!Enum.IsDefined(typeof(HdbfFaction), record.Faction))
            {
                accumulator.Clamp("faction-invalid:" + ((int)record.Faction).ToString(CultureInfo.InvariantCulture));
                record.Faction = HdbfFaction.Folktails;
            }

            if (!Enum.IsDefined(typeof(IndoorMode), record.Mode))
            {
                accumulator.Clamp("mode-invalid:" + ((int)record.Mode).ToString(CultureInfo.InvariantCulture));
                record.Mode = IndoorMode.Normal;
            }

            if (!Enum.IsDefined(typeof(CropPolicy), record.CropPolicy))
            {
                accumulator.Clamp("cropPolicy-invalid:" +
                    ((int)record.CropPolicy).ToString(CultureInfo.InvariantCulture));
                record.CropPolicy = CropPolicy.Rotation;
            }

            RepairPower(record, tier, accumulator);
            RepairPorts(record, tier, accumulator);
        }

        private static void RepairPower(IndoorFarmRecord record, TierProfile tier,
            IndoorRepairAccumulator accumulator)
        {
            if (tier.NeedsPower)
            {
                if (record.PowerMode != PowerConnection.Grid && record.PowerMode != PowerConnection.OffGrid)
                {
                    accumulator.Clamp("power.mode-invalid:" + IndoorSaveEnums.PowerModeName(record.PowerMode));
                    record.PowerMode = PowerConnection.Grid;
                }

                return;
            }

            if (record.PowerMode != PowerConnection.NotRequired)
            {
                accumulator.Clamp("power.mode-not-required:" + IndoorSaveEnums.PowerModeName(record.PowerMode));
                record.PowerMode = PowerConnection.NotRequired;
            }

            if (record.PowerConnected)
            {
                accumulator.Clamp("power.connected-without-power-tier");
                record.PowerConnected = false;
            }
        }

        private static void RepairPorts(IndoorFarmRecord record, TierProfile tier,
            IndoorRepairAccumulator accumulator)
        {
            if (!tier.NeedsPower && record.PowerPort)
            {
                accumulator.Clamp("ports.power-without-power-tier");
                record.PowerPort = false;
            }

            if ((int)tier.Tier < (int)HdbfTier.Intermediate && record.LogisticsPort)
            {
                accumulator.Clamp("ports.logistics-below-intermediate");
                record.LogisticsPort = false;
            }
        }
    }
}
