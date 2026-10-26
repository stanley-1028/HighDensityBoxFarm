using System;

namespace HDBF.Core.Balance
{
    /// <summary>
    /// The single implementation of every balance formula in the requirement document.
    ///
    /// Rules that make this class safe to depend on:
    /// * pure static functions, no allocation, no mutable state (REQ-ARCH-06, REQ-IMPL-31);
    /// * every multiplication layer is explicit, so a layer can never be silently skipped
    ///   (REQ-BAL-28);
    /// * out-of-range input is clamped instead of throwing, because these functions sit on the
    ///   boundary between untrusted save data and the simulation (chapter 10.1). Inputs that
    ///   indicate a *config* error (cMin outside (0,1]) are clamped to a safe value and callers
    ///   are expected to validate config separately.
    ///
    /// Chapter references below map each function back to the requirement it implements.
    /// </summary>
    public static class BalanceFormulas
    {
        public static float Clamp01(float value)
        {
            if (float.IsNaN(value))
            {
                return 0f;
            }

            if (value < 0f)
            {
                return 0f;
            }

            return value > 1f ? 1f : value;
        }

        /// <summary>
        /// Effective plots: min(slot capacity, on-duty workers * plots per farmer).
        /// Chapter 6.2 / REQ-IMPL-08. Built-in bonus: none, by design.
        /// </summary>
        public static int EffectivePlots(int plotCapacity, int activeWorkers, int plotsPerFarmer = TierCatalog.PlotsPerFarmer)
        {
            if (plotCapacity <= 0 || activeWorkers <= 0 || plotsPerFarmer <= 0)
            {
                return 0;
            }

            var allowedByWorkers = (long)activeWorkers * plotsPerFarmer;
            return (int)Math.Min(plotCapacity, allowedByWorkers);
        }

        /// <summary>Irrigation coverage C = irrigated / registered. Chapter 6.3 / REQ-IMPL-11.</summary>
        public static float Coverage(int irrigatedPlots, int registeredPlots)
        {
            if (registeredPlots <= 0)
            {
                return 0f;
            }

            if (irrigatedPlots <= 0)
            {
                return 0f;
            }

            return Math.Min(1f, (float)irrigatedPlots / registeredPlots);
        }

        /// <summary>
        /// G_irr = 0 below 0.25*C_min, then linear up to 1.0 at C_min.
        /// Chapter 6.3 formula, TC-16 expects (0, 0, 1/3, 1, 1) at C = 0, 0.25Cmin, 0.5Cmin, Cmin, 1.
        /// </summary>
        public static float IrrigationMultiplier(float coverage, float minIrrigationCoverage, float zeroAtRatio = 0.25f)
        {
            if (minIrrigationCoverage <= 0f || float.IsNaN(minIrrigationCoverage))
            {
                // A tier without an irrigation requirement is always fully irrigated.
                return 1f;
            }

            var clampedMin = minIrrigationCoverage > 1f ? 1f : minIrrigationCoverage;
            var ratio = zeroAtRatio <= 0f ? 0f : zeroAtRatio > 1f ? 1f : zeroAtRatio;
            var zeroPoint = clampedMin * ratio;

            if (coverage <= zeroPoint)
            {
                return 0f;
            }

            if (coverage >= clampedMin)
            {
                return 1f;
            }

            return Clamp01((coverage - zeroPoint) / (clampedMin - zeroPoint));
        }

        /// <summary>G_fat = 1 - 0.60*F; F is clamped to [0,1]. Chapter 6.4.</summary>
        public static float FatigueMultiplier(float fatigue, float penalty = TierCatalog.FatiguePenalty)
        {
            var clampedPenalty = penalty < 0f ? 0f : penalty > 1f ? 1f : penalty;
            return 1f - clampedPenalty * Clamp01(fatigue);
        }

        /// <summary>
        /// Fatigue gained by one harvest. Same crop as last time: full increment.
        /// Rotated crop: increment * rotationFactor (0.40). Chapter 6.4 / REQ-BAL-09.
        /// </summary>
        public static float FatigueDelta(bool sameCropAsPrevious, float tierFatigueFactor,
            float baseIncrement = TierCatalog.FatiguePerHarvest, float rotationFactor = TierCatalog.RotationFactor)
        {
            if (tierFatigueFactor < 0f || float.IsNaN(tierFatigueFactor))
            {
                tierFatigueFactor = 0f;
            }

            var delta = baseIncrement * tierFatigueFactor;
            return sameCropAsPrevious ? delta : delta * rotationFactor;
        }

