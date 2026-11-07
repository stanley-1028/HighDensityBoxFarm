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

        // ------------------------------------------- one-day temporary output buffer (2026-11-07 fix)

        /// <summary>
        /// 2026-11-07 fix ("the farm stores like a warehouse instead of temporary staging"): the
        /// in-farm buffer is one game day of the tier's full-crew harvest, so a whole mature box can no
        /// longer be dumped into it in one day. The buffer must still never block a single day of
        /// harvesting - 16 farmers x 13 actions = 208 harvests, 208 Carrots x 3 items = 624 &lt;= the
        /// 832 bound. This is the no-regression guard for the 2026-10-29 report ("600 corn mature,
        /// only ~50 delivered").
        /// </summary>
        public static void TestOneDayHarvestAlwaysFitsTheTemporaryOutputBuffer()
        {
            var record = Record(HdbfTier.Top);
            for (var index = 0; index < record.Slots.Length; index++)
            {
                Mature(record, index, "Carrot");
            }

            var capacity = IndoorConstants.OutputCapacityFor(Top().SlotCapacity);
            var sink = new CappedSink(capacity);
            var stats = IndoorLaborMath.RunActions(record, Top(), FallbackCropCatalog.Default,
                Top().MaxWorkers, sink, true);

            TestAssert.Equal(16 * 13, stats.Harvested, "the whole daily action budget is spent on harvest");
            TestAssert.Equal(0, stats.BlockedByOutput, "one day of harvesting must always fit the buffer");
            TestAssert.False(stats.Backlog);
            TestAssert.Equal(208 * 3, sink.Total, "every harvested Carrot is stored in the temporary buffer");
        }

        /// <summary>
        /// The complement of the one-day guard: the whole 600-slot box (600 x 3 = 1800 Carrots) no
        /// longer fits the temporary buffer, so with NO hauling the farm correctly backpressures after
        /// the first day instead of silently storing the whole box. That backpressure is the intended
        /// "temporary staging" semantics: the reporting surface is <c>BlockedByOutput</c>/the
        /// OutputFull warning, not an ever-growing in-farm warehouse.
        /// </summary>
        public static void TestWholeMatureBoxBackpressuresTheTemporaryBuffer()
        {
            var record = Record(HdbfTier.Top);
            for (var index = 0; index < record.Slots.Length; index++)
            {
                Mature(record, index, "Carrot");
            }

            var capacity = IndoorConstants.OutputCapacityFor(Top().SlotCapacity);
            var sink = new CappedSink(capacity);
            var dayOne = IndoorLaborMath.RunActions(record, Top(), FallbackCropCatalog.Default,
                Top().MaxWorkers, sink, true);
            var dayTwo = IndoorLaborMath.RunActions(record, Top(), FallbackCropCatalog.Default,
                Top().MaxWorkers, sink, true);

            TestAssert.Equal(16 * 13, dayOne.Harvested, "day one harvests the daily budget");
            TestAssert.Equal(0, dayOne.BlockedByOutput);
            TestAssert.True(dayTwo.BlockedByOutput > 0,
                "without hauling the temporary buffer fills: the farm reports backpressure instead of warehousing the box");
            TestAssert.True(RemainingMature(record) > 0,
                "crops that do not fit stay mature and are harvested later (nothing is destroyed)");
        }

        /// <summary>
        /// End-to-end regression of the 2026-10-29 player report ("600 corn mature, only 50 delivered
        /// to the storage"): with a one-day temporary buffer and working haulers (200 items/day, the
        /// rate the 2026-10-29 analysis used) the whole mature box is harvested and moved out within a
        /// bounded number of days - no permanent stall - and the buffer never holds more than its
        /// capacity.
        /// </summary>
        public static void TestTemporaryBufferPlusHaulingClearsAWholeMatureBox()
        {
            var record = Record(HdbfTier.Top);
            for (var index = 0; index < record.Slots.Length; index++)
            {
                Mature(record, index, "Carrot");
            }

            var capacity = IndoorConstants.OutputCapacityFor(Top().SlotCapacity);
            var sink = new CappedSink(capacity);
            var harvested = 0;
            var maxInStock = 0;
            var days = 0;
            while (harvested < record.Slots.Length && days < 20)
            {
                var stats = IndoorLaborMath.RunActions(record, Top(), FallbackCropCatalog.Default,
                    Top().MaxWorkers, sink, true);
                harvested += stats.Harvested;
                maxInStock = Math.Max(maxInStock, sink.InStock);
                sink.Drain(200);
                days++;
            }

            TestAssert.Equal(600, harvested, "hauling plus the temporary buffer clears the whole mature box");
            TestAssert.Equal(0, RemainingMature(record), "no mature slot may be left behind");
            TestAssert.True(maxInStock <= capacity,
                "the in-farm buffer never holds more than its one-day capacity (held " + maxInStock + ")");
        }

        /// <summary>
        /// Negative control kept from the 2026-10-29 fix: the old fixed vanilla-farmhouse capacity 50
        /// fills after 16 Carrot harvests (16 x 3 = 48; the 17th would exceed 50), so 584 slots stay
        /// mature and the day reports an output backlog. This is why the 2026-11-07 temporary bound is
        /// per tier (one full crew day) rather than a fixed 50: a 50-item buffer caps a 16-farmer farm
        /// at ~16 harvested slots/day even when haulers could move more.
        /// </summary>
        public static void TestVanillaFiftyBufferBlocksAWholeMatureBox()
        {
            var record = Record(HdbfTier.Top);
            for (var index = 0; index < record.Slots.Length; index++)
            {
                Mature(record, index, "Carrot");
            }

            var stats = IndoorLaborMath.RunActions(record, Top(), FallbackCropCatalog.Default,
                Top().MaxWorkers, new CappedSink(50), true);
            TestAssert.Equal(16, stats.Harvested, "50 / 3 items per Carrot harvest = 16 slots");
            TestAssert.Equal(584, stats.BlockedByOutput);
            TestAssert.True(stats.Backlog);
            TestAssert.Equal(SlotStage.Mature, record.Slots[16].Stage);
            TestAssert.Equal(SlotStage.Mature, record.Slots[599].Stage);
        }

        /// <summary>
        /// The 2026-11-07 buffer formula is one full crew day for every tier
        /// (<c>ceil(Slots / SlotsPerFarmer) x ActionsPerFarmerDay x MaxHarvestItemsPerSlot</c>,
        /// 60/150/300/600 -> 104/208/416/832), which is what makes the temporary staging rule
        /// tier-independent. The tier crews are sized to cover their box exactly (2/4/8/16), so the
        /// formula also equals <c>MaxWorkers x 13 x 4</c>.
        /// </summary>
        public static void TestOutputCapacityForIsOneDayHarvestBound()
        {
            var expected = new[] { 104, 208, 416, 832 };
            var tiers = new[] { HdbfTier.Basic, HdbfTier.Intermediate, HdbfTier.Advanced, HdbfTier.Top };
            for (var index = 0; index < tiers.Length; index++)
            {
                var profile = TierCatalog.Get(tiers[index]);
                var capacity = IndoorConstants.OutputCapacityFor(profile.SlotCapacity);
                TestAssert.Equal(expected[index], capacity,
                    tiers[index] + ": output buffer must be one full crew day (slots / 38 farmers x 13 actions x 4)");
                TestAssert.Equal(profile.MaxWorkers * IndoorConstants.ActionsPerFarmerDay
                                 * IndoorConstants.MaxHarvestItemsPerSlot,
                    capacity,
                    tiers[index] + ": the one-day bound equals MaxWorkers x 13 x 4");
            }

            TestAssert.Equal(0, IndoorConstants.OutputCapacityFor(0), "an unknown tier has no buffer");
        }

        /// <summary>
        /// Tight boundary of the one-day staging rule: dense-mode Top converts the 3-item Carrot base
        /// yield into 4 items (3 x 1.35 rounded), so a full crew day is 208 harvests x 4 = 832 == the
        /// temporary bound - still no backlog inside the day. Normal-mode Carrot only fills 624 of 832,
        /// so this is the zero-margin proof that "capacity &gt;= one full crew day" actually holds.
        /// </summary>
        public static void TestDenseOneDayHarvestExactlyFitsTheTemporaryOutputBuffer()
        {
            var record = Record(HdbfTier.Top);
            record.Mode = IndoorMode.Dense;
            for (var index = 0; index < record.Slots.Length; index++)
            {
                Mature(record, index, "Carrot");
            }

            var capacity = IndoorConstants.OutputCapacityFor(Top().SlotCapacity);
            var sink = new CappedSink(capacity);
            var stats = IndoorLaborMath.RunActions(record, Top(), FallbackCropCatalog.Default,
                Top().MaxWorkers, sink, true);

            TestAssert.Equal(16 * 13, stats.Harvested, "the full daily action budget is spent on harvest");
            TestAssert.Equal(0, stats.BlockedByOutput,
                "the dense zero-margin day (208 x 4 = 832) must still fit the one-day bound");
            TestAssert.Equal(capacity, sink.Total, "the dense day fills the temporary buffer exactly");
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

        /// <summary>Per-good capped sink used by the output-buffer regression tests.</summary>
        private sealed class CappedSink : IInventorySink
        {
            private readonly Dictionary<string, int> _stock = new Dictionary<string, int>(StringComparer.Ordinal);
            private readonly int _capacity;

            public CappedSink(int capacity)
            {
                _capacity = capacity;
            }

            /// <summary>Items ever accepted (monotonic; the regression asserts the whole harvest enters).</summary>
            public int Total { get; private set; }

            /// <summary>Items currently held by the simulated output inventory.</summary>
            public int InStock { get; private set; }

            public bool TryAdd(string goodId, int amount)
            {
                if (!_stock.TryGetValue(goodId, out var have))
                {
                    have = 0;
                }

                if (have + amount > _capacity)
                {
                    return false;
                }

                _stock[goodId] = have + amount;
                Total += amount;
                InStock += amount;
                return true;
            }

            /// <summary>
            /// Moves up to <paramref name="amount"/> items out of the buffer, the test-side model of a
            /// vanilla hauler draining an output inventory (deterministic ascending good id order).
            /// </summary>
            public void Drain(int amount)
            {
                var goodIds = new List<string>(_stock.Keys);
                goodIds.Sort(StringComparer.Ordinal);
                foreach (var goodId in goodIds)
                {
                    if (amount <= 0)
                    {
                        return;
                    }

                    var have = _stock[goodId];
                    if (have <= 0)
                    {
                        continue;
                    }

                    var taken = Math.Min(have, amount);
                    _stock[goodId] = have - taken;
                    InStock -= taken;
                    amount -= taken;
                }
            }
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

        private static int RemainingMature(IndoorFarmRecord record)
        {
            var remaining = 0;
            foreach (var slot in record.Slots)
            {
                if (slot is not null && slot.Stage == SlotStage.Mature)
                {
                    remaining++;
                }
            }

            return remaining;
        }
    }
}
