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
    public sealed record CropDefinition(string Id, float GrowthTimeInDays, string YieldItemId, int BaseYield)
    {
        /// <summary>
        /// Localization key of the crop display name read from the blueprint's
        /// <c>LabeledEntitySpec.DisplayNameLocKey</c> (L-12). Kept as an init property, not a
        /// positional parameter, so the existing constructors and the built-in fallback table keep
        /// compiling. Null/empty when the spec is missing: the panel then uses its built-in name
        /// table (section 19.5) instead of showing a raw key.
        /// </summary>
        public string? DisplayNameLocKey { get; init; }
    }
}
