// ============================================================================
// HDBF v2 | Layer: L1 test | Owner: V2-CORE-01
// Purpose: Growth boundaries, growth multiplier combinations, yield formula and
//          harvest rounding for the pure indoor math (section 19.5 / section 19.14).
// Requirements: REQ-V2-CORE-04, REQ-V2-BAL-01, REQ-V2-TEST-01
// ============================================================================
using HDBF.Core;
using HDBF.Core.Indoor;

namespace HDBF.Tests
{
    public static class V2GrowthTests
    {
        // ---------------------------------------------------------------- slot growth

        public static void TestZeroDaysDoesNotAdvance()
        {
            var slot = Growing("Carrot", 1f);
            IndoorGrowthMath.Advance(slot, Carrot(), 0f, 1f);
            TestAssert.Equal(1f, slot.GrowthDays);
            TestAssert.Equal(SlotStage.Growing, slot.Stage);
        }

        public static void TestNegativeOrNaNStepDoesNotAdvance()
        {
            var slot = Growing("Carrot", 1f);
            IndoorGrowthMath.Advance(slot, Carrot(), -1f, 1f);
            TestAssert.Equal(1f, slot.GrowthDays);
            IndoorGrowthMath.Advance(slot, Carrot(), float.NaN, 1f);
            TestAssert.Equal(1f, slot.GrowthDays);
        }

        public static void TestPartialDayScalesByGrowthMultiplier()
        {
            var slot = Growing("Carrot", 0f);
            IndoorGrowthMath.Advance(slot, Carrot(), 1f, 0.5f);
            TestAssert.Equal(0.5f, slot.GrowthDays, 1e-5f);
            TestAssert.Equal(SlotStage.Growing, slot.Stage);
        }

        public static void TestExactBoundaryBecomesMature()
        {
            var slot = Growing("Carrot", 3f);
            IndoorGrowthMath.Advance(slot, Carrot(), 1f, 1f);
            TestAssert.Equal(4f, slot.GrowthDays, 1e-5f);
            TestAssert.Equal(SlotStage.Mature, slot.Stage);
        }

        public static void TestOverflowClampsAtGrowthTime()
        {
            var slot = Growing("Carrot", 3.5f);
            IndoorGrowthMath.Advance(slot, Carrot(), 1f, 2f);
            TestAssert.Equal(4f, slot.GrowthDays, 1e-5f, "progress must clamp at GrowthTimeInDays");
            TestAssert.Equal(SlotStage.Mature, slot.Stage);
        }

        public static void TestZeroGrowthMultiplierStopsButKeepsSlot()
        {
            var slot = Growing("Carrot", 2f);
            IndoorGrowthMath.Advance(slot, Carrot(), 1f, 0f);
            TestAssert.Equal(2f, slot.GrowthDays);
            TestAssert.Equal(SlotStage.Growing, slot.Stage);
            TestAssert.Equal("Carrot", slot.CropId);
        }

        public static void TestEmptySlotDoesNotAdvance()
        {
            var slot = new IndoorSlot { Stage = SlotStage.Empty, CropId = string.Empty };
            IndoorGrowthMath.Advance(slot, Carrot(), 1f, 1f);
            TestAssert.Equal(0f, slot.GrowthDays);
            TestAssert.Equal(SlotStage.Empty, slot.Stage);
        }

        public static void TestMatureSlotDoesNotAdvance()
        {
            var slot = Growing("Carrot", 4f);
            slot.Stage = SlotStage.Mature;
            IndoorGrowthMath.Advance(slot, Carrot(), 1f, 1f);
            TestAssert.Equal(4f, slot.GrowthDays);
            TestAssert.Equal(SlotStage.Mature, slot.Stage);
        }

        public static void TestGrowingStageWithoutCropDoesNotAdvance()
        {
            var slot = new IndoorSlot { Stage = SlotStage.Growing, CropId = string.Empty };
            IndoorGrowthMath.Advance(slot, Carrot(), 1f, 1f);
            TestAssert.Equal(0f, slot.GrowthDays);
        }

        public static void TestInvalidCropGrowthTimeMaturesImmediately()
        {
            // Missing/invalid spec (GrowthTimeInDays <= 0) must not leave a slot stuck forever.
            var slot = Growing("Carrot", 0f);
            IndoorGrowthMath.Advance(slot, new CropDefinition("Carrot", 0f, "Carrot", 3), 1f, 1f);
            TestAssert.Equal(SlotStage.Mature, slot.Stage);
        }

        public static void TestAdvanceNullArgumentsAreNoOps()
        {
            var slot = Growing("Carrot", 1f);
            IndoorGrowthMath.Advance(null!, Carrot(), 1f, 1f);
            IndoorGrowthMath.Advance(slot, null!, 1f, 1f);
            TestAssert.Equal(1f, slot.GrowthDays);
        }

        // ---------------------------------------------------------------- multipliers

