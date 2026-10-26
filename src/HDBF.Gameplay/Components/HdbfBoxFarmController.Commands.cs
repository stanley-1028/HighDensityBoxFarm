// ============================================================================
// HDBF v2 | Layer: Gameplay/Components | Owner: V2-GAMEPLAY-01 / V2-GAMEPLAY-02
// Purpose: Player/UI commands on one indoor farm: mode (with the one-day transition state),
//          ports, crop policy and the maintenance material plan/apply split. The manual-refill
//          command lives in the .ManualRefill partial; every change is validated and dirty-marked.
// Requirements: REQ-V2-LABOR-03, REQ-V2-WATER-04, REQ-V2-UI-02, REQ-V2-BAL-02/03,
//               REQ-V2-SAFE-01, REQ-V2-EXT-01
// ============================================================================
using System;
using System.Globalization;
using HDBF.Core;
using HDBF.Core.Abstractions;
using HDBF.Core.Balance;
using HDBF.Core.Indoor;
using HDBF.Core.Logging;

namespace HDBF.Gameplay.Components
{
    public partial class HdbfBoxFarmController
    {
        /// <summary>Resolved balance profile of the building (read-only; command service use).</summary>
        internal TierProfile TierProfile => _profile;

        /// <summary>True while the top tier is actually powered by the mechanical network.</summary>
        internal bool PowerConnected => _record is not null && _record.PowerConnected;

        /// <summary>True while the controller is finished and registered with the query service.</summary>
        internal bool IsActive => _activated;

        /// <summary>Current v2 operating mode.</summary>
        public IndoorMode Mode => _record?.Mode ?? IndoorMode.Normal;

        /// <summary>Current crop policy.</summary>
        public CropPolicy CropPolicy => _record?.CropPolicy ?? CropPolicy.Rotation;

        /// <summary>Fixed crop id when <see cref="CropPolicy"/> is Fixed; empty otherwise.</summary>
        public string FixedCropId => _record?.FixedCropId ?? string.Empty;

        /// <summary>Last rejected command reason of this farm, for the panel action line.</summary>
        public string? LastCommandFailureKey => _lastCommandFailureKey;

        /// <summary>
        /// Switches the operating mode (Normal/Dense/Fallow). The one-game-day cooldown and the
        /// dense-planting feature flag are the same v1 rules; the switch also starts the one-day
        /// transition in which harvests settle at the lower of the old/new mode yield
        /// (REQ-V2-BAL-03/section 19.8). The transition flag is transient and falls back to the
        /// current mode after a load (documented approximation, V2-GAMEPLAY-02).
        /// </summary>
        /// <param name="mode">Requested v1-compatible mode value from the panel.</param>
        /// <returns>True when the mode is now the requested one.</returns>
        public bool TrySetMode(HdbfMode mode)
        {
            if (_record is null || !Enum.IsDefined(typeof(HdbfMode), mode))
            {
                return false;
            }

            var next = mode switch
            {
                HdbfMode.Dense => IndoorMode.Dense,
                HdbfMode.Fallow => IndoorMode.Fallow,
                _ => IndoorMode.Normal,
            };
            if (next == _record.Mode)
            {
                return true;
            }

            if (next == IndoorMode.Dense)
            {
                try
                {
                    if (!_configProvider.Config.Features.DensePlanting)
                    {
                        return false;
                    }
                }
                catch (Exception)
                {
                    return false;
                }
            }

            var today = ReadCurrentDay();
            if (!float.IsNaN(_lastModeChangeDay) && today - _lastModeChangeDay < 1f)
            {
                return false;
            }

            _modeTransitionFrom = _record.Mode;
            _record.Mode = next;
            _lastModeChangeDay = today;
            _saveDirty = true;
            _log.Write(HdbfLogCategory.Info, IndoorLogKeys.Mode,
                "indoor mode changed to " + next + ", transition yields settle at the lower mode, entityId="
                + _entityId);
            return true;
        }

        /// <summary>
        /// Switches the pipe or logistics port. The power port mirrors the mechanical network and
        /// is never toggled here (chapter 8.3 semantics).
        /// </summary>
        /// <param name="port">Port kind from the v1 panel contract.</param>
        /// <param name="connected">Requested state.</param>
        /// <returns>True when the port exists and the state changed/was already current.</returns>
        public bool TrySetPortConnected(HdbfPortKind port, bool connected)
        {
            var record = _record;
            if (record is null)
            {
                return false;
            }

            switch (port)
            {
                case HdbfPortKind.Pipe:
                    if (!_spec.HasPipePort)
                    {
                        return false;
                    }

                    if (record.PipePort != connected)
                    {
                        record.PipePort = connected;
                        _saveDirty = true;
                        LogPortChange(port, connected);
                    }

                    return true;
                case HdbfPortKind.Logistics:
                    if (!_spec.HasLogisticsPort)
                    {
                        return false;
                    }

                    if (record.LogisticsPort != connected)
                    {
                        record.LogisticsPort = connected;
                        _saveDirty = true;
                        LogPortChange(port, connected);
                    }

                    return true;
                default:
                    _log.Write(HdbfLogCategory.Info, IndoorLogKeys.Port,
                        "power port state is owned by the mechanical network; request ignored");
                    return false;
            }
        }

