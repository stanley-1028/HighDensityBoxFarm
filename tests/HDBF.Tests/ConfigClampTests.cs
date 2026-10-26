using System;
using System.Collections.Generic;
using HDBF.Core.Config;

namespace HDBF.Tests
{
    /// <summary>
    /// L1 tests for the chapter-18 clamp contract of config schema 2 (REQ-SAFE-13): every value
    /// has a range and a default, an out-of-range value is clamped, and every clamp is reported.
    /// </summary>
    public static class ConfigClampTests
    {
        public static void TestClampsSlotCapacity()
        {
            var low = ParseWithWarnings("{\"balance\":{\"tier\":[{\"slotCapacity\":5}]}}");
            TestAssert.Equal(30, low.Config.TierOverrides[0]!.SlotCapacity);
            AssertClampWarning(low.Warnings, "balance.tier[0].slotCapacity");

            var high = ParseWithWarnings("{\"balance\":{\"tier\":[{\"slotCapacity\":5000}]}}");
            TestAssert.Equal(1000, high.Config.TierOverrides[0]!.SlotCapacity);
        }

        public static void TestClampsTierWorkerCap()
        {
            var low = ParseWithWarnings("{\"balance\":{\"tier\":[{\"maxWorkers\":0}]}}");
            TestAssert.Equal(1, low.Config.TierOverrides[0]!.MaxWorkers);
            AssertClampWarning(low.Warnings, "maxWorkers");

            var high = ParseWithWarnings("{\"balance\":{\"tier\":[{\"maxWorkers\":99}]}}");
            TestAssert.Equal(40, high.Config.TierOverrides[0]!.MaxWorkers);
        }

        public static void TestClampsIrrigationCoverage()
        {
            var low = ParseWithWarnings("{\"balance\":{\"tier\":[{\"minIrrigationCoverage\":0.1}]}}");
            TestAssert.Equal(0.30f, low.Config.TierOverrides[0]!.MinIrrigationCoverage!.Value, 1e-5f);

            var high = ParseWithWarnings("{\"balance\":{\"tier\":[{\"minIrrigationCoverage\":0.99}]}}");
            TestAssert.Equal(0.95f, high.Config.TierOverrides[0]!.MinIrrigationCoverage!.Value, 1e-5f);
        }

        public static void TestClampsWaterAndTankDays()
        {
            var low = ParseWithWarnings(
                "{\"balance\":{\"tier\":[{\"waterPerSlotPerDay\":0,\"tankCapacityDays\":0}]}}");
            TestAssert.Equal(0.005f, low.Config.TierOverrides[0]!.WaterPerSlotPerDay!.Value, 1e-6f);
            TestAssert.Equal(1, low.Config.TierOverrides[0]!.TankCapacityDays);

            var high = ParseWithWarnings(
                "{\"balance\":{\"tier\":[{\"waterPerSlotPerDay\":1,\"tankCapacityDays\":50}]}}");
            TestAssert.Equal(0.08f, high.Config.TierOverrides[0]!.WaterPerSlotPerDay!.Value, 1e-6f);
            TestAssert.Equal(10, high.Config.TierOverrides[0]!.TankCapacityDays);
        }

        public static void TestClampsFatigueFactorAndScienceCost()
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

        public static void TestClampsDenseModeMultipliers()
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

        public static void TestClampsFatigueAndRotationParameters()
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

        public static void TestClampsRemainingPerfSettings()
        {
            var config = ParseWithWarnings(
                    "{\"perf\":{\"uiRefreshHz\":0,\"reducedBuildingThreshold\":5,"
                    + "\"steadyFrameBudgetMs\":0,\"warnFrameMs\":10,\"pauseFrameMs\":1}}")
                .Config.Perf;

            TestAssert.Equal(1, config.UiRefreshHz);
            TestAssert.Equal(20, config.ReducedBuildingThreshold);
            TestAssert.Equal(0.2f, config.SteadyFrameBudgetMs, 1e-5f);
            TestAssert.Equal(5f, config.WarnFrameMs, 1e-5f);
            TestAssert.Equal(3f, config.PauseFrameMs, 1e-5f);
        }

        public static void TestClampsWorldIconSettings()
        {
            var low = ParseWithWarnings(
                "{\"ui\":{\"worldIconMaxDistance\":1,\"worldIconIntervalSeconds\":0.01,"
                + "\"worldIconFarmsPerTick\":0}}").Config.Ui;
            TestAssert.Equal(10, low.WorldIconMaxDistance);
            TestAssert.Equal(0.25f, low.WorldIconIntervalSeconds, 1e-5f);
            TestAssert.Equal(1, low.WorldIconFarmsPerTick);

            var high = ParseWithWarnings(
                "{\"ui\":{\"worldIconMaxDistance\":1000,\"worldIconIntervalSeconds\":9,"
                + "\"worldIconFarmsPerTick\":999}}").Config.Ui;
            TestAssert.Equal(120, high.WorldIconMaxDistance);
            TestAssert.Equal(2f, high.WorldIconIntervalSeconds, 1e-5f);
            TestAssert.Equal(200, high.WorldIconFarmsPerTick);
        }

        public static void TestClampsEachValueAndReportsEveryClamp()
        {
            var warnings = new List<string>();
            var config = HdbfConfigCodec.Parse(
                "{\"perf\":{\"uiRefreshHz\":99},\"ui\":{\"worldIconMaxDistance\":1},"
                + "\"balance\":{\"denseYieldMultiplier\":9,\"tier\":[{\"maxWorkers\":99}]}}",
                warnings.Add);

            TestAssert.Equal(10, config.Perf.UiRefreshHz);
            TestAssert.Equal(10, config.Ui.WorldIconMaxDistance);
            TestAssert.Equal(1.6f, config.DenseYieldMultiplier, 1e-5f);
            TestAssert.Equal(40, config.TierOverrides[0]!.MaxWorkers);
            TestAssert.Equal(4, warnings.Count, "one warning per clamp: " + string.Join(" | ", warnings));
        }

        public static void TestTierArrayShapeProblemsWarnAndKeepDefaults()
        {
            var nonObject = ParseWithWarnings("{\"balance\":{\"tier\":[42]}}");
            TestAssert.Null(nonObject.Config.TierOverrides[0]);
            TestAssert.True(nonObject.Warnings.Count > 0);

            var extra = ParseWithWarnings("{\"balance\":{\"tier\":[{}, {}, {}, {}, {\"maxWorkers\":4}]}}");
            TestAssert.Equal(4, extra.Config.TierOverrides.Count);
            TestAssert.True(extra.Warnings.Exists(message => message.Contains("only 4 used")),
                "extra tier entries must be reported: " + string.Join(" | ", extra.Warnings));

            var nullField = HdbfConfigCodec.Parse("{\"balance\":{\"tier\":[{\"slotCapacity\":null}]}}");
            TestAssert.Null(nullField.TierOverrides[0]);
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
    }
}
