using System.Collections.Generic;
using HDBF.Core.Plots;
using UnityEngine;

namespace HDBF.Gameplay.Components
{
    /// <summary>
    /// Callback contract of <see cref="HdbfPlotScanner"/>; implemented by the controller so the
    /// scanner itself stays free of game services and is easy to reason about.
    ///
    /// BUG-04 structural rule (round-9 in-game log, <c>selected=1 granted=1</c> for 102 accepted
    /// cells): the host never returns a plot id. It only reports whether a resolved plot cell is
    /// accepted, and the scanner derives the id from that cell through the single Core codec. An
    /// "accepted cell without an id" is therefore not representable: the previous contract
    /// <c>bool TryAcceptPlot(..., out int plotId)</c> allowed the host to return <c>true</c> while
    /// the out parameter still held its initial 0, so all 102 candidates collapsed to plot id 0 /
    /// one selected plot / <c>registeredNow=1</c> and every moisture sample landed on cell (0,0,0).
    /// </summary>
    public interface IHdbfPlotScanHost
    {
        /// <summary>
        /// Resolves the game cell a horizontal scan position is validated and registered as. The
        /// z of the plant cell comes from the game's terrain data (see <see cref="PlotCellHeights"/>
        /// for the IL evidence), which is why this mapping — and only this mapping — stays on the
        /// host. Must have no gameplay side effects; the cell is re-validated by
        /// <see cref="TryAcceptPlot"/> before it can become a managed plot.
        /// </summary>
        Vector3Int ResolvePlotCell(Vector3Int horizontalCell);

        /// <summary>
        /// Validates one resolved plot cell and reports whether it becomes a managed plot in this
        /// scan. Deliberately returns only <c>bool</c>: after a <c>true</c> the scanner always
        /// encodes <paramref name="plotCell"/> itself, so the accepted cell and its plot id can
        /// never disagree.
        /// </summary>
        bool TryAcceptPlot(Vector3Int plotCell, int distanceSquared);

        /// <summary>Called exactly once when the range has been fully visited.</summary>
        void OnPlotsScanned(IReadOnlyList<PlotCandidate> candidates, int deferredFrames);
    }

    /// <summary>
    /// Incremental work-range scanner (chapter 11.3 / REQ-IMPL-09, REQ-SAFE-17).
    ///
    /// The scan is event driven (building finished, daily tick, manual request) and then spread
    /// over frames with a hard per-frame cell budget from configuration
    /// (<c>perf.scanBudgetPerFrame</c>, default 128). No scan runs synchronously in a placement
    /// callback and no full-map walk ever happens, so the cost is bounded by the tier's square
    /// (at most 35 × 35 = 1225 cells) and amortised over a few frames.
    ///
    /// The candidate buffer is allocated once and reused, so a steady-state day tick does not
    /// produce scan garbage (REQ-SAFE-20).
    ///
    /// Plot-id ownership (BUG-04): for every accepted cell this class calls
    /// <see cref="HdbfPlotCellCodec.Encode"/>, which is a thin adapter over the one Core bit
    /// layout (<see cref="PlotCellCodec"/>). There is no second bit layout and no host-supplied id.
    /// </summary>
    internal sealed class HdbfPlotScanner
    {
        private readonly List<PlotCandidate> _candidates;

        private Vector3Int _origin;
        private int _side;
        private int _half;
        private int _cursor;
        private int _deferredFrames;

        public HdbfPlotScanner(int maxSide)
        {
            var capacity = maxSide > 0 ? maxSide * maxSide : 1;
            _candidates = new List<PlotCandidate>(capacity);
        }

        /// <summary>True while a scan is in progress and <see cref="Step"/> still has work.</summary>
        public bool Active { get; private set; }

        /// <summary>Starts (or restarts) a scan centred on the entrance cell.</summary>
        public void Begin(Vector3Int origin, int side)
        {
            if (side < 1)
            {
                side = 1;
            }

            var capacity = side * side;
            if (_candidates.Capacity < capacity)
            {
                _candidates.Capacity = capacity;
            }

            _candidates.Clear();
            _origin = origin;
            _side = side;
            _half = side / 2;
            _cursor = 0;
            _deferredFrames = 0;
            Active = true;
        }

        /// <summary>
        /// Processes at most <paramref name="budget"/> cells. Calls
        /// <see cref="IHdbfPlotScanHost.OnPlotsScanned"/> once when the range is exhausted.
        /// </summary>
        public void Step(int budget, IHdbfPlotScanHost host)
        {
            if (!Active)
            {
                return;
            }

            if (budget < 1)
            {
                budget = 1;
            }

            var total = _side * _side;
            for (var processed = 0; processed < budget && _cursor < total; processed++)
            {
                var index = _cursor++;
                var dx = (index % _side) - _half;
                var dy = (index / _side) - _half;
                var horizontal = new Vector3Int(_origin.x + dx, _origin.y + dy, _origin.z);
                var distance = (dx * dx) + (dy * dy);
                var plotCell = host.ResolvePlotCell(horizontal);
                if (!host.TryAcceptPlot(plotCell, distance))
                {
                    continue;
                }

                // One id per accepted cell, computed here and only here: the candidate is always
                // built from the exact cell the host just accepted.
                _candidates.Add(new PlotCandidate(HdbfPlotCellCodec.Encode(plotCell), distance));
            }

            if (_cursor < total)
            {
                _deferredFrames++;
                return;
            }

            Active = false;
            host.OnPlotsScanned(_candidates, _deferredFrames);
        }

        /// <summary>Abandons the current scan (building removed or an exception was isolated).</summary>
        public void Cancel()
        {
            Active = false;
        }
    }
}
