// ============================================================================
// HDBF v2 | Layer: Gameplay/Components | Owner: V2-GAMEPLAY-02
// Purpose: The manual water refill command and its daily execution bookkeeping
//          (section 19.6 / REQ-V2-WATER-04): a real reachable source within 12 cells,
//          the TankCapacity/3 refill, the 2-action cost paid from today's budget and the
//          specific failure reasons returned by the command.
// Requirements: REQ-V2-WATER-02, REQ-V2-WATER-04, REQ-V2-LABOR-01, REQ-V2-UI-02,
//               REQ-V2-SAFE-01
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
        /// Queues one manual water refill trip (section 19.6). The command validates the whole trip
        /// synchronously - active farm, on-duty worker, open entrance, 2 free farm actions and a
        /// real reachable 12-cell water source - so a rejection can name the concrete reason. The
        /// next daily tick performs the draw and spends the 2 actions (REQ-V2-WATER-04).
        /// </summary>
        /// <param name="failureKey">Specific failure key when false.</param>
        /// <returns>True when the refill request is queued.</returns>
        public bool TryRequestManualRefill(out string? failureKey)
        {
            failureKey = null;
            _lastCommandFailureKey = null;
            if (_record is null || !_activated)
            {
                failureKey = HdbfIndoorCommandFailures.NotActive;
                _lastCommandFailureKey = failureKey;
                return false;
            }

            if (_manualRefillRequested && _pendingManualSource is not null)
            {
                // One queued trip at a time; repeating the click must not queue a second one.
                return true;
            }

            try
            {
                var workers = ReadActiveWorkers();
                if (workers <= 0)
                {
                    failureKey = HdbfIndoorCommandFailures.NoWorkers;
                    _lastCommandFailureKey = failureKey;
                    _log.Write(HdbfLogCategory.Warn, IndoorLogKeys.ManualRefill,
                        "manual refill rejected: no worker on duty, entityId=" + _entityId);
                    return false;
                }

                if (_entranceBlocked || ReadEntranceBlocked())
                {
                    failureKey = HdbfIndoorCommandFailures.EntranceBlocked;
                    _lastCommandFailureKey = failureKey;
                    _log.Write(HdbfLogCategory.Warn, IndoorLogKeys.ManualRefill,
                        "manual refill rejected: entrance blocked, entityId=" + _entityId);
                    return false;
                }

                var freeActions = RemainingActionsToday(workers);
                if (freeActions < IndoorConstants.ManualRefillActions)
                {
                    failureKey = HdbfIndoorCommandFailures.NoActionsLeft;
                    _lastCommandFailureKey = failureKey;
                    _log.Write(HdbfLogCategory.Warn, IndoorLogKeys.ManualRefill,
                        "manual refill rejected: only " + freeActions + " of the daily actions are left, entityId="
                        + _entityId);
                    return false;
                }

                var requiredUnits = IndoorWaterMath.RefillAmount(_profile.TankCapacity);
                if (!_manualWater.TryFindSource(this, requiredUnits, out var source, out var sourceFailure)
                    || source is null)
                {
                    failureKey = sourceFailure ?? HdbfIndoorCommandFailures.NoWaterSource;
                    _lastCommandFailureKey = failureKey;
                    _manualSourceAvailable = false;
                    _log.Write(HdbfLogCategory.Warn, IndoorLogKeys.ManualRefill,
                        "manual refill rejected: " + failureKey + ", entityId=" + _entityId);
                    return false;
                }

                _pendingManualSource = source;
                _manualRefillRequested = true;
                _manualSourceAvailable = true;
                _log.Write(HdbfLogCategory.Info, IndoorLogKeys.ManualRefill,
                    "manual refill queued (2 farm actions, source=" + source.Kind + "), entityId=" + _entityId);
                return true;
            }
            catch (Exception exception)
            {
                failureKey = HdbfIndoorCommandFailures.RefillUnavailable;
                _lastCommandFailureKey = failureKey;
                _log.Error(HdbfLogCategory.Error, IndoorLogKeys.RefillFailed,
                    "manual refill request failed; no state changed", exception, _entityId.ToString());
                return false;
            }
        }

        /// <summary>
        /// Refreshes the panel-facing water supply kind once per day: pipe when the adapter is
        /// connected, manual when a reachable 12-cell source was probed, otherwise none. The panel
        /// enables its refill button only for <see cref="WaterSupplyKind.Manual"/>.
        /// </summary>
        private void RefreshWaterSupply(bool hasPipeWater)
        {
            if (hasPipeWater)
            {
                _waterSupply = WaterSupplyKind.Pipe;
                _manualSourceAvailable = true;
                return;
            }

            if (_manualRefillRequested && _pendingManualSource is not null)
            {
                _manualSourceAvailable = true;
                _waterSupply = WaterSupplyKind.Manual;
                return;
            }

            try
            {
                var requiredUnits = IndoorWaterMath.RefillAmount(_profile.TankCapacity);
                _manualSourceAvailable = _manualWater.TryFindSource(this, requiredUnits, out var source, out _)
                                         && source is not null;
            }
            catch (Exception exception)
            {
                _manualSourceAvailable = false;
                _log.Error(HdbfLogCategory.Warn, IndoorLogKeys.ManualRefill,
                    "manual water source refresh failed; treating as no source", exception,
                    _entityId.ToString());
            }

            _waterSupply = _manualSourceAvailable ? WaterSupplyKind.Manual : WaterSupplyKind.None;
        }

        /// <summary>Consumes the tick result of the queued manual refill trip (if any).</summary>
        private void ApplyManualRefillResult(in IndoorDayResult result)
        {
            if (result.ManualRefill == ManualRefillOutcome.NotRequested)
            {
                return;
            }

            _manualRefillRequested = false;
            _pendingManualSource = null;
            if (result.ManualRefill == ManualRefillOutcome.Completed)
            {
                _waterSupply = WaterSupplyKind.Manual;
                _manualSourceAvailable = true;
                _saveDirty = true;
                _log.Write(HdbfLogCategory.Info, IndoorLogKeys.ManualRefill,
                    "manual refill performed: requested +" + (IndoorWaterMath.RefillAmount(_profile.TankCapacity))
                    + " water units for 2 farm actions, entityId=" + _entityId);
                return;
            }

            _lastCommandFailureKey = OutcomeToFailureKey(result.ManualRefill);
            _log.Write(HdbfLogCategory.Warn, IndoorLogKeys.RefillFailed,
                "manual refill trip failed: " + result.ManualRefill + ", entityId=" + _entityId);
        }

        /// <summary>Counts the actions of the day (farm actions + refill) for the next command check.</summary>
        private void TrackDayActions(in IndoorDayResult result)
        {
            _actionsUsedToday = result.Actions.ActionsUsed + result.RefillActionsUsed;
            if (_actionsUsedToday < 0f)
            {
                _actionsUsedToday = 0f;
            }

            _actionsDayIndex = DayIndexOf(ReadCurrentDay());
            _lastBacklogActions = result.Actions.BlockedByOutput;
        }

        /// <summary>
        /// Remaining farm actions of the current day for command validation. A different game day
        /// starts a fresh budget, so a queued refill can never overspend yesterday's actions
        /// (REQ-V2-LABOR-01: no cross-day overspend).
        /// </summary>
        private int RemainingActionsToday(int workers)
        {
            return IndoorLaborMath.RemainingActions(workers, ActionsUsedToday());
        }

        /// <summary>
        /// Farm actions already spent today (0 after a new day starts). Passed to the day tick as
        /// <see cref="IndoorDayContext.ReservedActions"/> so a refill queued late in the day cannot
        /// push the day's total past the budget.
        /// </summary>
        private int ActionsUsedToday()
        {
            var today = DayIndexOf(ReadCurrentDay());
            return today == _actionsDayIndex ? (int)Math.Round(_actionsUsedToday) : 0;
        }

        private static int DayIndexOf(float day)
        {
            return float.IsNaN(day) || float.IsInfinity(day) ? -1 : (int)Math.Floor(day);
        }

        private static string OutcomeToFailureKey(ManualRefillOutcome outcome)
        {
            switch (outcome)
            {
                case ManualRefillOutcome.NoWorkers:
                    return HdbfIndoorCommandFailures.NoWorkers;
                case ManualRefillOutcome.EntranceBlocked:
                    return HdbfIndoorCommandFailures.EntranceBlocked;
                case ManualRefillOutcome.NoBudget:
                    return HdbfIndoorCommandFailures.NoActionsLeft;
                case ManualRefillOutcome.NoSource:
                    return HdbfIndoorCommandFailures.NoWaterSource;
                default:
                    return HdbfIndoorCommandFailures.RefillUnavailable;
            }
        }
    }
}
