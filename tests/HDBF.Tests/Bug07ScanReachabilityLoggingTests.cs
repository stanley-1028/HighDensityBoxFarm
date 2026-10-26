using System;
using System.Collections.Generic;
using System.IO;
using HDBF.Core;
using HDBF.Core.Logging;
using HDBF.Core.Plots;

namespace HDBF.Tests
{
    /// <summary>
    /// BUG-07 L1 (game-free): round-28 in-game log 2026-10-25 showed the scan flooding the log with
    /// one <c>HDBF/Warn</c> under <c>compat.failed</c> — <i>including the full stack</i> — for every
    /// cell whose navigation query threw
    /// <c>InvalidOperationException: There's no cached flow field</c> (27x27 Advanced scans have
    /// hundreds of such cells). A path query is a recoverable per-cell filter, not a compatibility
    /// adapter failure, so the fix counts the throws in the Core <see cref="PlotScanStats"/> and
    /// assembles one throttled summary line per completed scan
    /// (<see cref="HdbfLogKeys.ScanReachability"/>).
    ///
    /// These tests pin the Core data/message contract (counters, first cell, exception type, no
    /// stack, stable key) and — because the controller partial references Timberborn and cannot be
    /// compiled into this assembly — the production seam through the source embedded by
    /// <c>HDBF.Tests.csproj</c>: no compat key, no per-cell logging in IsReachable, the reject
    /// semantics preserved, and the same-class retention-probe catch moved off the stack-bearing
    /// generic error. No Timberborn/Unity assembly is loaded at compile or run time (REQ-TEST-03).
    /// </summary>
    public static class Bug07ScanReachabilityLoggingTests
    {
        /// <summary>Round-28 building id (from the in-game log excerpt).</summary>
        private const string EntityId = "eed460d8-1111-2222-3333-444455556666";

        /// <summary>The engine exception that round 28 threw for a cell without a flow field.</summary>
        private const string FlowFieldException = "System.InvalidOperationException";

        // ------------------------------------------------------------------ counting + first cell

        public static void TestReachabilityFailuresAccumulateAndKeepTheFirstCell()
        {
            var stats = new PlotScanStats { Scanned = 729, Accepted = 12 };

            stats.RecordReachabilityFailure(10, 20, 3, FlowFieldException);
            stats.RecordReachabilityFailure(11, 20, 3, "System.Exception");
            stats.RecordReachabilityFailure(12, 20, 3, "System.Exception");

            TestAssert.Equal(3, stats.ReachFailures,
                "every failed per-cell query must be counted, not just the first");
            TestAssert.True(stats.HasReachFailures);
            TestAssert.Equal(10, stats.FirstReachFailureX, "the summary must identify the first failed cell");
            TestAssert.Equal(20, stats.FirstReachFailureY);
            TestAssert.Equal(3, stats.FirstReachFailureZ);
            TestAssert.Equal(FlowFieldException, stats.FirstReachFailureException,
                "the first failure's exception type is the one kept for the summary");
        }

        // ------------------------------------------------------------------ one summary line

        public static void TestManyFailuresAssembleExactlyOneSummaryLine()
        {
            var stats = new PlotScanStats { Scanned = 729, Accepted = 5 };
            for (var i = 0; i < 200; i++)
            {
                stats.RecordReachabilityFailure(i - 100, 7, 4, FlowFieldException);
            }

            var summary = stats.DescribeReachabilityFailures(EntityId);

            TestAssert.True(summary.IndexOf('\n') < 0, "the summary must be a single line");
            TestAssert.True(summary.IndexOf('\r') < 0, "the summary must be a single line");
            TestAssert.True(summary.Contains("reachFailures=200", StringComparison.Ordinal), summary);
            TestAssert.True(summary.Contains("firstFailureCell=(-100,7,4)", StringComparison.Ordinal), summary);
            TestAssert.True(summary.Contains("exception=" + FlowFieldException, StringComparison.Ordinal), summary);
            TestAssert.Equal(1, CountOccurrences(summary, "firstFailureCell="),
                "the line must carry exactly one first-failure cell, not a list");
        }

