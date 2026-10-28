// ============================================================================
// HDBF v2 | Layer: L1 test | Owner: V2-CORE-01
// Purpose: P_eff truncation, action budget (workers x 13), harvest-before-plant
//          priority, output-full backlog and zero-worker behaviour (section 19.7).
// Requirements: REQ-V2-LABOR-01, REQ-V2-LABOR-02, REQ-V2-CORE-01, REQ-V2-TEST-01
// ============================================================================
using System;
using System.Collections.Generic;
using HDBF.Core;
using HDBF.Core.Balance;
using HDBF.Core.Indoor;

namespace HDBF.Tests
{
    public static class V2LaborTests
    {
        // ---------------------------------------------------------------- capacity / budget

        public static void TestEffectiveSlotsPerWorker()
        {
            TestAssert.Equal(38, IndoorLaborMath.EffectiveSlots(Basic(), 1));
            TestAssert.Equal(60, IndoorLaborMath.EffectiveSlots(Basic(), 2), "capacity binds at 2 workers");
            TestAssert.Equal(570, IndoorLaborMath.EffectiveSlots(Top(), 15));
            TestAssert.Equal(600, IndoorLaborMath.EffectiveSlots(Top(), 16));
        }

        public static void TestEffectiveSlotsWithoutWorkers()
        {
            TestAssert.Equal(0, IndoorLaborMath.EffectiveSlots(Basic(), 0));
            TestAssert.Equal(0, IndoorLaborMath.EffectiveSlots(Basic(), -3));
        }

        public static void TestActionBudgetIsThirteenPerWorker()
        {
            TestAssert.Equal(13, IndoorLaborMath.ActionBudget(1));
            TestAssert.Equal(26, IndoorLaborMath.ActionBudget(2));
            TestAssert.Equal(208, IndoorLaborMath.ActionBudget(16));
            TestAssert.Equal(0, IndoorLaborMath.ActionBudget(0));
            TestAssert.Equal(0, IndoorLaborMath.ActionBudget(-2));
        }

        public static void TestWorkDaysPerActionHasNoPathTerm()
        {
            TestAssert.Equal(0.08f, IndoorLaborMath.WorkDaysPerAction(IndoorMode.Normal), 1e-6f);
            TestAssert.Equal(0.08f, IndoorLaborMath.WorkDaysPerAction(IndoorMode.Fallow), 1e-6f);
            TestAssert.Equal(0.096f, IndoorLaborMath.WorkDaysPerAction(IndoorMode.Dense), 1e-6f);
        }

        // ---------------------------------------------------------------- priority / budget

        public static void TestHarvestPriorityConsumesBudgetBeforePlanting()
        {
            var record = Record(HdbfTier.Basic);
            for (var index = 0; index < 14; index++)
            {
                Mature(record, index, "Carrot");
            }

            var stats = Run(record, 1);
            TestAssert.Equal(13, stats.Harvested, "one worker performs exactly 13 actions");
            TestAssert.Equal(0, stats.Planted, "planting must wait until harvesting is done");
            TestAssert.Equal(13, stats.ActionsUsed);
        }

        public static void TestBudgetScalesWithWorkers()
        {
            var record = Record(HdbfTier.Basic);
            for (var index = 0; index < 30; index++)
            {
                Mature(record, index, "Carrot");
            }

            TestAssert.Equal(26, Run(record, 2).ActionsUsed);
        }

        public static void TestPlantFillsLowestEmptySlots()
        {
            var record = Record(HdbfTier.Basic);
            record.Slots[1].CropId = "Potato";
            record.Slots[1].Stage = SlotStage.Growing;
            var stats = Run(record, 1);
            TestAssert.Equal(13, stats.Planted);
            TestAssert.Equal(SlotStage.Growing, record.Slots[0].Stage, "slot 0 was planted first");
            TestAssert.Equal("Carrot", record.Slots[0].CropId);
            TestAssert.Equal("Potato", record.Slots[1].CropId, "an occupied slot is never overwritten");
            TestAssert.Equal(SlotStage.Empty, record.Slots[14].Stage, "the budget stops the seed pass");
        }

        public static void TestNoWorkersNoActions()
        {
            var record = Record(HdbfTier.Basic);
            Mature(record, 0, "Carrot");
            var stats = IndoorLaborMath.RunActions(record, Basic(), FallbackCropCatalog.Default, 0,
                new AcceptAllSink(), true);
            TestAssert.Equal(0, stats.ActionsUsed);
            TestAssert.Equal(0, stats.Harvested);
            TestAssert.Equal(0, stats.Planted);
            TestAssert.Equal(SlotStage.Mature, record.Slots[0].Stage);
        }

        public static void TestFarmActionsDisallowedPerformsNothing()
        {
            var record = Record(HdbfTier.Basic);
            Mature(record, 0, "Carrot");
            var stats = IndoorLaborMath.RunActions(record, Basic(), FallbackCropCatalog.Default, 2,
                new AcceptAllSink(), false);
            TestAssert.Equal(0, stats.ActionsUsed);
            TestAssert.Equal(SlotStage.Mature, record.Slots[0].Stage);
        }

