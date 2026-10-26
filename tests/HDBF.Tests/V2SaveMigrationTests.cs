// ============================================================================
// HDBF v2 | Layer: L1 test | Owner: V2-CORE-02
// Purpose: v1 -> v2 migration tests: ordered plot/crop/fatigue mapping, capacity cap,
//          preserved state fields, failure-safe defaults, log keys, sanitisation,
//          idempotence and the v2 -> v2 upgrade step (REQ-V2-SAVE-02/05, EXT-03).
// Requirements: REQ-V2-SAVE-02, REQ-V2-SAVE-05, REQ-V2-EXT-03
// ============================================================================
using System;
using HDBF.Core;
using HDBF.Core.Balance;
using HDBF.Core.Indoor;

namespace HDBF.Tests
{
    public static class V2SaveMigrationTests
    {
        public static void TestV1PlotsMapToLeadingSlotsInOrder()
        {
            var legacy = Legacy(HdbfTier.Advanced, plots: 4);
            var migrated = Migrate(legacy, V2SaveFixtures.Advanced);

            TestAssert.Equal(V2SaveFixtures.Advanced.SlotCapacity, migrated.Slots.Length);
            TestAssert.Equal("legacy-1", migrated.EntityId);
            TestAssert.Equal(IndoorMode.Dense, migrated.Mode, "v1 mode is preserved");
            TestAssert.Equal(IndoorFarmRecord.CurrentSchemaVersion, migrated.SchemaVersion);
            for (var index = 0; index < 4; index++)
            {
                TestAssert.Equal(V2SaveFixtures.FolktailsCrops[index], migrated.Slots[index].CropId,
                    "slot " + index + " crop order");
                TestAssert.Equal(SlotStage.Growing, migrated.Slots[index].Stage);
                TestAssert.Equal(0f, migrated.Slots[index].GrowthDays, 0f, "v1 stored no progress");
                TestAssert.Equal(index * 0.1f, migrated.Slots[index].Fatigue, 1e-6f, "fatigue order");
            }

            TestAssert.Equal(string.Empty, migrated.Slots[4].CropId, "slots beyond the v1 count stay empty");
        }

        public static void TestV1MappingIsCappedByCapacity()
        {
            var legacy = Legacy(HdbfTier.Basic, plots: 100);
            var migrated = Migrate(legacy, V2SaveFixtures.Basic);

            TestAssert.Equal(V2SaveFixtures.Basic.SlotCapacity, migrated.Slots.Length);
            var planted = 0;
            for (var index = 0; index < migrated.Slots.Length; index++)
            {
                if (migrated.Slots[index].CropId.Length > 0)
                {
                    planted++;
                }
            }

            TestAssert.Equal(V2SaveFixtures.Basic.SlotCapacity, planted, "only the capacity is migrated");
        }

        public static void TestV1TankMaintenancePortsAndAdapterArePreserved()
        {
            var legacy = Legacy(HdbfTier.Intermediate, plots: 4);
            var migrated = Migrate(legacy, V2SaveFixtures.Intermediate);

            TestAssert.Equal(1.5f, migrated.Tank, 1e-4f);
            TestAssert.Equal(12.5f, migrated.MaintenanceDueDay, 1e-4f);
            TestAssert.Equal(1, migrated.MaintenanceCyclesMissed);
            TestAssert.True(migrated.PipePort);
            TestAssert.True(migrated.LogisticsPort);
            TestAssert.False(migrated.PowerPort);
            TestAssert.Equal(IndoorSaveLimits.AdapterStateDegraded, migrated.TimberPipesAdapter);
            TestAssert.Equal(IndoorMode.Dense, migrated.Mode);
        }

        public static void TestV1MissingArraysFallBackToSafeDefaults()
        {
            var log = new CapturingLog();
            var migrator = new IndoorSaveMigrator(log);
            var legacy = Legacy(HdbfTier.Intermediate, plots: 3);
            legacy.Crops = null!;

            var ok = migrator.TryMigrateV1(legacy, V2SaveFixtures.Intermediate, out var migrated);

            TestAssert.False(ok, "a truncated v1 node must report failure");
            TestAssert.NotNull(migrated, "the caller still receives the safe default record");
            TestAssert.Equal(IndoorFarmRecord.CurrentSchemaVersion, migrated.SchemaVersion);
            TestAssert.Equal(V2SaveFixtures.Intermediate.SlotCapacity, migrated.Slots.Length);
            for (var index = 0; index < migrated.Slots.Length; index++)
            {
                TestAssert.Equal(string.Empty, migrated.Slots[index].CropId, "safe default slot " + index);
            }

            TestAssert.Equal(V2SaveFixtures.Intermediate.TankCapacity, migrated.Tank, 1e-4f, "full tank");
            TestAssert.Equal(0, migrated.MaintenanceCyclesMissed);
            TestAssert.True(log.Has(IndoorLogKeys.MigratedFailed), "failure logs save.migrated-failed");
        }

