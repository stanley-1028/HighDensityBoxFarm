// ============================================================================
// HDBF v2 | Layer: Core/Indoor | Owner: V2-CORE-03
// Purpose: One pure "why is this farm not working" reason for the panel status
//          line (WP-06 / L-03, REQ-V2-UI-01): a single priority order over the
//          immutable snapshot plus its localization key. No game references.
// Requirements: REQ-V2-UI-01, REQ-V2-READ-04, REQ-V2-BAL-02
// ============================================================================
using System;

namespace HDBF.Core.Indoor
{
    /// <summary>
    /// Single highest-priority reason an indoor farm is not doing farm work (WP-06 / L-03). The
    /// panel shows exactly one of these so a stalled farm never looks "all green"; the numeric
    /// values are part of the v2 panel contract and must not be renumbered.
    /// </summary>
    public enum IndoorIdleReason
    {
        /// <summary>Nothing is wrong: the farm is working (or has nothing to do).</summary>
        None = 0,

        /// <summary>The player paused the building; the whole simulation is stopped.</summary>
        Paused = 1,

        /// <summary>No worker is on duty, so nothing is planted or harvested.</summary>
        NoWorkers = 2,

        /// <summary>The front entrance is blocked; farm actions pause until it clears.</summary>
        EntranceBlocked = 3,

        /// <summary>The output inventory is full, so mature slots wait for hauling.</summary>
        OutputFull = 4,

        /// <summary>The tank is empty or below the tier minimum coverage; growth is frozen/slowed.</summary>
        WaterShortage = 5,

        /// <summary>Two or more maintenance cycles were missed; farm actions are stalled.</summary>
        MaintenanceStalled = 6,

        /// <summary>Material maintenance is overdue (growth/labor derated) but not yet stalled.</summary>
        MaintenanceDue = 7,

        /// <summary>The farm deliberately rests; no planting or harvesting happens this mode.</summary>
        Fallow = 8,
    }

    /// <summary>
    /// Pure resolution of the single panel idle reason (WP-06, REQ-V2-UI-01). The priority order is
    /// fixed and observable: Paused, NoWorkers, EntranceBlocked, OutputFull, WaterShortage,
    /// MaintenanceStalled, MaintenanceDue, Fallow, None. Both the world icon and the panel read the
    /// same function so they cannot disagree (L-11 seam).
    /// </summary>
    public static class IndoorIdleReasonMath
    {
        /// <summary>
        /// Resolves the single highest-priority idle reason of a snapshot. A null snapshot is a safe
        /// "no information" value (<see cref="IndoorIdleReason.None"/>), never an exception.
        /// </summary>
        /// <param name="snapshot">Immutable panel snapshot; null returns None.</param>
        /// <returns>The highest-priority reason the farm is idle.</returns>
        public static IndoorIdleReason Resolve(IndoorFarmSnapshot? snapshot)
        {
            if (snapshot is null)
            {
                return IndoorIdleReason.None;
            }

            if (snapshot.Paused)
            {
                return IndoorIdleReason.Paused;
            }

            if (snapshot.Workers <= 0)
            {
                return IndoorIdleReason.NoWorkers;
            }

            if (Has(snapshot.Warnings, IndoorWarningFlags.EntranceBlocked))
            {
                return IndoorIdleReason.EntranceBlocked;
            }

            if (Has(snapshot.Warnings, IndoorWarningFlags.OutputFull))
            {
                return IndoorIdleReason.OutputFull;
            }

            if (HasWaterShortage(snapshot))
            {
                return IndoorIdleReason.WaterShortage;
            }

            if (Has(snapshot.Warnings, IndoorWarningFlags.MaintenanceStalled)
                || IndoorMaintenanceMath.IsStalled(snapshot.MaintenanceCyclesMissed))
            {
                return IndoorIdleReason.MaintenanceStalled;
            }

            if (Has(snapshot.Warnings, IndoorWarningFlags.MaintenanceDue)
                || snapshot.MaintenanceCyclesMissed > 0)
            {
                return IndoorIdleReason.MaintenanceDue;
            }

            if (snapshot.Mode == IndoorMode.Fallow)
            {
                return IndoorIdleReason.Fallow;
            }

            return IndoorIdleReason.None;
        }

        /// <summary>
        /// Localization key of one reason: <c>"HDBF.IdleReason.&lt;EnumName&gt;"</c>
        /// (<see cref="IndoorIdleReason.None"/> maps to <c>HDBF.IdleReason.None</c>). An undefined
        /// value also maps to the None key so a damaged snapshot can never render a raw number.
        /// </summary>
        /// <param name="reason">Reason to localize.</param>
        /// <returns>Stable localization key of the reason.</returns>
        public static string LocalizationKey(IndoorIdleReason reason)
        {
            if (!Enum.IsDefined(typeof(IndoorIdleReason), reason))
            {
                return "HDBF.IdleReason.None";
            }

            return "HDBF.IdleReason." + reason.ToString();
        }

        private static bool HasWaterShortage(IndoorFarmSnapshot snapshot)
        {
            if (Has(snapshot.Warnings, IndoorWarningFlags.NoWater)
                || Has(snapshot.Warnings, IndoorWarningFlags.LowWater))
            {
                return true;
            }

            // Live coverage is the authoritative mid-day signal of the panel; the day-tick flags
            // are only refreshed once per day, so both are consulted (L-11 seam).
            var minimum = snapshot.MinCoverage;
            if (!(minimum > 0f) || float.IsNaN(snapshot.Coverage))
            {
                return false;
            }

            return snapshot.Coverage < minimum;
        }

        private static bool Has(IndoorWarningFlags flags, IndoorWarningFlags expected)
        {
            return (flags & expected) == expected;
        }
    }
}
