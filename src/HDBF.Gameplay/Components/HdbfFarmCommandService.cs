using System;
using HDBF.Core;
using HDBF.Core.Abstractions;
using HDBF.Core.Balance;
using HDBF.Core.Logging;
using Timberborn.BaseComponentSystem;
using Timberborn.GameDistricts;
using Timberborn.Goods;
using Timberborn.InventorySystem;
using UnityEngine;

namespace HDBF.Gameplay.Components
{
    /// <summary>
    /// Gameplay implementation of the write-side panel contract
    /// (<see cref="IBoxFarmCommand"/>, chapter 14.3). Kept out of the controller so the
    /// controller stays a per-entity runtime and this service stays the session singleton that
    /// resolves a farm by its runtime <see cref="EntityComponent.EntityId"/> and performs the
    /// material transaction against the settlement's inventories.
    ///
    /// Failure policy (chapter 10): every entry point returns false with a localization key
    /// instead of throwing; the UI renders the key and never sees an exception.
    /// </summary>
    public sealed class HdbfFarmCommandService : IBoxFarmCommand
    {
        private const string LogKeyCommand = "gameplay.command";

        private readonly HdbfFarmQueryService _farms;
        private readonly DistrictCenterRegistry _districtCenters;
        private readonly IHdbfGameConfigProvider _config;
        private readonly IHdbfLog _log;
        private readonly HdbfPlotHighlightService _highlights;

        public HdbfFarmCommandService(HdbfFarmQueryService farms, DistrictCenterRegistry districtCenters,
            IHdbfGameConfigProvider config, IHdbfLog log, HdbfPlotHighlightService highlights)
        {
            _farms = farms ?? throw new ArgumentNullException(nameof(farms));
            _districtCenters = districtCenters ?? throw new ArgumentNullException(nameof(districtCenters));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _highlights = highlights ?? throw new ArgumentNullException(nameof(highlights));
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

            if (mode == HdbfMode.Dense && !_config.Config.Features.DensePlanting)
            {
                failureKey = HdbfCommandFailures.DenseDisabled;
                return false;
            }

            try
            {
                if (farm.TrySetMode(mode))
                {
                    return true;
                }

                // The controller rejects an unchanged mode, an out-of-range enum and the
                // one-day anti-flicker cooldown; the first two are UI bugs, the cooldown is the
                // only expected rejection (REQ-BAL-20).
                failureKey = HdbfCommandFailures.ModeCooldown;
                return false;
            }
            catch (Exception exception)
            {
                _log.Error(HdbfLogCategory.Error, LogKeyCommand,
                    "set mode failed; no state changed", exception, entityId.ToString());
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
                var profile = farm.TierProfile;
                if (profile.MaintenanceKind == HdbfMaintenanceKind.IrrigationOnly)
                {
                    failureKey = HdbfCommandFailures.PortNotAvailable;
                    return false;
                }

                if (!TryWithdrawMaintenanceMaterials(farm, profile, out failureKey))
                {
                    return false;
                }

                if (farm.TryPerformMaintenance())
                {
                    _log.Write(HdbfLogCategory.Info, LogKeyCommand,
                        "maintenance performed, entityId=" + entityId);
                    return true;
                }

                // The controller only refuses when it has no material cycle (on-grid top tier,
                // maintenance feature disabled). That is not an error the player can fix.
                failureKey = HdbfCommandFailures.Unknown;
                return false;
            }
            catch (Exception exception)
            {
                _log.Error(HdbfLogCategory.Error, LogKeyCommand,
                    "maintenance failed; no state changed", exception, entityId.ToString());
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
                // The power port reflects the physical mechanical network; it is never toggled
                // from the panel (chapter 8.3).
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
                    "port switch failed; no state changed", exception, entityId.ToString());
                failureKey = HdbfCommandFailures.Unknown;
                return false;
            }
        }

        /// <inheritdoc />
        public bool TrySetPlotHighlight(Guid entityId, bool active, out string? failureKey)
        {
            failureKey = null;
            if (!TryGetController(entityId, out _))
            {
                failureKey = HdbfCommandFailures.NotABoxFarm;
                return false;
            }

            try
            {
                if (active)
                {
                    if (_highlights.Activate(entityId))
                    {
                        return true;
                    }

                    failureKey = HdbfCommandFailures.NotABoxFarm;
                    return false;
                }

                // Closing may target a farm whose source is already gone (demolished building):
                // the session is closed regardless, which is always the safe outcome.
                _highlights.Deactivate();
                return true;
            }
            catch (Exception exception)
            {
                _log.Error(HdbfLogCategory.Error, LogKeyCommand,
                    "plot highlight toggle failed", exception, entityId.ToString());
                failureKey = HdbfCommandFailures.Unknown;
                return false;
            }
        }

