using System;
using System.Collections.Generic;
using HDBF.Core;
using HDBF.Core.Balance;
using HDBF.Core.Farming;
using HDBF.Core.Logging;
using HDBF.Core.Plots;
using HDBF.Gameplay.Components;
using HDBF.Gameplay.Farming;
using UnityEngine;

namespace HDBF.Tests
{
    /// <summary>
    /// L1 regressions for BUG-04 (P0, round-9 in-game log 2026-10-25): the plot-id assignment
    /// defect and the farm-plan path-query fault isolation.
    ///
    /// Root cause 1 — <c>TryAcceptPlot(..., out int plotId)</c> never assigned <c>plotId</c>, so
    /// the 102 accepted cells all became plot id 0, <see cref="PlotSelection"/> collapsed them to
    /// one selected plot and <c>registeredNow=1</c>. The structural fix removes the out id: the
    /// host only answers <c>bool</c> for the cell it resolved, and <see cref="HdbfPlotScanner"/>
    /// always encodes that accepted cell with the Core codec. The scanner is compiled into this
    /// assembly as linked production source (same pattern as FarmWorkPlanner), so these tests
    /// drive the real candidate construction rather than a re-implementation.
    ///
    /// Root cause 2 — a path query for a cell without a navigation flow field threw
    /// <c>InvalidOperationException</c> and was counted as a farm-loop fault, cancelling/ disabling
    /// the whole plan. <see cref="FarmPathProbe"/> now turns any per-cell query failure into
    /// "unreachable for this plan" (still isolated, still logged once per building as HDBF/Warn);
    /// the plan keeps every other candidate.
    ///
    /// No Timberborn or Unity assembly is referenced (REQ-TEST-03).
    /// </summary>
    public static class Bug04PlotIdAndPathTests
    {
        /// <summary>Basic tier work range: 11 x 11 = 121 scanned cells (round-9 log).</summary>
        private const int ScanSide = 11;

        /// <summary>round-9 fixture entrance/plant height; the fixture was otherwise flat.</summary>
        private const int SurfaceZ = 3;

        // ------------------------------------------------------------------ scanner: accepted cell -> plot id

        /// <summary>
        /// Reproduces the exact round-9 numbers: 121 scanned, 102 field-accepted, Basic capacity
        /// 60. Every accepted cell must produce its own candidate id (the pre-fix code produced a
        /// single plot id 0), and selection must grant the full 60 instead of 1.
        /// </summary>
        public static void TestScannerBuildsOneCandidatePerAcceptedCellWithDistinctIds()
        {
            var capacity = TierCatalog.Get(HdbfTier.Basic).PlotCapacity;
            TestAssert.Equal(60, capacity, "Basic tier plot capacity (round-9 expected registeredNow)");

            // Origin (5,5) puts the 11x11 work range on x,y in 0..10. Reject the first 19 scan
            // indices (= the round-9 field rejects), accept the other 102 cells.
            var host = new FakeScanHost(cell => (cell.x + (ScanSide * cell.y)) >= 19);
            var scanner = new HdbfPlotScanner(ScanSide);
            scanner.Begin(new Vector3Int(5, 5, SurfaceZ), ScanSide);

            scanner.Step(128, host); // one frame covers 121 cells at the default budget

            TestAssert.Equal(1, host.ScannedFrames, "OnPlotsScanned must be called exactly once");
            TestAssert.Equal(121, host.ResolvedCells, "every horizontal cell is resolved exactly once");
            TestAssert.NotNull(host.Candidates);
            var candidates = host.Candidates!;
            TestAssert.Equal(102, candidates.Count, "accepted=102 must produce candidates=102");

            var expectedIds = new HashSet<int>();
            for (var y = 0; y < ScanSide; y++)
            {
                for (var x = 0; x < ScanSide; x++)
                {
                    if (x + (ScanSide * y) < 19)
                    {
                        continue;
                    }

                    expectedIds.Add(PlotCellCodec.Encode(x, y, FakeScanHost.PlotCellZFor(x, y)));
                }
            }

            var ids = new HashSet<int>();
            foreach (var candidate in candidates)
            {
                TestAssert.True(ids.Add(candidate.PlotId),
                    "two accepted cells share plot id " + candidate.PlotId);
                TestAssert.True(expectedIds.Contains(candidate.PlotId),
                    "candidate id " + candidate.PlotId + " is not the Core codec of the accepted cell");
            }

            TestAssert.Equal(102, ids.Count);
            TestAssert.Equal(expectedIds.Count, ids.Count);

            // Registration invariant of the card: with no other farm owning cells and a large
            // enough capacity the selector must return every distinct accepted cell.
            TestAssert.Equal(102, PlotSelection.Select(candidates, 102, null).Length);

            // The round-9 regression: 102 accepted -> min(capacity, accepted) = 60 selected (the
            // old contract produced selected=1 granted=1 registeredNow=1).
            var selected = PlotSelection.Select(candidates, capacity, null);
            TestAssert.Equal(capacity, selected.Length,
                "102 distinct accepted cells must select the full Basic capacity of 60");
            TestAssert.Equal(60, new HashSet<int>(selected).Count, "selected ids must be distinct");
        }

