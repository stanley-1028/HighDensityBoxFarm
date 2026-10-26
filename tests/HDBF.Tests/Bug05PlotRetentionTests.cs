using System;
using System.Collections.Generic;
using System.IO;
using HDBF.Core;
using HDBF.Core.Balance;
using HDBF.Core.Plots;
using HDBF.Gameplay.Components;
using UnityEngine;

namespace HDBF.Tests
{
    /// <summary>
    /// L1 regressions for BUG-05 (P0, round-11 in-game log 2026-10-25): the daily rescan treated
    /// already registered plots as "new cells", so as soon as a crop occupied its plot cell the
    /// game's <c>CanPlant</c> / <c>AnyObjectAt</c> / planting-blocker filters rejected it. The
    /// registry shrank 60 → 25 → 0, the last scan logged <c>plot.scan-empty</c> and the farm had
    /// no managed plot left to harvest.
    ///
    /// The fix has a Core half and a Gameplay wiring that L1 can only meet at the seam: the rule
    /// itself (<see cref="PlotRetention"/>) and the observability delta
    /// (<see cref="PlotScanStats.RecordSelection"/>) are pure Core and tested directly; the
    /// scanning/selection integration is tested through the real linked production
    /// <see cref="HdbfPlotScanner"/> plus the real <see cref="PlotSelection"/>, with a game-free
    /// host double that maps cell states through the same Core rule the controller applies.
    ///
    /// No Timberborn or Unity assembly is referenced (REQ-TEST-03); UnityEngine is the structural
    /// Vector3Int stub only.
    /// </summary>
    public static class Bug05PlotRetentionTests
    {
        /// <summary>Basic tier work range: 11 x 11 = 121 scanned cells (round-11 log).</summary>
        private const int ScanSide = 11;

        /// <summary>Round-11 fixture surface height; the fixture was otherwise flat.</summary>
        private const int SurfaceZ = 3;

        /// <summary>Basic tier plot capacity: the round-11 <c>registeredNow</c> expectation.</summary>
        private const int BasicCapacity = 60;

        // ------------------------------------------------------------------ Core retention rule

        /// <summary>
        /// The round-11 state of a managed plot whose crop has grown: the cell is occupied by the
        /// farm's own crop, which is exactly what makes CanPlant/AnyObjectAt reject it. The
        /// retention rule must model that as "no disqualifier" — otherwise the registry is wiped.
        /// </summary>
        public static void TestRetentionKeepsRegisteredPlotOccupiedByItsOwnCrop()
        {
            var cropOccupiedPlot = default(PlotRetentionState);
            TestAssert.Equal(PlotDropReason.None, PlotRetention.Evaluate(cropOccupiedPlot));
            TestAssert.True(PlotRetention.ShouldKeep(cropOccupiedPlot),
                "a crop on its own registered plot must be retained, not dropped");
        }

        /// <summary>A road/building (non-crop block object) on the cell must remove the registration.</summary>
        public static void TestRetentionDropsPlotTakenOverByRoadOrBuilding()
        {
            var roadOverPlot = new PlotRetentionState { OccupiedByNonCrop = true };
            TestAssert.Equal(PlotDropReason.OccupiedByNonCrop, PlotRetention.Evaluate(roadOverPlot));
            TestAssert.False(PlotRetention.ShouldKeep(roadOverPlot),
                "a road/building on the cell must not be retained forever");
        }

        /// <summary>Every legitimate removal reason is recognized (terrain/range/exclusion/other farm).</summary>
        public static void TestRetentionDropsTerrainMissingOutOfRangeExcludedAndClaimedPlots()
        {
            TestAssert.Equal(PlotDropReason.TerrainMissing,
                PlotRetention.Evaluate(new PlotRetentionState { TerrainMissing = true }));
            TestAssert.Equal(PlotDropReason.OutOfRange,
                PlotRetention.Evaluate(new PlotRetentionState { OutOfRange = true }));
            TestAssert.Equal(PlotDropReason.ExcludedByPlayer,
                PlotRetention.Evaluate(new PlotRetentionState { ExcludedByPlayer = true }));
            TestAssert.Equal(PlotDropReason.ClaimedByOtherFarm,
                PlotRetention.Evaluate(new PlotRetentionState { ClaimedByOtherFarm = true }));
        }

