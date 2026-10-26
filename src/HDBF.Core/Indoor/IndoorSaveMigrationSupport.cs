// ============================================================================
// HDBF v2 | Layer: Core/Indoor | Owner: V2-CORE-02
// Purpose: Internal copy/sanitize helpers of the v1 -> v2 save migration: mode mapping,
//          tank/due-day/cycle/fatigue clamps, deep copy and the diagnostic entity text.
//          Called by IndoorSaveMigrator; no public API and no save policy beyond clamping.
// Requirements: REQ-V2-SAVE-02, REQ-V2-SAVE-05
// ============================================================================
using System;
using HDBF.Core.Balance;

namespace HDBF.Core.Indoor
{
    /// <summary>
    /// Value helpers of <see cref="IndoorSaveMigrator"/>. Every method is a total function:
    /// invalid/NaN/out-of-range legacy values degrade to the documented safe value instead of
    /// throwing, so a damaged v1 node can never abort a load (REQ-V2-SAVE-05).
    /// </summary>
    internal static class IndoorSaveMigrationSupport
    {
        /// <summary>Faction of the legacy record; unknown values degrade to Folktails.</summary>
        internal static HdbfFaction SafeFaction(IndoorLegacyV1Record? legacy)
        {
            if (legacy is null || !Enum.IsDefined(typeof(HdbfFaction), legacy.Faction))
            {
                return HdbfFaction.Folktails;
            }

            return legacy.Faction;
        }

        /// <summary>Maps the v1 mode enum onto the v2 enum; unknown values degrade to Normal.</summary>
        internal static IndoorMode MapMode(HdbfMode mode)
        {
            switch (mode)
            {
                case HdbfMode.Normal: return IndoorMode.Normal;
                case HdbfMode.Dense: return IndoorMode.Dense;
                case HdbfMode.Fallow: return IndoorMode.Fallow;
                default: return IndoorMode.Normal;
            }
        }

        /// <summary>Clamps the v1 tank into [0, TankCapacity]; non-finite/negative values become 0.</summary>
        internal static float SanitizeTank(float tank, float tankCapacity)
        {
            if (!IsFinite(tank) || tank < 0f)
            {
                return 0f;
            }

            if (tankCapacity > 0f && tank > tankCapacity)
            {
                return tankCapacity;
            }

            return tank;
        }

        /// <summary>
        /// Keeps a usable v1 due day verbatim (a reload must not restart the countdown); a missing
        /// or damaged one gets one safe cycle from load day 0 and is capped by the sanity bound.
        /// </summary>
        internal static float SanitizeDueDay(TierProfile tier, float dueDay)
        {
            var resolved = MaintenanceDefaults.ResolveDueDay(tier, 0f, dueDay);
            if (!IsFinite(resolved) || resolved < 0f)
            {
                return 0f;
            }

            return resolved > IndoorSaveLimits.MaxMaintenanceDueDay
                ? IndoorSaveLimits.MaxMaintenanceDueDay
                : resolved;
        }

        /// <summary>Clamps the v1 missed-cycle counter into [0, MaxMaintenanceCyclesMissed].</summary>
        internal static int SanitizeCycles(int cycles)
        {
            if (cycles < 0)
            {
                return 0;
            }

            return cycles > IndoorSaveLimits.MaxMaintenanceCyclesMissed
                ? IndoorSaveLimits.MaxMaintenanceCyclesMissed
                : cycles;
        }

        /// <summary>Clamps the v1 per-plot fatigue into [0, 1]; non-finite/negative values become 0.</summary>
        internal static float SanitizeFatigue(float fatigue)
        {
            if (!IsFinite(fatigue) || fatigue < 0f)
            {
                return 0f;
            }

            return fatigue > IndoorConstants.MaxFatigue ? IndoorConstants.MaxFatigue : fatigue;
        }

        /// <summary>Diagnostic entity id of a migrated record (empty when absent).</summary>
        internal static string DescribeEntity(IndoorFarmRecord? record)
        {
            return record is null ? string.Empty : record.EntityId ?? string.Empty;
        }

        /// <summary>Diagnostic entity id of a legacy record (empty when absent).</summary>
        internal static string DescribeEntity(IndoorLegacyV1Record? legacy)
        {
            return legacy is null ? string.Empty : legacy.EntityId ?? string.Empty;
        }

        /// <summary>Number of slots currently holding a crop; used by the migration log line.</summary>
        internal static int CountCrops(IndoorFarmRecord record)
        {
            var slots = record.Slots;
            if (slots is null)
            {
                return 0;
            }

            var count = 0;
            for (var index = 0; index < slots.Length; index++)
            {
                var slot = slots[index];
                if (slot is not null && !string.IsNullOrEmpty(slot.CropId))
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>Deep copy used by the idempotent v2 -> v2 upgrade step (no aliasing).</summary>
        internal static IndoorFarmRecord Clone(IndoorFarmRecord record)
        {
            var slots = record.Slots;
            var slotCount = slots is null ? 0 : slots.Length;
            var copies = new IndoorSlot[slotCount];
            for (var index = 0; index < slotCount; index++)
            {
                var slot = slots![index];
                copies[index] = slot is null
                    ? null!
                    : new IndoorSlot
                    {
                        CropId = slot.CropId,
                        GrowthDays = slot.GrowthDays,
                        Fatigue = slot.Fatigue,
                        LastCropId = slot.LastCropId,
                        Stage = slot.Stage,
                        Flags = slot.Flags,
                    };
            }

            return new IndoorFarmRecord
            {
                SchemaVersion = record.SchemaVersion,
                EntityId = record.EntityId ?? string.Empty,
                Tier = record.Tier,
                Faction = record.Faction,
                Mode = record.Mode,
                CropPolicy = record.CropPolicy,
                FixedCropId = record.FixedCropId ?? string.Empty,
                Slots = copies,
                Tank = record.Tank,
                MaintenanceDueDay = record.MaintenanceDueDay,
                MaintenanceCyclesMissed = record.MaintenanceCyclesMissed,
                PowerConnected = record.PowerConnected,
                PowerMode = record.PowerMode,
                PipePort = record.PipePort,
                LogisticsPort = record.LogisticsPort,
                PowerPort = record.PowerPort,
                TimberPipesAdapter = record.TimberPipesAdapter ?? IndoorSaveLimits.AdapterStateNone,
            };
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
