using System;
using System.Collections.Generic;
using HDBF.Core;
using HDBF.Core.Balance;
using HDBF.Gameplay.Farming;

namespace HDBF.Tests
{
    /// <summary>
    /// L1 tests for the P1 farming rules written by the farm-loop workstream
    /// (src/HDBF.Gameplay/Farming/FarmWorkPlanner.cs), compiled into this assembly as linked
    /// production source plus the <see cref="UnityEngine.Vector3Int"/> structural stub.
    ///
    /// This covers the parts of the P1 loop that are game-free by design:
    /// * dispatch order: mature harvest before empty plant, then real path length, then plot id;
    /// * the daily labour cap (workers x 13 actions, 13 x 0.08 workdays, path cost consumes it);
    /// * workCost = 0.08 x (1 + path/20) x mode work multiplier (REQ-IMPL-16, chapter 6.7);
    /// * path-query quota bound and claimed-plot filtering.
    ///
    /// What still needs a running game (documented in docs/VERIFICATION.md): the real path
    /// measurement, the actual planting/harvest task dispatch, crop growth and output inventory.
    /// </summary>
    public static class FarmPlannerTests
    {
        // ------------------------------------------------------- ordering (REQ-IMPL-16)

        public static void TestPriorityHarvestBeforePlant()
        {
            TestAssert.Equal(0, FarmWorkPlanner.PriorityOf(FarmActionKind.Harvest));
            TestAssert.Equal(1, FarmWorkPlanner.PriorityOf(FarmActionKind.Plant));
            TestAssert.Equal(0, FarmWorkPlanner.HarvestPriority);
            TestAssert.Equal(1, FarmWorkPlanner.PlantPriority);
        }

        public static void TestMeasuredOrderHarvestFirstThenPathThenPlotId()
        {
            var harvestFar = Candidate(100, FarmActionKind.Harvest, 40);
            var harvestNear = Candidate(50, FarmActionKind.Harvest, 5);
            var plantNear = Candidate(10, FarmActionKind.Plant, 1);
            var plantTieA = Candidate(30, FarmActionKind.Plant, 7);
            var plantTieB = Candidate(20, FarmActionKind.Plant, 7);

            var list = new List<FarmWorkCandidate> { plantTieB, plantNear, harvestFar, plantTieA, harvestNear };
            FarmWorkPlanner.SortMeasured(list);

            TestAssert.Equal(50, list[0].PlotId, "nearest harvest first");
            TestAssert.Equal(100, list[1].PlotId, "farther harvest second");
            TestAssert.Equal(10, list[2].PlotId, "plant after harvest");
            TestAssert.Equal(20, list[3].PlotId, "equal path -> lower plot id");
            TestAssert.Equal(30, list[4].PlotId);
        }

        public static void TestUnmeasuredCandidateKeepsPriorityButSortsLastWithinIt()
        {
            var unmeasured = new FarmWorkCandidate(1, FarmActionKind.Harvest, "Carrot"); // path -1
            var measured = Candidate(2, FarmActionKind.Harvest, 500);
            var plant = Candidate(3, FarmActionKind.Plant, 1);

            var list = new List<FarmWorkCandidate> { unmeasured, plant, measured };
            FarmWorkPlanner.SortMeasured(list);

            TestAssert.Equal(2, list[0].PlotId, "measured harvest first (unmeasured sorts last within the harvest priority)");
            TestAssert.Equal(1, list[1].PlotId, "unmeasured harvest still outranks the plant because priority wins");
            TestAssert.Equal(3, list[2].PlotId, "plant is last");
        }

        public static void TestCompareMeasuredIsTotalOrder()
        {
            var a = Candidate(1, FarmActionKind.Plant, 3);
            var b = Candidate(2, FarmActionKind.Plant, 3);
            var c = Candidate(3, FarmActionKind.Plant, 4);

            TestAssert.True(FarmWorkPlanner.CompareMeasured(a, b) < 0);
            TestAssert.True(FarmWorkPlanner.CompareMeasured(b, a) > 0);
            TestAssert.Equal(0, FarmWorkPlanner.CompareMeasured(a, Candidate(1, FarmActionKind.Plant, 3)));
            TestAssert.True(FarmWorkPlanner.CompareMeasured(b, c) < 0);
        }

        public static void TestEstimatedOrderHarvestFirstThenDistanceThenPlotId()
        {
            var harvest = Candidate(1, FarmActionKind.Harvest, -1);
            var plantFar = Candidate(2, FarmActionKind.Plant, -1);
            var plantNear = Candidate(3, FarmActionKind.Plant, -1);
            var plantNearTie = Candidate(2, FarmActionKind.Plant, -1);

            TestAssert.True(FarmWorkPlanner.CompareEstimated(harvest, plantNear, 99, 1) < 0,
                "harvest priority wins over distance");
            TestAssert.True(FarmWorkPlanner.CompareEstimated(plantNear, plantFar, 2, 9) < 0,
                "same priority: nearer first");
            TestAssert.True(FarmWorkPlanner.CompareEstimated(plantNearTie, plantNear, 3, 3) < 0,
                "same distance: lower plot id first");
        }

