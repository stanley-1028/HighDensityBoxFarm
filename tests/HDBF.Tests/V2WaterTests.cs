// ============================================================================
// HDBF v2 | Layer: L1 test | Owner: V2-CORE-01
// Purpose: Tank demand, coverage, G_irr, continuous per-elapsed-time drain, pipe/manual refill
//          and the divide-by-zero/negative guards (section 19.6 / section 19.14).
// Requirements: REQ-V2-WATER-01, REQ-V2-WATER-02, REQ-V2-WATER-04, REQ-V2-TEST-01
// ============================================================================
using System;
using HDBF.Core;
using HDBF.Core.Balance;
using HDBF.Core.Indoor;

namespace HDBF.Tests
{
    public static class V2WaterTests
    {
        // ---------------------------------------------------------------- demand / coverage

        public static void TestDemandPerDay()
        {
            TestAssert.Equal(0.76f, IndoorWaterMath.DemandPerDay(38, 0.02f, IndoorMode.Normal), 1e-5f);
        }

        public static void TestDemandPerDayZeroSlots()
        {
            TestAssert.Equal(0f, IndoorWaterMath.DemandPerDay(0, 0.02f, IndoorMode.Normal));
            TestAssert.Equal(0f, IndoorWaterMath.DemandPerDay(-5, 0.02f, IndoorMode.Normal));
        }

        public static void TestDemandPerDayInvalidRate()
        {
            TestAssert.Equal(0f, IndoorWaterMath.DemandPerDay(10, 0f, IndoorMode.Normal));
            TestAssert.Equal(0f, IndoorWaterMath.DemandPerDay(10, -1f, IndoorMode.Normal));
            TestAssert.Equal(0f, IndoorWaterMath.DemandPerDay(10, float.NaN, IndoorMode.Normal));
        }

        public static void TestCoverageFullTank()
        {
            TestAssert.Equal(1f, IndoorWaterMath.Coverage(10f, 5f));
        }

        public static void TestCoveragePartial()
        {
            TestAssert.Equal(0.5f, IndoorWaterMath.Coverage(2.5f, 5f), 1e-5f);
        }

        public static void TestCoverageZeroDemandIsOne()
        {
            TestAssert.Equal(1f, IndoorWaterMath.Coverage(0f, 0f), "demand 0 must not divide");
            TestAssert.Equal(1f, IndoorWaterMath.Coverage(-4f, 0f));
        }

        public static void TestCoverageInvalidInputs()
        {
            TestAssert.Equal(0f, IndoorWaterMath.Coverage(-3f, 5f));
            TestAssert.Equal(0f, IndoorWaterMath.Coverage(float.NaN, 5f));
            TestAssert.Equal(0f, IndoorWaterMath.Coverage(5f, float.NaN));
        }

        // ---------------------------------------------------------------- G_irr

        public static void TestIrrigationBelowZeroPointIsZero()
        {
            TestAssert.Equal(0f, IndoorWaterMath.IrrigationMultiplier(0.1f, 0.5f));
            TestAssert.Equal(0f, IndoorWaterMath.IrrigationMultiplier(0.125f, 0.5f));
        }

        public static void TestIrrigationAtMinimumIsOne()
        {
            TestAssert.Equal(1f, IndoorWaterMath.IrrigationMultiplier(0.5f, 0.5f));
            TestAssert.Equal(1f, IndoorWaterMath.IrrigationMultiplier(1f, 0.5f));
        }

        public static void TestIrrigationMidpoint()
        {
            // C = 0.25, C_min = 0.50: (0.25 - 0.125) / 0.375 = 1/3.
            TestAssert.Equal(1f / 3f, IndoorWaterMath.IrrigationMultiplier(0.25f, 0.5f), 1e-5f);
        }

        public static void TestIrrigationWithoutRequirementIsOne()
        {
            TestAssert.Equal(1f, IndoorWaterMath.IrrigationMultiplier(0f, 0f));
            TestAssert.Equal(1f, IndoorWaterMath.IrrigationMultiplier(0f, float.NaN));
        }

        public static void TestIrrigationNegativeCoverageIsZero()
        {
            TestAssert.Equal(0f, IndoorWaterMath.IrrigationMultiplier(-1f, 0.5f));
            TestAssert.Equal(0f, IndoorWaterMath.IrrigationMultiplier(float.NaN, 0.5f));
        }

        // ---------------------------------------------------------------- continuous drain

