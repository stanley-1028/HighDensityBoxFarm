using System;
using System.Collections.Generic;
using HDBF.Core.Abstractions;

namespace HDBF.Core.Plots
{
    /// <summary>
    /// Maps one observed plot cell onto the four overlay states of the managed-plot highlight
    /// (REQ-UI-05, chapter 14.3). Pure Core so the renderer, gameplay probes and L1 tests share one
    /// decision (REQ-TEST-03); the state colour mapping lives in
    /// <see cref="HdbfPlotHighlightPalette"/>.
    ///
    /// Classification precedence (highest first):
    /// * <see cref="HdbfPlotHighlightState.Excluded"/> — the player excluded the plot, the overlay
    ///   keeps showing it gray so the exclusion is visible;
    /// * <see cref="HdbfPlotHighlightState.Unreachable"/> — the optional chapter-11.3 diagnostic
    ///   (a plantable cell the workers cannot reach) wins over the registered colour;
    /// * a registered plot is <see cref="HdbfPlotHighlightState.Irrigated"/> when the soil moisture
    ///   probe reports moisture, otherwise <see cref="HdbfPlotHighlightState.Dry"/>;
    /// * a cell that is neither registered, excluded nor unreachable has nothing to paint.
    /// </summary>
    public static class PlotHighlightClassifier
    {
        /// <summary>
        /// Classifies one cell for the overlay. Returns <c>false</c> (with <paramref name="state"/>
        /// set to its default value) when the cell is neither registered, excluded nor unreachable,
        /// i.e. when there is nothing to paint. On <c>true</c> the caller paints
        /// <paramref name="state"/> with <see cref="HdbfPlotHighlightPalette.Rgba"/>.
        /// </summary>
        /// <param name="isRegistered">True when the cell is one of the farm's managed plots.</param>
        /// <param name="isExcluded">True when the player excluded the plot (REQ-IMPL-10).</param>
        /// <param name="isIrrigated">True when the plot cell's soil moisture is present.</param>
        /// <param name="isUnreachable">True for the optional chapter-11.3 unreachable diagnostic.</param>
        /// <param name="state">The state to paint when the method returns <c>true</c>.</param>
        public static bool TryClassify(bool isRegistered, bool isExcluded, bool isIrrigated,
            bool isUnreachable, out HdbfPlotHighlightState state)
        {
            // Excluded wins: the player must always see the gray exclusion marker, even when the
            // cell is also unreachable or registered (chapter 14.3).
            if (isExcluded)
            {
                state = HdbfPlotHighlightState.Excluded;
                return true;
            }

            // The optional unreachable diagnostic wins over the registered colour.
            if (isUnreachable)
            {
                state = HdbfPlotHighlightState.Unreachable;
                return true;
            }

            // Only a registered cell is painted green/yellow; moisture on an unregistered cell
            // says nothing about this farm's managed plots.
            if (isRegistered)
            {
                state = isIrrigated ? HdbfPlotHighlightState.Irrigated : HdbfPlotHighlightState.Dry;
                return true;
            }

            state = default;
            return false;
        }
    }

