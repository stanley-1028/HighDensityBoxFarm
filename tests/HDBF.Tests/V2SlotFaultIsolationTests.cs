// ============================================================================
// HDBF v2 | Layer: L1 test | Owner: V2-CORE-01
// Purpose: Single-slot / single-service fault isolation of the daily tick
//          (REQ-V2-SAFE-02 / REQ-V2-SAFE-04, section 19.14 slot exception isolation).
// Requirements: REQ-V2-SAFE-01, REQ-V2-SAFE-02, REQ-V2-SAFE-04, REQ-V2-CORE-05
// ============================================================================
using System;
using System.Collections.Generic;
using HDBF.Core;
using HDBF.Core.Balance;
using HDBF.Core.Indoor;

namespace HDBF.Tests
{
    public static class V2SlotFaultIsolationTests
    {
        public static void TestThrowingOutputSinkBlocksOnlyThatHarvest()
        {
            var record = Record();
            Mature(record, 0, "Carrot");
            Mature(record, 1, "Wheat");
            var result = Tick(record, new IndoorDayContext
            {
                ActiveWorkers = 2,
                CurrentDay = 1f,
                HasPipeWater = true,
                Water = new InfiniteWater(),
                Output = new ThrowingSink("Wheat"),
            });
            TestAssert.Equal(1, result.Actions.Harvested, "the good crop is still harvested");
            TestAssert.Equal(1, result.Actions.BlockedByOutput, "the throwing good is isolated");
            TestAssert.True(result.Actions.Backlog);
            TestAssert.Equal(SlotStage.Mature, record.Slots[1].Stage);
        }

        public static void TestThrowingWaterSourceDoesNotThrow()
        {
            var record = Record();
            record.Tank = 0f;
            var result = Tick(record, new IndoorDayContext
            {
                ActiveWorkers = 1,
                CurrentDay = 1f,
                HasPipeWater = true,
                Water = new ThrowingWater(),
            });
            TestAssert.True(Has(result.Warnings, IndoorWarningFlags.NoWater));
            TestAssert.Equal(0f, record.Tank);
        }

        public static void TestThrowingMaterialSourceIsIsolated()
        {
            var record = IndoorFarmRecord.Create(HdbfTier.Intermediate, HdbfFaction.Folktails);
            record.MaintenanceDueDay = 0f;
            var result = Tick(record, new IndoorDayContext
            {
                ActiveWorkers = 0,
                CurrentDay = 25f,
                Materials = new ThrowingMaterials(),
            });
            TestAssert.True(Has(result.Warnings, IndoorWarningFlags.MissingMaterial));
        }

        public static void TestThrowingCatalogFallsBackForGrowthAndPlanting()
        {
            var record = Record();
            var slot = record.Slots[0];
            slot.CropId = "Carrot";
            slot.Stage = SlotStage.Growing;
            var result = Tick(record, new IndoorDayContext
            {
                ActiveWorkers = 1,
                CurrentDay = 1f,
                HasPipeWater = true,
                Water = new InfiniteWater(),
            }, new ThrowingCatalog());
            TestAssert.True(slot.GrowthDays > 0f, "the built-in table keeps growth working");
            // Player request 2026-10-28: a watered tank auto-fills every empty worker-tended slot
            // (1 farmer -> 38 tended slots, slot 0 is still growing -> 37 planted, no budget limit).
            TestAssert.Equal(37, result.Actions.Planted, "auto-fill plants every empty worker-tended slot");
        }

        public static void TestCorruptSlotValuesDoNotThrow()
        {
            var record = Record();
            var slot = record.Slots[0];
            slot.CropId = "Carrot";
            slot.Stage = SlotStage.Growing;
            slot.GrowthDays = float.NaN;
            slot.Fatigue = float.NaN;
            var result = Tick(record, new IndoorDayContext
            {
                ActiveWorkers = 1,
                CurrentDay = 1f,
                HasPipeWater = true,
                Water = new InfiniteWater(),
            });
            TestAssert.Equal(0f, slot.Fatigue);
            TestAssert.True(slot.GrowthDays >= 0f && !float.IsNaN(slot.GrowthDays));
            TestAssert.NotNull(result);
        }

        public static void TestCorruptTankDoesNotThrow()
        {
            var record = Record();
            record.Tank = float.NaN;
            var result = Tick(record, new IndoorDayContext { ActiveWorkers = 1, CurrentDay = 1f });
            TestAssert.Equal(0f, record.Tank);
            TestAssert.True(Has(result.Warnings, IndoorWarningFlags.NoWater));
        }

        public static void TestNullSlotElementIsSkipped()
        {
            var record = Record();
            record.Slots[0] = null!;
            Mature(record, 1, "Carrot");
            var result = Tick(record, new IndoorDayContext
            {
                ActiveWorkers = 1,
                CurrentDay = 1f,
                HasPipeWater = true,
                Water = new InfiniteWater(),
                Output = new AcceptAllSink(),
            });
            TestAssert.Equal(1, result.Actions.Harvested);
        }

        public static void TestSlotsArrayShorterThanCapacityIsHandled()
        {
            var record = Record();
            var shortSlots = new IndoorSlot[2];
            shortSlots[0] = new IndoorSlot { CropId = "Carrot", Stage = SlotStage.Growing };
            shortSlots[1] = new IndoorSlot();
            record.Slots = shortSlots;
            Tick(record, new IndoorDayContext { ActiveWorkers = 2, CurrentDay = 1f });
            TestAssert.True(shortSlots[0].GrowthDays >= 0f);
        }