        /// <summary>
        /// The scan range can contain negative horizontal coordinates (entrance near the map
        /// corner); those are the codec sign-extension branch, so the accepted-cell ids must stay
        /// distinct there too.
        /// </summary>
        public static void TestScannerIdsStayDistinctForNegativeCoordinates()
        {
            var host = new FakeScanHost(_ => true);
            var scanner = new HdbfPlotScanner(ScanSide);
            scanner.Begin(new Vector3Int(-3, -4, SurfaceZ), ScanSide);
            scanner.Step(200, host);

            TestAssert.NotNull(host.Candidates);
            var candidates = host.Candidates!;
            TestAssert.Equal(121, candidates.Count);

            var ids = new HashSet<int>();
            foreach (var candidate in candidates)
            {
                PlotCellCodec.Decode(candidate.PlotId, out var x, out var y, out var z);
                TestAssert.True(x >= -8 && x <= 2, "decoded x out of scan range: " + x);
                TestAssert.True(y >= -9 && y <= 1, "decoded y out of scan range: " + y);
                TestAssert.True(z >= SurfaceZ && z <= SurfaceZ + 3, "decoded z out of column range: " + z);
                TestAssert.True(ids.Add(candidate.PlotId),
                    "duplicate plot id for negative cell (" + x + "," + y + "," + z + ")");
            }

            TestAssert.Equal(121, ids.Count);
            TestAssert.Equal(60, PlotSelection.Select(candidates, 60, null).Length);
        }

        /// <summary>
        /// Pure Core side of the same guarantee: 121 different cells — negative horizontal
        /// coordinates and height boundaries included — encode to 121 distinct plot ids, so a
        /// scan of the Basic work range can never alias two cells onto one registration slot.
        /// </summary>
        public static void TestCodecGivesDistinctIdsTo121ScanCellsIncludingBoundaries()
        {
            var heights = new[] { 0, 1, SurfaceZ, 511, 1022, 1023 };
            var ids = new HashSet<int>();
            var candidates = new List<PlotCandidate>(ScanSide * ScanSide);
            for (var index = 0; index < ScanSide * ScanSide; index++)
            {
                var x = -40 + (index % ScanSide) - (ScanSide / 2);
                var y = -70 + (index / ScanSide) - (ScanSide / 2);
                var z = heights[index % heights.Length];
                var plotId = PlotCellCodec.Encode(x, y, z);
                PlotCellCodec.Decode(plotId, out var decodedX, out var decodedY, out var decodedZ);
                TestAssert.Equal(x, decodedX, "x round-trip at scan index " + index);
                TestAssert.Equal(y, decodedY, "y round-trip at scan index " + index);
                TestAssert.Equal(z, decodedZ, "z round-trip at scan index " + index);
                TestAssert.True(ids.Add(plotId), "duplicate plot id at scan index " + index);
                candidates.Add(new PlotCandidate(plotId, Math.Abs(x) + Math.Abs(y)));
            }

            TestAssert.Equal(121, ids.Count);
            TestAssert.Equal(60, PlotSelection.Select(candidates, 60, null).Length);
            TestAssert.Equal(121, PlotSelection.Select(candidates, 121, null).Length);
        }

