// ============================================================================
// HDBF v2 | Layer: Core/Indoor | Owner: V2-GAMEPLAY-02
// Purpose: The four world-icon classes of v2 (section 19.11: water shortage, maintenance
//          overdue, power loss, blocked entrance), their pure flag->icon/severity mapping and
//          the localization keys the gameplay icon service needs. Game-free; no sprites/UI types.
// Requirements: REQ-V2-BAL-03, REQ-V2-WATER-03, REQ-V2-UI-02, REQ-V2-COMPAT-02
// ============================================================================
using HDBF.Core.Balance;

namespace HDBF.Core.Indoor
{
    /// <summary>Panel priority of one warning row (P0 first), also used for the text marker.</summary>
    public enum IndoorWarningSeverity
    {
        /// <summary>P0: no water / maintenance shutdown; a world icon blinks.</summary>
        Critical = 0,

        /// <summary>P1: low coverage / power cut / blocked entrance.</summary>
        High = 1,

        /// <summary>P2: maintenance due or missing material.</summary>
        Medium = 2,

        /// <summary>P3: informational problem that keeps the farm running.</summary>
        Low = 3,

        /// <summary>P4: lowest priority.</summary>
        Info = 4,
    }

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
    /// Localization keys of the v2 warning categories. UI formats the same keys from its own
    /// constant table; the gameplay icon tooltip reads them through <c>ILoc</c>.
    /// </summary>
    public static class IndoorWarningKeys
    {
        /// <summary>P0 water shortage warning key ("HDBF.Warning.NoWater").</summary>
        public const string NoWater = "HDBF.Warning.NoWater";

        /// <summary>P1 low-coverage warning key ("HDBF.Warning.IrrigationLow").</summary>
        public const string IrrigationLow = "HDBF.Warning.IrrigationLow";

        /// <summary>P0 stalled / P1 due maintenance warning key ("HDBF.Warning.MaintenanceOverdue").</summary>
        public const string MaintenanceOverdue = "HDBF.Warning.MaintenanceOverdue";

        /// <summary>P1 power-cut warning key ("HDBF.Warning.NoPower").</summary>
        public const string NoPower = "HDBF.Warning.NoPower";

        /// <summary>P1 blocked-entrance warning key ("HDBF.Warning.EntranceBlocked").</summary>
        public const string EntranceBlocked = "HDBF.Warning.EntranceBlocked";
    }

    /// <summary>
    /// Pure mapping from the daily <see cref="IndoorWarningFlags"/> to the four v2 world-icon
    /// classes and their panel priority. One farm shows at most one icon: the highest-priority
    /// class wins, so the caller never has to sort warning rows.
    /// </summary>
    public static class IndoorWarningMap
    {
        /// <summary>Number of distinct world-icon classes (excluding None).</summary>
        public const int WorldIconKindCount = 4;

        /// <summary>Highest valid <see cref="IndoorWarningIconKind"/> value (fixed array size).</summary>
        public const int MaxWorldIconKind = (int)IndoorWarningIconKind.EntranceBlocked;

        /// <summary>Highest-priority world-icon class of one flag set; None when no icon applies.</summary>
        /// <param name="flags">Daily warning flags of the farm.</param>
        /// <returns>One of the four v2 icon classes, or <see cref="IndoorWarningIconKind.None"/>.</returns>
        public static IndoorWarningIconKind IconKindOf(IndoorWarningFlags flags)
        {
            if ((flags & IndoorWarningFlags.NoWater) != 0)
            {
                return IndoorWarningIconKind.Water;
            }

            if ((flags & IndoorWarningFlags.LowWater) != 0)
            {
                return IndoorWarningIconKind.Water;
            }

            if ((flags & (IndoorWarningFlags.MaintenanceStalled | IndoorWarningFlags.MaintenanceDue)) != 0)
            {
                return IndoorWarningIconKind.Maintenance;
            }

            if ((flags & IndoorWarningFlags.NoPower) != 0)
            {
                return IndoorWarningIconKind.NoPower;
            }

            if ((flags & IndoorWarningFlags.EntranceBlocked) != 0)
            {
                return IndoorWarningIconKind.EntranceBlocked;
            }

            return IndoorWarningIconKind.None;
        }

