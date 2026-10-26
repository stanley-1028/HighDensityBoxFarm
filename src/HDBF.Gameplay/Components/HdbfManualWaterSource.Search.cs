// ============================================================================
// HDBF v2 | Layer: Gameplay/Components | Owner: V2-GAMEPLAY-02
// Purpose: The actual 12-cell source search of the manual water refill probe (section 19.6):
//          nearest water-body column (water map) and nearest vanilla water-good inventory
//          (district registry). Split from HdbfManualWaterSource.cs for the 300-line gate.
// Requirements: REQ-V2-WATER-02, REQ-V2-WATER-04, REQ-V2-COMPAT-02, REQ-V2-SAFE-01
// ============================================================================
using System;
using HDBF.Core;
using HDBF.Core.Indoor;
using HDBF.Core.Logging;
using HdbfWaterSource = HDBF.Core.Indoor.IWaterSource;
using Timberborn.Coordinates;
using Timberborn.GameDistricts;
using Timberborn.InventorySystem;
using UnityEngine;

namespace HDBF.Gameplay.Components
{
    public sealed partial class HdbfManualWaterSource
    {
        private const float ContaminationEpsilon = 0.001f;
        private const int MaxVerticalScan = 12;

        /// <summary>
        /// Scans the 12-cell neighbourhood for the nearest water-body column that can supply a
        /// full trip. A column is usable only when it is uncontaminated, deep enough for the
        /// requested volume AND walk-reachable from the farm's access point.
        /// </summary>
        private void TryFindWaterBody(Vector3Int origin, Vector3 start, float requiredUnits,
            ref float bestDistance, out HdbfWaterSource? source, ref bool sawWater, ref bool sawContaminated,
            ref bool sawUnreachable)
        {
            source = null;
            var volume = requiredUnits * VolumePerUnit;
            var radius = IndoorConstants.ManualRefillRadiusCells;
            var foundDistance = float.MaxValue;
            Vector3Int foundCell = default;
            for (var dx = -radius; dx <= radius; dx++)
            {
                for (var dz = -radius; dz <= radius; dz++)
                {
                    var distance = Mathf.Sqrt(dx * dx + dz * dz);
                    if (distance > radius || distance >= foundDistance)
                    {
                        continue;
                    }

                    var x = origin.x + dx;
                    var y = origin.y + dz;
                    bool anyWater;
                    try
                    {
                        anyWater = _waterMap.IsWaterOnAnyHeight(new Vector2Int(x, y));
                    }
                    catch (Exception)
                    {
                        continue;
                    }

                    if (!anyWater)
                    {
                        continue;
                    }

                    sawWater = true;
                    if (!TryFindWaterColumn(x, y, origin.z, out var cell, out var depth, out var contaminated))
                    {
                        continue;
                    }

                    if (contaminated)
                    {
                        sawContaminated = true;
                        continue;
                    }

                    if (depth < volume)
                    {
                        continue;
                    }

                    var target = CoordinateSystem.GridToWorldCentered(cell);
                    if (!IsReachable(start, target))
                    {
                        sawUnreachable = true;
                        continue;
                    }

                    foundCell = cell;
                    foundDistance = distance;
                }
            }

            if (foundDistance >= float.MaxValue)
            {
                return;
            }

            source = new WaterBodySource(_waterMap, _waterService, foundCell);
            bestDistance = foundDistance;
        }

        /// <summary>
        /// Finds the vertical water column of one map cell: the first z inside a real water column
        /// (the water map returns 0 outside every column) plus its depth and contamination.
        /// </summary>
        private bool TryFindWaterColumn(int x, int y, int baseZ, out Vector3Int cell, out float depth,
            out bool contaminated)
        {
            cell = default;
            depth = 0f;
            contaminated = false;
            var lowest = baseZ - MaxVerticalScan;
            if (lowest < 0)
            {
                lowest = 0;
            }

            var highest = baseZ + 4;
            for (var z = lowest; z <= highest; z++)
            {
                var candidate = new Vector3Int(x, y, z);
                float candidateDepth;
                try
                {
                    candidateDepth = _waterMap.WaterDepth(candidate);
                }
                catch (Exception)
                {
                    return false;
                }

                if (!(candidateDepth > 0f))
                {
                    continue;
                }

                cell = candidate;
                depth = candidateDepth;
                try
                {
                    contaminated = _waterMap.ColumnContamination(candidate) > ContaminationEpsilon;
                }
                catch (Exception)
                {
                    contaminated = true;
                }

                return true;
            }

            return false;
        }

        /// <summary>
        /// Scans the finished districts for the nearest inventory that holds enough unreserved
        /// vanilla water goods (water-pump output, water tank, storage). The inventory must be
        /// within 12 cells and walk-reachable from the farm.
        /// </summary>
        private bool TryFindWaterInventory(Vector3 farmPosition, Vector3 start, float requiredUnits,
            ref float bestDistance, out HdbfWaterSource? source, ref bool sawUnreachable)
        {
            source = null;
            var goodId = WaterGoodId;
            var goods = UnitsToGoods(requiredUnits);
            Inventory? found = null;
            var foundDistance = float.MaxValue;
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
                    var inventories = registry?.ActiveInventoriesWithStock(goodId);
                    if (inventories is null)
                    {
                        continue;
                    }

                    foreach (var inventory in inventories)
                    {
                        if (inventory is null || !inventory.Takes(goodId)
                            || inventory.UnreservedAmountInStock(goodId) < goods)
                        {
                            continue;
                        }

                        var position = inventory.Transform.position;
                        var distance = Vector3.Distance(farmPosition, position);
                        if (distance > IndoorConstants.ManualRefillRadiusCells || distance >= foundDistance)
                        {
                            continue;
                        }

                        if (!IsReachable(start, position))
                        {
                            sawUnreachable = true;
                            continue;
                        }

                        found = inventory;
                        foundDistance = distance;
                    }
                }
            }
            catch (Exception exception)
            {
                _log.Error(HdbfLogCategory.Warn, IndoorLogKeys.ManualRefill,
                    "manual water inventory probe failed; continuing with water bodies only", exception);
            }

            if (found is null)
            {
                return false;
            }

            source = new InventoryWaterSource(found, goodId);
            bestDistance = foundDistance;
            return true;
        }

        /// <summary>
        /// Converts a water-unit trip into whole vanilla water goods. One HDBF unit is one water
        /// good (spec unit mapping), so <c>capacity/3</c> can be fractional (basic: 1.2) and is
        /// rounded to the nearest whole good; the tank clamp absorbs the small remainder.
        /// </summary>
        internal static int UnitsToGoods(float requiredUnits)
        {
            if (!(requiredUnits > 0f))
            {
                return 0;
            }

            var goods = Mathf.RoundToInt(requiredUnits);
            return goods < 1 ? 1 : goods;
        }
    }
}
