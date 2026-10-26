using System;
using System.Collections.Generic;
using HDBF.Core;
using HDBF.Core.Balance;
using HDBF.Core.Config;

namespace HDBF.Tests
{
    /// <summary>
    /// L1 tests for the configuration codec and tier resolution (chapter 10.3, chapter 18,
    /// REQ-SAFE-10/13, REQ-BAL-18). The parser must be total: no input may throw, and every
    /// clamp must be reported.
    /// </summary>
    public static class ConfigCodecTests
    {
        // ---------------------------------------------------------------- defaults

        public static void TestDefaultConfigValues()
        {
            var config = HdbfConfig.Default;

            TestAssert.Equal(HdbfConfig.CurrentSchemaVersion, config.ConfigSchemaVersion);
            TestAssert.Equal(4, config.TierOverrides.Count);
            for (var i = 0; i < 4; i++)
            {
                TestAssert.Null(config.TierOverrides[i], "default tier override " + i + " must be null");
            }
        }

        public static void TestDefaultFeatureFlags()
        {
            var features = HdbfConfig.Default.Features;

            TestAssert.True(features.DensePlanting);
            TestAssert.True(features.Rotation);
            TestAssert.True(features.Maintenance);
            TestAssert.True(features.PowerConnection);
            TestAssert.True(features.PipeConnection);
            TestAssert.False(features.LogisticsOutput, "logisticsOutput defaults to false (table 10-3)");
            TestAssert.True(features.SoilFatigue);
        }

        public static void TestDefaultPerfSettings()
        {
            var perf = HdbfConfig.Default.Perf;

            TestAssert.Equal(128, perf.ScanBudgetPerFrame);
            TestAssert.Equal(2f, perf.ScanIntervalSeconds);
            TestAssert.Equal(0.5f, perf.IrrigationSampleInterval);
            TestAssert.Equal(4, perf.UiRefreshHz);
            TestAssert.Equal(100, perf.ReducedBuildingThreshold);
            TestAssert.Equal(0.5f, perf.SteadyFrameBudgetMs);
            TestAssert.Equal(2f, perf.WarnFrameMs);
            TestAssert.Equal(5f, perf.PauseFrameMs);
        }

        public static void TestDefaultBalanceMultipliers()
        {
            var config = HdbfConfig.Default;

            TestAssert.Equal(1.35f, config.DenseYieldMultiplier);
            TestAssert.Equal(1.50f, config.DenseWaterMultiplier);
            TestAssert.Equal(1.50f, config.DenseFatigueMultiplier);
            TestAssert.Equal(1.20f, config.DenseWorkMultiplier);
            TestAssert.Equal(0.03f, config.FallowRecoveryPerDay, 1e-6f);
            TestAssert.Equal(0.60f, config.FatiguePenalty, 1e-6f);
            TestAssert.Equal(0.40f, config.RotationFactor, 1e-6f);
            TestAssert.Equal(0.05f, config.RotationRecovery, 1e-6f);
        }

        // ---------------------------------------------------------------- total parsing

        public static void TestParseNullOrEmptyReturnsDefault()
        {
            var warnings = new List<string>();
            var fromNull = HdbfConfigCodec.Parse(null!, warnings.Add);
            var fromEmpty = HdbfConfigCodec.Parse("   ", warnings.Add);

            TestAssert.True(warnings.Count >= 2, "a missing config must be logged (REQ-SAFE-13)");
            TestAssert.Equal(HdbfConfig.Default.DenseYieldMultiplier, fromNull.DenseYieldMultiplier);
            TestAssert.Equal(HdbfConfig.Default.DenseYieldMultiplier, fromEmpty.DenseYieldMultiplier);
            TestAssert.True(warnings.TrueForAll(message => message.Contains("config.missing")),
                "warnings should identify the missing config: " + string.Join(" | ", warnings));
        }

