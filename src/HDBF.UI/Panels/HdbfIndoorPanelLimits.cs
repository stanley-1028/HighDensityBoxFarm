// ============================================================================
// HDBF v2 | Layer: UI/Panels | Owner: V2-UI-01
// Purpose: Numeric helpers of the v2 indoor panel: safe 0..1 fill ratios for the vanilla progress
//          bars, the read-only "—" rule for not-ready values and the tier-scaled output-inventory
//          capacity the snapshot does not carry. Pure BCL math; no game type.
// Requirements: REQ-V2-UI-01/02/03, REQ-V2-READ-04, REQ-V2-WATER-03
// ============================================================================
using HDBF.Core.Indoor;

namespace HDBF.UI.Panels
{
    /// <summary>
    /// Small numeric/limit helpers behind <see cref="HdbfIndoorPanelFactory"/>. Keeping them here
    /// avoids magic numbers in the mapping and makes the values assertable from L1.
    /// </summary>
    public static class HdbfIndoorPanelLimits
    {
        /// <summary>
        /// Capacity of the vanilla simple output inventory of a tier. The snapshot DTO (INTERFACES
        /// section 9) carries the occupied amount but not the capacity, so the panel derives it from
        /// the tier's slot capacity with the shared Core rule; the shipped
        /// <c>SimpleOutputInventorySpec.Capacity</c> is pinned to the same number by a contract test,
        /// so data and code can never drift silently.
        /// </summary>
        /// <param name="slotCapacity">Slot capacity of the tier (snapshot <c>Capacity</c>).</param>
        /// <returns>Per-good output inventory capacity; 0 when the tier is unknown.</returns>
        public static int OutputCapacity(int slotCapacity)
        {
            return IndoorConstants.OutputCapacityFor(slotCapacity);
        }

        /// <summary>Clamps a fill ratio into [0,1]; NaN/negative become 0, above 1 becomes 1.</summary>
        /// <param name="value">Raw ratio.</param>
        /// <returns>Safe progress-bar fill.</returns>
        public static float Fill(float value)
        {
            if (float.IsNaN(value) || value <= 0f)
            {
                return 0f;
            }

            return value >= 1f ? 1f : value;
        }

        /// <summary>
        /// Ratio of two non-negative quantities, clamped to [0,1]. Zero/NaN denominators return 0 so
        /// a not-ready snapshot never draws a NaN progress bar.
        /// </summary>
        /// <param name="value">Current amount (e.g. tank units).</param>
        /// <param name="maximum">Denominator (e.g. tank capacity).</param>
        /// <returns>Safe progress-bar fill.</returns>
        public static float Ratio(float value, float maximum)
        {
            if (float.IsNaN(value) || float.IsNaN(maximum) || value <= 0f || maximum <= 0f)
            {
                return 0f;
            }

            return Fill(value / maximum);
        }

        /// <summary>Ratio of two counts clamped to [0,1]; a zero denominator returns 0.</summary>
        /// <param name="value">Current count.</param>
        /// <param name="maximum">Denominator count.</param>
        /// <returns>Safe progress-bar fill.</returns>
        public static float Ratio(int value, int maximum)
        {
            if (value <= 0 || maximum <= 0)
            {
                return 0f;
            }

            return value >= maximum ? 1f : (float)value / maximum;
        }

        /// <summary>
        /// Bounded output-inventory fill. The snapshot has no output capacity; the panel uses the
        /// tier-scaled blueprint capacity so "0/2400" and the bar stay readable instead of being cut
        /// off.
        /// </summary>
        /// <param name="occupied">Items currently held by the output inventory.</param>
        /// <param name="slotCapacity">Slot capacity of the tier (snapshot <c>Capacity</c>).</param>
        /// <returns>Fill ratio in [0,1].</returns>
        public static float OutputFill(int occupied, int slotCapacity)
        {
            return Ratio(occupied, OutputCapacity(slotCapacity));
        }
    }
}
