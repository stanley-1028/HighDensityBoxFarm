// ============================================================================
// HDBF v2 | Layer: Gameplay/Components | Owner: V2-GAMEPLAY-02
// Purpose: The two concrete manual water sources of HdbfManualWaterSource (section 19.6):
//          a real water body drained through the vanilla IWaterService, and a vanilla
//          water-good inventory (water-pump output / water tank) withdrawn all-or-nothing.
//          Both implement Core IWaterSource and never conjure water.
// Requirements: REQ-V2-WATER-02, REQ-V2-WATER-04, REQ-V2-SAFE-01, REQ-V2-EXT-02
// ============================================================================
using System;
using HDBF.Core.Indoor;
using HdbfWaterSource = HDBF.Core.Indoor.IWaterSource;
using Timberborn.Goods;
using Timberborn.InventorySystem;
using Timberborn.WaterSystem;
using UnityEngine;

namespace HDBF.Gameplay.Components
{
    public sealed partial class HdbfManualWaterSource
    {
        /// <summary>
        /// One real water-body cell. <see cref="TryDraw"/> removes the requested volume from that
        /// column through the vanilla <see cref="IWaterService"/> (the same API a water pump uses)
        /// and reports the drawn water units; a contaminated column is never drained.
        /// </summary>
        internal sealed class WaterBodySource : HdbfWaterSource
        {
            private const float ContaminationEpsilon = 0.001f;

            private readonly IThreadSafeWaterMap _waterMap;
            private readonly IWaterService _waterService;
            private readonly Vector3Int _cell;

            internal WaterBodySource(IThreadSafeWaterMap waterMap, IWaterService waterService, Vector3Int cell)
            {
                _waterMap = waterMap;
                _waterService = waterService;
                _cell = cell;
            }

            /// <inheritdoc />
            public WaterSupplyKind Kind => WaterSupplyKind.Manual;

            /// <inheritdoc />
            public bool IsAvailable
            {
                get
                {
                    try
                    {
                        return _waterMap.WaterDepth(_cell) > 0f
                               && _waterMap.ColumnContamination(_cell) <= ContaminationEpsilon;
                    }
                    catch (Exception)
                    {
                        return false;
                    }
                }
            }

            /// <inheritdoc />
            public bool TryDraw(float amount, out float actual)
            {
                actual = 0f;
                if (!(amount > 0f))
                {
                    return false;
                }

                try
                {
                    var depth = _waterMap.WaterDepth(_cell);
                    if (!(depth > 0f) || _waterMap.ColumnContamination(_cell) > ContaminationEpsilon)
                    {
                        return false;
                    }

                    var volume = amount * VolumePerUnit;
                    if (volume > depth)
                    {
                        volume = depth;
                    }

                    if (!(volume > 0f))
                    {
                        return false;
                    }

                    _waterService.RemoveCleanWater(_cell, volume);
                    actual = volume / VolumePerUnit;
                    return actual > 0f && !float.IsNaN(actual) && !float.IsInfinity(actual);
                }
                catch (Exception)
                {
                    actual = 0f;
                    return false;
                }
            }
        }

        /// <summary>
        /// One vanilla inventory that holds the water good (water-pump output or water tank).
        /// Consumes whole goods only, all-or-nothing, and reports the equivalent water units.
        /// </summary>
        internal sealed class InventoryWaterSource : HdbfWaterSource
        {
            private readonly Inventory _inventory;
            private readonly string _goodId;

            internal InventoryWaterSource(Inventory inventory, string goodId)
            {
                _inventory = inventory;
                _goodId = goodId;
            }

            /// <inheritdoc />
            public WaterSupplyKind Kind => WaterSupplyKind.Manual;

            /// <inheritdoc />
            public bool IsAvailable
            {
                get
                {
                    try
                    {
                        return _inventory.UnreservedAmountInStock(_goodId) > 0;
                    }
                    catch (Exception)
                    {
                        return false;
                    }
                }
            }

            /// <inheritdoc />
            public bool TryDraw(float amount, out float actual)
            {
                actual = 0f;
                if (!(amount > 0f))
                {
                    return false;
                }

                try
                {
                    var available = _inventory.UnreservedAmountInStock(_goodId);
                    if (available <= 0)
                    {
                        return false;
                    }

                    var goods = UnitsToGoods(amount);
                    if (goods > available)
                    {
                        goods = available;
                    }

                    if (goods <= 0)
                    {
                        return false;
                    }

                    _inventory.TakeExisting(new GoodAmount(_goodId, goods));
                    actual = goods;
                    return true;
                }
                catch (Exception)
                {
                    actual = 0f;
                    return false;
                }
            }
        }
    }
}
