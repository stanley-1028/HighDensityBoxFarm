using System;
using System.Collections.Generic;
using System.Globalization;
using HDBF.Core;
using HDBF.Core.Balance;
using HDBF.Core.Save;

namespace HDBF.Tests
{
    /// <summary>
    /// L6-class save-integrity tests that can run without the game (REQ-TEST-06/07, TC-10/TC-11,
    /// chapter 12). They complement <see cref="SaveCodecTests"/> with:
    ///
    /// * a three-pass round trip compared field by field (not only via the canonical string);
    /// * v0 -> v1 migration verified field by field and idempotent across three passes;
    /// * corruption injection: truncated plots.ids, fatigue out of range (including NaN/Infinity),
    ///   missing fields (null collections), invalid enum/schema/number values;
    /// * single-building isolation (a damaged building must not change the healthy one and the
    ///   caller's original record must not be mutated);
    /// * a deterministic damage sweep (fixed LCG seed) asserting the repair invariants on every
    ///   iteration, so no combination of damage may make the codec throw.
    ///
    /// What is deliberately NOT here: JSON-level type errors (e.g. a string where an int is
    /// expected) are rejected by Timberborn's persistence layer and by
    /// <c>HDBF.Persistence.HdbfEntitySaveBridge</c>, both of which require the game assemblies;
    /// they are listed as L3/L6 game-run items in docs/VERIFICATION.md.
    /// </summary>
    public static class SaveRoundTripFaultTests
    {
        private static readonly TierProfile Basic = TierCatalog.Get(HdbfTier.Basic);
        private static readonly TierProfile Intermediate = TierCatalog.Get(HdbfTier.Intermediate);
        private static readonly TierProfile Advanced = TierCatalog.Get(HdbfTier.Advanced);

        // ------------------------------------------------------- three-pass field-by-field round trip

        public static void TestSaveRoundTripThreePassesIsFieldIdentical()
        {
            // AC-06 / chapter 12.4: save -> load -> save three times, every field must survive.
            var original = BuildRichRecord(HdbfTier.Advanced, 300);

            var first = RoundTrip(original, Advanced, 12f);
            AssertRecordsEqual(original, first, "pass 1 must preserve a clean record");

            var current = first;
            for (var pass = 2; pass <= 3; pass++)
            {
                current = RoundTrip(current, Advanced, 12f);
                AssertRecordsEqual(first, current, "round-trip pass " + pass);
                TestAssert.Equal(HdbfSaveCodec.Canonicalize(first), HdbfSaveCodec.Canonicalize(current),
                    "canonical text changed on pass " + pass);
            }
        }

        public static void TestSaveRoundTripKeepsLargeEntityIdAndPorts()
        {
            var record = BuildRichRecord(HdbfTier.Basic, 60);
            record.EntityId = 4294967299L; // 2^32 + 3, beyond a 32-bit int
            record.PortPower = true;
            record.PortPipe = true;
            record.PortLogistics = true;
            record.Adapters = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["pipes"] = "active",
                ["belts"] = "failed",
                ["stairs"] = "missing",
            };

            var roundTripped = RoundTrip(record, Basic, 4f);

            TestAssert.Equal(4294967299L, roundTripped.EntityId);
            TestAssert.True(roundTripped.PortPower);
            TestAssert.True(roundTripped.PortPipe);
            TestAssert.True(roundTripped.PortLogistics);
            TestAssert.Equal(3, roundTripped.Adapters.Count);
            TestAssert.Equal("failed", roundTripped.Adapters["belts"]);
            AssertRecordsEqual(record, roundTripped, "rich record");
        }

