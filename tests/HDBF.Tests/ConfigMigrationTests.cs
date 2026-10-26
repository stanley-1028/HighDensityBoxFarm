using System;
using System.Collections.Generic;
using HDBF.Core.Config;

namespace HDBF.Tests
{
    /// <summary>
    /// L1 tests for the config schema 1 → 2 migration and the unknown-key contract
    /// (REQ-V2-EXT-04 / REQ-V2-SAFE-06): a v1 file keeps every recognizable value, the deleted
    /// v1 keys are dropped with one <c>config.migrated</c> line, a truly unknown key is ignored
    /// and reported as <c>config.ignored-key</c>, and nothing ever throws.
    /// </summary>
    public static class ConfigMigrationTests
    {
        private const string V1File = @"{
  ""configSchemaVersion"": 1,
  ""features"": {
    ""densePlanting"": false,
    ""rotation"": true,
    ""maintenance"": true,
    ""powerConnection"": false,
    ""pipeConnection"": true,
    ""logisticsOutput"": false,
    ""soilFatigue"": true,
    ""growthMultiplierPatch"": true
  },
  ""perf"": {
    ""scanBudgetPerFrame"": 128,
    ""scanIntervalSeconds"": 2.0,
    ""irrigationSampleInterval"": 0.5,
    ""uiRefreshHz"": 6,
    ""reducedBuildingThreshold"": 80,
    ""steadyFrameBudgetMs"": 0.5,
    ""warnFrameMs"": 2.0,
    ""pauseFrameMs"": 5.0,
    ""cropVisibilityIntervalSeconds"": 2.0,
    ""cropVisibilityCellsPerFrame"": 64
  },
  ""ui"": {
    ""worldIcons"": true,
    ""worldIconBlink"": false,
    ""worldIconMaxDistance"": 30,
    ""worldIconIntervalSeconds"": 0.5,
    ""worldIconFarmsPerTick"": 12,
    ""warningAggregateTtlSeconds"": 3.0,
    ""hideManagedCrops"": true
  },
  ""balance"": {
    ""tier"": [
      { ""plotCapacity"": 120, ""maxWorkers"": 3, ""workRange"": 13, ""waterPerPlotPerDay"": 0.03 },
      {},
      {},
      {}
    ],
    ""denseYieldMultiplier"": 1.4
  }
}";

        public static void TestMigratesSchemaOneToSchemaTwoAndKeepsRecognizedValues()
        {
            var warnings = new List<string>();
            var config = HdbfConfigCodec.Parse(V1File, warnings.Add);

            TestAssert.Equal(HdbfConfig.CurrentSchemaVersion, config.ConfigSchemaVersion,
                "the parsed config is always schema 2");
            TestAssert.False(config.Features.DensePlanting, "recognized feature flags survive the migration");
            TestAssert.False(config.Features.PowerConnection);
            TestAssert.False(config.Ui.WorldIconBlink);
            TestAssert.Equal(30, config.Ui.WorldIconMaxDistance);
            TestAssert.Equal(12, config.Ui.WorldIconFarmsPerTick);
            TestAssert.Equal(6, config.Perf.UiRefreshHz);
            TestAssert.Equal(80, config.Perf.ReducedBuildingThreshold);
            TestAssert.Equal(1.4f, config.DenseYieldMultiplier, 1e-6f);

            TestAssert.NotNull(config.TierOverrides[0]);
            TestAssert.Equal(120, config.TierOverrides[0]!.SlotCapacity, "plotCapacity is migrated to slotCapacity");
            TestAssert.Equal(3, config.TierOverrides[0]!.MaxWorkers);
            TestAssert.Equal(0.03f, config.TierOverrides[0]!.WaterPerSlotPerDay!.Value, 1e-6f,
                "waterPerPlotPerDay is migrated to waterPerSlotPerDay");
            TestAssert.Null(config.TierOverrides[1]);
        }

        public static void TestMigrationIsReportedExactlyOnceWithTheDroppedKeys()
        {
            var warnings = new List<string>();
            HdbfConfigCodec.Parse(V1File, warnings.Add);

            var migrated = warnings.FindAll(message => message.StartsWith("config.migrated:", StringComparison.Ordinal));
            TestAssert.Equal(1, migrated.Count, "one config.migrated line: " + string.Join(" | ", warnings));
            TestAssert.True(migrated[0].Contains("schema 1 -> 2", StringComparison.Ordinal), migrated[0]);
            foreach (var dropped in new[]
                     {
                         "features.growthMultiplierPatch", "perf.scanBudgetPerFrame",
                         "perf.cropVisibilityCellsPerFrame", "ui.hideManagedCrops",
                         "ui.warningAggregateTtlSeconds", "balance.tier[0].workRange",
                     })
            {
                TestAssert.True(migrated[0].Contains(dropped, StringComparison.Ordinal),
                    "migration must list the dropped key " + dropped + ": " + migrated[0]);
            }

            TestAssert.True(migrated[0].Contains("plotCapacity", StringComparison.Ordinal),
                "migration must list the renamed tier key: " + migrated[0]);
            TestAssert.False(warnings.Exists(message => message.Contains("config.ignored-key")),
                "v1 keys are recognized, not unknown: " + string.Join(" | ", warnings));
        }