        public static void TestParseGarbageJsonNeverThrows()
        {
            var inputs = new[]
            {
                "{{{", "{", "}", "not json", "{\"a\":}", "{\"a\": [1,]}", "\"unterminated",
                "{\"balance\": {\"denseYieldMultiplier\": 1.35} trailing}", new string('[', 200),
            };

            foreach (var input in inputs)
            {
                var warnings = new List<string>();
                var config = HdbfConfigCodec.Parse(input, warnings.Add);
                TestAssert.NotNull(config, "parse returned null for: " + input);
                TestAssert.Equal(1.35f, config.DenseYieldMultiplier);
                TestAssert.True(warnings.Count > 0, "invalid JSON must warn: " + input);
            }
        }

        public static void TestParseNonObjectRootUsesDefaults()
        {
            foreach (var input in new[] { "[]", "42", "null", "\"text\"", "true" })
            {
                var warnings = new List<string>();
                var config = HdbfConfigCodec.Parse(input, warnings.Add);

                TestAssert.Equal(128, config.Perf.ScanBudgetPerFrame, "input " + input);
                TestAssert.True(warnings.Count > 0, "root type must warn for " + input);
            }
        }

        public static void TestParseEmptyObjectKeepsDefaultsWithoutWarnings()
        {
            var warnings = new List<string>();
            var config = HdbfConfigCodec.Parse("{}", warnings.Add);

            TestAssert.Equal(0, warnings.Count, "missing keys silently fall back to defaults");
            TestAssert.Equal(128, config.Perf.ScanBudgetPerFrame);
            TestAssert.True(config.Features.DensePlanting);
            TestAssert.False(config.Features.LogisticsOutput);
            TestAssert.Equal(1.35f, config.DenseYieldMultiplier);
        }

        public static void TestParseReadsFeatureFlags()
        {
            var config = HdbfConfigCodec.Parse(
                "{\"features\":{\"rotation\":false,\"logisticsOutput\":true}}");

            TestAssert.False(config.Features.Rotation);
            TestAssert.True(config.Features.LogisticsOutput);
            TestAssert.True(config.Features.DensePlanting, "unspecified flags keep their defaults");
        }

        public static void TestParseLogisticsOutputDefaultsFalse()
        {
            var config = HdbfConfigCodec.Parse("{\"features\":{\"densePlanting\":true}}");
            TestAssert.False(config.Features.LogisticsOutput);
        }

        public static void TestParseWrongTypeUsesDefaultAndWarns()
        {
            var warnings = new List<string>();
            var config = HdbfConfigCodec.Parse(
                "{\"features\":{\"densePlanting\":\"yes\"},\"perf\":{\"scanBudgetPerFrame\":\"128\"}}",
                warnings.Add);

            TestAssert.True(config.Features.DensePlanting);
            TestAssert.Equal(128, config.Perf.ScanBudgetPerFrame);
            TestAssert.Equal(2, warnings.Count);
            TestAssert.True(warnings.TrueForAll(message => message.Contains("config.invalid")),
                "type errors must be reported: " + string.Join(" | ", warnings));
        }

        public static void TestParseUnknownKeysAreIgnored()
        {
            var warnings = new List<string>();
            var config = HdbfConfigCodec.Parse(
                "{\"unknown\":{\"x\":1},\"features\":{\"densePlanting\":false}}", warnings.Add);

            TestAssert.False(config.Features.DensePlanting);
            TestAssert.Equal(0, warnings.Count);
        }

        // ---------------------------------------------------------------- clamping (chapter 18)

        public static void TestParseClampsScanBudgetToChapterEighteenRange()
        {
            var low = ParseWithWarnings("{\"perf\":{\"scanBudgetPerFrame\":10}}");
            TestAssert.Equal(32, low.Config.Perf.ScanBudgetPerFrame);
            AssertClampWarning(low.Warnings, "perf.scanBudgetPerFrame");

            var high = ParseWithWarnings("{\"perf\":{\"scanBudgetPerFrame\":100000}}");
            TestAssert.Equal(512, high.Config.Perf.ScanBudgetPerFrame);
            AssertClampWarning(high.Warnings, "perf.scanBudgetPerFrame");
        }