        public static void TestSaveRoundTripKeepsFatigueAndExclusionsOrder()
        {
            var record = BuildRichRecord(HdbfTier.Advanced, 300);
            record.PlotIds = new[] { 40, 10, 30, 20 };
            record.CropIds = new[] { "Carrot", "Potato", "Wheat", "" };
            record.Fatigue = new[] { 0.9f, 0.25f, 0f, 0.5f };
            record.ExcludedPlotIds = new[] { 777, 555 };

            var roundTripped = RoundTrip(record, Advanced, 8f);

            TestAssert.Equal(4, roundTripped.PlotIds.Length);
            TestAssert.Equal(40, roundTripped.PlotIds[0], "registration order must not be re-sorted by the codec");
            TestAssert.Equal(30, roundTripped.PlotIds[2]);
            TestAssert.Equal("Wheat", roundTripped.CropIds[2]);
            TestAssert.Equal(string.Empty, roundTripped.CropIds[3]);
            TestAssert.Equal(0.25f, roundTripped.Fatigue[1], 1e-6f);
            TestAssert.Equal(777, roundTripped.ExcludedPlotIds[0]);
            TestAssert.Equal(555, roundTripped.ExcludedPlotIds[1]);
        }

        // ------------------------------------------------------- v0 -> v1 migration (TC-10, REQ-TEST-06)

        public static void TestMigrateV0ThreePassesIsFieldIdentical()
        {
            var v0 = BuildRichRecord(HdbfTier.Intermediate, 150);
            v0.SchemaVersion = 0;
            v0.DueDay = 0f;

            var once = HdbfSaveCodec.Migrate(v0, Intermediate, 7f);
            TestAssert.Equal(HdbfBuildingRecord.CurrentSchemaVersion, once.SchemaVersion);
            TestAssert.Equal(17f, once.DueDay, 1e-6f, "v0 maintenance date = current day + cycle");
            AssertRecordsEqual(v0, once, "migration must not lose v0 data", ignoreSchema: true, ignoreDueDay: true);

            var twice = HdbfSaveCodec.Migrate(once, Intermediate, 7f);
            var thrice = HdbfSaveCodec.Migrate(twice, Intermediate, 7f);
            AssertRecordsEqual(once, twice, "v0->v1 pass 2");
            AssertRecordsEqual(once, thrice, "v0->v1 pass 3");
            TestAssert.Equal(HdbfSaveCodec.Canonicalize(once), HdbfSaveCodec.Canonicalize(twice),
                "second migration changed the record");
            TestAssert.Equal(HdbfSaveCodec.Canonicalize(once), HdbfSaveCodec.Canonicalize(thrice),
                "third migration changed the record");
        }

        public static void TestMigrateV0KeepsExistingDueDay()
        {
            var v0 = BuildRichRecord(HdbfTier.Intermediate, 150);
            v0.SchemaVersion = 0;
            v0.DueDay = 33.5f;

            var migrated = HdbfSaveCodec.Migrate(v0, Intermediate, 7f);
            TestAssert.Equal(33.5f, migrated.DueDay, 1e-6f);
        }

        public static void TestMigrateV0AfterSanitizeFillsDefaultsExactlyOnce()
        {
            // A v0 prototype record had no fatigue array and no maintenance fields. The bridge
            // always sanitises before migrating, so this is the real production path. The
            // sanitiser must keep the v0 stamp long enough for Migrate to fill the v1 defaults
            // (regression: it used to normalise the stamp first, leaving DueDay = 0).
            var v0 = new HdbfBuildingRecord
            {
                SchemaVersion = 0,
                EntityId = 77,
                Tier = HdbfTier.Intermediate,
                Mode = HdbfMode.Dense,
                PlotIds = new[] { 1, 2, 3 },
                CropIds = new[] { "Carrot", "Carrot", "Potato" },
                Fatigue = null!,
                ExcludedPlotIds = null!,
                DueDay = 0f,
                CyclesMissed = 0,
                TankWater = 0f,
                Adapters = null!,
            };

            var clean = HdbfSaveCodec.TrySanitize(v0, Intermediate, 5f, out var repaired, out var reason);
            // The null fatigue/excluded/adapters arrays are repaired separately, so this record
            // is not "clean"; the assertion that matters is that stock migration still ran.
            TestAssert.False(clean, "missing v0 arrays are repaired");
            TestAssert.Equal("fatigue-length-mismatch", reason);
            TestAssert.Equal(3, repaired.Fatigue.Length);
            TestAssert.Equal(0f, repaired.Fatigue[0], 1e-6f);
            TestAssert.Equal(0, repaired.ExcludedPlotIds.Length);
            TestAssert.NotNull(repaired.Adapters);
            TestAssert.Equal(HdbfBuildingRecord.CurrentSchemaVersion, repaired.SchemaVersion);
            TestAssert.Equal(15f, repaired.DueDay, 1e-6f,
                "sanitise must let migration fill DueDay = current day + maintenance cycle");

            var migratedAgain = HdbfSaveCodec.Migrate(repaired, Intermediate, 5f);
            AssertRecordsEqual(repaired, migratedAgain, "sanitise+migrate must be idempotent");
        }