        public static void TestMigratedConfigSerializesWithoutV1KeysAndReparsesClean()
        {
            var migrated = HdbfConfigCodec.Parse(V1File);
            var json = HdbfConfigCodec.Serialize(migrated);

            foreach (var deleted in new[]
                     {
                         "growthMultiplierPatch", "scanBudgetPerFrame", "scanIntervalSeconds",
                         "irrigationSampleInterval", "cropVisibility", "hideManagedCrops",
                         "warningAggregateTtlSeconds", "plotCapacity", "waterPerPlotPerDay", "workRange",
                     })
            {
                TestAssert.False(json.Contains(deleted, StringComparison.Ordinal),
                    "a migrated file must not write the v1 key " + deleted);
            }

            var warnings = new List<string>();
            var reparsed = HdbfConfigCodec.Parse(json, warnings.Add);
            TestAssert.Equal(0, warnings.Count, "round trip warnings: " + string.Join(" | ", warnings));
            TestAssert.Equal(120, reparsed.TierOverrides[0]!.SlotCapacity);
            TestAssert.Equal(HdbfConfig.CurrentSchemaVersion, reparsed.ConfigSchemaVersion);
        }

        public static void TestSchemaTwoFileDoesNotLogMigration()
        {
            var warnings = new List<string>();
            HdbfConfigCodec.Parse("{\"configSchemaVersion\":2,\"ui\":{\"worldIconMaxDistance\":50}}",
                warnings.Add);
            TestAssert.Equal(0, warnings.Count,
                "a schema-2 file is not migrated: " + string.Join(" | ", warnings));
        }

        public static void TestUnknownKeysAreIgnoredAndReportedWithTheirPath()
        {
            var warnings = new List<string>();
            var config = HdbfConfigCodec.Parse(
                "{\"configSchemaVersion\":2,\"features\":{\"densePlanting\":false,\"futureFlag\":true},"
                + "\"perf\":{\"mysteryKnob\":7},\"ui\":{\"unknownUi\":1},"
                + "\"balance\":{\"unknownBalance\":1,\"tier\":[{\"unknownTierKey\":2}]},"
                + "\"unknownSection\":{}}",
                warnings.Add);

            TestAssert.False(config.Features.DensePlanting, "known keys still apply");
            var ignored = warnings.FindAll(message => message.StartsWith("config.ignored-key:", StringComparison.Ordinal));
            TestAssert.Equal(1, ignored.Count, string.Join(" | ", warnings));
            foreach (var path in new[]
                     {
                         "futureFlag", "perf.mysteryKnob", "ui.unknownUi", "balance.unknownBalance",
                         "balance.tier[0].unknownTierKey", "unknownSection",
                     })
            {
                TestAssert.True(ignored[0].Contains(path, StringComparison.Ordinal),
                    "ignored-key line must name " + path + ": " + ignored[0]);
            }
        }

        public static void TestUnknownKeysNeverChangeParsedValues()
        {
            var withUnknown = HdbfConfigCodec.Parse(
                "{\"configSchemaVersion\":2,\"features\":{\"densePlanting\":false},\"future\":{\"x\":[1,2]}}");
            var plain = HdbfConfigCodec.Parse(
                "{\"configSchemaVersion\":2,\"features\":{\"densePlanting\":false}}");

            TestAssert.Equal(plain.Features.DensePlanting, withUnknown.Features.DensePlanting);
            TestAssert.Equal(plain.DenseYieldMultiplier, withUnknown.DenseYieldMultiplier);
            TestAssert.Equal(plain.Ui.WorldIconMaxDistance, withUnknown.Ui.WorldIconMaxDistance);
        }

        public static void TestInvalidOrFutureSchemaVersionFallsBackSafely()
        {
            var warnings = new List<string>();
            var future = HdbfConfigCodec.Parse(
                "{\"configSchemaVersion\":99,\"features\":{\"densePlanting\":false}}", warnings.Add);
            TestAssert.Equal(HdbfConfig.CurrentSchemaVersion, future.ConfigSchemaVersion,
                "a future file is read with the current vocabulary, never rejected");
            TestAssert.False(future.Features.DensePlanting);
            TestAssert.True(warnings.Exists(message => message.Contains("config.invalid")),
                string.Join(" | ", warnings));

            var garbage = new List<string>();
            var bad = HdbfConfigCodec.Parse("{\"configSchemaVersion\":\"two\"}", garbage.Add);
            TestAssert.Equal(HdbfConfig.CurrentSchemaVersion, bad.ConfigSchemaVersion);
            TestAssert.True(garbage.Exists(message => message.Contains("config.invalid")),
                string.Join(" | ", garbage));
        }

        public static void TestMissingSchemaVersionKeepsDefaultsWithoutWarnings()
        {
            var warnings = new List<string>();
            var config = HdbfConfigCodec.Parse("{\"features\":{\"densePlanting\":false}}", warnings.Add);

            TestAssert.Equal(HdbfConfig.CurrentSchemaVersion, config.ConfigSchemaVersion);
            TestAssert.False(config.Features.DensePlanting);
            TestAssert.Equal(0, warnings.Count, string.Join(" | ", warnings));
        }
    }
}