        /// <summary>Applies one harvest worth of fatigue, clamping to [0,1] (chapter 6.4).</summary>
        public static float ApplyHarvestFatigue(float currentFatigue, bool sameCropAsPrevious, float tierFatigueFactor,
            float baseIncrement = TierCatalog.FatiguePerHarvest, float rotationFactor = TierCatalog.RotationFactor,
            float rotationRecovery = TierCatalog.RotationRecovery)
        {
            var next = Clamp01(currentFatigue) + FatigueDelta(sameCropAsPrevious, tierFatigueFactor, baseIncrement, rotationFactor);
            next = Clamp01(next);
            if (!sameCropAsPrevious)
            {
                next = Math.Max(0f, next - rotationRecovery);
            }

            return next;
        }

        /// <summary>Fallow recovery: F -= 0.03 per game day, floored at 0. Chapter 6.7.</summary>
        public static float ApplyFallowRecovery(float currentFatigue, int days, float perDay = TierCatalog.FallowRecoveryPerDay)
        {
            if (days <= 0)
            {
                return Clamp01(currentFatigue);
            }

            var decrease = perDay * days;
            return Math.Max(0f, Clamp01(currentFatigue) - decrease);
        }

        /// <summary>
        /// Number of maintenance cycles missed. Mirrors the reference pseudo-code in chapter 11.6:
        /// 0 while today &lt; dueDay, otherwise 1 + floor((today - dueDay) / cycleDays).
        /// </summary>
        public static int MaintenanceCyclesMissed(float dueDay, float today, float cycleDays)
        {
            if (cycleDays <= 0f || float.IsNaN(cycleDays) || float.IsNaN(dueDay) || float.IsNaN(today))
            {
                return 0;
            }

            if (today < dueDay)
            {
                return 0;
            }

            var overdue = (today - dueDay) / cycleDays;
            var cycles = 1 + (int)Math.Floor(overdue);
            return cycles < 0 ? 0 : cycles;
        }

        /// <summary>
        /// G_maint: 1.0 on schedule, 0.5 one cycle overdue, 0.0 from two missed cycles (shutdown).
        /// Chapter 6.5 / REQ-IMPL-20 / REQ-BAL-12.
        /// </summary>
        public static float MaintenanceMultiplier(int cyclesMissed)
        {
            if (cyclesMissed <= 0)
            {
                return 1f;
            }

            return cyclesMissed == 1 ? 0.5f : 0f;
        }

        /// <summary>G_power: 1.0 connected, off-grid efficiency (0.6) otherwise. Chapter 6.5.</summary>
        public static float PowerMultiplier(bool powerConnected, float offGridEfficiency = TierCatalog.OffGridEfficiency)
        {
            if (powerConnected)
            {
                return 1f;
            }

            if (float.IsNaN(offGridEfficiency))
            {
                return 0f;
            }

            return offGridEfficiency < 0f ? 0f : offGridEfficiency > 1f ? 1f : offGridEfficiency;
        }

        /// <summary>Mode yield multiplier: Normal 1.0, Dense 1.35, Fallow 0 (chapter 6.7).</summary>
        public static float ModeYieldMultiplier(HdbfMode mode, float denseYield = TierCatalog.DenseYieldMultiplier)
        {
            switch (mode)
            {
                case HdbfMode.Dense:
                    return denseYield < 1f ? 1f : denseYield;
                case HdbfMode.Fallow:
                    return 0f;
                default:
                    return 1f;
            }
        }

        /// <summary>Mode water multiplier: Normal 1.0, Dense 1.5, Fallow 0.3 (chapter 6.7).</summary>
        public static float ModeWaterMultiplier(HdbfMode mode, float denseWater = TierCatalog.DenseWaterMultiplier)
        {
            switch (mode)
            {
                case HdbfMode.Dense:
                    return denseWater < 1f ? 1f : denseWater;
                case HdbfMode.Fallow:
                    return TierCatalog.FallowWaterMultiplier;
                default:
                    return 1f;
            }
        }