        /// <summary>The drop-reason precedence is documented and deterministic (diagnostics contract).</summary>
        public static void TestRetentionPrecedenceIsDeterministic()
        {
            TestAssert.Equal(PlotDropReason.TerrainMissing, PlotRetention.Evaluate(new PlotRetentionState
            {
                TerrainMissing = true,
                OutOfRange = true,
                ExcludedByPlayer = true,
                ClaimedByOtherFarm = true,
                OccupiedByNonCrop = true,
            }));

            TestAssert.Equal(PlotDropReason.OutOfRange, PlotRetention.Evaluate(new PlotRetentionState
            {
                OutOfRange = true,
                ExcludedByPlayer = true,
                ClaimedByOtherFarm = true,
                OccupiedByNonCrop = true,
            }));

            TestAssert.Equal(PlotDropReason.ExcludedByPlayer, PlotRetention.Evaluate(new PlotRetentionState
            {
                ExcludedByPlayer = true,
                ClaimedByOtherFarm = true,
                OccupiedByNonCrop = true,
            }));

            TestAssert.Equal(PlotDropReason.ClaimedByOtherFarm, PlotRetention.Evaluate(new PlotRetentionState
            {
                ClaimedByOtherFarm = true,
                OccupiedByNonCrop = true,
            }));

            TestAssert.Equal(PlotDropReason.OccupiedByNonCrop,
                PlotRetention.Evaluate(new PlotRetentionState { OccupiedByNonCrop = true }));
        }

        // ------------------------------------------------------------------ batch retention invariant

        /// <summary>
        /// The card's central invariant: 60 previously registered plots + every one of them now
        /// carrying a crop → the batch retention rule still returns exactly those 60, in order.
        /// </summary>
        public static void TestRetainKeepsAll60CropOccupiedRegisteredPlots()
        {
            var previous = RegisteredScanIds(BasicCapacity);
            var observed = CropOccupiedObservations(previous);

            var retained = PlotRetention.Retain(previous, observed, BasicCapacity);

            TestAssert.Equal(BasicCapacity, retained.Length,
                "all 60 crop-occupied registered plots must be retained (round-11: 0/7)");
            AssertSameSequence(previous, retained, "retained ids must keep the registration order");
        }

        /// <summary>A road-occupied plot and a plot the scan no longer sees (out of range) are dropped.</summary>
        public static void TestRetainDropsRoadOccupiedAndUnobservedPlots()
        {
            var previous = RegisteredScanIds(BasicCapacity);
            var roadPlotId = previous[10];
            var outOfRangePlotId = previous[20];

            var observed = new List<PlotRetentionObservation>(previous.Count - 1);
            for (var i = 0; i < previous.Count; i++)
            {
                var plotId = previous[i];
                if (plotId == outOfRangePlotId)
                {
                    continue; // the scan never saw this cell: out of the work range
                }

                observed.Add(new PlotRetentionObservation(plotId,
                    plotId == roadPlotId
                        ? new PlotRetentionState { OccupiedByNonCrop = true }
                        : default));
            }

            var retained = PlotRetention.Retain(previous, observed, BasicCapacity);

            TestAssert.Equal(BasicCapacity - 2, retained.Length,
                "a road-occupied and an out-of-range plot must be dropped");
            TestAssert.False(Contains(retained, roadPlotId), "a road-occupied plot must be dropped");
            TestAssert.False(Contains(retained, outOfRangePlotId),
                "a registered plot outside the work range must be dropped");
        }

