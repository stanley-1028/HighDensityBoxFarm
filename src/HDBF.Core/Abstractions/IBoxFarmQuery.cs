using System;
using System.Collections.Generic;

namespace HDBF.Core.Abstractions
{
    /// <summary>
    /// Read-only farm query contract (chapter 9.2 "IBoxFarmQuery"). Implemented by gameplay,
    /// consumed by UI and diagnostics; the implementation type must never leak through this seam.
    ///
    /// The identifier is the entity id because UI lookups start from a selected entity. Save
    /// records keep their own numeric id, so this contract never dictates save layout.
    /// </summary>
    public interface IBoxFarmQuery
    {
        /// <summary>Returns false (and a default snapshot) when the entity is not a box farm.</summary>
        bool TryGetSnapshot(Guid entityId, out BoxFarmSnapshot snapshot);

        /// <summary>
        /// Snapshot of every live box farm. Implementations may cache; callers must treat the
        /// returned list as read-only and may hold it only for the current frame.
        /// </summary>
        IReadOnlyList<BoxFarmSnapshot> AllSnapshots();
    }
}
