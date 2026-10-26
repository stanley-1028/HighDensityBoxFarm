// ============================================================================
// HDBF v2 | Layer: Core/Indoor | Owner: V2-GAMEPLAY-02
// Purpose: The four world-icon classes of v2 (section 19.11: water shortage, maintenance
//          overdue, power loss, blocked entrance) and the pure key->class mapping shared by
//          the gameplay icon service and by L1 tests. Game-free; no sprites, no UI types.
// Requirements: REQ-V2-BAL-03, REQ-V2-WATER-03, REQ-V2-UI-02, REQ-V2-COMPAT-02
// ============================================================================
using HDBF.Core.Warnings;

namespace HDBF.Core.Indoor
{
    /// <summary>
    /// World-space icon classes of v2. Exactly four visuals keep an icon (section 19.11); every
    /// other warning is panel-only. The frozen v1 icon slots are collapsed deliberately:
    /// water uses the v1 "needs water" sprite for both NoWater and low-coverage warnings,
    /// maintenance uses the v1 shutdown slot, and a blocked entrance reuses the unconnected slot.
    /// </summary>
    public enum IndoorWarningIconKind
    {
        /// <summary>No world icon.</summary>
        None = 0,

        /// <summary>Water shortage / low coverage ("BuildingNeedsWater" sprite).</summary>
        Water = 1,

        /// <summary>Maintenance overdue or stalled ("UnspecifiedRecipe" sprite).</summary>
        Maintenance = 2,

        /// <summary>Top tier power cut ("NoPower" sprite).</summary>
        NoPower = 3,

        /// <summary>Blocked entrance ("UnconnectedBuilding" sprite).</summary>
        EntranceBlocked = 4,
    }

    /// <summary>
    /// Pure mapping from a warning localization key to one of the four v2 icon classes. The keys
    /// are the ones the shared warning mapper emits into the retained snapshot (v1 keys where the
    /// category is unchanged); unknown/panel-only keys map to <see cref="IndoorWarningIconKind.None"/>
    /// and therefore never draw a world icon.
    /// </summary>
    public static class IndoorWarningMap
    {
        /// <summary>Number of distinct world-icon classes (excluding None).</summary>
        public const int WorldIconKindCount = 4;

        /// <summary>Highest valid <see cref="IndoorWarningIconKind"/> value (fixed array size).</summary>
        public const int MaxWorldIconKind = (int)IndoorWarningIconKind.EntranceBlocked;

        /// <summary>Warning key of a blocked entrance (shipped in all three localizations).</summary>
        public const string EntranceBlockedKey = "HDBF.Warning.EntranceBlocked";

        /// <summary>World-icon class of one warning key; unknown keys map to None.</summary>
        /// <param name="codeKey">Warning key of a retained <c>FarmWarning</c> row.</param>
        /// <returns>One of the four v2 icon classes, or <see cref="IndoorWarningIconKind.None"/>.</returns>
        public static IndoorWarningIconKind IconKindOf(string? codeKey)
        {
            switch (codeKey)
            {
                case HdbfWarningKeys.NoWater:
                case HdbfWarningKeys.IrrigationLow:
                    return IndoorWarningIconKind.Water;
                case HdbfWarningKeys.Shutdown:
                case HdbfWarningKeys.MaintenanceOverdue:
                    return IndoorWarningIconKind.Maintenance;
                case HdbfWarningKeys.NoPower:
                    return IndoorWarningIconKind.NoPower;
                case HdbfWarningKeys.AdapterFailed:
                case EntranceBlockedKey:
                    return IndoorWarningIconKind.EntranceBlocked;
                default:
                    return IndoorWarningIconKind.None;
            }
        }

        /// <summary>True when the key is one of the four v2 world-icon classes.</summary>
        /// <param name="codeKey">Warning key of a retained <c>FarmWarning</c> row.</param>
        /// <returns>True when the key draws a world icon.</returns>
        public static bool HasWorldIcon(string? codeKey) =>
            IconKindOf(codeKey) != IndoorWarningIconKind.None;

        /// <summary>Fixed array index of an icon class (0..MaxWorldIconKind); None maps to 0.</summary>
        /// <param name="kind">Icon class.</param>
        /// <returns>The stable array slot of the class.</returns>
        public static int SlotOf(IndoorWarningIconKind kind)
        {
            var value = (int)kind;
            return value < 0 || value > MaxWorldIconKind ? 0 : value;
        }
    }
}
