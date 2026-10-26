using System.Collections.Generic;
using System.Globalization;
using HDBF.Core.Logging;

namespace HDBF.Core.Plots
{
    /// <summary>
    /// Per-scan counters of one managed-plot registration pass (chapter 11.3, REQ-IMPL-09).
    ///
    /// The P0 symptom "coverage is always 0 %" is invisible when the registry is empty: the panel
    /// only shows the worker-derived effective plot count. These counters are the data behind the
    /// player-visible <c>HDBF/Warn</c> line that makes an empty or reachability-starved registry
    /// visible and classifies why the cells were rejected. They are production state (not part of
    /// the temporary in-game diagnostics file) and are reset at the start of every scan.
    ///
    /// BUG-05 adds the selection delta (<see cref="Kept"/>/<see cref="Added"/>/<see cref="Dropped"/>):
    /// the round-11 log could only show the registry shrinking (60 → 25 → 0); with this delta the
    /// next in-game round proves directly that a rescan over crop-occupied plots keeps the same 60
    /// (<c>kept=60 added=0 dropped=0</c>).
    ///
    /// BUG-07 adds the reachability-query failure accumulation (<see cref="ReachFailures"/>,
    /// <see cref="RecordReachabilityFailure"/>, <see cref="DescribeReachabilityFailures"/>): the
    /// round-28 log wrote one <c>compat.failed</c> warning with a full stack per cell whose
    /// navigation query threw. Accumulating here keeps the per-cell decision (unreachable) and
    /// turns the reporting into one stable summary line per completed scan.
    ///
    /// <see cref="ShouldWarnAboutRegistration"/> is the decision rule; <see cref="WarningKey"/>
    /// chooses the throttle key and <see cref="Describe"/> the stable message. Keeping all three
    /// in Core makes the rule unit-testable without a game session (REQ-TEST-03).
    /// </summary>
    public sealed class PlotScanStats
    {
        /// <summary>Cells the scanner visited in the work range.</summary>
        public int Scanned { get; set; }

        /// <summary>Cells that passed every filter and became managed plots.</summary>
        public int Accepted { get; set; }

        /// <summary>Rejected: outside the terrain column map or not standing on the surface.</summary>
        public int TerrainRejects { get; set; }

        /// <summary>Rejected: <c>PlantingAreaValidator.CanPlant</c> said no (or no crop template).</summary>
        public int FieldRejects { get; set; }

        /// <summary>Rejected: a block object occupies the cell.</summary>
        public int BlockRejects { get; set; }

        /// <summary>Rejected: an existing planting blocker (e.g. a path) sits on the cell.</summary>
        public int BlockerRejects { get; set; }

        /// <summary>Rejected: navigation could not reach the cell from the entrance.</summary>
        public int ReachabilityRejects { get; set; }

        /// <summary>Rejected: the player excluded the cell (REQ-IMPL-10).</summary>
        public int Excluded { get; set; }

        /// <summary>Rejected: another box farm registered the cell first (REQ-BLD-04).</summary>
        public int Claimed { get; set; }

        /// <summary>Sum of all reject categories.</summary>
        public int TotalRejects =>
            TerrainRejects + FieldRejects + BlockRejects + BlockerRejects
            + ReachabilityRejects + Excluded + Claimed;

        // ------------------------------------------------------------------ scan query failures (BUG-07)

        /// <summary>
        /// Per-cell reachability queries that threw during this scan (BUG-07; round-28 in-game
        /// log: <c>InvalidOperationException: There's no cached flow field at 1187834</c>). The
        /// cell is still treated as unreachable — <see cref="ReachabilityRejects"/> counts it too,
        /// so the reachability decision semantics are unchanged — but the throw is accumulated
        /// here and reported once per completed scan instead of one full-stack warning per cell.
        /// </summary>
        public int ReachFailures { get; private set; }

        /// <summary>X coordinate of the first cell whose reachability query threw this scan.</summary>
        public int FirstReachFailureX { get; private set; }

        /// <summary>Y coordinate of the first cell whose reachability query threw this scan.</summary>
        public int FirstReachFailureY { get; private set; }