        public static void TestSanitizeCleanV0IsMigratedNotDegraded()
        {
            var v0 = BuildRichRecord(HdbfTier.Intermediate, 150);
            v0.SchemaVersion = 0;
            v0.DueDay = 0f;

            var clean = HdbfSaveCodec.TrySanitize(v0, Intermediate, 5f, out var repaired, out var reason);

            TestAssert.True(clean, "a structurally complete v0 record is migratable, not damaged");
            TestAssert.True(reason is null, "no repair reason for a clean v0 migration");
            TestAssert.Equal(HdbfBuildingRecord.CurrentSchemaVersion, repaired.SchemaVersion);
            TestAssert.Equal(15f, repaired.DueDay, 1e-6f);
        }

        public static void TestSanitizeNegativeSchemaMigratesFromZero()
        {
            var v0 = BuildRichRecord(HdbfTier.Intermediate, 150);
            v0.SchemaVersion = -3;
            v0.DueDay = 0f;

            var clean = HdbfSaveCodec.TrySanitize(v0, Intermediate, 5f, out var repaired, out var reason);
            TestAssert.False(clean, "a negative schema stamp is repaired");
            TestAssert.Equal("schema-out-of-range", reason);
            TestAssert.Equal(HdbfBuildingRecord.CurrentSchemaVersion, repaired.SchemaVersion);
            TestAssert.Equal(15f, repaired.DueDay, 1e-6f,
                "a repaired pre-v1 stamp must still receive the v1 maintenance default");
        }

        public static void TestMigrateNewerSchemaKeepsStamp()
        {
            var record = BuildRichRecord(HdbfTier.Basic, 60);
            record.SchemaVersion = HdbfBuildingRecord.CurrentSchemaVersion + 1;

            var migrated = HdbfSaveCodec.Migrate(record, Basic, 2f);

            TestAssert.Equal(HdbfBuildingRecord.CurrentSchemaVersion + 1, migrated.SchemaVersion,
                "a newer save must be opened read-only, not downgraded");
            AssertRecordsEqual(record, migrated, "newer-schema migration must not alter data");
        }

        // ------------------------------------------------------- corruption: truncated arrays (TC-11)

        public static void TestSanitizeTruncatedPlotIdsDropsOrphanCropAndFatigueEntries()
        {
            // TC-11: the building node was cut in half -> plots.ids has 2 ids while crops/fatigue
            // still have the original 60 entries. Keep the ids that survived, drop the orphans.
            var record = BuildRichRecord(HdbfTier.Basic, 60);
            var originalCrop2 = record.CropIds[2];
            record.PlotIds = new[] { record.PlotIds[0], record.PlotIds[1] };

            var clean = HdbfSaveCodec.TrySanitize(record, Basic, 10f, out var repaired, out var reason);

            TestAssert.False(clean);
            TestAssert.True(reason is not null && reason!.Contains("mismatch"), "reason was " + reason);
            TestAssert.Equal(2, repaired.PlotIds.Length);
            TestAssert.Equal(2, repaired.CropIds.Length);
            TestAssert.Equal(2, repaired.Fatigue.Length);
            TestAssert.Equal(record.PlotIds[0], repaired.PlotIds[0]);
            TestAssert.Equal(record.PlotIds[1], repaired.PlotIds[1]);
            TestAssert.Equal(originalCrop2, record.CropIds[2], "the caller's original record must not be mutated");
        }