        /// <summary>The selector must still collapse a genuinely repeated candidate cell to one slot.</summary>
        public static void TestRepeatedCandidatesForOneCellCollapseToASingleSlot()
        {
            var cellId = PlotCellCodec.Encode(-12, 34, SurfaceZ);
            var candidates = new List<PlotCandidate>
            {
                new PlotCandidate(cellId, 10),
                new PlotCandidate(cellId, 3),
                new PlotCandidate(cellId, 7),
            };

            var selected = PlotSelection.Select(candidates, 10, null);
            TestAssert.Equal(1, selected.Length, "one cell must occupy exactly one registration slot");
            TestAssert.Equal(cellId, selected[0]);
        }

        // ------------------------------------------------------------------ per-cell path-query tolerance

        /// <summary>
        /// The round-9 failure: the engine throws
        /// <c>InvalidOperationException: There's no cached flow field at 1187869</c> for one
        /// cell. That must become the documented "unreachable" value (-1), never escape, and never
        /// be mistaken for a measured path.
        /// </summary>
        public static void TestPathQueryFailureBecomesUnreachableInsteadOfThrowing()
        {
            var pathLength = FarmPathProbe.Measure(
                () => throw new InvalidOperationException("There's no cached flow field at 1187869"),
                out var failure);

            TestAssert.Equal(FarmPathProbe.Unreachable, pathLength);
            TestAssert.Equal(-1, pathLength);
            TestAssert.NotNull(failure);
            TestAssert.Equal("System.InvalidOperationException", failure!.GetType().FullName);
            TestAssert.False(FarmPathProbe.IsReachable(pathLength),
                "a failed query must never be a dispatchable path length");
        }

        /// <summary>A successful per-cell query keeps its measured value; an invalid one is unreachable.</summary>
        public static void TestMeasuredPathQueryKeepsItsLengthAndInvalidValuesAreUnreachable()
        {
            TestAssert.Equal(7, FarmPathProbe.Measure(() => 7, out var failure));
            TestAssert.Null(failure);

            TestAssert.Equal(FarmPathProbe.Unreachable, FarmPathProbe.Measure(() => -2, out var negative));
            TestAssert.Null(negative);

            TestAssert.Equal(FarmPathProbe.Unreachable, FarmPathProbe.Measure(null, out var missing));
            TestAssert.Null(missing);
        }

        /// <summary>Game path distances are floats; conversion rounds up and rejects untrusted values.</summary>
        public static void TestPathQueryDistanceConversionRoundsUpAndRejectsInvalidValues()
        {
            TestAssert.Equal(0, FarmPathProbe.FromQuery(true, 0f));
            TestAssert.Equal(1, FarmPathProbe.FromQuery(true, 0.1f));
            TestAssert.Equal(5, FarmPathProbe.FromQuery(true, 4.2f));
            TestAssert.Equal(5, FarmPathProbe.FromQuery(true, 5f));
            TestAssert.Equal(FarmPathProbe.Unreachable, FarmPathProbe.FromQuery(false, 4f));
            TestAssert.Equal(FarmPathProbe.Unreachable, FarmPathProbe.FromQuery(true, -1f));
            TestAssert.Equal(FarmPathProbe.Unreachable, FarmPathProbe.FromQuery(true, float.NaN));
            TestAssert.Equal(FarmPathProbe.Unreachable, FarmPathProbe.FromQuery(true, float.PositiveInfinity));
        }

