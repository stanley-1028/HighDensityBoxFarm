// ============================================================================
// HDBF v2 | Layer: Core/Indoor | Owner: V2-CORE-01
// Purpose: Water source abstraction implemented by the pipe adapter / manual refill
//          bridge (INTERFACES section 6). Core defines it; other assemblies implement it.
// Requirements: REQ-V2-WATER-02, REQ-V2-WATER-04, REQ-V2-EXT-02
// ============================================================================
namespace HDBF.Core.Indoor
{
    /// <summary>
    /// A real water source (pipe adapter, manual fetch, future water wheel/tank). The daily tick
    /// may only add tank water that was drawn through this interface (REQ-V2-WATER-02).
    /// </summary>
    public interface IWaterSource
    {
        /// <summary>Kind of source, used by the panel and by the refill precedence rules.</summary>
        WaterSupplyKind Kind { get; }

        /// <summary>True while the source can supply water at all (pipe connected, water body in range).</summary>
        bool IsAvailable { get; }

        /// <summary>
        /// Draws up to <paramref name="amount"/> water units from the real source.
        /// </summary>
        /// <param name="amount">Requested amount in water units.</param>
        /// <param name="actual">Actually drawn amount in water units.</param>
        /// <returns>True when the source accepted the draw; false means no water moved.</returns>
        bool TryDraw(float amount, out float actual);
    }
}