        /// <summary>Z (vertical) coordinate of the first cell whose reachability query threw.</summary>
        public int FirstReachFailureZ { get; private set; }

        /// <summary>
        /// Full type name of the first failed query's exception, or null when no query failed.
        /// Type only, never a stack: a path query is a recoverable per-cell filter condition.
        /// </summary>
        public string? FirstReachFailureException { get; private set; }

        /// <summary>True when at least one per-cell reachability query threw during this scan.</summary>
        public bool HasReachFailures => ReachFailures > 0;

        /// <summary>
        /// Records one failed per-cell reachability query. Only the first failure of the scan keeps
        /// its cell/exception: the log contract is one summary line per scan, so the counters are
        /// cumulative while the location is the first occurrence.
        /// </summary>
        public void RecordReachabilityFailure(int x, int y, int z, string? exceptionTypeName)
        {
            if (ReachFailures == 0)
            {
                FirstReachFailureX = x;
                FirstReachFailureY = y;
                FirstReachFailureZ = z;
                FirstReachFailureException = exceptionTypeName;
            }

            ReachFailures++;
        }

        /// <summary>
        /// Throttle key of the scan reachability-failure summary, or null when no query failed.
        /// The gameplay caller appends the building entity id to the key, so the 30 s throttle is
        /// per building; combined with emitting only at scan end this gives at most one line per
        /// completed scan and never one line per cell.
        /// </summary>
        public string? ReachabilityFailureKey()
        {
            return ReachFailures > 0 ? HdbfLogKeys.ScanReachability : null;
        }

        /// <summary>
        /// Stable, single-line summary of this scan's reachability-query failures (BUG-07
        /// diagnostics contract). It carries the entity id, the cumulative counters, the first
        /// failed cell and the exception type — deliberately never a stack trace: round 28 wrote
        /// one <c>compat.failed</c> warning <i>with the full stack</i> for every such cell, which
        /// flooded the log and read as a compatibility break.
        /// </summary>
        public string DescribeReachabilityFailures(string entityId)
        {
            return "plot reachability queries failed; failed cells are excluded from this scan"
                   + "; entityId=" + (string.IsNullOrEmpty(entityId) ? "<unknown>" : entityId)
                   + " scanned=" + Scanned.ToString(CultureInfo.InvariantCulture)
                   + " reachFailures=" + ReachFailures.ToString(CultureInfo.InvariantCulture)
                   + " accepted=" + Accepted.ToString(CultureInfo.InvariantCulture)
                   + " firstFailureCell=(" + FirstReachFailureX.ToString(CultureInfo.InvariantCulture)
                   + "," + FirstReachFailureY.ToString(CultureInfo.InvariantCulture)
                   + "," + FirstReachFailureZ.ToString(CultureInfo.InvariantCulture) + ")"
                   + " exception=" + (string.IsNullOrEmpty(FirstReachFailureException)
                       ? "<unknown>"
                       : FirstReachFailureException);
        }

        // ------------------------------------------------------------------ retention delta (BUG-05)

        /// <summary>
        /// Previously registered plots that the completed selection still keeps (BUG-05). These are
        /// the plots whose crop occupied the cell during the rescan and which therefore had to be
        /// admitted by the retention rule, not by <c>CanPlant</c>.
        /// </summary>
        public int Kept { get; private set; }

        /// <summary>Ids of the completed selection that were not registered before this scan.</summary>
        public int Added { get; private set; }

        /// <summary>Previously registered plots the completed selection did not keep.</summary>
        public int Dropped { get; private set; }

