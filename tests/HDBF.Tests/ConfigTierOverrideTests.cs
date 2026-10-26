using System;
using System.Collections.Generic;
using HDBF.Core;
using HDBF.Core.Balance;
using HDBF.Core.Config;

namespace HDBF.Tests
{
    /// <summary>
    /// L1 tests for <see cref="HdbfConfigCodec.ResolveTiers"/> (chapter 9.5/18, REQ-BAL-18): the
    /// override layer keeps the tier BlueprintId and every non-overridable field, applies the v2
    /// slot keys and clamps each supplied value with one warning.
    /// </summary>
    public static class ConfigTierOverrideTests
    {
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
                        SlotCapacity = 200,
                        MaxWorkers = 3,
                        MinIrrigationCoverage = 0.55f,
                        WaterPerSlotPerDay = 0.03f,
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
            var basic = HdbfConfigCodec.ResolveTiers(config, warnings.Add)[0];

            TestAssert.Equal(0, warnings.Count);
            TestAssert.Equal(200, basic.PlotCapacity, "slotCapacity overrides the tier capacity");
            TestAssert.Equal(3, basic.MaxWorkers);
            TestAssert.Equal(0.55f, basic.MinIrrigationCoverage, 1e-6f);
            TestAssert.Equal(0.03f, basic.WaterPerPlotPerDay, 1e-6f);
            TestAssert.Equal(5, basic.TankCapacityDays);
            TestAssert.Equal(1.2f, basic.SoilFatigueFactor, 1e-6f);
            TestAssert.Equal(400, basic.ScienceCost);
            TestAssert.Equal("HDBF.BoxFarm.Basic", basic.BlueprintId);
            TestAssert.Equal(40, basic.BuildWood, "non-overridable build cost must come from the catalog");
            TestAssert.Equal(HdbfMaintenanceKind.IrrigationOnly, basic.MaintenanceKind);
            TestAssert.Equal(11, basic.WorkRange, "the v1 work range is not a schema-2 config key");

            AssertSameProfile(TierCatalog.Get(HdbfTier.Top), HdbfConfigCodec.ResolveTiers(config)[3]);
        }

        public static void TestResolveTiersClampsOutOfRangeOverrides()
        {
            var config = new HdbfConfig
            {
                TierOverrides = new TierOverride?[]
                {
                    new TierOverride
                    {
                        SlotCapacity = 5,
                        MaxWorkers = 200,
                        MinIrrigationCoverage = 0f,
                        WaterPerSlotPerDay = 10f,
                        TankCapacityDays = 0,
                        SoilFatigueFactor = 10f,
                        ScienceCost = 1,
                    },
                },
            };

            var warnings = new List<string>();
            var basic = HdbfConfigCodec.ResolveTiers(config, warnings.Add)[0];

            TestAssert.Equal(30, basic.PlotCapacity);
            TestAssert.Equal(40, basic.MaxWorkers);
            TestAssert.Equal(0.30f, basic.MinIrrigationCoverage, 1e-6f);
            TestAssert.Equal(0.08f, basic.WaterPerPlotPerDay, 1e-6f);
            TestAssert.Equal(1, basic.TankCapacityDays);
            TestAssert.Equal(2f, basic.SoilFatigueFactor, 1e-6f);
            TestAssert.Equal(100, basic.ScienceCost);
            TestAssert.Equal(7, warnings.Count, "one clamp warning per field: " + string.Join(" | ", warnings));
        }

        public static void TestResolveTiersKeepsEveryBlueprintId()
        {
            var config = HdbfConfigCodec.Parse(
                "{\"balance\":{\"tier\":[{\"maxWorkers\":3},{},{\"maxWorkers\":9},{\"slotCapacity\":500}]}}");
            var resolved = HdbfConfigCodec.ResolveTiers(config);

            TestAssert.Equal("HDBF.BoxFarm.Basic", resolved[0].BlueprintId);
            TestAssert.Equal("HDBF.BoxFarm.Intermediate", resolved[1].BlueprintId);
            TestAssert.Equal("HDBF.BoxFarm.Advanced", resolved[2].BlueprintId);
            TestAssert.Equal("HDBF.BoxFarm.Top", resolved[3].BlueprintId);
            TestAssert.Equal(500, resolved[3].PlotCapacity);
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
