using System;
using HDBF.Core;
using HDBF.Core.Balance;

namespace HDBF.Tests
{
    /// <summary>
    /// L1 tests for chapter 6.4 (fatigue, TC-19), chapter 6.5 (maintenance TC-20 and power
    /// TC-05), chapter 6.7 (mode multipliers), the chapter 6.1 yield product and the chapter 6.3
    /// internal water tank. Expected values are the document's numbers.
    /// </summary>
    public static class BalanceFatiguePowerTankTests
    {
        // ---------------------------------------------------------------- G_fat (chapter 6.4)

        public static void TestFatigueMultiplierAtZeroHalfAndFull()
        {
            // G_fat = 1 - 0.60*F -> 1 / 0.7 / 0.4.
            TestAssert.Equal(1f, BalanceFormulas.FatigueMultiplier(0f));
            TestAssert.Equal(0.7f, BalanceFormulas.FatigueMultiplier(0.5f));
            TestAssert.Equal(0.4f, BalanceFormulas.FatigueMultiplier(1f));
        }

        public static void TestFatigueMultiplierClampsAndHandlesNaN()
        {
            TestAssert.Equal(1f, BalanceFormulas.FatigueMultiplier(-1f));
            TestAssert.Equal(0.4f, BalanceFormulas.FatigueMultiplier(2f));
            TestAssert.Equal(1f, BalanceFormulas.FatigueMultiplier(float.NaN));
        }

        public static void TestFatigueMultiplierHonoursConfigurablePenalty()
        {
            TestAssert.Equal(0.75f, BalanceFormulas.FatigueMultiplier(0.5f, 0.5f));
            TestAssert.Equal(1f, BalanceFormulas.FatigueMultiplier(0.5f, 0f));
            // Defensive clamp of the coefficient itself (chapter 18.5 range 0.2..0.9).
            TestAssert.Equal(0f, BalanceFormulas.FatigueMultiplier(1f, 2f));
        }

        public static void TestFatigueDeltaSameCropPerTier()
        {
            // chapter 6.4: 0.08 x fatigue factor = 0.080 / 0.088 / 0.100 / 0.120.
            TestAssert.Equal(0.080f, BalanceFormulas.FatigueDelta(true, 1.00f), 1e-6f);
            TestAssert.Equal(0.088f, BalanceFormulas.FatigueDelta(true, 1.10f), 1e-6f);
            TestAssert.Equal(0.100f, BalanceFormulas.FatigueDelta(true, 1.25f), 1e-6f);
            TestAssert.Equal(0.120f, BalanceFormulas.FatigueDelta(true, 1.50f), 1e-6f);
        }

        public static void TestFatigueDeltaRotationIsFortyPercentPlusRecovery()
        {
            // TC-19: rotation increment is x0.4; 0.08*1.0*0.4 = 0.032.
            TestAssert.Equal(0.032f, BalanceFormulas.FatigueDelta(false, 1.00f), 1e-6f);
            TestAssert.Equal(0.048f, BalanceFormulas.FatigueDelta(false, 1.50f), 1e-6f);
        }

        public static void TestFatigueDeltaHonoursConfigurableRotationFactor()
        {
            TestAssert.Equal(0.080f, BalanceFormulas.FatigueDelta(false, 1f, 0.08f, 1.0f), 1e-6f);
            TestAssert.Equal(0.008f, BalanceFormulas.FatigueDelta(false, 1f, 0.08f, 0.1f), 1e-6f);
        }

        public static void TestFatigueDeltaNegativeFactorCountsAsZero()
        {
            TestAssert.Equal(0f, BalanceFormulas.FatigueDelta(true, -1f));
        }

        public static void TestThreeSameCropsThenRotation()
        {
            // TC-19: three same-crop harvests add 0.24, then one rotation adds 0.032 and -0.05.
            var fatigue = 0f;
            for (var harvest = 0; harvest < 3; harvest++)
            {
                fatigue = BalanceFormulas.ApplyHarvestFatigue(fatigue, true, 1.0f);
            }

            TestAssert.Equal(0.24f, fatigue, 1e-6f);
            fatigue = BalanceFormulas.ApplyHarvestFatigue(fatigue, false, 1.0f);
            TestAssert.Equal(0.222f, fatigue, 1e-6f);
        }

        public static void TestHarvestFatigueCapsAtOne()
        {
            TestAssert.Equal(1f, BalanceFormulas.ApplyHarvestFatigue(0.96f, true, 1.0f));
            TestAssert.Equal(1f, BalanceFormulas.ApplyHarvestFatigue(1f, true, 1.0f));
        }

