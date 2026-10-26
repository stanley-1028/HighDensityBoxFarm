using System;
using System.Collections.Generic;
using HDBF.Core;
using HDBF.Core.Balance;
using HDBF.Core.Save;

namespace HDBF.Tests
{
    /// <summary>
    /// L1 tests for the save record codec: default construction, damaged-record repair
    /// (REQ-SAFE-08), idempotent migration (REQ-SAVE-05/06, TC-10), canonicalisation and
    /// round-trip stability (TC-11 class, chapter 12.4).
    /// </summary>
    public static class SaveCodecTests
    {
        private static readonly TierProfile Basic = TierCatalog.Get(HdbfTier.Basic);
        private static readonly TierProfile Intermediate = TierCatalog.Get(HdbfTier.Intermediate);
        private static readonly TierProfile Top = TierCatalog.Get(HdbfTier.Top);

        // ---------------------------------------------------------------- defaults

        public static void TestCreateDefaultBasicRecord()
        {
            var record = HdbfSaveCodec.CreateDefault(3, HdbfTier.Basic, Basic, 10f);

            TestAssert.Equal(HdbfBuildingRecord.CurrentSchemaVersion, record.SchemaVersion);
            TestAssert.Equal(3L, record.EntityId);
            TestAssert.Equal(HdbfTier.Basic, record.Tier);
            TestAssert.Equal(HdbfMode.Normal, record.Mode);
            TestAssert.Equal(0, record.PlotIds.Length);
            TestAssert.Equal(0, record.CropIds.Length);
            TestAssert.Equal(0, record.Fatigue.Length);
            TestAssert.Equal(0, record.ExcludedPlotIds.Length);
            TestAssert.Equal(0f, record.DueDay, "irrigation-only tier has no material maintenance date");
            TestAssert.Equal(0, record.CyclesMissed);
            TestAssert.Equal(0f, record.TankWater);
            TestAssert.False(record.PortPower);
            TestAssert.False(record.PortPipe);
            TestAssert.False(record.PortLogistics);
            TestAssert.Equal(0, record.Adapters.Count);
        }

        public static void TestCreateDefaultSchedulesMaintenanceFromCycle()
        {
            var intermediate = HdbfSaveCodec.CreateDefault(1, HdbfTier.Intermediate, Intermediate, 5f);
            TestAssert.Equal(15f, intermediate.DueDay, 1e-6f);

            var advanced = HdbfSaveCodec.CreateDefault(2, HdbfTier.Advanced, TierCatalog.Get(HdbfTier.Advanced), 7.5f);
            TestAssert.Equal(17.5f, advanced.DueDay, 1e-6f);
        }

        // ---------------------------------------------------------------- repair

        public static void TestSanitizeAcceptsCleanRecord()
        {
            var record = BuildValidRecord();
            var clean = HdbfSaveCodec.TrySanitize(record, Basic, 10f, out var repaired, out var reason);

            TestAssert.True(clean, "a valid record must not be reported as degraded");
            TestAssert.Null(reason);
            TestAssert.Equal(2, repaired.PlotIds.Length);
            TestAssert.Equal(1, repaired.PlotIds[0]);
            TestAssert.Equal(2, repaired.PlotIds[1]);
            TestAssert.Equal("Carrot", repaired.CropIds[0]);
            TestAssert.Equal(0.1f, repaired.Fatigue[0], 1e-6f);
        }

        public static void TestSanitizeClampsFatigueAboveOne()
        {
            var record = BuildValidRecord();
            record.Fatigue = new[] { 5f, 0.2f };

            var clean = HdbfSaveCodec.TrySanitize(record, Basic, 10f, out var repaired, out var reason);

            TestAssert.False(clean);
            TestAssert.Equal("fatigue-out-of-range", reason);
            TestAssert.Equal(1f, repaired.Fatigue[0], 1e-6f);
            TestAssert.Equal(0.2f, repaired.Fatigue[1], 1e-6f);
        }

        public static void TestSanitizeRepairsMismatchedArrays()
        {
            var record = BuildValidRecord();
            record.CropIds = new[] { "Carrot" };
            record.Fatigue = new[] { 0.1f };

            var clean = HdbfSaveCodec.TrySanitize(record, Basic, 10f, out var repaired, out var reason);

            TestAssert.False(clean);
            TestAssert.Equal("crops-length-mismatch", reason);
            TestAssert.Equal(2, repaired.PlotIds.Length);
            TestAssert.Equal(2, repaired.CropIds.Length);
            TestAssert.Equal(2, repaired.Fatigue.Length);
            TestAssert.Equal("Carrot", repaired.CropIds[0]);
            TestAssert.Equal(string.Empty, repaired.CropIds[1]);
            TestAssert.Equal(0f, repaired.Fatigue[1], 1e-6f);
        }