        /// <summary>Retention never exceeds the tier capacity and keeps the previous order.</summary>
        public static void TestRetainRespectsCapacityAndPreviousOrder()
        {
            var previous = RegisteredScanIds(BasicCapacity);
            var observed = CropOccupiedObservations(previous);

            var capped = PlotRetention.Retain(previous, observed, 50);
            TestAssert.Equal(50, capped.Length, "capacity 50 must cap the retained set at 50");
            for (var i = 0; i < capped.Length; i++)
            {
                TestAssert.Equal(previous[i], capped[i], "capped retention must keep previous order at index " + i);
            }

            TestAssert.Equal(BasicCapacity, PlotRetention.Retain(previous, observed, BasicCapacity).Length);
            TestAssert.Equal(0, PlotRetention.Retain(previous, observed, 0).Length,
                "capacity 0 must retain nothing");
            TestAssert.Equal(0, PlotRetention.Retain(null, observed, BasicCapacity).Length,
                "no previous registration means nothing to retain");
        }

        /// <summary>Cells that were not registered before are not "retained"; they are discovered as new cells.</summary>
        public static void TestRetainIgnoresCellsThatWereNeverRegistered()
        {
            var previous = RegisteredScanIds(3);
            var observed = CropOccupiedObservations(previous);
            observed.Add(new PlotRetentionObservation(ScanCellId(80), default));

            var retained = PlotRetention.Retain(previous, observed, BasicCapacity);

            TestAssert.Equal(3, retained.Length, "retention only covers previous registrations");
            AssertSameSequence(previous, retained, "retained order");
        }

        /// <summary>The batch form and the per-cell form of the rule can never disagree.</summary>
        public static void TestRetainBatchAgreesWithPerCellRuleForEveryReason()
        {
            var plotId = ScanCellId(0);
            var previous = new List<int> { plotId };
            var states = new[]
            {
                default(PlotRetentionState),
                new PlotRetentionState { TerrainMissing = true },
                new PlotRetentionState { OutOfRange = true },
                new PlotRetentionState { ExcludedByPlayer = true },
                new PlotRetentionState { ClaimedByOtherFarm = true },
                new PlotRetentionState { OccupiedByNonCrop = true },
            };

            foreach (var state in states)
            {
                var retained = PlotRetention.Retain(previous,
                    new[] { new PlotRetentionObservation(plotId, state) }, BasicCapacity);
                var expected = PlotRetention.ShouldKeep(state);
                TestAssert.Equal(expected ? 1 : 0, retained.Length,
                    "batch Retain disagrees with ShouldKeep for state " + state);
            }
        }

        // ------------------------------------------------------------------ scan selection delta (diag contract)

        /// <summary>
        /// <c>diag.scan.end kept= added= dropped=</c> is what proves the next in-game round.
        /// The counter must report the crop-occupied rescan as kept=60 added=0 dropped=0.
        /// </summary>
        public static void TestScanStatsRecordSelectionReportsKeptAddedDropped()
        {
            var previous = RegisteredScanIds(BasicCapacity);
            var stats = new PlotScanStats();

            stats.RecordSelection(previous, RegisteredScanIds(BasicCapacity));
            TestAssert.Equal(BasicCapacity, stats.Kept, "kept (identical rescan)");
            TestAssert.Equal(0, stats.Added, "added (identical rescan)");
            TestAssert.Equal(0, stats.Dropped, "dropped (identical rescan)");

            var selected = new List<int>();
            for (var i = 0; i < previous.Count - 2; i++)
            {
                selected.Add(previous[i]);
            }

            selected.Add(ScanCellId(100));
            selected.Add(ScanCellId(101));
            stats.RecordSelection(previous, selected);
            TestAssert.Equal(BasicCapacity - 2, stats.Kept, "kept (two replanted cells)");
            TestAssert.Equal(2, stats.Added, "added (two replanted cells)");
            TestAssert.Equal(2, stats.Dropped, "dropped (two replanted cells)");

            stats.Reset();
            TestAssert.Equal(0, stats.Kept, "Reset must clear kept");
            TestAssert.Equal(0, stats.Added, "Reset must clear added");
            TestAssert.Equal(0, stats.Dropped, "Reset must clear dropped");
        }