        public static void TestV1NullOrInvalidTierFailsWithoutThrowing()
        {
            var migrator = IndoorSaveMigrator.Default;
            var noTier = migrator.TryMigrateV1(Legacy(HdbfTier.Basic, plots: 2), null!, out var fromNullTier);
            TestAssert.False(noTier);
            TestAssert.NotNull(fromNullTier, "a null profile falls back to the Basic safe record");

            var noLegacy = migrator.TryMigrateV1(null!, V2SaveFixtures.Top, out var fromNullLegacy);
            TestAssert.False(noLegacy);
            TestAssert.Equal(V2SaveFixtures.Top.SlotCapacity, fromNullLegacy.Slots.Length);

            var brokenProfile = V2SaveFixtures.Basic with { Tier = (HdbfTier)9 };
            var badProfile = migrator.TryMigrateV1(Legacy(HdbfTier.Basic, plots: 1), brokenProfile,
                out var fromBadProfile);
            TestAssert.False(badProfile, "an unresolvable tier profile must fail the migration");
            TestAssert.NotNull(fromBadProfile, "the failure still hands out the safe default record");
        }

        public static void TestV1UnknownCropBecomesEmptySlot()
        {
            var legacy = Legacy(HdbfTier.Basic, plots: 3);
            legacy.Crops = new[] { "Carrot", "Durian", "Wheat" };
            var migrated = Migrate(legacy, V2SaveFixtures.Basic);

            TestAssert.Equal("Carrot", migrated.Slots[0].CropId);
            TestAssert.Equal(string.Empty, migrated.Slots[1].CropId, "unknown v1 crop cannot be migrated");
            TestAssert.Equal(SlotStage.Empty, migrated.Slots[1].Stage);
            TestAssert.Equal("Wheat", migrated.Slots[2].CropId);
        }

        public static void TestV1DamagedValuesAreSanitized()
        {
            var legacy = Legacy(HdbfTier.Intermediate, plots: 2);
            legacy.Tank = float.NaN;
            legacy.Mode = (HdbfMode)9;
            legacy.MaintenanceDueDay = float.NaN;
            legacy.MaintenanceCyclesMissed = -3;
            legacy.Fatigue = new[] { 5f, -1f };

            var migrated = Migrate(legacy, V2SaveFixtures.Intermediate);

            TestAssert.Equal(0f, migrated.Tank, 1e-4f, "non-finite tank becomes empty");
            TestAssert.Equal(IndoorMode.Normal, migrated.Mode, "unknown mode becomes Normal");
            TestAssert.Equal(MaintenanceDefaults.DefaultCycleDays(V2SaveFixtures.Intermediate),
                migrated.MaintenanceDueDay, 1e-4f);
            TestAssert.Equal(0, migrated.MaintenanceCyclesMissed);
            TestAssert.Equal(1f, migrated.Slots[0].Fatigue, 1e-6f);
            TestAssert.Equal(0f, migrated.Slots[1].Fatigue, 1e-6f);
        }

        public static void TestMigrationIsDeterministicAndIdempotent()
        {
            var legacy = Legacy(HdbfTier.Advanced, plots: 5);
            var first = Migrate(legacy, V2SaveFixtures.Advanced);
            var second = Migrate(legacy, V2SaveFixtures.Advanced);
            TestAssert.Equal(IndoorSaveCodec.Encode(first), IndoorSaveCodec.Encode(second),
                "migrating the same v1 record twice is byte-identical");

            var upgraded = IndoorSaveMigrator.Upgrade(first, V2SaveFixtures.Advanced);
            TestAssert.False(ReferenceEquals(first, upgraded), "upgrade returns a copy");
            TestAssert.Equal(IndoorSaveCodec.Encode(first), IndoorSaveCodec.Encode(upgraded),
                "v2 input is returned unchanged");

            var twice = IndoorSaveMigrator.Upgrade(upgraded, V2SaveFixtures.Advanced);
            TestAssert.Equal(IndoorSaveCodec.Encode(first), IndoorSaveCodec.Encode(twice),
                "the upgrade step is idempotent");

            var result = IndoorSaveValidator.ValidateAndRepair(first, V2SaveFixtures.Advanced,
                FallbackCropCatalog.Default, first.EntityId);
            TestAssert.False(result.Repaired, "a migrated record passes the write validation: " + result);
        }

