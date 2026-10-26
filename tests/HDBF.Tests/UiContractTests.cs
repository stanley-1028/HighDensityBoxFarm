using System;
using System.Linq;
using HDBF.Core;
using HDBF.Core.Balance;
using HDBF.Core.Indoor;

namespace HDBF.Tests
{
    /// <summary>
    /// Offline contract tests for the data the v2 UI layer consumes (section 19.11,
    /// REQ-V2-UI-01..04) and for the remaining labour-cap formulas (chapter 6.2).
    ///
    /// Scope note, kept honest: the panel mapper lives in HDBF.UI and references Timberborn
    /// assemblies, so L1 cannot load that assembly directly (REQ-V2-TEST-01). The v2 panel
    /// mapping is tested through the linked game-free HdbfIndoor* sources in
    /// <see cref="V2PanelContractTests"/>; this class pins the Core contract the UI reads: the
    /// snapshot DTO shape, the warning severity order and the frozen enum values the save schema
    /// depends on. The v1 plot/save/warning DTOs were deleted with their subsystems.
    /// </summary>
    public static class UiContractTests
    {
        // ------------------------------------------------------- warning severity contract

        public static void TestWarningSeverityOrdinalsMatchP0ToP4()
        {
            // The UI sorts warning rows by severity number. Reordering or renumbering this enum
            // would silently change the panel order, so the numbers are a contract.
            TestAssert.Equal(0, (int)IndoorWarningSeverity.Critical, "P0 no water / maintenance stall");
            TestAssert.Equal(1, (int)IndoorWarningSeverity.High, "P1 irrigation low / no power / entrance");
            TestAssert.Equal(2, (int)IndoorWarningSeverity.Medium, "P2 maintenance due / missing material");
            TestAssert.Equal(3, (int)IndoorWarningSeverity.Low, "P3 output full / no workers");
            TestAssert.Equal(4, (int)IndoorWarningSeverity.Info, "P4 informational");
        }

        public static void TestWarningSeverityOrdinalSortIsP0First()
        {
            var values = new[]
            {
                IndoorWarningSeverity.Info,
                IndoorWarningSeverity.Medium,
                IndoorWarningSeverity.Critical,
                IndoorWarningSeverity.Low,
                IndoorWarningSeverity.High,
            };

            Array.Sort(values);
            TestAssert.Equal(IndoorWarningSeverity.Critical, values[0]);
            TestAssert.Equal(IndoorWarningSeverity.High, values[1]);
            TestAssert.Equal(IndoorWarningSeverity.Medium, values[2]);
            TestAssert.Equal(IndoorWarningSeverity.Low, values[3]);
            TestAssert.Equal(IndoorWarningSeverity.Info, values[4]);
        }

        public static void TestPanelModeLabelsHaveExactlyThreeModes()
        {
            // Three mode buttons (Normal/Dense/Fallow) are a chapter-14.3 UI contract.
            var modes = (HdbfMode[])Enum.GetValues(typeof(HdbfMode));
            TestAssert.Equal(3, modes.Length);
            TestAssert.Equal(HdbfMode.Normal, modes[0]);
            TestAssert.Equal(HdbfMode.Dense, modes[1]);
            TestAssert.Equal(HdbfMode.Fallow, modes[2]);
        }

        // ------------------------------------------------------- labour cap (chapter 6.2)

        public static void TestNoWorkersMeansNoEffectiveSlotsAndNoActionsForEveryTier()
        {
            // REQ-V2-LABOR-01 / TC-33: without farmers a farm may not plant, harvest or produce.
            foreach (var profile in TierCatalog.All)
            {
                TestAssert.Equal(0, BalanceFormulas.EffectivePlots(profile.PlotCapacity, 0),
                    "effective slots " + profile.Tier);
                TestAssert.Equal(0, BalanceFormulas.DailyPlotActions(0), "daily actions " + profile.Tier);
            }
        }

        public static void TestOneFarmerCoversExactlyThirtyEightSlotsForEveryTier()
        {
            foreach (var profile in TierCatalog.All)
            {
                var expected = Math.Min(profile.PlotCapacity, TierCatalog.PlotsPerFarmer);
                TestAssert.Equal(expected, BalanceFormulas.EffectivePlots(profile.PlotCapacity, 1),
                    "one farmer " + profile.Tier);
                TestAssert.Equal(13, BalanceFormulas.DailyPlotActions(1), "one farmer actions " + profile.Tier);
            }
        }

