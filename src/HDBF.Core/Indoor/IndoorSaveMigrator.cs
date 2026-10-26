// ============================================================================
// HDBF v2 | Layer: Core/Indoor | Owner: V2-CORE-02
// Purpose: Best-effort v1 -> v2 save migration (INTERFACES section 8, section 19.10):
//          maps v1 plot/crop/fatigue arrays onto the leading slots, preserves tank /
//          maintenance / mode / ports / adapter, and falls back to RepairDefaults on bad
//          data. Also owns the idempotent v2->v2 upgrade step of the migration chain;
//          copies/sanitizers live in IndoorSaveMigrationSupport.
// Requirements: REQ-V2-SAVE-02, REQ-V2-SAVE-05, REQ-V2-EXT-03, REQ-V2-SAFE-01
// ============================================================================
// Namespace: HDBF.Core.Indoor (frozen INTERFACES section 8 contract; no .Save sub-namespace).
using System;
using System.Globalization;
using HDBF.Core.Balance;
using HDBF.Core.Logging;

namespace HDBF.Core.Indoor
{
    /// <summary>
    /// v1 -> v2 migration of the indoor save (REQ-V2-SAVE-02). The class is stateless apart from
    /// an optional throttled logger; <see cref="Default"/> is the shared logger-less instance used
    /// by L1 tests. A failed migration never throws and never deletes player data: it returns
    /// false and hands the caller the safe default record (empty slots, full tank, default
    /// maintenance, REQ-V2-SAVE-05). Re-running the migration on its own output is byte-identical.
    /// </summary>
    public sealed class IndoorSaveMigrator : ISaveMigrator
    {
        /// <summary>Schema version of the v1 record this migrator accepts (REQ-V2-SAVE-02).</summary>
        public const int LegacySchemaVersion = IndoorSaveLimits.LegacySchemaVersion;

        /// <summary>Shared stateless instance without a log sink (L1 tests, default callers).</summary>
        public static IndoorSaveMigrator Default { get; } = new IndoorSaveMigrator(null);

        private readonly IHdbfLog? _log;

        /// <summary>Creates a migrator without a log sink; migration failures are only returned.</summary>
        public IndoorSaveMigrator()
            : this(null)
        {
        }

        /// <summary>
        /// Creates a migrator that reports <c>save.migrated</c> / <c>save.migrated-failed</c>
        /// through the injected throttled logger (REQ-V2-SAVE-05, ARCHITECTURE section 6).
        /// </summary>
        /// <param name="log">Game log sink; null keeps Core silent and testable.</param>
        public IndoorSaveMigrator(IHdbfLog? log)
        {
            _log = log;
        }

        /// <summary>
        /// The safe state of a farm whose record is missing or cannot be migrated (INTERFACES
        /// section 8, section 19.18.3-A3): empty slots, full tank, default maintenance date, zero
        /// missed cycles and the tier default ports. The due date is <c>0 + cycle</c> because load
        /// time has no current day yet; the first daily tick reschedules it from the live day.
        /// </summary>
        /// <param name="tier">Resolved tier profile; null falls back to the Basic profile (never throws).</param>
        /// <param name="faction">Faction of the building; an unknown value falls back to Folktails.</param>
        /// <returns>A valid empty record; never null.</returns>
        public static IndoorFarmRecord RepairDefaults(TierProfile tier, HdbfFaction faction)
        {
            var profile = tier is null || !Enum.IsDefined(typeof(HdbfTier), tier.Tier)
                ? TierCatalog.Get(HdbfTier.Basic)
                : tier;
            var safeFaction = Enum.IsDefined(typeof(HdbfFaction), faction) ? faction : HdbfFaction.Folktails;
            // IndoorFarmRecord.Create already is the documented safe state (section 19.18.3-A3).
            return IndoorFarmRecord.Create(profile.Tier, safeFaction);
        }

        /// <summary>
        /// Idempotent v2 -> v2 step of the migration chain (REQ-V2-EXT-03). A record already at the
        /// current schema is returned as an equal deep copy (the caller cannot alias the live
        /// state); an older in-memory record only gets its version stamp raised, because the
        /// structural repair belongs to <see cref="IndoorSaveValidator"/>. Never throws.
        /// </summary>
        /// <param name="record">Record to upgrade; null returns the safe default record.</param>
        /// <param name="tier">Resolved tier profile of the building.</param>
        /// <returns>Deep copy at the current schema version; never the input instance.</returns>
        public static IndoorFarmRecord Upgrade(IndoorFarmRecord record, TierProfile tier)
        {
            if (record is null)
            {
                return RepairDefaults(tier, HdbfFaction.Folktails);
            }

            var copy = IndoorSaveMigrationSupport.Clone(record);
            if (copy.SchemaVersion < IndoorFarmRecord.CurrentSchemaVersion)
            {
                copy.SchemaVersion = IndoorFarmRecord.CurrentSchemaVersion;
            }

            return copy;
        }

