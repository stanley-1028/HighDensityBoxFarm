using System;
using HDBF.Core.Indoor;

namespace HDBF.Core.Abstractions
{
    /// <summary>
    /// Read-only farm query contract (chapter 9.2/19.11). Implemented by gameplay, consumed by UI
    /// and diagnostics; the implementation type must never leak through this seam.
    ///
    /// The identifier is the runtime entity id because UI lookups start from a selected entity.
    /// The returned DTO is the frozen Core <see cref="IndoorFarmSnapshot"/>; v2 has no world plot
    /// snapshot and no plot registry (REQ-V2-CORE-03).
    /// </summary>
    public interface IBoxFarmQuery
    {
        /// <summary>
        /// Returns the Core panel snapshot of one live indoor farm, or <c>null</c> when the entity
        /// is unknown, not registered or its snapshot failed (the panel hides its rows then).
        /// </summary>
        /// <param name="entityId">Runtime entity id of the selected building.</param>
        /// <returns>Immutable snapshot, or null when no data is available right now.</returns>
        IndoorFarmSnapshot? GetIndoorSnapshot(Guid entityId);
    }
}