        public static void TestRotatedHarvestAtCapStillRecoversFivePercent()
        {
            TestAssert.Equal(0.95f, BalanceFormulas.ApplyHarvestFatigue(1f, false, 1.0f), 1e-6f);
        }

        public static void TestFallowRecoveryMinusThreeHundredthsPerDay()
        {
            TestAssert.Equal(0.04f, BalanceFormulas.ApplyFallowRecovery(0.1f, 2), 1e-6f);
            TestAssert.Equal(0.1f, BalanceFormulas.ApplyFallowRecovery(0.1f, 0), 1e-6f);
            TestAssert.Equal(0f, BalanceFormulas.ApplyFallowRecovery(0.01f, 1), 1e-6f);
            TestAssert.Equal(0f, BalanceFormulas.ApplyFallowRecovery(0f, 5), 1e-6f);
        }

        public static void TestFallowRecoveryHonoursConfigurableRate()
        {
            TestAssert.Equal(0f, BalanceFormulas.ApplyFallowRecovery(0.2f, 2, 0.1f), 1e-6f);
        }

        public static void TestFatigueConstantsMatchChapterSixFour()
        {
            TestAssert.Equal(0.08f, TierCatalog.FatiguePerHarvest, 1e-6f);
            TestAssert.Equal(0.60f, TierCatalog.FatiguePenalty, 1e-6f);
            TestAssert.Equal(0.40f, TierCatalog.RotationFactor, 1e-6f);
            TestAssert.Equal(0.05f, TierCatalog.RotationRecovery, 1e-6f);
            TestAssert.Equal(0.03f, TierCatalog.FallowRecoveryPerDay, 1e-6f);
        }

        // ---------------------------------------------------------------- G_maint (chapter 6.5, TC-20)

        public static void TestMaintenanceMultiplierThreeLevels()
        {
            TestAssert.Equal(1f, BalanceFormulas.MaintenanceMultiplier(0));
            TestAssert.Equal(0.5f, BalanceFormulas.MaintenanceMultiplier(1));
            TestAssert.Equal(0f, BalanceFormulas.MaintenanceMultiplier(2));
            TestAssert.Equal(0f, BalanceFormulas.MaintenanceMultiplier(3));
            TestAssert.Equal(1f, BalanceFormulas.MaintenanceMultiplier(-1));
        }

        public static void TestShutdownStartsAtTwoMissedCycles()
        {
            TestAssert.False(BalanceFormulas.IsShutdown(0));
            TestAssert.False(BalanceFormulas.IsShutdown(1));
            TestAssert.True(BalanceFormulas.IsShutdown(2), "TC-20: >=2 missed cycles must shut the farm down");
        }

        public static void TestCyclesMissedBeforeDueDay()
        {
            TestAssert.Equal(0, BalanceFormulas.MaintenanceCyclesMissed(10f, 9f, 10f));
            TestAssert.Equal(0, BalanceFormulas.MaintenanceCyclesMissed(10f, 0f, 10f));
        }

        public static void TestCyclesMissedAtDueDayIsOne()
        {
            // Matches the reference pseudo-code in chapter 11.6.
            TestAssert.Equal(1, BalanceFormulas.MaintenanceCyclesMissed(10f, 10f, 10f));
        }

        public static void TestCyclesMissedAtDueDayPlusOneCycle()
        {
            TestAssert.Equal(2, BalanceFormulas.MaintenanceCyclesMissed(10f, 20f, 10f));
        }

        public static void TestCyclesMissedAtDueDayPlusTwoCycles()
        {
            TestAssert.Equal(3, BalanceFormulas.MaintenanceCyclesMissed(10f, 30f, 10f));
        }

        public static void TestCyclesMissedPartialCycleFloors()
        {
            TestAssert.Equal(2, BalanceFormulas.MaintenanceCyclesMissed(10f, 24.9f, 10f));
            TestAssert.Equal(1, BalanceFormulas.MaintenanceCyclesMissed(10f, 19.9f, 10f));
        }

        public static void TestCyclesMissedWithNonPositiveCycleIsZero()
        {
            TestAssert.Equal(0, BalanceFormulas.MaintenanceCyclesMissed(10f, 100f, 0f));
            TestAssert.Equal(0, BalanceFormulas.MaintenanceCyclesMissed(10f, 100f, -5f));
            TestAssert.Equal(0, BalanceFormulas.MaintenanceCyclesMissed(float.NaN, 100f, 10f));
        }

        // ---------------------------------------------------------------- G_power (TC-05)

        public static void TestPowerMultiplierConnectedAndOffGrid()
        {
            TestAssert.Equal(1f, BalanceFormulas.PowerMultiplier(true));
            TestAssert.Equal(0.6f, BalanceFormulas.PowerMultiplier(false));
        }

