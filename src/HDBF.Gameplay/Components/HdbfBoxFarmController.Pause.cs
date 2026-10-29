// ============================================================================
// HDBF v2 | Layer: Gameplay/Components | Owner: V2-GAMEPLAY-01
// Purpose: Vanilla building-pause integration of one indoor farm (player report 2026-10-29:
//          "it crash when you paused it"). A paused farm must really stop, and it must never keep
//          a beaver stranded inside the blocked building: the vanilla pause only unassigns the
//          workplace's workers (Workplace.OnObjectBlocked -> UnassignAllWorkers -> Worker.Unemploy,
//          which does NOT call Enterer.Exit/Enterable.Remove), so the HDBF farm - the only farm that
//          keeps its farmers inside through WaitInsideIdlyWorkplaceBehavior - would otherwise hold
//          abandoned enterers inside a blocked building, exactly the state the vanilla
//          Enterer/Enterable/Accessible invariants are not written for.
// Requirements: REQ-V2-SAFE-01, REQ-V2-LABOR-03, REQ-V2-WATER-02, REQ-V2-MAINT-02
// Size: above the 300-line soft gate with a written reason (REQ-V2-MAINT-02): the pause state
//       machine, the beaver ejection and their failure isolation stay in one file so the
//       pause/resume path is reviewed as one unit.
// ============================================================================
using System;
using HDBF.Core;
using HDBF.Core.Indoor;
using HDBF.Core.Logging;
using Timberborn.Buildings;
using Timberborn.EnterableSystem;

namespace HDBF.Gameplay.Components
{
    public partial class HdbfBoxFarmController
    {
        /// <summary>Vanilla pause component auto-decorated on every BuildingSpec; may be absent when a mod removes it.</summary>
        private PausableBuilding? _pausable;

        /// <summary>The farm's vanilla enterable, used to move beavers out of a paused (blocked) building.</summary>
        private Enterable? _enterable;

        /// <summary>True while the vanilla building-pause toggle holds this farm.</summary>
        private bool _paused;

        /// <summary>
        /// Resolves the pause components once in Awake. Both are optional: a missing PausableBuilding
        /// (third-party mod removed it) only disables pause integration, and the farm stays fully
        /// playable (REQ-V2-SAFE-01).
        /// </summary>
        private void ResolvePauseComponents()
        {
            try
            {
                _pausable = HasComponent<PausableBuilding>() ? GetComponent<PausableBuilding>() : null;
            }
            catch (Exception exception)
            {
                _pausable = null;
                _log.Error(HdbfLogCategory.Warn, IndoorLogKeys.Paused,
                    "vanilla PausableBuilding could not be resolved; pause integration stays off", exception,
                    _entityId.ToString());
            }

            try
            {
                _enterable = HasComponent<Enterable>() ? GetComponent<Enterable>() : null;
            }
            catch (Exception exception)
            {
                _enterable = null;
                _log.Error(HdbfLogCategory.Warn, IndoorLogKeys.Paused,
                    "vanilla Enterable could not be resolved; beavers cannot be moved out on pause", exception,
                    _entityId.ToString());
            }
        }

        /// <summary>
        /// Subscribes to the vanilla pause toggle. Idempotent, so an activation after a failed one
        /// never registers the handler twice (a double subscription would run the stop twice).
        /// </summary>
        private void BindPauseState()
        {
            var pausable = _pausable;
            if (pausable is null)
            {
                return;
            }

            try
            {
                pausable.PausedChanged -= OnPausedChanged;
                pausable.PausedChanged += OnPausedChanged;
            }
            catch (Exception exception)
            {
                _log.Error(HdbfLogCategory.Warn, IndoorLogKeys.Paused,
                    "vanilla pause event could not be bound; pause integration stays off", exception,
                    _entityId.ToString());
            }
        }

        /// <summary>Removes the pause handler when the farm leaves the finished state.</summary>
        private void UnbindPauseState()
        {
            var pausable = _pausable;
            if (pausable is null)
            {
                return;
            }

            try
            {
                pausable.PausedChanged -= OnPausedChanged;
            }
            catch (Exception exception)
            {
                _log.Error(HdbfLogCategory.Warn, IndoorLogKeys.Paused,
                    "vanilla pause event could not be unbound", exception, _entityId.ToString());
            }
        }

