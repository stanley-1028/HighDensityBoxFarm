using System;
using System.Collections.Generic;

namespace HDBF.Core.Plots
{
    /// <summary>
    /// Why a plot that a previous scan registered is <b>not</b> retained by the current rescan.
    /// The values are the documented precedence of <see cref="PlotRetention.Evaluate"/>; the
    /// same list is the only set of legitimate removal reasons for an already registered plot.
    /// </summary>
    public enum PlotDropReason
    {
        /// <summary>No disqualifier: the plot stays registered.</summary>
        None = 0,

        /// <summary>The terrain column/cell is gone, or the stored cell is no longer the surface cell.</summary>
        TerrainMissing = 1,

        /// <summary>The plot is no longer inside the building's work range (the scan never saw it).</summary>
        OutOfRange = 2,

        /// <summary>The player excluded the plot (REQ-IMPL-10).</summary>
        ExcludedByPlayer = 3,

        /// <summary>Another box farm already owns the plot (REQ-BLD-04, first come first served).</summary>
        ClaimedByOtherFarm = 4,

        /// <summary>
        /// A road, a building or another non-crop block object occupies the cell (a block object
        /// that has no crop component); the managed plot is gone for good.
        /// </summary>
        OccupiedByNonCrop = 5,
    }

    /// <summary>
    /// The disqualifiers a rescan can observe on one <b>previously registered</b> plot cell
    /// (BUG-05, P0; round-11 in-game log 2026-10-25).
    ///
    /// The important case is the default state — every flag false. It means the cell carries a
    /// growing/mature crop: the game's <c>PlantingAreaValidator.CanPlant</c> rejects it,
    /// <c>IBlockService.AnyObjectAt</c> sees the crop block object and the crop's own planting
    /// blocker exists, so every "new cell" filter the old rescan applied said "no". The round-11
    /// log is exactly that failure: <c>diag.scan.end</c> moved from
    /// <c>accepted=103 registeredNow=60</c> (fresh map) to <c>accepted=25</c>, then
    /// <c>accepted=0 registeredNow=0</c> and <c>plot.scan-empty</c> once the crops grew, so the
    /// farm had no managed plot left to harvest. A crop occupying its own plot is the expected
    /// state, not a disqualifier; only the flags below may remove the registration.
    /// </summary>
    public readonly struct PlotRetentionState
    {
        /// <summary>Terrain vanished, or the stored cell is no longer the terrain surface cell.</summary>
        public bool TerrainMissing { get; init; }

        /// <summary>The cell was not observed by the current scan range (removed from range).</summary>
        public bool OutOfRange { get; init; }

        /// <summary>The player excluded the plot from this farm (REQ-IMPL-10).</summary>
        public bool ExcludedByPlayer { get; init; }

        /// <summary>Another box farm owns the cell (REQ-BLD-04).</summary>
        public bool ClaimedByOtherFarm { get; init; }

        /// <summary>
        /// A non-crop block object (road/building/stockpile) occupies the cell. A crop object is
        /// <b>not</b> represented by this flag — that is the retain case.
        /// </summary>
        public bool OccupiedByNonCrop { get; init; }
    }

    /// <summary>
    /// One previously registered plot as observed by the current scan: the plot id plus the
    /// retention state of its cell. Only cells the scan actually visited can be observations; a
    /// registered id without an observation is out of range and is dropped by
    /// <see cref="PlotRetention.Retain"/>.
    /// </summary>
    public readonly struct PlotRetentionObservation
    {
        /// <summary>Creates one observation.</summary>
        public PlotRetentionObservation(int plotId, PlotRetentionState state)
        {
            PlotId = plotId;
            State = state;
        }

        /// <summary>Stable managed-plot id of the observed cell.</summary>
        public int PlotId { get; }

        /// <summary>Disqualifiers found on that cell.</summary>
        public PlotRetentionState State { get; }
    }

