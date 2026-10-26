using System;
using HDBF.Core;
using HDBF.Core.Balance;
using HDBF.Core.Config;
using HDBF.Core.Logging;
using HDBF.Core.Plots;
using UnityEngine;

namespace HDBF.Gameplay.Components
{
    /// <summary>
    /// Partial part: the once-per-game-day simulation step in the fixed order of REQ-IMPL-22
    /// (tank draw, coverage snapshot, fatigue, maintenance, dirty flag) plus power state.
    /// </summary>
    public partial class HdbfBoxFarmController
    {
        // ------------------------------------------------------------------ daily simulation

        private void OnDayTick()
        {
            try
            {
                RunDailyTick();
            }
            catch (Exception exception)
            {
                _log.Error(HdbfLogCategory.Error, LogKeyTick, "daily box farm tick failed", exception,
                    _entityId.ToString());
            }
            finally
            {
                RearmDayTrigger();
            }
        }

        /// <summary>
        /// One game day of farm state in the fixed order required by REQ-IMPL-22. Deterministic:
        /// it only reads the day counter and the persisted record, so 30 fps and 144 fps produce
        /// the same numbers.
        /// </summary>
        private void RunDailyTick()
        {
            if (_record is null)
            {
                return;
            }

            var config = _configProvider.Config;
            var today = _dayNightCycle.PartialDayNumber;
            var mode = _record.Mode;

            // 1. Tank draw: only plots that are actually dry and fed by the internal tank cost
            //    water (chapter 6.3 / REQ-IMPL-12). Natural moisture costs nothing.
            var registered = _record.PlotIds.Length;
            var moist = SampleMoistPlots();
            var dry = registered - moist;
            if (dry < 0)
            {
                dry = 0;
            }

            var perPlot = _profile.WaterPerPlotPerDay;
            var perPlotWithMode = perPlot * BalanceFormulas.ModeWaterMultiplier(mode, config.DenseWaterMultiplier);
            var tankFed = 0;
            if (dry > 0 && _record.TankWater > 0f && perPlotWithMode > 0f)
            {
                tankFed = (int)Math.Floor(_record.TankWater / perPlotWithMode);
                if (tankFed > dry)
                {
                    tankFed = dry;
                }

                if (tankFed > 0)
                {
                    var draw = BalanceFormulas.DailyTankDraw(tankFed, perPlot, mode);
                    _record.TankWater = Math.Max(0f, _record.TankWater - draw);
                    _saveDirty = true;
                }
            }

            // 2. Coverage snapshot: natural moisture plus the plots the tank just watered.
            var coverageSample = IrrigationCoverage.Compute(registered, moist, tankFed);
            _tankFedPlots = coverageSample.TankFedPlots;
            _irrigatedCount = coverageSample.IrrigatedPlots;
            _coverage = coverageSample.Coverage;
            DiagDaily(registered, moist, dry, _tankFedPlots, _coverage);

            // 3. Fatigue: only fallow mode changes it on a daily basis (chapter 6.4/6.7).
            if (mode == HdbfMode.Fallow && config.Features.SoilFatigue)
            {
                var fatigue = _record.Fatigue;
                for (var i = 0; i < fatigue.Length; i++)
                {
                    fatigue[i] = BalanceFormulas.ApplyFallowRecovery(fatigue[i], 1,
                        config.FallowRecoveryPerDay);
                }

                _saveDirty = true;
            }

            // 4. Maintenance expiry and power/off-grid fallback (chapter 6.5, REQ-IMPL-20/21).
            UpdatePowerAndMaintenance(today, config);

            // 5. Crop loop: new day budget/plan and the dense-mode growth channel (chapter 6.7).
            //    Maintenance shutdown was just resolved, so the plan sees the final state.
            OnFarmDayTick(today);

            // BUILD-05: one HDBF/Info line per day with the managed-crop visual counters (skipped
            // when the farm hid nothing and had no failures, so idle farms stay quiet).
            LogCropVisibilityDaily();

            // 6. Dirty flag is set by the steps that changed persisted state; the daily tick itself
            //    always ends with a scan request (chapter 11.3 trigger list).
            StartScan();
        }