        /// <summary>
        /// Best-effort v1 -> v2 migration of one legacy record (REQ-V2-SAVE-02). A well-formed
        /// record maps onto the leading slots and preserves tank/maintenance/mode/ports/adapter; a
        /// truncated or throwing record returns false and hands back the safe default record
        /// (REQ-V2-SAVE-05). Never throws and never mutates <paramref name="legacy"/>.
        /// </summary>
        /// <param name="legacy">Legacy v1 record read from the old save node.</param>
        /// <param name="tier">Resolved tier profile of the building.</param>
        /// <param name="migrated">Migrated record (success) or the safe default record (failure).</param>
        /// <returns>True when a complete v2 record could be produced.</returns>
        public bool TryMigrateV1(IndoorLegacyV1Record legacy, TierProfile tier, out IndoorFarmRecord migrated)
        {
            try
            {
                if (TryBuildV1(legacy, tier, out migrated, out var failure))
                {
                    Log(HdbfLogCategory.Info, IndoorLogKeys.Migrated,
                        "entity=" + IndoorSaveMigrationSupport.DescribeEntity(migrated) + " slots=" +
                        IndoorSaveMigrationSupport.CountCrops(migrated).ToString(CultureInfo.InvariantCulture) +
                        "/" + migrated.Slots.Length.ToString(CultureInfo.InvariantCulture));
                    return true;
                }

                migrated = RepairDefaults(tier!, IndoorSaveMigrationSupport.SafeFaction(legacy));
                Log(HdbfLogCategory.Warn, IndoorLogKeys.MigratedFailed,
                    "entity=" + IndoorSaveMigrationSupport.DescribeEntity(legacy) + " reason=" + failure);
                // REQ-V2-SAVE-05: a failed v1 migration returns the safe default and deletes nothing.
                return false;
            }
            catch (Exception exception)
            {
                // REQ-V2-SAVE-05: a throwing legacy record must not stop the save from loading.
                migrated = RepairDefaults(tier!, IndoorSaveMigrationSupport.SafeFaction(legacy));
                Log(HdbfLogCategory.Warn, IndoorLogKeys.MigratedFailed,
                    "entity=" + IndoorSaveMigrationSupport.DescribeEntity(legacy) +
                    " reason=exception:" + exception.GetType().Name);
                return false;
            }
        }

        // ---------------------------------------------------------------- migration body

        private static bool TryBuildV1(IndoorLegacyV1Record legacy, TierProfile tier,
            out IndoorFarmRecord migrated, out string failure)
        {
            migrated = null!;
            failure = string.Empty;
            if (legacy is null)
            {
                failure = "legacy-missing";
                return false;
            }

            if (tier is null || tier.SlotCapacity <= 0 || !Enum.IsDefined(typeof(HdbfTier), tier.Tier))
            {
                failure = "tier-invalid";
                return false;
            }

            // The three parallel v1 arrays are the only migration source; a truncated node is
            // exactly the "坏数据" case that must land on the safe default (section 19.18.3-A3).
            if (legacy.PlotIds is null)
            {
                failure = "plot-ids-missing";
                return false;
            }

            if (legacy.Crops is null)
            {
                failure = "crops-missing";
                return false;
            }

            if (legacy.Fatigue is null)
            {
                failure = "fatigue-missing";
                return false;
            }

            var faction = IndoorSaveMigrationSupport.SafeFaction(legacy);
            var record = IndoorFarmRecord.Create(tier.Tier, faction);
            record.EntityId = legacy.EntityId ?? string.Empty;
            record.Mode = IndoorSaveMigrationSupport.MapMode(legacy.Mode);
            record.CropPolicy = CropPolicy.Rotation;
            record.FixedCropId = string.Empty;
            record.Tank = IndoorSaveMigrationSupport.SanitizeTank(legacy.Tank, tier.TankCapacity);
            record.MaintenanceDueDay = IndoorSaveMigrationSupport.SanitizeDueDay(tier, legacy.MaintenanceDueDay);
            record.MaintenanceCyclesMissed =
                IndoorSaveMigrationSupport.SanitizeCycles(legacy.MaintenanceCyclesMissed);
            record.PowerConnected = tier.NeedsPower && legacy.PowerConnected;
            record.PowerMode = tier.NeedsPower ? PowerConnection.Grid : PowerConnection.NotRequired;
            record.PipePort = legacy.PipePort;
            record.LogisticsPort = legacy.LogisticsPort;
            record.PowerPort = legacy.PowerPort;
            record.TimberPipesAdapter = string.IsNullOrEmpty(legacy.TimberPipesAdapter)
                ? IndoorSaveLimits.AdapterStateNone
                : legacy.TimberPipesAdapter!;
            MapSlots(record, legacy, tier, faction);
            record.SchemaVersion = IndoorFarmRecord.CurrentSchemaVersion;
            migrated = record;
            return true;
        }

        private static void MapSlots(IndoorFarmRecord record, IndoorLegacyV1Record legacy, TierProfile tier,
            HdbfFaction faction)
        {
            // REQ-V2-SAVE-02: v1 plots.ids/crops/fatigue map in order onto min(count, Capacity) slots.
            var capacity = tier.SlotCapacity;
            var count = legacy.PlotIds.Length < capacity ? legacy.PlotIds.Length : capacity;
            for (var index = 0; index < count; index++)
            {
                var slot = record.Slots[index];
                var cropId = index < legacy.Crops.Length ? legacy.Crops[index] : null;
                if (!string.IsNullOrEmpty(cropId)
                    && cropId!.Length <= IndoorSaveLimits.MaxCropIdLength
                    && FallbackCropCatalog.Default.TryGet(faction, cropId, out _))
                {
                    // v1 persisted no growth progress: a migrated crop restarts at day 0 so it
                    // matures through the normal indoor loop instead of instantly harvesting.
                    slot.CropId = cropId;
                    slot.Stage = SlotStage.Growing;
                    slot.GrowthDays = 0f;
                }

                var fatigue = index < legacy.Fatigue.Length ? legacy.Fatigue[index] : 0f;
                slot.Fatigue = IndoorSaveMigrationSupport.SanitizeFatigue(fatigue);
                slot.LastCropId = string.Empty;
                slot.Flags = 0;
            }
        }

        // ---------------------------------------------------------------- logging

        private void Log(HdbfLogCategory category, string key, string message)
        {
            var log = _log;
            if (log is null)
            {
                return;
            }

            try
            {
                log.Write(category, key, message);
            }
            catch
            {
                // The log sink contract says "never throw"; a broken sink must not fail a load.
            }
        }
    }
}
