// ============================================================================
// HDBF v2 | Layer: Gameplay/Components | Owner: V2-GAMEPLAY-02
// Purpose: Worker walk-start resolution of the manual water refill probe (section 19.6): the
//          building's own access points (enumerated, never the ambiguous single lookup), the
//          entrance doorstep and the nearest nav mesh position next to the building. Split from
//          HdbfManualWaterSource.cs for the 300-line gate.
// Requirements: REQ-V2-WATER-04, REQ-V2-COMPAT-02, REQ-V2-SAFE-01
// ============================================================================
using System;
using System.Collections.Generic;
using Timberborn.BaseComponentSystem;
using Timberborn.BlockSystem;
using Timberborn.Coordinates;
using Timberborn.Navigation;
using UnityEngine;

namespace HDBF.Gameplay.Components
{
    public sealed partial class HdbfManualWaterSource
    {
        /// <summary>
        /// Reused buffer of the farm's <see cref="Accessible"/> components (the probe runs once per
        /// game day, on the single game thread; never shared across threads).
        /// </summary>
        private readonly List<Accessible> _accessibleBuffer = new List<Accessible>();

        /// <summary>
        /// Resolves the farm's grid origin and the walk start of its workers. A finished Timberborn
        /// building owns two <see cref="Accessible"/> decorators (the construction-site one and the
        /// building one), so <c>GetComponent&lt;Accessible&gt;()</c> is ambiguous and throws; the
        /// access points are enumerated instead. The entrance doorstep and the nearest nav mesh
        /// position are fallbacks for a building whose access components are not usable yet, so the
        /// probe works for every finished box farm (REQ-V2-WATER-04: the worker must really walk).
        /// </summary>
        private bool TryResolveFarm(BaseComponent farm, out Vector3Int origin, out Vector3 start)
        {
            origin = default;
            start = default;
            BlockObject? block;
            try
            {
                block = farm.GetComponent<BlockObject>();
                origin = block is not null ? block.Coordinates : CoordinateSystem.WorldToGridInt(farm.Transform.position);
            }
            catch (Exception)
            {
                return false;
            }

            if (TryResolveAccessibleStart(farm, out start))
            {
                return true;
            }

            if (TryResolveEntranceStart(block, out start))
            {
                return true;
            }

            return TryResolveNavMeshStart(farm.Transform.position, out start);
        }

        /// <summary>
        /// Picks the walkable access point from the entity's <see cref="Accessible"/> components.
        /// The enabled access of the finished building is selected; a disabled component (or one
        /// whose accesses were cleared when the building left the construction state) is skipped.
        /// </summary>
        private bool TryResolveAccessibleStart(BaseComponent farm, out Vector3 start)
        {
            start = default;
            try
            {
                _accessibleBuffer.Clear();
                farm.GetComponents(_accessibleBuffer);
                for (var index = 0; index < _accessibleBuffer.Count; index++)
                {
                    var accessible = _accessibleBuffer[index];
                    if (accessible is null)
                    {
                        continue;
                    }

                    try
                    {
                        var single = accessible.UnblockedSingleAccess;
                        if (single.HasValue)
                        {
                            start = single.Value;
                            return true;
                        }
                    }
                    catch (Exception)
                    {
                        // Not exactly one raw access (0 or several); the raw fallback below covers it.
                    }
                }

                for (var index = 0; index < _accessibleBuffer.Count; index++)
                {
                    var accessible = _accessibleBuffer[index];
                    if (accessible is null)
                    {
                        continue;
                    }

                    try
                    {
                        var accesses = accessible.Accesses;
                        if (accesses.Count > 0)
                        {
                            start = accesses[0];
                            return true;
                        }
                    }
                    catch (Exception)
                    {
                        // A broken component cannot provide a start point; try the next one.
                    }
                }
            }
            catch (Exception)
            {
                // An unusable Accessible set falls through to the entrance/nav mesh fallbacks.
            }
            finally
            {
                _accessibleBuffer.Clear();
            }

            return false;
        }

        /// <summary>
        /// Walk start at the building's entrance doorstep (blueprint data, always available), snapped
        /// onto the nav mesh. Used when the entity's access components are not usable yet.
        /// </summary>
        private bool TryResolveEntranceStart(BlockObject? block, out Vector3 start)
        {
            start = default;
            if (block is null || !block.HasEntrance)
            {
                return false;
            }

            try
            {
                var entrance = block.PositionedEntrance;
                if (entrance is null)
                {
                    return false;
                }

                return TryResolveNavMeshStart(
                    CoordinateSystem.GridToWorldCentered(entrance.DoorstepCoordinates), out start);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Walk start at the nav mesh position closest to a world position (never off-mesh).</summary>
        private bool TryResolveNavMeshStart(Vector3 position, out Vector3 start)
        {
            start = default;
            try
            {
                var closest = _navigation.ClosestPositionOnNavMesh(position, MaxNavMeshSearchDistance);
                if (!closest.HasValue)
                {
                    return false;
                }

                start = closest.Value;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
