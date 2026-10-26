// ============================================================================
// HDBF v2 | Layer: L1 test | Owner: V2-CORE-01
// Purpose: G_maint 1.0/0.5/0, G_power 0.6, stall stopping only farm actions and the
//          material/power warnings (section 19.8 / section 19.14).
// Requirements: REQ-V2-BAL-01, REQ-V2-BAL-02, REQ-V2-TEST-01
// ============================================================================
using HDBF.Core;
using HDBF.Core.Balance;
using HDBF.Core.Indoor;

namespace HDBF.Tests
{
    public static class V2MaintenancePowerTests
    {
        // ---------------------------------------------------------------- pure functions

        public static void TestMaintenanceEfficiency()
        {
            TestAssert.Equal(1f, IndoorMaintenanceMath.Efficiency(0));
            TestAssert.Equal(0.5f, IndoorMaintenanceMath.Efficiency(1));
            TestAssert.Equal(0f, IndoorMaintenanceMath.Efficiency(2));
            TestAssert.Equal(0f, IndoorMaintenanceMath.Efficiency(3));
            TestAssert.Equal(1f, IndoorMaintenanceMath.Efficiency(-1));
        }

        public static void TestMaintenanceStalled()
        {
            TestAssert.False(IndoorMaintenanceMath.IsStalled(0));
            TestAssert.False(IndoorMaintenanceMath.IsStalled(1));
            TestAssert.True(IndoorMaintenanceMath.IsStalled(2));
            TestAssert.True(IndoorMaintenanceMath.IsStalled(3));
        }

        public static void TestMaintenanceMaterialMultiplier()
        {
            TestAssert.Equal(1f, IndoorMaintenanceMath.MaintenanceMaterialMultiplier(true));
            TestAssert.Equal(2f, IndoorMaintenanceMath.MaintenanceMaterialMultiplier(false));
        }

        public static void TestPowerEfficiency()
        {
            TestAssert.Equal(1f, IndoorPowerMath.Efficiency(false, false), "a tier without power grows at 1.0");
            TestAssert.Equal(1f, IndoorPowerMath.Efficiency(true, true));
            TestAssert.Equal(0.6f, IndoorPowerMath.Efficiency(true, false), "a power cut derates to 0.6");
        }

        public static void TestMaterialItemIds()
        {
            TestAssert.Equal("Plank", IndoorConstants.MaintenanceItemPlank);
            TestAssert.Equal("MetalBlock", IndoorConstants.MaintenanceItemMetal);
        }

        // ---------------------------------------------------------------- one missed cycle

        public static void TestOneMissedCycleHalvesGrowth()
        {
            var record = IntermediateRecord();
            record.MaintenanceDueDay = 0f;
            var slot = record.Slots[0];
            slot.CropId = "Carrot";
            slot.Stage = SlotStage.Growing;

            var result = Tick(record, new IndoorDayContext { ActiveWorkers = 1, CurrentDay = 5f });
            // cycle 10: today 5 >= due 0 -> 1 + floor(5/10) = 1 cycle -> G_maint 0.5.
            TestAssert.Equal(0.5f, result.GrowthMultiplier, 1e-5f);
            TestAssert.Equal(0.5f, slot.GrowthDays, 1e-5f);
            TestAssert.True(Has(result.Warnings, IndoorWarningFlags.MaintenanceDue));
        }

        public static void TestMaintenanceCyclesArePersistedAndDueDayIsNotReset()
        {
            var record = IntermediateRecord();
            record.MaintenanceDueDay = 2f;
            Tick(record, new IndoorDayContext { ActiveWorkers = 0, CurrentDay = 15f });
            TestAssert.Equal(2, record.MaintenanceCyclesMissed);
            TestAssert.Equal(2f, record.MaintenanceDueDay, 1e-5f, "the tick never performs the maintenance");
        }

        // ---------------------------------------------------------------- stall

        public static void TestStallStopsActionsButNotGrowth()
        {
            var record = IntermediateRecord();
            record.MaintenanceDueDay = 0f;
            var growing = record.Slots[0];
            growing.CropId = "Carrot";
            growing.Stage = SlotStage.Growing;
            var mature = record.Slots[1];
            mature.CropId = "Carrot";
            mature.Stage = SlotStage.Mature;
            mature.GrowthDays = 4f;

            var sink = new RecordingSink();
            var result = Tick(record, new IndoorDayContext { ActiveWorkers = 2, CurrentDay = 25f, Output = sink });
            // 1 + floor(25/10) = 3 missed cycles -> stalled.
            TestAssert.True(result.Stalled);
            TestAssert.True(Has(result.Warnings, IndoorWarningFlags.MaintenanceStalled));
            TestAssert.Equal(0, result.Actions.Harvested, "stall stops farm actions");
            TestAssert.Equal(0, sink.Total);
            TestAssert.Equal(SlotStage.Mature, mature.Stage);
            // REQ-V2-BAL-02: growth continues at the v1 minimum growth rate instead of freezing.
            TestAssert.Equal(IndoorConstants.StalledGrowthMultiplier, result.GrowthMultiplier, 1e-5f);
            TestAssert.Equal(IndoorConstants.StalledGrowthMultiplier, growing.GrowthDays, 1e-5f);
        }