        public static void TestSanitizeDropsDuplicatePlotIds()
        {
            var record = BuildValidRecord();
            record.PlotIds = new[] { 5, 6, 5, 7 };
            record.CropIds = new[] { "a", "b", "c", "d" };
            record.Fatigue = new[] { 0.1f, 0.2f, 0.3f, 0.4f };

            var clean = HdbfSaveCodec.TrySanitize(record, Basic, 10f, out var repaired, out var reason);

            TestAssert.False(clean);
            TestAssert.Equal("duplicate-plot-id", reason);
            TestAssert.Equal(3, repaired.PlotIds.Length);
            TestAssert.Equal(5, repaired.PlotIds[0]);
            TestAssert.Equal(6, repaired.PlotIds[1]);
            TestAssert.Equal(7, repaired.PlotIds[2]);
            TestAssert.Equal("b", repaired.CropIds[1]);
            TestAssert.Equal("d", repaired.CropIds[2]);
            TestAssert.Equal(0.4f, repaired.Fatigue[2], 1e-6f);
        }

        public static void TestSanitizeRepairsNaNtank()
        {
            var record = BuildValidRecord();
            record.TankWater = float.NaN;

            var clean = HdbfSaveCodec.TrySanitize(record, Basic, 10f, out var repaired, out var reason);

            TestAssert.False(clean);
            TestAssert.Equal("tank-invalid", reason);
            TestAssert.Equal(0f, repaired.TankWater);
        }

        public static void TestSanitizeClampsTankToTierCapacity()
        {
            var record = HdbfSaveCodec.CreateDefault(1, HdbfTier.Top, Top, 10f);
            record.TankWater = 1000f;

            var clean = HdbfSaveCodec.TrySanitize(record, Top, 10f, out var repaired, out var reason);

            TestAssert.False(clean);
            TestAssert.Equal("tank-over-capacity", reason);
            TestAssert.Equal(76.8f, repaired.TankWater, 1e-4f);
        }

        public static void TestSanitizeRemovesExcludedIdsThatAreRegistered()
        {
            var record = BuildValidRecord();
            record.ExcludedPlotIds = new[] { 1, 100 };

            var clean = HdbfSaveCodec.TrySanitize(record, Basic, 10f, out var repaired, out var reason);

            TestAssert.False(clean);
            TestAssert.Equal("excluded-plot-invalid", reason);
            TestAssert.Equal(1, repaired.ExcludedPlotIds.Length);
            TestAssert.Equal(100, repaired.ExcludedPlotIds[0]);
        }

        public static void TestSanitizeRepairsUnknownMode()
        {
            var record = BuildValidRecord();
            record.Mode = (HdbfMode)99;

            var clean = HdbfSaveCodec.TrySanitize(record, Basic, 10f, out var repaired, out var reason);

            TestAssert.False(clean);
            TestAssert.Equal("mode-invalid", reason);
            TestAssert.Equal(HdbfMode.Normal, repaired.Mode);
        }

        public static void TestSanitizeRepairsSchemaOutOfRange()
        {
            var record = BuildValidRecord();
            record.SchemaVersion = 99;

            var clean = HdbfSaveCodec.TrySanitize(record, Basic, 10f, out var repaired, out var reason);

            TestAssert.False(clean);
            TestAssert.Equal("schema-out-of-range", reason);
            TestAssert.Equal(HdbfBuildingRecord.CurrentSchemaVersion, repaired.SchemaVersion);
        }

        public static void TestSanitizeNullRecordRebuildsDefaults()
        {
            var clean = HdbfSaveCodec.TrySanitize(null, Basic, 10f, out var repaired, out var reason);

            TestAssert.False(clean);
            TestAssert.Equal("record-missing", reason);
            TestAssert.NotNull(repaired);
            TestAssert.Equal(HdbfBuildingRecord.CurrentSchemaVersion, repaired.SchemaVersion);
            TestAssert.Equal(0, repaired.PlotIds.Length);
        }

