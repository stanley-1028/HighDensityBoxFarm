// ============================================================================
// HDBF v2 | Layer: Gameplay/Components | Owner: V2-GAMEPLAY-02
// Purpose: Manual water refill source probe (section 19.6): validates that a real, reachable,
//          uncontaminated water source exists within 12 cells and returns it as a Core
//          IWaterSource. The source search itself lives in the .Search partial.
// Requirements: REQ-V2-WATER-02, REQ-V2-WATER-04, REQ-V2-COMPAT-02, REQ-V2-SAFE-01,
//               REQ-V2-LABOR-03
// ============================================================================
using System;
using HDBF.Core;
using HDBF.Core.Indoor;
using HDBF.Core.Logging;
using HdbfWaterSource = HDBF.Core.Indoor.IWaterSource;
using Timberborn.BaseComponentSystem;
using Timberborn.BlockSystem;
using Timberborn.Coordinates;
using Timberborn.GameDistricts;
using Timberborn.Navigation;
using Timberborn.WaterSystem;
using UnityEngine;

namespace HDBF.Gameplay.Components
{
    /// <summary>
    /// Finds the nearest usable manual water source of one farm. Real APIs only (IL-verified
    /// against Timberborn 1.1.2.8): the water map answers where water is and how deep/contaminated,
    /// <c>IWaterService.RemoveCleanWater</c> is the vanilla way to take water out of a body,
    /// <c>DistrictInventoryRegistry</c> exposes water-pump output and water-tank inventories, and
    /// <c>INavigationService</c> proves the worker can actually walk there (no teleport,
    /// REQ-V2-WATER-04).
    ///
    /// Failure policy: every query is isolated; a broken water/navigation API degrades to "no
    /// source" with a throttled <c>water.manual-refill</c> warning instead of breaking the farm.
    /// </summary>
    public sealed partial class HdbfManualWaterSource
    {
        private const float MaxNavMeshSearchDistance = 8f;

        private readonly DistrictCenterRegistry _centers;
        private readonly IThreadSafeWaterMap _waterMap;
        private readonly IWaterService _waterService;
        private readonly INavigationService _navigation;
        private readonly IHdbfLog _log;

        /// <summary>Creates the probe from the game singletons (one instance per session).</summary>
        /// <param name="centers">Finished district centers (inventory search).</param>
        /// <param name="waterMap">Thread-safe water map (depth/contamination queries).</param>
        /// <param name="waterService">Water service (the only API allowed to drain a water body).</param>
        /// <param name="navigation">Navigation service (walk reachability of the worker).</param>
        /// <param name="log">Throttled mod log.</param>
        public HdbfManualWaterSource(DistrictCenterRegistry centers, IThreadSafeWaterMap waterMap,
            IWaterService waterService, INavigationService navigation, IHdbfLog log)
        {
            _centers = centers ?? throw new ArgumentNullException(nameof(centers));
            _waterMap = waterMap ?? throw new ArgumentNullException(nameof(waterMap));
            _waterService = waterService ?? throw new ArgumentNullException(nameof(waterService));
            _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        /// <summary>
        /// Vanilla water good id, IL-verified against Timberborn 1.1.2.8:
        /// <c>WaterWorkshops.WaterGoodToWaterAmountConverter</c> static ctor stores the literal
        /// <c>"Water"</c>. That helper type is internal, so the verified value lives in the Core
        /// constant (which this probe uses at runtime).
        /// </summary>
        public static string WaterGoodId => IndoorConstants.WaterGoodId;

        /// <summary>
        /// Water volume of one HDBF unit in m3. IL-verified: the same internal converter's static
        /// ctor stores <c>WaterAmountConversion = 0.2</c> (one vanilla water good = 0.2 m3), which
        /// is also the Timber Pipes unit mapping used by the v2 pipe source.
        /// </summary>
        public static float VolumePerUnit => IndoorConstants.WaterVolumeCubicMetersPerUnit;

        /// <summary>
        /// Probes the nearest usable source for one manual trip. Returns false with a specific
        /// failure key (no source / unreachable / contaminated) and moves no water.
        /// </summary>
        /// <param name="farm">The farm component (position + walk access of the workers).</param>
        /// <param name="requiredUnits">Water units one trip must be able to fetch (tank capacity/3).</param>
        /// <param name="source">Usable source when true; null otherwise.</param>
        /// <param name="failureKey">Specific failure key when false.</param>
        /// <returns>True when a real, reachable, uncontaminated source can fill the trip.</returns>
        public bool TryFindSource(BaseComponent farm, float requiredUnits, out HdbfWaterSource? source,
            out string? failureKey)
        {
            source = null;
            failureKey = null;
            if (farm is null || !(requiredUnits > 0f))
            {
                failureKey = HdbfIndoorCommandFailures.NoWaterSource;
                return false;
            }

            try
            {
                if (!TryResolveFarm(farm, out var origin, out var start))
                {
                    failureKey = HdbfIndoorCommandFailures.WaterSourceUnreachable;
                    LogProblem("farm has no walk access; manual refill cannot start");
                    return false;
                }

                var bestDistance = float.MaxValue;
                var sawWater = false;
                var sawContaminated = false;
                var sawUnreachable = false;
                TryFindWaterBody(origin, start, requiredUnits, ref bestDistance, out source,
                    ref sawWater, ref sawContaminated, ref sawUnreachable);
                if (source is null)
                {
                    TryFindWaterInventory(farm.Transform.position, start, requiredUnits, ref bestDistance,
                        out source, ref sawUnreachable);
                }

                if (source is not null)
                {
                    return true;
                }

                failureKey = sawContaminated
                    ? HdbfIndoorCommandFailures.WaterContaminated
                    : sawUnreachable || sawWater
                        ? HdbfIndoorCommandFailures.WaterSourceUnreachable
                        : HdbfIndoorCommandFailures.NoWaterSource;
                LogProblem("no usable manual water source: " + failureKey);
                return false;
            }
            catch (Exception exception)
            {
                // REQ-V2-SAFE-01: a broken game API degrades to "no source", never into the tick.
                _log.Error(HdbfLogCategory.Warn, IndoorLogKeys.ManualRefill,
                    "manual water source probe failed; treating the farm as having no source", exception);
                source = null;
                failureKey = HdbfIndoorCommandFailures.NoWaterSource;
                return false;
            }
        }

        private bool TryResolveFarm(BaseComponent farm, out Vector3Int origin, out Vector3 start)
        {
            origin = default;
            start = default;
            try
            {
                var block = farm.GetComponent<BlockObject>();
                origin = block is not null ? block.Coordinates : CoordinateSystem.WorldToGridInt(farm.Transform.position);
                var accessible = farm.GetComponent<Accessible>();
                if (accessible is null)
                {
                    return false;
                }

                var single = accessible.UnblockedSingleAccess;
                if (single.HasValue)
                {
                    start = single.Value;
                    return true;
                }

                var accesses = accessible.Accesses;
                if (accesses.Count == 0)
                {
                    return false;
                }

                start = accesses[0];
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private bool IsReachable(Vector3 start, Vector3 target)
        {
            try
            {
                var closest = _navigation.ClosestPositionOnNavMesh(target, MaxNavMeshSearchDistance);
                return closest.HasValue && _navigation.DestinationIsReachable(start, closest.Value);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private void LogProblem(string message)
        {
            try
            {
                _log.Write(HdbfLogCategory.Warn, IndoorLogKeys.ManualRefill, message);
            }
            catch (Exception)
            {
                // Diagnostics must never break the probe (REQ-V2-SAFE-01).
            }
        }
    }
}
