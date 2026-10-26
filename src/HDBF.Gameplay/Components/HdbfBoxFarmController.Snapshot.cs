// ============================================================================
// HDBF v2 | Layer: Gameplay/Components | Owner: V2-GAMEPLAY-01
// Purpose: Maps the v2 IndoorFarmRecord onto the temporary v1 BoxFarmSnapshot used by the
//          still-shipped UI and fills the retained warning-input contract. The Core
//          IndoorFarmSnapshot DTO and its helpers live in the .IndoorSnapshot partial.
// Requirements: REQ-V2-WATER-03, REQ-V2-UI-01/02, REQ-V2-CORE-03, REQ-V2-SAFE-01
// ============================================================================
using System;
using HDBF.Core;
using HDBF.Core.Abstractions;
using HDBF.Core.Indoor;

namespace HDBF.Gameplay.Components
{
    public partial class HdbfBoxFarmController
    {
        /// <summary>
        /// Temporary adapter for the v1 UI: maps the v2 record onto the frozen
        /// <see cref="BoxFarmSnapshot"/> contract so HDBF.UI still compiles until V2-UI-01 ships.
        /// Plot-specific numeric fields are mapped to slot equivalents; no world plot is implied.
        /// </summary>
        public BoxFarmSnapshot CreateSnapshot()
        {
            var record = _record;
            if (record is null)
            {
                return new BoxFarmSnapshot
                {
                    EntityId = _saveEntityId,
                    RuntimeEntityId = _entityId,
                    Tier = _profile.Tier,
                    Mode = HdbfMode.Normal,
                };
            }

            var workers = WorkerCount();
            var effective = IndoorLaborMath.EffectiveSlots(_profile, workers);
            var fatigue = FatigueMax();

            return new BoxFarmSnapshot
            {
                EntityId = _saveEntityId,
                RuntimeEntityId = _entityId,
                Tier = record.Tier,
                Mode = MapMode(record.Mode),
                // Temporary v1 field mapping: slot capacity replaces the old plot count.
                RegisteredPlots = effective,
                EffectivePlots = effective,
                TillablePlots = _profile.SlotCapacity,
                ActiveWorkers = workers,
                MaxWorkers = _workplace is null ? _profile.MaxWorkers : _workplace.MaxWorkers,
                IrrigationCoverage = _lastCoverage,
                MinIrrigationCoverage = _profile.MinIrrigationCoverage,
                FatigueAverage = fatigue,
                FatigueMax = fatigue,
                MaintenanceDaysLeft = MaintenanceDaysLeft(),
                CyclesMissed = record.MaintenanceCyclesMissed,
                TankWater = record.Tank,
                TankDaysLeft = TankDaysRemaining(workers),
                TierNeedsPower = _profile.NeedsPower,
                PowerConnected = record.PowerConnected,
                PipePort = PortState(HdbfPortKind.Pipe),
                PowerPort = PortState(HdbfPortKind.Power),
                LogisticsPort = PortState(HdbfPortKind.Logistics),
                IsShutdown = _lastStalled,
                IsEntranceBlocked = _entranceBlocked,
                IsPlotHighlightActive = false,
                PlotActionFailureKey = null,
                Multipliers = new FarmMultipliers
                {
                    Irrigation = IndoorWaterMath.IrrigationMultiplier(_lastCoverage, _profile.MinIrrigationCoverage),
                    Fatigue = IndoorGrowthMath.FatigueYieldMultiplier(fatigue),
                    Maintenance = IndoorMaintenanceMath.Efficiency(record.MaintenanceCyclesMissed),
                    Power = IndoorPowerMath.Efficiency(
                        _profile.NeedsPower && record.PowerMode == PowerConnection.Grid,
                        record.PowerConnected),
                    Mode = IndoorGrowthMath.ModeYieldMultiplier(record.Mode),
                },
                Warnings = Array.Empty<FarmWarning>(),
            };
        }

        /// <summary>
        /// Warning evaluation input derived from the v2 record for the retained hysteresis and
        /// world-icon framework. It keeps the v1 input shape until V2-UI-01 replaces the panel.
        /// </summary>
        public HdbfWarningInputs CreateWarningInputs(BoxFarmSnapshot snapshot)
        {
            var record = _record;
            if (record is null)
            {
                return new HdbfWarningInputs();
            }

            var workers = WorkerCount();
            var cycles = record.MaintenanceCyclesMissed;
            return new HdbfWarningInputs
            {
                IsShutdown = _lastStalled || IndoorMaintenanceMath.IsStalled(cycles),
                HasRegisteredPlots = _profile.SlotCapacity > 0,
                IrrigationCoverage = _lastCoverage,
                MinIrrigationCoverage = _profile.MinIrrigationCoverage,
                RequiresPower = _profile.NeedsPower,
                PowerConnected = record.PowerConnected,
                CyclesMissed = cycles,
                FatigueMax = FatigueMax(),
                TankDaysLeft = TankDaysRemaining(workers),
                TankFedPlots = IndoorLaborMath.EffectiveSlots(_profile, workers),
                TankCapacityDays = _profile.TankCapacityDays,
                AdapterFailedCount = AdapterCount(HdbfAdapterHealth.Failed),
                AdapterDisabledCount = AdapterCount(HdbfAdapterHealth.Disabled),
                HysteresisSeconds = HdbfWarningRules.HysteresisSeconds,
            };
        }

        /// <summary>
        /// ADR-009 compatibility shim: v2 has no world plots, so this always reports "not managed"
        /// and keeps vanilla growth untouched (REQ-V2-CORE-03).
        /// </summary>
        public bool TryGetPlotGrowthRate(int plotId, out float growthRate)
        {
            growthRate = 1f;
            return false;
        }

        private int WorkerCount()
        {
            try
            {
                return _workplace is null ? 0 : Math.Max(0, _workplace.NumberOfAssignedWorkers);
            }
            catch (Exception)
            {
                return 0;
            }
        }

        private static HdbfMode MapMode(IndoorMode mode)
        {
            switch (mode)
            {
                case IndoorMode.Dense: return HdbfMode.Dense;
                case IndoorMode.Fallow: return HdbfMode.Fallow;
                default: return HdbfMode.Normal;
            }
        }
    }
}
