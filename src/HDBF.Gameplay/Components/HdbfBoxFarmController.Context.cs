// ============================================================================
// HDBF v2 | Layer: Gameplay/Components | Owner: V2-GAMEPLAY-01 / V2-GAMEPLAY-02
// Purpose: Per-day game-object context collection for the indoor tick: workers, day,
//          entrance, pipe water, adapter health, the mechanical power refresh and the
//          port availability refresh. Everything is a once-per-day read, never per frame.
// Requirements: REQ-V2-LABOR-03, REQ-V2-WATER-02, REQ-V2-BLD-02, REQ-V2-BAL-03,
//               REQ-V2-COMPAT-02, REQ-V2-SAFE-01
// ============================================================================
using System;
using HDBF.Core;
using HDBF.Core.Indoor;
using HDBF.Core.Logging;

namespace HDBF.Gameplay.Components
{
    public partial class HdbfBoxFarmController
    {
        private int ReadActiveWorkers()
        {
            try
            {
                return _workplace is null ? 0 : Math.Max(0, _workplace.NumberOfAssignedWorkers);
            }
            catch (Exception exception)
            {
                _log.Error(HdbfLogCategory.Warn, IndoorLogKeys.Day,
                    "worker count failed; treating the farm as unstaffed", exception, _entityId.ToString());
                return 0;
            }
        }

        private float ReadCurrentDay()
        {
            try
            {
                var day = _dayNightCycle.PartialDayNumber;
                return float.IsNaN(day) || float.IsInfinity(day) ? 0f : day;
            }
            catch (Exception exception)
            {
                _log.Error(HdbfLogCategory.Warn, IndoorLogKeys.Day,
                    "game day read failed; using day 0", exception, _entityId.ToString());
                return 0f;
            }
        }

        private bool ReadEntranceBlocked()
        {
            try
            {
                if (_blockObject is null || !_blockObject.HasEntrance || _blockObject.IsUnfinished)
                {
                    return false;
                }

                var entrance = _blockObject.PositionedEntrance;
                if (entrance is null)
                {
                    return false;
                }

                // The game's own definition (Timberborn.Buildings.BuildingBlockedAccessible.IsBlocked):
                // the doorway cell and its doorstep cell must be connected in the nav mesh. The old
                // "a Path block object must sit on the doorstep" test flagged every farm on a raised
                // floor - e.g. a box farm stacked on another farm's flat roof, whose doorstep is a
                // roof cell - as blocked forever, so planting never ran (2026-10-29 player report).
                return !_navMesh.AreConnected(entrance.Coordinates, entrance.DoorstepCoordinates);
            }
            catch (Exception exception)
            {
                _log.Error(HdbfLogCategory.Warn, IndoorLogKeys.Day,
                    "entrance probe failed; treating the entrance as open", exception, _entityId.ToString());
                return false;
            }
        }

        private IWaterSource? ReadPipeWater(out bool hasPipeWater)
        {
            hasPipeWater = false;
            // L-18: features.pipeConnection=false forces the port off at activation and must also stop
            // the automatic pipe draw here, so the farm falls back to manual/hauled water only.
            if (_record is null || !_record.PipePort || !SafeFeatures().PipeConnection)
            {
                return null;
            }

            try
            {
                // Player report 2026-11-07: tell the adapter host which footprint is asking before
                // any pipe query, so a pipe attached to the farm shell can supply this farm.
                PublishWaterAnchor();
                if (!_adapterHost.IsPipeConnected)
                {
                    // Degrade, never block: the farm keeps running and manual refill stays possible.
                    _log.Write(HdbfLogCategory.Compat, IndoorLogKeys.PipeUnavailable,
                        "pipe port has no usable water connection; manual refill stays possible, entityId="
                        + _entityId);
                    return null;
                }

                var source = _adapterHost.PipeWaterSource;
                if (source is null)
                {
                    return null;
                }

                hasPipeWater = true;
                return source;
            }
            catch (Exception exception)
            {
                _log.Error(HdbfLogCategory.Warn, IndoorLogKeys.PipeUnavailable,
                    "pipe water source query failed; no pipe water this day", exception, _entityId.ToString());
                return null;
            }
        }

        private string ReadAdapterState()
        {
            switch (ReadAdapterHealth())
            {
                case HdbfAdapterHealth.Ok:
                    return IndoorSaveLimits.AdapterStateOk;
                case HdbfAdapterHealth.Degraded:
                    return IndoorSaveLimits.AdapterStateDegraded;
                case HdbfAdapterHealth.Disabled:
                    return IndoorSaveLimits.AdapterStateDisabled;
                case HdbfAdapterHealth.Failed:
                    return IndoorSaveLimits.AdapterStateFailed;
                default:
                    return IndoorSaveLimits.AdapterStateNone;
            }
        }

        private HdbfAdapterHealth ReadAdapterHealth()
        {
            try
            {
                return _adapterHost.PipeHealth;
            }
            catch (Exception)
            {
                return HdbfAdapterHealth.Failed;
            }
        }

