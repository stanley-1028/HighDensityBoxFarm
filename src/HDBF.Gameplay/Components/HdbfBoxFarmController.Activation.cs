// ============================================================================
// HDBF v2 | Layer: Gameplay/Components | Owner: V2-GAMEPLAY-01
// Purpose: Finished-state activation, query/world-icon registration, tier/faction
//          resolution and lifetime trigger creation for the indoor controller.
// Requirements: REQ-V2-BLD-02, REQ-V2-LABOR-03, REQ-V2-SAFE-01/04, REQ-V2-MAINT-04
// ============================================================================
using System;
using HDBF.Core;
using HDBF.Core.Balance;
using HDBF.Core.Indoor;
using HDBF.Core.Logging;

namespace HDBF.Gameplay.Components
{
    public partial class HdbfBoxFarmController
    {
        private void Activate()
        {
            if (_activated || !_awake || _record is null)
            {
                return;
            }

            if (_blockObject is not null && !_blockObject.IsFinished)
            {
                return;
            }

            try
            {
                _activated = true;
                SyncEntityIdentity();
                if (!_saveLoaded)
                {
                    _record.EntityId = _saveEntityId.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }

                // Hauled water (REQ-V2-WATER-02): the vanilla inventory initializer creates the water
                // input inventory disabled and the owner building must enable it; otherwise the
                // fill-input hauling chain never sees a target (see EnsureWaterInventoryEnabled).
                EnsureWaterInventoryEnabled();

                // Continuous water drain (player request 2026-10-28): the flow service drains every
                // registered finished farm per frame; a farm that is not finished never registers.
                _waterFlow.Register(this);

                EnsureMaintenanceDeadline();
                // The persisted power/port flags are refreshed once at activation as well, so a
                // freshly built or loaded building shows the real live state before its first day
                // tick (REQ-V2-BAL-03 / REQ-V2-COMPAT-02). Both calls are isolated internally.
                try
                {
                    UpdatePower();
                    UpdatePorts();
                    // A fresh/loaded farm gets its water-supply kind before the first day tick, so
                    // the panel can enable the refill button immediately (REQ-V2-WATER-03/04).
                    _ = ReadPipeWater(out var hasPipeWaterAtActivation);
                    RefreshWaterSupply(hasPipeWaterAtActivation);
                }
                catch (Exception exception)
                {
                    _log.Error(HdbfLogCategory.Warn, LogKeyEntity,
                        "initial power/port refresh failed; the first day tick retries", exception,
                        _entityId.ToString());
                }

                _farms.RegisterSource(this);
                EnableComponent();
                if (_dayTrigger is null)
                {
                    // The day counter starts when the building is finished, not when its
                    // construction entity was created (REQ-V2-MAINT-03: one tick per game day).
                    _dayTrigger = _timeTriggerFactory.Create(OnDayTick, DayTriggerDelayDays);
                }
                else
                {
                    _dayTrigger.Reset();
                }

                _dayTrigger.Resume();
                _worldIcons.Register(_entityId, this);
                // Player report 2026-10-29 "it crash when you paused it": subscribe to the vanilla
                // building pause and stop every per-farm subsystem while the farm is paused; a farm
                // loaded in the paused state enters the paused state right here (see .Pause.cs).
                BindPauseState();
                SyncPauseStateOnActivate();
                _log.Write(HdbfLogCategory.Info, LogKeyEntity,
                    "indoor farm active, entityId=" + _entityId + ", tier=" + _profile.Tier
                    + ", faction=" + _faction + ", blueprint=" + (BlueprintId() ?? "<null>"));
            }
            catch (Exception exception)
            {
                _activated = false;
                TryQuiesceAfterFailedActivation();
                _log.Error(HdbfLogCategory.Error, LogKeyEntity,
                    "indoor farm activation failed; component disabled", exception, _entityId.ToString());
            }
        }

        private void Deactivate()
        {
            if (!_activated)
            {
                return;
            }

            _activated = false;
            // Pause integration stops with the finished state; a later activation re-binds and
            // re-syncs from the live PausableBuilding value (see .Pause.cs).
            UnbindPauseState();
            _paused = false;
            // Continuous drain stops with the finished state; the tank keeps its last value.
            _waterFlow.Unregister(this);
            try
            {
                _dayTrigger?.Pause();
            }
            catch (Exception exception)
            {
                _log.Error(HdbfLogCategory.Warn, LogKeyEntity, "day trigger pause failed", exception,
                    _entityId.ToString());
            }

            try
            {
                _farms.UnregisterSource(_entityId);
            }
            catch (Exception exception)
            {
                _log.Error(HdbfLogCategory.Warn, LogKeyEntity, "farm source unregister failed", exception,
                    _entityId.ToString());
            }

            try
            {
                _worldIcons.Deactivate(_entityId);
            }
            catch (Exception exception)
            {
                _log.Error(HdbfLogCategory.Warn, LogKeyEntity, "world icon deactivation failed", exception,
                    _entityId.ToString());
            }

            DisableWaterInventory();
            DisableComponent();
        }