        /// <summary>
        /// One failed plot must not cancel the day plan: the unreachable candidate sorts last and
        /// is skipped, and the next reachable plot is still dispatched with its real path cost.
        /// </summary>
        public static void TestOneUnreachablePlotDoesNotAbortTheDailyPlan()
        {
            var unreachableId = PlotCellCodec.Encode(0, 0, SurfaceZ);
            var reachableId = PlotCellCodec.Encode(0, 1, SurfaceZ);
            var candidates = new List<FarmWorkCandidate>
            {
                new FarmWorkCandidate(unreachableId, FarmActionKind.Plant, "Carrot",
                    FarmPathProbe.Unreachable),
                new FarmWorkCandidate(reachableId, FarmActionKind.Plant, "Carrot",
                    FarmPathProbe.FromQuery(true, 4.2f)),
            };

            FarmWorkPlanner.SortMeasured(candidates); // -1 sorts last, the plan is not discarded

            var budget = FarmDailyBudget.Create(1);
            var claimed = new HashSet<int>();
            var index = FarmWorkPlanner.FindNext(candidates, budget, claimed, HdbfMode.Normal,
                out var workCost);

            TestAssert.Equal(0, index, "the reachable plot is still dispatchable after a bad cell");
            TestAssert.Equal(reachableId, candidates[index].PlotId);
            TestAssert.Equal(5, candidates[index].PathLength, "real path 4.2 cells -> 5 whole cells");
            TestAssert.True(workCost > 0f, "dispatch keeps its real-path work cost");
        }

        /// <summary>
        /// Diagnostics contract: the round-10 log grep targets a stable per-building warning key
        /// and the old plan-fatal <c>farm loop fault (path length)</c> line must disappear. The
        /// controller appends the entity id to this key (throttle per building).
        /// </summary>
        public static void TestFarmPathUnreachableLogKeyIsStable()
        {
            TestAssert.Equal("gameplay.path-unreachable", HdbfLogKeys.FarmPathUnreachable);
        }

        // ------------------------------------------------------------------ fake scan host

        /// <summary>
        /// Game-free scan host double: it owns only the two host responsibilities (terrain-cell
        /// resolution and the accept predicate over the resolved cell). It never produces a plot
        /// id — that is exactly the BUG-04 structural change under test.
        /// </summary>
        private sealed class FakeScanHost : IHdbfPlotScanHost
        {
            private readonly Func<Vector3Int, bool> _isAccepted;

            public FakeScanHost(Func<Vector3Int, bool> isAccepted)
            {
                _isAccepted = isAccepted ?? (_ => false);
            }

            public List<PlotCandidate>? Candidates { get; private set; }

            public int ScannedFrames { get; private set; }

            public int ResolvedCells { get; private set; }

            public static int PlotCellZFor(int x, int y)
            {
                // Per-column plant-cell height, like a real map: the scanner must encode the cell
                // the host resolved and not a flat z copied from the scan origin.
                return SurfaceZ + (Math.Abs(x + y) % 4);
            }

            public Vector3Int ResolvePlotCell(Vector3Int horizontalCell)
            {
                ResolvedCells++;
                return new Vector3Int(horizontalCell.x, horizontalCell.y,
                    PlotCellZFor(horizontalCell.x, horizontalCell.y));
            }

            public bool TryAcceptPlot(Vector3Int plotCell, int distanceSquared)
            {
                // The predicate sees the resolved cell (x,y) — the same shape the controller's
                // game-service filters decide on.
                return _isAccepted(plotCell);
            }

            public void OnPlotsScanned(IReadOnlyList<PlotCandidate> candidates, int deferredFrames)
            {
                ScannedFrames++;
                Candidates = new List<PlotCandidate>(candidates);
            }
        }
    }
}
