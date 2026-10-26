// ============================================================================
// HDBF v2 | Layer: Core/Indoor | Owner: V2-CORE-01
// Purpose: Read-only snapshot provider for UI and tests (INTERFACES section 6/9).
//          UI layers depend on this instead of reading game objects.
// Requirements: REQ-V2-READ-04, REQ-V2-EXT-02
// ============================================================================
namespace HDBF.Core.Indoor
{
    /// <summary>
    /// Read-only view of one indoor farm for UI and tests (INTERFACES section 6): the panel reads
    /// only <see cref="Snapshot"/> and never touches game objects directly.
    /// </summary>
    public interface IIndoorFarmState
    {
        /// <summary>Current immutable panel snapshot; implementations must not return null.</summary>
        IndoorFarmSnapshot Snapshot { get; }
    }
}
