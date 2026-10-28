using System;
using HDBF.Core;
using HDBF.Core.Balance;

namespace HDBF.Tests
{
    /// <summary>
    /// L1 assertions for the chapter 5 specification table and the chapter 6.10 self-check
    /// invariants (REQ-BAL-01..03, REQ-BAL-29). These tests guard the balance baseline itself:
    /// a config typo or a "harmless" constant bump fails here before it ships.
    /// </summary>
    public static class BalanceInvariantTests
    {
        // ---------------------------------------------------------------- chapter 5 table

        public static void TestTierSlotCapacities()
        {
            TestAssert.Equal(60, TierCatalog.Get(HdbfTier.Basic).PlotCapacity);
            TestAssert.Equal(150, TierCatalog.Get(HdbfTier.Intermediate).PlotCapacity);
            TestAssert.Equal(300, TierCatalog.Get(HdbfTier.Advanced).PlotCapacity);
            TestAssert.Equal(600, TierCatalog.Get(HdbfTier.Top).PlotCapacity);
        }

        public static void TestTierWorkerCaps()
        {
            TestAssert.Equal(2, TierCatalog.Get(HdbfTier.Basic).MaxWorkers);
            TestAssert.Equal(4, TierCatalog.Get(HdbfTier.Intermediate).MaxWorkers);
            TestAssert.Equal(8, TierCatalog.Get(HdbfTier.Advanced).MaxWorkers);
            TestAssert.Equal(16, TierCatalog.Get(HdbfTier.Top).MaxWorkers);
        }

        public static void TestTierWorkRangesAndAreas()
        {
            var ranges = new[] { 11, 15, 19, 27 };
            var areas = new[] { 121, 225, 361, 729 };
            for (var index = 0; index < ranges.Length; index++)
            {
                var profile = TierCatalog.Get((HdbfTier)index);
                TestAssert.Equal(ranges[index], profile.WorkRange);
                TestAssert.Equal(areas[index], ranges[index] * ranges[index]);
                TestAssert.True(profile.WorkRange <= TierCatalog.MaxWorkRangeCells,
                    "REQ-BAL-18: work range may never exceed 27x27 for " + profile.Tier);
                TestAssert.True(areas[index] > profile.PlotCapacity,
                    "chapter 11.3: range area must exceed plot capacity for " + profile.Tier);
            }
        }

        public static void TestPlotsPerFarmerIsThirtyEightForEveryTier()
        {
            TestAssert.Equal(38, TierCatalog.PlotsPerFarmer);
            for (var index = 0; index < TierCatalog.All.Count; index++)
            {
                // The spec field exists per tier; gameplay passes it through, so it must stay 38.
                var profile = TierCatalog.Get((HdbfTier)index);
                TestAssert.True(profile.PlotCapacity > 0);
            }

            TestAssert.Equal(38, BalanceFormulas.EffectivePlots(1000, 1));
        }

        public static void TestTierFatigueFactorsAndPerHarvestGain()
        {
            var factors = new[] { 1.00f, 1.10f, 1.25f, 1.50f };
            var gains = new[] { 0.080f, 0.088f, 0.100f, 0.120f };
            for (var index = 0; index < factors.Length; index++)
            {
                var profile = TierCatalog.Get((HdbfTier)index);
                TestAssert.Equal(factors[index], profile.SoilFatigueFactor, 1e-6f);
                TestAssert.Equal(gains[index], BalanceFormulas.FatigueDelta(true, profile.SoilFatigueFactor), 1e-6f);
            }
        }

        public static void TestTierScienceCosts()
        {
            TestAssert.Equal(200, TierCatalog.Get(HdbfTier.Basic).ScienceCost);
            TestAssert.Equal(600, TierCatalog.Get(HdbfTier.Intermediate).ScienceCost);
            TestAssert.Equal(1000, TierCatalog.Get(HdbfTier.Advanced).ScienceCost);
            TestAssert.Equal(3000, TierCatalog.Get(HdbfTier.Top).ScienceCost);
        }

        public static void TestTierBuildWorkdays()
        {
            // v2.1 (2026-10-26) shortened 6/10/16/28 -> 2/3/5/8 workdays; 2026-10-27 halved to
            // 1/1.5/2.5/4 workdays.
            TestAssert.Equal(1f, TierCatalog.Get(HdbfTier.Basic).BuildWorkdays);
            TestAssert.Equal(1.5f, TierCatalog.Get(HdbfTier.Intermediate).BuildWorkdays);
            TestAssert.Equal(2.5f, TierCatalog.Get(HdbfTier.Advanced).BuildWorkdays);
            TestAssert.Equal(4f, TierCatalog.Get(HdbfTier.Top).BuildWorkdays);
        }