    /// <summary>
    /// Box selection of registered managed plots for the overlay (REQ-UI-05, chapter 14.3): the
    /// player drags a rectangle over the world and every managed plot inside it is selected for
    /// exclusion/restore. Pure Core so the drag-to-ids rule is deterministic and testable without
    /// Unity (REQ-TEST-03); the overlay only converts the world drag into the two corner cells and
    /// feeds back the batch of plot ids.
    /// </summary>
    public static class PlotBoxSelection
    {
        /// <summary>
        /// Returns the distinct plot ids of the managed cells inside the box.
        ///
        /// The rectangle is built from the two drag corners and normalized, so a drag in any
        /// direction selects the same cells; both edges are inclusive. Only cells on height
        /// <paramref name="z"/> whose state is <see cref="HdbfPlotHighlightState.Irrigated"/> or
        /// <see cref="HdbfPlotHighlightState.Dry"/> are selected: an already
        /// <see cref="HdbfPlotHighlightState.Excluded"/> cell is not re-excluded, and an
        /// <see cref="HdbfPlotHighlightState.Unreachable"/> cell is not a registered plot. The
        /// result is sorted ascending so a batch update is deterministic across frames.
        /// Never returns <c>null</c>; an empty or null cell list returns an empty list.
        /// </summary>
        /// <param name="cells">Current overlay cells (registered plots plus optional exclusions).</param>
        /// <param name="cornerX">Drag start X, or the opposite corner (order does not matter).</param>
        /// <param name="cornerY">Drag start Y, or the opposite corner (order does not matter).</param>
        /// <param name="oppositeX">Drag end X (inclusive).</param>
        /// <param name="oppositeY">Drag end Y (inclusive).</param>
        /// <param name="z">The plant-cell height the drag applies to.</param>
        public static IReadOnlyList<int> RegisteredCellsInBox(IReadOnlyList<PlotHighlightCell>? cells,
            int cornerX, int cornerY, int oppositeX, int oppositeY, int z)
        {
            if (cells is null || cells.Count == 0)
            {
                return Array.Empty<int>();
            }

            // Normalize the rectangle: a drag from any corner covers the same inclusive cell range.
            var minX = cornerX < oppositeX ? cornerX : oppositeX;
            var maxX = cornerX < oppositeX ? oppositeX : cornerX;
            var minY = cornerY < oppositeY ? cornerY : oppositeY;
            var maxY = cornerY < oppositeY ? oppositeY : cornerY;

            var selected = new List<int>();
            var seen = new HashSet<int>();
            for (var i = 0; i < cells.Count; i++)
            {
                var cell = cells[i];
                if (cell.Z != z || cell.X < minX || cell.X > maxX || cell.Y < minY || cell.Y > maxY)
                {
                    continue;
                }

                // Excluded cells are already out of management and unreachable cells are not
                // registered, so neither may enter an exclusion batch.
                if (cell.State != HdbfPlotHighlightState.Irrigated
                    && cell.State != HdbfPlotHighlightState.Dry)
                {
                    continue;
                }

                if (seen.Add(cell.PlotId))
                {
                    selected.Add(cell.PlotId);
                }
            }

            // Deterministic batch order: the renderer/gameplay must not depend on dictionary order.
            selected.Sort();
            return selected.ToArray();
        }
    }

    /// <summary>
    /// Outcome of <see cref="PlotExclusionPolicy.Plan"/> for one box-exclusion request
    /// (REQ-UI-05, chapter 14.3). Immutable, so the caller applies exactly the plan it validated.
    /// </summary>
    public readonly struct PlotExclusionPlan
    {
        /// <summary>Creates a plan; null id lists are normalized to empty lists.</summary>
        public PlotExclusionPlan(IReadOnlyList<int>? excludedPlotIds, bool refusedByMinimum,
            int remainingPlots, int requestedCount)
        {
            ExcludedPlotIds = excludedPlotIds ?? Array.Empty<int>();
            RefusedByMinimum = refusedByMinimum;
            RemainingPlots = remainingPlots;
            RequestedCount = requestedCount;
        }

        /// <summary>
        /// The distinct registered plot ids the request actually excludes, sorted ascending. Empty
        /// when nothing is excluded <b>and</b> when the whole request is refused.
        /// </summary>
        public IReadOnlyList<int> ExcludedPlotIds { get; }

        /// <summary>
        /// True when applying the request would leave the farm with fewer than one registered plot,
        /// so the whole request is refused and nothing is excluded (REQ-UI-05 "last plot"). Gameplay
        /// reports <see cref="HdbfCommandFailures.PlotMinimum"/> in that case.
        /// </summary>
        public bool RefusedByMinimum { get; }

        /// <summary>
        /// Registered plots the farm keeps after applying the plan. On a refusal this is the
        /// unchanged registered count; otherwise it is registered count minus excluded count.
        /// </summary>
        public int RemainingPlots { get; }

        /// <summary>
        /// Number of distinct registered plots the request asked to exclude (requested ids
        /// intersected with registered ids and de-duplicated). 0 for an empty/unknown request; on a
        /// refusal it still reports the requested plots, so diagnostics can show why it was refused.
        /// </summary>
        public int RequestedCount { get; }
    }