        public static void TestPowerMultiplierHonoursConfigurableOffGridEfficiency()
        {
            TestAssert.Equal(0.5f, BalanceFormulas.PowerMultiplier(false, 0.5f));
            TestAssert.Equal(1f, BalanceFormulas.PowerMultiplier(false, 2f));
            TestAssert.Equal(0f, BalanceFormulas.PowerMultiplier(false, float.NaN));
        }

        // ---------------------------------------------------------------- G_mode (chapter 6.7)

        public static void TestModeYieldMultipliers()
        {
            TestAssert.Equal(1.00f, BalanceFormulas.ModeYieldMultiplier(HdbfMode.Normal));
            TestAssert.Equal(1.35f, BalanceFormulas.ModeYieldMultiplier(HdbfMode.Dense));
            TestAssert.Equal(0f, BalanceFormulas.ModeYieldMultiplier(HdbfMode.Fallow));
        }

        public static void TestModeWaterMultipliers()
        {
            TestAssert.Equal(1.00f, BalanceFormulas.ModeWaterMultiplier(HdbfMode.Normal));
            TestAssert.Equal(1.50f, BalanceFormulas.ModeWaterMultiplier(HdbfMode.Dense));
            TestAssert.Equal(0.30f, BalanceFormulas.ModeWaterMultiplier(HdbfMode.Fallow));
        }

        public static void TestModeFatigueMultipliers()
        {
            TestAssert.Equal(1.00f, BalanceFormulas.ModeFatigueMultiplier(HdbfMode.Normal));
            TestAssert.Equal(1.50f, BalanceFormulas.ModeFatigueMultiplier(HdbfMode.Dense));
            TestAssert.Equal(0f, BalanceFormulas.ModeFatigueMultiplier(HdbfMode.Fallow));
        }

        public static void TestModeWorkMultipliers()
        {
            TestAssert.Equal(1.00f, BalanceFormulas.ModeWorkMultiplier(HdbfMode.Normal));
            TestAssert.Equal(1.20f, BalanceFormulas.ModeWorkMultiplier(HdbfMode.Dense));
            TestAssert.Equal(0.20f, BalanceFormulas.ModeWorkMultiplier(HdbfMode.Fallow));
        }

        public static void TestDenseNetLabourEfficiencyIsOnePointOneTwoFive()
        {
            // chapter 6.7: 1.35 / 1.20 = 1.125 (+12.5%).
            TestAssert.Equal(1.125f, BalanceFormulas.DenseNetLabourEfficiency(), 1e-6f);
            TestAssert.Equal(0f, BalanceFormulas.DenseNetLabourEfficiency(1.35f, 0f));
        }

        public static void TestModeYieldDefendsAgainstInvalidDenseOverride()
        {
            TestAssert.Equal(1f, BalanceFormulas.ModeYieldMultiplier(HdbfMode.Dense, 0.5f));
        }

        // ---------------------------------------------------------------- yield product (chapter 6.1)

        public static void TestPlotYieldNeutralLayersReturnBase()
        {
            TestAssert.Equal(10f, BalanceFormulas.PlotYield(10f, 1f, 1f, 1f, 1f, 1f));
        }

        public static void TestPlotYieldZeroWhenAnyLayerIsZero()
        {
            // REQ-BAL-02 / REQ-BAL-28: no layer may be skipped; any zero yields zero.
            TestAssert.Equal(0f, BalanceFormulas.PlotYield(10f, 0f, 1f, 1f, 1f, 1f), "G_irr=0");
            TestAssert.Equal(0f, BalanceFormulas.PlotYield(10f, 1f, 0f, 1f, 1f, 1f), "G_fat=0");
            TestAssert.Equal(0f, BalanceFormulas.PlotYield(10f, 1f, 1f, 0f, 1f, 1f), "G_maint=0");
            TestAssert.Equal(0f, BalanceFormulas.PlotYield(10f, 1f, 1f, 1f, 0f, 1f), "G_power=0");
            TestAssert.Equal(0f, BalanceFormulas.PlotYield(10f, 1f, 1f, 1f, 1f, 0f), "G_mode=0");
        }

        public static void TestPlotYield_DenseModeKeeps135Bonus()
        {
            // Regression (defect: PlotYield clamped G_mode to 1, deleting dense mode entirely).
            // G_mode is the only layer allowed above 1; 10 * 1.35 must remain 13.5.
            TestAssert.Equal(13.5f, BalanceFormulas.PlotYield(10f, 1f, 1f, 1f, 1f, 1.35f), 1e-5f);
            TestAssert.Equal(2f, BalanceFormulas.PlotYield(1f, 1f, 1f, 1f, 1f, 2f), 1e-5f);
        }

