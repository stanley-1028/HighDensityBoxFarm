using System;
using System.Collections.Generic;
using HDBF.Core.Plots;

namespace HDBF.Core.Abstractions
{
    /// <summary>
    /// The four plot states the managed-plot overlay paints (chapter 14.3):
    /// irrigated = green, dry = yellow, excluded = gray, unreachable = red.
    ///
    /// The enum is a Core contract so UI, gameplay rendering and L1 tests share one definition
    /// (the UI must never learn a game type, REQ-ARCH-01). Unreachable is the optional diagnostic
    /// state of chapter 11.3: a cell inside the work range that is plantable but not reachable is
    /// never registered, so the current scan does not produce it; the contract still defines it so
    /// the renderer/legend are complete and a later scan can start feeding it without a contract
    /// change.
    /// </summary>
    public enum HdbfPlotHighlightState
    {
        /// <summary>Registered managed plot whose soil moisture is present (green).</summary>
        Irrigated = 0,

        /// <summary>Registered managed plot without soil moisture (yellow).</summary>
        Dry = 1,

        /// <summary>Player-excluded plot, persisted in <c>excludedPlotIds</c> (gray).</summary>
        Excluded = 2,

        /// <summary>Optional chapter-11.3 diagnostic: plantable but unreachable cell (red).</summary>
        Unreachable = 3,
    }

    /// <summary>
    /// One cell of the managed-plot overlay: the stable plot id, the decoded plant cell and the
    /// state that decides its colour. Game-free so L1 can test the box-selection and dirty-diff
    /// rules without Unity (REQ-TEST-03).
    ///
    /// <see cref="X"/>/<see cref="Y"/>/<see cref="Z"/> are the decoded plot-cell coordinates
    /// (<see cref="PlotCellCodec"/> layout); <see cref="PlotId"/> remains the persisted identity.
    /// </summary>
    public readonly struct PlotHighlightCell : IEquatable<PlotHighlightCell>
    {
        public PlotHighlightCell(int plotId, int x, int y, int z, HdbfPlotHighlightState state)
        {
            PlotId = plotId;
            X = x;
            Y = y;
            Z = z;
            State = state;
        }

        /// <summary>Stable plot id stored in the save record (REQ-IMPL-10 identity).</summary>
        public int PlotId { get; }

        public int X { get; }

        public int Y { get; }

        public int Z { get; }

        public HdbfPlotHighlightState State { get; }

        /// <summary>
        /// Builds a cell from a plot id, decoding the plant cell with the same codec the save and
        /// scan use, so the rendered quad can never drift from the registered plot.
        /// </summary>
        public static PlotHighlightCell FromPlotId(int plotId, HdbfPlotHighlightState state)
        {
            PlotCellCodec.Decode(plotId, out var x, out var y, out var z);
            return new PlotHighlightCell(plotId, x, y, z, state);
        }

        /// <summary>Same coordinates and state, different state; keeps the cell identity.</summary>
        public PlotHighlightCell WithState(HdbfPlotHighlightState state) =>
            new PlotHighlightCell(PlotId, X, Y, Z, state);

        public bool Equals(PlotHighlightCell other) =>
            PlotId == other.PlotId && X == other.X && Y == other.Y && Z == other.Z && State == other.State;

        public override bool Equals(object? obj) => obj is PlotHighlightCell other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = PlotId;
                hash = (hash * 397) ^ X;
                hash = (hash * 397) ^ Y;
                hash = (hash * 397) ^ Z;
                hash = (hash * 397) ^ (int)State;
                return hash;
            }
        }

        public static bool operator ==(PlotHighlightCell left, PlotHighlightCell right) => left.Equals(right);

        public static bool operator !=(PlotHighlightCell left, PlotHighlightCell right) => !left.Equals(right);

        public override string ToString() =>
            "plot=" + PlotId + " cell=(" + X + "," + Y + "," + Z + ") state=" + State;
    }

    /// <summary>
    /// RGBA colours of the four overlay states, single-sourced for the world overlay and the panel
    /// legend. Values are <c>0xRRGGBBAA</c> so Core stays free of UnityEngine.Color; UI converts to
    /// <c>Color32</c> and gameplay to <c>Color</c>.
    ///
    /// The alpha (0xA6 ≈ 65 %) is a first-version visual constant (初版可调), deliberately below 1
    /// so terrain and crops stay readable, matching the vanilla semi-transparent tile markers.
    /// </summary>
    public static class HdbfPlotHighlightPalette
    {
        public const uint IrrigatedRgba = 0x4CAF50A6;
        public const uint DryRgba = 0xFFC107A6;
        public const uint ExcludedRgba = 0x9E9E9EA6;
        public const uint UnreachableRgba = 0xF44336A6;

        /// <summary>Legend order, also the order the requirement text lists the states in.</summary>
        public static IReadOnlyList<HdbfPlotHighlightState> LegendOrder { get; } =
            new[]
            {
                HdbfPlotHighlightState.Irrigated,
                HdbfPlotHighlightState.Dry,
                HdbfPlotHighlightState.Excluded,
                HdbfPlotHighlightState.Unreachable,
            };

        public static uint Rgba(HdbfPlotHighlightState state)
        {
            switch (state)
            {
                case HdbfPlotHighlightState.Irrigated:
                    return IrrigatedRgba;
                case HdbfPlotHighlightState.Dry:
                    return DryRgba;
                case HdbfPlotHighlightState.Excluded:
                    return ExcludedRgba;
                default:
                    return UnreachableRgba;
            }
        }
    }
}
