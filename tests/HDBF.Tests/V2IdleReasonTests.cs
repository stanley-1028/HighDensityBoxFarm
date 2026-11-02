// ============================================================================
// HDBF v2 | Layer: L1 test | Owner: V2-CORE-03
// Purpose: WP-06 idle-reason contract: one deterministic priority order over the
//          immutable panel snapshot plus the localization key of every reason
//          (L-03/L-10/L-11 seam). Pure Core function, no game objects.
// Requirements: REQ-V2-UI-01, REQ-V2-READ-04, REQ-V2-BAL-02
// ============================================================================
using HDBF.Core;
using HDBF.Core.Indoor;

namespace HDBF.Tests
{
    public static class V2IdleReasonTests
    {
        public static void TestIdleReasonIsNoneWhenTheFarmIsWorking()
        {
            TestAssert.Equal(IndoorIdleReason.None, IndoorIdleReasonMath.Resolve(Healthy()));
            TestAssert.Equal(IndoorIdleReason.None, IndoorIdleReasonMath.Resolve(null),
                "a null snapshot is 'no information', never an exception");
        }

        public static void TestIdleReasonPriorityOrderIsFixed()
        {
            var healthy = Healthy();

            // Fallow is the lowest positive reason: it only shows once nothing is wrong.
            TestAssert.Equal(IndoorIdleReason.Fallow,
                IndoorIdleReasonMath.Resolve(healthy with { Mode = IndoorMode.Fallow }));

            // Maintenance due, then stalled (stalled outranks due).
            TestAssert.Equal(IndoorIdleReason.MaintenanceDue,
                IndoorIdleReasonMath.Resolve(healthy with { MaintenanceCyclesMissed = 1 }));
            TestAssert.Equal(IndoorIdleReason.MaintenanceStalled,
                IndoorIdleReasonMath.Resolve(healthy with { MaintenanceCyclesMissed = 2 }));

            // Water shortage outranks maintenance.
            TestAssert.Equal(IndoorIdleReason.WaterShortage,
                IndoorIdleReasonMath.Resolve(healthy with
                {
                    MaintenanceCyclesMissed = 3,
                    Warnings = IndoorWarningFlags.NoWater,
                }));

            // Output full outranks water.
            TestAssert.Equal(IndoorIdleReason.OutputFull,
                IndoorIdleReasonMath.Resolve(healthy with
                {
                    Warnings = IndoorWarningFlags.OutputFull | IndoorWarningFlags.NoWater,
                }));

            // Entrance blocked outranks output.
            TestAssert.Equal(IndoorIdleReason.EntranceBlocked,
                IndoorIdleReasonMath.Resolve(healthy with
                {
                    Warnings = IndoorWarningFlags.EntranceBlocked | IndoorWarningFlags.OutputFull,
                }));

            // No workers outranks every warning.
            TestAssert.Equal(IndoorIdleReason.NoWorkers,
                IndoorIdleReasonMath.Resolve(healthy with
                {
                    Workers = 0,
                    Warnings = IndoorWarningFlags.EntranceBlocked | IndoorWarningFlags.NoWater,
                }));

            // Paused outranks everything: the whole simulation is stopped.
            TestAssert.Equal(IndoorIdleReason.Paused,
                IndoorIdleReasonMath.Resolve(healthy with
                {
                    Paused = true,
                    Workers = 0,
                    Warnings = IndoorWarningFlags.MaintenanceStalled | IndoorWarningFlags.NoWater,
                }));
        }

        public static void TestIdleReasonUsesLiveCoverageWhenTheFlagsAreStale()
        {
            // L-03/L-11: the panel reads live coverage; a below-minimum tank is a water shortage even
            // before the next daily tick refreshes the warning flags.
            var snapshot = Healthy() with { Coverage = 0.1f, MinCoverage = 0.5f };
            TestAssert.Equal(IndoorIdleReason.WaterShortage, IndoorIdleReasonMath.Resolve(snapshot));

            var lowFlag = Healthy() with { Warnings = IndoorWarningFlags.LowWater };
            TestAssert.Equal(IndoorIdleReason.WaterShortage, IndoorIdleReasonMath.Resolve(lowFlag));

            var noMinimum = Healthy() with { Coverage = 0f, MinCoverage = 0f };
            TestAssert.Equal(IndoorIdleReason.None, IndoorIdleReasonMath.Resolve(noMinimum),
                "a tier without a minimum coverage is never idle for water");
        }