        /// <summary>
        /// Counts the registered plots the soil-moisture service reports as moist.
        ///
        /// The stored plot id z is the plant cell z (the terrain column ceiling, the cell the
        /// Fields tool paints). That is exactly the cell the game's own planting soil validator
        /// queries (IL: <c>Timberborn.Planting.PlantingSoilValidator.Validate</c> calls
        /// <c>ISoilMoistureService.SoilIsMoist(plantingSpot.Coordinates)</c>), and the service
        /// resolves moisture by <c>IThreadSafeColumnTerrainMap.TryGetIndexAtCeiling(
        /// CellToIndex(XY(coordinates)), coordinates.z)</c>, matching
        /// <c>ReadOnlyTerrainColumn.Ceiling == coordinates.z</c>. The mapping is single-sourced in
        /// <see cref="PlotCellHeights.ResolveMoistureSampleZ"/> so the identity (and any future
        /// game-build change) is explicit and unit-tested.
        /// </summary>
        private int SampleMoistPlots()
        {
            try
            {
                var moist = 0;
                var ids = _record.PlotIds;
                for (var i = 0; i < ids.Length; i++)
                {
                    var plantCell = HdbfPlotCellCodec.Decode(ids[i]);
                    var sampleCell = new Vector3Int(plantCell.x, plantCell.y,
                        PlotCellHeights.ResolveMoistureSampleZ(plantCell.z));
                    if (_soilMoistureService.SoilIsMoist(sampleCell))
                    {
                        moist++;
                    }
                }

                _lastMoistCount = moist;
                return moist;
            }
            catch (Exception exception)
            {
                // Keep the previous sample and degrade instead of throwing into the simulation
                // (REQ-IMPL-14).
                _log.Error(HdbfLogCategory.Warn, HdbfLogKeys.CompatFailed,
                    "soil moisture sampling failed; keeping the previous coverage", exception,
                    _entityId.ToString());
                return _lastMoistCount;
            }
        }

        /// <summary>
        /// Re-samples moisture and refreshes the cached coverage right after the registered plot
        /// set changed (scan completion). Without this the panel shows 0 % from activation until
        /// the first daily tick even when every plot is moist — the reported P0 symptom — because
        /// <c>_coverage</c> used to be written only by the daily simulation step.
        ///
        /// The tank-fed count is kept (clamped) because the day tick may have just watered plots:
        /// the rescan at the end of the day must not make coverage flicker back to natural
        /// moisture only. The next day tick recomputes both counts exactly.
        /// </summary>
        private void RefreshCoverageFromRegisteredPlots()
        {
            var registered = _record?.PlotIds.Length ?? 0;
            if (registered <= 0)
            {
                // Empty registry: coverage is 0 by definition and nothing is sampled (no
                // division by zero, no tank-fed fiction).
                _lastMoistCount = 0;
                _tankFedPlots = 0;
                _irrigatedCount = 0;
                _coverage = 0f;
                return;
            }

            var moist = SampleMoistPlots();
            var sample = IrrigationCoverage.Compute(registered, moist, _tankFedPlots);
            _tankFedPlots = sample.TankFedPlots;
            _irrigatedCount = sample.IrrigatedPlots;
            _coverage = sample.Coverage;
        }

        /// <summary>
        /// Clamps the cached coverage snapshot to the current registered count without sampling
        /// (used when the plot set changed but moisture was not re-read yet). An empty registry
        /// becomes coverage 0 with no division.
        /// </summary>
        private void ClampCoverageToRegisteredPlots()
        {
            var registered = _record?.PlotIds.Length ?? 0;
            if (registered <= 0)
            {
                _lastMoistCount = 0;
                _tankFedPlots = 0;
                _irrigatedCount = 0;
                _coverage = 0f;
                return;
            }

            var sample = IrrigationCoverage.Compute(registered, _lastMoistCount, _tankFedPlots);
            _tankFedPlots = sample.TankFedPlots;
            _irrigatedCount = sample.IrrigatedPlots;
            _coverage = sample.Coverage;
        }