        public static void TestParseClampsTierWorkerCap()
        {
            var low = ParseWithWarnings("{\"balance\":{\"tier\":[{\"maxWorkers\":0}]}}");
            TestAssert.Equal(1, low.Config.TierOverrides[0]!.MaxWorkers);
            AssertClampWarning(low.Warnings, "maxWorkers");

            var high = ParseWithWarnings("{\"balance\":{\"tier\":[{\"maxWorkers\":99}]}}");
            TestAssert.Equal(40, high.Config.TierOverrides[0]!.MaxWorkers);
        }

        public static void TestParseClampsWorkRangeToHardCap()
        {
            // REQ-BAL-18: a config may never make the range exceed 27x27.
            var high = ParseWithWarnings("{\"balance\":{\"tier\":[{\"workRange\":100}]}}");
            TestAssert.Equal(27, high.Config.TierOverrides[0]!.WorkRange);

            var low = ParseWithWarnings("{\"balance\":{\"tier\":[{\"workRange\":1}]}}");
            TestAssert.Equal(7, low.Config.TierOverrides[0]!.WorkRange);
        }

        public static void TestParseClampsPlotCapacity()
        {
            var low = ParseWithWarnings("{\"balance\":{\"tier\":[{\"plotCapacity\":5}]}}");
            TestAssert.Equal(30, low.Config.TierOverrides[0]!.PlotCapacity);

            var high = ParseWithWarnings("{\"balance\":{\"tier\":[{\"plotCapacity\":5000}]}}");
            TestAssert.Equal(1000, high.Config.TierOverrides[0]!.PlotCapacity);
        }

        public static void TestParseClampsIrrigationCoverage()
        {
            var low = ParseWithWarnings("{\"balance\":{\"tier\":[{\"minIrrigationCoverage\":0.1}]}}");
            TestAssert.Equal(0.30f, low.Config.TierOverrides[0]!.MinIrrigationCoverage!.Value, 1e-5f);

            var high = ParseWithWarnings("{\"balance\":{\"tier\":[{\"minIrrigationCoverage\":0.99}]}}");
            TestAssert.Equal(0.95f, high.Config.TierOverrides[0]!.MinIrrigationCoverage!.Value, 1e-5f);
        }

        public static void TestParseClampsWaterAndTankDays()
        {
            var low = ParseWithWarnings(
                "{\"balance\":{\"tier\":[{\"waterPerPlotPerDay\":0,\"tankCapacityDays\":0}]}}");
            TestAssert.Equal(0.005f, low.Config.TierOverrides[0]!.WaterPerPlotPerDay!.Value, 1e-6f);
            TestAssert.Equal(1, low.Config.TierOverrides[0]!.TankCapacityDays);

            var high = ParseWithWarnings(
                "{\"balance\":{\"tier\":[{\"waterPerPlotPerDay\":1,\"tankCapacityDays\":50}]}}");
            TestAssert.Equal(0.08f, high.Config.TierOverrides[0]!.WaterPerPlotPerDay!.Value, 1e-6f);
            TestAssert.Equal(10, high.Config.TierOverrides[0]!.TankCapacityDays);
        }

        public static void TestParseClampsFatigueFactorAndScienceCost()
        {
            var low = ParseWithWarnings(
                "{\"balance\":{\"tier\":[{\"soilFatigueFactor\":0.1,\"scienceCost\":1}]}}");
            TestAssert.Equal(0.5f, low.Config.TierOverrides[0]!.SoilFatigueFactor!.Value, 1e-5f);
            TestAssert.Equal(100, low.Config.TierOverrides[0]!.ScienceCost);

            var high = ParseWithWarnings(
                "{\"balance\":{\"tier\":[{\"soilFatigueFactor\":5,\"scienceCost\":99999}]}}");
            TestAssert.Equal(2f, high.Config.TierOverrides[0]!.SoilFatigueFactor!.Value, 1e-5f);
            TestAssert.Equal(5000, high.Config.TierOverrides[0]!.ScienceCost);
        }