        public static void TestSummaryCarriesTheMandatedFields()
        {
            var stats = new PlotScanStats { Scanned = 729, Accepted = 7, ReachabilityRejects = 30 };
            stats.RecordReachabilityFailure(118, 42, 3, FlowFieldException);
            stats.RecordReachabilityFailure(119, 42, 3, FlowFieldException);

            var summary = stats.DescribeReachabilityFailures(EntityId);

            foreach (var field in new[]
                     {
                         "entityId=" + EntityId,
                         "scanned=729",
                         "reachFailures=2",
                         "accepted=7",
                         "firstFailureCell=(118,42,3)",
                         "exception=" + FlowFieldException,
                     })
            {
                TestAssert.True(summary.Contains(field, StringComparison.Ordinal),
                    "summary must contain '" + field + "': " + summary);
            }
        }

        public static void TestSummaryHasNoStackAndNoCompatClassification()
        {
            var stats = new PlotScanStats { Scanned = 1 };
            stats.RecordReachabilityFailure(0, 0, 3, FlowFieldException);

            var summary = stats.DescribeReachabilityFailures(EntityId);

            TestAssert.False(summary.Contains("stack", StringComparison.OrdinalIgnoreCase),
                "the per-scan summary must never carry a stack trace: " + summary);
            TestAssert.False(summary.Contains(" at HDBF", StringComparison.Ordinal),
                "no stack frame text: " + summary);
            TestAssert.False(summary.Contains("compat.", StringComparison.Ordinal),
                "a path query failure is not a compatibility event: " + summary);
        }

        public static void TestNoFailureHasNoSummaryKey()
        {
            var stats = new PlotScanStats { Scanned = 121, Accepted = 60 };

            TestAssert.False(stats.HasReachFailures);
            TestAssert.Null(stats.ReachabilityFailureKey(),
                "a scan without reachability query failures must not emit the summary");
        }

        public static void TestFailureKeyIsTheNewGameplayKey()
        {
            var stats = new PlotScanStats();
            stats.RecordReachabilityFailure(0, 0, 0, "System.Exception");

            TestAssert.Equal(HdbfLogKeys.ScanReachability, stats.ReachabilityFailureKey());
        }

        public static void TestResetClearsTheReachabilityFailureState()
        {
            var stats = new PlotScanStats { Scanned = 729 };
            stats.RecordReachabilityFailure(1, 2, 3, FlowFieldException);

            stats.Reset();

            TestAssert.Equal(0, stats.ReachFailures);
            TestAssert.False(stats.HasReachFailures);
            TestAssert.Null(stats.ReachabilityFailureKey());
            TestAssert.Null(stats.FirstReachFailureException);
            TestAssert.Equal(0, stats.FirstReachFailureX);
        }

        public static void TestSummaryUsesInvariantCoordinatesForNegativeCells()
        {
            // A "-100" formatted with a grouping culture would corrupt the greppable contract.
            var stats = new PlotScanStats();
            stats.RecordReachabilityFailure(-1024, 1023, 0, FlowFieldException);

            var summary = stats.DescribeReachabilityFailures(EntityId);
            TestAssert.True(summary.Contains("firstFailureCell=(-1024,1023,0)", StringComparison.Ordinal),
                summary);
        }

        public static void TestReachFailureCounterIsSeparateFromTheRejectCounters()
        {
            // The gameplay caller still counts a failed query as a reachability reject (the cell is
            // excluded); the stats object only accumulates the throw so "reachFailures" and the
            // registration-warning "reach=" reject counter cannot be confused.
            var stats = new PlotScanStats { Scanned = 121, Accepted = 3 };
            stats.RecordReachabilityFailure(5, 5, 3, FlowFieldException);

            TestAssert.Equal(0, stats.ReachabilityRejects);
            TestAssert.Equal(0, stats.TotalRejects);
            TestAssert.Equal(1, stats.ReachFailures);
        }

