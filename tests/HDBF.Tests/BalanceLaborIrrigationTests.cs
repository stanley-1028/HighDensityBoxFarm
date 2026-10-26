using System;
using HDBF.Core;
using HDBF.Core.Balance;

namespace HDBF.Tests
{
    /// <summary>
    /// L1 tests for the labour and irrigation formulas: chapter 6.2 (TC-17, TC-18) and
    /// chapter 6.3 (TC-16, AC-04). Every expected number is copied from the requirement
    /// document, not recomputed from the implementation under test.
    /// </summary>
    public static class BalanceLaborIrrigationTests
    {
        // ---------------------------------------------------------------- P_eff (chapter 6.2)

        public static void TestEffectivePlotsZeroWorkersIsZero()
        {
            TestAssert.Equal(0, BalanceFormulas.EffectivePlots(TierCatalog.MaxPlotCapacity, 0));
            TestAssert.Equal(0, BalanceFormulas.EffectivePlots(60, 0));
        }

        public static void TestEffectivePlotsWorkerLimitedTier()
        {
            // Basic: 2 workers x 38 = 76 > 60 slot capacity -> 60 (chapter 5.2).
            TestAssert.Equal(60, BalanceFormulas.EffectivePlots(60, 2));
            TestAssert.Equal(38, BalanceFormulas.EffectivePlots(60, 1));
        }

        public static void TestEffectivePlotsTopTierClampsAtSixHundred()
        {
            // TC-17 / REQ-TEST-08: 16 x 38 = 608 must be clamped to the 600 slot capacity.
            TestAssert.Equal(600, BalanceFormulas.EffectivePlots(600, 16));
            TestAssert.True(16 * TierCatalog.PlotsPerFarmer > TierCatalog.MaxPlotCapacity,
                "fixture must exercise the clamp branch (608 > 600)");
        }

        public static void TestEffectivePlotsMidAndHighTiersAreSlotBound()
        {
            // 4 x 38 = 152 -> 150; 8 x 38 = 304 -> 300 (chapter 5.2).
            TestAssert.Equal(150, BalanceFormulas.EffectivePlots(150, 4));
            TestAssert.Equal(300, BalanceFormulas.EffectivePlots(300, 8));
        }

        public static void TestEffectivePlotsNonPositiveInputsAreZero()
        {
            TestAssert.Equal(0, BalanceFormulas.EffectivePlots(0, 4));
            TestAssert.Equal(0, BalanceFormulas.EffectivePlots(-10, 4));
            TestAssert.Equal(0, BalanceFormulas.EffectivePlots(100, -1));
            TestAssert.Equal(0, BalanceFormulas.EffectivePlots(100, 4, 0));
        }

        public static void TestEffectivePlotsHonoursCustomPlotsPerFarmer()
        {
            // chapter 18.2 allows balance.plotsPerWorker in 20..50.
            TestAssert.Equal(40, BalanceFormulas.EffectivePlots(100, 2, 20));
            TestAssert.Equal(100, BalanceFormulas.EffectivePlots(100, 2, 50));
        }

        public static void TestDailyPlotActionsIsThirteenPerWorker()
        {
            // chapter 6.2: C_day = W x 13.
            TestAssert.Equal(0, BalanceFormulas.DailyPlotActions(0));
            TestAssert.Equal(13, BalanceFormulas.DailyPlotActions(1));
            TestAssert.Equal(26, BalanceFormulas.DailyPlotActions(2));
            TestAssert.Equal(208, BalanceFormulas.DailyPlotActions(16));
        }

        public static void TestWorkCostAtChapterExamplePathLengths()
        {
            // TC-18: 0.08 x (1 + path/20) = 0.08 / 0.12 / 0.16 workdays.
            TestAssert.Equal(0.08f, BalanceFormulas.WorkCost(0));
            TestAssert.Equal(0.12f, BalanceFormulas.WorkCost(10));
            TestAssert.Equal(0.16f, BalanceFormulas.WorkCost(20));
        }

        public static void TestWorkCostNegativePathCountsAsZero()
        {
            TestAssert.Equal(0.08f, BalanceFormulas.WorkCost(-5));
        }

        public static void TestWorkCostUsesConfigurableBaseAndDivisor()
        {
            // chapter 18.3: base in 0.03..0.2, divisor in 5..50.
            TestAssert.Equal(0.2f, BalanceFormulas.WorkCost(20, 0.1f, 20f));
            TestAssert.Equal(0.15f, BalanceFormulas.WorkCost(5, 0.1f, 10f));
        }

        // ---------------------------------------------------------------- coverage (chapter 6.3)

        public static void TestCoverageZeroRegisteredIsZero()
        {
            TestAssert.Equal(0f, BalanceFormulas.Coverage(0, 0));
            TestAssert.Equal(0f, BalanceFormulas.Coverage(10, 0));
        }

        public static void TestCoverageZeroIrrigatedIsZero()
        {
            TestAssert.Equal(0f, BalanceFormulas.Coverage(0, 100));
        }

        public static void TestCoverageExactRatios()
        {
            TestAssert.Equal(0.25f, BalanceFormulas.Coverage(25, 100));
            TestAssert.Equal(0.5f, BalanceFormulas.Coverage(50, 100));
            TestAssert.Equal(0.8f, BalanceFormulas.Coverage(80, 100));
        }

        public static void TestCoverageNeverExceedsOne()
        {
            // Defensive clamp: a sampler glitch must not create a >100% coverage bonus.
            TestAssert.Equal(1f, BalanceFormulas.Coverage(150, 100));
        }

