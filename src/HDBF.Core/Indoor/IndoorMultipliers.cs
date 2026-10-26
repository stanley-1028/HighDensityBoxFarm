// ============================================================================
// HDBF v2 | Layer: Core/Indoor | Owner: V2-CORE-01
// Purpose: The single aggregation point of the G_irr / G_maint / G_power layers
//          (REQ-V2-BAL-01) plus the shared numeric guards used across Core/Indoor.
//          No other file may multiply these three layers.
// Requirements: REQ-V2-BAL-01, REQ-V2-SAFE-01, REQ-V2-READ-02
// ============================================================================
namespace HDBF.Core.Indoor
{
    /// <summary>
    /// The three growth-rate layers of one day (INTERFACES section 5). <see cref="Growth"/> is the
    /// only place where <c>G_growth = G_irr x G_maint x G_power</c> is multiplied (REQ-V2-BAL-01).
    /// </summary>
    /// <param name="Irrigation">G_irr from tank coverage and the tier minimum coverage, 0..1.</param>
    /// <param name="Maintenance">G_maint from missed maintenance cycles, 0/0.5/1.</param>
    /// <param name="Power">G_power from the power contract, 0.6/1.</param>
    public readonly record struct IndoorMultipliers(float Irrigation, float Maintenance, float Power)
    {
        /// <summary>
        /// G_growth = G_irr x G_maint x G_power, each layer clamped to [0,1]. This is the single
        /// multiplication point required by REQ-V2-BAL-01; every component is sanitized so a NaN
        /// from a damaged save can never freeze the whole farm.
        /// </summary>
        public float Growth =>
            IndoorMath.Clamp01(Irrigation) * IndoorMath.Clamp01(Maintenance) * IndoorMath.Clamp01(Power);
    }

    /// <summary>
    /// Shared numeric guards for <c>HDBF.Core.Indoor</c>. Every formula clamps instead of throwing
    /// because its inputs sit on the trust boundary between save/config data and the simulation
    /// (REQ-V2-SAFE-01, section 19.18.3).
    /// </summary>
    internal static class IndoorMath
    {
        /// <summary>Clamps to [0,1]; NaN and negative values become 0, values above 1 become 1.</summary>
        internal static float Clamp01(float value)
        {
            if (float.IsNaN(value) || value <= 0f)
            {
                return 0f;
            }

            return value >= 1f ? 1f : value;
        }

        /// <summary>True when the value is neither NaN nor infinite.</summary>
        internal static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        /// <summary>True when the value is finite and strictly positive.</summary>
        internal static bool IsPositiveFinite(float value) => value > 0f && IsFinite(value);
    }
}