        /// <summary>
        /// The round-12 acceptance grep depends on the exact scan-end diagnostics fragment: the
        /// field names, their order and the invariant number format must never drift. This pins
        /// the Core-owned fragment <c>kept= added= dropped=</c> itself.
        /// </summary>
        public static void TestScanStatsDescribeSelectionDeltaPinsTheScanEndFragment()
        {
            var previous = RegisteredScanIds(BasicCapacity);
            var stats = new PlotScanStats();

            stats.RecordSelection(previous, RegisteredScanIds(BasicCapacity));
            TestAssert.Equal("kept=60 added=0 dropped=0", stats.DescribeSelectionDelta(),
                "round-12 acceptance grep fragment (all crop-occupied plots retained)");

            var selected = new List<int>();
            for (var i = 0; i < previous.Count - 2; i++)
            {
                selected.Add(previous[i]);
            }

            selected.Add(ScanCellId(100));
            selected.Add(ScanCellId(101));
            stats.RecordSelection(previous, selected);
            TestAssert.Equal("kept=58 added=2 dropped=2", stats.DescribeSelectionDelta(),
                "the fragment must report back-filled selections too");
        }

        // ------------------------------------------------------------------ scanner + selection integration

        /// <summary>
        /// The round-11 failure replayed end to end with the real production scanner and selector:
        /// 60 registered plots, all now crop-occupied, no other plantable cell in the range → the
        /// rescan must still produce the same 60 candidates and keep exactly those 60. The same
        /// fixture with the old "crop blocks the cell" rule produces 0 candidates / 0 selected,
        /// which is the <c>plot.scan-empty</c> signature of the round-11 log.
        /// </summary>
        public static void TestRescanWithAllCropsGrownStillSelectsTheSame60()
        {
            TestAssert.Equal(BasicCapacity, TierCatalog.Get(HdbfTier.Basic).PlotCapacity,
                "Basic tier plot capacity fixture (round-11 registeredNow)");

            var previous = RegisteredScanIds(BasicCapacity);
            var previousSet = new HashSet<int>(previous);

            // The other 61 in-range cells are not plantable either (round-11 day 5: every one of
            // the 121 cells was a field= or block= reject), so only retention can yield candidates.
            var fixedHost = new RetentionScanHost(previousSet, _ => false);
            var candidates = RunScan(fixedHost);

            TestAssert.Equal(BasicCapacity, candidates.Count,
                "a crop-occupied rescan must yield 60 candidates, not 0/7");
            var selected = PlotSelection.Select(candidates, BasicCapacity, previous);
            TestAssert.Equal(BasicCapacity, selected.Length, "selection must keep the full Basic capacity");
            AssertSameSequence(previous, selected, "registered plot ids must stay identical across the rescan");

            // Regression signature, for the record: the old rescan rule (no retention) sees only
            // "not plantable" cells and produces nothing — exactly round 11.
            var oldRuleHost = new RetentionScanHost(previousSet, _ => false, retentionEnabled: false);
            var oldCandidates = RunScan(oldRuleHost);
            TestAssert.Equal(0, oldCandidates.Count,
                "the pre-BUG-05 CanPlant/AnyObjectAt-only rule yields 0 candidates (round-11 defect)");
            TestAssert.Equal(0, PlotSelection.Select(oldCandidates, BasicCapacity, previous).Length,
                "the pre-BUG-05 rule yields registeredNow=0 (plot.scan-empty)");
        }

