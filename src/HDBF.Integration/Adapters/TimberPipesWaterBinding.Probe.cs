// ============================================================================
// HDBF v2 | Layer: Integration/Adapters | Owner: V2-INTEG-01
// Purpose: Read-only probe half of the Timber Pipes water binding (WP-05/RE-05): census the
//          finished, network-connected tanks and map them to the Core PipeProbe reason codes.
//          It never draws water and never reports a failure to the three-strike adaptor health.
// Requirements: REQ-V2-WATER-02, REQ-V2-COMPAT-01, REQ-V2-COMPAT-02, REQ-V2-SAFE-05
// ============================================================================
using System;
using System.Collections;
using HDBF.Core.Indoor;

namespace HDBF.Integration.Adapters
{
    internal sealed partial class TimberPipesWaterBinding
    {
        /// <summary>Enumerates the mod's tanks after resolving the binding; false on any failure.</summary>
        internal bool TryEnumerateTanks(out IEnumerable? tanks)
        {
            tanks = null;
            return TryBind() && TryGetTanks(out tanks) && tanks is not null;
        }

        /// <summary>
        /// Reads one tank and keeps it only when it is a finished, network-connected water tank
        /// (foreign fluids excluded); the draw path and <c>IsAvailable</c> share this filter.
        /// </summary>
        internal bool TryDescribeFeedableTank(object? tank, out PipeTankState state)
        {
            state = default;
            if (!TryDescribeTank(tank, out var candidate) || !candidate.IsFinished ||
                !candidate.HasPipeGraph || !IsWaterCapable(in candidate))
            {
                return false;
            }

            state = candidate;
            return true;
        }

        /// <summary>Builds the failure probe; <see cref="PipeProbe.Detail"/> is never null.</summary>
        private PipeProbe FailedProbe(string detail, int pipeTanks = 0, int waterTanks = 0)
        {
            return new PipeProbe(PipeProbeReason.BindingFailed, pipeTanks, waterTanks,
                detail ?? string.Empty);
        }

        /// <summary>
        /// Observation-only pipe census (WP-05). It reads the mod registry through the same guarded
        /// reflection as <see cref="TryDescribeTank"/>, counts the finished tanks on a pipe network
        /// (any good), the subset that accepts the water good and the watered subset, and maps them
        /// to the frozen reason codes: no network tank -&gt; NoPipeNetwork, no water-capable tank
        /// -&gt; NoPipeTank, all dry -&gt; TankDry, otherwise Connected. A bind/tank-read failure
        /// answers BindingFailed with <see cref="LastFailureReason"/> in the detail, so version drift
        /// is distinguishable from "the player has not connected a tank". It never calls
        /// <c>ApplyVolume</c> and never reports a runtime failure to the adaptor health machine.
        /// </summary>
        internal PipeProbe ProbeTanks()
        {
            try
            {
                if (!TryBind() || !TryGetTanks(out var tanks) || tanks is null)
                {
                    // Keep the binding reason (anchor/signature/registry) visible for the log and
                    // the panel detail; version drift must not look like "no tank built" (F-PIPE-04).
                    return FailedProbe(LastFailureReason ?? "tanks-unavailable");
                }

                var pipeTanks = 0;
                var waterTanks = 0;
                var wateredTanks = 0;
                foreach (var tank in tanks)
                {
                    if (!TryDescribeTank(tank, out var state))
                    {
                        if (LastFailureReason is not null)
                        {
                            return FailedProbe(LastFailureReason, pipeTanks, waterTanks);
                        }

                        continue;
                    }

                    if (!state.IsFinished || !state.HasPipeGraph)
                    {
                        continue;
                    }

                    pipeTanks++;
                    if (!IsWaterCapable(in state))
                    {
                        continue;
                    }

                    waterTanks++;
                    if (state.VolumeM3 > 0f)
                    {
                        wateredTanks++;
                    }
                }

                if (pipeTanks == 0)
                {
                    return new PipeProbe(PipeProbeReason.NoPipeNetwork, 0, 0, "no-pipe-network-tank");
                }

                if (waterTanks == 0)
                {
                    return new PipeProbe(PipeProbeReason.NoPipeTank, pipeTanks, 0, "no-water-capable-tank");
                }

                if (wateredTanks == 0)
                {
                    return new PipeProbe(PipeProbeReason.TankDry, pipeTanks, waterTanks,
                        "all-water-tanks-dry");
                }

                return new PipeProbe(PipeProbeReason.Connected, pipeTanks, waterTanks,
                    "watered-tanks=" + wateredTanks);
            }
            catch (Exception exception)
            {
                // REQ-V2-SAFE-01: a read-only probe must never escape into the panel/render path.
                SetFailure("water-probe-exception", exception);
                return FailedProbe("water-probe-exception:" + exception.GetType().Name);
            }
        }

        /// <summary>
        /// True when the tank accepts the water good and holds no foreign fluid (badwater); the
        /// finished/pipe-network filters stay with the caller because the census counts them first.
        /// </summary>
        internal bool IsWaterCapable(in PipeTankState candidate)
        {
            return candidate.TakesWaterGood &&
                (candidate.FluidGoodId is null ||
                 string.Equals(candidate.FluidGoodId, _waterGoodId, StringComparison.Ordinal));
        }
    }
}
