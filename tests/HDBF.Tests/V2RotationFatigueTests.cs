// ============================================================================
// HDBF v2 | Layer: L1 test | Owner: V2-CORE-01
// Purpose: Rotation, fatigue cap/recovery, fallow recovery and determinism of the
//          indoor farm (section 19.5 / section 19.14).
// Requirements: REQ-V2-CORE-04, REQ-V2-SAVE-03, REQ-V2-TEST-01
// ============================================================================
using System;
using HDBF.Core;
using HDBF.Core.Balance;
using HDBF.Core.Indoor;

namespace HDBF.Tests
{
    public static class V2RotationFatigueTests
    {
        // ---------------------------------------------------------------- fatigue

        public static void TestSameCropHarvestGainsTierFatigue()
        {
            var record = Record(HdbfTier.Basic);
            Mature(record, 0, "Carrot", "Carrot", 0f);
            Harvest(record, HdbfTier.Basic);
            TestAssert.Equal(0.08f, record.Slots[0].Fatigue, 1e-5f);
        }

        public static void TestTopTierFatigueFactorIsOneAndHalf()
        {
            var record = Record(HdbfTier.Top);
            Mature(record, 0, "Carrot", "Carrot", 0f);
            Harvest(record, HdbfTier.Top);
            TestAssert.Equal(0.12f, record.Slots[0].Fatigue, 1e-5f);
        }

        public static void TestRotatedHarvestGetsReducedDeltaAndRecovery()
        {
            var record = Record(HdbfTier.Basic);
            Mature(record, 0, "Carrot", "Potato", 0.5f);
            Harvest(record, HdbfTier.Basic);
            // 0.5 + 0.08*0.40 - 0.05 = 0.482
            TestAssert.Equal(0.482f, record.Slots[0].Fatigue, 1e-5f);
        }

        public static void TestRotationRecoveryFloorsAtZero()
        {
            var record = Record(HdbfTier.Basic);
            Mature(record, 0, "Carrot", "Potato", 0.01f);
            Harvest(record, HdbfTier.Basic);
            TestAssert.Equal(0f, record.Slots[0].Fatigue);
        }

        public static void TestFatigueCapsAtOne()
        {
            var record = Record(HdbfTier.Top);
            Mature(record, 0, "Carrot", "Carrot", 0.95f);
            Harvest(record, HdbfTier.Top);
            TestAssert.Equal(1f, record.Slots[0].Fatigue);
        }

        public static void TestDenseFatigueMultiplier()
        {
            var record = Record(HdbfTier.Basic, IndoorMode.Dense);
            Mature(record, 0, "Carrot", "Carrot", 0f);
            Harvest(record, HdbfTier.Basic);
            TestAssert.Equal(0.12f, record.Slots[0].Fatigue, 1e-5f);
        }

        public static void TestFallowRecoveryPerDay()
        {
            var record = Record(HdbfTier.Basic, IndoorMode.Fallow);
            record.Slots[3].Fatigue = 0.5f;
            var result = Tick(record, HdbfTier.Basic, Workers(0, 1f));
            TestAssert.Equal(0.47f, record.Slots[3].Fatigue, 1e-5f);
            TestAssert.True(result.Changed, "fallow recovery is a persisted change");
        }

        public static void TestFallowRecoveryReachesZero()
        {
            var record = Record(HdbfTier.Basic, IndoorMode.Fallow);
            record.Slots[3].Fatigue = 0.02f;
            Tick(record, HdbfTier.Basic, Workers(0, 1f));
            TestAssert.Equal(0f, record.Slots[3].Fatigue);
        }

        // ---------------------------------------------------------------- rotation

        public static void TestLastCropRecordedAndSlotCleared()
        {
            var record = Record(HdbfTier.Basic);
            record.CropPolicy = CropPolicy.Fixed;
            record.FixedCropId = string.Empty; // keep the planting pass from refilling the slot
            Mature(record, 0, "Carrot", string.Empty, 0f);
            Harvest(record, HdbfTier.Basic);
            var slot = record.Slots[0];
            TestAssert.Equal(string.Empty, slot.CropId);
            TestAssert.Equal("Carrot", slot.LastCropId);
            TestAssert.Equal(SlotStage.Empty, slot.Stage);
            TestAssert.Equal(0f, slot.GrowthDays);
        }