        /// <summary>
        /// A plot taken over by a road is dropped (never retained forever) and the freed capacity is
        /// back-filled with a genuinely plantable new cell: kept=59 added=1 dropped=1.
        /// </summary>
        public static void TestRescanDropsRoadPlotAndBackfillsFromNewCells()
        {
            var previous = RegisteredScanIds(BasicCapacity);
            var previousSet = new HashSet<int>(previous);
            var roadPlotId = previous[7];
            var newPlotId = ScanCellId(100); // first 60 registrations are scan indices 0..59

            var host = new RetentionScanHost(previousSet,
                cell => PlotCellCodec.Encode(cell.x, cell.y, cell.z) == newPlotId);
            host.SetState(roadPlotId, new PlotRetentionState { OccupiedByNonCrop = true });

            var candidates = RunScan(host);
            TestAssert.Equal(BasicCapacity, candidates.Count,
                "59 retained + 1 new plantable cell must be offered to the selector");

            var selected = PlotSelection.Select(candidates, BasicCapacity, previous);
            TestAssert.Equal(BasicCapacity, selected.Length, "the freed capacity must be back-filled");
            TestAssert.False(Contains(selected, roadPlotId), "the road-occupied plot must be dropped");
            TestAssert.True(Contains(selected, newPlotId), "the new plantable cell must fill the freed slot");

            var stats = new PlotScanStats();
            stats.RecordSelection(previous, selected);
            TestAssert.Equal(BasicCapacity - 1, stats.Kept, "kept (one road)");
            TestAssert.Equal(1, stats.Added, "added (one new cell)");
            TestAssert.Equal(1, stats.Dropped, "dropped (one road)");
        }

        /// <summary>
        /// A saved registration outside the current work range is simply not observed by the scan:
        /// the rescan keeps the other 59 and reports dropped=1 (never resurrects the stale id).
        /// </summary>
        public static void TestRescanDropsOutOfRangeRegistrationAndKeepsTheRest()
        {
            var previous = RegisteredScanIds(BasicCapacity - 1);
            var outOfRangePlotId = PlotCellCodec.Encode(500, 500, SurfaceZ);
            previous.Add(outOfRangePlotId);
            var previousSet = new HashSet<int>(previous);

            var host = new RetentionScanHost(previousSet, _ => false);
            var candidates = RunScan(host);
            TestAssert.Equal(BasicCapacity - 1, candidates.Count,
                "the out-of-range registration must not produce a candidate");

            var selected = PlotSelection.Select(candidates, BasicCapacity, previous);
            TestAssert.Equal(BasicCapacity - 1, selected.Length);
            TestAssert.False(Contains(selected, outOfRangePlotId), "out-of-range registration must be dropped");

            var stats = new PlotScanStats();
            stats.RecordSelection(previous, selected);
            TestAssert.Equal(BasicCapacity - 1, stats.Kept, "kept (out-of-range registration lost)");
            TestAssert.Equal(0, stats.Added, "added (nothing new in range)");
            TestAssert.Equal(1, stats.Dropped, "dropped (out-of-range registration)");
        }

        // ------------------------------------------------------------------ production source contract