        public static void TestPlotYieldMultipliesEveryLayer()
        {
            // 10 x 0.5 x 0.7 x 0.5 x 0.6 x 1.35 = 1.4175.
            TestAssert.Equal(1.4175f, BalanceFormulas.PlotYield(10f, 0.5f, 0.7f, 0.5f, 0.6f, 1.35f), 1e-5f);
        }

        public static void TestPlotYieldRejectsInvalidInput()
        {
            TestAssert.Equal(0f, BalanceFormulas.PlotYield(0f, 1f, 1f, 1f, 1f, 1f));
            TestAssert.Equal(0f, BalanceFormulas.PlotYield(-1f, 1f, 1f, 1f, 1f, 1f));
            TestAssert.Equal(0f, BalanceFormulas.PlotYield(10f, float.NaN, 1f, 1f, 1f, 1f));
            TestAssert.Equal(0f, BalanceFormulas.PlotYield(10f, 1f, 1f, 1f, 1f, -1.35f));
            TestAssert.Equal(0f, BalanceFormulas.PlotYield(10f, 1f, 1f, 0f, 1f, 1.35f));
        }

        // ---------------------------------------------------------------- water tank (chapter 6.3)

        public static void TestTankCapacitiesMatchChapterFiveTable()
        {
            // 3.6 / 10.8 / 33.6 / 76.8 water units.
            TestAssert.Equal(3.6f, TierCatalog.Get(HdbfTier.Basic).TankCapacity, 1e-4f);
            TestAssert.Equal(10.8f, TierCatalog.Get(HdbfTier.Intermediate).TankCapacity, 1e-4f);
            TestAssert.Equal(33.6f, TierCatalog.Get(HdbfTier.Advanced).TankCapacity, 1e-4f);
            TestAssert.Equal(76.8f, TierCatalog.Get(HdbfTier.Top).TankCapacity, 1e-4f);
        }

        public static void TestDailyTankDrawPerMode()
        {
            // Basic full load: 60 x 0.020 = 1.2 / day, dense x1.5 = 1.8, fallow x0.3 = 0.36.
            TestAssert.Equal(1.2f, BalanceFormulas.DailyTankDraw(60, 0.020f, HdbfMode.Normal), 1e-5f);
            TestAssert.Equal(1.8f, BalanceFormulas.DailyTankDraw(60, 0.020f, HdbfMode.Dense), 1e-5f);
            TestAssert.Equal(0.36f, BalanceFormulas.DailyTankDraw(60, 0.020f, HdbfMode.Fallow), 1e-5f);
            TestAssert.Equal(0f, BalanceFormulas.DailyTankDraw(0, 0.020f, HdbfMode.Normal));
        }

        public static void TestTankDaysRemainingPerMode()
        {
            // Basic tank 3.6 units.
            TestAssert.Equal(3f, BalanceFormulas.TankDaysRemaining(3.6f, 60, 0.020f, HdbfMode.Normal), 1e-5f);
            TestAssert.Equal(2f, BalanceFormulas.TankDaysRemaining(3.6f, 60, 0.020f, HdbfMode.Dense), 1e-5f);
            TestAssert.Equal(10f, BalanceFormulas.TankDaysRemaining(3.6f, 60, 0.020f, HdbfMode.Fallow), 1e-5f);
        }

        public static void TestTopTierTankLastsFourDaysAtFullLoad()
        {
            // REQ-TEST-08: 600 x 0.032 = 19.2 / day; 76.8 / 19.2 = exactly 4 days.
            var days = BalanceFormulas.TankDaysRemaining(TierCatalog.Get(HdbfTier.Top).TankCapacity,
                600, 0.032f, HdbfMode.Normal);
            TestAssert.Equal(4f, days, 1e-5f);
        }

        public static void TestTankDaysNeverDividesByZero()
        {
            TestAssert.Equal(0f, BalanceFormulas.TankDaysRemaining(10f, 0, 0.02f, HdbfMode.Normal));
            TestAssert.Equal(0f, BalanceFormulas.TankDaysRemaining(0f, 60, 0.02f, HdbfMode.Normal));
            TestAssert.Equal(0f, BalanceFormulas.TankDaysRemaining(-5f, 60, 0.02f, HdbfMode.Normal));
            TestAssert.Equal(0f, BalanceFormulas.TankDaysRemaining(10f, 60, 0f, HdbfMode.Normal));
        }

        public static void TestTankDaysMiddleValues()
        {
            TestAssert.Equal(4.6875f, BalanceFormulas.TankDaysRemaining(90f, 600, 0.032f, HdbfMode.Normal), 1e-5f);
        }
    }
}
