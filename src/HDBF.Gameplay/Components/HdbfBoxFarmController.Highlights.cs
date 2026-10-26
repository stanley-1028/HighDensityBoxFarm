using System;
using System.Collections.Generic;
using HDBF.Core;
using HDBF.Core.Abstractions;
using HDBF.Core.Logging;
using HDBF.Core.Plots;
using UnityEngine;

namespace HDBF.Gameplay.Components
{
    /// <summary>
    /// Partial part: data and write actions for the managed-plot overlay (REQ-UI-05, chapter
    /// 14.3/14.6). Split from <c>Commands.cs</c> so the world-space interaction path (click /
    /// shift-drag) can be reviewed next to the read model it needs, while the actual click/box
    /// handling and rendering live in <see cref="HdbfPlotHighlightService"/>.
    ///
    /// Nothing here scans the map or runs per frame: the overlay asks for a fixed list of plot
    /// ids and classifies them in frame-budgeted slices in the service (table 10-5).
    /// </summary>
    public partial class HdbfBoxFarmController
    {
        // ------------------------------------------------------------------ read model

        /// <summary>Number of plots currently registered (excluded plots are not registered).</summary>
        internal int RegisteredPlotCount => _record?.PlotIds.Length ?? 0;

        /// <summary>Number of plots the overlay must display: registered + player-excluded ids.</summary>
        internal int HighlightPlotCount => RegisteredPlotCount + _excluded.Count;

        /// <summary>True when the plot is registered by this farm right now.</summary>
        internal bool IsRegisteredForHighlight(int plotId) => IsRegisteredPlot(plotId);

        /// <summary>True when the plot is in the persisted player-excluded set (REQ-IMPL-10).</summary>
        internal bool IsPlotExcluded(int plotId) => _excluded.Contains(plotId);

        /// <summary>True when the plot is either registered or excluded, i.e. it has an overlay cell.</summary>
        internal bool HasPlotForHighlight(int plotId) => IsRegisteredPlot(plotId) || _excluded.Contains(plotId);

        /// <summary>
        /// Per-plot irrigation sample for the overlay colour. This is the same query the daily
        /// coverage sample uses (<c>ISoilMoistureService.SoilIsMoist</c> at the moisture-sample
        /// cell of <see cref="PlotCellHeights"/>), just per plot instead of one counter.
        ///
        /// A failure degrades to "dry" (yellow) for that cell only: painting a failed sample green
        /// would overstate irrigation, and the daily coverage counter keeps its own fallback
        /// (REQ-IMPL-14). One cheap dictionary lookup per plot, called only while the overlay is
        /// open and only for the current frame-budget slice.
        /// </summary>
        internal bool IsPlotIrrigated(int plotId)
        {
            try
            {
                var plantCell = HdbfPlotCellCodec.Decode(plotId);
                var sampleCell = new Vector3Int(plantCell.x, plantCell.y,
                    PlotCellHeights.ResolveMoistureSampleZ(plantCell.z));
                return _soilMoistureService.SoilIsMoist(sampleCell);
            }
            catch (Exception)
            {
                // Deliberately silent per cell: the throttled log would be flooded by a broken
                // 600-cell overlay. The existing coverage path already reports the service failure.
                return false;
            }
        }

        /// <summary>
        /// Snapshot of every plot id the overlay must display: registered ids in their stable
        /// registry order, then the excluded ids sorted ascending (a HashSet has no order, and the
        /// overlay must rebuild deterministically). The returned array is owned by the caller.
        /// </summary>
        internal int[] BuildHighlightPlotIdList()
        {
            var registered = _record?.PlotIds ?? Array.Empty<int>();
            var excluded = ToIntArray(_excluded);
            Array.Sort(excluded);

            var result = new int[registered.Length + excluded.Length];
            Array.Copy(registered, result, registered.Length);
            Array.Copy(excluded, 0, result, registered.Length, excluded.Length);
            return result;
        }