        public static void TestParseClampsDenseModeMultipliers()
        {
            var config = ParseWithWarnings(
                    "{\"balance\":{\"denseYieldMultiplier\":0.5,\"denseWaterMultiplier\":5,"
                    + "\"denseFatigueMultiplier\":0,\"denseWorkMultiplier\":0}}")
                .Config;

            TestAssert.Equal(1.10f, config.DenseYieldMultiplier, 1e-5f);
            TestAssert.Equal(2.00f, config.DenseWaterMultiplier, 1e-5f);
            TestAssert.Equal(1.00f, config.DenseFatigueMultiplier, 1e-5f);
            TestAssert.Equal(1.00f, config.DenseWorkMultiplier, 1e-5f);
        }

        public static void TestParseClampsFatigueAndRotationParameters()
        {
            var config = ParseWithWarnings(
                    "{\"balance\":{\"fallowRecoveryPerDay\":1,\"fatiguePenalty\":0,"
                    + "\"rotationFactor\":0,\"rotationRecovery\":1}}")
                .Config;

            TestAssert.Equal(0.10f, config.FallowRecoveryPerDay, 1e-5f);
            TestAssert.Equal(0.20f, config.FatiguePenalty, 1e-5f);
            TestAssert.Equal(0.10f, config.RotationFactor, 1e-5f);
            TestAssert.Equal(0.20f, config.RotationRecovery, 1e-5f);
        }

        public static void TestParseClampsRemainingPerfSettings()
        {
            var config = ParseWithWarnings(
                    "{\"perf\":{\"scanIntervalSeconds\":0.2,\"irrigationSampleInterval\":9,"
                    + "\"uiRefreshHz\":0,\"reducedBuildingThreshold\":5,\"steadyFrameBudgetMs\":0,"
                    + "\"warnFrameMs\":10,\"pauseFrameMs\":1}}")
                .Config.Perf;

            TestAssert.Equal(1f, config.ScanIntervalSeconds, 1e-5f);
            TestAssert.Equal(2f, config.IrrigationSampleInterval, 1e-5f);
            TestAssert.Equal(1, config.UiRefreshHz);
            TestAssert.Equal(20, config.ReducedBuildingThreshold);
            TestAssert.Equal(0.2f, config.SteadyFrameBudgetMs, 1e-5f);
            TestAssert.Equal(5f, config.WarnFrameMs, 1e-5f);
            TestAssert.Equal(3f, config.PauseFrameMs, 1e-5f);
        }

        public static void TestParseClampsConfigSchemaVersion()
        {
            var parsed = ParseWithWarnings("{\"configSchemaVersion\":2}");
            TestAssert.Equal(HdbfConfig.CurrentSchemaVersion, parsed.Config.ConfigSchemaVersion);
            AssertClampWarning(parsed.Warnings, "configSchemaVersion");
        }

        public static void TestParseClampsEachValueAndReportsEveryClamp()
        {
            var warnings = new List<string>();
            var config = HdbfConfigCodec.Parse(
                "{\"perf\":{\"scanBudgetPerFrame\":1,\"uiRefreshHz\":99},"
                + "\"balance\":{\"denseYieldMultiplier\":9,\"tier\":[{\"maxWorkers\":99}]}}",
                warnings.Add);

            TestAssert.Equal(32, config.Perf.ScanBudgetPerFrame);
            TestAssert.Equal(10, config.Perf.UiRefreshHz);
            TestAssert.Equal(1.6f, config.DenseYieldMultiplier, 1e-5f);
            TestAssert.Equal(40, config.TierOverrides[0]!.MaxWorkers);
            TestAssert.Equal(4, warnings.Count, "one warning per clamp: " + string.Join(" | ", warnings));
        }

        // ---------------------------------------------------------------- tier array shapes

