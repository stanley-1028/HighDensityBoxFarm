// ============================================================================
// HDBF v2 | Layer: Persistence/Indoor | Owner: V2-SAVE-01
// Purpose: Keyed schema-v2 save path: validate a copy before writing, write the complete record
//          through IEntitySaver, and read it back tolerantly with payload/v1 fallbacks.
// Requirements: REQ-V2-SAVE-01, REQ-V2-SAVE-02, REQ-V2-SAVE-04, REQ-V2-SAVE-05,
//               REQ-V2-SAVE-06, REQ-V2-SAFE-01
// ============================================================================
using System;
using HDBF.Core;
using HDBF.Core.Balance;
using HDBF.Core.Indoor;
using HDBF.Core.Logging;
using Timberborn.Persistence;
using Timberborn.WorldPersistence;

namespace HDBF.Persistence.Indoor
{
    /// <summary>
    /// Keyed schema-v2 variant of the entity save bridge (INTERFACES section 8, section 19.10).
    /// One documented property per record field plus six parallel slot lists replace the single
    /// payload string, while the old payload and v1 nodes stay readable (REQ-V2-SAVE-02).
    ///
    /// Safety contract (section 19.18.3-A): the write path validates a deep copy first and refuses
    /// the write on any repair, so the live record and the previous entity record are never
    /// replaced by a damaged one; the read path never throws into the save callback and repairs a
    /// damaged building in isolation. Both entry points are total functions: null arguments and
    /// broken game nodes return false/null, never an exception (REQ-V2-SAVE-06, REQ-V2-SAFE-01).
    /// </summary>
    public static partial class IndoorEntitySaveBridge
    {
        /// <summary>
        /// Writes one validated schema-v2 record through the game entity saver (REQ-V2-SAVE-04).
        /// Validation runs on a deep copy, so a refused write leaves the live record and the
        /// previous entity record untouched; the caller keeps its dirty flag and retries.
        /// </summary>
        /// <param name="record">Live record to persist; null fails cleanly.</param>
        /// <param name="tier">Resolved tier profile; null fails cleanly.</param>
        /// <param name="catalog">Live crop catalog; the built-in fallback table covers missing specs.</param>
        /// <param name="saver">Game entity saver; null fails cleanly.</param>
        /// <param name="warn">Optional throttled warning sink; receives <c>save.*</c> keyed text.</param>
        /// <returns>True only when a complete, validated record was written.</returns>
        public static bool Save(IndoorFarmRecord? record, TierProfile? tier, ICropCatalog? catalog,
            IEntitySaver? saver, Action<string>? warn = null)
        {
            if (record is null || tier is null || saver is null)
            {
                Notify(warn, IndoorLogKeys.ValidationFailed, "missing record, tier or saver; nothing written");
                return false;
            }

            IndoorFarmRecord candidate;
            IndoorRepairResult validation;
            try
            {
                // REQ-V2-SAVE-04: validate a copy, never the live record (previous good snapshot kept).
                candidate = Clone(record);
                validation = IndoorSaveValidator.ValidateAndRepair(candidate, tier,
                    catalog ?? FallbackCropCatalog.Default, candidate.EntityId);
            }
            catch (Exception exception)
            {
                Notify(warn, IndoorLogKeys.ValidationFailed,
                    "pre-write validation threw " + exception.GetType().Name + "; previous record kept");
                return false;
            }

            if (validation is null || validation.Repaired)
            {
                // A repaired record is not the record the caller owns: refuse and retry next save.
                Notify(warn, IndoorLogKeys.ValidationFailed,
                    "write refused by pre-write validation; previous record kept (" + Describe(validation) + ")");
                return false;
            }

            try
            {
                var component = saver.GetComponent(IndoorSaveKeys.Component);
                if (component is null)
                {
                    Notify(warn, IndoorLogKeys.SaveWrite, "entity saver returned no component; nothing written");
                    return false;
                }

                // One complete record per save callback: every field of the schema is set here, so
                // the game never serializes a partial key set from this path (section 19.18.3-A5).
                WriteRecord(component, candidate);
                return true;
            }
            catch (Exception exception)
            {
                // REQ-V2-SAFE-01: one building must never abort the world save.
                Notify(warn, IndoorLogKeys.SaveWrite,
                    "write failed; previous entity record kept: " + exception.GetType().Name);
                return false;
            }
        }