        public static void TestDemandForElapsedDaysSlicesTheDailyDemand()
        {
            // Basic with two farmers: 60 slots x 0.020 = 1.2 units per game day. A whole day is
            // exactly the daily demand; one 600th of a day (one second of a 600 s day) is a slice.
            TestAssert.Equal(1.2f, IndoorWaterMath.DemandForElapsedDays(60, 0.02f, IndoorMode.Normal, 1f), 1e-5f);
            TestAssert.Equal(0.002f,
                IndoorWaterMath.DemandForElapsedDays(60, 0.02f, IndoorMode.Normal, 1f / 600f), 1e-6f);
            TestAssert.Equal(0f, IndoorWaterMath.DemandForElapsedDays(60, 0.02f, IndoorMode.Normal, 0f));
            TestAssert.Equal(0f, IndoorWaterMath.DemandForElapsedDays(60, 0.02f, IndoorMode.Normal, -1f));
            TestAssert.Equal(0f, IndoorWaterMath.DemandForElapsedDays(60, 0.02f, IndoorMode.Normal, float.NaN));
            TestAssert.Equal(0f, IndoorWaterMath.DemandForElapsedDays(0, 0.02f, IndoorMode.Normal, 1f));
        }

        public static void TestConsumeDrainsSmoothlyInsteadOfOneDailyChunk()
        {
            var record = Record(HdbfTier.Basic);
            var tier = TierCatalog.Get(HdbfTier.Basic);
            // 100 small slices of the 1.2/day demand empty exactly 1.2 from the 3.6 tank.
            var slice = IndoorWaterMath.DemandForElapsedDays(60, 0.02f, IndoorMode.Normal, 1f) / 100f;
            var drained = 0f;
            for (var index = 0; index < 100; index++)
            {
                drained += IndoorWaterMath.Consume(record, tier, slice);
            }

            TestAssert.Equal(1.2f, drained, 1e-4f);
            TestAssert.Equal(2.4f, record.Tank, 1e-4f, "3.6 minus one continuous game day");
        }

        public static void TestConsumeClampsAtZeroAndSanitizes()
        {
            var record = Record(HdbfTier.Basic);
            var tier = TierCatalog.Get(HdbfTier.Basic);
            TestAssert.Equal(0.6f, IndoorWaterMath.Consume(record, tier, 0.6f), 1e-5f);
            TestAssert.Equal(3f, record.Tank, 1e-5f);
            TestAssert.Equal(3f, IndoorWaterMath.Consume(record, tier, 100f), 1e-5f, "the tank never goes negative");
            TestAssert.Equal(0f, record.Tank);
            TestAssert.Equal(0f, IndoorWaterMath.Consume(record, tier, 0.5f));
            record.Tank = float.NaN;
            IndoorWaterMath.Consume(record, tier, 0f);
            TestAssert.Equal(0f, record.Tank);
            record.Tank = -5f;
            IndoorWaterMath.Consume(record, tier, 0f);
            TestAssert.Equal(0f, record.Tank);
            record.Tank = 100f;
            IndoorWaterMath.Consume(record, tier, 0f);
            TestAssert.Equal(3.6f, record.Tank, 1e-5f, "tank must be clamped to the tier capacity");
        }

        public static void TestConsumeIgnoresInvalidAmount()
        {
            var record = Record(HdbfTier.Basic);
            var tier = TierCatalog.Get(HdbfTier.Basic);
            TestAssert.Equal(0f, IndoorWaterMath.Consume(record, tier, 0f));
            TestAssert.Equal(0f, IndoorWaterMath.Consume(record, tier, -2f));
            TestAssert.Equal(0f, IndoorWaterMath.Consume(record, tier, float.NaN));
            TestAssert.Equal(3.6f, record.Tank);
        }

        public static void TestRefillAmountIsThirdOfCapacity()
        {
            TestAssert.Equal(25.6f, IndoorWaterMath.RefillAmount(76.8f), 1e-4f);
            TestAssert.Equal(0f, IndoorWaterMath.RefillAmount(0f));
            TestAssert.Equal(0f, IndoorWaterMath.RefillAmount(float.NaN));
        }

        public static void TestTryRefillClampsAtCapacity()
        {
            var record = Record(HdbfTier.Top);
            record.Tank = 70f;
            var added = IndoorWaterMath.TryRefill(record, 25f);
            TestAssert.Equal(6.8f, added, 1e-4f);
            TestAssert.Equal(76.8f, record.Tank, 1e-4f);
            TestAssert.Equal(0f, IndoorWaterMath.TryRefill(record, 5f));
        }

        public static void TestTryRefillIgnoresInvalidAmount()
        {
            var record = Record(HdbfTier.Basic);
            record.Tank = 1f;
            TestAssert.Equal(0f, IndoorWaterMath.TryRefill(record, 0f));
            TestAssert.Equal(0f, IndoorWaterMath.TryRefill(record, -2f));
            TestAssert.Equal(0f, IndoorWaterMath.TryRefill(record, float.NaN));
            TestAssert.Equal(1f, record.Tank);
        }

        public static void TestDaysRemaining()
        {
            TestAssert.Equal(3f, IndoorWaterMath.DaysRemaining(3.6f, 1.2f), 1e-5f);
            TestAssert.True(float.IsPositiveInfinity(IndoorWaterMath.DaysRemaining(3.6f, 0f)));
            TestAssert.Equal(0f, IndoorWaterMath.DaysRemaining(0f, 1.2f));
            TestAssert.Equal(0f, IndoorWaterMath.DaysRemaining(-1f, 1.2f));
        }