        /// <summary>
        /// Sets the crop policy. A fixed crop must belong to the building faction's catalog
        /// (REQ-V2-EXT-01); an invalid choice changes nothing and returns a specific failure key.
        /// </summary>
        /// <param name="policy">Rotation or fixed.</param>
        /// <param name="fixedCropId">Required crop id for <see cref="CropPolicy.Fixed"/>.</param>
        /// <param name="failureKey">Specific failure key when false.</param>
        /// <returns>True when the policy was applied.</returns>
        public bool TrySetCropPolicy(CropPolicy policy, string? fixedCropId, out string? failureKey)
        {
            failureKey = null;
            var record = _record;
            if (record is null || !_activated)
            {
                failureKey = HdbfIndoorCommandFailures.NotActive;
                return false;
            }

            if (!Enum.IsDefined(typeof(CropPolicy), policy))
            {
                failureKey = HdbfIndoorCommandFailures.InvalidMode;
                return false;
            }

            if (policy == CropPolicy.Fixed
                && !FallbackCropCatalog.TryResolve(_cropCatalog, _faction, fixedCropId, out _))
            {
                failureKey = HdbfIndoorCommandFailures.CropNotAvailable;
                _log.Write(HdbfLogCategory.Warn, IndoorLogKeys.CropPolicy,
                    "fixed crop rejected (not in the faction catalog): '" + (fixedCropId ?? "<null>")
                    + "', entityId=" + _entityId);
                return false;
            }

            try
            {
                record.CropPolicy = policy;
                record.FixedCropId = policy == CropPolicy.Fixed ? fixedCropId ?? string.Empty : string.Empty;
                _saveDirty = true;
                _log.Write(HdbfLogCategory.Info, IndoorLogKeys.CropPolicy,
                    "indoor crop policy set to " + policy + ", entityId=" + _entityId);
                return true;
            }
            catch (Exception exception)
            {
                failureKey = HdbfCommandFailures.Unknown;
                _log.Error(HdbfLogCategory.Error, IndoorLogKeys.CommandFailed,
                    "setting the indoor crop policy failed; no state changed", exception, _entityId.ToString());
                return false;
            }
        }

        /// <summary>
        /// Builds the maintenance plan of this farm without moving any material: the upkeep good and
        /// count for the current power state plus the cycle to add to the due day. False for a tier
        /// with no material upkeep (basic tier, or a top tier maintained by its grid connection).
        /// </summary>
        /// <param name="itemId">Good to withdraw ("Plank"/"MetalBlock") or null when free.</param>
        /// <param name="amount">Item count of one cycle (0 when free).</param>
        /// <param name="cycleDays">Game days to add to the due day on success.</param>
        /// <returns>True when the farm has a material maintenance cycle to perform.</returns>
        internal bool TryGetMaintenancePlan(out string? itemId, out int amount, out float cycleDays)
        {
            itemId = null;
            amount = 0;
            cycleDays = 0f;
            if (_record is null || _profile.MaintenanceKind == HdbfMaintenanceKind.IrrigationOnly)
            {
                return false;
            }

            var offGrid = _profile.NeedsPower
                          && !(_record.PowerMode == PowerConnection.Grid && _record.PowerConnected);
            cycleDays = MaintenanceDefaults.CycleDaysFor(_profile, offGrid);
            if (cycleDays <= 0f)
            {
                return false;
            }

            // A grid-connected top tier is maintained by the network: no material bill at all, so
            // the command reports MaintenanceUnavailable instead of resetting anything for free.
            return IndoorMaintenanceMath.TryGetMaterialBill(_profile, _record, out itemId, out amount);
        }

        /// <summary>
        /// Applies a previously validated maintenance plan: <c>dueDay += cycle</c> (the saved
        /// deadline keeps its cadence instead of restarting from today) and clears the missed-cycle
        /// counter (section 19.8, REQ-V2-BAL-02).
        /// </summary>
        /// <param name="cycleDays">Cycle length from <see cref="TryGetMaintenancePlan"/>.</param>
        /// <param name="failureKey">Specific failure key when false.</param>
        /// <returns>True when the deadline was advanced.</returns>
        internal bool TryApplyMaintenance(float cycleDays, out string? failureKey)
        {
            failureKey = null;
            var record = _record;
            if (record is null || !(cycleDays > 0f) || float.IsNaN(cycleDays) || float.IsInfinity(cycleDays))
            {
                failureKey = HdbfIndoorCommandFailures.MaintenanceUnavailable;
                return false;
            }

            var today = ReadCurrentDay();
            var currentDue = MaintenanceDefaults.IsUsableDueDay(_profile, record.MaintenanceDueDay)
                ? record.MaintenanceDueDay
                : today;
            var nextDue = currentDue + cycleDays;
            if (float.IsNaN(nextDue) || float.IsInfinity(nextDue))
            {
                failureKey = HdbfIndoorCommandFailures.MaintenanceUnavailable;
                return false;
            }

            record.MaintenanceDueDay = nextDue;
            record.MaintenanceCyclesMissed = 0;
            _maintenanceInitialized = true;
            _saveDirty = true;
            _log.Write(HdbfLogCategory.Info, IndoorLogKeys.Maintenance,
                "indoor maintenance scheduled, dueDay=" + nextDue.ToString("F2", CultureInfo.InvariantCulture)
                + ", entityId=" + _entityId);
            return true;
        }

        private void LogPortChange(HdbfPortKind port, bool connected)
        {
            _log.Write(HdbfLogCategory.Info, IndoorLogKeys.Port,
                "indoor port " + port + " set to " + (connected ? "connected" : "disconnected")
                + ", entityId=" + _entityId);
        }
    }
}