        /// <summary>
        /// Records the kept/added/dropped delta of one completed plot selection. Must be called
        /// with the previous registration array and the selected array of the same scan (the
        /// controller calls it before <c>ApplyPlotArrays</c> replaces the record, so the numbers
        /// describe a single selection). Selectors return distinct ids; duplicates in either input
        /// are de-duplicated so the counters can never over-report.
        /// </summary>
        public void RecordSelection(IReadOnlyList<int>? previousIds, IReadOnlyList<int>? selectedIds)
        {
            Kept = 0;
            Added = 0;
            Dropped = 0;

            var selected = new HashSet<int>();
            if (selectedIds is not null)
            {
                for (var i = 0; i < selectedIds.Count; i++)
                {
                    selected.Add(selectedIds[i]);
                }
            }

            if (previousIds is not null)
            {
                var seen = new HashSet<int>();
                for (var i = 0; i < previousIds.Count; i++)
                {
                    var plotId = previousIds[i];
                    if (!seen.Add(plotId))
                    {
                        continue;
                    }

                    if (selected.Contains(plotId))
                    {
                        Kept++;
                    }
                    else
                    {
                        Dropped++;
                    }
                }
            }

            Added = selected.Count - Kept;
        }

        /// <summary>
        /// Stable diagnostics fragment <c>kept=&lt;n&gt; added=&lt;n&gt; dropped=&lt;n&gt;</c> of the
        /// last completed selection (BUG-05). The scan-end diagnostics line (and the round-12
        /// acceptance grep) depend on these exact field names/order, so the fragment lives in Core
        /// with invariant number formatting and is pinned by an L1 test instead of being assembled
        /// inline in the temporary diagnostics file.
        /// </summary>
        public string DescribeSelectionDelta()
        {
            return "kept=" + Kept.ToString(CultureInfo.InvariantCulture)
                   + " added=" + Added.ToString(CultureInfo.InvariantCulture)
                   + " dropped=" + Dropped.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>Clears every counter; called when a new scan begins.</summary>
        public void Reset()
        {
            Scanned = 0;
            Accepted = 0;
            TerrainRejects = 0;
            FieldRejects = 0;
            BlockRejects = 0;
            BlockerRejects = 0;
            ReachabilityRejects = 0;
            Excluded = 0;
            Claimed = 0;
            ReachFailures = 0;
            FirstReachFailureX = 0;
            FirstReachFailureY = 0;
            FirstReachFailureZ = 0;
            FirstReachFailureException = null;
            Kept = 0;
            Added = 0;
            Dropped = 0;
        }

        /// <summary>
        /// True when the scan outcome needs a player-visible warning: nothing is registered at all
        /// (the farm can never irrigate) or the reachability filter rejected more cells than the
        /// scan accepted (the farm is registered but starved, the most likely in-game failure).
        /// </summary>
        public bool ShouldWarnAboutRegistration(int registeredPlots)
        {
            return WarningKey(registeredPlots) is not null;
        }

        /// <summary>
        /// Throttle key of the warning for this outcome, or null when no warning is needed.
        /// Keys are stable support/diagnostics identifiers (<see cref="HdbfLogKeys"/>).
        /// </summary>
        public string? WarningKey(int registeredPlots)
        {
            if (registeredPlots <= 0)
            {
                return HdbfLogKeys.PlotScanEmpty;
            }

            return ReachabilityRejects > Accepted ? HdbfLogKeys.PlotScanReachability : null;
        }

        /// <summary>
        /// Stable, single-line message with the entity id and the full reject breakdown. The format
        /// is part of the diagnostics contract (the next in-game round greps for these fields), so
        /// changes here are deliberate and covered by tests.
        /// </summary>
        public string Describe(int registeredPlots, string entityId)
        {
            var reason = registeredPlots <= 0
                ? "no usable managed plots were registered; irrigation can never be non-zero"
                : "reachability rejected more cells than the scan accepted; the farm is plot-starved";
            return reason
                   + "; entityId=" + (string.IsNullOrEmpty(entityId) ? "<unknown>" : entityId)
                   + " registered=" + registeredPlots
                   + " scanned=" + Scanned
                   + " accepted=" + Accepted
                   + " rejects[terrain=" + TerrainRejects
                   + ",field=" + FieldRejects
                   + ",block=" + BlockRejects
                   + ",blocker=" + BlockerRejects
                   + ",reach=" + ReachabilityRejects
                   + ",excluded=" + Excluded
                   + ",claimed=" + Claimed + "]";
        }
    }
}
