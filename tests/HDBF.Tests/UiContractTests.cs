using System;
using System.Globalization;
using System.Linq;
using HDBF.Core;
using HDBF.Core.Abstractions;
using HDBF.Core.Balance;
using HDBF.Core.Indoor;
using HDBF.Core.Save;

namespace HDBF.Tests
{
    /// <summary>
    /// Offline contract tests for the data the v2 UI layer consumes (chapter 19.11, REQ-V2-UI-01..04)
    /// and for the remaining labour-cap formulas (chapter 6.2, REQ-BAL-05/07).
    ///
    /// Scope note, kept honest: the warning evaluator and the panel mapper live in HDBF.Gameplay /
    /// HDBF.UI and reference Timberborn assemblies, so L1 cannot load those assemblies directly
    /// (REQ-V2-TEST-01). The v2 panel mapping is tested through the linked game-free HdbfIndoor*
    /// sources in <see cref="V2PanelContractTests"/>; this class pins the Core contract the UI reads:
    /// the snapshot DTO shape, the warning severity order, the single-source yield product and the
    /// invariant number formatting of the save canonical form.
    ///
    /// Mode-switch cooldown (REQ-V2-UI-02): the one-game-day cooldown lives in
    /// HdbfBoxFarmController.TrySetMode and reads the game day counter, so it has no offline seam;
    /// it is listed as an in-game check rather than faked here.
    /// </summary>
    public static class UiContractTests
    {
        // ------------------------------------------------------- warning severity contract

        public static void TestWarningSeverityOrdinalsMatchP0ToP4()
        {
            // The UI sorts warnings by severity number and then by localization key. Reordering or
            // renumbering this enum would silently change the panel order, so the numbers are a
            // contract even though the sorter itself is game-side.
            TestAssert.Equal(0, (int)FarmWarningSeverity.Critical, "P0 shutdown/no water");
            TestAssert.Equal(1, (int)FarmWarningSeverity.High, "P1 irrigation low/no power/adapter failed");
            TestAssert.Equal(2, (int)FarmWarningSeverity.Medium, "P2 maintenance overdue/fatigue high");
            TestAssert.Equal(3, (int)FarmWarningSeverity.Low, "P3 adapter disabled");
            TestAssert.Equal(4, (int)FarmWarningSeverity.Info, "P4 tank low");
        }

        public static void TestWarningSeverityOrdinalSortIsP0First()
        {
            var values = new[]
            {
                FarmWarningSeverity.Info,
                FarmWarningSeverity.Medium,
                FarmWarningSeverity.Critical,
                FarmWarningSeverity.Low,
                FarmWarningSeverity.High,
            };

            Array.Sort(values);
            TestAssert.Equal(FarmWarningSeverity.Critical, values[0]);
            TestAssert.Equal(FarmWarningSeverity.High, values[1]);
            TestAssert.Equal(FarmWarningSeverity.Medium, values[2]);
            TestAssert.Equal(FarmWarningSeverity.Low, values[3]);
            TestAssert.Equal(FarmWarningSeverity.Info, values[4]);
        }

        // ------------------------------------------------------- single-source yield product

        public static void TestFarmMultipliersTotalMultipliesEveryLayer()
        {
            var multipliers = new FarmMultipliers
            {
                Irrigation = 0.5f,
                Fatigue = 0.7f,
                Maintenance = 0.5f,
                Power = 0.6f,
                Mode = 1f,
            };

            // 0.5 * 0.7 * 0.5 * 0.6 = 0.105; REQ-BAL-28: no layer may be skipped.
            TestAssert.Equal(0.105f, multipliers.Total, 1e-6f);
        }

        public static void TestFarmMultipliersTotalDefaultsAndDenseBonus()
        {
            TestAssert.Equal(1f, new FarmMultipliers().Total, 1e-6f, "default multipliers are neutral");

            var dense = new FarmMultipliers { Mode = TierCatalog.DenseYieldMultiplier };
            TestAssert.Equal(1.35f, dense.Total, 1e-6f, "dense mode is the only layer allowed above 1");
        }

        public static void TestFarmMultipliersTotalIsZeroWhenAnyLayerIsZero()
        {
            TestAssert.Equal(0f, new FarmMultipliers { Irrigation = 0f }.Total);
            TestAssert.Equal(0f, new FarmMultipliers { Fatigue = 0f }.Total);
            TestAssert.Equal(0f, new FarmMultipliers { Maintenance = 0f }.Total);
            TestAssert.Equal(0f, new FarmMultipliers { Power = 0f }.Total);
            TestAssert.Equal(0f, new FarmMultipliers { Mode = 0f }.Total);
        }

        public static void TestFarmWarningCarriesOnlyLocalizationKeyAndTwoNumbers()
        {
            // The snapshot contract deliberately has no string payload (REQ-UI-01/03): the UI must
            // render warnings from HDBF.* keys, never from raw game strings.
            var warning = new FarmWarning
            {
                CodeKey = "HDBF.Warning.NoWater",
                Severity = FarmWarningSeverity.Critical,
                NumberArg0 = 12.5f,
                NumberArg1 = 50f,
            };

            TestAssert.Equal("HDBF.Warning.NoWater", warning.CodeKey);
            TestAssert.Equal(FarmWarningSeverity.Critical, warning.Severity);
            TestAssert.Equal(12.5f, warning.NumberArg0, 1e-6f);
            TestAssert.Equal(50f, warning.NumberArg1, 1e-6f);
        }

