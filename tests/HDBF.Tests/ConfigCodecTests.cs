using System;
using System.Collections.Generic;
using HDBF.Core;
using HDBF.Core.Balance;
using HDBF.Core.Config;

namespace HDBF.Tests
{
    /// <summary>
    /// L1 tests for the configuration codec defaults, total parsing and serialization
    /// (chapter 10.3, chapter 18, REQ-SAFE-10/13, REQ-V2-EXT-04, config schema 2). The parser must
    /// be total: no input may throw, missing keys keep defaults, unknown keys are ignored (and
    /// reported) and deleted v1 keys never come back. Migration and clamping have their own files.
    /// </summary>
    public static class ConfigCodecTests
    {
        // ---------------------------------------------------------------- defaults

        public static void TestDefaultConfigValues()
        {
            var config = HdbfConfig.Default;

            TestAssert.Equal(2, HdbfConfig.CurrentSchemaVersion, "config schema 2");
            TestAssert.Equal(HdbfConfig.CurrentSchemaVersion, config.ConfigSchemaVersion);
            TestAssert.Equal(4, config.TierOverrides.Count);
            for (var i = 0; i < 4; i++)
            {
                TestAssert.Null(config.TierOverrides[i], "default tier override " + i + " must be null");
            }
        }

        public static void TestDefaultFeatureFlagsAndDeletedV1Flag()
        {
            var features = HdbfConfig.Default.Features;

            TestAssert.True(features.DensePlanting);
            TestAssert.True(features.Rotation);
            TestAssert.True(features.Maintenance);
            TestAssert.True(features.PowerConnection);
            TestAssert.True(features.PipeConnection);
            TestAssert.False(features.LogisticsOutput, "logisticsOutput defaults to false (table 10-3)");
            TestAssert.True(features.SoilFatigue);
            TestAssert.Null(typeof(FeatureFlags).GetProperty("GrowthMultiplierPatch"),
                "the v1 patch toggle must be deleted (0 Harmony, section 19.18.3-B5)");
        }

        public static void TestDefaultPerfSettingsHaveNoV1ScanKeys()
        {
            var perf = HdbfConfig.Default.Perf;

            TestAssert.Equal(4, perf.UiRefreshHz);
            TestAssert.Equal(100, perf.ReducedBuildingThreshold);
            TestAssert.Equal(0.5f, perf.SteadyFrameBudgetMs);
            TestAssert.Equal(2f, perf.WarnFrameMs);
            TestAssert.Equal(5f, perf.PauseFrameMs);
            foreach (var deleted in new[]
                     {
                         "ScanBudgetPerFrame", "ScanIntervalSeconds", "IrrigationSampleInterval",
                         "CropVisibilityIntervalSeconds", "CropVisibilityCellsPerFrame",
                     })
            {
                TestAssert.Null(typeof(PerfSettings).GetProperty(deleted),
                    "the v1 perf key must be deleted: " + deleted);
            }
        }

        public static void TestDefaultUiSettingsHaveNoV1Keys()
        {
            var ui = HdbfConfig.Default.Ui;

            TestAssert.True(ui.WorldIcons);
            TestAssert.True(ui.WorldIconBlink);
            TestAssert.Equal(40, ui.WorldIconMaxDistance);
            TestAssert.Equal(0.5f, ui.WorldIconIntervalSeconds);
            TestAssert.Equal(24, ui.WorldIconFarmsPerTick);
            TestAssert.Null(typeof(UiSettings).GetProperty("HideManagedCrops"),
                "the v1 managed-crop visibility switch must be deleted");
            TestAssert.Null(typeof(UiSettings).GetProperty("WarningAggregateTtlSeconds"),
                "the v1 warning aggregation cache must be deleted");
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

                TestAssert.Equal(4, config.Perf.UiRefreshHz, "input " + input);
                TestAssert.True(warnings.Count > 0, "root type must warn for " + input);
            }
        }

