// ============================================================================
// HDBF v2 | Layer: Gameplay/Components | Owner: V2-GAMEPLAY-02
// Purpose: The all-or-nothing upkeep-material transaction of the "perform maintenance"
//          command (section 19.8): nearest finished-district stockpile, real item ids,
//          no negative deduction, missing-material failure with item+count and the
//          registered indoor.maintenance-missing log key.
// Requirements: REQ-V2-BAL-02, REQ-V2-UI-02, REQ-V2-SAFE-01, REQ-V2-READ-03
// ============================================================================
using System;
using HDBF.Core;
using HDBF.Core.Abstractions;
using HDBF.Core.Indoor;
using HDBF.Core.Logging;
using Timberborn.BaseComponentSystem;
using Timberborn.GameDistricts;
using Timberborn.Goods;
using Timberborn.InventorySystem;
using UnityEngine;

namespace HDBF.Gameplay.Components
{
    /// <summary>
    /// Withdraws one maintenance bill from the closest finished-district inventory that holds the
    /// full, unreserved amount. The transaction is all-or-nothing: a shortage consumes nothing and
    /// reports <see cref="HdbfCommandFailures.MissingMaterials"/> plus a throttled
    /// <c>indoor.maintenance-missing</c> line listing the good and the count (REQ-V2-UI-02).
    /// "Nearest regional/building stockpile" is exactly the finished districts' inventory registry:
    /// every inventory there belongs to a building in a district (warehouses, tanks, storage piles).
    /// </summary>
    internal sealed class HdbfMaintenanceTransaction
    {
        private readonly DistrictCenterRegistry _centers;
        private readonly IHdbfLog _log;

        internal HdbfMaintenanceTransaction(DistrictCenterRegistry centers, IHdbfLog log)
        {
            _centers = centers ?? throw new ArgumentNullException(nameof(centers));
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        /// <summary>
        /// Consumes <paramref name="amount"/> of <paramref name="itemId"/> from the nearest usable
        /// stockpile. Returns true when nothing had to be paid or the full amount was withdrawn.
        /// </summary>
        /// <param name="farm">Farm whose position defines "nearest".</param>
        /// <param name="entityId">Farm entity id for the log line.</param>
        /// <param name="itemId">Verified vanilla good id ("Plank"/"MetalBlock").</param>
        /// <param name="amount">Item count of one maintenance cycle; must be positive.</param>
        /// <param name="failureKey">Specific failure key when false.</param>
        /// <returns>True when the material transaction succeeded or was not needed.</returns>
        internal bool TryConsumeNearest(BaseComponent farm, Guid entityId, string? itemId, int amount,
            out string? failureKey)
        {
            failureKey = null;
            if (string.IsNullOrEmpty(itemId) || amount <= 0)
            {
                return true;
            }

            var goodAmount = new GoodAmount(itemId, amount);
            Inventory? best = null;
            var bestDistance = float.MaxValue;
            var farmPosition = farm.Transform.position;
            try
            {
                var centers = _centers.FinishedDistrictCenters;
                for (var centerIndex = 0; centerIndex < centers.Count; centerIndex++)
                {
                    var center = centers[centerIndex];
                    if (center is null)
                    {
                        continue;
                    }

                    var registry = center.GetComponent<DistrictInventoryRegistry>();
                    var inventories = registry?.ActiveInventoriesWithStock(itemId);
                    if (inventories is null)
                    {
                        continue;
                    }

                    foreach (var inventory in inventories)
                    {
                        if (inventory is null || !inventory.Takes(itemId)
                            || !inventory.HasUnreservedStock(goodAmount))
                        {
                            continue;
                        }

                        var distance = (inventory.Transform.position - farmPosition).sqrMagnitude;
                        if (distance < bestDistance)
                        {
                            bestDistance = distance;
                            best = inventory;
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                // REQ-V2-SAFE-01: a broken inventory query means "nothing consumed", never a crash.
                failureKey = HdbfCommandFailures.Unknown;
                _log.Error(HdbfLogCategory.Warn, IndoorLogKeys.MaintenanceMissing,
                    "maintenance stockpile query failed; no material consumed", exception,
                    entityId.ToString());
                return false;
            }

            if (best is null)
            {
                failureKey = HdbfCommandFailures.MissingMaterials;
                _log.Write(HdbfLogCategory.Warn, IndoorLogKeys.MaintenanceMissing,
                    "indoor maintenance needs " + amount + " " + itemId
                    + " but no stockpile holds that unreserved amount; entityId=" + entityId);
                return false;
            }

            try
            {
                // All-or-nothing: guarded above by HasUnreservedStock; TakeExisting only removes
                // the exact amount (REQ-V2-SAFE-04: never deduct a negative/partial amount).
                best.TakeExisting(goodAmount);
                return true;
            }
            catch (Exception exception)
            {
                failureKey = HdbfCommandFailures.Unknown;
                _log.Error(HdbfLogCategory.Error, IndoorLogKeys.MaintenanceMissing,
                    "maintenance material withdrawal failed; no state changed", exception,
                    entityId.ToString());
                return false;
            }
        }
    }
}