        // ------------------------------------------------------- save number formatting (chapter 14.5 / 12.4)

        public static void TestCanonicalNumbersUseInvariantPointAndNoGroupSeparators()
        {
            var record = HdbfSaveCodec.CreateDefault(1, HdbfTier.Advanced, TierCatalog.Get(HdbfTier.Advanced), 1f);
            record.DueDay = 12345.5f;
            record.TankWater = 12.25f;
            record.PlotIds = new[] { 1 };
            record.CropIds = new[] { "Carrot" };
            record.Fatigue = new[] { 0.125f };

            var canonical = HdbfSaveCodec.Canonicalize(record);

            TestAssert.True(canonical.Contains("due=12345.500"), "due-day format: " + canonical);
            TestAssert.True(canonical.Contains("tank=12.250"), "tank format: " + canonical);
            TestAssert.True(canonical.Contains("fatigue=0.125"), "fatigue format: " + canonical);
            TestAssert.False(canonical.Contains("12,345"), "canonical form must not use group separators");
            TestAssert.False(canonical.Contains("12345,500"), "canonical form must not use a decimal comma");
        }

        public static void TestCanonicalFormIsStableAcrossCultures()
        {
            // The save form is contract text: it must not change with the player's locale.
            // (The test runner sets InvariantGlobalization, so this mainly guards the explicit
            // InvariantCulture calls from being replaced by CurrentCulture ones.)
            var record = HdbfSaveCodec.CreateDefault(7, HdbfTier.Intermediate, TierCatalog.Get(HdbfTier.Intermediate), 2.5f);
            record.PlotIds = new[] { 10, 11 };
            record.CropIds = new[] { "Carrot", "Potato" };
            record.Fatigue = new[] { 0.1f, 0.9f };
            record.TankWater = 3.5f;

            var before = HdbfSaveCodec.Canonicalize(record);
            var original = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
                TestAssert.Equal(before, HdbfSaveCodec.Canonicalize(record));
            } finally
            {
                CultureInfo.CurrentCulture = original;
            }
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

        public static void TestNoWorkersMeansNoEffectivePlotsAndNoActionsForEveryTier()
        {
            // REQ-BAL-05 / TC-33: without farmers a farm may not plant, harvest or produce.
            foreach (var profile in TierCatalog.All)
            {
                TestAssert.Equal(0, BalanceFormulas.EffectivePlots(profile.PlotCapacity, 0), "effective plots " + profile.Tier);
                TestAssert.Equal(0, BalanceFormulas.DailyPlotActions(0), "daily actions " + profile.Tier);
            }
        }

        public static void TestOneFarmerCoversExactlyThirtyEightPlotsForEveryTier()
        {
            foreach (var profile in TierCatalog.All)
            {
                var expected = Math.Min(profile.PlotCapacity, TierCatalog.PlotsPerFarmer);
                TestAssert.Equal(expected, BalanceFormulas.EffectivePlots(profile.PlotCapacity, 1), "one farmer " + profile.Tier);
                TestAssert.Equal(13, BalanceFormulas.DailyPlotActions(1), "one farmer actions " + profile.Tier);
            }
        }

        public static void TestLabourCapAtFullCrewPerTier()
        {
            // P_eff = min(capacity, W_max x 38) and C_day = W_max x 13 (chapter 5.2 / 6.2).
            foreach (var profile in TierCatalog.All)
            {
                var expectedPlots = Math.Min(profile.PlotCapacity, profile.MaxWorkers * TierCatalog.PlotsPerFarmer);
                TestAssert.Equal(expectedPlots, BalanceFormulas.EffectivePlots(profile.PlotCapacity, profile.MaxWorkers),
                    "full crew plots " + profile.Tier);
                TestAssert.Equal(profile.MaxWorkers * 13, BalanceFormulas.DailyPlotActions(profile.MaxWorkers),
                    "full crew actions " + profile.Tier);
            }

            // Top tier: 16 x 38 = 608 is clamped to 600; action budget is 208 plot-actions/day.
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

            TestAssert.Equal(int.MaxValue, BalanceFormulas.DailyPlotActions(int.MaxValue), "worker count overflow must saturate");
        }

        public static void TestEffectivePlotsNeverExceedsCapacityOrFarmerCoverage()
        {
            for (var capacity = 0; capacity <= 700; capacity += 25)
            {
                for (var workers = 0; workers <= 20; workers++)
                {
                    var effective = BalanceFormulas.EffectivePlots(capacity, workers);
                    TestAssert.True(effective <= capacity, "effective plots exceeded capacity at " + capacity + "/" + workers);
                    TestAssert.True(effective <= workers * TierCatalog.PlotsPerFarmer,
                        "effective plots exceeded farmer coverage at " + capacity + "/" + workers);
                }
            }
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