        /// <summary>
        /// Panel priority of one flag set (P0 when a critical flag is present). L-10: an unstaffed
        /// farm does nothing at all, so <see cref="IndoorWarningFlags.NoWorkers"/> is P1 (High) like
        /// the other "the farm cannot work" problems - it used to be the lowest priority and read as
        /// "all green". The four-visual world-icon rule is unchanged: NoWorkers has no icon class of
        /// its own (see <see cref="IconKindOf"/>), so the panel/status line carries it.
        /// </summary>
        /// <param name="flags">Daily warning flags of the farm.</param>
        /// <returns>The highest priority the panel must show for these flags.</returns>
        public static IndoorWarningSeverity SeverityOf(IndoorWarningFlags flags)
        {
            if ((flags & (IndoorWarningFlags.NoWater | IndoorWarningFlags.MaintenanceStalled)) != 0)
            {
                return IndoorWarningSeverity.Critical;
            }

            if ((flags & (IndoorWarningFlags.LowWater | IndoorWarningFlags.NoPower
                          | IndoorWarningFlags.EntranceBlocked | IndoorWarningFlags.NoWorkers)) != 0)
            {
                return IndoorWarningSeverity.High;
            }

            if ((flags & (IndoorWarningFlags.MaintenanceDue | IndoorWarningFlags.MissingMaterial)) != 0)
            {
                return IndoorWarningSeverity.Medium;
            }

            if ((flags & IndoorWarningFlags.OutputFull) != 0)
            {
                return IndoorWarningSeverity.Low;
            }

            return IndoorWarningSeverity.Info;
        }

        /// <summary>True when the flag set carries a P0 warning (the icon may blink, section 14.5).</summary>
        /// <param name="flags">Daily warning flags of the farm.</param>
        /// <returns>True for the P0 warning classes.</returns>
        public static bool IsCritical(IndoorWarningFlags flags) =>
            SeverityOf(flags) == IndoorWarningSeverity.Critical;

        /// <summary>Fixed array index of an icon class (0..MaxWorldIconKind); None maps to 0.</summary>
        /// <param name="kind">Icon class.</param>
        /// <returns>The stable array slot of the class.</returns>
        public static int SlotOf(IndoorWarningIconKind kind)
        {
            var value = (int)kind;
            return value < 0 || value > MaxWorldIconKind ? 0 : value;
        }

        /// <summary>Localization key of the sentence behind one icon class (tooltip source).</summary>
        /// <param name="kind">Icon class.</param>
        /// <returns>A shipped <c>HDBF.Warning.*</c> key; never null.</returns>
        public static string WarningKeyOf(IndoorWarningIconKind kind)
        {
            switch (kind)
            {
                case IndoorWarningIconKind.Water:
                    return IndoorWarningKeys.NoWater;
                case IndoorWarningIconKind.Maintenance:
                    return IndoorWarningKeys.MaintenanceOverdue;
                case IndoorWarningIconKind.NoPower:
                    return IndoorWarningKeys.NoPower;
                default:
                    return IndoorWarningKeys.EntranceBlocked;
            }
        }

