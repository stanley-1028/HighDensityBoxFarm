// ============================================================================
// HDBF v2 | Layer: Core/Indoor | Owner: V2-CORE-01
// Purpose: Output inventory abstraction (vanilla stockpile, conveyor, future
//          warehouse) that Core harvests write into (INTERFACES section 6).
// Requirements: REQ-V2-CORE-01, REQ-V2-EXT-02
// ============================================================================
namespace HDBF.Core.Indoor
{
    /// <summary>
    /// Where harvested goods go (vanilla <c>SimpleOutputInventory</c>, conveyor adapter, future
    /// warehouse). The daily tick never touches global resource counters directly.
    /// </summary>
    public interface IInventorySink
    {
        /// <summary>
        /// Tries to add produce to the output storage.
        /// </summary>
        /// <param name="goodId">Vanilla good id of the produce.</param>
        /// <param name="amount">Item count; always positive for a harvest.</param>
        /// <returns>True when the whole amount was accepted; false means "full, not stored".</returns>
        bool TryAdd(string goodId, int amount);
    }
}
