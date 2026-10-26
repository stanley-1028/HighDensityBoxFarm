// ============================================================================
// HDBF v2 | Layer: Core/Indoor | Owner: V2-CORE-01
// Purpose: G_maint from missed maintenance cycles, the material-cost multiplier of a
//          power cut, and the tier/record material lookup the daily tick needs
//          (INTERFACES section 5, section 19.8). Pure functions.
// Requirements: REQ-V2-BAL-01, REQ-V2-BAL-02, REQ-V2-CORE-04
// ============================================================================
using HDBF.Core.Balance;

namespace HDBF.Core.Indoor
{
    /// <summary>
    /// Maintenance mathematics of an indoor farm (INTERFACES section 5). Missed cycles only derate
    /// or stall farm actions; they never stop crop growth (REQ-V2-BAL-02).
    /// </summary>
    public static class IndoorMaintenanceMath
    {
        /// <summary>
        /// G_maint: 0 cycles missed -> 1.0, exactly 1 -> 0.5, 2 or more -> 0 (stalled), section 19.8.
        /// </summary>
        /// <param name="cyclesMissed">Maintenance cycles missed according to the due-day formula.</param>
        /// <returns>Maintenance growth multiplier.</returns>
        public static float Efficiency(int cyclesMissed)
        {
            if (cyclesMissed <= 0)
            {
                return IndoorConstants.NormalModeMultiplier;
            }

            if (cyclesMissed == 1)
            {
                return IndoorConstants.OneCycleMissedEfficiency;
            }

            return 0f;
        }

        /// <summary>True when the farm stalls farm actions (two or more missed cycles).</summary>
        /// <param name="cyclesMissed">Maintenance cycles missed.</param>
        /// <returns>True from <see cref="IndoorConstants.StalledAfterCycles"/> missed cycles on.</returns>
        public static bool IsStalled(int cyclesMissed)
        {
            return cyclesMissed >= IndoorConstants.StalledAfterCycles;
        }

        /// <summary>
        /// Material consumption multiplier of a power cut: 1.0 while the upkeep state is normal,
        /// 2.0 when a power tier lost its grid supply (section 19.8 "维护消耗 x2").
        /// </summary>
        /// <param name="powerConnected">True when no power cutoff applies (connected or off-grid).</param>
        /// <returns>Material maintenance multiplier.</returns>
        public static float MaintenanceMaterialMultiplier(bool powerConnected)
        {
            return powerConnected ? IndoorConstants.NormalModeMultiplier : IndoorConstants.PowerCutMaintenanceMultiplier;
        }

        /// <summary>Material good id of the current maintenance path; null when no material is due.</summary>
        internal static string? MaterialItemId(TierProfile tier, IndoorFarmRecord record)
        {
            switch (tier.MaintenanceKind)
            {
                case HdbfMaintenanceKind.Plank:
                    return IndoorConstants.MaintenanceItemPlank;
                case HdbfMaintenanceKind.Metal:
                    return IndoorConstants.MaintenanceItemMetal;
                case HdbfMaintenanceKind.PowerOrOffGridMetal:
                    return record.PowerConnected && record.PowerMode == PowerConnection.Grid
                        ? null
                        : IndoorConstants.MaintenanceItemMetal;
                default:
                    return null;
            }
        }

        /// <summary>Material count of one maintenance cycle, including the power-cut x2 penalty.</summary>
        internal static int MaterialAmount(TierProfile tier, IndoorFarmRecord record)
        {
            switch (tier.MaintenanceKind)
            {
                case HdbfMaintenanceKind.Plank:
                case HdbfMaintenanceKind.Metal:
                    return tier.MaintenanceMaterialCount;
                case HdbfMaintenanceKind.PowerOrOffGridMetal:
                    if (record.PowerConnected && record.PowerMode == PowerConnection.Grid)
                    {
                        return 0;
                    }

                    var multiplier = MaintenanceMaterialMultiplier(
                        record.PowerConnected || record.PowerMode == PowerConnection.OffGrid);
                    return (int)(tier.OffGridMaintenanceMaterial * multiplier);
                default:
                    return 0;
            }
        }

        /// <summary>True when the tier requires material and the stockpile cannot pay it.</summary>
        internal static bool IsMaterialMissing(IMaintenanceMaterialSource? materials, TierProfile tier,
            IndoorFarmRecord record)
        {
            var itemId = MaterialItemId(tier, record);
            if (itemId is null)
            {
                return false;
            }

            var amount = MaterialAmount(tier, record);
            if (amount <= 0)
            {
                return false;
            }

            if (materials is null)
            {
                return true;
            }

            try
            {
                return materials.Available(itemId) < amount;
            }
            catch
            {
                return true;
            }
        }
    }
}