        /// <summary>
        /// Anti-revert source contract for the production seam that L1 cannot compile: the
        /// controller partial references Timberborn types, so no game-free test can execute it.
        /// The two production files of the fix are embedded as text by HDBF.Tests.csproj and the
        /// ordering that makes the fix work is pinned here:
        ///
        /// * the previously-registered branch must run before the new-cell CanPlant / AnyObjectAt /
        ///   planting-blocker filters (deleting it is exactly the round-11 wipe);
        /// * the retained decision must come from the Core <see cref="PlotRetention"/> rule and the
        ///   crop probe must classify crops via <c>GetFirstObjectWithComponentAt&lt;Crop&gt;</c>
        ///   (a crop must not be mistaken for a road/building);
        /// * the kept/added/dropped delta must be recorded before ApplyPlotArrays rewrites the
        ///   record, and the scan-end line must print the four BUG-05 diagnostics fields.
        ///
        /// Proven necessary: removing the retained branch in a scratch copy leaves the build clean
        /// and every behaviour test green, but fails this test.
        /// </summary>
        public static void TestProductionRetentionSourceContractIsStillWired()
        {
            var scan = ReadEmbeddedSource("HDBF.Tests.Source.Scan.cs");
            var diagnostics = ReadEmbeddedSource("HDBF.Tests.Source.Diagnostics.cs");

            var retainedBranch = scan.IndexOf("_registeredWhenScanBegan.Contains(id)",
                StringComparison.Ordinal);
            TestAssert.True(retainedBranch >= 0,
                "Scan.cs must still branch on the per-scan registration snapshot (BUG-05 retained path)");

            var retainedHelper = scan.IndexOf("private bool TryAcceptRetainedPlot", StringComparison.Ordinal);
            TestAssert.True(retainedHelper > retainedBranch,
                "the registered branch must delegate to TryAcceptRetainedPlot");

            TestAssert.True(retainedBranch < scan.IndexOf(
                    "_plantingAreaValidator.CanPlant(cell, scanCrop)", StringComparison.Ordinal),
                "the retained branch must run before the new-cell CanPlant filter");
            TestAssert.True(retainedBranch < scan.IndexOf(
                    "_blockService.AnyObjectAt(cell)", StringComparison.Ordinal),
                "the retained branch must run before the new-cell AnyObjectAt filter");
            TestAssert.True(retainedBranch < scan.IndexOf(
                    "TryGetPlantingBlocker(cell", StringComparison.Ordinal),
                "the retained branch must run before the new-cell planting-blocker filter");

            TestAssert.True(scan.Contains("PlotRetention.ShouldKeep(state)", StringComparison.Ordinal),
                "the retained decision must come from the Core PlotRetention rule");
            TestAssert.True(scan.Contains("GetFirstObjectWithComponentAt<Crop>", StringComparison.Ordinal),
                "the non-crop probe must classify crops by their Crop component");

            var recorded = scan.IndexOf("_scanStats.RecordSelection(previousIds, selected)",
                StringComparison.Ordinal);
            var applied = scan.IndexOf("ApplyPlotArrays(granted)", StringComparison.Ordinal);
            TestAssert.True(recorded >= 0,
                "OnPlotsScanned must record the kept/added/dropped selection delta");
            TestAssert.True(recorded < applied,
                "the delta must be recorded before ApplyPlotArrays rewrites the record");

            TestAssert.True(diagnostics.Contains("coverageAfterScan=", StringComparison.Ordinal),
                "scan-end diagnostics must still print coverageAfterScan=");
            TestAssert.True(diagnostics.Contains("DescribeSelectionDelta", StringComparison.Ordinal),
                "scan-end diagnostics must print the Core-pinned kept=/added=/dropped= fragment");
        }