    /// <summary>
    /// Validates a batch of player exclusions before gameplay applies it (REQ-UI-05, chapter 14.3):
    /// the farm must always keep at least one registered plot, so a box request that would exclude
    /// every registered plot is refused as a whole instead of being partially applied.
    ///
    /// The policy is pure Core so the "last plot" rule is unit-testable and the UI/gameplay cannot
    /// disagree about when <see cref="HdbfCommandFailures.PlotMinimum"/> applies (REQ-TEST-03).
    /// Unknown and duplicate ids are ignored, so a stale box selection is always a safe no-op.
    /// </summary>
    public static class PlotExclusionPolicy
    {
        /// <summary>
        /// Plans one exclusion request against the current registrations.
        ///
        /// Rules: requested ids are intersected with <paramref name="registeredPlotIds"/> and
        /// de-duplicated; an empty or wholly unknown request is a no-op plan (not refused); a
        /// request that would leave fewer than one registered plot is refused as a whole
        /// (<see cref="PlotExclusionPlan.RefusedByMinimum"/>, no excluded ids, remaining = current
        /// registered count); otherwise the plan excludes exactly the known requested ids, sorted
        /// ascending, and reports the remaining count. Null inputs are treated as empty lists.
        /// </summary>
        /// <param name="registeredPlotIds">Plots the farm currently manages.</param>
        /// <param name="requestedPlotIds">Plots the player selected for exclusion.</param>
        public static PlotExclusionPlan Plan(IReadOnlyList<int>? registeredPlotIds,
            IReadOnlyList<int>? requestedPlotIds)
        {
            var registered = new HashSet<int>();
            if (registeredPlotIds is not null)
            {
                for (var i = 0; i < registeredPlotIds.Count; i++)
                {
                    registered.Add(registeredPlotIds[i]);
                }
            }

            if (registered.Count == 0)
            {
                // Nothing registered: no request can exclude a plot, and there is no minimum to
                // violate. This is a no-op plan rather than a refusal.
                return new PlotExclusionPlan(Array.Empty<int>(), false, 0, 0);
            }

            var excluded = new List<int>();
            if (requestedPlotIds is not null)
            {
                var seen = new HashSet<int>();
                for (var i = 0; i < requestedPlotIds.Count; i++)
                {
                    var plotId = requestedPlotIds[i];
                    // Intersect with the registrations and ignore duplicates: a stale selection
                    // (already excluded / never registered) must never corrupt the plan.
                    if (registered.Contains(plotId) && seen.Add(plotId))
                    {
                        excluded.Add(plotId);
                    }
                }
            }

            if (excluded.Count == 0)
            {
                // Empty or wholly unknown request: nothing to exclude, not a refusal.
                return new PlotExclusionPlan(Array.Empty<int>(), false, registered.Count, 0);
            }

            var remaining = registered.Count - excluded.Count;
            if (remaining < 1)
            {
                // The farm must always keep at least one managed plot: refuse the whole request
                // instead of partially applying it (REQ-UI-05, chapter 14.3).
                return new PlotExclusionPlan(Array.Empty<int>(), true, registered.Count, excluded.Count);
            }

            excluded.Sort();
            return new PlotExclusionPlan(excluded.ToArray(), false, remaining, excluded.Count);
        }
    }

    /// <summary>
    /// Frame budget of the managed-plot overlay (REQ-UI-05, chapter 14.6): the 600-plot top tier may
    /// paint at most 200 cells per frame and must finish within 3 frames, so opening the overlay can
    /// never cause a frame spike. The constants and the arithmetic live in Core so gameplay, the
    /// renderer and L1 tests share one budget instead of each hard-coding 600 / 200 / 3.
    /// </summary>
    public static class HighlightFrameBudget
    {
        /// <summary>Maximum number of overlay cells painted in one frame (chapter 14.6).</summary>
        public const int CellsPerFrame = 200;

        /// <summary>
        /// Maximum number of frames the 600-cell top tier may take (600 / 200 = 3, chapter 14.6).
        /// This is the documented budget ceiling, not a clamp: <see cref="FramesNeeded"/> reports the
        /// real count so a regression can be detected.
        /// </summary>
        public const int MaxFrames = 3;

        /// <summary>
        /// Frames needed to paint <paramref name="cellCount"/> cells. 0 cells need 0 frames; a
        /// non-positive <paramref name="cellsPerFrame"/> is treated as 1 (defensive: the budget can
        /// never divide by zero), otherwise the ceiling division is used (600 → 3, 601 → 4).
        /// </summary>
        public static int FramesNeeded(int cellCount, int cellsPerFrame = CellsPerFrame)
        {
            if (cellCount <= 0)
            {
                return 0;
            }

            if (cellsPerFrame <= 0)
            {
                cellsPerFrame = 1;
            }

            return (cellCount + cellsPerFrame - 1) / cellsPerFrame;
        }