        public static void TestSanitizeTruncatesPlotsToTierCapacity()
        {
            var record = BuildValidRecord();
            var ids = new int[61];
            var crops = new string[61];
            var fatigue = new float[61];
            for (var i = 0; i < ids.Length; i++)
            {
                ids[i] = i;
                crops[i] = string.Empty;
            }

            record.PlotIds = ids;
            record.CropIds = crops;
            record.Fatigue = fatigue;

            var clean = HdbfSaveCodec.TrySanitize(record, Basic, 10f, out var repaired, out var reason);

            TestAssert.False(clean);
            TestAssert.Equal(60, repaired.PlotIds.Length);
            TestAssert.Equal(60, repaired.CropIds.Length);
            TestAssert.Equal(60, repaired.Fatigue.Length);
            TestAssert.True(reason is not null && reason!.Contains("plot-count-over-capacity"), "reason was " + reason);
        }

        public static void TestSanitizeUsesResolvedProfileCapacity()
        {
            var configurable = Basic with { PlotCapacity = 70 };
            var record = BuildValidRecord();
            var ids = new int[65];
            var crops = new string[65];
            var fatigue = new float[65];
            for (var i = 0; i < ids.Length; i++)
            {
                ids[i] = i;
            }

            record.PlotIds = ids;
            record.CropIds = crops;
            record.Fatigue = fatigue;
            record.ExcludedPlotIds = Array.Empty<int>();

            var clean = HdbfSaveCodec.TrySanitize(record, configurable, 10f, out var repaired, out _);
            TestAssert.True(clean, "65 plots fit a config-raised capacity of 70");
            TestAssert.Equal(65, repaired.PlotIds.Length);
        }

        public static void TestSanitizeIsIdempotent()
        {
            var record = BuildValidRecord();
            record.Fatigue = new[] { 5f, float.NaN };
            record.PlotIds = new[] { 5, 6, 5 };

            HdbfSaveCodec.TrySanitize(record, Basic, 10f, out var once, out _);
            var onceCanonical = HdbfSaveCodec.Canonicalize(once);

            var clean = HdbfSaveCodec.TrySanitize(once, Basic, 10f, out var twice, out var reason);

            TestAssert.True(clean, "a repaired record must sanitise cleanly again; reason=" + reason);
            TestAssert.Equal(onceCanonical, HdbfSaveCodec.Canonicalize(twice));
        }

        // ---------------------------------------------------------------- migration

        public static void TestMigrateUpgradesVersionZeroAndFillsDueDay()
        {
            var record = HdbfSaveCodec.CreateDefault(9, HdbfTier.Intermediate, Intermediate, 5f);
            record.SchemaVersion = 0;
            record.DueDay = 0f;

            var migrated = HdbfSaveCodec.Migrate(record, Intermediate, 5f);

            TestAssert.Equal(HdbfBuildingRecord.CurrentSchemaVersion, migrated.SchemaVersion);
            TestAssert.Equal(15f, migrated.DueDay, 1e-6f);
            TestAssert.Equal(9L, migrated.EntityId);
        }

        public static void TestMigrateIsIdempotent()
        {
            var record = BuildValidRecord();
            record.SchemaVersion = 0;
            record.DueDay = 0f;

            var once = HdbfSaveCodec.Migrate(record, Intermediate, 5f);
            var twice = HdbfSaveCodec.Migrate(once, Intermediate, 5f);

            TestAssert.Equal(HdbfSaveCodec.Canonicalize(once), HdbfSaveCodec.Canonicalize(twice));
        }

        public static void TestMigrateCurrentVersionReturnsEqualCopy()
        {
            var record = BuildValidRecord();
            var migrated = HdbfSaveCodec.Migrate(record, Basic, 10f);

            TestAssert.False(ReferenceEquals(record, migrated), "migration must not return the input instance");
            TestAssert.Equal(HdbfSaveCodec.Canonicalize(record), HdbfSaveCodec.Canonicalize(migrated));
        }

        public static void TestMigrateKeepsNewerSchemaStamp()
        {
            var record = BuildValidRecord();
            record.SchemaVersion = HdbfBuildingRecord.CurrentSchemaVersion + 1;

            var migrated = HdbfSaveCodec.Migrate(record, Basic, 10f);
            TestAssert.Equal(HdbfBuildingRecord.CurrentSchemaVersion + 1, migrated.SchemaVersion);
        }