        /// <inheritdoc />
        public bool TryExcludePlot(Guid entityId, int plotId, out string? failureKey)
        {
            failureKey = null;
            if (!TryGetController(entityId, out var farm))
            {
                failureKey = HdbfCommandFailures.NotABoxFarm;
                return false;
            }

            try
            {
                if (!farm.IsRegisteredPlot(plotId))
                {
                    failureKey = HdbfCommandFailures.PlotNotRegistered;
                    return false;
                }

                if (!farm.TryExcludePlot(plotId))
                {
                    failureKey = HdbfCommandFailures.PlotMinimum;
                    return false;
                }

                return true;
            }
            catch (Exception exception)
            {
                _log.Error(HdbfLogCategory.Error, LogKeyCommand,
                    "plot exclusion failed", exception, entityId.ToString());
                failureKey = HdbfCommandFailures.Unknown;
                return false;
            }
        }

        /// <inheritdoc />
        public bool TryIncludePlot(Guid entityId, int plotId, out string? failureKey)
        {
            failureKey = null;
            if (!TryGetController(entityId, out var farm))
            {
                failureKey = HdbfCommandFailures.NotABoxFarm;
                return false;
            }

            try
            {
                if (!farm.TryIncludePlot(plotId))
                {
                    failureKey = HdbfCommandFailures.PlotNotRegistered;
                    return false;
                }

                return true;
            }
            catch (Exception exception)
            {
                _log.Error(HdbfLogCategory.Error, LogKeyCommand,
                    "plot restore failed", exception, entityId.ToString());
                failureKey = HdbfCommandFailures.Unknown;
                return false;
            }
        }

        private bool TryGetController(Guid entityId, out HdbfBoxFarmController farm)
        {
            farm = null!;
            if (entityId == Guid.Empty || _farms is null)
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

        /// <summary>
        /// Withdraws the chapter 6.5 upkeep material from the closest stockpile in the farm's own
        /// district (falling back to any finished district) before the timer is reset. The
        /// transaction is all-or-nothing: when no inventory has the full amount, nothing is
        /// consumed and the caller reports a material shortage (REQ-BAL-11/12).
        /// </summary>
        private bool TryWithdrawMaintenanceMaterials(HdbfBoxFarmController farm, TierProfile profile,
            out string? failureKey)
        {
            failureKey = null;
            var goodId = MaintenanceGoodId(profile, farm);
            var amount = MaintenanceMaterialAmount(profile, farm);
            if (goodId is null || amount <= 0)
            {
                // On-grid top tier or material-free tier: nothing material to consume.
                return true;
            }

            var goodAmount = new GoodAmount(goodId, amount);
            var position = farm.Transform.position;
            Inventory? best = null;
            var bestDistance = float.MaxValue;

            // Timberborn.Common.ReadOnlyList<T> is a readonly struct, never null: an empty list
            // is the "no active district" state.
            var centers = _districtCenters.FinishedDistrictCenters;
            if (centers.Count == 0)
            {
                failureKey = HdbfCommandFailures.MissingMaterials;
                _log.Write(HdbfLogCategory.Warn, LogKeyCommand,
                    "maintenance needs " + amount + " " + goodId + " but no district registry is available");
                return false;
            }

            for (var i = 0; i < centers.Count; i++)
            {
                var center = centers[i];
                if (center is null)
                {
                    continue;
                }

                var registry = center.GetComponent<DistrictInventoryRegistry>();
                var inventories = registry?.ActiveInventoriesWithStock(goodId);
                if (inventories is null)
                {
                    continue;
                }

                foreach (var inventory in inventories)
                {
                    if (inventory is null || !inventory.Takes(goodId)
                        || !inventory.HasUnreservedStock(goodAmount))
                    {
                        continue;
                    }

                    var distance = (inventory.Transform.position - position).sqrMagnitude;
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = inventory;
                    }
                }
            }

            if (best is null)
            {
                failureKey = HdbfCommandFailures.MissingMaterials;
                _log.Write(HdbfLogCategory.Warn, LogKeyCommand,
                    "maintenance needs " + amount + " " + goodId + " but no stockpile holds that unreserved amount");
                return false;
            }

            best.TakeExisting(goodAmount);
            return true;
        }

        private static string? MaintenanceGoodId(TierProfile profile, HdbfBoxFarmController farm)
        {
            switch (profile.MaintenanceKind)
            {
                case HdbfMaintenanceKind.Plank:
                    return "Plank";
                case HdbfMaintenanceKind.Metal:
                    return "MetalBlock";
                case HdbfMaintenanceKind.PowerOrOffGridMetal:
                    // On-grid top tier is maintained by the mechanical network (chapter 6.5).
                    return farm.PowerConnected ? null : "MetalBlock";
                default:
                    return null;
            }
        }

        private static int MaintenanceMaterialAmount(TierProfile profile, HdbfBoxFarmController farm)
        {
            switch (profile.MaintenanceKind)
            {
                case HdbfMaintenanceKind.Plank:
                case HdbfMaintenanceKind.Metal:
                    return profile.MaintenanceMaterialCount;
                case HdbfMaintenanceKind.PowerOrOffGridMetal:
                    return farm.PowerConnected ? 0 : profile.OffGridMaintenanceMaterial;
                default:
                    return 0;
            }
        }
    }
}

