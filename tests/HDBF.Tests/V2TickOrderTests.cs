// ============================================================================
// HDBF v2 | Layer: L1 test | Owner: V2-CORE-01
// Purpose: The fixed daily tick order (ARCHITECTURE section 3), the aggregated
//          warnings/dirty flag and the registered log-key contract.
// Requirements: REQ-V2-MAINT-03, REQ-V2-BAL-02, REQ-V2-READ-03, REQ-V2-TEST-01
// ============================================================================
using System;
using System.Collections.Generic;
using System.Reflection;
using HDBF.Core;
using HDBF.Core.Balance;
using HDBF.Core.Indoor;

namespace HDBF.Tests
{
    public static class V2TickOrderTests
    {
        public static void TestPipeRefillRunsBeforeTheDailyCoverageRead()
        {
            // The continuous layer owns the drain; the daily tick still refills from the pipe first,
            // so the coverage read below sees the topped-up tank (coverage 1, full growth).
            var record = Record();
            record.Tank = 0f;
            var slot = record.Slots[0];
            slot.CropId = "Carrot";
            slot.Stage = SlotStage.Growing;
            var result = Tick(record, Healthy(1));
            // demand 0.76: the pipe adds 0.76; nothing is deducted by the tick itself.
            TestAssert.Equal(1f, result.Coverage, 1e-4f);
            TestAssert.Equal(1f, result.GrowthMultiplier, 1e-4f);
            TestAssert.Equal(1f, slot.GrowthDays, 1e-4f);
        }

        public static void TestGrowthHappensBeforeHarvestInTheSameDay()
        {
            var record = Record();
            var slot = record.Slots[0];
            slot.CropId = "Carrot";
            slot.Stage = SlotStage.Growing;
            slot.GrowthDays = 3f; // matures during phase 3, harvested by phase 4
            var sink = new RecordingSink();
            var result = Tick(record, Healthy(1, sink));
            TestAssert.Equal(1, result.Actions.Harvested, "growth ran before the harvest pass");
            TestAssert.Equal(3, sink.Total);
        }

        public static void TestEntranceGateIsEvaluatedBeforeActions()
        {
            var record = Record();
            var slot = record.Slots[0];
            slot.CropId = "Carrot";
            slot.Stage = SlotStage.Growing;
            slot.GrowthDays = 3.5f;
            var sink = new RecordingSink();
            var result = Tick(record, Healthy(1, sink, blocked: true));
            TestAssert.True(Has(result.Warnings, IndoorWarningFlags.EntranceBlocked));
            TestAssert.Equal(0, sink.Total, "blocked entrance stops farm actions");
            TestAssert.Equal(SlotStage.Mature, slot.Stage, "growth still happened and the slot waits");
        }

        public static void TestFallowPhaseStopsActionsButGrowthContinues()
        {
            var record = Record();
            record.Mode = IndoorMode.Fallow;
            var slot = record.Slots[0];
            slot.CropId = "Carrot";
            slot.Stage = SlotStage.Growing;
            var result = Tick(record, new IndoorDayContext { ActiveWorkers = 1, CurrentDay = 1f });
            TestAssert.True(slot.GrowthDays > 0f);
            TestAssert.Equal(0, result.Actions.ActionsUsed);
        }

        public static void TestChangedIsFalseWhenNothingMoves()
        {
            var record = Record();
            var result = Tick(record, new IndoorDayContext { ActiveWorkers = 0, CurrentDay = 0f });
            TestAssert.False(result.Changed, "an idle farm must not dirty the save");
        }

        public static void TestChangedIsTrueWhenWaterIsDeducted()
        {
            var record = Record();
            var result = Tick(record, new IndoorDayContext { ActiveWorkers = 1, CurrentDay = 1f });
            TestAssert.True(result.Changed);
        }

        public static void TestChangedIsTrueWhenACropGrows()
        {
            var record = Record();
            var slot = record.Slots[0];
            slot.CropId = "Carrot";
            slot.Stage = SlotStage.Growing;
            var result = Tick(record, Healthy(1));
            TestAssert.True(result.Changed);
        }

        public static void TestNoWorkersWarning()
        {
            var record = Record();
            var result = Tick(record, new IndoorDayContext { ActiveWorkers = 0, CurrentDay = 1f });
            TestAssert.True(Has(result.Warnings, IndoorWarningFlags.NoWorkers));
        }

        public static void TestLowWaterWarning()
        {
            var record = Record();
            record.Tank = 0.1f;
            var result = Tick(record, new IndoorDayContext { ActiveWorkers = 2, CurrentDay = 1f });
            // C = 0.1 / 1.2 = 0.083 < C_min 0.5 but above 0.
            TestAssert.True(Has(result.Warnings, IndoorWarningFlags.LowWater));
            TestAssert.False(Has(result.Warnings, IndoorWarningFlags.NoWater));
        }

        public static void TestNoWaterWarningWhenTankEmpty()
        {
            var record = Record();
            record.Tank = 0f;
            var result = Tick(record, new IndoorDayContext { ActiveWorkers = 2, CurrentDay = 1f });
            TestAssert.True(Has(result.Warnings, IndoorWarningFlags.NoWater));
        }