        public static void TestSanitizeTruncatedCropsFillsMissingEntriesWithEmpty()
        {
            var record = BuildRichRecord(HdbfTier.Basic, 60);
            record.CropIds = new[] { "Carrot", "Potato" };

            var clean = HdbfSaveCodec.TrySanitize(record, Basic, 10f, out var repaired, out var reason);

            TestAssert.False(clean);
            TestAssert.Equal("crops-length-mismatch", reason);
            TestAssert.Equal(60, repaired.CropIds.Length);
            TestAssert.Equal("Carrot", repaired.CropIds[0]);
            TestAssert.Equal("Potato", repaired.CropIds[1]);
            for (var i = 2; i < repaired.CropIds.Length; i++)
            {
                TestAssert.Equal(string.Empty, repaired.CropIds[i], "crop " + i + " must be empty");
            }
        }

        public static void TestSanitizeTruncatedFatigueFillsZero()
        {
            var record = BuildRichRecord(HdbfTier.Basic, 60);
            record.Fatigue = new[] { 0.5f, 0.25f };

            var clean = HdbfSaveCodec.TrySanitize(record, Basic, 10f, out var repaired, out var reason);

            TestAssert.False(clean);
            TestAssert.Equal("fatigue-length-mismatch", reason);
            TestAssert.Equal(60, repaired.Fatigue.Length);
            TestAssert.Equal(0.5f, repaired.Fatigue[0], 1e-6f);
            for (var i = 2; i < repaired.Fatigue.Length; i++)
            {
                TestAssert.Equal(0f, repaired.Fatigue[i], 1e-6f, "fatigue " + i + " must be zero");
            }
        }

        // ------------------------------------------------------- corruption: out-of-range values

        public static void TestSanitizeFatigueOutOfRangeVariantsAreClamped()
        {
            var values = new[] { 5f, -2f, float.NaN, float.PositiveInfinity, float.NegativeInfinity, 1.0001f };
            foreach (var value in values)
            {
                var record = BuildRichRecord(HdbfTier.Advanced, 300);
                record.Fatigue[3] = value;
                record.Fatigue[4] = 0.4f;

                var clean = HdbfSaveCodec.TrySanitize(record, Advanced, 10f, out var repaired, out var reason);

                TestAssert.False(clean, "fatigue " + value + " must be reported as repaired");
                TestAssert.Equal("fatigue-out-of-range", reason);
                var clamped = repaired.Fatigue[3];
                TestAssert.True(!float.IsNaN(clamped) && !float.IsInfinity(clamped), "clamped fatigue must be finite");
                TestAssert.True(clamped >= 0f && clamped <= 1f, "clamped fatigue out of range: " + clamped);
                TestAssert.Equal(0.4f, repaired.Fatigue[4], 1e-6f, "neighbouring fatigue must be untouched");
            }
        }

        public static void TestSanitizeInvalidTierIsReplacedByProfileTier()
        {
            var record = BuildRichRecord(HdbfTier.Basic, 60);
            record.Tier = (HdbfTier)42;

            var clean = HdbfSaveCodec.TrySanitize(record, Basic, 10f, out var repaired, out var reason);

            TestAssert.False(clean);
            TestAssert.Equal("tier-invalid", reason);
            TestAssert.Equal(HdbfTier.Basic, repaired.Tier);
        }