        // ------------------------------------------------------------------ throttled emission

        public static void TestOneThrottledSummaryLineReachesTheLogForManyCellFailures()
        {
            var emitted = new List<string>();
            var log = new ThrottledLog((_, text) => emitted.Add(text), () => 0d);
            var stats = new PlotScanStats { Scanned = 729 };

            // The per-cell path only counts; 200 failed cells must never call the log.
            for (var i = 0; i < 200; i++)
            {
                stats.RecordReachabilityFailure(i, 0, 3, FlowFieldException);
            }

            TestAssert.Equal(0, emitted.Count, "the per-cell path must not log");

            // The scan-end path emits exactly once, under the per-building key.
            var key = stats.ReachabilityFailureKey();
            TestAssert.NotNull(key);
            log.Write(HdbfLogCategory.Warn, key + "." + EntityId,
                stats.DescribeReachabilityFailures(EntityId));

            TestAssert.Equal(1, emitted.Count, "200 failed cells must reach the log as one line");
            var line = emitted[0];
            TestAssert.True(line.StartsWith("gameplay.scan-reachability." + EntityId + ": ",
                StringComparison.Ordinal), "the throttle key must be the new per-building gameplay key: " + line);
            TestAssert.True(line.Contains("reachFailures=200", StringComparison.Ordinal), line);
            TestAssert.True(line.Contains("scanned=729", StringComparison.Ordinal), line);
            TestAssert.False(line.Contains("stack=", StringComparison.Ordinal),
                "the emitted line must not attach a stack: " + line);
        }

        // ------------------------------------------------------------------ stable log keys

        public static void TestScanLogKeysAreStableAndNotCompatKeys()
        {
            // Log keys are the throttle identity and are grepped by the next in-game round; they
            // are part of the diagnostics contract and must not be renamed after release.
            TestAssert.Equal("gameplay.scan-reachability", HdbfLogKeys.ScanReachability);
            TestAssert.Equal("gameplay.scan-retention-probe", HdbfLogKeys.ScanRetentionProbe);
            TestAssert.True(HdbfLogKeys.ScanReachability.StartsWith("gameplay.", StringComparison.Ordinal),
                "a baseline-scan event belongs to the gameplay namespace");
            TestAssert.False(string.Equals(HdbfLogKeys.ScanReachability, HdbfLogKeys.CompatFailed,
                    StringComparison.Ordinal),
                "the scan must not reuse the compatibility-adapter failure key");
            TestAssert.False(string.Equals(HdbfLogKeys.ScanRetentionProbe, HdbfLogKeys.CompatFailed,
                    StringComparison.Ordinal),
                "the retention probe must not reuse the compatibility-adapter failure key");
            TestAssert.False(string.Equals(HdbfLogKeys.ScanReachability, HdbfLogKeys.PlotScanReachability,
                    StringComparison.Ordinal),
                "the query-failure summary and the starved-registry warning must stay distinct keys");
        }

        // ------------------------------------------------------------------ production source contract