        public static void TestParseNullTierFieldKeepsDefault()
        {
            var config = HdbfConfigCodec.Parse("{\"balance\":{\"tier\":[{\"plotCapacity\":null}]}}");
            TestAssert.Null(config.TierOverrides[0]);
        }

        public static void TestParseNonObjectTierEntryWarns()
        {
            var parsed = ParseWithWarnings("{\"balance\":{\"tier\":[42]}}");
            TestAssert.Null(parsed.Config.TierOverrides[0]);
            TestAssert.True(parsed.Warnings.Count > 0);
        }

        public static void TestParseExtraTierEntriesWarnAndAreIgnored()
        {
            var parsed = ParseWithWarnings(
                "{\"balance\":{\"tier\":[{}, {}, {}, {}, {\"maxWorkers\":4}]}}");
            TestAssert.Equal(4, parsed.Config.TierOverrides.Count);
            TestAssert.True(parsed.Warnings.Exists(message => message.Contains("only 4 used")),
                "extra tier entries must be reported: " + string.Join(" | ", parsed.Warnings));
        }

        // ---------------------------------------------------------------- serialisation

        public static void TestSerializeParseDefaultRoundTrip()
        {
            var json = HdbfConfigCodec.Serialize(HdbfConfig.Default);
            var warnings = new List<string>();
            var parsed = HdbfConfigCodec.Parse(json, warnings.Add);

            TestAssert.Equal(0, warnings.Count,
                "a serialized default config must parse without warnings: " + string.Join(" | ", warnings));
            TestAssert.Equal(HdbfConfig.Default.DenseYieldMultiplier, parsed.DenseYieldMultiplier);
            TestAssert.Equal(HdbfConfig.Default.Perf.ScanBudgetPerFrame, parsed.Perf.ScanBudgetPerFrame);
            TestAssert.True(parsed.Features.DensePlanting);
            TestAssert.False(parsed.Features.LogisticsOutput);
        }

        public static void TestSerializeWritesExpectedSections()
        {
            var json = HdbfConfigCodec.Serialize(HdbfConfig.Default);

            TestAssert.True(json.Contains("\"configSchemaVersion\""), "json: " + json);
            TestAssert.True(json.Contains("\"features\""), "json: " + json);
            TestAssert.True(json.Contains("\"perf\""), "json: " + json);
            TestAssert.True(json.Contains("\"balance\""), "json: " + json);
            TestAssert.True(json.Contains("\"logisticsOutput\": false"), "json: " + json);
        }

        public static void TestSerializeParseRoundTripsCustomValues()
        {
            var local = new HdbfConfig
            {
                ConfigSchemaVersion = HdbfConfig.CurrentSchemaVersion,
                TierOverrides = new TierOverride?[]
                {
                    new TierOverride { PlotCapacity = 120, MaxWorkers = 3 },
                    null,
                    null,
                    new TierOverride { ScienceCost = 2500 },
                },
                Features = new FeatureFlags { DensePlanting = false, LogisticsOutput = true },
                Perf = new PerfSettings { ScanBudgetPerFrame = 256, UiRefreshHz = 8 },
                DenseYieldMultiplier = 1.4f,
                DenseWaterMultiplier = 1.6f,
                DenseFatigueMultiplier = 1.3f,
                DenseWorkMultiplier = 1.1f,
                FallowRecoveryPerDay = 0.05f,
                FatiguePenalty = 0.5f,
                RotationFactor = 0.6f,
                RotationRecovery = 0.1f,
            };

            var warnings = new List<string>();
            var parsed = HdbfConfigCodec.Parse(HdbfConfigCodec.Serialize(local), warnings.Add);

            TestAssert.Equal(0, warnings.Count, string.Join(" | ", warnings));
            TestAssert.False(parsed.Features.DensePlanting);
            TestAssert.True(parsed.Features.LogisticsOutput);
            TestAssert.Equal(256, parsed.Perf.ScanBudgetPerFrame);
            TestAssert.Equal(8, parsed.Perf.UiRefreshHz);
            TestAssert.Equal(120, parsed.TierOverrides[0]!.PlotCapacity);
            TestAssert.Equal(3, parsed.TierOverrides[0]!.MaxWorkers);
            TestAssert.Null(parsed.TierOverrides[1]);
            TestAssert.Equal(2500, parsed.TierOverrides[3]!.ScienceCost);
            TestAssert.Equal(1.4f, parsed.DenseYieldMultiplier, 1e-6f);
            TestAssert.Equal(1.6f, parsed.DenseWaterMultiplier, 1e-6f);
            TestAssert.Equal(1.3f, parsed.DenseFatigueMultiplier, 1e-6f);
            TestAssert.Equal(1.1f, parsed.DenseWorkMultiplier, 1e-6f);
            TestAssert.Equal(0.05f, parsed.FallowRecoveryPerDay, 1e-6f);
            TestAssert.Equal(0.5f, parsed.FatiguePenalty, 1e-6f);
            TestAssert.Equal(0.6f, parsed.RotationFactor, 1e-6f);
            TestAssert.Equal(0.1f, parsed.RotationRecovery, 1e-6f);
        }