        public static void TestRotationPlantsNextCropAfterPrevious()
        {
            var record = Record(HdbfTier.Basic);
            Mature(record, 0, "Carrot", string.Empty, 0f);
            Harvest(record, HdbfTier.Basic);
            // Harvest + planting happen in the same RunActions pass; the lowest empty slot is 0.
            TestAssert.Equal("Potato", record.Slots[0].CropId);
            TestAssert.Equal("Carrot", record.Slots[0].LastCropId);
            TestAssert.Equal(SlotStage.Growing, record.Slots[0].Stage);
            TestAssert.Equal(0f, record.Slots[0].GrowthDays);
        }

        public static void TestRotationFollowsCatalogOrderAndWraps()
        {
            var cases = new[] { ("Carrot", "Potato"), ("Potato", "Wheat"), ("Wheat", "Sunflower"), ("Sunflower", "Carrot") };
            foreach (var pair in cases)
            {
                var record = Record(HdbfTier.Basic);
                Mature(record, 0, pair.Item1, string.Empty, 0f);
                Harvest(record, HdbfTier.Basic);
                TestAssert.Equal(pair.Item1, record.Slots[0].LastCropId);
                TestAssert.Equal(pair.Item2, record.Slots[0].CropId);
            }
        }

        public static void TestFixedPolicyPlantsFixedCrop()
        {
            var record = Record(HdbfTier.Basic);
            record.CropPolicy = CropPolicy.Fixed;
            record.FixedCropId = "Wheat";
            Harvest(record, HdbfTier.Basic);
            TestAssert.Equal("Wheat", record.Slots[0].CropId);
        }

        public static void TestFixedPolicyEmptyCropPlantsNothing()
        {
            var record = Record(HdbfTier.Basic);
            record.CropPolicy = CropPolicy.Fixed;
            record.FixedCropId = string.Empty;
            Harvest(record, HdbfTier.Basic);
            TestAssert.Equal(SlotStage.Empty, record.Slots[0].Stage);
        }

        public static void TestFixedPolicyInvalidCropPlantsNothing()
        {
            var record = Record(HdbfTier.Basic);
            record.CropPolicy = CropPolicy.Fixed;
            record.FixedCropId = "Bogus";
            Harvest(record, HdbfTier.Basic);
            TestAssert.Equal(SlotStage.Empty, record.Slots[0].Stage);
        }

        // ---------------------------------------------------------------- determinism

        public static void TestSameInputProducesSameOutputOverManyDays()
        {
            var left = Scenario();
            var right = Scenario();
            for (var day = 0; day < 30; day++)
            {
                var leftContext = new IndoorDayContext
                {
                    ActiveWorkers = 2,
                    CurrentDay = day,
                    Output = new AcceptAllSink(),
                    Water = new InfiniteWater(),
                    HasPipeWater = true,
                };
                var rightContext = new IndoorDayContext
                {
                    ActiveWorkers = 2,
                    CurrentDay = day,
                    Output = new AcceptAllSink(),
                    Water = new InfiniteWater(),
                    HasPipeWater = true,
                };
                var leftResult = Tick(left, HdbfTier.Basic, leftContext);
                var rightResult = Tick(right, HdbfTier.Basic, rightContext);
                TestAssert.Equal(leftResult.GrowthMultiplier, rightResult.GrowthMultiplier);
                TestAssert.Equal(leftResult.Coverage, rightResult.Coverage);
                TestAssert.Equal(leftResult.YieldMultiplier, rightResult.YieldMultiplier);
                TestAssert.Equal(leftResult.Actions.Harvested, rightResult.Actions.Harvested);
                TestAssert.Equal(leftResult.Actions.Planted, rightResult.Actions.Planted);
                TestAssert.Equal(leftResult.Actions.ActionsUsed, rightResult.Actions.ActionsUsed);
                TestAssert.Equal(leftResult.Changed, rightResult.Changed);
                TestAssert.Equal(leftResult.Warnings, rightResult.Warnings);
            }

            TestAssert.Equal(left.Tank, right.Tank);
            for (var index = 0; index < left.Slots.Length; index++)
            {
                TestAssert.Equal(left.Slots[index].CropId, right.Slots[index].CropId);
                TestAssert.Equal(left.Slots[index].LastCropId, right.Slots[index].LastCropId);
                TestAssert.Equal(left.Slots[index].GrowthDays, right.Slots[index].GrowthDays);
                TestAssert.Equal(left.Slots[index].Fatigue, right.Slots[index].Fatigue);
                TestAssert.Equal((int)left.Slots[index].Stage, (int)right.Slots[index].Stage);
            }
        }