    /// <summary>
    /// The one rule that decides whether a rescan keeps a plot this farm already registered
    /// (BUG-05, chapter 11.3). It is pure Core logic so the in-game path and L1 tests share a
    /// single source:
    ///
    /// * a registered plot is kept unless one of <see cref="PlotRetentionState"/>'s flags holds;
    /// * a crop occupying the plot cell is the expected state and is not a flag;
    /// * a registered plot that the scan cannot observe (out of the work range) is dropped;
    /// * retention never exceeds the tier's plot capacity, and the previous registration order is
    ///   preserved (the later <see cref="PlotSelection"/> step keeps the same order, so plot ids
    ///   stay stable across rescans — REQ-IMPL-07).
    ///
    /// The gameplay controller applies <see cref="ShouldKeep"/> per scanned cell (streaming, with
    /// its game-service probes mapped onto the flags above); <see cref="Retain"/> is the batch
    /// form used to state and test the rescan invariant.
    /// </summary>
    public static class PlotRetention
    {
        /// <summary>
        /// Classifies one observed previously registered cell. The check order is the documented
        /// precedence (terrain gone wins over range/exclusion/claim/occupancy) and is stable so
        /// diagnostics are deterministic.
        /// </summary>
        public static PlotDropReason Evaluate(in PlotRetentionState state)
        {
            if (state.TerrainMissing)
            {
                return PlotDropReason.TerrainMissing;
            }

            if (state.OutOfRange)
            {
                return PlotDropReason.OutOfRange;
            }

            if (state.ExcludedByPlayer)
            {
                return PlotDropReason.ExcludedByPlayer;
            }

            if (state.ClaimedByOtherFarm)
            {
                return PlotDropReason.ClaimedByOtherFarm;
            }

            if (state.OccupiedByNonCrop)
            {
                return PlotDropReason.OccupiedByNonCrop;
            }

            return PlotDropReason.None;
        }

        /// <summary>True when the observed previously registered plot must stay registered.</summary>
        public static bool ShouldKeep(in PlotRetentionState state)
        {
            return Evaluate(state) == PlotDropReason.None;
        }

        /// <summary>
        /// Batch form of the retention rule: the previous registration list in, the ids to keep in
        /// the same order out.
        ///
        /// A previous id that is not present in <paramref name="observed"/> is treated as
        /// <see cref="PlotDropReason.OutOfRange"/> (the rescan did not see that cell anymore).
        /// The result is capped at <paramref name="capacity"/> and de-duplicated, so a rescan can
        /// never register more plots than the tier allows.
        /// </summary>
        /// <param name="previousIds">Ids registered before this scan, in registration order.</param>
        /// <param name="observed">Retention states of the previously registered cells the scan saw.</param>
        /// <param name="capacity">Maximum number of managed plots; values &lt;= 0 keep nothing.</param>
        public static int[] Retain(IReadOnlyList<int>? previousIds,
            IReadOnlyList<PlotRetentionObservation>? observed, int capacity)
        {
            if (previousIds is null || previousIds.Count == 0 || capacity <= 0)
            {
                return Array.Empty<int>();
            }

            if (observed is null || observed.Count == 0)
            {
                return Array.Empty<int>();
            }

            var stateById = new Dictionary<int, PlotRetentionState>(observed.Count);
            for (var i = 0; i < observed.Count; i++)
            {
                var observation = observed[i];
                if (!stateById.ContainsKey(observation.PlotId))
                {
                    stateById[observation.PlotId] = observation.State;
                }
            }

            var retained = new List<int>(Math.Min(previousIds.Count, capacity));
            var seen = new HashSet<int>();
            for (var i = 0; i < previousIds.Count && retained.Count < capacity; i++)
            {
                var plotId = previousIds[i];
                if (!seen.Add(plotId))
                {
                    continue;
                }

                if (!stateById.TryGetValue(plotId, out var state))
                {
                    // Not observed by this scan: out of the current work range.
                    continue;
                }

                if (ShouldKeep(state))
                {
                    retained.Add(plotId);
                }
            }

            return retained.ToArray();
        }
    }
}
