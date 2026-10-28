// ============================================================================
// HDBF v2 | Layer: Gameplay/Components | Owner: V2-GAMEPLAY-02
// Purpose: One continuous water-drain step of one farm (player request 2026-10-28): turns the
//          elapsed game-day delta handed over by HdbfWaterFlowService into a water slice and drains
//          the internal tank with the pure Core math, so the tank empties per second instead of one
//          daily deduction. The daily tick still owns refill, coverage, growth and warnings.
// Requirements: REQ-V2-WATER-02, REQ-V2-PERF-02, REQ-V2-SAFE-01
// ============================================================================
using System;
using HDBF.Core;
using HDBF.Core.Indoor;
using HDBF.Core.Logging;

namespace HDBF.Gameplay.Components
{
    public partial class HdbfBoxFarmController
    {
        /// <summary>
        /// Drains the share of the daily demand that belongs to <paramref name="elapsedGameDays"/>.
        /// Called every frame by <see cref="HdbfWaterFlowService"/> while the farm is finished; the
        /// slice is proportional to game time, so pause/speed and frame rate never change the daily
        /// total. Never touches the day tick's decisions and never throws (REQ-V2-SAFE-01).
        /// </summary>
        /// <param name="elapsedGameDays">Game days elapsed since the previous frame; 0/NaN drains nothing.</param>
        internal void ConsumeElapsedWater(float elapsedGameDays)
        {
            if (!_activated || _record is null || !(elapsedGameDays > 0f))
            {
                return;
            }

            try
            {
                // Player request 2026-10-28: water goods a hauler just delivered become tank water
                // immediately (every frame), never waiting for the next daily tick/panel refresh.
                ApplyHauledWater();

                var effectiveSlots = IndoorLaborMath.EffectiveSlots(_profile, ReadActiveWorkers());
                var slice = IndoorWaterMath.DemandForElapsedDays(effectiveSlots,
                    _profile.WaterPerSlotPerDay, _record.Mode, elapsedGameDays);
                if (!(slice > 0f))
                {
                    return;
                }

                if (IndoorWaterMath.Consume(_record, _profile, slice) > 0f)
                {
                    // The tank is persisted state: one dirty bit for the whole frame is enough.
                    _saveDirty = true;
                }
            }
            catch (Exception exception)
            {
                // REQ-V2-SAFE-01: the daily tick still reads and refills the tank when the drain fails.
                _log.Error(HdbfLogCategory.Warn, IndoorLogKeys.WaterSettle,
                    "continuous water drain failed for this frame; the daily tick keeps the tank",
                    exception, _entityId.ToString());
            }
        }
    }
}
