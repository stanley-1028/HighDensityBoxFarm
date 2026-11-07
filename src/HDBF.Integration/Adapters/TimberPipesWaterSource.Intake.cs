// ============================================================================
// HDBF v2 | Layer: Integration/Adapters | Owner: V2-INTEG-01
// Purpose: Farm-adjacent intake half of the Timber Pipes water source: keeps the farm
//          footprint the Gameplay snapshot/day tick publishes and answers the three read/draw
//          questions the water path needs (connection exists, water available, draw it).
//          Player report 2026-11-07: a water pipe attached to the farm shell must supply it.
// Requirements: REQ-V2-WATER-02, REQ-V2-COMPAT-01, REQ-V2-SAFE-05
// ============================================================================
using System;
using System.Collections.Generic;
using HDBF.Core.Indoor;

namespace HDBF.Integration.Adapters
{
    public sealed partial class TimberPipesWaterSource
    {
        /// <summary>Footprint instance last published by Gameplay; reference-equal means unchanged.</summary>
        private IReadOnlyList<PipeCell>? _intakeFootprint;

        /// <summary>Face-neighbour cells of <see cref="_intakeFootprint"/>; null when unknown.</summary>
        private PipeCell[]? _intakeNeighbors;

        /// <summary>
        /// Stores the farm footprint published by Gameplay (optional companion contract) and
        /// rebuilds the neighbour cell cache only when a different footprint arrives, so a 4 Hz
        /// panel snapshot never reallocates. Null clears the anchor: the source then falls back to
        /// the settlement-wide tank path. Never throws.
        /// </summary>
        internal void SetIntakeFootprint(IReadOnlyList<PipeCell>? footprint)
        {
            if (ReferenceEquals(_intakeFootprint, footprint))
            {
                return;
            }

            _intakeFootprint = footprint;
            try
            {
                _intakeNeighbors = PipeIntakeMath.NeighborCells(footprint);
            }
            catch (Exception)
            {
                // REQ-V2-SAFE-01: a broken footprint only disables the optional intake path.
                _intakeNeighbors = null;
            }
        }

        /// <summary>True when a finished transport pipe touches the farm footprint (even if dry).</summary>
        private bool HasIntakeConnection()
        {
            try
            {
                return _binding.TryReadIntake(_intakeNeighbors, out var state) && state.PipeCount > 0;
            }
            catch (Exception)
            {
                // Query only; never throw into Gameplay/UI (REQ-V2-SAFE-01).
                return false;
            }
        }

        /// <summary>True when a farm-adjacent transport pipe holds drawable water right now.</summary>
        private bool HasIntakeWater()
        {
            try
            {
                return _binding.TryReadIntake(_intakeNeighbors, out var state) && state.WateredPipes > 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Drains up to the requested m3 from the farm-adjacent pipes (real pipe fluid).</summary>
        private bool TryDrawIntake(float requestedVolumeM3, out float drawnVolumeM3)
        {
            try
            {
                return _binding.TryDrawIntake(_intakeNeighbors, requestedVolumeM3, out drawnVolumeM3);
            }
            catch (Exception)
            {
                drawnVolumeM3 = 0f;
                return false;
            }
        }
    }
}