        public static void TestSanitizeNonFiniteDueDayIsRescheduled()
        {
            foreach (var value in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            {
                var record = BuildRichRecord(HdbfTier.Advanced, 300);
                record.DueDay = value;

                var clean = HdbfSaveCodec.TrySanitize(record, Advanced, 20f, out var repaired, out var reason);

                TestAssert.False(clean);
                TestAssert.Equal("due-day-invalid", reason);
                TestAssert.Equal(30f, repaired.DueDay, 1e-6f, "maintenance must be rescheduled one cycle from now");
            }
        }

        public static void TestSanitizeNegativeCyclesMissedIsReset()
        {
            var record = BuildRichRecord(HdbfTier.Advanced, 300);
            record.CyclesMissed = -3;

            var clean = HdbfSaveCodec.TrySanitize(record, Advanced, 10f, out var repaired, out var reason);

            TestAssert.False(clean);
            TestAssert.Equal("cycles-missed-invalid", reason);
            TestAssert.Equal(0, repaired.CyclesMissed);
        }

        public static void TestSanitizeNegativeAndInfiniteTankIsReset()
        {
            foreach (var value in new[] { -1f, float.NegativeInfinity, float.PositiveInfinity })
            {
                var record = BuildRichRecord(HdbfTier.Basic, 60);
                record.TankWater = value;

                var clean = HdbfSaveCodec.TrySanitize(record, Basic, 10f, out var repaired, out var reason);

                TestAssert.False(clean);
                TestAssert.Equal("tank-invalid", reason);
                TestAssert.Equal(0f, repaired.TankWater);
            }
        }

        public static void TestSanitizeCombinedDamageRepairsEveryFieldWithoutThrowing()
        {
            var record = BuildRichRecord(HdbfTier.Advanced, 300);
            record.SchemaVersion = 9;
            record.Tier = (HdbfTier)99;
            record.Mode = (HdbfMode)99;
            record.DueDay = float.NaN;
            record.CyclesMissed = -1;
            record.TankWater = float.PositiveInfinity;
            record.PlotIds = new[] { 1, 1, 2, 3 };
            record.CropIds = new[] { "Carrot" };
            record.Fatigue = new[] { 5f };
            record.ExcludedPlotIds = new[] { 1, 1, 999 };

            var clean = HdbfSaveCodec.TrySanitize(record, Advanced, 10f, out var repaired, out var reason);

            TestAssert.False(clean);
            TestAssert.NotNull(reason);
            TestAssert.Equal(HdbfBuildingRecord.CurrentSchemaVersion, repaired.SchemaVersion);
            TestAssert.Equal(HdbfTier.Advanced, repaired.Tier);
            TestAssert.Equal(HdbfMode.Normal, repaired.Mode);
            TestAssert.Equal(20f, repaired.DueDay, 1e-6f);
            TestAssert.Equal(0, repaired.CyclesMissed);
            TestAssert.Equal(0f, repaired.TankWater);
            TestAssert.Equal(3, repaired.PlotIds.Length); // duplicate 1 dropped
            TestAssert.Equal(3, repaired.CropIds.Length);
            TestAssert.Equal(3, repaired.Fatigue.Length);
            TestAssert.Equal(1, repaired.ExcludedPlotIds.Length); // duplicate and registered ids dropped
            TestAssert.Equal(999, repaired.ExcludedPlotIds[0]);
        }

        // ------------------------------------------------------- missing fields

        public static void TestSanitizeNullCollectionsAreTreatedAsEmpty()
        {
            var record = new HdbfBuildingRecord
            {
                SchemaVersion = HdbfBuildingRecord.CurrentSchemaVersion,
                EntityId = 5,
                Tier = HdbfTier.Basic,
                Mode = HdbfMode.Normal,
                PlotIds = null!,
                CropIds = null!,
                Fatigue = null!,
                ExcludedPlotIds = null!,
                Adapters = null!,
                DueDay = 10f,
                TankWater = 0f,
            };

            var clean = HdbfSaveCodec.TrySanitize(record, Basic, 10f, out var repaired, out var reason);

            TestAssert.True(clean, "null optional collections are the documented omitted-default case; reason=" + reason);
            TestAssert.Equal(0, repaired.PlotIds.Length);
            TestAssert.Equal(0, repaired.CropIds.Length);
            TestAssert.Equal(0, repaired.Fatigue.Length);
            TestAssert.Equal(0, repaired.ExcludedPlotIds.Length);
            TestAssert.NotNull(repaired.Adapters);
            TestAssert.Equal(0, repaired.Adapters.Count);
        }

        public static void TestSanitizeNullPlotsWithOrphanCropsDropsEverything()
        {
            var record = BuildRichRecord(HdbfTier.Basic, 60);
            record.PlotIds = null!; // ids lost, crop/fatigue arrays intact

            var clean = HdbfSaveCodec.TrySanitize(record, Basic, 10f, out var repaired, out var reason);

            TestAssert.False(clean);
            TestAssert.True(reason is not null && reason!.Contains("mismatch"), "reason was " + reason);
            TestAssert.Equal(0, repaired.PlotIds.Length);
            TestAssert.Equal(0, repaired.CropIds.Length);
            TestAssert.Equal(0, repaired.Fatigue.Length);
        }

        public static void TestSanitizeDropsEmptyAdapterKeysAndKeepsNullValuesEmpty()
        {
            var record = BuildRichRecord(HdbfTier.Basic, 60);
            record.Adapters = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [""] = "active",
                ["pipes"] = null!,
            };

            var clean = HdbfSaveCodec.TrySanitize(record, Basic, 10f, out var repaired, out _);

            TestAssert.True(clean, "adapter map cleanup must not degrade the record");
            TestAssert.Equal(1, repaired.Adapters.Count);
            TestAssert.True(repaired.Adapters.ContainsKey("pipes"));
            TestAssert.Equal(string.Empty, repaired.Adapters["pipes"]);
        }