        public static void TestRepairDefaultsIsTheSafeState()
        {
            foreach (var profile in TierCatalog.All)
            {
                var record = IndoorSaveMigrator.RepairDefaults(profile, HdbfFaction.IronTeeth);

                TestAssert.Equal(IndoorFarmRecord.CurrentSchemaVersion, record.SchemaVersion);
                TestAssert.Equal(profile.Tier, record.Tier);
                TestAssert.Equal(HdbfFaction.IronTeeth, record.Faction);
                TestAssert.Equal(profile.SlotCapacity, record.Slots.Length);
                TestAssert.Equal(profile.TankCapacity, record.Tank, 1e-4f, profile.Tier + ": full tank");
                TestAssert.Equal(MaintenanceDefaults.DefaultCycleDays(profile), record.MaintenanceDueDay, 1e-4f);
                TestAssert.Equal(0, record.MaintenanceCyclesMissed);
                TestAssert.Equal(IndoorSaveLimits.AdapterStateNone, record.TimberPipesAdapter);
                TestAssert.True(record.PipePort, "all v2 tiers have a pipe port");
                for (var index = 0; index < record.Slots.Length; index++)
                {
                    TestAssert.Equal(string.Empty, record.Slots[index].CropId, "empty slot " + index);
                }

                var result = IndoorSaveValidator.ValidateAndRepair(record, profile, FallbackCropCatalog.Default, null);
                TestAssert.False(result.Repaired, profile.Tier + " defaults must validate clean: " + result);
            }
        }

        public static void TestMigrationLogsSaveKeys()
        {
            var successLog = new CapturingLog();
            new IndoorSaveMigrator(successLog).TryMigrateV1(Legacy(HdbfTier.Basic, plots: 1),
                V2SaveFixtures.Basic, out _);
            TestAssert.True(successLog.Has(IndoorLogKeys.Migrated), "success logs save.migrated");

            var failureLog = new CapturingLog();
            new IndoorSaveMigrator(failureLog).TryMigrateV1(null!, V2SaveFixtures.Basic, out _);
            TestAssert.True(failureLog.Has(IndoorLogKeys.MigratedFailed), "failure logs save.migrated-failed");
            TestAssert.True(failureLog.Messages[0].Length > 0, "the failure carries a locatable reason");
        }

        public static void TestUpgradeDoesNotAliasTheLiveRecord()
        {
            var record = V2SaveFixtures.RichRecord(HdbfTier.Top);
            var copy = IndoorSaveMigrator.Upgrade(record, V2SaveFixtures.Top);

            copy.Slots[0].CropId = "Potato";
            copy.Tank = 0f;
            copy.Slots[1].Fatigue = 0.75f;

            TestAssert.Equal(V2SaveFixtures.FolktailsCrops[0], record.Slots[0].CropId, "original crop untouched");
            TestAssert.True(record.Tank > 0f, "original tank untouched");
            TestAssert.Equal(0.05f, record.Slots[1].Fatigue, 1e-6f, "original fatigue untouched");
        }

        private static IndoorFarmRecord Migrate(IndoorLegacyV1Record legacy, TierProfile tier)
        {
            var ok = IndoorSaveMigrator.Default.TryMigrateV1(legacy, tier, out var migrated);
            TestAssert.True(ok, "migration must succeed for a well-formed v1 record");
            TestAssert.NotNull(migrated);
            return migrated;
        }

        private static IndoorLegacyV1Record Legacy(HdbfTier tier, int plots, HdbfFaction faction = HdbfFaction.Folktails)
        {
            var crops = V2SaveFixtures.Crops(faction);
            var ids = new int[plots];
            var cropIds = new string[plots];
            var fatigue = new float[plots];
            for (var index = 0; index < plots; index++)
            {
                ids[index] = 1000 + index;
                cropIds[index] = crops[index % crops.Length];
                fatigue[index] = (index % 10) * 0.1f;
            }

            return new IndoorLegacyV1Record
            {
                EntityId = "legacy-1",
                Tier = tier,
                Faction = faction,
                Mode = HdbfMode.Dense,
                PlotIds = ids,
                Crops = cropIds,
                Fatigue = fatigue,
                Tank = 1.5f,
                MaintenanceDueDay = 12.5f,
                MaintenanceCyclesMissed = 1,
                PowerConnected = false,
                PipePort = true,
                LogisticsPort = (int)tier >= (int)HdbfTier.Intermediate,
                PowerPort = false,
                TimberPipesAdapter = IndoorSaveLimits.AdapterStateDegraded,
            };
        }
    }
}