        // ---------------------------------------------------------------- C_min (chapter 5 table)

        public static void TestTierMinimumIrrigationValues()
        {
            TestAssert.Equal(0.50f, TierCatalog.Get(HdbfTier.Basic).MinIrrigationCoverage);
            TestAssert.Equal(0.60f, TierCatalog.Get(HdbfTier.Intermediate).MinIrrigationCoverage);
            TestAssert.Equal(0.75f, TierCatalog.Get(HdbfTier.Advanced).MinIrrigationCoverage);
            TestAssert.Equal(0.80f, TierCatalog.Get(HdbfTier.Top).MinIrrigationCoverage);
        }

        // ---------------------------------------------------------------- G_irr (TC-16)

        public static void TestIrrigationMultiplierZeroAtOrBelowQuarterMinimum()
        {
            foreach (var tier in TierCatalog.All)
            {
                TestAssert.Equal(0f, BalanceFormulas.IrrigationMultiplier(0f, tier.MinIrrigationCoverage),
                    "G_irr at C=0 must be 0 for " + tier.Tier);
                TestAssert.Equal(0f,
                    BalanceFormulas.IrrigationMultiplier(tier.MinIrrigationCoverage * 0.25f, tier.MinIrrigationCoverage),
                    "G_irr at C=0.25*C_min must be 0 for " + tier.Tier);
                TestAssert.Equal(0f,
                    BalanceFormulas.IrrigationMultiplier(tier.MinIrrigationCoverage * 0.10f, tier.MinIrrigationCoverage),
                    "G_irr below 0.25*C_min must remain 0 for " + tier.Tier);
            }
        }

        public static void TestIrrigationMultiplierOneThirdAtHalfMinimum()
        {
            // TC-16: C = 0.5*C_min -> G_irr = 1/3.
            foreach (var tier in TierCatalog.All)
            {
                var coverage = tier.MinIrrigationCoverage * 0.5f;
                TestAssert.Equal(1f / 3f, BalanceFormulas.IrrigationMultiplier(coverage, tier.MinIrrigationCoverage),
                    1e-5f, "G_irr at C=0.5*C_min for " + tier.Tier);
            }
        }

        public static void TestIrrigationMultiplierTwoThirdsAtThreeQuarterMinimum()
        {
            foreach (var tier in TierCatalog.All)
            {
                var coverage = tier.MinIrrigationCoverage * 0.75f;
                TestAssert.Equal(2f / 3f, BalanceFormulas.IrrigationMultiplier(coverage, tier.MinIrrigationCoverage),
                    1e-5f, "G_irr at C=0.75*C_min for " + tier.Tier);
            }
        }

        public static void TestIrrigationMultiplierOneAtMinimumAndAbove()
        {
            // TC-16: C = C_min -> 1 and C = 1 -> 1, for every tier (chapter 5 C_min values).
            foreach (var tier in TierCatalog.All)
            {
                TestAssert.Equal(1f, BalanceFormulas.IrrigationMultiplier(tier.MinIrrigationCoverage, tier.MinIrrigationCoverage),
                    "G_irr at C=C_min for " + tier.Tier);
                TestAssert.Equal(1f, BalanceFormulas.IrrigationMultiplier(1f, tier.MinIrrigationCoverage),
                    "G_irr at C=1 for " + tier.Tier);
            }
        }

        public static void TestIrrigationMultiplierNoRequirementIsOne()
        {
            // A tier without an irrigation requirement is always fully irrigated.
            TestAssert.Equal(1f, BalanceFormulas.IrrigationMultiplier(0f, 0f));
            TestAssert.Equal(1f, BalanceFormulas.IrrigationMultiplier(0.2f, 0f));
        }

        public static void TestIrrigationMultiplierNegativeCoverageIsZero()
        {
            TestAssert.Equal(0f, BalanceFormulas.IrrigationMultiplier(-0.5f, 0.6f));
        }

        public static void TestIrrigationMultiplierIsMonotonicAcrossTheRamp()
        {
            // Acceptance AC-04: coverage above 0.25*C_min must never decrease the multiplier.
            const float cMin = 0.6f;
            var previous = 0f;
            for (var step = 0; step <= 20; step++)
            {
                var coverage = cMin * 0.25f + (cMin * 0.75f) * (step / 20f);
                var current = BalanceFormulas.IrrigationMultiplier(coverage, cMin);
                TestAssert.True(current >= previous - 1e-6f, "G_irr decreased at step " + step);
                previous = current;
            }

            TestAssert.Equal(1f, previous);
        }

        public static void TestCoverageAndIrrigationComposeAtChapterSixteenPoints()
        {
            // End-to-end version of TC-16 for the basic tier (C_min = 0.50, 100 registered plots):
            // C = 0 / 0.25*Cmin (12.5%) / 0.5*Cmin (25%) / C_min (50%) / 1 (100%).
            const float cMin = 0.50f;
            TestAssert.Equal(0f, Chain(0, 100, cMin));
            TestAssert.Equal(0f, Chain(12, 100, cMin)); // 12% < 0.25*C_min = 12.5%
            TestAssert.Equal(1f / 3f, Chain(25, 100, cMin), 1e-5f);
            TestAssert.Equal(1f, Chain(50, 100, cMin));
            TestAssert.Equal(1f, Chain(100, 100, cMin));
        }

        private static float Chain(int irrigated, int registered, float cMin)
        {
            var coverage = BalanceFormulas.Coverage(irrigated, registered);
            return BalanceFormulas.IrrigationMultiplier(coverage, cMin);
        }
    }
}
