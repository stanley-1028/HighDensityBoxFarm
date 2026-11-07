// ============================================================================
// HDBF v2 | Layer: Integration/Adapters | Owner: V2-INTEG-01
// Purpose: Farm-adjacent pipe intake half of the Timber Pipes water binding: reads the transport
//          pipes that touch the farm footprint and drains real pipe fluid (0.2 m3 = one HDBF
//          unit) into the farm. Read-only census and bounded draw only; no tick, no conjuring.
// Requirements: REQ-V2-WATER-02, REQ-V2-COMPAT-01, REQ-V2-COMPAT-02, REQ-V2-SAFE-05
// ============================================================================
using System;
using System.Collections.Generic;
using HDBF.Core.Indoor;
using UnityEngine;

namespace HDBF.Integration.Adapters
{
    internal sealed partial class TimberPipesWaterBinding
    {
        /// <summary>
        /// Read-only census of the transport pipes on the given farm-neighbour cells. Never draws:
        /// <see cref="PipeIntakeState.PipeCount"/> counts finished transport pipes (a connection
        /// exists even when dry), <see cref="PipeIntakeState.WateredPipes"/> counts the ones that
        /// currently hold at least one water unit.
        /// </summary>
        internal bool TryReadIntake(IReadOnlyList<PipeCell>? neighbors, out PipeIntakeState state)
        {
            state = new PipeIntakeState(0, 0, 0);
            if (neighbors is null || neighbors.Count == 0)
            {
                return false;
            }

            try
            {
                if (!TryResolveRegistry() || _registry is null || !IsIntakeBound)
                {
                    return false;
                }

                var unit = VolumePerUnit;
                if (!(unit > 0f))
                {
                    return false;
                }

                var pipeCount = 0;
                var waterPipes = 0;
                var wateredPipes = 0;
                for (var index = 0; index < neighbors.Count; index++)
                {
                    var pipe = FindAdjacentPipe(neighbors[index]);
                    if (!TryDescribeIntakePipe(pipe, out var carriesWater, out var volumeM3))
                    {
                        continue;
                    }

                    pipeCount++;
                    if (!carriesWater)
                    {
                        continue;
                    }

                    waterPipes++;
                    if (volumeM3 >= unit)
                    {
                        wateredPipes++;
                    }
                }

                state = new PipeIntakeState(pipeCount, waterPipes, wateredPipes);
                LastFailureReason = null;
                return true;
            }
            catch (Exception exception)
            {
                SetFailure("water-intake-census-exception", exception);
                return false;
            }
        }

        /// <summary>
        /// Drains up to <paramref name="requestedVolumeM3"/> of real water fluid from the transport
        /// pipes adjacent to the farm. Each pipe is drained through the mod's public
        /// <c>RemoveFluid(float)</c>, which clamps at the pipe's current volume; the fluid solver
        /// re-reads every pipe volume on its next tick, so no water is conjured and none is lost.
        /// </summary>
        internal bool TryDrawIntake(IReadOnlyList<PipeCell>? neighbors, float requestedVolumeM3,
            out float drawnVolumeM3)
        {
            drawnVolumeM3 = 0f;
            if (neighbors is null || neighbors.Count == 0 || !(requestedVolumeM3 > 0f) ||
                float.IsNaN(requestedVolumeM3))
            {
                return false;
            }

            try
            {
                if (!TryResolveRegistry() || _registry is null || !IsIntakeBound)
                {
                    return false;
                }

                var unit = VolumePerUnit;
                if (!(unit > 0f))
                {
                    return false;
                }

                for (var index = 0; index < neighbors.Count; index++)
                {
                    if (drawnVolumeM3 >= requestedVolumeM3 - unit * 0.001f)
                    {
                        break;
                    }

                    var pipe = FindAdjacentPipe(neighbors[index]);
                    if (!TryDescribeIntakePipe(pipe, out var carriesWater, out var beforeM3) ||
                        !carriesWater || beforeM3 < unit)
                    {
                        continue;
                    }

                    var takeM3 = requestedVolumeM3 - drawnVolumeM3;
                    if (takeM3 > beforeM3)
                    {
                        takeM3 = beforeM3;
                    }

                    if (!(takeM3 > 0f))
                    {
                        continue;
                    }

                    _removeFluid!.Invoke(pipe, new object[] { takeM3 });
                    var afterM3 = ReadFluidHeight(pipe);
                    var removedM3 = beforeM3 - afterM3;
                    if (removedM3 > 0f && !float.IsNaN(removedM3))
                    {
                        drawnVolumeM3 += removedM3;
                    }
                }

                LastFailureReason = null;
                return drawnVolumeM3 > 0f;
            }
            catch (Exception exception)
            {
                drawnVolumeM3 = 0f;
                SetFailure("water-intake-draw-exception", exception);
                return false;
            }
        }

        private object? FindAdjacentPipe(PipeCell cell)
        {
            if (_tryGetPipe is null || _registry is null)
            {
                return null;
            }

            var arguments = new object?[] { new Vector3Int(cell.X, cell.Y, cell.Z), null };
            var found = _tryGetPipe.Invoke(_registry, arguments);
            return found is bool value && value ? arguments[1] : null;
        }

        private bool TryDescribeIntakePipe(object? pipe, out bool carriesWater, out float volumeM3)
        {
            carriesWater = false;
            volumeM3 = 0f;
            if (pipe is null || _getIsTransportPipe is null || _getPipeIsFinished is null ||
                _getIsContaminated is null || _getNetworkGoodId is null || _getFluidHeight is null)
            {
                return false;
            }

            if (!(_getIsTransportPipe.Invoke(pipe, null) is bool transport) || !transport)
            {
                return false;
            }

            if (!(_getPipeIsFinished.Invoke(pipe, null) is bool finished) || !finished)
            {
                return false;
            }

            var contaminated = _getIsContaminated.Invoke(pipe, null) is bool bad && bad;
            var goodId = _getNetworkGoodId.Invoke(pipe, null) as string;
            carriesWater = !contaminated && string.Equals(goodId, _waterGoodId, StringComparison.Ordinal);
            volumeM3 = ReadFluidHeight(pipe);
            return true;
        }

        private float ReadFluidHeight(object? pipe)
        {
            if (pipe is null || _getFluidHeight is null)
            {
                return 0f;
            }

            var value = _getFluidHeight.Invoke(pipe, null);
            return value is float height && height > 0f && !float.IsNaN(height) && !float.IsInfinity(height)
                ? height
                : 0f;
        }
    }

    /// <summary>
    /// Immutable read-only census of the transport pipes adjacent to one farm footprint
    /// (farm-adjacent intake). Counts are zero when nothing was read; the instance is a value type
    /// so the daily tick and the probe share it without allocation.
    /// </summary>
    internal readonly struct PipeIntakeState
    {
        internal PipeIntakeState(int pipeCount, int waterPipes, int wateredPipes)
        {
            PipeCount = pipeCount < 0 ? 0 : pipeCount;
            WaterPipes = waterPipes < 0 ? 0 : waterPipes;
            WateredPipes = wateredPipes < 0 ? 0 : wateredPipes;
        }

        /// <summary>Finished transport pipes adjacent to the farm (connected even while dry).</summary>
        internal int PipeCount { get; }

        /// <summary>Adjacent pipes that accept the water good and carry no foreign fluid.</summary>
        internal int WaterPipes { get; }

        /// <summary>Water-capable adjacent pipes holding at least one unit (0.2 m3) of fluid.</summary>
        internal int WateredPipes { get; }
    }
}