        public static void TestLabourCapAtFullCrewPerTier()
        {
            // P_eff = min(capacity, W_max x 38) and C_day = W_max x 13 (chapter 5.2 / 6.2).
            foreach (var profile in TierCatalog.All)
            {
                var expectedPlots = Math.Min(profile.PlotCapacity, profile.MaxWorkers * TierCatalog.PlotsPerFarmer);
                TestAssert.Equal(expectedPlots,
                    BalanceFormulas.EffectivePlots(profile.PlotCapacity, profile.MaxWorkers),
                    "full crew slots " + profile.Tier);
                TestAssert.Equal(profile.MaxWorkers * 13, BalanceFormulas.DailyPlotActions(profile.MaxWorkers),
                    "full crew actions " + profile.Tier);
            }

            var top = TierCatalog.Get(HdbfTier.Top);
            TestAssert.Equal(600, BalanceFormulas.EffectivePlots(top.PlotCapacity, top.MaxWorkers));
            TestAssert.Equal(208, BalanceFormulas.DailyPlotActions(top.MaxWorkers));
            TestAssert.True(16 * TierCatalog.PlotsPerFarmer > top.PlotCapacity, "fixture must exercise the clamp");
        }

        public static void TestDailyActionBudgetIsLinearAndCannotOverflow()
        {
            for (var workers = 0; workers <= 64; workers++)
            {
                TestAssert.Equal(workers * 13, BalanceFormulas.DailyPlotActions(workers), "workers " + workers);
            }

            TestAssert.Equal(int.MaxValue, BalanceFormulas.DailyPlotActions(int.MaxValue),
                "worker count overflow must saturate");
        }

        // ------------------------------------------------------- v2 panel snapshot contract

        /// <summary>
        /// REQ-V2-UI-01: every chapter-19.11 panel block is a field of the Core DTO the panel
        /// renders. UI never reads a game object, so this DTO is the complete data contract.
        /// </summary>
        public static void TestIndoorSnapshotExposesEveryPanelBlock()
        {
            var fields = typeof(IndoorFarmSnapshot).GetProperties().Select(property => property.Name)
                .ToArray();
            foreach (var block in new[]
                     {
                         "Tier", "Faction", "Workers", "MaxWorkers", "Mode", "ModeTransitioning",
                         "Capacity", "ConfiguredSlots", "EffectiveSlots", "EmptySlots", "GrowingSlots",
                         "MatureSlots", "BacklogActions", "Tank", "TankCapacity", "TankDaysRemaining",
                         "Coverage", "MinCoverage", "WaterSupply", "MaintenanceDueDay",
                         "MaintenanceCyclesMissed", "MaintenanceMaterialMissing", "Power",
                         "PowerConnected", "CropPolicy", "FixedCropId", "RotationSequence",
                         "OutputOccupied", "OutputPortConnected", "PipePort", "LogisticsPort",
                         "PowerPort", "Warnings", "OutputInventoryPortState",
                     })
            {
                TestAssert.True(fields.Contains(block, StringComparer.Ordinal),
                    "IndoorFarmSnapshot is missing the panel field " + block);
            }
        }

        /// <summary>
        /// REQ-V2-UI-02/03 (chapter 19.11): the four v2 warning categories exist as flags, and the
        /// mode/crop enums keep their frozen numeric values the save schema depends on.
        /// </summary>
        public static void TestIndoorEnumsCoverTheFourV2WarningCategories()
        {
            TestAssert.Equal(1, (int)IndoorWarningFlags.NoWater);
            TestAssert.Equal(2, (int)IndoorWarningFlags.LowWater);
            TestAssert.Equal(4, (int)IndoorWarningFlags.MaintenanceDue);
            TestAssert.Equal(8, (int)IndoorWarningFlags.MaintenanceStalled);
            TestAssert.Equal(16, (int)IndoorWarningFlags.NoPower);
            TestAssert.Equal(32, (int)IndoorWarningFlags.EntranceBlocked);
            TestAssert.Equal(64, (int)IndoorWarningFlags.OutputFull);
            TestAssert.Equal(128, (int)IndoorWarningFlags.NoWorkers);
            TestAssert.Equal(256, (int)IndoorWarningFlags.MissingMaterial);

            TestAssert.Equal(3, Enum.GetValues(typeof(IndoorMode)).Length, "normal/dense/fallow");
            TestAssert.Equal(IndoorMode.Normal, (IndoorMode)0);
            TestAssert.Equal(IndoorMode.Dense, (IndoorMode)1);
            TestAssert.Equal(IndoorMode.Fallow, (IndoorMode)2);
            TestAssert.Equal(2, Enum.GetValues(typeof(CropPolicy)).Length, "rotation/fixed");
            TestAssert.Equal(CropPolicy.Rotation, (CropPolicy)0);
            TestAssert.Equal(CropPolicy.Fixed, (CropPolicy)1);
        }
    }
}
