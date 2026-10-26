// ============================================================================
// HDBF v2 | Layer: Gameplay/Components | Owner: V2-GAMEPLAY-01
// Purpose: Read-only IGrowthMultiplierSource shim for the v1 ADR-009 Harmony bridge.
//          The v2 world has no managed plots, so it always disables the patch and
//          answers "not managed"; the patch itself is removed by V2-GAMEPLAY-02.
// Requirements: REQ-V2-CORE-03, REQ-V2-SAFE-05
// ============================================================================
using HDBF.Core.Growth;

namespace HDBF.Gameplay.Components
{
    /// <summary>
    /// Transitional implementation of <see cref="IGrowthMultiplierSource"/>. HDBF.Integration's
    /// still-shipped ADR-009 bootstrap resolves this Core interface from DI; returning false avoids
    /// a container-resolution failure and guarantees no vanilla crop ever receives an HDBF growth
    /// rate, because v2 has no world plots (REQ-V2-CORE-03). Deleting the patch and this shim is
    /// the收口 card's job.
    /// </summary>
    public sealed class ReadOnlyGrowthRateProvider : IGrowthMultiplierSource
    {
        /// <inheritdoc />
        public bool IsPatchEnabled => false;

        /// <inheritdoc />
        public bool TryGetGrowthRate(int cellX, int cellY, int cellZ, out float growthRate)
        {
            growthRate = 1f;
            return false;
        }
    }
}
