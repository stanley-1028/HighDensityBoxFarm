namespace HDBF.Core.Plots
{
    /// <summary>
    /// Packs a Timberborn cell <c>(x, y, z)</c> into the <c>int</c> plot id stored in save records
    /// (<see cref="HDBF.Core.Save.HdbfBuildingRecord.PlotIds"/>).
    ///
    /// Timberborn's cell coordinates are <c>Vector3Int(x, y, z)</c> where <b>z is the vertical
    /// axis</b> and x/y are the two horizontal axes. The map is within ±1024 cells horizontally and
    /// 0..1023 vertically, which fits the layout: x in bits 21..31, y in bits 10..20, z in bits
    /// 0..9. Encoding is bijective for in-map cells, deterministic across sessions, and cheap
    /// enough for the per-day scan.
    ///
    /// The bit layout lives in Core (not Gameplay) so L1 tests can prove the bijection without
    /// referencing any Timberborn/Unity assembly (REQ-TEST-03); the Gameplay
    /// <c>HdbfPlotCellCodec</c> is a thin <c>Vector3Int</c> adapter over this class.
    ///
    /// The stored <b>z is the plant cell z</b>, i.e. the first air cell above the terrain column
    /// (the cell the game's Fields tool paints); see <see cref="PlotCellHeights"/> for the IL
    /// evidence and for the plant-cell → moisture-sample-cell mapping.
    /// </summary>
    public static class PlotCellCodec
    {
        /// <summary>Width of the horizontal fields in bits (11-bit two's complement).</summary>
        public const int HorizontalBits = 11;

        /// <summary>Width of the vertical field in bits (10-bit unsigned).</summary>
        public const int VerticalBits = 10;

        /// <summary>Horizontal value mask (0x7FF).</summary>
        public const int HorizontalMask = 0x7FF;

        /// <summary>Vertical value mask (0x3FF).</summary>
        public const int VerticalMask = 0x3FF;

        /// <summary>Sign bit inside the 11-bit horizontal field (0x400).</summary>
        public const int HorizontalSignBit = 0x400;

        /// <summary>Lowest encodable horizontal coordinate (inclusive).</summary>
        public const int MinHorizontalCoordinate = -1024;

        /// <summary>Highest encodable horizontal coordinate (inclusive).</summary>
        public const int MaxHorizontalCoordinate = 1023;

        /// <summary>Lowest encodable vertical coordinate (inclusive).</summary>
        public const int MinVerticalCoordinate = 0;

        /// <summary>Highest encodable vertical coordinate (inclusive).</summary>
        public const int MaxVerticalCoordinate = 1023;

        /// <summary>Encodes one game cell as a stable plot id.</summary>
        public static int Encode(int x, int y, int z)
        {
            return ((x & HorizontalMask) << 21)
                   | ((y & HorizontalMask) << 10)
                   | (z & VerticalMask);
        }

        /// <summary>Decodes a plot id written by <see cref="Encode"/>.</summary>
        public static void Decode(int plotId, out int x, out int y, out int z)
        {
            x = SignExtend((plotId >> 21) & HorizontalMask);
            y = SignExtend((plotId >> 10) & HorizontalMask);
            z = plotId & VerticalMask;
        }

        /// <summary>
        /// True when <see cref="Encode"/> / <see cref="Decode"/> round-trip the cell unchanged,
        /// i.e. it is inside the documented map bounds. Out-of-range cells are masked, which would
        /// alias another cell, so gameplay must never store such a cell.
        /// </summary>
        public static bool IsEncodable(int x, int y, int z)
        {
            return x >= MinHorizontalCoordinate && x <= MaxHorizontalCoordinate
                && y >= MinHorizontalCoordinate && y <= MaxHorizontalCoordinate
                && z >= MinVerticalCoordinate && z <= MaxVerticalCoordinate;
        }

        private static int SignExtend(int value)
        {
            return value >= HorizontalSignBit ? value - (HorizontalMask + 1) : value;
        }
    }
}
