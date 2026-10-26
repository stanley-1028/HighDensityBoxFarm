using System;

namespace HDBF.Core.Balance
{
    /// <summary>
    /// Single source for maintenance-cycle selection and for the safe defaults of a missing or
    /// unusable persisted maintenance date (requirement chapters 6.5, 9.4 "缺失字段安全默认值"
    /// and 12.2 <c>maintenance.dueDay</c> / <c>maintenance.cyclesMissed</c>).
    ///
    /// Every number is derived from the config-resolved <see cref="TierProfile"/>: the top tier
    /// has no on-grid material cycle (<c>MaintenanceCycleDays = 0</c>, connected power maintains
    /// it automatically) but a defined off-grid cycle (15 metal / 8 days), the intermediate and
    /// advanced tiers have a 10-day material cycle, and the basic tier has no material
    /// maintenance at all. This type must never hard-code those values: editing the tier data or
    /// a JSON balance override has to change the derived defaults with it (REQ-IMPL-03).
    ///
    /// The two cycle flavours are deliberately distinct:
    /// * <see cref="CycleDaysFor(TierProfile?, bool)"/> is the cycle of the *current* power state,
    ///   used by the once-per-day simulation step;
    /// * <see cref="DefaultCycleDays(TierProfile?)"/> is the conservative load-time default, used
    ///   when the save carries no usable due date. Load happens before the mechanical network
    ///   state is resolved, so the off-grid material cycle is the safe choice: it never derates a
    ///   building straight after a load (requirement: "避免读档即停摆").
    ///
    /// Pure, game-free and side-effect free so L1 can pin it without the game assemblies.
    /// </summary>
    public static class MaintenanceDefaults
    {
        /// <summary>
        /// Cycle length in game days for the given live power state. Returns 0 for the
        /// irrigation-only tier and for any non-positive/non-finite cycle value, which callers
        /// treat as "no material maintenance to track".
        /// </summary>
        public static float CycleDaysFor(TierProfile? profile, bool offGrid)
        {
            if (profile is null || profile.MaintenanceKind == HdbfMaintenanceKind.IrrigationOnly)
            {
                return 0f;
            }

            var cycle = offGrid && IsPositiveFinite(profile.OffGridMaintenanceCycleDays)
                ? profile.OffGridMaintenanceCycleDays
                : profile.MaintenanceCycleDays;
            return SanitizeCycle(cycle);
        }

        /// <summary>
        /// Conservative one-cycle length for a record whose maintenance date is missing or
        /// unusable. The top tier falls back to its off-grid material cycle (8 days) because its
        /// on-grid cycle is 0 by design; the other maintenance tiers use their regular cycle.
        /// </summary>
        public static float DefaultCycleDays(TierProfile? profile)
        {
            if (profile is null || profile.MaintenanceKind == HdbfMaintenanceKind.IrrigationOnly)
            {
                return 0f;
            }

            var cycle = profile.MaintenanceKind == HdbfMaintenanceKind.PowerOrOffGridMetal
                && IsPositiveFinite(profile.OffGridMaintenanceCycleDays)
                ? profile.OffGridMaintenanceCycleDays
                : profile.MaintenanceCycleDays;
            return SanitizeCycle(cycle);
        }

        /// <summary>
        /// True when a persisted due date can be trusted as-is. A zero due date is the documented
        /// "no material maintenance" value of the irrigation-only tier; for every maintenance tier
        /// it means "never scheduled", i.e. missing, and must be replaced by the safe default.
        /// Negative/non-finite values are never usable.
        /// </summary>
        public static bool IsUsableDueDay(TierProfile? profile, float dueDay)
        {
            if (IsNaNOrInfinity(dueDay))
            {
                return false;
            }

            if (dueDay > 0f)
            {
                return true;
            }

            return dueDay == 0f
                && (profile is null || profile.MaintenanceKind == HdbfMaintenanceKind.IrrigationOnly);
        }

        /// <summary>
        /// The due day to persist after a load: the saved value verbatim when it is usable, so a
        /// reload can never restart the maintenance countdown ("免費維護"). Otherwise one full
        /// cycle from <paramref name="currentDay"/>, as requirement 12.1/12.2 demands; a profile
        /// without a material cycle (basic tier) stays at 0.
        /// </summary>
        public static float ResolveDueDay(TierProfile? profile, float currentDay, float savedDueDay)
        {
            if (IsUsableDueDay(profile, savedDueDay))
            {
                return savedDueDay;
            }

            if (profile is null || profile.MaintenanceKind == HdbfMaintenanceKind.IrrigationOnly)
            {
                return 0f;
            }

            var cycleDays = DefaultCycleDays(profile);
            if (cycleDays <= 0f || IsNaNOrInfinity(currentDay))
            {
                return 0f;
            }

            var dueDay = currentDay + cycleDays;
            return IsNaNOrInfinity(dueDay) ? 0f : dueDay;
        }

        private static float SanitizeCycle(float cycleDays)
        {
            return IsPositiveFinite(cycleDays) ? cycleDays : 0f;
        }

        private static bool IsPositiveFinite(float value) => value > 0f && !IsNaNOrInfinity(value);

        private static bool IsNaNOrInfinity(float value) => float.IsNaN(value) || float.IsInfinity(value);
    }
}
