// ============================================================================
// HDBF v2 | Layer: Core/Indoor | Owner: V2-CORE-01
// Purpose: G_power of a power-needing tier (INTERFACES section 5, section 19.8).
//          Pure function; no mechanical network objects.
// Requirements: REQ-V2-BAL-01, REQ-V2-CORE-04
// ============================================================================
namespace HDBF.Core.Indoor
{
    /// <summary>Power mathematics of an indoor farm (INTERFACES section 5).</summary>
    public static class IndoorPowerMath
    {
        /// <summary>
        /// G_power: a tier that needs no power, or a connected grid, grows at 1.0; a power-needing
        /// tier with a cut grid grows at 0.60 (section 19.8).
        /// </summary>
        /// <param name="needsPower">True when the building must draw grid power today.</param>
        /// <param name="connected">True when the grid connection is up.</param>
        /// <returns>Power growth multiplier.</returns>
        public static float Efficiency(bool needsPower, bool connected)
        {
            if (!needsPower || connected)
            {
                return IndoorConstants.NormalModeMultiplier;
            }

            return IndoorConstants.OffGridEfficiency;
        }
    }
}
