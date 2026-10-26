using HDBF.Core.Plots;
using UnityEngine;

namespace HDBF.Gameplay.Components
{
    /// <summary>
    /// Gameplay adapter for the pure plot-id codec: packs a Timberborn cell into the <c>int</c>
    /// plot id stored in save records (<see cref="HDBF.Core.Save.HdbfBuildingRecord.PlotIds"/>).
    ///
    /// The bit layout and its tests live in <see cref="PlotCellCodec"/> (HDBF.Core), so L1 can
    /// prove the encode/decode bijection without referencing the game assemblies (REQ-TEST-03).
    /// This class only converts <c>Vector3Int</c> ↔ the three ints.
    ///
    /// The stored <b>z is the plant cell z</b> — the first air cell above the terrain column, the
    /// cell the game's Fields tool paints — and it is also the cell
    /// <c>ISoilMoistureService.SoilIsMoist</c> expects. See
    /// <see cref="PlotCellHeights"/> for the full IL evidence chain and the plant-cell →
    /// moisture-sample-cell seam.
    /// </summary>
    public static class HdbfPlotCellCodec
    {
        /// <summary>Encodes one game cell as a stable plot id.</summary>
        public static int Encode(Vector3Int cell)
        {
            return PlotCellCodec.Encode(cell.x, cell.y, cell.z);
        }

        /// <summary>Decodes a plot id written by <see cref="Encode"/>.</summary>
        public static Vector3Int Decode(int plotId)
        {
            PlotCellCodec.Decode(plotId, out var x, out var y, out var z);
            return new Vector3Int(x, y, z);
        }
    }
}
