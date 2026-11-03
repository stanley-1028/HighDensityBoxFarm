// ============================================================================
// HDBF v2 | Layer: UI/Localization | Owner: V2-UI-01
// Purpose: Second half of the v2 panel localization keys (WP-08 refill-all and the honest
//          settlement-wide pipe-supply label). Split from HdbfUiLocKeys.cs so both files stay
//          inside the 300-line gate (REQ-V2-MAINT-02); it is the same constant table.
// Requirements: REQ-V2-UI-02/03, REQ-V2-WATER-04, REQ-V2-MAINT-02
// ============================================================================
namespace HDBF.UI.Localization
{
    public static partial class HdbfUiLocKeys
    {
        /// <summary>WP-08: settlement-wide "refill every indoor farm once" action button.</summary>
        public const string ActionRefillAll = "HDBF.Action.RefillAll";

        /// <summary>WP-08: result line after the refill-all action; {0} = farms that received water.</summary>
        public const string ActionRefillAllDone = "HDBF.Action.RefillAllDone";

        /// <summary>WP-08: tooltip of the refill-all action (per-farm action cost, 12-cell source).</summary>
        public const string TooltipRefillAll = "HDBF.Tooltip.RefillAll";

        /// <summary>L-02: the pipe supply is settlement-wide (any connected vanilla liquid tank feeds every farm).</summary>
        public const string PortGlobalSupply = "HDBF.Port.GlobalSupply";

        /// <summary>WP-08: no live indoor farm exists, so the refill-all action has nothing to do.</summary>
        public const string ErrorNoFarms = "HDBF.Error.NoFarms";
    }
}
