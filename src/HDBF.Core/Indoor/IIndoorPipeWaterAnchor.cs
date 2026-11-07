// ============================================================================
// HDBF v2 | Layer: Core/Indoor | Owner: V2-CORE-01
// Purpose: Optional companion contract that lets Gameplay publish the farm's occupied
//          footprint cells to the water adapter, so the adapter can accept water supplied
//          by a Timber Pipes transport pipe that physically touches the farm shell.
//          Core value type only: no Unity/game/third-party type crosses the layer boundary.
// Requirements: REQ-V2-WATER-02, REQ-V2-COMPAT-01, REQ-V2-SAFE-05
// ============================================================================
using System.Collections.Generic;

namespace HDBF.Core.Indoor
{
    /// <summary>
    /// One grid cell of a farm footprint, in world coordinates. A Core value type on purpose:
    /// the Gameplay layer publishes the cells and the Integration layer converts them to the
    /// game's coordinate type, so no game assembly reference ever enters HDBF.Core.
    /// </summary>
    /// <param name="X">World grid x coordinate.</param>
    /// <param name="Y">World grid y coordinate.</param>
    /// <param name="Z">World grid z coordinate.</param>
    public readonly record struct PipeCell(int X, int Y, int Z);

    /// <summary>
    /// Optional companion of <see cref="IWaterSource"/>: the caller tells the pipe water source
    /// which grid cells the farm occupies before asking it to draw. The adapter can then accept
    /// water from a Timber Pipes transport pipe that is adjacent to the farm shell, instead of
    /// only drawing from settlement-wide liquid tanks.
    ///
    /// Implementations must stay defensive: <c>null</c> clears the footprint (the next draw falls
    /// back to the settlement-wide tank path) and every query must answer "no water" rather than
    /// throw into the daily tick/panel path. A separate interface keeps the frozen
    /// <see cref="IWaterSource"/> and <see cref="IIndoorAdapterHost"/> contracts unchanged, and a
    /// host that does not implement it simply keeps the previous tank-only behaviour.
    /// </summary>
    public interface IIndoorPipeWaterAnchor
    {
        /// <summary>
        /// Sets the farm footprint used by the next draws/probes; <c>null</c> clears it.
        /// </summary>
        /// <param name="footprint">World cells the farm building occupies, or null when unknown.</param>
        void SetWaterAnchor(IReadOnlyList<PipeCell>? footprint);
    }
}