        /// <summary>
        /// The controller partial cannot be compiled into this game-free assembly, so the fix is
        /// pinned against the embedded production source. Fails if the round-28 behaviour is put
        /// back, even though every other L1 test and the build would stay green.
        /// </summary>
        public static void TestProductionScanSourceKeepsTheFixWired()
        {
            var scan = ReadEmbeddedSource("HDBF.Tests.Source.Scan.cs");

            // (1) No compatibility-adapter classification on the scan path.
            TestAssert.False(scan.Contains("HdbfLogKeys.CompatFailed", StringComparison.Ordinal),
                "Scan.cs must not use the compat adapter failure key; a path query is not a compat event");

            // (2) The per-cell catch only counts; the single summary is emitted once at scan end.
            TestAssert.True(scan.Contains("_scanStats.RecordReachabilityFailure(", StringComparison.Ordinal),
                "the IsReachable catch must count the failure in PlotScanStats");
            TestAssert.True(scan.Contains("_scanStats.DescribeReachabilityFailures(", StringComparison.Ordinal),
                "the scan end must emit the Core-assembled summary");

            var scanEnd = scan.IndexOf("public void OnPlotsScanned", StringComparison.Ordinal);
            var applyArrays = scan.IndexOf("private void ApplyPlotArrays", StringComparison.Ordinal);
            TestAssert.True(scanEnd >= 0 && applyArrays > scanEnd, "OnPlotsScanned body not found");
            var scanEndRegion = scan.Substring(scanEnd, applyArrays - scanEnd);
            TestAssert.Equal(1, CountOccurrences(scanEndRegion, "WarnIfScanQueriesFailed()"),
                "the summary must be emitted exactly once per completed scan");
            TestAssert.True(scanEndRegion.Contains("WarnIfRegistrationLooksBroken()", StringComparison.Ordinal),
                "the existing registration warning must remain");

            // (3) IsReachable itself never logs per cell (round 28: one full-stack Warn per cell).
            var reachStart = scan.IndexOf("private bool IsReachable(", StringComparison.Ordinal);
            var reachEnd = scan.IndexOf("private Accessible? ResolveAccessible(", StringComparison.Ordinal);
            TestAssert.True(reachStart >= 0 && reachEnd > reachStart, "IsReachable method not found");
            var reachRegion = scan.Substring(reachStart, reachEnd - reachStart);
            TestAssert.True(reachRegion.Contains("RecordReachabilityFailure", StringComparison.Ordinal),
                "the IsReachable catch must record the failure");
            TestAssert.False(reachRegion.Contains("_log.", StringComparison.Ordinal),
                "IsReachable must not emit one log line per failed cell");

            // (4) A failed query still makes the cell unreachable and excluded from the candidates.
            var guard = scan.IndexOf("if (!IsReachable(cell))", StringComparison.Ordinal);
            var reject = scan.IndexOf("_scanStats.ReachabilityRejects++;", StringComparison.Ordinal);
            TestAssert.True(guard >= 0 && reject > guard,
                "a failed reachability query must still count as a reachability reject");
            TestAssert.True(scan.IndexOf("return false;", reject, StringComparison.Ordinal) > reject,
                "a failed reachability query must still exclude the cell from the candidates");

            // (5) Same-class per-cell catch on the retained path: dedicated stack-free key.
            var retentionStart = scan.IndexOf("private bool IsOccupiedByNonCrop(", StringComparison.Ordinal);
            TestAssert.True(retentionStart >= 0 && reachStart > retentionStart,
                "IsOccupiedByNonCrop must stay in front of IsReachable");
            var retentionRegion = scan.Substring(retentionStart, reachStart - retentionStart);
            TestAssert.True(retentionRegion.Contains("HdbfLogKeys.ScanRetentionProbe", StringComparison.Ordinal),
                "the retention probe failure must use its own scan key");
            TestAssert.False(retentionRegion.Contains("_log.Error(", StringComparison.Ordinal),
                "the retention probe failure must not attach a stack");
        }

        // ------------------------------------------------------------------ helpers

        private static int CountOccurrences(string text, string needle)
        {
            var count = 0;
            var index = 0;
            while ((index = text.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += needle.Length;
            }

            return count;
        }

        private static string ReadEmbeddedSource(string logicalName)
        {
            var assembly = typeof(Bug07ScanReachabilityLoggingTests).Assembly;
            using var stream = assembly.GetManifestResourceStream(logicalName);
            TestAssert.NotNull(stream, "embedded production source missing: " + logicalName);
            using var reader = new StreamReader(stream!);
            return reader.ReadToEnd();
        }
    }
}
