// ============================================================================
// HDBF v2 | Layer: Core/Indoor | Owner: V2-CORE-01
// Purpose: Tank demand, coverage, G_irr and the once-per-day water settlement
//          (INTERFACES section 5, section 19.6). Pure BCL math; no water entities.
// Requirements: REQ-V2-WATER-01, REQ-V2-WATER-02, REQ-V2-CORE-04
// ============================================================================
using System;
using HDBF.Core.Balance;

namespace HDBF.Core.Indoor
{
    /// <summary>
    /// Internal-tank water mathematics (INTERFACES section 5). The tank is the only water buffer:
    /// the daily tick deducts once, and only a real <see cref="IWaterSource"/> may add water
    /// (REQ-V2-WATER-02). All functions are pure and safe for zero/negative/NaN inputs.
    /// </summary>
    public static class IndoorWaterMath
    {
        /// <summary>
        /// Daily demand: <c>activeSlots x WaterPerSlotPerDay x modeWaterFactor</c> (section 19.6).
        /// </summary>
        /// <param name="activeSlots">Effective (P_eff) slots fed by this tank.</param>
        /// <param name="waterPerSlotPerDay">Tier water consumption per slot and day.</param>
        /// <param name="mode">Operating mode (dense x1.5, fallow x0.3).</param>
        /// <returns>Water units per game day; 0 for invalid input.</returns>
        public static float DemandPerDay(int activeSlots, float waterPerSlotPerDay, IndoorMode mode)
        {
            if (activeSlots <= 0 || !IndoorMath.IsPositiveFinite(waterPerSlotPerDay))
            {
                return 0f;
            }

            var factor = ModeWaterFactor(mode);
            if (!(factor > 0f))
            {
                return 0f;
            }

            var demand = activeSlots * waterPerSlotPerDay * factor;
            return IndoorMath.IsFinite(demand) ? demand : 0f;
        }

        /// <summary>
        /// Coverage C = clamp(tank / demand, 0, 1). A zero (or negative) demand means "nothing to
        /// water", so coverage is 1 and no division happens (REQ-V2-WATER-01).
        /// </summary>
        /// <param name="tank">Current tank content in water units.</param>
        /// <param name="demandPerDay">Daily demand from <see cref="DemandPerDay"/>.</param>
        /// <returns>Coverage in [0,1]; 1 when demand is 0, 0 for NaN/negative tanks.</returns>
        public static float Coverage(float tank, float demandPerDay)
        {
            if (float.IsNaN(demandPerDay))
            {
                return 0f;
            }

            if (demandPerDay <= 0f)
            {
                return 1f;
            }

            if (!IndoorMath.IsFinite(tank) || tank <= 0f)
            {
                return 0f;
            }

            var coverage = tank / demandPerDay;
            return coverage >= 1f ? 1f : coverage;
        }

        /// <summary>
        /// G_irr = clamp((C - 0.25 x C_min) / (0.75 x C_min), 0, 1); 0 below 0.25 x C_min and 1 at
        /// or above C_min (section 19.6). A tier without a minimum coverage is always irrigated.
        /// </summary>
        /// <param name="coverage">Tank coverage C from <see cref="Coverage"/>.</param>
        /// <param name="minCoverage">Tier minimum coverage C_min.</param>
        /// <returns>Irrigation growth multiplier in [0,1].</returns>
        public static float IrrigationMultiplier(float coverage, float minCoverage)
        {
            if (float.IsNaN(minCoverage) || minCoverage <= 0f)
            {
                return 1f;
            }

            var cmin = minCoverage > 1f ? 1f : minCoverage;
            if (float.IsNaN(coverage) || coverage <= 0f)
            {
                return 0f;
            }

            var zeroPoint = IndoorConstants.IrrigationZeroPointRatio * cmin;
            if (coverage <= zeroPoint)
            {
                return 0f;
            }

            if (coverage >= cmin)
            {
                return 1f;
            }

            var span = IndoorConstants.IrrigationRampSpanRatio * cmin;
            if (span <= 0f)
            {
                return 1f;
            }

            return IndoorMath.Clamp01((coverage - zeroPoint) / span);
        }

