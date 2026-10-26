using System;
using HDBF.Core;
using HDBF.Core.Abstractions;
using HDBF.Core.Balance;

namespace HDBF.Gameplay.Components
{
    /// <summary>
    /// Partial part: the immutable UI snapshot and the warning inputs derived from it (chapter 9.2,
    /// chapter 14.2). This is the only place gameplay reads state back out for UI.
    /// </summary>
    public partial class HdbfBoxFarmController
    {
        // ------------------------------------------------------------------ snapshot

        /// <inheritdoc />
        public BoxFarmSnapshot CreateSnapshot()
        {
            UpdatePower();
            var config = _configProvider.Config;
            var mode = _record?.Mode ?? HdbfMode.Normal;
            var registered = _record?.PlotIds.Length ?? 0;
            var workers = _workplace is not null ? _workplace.NumberOfAssignedWorkers : 0;
            var maxWorkers = _workplace is not null ? _workplace.MaxWorkers : _profile.MaxWorkers;
            var plotsPerFarmer = _spec.PlotsPerFarmer > 0 ? _spec.PlotsPerFarmer : TierCatalog.PlotsPerFarmer;
            var effective = BalanceFormulas.EffectivePlots(_profile.PlotCapacity, workers, plotsPerFarmer);

            float fatigueAverage;
            float fatigueMax;
            ComputeFatigue(out fatigueAverage, out fatigueMax);

            var multipliers = new FarmMultipliers
            {
                Irrigation = BalanceFormulas.IrrigationMultiplier(_coverage, _profile.MinIrrigationCoverage),
                Fatigue = BalanceFormulas.FatigueMultiplier(fatigueMax, config.FatiguePenalty),
                Maintenance = _maintenanceMultiplier,
                Power = _powerMultiplier,
                Mode = BalanceFormulas.ModeYieldMultiplier(mode, config.DenseYieldMultiplier),
            };

            var tankWater = _record?.TankWater ?? 0f;
            var tankDays = BalanceFormulas.TankDaysRemaining(tankWater, _tankFedPlots,
                _profile.WaterPerPlotPerDay, mode);

            return new BoxFarmSnapshot
            {
                EntityId = _saveEntityId,
                // Runtime key: the cross-building warning rows need it to select the building in
                // the world (IBoxFarmLocator); UI lookup already uses the Guid.
                RuntimeEntityId = _entityId,
                Tier = _profile.Tier,
                Mode = mode,
                RegisteredPlots = registered,
                EffectivePlots = effective,
                TillablePlots = _tillablePlots,
                ActiveWorkers = workers,
                MaxWorkers = maxWorkers,
                IrrigationCoverage = _coverage,
                MinIrrigationCoverage = _profile.MinIrrigationCoverage,
                FatigueAverage = fatigueAverage,
                FatigueMax = fatigueMax,
                MaintenanceDaysLeft = MaintenanceDaysLeft(),
                CyclesMissed = _cyclesMissed,
                TankWater = tankWater,
                TankDaysLeft = tankDays,
                TierNeedsPower = _profile.NeedsPower,
                PowerConnected = _powerConnected,
                PipePort = PortState(HdbfPortKind.Pipe),
                PowerPort = PortState(HdbfPortKind.Power),
                LogisticsPort = PortState(HdbfPortKind.Logistics),
                IsShutdown = _isShutdown,
                IsEntranceBlocked = IsEntranceBlocked(),
                // The overlay session is a gameplay singleton; the snapshot only mirrors it so the
                // panel toggle renders the authoritative state instead of a local guess (REQ-UI-05).
                IsPlotHighlightActive = _plotHighlightState.IsActiveFor(_entityId),
                PlotActionFailureKey = _plotHighlightState.IsActiveFor(_entityId)
                    ? _plotHighlightState.FailureKey
                    : null,
                Multipliers = multipliers,
                Warnings = Array.Empty<FarmWarning>(),
            };
        }

        /// <inheritdoc />
        public HdbfWarningInputs CreateWarningInputs(BoxFarmSnapshot snapshot)
        {
            return new HdbfWarningInputs
            {
                IsShutdown = snapshot.IsShutdown,
                HasRegisteredPlots = snapshot.RegisteredPlots > 0,
                IrrigationCoverage = snapshot.IrrigationCoverage,
                MinIrrigationCoverage = snapshot.MinIrrigationCoverage,
                RequiresPower = snapshot.TierNeedsPower,
                PowerConnected = snapshot.PowerConnected,
                CyclesMissed = snapshot.CyclesMissed,
                FatigueMax = snapshot.FatigueMax,
                TankDaysLeft = snapshot.TankDaysLeft,
                TankFedPlots = _tankFedPlots,
                TankCapacityDays = _profile.TankCapacityDays,
                AdapterFailedCount = CountAdapters(HdbfAdapterHealth.Failed),
                AdapterDisabledCount = CountAdapters(HdbfAdapterHealth.Disabled),
                // The gate is wall-clock based, so the icon pass and the panel refresh share the
                // same 2 s window (chapter 14.2). The value is a requirement constant, not the UI
                // refresh rate: changing uiRefreshHz must not change the debounce time.
                HysteresisSeconds = HdbfWarningRules.HysteresisSeconds,
            };
        }

        private void ComputeFatigue(out float average, out float maximum)
        {
            var fatigue = _record?.Fatigue;
            if (fatigue is null || fatigue.Length == 0)
            {
                average = 0f;
                maximum = 0f;
                return;
            }

            var sum = 0f;
            var max = 0f;
            for (var i = 0; i < fatigue.Length; i++)
            {
                var value = BalanceFormulas.Clamp01(fatigue[i]);
                sum += value;
                if (value > max)
                {
                    max = value;
                }
            }

            average = sum / fatigue.Length;
            maximum = max;
        }

        private float MaintenanceDaysLeft()
        {
            if (_record is null || _cycleDays <= 0f)
            {
                return 0f;
            }

            return _record.DueDay - _dayNightCycle.PartialDayNumber;
        }

        private HdbfPortState PortState(HdbfPortKind port)
        {
            var enabled = port switch
            {
                HdbfPortKind.Pipe => _spec.HasPipePort,
                HdbfPortKind.Power => _spec.HasPowerPort,
                HdbfPortKind.Logistics => _spec.HasLogisticsPort,
                _ => false,
            };

            if (!enabled)
            {
                return HdbfPortState.Hidden;
            }

            var connected = port switch
            {
                HdbfPortKind.Pipe => _record is not null && _record.PortPipe,
                HdbfPortKind.Power => _powerConnected,
                HdbfPortKind.Logistics => _record is not null && _record.PortLogistics,
                _ => false,
            };

            return connected ? HdbfPortState.Connected : HdbfPortState.Available;
        }

        private bool IsEntranceBlocked()
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

            // A path at the doorstep is what makes the entrance usable; a wall/platform blocks it.
            return _blockService.GetPathObjectAt(entrance.DoorstepCoordinates) is null;
        }

        private int CountAdapters(HdbfAdapterHealth health)
        {
            var adapters = _record?.Adapters;
            if (adapters is null || adapters.Count == 0)
            {
                return 0;
            }

            var count = 0;
            foreach (var pair in adapters)
            {
                if (Enum.TryParse(pair.Value, true, out HdbfAdapterHealth parsed) && parsed == health)
                {
                    count++;
                }
            }

            return count;
        }
    }
}

