// ============================================================================
// HDBF v2 | Layer: Core/Indoor | Owner: V2-CORE-02
// Purpose: Field-level rules of the pre-write validation (section 19.18.3-A2): entity id,
//          tank/due-day/cycles ranges, fixed crop, adapter whitelist and the crop lookup
//          shared with the slot repair. Called by IndoorSaveValidator.
// Requirements: REQ-V2-SAVE-04, REQ-V2-SAVE-06, REQ-V2-CORE-02
// ============================================================================
using System;
using System.Globalization;
using HDBF.Core.Balance;

namespace HDBF.Core.Indoor
{
    /// <summary>
    /// Repairs the scalar fields of one record in place: identity, tank, maintenance date and
    /// missed cycles, fixed crop and the Timber Pipes adapter state. Every rule is idempotent
    /// (a repaired record is clean on the next pass) and none of them throws.
    /// </summary>
    internal static class IndoorSaveFieldRepair
    {
        /// <summary>Enforces the entity id length and reconciles it with the storage key.</summary>
        internal static void RepairIdentity(IndoorFarmRecord record, IndoorRepairAccumulator accumulator,
            string? expectedEntityId)
        {
            if (record.EntityId is null)
            {
                record.EntityId = string.Empty;
                accumulator.Clamp("entityId-null");
            }
            else if (record.EntityId.Length > IndoorSaveLimits.MaxEntityIdLength)
            {
                record.EntityId = record.EntityId.Substring(0, IndoorSaveLimits.MaxEntityIdLength);
                accumulator.Clamp("entityId-truncated");
            }

            if (expectedEntityId is null)
            {
                return;
            }

            if (expectedEntityId.Length > IndoorSaveLimits.MaxEntityIdLength)
            {
                accumulator.Clamp("expectedEntityId-too-long");
                return;
            }

            if (record.EntityId.Length == 0)
            {
                record.EntityId = expectedEntityId;
                accumulator.Clamp("entityId-filled");
            }
            else if (!string.Equals(record.EntityId, expectedEntityId, StringComparison.Ordinal))
            {
                accumulator.Clamp("entityId-mismatch:" + record.EntityId);
                record.EntityId = expectedEntityId;
            }
        }

        /// <summary>Clamps tank, maintenance due day and missed cycles into their documented ranges.</summary>
        internal static void RepairNumbers(IndoorFarmRecord record, TierProfile tier,
            IndoorRepairAccumulator accumulator)
        {
            // REQ-V2-SAVE-04: finite-float and clamp ranges of section 19.18.3-A2.
            var tankCapacity = tier.TankCapacity;
            var tank = record.Tank;
            if (!IndoorMath.IsFinite(tank))
            {
                accumulator.Clamp("tank-nonfinite");
                record.Tank = 0f;
            }
            else if (tank < 0f)
            {
                accumulator.Clamp("tank-negative:" + Format(tank));
                record.Tank = 0f;
            }
            else if (tankCapacity > 0f && tank > tankCapacity)
            {
                accumulator.Clamp("tank-clamped:" + Format(tank) + "->" + Format(tankCapacity));
                record.Tank = tankCapacity;
            }

            RepairDueDay(record, tier, accumulator);

            if (record.MaintenanceCyclesMissed < 0)
            {
                accumulator.Clamp("maintenance.cycles-negative:" +
                    record.MaintenanceCyclesMissed.ToString(CultureInfo.InvariantCulture));
                record.MaintenanceCyclesMissed = 0;
            }
            else if (record.MaintenanceCyclesMissed > IndoorSaveLimits.MaxMaintenanceCyclesMissed)
            {
                accumulator.Clamp("maintenance.cycles-clamped:" +
                    record.MaintenanceCyclesMissed.ToString(CultureInfo.InvariantCulture));
                record.MaintenanceCyclesMissed = IndoorSaveLimits.MaxMaintenanceCyclesMissed;
            }
        }

