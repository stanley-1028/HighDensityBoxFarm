// ============================================================================
// HDBF v2 | Layer: Core/Indoor | Owner: V2-CORE-01
// Purpose: Crop definition record shared by the game-backed catalog and the
//          built-in fallback table (INTERFACES section 6/7).
// Requirements: REQ-V2-EXT-01, REQ-V2-READ-02
// ============================================================================
namespace HDBF.Core.Indoor
{
    /// <summary>
    /// Immutable crop definition read from the vanilla specs (or the fallback table):
    /// growth time, output good id and base yield per harvest (INTERFACES section 7).
    /// </summary>
    /// <param name="Id">Crop template id, e.g. "Carrot".</param>
    /// <param name="GrowthTimeInDays">Game days from planting to maturity; must be positive.</param>
    /// <param name="YieldItemId">Vanilla good id the harvest adds to the output inventory.</param>
    /// <param name="BaseYield">Base item yield per harvest before G_fat and G_mode.</param>
    public sealed record CropDefinition(string Id, float GrowthTimeInDays, string YieldItemId, int BaseYield);
}