        public static void TestTierBuildMaterials()
        {
            var basic = TierCatalog.Get(HdbfTier.Basic);
            TestAssert.Equal(40, basic.BuildWood);
            TestAssert.Equal(0, basic.BuildPlank);
            TestAssert.Equal(0, basic.BuildMetal);

            var intermediate = TierCatalog.Get(HdbfTier.Intermediate);
            TestAssert.Equal(20, intermediate.BuildWood);
            TestAssert.Equal(40, intermediate.BuildPlank);
            TestAssert.Equal(0, intermediate.BuildMetal);

            var advanced = TierCatalog.Get(HdbfTier.Advanced);
            TestAssert.Equal(0, advanced.BuildWood);
            TestAssert.Equal(50, advanced.BuildPlank);
            TestAssert.Equal(30, advanced.BuildMetal);

            var top = TierCatalog.Get(HdbfTier.Top);
            TestAssert.Equal(0, top.BuildWood);
            TestAssert.Equal(50, top.BuildPlank);
            TestAssert.Equal(100, top.BuildMetal);
        }

        public static void TestFullLoadWaterDrawPerTier()
        {
            // chapter 5.2: 1.2 / 3.6 / 8.4 / 19.2 units per day at full load.
            var expected = new[] { 1.2f, 3.6f, 8.4f, 19.2f };
            for (var index = 0; index < expected.Length; index++)
            {
                var profile = TierCatalog.Get((HdbfTier)index);
                var draw = BalanceFormulas.DailyTankDraw(profile.PlotCapacity, profile.WaterPerPlotPerDay,
                    HdbfMode.Normal);
                TestAssert.Equal(expected[index], draw, 1e-4f, "full-load draw for " + profile.Tier);
            }
        }

        public static void TestTierMaintenanceSpec()
        {
            var basic = TierCatalog.Get(HdbfTier.Basic);
            TestAssert.Equal(HdbfMaintenanceKind.IrrigationOnly, basic.MaintenanceKind);
            TestAssert.Equal(0f, basic.MaintenanceCycleDays);
            TestAssert.Equal(0, basic.MaintenanceMaterialCount);

            var intermediate = TierCatalog.Get(HdbfTier.Intermediate);
            TestAssert.Equal(HdbfMaintenanceKind.Plank, intermediate.MaintenanceKind);
            TestAssert.Equal(10f, intermediate.MaintenanceCycleDays);
            TestAssert.Equal(8, intermediate.MaintenanceMaterialCount);

            var advanced = TierCatalog.Get(HdbfTier.Advanced);
            TestAssert.Equal(HdbfMaintenanceKind.Metal, advanced.MaintenanceKind);
            TestAssert.Equal(10f, advanced.MaintenanceCycleDays);
            TestAssert.Equal(6, advanced.MaintenanceMaterialCount);

            var top = TierCatalog.Get(HdbfTier.Top);
            TestAssert.Equal(HdbfMaintenanceKind.PowerOrOffGridMetal, top.MaintenanceKind);
            TestAssert.True(top.NeedsPower, "top tier must support the connected power path (REQ-BAL-13)");
            TestAssert.Equal(150, top.PowerDemand);
            TestAssert.Equal(15, top.OffGridMaintenanceMaterial);
            TestAssert.Equal(8f, top.OffGridMaintenanceCycleDays);
        }

        // ---------------------------------------------------------------- chapter 6.10 self-check

        public static void TestUnitPlotBuildMaterialCostDecreasesMonotonically()
        {
            // chapter 5.2 / 6.10: total build material items per managed plot = 0.67 / 0.40 / 0.27 / 0.25.
            var expected = new[] { 2f / 3f, 0.40f, 4f / 15f, 0.25f };
            var previous = float.MaxValue;
            for (var index = 0; index < expected.Length; index++)
            {
                var profile = TierCatalog.Get((HdbfTier)index);
                var items = profile.BuildWood + profile.BuildPlank + profile.BuildMetal;
                var perPlot = (float)items / profile.PlotCapacity;
                TestAssert.Equal(expected[index], perPlot, 1e-5f, "material items per plot for " + profile.Tier);
                TestAssert.True(perPlot < previous, "unit-plot build cost must strictly decrease at " + profile.Tier);
                previous = perPlot;
            }
        }

        public static void TestUnitPlotBuildWorkdaysDecreasesMonotonically()
        {
            // Halved build workdays 1/1.5/2.5/4 over 60/150/300/600 slots keep the unit-plot effort decreasing.
            var expected = new[] { 1f / 60f, 1.5f / 150f, 2.5f / 300f, 4f / 600f };
            var previous = float.MaxValue;
            for (var index = 0; index < expected.Length; index++)
            {
                var profile = TierCatalog.Get((HdbfTier)index);
                var perPlot = profile.BuildWorkdays / profile.PlotCapacity;
                TestAssert.Equal(expected[index], perPlot, 1e-6f, "workdays per plot for " + profile.Tier);
                TestAssert.True(perPlot < previous, "unit-plot build effort must decrease at " + profile.Tier);
                previous = perPlot;
            }
        }

