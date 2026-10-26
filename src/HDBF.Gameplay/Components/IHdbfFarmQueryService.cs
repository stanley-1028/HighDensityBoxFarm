using System;
using System.Collections.Generic;
using HDBF.Core.Abstractions;

namespace HDBF.Gameplay.Components
{
    /// <summary>
    /// Gameplay-side query contract declared by HDBF.Gameplay (docs/INTERFACES.md), kept for
    /// callers that prefer the gameplay namespace. It is API-identical to
    /// <see cref="IBoxFarmQuery"/> and the same singleton implements both, so UI can consume the
    /// Core interface without ever referencing this assembly (chapter 9.2).
    /// </summary>
    public interface IHdbfFarmQueryService
    {
        /// <summary>Returns false when the entity is not a box farm.</summary>
        bool TryGetSnapshot(Guid entityId, out BoxFarmSnapshot snapshot);

        /// <summary>Snapshots of every live box farm.</summary>
        IReadOnlyList<BoxFarmSnapshot> AllSnapshots();

        /// <summary>
        /// Resolves the live state source of one farm (its controller). Used by the ADR-009 growth
        /// lookup: plot id → owning farm → per-plot growth-rate multiplier. Returns false for an
        /// unknown entity id; never throws.
        /// </summary>
        bool TryGetSource(Guid entityId, out IHdbfBoxFarmSnapshotSource source);
    }
}
