// ============================================================================
// HDBF v2 | Layer: UI/Panels | Owner: V2-UI-01
// Purpose: Player-action callbacks of the indoor panel (mode radio group, crop dropdown, water
//          refill, maintenance). Every rejection shows a specific localized reason
//          (REQ-V2-UI-02) and re-reads the authoritative snapshot; no optimistic UI state is kept.
// Requirements: REQ-V2-UI-01/02, REQ-V2-SAFE-01, REQ-V2-WATER-04
// ============================================================================
using System;
using HDBF.Core;
using HDBF.Core.Indoor;
using HDBF.Core.Logging;
using HDBF.UI.Localization;

namespace HDBF.UI.Panels
{
    public sealed partial class HdbfFarmPanelFragment
    {
        /// <summary>Vanilla radio-group callback: index 0/1/2 maps to normal/dense/fallow.</summary>
        private void OnModeSelected(object? sender, int index)
        {
            if (index < (int)IndoorMode.Normal || index > (int)IndoorMode.Fallow)
            {
                return;
            }

            var mode = (IndoorMode)index;
            if (_lastData is not null && _lastData.Mode == mode)
            {
                // Clicking the active radio button is a no-op (the command would reject it).
                return;
            }

            OnModeClicked(mode);
        }

        /// <summary>
        /// Mode switch. UI keeps no optimistic state: the radio group is re-rendered from the next
        /// snapshot, and a rejection renders the gameplay failure key in the action line.
        /// </summary>
        private void OnModeClicked(IndoorMode mode)
        {
            if (_entityId == Guid.Empty)
            {
                return;
            }

            try
            {
                if (_command.TrySetMode(_entityId, ToLegacyMode(mode), out var failureKey))
                {
                    SetActionMessage(_loc.T(HdbfUiLocKeys.ActionModeChanged));
                }
                else
                {
                    SetActionMessage(HdbfIndoorPanelFormat.FormatCommandFailure(_loc.T, failureKey));
                    LogRejected("set mode " + mode, failureKey);
                }
            }
            catch (Exception exception)
            {
                SetActionMessage(_loc.T(HdbfUiLocKeys.ErrorActionFailed));
                LogCommandException("set mode " + mode, exception);
            }

            Refresh(force: true);
        }

        /// <summary>Crop dropdown callback: empty id selects rotation, a crop id selects fixed crop.</summary>
        private void OnCropSelected(int index)
        {
            if (_entityId == Guid.Empty)
            {
                return;
            }

            var cropId = _cropProvider.IdAt(index);
            var policy = string.IsNullOrEmpty(cropId) ? CropPolicy.Rotation : CropPolicy.Fixed;
            try
            {
                if (_indoor.TrySetCropPolicy(_entityId, policy, cropId, out var failureKey))
                {
                    SetActionMessage(_loc.T(HdbfUiLocKeys.ActionCropPolicyChanged));
                }
                else
                {
                    SetActionMessage(HdbfIndoorPanelFormat.FormatCommandFailure(_loc.T, failureKey));
                    LogRejected("set crop policy " + policy + " (" + cropId + ")", failureKey);
                }
            }
            catch (Exception exception)
            {
                SetActionMessage(_loc.T(HdbfUiLocKeys.ErrorActionFailed));
                LogCommandException("set crop policy", exception);
            }

            Refresh(force: true);
            ScheduleCropValueSync();
        }

        /// <summary>
        /// Manual water refill request: gameplay validates workers, entrance, source and budget,
        /// then the next daily tick performs the trip (REQ-V2-WATER-04). Every rejection renders
        /// its specific reason instead of a silent no-op.
        /// </summary>
        private void OnRefillClicked()
        {
            if (_entityId == Guid.Empty)
            {
                return;
            }

            try
            {
                if (_indoor.TryRequestManualRefill(_entityId, out var failureKey))
                {
                    SetActionMessage(_loc.T(HdbfUiLocKeys.ActionRefillRequested));
                }
                else
                {
                    SetActionMessage(HdbfIndoorPanelFormat.FormatCommandFailure(_loc.T, failureKey));
                    LogRejected("request water refill", failureKey);
                }
            }
            catch (Exception exception)
            {
                SetActionMessage(_loc.T(HdbfUiLocKeys.ErrorActionFailed));
                LogCommandException("request water refill", exception);
            }

            Refresh(force: true);
        }

        /// <summary>
        /// Material maintenance. Gameplay performs the all-or-nothing material transaction; a
        /// shortage shows the returned reason (the missing material list is also rendered above).
        /// </summary>
        private void OnMaintenanceClicked()
        {
            if (_entityId == Guid.Empty)
            {
                return;
            }

            try
            {
                if (_command.TryPerformMaintenance(_entityId, out var failureKey))
                {
                    SetActionMessage(_loc.T(HdbfUiLocKeys.ActionMaintenanceDone));
                }
                else
                {
                    SetActionMessage(HdbfIndoorPanelFormat.FormatCommandFailure(_loc.T, failureKey));
                    LogRejected("perform maintenance", failureKey);
                }
            }
            catch (Exception exception)
            {
                SetActionMessage(_loc.T(HdbfUiLocKeys.ErrorActionFailed));
                LogCommandException("perform maintenance", exception);
            }

            Refresh(force: true);
        }

        private static HdbfMode ToLegacyMode(IndoorMode mode)
        {
            switch (mode)
            {
                case IndoorMode.Dense: return HdbfMode.Dense;
                case IndoorMode.Fallow: return HdbfMode.Fallow;
                default: return HdbfMode.Normal;
            }
        }

        private void LogRejected(string operation, string? failureKey)
        {
            _log.Write(HdbfLogCategory.Warn, LogKeyCommandRejected,
                operation + " rejected (" + (failureKey ?? "(no key)") + "), entityId=" + _entityId);
        }

        private void LogCommandException(string operation, Exception exception)
        {
            _log.Error(HdbfLogCategory.Error, LogKeyCommandFailed,
                operation + " threw; the panel ignored it", exception, _entityId.ToString());
        }
    }
}
