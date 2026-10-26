// ============================================================================
// HDBF v2 | Layer: Gameplay/Components | Owner: V2-GAMEPLAY-01 / V2-GAMEPLAY-02
// Purpose: Write-side IBoxFarmCommand implementation for v2 indoor farms. V2-GAMEPLAY-02 adds
//          the completed maintenance transaction, the manual-refill request and the crop-policy
//          command, all with specific failure keys and all-or-nothing semantics.
// Requirements: REQ-V2-UI-02, REQ-V2-BAL-02, REQ-V2-WATER-04, REQ-V2-SAFE-01,
//               REQ-V2-CORE-03, REQ-V2-LABOR-01
// ============================================================================
using System;
using HDBF.Core;
using HDBF.Core.Abstractions;
using HDBF.Core.Indoor;
using HDBF.Core.Logging;
using Timberborn.GameDistricts;

namespace HDBF.Gameplay.Components
{
    /// <summary>
    /// Session singleton implementing the write-side panel contract. Every entry point validates
    /// and changes all-or-nothing, returns a specific failure key instead of throwing, and logs
    /// under <c>ui.command-failed</c> (or the subsystem key) where the UI can find it.
    /// </summary>
    public sealed class HdbfFarmCommandService : IBoxFarmCommand
    {
        private const string LogKeyCommand = "ui.command-failed";

        private readonly HdbfFarmQueryService _farms;
        private readonly HdbfMaintenanceTransaction _maintenance;
        private readonly IHdbfLog _log;

        /// <summary>Creates the command service from the session singletons.</summary>
        /// <param name="farms">Live farm source registry (query singleton).</param>
        /// <param name="districtCenters">Finished district centers (maintenance material source).</param>
        /// <param name="log">Throttled mod log.</param>
        public HdbfFarmCommandService(HdbfFarmQueryService farms, DistrictCenterRegistry districtCenters,
            IHdbfLog log)
        {
            _farms = farms ?? throw new ArgumentNullException(nameof(farms));
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _maintenance = new HdbfMaintenanceTransaction(districtCenters, log);
        }

        /// <inheritdoc />
        public bool TrySetMode(Guid entityId, HdbfMode mode, out string? failureKey)
        {
            failureKey = null;
            if (!TryGetController(entityId, out var farm))
            {
                failureKey = HdbfCommandFailures.NotABoxFarm;
                return false;
            }

            try
            {
                if (farm.TrySetMode(mode))
                {
                    return true;
                }

                // The controller rejects a cooldown, an invalid mode and a disabled dense feature.
                failureKey = mode == HdbfMode.Dense
                    ? HdbfCommandFailures.DenseDisabled
                    : HdbfCommandFailures.ModeCooldown;
                return false;
            }
            catch (Exception exception)
            {
                _log.Error(HdbfLogCategory.Error, LogKeyCommand,
                    "indoor set mode failed; no state changed", exception, entityId.ToString());
                failureKey = HdbfCommandFailures.Unknown;
                return false;
            }
        }

        /// <inheritdoc />
        public bool TryPerformMaintenance(Guid entityId, out string? failureKey)
        {
            failureKey = null;
            if (!TryGetController(entityId, out var farm))
            {
                failureKey = HdbfCommandFailures.NotABoxFarm;
                return false;
            }

            try
            {
                // Plan first: the bill and the new deadline are validated before any item moves,
                // so a rejected command can never eat the upkeep material (all-or-nothing).
                if (!farm.TryGetMaintenancePlan(out var itemId, out var amount, out var cycleDays))
                {
                    failureKey = HdbfIndoorCommandFailures.MaintenanceUnavailable;
                    return false;
                }

                if (!_maintenance.TryConsumeNearest(farm, entityId, itemId, amount, out failureKey))
                {
                    return false;
                }

                if (!farm.TryApplyMaintenance(cycleDays, out failureKey))
                {
                    return false;
                }

                _log.Write(HdbfLogCategory.Info, IndoorLogKeys.Maintenance,
                    "indoor maintenance performed; entityId=" + entityId + ", item=" + (itemId ?? "<none>")
                    + ", amount=" + amount + ", cycleDays=" + cycleDays);
                return true;
            }
            catch (Exception exception)
            {
                _log.Error(HdbfLogCategory.Error, LogKeyCommand,
                    "indoor maintenance failed; no state changed", exception, entityId.ToString());
                failureKey = HdbfCommandFailures.Unknown;
                return false;
            }
        }

