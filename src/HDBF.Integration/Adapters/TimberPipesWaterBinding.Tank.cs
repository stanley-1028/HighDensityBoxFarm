// ============================================================================
// HDBF v2 | Layer: Integration/Adapters | Owner: V2-INTEG-01
// Purpose: Tank half of the Timber Pipes water binding: read one tank's state and drain it
//          through the mod's own public ApplyVolume path (never through private members).
// Requirements: REQ-V2-WATER-02, REQ-V2-COMPAT-01, REQ-V2-SAFE-05
// ============================================================================
using System;

namespace HDBF.Integration.Adapters
{
    internal sealed partial class TimberPipesWaterBinding
    {
        /// <summary>
        /// Reads the descriptive state of one tank object. <paramref name="tank"/> is an opaque
        /// object from the mod's tank list; anything else (null, another type) answers false.
        /// </summary>
        internal bool TryDescribeTank(object? tank, out PipeTankState state)
        {
            state = default;
            try
            {
                if (tank is null || _getIsFinished is null || _getVolumeM3 is null ||
                    _getFluidGoodId is null || _getPipe is null || _getGraph is null ||
                    _takesGood is null)
                {
                    return false;
                }

                var finished = _getIsFinished.Invoke(tank, null) is bool isFinished && isFinished;
                var volume = _getVolumeM3.Invoke(tank, null) is float volumeM3 ? volumeM3 : 0f;
                var pipe = _getPipe.Invoke(tank, null);
                var graph = pipe is null ? null : _getGraph.Invoke(pipe, null);
                var fluid = _getFluidGoodId.Invoke(tank, null) as string;
                var takesWater = _takesGood.Invoke(tank, new object[] { _waterGoodId }) is bool takes && takes;
                state = new PipeTankState(finished, graph is not null, takesWater, fluid, volume);
                LastFailureReason = null;
                return true;
            }
            catch (Exception exception)
            {
                // REQ-V2-SAFE-05: a throwing external API is a degraded adapter, never a farm crash.
                SetFailure("water-tank-read-exception", exception);
                return false;
            }
        }

        /// <summary>
        /// Drains up to <paramref name="requestedVolumeM3"/> from one tank by lowering its real
        /// fluid volume through the mod's public <c>ApplyVolume</c> (the mod then moves the same
        /// water good out of its output inventory through its own quantisation path).
        /// </summary>
        internal bool TryDrawVolume(object? tank, float requestedVolumeM3, out float drawnVolumeM3)
        {
            drawnVolumeM3 = 0f;
            try
            {
                if (tank is null || _applyVolume is null || _getVolumeM3 is null ||
                    float.IsNaN(requestedVolumeM3) || requestedVolumeM3 <= 0f)
                {
                    return false;
                }

                var beforeValue = _getVolumeM3.Invoke(tank, null);
                if (beforeValue is not float before || before <= 0f)
                {
                    return false;
                }

                var drawVolume = requestedVolumeM3 < before ? requestedVolumeM3 : before;
                var target = before - drawVolume;
                _applyVolume.Invoke(tank, new object[] { target, _waterGoodId });
                var afterValue = _getVolumeM3.Invoke(tank, null);
                var after = afterValue is float afterVolume && afterVolume >= 0f ? afterVolume : before;
                drawnVolumeM3 = before - after;
                if (!(drawnVolumeM3 > 0f) || float.IsNaN(drawnVolumeM3))
                {
                    drawnVolumeM3 = 0f;
                    return false;
                }

                LastFailureReason = null;
                return true;
            }
            catch (Exception exception)
            {
                drawnVolumeM3 = 0f;
                SetFailure("water-volume-apply-exception", exception);
                return false;
            }
        }
    }

    /// <summary>
    /// Immutable description of one Timber Pipes tank as read by
    /// <see cref="TimberPipesWaterBinding.TryDescribeTank"/>.
    /// </summary>
    internal readonly struct PipeTankState
    {
        internal PipeTankState(bool isFinished, bool hasPipeGraph, bool takesWaterGood, string? fluidGoodId,
            float volumeM3)
        {
            IsFinished = isFinished;
            HasPipeGraph = hasPipeGraph;
            TakesWaterGood = takesWaterGood;
            FluidGoodId = fluidGoodId;
            VolumeM3 = volumeM3;
        }

        /// <summary>Construction finished; unfinished tanks hold no usable water.</summary>
        internal bool IsFinished { get; }

        /// <summary>True when the tank's building pipe belongs to a real pipe network.</summary>
        internal bool HasPipeGraph { get; }

        /// <summary>True when the tank's output inventory accepts the water good.</summary>
        internal bool TakesWaterGood { get; }

        /// <summary>Fluid currently stored, or <c>null</c> when the tank is empty/neutral.</summary>
        internal string? FluidGoodId { get; }

        /// <summary>Current fluid volume in m3; the physical water stock the source can drain.</summary>
        internal float VolumeM3 { get; }
    }
}