        public static void TestIdleReasonLocalizationKeysAreStable()
        {
            TestAssert.Equal("HDBF.IdleReason.None",
                IndoorIdleReasonMath.LocalizationKey(IndoorIdleReason.None));
            TestAssert.Equal("HDBF.IdleReason.Paused",
                IndoorIdleReasonMath.LocalizationKey(IndoorIdleReason.Paused));
            TestAssert.Equal("HDBF.IdleReason.NoWorkers",
                IndoorIdleReasonMath.LocalizationKey(IndoorIdleReason.NoWorkers));
            TestAssert.Equal("HDBF.IdleReason.EntranceBlocked",
                IndoorIdleReasonMath.LocalizationKey(IndoorIdleReason.EntranceBlocked));
            TestAssert.Equal("HDBF.IdleReason.OutputFull",
                IndoorIdleReasonMath.LocalizationKey(IndoorIdleReason.OutputFull));
            TestAssert.Equal("HDBF.IdleReason.WaterShortage",
                IndoorIdleReasonMath.LocalizationKey(IndoorIdleReason.WaterShortage));
            TestAssert.Equal("HDBF.IdleReason.MaintenanceStalled",
                IndoorIdleReasonMath.LocalizationKey(IndoorIdleReason.MaintenanceStalled));
            TestAssert.Equal("HDBF.IdleReason.MaintenanceDue",
                IndoorIdleReasonMath.LocalizationKey(IndoorIdleReason.MaintenanceDue));
            TestAssert.Equal("HDBF.IdleReason.Fallow",
                IndoorIdleReasonMath.LocalizationKey(IndoorIdleReason.Fallow));
            TestAssert.Equal("HDBF.IdleReason.None",
                IndoorIdleReasonMath.LocalizationKey((IndoorIdleReason)99),
                "an undefined value must never render a raw number");
        }

        public static void TestIdleReasonEnumValuesArePartOfThePanelContract()
        {
            TestAssert.Equal(0, (int)IndoorIdleReason.None);
            TestAssert.Equal(1, (int)IndoorIdleReason.Paused);
            TestAssert.Equal(2, (int)IndoorIdleReason.NoWorkers);
            TestAssert.Equal(3, (int)IndoorIdleReason.EntranceBlocked);
            TestAssert.Equal(4, (int)IndoorIdleReason.OutputFull);
            TestAssert.Equal(5, (int)IndoorIdleReason.WaterShortage);
            TestAssert.Equal(6, (int)IndoorIdleReason.MaintenanceStalled);
            TestAssert.Equal(7, (int)IndoorIdleReason.MaintenanceDue);
            TestAssert.Equal(8, (int)IndoorIdleReason.Fallow);
        }

        // ---------------------------------------------------------------- helpers

        private static IndoorFarmSnapshot Healthy()
        {
            return new IndoorFarmSnapshot(
                HdbfTier.Intermediate,
                HdbfFaction.Folktails,
                2,
                2,
                IndoorMode.Normal,
                false,
                60,
                60,
                60,
                10,
                30,
                0,
                0,
                10.8f,
                10.8f,
                10.8f,
                1f,
                0.5f,
                WaterSupplyKind.None,
                10f,
                0,
                false,
                PowerConnection.NotRequired,
                false,
                CropPolicy.Rotation,
                string.Empty,
                string.Empty,
                0,
                false,
                true,
                true,
                false,
                IndoorWarningFlags.None,
                IndoorSaveLimits.AdapterStateNone);
        }
    }
}
