// ============================================================================
// HDBF v2 | Layer: Gameplay/Components | Owner: V2-UI-01
// Purpose: Gameplay-side v2 query contract consumed by the panel: the Core IndoorFarmSnapshot
//          read model of one live indoor farm. The same singleton also implements the Core
//          IBoxFarmQuery; UI consumes only the Core DTO through this seam and never a controller
//          type. Write actions stay on the Core IBoxFarmCommand singleton (mode/maintenance) and
//          on its v2 members (refill / crop policy), which UI resolves by name.
// Requirements: REQ-V2-UI-01, REQ-V2-WATER-03, REQ-V2-SAFE-01/04
// ============================================================================
using System;
using System.Collections.Generic;
using HDBF.Core.Abstractions;
using HDBF.Core.Indoor;

namespace HDBF.Gameplay.Components
{
    /// <summary>
    /// Gameplay-side query contract declared by HDBF.Gameplay (docs/INTERFACES.md), kept for
    /// callers that prefer the gameplay namespace. It is API-identical to
    /// <see cref="IBoxFarmQuery"/> for the v1 snapshot and adds the v2 indoor read model, so the
    /// entity panel never needs a gameplay implementation type.
    /// </summary>
    public interface IHdbfFarmQueryService
    {
        /// <summary>Returns false when the entity is not a box farm.</summary>
        bool TryGetSnapshot(Guid entityId, out BoxFarmSnapshot snapshot);

        /// <summary>Snapshots of every live box farm.</summary>
        IReadOnlyList<BoxFarmSnapshot> AllSnapshots();

        /// <summary>
        /// Current v2 panel snapshot of one live indoor farm (INTERFACES section 9), or null when
        /// the entity is unknown, not registered/active, or its snapshot threw. Never throws; this
        /// is the read path the entity panel uses (REQ-V2-UI-01: UI reads DTOs, not objects).
        /// </summary>
        /// <param name="entityId">Runtime entity id of the selected building.</param>
        /// <returns>Immutable Core DTO, or null when no data is available right now.</returns>
        IndoorFarmSnapshot? GetIndoorSnapshot(Guid entityId);

        /// <summary>
        /// Resolves the live state source of one farm (its controller). Used by the ADR-009 growth
        /// lookup: plot id → owning farm → per-plot growth-rate multiplier. Returns false for an
        /// unknown entity id; never throws.
        /// </summary>
        bool TryGetSource(Guid entityId, out IHdbfBoxFarmSnapshotSource source);
    }
}
