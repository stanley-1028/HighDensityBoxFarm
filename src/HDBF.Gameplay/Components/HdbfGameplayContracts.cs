// ============================================================================
// HDBF v2 | Layer: Gameplay/Components | Owner: V2-UI-01
// Purpose: The gameplay-side read contracts shared by the controller, the query singleton and
//          the world-icon service. The v1 plot-ownership registry and the retained v1 warning
//          evaluator are gone (section 19.2); these types only describe the v2 indoor snapshot
//          source and the resolved config lookup.
// Requirements: REQ-V2-CORE-01, REQ-V2-WATER-03, REQ-V2-UI-01/02, REQ-V2-CORE-03
// ============================================================================
using System;
using System.Collections.Generic;
using HDBF.Core;
using HDBF.Core.Balance;
using HDBF.Core.Config;
using HDBF.Core.Indoor;

namespace HDBF.Gameplay.Components
{
    /// <summary>
    /// Read source published by one live indoor farm to <see cref="HdbfFarmQueryService"/>. UI
    /// never sees this type; UI consumes the Core <see cref="IndoorFarmSnapshot"/> DTO through the
    /// <c>IBoxFarmQuery</c> seam only (section 19.18.2 module map).
    /// </summary>
    public interface IHdbfIndoorFarmSource
    {
        /// <summary>Runtime entity id used as the UI lookup key.</summary>
        Guid EntityId { get; }

        /// <summary>Save-file identity of the building.</summary>
        long SaveEntityId { get; }

        /// <summary>
        /// Builds the Core v2 panel DTO (INTERFACES section 9). Must not throw; the caller
        /// isolates it per farm (REQ-V2-SAFE-01/07).
        /// </summary>
        /// <returns>Fresh immutable snapshot of this farm.</returns>
        IndoorFarmSnapshot CreateIndoorSnapshot();
    }

    /// <summary>
    /// Resolved balance data for gameplay. Implemented by <see cref="HdbfSystemRegistrar"/>:
    /// loads <c>HDBF_Config.json</c> once and exposes the config-resolved tier table.
    /// </summary>
    public interface IHdbfGameConfigProvider
    {
        /// <summary>Fully resolved configuration (defaults when the file is absent or broken).</summary>
        HdbfConfig Config { get; }

        /// <summary>Config-resolved tier table, four entries indexed by <see cref="HdbfTier"/>.</summary>
        IReadOnlyList<TierProfile> Profiles { get; }

        /// <summary>
        /// Looks a profile up by blueprint id. Returns false when the blueprint is unknown; the
        /// <paramref name="profile"/> is then the tier fallback (never throws).
        /// </summary>
        /// <param name="blueprintId">Blueprint name carried by the custom spec.</param>
        /// <param name="fallbackTier">Tier parsed from the spec when the blueprint is unknown.</param>
        /// <param name="profile">Resolved tier profile; never null.</param>
        /// <returns>True when the blueprint id mapped to a known tier.</returns>
        bool TryGetTierProfile(string? blueprintId, HdbfTier fallbackTier, out TierProfile profile);
    }
}