        // ------------------------------------------------------- isolation (REQ-TEST-07 / REQ-SAVE-09)

        public static void TestDamagedBuildingDoesNotAffectHealthyBuilding()
        {
            var healthy = BuildRichRecord(HdbfTier.Basic, 60);
            var damaged = BuildRichRecord(HdbfTier.Basic, 60);
            var healthyCanonical = HdbfSaveCodec.Canonicalize(healthy);

            damaged.PlotIds = new[] { damaged.PlotIds[0] };
            damaged.Fatigue = new[] { float.NaN };
            damaged.TankWater = 99f;

            // Repair the damaged building only, exactly like the per-entity save bridge does.
            var clean = HdbfSaveCodec.TrySanitize(damaged, Basic, 10f, out var repairedDamaged, out var reason);
            var healthyAgain = HdbfSaveCodec.TrySanitize(healthy, Basic, 10f, out var repairedHealthy, out var healthyReason);

            TestAssert.False(clean, "the damaged record must be reported as repaired");
            TestAssert.True(reason is not null);
            TestAssert.True(healthyAgain, "the healthy record must stay clean; reason=" + healthyReason);
            TestAssert.Equal(healthyCanonical, HdbfSaveCodec.Canonicalize(repairedHealthy),
                "repairing one building changed another building's data");
            TestAssert.Equal(1, repairedDamaged.PlotIds.Length);
            TestAssert.Equal(60, repairedHealthy.PlotIds.Length);
        }

        // ------------------------------------------------------- deterministic damage sweep

