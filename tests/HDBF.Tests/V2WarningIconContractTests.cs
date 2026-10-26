// ============================================================================
// HDBF v2 | Layer: L1 test | Owner: V2-GAMEPLAY-02
// Purpose: Contract of the four v2 world-icon classes: flag -> class/priority map, the pure
//          icon scheduling policy and the key parity with the shipped UI localization keys.
//          Replaces the v1 WarningIconContractTests (aggregation/hysteresis chain deleted).
// Requirements: REQ-V2-BAL-03, REQ-V2-WATER-03, REQ-V2-UI-02, REQ-V2-READ-03
// ============================================================================
using System;
using HDBF.Core.Indoor;
using HDBF.UI.Localization;

namespace HDBF.Tests
{
    /// <summary>
    /// L1 contract of the v2 warning icon mapping (section 19.11). The gameplay coordinator cannot
    /// be loaded here (Timberborn/Unity references), so the pure Core map and policy are exercised
    /// directly; the source-level wiring is pinned by <see cref="V2GameplayWiringTests"/>.
    /// </summary>
    public static class V2WarningIconContractTests
    {
        public static void TestMapHasExactlyFourClassesForTheFourCategories()
        {
            TestAssert.Equal(1, (int)IndoorWarningIconKind.Water);
            TestAssert.Equal(2, (int)IndoorWarningIconKind.Maintenance);
            TestAssert.Equal(3, (int)IndoorWarningIconKind.NoPower);
            TestAssert.Equal(4, (int)IndoorWarningIconKind.EntranceBlocked);
            TestAssert.Equal(4, IndoorWarningMap.WorldIconKindCount);
            TestAssert.Equal(4, IndoorWarningMap.MaxWorldIconKind);

            TestAssert.Equal(IndoorWarningIconKind.Water,
                IndoorWarningMap.IconKindOf(IndoorWarningFlags.NoWater));
            TestAssert.Equal(IndoorWarningIconKind.Water,
                IndoorWarningMap.IconKindOf(IndoorWarningFlags.LowWater));
            TestAssert.Equal(IndoorWarningIconKind.Maintenance,
                IndoorWarningMap.IconKindOf(IndoorWarningFlags.MaintenanceDue));
            TestAssert.Equal(IndoorWarningIconKind.Maintenance,
                IndoorWarningMap.IconKindOf(IndoorWarningFlags.MaintenanceStalled));
            TestAssert.Equal(IndoorWarningIconKind.NoPower,
                IndoorWarningMap.IconKindOf(IndoorWarningFlags.NoPower));
            TestAssert.Equal(IndoorWarningIconKind.EntranceBlocked,
                IndoorWarningMap.IconKindOf(IndoorWarningFlags.EntranceBlocked));
            TestAssert.Equal(IndoorWarningIconKind.None,
                IndoorWarningMap.IconKindOf(IndoorWarningFlags.OutputFull
                                             | IndoorWarningFlags.NoWorkers
                                             | IndoorWarningFlags.MissingMaterial));
        }

        public static void TestMapPriorityIsP0WaterFirstThenMaintenancePowerEntrance()
        {
            var all = IndoorWarningFlags.NoPower | IndoorWarningFlags.EntranceBlocked
                      | IndoorWarningFlags.MaintenanceDue | IndoorWarningFlags.LowWater;
            TestAssert.Equal(IndoorWarningIconKind.Water, IndoorWarningMap.IconKindOf(all));

            var maintenance = IndoorWarningFlags.NoPower | IndoorWarningFlags.EntranceBlocked
                              | IndoorWarningFlags.MaintenanceDue;
            TestAssert.Equal(IndoorWarningIconKind.Maintenance, IndoorWarningMap.IconKindOf(maintenance));

            var power = IndoorWarningFlags.NoPower | IndoorWarningFlags.EntranceBlocked;
            TestAssert.Equal(IndoorWarningIconKind.NoPower, IndoorWarningMap.IconKindOf(power));
        }

        public static void TestSeverityMatchesTheFourPanelCategories()
        {
            TestAssert.Equal(IndoorWarningSeverity.Critical,
                IndoorWarningMap.SeverityOf(IndoorWarningFlags.NoWater));
            TestAssert.Equal(IndoorWarningSeverity.Critical,
                IndoorWarningMap.SeverityOf(IndoorWarningFlags.MaintenanceStalled));
            TestAssert.Equal(IndoorWarningSeverity.High,
                IndoorWarningMap.SeverityOf(IndoorWarningFlags.LowWater));
            TestAssert.Equal(IndoorWarningSeverity.High,
                IndoorWarningMap.SeverityOf(IndoorWarningFlags.NoPower));
            TestAssert.Equal(IndoorWarningSeverity.High,
                IndoorWarningMap.SeverityOf(IndoorWarningFlags.EntranceBlocked));
            TestAssert.Equal(IndoorWarningSeverity.Medium,
                IndoorWarningMap.SeverityOf(IndoorWarningFlags.MaintenanceDue));
            TestAssert.Equal(IndoorWarningSeverity.Info,
                IndoorWarningMap.SeverityOf(IndoorWarningFlags.None));

            TestAssert.True(IndoorWarningMap.IsCritical(IndoorWarningFlags.NoWater));
            TestAssert.True(IndoorWarningMap.IsCritical(IndoorWarningFlags.MaintenanceStalled));
            TestAssert.False(IndoorWarningMap.IsCritical(IndoorWarningFlags.LowWater));
            TestAssert.False(IndoorWarningMap.IsCritical(IndoorWarningFlags.None));
        }