        // ---------------------------------------------------------------- ResolveTiers

        public static void TestResolveTiersDefaultsMatchCatalog()
        {
            var resolved = HdbfConfigCodec.ResolveTiers(HdbfConfig.Default);

            TestAssert.Equal(4, resolved.Count);
            for (var index = 0; index < resolved.Count; index++)
            {
                AssertSameProfile(TierCatalog.Get((HdbfTier)index), resolved[index]);
            }
        }

        public static void TestResolveTiersNullConfigUsesDefaults()
        {
            var resolved = HdbfConfigCodec.ResolveTiers(null);
            TestAssert.Equal(4, resolved.Count);
            TestAssert.Equal(600, resolved[3].PlotCapacity);
        }

        public static void TestResolveTiersAppliesOverridesAndKeepsBlueprintId()
        {
            var config = new HdbfConfig
            {
                TierOverrides = new TierOverride?[]
                {
                    new TierOverride
                    {
                        PlotCapacity = 200,
                        MaxWorkers = 3,
                        WorkRange = 13,
                        MinIrrigationCoverage = 0.55f,
                        WaterPerPlotPerDay = 0.03f,
                        TankCapacityDays = 5,
                        SoilFatigueFactor = 1.2f,
                        ScienceCost = 400,
                    },
                    null,
                    null,
                    null,
                },
            };

            var warnings = new List<string>();
            var resolved = HdbfConfigCodec.ResolveTiers(config, warnings.Add);
            var basic = resolved[0];

            TestAssert.Equal(0, warnings.Count);
            TestAssert.Equal(200, basic.PlotCapacity);
            TestAssert.Equal(3, basic.MaxWorkers);
            TestAssert.Equal(13, basic.WorkRange);
            TestAssert.Equal(0.55f, basic.MinIrrigationCoverage, 1e-6f);
            TestAssert.Equal(0.03f, basic.WaterPerPlotPerDay, 1e-6f);
            TestAssert.Equal(5, basic.TankCapacityDays);
            TestAssert.Equal(1.2f, basic.SoilFatigueFactor, 1e-6f);
            TestAssert.Equal(400, basic.ScienceCost);
            TestAssert.Equal("HDBF.BoxFarm.Basic", basic.BlueprintId);
            TestAssert.Equal(40, basic.BuildWood, "non-overridable build cost must come from the catalog");
            TestAssert.Equal(HdbfMaintenanceKind.IrrigationOnly, basic.MaintenanceKind);

            // Untouched tiers keep their catalog values.
            AssertSameProfile(TierCatalog.Get(HdbfTier.Top), resolved[3]);
        }

