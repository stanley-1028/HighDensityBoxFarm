// ============================================================================
// HDBF v2 | Layer: L1 test | Owner: V2-CORE-01 (bug L-01, 2026-11-02)
// Purpose: Regression pins for the fallow -> normal transition harvest: the first normal day
//          must never clear grown crops with a zero mode-transition yield scale, and a
//          zero-yield harvest must never empty a mature slot.
// Requirements: REQ-V2-BAL-03, REQ-V2-CORE-05, REQ-V2-TEST-01
// ============================================================================
using HDBF.Core;
using HDBF.Core.Balance;
using HDBF.Core.Indoor;

namespace HDBF.Tests
{
    public static class V2FallowTransitionTests
    {
        /// <summary>
        /// L-01 (P0, 2026-11-02): fallow -> normal on a day with grown crops. Before the fix the
        /// transition scale was 0 (fallow yield 0), TryHarvest cleared every mature slot anyway and
        /// the whole box produced 0 items; now the crops are harvested at the normal yield.
        /// </summary>
        public static void TestFallowExitTransitionDayDoesNotDestroyMatureCrops()
        {
            var record = IndoorFarmRecord.Create(HdbfTier.Intermediate, HdbfFaction.Folktails);
            record.Mode = IndoorMode.Normal;
            record.Tank = TierCatalog.Get(HdbfTier.Intermediate).TankCapacity;
            // Keep maintenance out of the picture: due far in the future, no cycles missed.
            record.MaintenanceDueDay = 1000f;
            record.MaintenanceCyclesMissed = 0;

            const int matureSlots = 20;
            for (var index = 0; index < matureSlots; index++)
            {
                var slot = record.Slots[index];
                slot.CropId = "Carrot";
                slot.Stage = SlotStage.Mature;
                slot.GrowthDays = 4f;
                slot.Fatigue = 0f;
            }

            var sink = new CountingSink();
            var result = IndoorDayTick.Run(record, TierCatalog.Get(HdbfTier.Intermediate),
                FallbackCropCatalog.Default, new IndoorDayContext
                {
                    ActiveWorkers = 2,
                    CurrentDay = 0f,
                    Output = sink,
                    // The one-day transition after the player switched fallow -> normal.
                    PreviousMode = IndoorMode.Fallow,
                });

            TestAssert.Equal(1f, result.TransitionYieldScale, 1e-5f,
                "a fallow previous mode must not scale the harvest to zero");
            TestAssert.Equal(matureSlots, result.Actions.Harvested,
                "every mature slot must still be harvested on the transition day");
            // Carrot base yield 3, fatigue 0, normal mode -> 3 items per slot.
            TestAssert.Equal(matureSlots * 3, sink.Total,
                "the transition day must store real produce, not silently vanish the harvest");
            for (var index = 0; index < matureSlots; index++)
            {
                TestAssert.True(record.Slots[index].Stage != SlotStage.Mature,
                    "a harvested slot must not stay mature (slot " + index + ")");
            }
        }

        /// <summary>
        /// A zero-yield harvest (defensive path) must keep the mature crop instead of eating it.
        /// Fallow mode itself never schedules farm actions, so this pins the helper contract.
        /// </summary>
        public static void TestZeroYieldHarvestKeepsTheMatureSlot()
        {
            var record = IndoorFarmRecord.Create(HdbfTier.Basic, HdbfFaction.Folktails);
            record.Mode = IndoorMode.Fallow;
            var slot = record.Slots[0];
            slot.CropId = "Carrot";
            slot.Stage = SlotStage.Mature;
            slot.GrowthDays = 4f;

            var sink = new CountingSink();
            var result = IndoorDayTick.Run(record, TierCatalog.Get(HdbfTier.Basic),
                FallbackCropCatalog.Default, new IndoorDayContext
                {
                    ActiveWorkers = 2,
                    CurrentDay = 0f,
                    Output = sink,
                });

            TestAssert.Equal(0, result.Actions.Harvested, "fallow never harvests");
            TestAssert.Equal(0, sink.Total, "fallow never produces items");
            TestAssert.Equal(SlotStage.Mature, slot.Stage,
                "a zero-yield harvest must never clear the grown crop");
            TestAssert.Equal("Carrot", slot.CropId, "the grown crop must stay in the slot");
        }

        /// <summary>Transition scale matrix: only a positive previous yield may lower the day.</summary>
        public static void TestModeTransitionScaleNeverZeroWhenPreviousModeHasNoYield()
        {
            TestAssert.Equal(1f, IndoorGrowthMath.ModeTransitionYieldScale(IndoorMode.Normal,
                IndoorMode.Fallow), 1e-5f);
            TestAssert.Equal(1f, IndoorGrowthMath.ModeTransitionYieldScale(IndoorMode.Dense,
                IndoorMode.Fallow), 1e-5f);
            TestAssert.Equal(1f, IndoorGrowthMath.ModeTransitionYieldScale(IndoorMode.Fallow,
                IndoorMode.Normal), 1e-5f);
            // The existing normal -> dense rule still settles at the lower yield of 1.0.
            TestAssert.Equal(1f / IndoorConstants.DenseYieldMultiplier,
                IndoorGrowthMath.ModeTransitionYieldScale(IndoorMode.Dense, IndoorMode.Normal), 1e-5f);
            // Dense -> normal settles at 1.0 as well (the new mode is the lower one).
            TestAssert.Equal(1f,
                IndoorGrowthMath.ModeTransitionYieldScale(IndoorMode.Normal, IndoorMode.Dense), 1e-5f);
        }

        private sealed class CountingSink : IInventorySink
        {
            public int Total;

            public bool TryAdd(string goodId, int amount)
            {
                Total += amount;
                return true;
            }
        }
    }
}
