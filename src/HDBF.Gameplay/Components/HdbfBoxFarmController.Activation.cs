using System;
using HDBF.Core;
using HDBF.Core.Logging;

namespace HDBF.Gameplay.Components
{
    /// <summary>
    /// Partial part: finished-state activation, registration with the query service and tier
    /// resolution. Activation is the only place that starts the day trigger and the first scan.
    /// </summary>
    public partial class HdbfBoxFarmController
    {
        // ------------------------------------------------------------------ activation helpers

        private void Activate()
        {
            if (_activated || !_awake || _blockObject is null || !_blockObject.IsFinished)
            {
                return;
            }

            try
            {
                _activated = true;
                SyncEntityIdentity();
                if (!_saveLoaded)
                {
                    _record.EntityId = _saveEntityId;
                }

                ClaimLoadedPlots();
                _registry.RegisterSource(this);
                EnableComponent();
                _dayTrigger?.Resume();
                StartScan();

                // Chapter 14.2: register the vanilla floating-status pipeline so P0/P1 warnings can
                // appear above the building. Icons are optional: a registration failure only logs
                // and leaves the panel/simulation untouched.
                _worldIcons.Register(_entityId, this);

                // BUILD-05: start hiding the models of the crops on this farm's managed plots
                // (renderer-only; growth/yield/harvest stay vanilla).
                StartCropVisibility();

                _log.Write(HdbfLogCategory.Info, LogKeyEntity,
                    "box farm active, entityId=" + _entityId + ", tier=" + _profile.Tier
                    + ", blueprint=" + BlueprintId());
            }
            catch (Exception exception)
            {
                _activated = false;
                DeactivateWorldIcons();
                _log.Error(HdbfLogCategory.Error, LogKeyEntity, "box farm activation failed", exception,
                    _entityId.ToString());
            }
        }

        private void Deactivate()
        {
            if (!_activated)
            {
                return;
            }

            _activated = false;
            _scanner?.Cancel();
            InvalidateFarmPlan();

            try
            {
                _dayTrigger?.Pause();
            }
            catch (Exception exception)
            {
                _log.Error(HdbfLogCategory.Warn, LogKeyEntity, "day trigger pause failed", exception,
                    _entityId.ToString());
            }

            _registry.UnregisterSource(_entityId);
            DeactivateWorldIcons();

            // BUILD-05: the farm is no longer managing anything, so every crop model it hid must
            // become visible again (vanilla visuals restored before the component disables).
            RestoreAllCropVisuals("deactivate");

            DisableComponent();
        }

        /// <summary>
        /// Deactivates the farm's warning-icon toggles when it leaves the finished state. The
        /// toggles stay registered for a later re-activation; <see cref="ForgetWorldIcons"/> drops
        /// them entirely on demolition. Isolated because icon cleanup must never block the farm
        /// from shutting down (REQ-SAFE-01).
        /// </summary>
        private void DeactivateWorldIcons()
        {
            try
            {
                _worldIcons.Deactivate(_entityId);
            }
            catch (Exception exception)
            {
                _log.Error(HdbfLogCategory.Warn, LogKeyEntity,
                    "world warning icon deactivation failed", exception, _entityId.ToString());
            }
        }

        /// <summary>Drops all icon state for a demolished/removed farm.</summary>
        private void ForgetWorldIcons()
        {
            try
            {
                _worldIcons.Forget(_entityId);
            }
            catch (Exception exception)
            {
                _log.Error(HdbfLogCategory.Warn, LogKeyEntity,
                    "world warning icon cleanup failed during deletion", exception, _entityId.ToString());
            }
        }

        private void ClaimLoadedPlots()
        {
            if (_record.PlotIds.Length == 0)
            {
                return;
            }

            var granted = _registry.ApplyPlotSelection(_entityId, _record.PlotIds);
            if (granted.Length != _record.PlotIds.Length)
            {
                ApplyPlotArrays(granted);
                _log.Write(HdbfLogCategory.Warn, HdbfLogKeys.SaveDegraded,
                    "some saved plots were already owned by another farm and were dropped, entityId="
                    + _entityId);
            }
        }

        private void ResolveTierProfile()
        {
            var blueprintId = BlueprintId();
            var fallbackTier = Enum.TryParse(_spec.Tier, true, out HdbfTier parsedTier)
                ? parsedTier
                : HdbfTier.Basic;

            if (!_configProvider.TryGetTierProfile(blueprintId, fallbackTier, out var profile))
            {
                _log.Write(HdbfLogCategory.Info, HdbfLogKeys.ConfigInvalid,
                    "unknown box farm blueprint '" + (blueprintId ?? "<null>")
                    + "'; falling back to tier " + fallbackTier);
            }

            _profile = profile;

            if (Math.Abs(_spec.PlotCapacity - profile.PlotCapacity) > 0.5f
                || Math.Abs(_spec.WaterPerPlotPerDay - profile.WaterPerPlotPerDay) > 0.0001f)
            {
                // Single source of truth: the config-resolved tier table wins, so a blueprint and
                // a config override can never fight over the same balance number (REQ-IMPL-03).
                _log.Write(HdbfLogCategory.Warn, HdbfLogKeys.ConfigInvalid,
                    "box farm blueprint values differ from the resolved tier table; the tier table wins, blueprint="
                    + (blueprintId ?? "<null>"));
            }
        }

        private string? BlueprintId()
        {
            var blueprint = _spec.Blueprint;
            return blueprint is not null ? blueprint.Name : null;
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
        /// one entity for the whole save; only used as an opaque record field (chapter 12.2).
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

