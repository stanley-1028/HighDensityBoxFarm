// Minimal structural stand-in for UnityEngine.Vector3Int.
//
// Why this exists: three production files under src/HDBF.Gameplay are game-free enough to be
// compiled into this test assembly via <Compile Link> items:
//   * Farming/FarmWorkPlanner.cs        (pure ordering/work-cost rules; one unused Vector3Int DTO field)
//   * Components/HdbfPlotScanner.cs     (the BUG-04 plot-id ownership fix: the scanner encodes
//                                        the accepted cell itself instead of trusting a host out id)
//   * Components/HdbfPlotCellCodec.cs   (thin Vector3Int -> Core PlotCellCodec adapter)
// so the real production source is asserted without referencing any Timberborn/Unity assembly at
// compile or run time (REQ-TEST-03: L1 must run in a clean, game-free environment).
//
// The shape mirrors the Unity type the game code actually touches: the x/y/z properties are the
// terrain-cell coordinates (Unity's Vector3Int exposes them as properties, not fields). If the
// linked files ever start using more of the real Unity type, this project stops compiling instead
// of silently passing with unverified logic - that is intentional.
namespace UnityEngine
{
    /// <summary>Shape-compatible subset of UnityEngine.Vector3Int used only for compilation.</summary>
    public struct Vector3Int
    {
        public Vector3Int(int x, int y, int z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public int x { get; }

        public int y { get; }

        public int z { get; }
    }
}