        private void TryQuiesceAfterFailedActivation()
        {
            _waterFlow.Unregister(this);
            try
            {
                _farms.UnregisterSource(_entityId);
            }
            catch (Exception)
            {
                // Activation is already failing; cleanup must not throw a second exception.
            }

            try
            {
                _worldIcons.Deactivate(_entityId);
            }
            catch (Exception)
            {
                // Activation is already failing; cleanup must not throw a second exception.
            }

            DisableComponent();
        }

        /// <summary>
        /// Fresh buildings get their first material-maintenance deadline from the activation day,
        /// not from the (much earlier) construction-site day. A loaded record keeps its saved
        /// deadline; only a missing/unusable deadline is resolved to one full cycle (REQ-V2-BAL-02).
        /// </summary>
        private void EnsureMaintenanceDeadline()
        {
            if (_maintenanceInitialized || _record is null)
            {
                return;
            }

            _maintenanceInitialized = true;
            if (_profile.MaintenanceKind == HdbfMaintenanceKind.IrrigationOnly)
            {
                return;
            }

            var today = ReadCurrentDay();
            var cycleDays = MaintenanceDefaults.DefaultCycleDays(_profile);
            if (cycleDays <= 0f)
            {
                return;
            }

            var dueDay = today + cycleDays;
            if (float.IsNaN(dueDay) || float.IsInfinity(dueDay))
            {
                return;
            }

            _record.MaintenanceDueDay = dueDay;
            _record.MaintenanceCyclesMissed = 0;
            _saveDirty = true;
        }

        private void ResolveMaintenanceAfterLoad()
        {
            if (_record is null)
            {
                return;
            }

            _maintenanceInitialized = true;
            var today = ReadCurrentDay();
            var resolved = MaintenanceDefaults.ResolveDueDay(_profile, today, _record.MaintenanceDueDay);
            if (resolved != _record.MaintenanceDueDay)
            {
                _record.MaintenanceDueDay = resolved;
                _saveDirty = true;
            }
        }

        private void ResolveTierProfile()
        {
            var blueprintId = BlueprintId();
            var fallbackTier = Enum.TryParse(_spec.Tier, true, out HdbfTier parsedTier)
                               && Enum.IsDefined(typeof(HdbfTier), parsedTier)
                ? parsedTier
                : HdbfTier.Basic;
            try
            {
                if (!_configProvider.TryGetTierProfile(blueprintId, fallbackTier, out var profile)
                    || profile is null)
                {
                    _log.Write(HdbfLogCategory.Warn, HdbfLogKeys.ConfigInvalid,
                        "unknown box farm blueprint '" + (blueprintId ?? "<null>")
                        + "'; using tier " + fallbackTier);
                    _profile = TierCatalog.Get(fallbackTier);
                    return;
                }

                _profile = profile;
            }
            catch (Exception exception)
            {
                _profile = TierCatalog.Get(fallbackTier);
                _log.Error(HdbfLogCategory.Warn, HdbfLogKeys.ConfigInvalid,
                    "tier table lookup failed; using the compiled default", exception);
            }
        }

        private string? BlueprintId()
        {
            try
            {
                var blueprint = _spec.Blueprint;
                return blueprint is not null ? blueprint.Name : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private void SyncEntityIdentity()
        {
            if (_entityComponent is not null)
            {
                _entityId = _entityComponent.EntityId;
            }

            _saveEntityId = ToSaveEntityId(_entityId);
        }

        /// <summary>
        /// Deterministic map from the runtime Guid entity id to the long save identity. Stable for
        /// one entity for the whole save and only used as an opaque diagnostic field.
        /// </summary>
        private static long ToSaveEntityId(Guid entityId)
        {
            if (entityId == Guid.Empty)
            {
                return 0L;
            }

            var bytes = entityId.ToByteArray();
            return BitConverter.ToInt64(bytes, 0);
        }
    }
}