        // ---------------------------------------------------------------- output full

        public static void TestOutputFullKeepsSlotsMatureAndReportsBacklog()
        {
            var record = Record(HdbfTier.Basic);
            Mature(record, 0, "Carrot");
            Mature(record, 1, "Carrot");
            var stats = IndoorLaborMath.RunActions(record, Basic(), FallbackCropCatalog.Default, 1,
                new FullSink(), true);
            TestAssert.Equal(0, stats.Harvested);
            TestAssert.Equal(2, stats.BlockedByOutput);
            TestAssert.True(stats.Backlog);
            TestAssert.Equal(SlotStage.Mature, record.Slots[0].Stage);
            TestAssert.Equal(SlotStage.Mature, record.Slots[1].Stage);
        }

        public static void TestOutputFullStillPlantsEmptySlots()
        {
            var record = Record(HdbfTier.Basic);
            Mature(record, 0, "Carrot");
            var stats = IndoorLaborMath.RunActions(record, Basic(), FallbackCropCatalog.Default, 1,
                new FullSink(), true);
            TestAssert.Equal(0, stats.Harvested);
            TestAssert.Equal(13, stats.Planted, "blocked harvests do not consume the action budget");
        }

        public static void TestNullOutputBlocksHarvest()
        {
            var record = Record(HdbfTier.Basic);
            Mature(record, 0, "Carrot");
            var stats = IndoorLaborMath.RunActions(record, Basic(), FallbackCropCatalog.Default, 1, null!, true);
            TestAssert.Equal(0, stats.Harvested);
            TestAssert.Equal(1, stats.BlockedByOutput);
            TestAssert.True(stats.Backlog);
        }

        // ---------------------------------------------------------------- P_eff truncation

        public static void TestHarvestOnlyWithinEffectiveSlots()
        {
            var record = Record(HdbfTier.Basic);
            Mature(record, 10, "Carrot");
            Mature(record, 50, "Carrot");
            var stats = Run(record, 1);
            TestAssert.Equal(1, stats.Harvested, "one worker schedules only the first 38 slots");
            TestAssert.Equal("Carrot", record.Slots[10].LastCropId, "slot 10 was harvested");
            TestAssert.Equal(SlotStage.Mature, record.Slots[50].Stage);
            TestAssert.Equal(string.Empty, record.Slots[50].LastCropId);
        }

        public static void TestPlantingStartsAtLowestSlotAndStopsAtBudget()
        {
            var record = Record(HdbfTier.Basic);
            var stats = Run(record, 1);
            TestAssert.Equal(13, stats.Planted);
            for (var index = 0; index < 13; index++)
            {
                TestAssert.Equal(SlotStage.Growing, record.Slots[index].Stage, "slot " + index + " planted");
            }

            TestAssert.Equal(SlotStage.Empty, record.Slots[13].Stage);
            TestAssert.Equal(SlotStage.Empty, record.Slots[59].Stage);
        }

        // ---------------------------------------------------------------- water-fed auto-fill (2026-10-28)

        public static void TestAutoFillPlantsEveryEmptyTendedSlotWithoutBudget()
        {
            var record = Record(HdbfTier.Basic);
            var stats = IndoorLaborMath.RunActions(record, Basic(), FallbackCropCatalog.Default, 1,
                new AcceptAllSink(), true, 0, 1f, waterAvailable: true);
            // One farmer tends min(60, 1 x 38) = 38 slots; all of them are planted in one pass.
            TestAssert.Equal(38, stats.Planted);
            TestAssert.Equal(0, stats.ActionsUsed, "auto-fill planting does not spend the daily budget");
            TestAssert.Equal(SlotStage.Growing, record.Slots[37].Stage);
            TestAssert.Equal(SlotStage.Empty, record.Slots[38].Stage, "slots beyond P_eff stay untouched");
        }

        public static void TestAutoFillPlantsNothingWithoutWater()
        {
            var record = Record(HdbfTier.Basic);
            var stats = IndoorLaborMath.RunActions(record, Basic(), FallbackCropCatalog.Default, 2,
                new AcceptAllSink(), true, 0, 1f, waterAvailable: false);
            TestAssert.Equal(0, stats.Planted, "a dry tank plants nothing");
            TestAssert.Equal(0, stats.ActionsUsed);
        }

        public static void TestAutoFillHarvestsFirstThenPlantsAllEmptySlots()
        {
            var record = Record(HdbfTier.Basic);
            for (var index = 0; index < 20; index++)
            {
                Mature(record, index, "Carrot");
            }

            var stats = IndoorLaborMath.RunActions(record, Basic(), FallbackCropCatalog.Default, 1,
                new AcceptAllSink(), true, 0, 1f, waterAvailable: true);
            // One worker, 13 harvest actions: slots 0..12 are harvested, slots 13..19 stay mature.
            TestAssert.Equal(13, stats.Harvested);
            TestAssert.Equal(13, stats.ActionsUsed, "harvests keep the daily budget");
            // Filled: the 13 harvested slots plus the 18 empty slots 20..37 = 31, free of budget.
            TestAssert.Equal(31, stats.Planted);
            TestAssert.Equal(SlotStage.Growing, record.Slots[0].Stage);
            TestAssert.Equal(SlotStage.Mature, record.Slots[19].Stage);
            TestAssert.Equal(SlotStage.Growing, record.Slots[37].Stage);
            TestAssert.Equal(SlotStage.Empty, record.Slots[38].Stage);
        }

