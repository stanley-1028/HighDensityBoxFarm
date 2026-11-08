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

                // L-18: apply the session config feature flags to the record before any daily
                // subsystem reads it (forced ports, rotation fallback, one-time disabled log).
                ApplyFeatureFlags();

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
                // Player request 2026-11-08: the farm works without haulers (buffered output,
                // pipe/manual water), so the vanilla "requires haulers to function." status must
                // never be shown on it (see .HaulerStatus.cs).
                SuppressHaulerRequirementStatus();
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
            // A re-activation (rebuilt/reloaded farm) must probe the vanilla hauler status again
            // instead of trusting the reference of the previous activation.
            ResetHaulerRequirementStatus();
            // Pause integration stops with the finished state; a later activation re-binds and
            // re-syncs from the live PausableBuilding value (see .Pause.cs).
            UnbindPauseState();
            _paused = false;
            _pauseReconcile.Unregister(this);
            // L-13 (2026-11-02): never rely on the vanilla ForceRemoveEveryone call ordering when
            // the finished state is lost (or the building is deleted); eject a residual enterer out
            // first. Failure-isolated: a broken enterer must not abort the deactivation.
            try
            {
                EjectBeaversFromPausedFarm("deactivate");
            }
            catch (Exception exception)
            {
                _log.Error(HdbfLogCategory.Warn, LogKeyEntity,
                    "beaver ejection on deactivation failed; the vanilla enterable cleanup still runs",
                    exception, _entityId.ToString());
            }

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
            ResetHaulerRequirementStatus();
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