        public static void TestMaintenancePressureIncreasesWithTier()
        {
            var basic = TierCatalog.Get(HdbfTier.Basic);
            var intermediate = TierCatalog.Get(HdbfTier.Intermediate);
            var advanced = TierCatalog.Get(HdbfTier.Advanced);
            var top = TierCatalog.Get(HdbfTier.Top);

            // chapter 6.10 "current design": 0 / 0.8 planks/day / 0.6 metal/day / 1.875 metal/day.
            TestAssert.Equal(0f, MaterialPerDay(basic), 1e-6f);
            TestAssert.Equal(0.8f, MaterialPerDay(intermediate), 1e-6f);
            TestAssert.Equal(0.6f, MaterialPerDay(advanced), 1e-6f);
            TestAssert.Equal(1.875f, MaterialPerDay(top), 1e-6f);

            // Raw counts mix planks and metal, so pressure is compared after weighting the
            // material class (plank = 1, metal = 2 per unit): 0 < 0.8 < 1.2 < 3.75.
            var weighted = new[]
            {
                0f,
                MaterialPerDay(intermediate) * 1f,
                MaterialPerDay(advanced) * 2f,
                MaterialPerDay(top) * 2f,
            };
            var previous = -1f;
            for (var index = 0; index < weighted.Length; index++)
            {
                TestAssert.True(weighted[index] > previous,
                    "maintenance pressure must increase at " + (HdbfTier)index + " (got " + weighted[index] + ")");
                previous = weighted[index];
            }

            TestAssert.Equal(1.2f, weighted[2], 1e-6f);
            TestAssert.Equal(3.75f, weighted[3], 1e-6f);
        }

        public static void TestFatigueRateIncreasesWithTier()
        {
            // chapter 6.10: 0.080 / 0.088 / 0.100 / 0.120 per same-crop harvest.
            var previous = 0f;
            for (var index = 0; index < TierCatalog.All.Count; index++)
            {
                var progress = BalanceFormulas.FatigueDelta(true, TierCatalog.Get((HdbfTier)index).SoilFatigueFactor);
                TestAssert.True(progress > previous,
                    "fatigue rate must increase at " + (HdbfTier)index + " (got " + progress + ")");
                previous = progress;
            }
        }

        public static void TestMidHighTopUnitFarmerOutputIsEqual()
        {
            // chapter 6.10 / REQ-BAL-01 / REQ-BAL-29. At full crew the slot capacity binds
            // (38*workers >= capacity), so the actual rate is capacity/workers: 150/4 =
            // 300/8 = 600/16 = 37.5 plots per farmer for all three tiers.
            var unitOutputs = new float[3];
            for (var index = 1; index < 4; index++)
            {
                var profile = TierCatalog.Get((HdbfTier)index);
                TestAssert.True(profile.PlotCapacity <= profile.MaxWorkers * TierCatalog.PlotsPerFarmer,
                    "at full crew the slot capacity must be the binding limit: " + profile.Tier);
                var effective = BalanceFormulas.EffectivePlots(profile.PlotCapacity, profile.MaxWorkers);
                unitOutputs[index - 1] = (float)effective / profile.MaxWorkers;
            }

            TestAssert.Equal(37.5f, unitOutputs[0], 1e-6f);
            TestAssert.Equal(37.5f, unitOutputs[1], 1e-6f);
            TestAssert.Equal(37.5f, unitOutputs[2], 1e-6f);
            TestAssert.Equal(0f, unitOutputs[1] - unitOutputs[0], 1e-6f);
            TestAssert.Equal(0f, unitOutputs[2] - unitOutputs[0], 1e-6f);
        }

        public static void TestBasicTierIsSlotLimitedAtThirtyPerFarmer()
        {
            // chapter 5.2 / 6.10: 60 slots / 2 workers = 30, the intended entry-tier discount.
            var basic = TierCatalog.Get(HdbfTier.Basic);
            var effective = BalanceFormulas.EffectivePlots(basic.PlotCapacity, basic.MaxWorkers);
            TestAssert.Equal(60, effective);
            TestAssert.Equal(30f, (float)effective / basic.MaxWorkers, 1e-6f);
        }