        // ---------------------------------------------------------------- daily tick refill

        public static void TestTickNoLongerDeductsWaterItself()
        {
            // Player request 2026-10-28: the game layer drains the tank continuously, so the daily
            // tick must only price the day from the live tank and never subtract the demand again.
            var record = Record(HdbfTier.Basic);
            var context = new IndoorDayContext { ActiveWorkers = 2, CurrentDay = 1f };
            var first = Tick(record, context);
            TestAssert.Equal(3.6f, record.Tank, 1e-5f, "the daily tick never deducts water");
            TestAssert.Equal(1f, first.Coverage, 1e-5f);
            var second = Tick(record, context);
            TestAssert.Equal(3.6f, record.Tank, 1e-5f);
            TestAssert.Equal(1f, second.Coverage, 1e-5f);
        }

        public static void TestTickPipeRefillKeepsCoverage()
        {
            var record = Record(HdbfTier.Basic);
            record.Tank = 2f;
            var source = new TestWater { Supply = 100f };
            var result = Tick(record, new IndoorDayContext
            {
                ActiveWorkers = 1,
                CurrentDay = 1f,
                HasPipeWater = true,
                Water = source,
            });
            // demand 38 x 0.02 = 0.76; headroom 1.6 so the pipe adds 0.76 and nothing is deducted.
            TestAssert.Equal(2.76f, record.Tank, 1e-4f);
            TestAssert.Equal(1f, result.Coverage, 1e-4f);
        }

        public static void TestTickPipeUnavailableWarnsNoWater()
        {
            var record = Record(HdbfTier.Basic);
            record.Tank = 0f;
            var result = Tick(record, new IndoorDayContext { ActiveWorkers = 1, CurrentDay = 1f, HasPipeWater = true });
            TestAssert.True(HasWarning(result.Warnings, IndoorWarningFlags.NoWater));
        }

        public static void TestTickManualRefillAddsThirdTank()
        {
            var record = Record(HdbfTier.Basic);
            record.Tank = 0f;
            var source = new TestWater { Supply = 100f };
            Tick(record, new IndoorDayContext
            {
                ActiveWorkers = 1,
                CurrentDay = 1f,
                ManualRefillRequested = true,
                Water = source,
            });
            // refill 3.6/3 = 1.2; the continuous layer owns the drain, so the tick adds 1.2 only.
            TestAssert.Equal(1.2f, record.Tank, 1e-4f);
        }

        public static void TestTickManualRefillWithoutSourceFails()
        {
            var record = Record(HdbfTier.Basic);
            record.Tank = 0f;
            var result = Tick(record, new IndoorDayContext
            {
                ActiveWorkers = 1,
                CurrentDay = 1f,
                ManualRefillRequested = true,
            });
            TestAssert.Equal(0f, record.Tank);
            TestAssert.True(HasWarning(result.Warnings, IndoorWarningFlags.NoWater));
        }

        public static void TestTickManualRefillWithoutWorkersFails()
        {
            var record = Record(HdbfTier.Basic);
            var source = new TestWater { Supply = 100f };
            var result = Tick(record, new IndoorDayContext
            {
                ActiveWorkers = 0,
                CurrentDay = 1f,
                ManualRefillRequested = true,
                Water = source,
            });
            TestAssert.Equal(3.6f, record.Tank, 1e-4f, "nobody can fetch water without workers");
            TestAssert.True(HasWarning(result.Warnings, IndoorWarningFlags.NoWater));
            TestAssert.Equal(100f, source.Supply, 1e-4f, "no water may be drawn");
        }

        public static void TestTickNoWorkersMeansNoDemand()
        {
            var record = Record(HdbfTier.Basic);
            var result = Tick(record, new IndoorDayContext { ActiveWorkers = 0, CurrentDay = 1f });
            TestAssert.Equal(3.6f, record.Tank, 1e-5f);
            TestAssert.Equal(1f, result.Coverage, 1e-5f);
        }

        // ---------------------------------------------------------------- helpers

        private sealed class TestWater : IWaterSource
        {
            public float Supply;
            public WaterSupplyKind Kind => WaterSupplyKind.Pipe;
            public bool IsAvailable => Supply > 0f;
            public bool TryDraw(float amount, out float actual)
            {
                actual = Math.Min(amount, Supply);
                Supply -= actual;
                return actual > 0f;
            }
        }

        private static bool HasWarning(IndoorWarningFlags flags, IndoorWarningFlags expected)
        {
            return (flags & expected) == expected;
        }

        private static IndoorFarmRecord Record(HdbfTier tier)
        {
            return IndoorFarmRecord.Create(tier, HdbfFaction.Folktails);
        }

        private static IndoorDayResult Tick(IndoorFarmRecord record, IndoorDayContext context)
        {
            return IndoorDayTick.Run(record, TierCatalog.Get(record.Tier), FallbackCropCatalog.Default, context);
        }
    }
}