        /// <summary>
        /// Warning flags re-derived from the live values of one snapshot (L-11/WP-10). The daily
        /// <see cref="IndoorFarmSnapshot.Warnings"/> cache is only refreshed once per game day, which
        /// is why a refilled tank or a hauled output kept its warning (and its world icon) for a
        /// whole day. Deriving coverage, overdue maintenance (against
        /// <see cref="IndoorFarmSnapshot.CurrentDay"/>), power, crew, entrance and output occupancy
        /// from the snapshot itself makes the panel and the world icon react on the next refresh.
        /// <see cref="IndoorWarningFlags.MissingMaterial"/> is the one flag with no live source here
        /// (judging it needs the material stockpile of the daily pass), so it is carried over.
        /// </summary>
        /// <param name="snapshot">Read-only farm snapshot; null carries no flags.</param>
        /// <returns>Live-derived flags of the snapshot.</returns>
        public static IndoorWarningFlags LiveFlagsOf(IndoorFarmSnapshot? snapshot)
        {
            if (snapshot is null)
            {
                return IndoorWarningFlags.None;
            }

            var flags = snapshot.Warnings & IndoorWarningFlags.MissingMaterial;
            // L-10: a farm without a crew does nothing, so this is a real warning, not a low note.
            if (snapshot.Workers <= 0) flags |= IndoorWarningFlags.NoWorkers;
            if (snapshot.EntranceBlocked) flags |= IndoorWarningFlags.EntranceBlocked;

            // Same thresholds the daily settlement uses (and the panel already renders): C = 0 is
            // "no water", 0 < C < C_min is "water low". A zero demand (no crew) reads C = 1.
            var coverage = snapshot.Coverage;
            if (!float.IsNaN(coverage) && !float.IsInfinity(coverage))
            {
                if (coverage <= 0f) flags |= IndoorWarningFlags.NoWater;
                else if (snapshot.MinCoverage > 0f && coverage < snapshot.MinCoverage)
                    flags |= IndoorWarningFlags.LowWater;
            }

            var cycles = LiveMaintenanceCycles(snapshot);
            if (cycles > 0)
            {
                flags |= IndoorWarningFlags.MaintenanceDue;
                if (IndoorMaintenanceMath.IsStalled(cycles))
                {
                    flags |= IndoorWarningFlags.MaintenanceStalled;
                }
            }

            if (snapshot.Power == PowerConnection.Grid && !snapshot.PowerConnected
                && (snapshot.Profile is null || snapshot.Profile.NeedsPower))
            {
                flags |= IndoorWarningFlags.NoPower;
            }

            // OutputFull is live from the occupancy: the moment haulers free room in the buffer the
            // warning clears, instead of waiting for the next day tick to re-evaluate the backlog.
            var outputCapacity = IndoorConstants.OutputCapacityFor(snapshot.Capacity);
            if (outputCapacity > 0 && snapshot.OutputOccupied >= outputCapacity)
                flags |= IndoorWarningFlags.OutputFull;

            return flags;
        }

        /// <summary>
        /// Flags the panel and the world icon must render (L-11): the live-derived set when the
        /// snapshot producer supplied it, otherwise the once-per-day cache. A null snapshot carries
        /// no flags, so callers never have to special-case a missing farm.
        /// </summary>
        /// <param name="snapshot">Read-only farm snapshot; null means "no information".</param>
        /// <returns>The effective warning flags of the snapshot.</returns>
        public static IndoorWarningFlags EffectiveFlags(IndoorFarmSnapshot? snapshot) =>
            snapshot is null ? IndoorWarningFlags.None : snapshot.LiveWarnings ?? snapshot.Warnings;

        /// <summary>
        /// Maintenance cycles of the live day (L-11): the same formula as the daily tick, so the
        /// panel reports "maintenance overdue" the moment the deadline passes instead of on the next
        /// tick. Falls back to the stored counter when the profile/clock is missing (tests, older
        /// gameplay builds), which keeps those snapshots' behaviour unchanged.
        /// </summary>
        /// <param name="snapshot">Read-only farm snapshot.</param>
        /// <returns>Missed maintenance cycles (0 when maintenance does not apply).</returns>
        private static int LiveMaintenanceCycles(IndoorFarmSnapshot snapshot)
        {
            var profile = snapshot.Profile;
            var day = snapshot.CurrentDay;
            if (profile is null || float.IsNaN(day) || float.IsInfinity(day))
                return snapshot.MaintenanceCyclesMissed;
            var offGrid = profile.NeedsPower
                          && !(snapshot.Power == PowerConnection.Grid && snapshot.PowerConnected);
            var cycleDays = MaintenanceDefaults.CycleDaysFor(profile, offGrid);
            var cycles = BalanceFormulas.MaintenanceCyclesMissed(snapshot.MaintenanceDueDay, day, cycleDays);
            return cycles < 0 ? 0 : cycles;
        }
    }
}
