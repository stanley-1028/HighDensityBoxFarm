// ============================================================================
// HDBF v2 | Layer: Gameplay/Components | Owner: V2-GAMEPLAY-01 / V2-GAMEPLAY-02
// Purpose: Daily orchestration of one indoor farm: once-per-day power/port refresh, the
//          game-object/service context (workers, water, manual refill, output, entrance,
//          materials, mode transition), the pure IndoorDayTick call and the application of
//          its result (dirty flag, action tracking, refill outcome, warnings/logs).
// Requirements: REQ-V2-MAINT-03, REQ-V2-LABOR-03, REQ-V2-WATER-02/04, REQ-V2-BAL-03,
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
        private void OnDayTick()
        {
            try
            {
                RunDailyTick();
            }
            catch (Exception exception)
            {
                // REQ-V2-SAFE-01: one building's fault never reaches the game loop. The trigger is
                // re-armed in finally so a single bad day does not silently stop the farm forever.
                _log.Error(HdbfLogCategory.Error, IndoorLogKeys.Day,
                    "indoor daily tick failed; farm keeps its previous state", exception, _entityId.ToString());
            }
            finally
            {
                RearmDayTrigger();
            }
        }

        private void RunDailyTick()
        {
            if (_record is null || !_activated)
            {
                return;
            }

            // Phase 0: live game-object state that the persisted record mirrors once per day:
            // mechanical power (PowerConnected/PowerMode) and adapter-driven port availability.
            var liveStateChanged = false;
            try
            {
                liveStateChanged = UpdatePower();
            }
            catch (Exception exception)
            {
                _log.Error(HdbfLogCategory.Warn, IndoorLogKeys.PowerCut,
                    "power state update failed; keeping the previous power state", exception, _entityId.ToString());
            }

            try
            {
                liveStateChanged |= UpdatePorts();
            }
            catch (Exception exception)
            {
                _log.Error(HdbfLogCategory.Warn, IndoorLogKeys.Port,
                    "port state update failed; keeping the previous port flags", exception, _entityId.ToString());
            }

            // Each context source is isolated: a broken service degrades to "nothing available"
            // and the Core tick still runs for the rest of the farm (REQ-V2-SAFE-01/04).
            var activeWorkers = ReadActiveWorkers();
            var currentDay = ReadCurrentDay();
            _entranceBlocked = ReadEntranceBlocked();
            var output = _outputSink;
            var water = ReadPipeWater(out var hasPipeWater);
            var materials = _materials;

            // A queued trip without a source (loaded/repaired state) is dropped rather than retried.
            if (_manualRefillRequested && _pendingManualSource is null)
            {
                _manualRefillRequested = false;
            }

            var manualRequested = _manualRefillRequested && _pendingManualSource is not null;
            var context = new IndoorDayContext
            {
                ActiveWorkers = activeWorkers,
                CurrentDay = currentDay,
                EntranceBlocked = _entranceBlocked,
                Output = output,
                Water = water,
                ManualWater = manualRequested ? _pendingManualSource : null,
                Materials = materials,
                HasPipeWater = hasPipeWater,
                ManualRefillRequested = manualRequested,
                // Actions already spent today are reserved so the refill trip (2 more actions)
                // can never push the day past its budget (REQ-V2-LABOR-01).
                ReservedActions = ActionsUsedToday(),
                PreviousMode = ModeTransitionPrevious(currentDay),
            };

            IndoorDayResult result;
            try
            {
                // REQ-V2-MAINT-03: the fixed order lives entirely in Core (IndoorDayTick.Run).
                result = IndoorDayTick.Run(_record, _profile, _cropCatalog, in context);
            }
            catch (Exception exception)
            {
                _log.Error(HdbfLogCategory.Error, IndoorLogKeys.Day,
                    "IndoorDayTick threw; farm state kept for this day", exception, _entityId.ToString());
                return;
            }

            try
            {
                ApplyDayResult(in result);
                TrackDayActions(in result);
                ApplyManualRefillResult(in result);
                RefreshWaterSupply(hasPipeWater);
                if (liveStateChanged)
                {
                    _saveDirty = true;
                }
            }
            catch (Exception exception)
            {
                _log.Error(HdbfLogCategory.Error, IndoorLogKeys.Day,
                    "applying the indoor daily result failed; partial state kept", exception,
                    _entityId.ToString());
            }
        }

        /// <summary>
        /// Previous mode while the one-game-day transition runs, or null. The transition state is
        /// transient by design: a load falls back to the current mode and the panel reports the
        /// approximation (V2-GAMEPLAY-02 report / section 19.8).
        /// </summary>
        private IndoorMode? ModeTransitionPrevious(float currentDay)
        {
            if (float.IsNaN(_lastModeChangeDay))
            {
                return null;
            }

            return currentDay - _lastModeChangeDay <= IndoorConstants.ModeTransitionDays
                ? _modeTransitionFrom
                : (IndoorMode?)null;
        }

        private void ApplyDayResult(in IndoorDayResult result)
        {
            _lastCoverage = result.Coverage;
            _lastWarnings = result.Warnings;
            _lastStalled = result.Stalled;
            if (result.Changed)
            {
                _saveDirty = true;
            }

            // Adapter health is persisted for the panel and saved as a compact string.
            var adapterState = ReadAdapterState();
            if (!string.Equals(_record.TimberPipesAdapter, adapterState, StringComparison.Ordinal))
            {
                _record.TimberPipesAdapter = adapterState;
                _saveDirty = true;
            }

            LogProblems(in result);
        }

        private void LogProblems(in IndoorDayResult result)
        {
            // REQ-V2-READ-03: every line goes through the throttled log, one emission per key per
            // 30 s window; the entity id lives in the message, never in the key.
            _log.Write(HdbfLogCategory.Info, IndoorLogKeys.WaterSettle,
                "indoor water settled: coverage=" + Format(result.Coverage) + ", tank=" + Format(_record.Tank)
                + ", entityId=" + _entityId);

            // Daily action evidence line (in-game verification reads harvest/plant counters from
            // the log; the panel is not clickable in unattended rounds).
            if (result.Actions.Harvested > 0 || result.Actions.Planted > 0
                || result.Actions.ActionsUsed > 0 || result.Actions.Backlog)
            {
                _log.Write(HdbfLogCategory.Info, IndoorLogKeys.Day,
                    "indoor actions: harvested=" + result.Actions.Harvested
                    + ", planted=" + result.Actions.Planted
                    + ", actionsUsed=" + result.Actions.ActionsUsed
                    + ", blockedByOutput=" + result.Actions.BlockedByOutput
                    + ", growthMultiplier=" + Format(result.GrowthMultiplier)
                    + ", entityId=" + _entityId);
            }

            if ((result.Warnings & IndoorWarningFlags.NoWater) != 0)
            {
                _log.Write(HdbfLogCategory.Warn, IndoorLogKeys.RefillFailed,
                    "indoor farm has no usable water; coverage=" + Format(result.Coverage)
                    + ", entityId=" + _entityId);
            }
            else if ((result.Warnings & IndoorWarningFlags.LowWater) != 0)
            {
                _log.Write(HdbfLogCategory.Warn, IndoorLogKeys.Low,
                    "indoor farm coverage below C_min; entityId=" + _entityId);
            }

            if ((result.Warnings & IndoorWarningFlags.MaintenanceDue) != 0)
            {
                _log.Write(HdbfLogCategory.Warn, IndoorLogKeys.Maintenance,
                    "indoor maintenance overdue; cyclesMissed=" + _record.MaintenanceCyclesMissed
                    + ", entityId=" + _entityId);
            }

            if ((result.Warnings & IndoorWarningFlags.MissingMaterial) != 0)
            {
                _log.Write(HdbfLogCategory.Warn, IndoorLogKeys.MaintenanceMissing,
                    "indoor maintenance material missing; cyclesMissed=" + _record.MaintenanceCyclesMissed
                    + ", entityId=" + _entityId);
            }

            if (result.Stalled)
            {
                _log.Write(HdbfLogCategory.Warn, IndoorLogKeys.Maintenance,
                    "indoor farm maintenance stalled; farm actions paused, crops keep growing, entityId="
                    + _entityId);
            }

            if ((result.Warnings & IndoorWarningFlags.NoPower) != 0)
            {
                _log.Write(HdbfLogCategory.Warn, IndoorLogKeys.PowerCut,
                    "indoor top tier has no grid power; G_power=0.60 and x2 upkeep apply, entityId="
                    + _entityId);
            }

            if ((result.Warnings & IndoorWarningFlags.EntranceBlocked) != 0)
            {
                _log.Write(HdbfLogCategory.Warn, IndoorLogKeys.EntranceBlocked,
                    "indoor farm entrance is blocked; farm actions paused, entityId=" + _entityId);
            }

            if (result.Actions.Backlog)
            {
                _log.Write(HdbfLogCategory.Info, IndoorLogKeys.ActionBacklog,
                    "indoor harvest backlog (output full), entityId=" + _entityId);
            }
        }

        private static string Format(float value)
        {
            return value.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
        }

        private void RearmDayTrigger()
        {
            var trigger = _dayTrigger;
            if (trigger is null || !_activated)
            {
                return;
            }

            try
            {
                trigger.Reset();
                trigger.Resume();
            }
            catch (Exception exception)
            {
                _log.Error(HdbfLogCategory.Error, IndoorLogKeys.Day,
                    "day trigger re-arm failed", exception, _entityId.ToString());
            }
        }
    }
}