        private static string ReadEmbeddedSource(string logicalName)
        {
            var assembly = typeof(Bug05PlotRetentionTests).Assembly;
            using var stream = assembly.GetManifestResourceStream(logicalName);
            TestAssert.NotNull(stream, "embedded production source missing: " + logicalName);
            using var reader = new StreamReader(stream!);
            return reader.ReadToEnd();
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>Runs the real production scanner over the 11x11 range once and returns its candidates.</summary>
        private static List<PlotCandidate> RunScan(RetentionScanHost host)
        {
            var scanner = new HdbfPlotScanner(ScanSide);
            scanner.Begin(new Vector3Int(5, 5, SurfaceZ), ScanSide);
            scanner.Step(ScanSide * ScanSide + 1, host);
            TestAssert.NotNull(host.Candidates, "OnPlotsScanned must be called exactly once");
            return host.Candidates!;
        }

        /// <summary>The first <paramref name="count"/> scan cells (row-major, flat z = SurfaceZ).</summary>
        private static List<int> RegisteredScanIds(int count)
        {
            var ids = new List<int>(count);
            for (var index = 0; index < count; index++)
            {
                ids.Add(ScanCellId(index));
            }

            return ids;
        }

        private static int ScanCellId(int scanIndex)
        {
            return PlotCellCodec.Encode(scanIndex % ScanSide, scanIndex / ScanSide, SurfaceZ);
        }

        private static List<PlotRetentionObservation> CropOccupiedObservations(IReadOnlyList<int> plotIds)
        {
            var observed = new List<PlotRetentionObservation>(plotIds.Count);
            for (var i = 0; i < plotIds.Count; i++)
            {
                // default state == crop on the cell == no disqualifier (BUG-05)
                observed.Add(new PlotRetentionObservation(plotIds[i], default));
            }

            return observed;
        }

        private static bool Contains(IReadOnlyList<int> ids, int plotId)
        {
            for (var i = 0; i < ids.Count; i++)
            {
                if (ids[i] == plotId)
                {
                    return true;
                }
            }

            return false;
        }

        private static void AssertSameSequence(IReadOnlyList<int> expected, IReadOnlyList<int> actual,
            string what)
        {
            TestAssert.Equal(expected.Count, actual.Count, what + ": count");
            for (var i = 0; i < expected.Count; i++)
            {
                TestAssert.Equal(expected[i], actual[i], what + ": id at index " + i);
            }
        }

        // ------------------------------------------------------------------ game-free scan host

        /// <summary>
        /// Game-free <see cref="IHdbfPlotScanHost"/> double. It owns only the two host
        /// responsibilities (resolve the plant cell, accept/reject it) and maps a previously
        /// registered cell onto the same Core <see cref="PlotRetention"/> rule the controller
        /// applies, so the scanner/selection integration is exercised without any game assembly.
        /// Set <c>retentionEnabled: false</c> to replay the pre-BUG-05 rule ("a crop blocks the
        /// cell, so a registered crop-occupied cell is rejected like any occupied cell").
        /// </summary>
        private sealed class RetentionScanHost : IHdbfPlotScanHost
        {
            private readonly HashSet<int> _previouslyRegistered;
            private readonly Func<Vector3Int, bool> _isNewCellAccepted;
            private readonly bool _retentionEnabled;
            private readonly Dictionary<int, PlotRetentionState> _stateByPlot =
                new Dictionary<int, PlotRetentionState>();

            public RetentionScanHost(HashSet<int> previouslyRegistered,
                Func<Vector3Int, bool> isNewCellAccepted, bool retentionEnabled = true)
            {
                _previouslyRegistered = previouslyRegistered;
                _isNewCellAccepted = isNewCellAccepted;
                _retentionEnabled = retentionEnabled;
            }

            public List<PlotCandidate>? Candidates { get; private set; }

            public void SetState(int plotId, PlotRetentionState state)
            {
                _stateByPlot[plotId] = state;
            }

            public Vector3Int ResolvePlotCell(Vector3Int horizontalCell)
            {
                return new Vector3Int(horizontalCell.x, horizontalCell.y, SurfaceZ);
            }

            public bool TryAcceptPlot(Vector3Int plotCell, int distanceSquared)
            {
                var plotId = PlotCellCodec.Encode(plotCell.x, plotCell.y, plotCell.z);
                if (_previouslyRegistered.Contains(plotId))
                {
                    if (!_retentionEnabled)
                    {
                        return false;
                    }

                    var state = _stateByPlot.TryGetValue(plotId, out var found) ? found : default;
                    return PlotRetention.ShouldKeep(state);
                }

                return _isNewCellAccepted(plotCell);
            }

            public void OnPlotsScanned(IReadOnlyList<PlotCandidate> candidates, int deferredFrames)
            {
                Candidates = new List<PlotCandidate>(candidates);
            }
        }
    }
}
