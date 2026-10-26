using System;
using HDBF.Core;
using HDBF.Core.Balance;
using HDBF.Core.Logging;

namespace HDBF.Gameplay.Components
{
    /// <summary>
    /// Partial part: player/UI commands (mode, maintenance, plot exclusion, port switches).
    /// </summary>
    public partial class HdbfBoxFarmController
    {
        // ------------------------------------------------------------------ player/UI commands

        /// <summary>Resolved balance profile for this building (read-only; used by the command service).</summary>
        internal TierProfile TierProfile => _profile;

        /// <summary>True while the top tier is actually powered by the mechanical network.</summary>
        internal bool PowerConnected => _powerConnected;

        /// <summary>Current operating mode (chapter 6.7).</summary>
        public HdbfMode Mode => _record?.Mode ?? HdbfMode.Normal;

        /// <summary>
        /// Switches mode with the one-game-day anti-flicker cooldown of REQ-BAL-20. Returns false
        /// (and changes nothing) when the mode is invalid, unchanged, still cooling down, or when
        /// dense planting is disabled by configuration.
        /// </summary>
        public bool TrySetMode(HdbfMode mode)
        {
            if (_record is null || !Enum.IsDefined(typeof(HdbfMode), mode))
            {
                return false;
            }

            if (mode == _record.Mode)
            {
                return true;
            }

            var config = _configProvider.Config;
            if (mode == HdbfMode.Dense && !config.Features.DensePlanting)
            {
                return false;
            }

            var today = _dayNightCycle.PartialDayNumber;
            if (!float.IsNaN(_lastModeChangeDay) && today - _lastModeChangeDay < 1f)
            {
                return false;
            }

            _record.Mode = mode;
            _lastModeChangeDay = today;
            _saveDirty = true;
            _log.Write(HdbfLogCategory.Info, LogKeyMode,
                "mode changed to " + mode + ", entityId=" + _entityId);
            return true;
        }

        /// <summary>
        /// Resets the maintenance timer. Material deduction is owned by the inventory integration
        /// (v0.1 gap; see the gameplay report): this method only validates state and resets timing.
        /// </summary>
        public bool TryPerformMaintenance()
        {
            if (_record is null || _cycleDays <= 0f)
            {
                return false;
            }

            _record.DueDay = _dayNightCycle.PartialDayNumber + _cycleDays;
            _record.CyclesMissed = 0;
            _cyclesMissed = 0;
            _maintenanceMultiplier = 1f;
            _isShutdown = false;
            _saveDirty = true;
            _log.Write(HdbfLogCategory.Info, LogKeyTick,
                "maintenance performed, entityId=" + _entityId);
            return true;
        }

        /// <summary>True when the plot is currently registered by this farm.</summary>
        public bool IsRegisteredPlot(int plotId)
        {
            var ids = _record?.PlotIds;
            if (ids is null)
            {
                return false;
            }

            for (var i = 0; i < ids.Length; i++)
            {
                if (ids[i] == plotId)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Player exclusion (REQ-UI-05/REQ-IMPL-10): removes a registered plot and persists the
        /// exclusion. Refuses to remove the last plot. Returns false when nothing changed.
        /// </summary>
        public bool TryExcludePlot(int plotId)
        {
            if (_record is null || _record.PlotIds.Length <= 1 || !_excluded.Add(plotId))
            {
                return false;
            }

            var ids = _record.PlotIds;
            var index = Array.IndexOf(ids, plotId);
            if (index < 0)
            {
                _excluded.Remove(plotId);
                return false;
            }

            var remaining = new int[ids.Length - 1];
            Array.Copy(ids, 0, remaining, 0, index);
            Array.Copy(ids, index + 1, remaining, index, ids.Length - index - 1);
            ApplyPlotArrays(remaining);
            // Removing a plot changes the coverage denominator; resample now instead of waiting
            // for the next day tick (the P0 "panel shows a stale 0 %" class of bug).
            RefreshCoverageFromRegisteredPlots();
            _record.ExcludedPlotIds = ToIntArray(_excluded);
            // The exclusion is persisted state (REQ-IMPL-10): mark the record dirty like the
            // restore path already does, otherwise a save before the next scan can lose it.
            _saveDirty = true;
            _registry.ApplyPlotSelection(_entityId, remaining);
            return true;
        }

        /// <summary>Restores a player-excluded plot; the next scan can register it again.</summary>
        public bool TryIncludePlot(int plotId)
        {
            if (_record is null || !_excluded.Remove(plotId))
            {
                return false;
            }

            _record.ExcludedPlotIds = ToIntArray(_excluded);
            _saveDirty = true;
            StartScan();
            return true;
        }

        /// <summary>
        /// Adapter-facing port switch for the pipe/logistics ports (chapter 8.3). The power port
        /// reflects the physical mechanical connection and is therefore not switchable from here.
        /// </summary>
        public bool TrySetPortConnected(HdbfPortKind port, bool connected)
        {
            if (_record is null)
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

                    _record.PortPipe = connected;
                    break;
                case HdbfPortKind.Logistics:
                    if (!_spec.HasLogisticsPort)
                    {
                        return false;
                    }

                    _record.PortLogistics = connected;
                    break;
                default:
                    _log.Write(HdbfLogCategory.Info, LogKeyPort,
                        "power port state is owned by the mechanical network; request ignored");
                    return false;
            }

            _saveDirty = true;
            return true;
        }
    }
}

