// ============================================================================
// HDBF v2 | Layer: Core/Indoor | Owner: V2-CORE-01
// Purpose: One selectable crop of the panel's fixed-crop dropdown: the live crop id plus the
//          display-name localization key read from the game blueprint (L-12). Pure data; no game
//          type and no localization dependency, so the panel stays testable offline.
// Requirements: REQ-V2-EXT-01, REQ-V2-UI-03, REQ-V2-READ-04
// ============================================================================
namespace HDBF.Core.Indoor
{
    /// <summary>
    /// One crop the building faction can select (L-12). Gameplay fills the panel snapshot from the
    /// live crop catalog, so a crop added by a crop mod appears in the dropdown; the panel resolves
    /// <see cref="DisplayNameLocKey"/> through the game localization, so a mod crop shows its own
    /// name instead of its raw template id.
    /// </summary>
    public sealed record IndoorCropChoice
    {
        /// <summary>Crop template id, e.g. "Carrot"; never empty for a listed choice.</summary>
        public string Id { get; init; } = string.Empty;

        /// <summary>
        /// Localization key of the crop display name, or null/empty when the blueprint has no
        /// <c>LabeledEntitySpec</c>. The panel then falls back to its built-in ten-crop name table
        /// and finally to the raw id (section 19.5).
        /// </summary>
        public string? DisplayNameLocKey { get; init; }
    }
}
