// ============================================================================
// HDBF v2 | Layer: Core/Indoor | Owner: V2-CORE-01
// Purpose: One abstract indoor growing slot (INTERFACES section 3). Pure data;
//          no world cell, no plot id, no registry entry, no coordinate.
// Requirements: REQ-V2-CORE-01, REQ-V2-CORE-03, REQ-V2-SAVE-04
// ============================================================================
namespace HDBF.Core.Indoor
{
    /// <summary>
    /// One abstract growing slot inside a box farm building (INTERFACES section 3).
    /// Slots are indexed 0..Capacity-1 and never map to a world cell (REQ-V2-CORE-03).
    /// </summary>
    public sealed class IndoorSlot
    {
        /// <summary>Crop template id of the current crop; empty string means the slot is empty.</summary>
        public string CropId { get; set; } = string.Empty;

        /// <summary>Growth progress in game days, 0..GrowthTimeInDays (clamped by the growth step).</summary>
        public float GrowthDays { get; set; }

        /// <summary>Soil fatigue of this slot, 0..1; only reduces yield, never growth speed.</summary>
        public float Fatigue { get; set; }

        /// <summary>Previous crop template id, the rotation basis; empty string means "no previous crop".</summary>
        public string LastCropId { get; set; } = string.Empty;

        /// <summary>Current stage of the slot state machine.</summary>
        public SlotStage Stage { get; set; }

        /// <summary>Bit flags; bit 0 = reserved by a worker, other bits reserved for future use.</summary>
        public byte Flags { get; set; }

        /// <summary>True when the slot holds no crop at all (<see cref="SlotStage.Empty"/>).</summary>
        public bool IsEmpty => Stage == SlotStage.Empty;
    }
}
