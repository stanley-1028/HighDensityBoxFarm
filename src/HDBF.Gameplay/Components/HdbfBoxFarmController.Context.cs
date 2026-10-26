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

                // A path at the doorstep makes the entrance usable; a wall/platform blocks it.
                return _blockService.GetPathObjectAt(entrance.DoorstepCoordinates) is null;
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
            if (_record is null || !_record.PipePort)
            {
                return null;
            }

            try
            {
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
        /// Daily port availability refresh (REQ-V2-COMPAT-02): the record flags describe what the
        /// player can actually use today. A missing/broken adapter hides the port (false) instead of
        /// showing a "missing mod" error, and the farm itself never stops because of it.
        /// </summary>
        /// <returns>True when a persisted port flag changed.</returns>
        private bool UpdatePorts()
        {
            var record = _record;
            if (record is null)
            {
                return false;
            }

            var health = ReadAdapterHealth();
            // Missing/disabled adapter = hide (greyed "not applicable"); a failed adapter keeps the
            // port visible as Fault so the panel can show a degraded state without an error text.
            var adapterHidden = health == HdbfAdapterHealth.Disabled || health == HdbfAdapterHealth.Unknown;
            var nextPipe = _spec.HasPipePort && !adapterHidden;
            var nextLogistics = _spec.HasLogisticsPort && !adapterHidden;
            var nextPower = _profile.NeedsPower && _spec.HasPowerPort;

            var changed = false;
            if (record.PipePort != nextPipe)
            {
                if (!nextPipe && _spec.HasPipePort)
                {
                    // REQ-V2-WATER-02/COMPAT-02: the pipe path degraded to manual refill; throttled
                    // so a missing adapter produces one water.* warning, never a per-farm spam.
                    _log.Write(HdbfLogCategory.Warn, IndoorLogKeys.PipeUnavailable,
                        "pipe port unavailable (adapter=" + health + "); degrading to manual refill, entityId="
                        + _entityId);
                }

                record.PipePort = nextPipe;
                changed = true;
            }

            if (record.LogisticsPort != nextLogistics)
            {
                record.LogisticsPort = nextLogistics;
                changed = true;
            }

            if (record.PowerPort != nextPower)
            {
                record.PowerPort = nextPower;
                changed = true;
            }

            if (changed)
            {
                _log.Write(HdbfLogCategory.Compat, IndoorLogKeys.Port,
                    "indoor port availability refreshed: pipe=" + nextPipe + ", logistics=" + nextLogistics
                    + ", power=" + nextPower + ", adapter=" + health + ", entityId=" + _entityId);
            }

            return changed;
        }
    }
}
