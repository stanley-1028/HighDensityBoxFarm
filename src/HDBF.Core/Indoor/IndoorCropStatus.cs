// ============================================================================
// HDBF v2 | Layer: Core/Indoor | Owner: V2-CORE-01
// Purpose: One row of the panel's live "growing now" card: a crop that currently
//          holds at least one slot, with its slot split and average growth progress.
//          Pure data; no world cell, no game type.
// Requirements: REQ-V2-UI-05, REQ-V2-CORE-04, REQ-V2-READ-04
// ============================================================================
namespace HDBF.Core.Indoor
{
    /// <summary>
    /// Live status of one crop that currently occupies slots of an indoor farm
    /// (REQ-V2-UI-05). The panel shows one row per definition, always from the very first
    /// planted slot, so a partially planted farm is visible instead of only a full one.
    /// </summary>
    /// <param name="CropId">Crop template id, e.g. "Carrot"; never empty.</param>
    /// <param name="YieldItemId">Good id the harvest produces; empty when the spec is unknown.</param>
    /// <param name="GrowingSlots">Slots still growing this crop.</param>
    /// <param name="MatureSlots">Slots of this crop waiting for harvest.</param>
    /// <param name="AverageProgress">
    /// Mean growth progress in [0,1] over every occupied slot of this crop; a mature slot counts
    /// as 1 because it already reached its growth time.
    /// </param>
    /// <param name="GrowthTimeInDays">
    /// Growth time of the crop in game days; 0 when the spec is unknown (progress then reads 0).
    /// </param>
    public sealed record IndoorCropStatus(
        string CropId,
        string YieldItemId,
        int GrowingSlots,
        int MatureSlots,
        float AverageProgress,
        float GrowthTimeInDays)
    {
        /// <summary>Occupied slots of this crop: growing plus waiting for harvest.</summary>
        public int TotalSlots => GrowingSlots + MatureSlots;

        /// <summary>True when at least one slot holds this crop (always true for a listed row).</summary>
        public bool HasCrop => TotalSlots > 0;
    }
}
