// ============================================================================
// HDBF v2 | Layer: Core/Indoor | Owner: V2-CORE-01
// Purpose: Maintenance material source abstraction (nearest stockpile transaction,
//          INTERFACES section 6). Core asks; the gameplay bridge withdraws.
// Requirements: REQ-V2-SAVE-04, REQ-V2-EXT-02, REQ-V2-BAL-02
// ============================================================================
namespace HDBF.Core.Indoor
{
    /// <summary>
    /// Read/consume contract of the nearest stockpile that pays material maintenance (section
    /// 19.8). A missing or throwing source is treated as "no material available", never as a crash.
    /// </summary>
    public interface IMaintenanceMaterialSource
    {
        /// <summary>Returns the currently available, unreserved amount of one good.</summary>
        /// <param name="itemId">Vanilla good id, e.g. "Plank" or "MetalBlock".</param>
        /// <returns>Available item count; 0 when unknown.</returns>
        int Available(string itemId);

        /// <summary>
        /// Consumes the given amount of one good, all-or-nothing.
        /// </summary>
        /// <param name="itemId">Vanilla good id.</param>
        /// <param name="count">Requested item count.</param>
        /// <returns>True when the full amount was withdrawn.</returns>
        bool TryConsume(string itemId, int count);
    }
}