        public static void TestFatigueYieldMultiplierBoundaries()
        {
            TestAssert.Equal(1f, IndoorGrowthMath.FatigueYieldMultiplier(0f));
            TestAssert.Equal(0.7f, IndoorGrowthMath.FatigueYieldMultiplier(0.5f), 1e-5f);
            TestAssert.Equal(0.4f, IndoorGrowthMath.FatigueYieldMultiplier(1f), 1e-5f);
        }

        public static void TestFatigueYieldMultiplierClampsInvalidInput()
        {
            TestAssert.Equal(0.4f, IndoorGrowthMath.FatigueYieldMultiplier(5f), 1e-5f);
            TestAssert.Equal(1f, IndoorGrowthMath.FatigueYieldMultiplier(-5f), 1e-5f);
            TestAssert.Equal(1f, IndoorGrowthMath.FatigueYieldMultiplier(float.NaN), 1e-5f);
        }

        public static void TestModeYieldMultipliers()
        {
            TestAssert.Equal(1f, IndoorGrowthMath.ModeYieldMultiplier(IndoorMode.Normal));
            TestAssert.Equal(1.35f, IndoorGrowthMath.ModeYieldMultiplier(IndoorMode.Dense), 1e-5f);
            TestAssert.Equal(0f, IndoorGrowthMath.ModeYieldMultiplier(IndoorMode.Fallow));
        }

        public static void TestYieldCombinesFatigueAndMode()
        {
            TestAssert.Equal(3f, IndoorGrowthMath.Yield(3, 0f, IndoorMode.Normal), 1e-5f);
            TestAssert.Equal(2.1f, IndoorGrowthMath.Yield(3, 0.5f, IndoorMode.Normal), 1e-5f);
            TestAssert.Equal(2.835f, IndoorGrowthMath.Yield(3, 0.5f, IndoorMode.Dense), 1e-5f);
            TestAssert.Equal(0f, IndoorGrowthMath.Yield(3, 0f, IndoorMode.Fallow));
        }

        public static void TestYieldGuardsInvalidBaseAndFatigue()
        {
            TestAssert.Equal(0f, IndoorGrowthMath.Yield(0, 0f, IndoorMode.Normal));
            TestAssert.Equal(0f, IndoorGrowthMath.Yield(-3, 0f, IndoorMode.Normal));
            TestAssert.Equal(3f, IndoorGrowthMath.Yield(3, -5f, IndoorMode.Normal), 1e-5f);
            TestAssert.Equal(3f, IndoorGrowthMath.Yield(3, float.NaN, IndoorMode.Normal), 1e-5f);
        }

        public static void TestHarvestItemsRoundsAndKeepsPositiveYield()
        {
            TestAssert.Equal(3, IndoorGrowthMath.HarvestItems(3, 0f, IndoorMode.Normal));
            TestAssert.Equal(4, IndoorGrowthMath.HarvestItems(3, 0f, IndoorMode.Dense));
            TestAssert.Equal(2, IndoorGrowthMath.HarvestItems(3, 0.5f, IndoorMode.Normal));
            TestAssert.Equal(1, IndoorGrowthMath.HarvestItems(1, 1f, IndoorMode.Normal), "a positive yield never becomes 0");
            TestAssert.Equal(0, IndoorGrowthMath.HarvestItems(1, 0f, IndoorMode.Fallow));
        }

        public static void TestGrowthMultiplierIsSingleMultiplication()
        {
            TestAssert.Equal(0.15f, new IndoorMultipliers(0.5f, 0.5f, 0.6f).Growth, 1e-5f);
            TestAssert.Equal(1f, new IndoorMultipliers(1f, 1f, 1f).Growth, 1e-5f);
            TestAssert.Equal(0f, new IndoorMultipliers(0f, 1f, 1f).Growth);
            TestAssert.Equal(0.5f, new IndoorMultipliers(1f, 0.5f, 1f).Growth, 1e-5f);
            TestAssert.Equal(0.6f, new IndoorMultipliers(1f, 1f, 0.6f).Growth, 1e-5f);
        }

        public static void TestGrowthMultiplierClampsInvalidFactors()
        {
            TestAssert.Equal(0f, new IndoorMultipliers(float.NaN, 1f, 1f).Growth);
            TestAssert.Equal(1f, new IndoorMultipliers(2f, 1f, 1f).Growth);
            TestAssert.Equal(0f, new IndoorMultipliers(-1f, 1f, 1f).Growth);
            TestAssert.Equal(0f, new IndoorMultipliers(0.5f, float.NaN, 1f).Growth);
        }

        // ---------------------------------------------------------------- helpers

        private static IndoorSlot Growing(string cropId, float progress)
        {
            return new IndoorSlot
            {
                CropId = cropId,
                GrowthDays = progress,
                Stage = SlotStage.Growing,
                LastCropId = string.Empty,
            };
        }

        private static CropDefinition Carrot() => new CropDefinition("Carrot", 4f, "Carrot", 3);
    }
}