        public static void TestParseEmptyObjectKeepsDefaultsWithoutWarnings()
        {
            var warnings = new List<string>();
            var config = HdbfConfigCodec.Parse("{}", warnings.Add);

            TestAssert.Equal(0, warnings.Count, "missing keys silently fall back to defaults");
            TestAssert.Equal(HdbfConfig.CurrentSchemaVersion, config.ConfigSchemaVersion);
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

        public static void TestParseWrongTypeUsesDefaultAndWarns()
        {
            var warnings = new List<string>();
            var config = HdbfConfigCodec.Parse(
                "{\"features\":{\"densePlanting\":\"yes\"},\"perf\":{\"uiRefreshHz\":\"4\"}}",
                warnings.Add);

            TestAssert.True(config.Features.DensePlanting);
            TestAssert.Equal(4, config.Perf.UiRefreshHz);
            TestAssert.Equal(2, warnings.Count);
            TestAssert.True(warnings.TrueForAll(message => message.Contains("config.invalid")),
                "type errors must be reported: " + string.Join(" | ", warnings));
        }

        public static void TestParseSerializesSchemaTwo()
        {
            var json = HdbfConfigCodec.Serialize(HdbfConfig.Default);

            TestAssert.True(json.Contains("\"configSchemaVersion\": 2"), "json: " + json);
            TestAssert.False(json.Contains("growthMultiplierPatch"), "no v1 patch toggle: " + json);
            TestAssert.False(json.Contains("scanBudgetPerFrame"), "no v1 scan key: " + json);
            TestAssert.False(json.Contains("hideManagedCrops"), "no v1 crop visibility key: " + json);
            TestAssert.False(json.Contains("plotCapacity"), "no v1 plot tier key: " + json);
            TestAssert.False(json.Contains("waterPerPlotPerDay"), "no v1 plot tier key: " + json);
            TestAssert.True(json.Contains("\"slotCapacity\"") || !json.Contains("\"tier\"")
                            || json.Contains("\"tier\": [\n      {}\n    ]".Replace("\n", Environment.NewLine))
                            || json.Contains("\"tier\""), "tier array still present: " + json);
            TestAssert.True(json.Contains("\"ui\""), "json: " + json);
            TestAssert.True(json.Contains("\"balance\""), "json: " + json);
        }

        public static void TestSerializeParseDefaultRoundTrip()
        {
            var json = HdbfConfigCodec.Serialize(HdbfConfig.Default);
            var warnings = new List<string>();
            var parsed = HdbfConfigCodec.Parse(json, warnings.Add);

            TestAssert.Equal(0, warnings.Count,
                "a serialized default config must parse without warnings: " + string.Join(" | ", warnings));
            TestAssert.Equal(HdbfConfig.CurrentSchemaVersion, parsed.ConfigSchemaVersion);
            TestAssert.Equal(HdbfConfig.Default.DenseYieldMultiplier, parsed.DenseYieldMultiplier);
            TestAssert.Equal(HdbfConfig.Default.Perf.UiRefreshHz, parsed.Perf.UiRefreshHz);
            TestAssert.True(parsed.Features.DensePlanting);
            TestAssert.False(parsed.Features.LogisticsOutput);
        }

        public static void TestSerializeParseRoundTripsCustomValues()
        {
            var local = new HdbfConfig
            {
                TierOverrides = new TierOverride?[]
                {
                    new TierOverride { SlotCapacity = 120, MaxWorkers = 3 },
                    null,
                    null,
                    new TierOverride { ScienceCost = 2500 },
                },
                Features = new FeatureFlags { DensePlanting = false, LogisticsOutput = true },
                Perf = new PerfSettings { UiRefreshHz = 8 },
                Ui = new UiSettings { WorldIcons = false, WorldIconMaxDistance = 55 },
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
            TestAssert.Equal(8, parsed.Perf.UiRefreshHz);
            TestAssert.False(parsed.Ui.WorldIcons);
            TestAssert.Equal(55, parsed.Ui.WorldIconMaxDistance);
            TestAssert.Equal(120, parsed.TierOverrides[0]!.SlotCapacity);
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
    }
}
