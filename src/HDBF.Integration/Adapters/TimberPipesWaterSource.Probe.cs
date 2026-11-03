// ============================================================================
// HDBF v2 | Layer: Integration/Adapters | Owner: V2-INTEG-01
// Purpose: Read-only pipe probe half of the Timber Pipes water source (WP-05/RE-05/L-02):
//          report why the pipe port is (not) usable and whether the available water is only
//          settlement-wide. Observation only: no draw, no health strike, never throws.
// Requirements: REQ-V2-WATER-02, REQ-V2-COMPAT-01, REQ-V2-COMPAT-02, REQ-V2-SAFE-05
// ============================================================================
using System;
using HDBF.Core;
using HDBF.Core.Indoor;
using HDBF.Core.Logging;

namespace HDBF.Integration.Adapters
{
    /// <summary>
    /// The observation-only half of <see cref="TimberPipesWaterSource"/> (WP-05/RE-05/L-02). It
    /// classifies the settlement tank census into the frozen <see cref="PipeProbeReason"/> values
    /// and, with the optional farm anchor (L-02(c)), tells "the farm sits on a pipe graph" apart
    /// from "the settlement supplies pipe water elsewhere" (<see cref="PipeProbeReason.GlobalSupply"/>).
    /// No water is drawn, no three-strike failure is fed, and every failure falls back to the census.
    /// </summary>
    public sealed partial class TimberPipesWaterSource
    {
        /// <summary>
        /// Farm pipe-port position of the panel that is asking, published by the Gameplay snapshot
        /// right before the probe runs; null means unknown, so the probe keeps the census answer.
        /// </summary>
        private PipeProbeAnchor? _probeAnchor;

        /// <inheritdoc />
        public void SetProbeAnchor(PipeProbeAnchor? anchor)
        {
            _probeAnchor = anchor;
        }

        /// <inheritdoc />
        public PipeProbe ProbePipe()
        {
            if (!IsAdaptorServing())
            {
                return new PipeProbe(PipeProbeReason.AdapterUnavailable, 0, 0, "adapter-not-serving");
            }

            var probe = ClassifyFarmGraph(_binding.ProbeTanks());
            try
            {
                // ThrottledLog keeps this to one line per 30 s for the key (REQ-V2-READ-03).
                _log?.Write(HdbfLogCategory.Compat, IndoorLogKeys.PipeProbe,
                    "pipe probe: reason=" + probe.Reason + ", pipeTanks=" + probe.TankCount
                    + ", waterTanks=" + probe.WaterTankCount + ", detail=" + probe.Detail);
            }
            catch (Exception)
            {
                // Diagnostics must never break a probe (REQ-V2-SAFE-01).
            }

            return probe;
        }

        /// <summary>
        /// L-02(c)/WP-05 honesty pass: HDBF draws water from the settlement-wide tank census, while a
        /// box farm is not a Timber Pipes endpoint. When the census finds watered network tanks but
        /// the farm's own pipe port carries no pipe graph, the truthful label is
        /// <see cref="PipeProbeReason.GlobalSupply"/> ("connected, settlement-wide supply") instead of
        /// pretending the farm itself is connected. An unknown graph answer (no anchor, older mod,
        /// reflection drift) keeps the census result, so the probe can never change the draw path.
        /// </summary>
        /// <param name="probe">Census result of <see cref="TimberPipesWaterBinding.ProbeTanks"/>.</param>
        /// <returns>Census result, or the settlement-wide variant when the farm port has no graph.</returns>
        private PipeProbe ClassifyFarmGraph(PipeProbe probe)
        {
            if (probe.Reason != PipeProbeReason.Connected)
            {
                // No watered network tank: the existing no-network/no-tank/dry reasons stay exact.
                return probe;
            }

            var anchor = _probeAnchor;
            if (anchor is null)
            {
                return probe;
            }

            try
            {
                if (!_binding.TryProbeFarmGraph(anchor.Value.X, anchor.Value.Y, anchor.Value.Z, out var hasGraph)
                    || hasGraph)
                {
                    return probe;
                }

                return new PipeProbe(PipeProbeReason.GlobalSupply, probe.TankCount, probe.WaterTankCount,
                    "settlement-wide-supply; farm-port-no-graph");
            }
            catch (Exception)
            {
                // REQ-V2-SAFE-01: a read-only diagnostic must never break the panel.
                return probe;
            }
        }
    }
}