        public static void TestDeterministicDamageSweepAlwaysYieldsValidRecord()
        {
            // Fixed LCG seed: reproducible on every machine, no time/random dependency.
            uint state = 0xC0FFEEu;
            for (var iteration = 0; iteration < 200; iteration++)
            {
                var record = BuildRichRecord(HdbfTier.Basic, 60);
                var kind = Next(ref state) % 12u;
                switch (kind)
                {
                    case 0:
                        record.PlotIds = Truncate(record.PlotIds, (int)(Next(ref state) % 61u));
                        break;
                    case 1:
                        record.Fatigue[(int)(Next(ref state) % 60u)] = PickFloat(ref state);
                        break;
                    case 2:
                        record.CropIds = (Next(ref state) % 2u) == 0u ? null! : Truncate(record.CropIds, (int)(Next(ref state) % 61u));
                        break;
                    case 3:
                        record.Mode = (HdbfMode)(int)(Next(ref state) % 9u);
                        break;
                    case 4:
                        record.Tier = (HdbfTier)(int)(Next(ref state) % 9u);
                        break;
                    case 5:
                        record.SchemaVersion = (int)(Next(ref state) % 6u) - 2;
                        break;
                    case 6:
                        record.TankWater = PickFloat(ref state);
                        break;
                    case 7:
                        record.CyclesMissed = (int)(Next(ref state) % 9u) - 4;
                        break;
                    case 8:
                        record.DueDay = (Next(ref state) % 2u) == 0u ? float.NaN : -1f;
                        break;
                    case 9:
                        record.Adapters = (Next(ref state) % 2u) == 0u
                            ? null!
                            : new Dictionary<string, string>(StringComparer.Ordinal) { [""] = "x", ["pipes"] = null!, ["belts"] = "active" };
                        break;
                    case 10:
                        record.ExcludedPlotIds = new[] { 1, 1, record.PlotIds[0], 99999 };
                        break;
                    default:
                        record.Fatigue = (Next(ref state) % 2u) == 0u ? null! : Truncate(record.Fatigue, (int)(Next(ref state) % 61u));
                        break;
                }

                var clean = HdbfSaveCodec.TrySanitize(record, Basic, 10f, out var repaired, out _);

                TestAssert.NotNull(repaired, "iteration " + iteration + " returned a null record");
                TestAssert.Equal(repaired.PlotIds.Length, repaired.CropIds.Length, "iteration " + iteration + " crops length");
                TestAssert.Equal(repaired.PlotIds.Length, repaired.Fatigue.Length, "iteration " + iteration + " fatigue length");
                TestAssert.True(repaired.SchemaVersion >= 1 && repaired.SchemaVersion <= HdbfBuildingRecord.CurrentSchemaVersion + 1,
                    "iteration " + iteration + " schema " + repaired.SchemaVersion);
                TestAssert.True(Enum.IsDefined(typeof(HdbfTier), repaired.Tier), "iteration " + iteration + " tier");
                TestAssert.True(Enum.IsDefined(typeof(HdbfMode), repaired.Mode), "iteration " + iteration + " mode");
                TestAssert.True(!float.IsNaN(repaired.DueDay) && !float.IsInfinity(repaired.DueDay), "iteration " + iteration + " dueDay");

                var seen = new HashSet<int>();
                for (var i = 0; i < repaired.PlotIds.Length; i++)
                {
                    TestAssert.True(seen.Add(repaired.PlotIds[i]), "iteration " + iteration + " duplicate plot id");
                    var fatigue = repaired.Fatigue[i];
                    TestAssert.True(!float.IsNaN(fatigue) && !float.IsInfinity(fatigue) && fatigue >= 0f && fatigue <= 1f,
                        "iteration " + iteration + " fatigue value " + fatigue);
                }

                TestAssert.True(repaired.TankWater >= 0f && repaired.TankWater <= Basic.TankCapacity,
                    "iteration " + iteration + " tank " + repaired.TankWater);

                var excluded = new HashSet<int>();
                for (var i = 0; i < repaired.ExcludedPlotIds.Length; i++)
                {
                    TestAssert.True(excluded.Add(repaired.ExcludedPlotIds[i]), "iteration " + iteration + " duplicate exclusion");
                    TestAssert.False(seen.Contains(repaired.ExcludedPlotIds[i]), "iteration " + iteration + " exclusion is a registered plot");
                }

                TestAssert.NotNull(repaired.Adapters);
                if (!clean)
                {
                    // A repaired record must always come back clean on the next pass.
                    var again = HdbfSaveCodec.TrySanitize(repaired, Basic, 10f, out var repairedAgain, out var secondReason);
                    TestAssert.True(again, "iteration " + iteration + " repaired record is still dirty: " + secondReason);
                    TestAssert.Equal(HdbfSaveCodec.Canonicalize(repaired), HdbfSaveCodec.Canonicalize(repairedAgain),
                        "iteration " + iteration + " repair is not idempotent");
                }
            }
        }

        // ------------------------------------------------------- helpers

        private static HdbfBuildingRecord RoundTrip(HdbfBuildingRecord record, TierProfile profile, float currentDay)
        {
            var clean = HdbfSaveCodec.TrySanitize(record, profile, currentDay, out var repaired, out var reason);
            TestAssert.True(clean, "a clean record was reported as repaired: " + reason);
            return HdbfSaveCodec.Migrate(repaired, profile, currentDay);
        }