        public static void TestResolveTiersClampsOutOfRangeOverrides()
        {
            var config = new HdbfConfig
            {
                TierOverrides = new TierOverride?[]
                {
                    new TierOverride
                    {
                        PlotCapacity = 5,
                        MaxWorkers = 200,
                        WorkRange = 100,
                        MinIrrigationCoverage = 0f,
                        WaterPerPlotPerDay = 10f,
                        TankCapacityDays = 0,
                        SoilFatigueFactor = 10f,
                        ScienceCost = 1,
                    },
                    null,
                    null,
                    null,
                },
            };

            var warnings = new List<string>();
            var basic = HdbfConfigCodec.ResolveTiers(config, warnings.Add)[0];

            TestAssert.Equal(30, basic.PlotCapacity);
            TestAssert.Equal(40, basic.MaxWorkers);
            TestAssert.Equal(27, basic.WorkRange);
            TestAssert.Equal(0.30f, basic.MinIrrigationCoverage, 1e-6f);
            TestAssert.Equal(0.08f, basic.WaterPerPlotPerDay, 1e-6f);
            TestAssert.Equal(1, basic.TankCapacityDays);
            TestAssert.Equal(2f, basic.SoilFatigueFactor, 1e-6f);
            TestAssert.Equal(100, basic.ScienceCost);
            TestAssert.Equal(8, warnings.Count, "one clamp warning per field: " + string.Join(" | ", warnings));
        }

        public static void TestResolveTiersKeepsEveryBlueprintId()
        {
            var config = HdbfConfigCodec.Parse(
                "{\"balance\":{\"tier\":[{\"maxWorkers\":3},{},{\"maxWorkers\":9},{\"plotCapacity\":500}]}}");
            var resolved = HdbfConfigCodec.ResolveTiers(config);

            TestAssert.Equal("HDBF.BoxFarm.Basic", resolved[0].BlueprintId);
            TestAssert.Equal("HDBF.BoxFarm.Intermediate", resolved[1].BlueprintId);
            TestAssert.Equal("HDBF.BoxFarm.Advanced", resolved[2].BlueprintId);
            TestAssert.Equal("HDBF.BoxFarm.Top", resolved[3].BlueprintId);
            TestAssert.Equal(500, resolved[3].PlotCapacity);
        }

        // ---------------------------------------------------------------- helpers

        private static (HdbfConfig Config, List<string> Warnings) ParseWithWarnings(string json)
        {
            var warnings = new List<string>();
            return (HdbfConfigCodec.Parse(json, warnings.Add), warnings);
        }

        private static void AssertClampWarning(List<string> warnings, string pathFragment)
        {
            TestAssert.True(warnings.Exists(message =>
                    message.Contains("config.clamp") && message.Contains(pathFragment)),
                "expected a clamp warning for " + pathFragment + ", got: " + string.Join(" | ", warnings));
        }

        private static void AssertSameProfile(TierProfile expected, TierProfile actual)
        {
            TestAssert.Equal(expected.Tier, actual.Tier);
            TestAssert.Equal(expected.BlueprintId, actual.BlueprintId);
            TestAssert.Equal(expected.PlotCapacity, actual.PlotCapacity);
            TestAssert.Equal(expected.MaxWorkers, actual.MaxWorkers);
            TestAssert.Equal(expected.WorkRange, actual.WorkRange);
            TestAssert.Equal(expected.MinIrrigationCoverage, actual.MinIrrigationCoverage);
            TestAssert.Equal(expected.WaterPerPlotPerDay, actual.WaterPerPlotPerDay);
            TestAssert.Equal(expected.TankCapacityDays, actual.TankCapacityDays);
            TestAssert.Equal(expected.SoilFatigueFactor, actual.SoilFatigueFactor);
            TestAssert.Equal(expected.ScienceCost, actual.ScienceCost);
            TestAssert.Equal(expected.MaintenanceCycleDays, actual.MaintenanceCycleDays);
            TestAssert.Equal(expected.MaintenanceMaterialCount, actual.MaintenanceMaterialCount);
            TestAssert.Equal(expected.PowerDemand, actual.PowerDemand);
        }
    }
}