        /// <summary>
        /// Applies the persisted/live pause state from activation. A save that was saved while the
        /// building was paused restores Paused through PausableBuilding.Load before this runs, so the
        /// farm must stop its services here even though the PausedChanged event happened earlier.
        /// </summary>
        private void SyncPauseStateOnActivate()
        {
            if (!TryReadPaused())
            {
                _paused = false;
                return;
            }

            // Force the pause entry: activation just registered the water flow/day trigger/icons,
            // so the early-return in ApplyPausedState would leave them running.
            _paused = false;
            ApplyPausedState(true);
        }

        private bool TryReadPaused()
        {
            try
            {
                return _pausable is not null && _pausable.Paused;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private void OnPausedChanged(object? sender, EventArgs e)
        {
            try
            {
                var paused = TryReadPaused();
                if (!_activated)
                {
                    // Load-order safety: only remember the state; Activate re-syncs from the live value.
                    _paused = paused;
                    return;
                }

                ApplyPausedState(paused);
            }
            catch (Exception exception)
            {
                // REQ-V2-SAFE-01: a failed pause transition must never escape into the game loop.
                _log.Error(HdbfLogCategory.Warn, IndoorLogKeys.Paused,
                    "indoor farm pause transition failed; the farm keeps its previous state", exception,
                    _entityId.ToString());
            }
        }

        private void ApplyPausedState(bool paused)
        {
            if (_paused == paused)
            {
                return;
            }

            _paused = paused;
            if (paused)
            {
                EnterPausedState();
            }
            else
            {
                ExitPausedState();
            }
        }

        /// <summary>
        /// Stops every per-farm subsystem the pause would otherwise leave running (the game clock is
        /// not stopped by a building pause) and moves every beaver out of the blocked building.
        /// </summary>
        private void EnterPausedState()
        {
            // Continuous water drain and the daily tick stop with the pause; the tank/record is
            // persisted state and keeps its last value until the player resumes.
            _waterFlow.Unregister(this);
            try
            {
                _dayTrigger?.Pause();
            }
            catch (Exception exception)
            {
                _log.Error(HdbfLogCategory.Warn, IndoorLogKeys.Paused,
                    "day trigger could not be paused", exception, _entityId.ToString());
            }

            try
            {
                _worldIcons.Deactivate(_entityId);
            }
            catch (Exception exception)
            {
                _log.Error(HdbfLogCategory.Warn, IndoorLogKeys.Paused,
                    "world warning icons could not be deactivated on pause", exception, _entityId.ToString());
            }

            EjectBeaversFromPausedFarm();
            _log.Write(HdbfLogCategory.Info, IndoorLogKeys.Paused,
                "indoor farm paused by the player; daily tick, water drain and warning icons stopped, entityId="
                + _entityId);
        }

        /// <summary>
        /// Restarts the subsystems stopped by the pause. The vanilla game re-staffs the then-understaffed
        /// workplace on its own, so no worker state is touched here.
        /// </summary>
        private void ExitPausedState()
        {
            // A paused save can finish loading with an enterer inside; clear it before work resumes
            // so the re-enter path never sees an "already inside" state.
            EjectBeaversFromPausedFarm();
            _waterFlow.Register(this);
            try
            {
                _dayTrigger?.Resume();
            }
            catch (Exception exception)
            {
                _log.Error(HdbfLogCategory.Warn, IndoorLogKeys.Resumed,
                    "day trigger could not be resumed", exception, _entityId.ToString());
            }

            try
            {
                _worldIcons.Register(_entityId, this);
            }
            catch (Exception exception)
            {
                _log.Error(HdbfLogCategory.Warn, IndoorLogKeys.Resumed,
                    "world warning icons could not be re-registered on resume", exception, _entityId.ToString());
            }

            _log.Write(HdbfLogCategory.Info, IndoorLogKeys.Resumed,
                "indoor farm resumed by the player; daily tick, water drain and warning icons restarted, entityId="
                + _entityId);
        }
    }
}