        private void UpdatePowerAndMaintenance(float today, HdbfConfig config)
        {
            UpdatePower();

            if (!config.Features.Maintenance
                || _profile.MaintenanceKind == HdbfMaintenanceKind.IrrigationOnly)
            {
                _cycleDays = 0f;
                _cyclesMissed = 0;
                _maintenanceMultiplier = 1f;
                _isShutdown = false;
                return;
            }

            var offGrid = _profile.NeedsPower && !_powerConnected;
            // Cycle selection is single-sourced in MaintenanceDefaults so the day tick and the
            // load-time safe default can never drift apart (BUG-06: the top tier's on-grid cycle
            // is 0, its off-grid cycle is the material cycle).
            var cycleDays = MaintenanceDefaults.CycleDaysFor(_profile, offGrid);

            if (!offGrid && _profile.MaintenanceKind == HdbfMaintenanceKind.PowerOrOffGridMetal
                && cycleDays <= 0f)
            {
                // On-grid top tier: connected power maintains the building automatically
                // (chapter 6.5), so there is no material cycle to track.
                _cycleDays = 0f;
                _cyclesMissed = 0;
                _maintenanceMultiplier = 1f;
                _isShutdown = false;
                return;
            }

            if (cycleDays <= 0f)
            {
                _cycleDays = 0f;
                _cyclesMissed = 0;
                _maintenanceMultiplier = 1f;
                _isShutdown = false;
                return;
            }

            _cycleDays = cycleDays;
            if (!_maintenanceInitialized)
            {
                // New building: the material cycle starts when the farm becomes active, not when
                // the construction site entity was spawned (chapter 6.5).
                if (!_saveLoaded)
                {
                    _record.DueDay = today + cycleDays;
                    _record.CyclesMissed = 0;
                    _saveDirty = true;
                }

                _maintenanceInitialized = true;
            }
            else if (_record.DueDay <= 0f)
            {
                // Off-grid switch: start the material cycle now (REQ-IMPL-21).
                _record.DueDay = today + cycleDays;
                _saveDirty = true;
            }

            var cyclesMissed = BalanceFormulas.MaintenanceCyclesMissed(_record.DueDay, today, cycleDays);
            if (cyclesMissed != _record.CyclesMissed)
            {
                _record.CyclesMissed = cyclesMissed;
                _saveDirty = true;
            }

            _cyclesMissed = cyclesMissed;
            _maintenanceMultiplier = BalanceFormulas.MaintenanceMultiplier(cyclesMissed);
            var shutdown = BalanceFormulas.IsShutdown(cyclesMissed);
            if (shutdown != _isShutdown)
            {
                _log.Write(HdbfLogCategory.Warn, LogKeyTick,
                    shutdown
                        ? "box farm entered maintenance shutdown; seeding and harvesting stopped"
                        : "box farm maintenance state changed; shutdown=" + shutdown);
            }

            _isShutdown = shutdown;
        }

        private void UpdatePower()
        {
            if (_record is null)
            {
                return;
            }

            if (!_profile.NeedsPower)
            {
                _powerConnected = true;
                _powerMultiplier = 1f;
                return;
            }

            if (_mechanicalBuilding is not null)
            {
                // Vanilla mechanical network state: true only while the building is finished and
                // actually receiving power (chapter 6.5, REQ-IMPL-21).
                _powerConnected = _mechanicalBuilding.ActiveAndPowered;
            }
            else
            {
                // No mechanical component on this blueprint (data not wired yet, or a compatibility
                // mod supplies the port): fall back to the persisted port state.
                _powerConnected = _record.PortPower;
            }

            if (_record.PortPower != _powerConnected)
            {
                _record.PortPower = _powerConnected;
                _saveDirty = true;
            }

            _powerMultiplier = BalanceFormulas.PowerMultiplier(_powerConnected,
                TierCatalog.OffGridEfficiency);
        }

        private void RearmDayTrigger()
        {
            if (_dayTrigger is null)
            {
                return;
            }

            try
            {
                _dayTrigger.Reset();
                _dayTrigger.Resume();
            }
            catch (Exception exception)
            {
                _log.Error(HdbfLogCategory.Error, LogKeyTick, "day trigger re-arm failed", exception,
                    _entityId.ToString());
            }
        }
    }
}