        public static void TestEveryFlagCombinationMapsToAKnownClassAndStableSlot()
        {
            // 9 flags -> 512 combinations; the map must never invent a fifth class and the slot
            // table must stay inside the fixed StatusToggle array.
            for (var bits = 0; bits <= 511; bits++)
            {
                var flags = (IndoorWarningFlags)bits;
                var kind = IndoorWarningMap.IconKindOf(flags);
                TestAssert.True((int)kind >= 0 && (int)kind <= IndoorWarningMap.MaxWorldIconKind,
                    "class out of range for flags " + flags);
                var slot = IndoorWarningMap.SlotOf(kind);
                TestAssert.True(slot >= 0 && slot <= IndoorWarningMap.MaxWorldIconKind,
                    "slot out of range for flags " + flags);
            }

            TestAssert.Equal(0, IndoorWarningMap.SlotOf(IndoorWarningIconKind.None));
            TestAssert.Equal(1, IndoorWarningMap.SlotOf(IndoorWarningIconKind.Water));
            TestAssert.Equal(4, IndoorWarningMap.SlotOf(IndoorWarningIconKind.EntranceBlocked));
        }

        public static void TestWarningKeysMatchTheShippedUiConstants()
        {
            TestAssert.Equal(HdbfUiLocKeys.WarningNoWater, IndoorWarningKeys.NoWater);
            TestAssert.Equal(HdbfUiLocKeys.WarningIrrigationLow, IndoorWarningKeys.IrrigationLow);
            TestAssert.Equal(HdbfUiLocKeys.WarningMaintenanceOverdue, IndoorWarningKeys.MaintenanceOverdue);
            TestAssert.Equal(HdbfUiLocKeys.WarningNoPower, IndoorWarningKeys.NoPower);
            TestAssert.Equal(HdbfUiLocKeys.WarningEntranceBlocked, IndoorWarningKeys.EntranceBlocked);

            TestAssert.Equal(IndoorWarningKeys.NoWater,
                IndoorWarningMap.WarningKeyOf(IndoorWarningIconKind.Water));
            TestAssert.Equal(IndoorWarningKeys.MaintenanceOverdue,
                IndoorWarningMap.WarningKeyOf(IndoorWarningIconKind.Maintenance));
            TestAssert.Equal(IndoorWarningKeys.NoPower,
                IndoorWarningMap.WarningKeyOf(IndoorWarningIconKind.NoPower));
            TestAssert.Equal(IndoorWarningKeys.EntranceBlocked,
                IndoorWarningMap.WarningKeyOf(IndoorWarningIconKind.EntranceBlocked));
        }

        public static void TestVisibilityPolicyCullsOffscreenFarAndNonFiniteInput()
        {
            TestAssert.True(IndoorIconPolicy.IsWorldIconVisible(true, 10f, 40f));
            TestAssert.True(IndoorIconPolicy.IsWorldIconVisible(true, 40f, 40f));
            TestAssert.False(IndoorIconPolicy.IsWorldIconVisible(false, 10f, 40f), "off screen");
            TestAssert.False(IndoorIconPolicy.IsWorldIconVisible(true, 40.5f, 40f), "too far");
            TestAssert.False(IndoorIconPolicy.IsWorldIconVisible(true, float.NaN, 40f), "NaN distance");
            TestAssert.False(IndoorIconPolicy.IsWorldIconVisible(true, float.PositiveInfinity, 40f),
                "infinite distance");
            TestAssert.False(IndoorIconPolicy.IsWorldIconVisible(true, 10f, float.NaN), "NaN budget");
            TestAssert.False(IndoorIconPolicy.IsWorldIconVisible(true, 10f, -1f), "negative budget");
        }

        public static void TestBlinkPolicyIsAtMostOncePerSecondAndOnlyForP0()
        {
            TestAssert.True(IndoorIconPolicy.ShouldBlink(true, true));
            TestAssert.False(IndoorIconPolicy.ShouldBlink(true, false), "the player switch wins");
            TestAssert.False(IndoorIconPolicy.ShouldBlink(false, true), "only P0 blinks");

            TestAssert.True(IndoorIconPolicy.IsBlinkOn(0.10, 1f));
            TestAssert.False(IndoorIconPolicy.IsBlinkOn(0.60, 1f));
            TestAssert.True(IndoorIconPolicy.IsBlinkOn(1.10, 1f), "phase wraps after one second");
            TestAssert.True(IndoorIconPolicy.IsBlinkOn(-0.10, 1f), "negative clocks use the magnitude");
            TestAssert.True(IndoorIconPolicy.IsBlinkOn(0.60, 0f), "a disabled rate never hides");
            TestAssert.True(IndoorIconPolicy.IsBlinkOn(double.NaN, 1f), "NaN clock never hides");
        }

        public static void TestPassScheduleHonoursTheInterval()
        {
            TestAssert.True(IndoorIconPolicy.IsDue(10d, 5d, 1f));
            TestAssert.False(IndoorIconPolicy.IsDue(4d, 5d, 1f));
            TestAssert.True(IndoorIconPolicy.IsDue(4d, 5d, 0f), "a zero interval is always due");
            TestAssert.True(IndoorIconPolicy.IsDue(double.NaN, 5d, 1f), "a broken clock runs the pass");
            TestAssert.Equal(1f, IndoorIconPolicy.DefaultIconIntervalSeconds);
            TestAssert.Equal(40f, IndoorIconPolicy.DefaultMaxDistance);
            TestAssert.Equal(1f, IndoorIconPolicy.DefaultBlinkHz);
        }
    }
}