        /// <summary>
        /// Repairs the maintenance due day: an irrigation-only tier has none (0); a material tier
        /// whose date is missing/damaged gets one safe cycle (load day is unknown here; the first
        /// daily tick reschedules it from the live day, so a load can never stall a farm).
        /// </summary>
        private static void RepairDueDay(IndoorFarmRecord record, TierProfile tier,
            IndoorRepairAccumulator accumulator)
        {
            if (tier.MaintenanceKind == HdbfMaintenanceKind.IrrigationOnly)
            {
                if (record.MaintenanceDueDay != 0f)
                {
                    accumulator.Clamp("maintenance.dueDay-cleared:" + Format(record.MaintenanceDueDay));
                    record.MaintenanceDueDay = 0f;
                }

                return;
            }

            var fallback = MaintenanceDefaults.DefaultCycleDays(tier);
            var dueDay = record.MaintenanceDueDay;
            if (!IndoorMath.IsFinite(dueDay) || dueDay < 0f || (dueDay == 0f && fallback > 0f))
            {
                accumulator.Clamp("maintenance.dueDay-default:" + Format(dueDay));
                record.MaintenanceDueDay = fallback;
            }
            else if (dueDay > IndoorSaveLimits.MaxMaintenanceDueDay)
            {
                accumulator.Clamp("maintenance.dueDay-clamped:" + Format(dueDay));
                record.MaintenanceDueDay = IndoorSaveLimits.MaxMaintenanceDueDay;
            }
        }

        /// <summary>
        /// Keeps the fixed crop only when the policy is Fixed and the faction knows the crop;
        /// a rotation record carries no fixed crop (section 19.10 field contract).
        /// </summary>
        internal static void RepairCropPolicy(IndoorFarmRecord record, ICropCatalog? catalog,
            IndoorRepairAccumulator accumulator)
        {
            var fixedCrop = record.FixedCropId ?? string.Empty;
            if (fixedCrop.Length > IndoorSaveLimits.MaxCropIdLength)
            {
                accumulator.Clamp("fixedCrop-too-long");
                fixedCrop = string.Empty;
            }

            if (record.CropPolicy == CropPolicy.Fixed)
            {
                if (fixedCrop.Length > 0 && !IsKnownCrop(catalog, record.Faction, fixedCrop))
                {
                    accumulator.Clamp("fixedCrop-unknown:" + fixedCrop);
                    fixedCrop = string.Empty;
                }
            }
            else if (fixedCrop.Length > 0)
            {
                accumulator.Clamp("fixedCrop-not-applicable:" + fixedCrop);
                fixedCrop = string.Empty;
            }

            record.FixedCropId = fixedCrop;
        }

        /// <summary>Keeps the Timber Pipes adapter state only when it is one of the five known states.</summary>
        internal static void RepairAdapter(IndoorFarmRecord record, IndoorRepairAccumulator accumulator)
        {
            var state = record.TimberPipesAdapter;
            if (string.IsNullOrEmpty(state))
            {
                accumulator.Clamp("adapter-default");
                record.TimberPipesAdapter = IndoorSaveLimits.AdapterStateNone;
                return;
            }

            if (state!.Length > IndoorSaveLimits.MaxAdapterStateLength || !IsKnownAdapterState(state))
            {
                accumulator.Clamp("adapter-invalid:" + state);
                record.TimberPipesAdapter = IndoorSaveLimits.AdapterStateNone;
            }
        }

        /// <summary>
        /// True when the faction can plant this crop. The live catalog is tried first and the
        /// built-in fallback table second (section 19.5: a missing vanilla spec must not invalidate
        /// a save). A throwing catalog counts as "missing" (REQ-V2-SAVE-06).
        /// </summary>
        internal static bool IsKnownCrop(ICropCatalog? catalog, HdbfFaction faction, string cropId)
        {
            if (string.IsNullOrEmpty(cropId))
            {
                return false;
            }

            return FallbackCropCatalog.TryResolve(catalog, faction, cropId, out var crop)
                && crop is not null
                && IndoorMath.IsPositiveFinite(crop.GrowthTimeInDays);
        }

        private static bool IsKnownAdapterState(string state)
        {
            return string.Equals(state, IndoorSaveLimits.AdapterStateNone, StringComparison.Ordinal)
                || string.Equals(state, IndoorSaveLimits.AdapterStateOk, StringComparison.Ordinal)
                || string.Equals(state, IndoorSaveLimits.AdapterStateDegraded, StringComparison.Ordinal)
                || string.Equals(state, IndoorSaveLimits.AdapterStateDisabled, StringComparison.Ordinal)
                || string.Equals(state, IndoorSaveLimits.AdapterStateFailed, StringComparison.Ordinal);
        }

        private static string Format(float value)
        {
            return IndoorSaveFormat.FormatFloat(value);
        }
    }
}