        /// <summary>
        /// Settles one game day of water: deducts the daily demand exactly once, drains the tank
        /// when it cannot cover the demand, and returns the coverage C (REQ-V2-WATER-02).
        /// </summary>
        /// <param name="record">Farm record whose tank is deducted in place.</param>
        /// <param name="tier">Tier profile that supplies the per-slot rate and tank capacity.</param>
        /// <param name="activeSlots">Effective slots fed by the tank this day.</param>
        /// <returns>Coverage C in [0,1]; 1 when no water is needed.</returns>
        public static float SettleDay(IndoorFarmRecord record, TierProfile tier, int activeSlots)
        {
            if (record is null || tier is null)
            {
                return 0f;
            }

            var capacity = tier.TankCapacity;
            var tank = SanitizeTank(record.Tank, capacity);
            var demand = DemandPerDay(activeSlots, tier.WaterPerSlotPerDay, record.Mode);
            if (demand <= 0f)
            {
                record.Tank = tank;
                return 1f;
            }

            if (tank <= 0f)
            {
                record.Tank = 0f;
                return 0f;
            }

            if (tank >= demand)
            {
                record.Tank = tank - demand;
                return 1f;
            }

            var coverage = IndoorMath.Clamp01(tank / demand);
            record.Tank = 0f;
            return coverage;
        }

        /// <summary>Water added by one manual refill trip: TankCapacity / 3 (section 19.6).</summary>
        /// <param name="tankCapacity">Tier tank capacity in water units.</param>
        /// <returns>Refill amount; 0 for an invalid capacity.</returns>
        public static float RefillAmount(float tankCapacity)
        {
            if (!IndoorMath.IsPositiveFinite(tankCapacity))
            {
                return 0f;
            }

            return tankCapacity * IndoorConstants.ManualRefillTankFraction;
        }

        /// <summary>
        /// Adds water to the tank, clamped to the tier tank capacity. Used by the pipe and manual
        /// refill paths; it never creates water on its own (the caller already drew it from a real
        /// <see cref="IWaterSource"/>).
        /// </summary>
        /// <param name="record">Farm record whose tank is increased in place.</param>
        /// <param name="amount">Drawn water amount; negative/NaN is ignored.</param>
        /// <returns>The amount actually added to the tank.</returns>
        public static float TryRefill(IndoorFarmRecord record, float amount)
        {
            if (record is null || !IndoorMath.IsPositiveFinite(amount))
            {
                return 0f;
            }

            var capacity = CapacityOf(record.Tier);
            var tank = SanitizeTank(record.Tank, capacity);
            if (capacity <= 0f || tank >= capacity)
            {
                record.Tank = tank;
                return 0f;
            }

            var headroom = capacity - tank;
            var added = amount < headroom ? amount : headroom;
            record.Tank = tank + added;
            return added;
        }

        /// <summary>
        /// Display helper: how many full-load days the tank still supports. A zero demand returns
        /// <see cref="float.PositiveInfinity"/> so the panel can render the "—" placeholder.
        /// </summary>
        /// <param name="tank">Current tank content.</param>
        /// <param name="demandPerDay">Daily demand.</param>
        /// <returns>Days remaining, positive infinity for zero demand, 0 for an empty tank.</returns>
        public static float DaysRemaining(float tank, float demandPerDay)
        {
            if (!IndoorMath.IsPositiveFinite(demandPerDay))
            {
                return float.PositiveInfinity;
            }

            if (!IndoorMath.IsFinite(tank) || tank <= 0f)
            {
                return 0f;
            }

            return tank / demandPerDay;
        }

        /// <summary>Mode water factor: normal 1.00, dense 1.50, fallow 0.30 (section 19.5).</summary>
        internal static float ModeWaterFactor(IndoorMode mode)
        {
            switch (mode)
            {
                case IndoorMode.Dense:
                    return IndoorConstants.DenseWaterMultiplier;
                case IndoorMode.Fallow:
                    return IndoorConstants.FallowWaterMultiplier;
                default:
                    return IndoorConstants.NormalModeMultiplier;
            }
        }

        /// <summary>Clamps a persisted tank value into [0, capacity]; NaN/infinity become 0.</summary>
        internal static float SanitizeTank(float tank, float capacity)
        {
            if (!IndoorMath.IsFinite(tank) || tank <= 0f)
            {
                return 0f;
            }

            if (capacity > 0f && tank > capacity)
            {
                return capacity;
            }

            return tank;
        }

        private static float CapacityOf(HdbfTier tier)
        {
            var index = (int)tier;
            if (index < 0 || index >= TierCatalog.All.Count)
            {
                return 0f;
            }

            return TierCatalog.Get(tier).TankCapacity;
        }

        /// <summary>
        /// Draws from a water source for the daily tick: null, unavailable and throwing sources are
        /// a failed draw (no water moves), never an exception (REQ-V2-WATER-02, REQ-V2-SAFE-04).
        /// </summary>
        internal static bool TryDrawSource(IWaterSource? source, float amount, out float drawn)
        {
            drawn = 0f;
            if (source is null || !(amount > 0f))
            {
                return false;
            }

            try
            {
                if (!source.IsAvailable)
                {
                    return false;
                }

                return source.TryDraw(amount, out drawn);
            }
            catch
            {
                drawn = 0f;
                return false;
            }
        }
    }
}
