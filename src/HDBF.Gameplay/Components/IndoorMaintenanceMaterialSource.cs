// ============================================================================
// HDBF v2 | Layer: Gameplay/Components | Owner: V2-GAMEPLAY-01
// Purpose: IMaintenanceMaterialSource over the settlement's finished districts. Used by the
//          daily tick only to report MissingMaterial; the actual maintenance transaction is
//          performed by HdbfFarmCommandService through the same district inventories.
// Requirements: REQ-V2-BAL-02, REQ-V2-SAFE-01, REQ-V2-SAVE-06
// ============================================================================
using System;
using HDBF.Core;
using HDBF.Core.Indoor;
using HDBF.Core.Logging;
using Timberborn.GameDistricts;
using Timberborn.Goods;
using Timberborn.InventorySystem;

namespace HDBF.Gameplay.Components
{
    /// <summary>
    /// Read-only maintenance material view of the settlement (section 19.8). It sums the
    /// unreserved stock across finished district inventories; a missing registry or a throwing
    /// inventory answers 0, which the Core tick turns into a throttled MissingMaterial flag. This
    /// card does not consume materials from the tick: maintenance stays an explicit player/command
    /// action (V2-GAMEPLAY-02 owns the full nearest-stockpile transaction).
    /// </summary>
    internal sealed class IndoorMaintenanceMaterialSource : IMaintenanceMaterialSource
    {
        private const string LogKey = "indoor.maintenance";

        private readonly DistrictCenterRegistry _centers;
        private readonly IHdbfLog _log;

        internal IndoorMaintenanceMaterialSource(DistrictCenterRegistry centers, IHdbfLog log)
        {
            _centers = centers ?? throw new ArgumentNullException(nameof(centers));
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        /// <inheritdoc />
        public int Available(string itemId)
        {
            if (string.IsNullOrEmpty(itemId))
            {
                return 0;
            }

            try
            {
                var centers = _centers.FinishedDistrictCenters;
                var available = 0;
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
                        if (inventory is null || !inventory.Takes(itemId))
                        {
                            continue;
                        }

                        available += inventory.UnreservedAmountInStock(itemId);
                    }
                }

                return available;
            }
            catch (Exception exception)
            {
                _log.Error(HdbfLogCategory.Warn, LogKey,
                    "maintenance material query failed; treating the stockpile as empty", exception);
                return 0;
            }
        }

        /// <inheritdoc />
        public bool TryConsume(string itemId, int count)
        {
            if (string.IsNullOrEmpty(itemId) || count <= 0)
            {
                return false;
            }

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
                            || !inventory.HasUnreservedStock(new GoodAmount(itemId, count)))
                        {
                            continue;
                        }

                        inventory.TakeExisting(new GoodAmount(itemId, count));
                        return true;
                    }
                }
            }
            catch (Exception exception)
            {
                _log.Error(HdbfLogCategory.Warn, LogKey,
                    "maintenance material transaction failed; nothing consumed", exception);
            }

            return false;
        }
    }
}