        /// <summary>
        /// Daily power refresh (REQ-V2-BAL-03/section 19.8). The mechanical network is a live game
        /// object state, so the persisted <see cref="IndoorFarmRecord.PowerConnected"/> and
        /// <see cref="IndoorFarmRecord.PowerMode"/> are resolved once per game day, never per frame:
        /// * tier without a power contract -> NotRequired, disconnected;
        /// * power feature/config disabled or no mechanical node -> OffGrid (15 metal / 8 days);
        /// * wired grid -> Grid, connected = <c>MechanicalBuilding.ActiveAndPowered</c>; a wired but
        ///   unpowered grid is the "power cut" state (Core applies G_power = 0.60 and x2 upkeep).
        /// Returns true when a persisted field changed.
        /// </summary>
        private bool UpdatePower()
        {
            var record = _record;
            if (record is null)
            {
                return false;
            }

            PowerConnection nextMode;
            var nextConnected = false;
            if (!_profile.NeedsPower)
            {
                nextMode = PowerConnection.NotRequired;
            }
            else if (!IsPowerFeatureEnabled() || !TryReadMechanicalPower(out nextConnected))
            {
                nextMode = PowerConnection.OffGrid;
                nextConnected = false;
            }
            else
            {
                nextMode = PowerConnection.Grid;
            }

            if (record.PowerMode == nextMode && record.PowerConnected == nextConnected)
            {
                return false;
            }

            record.PowerMode = nextMode;
            record.PowerConnected = nextConnected;
            _log.Write(nextMode == PowerConnection.Grid && !nextConnected
                    ? HdbfLogCategory.Warn
                    : HdbfLogCategory.Info,
                nextMode == PowerConnection.Grid && !nextConnected
                    ? IndoorLogKeys.PowerCut
                    : IndoorLogKeys.Power,
                "indoor power state changed: mode=" + nextMode + ", connected=" + nextConnected
                + ", entityId=" + _entityId);
            return true;
        }

        private bool IsPowerFeatureEnabled()
        {
            try
            {
                return _configProvider.Config.Features.PowerConnection;
            }
            catch (Exception)
            {
                // A broken config degrades to the off-grid path instead of blocking the farm.
                return false;
            }
        }

        /// <summary>
        /// True when the top tier owns a mechanical node that is not deliberately detached, i.e. it
        /// can use the grid. <paramref name="connected"/> is the live powered state; a wired but
        /// unpowered node is a power cut, not off-grid.
        /// </summary>
        private bool TryReadMechanicalPower(out bool connected)
        {
            connected = false;
            if (_mechanicalBuilding is null || _mechanicalNode is null)
            {
                return false;
            }

            try
            {
                if (_mechanicalNode.IsDetached || _mechanicalNode.Graph is null)
                {
                    return false;
                }

                connected = _mechanicalBuilding.ActiveAndPowered;
                return true;
            }
            catch (Exception exception)
            {
                _log.Error(HdbfLogCategory.Warn, IndoorLogKeys.PowerCut,
                    "mechanical power state read failed; treating the tier as off-grid", exception,
                    _entityId.ToString());
                return false;
            }
        }

        /// <summary>
        /// Daily power refresh plus a read-only observation of the adapter state
        /// (REQ-V2-BAL-03, REQ-V2-COMPAT-02). L-06: the persisted
        /// <see cref="IndoorFarmRecord.PipePort"/> and
        /// <see cref="IndoorFarmRecord.LogisticsPort"/> flags are the player's enable choice (set
        /// through <c>TrySetPortConnected</c>) and are NEVER overwritten here. The old daily
        /// overwrite silently switched a port the player had turned off back on, so the panel
        /// reported it as usable again the next day. Port availability is now reported read-only
        /// through <see cref="IIndoorPipeProbe.ProbePipe"/> and the adapter health state, not by
        /// rewriting the persisted player choice. <see cref="IndoorFarmRecord.PowerPort"/> stays
        /// profile-derived (grid power is not a player toggle), and only a real adapter-state
        /// change emits the once-per-day <c>indoor.port</c> diagnostic line.
        /// </summary>
        /// <returns>True when a persisted port flag changed.</returns>
        private bool UpdatePorts()
        {
            var record = _record;
            if (record is null)
            {
                return false;
            }

            var nextPower = _profile.NeedsPower && _spec.HasPowerPort;
            var powerChanged = record.PowerPort != nextPower;
            if (powerChanged)
            {
                record.PowerPort = nextPower;
            }

            var adapterState = ReadAdapterState();
            var adapterChanged =
                !string.Equals(record.TimberPipesAdapter, adapterState, StringComparison.Ordinal);
            if (powerChanged || adapterChanged)
            {
                _log.Write(HdbfLogCategory.Compat, IndoorLogKeys.Port,
                    "indoor port state refreshed: adapter=" + adapterState + ", adapterChanged="
                    + adapterChanged + ", pipePort=" + (record.PipePort && _spec.HasPipePort)
                    + ", logisticsPort=" + (record.LogisticsPort && _spec.HasLogisticsPort)
                    + ", powerPort=" + nextPower + ", entityId=" + _entityId);
            }

            return powerChanged;
        }
    }
}