        /// <summary>
        /// Mode fatigue multiplier: Normal 1.0, Dense 1.5, Fallow 0 (fallow recovers fatigue instead,
        /// see <see cref="ApplyFallowRecovery"/>). Chapter 6.7.
        /// </summary>
        public static float ModeFatigueMultiplier(HdbfMode mode, float denseFatigue = TierCatalog.DenseFatigueMultiplier)
        {
            switch (mode)
            {
                case HdbfMode.Dense:
                    return denseFatigue < 1f ? 1f : denseFatigue;
                case HdbfMode.Fallow:
                    return 0f;
                default:
                    return 1f;
            }
        }

        /// <summary>Mode work multiplier: Normal 1.0, Dense 1.2, Fallow 0.2 (chapter 6.7).</summary>
        public static float ModeWorkMultiplier(HdbfMode mode, float denseWork = TierCatalog.DenseWorkMultiplier)
        {
            switch (mode)
            {
                case HdbfMode.Dense:
                    return denseWork < 1f ? 1f : denseWork;
                case HdbfMode.Fallow:
                    return TierCatalog.FallowWorkMultiplier;
                default:
                    return 1f;
            }
        }

        /// <summary>
        /// workCost = baseCost * (1 + pathLength / divisor), in workdays per plot action.
        /// Chapter 6.2 / REQ-IMPL-16. Negative path lengths count as 0.
        /// </summary>
        public static float WorkCost(int pathLength, float baseCost = TierCatalog.WorkCostPerPlotDay,
            float pathDistanceDivisor = TierCatalog.PathDistanceDivisor)
        {
            var path = pathLength < 0 ? 0 : pathLength;
            var divisor = pathDistanceDivisor <= 0f ? 1f : pathDistanceDivisor;
            return baseCost * (1f + path / divisor);
        }

        /// <summary>Plot actions available per day: workers * 13 (chapter 6.2).</summary>
        public static int DailyPlotActions(int activeWorkers, int actionsPerFarmerDay = TierCatalog.PlotActionsPerFarmerDay)
        {
            if (activeWorkers <= 0 || actionsPerFarmerDay <= 0)
            {
                return 0;
            }

            var total = (long)activeWorkers * actionsPerFarmerDay;
            return total > int.MaxValue ? int.MaxValue : (int)total;
        }

        /// <summary>
        /// Final single-plot yield: base * G_irr * G_fat * G_maint * G_power * G_mode.
        /// Chapter 6.1 / REQ-BAL-28 / REQ-IMPL-32: this is the only place that multiplies,
        /// so no caller can skip a layer.
        ///
        /// The four penalty layers are clamped to [0,1] because the design is "只减不加".
        /// G_mode is deliberately <b>not</b> capped at 1: dense planting is the single layer
        /// allowed to exceed 1 (1.35 in chapter 6.7), and clamping it would silently remove the
        /// entire dense-mode bonus.
        /// </summary>
        public static float PlotYield(float baseYield, float irrigationMultiplier, float fatigueMultiplier,
            float maintenanceMultiplier, float powerMultiplier, float modeYieldMultiplier)
        {
            if (baseYield <= 0f)
            {
                return 0f;
            }

            var mode = float.IsNaN(modeYieldMultiplier) || modeYieldMultiplier < 0f ? 0f : modeYieldMultiplier;

            var result = baseYield
                         * Clamp01(irrigationMultiplier)
                         * Clamp01(fatigueMultiplier)
                         * Clamp01(maintenanceMultiplier)
                         * Clamp01(powerMultiplier)
                         * mode;
            return result <= 0f || float.IsNaN(result) || float.IsInfinity(result) ? 0f : result;
        }

        /// <summary>
        /// Water drawn by the internal tank on one day. Only plots that are actually fed by the
        /// tank are counted; naturally irrigated plots cost nothing (chapter 6.3 / REQ-IMPL-12).
        /// </summary>
        public static float DailyTankDraw(int tankFedPlots, float waterPerPlotPerDay, HdbfMode mode)
        {
            if (tankFedPlots <= 0 || waterPerPlotPerDay <= 0f)
            {
                return 0f;
            }

            return tankFedPlots * waterPerPlotPerDay * ModeWaterMultiplier(mode);
        }