        /// <summary>
        /// Reads the keyed schema-v2 record of one entity with per-key tolerance (missing field,
        /// wrong type or length mismatch degrade to safe defaults + repair, never throw). A record
        /// that has neither keyed fields nor a payload is read through the v1 migration path; a
        /// failed migration returns the safe default state and leaves the old v1 node untouched
        /// (REQ-V2-SAVE-05). The caller keeps its own safe default when null is returned.
        /// </summary>
        /// <param name="loader">Game entity loader; null fails cleanly.</param>
        /// <param name="entityId">Logical entity id; persisted as invariant text for diagnostics.</param>
        /// <param name="tier">Resolved tier profile; supplies slot capacity and tank capacity.</param>
        /// <param name="faction">Faction resolved from the building blueprint (source of truth).</param>
        /// <param name="catalog">Live crop catalog used by the repair pass.</param>
        /// <param name="today">Current game day; dates the safe maintenance default of a recovery load.</param>
        /// <param name="degraded">True when the record was migrated, repaired or recovered.</param>
        /// <param name="warn">Optional throttled warning sink; receives <c>save.*</c> keyed text.</param>
        /// <returns>The loaded record, or null when the caller must keep its safe default record.</returns>
        public static IndoorFarmRecord? Load(IEntityLoader? loader, long entityId, TierProfile? tier,
            HdbfFaction faction, ICropCatalog? catalog, float today, out bool degraded,
            Action<string>? warn = null)
        {
            degraded = false;
            var identity = FormatEntityId(entityId);
            try
            {
                if (loader is null || tier is null)
                {
                    degraded = true;
                    Notify(warn, IndoorLogKeys.MigratedFailed, "missing loader or tier; safe default kept");
                    return null;
                }

                IHdbfLog? log = warn is null ? null : new WarnLog(warn);
                if (TryReadKeyed(loader, identity, tier, faction, catalog, log, ref degraded, out var record))
                {
                    return record;
                }

                // Payload-string schema-v2 records (written before the keyed format) stay readable;
                // the untouched v2 bridge also owns the v1 read, so this call keeps both fallbacks.
                var fallback = Load(loader, tier, faction, catalog, log, out var fallbackDegraded);
                if (fallback is not null)
                {
                    degraded |= fallbackDegraded;
                    FinalizeLoadedRecord(fallback, identity, tier, faction, today);
                    return fallback;
                }

                if (HasLegacyRecord(loader))
                {
                    // REQ-V2-SAVE-05: a failed v1 migration keeps the building playable and never
                    // deletes the old record or any world plot/crop (this bridge reads only).
                    degraded = true;
                    var safe = IndoorSaveMigrator.RepairDefaults(tier, faction);
                    FinalizeLoadedRecord(safe, identity, tier, faction, today);
                    Notify(warn, IndoorLogKeys.MigratedFailed,
                        "v1 record unreadable; safe default state loaded, old v1 node untouched");
                    return safe;
                }

                degraded |= fallbackDegraded;
                return null;
            }
            catch (Exception exception)
            {
                // REQ-V2-SAVE-06: a broken building must not stop the save from loading.
                degraded = true;
                Notify(warn, IndoorLogKeys.MigratedFailed,
                    "indoor record load failed; safe default kept: " + exception.GetType().Name);
                return null;
            }
        }