        // ------------------------------------------------------- workCost (chapter 6.2 / 6.7)

        public static void TestActionWorkCostNormalModeMatchesChapterExamples()
        {
            TestAssert.Equal(0.08f, FarmWorkPlanner.ActionWorkCost(0, HdbfMode.Normal), 1e-6f);
            TestAssert.Equal(0.12f, FarmWorkPlanner.ActionWorkCost(10, HdbfMode.Normal), 1e-6f);
            TestAssert.Equal(0.16f, FarmWorkPlanner.ActionWorkCost(20, HdbfMode.Normal), 1e-6f);
            TestAssert.Equal(0.08f, FarmWorkPlanner.ActionWorkCost(-5, HdbfMode.Normal), 1e-6f);
        }

        public static void TestActionWorkCostDenseModeAddsTwentyPercent()
        {
            // chapter 6.7: dense work multiplier 1.20.
            TestAssert.Equal(0.096f, FarmWorkPlanner.ActionWorkCost(0, HdbfMode.Dense), 1e-6f);
            TestAssert.Equal(0.144f, FarmWorkPlanner.ActionWorkCost(10, HdbfMode.Dense), 1e-6f);
            TestAssert.Equal(0.192f, FarmWorkPlanner.ActionWorkCost(20, HdbfMode.Dense), 1e-6f);
        }

        public static void TestActionWorkCostFallowModeUsesTwentyPercent()
        {
            // chapter 6.7: fallow work multiplier 0.20.
            TestAssert.Equal(0.016f, FarmWorkPlanner.ActionWorkCost(0, HdbfMode.Fallow), 1e-6f);
            TestAssert.Equal(0.032f, FarmWorkPlanner.ActionWorkCost(20, HdbfMode.Fallow), 1e-6f);
        }

        public static void TestActionWorkCostHonoursCustomDenseWorkMultiplier()
        {
            TestAssert.Equal(0.12f, FarmWorkPlanner.ActionWorkCost(0, HdbfMode.Dense, 1.5f), 1e-6f);
        }

        // ------------------------------------------------------- daily labour cap (chapter 6.2)

        public static void TestDailyBudgetWithoutWorkersAffordsNothing()
        {
            var budget = FarmDailyBudget.Create(0);
            TestAssert.Equal(0, budget.ActionsRemaining);
            TestAssert.Equal(0f, budget.WorkdaysRemaining, 1e-6f);
            TestAssert.False(budget.CanAfford(0f), "no worker -> no plot action, even a free one");
            TestAssert.False(budget.TryConsume(0f));
        }

        public static void TestDailyBudgetIsThirteenActionsAndOnePointZeroFourWorkdays()
        {
            var budget = FarmDailyBudget.Create(1);
            TestAssert.Equal(13, budget.ActionsRemaining);
            TestAssert.Equal(1.04f, budget.WorkdaysRemaining, 1e-6f);

            for (var action = 0; action < 13; action++)
            {
                TestAssert.True(budget.TryConsume(0.08f), "action " + action + " must fit the budget");
            }

            TestAssert.Equal(0, budget.ActionsRemaining);
            TestAssert.False(budget.CanAfford(0.08f), "14th action exceeds the 13-actions/day cap");
        }

        public static void TestActionCapBindsBeforeWorkdaysAtZeroPath()
        {
            // At zero path the pool would allow 13 actions (1.04 / 0.08); a 14th must fail on the
            // action cap, proving both counters are enforced independently.
            var budget = FarmDailyBudget.Create(2);
            TestAssert.Equal(26, budget.ActionsRemaining);
            for (var action = 0; action < 26; action++)
            {
                TestAssert.True(budget.TryConsume(0.08f), "action " + action);
            }

            TestAssert.False(budget.CanAfford(0.08f));
        }

        public static void TestLongPathConsumesMoreOfTheDailyPool()
        {
            // path 100 -> 0.08 * (1 + 5) = 0.48 workdays per action: only 2 actions fit 1.04.
            var budget = FarmDailyBudget.Create(1);
            TestAssert.Equal(0.48f, FarmWorkPlanner.ActionWorkCost(100, HdbfMode.Normal), 1e-6f);
            TestAssert.True(budget.TryConsume(0.48f));
            TestAssert.True(budget.TryConsume(0.48f));
            TestAssert.False(budget.CanAfford(0.48f), "third long trip must exceed the daily workday pool");
            TestAssert.Equal(11, budget.ActionsRemaining, "action cap is not the binding limit here");
        }

        public static void TestRemainingWorkdayPoolIsRespected()
        {
            var budget = FarmDailyBudget.Create(1);
            // 0.5 > 0.08 but within the float epsilon guard of CanAfford (cost <= pool + 0.0001)?
            // No: 0.5 > 1.04 is false, it does fit; consume to leave 0.54 then a 0.6 action must fail.
            TestAssert.True(budget.TryConsume(0.5f));
            TestAssert.Equal(0.54f, budget.WorkdaysRemaining, 1e-5f);
            TestAssert.False(budget.CanAfford(0.6f), "remaining pool must be respected");
            TestAssert.True(budget.CanAfford(0.54f));
        }