        // ---------------------------------------------------------------- canonical form

        public static void TestCanonicalizeExactFormat()
        {
            var record = new HdbfBuildingRecord
            {
                SchemaVersion = 1,
                EntityId = 7,
                Tier = HdbfTier.Advanced,
                Mode = HdbfMode.Dense,
                PlotIds = new[] { 3, 1 },
                CropIds = new[] { "Carrot", "Potato" },
                Fatigue = new[] { 0.5f, 0.125f },
                ExcludedPlotIds = new[] { 9 },
                DueDay = 12.5f,
                CyclesMissed = 1,
                TankWater = 2.25f,
                PortPower = true,
                PortPipe = false,
                PortLogistics = true,
                Adapters = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["pipes"] = "active",
                    ["belts"] = "failed",
                },
            };

            const string expected =
                "schema=1;entity=7;tier=2;mode=1;ids=3,1;crops=Carrot,Potato;fatigue=0.500,0.125;"
                + "excluded=9;due=12.500;missed=1;tank=2.250;ports=101;adapters=belts=failed,pipes=active";

            TestAssert.Equal(expected, HdbfSaveCodec.Canonicalize(record));
        }

        public static void TestCanonicalizeIsStableAcrossAdapterInsertionOrder()
        {
            var left = BuildValidRecord();
            var right = BuildValidRecord();
            left.Adapters = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["pipes"] = "active",
                ["belts"] = "failed",
            };
            right.Adapters = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["belts"] = "failed",
                ["pipes"] = "active",
            };

            TestAssert.Equal(HdbfSaveCodec.Canonicalize(left), HdbfSaveCodec.Canonicalize(right));
        }

        public static void TestCanonicalizeRoundsFloatsToThreeDecimals()
        {
            var record = BuildValidRecord();
            record.Fatigue = new[] { 0.12345f, 0.125f };
            record.DueDay = 3.14159f;

            var canonical = HdbfSaveCodec.Canonicalize(record);
            TestAssert.True(canonical.Contains("0.123,0.125"), "fatigue formatting: " + canonical);
            TestAssert.True(canonical.Contains("due=3.142"), "due-day formatting: " + canonical);
        }

        // ---------------------------------------------------------------- copy + round trip

        public static void TestCloneIsDeepCopy()
        {
            var record = BuildValidRecord();
            var clone = HdbfSaveCodec.Clone(record);

            clone.PlotIds[0] = 99;
            clone.Fatigue[0] = 0.9f;
            clone.Adapters["pipes"] = "failed";

            TestAssert.Equal(1, record.PlotIds[0]);
            TestAssert.Equal(0.1f, record.Fatigue[0], 1e-6f);
            TestAssert.Equal("active", record.Adapters["pipes"]);
        }

        public static void TestRecordRoundTripIsStable()
        {
            // TC-10/TC-11 class: sanitise -> migrate -> canonicalise three times, same text.
            var record = BuildValidRecord();
            record.PlotIds = new[] { 4, 8 };
            record.CropIds = new[] { "Carrot", "Potato" };
            record.Fatigue = new[] { 0.6f, 0.2f };
            record.ExcludedPlotIds = new[] { 42 };
            record.Adapters = new Dictionary<string, string>(StringComparer.Ordinal) { ["pipes"] = "active" };

            var previous = string.Empty;
            var current = record;
            for (var pass = 0; pass < 3; pass++)
            {
                HdbfSaveCodec.TrySanitize(current, Basic, 10f, out var repaired, out _);
                current = HdbfSaveCodec.Migrate(repaired, Basic, 10f);
                var canonical = HdbfSaveCodec.Canonicalize(current);
                if (pass > 0)
                {
                    TestAssert.Equal(previous, canonical, "round-trip pass " + pass + " changed the record");
                }

                previous = canonical;
            }
        }

        public static void TestNoCropsReturnsFreshEmptyArray()
        {
            var first = HdbfSaveCodec.NoCrops();
            TestAssert.Equal(0, first.Length);
            first = new[] { "mutated" };
            TestAssert.Equal(0, HdbfSaveCodec.NoCrops().Length);
        }

        // ------------------------------------------------- defensive regressions (healthy save must survive)

        public static void TestSanitize_Healthy60PlotRecordKeepsAllPlots()
        {
            var profile = TierCatalog.Get(HdbfTier.Basic);
            var ids = new int[60];
            var crops = new string[60];
            var fatigue = new float[60];
            for (var i = 0; i < 60; i++)
            {
                ids[i] = 1000 + i;
                crops[i] = i % 2 == 0 ? "Carrot" : "Potato";
                fatigue[i] = i / 100f;
            }

            var record = new HdbfBuildingRecord
            {
                SchemaVersion = 1,
                EntityId = 55,
                Tier = HdbfTier.Basic,
                Mode = HdbfMode.Normal,
                PlotIds = ids,
                CropIds = crops,
                Fatigue = fatigue,
                ExcludedPlotIds = new[] { 2000 },
                DueDay = 10f,
                TankWater = 3f,
                Adapters = new Dictionary<string, string>(StringComparer.Ordinal) { ["pipes"] = "active" },
            };

            var clean = HdbfSaveCodec.TrySanitize(record, profile, 10f, out var repaired, out var reason);

            TestAssert.True(clean, "a healthy 60-plot record must not be degraded; reason=" + reason);
            TestAssert.Equal(60, repaired.PlotIds.Length);
            TestAssert.Equal(60, repaired.CropIds.Length);
            TestAssert.Equal(60, repaired.Fatigue.Length);
            for (var i = 0; i < 60; i++)
            {
                TestAssert.Equal(ids[i], repaired.PlotIds[i], "plot id " + i);
                TestAssert.Equal(crops[i], repaired.CropIds[i], "crop " + i);
                TestAssert.Equal(fatigue[i], repaired.Fatigue[i], 1e-6f, "fatigue " + i);
            }

            TestAssert.Equal(2000, repaired.ExcludedPlotIds[0]);
            TestAssert.Equal("active", repaired.Adapters["pipes"]);
            TestAssert.Equal(HdbfSaveCodec.Canonicalize(record), HdbfSaveCodec.Canonicalize(repaired),
                "a healthy record must canonicalise identically before and after sanitising");
        }

        public static void TestSanitizeOfCreateDefaultIsStable()
        {
            foreach (var profile in TierCatalog.All)
            {
                var record = HdbfSaveCodec.CreateDefault(7, profile.Tier, profile, 4f);
                var clean = HdbfSaveCodec.TrySanitize(record, profile, 4f, out var repaired, out var reason);

                TestAssert.True(clean, "default record for " + profile.Tier + " must be clean; reason=" + reason);
                TestAssert.Equal(HdbfSaveCodec.Canonicalize(record), HdbfSaveCodec.Canonicalize(repaired),
                    "sanitise(createDefault) must be stable for " + profile.Tier);

                HdbfSaveCodec.TrySanitize(repaired, profile, 4f, out var twice, out _);
                TestAssert.Equal(HdbfSaveCodec.Canonicalize(repaired), HdbfSaveCodec.Canonicalize(twice));
            }
        }

        public static void TestSanitizingACloneMatchesCanonicalOfOriginal()
        {
            var record = BuildValidRecord();
            record.PlotIds = new[] { 11, 12, 13 };
            record.CropIds = new[] { "Carrot", "Potato", "Wheat" };
            record.Fatigue = new[] { 0.2f, 0.4f, 0.6f };
            record.ExcludedPlotIds = new[] { 77 };

            var clone = HdbfSaveCodec.Clone(record);
            HdbfSaveCodec.TrySanitize(clone, Basic, 10f, out var repaired, out _);

            TestAssert.Equal(HdbfSaveCodec.Canonicalize(record), HdbfSaveCodec.Canonicalize(repaired));

            // And the untouched original is unchanged by having been cloned and sanitised.
            TestAssert.Equal(11, record.PlotIds[0]);
            TestAssert.Equal("Wheat", record.CropIds[2]);
        }

        private static HdbfBuildingRecord BuildValidRecord()
        {
            return new HdbfBuildingRecord
            {
                SchemaVersion = 1,
                EntityId = 1,
                Tier = HdbfTier.Basic,
                Mode = HdbfMode.Normal,
                PlotIds = new[] { 1, 2 },
                CropIds = new[] { "Carrot", "Potato" },
                Fatigue = new[] { 0.1f, 0.2f },
                ExcludedPlotIds = new[] { 9 },
                DueDay = 10f,
                CyclesMissed = 0,
                TankWater = 1f,
                Adapters = new Dictionary<string, string>(StringComparer.Ordinal) { ["pipes"] = "active" },
            };
        }
    }
}
