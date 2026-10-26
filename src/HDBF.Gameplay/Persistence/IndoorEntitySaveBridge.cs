// ============================================================================
// HDBF v2 | Layer: Gameplay/Persistence | Owner: V2-GAMEPLAY-01
// Purpose: Entity-scoped schema-v2 save bridge: clone -> validate -> encode -> one
//          stable string key. Never throws into the game save/load callbacks and never
//          writes a record whose pre-write validation failed (REQ-V2-SAVE-04/06).
// Requirements: REQ-V2-SAVE-01, REQ-V2-SAVE-04, REQ-V2-SAVE-06, REQ-V2-SAFE-01
// ============================================================================
using System;
using HDBF.Core;
using HDBF.Core.Indoor;
using HDBF.Core.Logging;
using Timberborn.Persistence;
using Timberborn.WorldPersistence;

namespace HDBF.Gameplay.Persistence
{
    /// <summary>
    /// The v2 indoor-farm save bridge. V2-SAVE-01 delivered the game-free codec, validator and
    /// migrator in <c>HDBF.Core.Indoor</c> but no concrete entity bridge ships in
    /// <c>HDBF.Persistence</c>; this class is the Gameplay-side adapter that binds those pure
    /// functions to the entity's <see cref="IEntitySaver"/> / <see cref="IEntityLoader"/>.
    ///
    /// The whole record is stored as one deterministic schema-v2 string under a single property on
    /// its own component. That keeps the game's transactional entity save as the only writer, so a
    /// partial record can never be committed: either the complete string is set or the previous
    /// entity data stays untouched.
    /// </summary>
    public static partial class IndoorEntitySaveBridge
    {
        /// <summary>Stable component key of the v2 indoor record; never rename after release.</summary>
        public const string ComponentKeyName = "HDBF.IndoorFarm";

        /// <summary>Stable property key that carries the schema-v2 text payload.</summary>
        public const string PayloadKeyName = "Payload";

        private static readonly ComponentKey Component = new ComponentKey(ComponentKeyName);
        private static readonly PropertyKey<string> Payload = new PropertyKey<string>(PayloadKeyName);

        /// <summary>
        /// Writes one validated schema-v2 record. The live record is never mutated: validation runs
        /// on a deep copy, so a failed pass leaves the in-memory record exactly as it was and the
        /// caller can keep retrying with <c>SaveDirty</c> set (REQ-V2-SAVE-04).
        /// </summary>
        /// <param name="record">Live record to persist; null fails cleanly.</param>
        /// <param name="tier">Resolved tier profile; null fails cleanly.</param>
        /// <param name="catalog">Live crop catalog used by the validator.</param>
        /// <param name="expectedEntityId">Entity identity expected in the record; null accepts the current one.</param>
        /// <param name="saver">Game entity saver; null fails cleanly.</param>
        /// <param name="log">Optional throttled log sink.</param>
        /// <returns>True only when a complete, validated record was written.</returns>
        public static bool Save(IndoorFarmRecord? record, HDBF.Core.Balance.TierProfile? tier,
            ICropCatalog? catalog, string? expectedEntityId, IEntitySaver? saver, IHdbfLog? log)
        {
            if (record is null || tier is null || saver is null)
            {
                return false;
            }

            IndoorFarmRecord candidate;
            IndoorRepairResult validation;
            try
            {
                // REQ-V2-SAVE-04: validate a copy, never the live record; the previous good state
                // must stay available if this write is refused.
                candidate = Clone(record);
                validation = IndoorSaveValidator.ValidateAndRepair(candidate, tier,
                    catalog ?? FallbackCropCatalog.Default, expectedEntityId);
            }
            catch (Exception exception)
            {
                Warn(log, IndoorLogKeys.ValidationFailed,
                    "indoor save validation threw; keeping the previous record", exception);
                return false;
            }

            if (validation is null || validation.Repaired)
            {
                Warn(log, IndoorLogKeys.ValidationFailed,
                    "indoor save refused by pre-write validation (" + Describe(validation) + ")", null);
                return false;
            }

            try
            {
                var text = IndoorSaveCodec.Encode(candidate);
                if (string.IsNullOrEmpty(text))
                {
                    Warn(log, IndoorLogKeys.ValidationFailed, "indoor save encoder returned no payload", null);
                    return false;
                }

                // Round-trip the deterministic text once before it enters the save transaction: a
                // codec fault must never replace a good entity record.
                if (!IndoorSaveCodec.TryDecode(text, out _, out var decodeError) || !string.IsNullOrEmpty(decodeError))
                {
                    Warn(log, IndoorLogKeys.ValidationFailed,
                        "indoor save encoder produced undecodable text: " + decodeError, null);
                    return false;
                }

                var component = saver.GetComponent(Component);
                if (component is null)
                {
                    return false;
                }

                component.Set(Payload, text);
                Write(log, IndoorLogKeys.SaveWrite,
                    "indoor record written, slots=" + SlotCount(candidate));
                return true;
            }
            catch (Exception exception)
            {
                // REQ-V2-SAFE-01: one building must never abort the world save.
                Warn(log, IndoorLogKeys.SaveWrite, "indoor save write failed; previous record kept", exception);
                return false;
            }
        }

