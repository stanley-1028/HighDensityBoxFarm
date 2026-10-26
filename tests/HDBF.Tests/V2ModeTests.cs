// ============================================================================
// HDBF v2 | Layer: L1 test | Owner: V2-CORE-01
// Purpose: Normal/dense/fallow multipliers (yield, water, work, fatigue) and the
//          fallow behaviour of the daily tick (section 19.5 / section 19.14).
// Requirements: REQ-V2-CORE-04, REQ-V2-BAL-01, REQ-V2-TEST-01
// ============================================================================
using HDBF.Core;
using HDBF.Core.Balance;
using HDBF.Core.Indoor;

namespace HDBF.Tests
{
    public static class V2ModeTests
    {
        public static void TestNormalModeFactors()
        {
            TestAssert.Equal(1f, IndoorGrowthMath.ModeYieldMultiplier(IndoorMode.Normal));
            TestAssert.Equal(0.08f, IndoorLaborMath.WorkDaysPerAction(IndoorMode.Normal), 1e-6f);
        }

        public static void TestDenseYieldMultiplier()
        {
            TestAssert.Equal(1.35f, IndoorGrowthMath.ModeYieldMultiplier(IndoorMode.Dense), 1e-5f);
            TestAssert.Equal(4, IndoorGrowthMath.HarvestItems(3, 0f, IndoorMode.Dense));
        }

        public static void TestFallowYieldMultiplierIsZero()
        {
            TestAssert.Equal(0f, IndoorGrowthMath.ModeYieldMultiplier(IndoorMode.Fallow));
            TestAssert.Equal(0f, IndoorGrowthMath.Yield(3, 0f, IndoorMode.Fallow));
        }

        public static void TestDenseWaterMultiplier()
        {
            var normal = IndoorWaterMath.DemandPerDay(10, 0.02f, IndoorMode.Normal);
            var dense = IndoorWaterMath.DemandPerDay(10, 0.02f, IndoorMode.Dense);
            TestAssert.Equal(0.2f, normal, 1e-5f);
            TestAssert.Equal(0.3f, dense, 1e-5f);
        }

        public static void TestFallowWaterMultiplier()
        {
            var fallow = IndoorWaterMath.DemandPerDay(10, 0.02f, IndoorMode.Fallow);
            TestAssert.Equal(0.06f, fallow, 1e-5f);
        }

        public static void TestDenseWorkMultiplier()
        {
            TestAssert.Equal(0.096f, IndoorLaborMath.WorkDaysPerAction(IndoorMode.Dense), 1e-6f);
        }

        public static void TestFallowWorkMultiplierIsNormal()
        {
            // v2 fallow mode has no work discount (the v1 0.2 fallow work factor is obsolete).
            TestAssert.Equal(0.08f, IndoorLaborMath.WorkDaysPerAction(IndoorMode.Fallow), 1e-6f);
        }

        public static void TestDenseFatigueMultiplier()
        {
            var record = Record(IndoorMode.Dense);
            var slot = record.Slots[0];
            slot.CropId = "Carrot";
            slot.LastCropId = "Carrot";
            slot.GrowthDays = 4f;
            slot.Stage = SlotStage.Mature;
            IndoorLaborMath.RunActions(record, TierCatalog.Get(HdbfTier.Basic), FallbackCropCatalog.Default,
                1, new AcceptAllSink(), true);
            TestAssert.Equal(0.12f, slot.Fatigue, 1e-5f, "0.08 x dense 1.5");
        }

        public static void TestFallowStopsActionsButNotGrowth()
        {
            var record = Record(IndoorMode.Fallow);
            record.Slots[0].CropId = "Carrot";
            record.Slots[0].Stage = SlotStage.Mature;
            record.Slots[0].GrowthDays = 4f;
            record.Slots[1].CropId = "Carrot";
            record.Slots[1].Stage = SlotStage.Growing;
            record.Slots[1].GrowthDays = 0f;

            var result = Tick(record, new IndoorDayContext { ActiveWorkers = 2, CurrentDay = 1f });
            TestAssert.Equal(0, result.Actions.Harvested, "fallow never harvests");
            TestAssert.Equal(0, result.Actions.Planted, "fallow never plants");
            TestAssert.Equal(SlotStage.Mature, record.Slots[0].Stage);
            TestAssert.True(record.Slots[1].GrowthDays > 0f, "fallow keeps growing");
        }