        /// <summary>
        /// Cells the next frame may paint: the remaining count capped at the per-frame budget. A
        /// non-positive remaining count paints nothing; a non-positive
        /// <paramref name="cellsPerFrame"/> is treated as 1 so a frame always makes progress.
        /// </summary>
        public static int BatchSize(int remaining, int cellsPerFrame = CellsPerFrame)
        {
            if (remaining <= 0)
            {
                return 0;
            }

            if (cellsPerFrame <= 0)
            {
                cellsPerFrame = 1;
            }

            return remaining < cellsPerFrame ? remaining : cellsPerFrame;
        }
    }

    /// <summary>
    /// One incremental update of the managed-plot overlay: the cells to (re)paint and the plot ids
    /// to remove (REQ-UI-05). Immutable; the lists are never <c>null</c>, so the renderer can apply
    /// the delta without guard clauses. <see cref="IsEmpty"/> means "nothing changed, skip the
    /// frame work".
    /// </summary>
    public sealed class PlotHighlightDelta
    {
        /// <summary>Creates a delta; null lists are normalized to empty lists.</summary>
        public PlotHighlightDelta(IReadOnlyList<PlotHighlightCell>? addedOrChanged,
            IReadOnlyList<int>? removed)
        {
            AddedOrChanged = addedOrChanged ?? Array.Empty<PlotHighlightCell>();
            Removed = removed ?? Array.Empty<int>();
        }

        /// <summary>
        /// Cells whose overlay state is new or changed since the previous snapshot, in the order the
        /// current snapshot supplied them.
        /// </summary>
        public IReadOnlyList<PlotHighlightCell> AddedOrChanged { get; }

        /// <summary>Plot ids that were painted before and are gone now, sorted ascending.</summary>
        public IReadOnlyList<int> Removed { get; }

        /// <summary>True when the delta changes nothing and the renderer can skip the frame.</summary>
        public bool IsEmpty => AddedOrChanged.Count == 0 && Removed.Count == 0;
    }

    /// <summary>
    /// Dirty-diff of two managed-plot overlay snapshots (REQ-UI-05, chapter 14.3/14.6). The overlay
    /// keeps the last painted states and repaints only the delta, so a per-day plot rescan never
    /// rebuilds all 600 quads.
    ///
    /// Pure Core so the added/changed/removed rules are testable without Unity (REQ-TEST-03).
    /// </summary>
    public static class PlotHighlightDiff
    {
        /// <summary>
        /// Compares the previously painted states with the current snapshot.
        ///
        /// <see cref="PlotHighlightDelta.AddedOrChanged"/> contains every current cell whose state
        /// is absent from <paramref name="previousStates"/> or differs from it;
        /// <see cref="PlotHighlightDelta.Removed"/> contains the previous plot ids absent from the
        /// current snapshot, sorted ascending for a deterministic repaint order. The current
        /// snapshot is expected to hold one cell per plot id (the scan/overlay invariant); its order
        /// is preserved in the delta. Null inputs are treated as empty snapshots, and the returned
        /// lists are never <c>null</c>.
        /// </summary>
        /// <param name="previousStates">States painted by the previous update, keyed by plot id.</param>
        /// <param name="current">The overlay cells of the current update.</param>
        public static PlotHighlightDelta Diff(
            IReadOnlyDictionary<int, HdbfPlotHighlightState>? previousStates,
            IReadOnlyList<PlotHighlightCell>? current)
        {
            var addedOrChanged = new List<PlotHighlightCell>();

            // Only needed to decide removals; skip the allocation for a first paint.
            var currentIds = previousStates is not null && previousStates.Count > 0
                ? new HashSet<int>()
                : null;

            if (current is not null)
            {
                for (var i = 0; i < current.Count; i++)
                {
                    var cell = current[i];
                    currentIds?.Add(cell.PlotId);

                    if (previousStates is null
                        || !previousStates.TryGetValue(cell.PlotId, out var previousState)
                        || previousState != cell.State)
                    {
                        addedOrChanged.Add(cell);
                    }
                }
            }

            if (previousStates is null || previousStates.Count == 0)
            {
                return new PlotHighlightDelta(addedOrChanged, Array.Empty<int>());
            }

            var removed = new List<int>();
            foreach (var pair in previousStates)
            {
                if (currentIds is null || !currentIds.Contains(pair.Key))
                {
                    removed.Add(pair.Key);
                }
            }

            removed.Sort();
            return new PlotHighlightDelta(addedOrChanged, removed);
        }
    }
}