        /// <summary>
        /// Reads one schema-v2 record. A missing v2 node falls back to a best-effort v1 read
        /// (<see cref="TryLoadV1"/>); a missing/broken node returns <c>null</c> and the caller keeps
        /// its safe default record (REQ-V2-SAVE-05/06). Never throws.
        /// </summary>
        /// <param name="loader">Game entity loader; null fails cleanly.</param>
        /// <param name="tier">Resolved tier profile; supplies slot capacity.</param>
        /// <param name="faction">Faction resolved from the building blueprint.</param>
        /// <param name="catalog">Live crop catalog used by the repair pass.</param>
        /// <param name="log">Optional throttled log sink.</param>
        /// <param name="degraded">True when the returned record was repaired/migrated.</param>
        /// <returns>The loaded record, or null when the caller must keep its default/old record.</returns>
        public static IndoorFarmRecord? Load(IEntityLoader? loader, HDBF.Core.Balance.TierProfile? tier,
            HdbfFaction faction, ICropCatalog? catalog, IHdbfLog? log, out bool degraded)
        {
            degraded = false;
            if (loader is null || tier is null)
            {
                degraded = true;
                return null;
            }

            try
            {
                IObjectLoader? component = null;
                if (!loader.TryGetComponent(Component, out component) || component is null)
                {
                    // No v2 node: a new v2 building, or a v1 save that needs the legacy reader.
                    return TryLoadV1(loader, tier, faction, catalog, log, ref degraded);
                }

                if (!component.Has(Payload))
                {
                    return TryLoadV1(loader, tier, faction, catalog, log, ref degraded);
                }

                var text = component.Get(Payload);
                if (!IndoorSaveCodec.TryDecode(text, out var record, out var decodeError) || record is null)
                {
                    degraded = true;
                    Write(log, IndoorLogKeys.MigratedFailed,
                        "schema-v2 payload undecodable; trying the v1 fallback: " + decodeError);
                    return TryLoadV1(loader, tier, faction, catalog, log, ref degraded);
                }

                if (!string.IsNullOrEmpty(decodeError))
                {
                    degraded = true;
                    Write(log, IndoorLogKeys.Migrated, "schema-v2 payload decoded with repairs: " + decodeError);
                }

                var validation = IndoorSaveValidator.ValidateAndRepair(record, tier,
                    catalog ?? FallbackCropCatalog.Default, record.EntityId);
                if (validation is not null && validation.Repaired)
                {
                    degraded = true;
                    Write(log, IndoorLogKeys.Migrated,
                        "schema-v2 record repaired on load (" + Describe(validation) + ")");
                }

                if (record.Faction != faction)
                {
                    // The building blueprint is the source of truth for the crop table.
                    record.Faction = faction;
                    degraded = true;
                }

                if (record.Tier != tier.Tier)
                {
                    record.Tier = tier.Tier;
                    degraded = true;
                }

                record.SchemaVersion = IndoorFarmRecord.CurrentSchemaVersion;
                return record;
            }
            catch (Exception exception)
            {
                // REQ-V2-SAVE-06: a broken node must not stop the save from loading.
                degraded = true;
                Warn(log, IndoorLogKeys.MigratedFailed,
                    "indoor record load failed; safe defaults kept", exception);
                return null;
            }
        }

        /// <summary>
        /// Deep copy used by the pre-write validation pass. Kept local because the Core helper is
        /// internal to HDBF.Core and the live record must stay untouched on refusal.
        /// </summary>
        private static IndoorFarmRecord Clone(IndoorFarmRecord source)
        {
            var slots = source.Slots;
            var copySlots = slots is null ? new IndoorSlot[0] : new IndoorSlot[slots.Length];
            for (var index = 0; index < copySlots.Length; index++)
            {
                var slot = slots![index];
                copySlots[index] = slot is null
                    ? new IndoorSlot()
                    : new IndoorSlot
                    {
                        CropId = slot.CropId ?? string.Empty,
                        GrowthDays = slot.GrowthDays,
                        Fatigue = slot.Fatigue,
                        LastCropId = slot.LastCropId ?? string.Empty,
                        Stage = slot.Stage,
                        Flags = slot.Flags,
                    };
            }

            return new IndoorFarmRecord
            {
                SchemaVersion = source.SchemaVersion,
                EntityId = source.EntityId ?? string.Empty,
                Tier = source.Tier,
                Faction = source.Faction,
                Mode = source.Mode,
                CropPolicy = source.CropPolicy,
                FixedCropId = source.FixedCropId ?? string.Empty,
                Slots = copySlots,
                Tank = source.Tank,
                MaintenanceDueDay = source.MaintenanceDueDay,
                MaintenanceCyclesMissed = source.MaintenanceCyclesMissed,
                PowerConnected = source.PowerConnected,
                PowerMode = source.PowerMode,
                PipePort = source.PipePort,
                LogisticsPort = source.LogisticsPort,
                PowerPort = source.PowerPort,
                TimberPipesAdapter = source.TimberPipesAdapter ?? IndoorSaveLimits.AdapterStateNone,
            };
        }
    }
}