        private static HdbfBuildingRecord BuildRichRecord(HdbfTier tier, int plots)
        {
            var profile = TierCatalog.Get(tier);
            var ids = new int[plots];
            var crops = new string[plots];
            var fatigue = new float[plots];
            for (var i = 0; i < plots; i++)
            {
                ids[i] = 1000 + i;
                crops[i] = i % 3 == 0 ? "Carrot" : i % 3 == 1 ? "Potato" : "Wheat";
                fatigue[i] = (i % 20) * 0.05f;
            }

            return new HdbfBuildingRecord
            {
                SchemaVersion = HdbfBuildingRecord.CurrentSchemaVersion,
                EntityId = 4294967299L,
                Tier = tier,
                Mode = HdbfMode.Normal,
                PlotIds = ids,
                CropIds = crops,
                Fatigue = fatigue,
                ExcludedPlotIds = new[] { 900000, 900001 },
                DueDay = 30f,
                CyclesMissed = 0,
                TankWater = profile.TankCapacity,
                PortPower = false,
                PortPipe = true,
                PortLogistics = false,
                Adapters = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["pipes"] = "active",
                    ["belts"] = "failed",
                },
            };
        }

        private static void AssertRecordsEqual(HdbfBuildingRecord expected, HdbfBuildingRecord actual, string context,
            bool ignoreSchema = false, bool ignoreDueDay = false)
        {
            if (!ignoreSchema)
            {
                TestAssert.Equal(expected.SchemaVersion, actual.SchemaVersion, context + ": schemaVersion");
            }

            TestAssert.Equal(expected.EntityId, actual.EntityId, context + ": entityId");
            TestAssert.Equal(expected.Tier, actual.Tier, context + ": tier");
            TestAssert.Equal(expected.Mode, actual.Mode, context + ": mode");

            TestAssert.Equal(expected.PlotIds.Length, actual.PlotIds.Length, context + ": plot count");
            for (var i = 0; i < expected.PlotIds.Length; i++)
            {
                TestAssert.Equal(expected.PlotIds[i], actual.PlotIds[i], context + ": plot id " + i);
                TestAssert.Equal(expected.CropIds[i], actual.CropIds[i], context + ": crop " + i);
                TestAssert.Equal(expected.Fatigue[i], actual.Fatigue[i], 1e-6f, context + ": fatigue " + i);
            }

            TestAssert.Equal(expected.ExcludedPlotIds.Length, actual.ExcludedPlotIds.Length, context + ": exclusion count");
            for (var i = 0; i < expected.ExcludedPlotIds.Length; i++)
            {
                TestAssert.Equal(expected.ExcludedPlotIds[i], actual.ExcludedPlotIds[i], context + ": exclusion " + i);
            }

            if (!ignoreDueDay)
            {
                TestAssert.Equal(expected.DueDay, actual.DueDay, 1e-6f, context + ": dueDay");
            }

            TestAssert.Equal(expected.CyclesMissed, actual.CyclesMissed, context + ": cyclesMissed");
            TestAssert.Equal(expected.TankWater, actual.TankWater, 1e-6f, context + ": tankWater");
            TestAssert.Equal(expected.PortPower, actual.PortPower, context + ": portPower");
            TestAssert.Equal(expected.PortPipe, actual.PortPipe, context + ": portPipe");
            TestAssert.Equal(expected.PortLogistics, actual.PortLogistics, context + ": portLogistics");
            TestAssert.Equal(expected.Adapters.Count, actual.Adapters.Count, context + ": adapter count");
            foreach (var pair in expected.Adapters)
            {
                TestAssert.True(actual.Adapters.ContainsKey(pair.Key), context + ": missing adapter " + pair.Key);
                TestAssert.Equal(pair.Value, actual.Adapters[pair.Key], context + ": adapter " + pair.Key);
            }
        }

        private static uint Next(ref uint state)
        {
            state = unchecked(state * 1664525u + 1013904223u);
            return state;
        }

        private static float PickFloat(ref uint state)
        {
            switch (Next(ref state) % 7u)
            {
                case 0: return float.NaN;
                case 1: return float.PositiveInfinity;
                case 2: return float.NegativeInfinity;
                case 3: return -2f;
                case 4: return 2f;
                case 5: return 1.0001f;
                default: return (Next(ref state) % 100u) / 100f;
            }
        }

        private static int[] Truncate(int[] values, int length)
        {
            var result = new int[length];
            Array.Copy(values, result, Math.Min(length, values.Length));
            return result;
        }

        private static string[] Truncate(string[] values, int length)
        {
            var result = new string[length];
            Array.Copy(values, result, Math.Min(length, values.Length));
            return result;
        }

        private static float[] Truncate(float[] values, int length)
        {
            var result = new float[length];
            Array.Copy(values, result, Math.Min(length, values.Length));
            return result;
        }
    }
}