        public static void TestDenseModeHarvestYield()
        {
            var record = Record(IndoorMode.Dense);
            record.Slots[0].CropId = "Carrot";
            record.Slots[0].Stage = SlotStage.Mature;
            record.Slots[0].GrowthDays = 4f;
            var sink = new RecordingSink();
            var result = Tick(record, new IndoorDayContext { ActiveWorkers = 2, CurrentDay = 1f, Output = sink });
            TestAssert.Equal(1, result.Actions.Harvested);
            TestAssert.Equal(4, sink.Total, "3 x 1.35 rounds to 4");
        }

        public static void TestFallowRecoveryRunsInTick()
        {
            var record = Record(IndoorMode.Fallow);
            record.Slots[0].Fatigue = 0.5f;
            Tick(record, new IndoorDayContext { ActiveWorkers = 0, CurrentDay = 1f });
            TestAssert.Equal(0.47f, record.Slots[0].Fatigue, 1e-5f);
        }

        public static void TestModeConstantsMatchV2Document()
        {
            TestAssert.Equal(1.35f, IndoorConstants.DenseYieldMultiplier, 1e-6f);
            TestAssert.Equal(1.50f, IndoorConstants.DenseWaterMultiplier, 1e-6f);
            TestAssert.Equal(1.50f, IndoorConstants.DenseFatigueMultiplier, 1e-6f);
            TestAssert.Equal(1.20f, IndoorConstants.DenseWorkMultiplier, 1e-6f);
            TestAssert.Equal(0.30f, IndoorConstants.FallowWaterMultiplier, 1e-6f);
            TestAssert.Equal(0.03f, IndoorConstants.FallowRecoveryPerDay, 1e-6f);
        }

        public static void TestModeConstantsMatchTierCatalogPendants()
        {
            // v2 must not silently drift from the v1 tier data until the v1 path is deleted.
            TestAssert.Equal(TierCatalog.DenseYieldMultiplier, IndoorConstants.DenseYieldMultiplier, 1e-6f);
            TestAssert.Equal(TierCatalog.DenseWaterMultiplier, IndoorConstants.DenseWaterMultiplier, 1e-6f);
            TestAssert.Equal(TierCatalog.DenseFatigueMultiplier, IndoorConstants.DenseFatigueMultiplier, 1e-6f);
            TestAssert.Equal(TierCatalog.DenseWorkMultiplier, IndoorConstants.DenseWorkMultiplier, 1e-6f);
            TestAssert.Equal(TierCatalog.FallowWaterMultiplier, IndoorConstants.FallowWaterMultiplier, 1e-6f);
            TestAssert.Equal(TierCatalog.FallowRecoveryPerDay, IndoorConstants.FallowRecoveryPerDay, 1e-6f);
        }

        public static void TestModeTransitionConstantIsReserved()
        {
            // Mode transition state is not part of the frozen v2 record; the constant is reserved
            // for the gameplay card that persists the transition day.
            TestAssert.Equal(1f, IndoorConstants.ModeTransitionDays, 1e-6f);
        }

        // ---------------------------------------------------------------- helpers

        private sealed class AcceptAllSink : IInventorySink
        {
            public bool TryAdd(string goodId, int amount) => true;
        }

        private sealed class RecordingSink : IInventorySink
        {
            public int Total;
            public bool TryAdd(string goodId, int amount)
            {
                Total += amount;
                return true;
            }
        }

        private static IndoorFarmRecord Record(IndoorMode mode)
        {
            var record = IndoorFarmRecord.Create(HdbfTier.Basic, HdbfFaction.Folktails);
            record.Mode = mode;
            return record;
        }

        private static IndoorDayResult Tick(IndoorFarmRecord record, IndoorDayContext context)
        {
            return IndoorDayTick.Run(record, TierCatalog.Get(HdbfTier.Basic), FallbackCropCatalog.Default, context);
        }
    }
}