        /// <summary>
        /// Queues one manual water refill trip: validates workers, entrance, remaining daily action
        /// budget and a real 12-cell water source, then lets the next daily tick perform the draw and
        /// spend the 2 farm actions (REQ-V2-WATER-04). A panel-only command: it is resolved by the
        /// UI command bridge by name, not part of the Core <see cref="IBoxFarmCommand"/> surface.
        /// </summary>
        /// <param name="entityId">Runtime entity id of the farm.</param>
        /// <param name="failureKey">Specific failure key when false.</param>
        /// <returns>True when the refill request is queued.</returns>
        public bool TryRequestManualRefill(Guid entityId, out string? failureKey)
        {
            failureKey = null;
            if (!TryGetController(entityId, out var farm))
            {
                failureKey = HdbfCommandFailures.NotABoxFarm;
                return false;
            }

            try
            {
                if (farm.TryRequestManualRefill(out failureKey))
                {
                    return true;
                }

                failureKey ??= HdbfIndoorCommandFailures.RefillUnavailable;
                return false;
            }
            catch (Exception exception)
            {
                _log.Error(HdbfLogCategory.Error, LogKeyCommand,
                    "indoor manual refill request failed; no state changed", exception, entityId.ToString());
                failureKey = HdbfIndoorCommandFailures.RefillUnavailable;
                return false;
            }
        }

        /// <summary>
        /// Applies the crop policy (auto rotation or one fixed faction crop). Invalid crops are
        /// rejected with <see cref="HdbfIndoorCommandFailures.CropNotAvailable"/> and change nothing
        /// (REQ-V2-EXT-01). A panel-only command resolved by the UI command bridge by name.
        /// </summary>
        /// <param name="entityId">Runtime entity id of the farm.</param>
        /// <param name="policy">Rotation or fixed.</param>
        /// <param name="fixedCropId">Crop id for a fixed policy; ignored otherwise.</param>
        /// <param name="failureKey">Specific failure key when false.</param>
        /// <returns>True when gameplay applied the policy.</returns>
        public bool TrySetCropPolicy(Guid entityId, CropPolicy policy, string fixedCropId,
            out string? failureKey)
        {
            failureKey = null;
            if (!TryGetController(entityId, out var farm))
            {
                failureKey = HdbfCommandFailures.NotABoxFarm;
                return false;
            }

            try
            {
                return farm.TrySetCropPolicy(policy, fixedCropId, out failureKey);
            }
            catch (Exception exception)
            {
                _log.Error(HdbfLogCategory.Error, LogKeyCommand,
                    "indoor crop policy command failed; no state changed", exception, entityId.ToString());
                failureKey = HdbfCommandFailures.Unknown;
                return false;
            }
        }

        /// <inheritdoc />
        public bool TrySetPort(Guid entityId, HdbfPortKind port, bool connected, out string? failureKey)
        {
            failureKey = null;
            if (!TryGetController(entityId, out var farm))
            {
                failureKey = HdbfCommandFailures.NotABoxFarm;
                return false;
            }

            if (port == HdbfPortKind.Power)
            {
                failureKey = HdbfCommandFailures.PortManagedByNetwork;
                return false;
            }

            try
            {
                if (!farm.TrySetPortConnected(port, connected))
                {
                    failureKey = HdbfCommandFailures.PortNotAvailable;
                    return false;
                }

                return true;
            }
            catch (Exception exception)
            {
                _log.Error(HdbfLogCategory.Error, LogKeyCommand,
                    "indoor port switch failed; no state changed", exception, entityId.ToString());
                failureKey = HdbfCommandFailures.Unknown;
                return false;
            }
        }

        private bool TryGetController(Guid entityId, out HdbfBoxFarmController farm)
        {
            farm = null!;
            if (entityId == Guid.Empty)
            {
                return false;
            }

            if (!_farms.TryGetSource(entityId, out var source) || source is not HdbfBoxFarmController controller)
            {
                return false;
            }

            farm = controller;
            return true;
        }
    }
}
