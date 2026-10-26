// ============================================================================
// HDBF v2 | Layer: Gameplay/Components | Owner: V2-GAMEPLAY-01 / V2-GAMEPLAY-02
// Purpose: CreateIndoorSnapshot (Core IndoorFarmSnapshot for V2-UI-01) plus the slot/water/
//          mode/rotation helpers it needs. The port/occupancy helpers live in the
//          .IndoorSnapshot.Helpers partial so every embedded file stays within the gate.
// Requirements: REQ-V2-WATER-03, REQ-V2-UI-01/02, REQ-V2-CORE-01, REQ-V2-SAFE-07
// ============================================================================
using System;
using HDBF.Core;
using HDBF.Core.Indoor;

namespace HDBF.Gameplay.Components
{
    public partial class HdbfBoxFarmController
    {
        /// <summary>The Core panel DTO for V2-UI-01; no game types leave this method.</summary>
        public IndoorFarmSnapshot CreateIndoorSnapshot()
        {
            var record = _record;
            if (record is null)
            {
                return EmptyIndoorSnapshot();
            }

            var workers = WorkerCount();
            CountSlots(out var empty, out var growing, out var mature);
            var effective = IndoorLaborMath.EffectiveSlots(_profile, workers);
            return new IndoorFarmSnapshot(
                record.Tier,
                record.Faction,
                workers,
                _workplace is null ? _profile.MaxWorkers : _workplace.MaxWorkers,
                record.Mode,
                IsModeTransitioning(),
                _profile.SlotCapacity,
                // v2 has no per-plot configuration: all capacity slots are configured for the tier.
                _profile.SlotCapacity,
                effective,
                empty,
                growing,
                mature,
                _lastBacklogActions,
                record.Tank,
                _profile.TankCapacity,
                TankDaysRemaining(workers),
                _lastCoverage,
                _profile.MinIrrigationCoverage,
                WaterSupply(),
                record.MaintenanceDueDay,
                record.MaintenanceCyclesMissed,
                (_lastWarnings & IndoorWarningFlags.MissingMaterial) != 0,
                record.PowerMode,
                record.PowerConnected,
                record.CropPolicy,
                record.FixedCropId,
                RotationSequence(),
                OutputOccupied(),
                LogisticsConnected(),
                record.PipePort,
                record.LogisticsPort,
                record.PowerPort,
                _lastWarnings,
                record.TimberPipesAdapter);
        }

        private IndoorFarmSnapshot EmptyIndoorSnapshot()
        {
            return new IndoorFarmSnapshot(_profile.Tier, _faction, 0, _profile.MaxWorkers,
                IndoorMode.Normal, false, _profile.SlotCapacity, 0, 0, 0, 0, 0, 0,
                0f, _profile.TankCapacity, float.PositiveInfinity, 0f,
                _profile.MinIrrigationCoverage, WaterSupplyKind.None, 0f, 0, false,
                PowerConnection.NotRequired, false, CropPolicy.Rotation, string.Empty,
                string.Empty, 0, false, true, false, false, IndoorWarningFlags.None, string.Empty);
        }

        private void CountSlots(out int empty, out int growing, out int mature)
        {
            empty = 0;
            growing = 0;
            mature = 0;
            var slots = _record?.Slots;
            if (slots is null)
            {
                return;
            }

            for (var index = 0; index < slots.Length; index++)
            {
                var slot = slots[index];
                if (slot is null)
                {
                    empty++;
                }
                else if (slot.Stage == SlotStage.Mature)
                {
                    mature++;
                }
                else if (slot.Stage == SlotStage.Growing)
                {
                    growing++;
                }
                else
                {
                    empty++;
                }
            }
        }

        private float TankDaysRemaining(int workers)
        {
            var record = _record;
            if (record is null)
            {
                return float.PositiveInfinity;
            }

            var effective = IndoorLaborMath.EffectiveSlots(_profile, workers);
            var demand = IndoorWaterMath.DemandPerDay(effective, _profile.WaterPerSlotPerDay, record.Mode);
            return IndoorWaterMath.DaysRemaining(record.Tank, demand);
        }

        private bool IsModeTransitioning()
        {
            if (float.IsNaN(_lastModeChangeDay))
            {
                return false;
            }

            try
            {
                // The transition window is inclusive: a switch made exactly on the day boundary is
                // still derated by the next daily tick (section 19.8, one game day).
                return _dayNightCycle.PartialDayNumber - _lastModeChangeDay
                    <= IndoorConstants.ModeTransitionDays;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private WaterSupplyKind WaterSupply()
        {
            // The panel enables its refill button only for Manual, so the daily refresh decides the
            // kind once per day (pipe > reachable manual source > none) and this getter stays cheap.
            if (_waterSupply != WaterSupplyKind.None || _record is null)
            {
                return _waterSupply;
            }

            try
            {
                if (_record.PipePort && _adapterHost.IsPipeConnected && _adapterHost.PipeWaterSource is not null)
                {
                    return WaterSupplyKind.Pipe;
                }

                if (_manualSourceAvailable || _manualRefillRequested || _pendingManualSource is not null)
                {
                    return WaterSupplyKind.Manual;
                }
            }
            catch (Exception)
            {
                // Snapshot failure must not affect simulation; report no supply.
            }

            return _waterSupply;
        }

        /// <summary>
        /// Crop rotation sequence shown by the panel: the faction crop table in order (fixed mode
        /// shows the fixed crop). The string is built once per farm and cached; this method does not
        /// allocate in the steady state (REQ-V2-PERF-02).
        /// </summary>
        private string RotationSequence()
        {
            var record = _record;
            if (record is null)
            {
                return string.Empty;
            }

            if (record.CropPolicy == CropPolicy.Fixed)
            {
                return record.FixedCropId;
            }

            if (!string.IsNullOrEmpty(_rotationSequence))
            {
                return _rotationSequence;
            }

            try
            {
                var crops = _cropCatalog.ForFaction(_faction);
                if (crops is null || crops.Count == 0)
                {
                    return string.Empty;
                }

                var builder = new System.Text.StringBuilder();
                for (var index = 0; index < crops.Count; index++)
                {
                    var crop = crops[index];
                    if (crop is null || string.IsNullOrEmpty(crop.Id))
                    {
                        continue;
                    }

                    if (builder.Length > 0)
                    {
                        builder.Append(" > ");
                    }

                    builder.Append(crop.Id);
                    if (builder.Length > 160)
                    {
                        break;
                    }
                }

                _rotationSequence = builder.ToString();
                return _rotationSequence;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }
    }
}