        /// <summary>
        /// Resolves the plant cell (terrain column ceiling, the Fields-tool cell) at a horizontal
        /// click position. Returns false for a column outside the terrain map so the overlay never
        /// paints or encodes a non-plot.
        /// </summary>
        internal bool TryResolvePlantCell(int x, int y, out Vector3Int plantCell)
        {
            if (_terrainService is null || !_terrainService.Contains(new Vector2Int(x, y)))
            {
                plantCell = default;
                return false;
            }

            plantCell = ResolvePlotCell(new Vector3Int(x, y, 0));
            return true;
        }

        // ------------------------------------------------------------------ world actions

        /// <summary>
        /// Click action: exclude a registered plot, restore an excluded one. The last registered
        /// plot is refused with <see cref="HdbfCommandFailures.PlotMinimum"/> (REQ-UI-05); an
        /// unknown plot is refused with <see cref="HdbfCommandFailures.PlotNotRegistered"/> and the
        /// overlay reports both through the snapshot. Nothing is persisted on a refusal
        /// ("整单回滚").
        /// </summary>
        internal bool TryTogglePlotExclusion(int plotId, out string? failureKey)
        {
            failureKey = null;
            if (_record is null)
            {
                failureKey = HdbfCommandFailures.NotABoxFarm;
                return false;
            }

            if (_excluded.Contains(plotId))
            {
                if (TryIncludePlot(plotId))
                {
                    return true;
                }

                failureKey = HdbfCommandFailures.PlotNotRegistered;
                return false;
            }

            if (!IsRegisteredPlot(plotId))
            {
                failureKey = HdbfCommandFailures.PlotNotRegistered;
                return false;
            }

            if (TryExcludePlot(plotId))
            {
                return true;
            }

            failureKey = HdbfCommandFailures.PlotMinimum;
            return false;
        }

        /// <summary>
        /// Box-select action: excludes every requested registered plot in one transaction.
        ///
        /// The whole batch is refused when it would leave fewer than one registered plot (the same
        /// rule as a single click, REQ-UI-05) — a partial batch would be a silent surprise.
        /// Requested ids that are unknown or already excluded are ignored; an empty effective batch
        /// is a no-op success. One coverage resample and one registry update per batch keeps a
        /// 200-cell rectangle cheap.
        /// </summary>
        internal bool TryExcludePlots(IReadOnlyList<int> requestedPlotIds, out string? failureKey)
        {
            failureKey = null;
            if (_record is null)
            {
                failureKey = HdbfCommandFailures.NotABoxFarm;
                return false;
            }

            var plan = PlotExclusionPolicy.Plan(_record.PlotIds, requestedPlotIds);
            if (plan.RefusedByMinimum)
            {
                failureKey = HdbfCommandFailures.PlotMinimum;
                return false;
            }

            if (plan.ExcludedPlotIds.Count == 0)
            {
                // Nothing to do: all requested cells were already excluded/unknown.
                return true;
            }

            var remove = new HashSet<int>();
            for (var i = 0; i < plan.ExcludedPlotIds.Count; i++)
            {
                var plotId = plan.ExcludedPlotIds[i];
                if (_excluded.Add(plotId))
                {
                    remove.Add(plotId);
                }
            }

            var ids = _record.PlotIds;
            var keep = new int[ids.Length - remove.Count];
            var keepIndex = 0;
            for (var i = 0; i < ids.Length; i++)
            {
                if (!remove.Contains(ids[i]))
                {
                    keep[keepIndex++] = ids[i];
                }
            }

            ApplyPlotArrays(keep);
            // Removing plots changes the coverage denominator; resample now, exactly like the
            // single-plot path, so the panel count and coverage update together.
            RefreshCoverageFromRegisteredPlots();
            _record.ExcludedPlotIds = ToIntArray(_excluded);
            _saveDirty = true;
            _registry.ApplyPlotSelection(_entityId, keep);
            _log.Write(HdbfLogCategory.Info, "gameplay.highlight",
                "box excluded " + plan.ExcludedPlotIds.Count + " plot(s), entityId=" + _entityId
                + ", remaining=" + keep.Length);
            return true;
        }
    }
}