        public static void TestOutputFullWarning()
        {
            var record = Record();
            record.Slots[0].CropId = "Carrot";
            record.Slots[0].Stage = SlotStage.Mature;
            record.Slots[0].GrowthDays = 4f;
            var result = Tick(record, Healthy(1, new FullSink()));
            TestAssert.True(Has(result.Warnings, IndoorWarningFlags.OutputFull));
            TestAssert.True(result.Actions.Backlog);
        }

        public static void TestResultMultipliersWithHealthyFarm()
        {
            var record = Record();
            var result = Tick(record, Healthy(1));
            TestAssert.Equal(1f, result.GrowthMultiplier, 1e-5f);
            TestAssert.Equal(1f, result.YieldMultiplier, 1e-5f);
            TestAssert.Equal(1f, result.Coverage, 1e-5f);
        }

        public static void TestMatureSlotWaitsWithoutWorkers()
        {
            var record = Record();
            record.Slots[0].CropId = "Carrot";
            record.Slots[0].Stage = SlotStage.Mature;
            record.Slots[0].GrowthDays = 4f;
            var sink = new RecordingSink();
            var result = Tick(record, Healthy(0, sink));
            TestAssert.Equal(0, sink.Total, "no workers means zero produce");
            TestAssert.Equal(SlotStage.Mature, record.Slots[0].Stage);
            TestAssert.Equal(0, result.Actions.Harvested);
        }

        public static void TestZeroGrowthStopsProgressButKeepsSlot()
        {
            var record = Record();
            record.Tank = 0f;
            var slot = record.Slots[0];
            slot.CropId = "Carrot";
            slot.Stage = SlotStage.Growing;
            var result = Tick(record, new IndoorDayContext { ActiveWorkers = 1, CurrentDay = 1f });
            TestAssert.Equal(0f, result.GrowthMultiplier);
            TestAssert.Equal(0f, slot.GrowthDays);
            TestAssert.Equal(SlotStage.Growing, slot.Stage, "a stopped crop never disappears");
        }

        public static void TestNullArgumentsReturnEmptyResult()
        {
            var context = new IndoorDayContext { ActiveWorkers = 1 };
            var empty = IndoorDayTick.Run(null!, Basic(), FallbackCropCatalog.Default, context);
            TestAssert.Equal(0f, empty.Coverage);
            TestAssert.Equal(0, empty.Actions.ActionsUsed);
            var record = Record();
            var noTier = IndoorDayTick.Run(record, null!, FallbackCropCatalog.Default, context);
            TestAssert.Equal(0f, noTier.Coverage);
        }

        public static void TestNullSlotsAreHandled()
        {
            var record = Record();
            record.Slots = null!;
            var result = IndoorDayTick.Run(record, Basic(), FallbackCropCatalog.Default,
                new IndoorDayContext { ActiveWorkers = 2, CurrentDay = 1f });
            TestAssert.Equal(0, result.Actions.ActionsUsed);
            // With the continuous drain in the game layer the tick itself moves no water here.
            TestAssert.False(result.Changed, "a tick without slots, actions or refill changes nothing");
            TestAssert.Equal(3.6f, record.Tank, 1e-5f);
        }

        public static void TestNullContextIsTreatedAsNobodyOnDuty()
        {
            var record = Record();
            var result = IndoorDayTick.Run(record, Basic(), FallbackCropCatalog.Default, null!);
            TestAssert.True(Has(result.Warnings, IndoorWarningFlags.NoWorkers));
        }

        public static void TestLogKeysFollowRegisteredContract()
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var fields = typeof(IndoorLogKeys).GetFields(BindingFlags.Public | BindingFlags.Static);
            Array.Sort(fields, (left, right) => string.CompareOrdinal(left.Name, right.Name));
            foreach (var field in fields)
            {
                if (!field.IsLiteral || field.FieldType != typeof(string)) continue;
                var value = (string)field.GetValue(null)!;
                var ok = value.StartsWith("indoor.", StringComparison.Ordinal)
                         || value.StartsWith("water.", StringComparison.Ordinal)
                         || value.StartsWith("save.", StringComparison.Ordinal)
                         || value.StartsWith("ui.", StringComparison.Ordinal)
                         || value.StartsWith("compat.", StringComparison.Ordinal);
                TestAssert.True(ok, "unregistered log prefix: " + value);
                TestAssert.True(seen.Add(value), "duplicate log key: " + value);
            }

            TestAssert.True(seen.Count >= 15, "the log key table must stay registered");
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

        private sealed class FullSink : IInventorySink
        {
            public bool TryAdd(string goodId, int amount) => false;
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

        private static IndoorDayContext Healthy(int workers, IInventorySink? output = null, bool blocked = false)
        {
            return new IndoorDayContext
            {
                ActiveWorkers = workers,
                CurrentDay = 1f,
                HasPipeWater = true,
                Water = new InfiniteWater(),
                Output = output,
                EntranceBlocked = blocked,
            };
        }

        private static bool Has(IndoorWarningFlags flags, IndoorWarningFlags expected)
        {
            return (flags & expected) == expected;
        }

        private static TierProfile Basic() => TierCatalog.Get(HdbfTier.Basic);

        private static IndoorFarmRecord Record() => IndoorFarmRecord.Create(HdbfTier.Basic, HdbfFaction.Folktails);

        private static IndoorDayResult Tick(IndoorFarmRecord record, IndoorDayContext context)
        {
            return IndoorDayTick.Run(record, Basic(), FallbackCropCatalog.Default, context);
        }
    }
}