        // ---------------------------------------------------------------- safety

        public static void TestNullSlotsArrayIsHandled()
        {
            var record = Record(HdbfTier.Basic);
            record.Slots = null!;
            var stats = IndoorLaborMath.RunActions(record, Basic(), FallbackCropCatalog.Default, 2,
                new AcceptAllSink(), true);
            TestAssert.Equal(0, stats.ActionsUsed);
        }

        public static void TestUnknownCropStaysMature()
        {
            var record = Record(HdbfTier.Basic);
            Mature(record, 0, "Bogus");
            var stats = Run(record, 1);
            TestAssert.Equal(0, stats.Harvested);
            TestAssert.Equal(SlotStage.Mature, record.Slots[0].Stage);
        }

        public static void TestMissingPrimarySpecFallsBackForHarvest()
        {
            var record = Record(HdbfTier.Basic);
            Mature(record, 0, "Carrot");
            var stats = IndoorLaborMath.RunActions(record, Basic(), new EmptyCatalog(), 1,
                new AcceptAllSink(), true);
            TestAssert.Equal(1, stats.Harvested, "the built-in table covers a missing game spec");
        }

        public static void TestHarvestAddsExactItemsToSink()
        {
            var record = Record(HdbfTier.Basic);
            Mature(record, 0, "Carrot");
            var sink = new RecordingSink();
            IndoorLaborMath.RunActions(record, Basic(), FallbackCropCatalog.Default, 1, sink, true);
            TestAssert.Equal("Carrot", sink.GoodIds.Count == 1 ? sink.GoodIds[0] : string.Empty);
            TestAssert.Equal(3, sink.Total);
        }

        public static void TestPlantedSlotStartsGrowingFromZero()
        {
            var record = Record(HdbfTier.Basic);
            Run(record, 1);
            var slot = record.Slots[0];
            TestAssert.Equal(SlotStage.Growing, slot.Stage);
            TestAssert.Equal(0f, slot.GrowthDays);
            TestAssert.Equal("Carrot", slot.CropId);
        }

        public static void TestRunActionsIsDeterministic()
        {
            var left = Record(HdbfTier.Basic);
            var right = Record(HdbfTier.Basic);
            for (var index = 0; index < 5; index++)
            {
                Mature(left, index, "Carrot");
                Mature(right, index, "Carrot");
            }

            var leftStats = Run(left, 2);
            var rightStats = Run(right, 2);
            TestAssert.Equal(leftStats.Harvested, rightStats.Harvested);
            TestAssert.Equal(leftStats.Planted, rightStats.Planted);
            TestAssert.Equal(leftStats.ActionsUsed, rightStats.ActionsUsed);
            for (var index = 0; index < left.Slots.Length; index++)
            {
                TestAssert.Equal(left.Slots[index].CropId, right.Slots[index].CropId);
            }
        }

        // ---------------------------------------------------------------- helpers

        private sealed class AcceptAllSink : IInventorySink
        {
            public bool TryAdd(string goodId, int amount) => true;
        }

        private sealed class FullSink : IInventorySink
        {
            public bool TryAdd(string goodId, int amount) => false;
        }

        private sealed class RecordingSink : IInventorySink
        {
            public readonly List<string> GoodIds = new List<string>();
            public int Total;
            public bool TryAdd(string goodId, int amount)
            {
                GoodIds.Add(goodId);
                Total += amount;
                return true;
            }
        }

        private sealed class EmptyCatalog : ICropCatalog
        {
            public IReadOnlyList<CropDefinition> ForFaction(HdbfFaction faction) => new List<CropDefinition>();
            public bool TryGet(HdbfFaction faction, string cropId, out CropDefinition crop)
            {
                crop = null!;
                return false;
            }
        }

        private static TierProfile Basic() => TierCatalog.Get(HdbfTier.Basic);

        private static TierProfile Top() => TierCatalog.Get(HdbfTier.Top);

        private static IndoorFarmRecord Record(HdbfTier tier) => IndoorFarmRecord.Create(tier, HdbfFaction.Folktails);

        private static IndoorActionStats Run(IndoorFarmRecord record, int workers)
        {
            return IndoorLaborMath.RunActions(record, Basic(), FallbackCropCatalog.Default, workers,
                new AcceptAllSink(), true);
        }

        private static void Mature(IndoorFarmRecord record, int index, string cropId)
        {
            var slot = record.Slots[index];
            slot.CropId = cropId;
            slot.GrowthDays = 4f;
            slot.Stage = SlotStage.Mature;
        }
    }
}
