// ============================================================================
// HDBF v2 | Layer: Core/Indoor | Owner: V2-CORE-01
// Purpose: Continuous water drain of the internal tank (player request 2026-10-28): turns the
//          elapsed game time into a water slice and drains it, replacing the one-daily-deduction
//          settlement. Split from IndoorWaterMath.cs for the 300-line soft gate.
// Requirements: REQ-V2-WATER-01, REQ-V2-WATER-02, REQ-V2-PERF-02
// ============================================================================
using HDBF.Core.Balance;

namespace HDBF.Core.Indoor
{
    public static partial class IndoorWaterMath
    {
        /// <summary>
        /// Water slice consumed by one elapsed fraction of a game day. The game layer calls this
        /// every frame with the elapsed game-day delta, which makes the drain continuous and still
        /// frame-rate/speed independent: pausing advances nothing, and one game day consumes exactly
        /// <see cref="DemandPerDay"/> (player request 2026-10-28, replacing the daily deduction).
        /// </summary>
        /// <param name="activeSlots">Effective (P_eff) slots fed by this tank.</param>
        /// <param name="waterPerSlotPerDay">Tier water consumption per slot and day.</param>
        /// <param name="mode">Operating mode (dense x1.5, fallow x0.3).</param>
        /// <param name="elapsedGameDays">Game days elapsed since the previous call; 0/negative/NaN drain nothing.</param>
        /// <returns>Water units to consume; 0 for invalid input.</returns>
        public static float DemandForElapsedDays(int activeSlots, float waterPerSlotPerDay, IndoorMode mode,
            float elapsedGameDays)
        {
            var demand = DemandPerDay(activeSlots, waterPerSlotPerDay, mode);
            if (!(demand > 0f) || !IndoorMath.IsPositiveFinite(elapsedGameDays))
            {
                return 0f;
            }

            var slice = demand * elapsedGameDays;
            return IndoorMath.IsFinite(slice) ? slice : 0f;
        }

        /// <summary>
        /// Repairs a persisted tank value in place: NaN/infinity/negative become 0 and anything above
        /// the tier capacity is clamped. The continuous drain calls this every frame, and the daily
        /// tick keeps calling it so a farm loaded from a damaged save is safe before the first frame.
        /// </summary>
        /// <param name="record">Farm record whose tank is repaired in place.</param>
        /// <param name="tier">Tier profile that supplies the tank capacity.</param>
        public static void SanitizeTank(IndoorFarmRecord record, TierProfile tier)
        {
            if (record is null || tier is null)
            {
                return;
            }

            record.Tank = SanitizeTank(record.Tank, tier.TankCapacity);
        }

        /// <summary>
        /// Drains one continuous slice from the tank (player request 2026-10-28: per-second
        /// consumption instead of one daily deduction). The tank never goes negative and a bad
        /// persisted value is repaired even when the slice is invalid. The caller already owns the
        /// "real source" rules, so this helper only moves the water a source/pump/pipe provided.
        /// </summary>
        /// <param name="record">Farm record whose tank is drained in place.</param>
        /// <param name="tier">Tier profile that supplies the tank capacity.</param>
        /// <param name="amount">Water units to drain; non-positive/NaN drains nothing.</param>
        /// <returns>Water units actually drained (clamped to the tank content).</returns>
        public static float Consume(IndoorFarmRecord record, TierProfile tier, float amount)
        {
            if (record is null || tier is null)
            {
                return 0f;
            }

            var tank = SanitizeTank(record.Tank, tier.TankCapacity);
            if (!IndoorMath.IsPositiveFinite(amount))
            {
                record.Tank = tank;
                return 0f;
            }

            var consumed = amount < tank ? amount : tank;
            if (!(consumed > 0f))
            {
                record.Tank = tank;
                return 0f;
            }

            record.Tank = tank - consumed;
            return consumed;
        }
    }
}