        public static void TestFatigueOnlyAffectsYieldNotGrowth()
        {
            var fresh = new IndoorSlot { CropId = "Carrot", Stage = SlotStage.Growing, GrowthDays = 0f };
            var tired = new IndoorSlot { CropId = "Carrot", Stage = SlotStage.Growing, GrowthDays = 0f, Fatigue = 1f };
            var crop = new CropDefinition("Carrot", 4f, "Carrot", 3);
            IndoorGrowthMath.Advance(fresh, crop, 1f, 1f);
            IndoorGrowthMath.Advance(tired, crop, 1f, 1f);
            TestAssert.Equal(fresh.GrowthDays, tired.GrowthDays, "fatigue must not change growth speed");
            TestAssert.Equal(3f, IndoorGrowthMath.Yield(3, fresh.Fatigue, IndoorMode.Normal), 1e-5f);
            TestAssert.Equal(1.2f, IndoorGrowthMath.Yield(3, tired.Fatigue, IndoorMode.Normal), 1e-5f);
        }

        // ---------------------------------------------------------------- helpers

        private sealed class AcceptAllSink : IInventorySink
        {
            public bool TryAdd(string goodId, int amount) => true;
        }

        private sealed class InfiniteWater : IWaterSource
        {
            public WaterSupplyKind Kind => WaterSupplyKind.Pipe;
            public bool IsAvailable => true;
            public bool TryDraw(float amount, out float actual)
            {
                actual = amount;
                return true;
            }
        }

        private static IndoorFarmRecord Record(HdbfTier tier, IndoorMode mode = IndoorMode.Normal)
        {
            var record = IndoorFarmRecord.Create(tier, HdbfFaction.Folktails);
            record.Mode = mode;
            return record;
        }

        private static IndoorFarmRecord Scenario()
        {
            var record = Record(HdbfTier.Basic);
            for (var index = 0; index < 20; index++)
            {
                record.Slots[index].CropId = index % 2 == 0 ? "Carrot" : "Potato";
                record.Slots[index].Stage = SlotStage.Growing;
                record.Slots[index].GrowthDays = 0.5f;
            }

            return record;
        }

        private static IndoorDayContext Workers(int workers, float day)
        {
            return new IndoorDayContext { ActiveWorkers = workers, CurrentDay = day };
        }

        private static IndoorActionStats Harvest(IndoorFarmRecord record, HdbfTier tier)
        {
            return IndoorLaborMath.RunActions(record, TierCatalog.Get(tier), FallbackCropCatalog.Default,
                1, new AcceptAllSink(), true);
        }

        private static IndoorDayResult Tick(IndoorFarmRecord record, HdbfTier tier, IndoorDayContext context)
        {
            return IndoorDayTick.Run(record, TierCatalog.Get(tier), FallbackCropCatalog.Default, context);
        }

        private static void Mature(IndoorFarmRecord record, int slot, string cropId, string lastCropId, float fatigue)
        {
            var target = record.Slots[slot];
            target.CropId = cropId;
            target.LastCropId = lastCropId;
            target.Fatigue = fatigue;
            target.GrowthDays = 4f;
            target.Stage = SlotStage.Mature;
        }
    }
}