        // ------------------------------------------------------- dispatch selection

        public static void TestFindNextSkipsClaimedUnmeasuredAndUnaffordable()
        {
            var budget = FarmDailyBudget.Create(1);
            var claimed = new HashSet<int> { 10 };
            var candidates = new List<FarmWorkCandidate>
            {
                Candidate(10, FarmActionKind.Harvest, 1),   // claimed
                new FarmWorkCandidate(11, FarmActionKind.Harvest, "Carrot"), // unmeasured
                Candidate(12, FarmActionKind.Harvest, 1000), // too expensive (4.08 workdays)
                Candidate(13, FarmActionKind.Plant, 0),      // first workable
            };

            var index = FarmWorkPlanner.FindNext(candidates, budget, claimed, HdbfMode.Normal, out var cost);

            TestAssert.Equal(3, index);
            TestAssert.Equal(0.08f, cost, 1e-6f);
        }

        public static void TestFindNextReturnsMinusOneWhenNothingAffordable()
        {
            var budget = FarmDailyBudget.Create(0);
            var candidates = new List<FarmWorkCandidate> { Candidate(1, FarmActionKind.Plant, 0) };
            var index = FarmWorkPlanner.FindNext(candidates, budget, new HashSet<int>(), HdbfMode.Normal, out var cost);
            TestAssert.Equal(-1, index);
            TestAssert.Equal(0f, cost);
        }

        public static void TestFindNextReturnsMinusOneForNullInputs()
        {
            TestAssert.Equal(-1, FarmWorkPlanner.FindNext(null!, FarmDailyBudget.Create(1), new HashSet<int>(), HdbfMode.Normal, out _));
            TestAssert.Equal(-1, FarmWorkPlanner.FindNext(new List<FarmWorkCandidate>(), null!, new HashSet<int>(), HdbfMode.Normal, out _));
        }

        // ------------------------------------------------------- planning quota

        public static void TestMeasurementQuotaCoversTwiceTheActionBudget()
        {
            TestAssert.Equal(0, FarmWorkPlanner.MeasurementQuota(0));
            TestAssert.Equal(0, FarmWorkPlanner.MeasurementQuota(-1));
            TestAssert.Equal(0, FarmWorkPlanner.MeasurementQuota(13, 0));
            TestAssert.Equal(42, FarmWorkPlanner.MeasurementQuota(13));
            TestAssert.Equal(68, FarmWorkPlanner.MeasurementQuota(26));
            TestAssert.Equal(432, FarmWorkPlanner.MeasurementQuota(208));
        }

        public static void TestMeasurementQuotaIsCapped()
        {
            TestAssert.Equal(512, FarmWorkPlanner.MeasurementQuota(1000));
            TestAssert.Equal(100, FarmWorkPlanner.MeasurementQuota(1000, 100));
        }

        public static void TestMeasurementQuotaCoversEveryTierActionCap()
        {
            foreach (var profile in TierCatalog.All)
            {
                var actions = BalanceFormulas.DailyPlotActions(profile.MaxWorkers);
                var quota = FarmWorkPlanner.MeasurementQuota(actions);
                TestAssert.True(quota >= actions * 2, "quota " + quota + " must cover twice the " + actions + " actions of " + profile.Tier);
                TestAssert.True(quota <= 512, "quota must stay within the planning cap for " + profile.Tier);
            }
        }

        public static void TestSortMeasuredIsStableAcrossRepeatedCalls()
        {
            var list = new List<FarmWorkCandidate>
            {
                Candidate(5, FarmActionKind.Plant, 9),
                Candidate(4, FarmActionKind.Harvest, 2),
                Candidate(3, FarmActionKind.Plant, 9),
            };

            FarmWorkPlanner.SortMeasured(list);
            var first = new[] { list[0].PlotId, list[1].PlotId, list[2].PlotId };
            FarmWorkPlanner.SortMeasured(list);
            var second = new[] { list[0].PlotId, list[1].PlotId, list[2].PlotId };

            TestAssert.Equal(first[0], second[0]);
            TestAssert.Equal(first[1], second[1]);
            TestAssert.Equal(first[2], second[2]);
            TestAssert.Equal(4, first[0]);
            TestAssert.Equal(3, first[1]);
            TestAssert.Equal(5, first[2]);
        }

        public static void TestSortMeasuredHandlesNullAndSingletons()
        {
            FarmWorkPlanner.SortMeasured(null!);
            var single = new List<FarmWorkCandidate> { Candidate(1, FarmActionKind.Plant, 0) };
            FarmWorkPlanner.SortMeasured(single);
            TestAssert.Equal(1, single[0].PlotId);
        }

        private static FarmWorkCandidate Candidate(int plotId, FarmActionKind kind, int pathLength)
        {
            return new FarmWorkCandidate(plotId, kind, kind == FarmActionKind.Harvest ? "Carrot" : string.Empty, pathLength);
        }
    }
}