        public static void TestFullCrewRedundancyMatchesChapterFive()
        {
            // chapter 5.2: workers*38 - slots = +16 / +2 / +4 / +8.
            var expected = new[] { 16, 2, 4, 8 };
            for (var index = 0; index < expected.Length; index++)
            {
                var profile = TierCatalog.Get((HdbfTier)index);
                var redundancy = profile.MaxWorkers * TierCatalog.PlotsPerFarmer - profile.PlotCapacity;
                TestAssert.Equal(expected[index], redundancy, "redundancy for " + profile.Tier);
            }
        }

        public static void TestScienceCostPerPlotNotUsedAsBalanceClaim()
        {
            // Guard against someone mistaking science/plot for the monotonic material curve:
            // the document's own numbers are 3.33 / 4.00 / 3.33 / 5.00, i.e. not monotonic.
            TestAssert.Equal(200f / 60f, (float)TierCatalog.Get(HdbfTier.Basic).ScienceCost / 60, 1e-5f);
            TestAssert.Equal(600f / 150f, (float)TierCatalog.Get(HdbfTier.Intermediate).ScienceCost / 150, 1e-5f);
            TestAssert.Equal(1000f / 300f, (float)TierCatalog.Get(HdbfTier.Advanced).ScienceCost / 300, 1e-5f);
            TestAssert.Equal(3000f / 600f, (float)TierCatalog.Get(HdbfTier.Top).ScienceCost / 600, 1e-5f);
        }

        public static void TestBlueprintIdsAreStable()
        {
            // chapter 9.5: blueprint ids are a published contract and map back to the tier.
            TestAssert.Equal("HDBF.BoxFarm.Basic", TierCatalog.Get(HdbfTier.Basic).BlueprintId);
            TestAssert.Equal("HDBF.BoxFarm.Intermediate", TierCatalog.Get(HdbfTier.Intermediate).BlueprintId);
            TestAssert.Equal("HDBF.BoxFarm.Advanced", TierCatalog.Get(HdbfTier.Advanced).BlueprintId);
            TestAssert.Equal("HDBF.BoxFarm.Top", TierCatalog.Get(HdbfTier.Top).BlueprintId);

            for (var index = 0; index < TierCatalog.All.Count; index++)
            {
                var profile = TierCatalog.Get((HdbfTier)index);
                TestAssert.True(TierCatalog.TryGetByBlueprintId(profile.BlueprintId, out var found),
                    "blueprint lookup must succeed for " + profile.Tier);
                TestAssert.Equal(index, (int)found.Tier, "blueprint must map back to its tier");
            }

            TestAssert.False(TierCatalog.TryGetByBlueprintId("HDBF.BoxFarm.Unknown", out _));
            TestAssert.False(TierCatalog.TryGetByBlueprintId(null, out _));
        }

        public static void TestBlueprintLookupAcceptsRuntimeTemplateNames()
        {
            // The game reports the shipped template name ("BoxFarm.Top.Folktails"), not the
            // canonical HDBF id, from Blueprint.Name; both spellings must resolve to the same
            // profile or every load logs a false "config.invalid: unknown box farm blueprint".
            var tiers = new[]
            {
                HdbfTier.Basic, HdbfTier.Intermediate, HdbfTier.Advanced, HdbfTier.Top,
            };
            foreach (var tier in tiers)
            {
                var expected = (int)tier;
                var name = tier.ToString();
                foreach (var id in new[]
                         {
                             "BoxFarm." + name,
                             "BoxFarm." + name + ".Folktails",
                             "BoxFarm." + name + ".IronTeeth",
                             "BoxFarm." + name + ".Folktails (Clone)",
                             " HDBF.BoxFarm." + name + " ",
                         })
                {
                    TestAssert.True(TierCatalog.TryGetByBlueprintId(id, out var found),
                        "template name must resolve: " + id);
                    TestAssert.Equal(expected, (int)found.Tier, "template name must map to its tier: " + id);
                }
            }

            TestAssert.False(TierCatalog.TryGetByBlueprintId("BoxFarm.Unknown.Folktails", out _));
            TestAssert.False(TierCatalog.TryGetByBlueprintId("HDBF.BoxFarm.TopExtra", out _));
            TestAssert.False(TierCatalog.TryGetByBlueprintId("NotABoxFarm.Basic", out _));
        }

        private static float MaterialPerDay(TierProfile profile)
        {
            if (profile.MaintenanceKind == HdbfMaintenanceKind.IrrigationOnly || profile.MaintenanceCycleDays <= 0f)
            {
                // Top tier's off-grid path has a separate cycle; treat it as the maintenance load.
                if (profile.OffGridMaintenanceCycleDays <= 0f)
                {
                    return 0f;
                }

                return profile.OffGridMaintenanceMaterial / profile.OffGridMaintenanceCycleDays;
            }

            return (float)profile.MaintenanceMaterialCount / profile.MaintenanceCycleDays;
        }
    }
}
