using System;
using System.Collections.Generic;
using HDBF.Core.Plots;

namespace HDBF.Tests
{
    /// <summary>
    /// L1 tests for managed-plot selection ordering and identity preservation (REQ-IMPL-07,
    /// TC-24 class). The selector is the only place that decides which cells a farm owns, so the
    /// order must be fully deterministic and previously registered ids must survive rescans.
    /// </summary>
    public static class PlotSelectionTests
    {
        public static void TestEmptyCandidatesSelectNothing()
        {
            TestAssert.Equal(0, PlotSelection.Select(Array.Empty<PlotCandidate>(), 60, null).Length);
        }

        public static void TestNonPositiveCapacitySelectsNothing()
        {
            var candidates = Candidates((10, 1), (20, 2));
            TestAssert.Equal(0, PlotSelection.Select(candidates, 0, Array.Empty<int>()).Length);
            TestAssert.Equal(0, PlotSelection.Select(candidates, -3, Array.Empty<int>()).Length);
        }

        public static void TestOrdersByDistanceThenPlotId()
        {
            // 20:1, 30:2, 40:3, 50:3, 10:5 -> distance asc, ties by plot id asc.
            var candidates = Candidates((30, 2), (20, 1), (10, 5), (50, 3), (40, 3));

            var selected = PlotSelection.Select(candidates, 5, null);

            AssertSequence(new[] { 20, 30, 40, 50, 10 }, selected);
        }

        public static void TestCapacityCutKeepsNearestPlots()
        {
            var candidates = Candidates((30, 2), (20, 1), (10, 5), (40, 3));

            var selected = PlotSelection.Select(candidates, 2, Array.Empty<int>());

            AssertSequence(new[] { 20, 30 }, selected);
        }

        public static void TestSelectsAllWhenCapacityExceedsCandidates()
        {
            var candidates = Candidates((7, 4), (3, 2), (5, 3));

            var selected = PlotSelection.Select(candidates, 60, null);

            AssertSequence(new[] { 3, 5, 7 }, selected);
        }

        public static void TestKeepsPreviouslyRegisteredIdsFirst()
        {
            // 10 is the farthest candidate but already registered, so it must be kept (REQ-IMPL-07).
            var candidates = Candidates((10, 100), (20, 1), (30, 2), (40, 3));

            var selected = PlotSelection.Select(candidates, 3, new[] { 10 });

            AssertSequence(new[] { 10, 20, 30 }, selected);
        }

        public static void TestKeepsRegisteredIdEvenWhenItWouldMissTheCut()
        {
            var candidates = Candidates((10, 100), (20, 1), (30, 2));

            var selected = PlotSelection.Select(candidates, 2, new[] { 10 });

            AssertSequence(new[] { 10, 20 }, selected);
        }

        public static void TestDropsRegisteredIdThatIsNoLongerACandidate()
        {
            var candidates = Candidates((10, 1), (20, 2), (30, 3));

            var selected = PlotSelection.Select(candidates, 2, new[] { 99, 30 });

            AssertSequence(new[] { 30, 10 }, selected);
        }

        public static void TestDuplicateCandidateIdsCollapseToShortestDistance()
        {
            var candidates = Candidates((5, 10), (5, 2), (6, 3));

            var selected = PlotSelection.Select(candidates, 10, null);

            // 5 appears twice but owns a single registration slot: total is min(10, 2) = 2.
            AssertSequence(new[] { 5, 6 }, selected);
        }

        public static void TestSelectionIsStableAcrossRepeatedCalls()
        {
            var candidates = Candidates((30, 2), (10, 2), (20, 1));
            var first = PlotSelection.Select(candidates, 3, new[] { 10 });
            var second = PlotSelection.Select(candidates, 3, new[] { 10 });

            AssertSequence(first, second);
        }

        public static void TestPreviousIdsAreTrimmedToCapacityInRegistrationOrder()
        {
            var candidates = Candidates((1, 1), (2, 2), (3, 3));

            var selected = PlotSelection.Select(candidates, 2, new[] { 3, 1, 2 });

            AssertSequence(new[] { 3, 1 }, selected);
        }

        public static void TestAllPreviousIdsKeepTheirOrderWhenCapacityAllows()
        {
            var candidates = Candidates((10, 1), (20, 2), (30, 3), (40, 4));

            var selected = PlotSelection.Select(candidates, 4, new[] { 40, 20 });

            AssertSequence(new[] { 40, 20, 10, 30 }, selected);
        }

        public static void TestNullCandidateListIsSafe()
        {
            TestAssert.Equal(0, PlotSelection.Select(null, 10, new[] { 1 }).Length);
        }

        public static void TestResultNeverExceedsCapacity()
        {
            var candidates = Candidates((1, 1), (2, 2), (3, 3), (4, 4), (5, 5));

            for (var capacity = 0; capacity <= 6; capacity++)
            {
                var selected = PlotSelection.Select(candidates, capacity, new[] { 5 });
                TestAssert.True(selected.Length <= capacity, "capacity " + capacity + " exceeded");
                TestAssert.Equal(Math.Min(capacity, 5), selected.Length, "capacity " + capacity);
            }
        }

        private static PlotCandidate[] Candidates(params (int PlotId, int Distance)[] values)
        {
            var result = new PlotCandidate[values.Length];
            for (var i = 0; i < values.Length; i++)
            {
                result[i] = new PlotCandidate(values[i].PlotId, values[i].Distance);
            }

            return result;
        }

        private static void AssertSequence(IReadOnlyList<int> expected, IReadOnlyList<int> actual)
        {
            TestAssert.Equal(expected.Count, actual.Count, "length");
            for (var i = 0; i < expected.Count; i++)
            {
                TestAssert.Equal(expected[i], actual[i], "element " + i);
            }
        }
    }
}