        /// <summary>Days the tank can still supply, using the current draw rate. Display uses floor.</summary>
        public static float TankDaysRemaining(float tankWater, int tankFedPlots, float waterPerPlotPerDay, HdbfMode mode)
        {
            var draw = DailyTankDraw(tankFedPlots, waterPerPlotPerDay, mode);
            if (tankWater <= 0f || draw <= 0f)
            {
                return 0f;
            }

            return tankWater / draw;
        }

        /// <summary>
        /// Net labour efficiency of dense planting: yield multiplier / work multiplier
        /// (1.35/1.20 = 1.125 as stated in chapter 6.7). Used by tests and by the balance report.
        /// </summary>
        public static float DenseNetLabourEfficiency(
            float denseYield = TierCatalog.DenseYieldMultiplier,
            float denseWork = TierCatalog.DenseWorkMultiplier)
        {
            if (denseWork <= 0f)
            {
                return 0f;
            }

            return denseYield / denseWork;
        }

        /// <summary>
        /// True when the maintenance tier shuts the farm down this cycle. Shutdown stops
        /// seeding/harvesting only; crops keep growing (REQ-IMPL-20).
        /// </summary>
        public static bool IsShutdown(int cyclesMissed) => MaintenanceMultiplier(cyclesMissed) <= 0f;

        // ------------------------------------------------------------------ growth-rate dilation (ADR-009 / D-2)

        /// <summary>
        /// Smallest growth-rate multiplier the Harmony growth patch is allowed to apply.
        ///
        /// <see cref="GrowthMultiplier"/> can legitimately be 0 (shutdown, no irrigation), and the
        /// requirement layers that produce a zero multiplier must not freeze an already planted
        /// crop permanently: REQ-IMPL-20 explicitly keeps crops growing during a maintenance
        /// shutdown. The patch therefore applies a finite 20x slowdown at worst; a real zero
        /// <em>output</em> is still enforced by the dispatch gates (maintenance shutdown stops
        /// seeding/harvesting, so no crop is harvested and nothing enters the inventory).
        /// </summary>
        public const float MinimumGrowthRate = 0.05f;

        /// <summary>
        /// Growth-rate product of the four constraint layers:
        /// <c>G_irr × G_fat × G_maint × G_power</c>, clamped to [0,1] (REQ-CORE-03, REQ-BAL-02,
        /// REQ-BAL-28). This is the value the approved Postfix of ADR-009 uses to stretch the
        /// remaining vanilla growth time of a managed crop (1.0 = vanilla speed, 0 = stop).
        ///
        /// G_mode is deliberately not part of this product: dense mode is already realised by the
        /// existing growth channel of the farm controller (D-2), and this patch must not change
        /// that channel. Like <see cref="PlotYield"/> this is a "only reduces" layer.
        /// </summary>
        public static float GrowthMultiplier(float irrigationMultiplier, float fatigueMultiplier,
            float maintenanceMultiplier, float powerMultiplier)
        {
            var result = Clamp01(irrigationMultiplier)
                         * Clamp01(fatigueMultiplier)
                         * Clamp01(maintenanceMultiplier)
                         * Clamp01(powerMultiplier);
            if (float.IsNaN(result) || result < 0f)
            {
                return 0f;
            }

            return result > 1f ? 1f : result;
        }

        /// <summary>
        /// Maps a <see cref="GrowthMultiplier"/> to the rate actually applied to the crop timer,
        /// clamped to [<see cref="MinimumGrowthRate"/>, 1]. NaN and negative inputs become the
        /// minimum; values above 1 cannot happen for the four penalty layers but are clamped to
        /// keep the timer arithmetic finite.
        /// </summary>
        public static float GrowthRateMultiplier(float growthMultiplier)
        {
            if (float.IsNaN(growthMultiplier) || growthMultiplier < MinimumGrowthRate)
            {
                return MinimumGrowthRate;
            }

            return growthMultiplier > 1f ? 1f : growthMultiplier;
        }

        /// <summary>
        /// Inverse of <see cref="GrowthRateMultiplier"/>: the factor a crop's growth time is
        /// multiplied by so that it advances at the constrained rate. 1.0 = vanilla duration,
        /// 2.0 = half speed, at most 1/<see cref="MinimumGrowthRate"/> = 20x.
        /// </summary>
        public static float GrowthTimeScale(float growthRateMultiplier)
        {
            return 1f / GrowthRateMultiplier(growthRateMultiplier);
        }
    }
}