        /// <summary>
        /// Reads one present keyed v2 node. Returns false when the component carries no keyed
        /// marker (so the payload/v1 fallbacks can run); a present node is always repaired with
        /// <see cref="IndoorSaveValidator"/> and returned, degraded when anything was fixed.
        /// </summary>
        private static bool TryReadKeyed(IEntityLoader loader, string identity, TierProfile tier,
            HdbfFaction faction, ICropCatalog? catalog, IHdbfLog? log, ref bool degraded,
            out IndoorFarmRecord? record)
        {
            record = null;
            IObjectLoader? component;
            try
            {
                if (!loader.TryGetComponent(IndoorSaveKeys.Component, out component) || component is null
                    || !HasKeyedMarker(component))
                {
                    return false;
                }
            }
            catch (Exception)
            {
                // An unreadable v2 component is treated as "not keyed"; the fallbacks still run.
                return false;
            }

            var damaged = false;
            var loaded = new IndoorFarmRecord
            {
                SchemaVersion = ReadInt(component, IndoorSaveKeys.SchemaVersion,
                    IndoorFarmRecord.CurrentSchemaVersion, ref damaged),
                EntityId = ReadString(component, IndoorSaveKeys.EntityId, identity, ref damaged),
                Tier = (HdbfTier)ReadInt(component, IndoorSaveKeys.Tier, (int)tier.Tier, ref damaged),
                Faction = (HdbfFaction)ReadInt(component, IndoorSaveKeys.Faction, (int)faction, ref damaged),
                Mode = (IndoorMode)ReadInt(component, IndoorSaveKeys.Mode, (int)IndoorMode.Normal, ref damaged),
                CropPolicy = (CropPolicy)ReadInt(component, IndoorSaveKeys.CropPolicy,
                    (int)CropPolicy.Rotation, ref damaged),
                FixedCropId = ReadString(component, IndoorSaveKeys.FixedCropId, string.Empty, ref damaged),
                Slots = ReadSlots(component, tier, ref damaged),
                Tank = ReadFloat(component, IndoorSaveKeys.Tank, 0f, ref damaged),
                MaintenanceDueDay = ReadFloat(component, IndoorSaveKeys.MaintenanceDueDay, 0f, ref damaged),
                MaintenanceCyclesMissed = ReadInt(component, IndoorSaveKeys.MaintenanceCyclesMissed, 0, ref damaged),
                PowerConnected = ReadBool(component, IndoorSaveKeys.PowerConnected, ref damaged),
                PowerMode = (PowerConnection)ReadInt(component, IndoorSaveKeys.PowerMode,
                    (int)PowerConnection.NotRequired, ref damaged),
                PipePort = ReadBool(component, IndoorSaveKeys.PortPipe, ref damaged),
                LogisticsPort = ReadBool(component, IndoorSaveKeys.PortLogistics, ref damaged),
                PowerPort = ReadBool(component, IndoorSaveKeys.PortPower, ref damaged),
                TimberPipesAdapter = ReadString(component, IndoorSaveKeys.TimberPipesAdapter,
                    IndoorSaveLimits.AdapterStateNone, ref damaged),
            };

            // Length/type damage is already reflected in the values; the validator applies the
            // section-19.18.3-A2 rules and returns the locatable repair summary.
            var validation = IndoorSaveValidator.ValidateAndRepair(loaded, tier,
                catalog ?? FallbackCropCatalog.Default, identity);
            if (validation is not null && validation.Repaired)
            {
                degraded = true;
                Log(log, HdbfLogCategory.Warn, IndoorLogKeys.Migrated,
                    "schema-v2 keyed record repaired on load (" + Describe(validation) + ")");
            }

            // The building blueprint is the source of truth for the slot capacity and crop table.
            if (loaded.Tier != tier.Tier)
            {
                loaded.Tier = tier.Tier;
                degraded = true;
            }

            if (loaded.Faction != faction)
            {
                loaded.Faction = faction;
                degraded = true;
            }

            loaded.SchemaVersion = IndoorFarmRecord.CurrentSchemaVersion;
            degraded |= damaged;
            record = loaded;
            return true;
        }

        /// <summary>True when the component carries a keyed v2 marker (version or a slot list).</summary>
        private static bool HasKeyedMarker(IObjectLoader component)
        {
            return component.Has(IndoorSaveKeys.SchemaVersion)
                   || component.Has(IndoorSaveKeys.SlotCropIds)
                   || component.Has(IndoorSaveKeys.SlotGrowth)
                   || component.Has(IndoorSaveKeys.SlotStages);
        }

        /// <summary>True when the old v1 component exists; only its presence is checked, never mutated.</summary>
        private static bool HasLegacyRecord(IEntityLoader loader)
        {
            try
            {
                return loader.HasComponent(IndoorSaveKeys.LegacyComponent);
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