        public static void TestSlotsArrayLongerThanCapacityIsBounded()
        {
            var record = Record();
            var longSlots = new IndoorSlot[100];
            for (var index = 0; index < longSlots.Length; index++)
            {
                longSlots[index] = new IndoorSlot();
            }

            MatureInto(longSlots, 10, "Carrot");
            MatureInto(longSlots, 70, "Carrot");
            record.Slots = longSlots;
            var result = Tick(record, new IndoorDayContext
            {
                ActiveWorkers = 1,
                CurrentDay = 1f,
                HasPipeWater = true,
                Water = new InfiniteWater(),
                Output = new AcceptAllSink(),
            });
            TestAssert.Equal(1, result.Actions.Harvested, "only the first SlotCapacity entries are read");
            TestAssert.Equal(SlotStage.Mature, longSlots[70].Stage);
        }

        public static void TestUnknownCropInSlotKeepsSlotStable()
        {
            var record = Record();
            var slot = record.Slots[0];
            slot.CropId = "TotallyUnknownCrop";
            slot.Stage = SlotStage.Growing;
            Tick(record, new IndoorDayContext { ActiveWorkers = 1, CurrentDay = 1f });
            TestAssert.Equal(SlotStage.Growing, slot.Stage);
            TestAssert.Equal(0f, slot.GrowthDays);
            TestAssert.Equal("TotallyUnknownCrop", slot.CropId);
        }

        public static void TestFaultInOneBuildingDoesNotAffectAnother()
        {
            var broken = Record();
            Mature(broken, 0, "Carrot");
            var healthy = Record();
            Mature(healthy, 0, "Carrot");

            Tick(broken, new IndoorDayContext
            {
                ActiveWorkers = 1,
                CurrentDay = 1f,
                HasPipeWater = true,
                Water = new InfiniteWater(),
                Output = new ThrowingSink("Carrot"),
            });
            var healthyResult = Tick(healthy, new IndoorDayContext
            {
                ActiveWorkers = 1,
                CurrentDay = 1f,
                HasPipeWater = true,
                Water = new InfiniteWater(),
                Output = new AcceptAllSink(),
            });
            TestAssert.Equal(1, healthyResult.Actions.Harvested);
            TestAssert.Equal(SlotStage.Mature, broken.Slots[0].Stage);
        }

        public static void TestMissingSpecInBothCatalogsIsNotFatal()
        {
            var record = Record();
            var slot = record.Slots[0];
            slot.CropId = "TotallyUnknownCrop";
            slot.Stage = SlotStage.Mature;
            var result = Tick(record, new IndoorDayContext
            {
                ActiveWorkers = 1,
                CurrentDay = 1f,
                Output = new AcceptAllSink(),
            });
            TestAssert.Equal(0, result.Actions.Harvested);
            TestAssert.Equal(SlotStage.Mature, slot.Stage);
        }

        public static void TestNegativeWorkersAndDayAreSanitized()
        {
            var record = Record();
            var result = Tick(record, new IndoorDayContext { ActiveWorkers = -5, CurrentDay = float.NaN });
            TestAssert.True(Has(result.Warnings, IndoorWarningFlags.NoWorkers));
            TestAssert.False(result.Changed);
        }

        private sealed class AcceptAllSink : IInventorySink
        {
            public bool TryAdd(string goodId, int amount) => true;
        }
        private sealed class ThrowingSink : IInventorySink
        {
            private readonly string _goodId;
            public ThrowingSink(string goodId) => _goodId = goodId;
            public bool TryAdd(string goodId, int amount)
            {
                if (string.Equals(goodId, _goodId, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("inventory exploded for " + goodId);
                }
                return true;
            }
        }
        private sealed class ThrowingWater : IWaterSource
        {
            public WaterSupplyKind Kind => WaterSupplyKind.Pipe;
            public bool IsAvailable => throw new InvalidOperationException("water query failed");
            public bool TryDraw(float amount, out float actual) => throw new InvalidOperationException("draw failed");
        }
        private sealed class ThrowingMaterials : IMaintenanceMaterialSource
        {
            public int Available(string itemId) => throw new InvalidOperationException("stockpile query failed");
            public bool TryConsume(string itemId, int count) => throw new InvalidOperationException("consume failed");
        }
        private sealed class ThrowingCatalog : ICropCatalog
        {
            public IReadOnlyList<CropDefinition> ForFaction(HdbfFaction faction) => throw new InvalidOperationException("catalog failed");
            public bool TryGet(HdbfFaction faction, string cropId, out CropDefinition crop) => throw new InvalidOperationException("catalog failed");
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

        private static bool Has(IndoorWarningFlags flags, IndoorWarningFlags expected)
        {
            return (flags & expected) == expected;
        }

        private static IndoorFarmRecord Record() => IndoorFarmRecord.Create(HdbfTier.Basic, HdbfFaction.Folktails);

        private static IndoorDayResult Tick(IndoorFarmRecord record, IndoorDayContext context)
        {
            return Tick(record, context, FallbackCropCatalog.Default);
        }

        private static IndoorDayResult Tick(IndoorFarmRecord record, IndoorDayContext context, ICropCatalog catalog)
        {
            return IndoorDayTick.Run(record, TierCatalog.Get(record.Tier), catalog, context);
        }

        private static void Mature(IndoorFarmRecord record, int index, string cropId)
        {
            MatureInto(record.Slots, index, cropId);
        }

        private static void MatureInto(IndoorSlot[] slots, int index, string cropId)
        {
            slots[index].CropId = cropId;
            slots[index].Stage = SlotStage.Mature;
            slots[index].GrowthDays = 4f;
        }
    }
}