        public static void TestBasicTierNeverStalls()
        {
            var record = IndoorFarmRecord.Create(HdbfTier.Basic, HdbfFaction.Folktails);
            var result = Tick(record, new IndoorDayContext { ActiveWorkers = 0, CurrentDay = 10000f });
            TestAssert.False(result.Stalled);
            TestAssert.False(Has(result.Warnings, IndoorWarningFlags.MaintenanceDue));
            TestAssert.Equal(0, record.MaintenanceCyclesMissed);
        }

        public static void TestMissingMaterialWarning()
        {
            var record = AdvancedRecord();
            record.MaintenanceDueDay = 0f;
            var result = Tick(record, new IndoorDayContext { ActiveWorkers = 0, CurrentDay = 15f });
            TestAssert.True(Has(result.Warnings, IndoorWarningFlags.MissingMaterial));
        }

        public static void TestAvailableMaterialClearsMissingWarning()
        {
            var record = AdvancedRecord();
            record.MaintenanceDueDay = 0f;
            var result = Tick(record, new IndoorDayContext
            {
                ActiveWorkers = 0,
                CurrentDay = 15f,
                Materials = new Stockpile { PlankCount = 100, MetalCount = 100 },
            });
            TestAssert.False(Has(result.Warnings, IndoorWarningFlags.MissingMaterial));
            TestAssert.True(Has(result.Warnings, IndoorWarningFlags.MaintenanceDue));
        }

        // ---------------------------------------------------------------- power

        public static void TestTopTierPowerCutDeratesGrowth()
        {
            var record = TopRecord(PowerConnection.Grid, powerConnected: false);
            var slot = record.Slots[0];
            slot.CropId = "Carrot";
            slot.Stage = SlotStage.Growing;
            var result = Tick(record, new IndoorDayContext { ActiveWorkers = 1, CurrentDay = 0f });
            TestAssert.Equal(0.6f, result.GrowthMultiplier, 1e-5f);
            TestAssert.Equal(0.6f, slot.GrowthDays, 1e-5f);
            TestAssert.True(Has(result.Warnings, IndoorWarningFlags.NoPower));
        }

        public static void TestTopTierOffGridKeepsFullPower()
        {
            var record = TopRecord(PowerConnection.OffGrid, powerConnected: false);
            var result = Tick(record, new IndoorDayContext { ActiveWorkers = 1, CurrentDay = 0f });
            TestAssert.Equal(1f, result.GrowthMultiplier, 1e-5f, "off-grid pays material maintenance, not G_power");
            TestAssert.False(Has(result.Warnings, IndoorWarningFlags.NoPower));
        }

        public static void TestTopTierConnectedIsAutomaticallyMaintained()
        {
            var record = TopRecord(PowerConnection.Grid, powerConnected: true);
            var result = Tick(record, new IndoorDayContext { ActiveWorkers = 0, CurrentDay = 100f });
            TestAssert.False(result.Stalled);
            TestAssert.False(Has(result.Warnings, IndoorWarningFlags.MaintenanceDue));
            TestAssert.Equal(0, record.MaintenanceCyclesMissed);
            TestAssert.True(record.MaintenanceDueDay > 100f, "connected tier keeps its countdown full");

            // A later power cut must not stall the farm instantly.
            record.PowerConnected = false;
            var cut = Tick(record, new IndoorDayContext { ActiveWorkers = 0, CurrentDay = 101f });
            TestAssert.False(cut.Stalled);
            TestAssert.True(Has(cut.Warnings, IndoorWarningFlags.NoPower));
        }

        // ---------------------------------------------------------------- helpers

        private sealed class RecordingSink : IInventorySink
        {
            public int Total;
            public bool TryAdd(string goodId, int amount)
            {
                Total += amount;
                return true;
            }
        }

        private sealed class Stockpile : IMaintenanceMaterialSource
        {
            public int PlankCount;
            public int MetalCount;
            public int Available(string itemId) => itemId == "Plank" ? PlankCount : MetalCount;
            public bool TryConsume(string itemId, int count) => false;
        }

        private static bool Has(IndoorWarningFlags flags, IndoorWarningFlags expected)
        {
            return (flags & expected) == expected;
        }

        private static IndoorFarmRecord IntermediateRecord()
        {
            return IndoorFarmRecord.Create(HdbfTier.Intermediate, HdbfFaction.Folktails);
        }

        private static IndoorFarmRecord AdvancedRecord()
        {
            return IndoorFarmRecord.Create(HdbfTier.Advanced, HdbfFaction.Folktails);
        }

        private static IndoorFarmRecord TopRecord(PowerConnection connection, bool powerConnected)
        {
            var record = IndoorFarmRecord.Create(HdbfTier.Top, HdbfFaction.Folktails);
            record.PowerMode = connection;
            record.PowerConnected = powerConnected;
            return record;
        }

        private static IndoorDayResult Tick(IndoorFarmRecord record, IndoorDayContext context)
        {
            return IndoorDayTick.Run(record, TierCatalog.Get(record.Tier), FallbackCropCatalog.Default, context);
        }
    }
}
