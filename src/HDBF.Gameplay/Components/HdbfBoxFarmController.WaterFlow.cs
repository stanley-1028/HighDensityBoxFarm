// ============================================================================
// HDBF v2 | Layer: Gameplay/Components | Owner: V2-GAMEPLAY-02
// Purpose: One continuous water-drain step of one farm (player request 2026-10-28): turns the
//          elapsed game-day delta handed over by HdbfWaterFlowService into a water slice and drains
//          the internal tank with the pure Core math, so the tank empties per second instead of one
//          daily deduction. The daily tick still owns refill, coverage, growth and warnings.
//          L-14: the hauled-water conversion and the inventory re-resolve are frame-throttled.
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
        /// L-14: frames between two hauled-water conversions (approx. 0.5 s at 60 fps). The conversion
        /// only moves whole water goods that already sit in the farm's input inventory, so running it
        /// every N frames instead of every frame cannot change the daily totals - it only stops a
        /// 20-farm settlement from re-scanning its inventory state 60 times per second per farm.
        /// </summary>
        private const int HauledWaterFrameInterval = 30;

        /// <summary>L-14: frames before an unresolvable water input inventory is looked up again (~5 s).</summary>
        private const int WaterInventoryResolveCooldownFrames = 300;

        /// <summary>Frames left before the next hauled-water conversion (0 = due now).</summary>
        private int _hauledWaterCountdown;

        /// <summary>Frames left before another water-inventory resolve after a miss (0 = due now).</summary>
        private int _waterInventoryResolveCooldown;

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
                // promptly (L-14: throttled to ~0.5 s, never waiting for the next daily tick).
                ApplyHauledWaterThrottled();

                var effectiveSlots = IndoorLaborMath.EffectiveSlots(_profile, ReadActiveWorkers());
                var slice = IndoorWaterMath.DemandForElapsedDays(effectiveSlots,
                    _profile.WaterPerSlotPerDay, _record.Mode, elapsedGameDays);
                if (!(slice > 0f))
                {
                    return;
                }

                // L-14: set the persisted-state dirty bit only when the tank really changed.
                var before = _record.Tank;
                IndoorWaterMath.Consume(_record, _profile, slice);
                if (_record.Tank != before)
                {
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

        /// <summary>
        /// L-14: runs <see cref="ApplyHauledWater"/> at most once per
        /// <see cref="HauledWaterFrameInterval"/> frames. The conversion is idempotent for a frame
        /// where nothing was delivered, so throttling only removes the repeated inventory scan.
        /// </summary>
        private void ApplyHauledWaterThrottled()
        {
            if (_hauledWaterCountdown > 0)
            {
                _hauledWaterCountdown--;
                return;
            }

            _hauledWaterCountdown = HauledWaterFrameInterval;
            ApplyHauledWater();
        }

        /// <summary>
        /// L-14: true when an unresolvable water input inventory is still inside its re-resolve
        /// cooldown, so the per-frame hauling path does not rescan every inventory of the farm.
        /// </summary>
        /// <returns>True when the caller should skip the inventory resolve this frame.</returns>
        private bool ShouldSkipInventoryResolve()
        {
            if (_waterInventory is not null)
            {
                return false;
            }

            if (_waterInventoryResolveCooldown > 0)
            {
                _waterInventoryResolveCooldown--;
                return true;
            }

            _waterInventoryResolveCooldown = WaterInventoryResolveCooldownFrames;
            return false;
        }

        /// <summary>L-14: clears the cooldown once a usable water input inventory is resolved.</summary>
        private void ResetInventoryResolveCooldown()
        {
            _waterInventoryResolveCooldown = 0;
        }

        /// <summary>
        /// WP-05/L-02(c): publishes this farm's pipe-port position to the read-only pipe probe right
        /// before the panel snapshot asks for the probe state. HDBF draws pipe water settlement-wide,
        /// so without the position the probe cannot tell "this farm is on the network" from "the
        /// settlement supplies water elsewhere". The anchor is diagnostic only: it never draws water,
        /// never reports a failure and a null block object simply leaves the previous anchor unset.
        /// </summary>
        private void PublishProbeAnchor()
        {
            try
            {
                // Player report 2026-11-07: the panel snapshot also needs the water footprint before
                // it asks the adapter whether a pipe attached to the farm supplies water.
                PublishWaterAnchor();
                var probe = _adapterHost as IIndoorPipeProbeAnchor;
                var position = _blockObject?.Coordinates;
                if (probe is null || position is null)
                {
                    return;
                }

                probe.SetProbeAnchor(new PipeProbeAnchor(position.Value.x, position.Value.y, position.Value.z));
            }
            catch (Exception)
            {
                // The anchor is a read-only diagnostic: it must never break the snapshot/render path.
            }
        }
    }
}
