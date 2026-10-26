// ============================================================================
// HDBF v2 | Layer: UI/Panels | Owner: V2-UI-01
// Purpose: Player-action callbacks of the indoor panel (mode switch, water refill, maintenance,
//          crop strategy). Every rejection shows a specific localized reason (REQ-V2-UI-02) and
//          re-reads the authoritative snapshot; no optimistic UI state is kept.
// Requirements: REQ-V2-UI-01/02, REQ-V2-SAFE-01, REQ-V2-WATER-04
// ============================================================================
using System;
using HDBF.Core;
using HDBF.Core.Abstractions;
using HDBF.Core.Indoor;
using HDBF.Core.Logging;
using HDBF.UI.Localization;

namespace HDBF.UI.Panels
{
    public sealed partial class HdbfFarmPanelFragment
    {
        /// <summary>
        /// Mode switch. UI keeps no optimistic state: the mode buttons are re-rendered from the
        /// next snapshot, and a rejection renders the gameplay failure key.
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

        /// <summary>
        /// Material maintenance. Gameplay performs the all-or-nothing material transaction; a
        /// shortage shows the returned reason (the missing material and count are also listed in
        /// the maintenance block while the flag is set).
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
        /// Crop strategy changed through the dropdown: an empty id selects automatic rotation, a
        /// crop id selects the fixed-crop policy. Invalid crops are rejected by gameplay with a
        /// specific reason; the dropdown is re-synced from the next snapshot.
        /// </summary>
        private void OnCropChoiceChanged()
        {
            if (_suppressCropChange || _cropDropdown is null || _entityId == Guid.Empty)
            {
                return;
            }

            var index = _cropDropdown.index;
            if (index < 0 || index >= _cropChoiceIds.Count)
            {
                return;
            }

            var cropId = _cropChoiceIds[index];
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
