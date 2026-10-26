// ============================================================================
// HDBF v2 | Layer: Core/Indoor | Owner: V2-CORE-01
// Purpose: Crop table contract implemented by the gameplay spec reader and by
//          FallbackCropCatalog (INTERFACES section 6/7). Pure interface, no data.
// Requirements: REQ-V2-CORE-05, REQ-V2-EXT-01
// ============================================================================
using System.Collections.Generic;

namespace HDBF.Core.Indoor
{
    /// <summary>
    /// Read-only crop catalog for one or both factions. Implemented by the gameplay layer from the
    /// vanilla <c>FarmHouse</c> specs; <see cref="FallbackCropCatalog"/> is the built-in fallback
    /// used when a spec is missing (section 19.5).
    /// </summary>
    public interface ICropCatalog
    {
        /// <summary>
        /// Returns the crops the faction can plant, in a stable order. The order is the rotation
        /// order, so implementations must not reorder between calls (determinism).
        /// </summary>
        /// <param name="faction">Faction whose crop table is requested.</param>
        /// <returns>Crops in rotation order; empty (never null) for an unknown faction.</returns>
        IReadOnlyList<CropDefinition> ForFaction(HdbfFaction faction);

        /// <summary>Looks up one crop id for a faction; returns false instead of throwing when absent.</summary>
        /// <param name="faction">Faction that must know the crop.</param>
        /// <param name="cropId">Crop template id, e.g. "Carrot".</param>
        /// <param name="crop">Resolved definition when the method returns true.</param>
        /// <returns>True when the faction catalog contains the crop.</returns>
        bool TryGet(HdbfFaction faction, string cropId, out CropDefinition crop);
    }
}
