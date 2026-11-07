// ============================================================================
// HDBF v2 | Layer: L1 test | Owner: V2-UI-01
// Purpose: Contract of the v2 indoor panel: every Core snapshot field reaches the shipped cards,
//          the chapter-19.11 card order, the vanilla-control vocabulary, the live coverage rule
//          (no stale 0 % shortage), the concrete action reasons, the "—" rule, the no-braces rule,
//          the 4 Hz / fingerprint refresh gate and the output capacity pinned to the blueprints.
//          It drives the REAL game-free panel mapping (linked HdbfIndoor*.cs sources).
// Requirements: REQ-V2-UI-01/02/03/04, REQ-V2-WATER-03, REQ-V2-READ-04, REQ-V2-TEST-01
// ============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using HDBF.Core;
using HDBF.Core.Balance;
using HDBF.Core.Formatting;
using HDBF.Core.Indoor;
using HDBF.UI.Localization;
using HDBF.UI.Panels;

namespace HDBF.Tests
{
    /// <summary>
    /// L1 contract of the v2 panel. The fragment itself references UIElements/Timberborn and cannot
    /// load here (REQ-V2-TEST-01), so the test links the game-free read model and pins both the
    /// source-level UI rules and the rendered text of a fully populated Core snapshot.
    /// </summary>
    public static class V2PanelContractTests
    {
        private const string RepoRootMetadataKey = "HdbfRepoRoot";
        private const string UiPanelFolder = "src/HDBF.UI";

        private static readonly Guid EntityId = new Guid("00000000-0000-0000-0000-0000000000aa");

        private static readonly string[] SnapshotFields =
        {
            "Tier", "Faction", "Workers", "MaxWorkers", "Mode", "ModeTransitioning",
            "Capacity", "ConfiguredSlots", "EffectiveSlots", "EmptySlots", "GrowingSlots",
            "MatureSlots", "BacklogActions", "Tank", "TankCapacity", "TankDaysRemaining",
            "Coverage", "MinCoverage", "WaterSupply", "MaintenanceDueDay",
            "MaintenanceCyclesMissed", "MaintenanceMaterialMissing", "Power", "PowerConnected",
            "CropPolicy", "FixedCropId", "RotationSequence", "OutputOccupied", "OutputPortConnected",
            "PipePort", "LogisticsPort", "PowerPort", "Warnings", "OutputInventoryPortState",
            "Crops",
            // WP-01/02/04/05/06 init properties (plan section 3.6); the panel and its fingerprint
            // must read every one of them.
            "Paused", "AutoMaintenance", "MaintenanceCyclesToCatchUp", "MaintenanceCatchUpMaterialAmount",
            "IdleReason", "ManualRefillSourceAvailable", "PipeProbeState",
            // L-12/L-15: the config-resolved tier profile and the live faction crop table.
            "Profile", "CropChoices",
            // L-11/WP-10: the live warning inputs and the derived flag set the panel/icon read.
            "CurrentDay", "EntranceBlocked", "LiveWarnings",
        };

        // ------------------------------------------------------------------ contract / order

        public static void TestSnapshotContractHasExactlyTheDocumentedFields()
        {
            var properties = typeof(IndoorFarmSnapshot).GetProperties()
                .Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray();
            var expected = SnapshotFields.OrderBy(name => name, StringComparer.Ordinal).ToArray();
            TestAssert.Equal(expected.Length, properties.Length, "snapshot field count");
            for (var index = 0; index < expected.Length; index++)
            {
                TestAssert.Equal(expected[index], properties[index],
                    "snapshot field at index " + index + " (update the panel and this list together)");
            }
        }

        public static void TestEverySnapshotFieldIsReadByThePanelMapper()
        {
            var source = ReadUiPanelSources(onlyIndoorMapping: true);
            foreach (var field in SnapshotFields)
            {
                TestAssert.True(source.Contains("snapshot." + field, StringComparison.Ordinal),
                    "the panel mapping must read IndoorFarmSnapshot." + field);
            }
        }

        public static void TestCardsFollowTheChapter1911Order()
        {
            var data = CreateData(IntermediateFixture(), out var missing);
            TestAssert.Equal(0, missing.Count, "unmapped keys: " + string.Join(", ", missing));
            var kinds = data.Sections.Select(section => section.Kind).ToArray();
            TestAssert.Equal(HdbfPanelSectionKind.Workers, kinds[0]);
            // REQ-V2-UI-05 (2.4.2): the live crop card moved directly under the worker card so a
            // partly planted farm is visible without scrolling the 325 px panel.
            TestAssert.Equal(HdbfPanelSectionKind.Growing, kinds[1]);
            TestAssert.Equal(HdbfPanelSectionKind.Slots, kinds[2]);
            TestAssert.Equal(HdbfPanelSectionKind.Water, kinds[3]);
            TestAssert.Equal(HdbfPanelSectionKind.Maintenance, kinds[4]);
            TestAssert.Equal(HdbfPanelSectionKind.Power, kinds[5]);
            TestAssert.Equal(HdbfPanelSectionKind.Crops, kinds[6]);
            TestAssert.Equal(HdbfPanelSectionKind.Output, kinds[7]);
            TestAssert.Equal(8, kinds.Length,
                "worker/efficiency + the live growing card + the seven chapter-19.11 cards");
            TestAssert.Equal("Workers & efficiency", Section(data, HdbfPanelSectionKind.Workers).Title);
            TestAssert.Equal("Crop strategy", Section(data, HdbfPanelSectionKind.Crops).Title);
            // REQ-V2-UI-05: the live per-crop card sits between the slot counters and the water card.
            TestAssert.Equal("Growing now", Section(data, HdbfPanelSectionKind.Growing).Title);
        }

        // ------------------------------------------------------------------ header / workers

        public static void TestCompactSubtitleRowReplacesTheDeletedDuplicateHeader()
        {
            var data = CreateData(IntermediateFixture(), out _);
            // Requirement A: the vanilla EntityPanel header already shows the building icon, name
            // and district; the fragment only adds this compact line and never repeats the name.
            TestAssert.Equal("Intermediate · IronTeeth", data.MetaLine);
            TestAssert.False(data.MetaLine.Contains("Dense", StringComparison.Ordinal),
                "the mode is not repeated in the compact subtitle row (it is the radio group)");
            TestAssert.True(data.MetaTooltip.Length > 0);
            TestAssert.Equal(HdbfTier.Intermediate, data.Tier);
            TestAssert.Equal(IndoorMode.Dense, data.Mode);

            // The duplicated-header fields are gone from the read model entirely.
            var properties = typeof(HdbfIndoorPanelData).GetProperties()
                .Select(property => property.Name).ToArray();
            foreach (var removed in new[] { "Title", "ModeBadge", "HeaderTooltip" })
            {
                TestAssert.False(properties.Contains(removed, StringComparer.Ordinal),
                    "the duplicate-header field " + removed + " must be deleted (requirement A)");
            }

            // Header icons still are the v2.2 tier renders of the blueprint; the vanilla header
            // draws them, the fragment does not add a second one (requirement L: icons unchanged).
            TestAssert.Equal("Icons/BoxFarmBasic", HdbfIndoorPanelStyle.TierIconAsset(HdbfTier.Basic));
            TestAssert.Equal("Icons/BoxFarmIntermediate",
                HdbfIndoorPanelStyle.TierIconAsset(HdbfTier.Intermediate));
            TestAssert.Equal("Icons/BoxFarmAdvanced", HdbfIndoorPanelStyle.TierIconAsset(HdbfTier.Advanced));
            TestAssert.Equal("Icons/BoxFarmTop", HdbfIndoorPanelStyle.TierIconAsset(HdbfTier.Top));
        }

        public static void TestTransitionRowIsEmptyWhenNoTransitionRuns()
        {
            var data = CreateData(IntermediateFixture() with { ModeTransitioning = false }, out _);
            TestAssert.Equal(string.Empty, data.ModeTransitionText);
        }

        public static void TestWorkerCardRendersCrewEfficiencyAndReadOnlyRoster()
        {
            var data = CreateData(IntermediateFixture(), out _);
            var workers = Section(data, HdbfPanelSectionKind.Workers);
            TestAssert.Equal("3/4", Row(workers, "Crew").Value);
            TestAssert.Equal("100%", Row(workers, "Growth efficiency").Value,
                "C = 73 % is above C_min = 60 %, so G_irr is 1");
            TestAssert.True(Row(workers, "Growth efficiency").HasBar);
            TestAssert.Equal(4, data.WorkerChips.Count, "one chip per worker slot");
            for (var index = 0; index < 3; index++)
            {
                TestAssert.Equal("—", data.WorkerChips[index].Name, "name is not exposed to mods");
                TestAssert.False(data.WorkerChips[index].Vacant);
            }

            TestAssert.Equal("Vacant", data.WorkerChips[3].Name);
            TestAssert.True(data.WorkerChips[3].Vacant);
            TestAssert.True(data.WorkerRosterNote.Length > 0, "the read-only roster explains itself");
        }

        public static void TestWorkerCardWarnsOnNoCrewAndBlockedEntrance()
        {
            var broken = IntermediateFixture() with
            {
                Workers = 0,
                Warnings = IndoorWarningFlags.NoWorkers | IndoorWarningFlags.EntranceBlocked,
            };
            var workers = Section(CreateData(broken, out _), HdbfPanelSectionKind.Workers);
            var warnings = workers.Rows.Where(row => row.IsWarning).ToArray();
            TestAssert.Equal(2, warnings.Length);
            TestAssert.True(warnings[0].Label.StartsWith("No workers on duty", StringComparison.Ordinal),
                warnings[0].Label);
            TestAssert.True(warnings[1].Label.StartsWith("Entrance blocked", StringComparison.Ordinal),
                warnings[1].Label);
            TestAssert.True(warnings[0].Marker.Length > 0, "colour must not be the only carrier");
        }

        // ------------------------------------------------------------------ slots / water

        public static void TestSlotCardRendersEveryCount()
        {
            var slots = Section(CreateData(IntermediateFixture(), out _), HdbfPanelSectionKind.Slots);
            TestAssert.Equal("150", Row(slots, "Slot capacity").Value);
            TestAssert.Equal("120/150", Row(slots, "Configured").Value);
            TestAssert.Equal("120/150 configured", Row(slots, "Configured").BarText);
            TestAssert.Equal("114", Row(slots, "Effective P_eff").Value);
            TestAssert.Equal("40", Row(slots, "Empty").Value);
            // 2026-10-29 product request: "growing" is every crop in the planting slots
            // (still growing 70 + ready 10), so the row matches what the player sees in the box.
            TestAssert.Equal("80", Row(slots, "Growing").Value);
            TestAssert.Equal("10", Row(slots, "Ready").Value);
            TestAssert.Equal("2", Row(slots, "Backlog").Value);
            TestAssert.True(Row(slots, "Effective P_eff").Tooltip.Contains("P_eff", StringComparison.Ordinal),
                "the P_eff formula stays in the row tooltip");
        }

        // ------------------------------------------------------------------ growing now (UI-05)

        /// <summary>
        /// Product request 2026-10-29: "however many crops are in the planting slots, that many
        /// growing crops are displayed". A partly ripe box (35 still growing + 25 ready = 60 planted
        /// of 60 slots) must therefore report 60 growing on the slots card and 60 growing on the crop
        /// row, while the 25 ripe ones stay visible as the separate "Ready"/"待收" subset.
        /// </summary>
        public static void TestGrowingCountIsEveryCropInThePlantingSlots()
        {
            var snapshot = new IndoorFarmSnapshot(
                HdbfTier.Basic, HdbfFaction.IronTeeth,
                Workers: 2, MaxWorkers: 2, Mode: IndoorMode.Normal, ModeTransitioning: false,
                Capacity: 60, ConfiguredSlots: 60, EffectiveSlots: 60,
                EmptySlots: 0, GrowingSlots: 35, MatureSlots: 25, BacklogActions: 0,
                Tank: 3.6f, TankCapacity: 3.6f, TankDaysRemaining: 3.0f,
                Coverage: 1f, MinCoverage: 0.5f, WaterSupply: WaterSupplyKind.Manual,
                MaintenanceDueDay: 0f, MaintenanceCyclesMissed: 0, MaintenanceMaterialMissing: false,
                Power: PowerConnection.NotRequired, PowerConnected: false,
                CropPolicy: CropPolicy.Rotation, FixedCropId: string.Empty,
                RotationSequence: "Carrot → Potato", OutputOccupied: 0, OutputPortConnected: false,
                PipePort: true, LogisticsPort: false, PowerPort: false,
                Warnings: IndoorWarningFlags.None, OutputInventoryPortState: "None")
            {
                Crops = new[]
                {
                    new IndoorCropStatus("Carrot", "Carrot", GrowingSlots: 35, MatureSlots: 25,
                        AverageProgress: 0.75f, GrowthTimeInDays: 4f),
                },
            };

            var data = CreateData(snapshot, out var missing);
            TestAssert.Equal(0, missing.Count, "unmapped keys: " + string.Join(", ", missing));

            var slots = Section(data, HdbfPanelSectionKind.Slots);
            TestAssert.Equal("0", Row(slots, "Empty").Value);
            TestAssert.Equal("60", Row(slots, "Growing").Value,
                "all 60 crops in the planting slots count as growing");
            TestAssert.Equal("25", Row(slots, "Ready").Value,
                "the ripe crops stay visible as the ready subset");

            var growing = Section(data, HdbfPanelSectionKind.Growing);
            TestAssert.Equal("60 / 60 slots", growing.Rows[0].Value);
            TestAssert.Equal("Carrot", growing.Rows[1].Label);
            TestAssert.Equal("60 growing · 25 ready", growing.Rows[1].Value);
        }

        /// <summary>
        /// REQ-V2-UI-05: the live per-crop card renders one row per crop in snapshot order (name,
        /// "crops in slots · ready" split, growth bar/percent and the slots/average/days tooltip),
        /// and an empty farm keeps the card with the single "nothing planted yet" row.
        /// </summary>
        public static void TestGrowingCardListsLiveCropsAndFallsBackToTheEmptyState()
        {
            var populated = IntermediateFixture() with
            {
                Crops = new[]
                {
                    new IndoorCropStatus("Carrot", "Carrot", GrowingSlots: 4, MatureSlots: 2,
                        AverageProgress: 0.5f, GrowthTimeInDays: 3.0f),
                    new IndoorCropStatus("Wheat", "Wheat", GrowingSlots: 0, MatureSlots: 1,
                        AverageProgress: 1f, GrowthTimeInDays: 4.5f),
                },
            };
            var data = CreateData(populated, out var missing);
            TestAssert.Equal(0, missing.Count, "unmapped keys: " + string.Join(", ", missing));
            var growing = Section(data, HdbfPanelSectionKind.Growing);
            TestAssert.Equal("Growing now", growing.Title);
            TestAssert.Equal("growing", growing.IconKey);
            TestAssert.Equal("green", growing.Tone);
            TestAssert.Equal(3, growing.Rows.Count, "one planted summary + one row per crop");
            // 2.4.2: the summary row shows the dynamic planted/capacity quantity of THIS building.
            TestAssert.Equal("Planted slots", growing.Rows[0].Label);
            TestAssert.Equal("7 / 150 slots", growing.Rows[0].Value);
            TestAssert.True(growing.Rows[0].HasBar, "the summary row owns the averaged growth bar");
            TestAssert.Equal(4f / 7f, growing.Rows[0].Bar, 1e-4f);
            TestAssert.Equal("57%", growing.Rows[0].BarText);
            TestAssert.Equal("7 slots in use · average growth 57% · matures in 3.2 days",
                growing.Rows[0].Tooltip);
            TestAssert.Equal("Carrot", growing.Rows[1].Label);
            // 2026-10-29 product request: the growing number counts every crop in the planting
            // slots (4 growing + 2 ready), "ready" stays as the subset of those already ripe.
            TestAssert.Equal("6 growing · 2 ready", growing.Rows[1].Value);
            TestAssert.True(growing.Rows[1].HasBar, "each crop row owns a growth ProgressBar");
            TestAssert.Equal(0.5f, growing.Rows[1].Bar, 1e-4f);
            TestAssert.Equal("50%", growing.Rows[1].BarText);
            TestAssert.Equal("6 slots in use · average growth 50% · matures in 3.0 days",
                growing.Rows[1].Tooltip);
            TestAssert.Equal("Wheat", growing.Rows[2].Label, "the card keeps the snapshot order");
            TestAssert.Equal("1 growing · 1 ready", growing.Rows[2].Value);
            TestAssert.Equal(1f, growing.Rows[2].Bar, 1e-4f);
            TestAssert.Equal("100%", growing.Rows[2].BarText);

            var empty = Section(CreateData(IntermediateFixture(), out _), HdbfPanelSectionKind.Growing);
            TestAssert.Equal(1, empty.Rows.Count, "an empty farm keeps one explanatory row");
            TestAssert.Equal("empty", empty.Rows[0].IconKey);
            TestAssert.Equal("Nothing planted yet", empty.Rows[0].Label);
            TestAssert.Equal("—", empty.Rows[0].Value);
            TestAssert.False(empty.Rows[0].HasBar);
        }

        public static void TestWaterCardRendersTankCoverageMinimumAndSupply()
        {
            var water = Section(CreateData(IntermediateFixture(), out _), HdbfPanelSectionKind.Water);
            TestAssert.Equal("2.4/10.8 units", Row(water, "Tank").Value);
            TestAssert.Equal("about 2.0 days", Row(water, "Tank").BarText);
            TestAssert.Equal("73%", Row(water, "Coverage C").Value);
            TestAssert.Equal("C 73% · min 60%", Row(water, "Coverage C").BarText);
            TestAssert.Equal(0.60f, Row(water, "Coverage C").BarMarker, 1e-4f);
            TestAssert.Equal("60%", Row(water, "Minimum C_min").Value);
            TestAssert.Equal("pipe", Row(water, "Water source").Value);
        }

        public static void TestFullTankNeverShowsTheStaleWaterShortage()
        {
            // The reported defect: a freshly loaded farm with a full tank showed "coverage C 0 %"
            // and "water shortage: growth 0 %" because the snapshot used the previous tick cache.
            var loaded = IntermediateFixture() with
            {
                Tank = 10.8f,
                Coverage = 1f,
                TankDaysRemaining = 3f,
                Warnings = IndoorWarningFlags.NoWater | IndoorWarningFlags.LowWater,
            };
            var water = Section(CreateData(loaded, out _), HdbfPanelSectionKind.Water);
            TestAssert.Equal("100%", Row(water, "Coverage C").Value);
            TestAssert.Equal(0, water.Rows.Count(row => row.IsWarning),
                "live C >= C_min must not render a water warning even if the flags are stale");
            TestAssert.False(water.Rows.Any(row => row.Label.Contains("Water shortage", StringComparison.Ordinal)),
                "no shortage row while the tank covers the day");
        }

        public static void TestLowCoverageShowsTheSpecificDeratePercent()
        {
            var dry = IntermediateFixture() with
            {
                Coverage = 0.30f,
                MinCoverage = 0.60f,
                WaterSupply = WaterSupplyKind.Manual,
                ManualRefillSourceAvailable = true,
                Warnings = IndoorWarningFlags.LowWater,
            };
            var data = CreateData(dry, out _);
            var water = Section(data, HdbfPanelSectionKind.Water);
            // G_irr = (0.30 - 0.25*0.60) / (0.75*0.60) = 1/3 -> growth 33 %, growth slowed 67 %.
            var shortage = water.Rows.Single(row => row.Label.StartsWith("Water shortage", StringComparison.Ordinal));
            TestAssert.Equal("Water shortage: growth 33% (growth slowed 67%)", shortage.Label);
            TestAssert.True(shortage.IsWarning);
            TestAssert.Equal("Water low", water.Rows[0].Label, "the low-water warning keeps its text");
            TestAssert.True(data.CanRefill, "manual supply with a crew enables the refill action");
        }

        public static void TestWaterCardTurnsCriticalAtZeroCoverage()
        {
            var empty = IntermediateFixture() with
            {
                Tank = 0f,
                Coverage = 0f,
                WaterSupply = WaterSupplyKind.None,
            };
            var water = Section(CreateData(empty, out _), HdbfPanelSectionKind.Water);
            TestAssert.True(water.Rows[0].IsWarning);
            TestAssert.True(water.Rows[0].Label.StartsWith("Water shortage", StringComparison.Ordinal),
                water.Rows[0].Label);
            TestAssert.True(water.Rows[0].Label.Contains("No water source is connected", StringComparison.Ordinal),
                "a zero tank must name the missing source: " + water.Rows[0].Label);
        }

        // ------------------------------------------------------------------ maintenance / power

        public static void TestMaintenanceCardListsMaterialAndCount()
        {
            var maintenance = Section(CreateData(IntermediateFixture(), out _),
                HdbfPanelSectionKind.Maintenance);
            TestAssert.Equal("24.5", Row(maintenance, "Next due day").Value);
            TestAssert.Equal("1", Row(maintenance, "Overdue cycles").Value);
            // Requirement F: the upkeep-material requirement row is always present, not only when
            // the material is missing (Intermediate: 8 plank per 10-day cycle).
            TestAssert.Equal("Plank ×8 / 10 days", Row(maintenance, "Upkeep material").Value);
            // WP-02: the automatic-maintenance state is a visible On/Off row.
            TestAssert.Equal("Automatic maintenance", Row(maintenance, "Automatic maintenance").Label);
            TestAssert.Equal("On", Row(maintenance, "Automatic maintenance").Value);
            TestAssert.True(Row(maintenance, "Automatic maintenance").Tooltip.Length > 0,
                "the auto-maintenance tooltip explains the same material cost as manual");
            // WP-01: the catch-up row spells out the whole bill of the next click.
            TestAssert.Equal("Catch-up bill", Row(maintenance, "Catch-up bill").Label);
            TestAssert.Equal("1 cycle(s) overdue · needs Plank ×8", Row(maintenance, "Catch-up bill").Value);
            var warning = maintenance.Rows[0];
            TestAssert.True(warning.IsWarning);
            TestAssert.True(warning.Label.Contains("Maintenance material missing", StringComparison.Ordinal),
                warning.Label);
            TestAssert.True(warning.Label.Contains("Missing materials: Plank ×8", StringComparison.Ordinal),
                "the missing material and count are named: " + warning.Label);
        }

        public static void TestMaterialFreeTierShowsTheNoneValueInTheMaterialRow()
        {
            var basic = IntermediateFixture() with
            {
                Tier = HdbfTier.Basic,
                Faction = HdbfFaction.Folktails,
                Warnings = IndoorWarningFlags.None,
                MaintenanceMaterialMissing = false,
                MaintenanceCyclesMissed = 0,
            };
            var maintenance = Section(CreateData(basic, out _), HdbfPanelSectionKind.Maintenance);
            TestAssert.Equal("No material upkeep", Row(maintenance, "Upkeep material").Value,
                "the material row explains a material-free tier instead of disappearing");
        }

        public static void TestMaintenanceMaterialRowsForAdvancedAndOffGridTop()
        {
            // Requirement F: Advanced = 6 metal per 10-day cycle; Top off-grid = 15 metal / 8 days.
            var advanced = Section(CreateData(IntermediateFixture() with
            {
                Tier = HdbfTier.Advanced,
                Power = PowerConnection.NotRequired,
                PowerConnected = false,
                Warnings = IndoorWarningFlags.None,
                MaintenanceMaterialMissing = false,
                MaintenanceCyclesMissed = 0,
            }, out _), HdbfPanelSectionKind.Maintenance);
            TestAssert.Equal("Metal block ×6 / 10 days", Row(advanced, "Upkeep material").Value);

            var offGrid = Section(CreateData(IntermediateFixture() with
            {
                Tier = HdbfTier.Top,
                Power = PowerConnection.OffGrid,
                PowerConnected = false,
                Warnings = IndoorWarningFlags.None,
                MaintenanceMaterialMissing = false,
                MaintenanceCyclesMissed = 0,
            }, out _), HdbfPanelSectionKind.Maintenance);
            TestAssert.Equal("Metal block ×15 / 8 days", Row(offGrid, "Upkeep material").Value);
        }

        public static void TestMaterialFreeTierDisablesMaintenanceWithReason()
        {
            var basic = IntermediateFixture() with
            {
                Tier = HdbfTier.Basic,
                Faction = HdbfFaction.Folktails,
                Warnings = IndoorWarningFlags.None,
                MaintenanceMaterialMissing = false,
                MaintenanceCyclesMissed = 0,
            };
            var data = CreateData(basic, out _);
            var maintenance = Section(data, HdbfPanelSectionKind.Maintenance);
            TestAssert.Equal("No material upkeep", Row(maintenance, "Next due day").Value);
            TestAssert.False(data.CanPerformMaintenance);
            TestAssert.Equal("No material upkeep", data.MaintenanceReasonText);
            TestAssert.False(maintenance.Rows.Any(row => row.IsWarning));
        }

        public static void TestPowerCardMapsEveryContractState()
        {
            var top = IntermediateFixture() with { Tier = HdbfTier.Top, Power = PowerConnection.Grid };
            TestAssert.Equal("no power", PowerValue(CreateData(top with { PowerConnected = false }, out _)));
            TestAssert.Equal("grid", PowerValue(CreateData(top with { PowerConnected = true }, out _)));
            TestAssert.Equal("off-grid", PowerValue(CreateData(top with { Power = PowerConnection.OffGrid }, out _)));
            TestAssert.Equal("not required", PowerValue(CreateData(
                IntermediateFixture() with { Power = PowerConnection.NotRequired }, out _)));
        }

        // ------------------------------------------------------------------ crops / output

        public static void TestCropCardRendersOnePolicyRowAndTheDropdownChoices()
        {
            var crops = Section(CreateData(IntermediateFixture(), out _), HdbfPanelSectionKind.Crops);
            TestAssert.Equal("Current policy", crops.Rows[0].Label,
                "one policy row; the vanilla dropdown is appended to it by the fragment");
            TestAssert.Equal("Fixed crop: Corn", crops.Rows[0].Value);
            TestAssert.Equal("Corn", crops.Rows[1].Value, "the fixed crop row repeats the crop name");
            TestAssert.Equal("Fixed crop", crops.Rows[1].Label);
        }

        public static void TestCropChoicesAreLocalizedAndSelected()
        {
            var data = CreateData(IntermediateFixture(), out _);
            TestAssert.Equal(7, data.CropChoices.Count, "auto rotation + six IronTeeth crops");
            TestAssert.Equal(string.Empty, data.CropChoices[0].Id, "the first choice is automatic rotation");
            TestAssert.Equal("auto rotation", data.CropChoices[0].Text);
            TestAssert.Equal("Corn", data.SelectedCropId);
            TestAssert.True(data.CropChoices.Any(choice => choice.Id == "Corn" && choice.Text == "Corn"));
            TestAssert.True(data.CropTooltip.Length > 0);
        }

        public static void TestRotationRowUsesLocalizedCropNamesAndTheDash()
        {
            var rotation = IntermediateFixture() with
            {
                CropPolicy = CropPolicy.Rotation,
                FixedCropId = string.Empty,
                RotationSequence = "Carrot > Potato",
            };
            var data = CreateData(rotation, out _);
            var crops = Section(data, HdbfPanelSectionKind.Crops);
            TestAssert.Equal("auto rotation", data.CropPolicyText);
            TestAssert.Equal("Rotation", crops.Rows[1].Label);
            TestAssert.Equal("Carrot > Potato", crops.Rows[1].Value);

            var unknown = CreateData(rotation with { RotationSequence = string.Empty }, out _);
            TestAssert.Equal("—", Section(unknown, HdbfPanelSectionKind.Crops).Rows[1].Value,
                "unknown sequence renders the dash");
        }

        public static void TestOutputCardRendersOccupiedOverCapacityAndSeparatePortRows()
        {
            var output = Section(CreateData(IntermediateFixture(), out _), HdbfPanelSectionKind.Output);
            // 2026-11-07 temporary-staging rule: the panel capacity is tier-scaled (150 slots -> 208,
            // one full crew day), not the predecessor whole-box bound 600.
            var outputCapacity = HdbfIndoorPanelLimits.OutputCapacity(150);
            TestAssert.Equal(208, outputCapacity, "the 150-slot tier stages one full crew day");
            TestAssert.Equal("12/" + outputCapacity, Row(output, "Output stock").Value,
                "occupancy/capacity stays complete");
            TestAssert.Equal("12/" + outputCapacity + " stored", Row(output, "Output stock").BarText);
            TestAssert.True(Row(output, "Output stock").HasBar,
                "requirement F: the occupancy is a vanilla ProgressBar");

            // Requirement E: the output port and the three physical ports are independent rows;
            // no combined "Pipe port Connected · ..." value can overlap the line above any more.
            TestAssert.Equal("Connected", Row(output, "Pipe network (auto-refill)").Value);
            TestAssert.Equal("Not applicable", Row(output, "Power port").Value);
            TestAssert.False(output.Rows.Any(row => row.Label == "Ports"),
                "the combined ports row must be gone (requirement E)");
            TestAssert.Equal(5, output.Rows.Count, "stock + output port + three port rows");
            TestAssert.True(Row(output, "Output stock").Tooltip.Contains("Ok", StringComparison.Ordinal),
                "the output adapter state stays in the tooltip");
        }

        /// <summary>
        /// L-09: HDBF does not hand its produce to a conveyor, so the logistics rows must never say
        /// "Connected" (the reported defect). A tier that owns the port reports the detected module
        /// with the honest "produce not handed over yet" text plus a tooltip that points the player
        /// at vanilla hauling; a tier without the port says "not applicable".
        /// </summary>
        public static void TestLogisticsPortRowsNeverClaimAConnection()
        {
            var detected = IntermediateFixture();
            var output = Section(CreateData(detected, out _), HdbfPanelSectionKind.Output);
            var expected = "Logistics module detected (produce not handed over yet)";
            TestAssert.Equal(expected, Row(output, "Output port").Value,
                "the output-port row must not claim the conveyor is connected");
            TestAssert.Equal(expected, Row(output, "Logistics port").Value,
                "the logistics-port row reports the detected module, not a connection");
            var tooltip = Row(output, "Logistics port").Tooltip;
            TestAssert.True(tooltip.Contains("conveyors", StringComparison.Ordinal),
                "the logistics tooltip names the conveyor limitation: " + tooltip);
            TestAssert.True(tooltip.Contains("haulers", StringComparison.Ordinal),
                "the logistics tooltip points at vanilla hauling: " + tooltip);

            var noPort = detected with { LogisticsPort = false, OutputPortConnected = true };
            var noPortOutput = Section(CreateData(noPort, out _), HdbfPanelSectionKind.Output);
            TestAssert.Equal("Not applicable", Row(noPortOutput, "Output port").Value);
            TestAssert.Equal("Not applicable", Row(noPortOutput, "Logistics port").Value,
                "a tier without a logistics port is not applicable, never connected");

            // A port without a detected module stays honest in the other direction: not connected.
            var undetected = detected with { LogisticsPort = true, OutputPortConnected = false };
            var undetectedOutput = Section(CreateData(undetected, out _), HdbfPanelSectionKind.Output);
            TestAssert.Equal("Not connected", Row(undetectedOutput, "Logistics port").Value);
        }

        /// <summary>
        /// L-15: the panel renders the config-resolved profile carried by the snapshot, not the
        /// compiled TierCatalog row. A profile override of the upkeep material/cycle and of the
        /// material-free state must reach the maintenance card; a snapshot without a profile keeps
        /// the compiled values (the existing tests above pin those).
        /// </summary>
        public static void TestPanelUsesTheConfigOverrideProfileFromTheSnapshot()
        {
            var overridden = new TierProfile
            {
                Tier = HdbfTier.Intermediate,
                MaintenanceKind = HdbfMaintenanceKind.Plank,
                MaintenanceCycleDays = 6f,
                MaintenanceMaterialCount = 13,
            };
            var snapshot = IntermediateFixture() with
            {
                Profile = overridden,
                MaintenanceMaterialMissing = false,
                MaintenanceCyclesMissed = 0,
                Warnings = IndoorWarningFlags.None,
            };
            var maintenance = Section(CreateData(snapshot, out _), HdbfPanelSectionKind.Maintenance);
            TestAssert.Equal("Plank ×13 / 6 days", Row(maintenance, "Upkeep material").Value,
                "the panel must show the config-resolved profile, not the compiled 8 plank / 10 days");

            // The compiled table is still the fallback for a snapshot without a profile.
            var compiled = Section(CreateData(snapshot with { Profile = null }, out _),
                HdbfPanelSectionKind.Maintenance);
            TestAssert.Equal("Plank ×8 / 10 days", Row(compiled, "Upkeep material").Value,
                "a snapshot without Profile keeps the previous compiled behaviour");

            // A profile that turns the tier material-free wins over the compiled Intermediate row.
            var materialFree = IntermediateFixture() with
            {
                Profile = new TierProfile
                {
                    Tier = HdbfTier.Intermediate,
                    MaintenanceKind = HdbfMaintenanceKind.IrrigationOnly,
                },
                MaintenanceMaterialMissing = false,
                MaintenanceCyclesMissed = 0,
                Warnings = IndoorWarningFlags.None,
            };
            var freeData = CreateData(materialFree, out _);
            TestAssert.Equal("No material upkeep",
                Row(Section(freeData, HdbfPanelSectionKind.Maintenance), "Upkeep material").Value);
            TestAssert.False(freeData.CanPerformMaintenance,
                "an irrigation-only profile has no maintenance action");
        }

        /// <summary>
        /// L-12: the dropdown, the fixed-crop name and the invalid-crop warning read the live crop
        /// list of the snapshot. A crop only a crop mod knows is a valid choice and shows its own
        /// game-localized name; a live crop is never reported as invalid.
        /// </summary>
        public static void TestCropDropdownUsesTheLiveSnapshotCatalogue()
        {
            var live = new[]
            {
                new IndoorCropChoice
                {
                    Id = "Glowberry",
                    DisplayNameLocKey = HdbfUiLocKeys.CropKohlrabi,
                },
                new IndoorCropChoice { Id = "Corn" },
            };
            var snapshot = IntermediateFixture() with
            {
                CropChoices = live,
                CropPolicy = CropPolicy.Fixed,
                FixedCropId = "Glowberry",
            };
            var data = CreateData(snapshot, out _);
            var crops = Section(data, HdbfPanelSectionKind.Crops);

            // Auto rotation + the two live crops; the mod crop shows the localized name of the
            // blueprint key, not its raw template id.
            TestAssert.Equal(3, data.CropChoices.Count, "auto rotation + two live crops");
            TestAssert.Equal("Kohlrabi", data.CropChoices.Single(choice => choice.Id == "Glowberry").Text,
                "the live crop name is resolved from its localization key");
            TestAssert.Equal("Kohlrabi", crops.Rows[1].Value,
                "the fixed crop row shows the live localized name");
            TestAssert.True(data.CropPolicyText.Contains("Kohlrabi", StringComparison.Ordinal),
                "the policy sentence shows the live localized name");
            TestAssert.Equal("Glowberry", data.SelectedCropId);
            TestAssert.False(crops.Rows.Any(row => row.IsWarning),
                "a live crop accepted by the command layer must not be reported as invalid");

            // A live crop without a display key falls back to its id instead of a raw key.
            var noKey = snapshot with
            {
                CropChoices = new[] { new IndoorCropChoice { Id = "Mystery" } },
                FixedCropId = "Mystery",
            };
            var noKeyData = CreateData(noKey, out _);
            TestAssert.Equal("Mystery", noKeyData.CropChoices.Single(choice => choice.Id == "Mystery").Text);
            TestAssert.Equal("Mystery", Section(noKeyData, HdbfPanelSectionKind.Crops).Rows[1].Value);

            // A fixed crop outside both the live list and the built-in table is invalid again.
            var unknown = snapshot with { FixedCropId = "Bogus" };
            var unknownCrops = Section(CreateData(unknown, out _), HdbfPanelSectionKind.Crops);
            TestAssert.True(unknownCrops.Rows.Any(row => row.IsWarning
                && row.Label.Contains("Invalid fixed crop", StringComparison.Ordinal)),
                "an unknown fixed crop keeps its warning");

            // The live "Growing now" card uses the same live names.
            var growing = Section(CreateData(IntermediateFixture() with
            {
                CropChoices = live,
                Crops = new[]
                {
                    new IndoorCropStatus("Glowberry", "Glowberry", GrowingSlots: 4, MatureSlots: 1,
                        AverageProgress: 0.5f, GrowthTimeInDays: 5f),
                },
            }, out _), HdbfPanelSectionKind.Growing);
            TestAssert.True(growing.Rows.Any(row => row.Label == "Kohlrabi"),
                "the growing card shows the live localized crop name");
        }

        /// <summary>
        /// L-12/L-15: the 4 Hz dirty check must notice a changed live crop table and a changed
        /// config-resolved profile; otherwise a rebuilt catalog would leave the dropdown stale.
        /// </summary>
        public static void TestFingerprintCoversTheLiveProfileAndCropChoices()
        {
            var baseline = IntermediateFixture();
            var before = HdbfIndoorPanelFingerprint.Compute(baseline, key => key);
            var withCrops = baseline with
            {
                CropChoices = new[]
                {
                    new IndoorCropChoice { Id = "Glowberry", DisplayNameLocKey = "HDBF.Panel.CropKohlrabi" },
                },
            };
            TestAssert.True(before != HdbfIndoorPanelFingerprint.Compute(withCrops, key => key),
                "a changed live crop list must force a panel refresh");

            var withProfile = baseline with
            {
                Profile = new TierProfile
                {
                    Tier = HdbfTier.Intermediate,
                    MaintenanceKind = HdbfMaintenanceKind.Plank,
                    MaintenanceCycleDays = 6f,
                    MaintenanceMaterialCount = 13,
                },
            };
            TestAssert.True(before != HdbfIndoorPanelFingerprint.Compute(withProfile, key => key),
                "a changed config-resolved profile must force a panel refresh");
        }

        // ------------------------------------------------------------------ per-card refresh signature

        /// <summary>
        /// The 4 Hz refresh now decides per card: two renders of the same read model must produce
        /// the same signature for every card, so an unchanged card (and the controls inside it, e.g.
        /// the vanilla crop dropdown) stays attached instead of being cleared and rebuilt.
        /// </summary>
        public static void TestCardSignatureIsStableForIdenticalRenderedContent()
        {
            var first = CreateData(IntermediateFixture(), out _);
            var second = CreateData(IntermediateFixture(), out _);
            TestAssert.Equal(first.Sections.Count, second.Sections.Count);
            for (var index = 0; index < first.Sections.Count; index++)
            {
                var section = first.Sections[index];
                var signature = HdbfIndoorPanelSignature.ForSection(section, first);
                TestAssert.Equal(signature, HdbfIndoorPanelSignature.ForSection(section, first),
                    "the signature must be a pure function of the rendered content: " + section.Kind);
                TestAssert.Equal(signature,
                    HdbfIndoorPanelSignature.ForSection(second.Sections[index], second),
                    "an identically rendered card must not be rebuilt: " + section.Kind);
            }

            TestAssert.Equal(0L, HdbfIndoorPanelSignature.ForSection(null, first));
            TestAssert.Equal(0L, HdbfIndoorPanelSignature.ForSection(first.Sections[0], null));
        }

        /// <summary>
        /// The reported bug (2026-11-06): crop growth and the game clock change every tick and the
        /// panel refreshes at 4 Hz, but they must NOT invalidate the crop card - that card owns the
        /// vanilla crop Dropdown, and rebuilding it detaches the dropdown and closes its popup.
        /// </summary>
        public static void TestCropProgressRefreshDoesNotRebuildTheCropCard()
        {
            var before = CreateData(IntermediateFixture(), out _);
            var after = CreateData(IntermediateFixture() with
            {
                Crops = new[]
                {
                    new IndoorCropStatus("Corn", "Corn", GrowingSlots: 40, MatureSlots: 5,
                        AverageProgress: 0.42f, GrowthTimeInDays: 4f),
                },
                GrowingSlots = 45,
                MatureSlots = 5,
                EmptySlots = 100,
                CurrentDay = 30.25f,
            }, out _);

            TestAssert.Equal(HdbfIndoorPanelSignature.ForSection(
                    Section(before, HdbfPanelSectionKind.Crops), before),
                HdbfIndoorPanelSignature.ForSection(Section(after, HdbfPanelSectionKind.Crops), after),
                "crop growth / the game clock must not rebuild the card that owns the dropdown");

            TestAssert.True(HdbfIndoorPanelSignature.ForSection(
                               Section(before, HdbfPanelSectionKind.Growing), before)
                           != HdbfIndoorPanelSignature.ForSection(
                               Section(after, HdbfPanelSectionKind.Growing), after),
                "the growing card must still refresh with the new progress");

            TestAssert.True(HdbfIndoorPanelSignature.ForSection(
                               Section(before, HdbfPanelSectionKind.Slots), before)
                           != HdbfIndoorPanelSignature.ForSection(
                               Section(after, HdbfPanelSectionKind.Slots), after),
                "the slot counters must still refresh");

            // Positive case: a real crop-policy change MUST rebuild the crop card, otherwise the
            // policy row would go stale. Guards against over-suppressing the rebuild later.
            var policyChanged = CreateData(IntermediateFixture() with
            {
                CropPolicy = CropPolicy.Rotation,
                FixedCropId = string.Empty,
            }, out _);
            TestAssert.True(HdbfIndoorPanelSignature.ForSection(
                               Section(policyChanged, HdbfPanelSectionKind.Crops), policyChanged)
                           != HdbfIndoorPanelSignature.ForSection(
                               Section(before, HdbfPanelSectionKind.Crops), before),
                "a crop policy change must rebuild the crop card");
        }

        /// <summary>
        /// A card is rebuilt only when one of the fields <c>BuildSectionCard</c> renders changed:
        /// this pins the signature coverage (row text/tooltip/marker/bar, tone/title/icon and the
        /// worker-card extras) so a new rendered field cannot silently go stale.
        /// </summary>
        public static void TestCardSignatureTracksEveryRenderedFieldOfACard()
        {
            static HdbfPanelSection WithRow(HdbfPanelSection section, int index, HdbfPanelRow row)
            {
                var rows = new List<HdbfPanelRow>(section.Rows);
                rows[index] = row;
                return section with { Rows = rows };
            }

            var data = CreateData(IntermediateFixture(), out _);
            var crops = Section(data, HdbfPanelSectionKind.Crops);
            var cropsHash = HdbfIndoorPanelSignature.ForSection(crops, data);
            var cropsRow = crops.Rows[0];
            var rowVariants = new (string Name, HdbfPanelSection Section)[]
            {
                ("tone", crops with { Tone = crops.Tone == "red" ? "green" : "red" }),
                ("title", crops with { Title = crops.Title + "x" }),
                ("icon", crops with { IconKey = crops.IconKey + "x" }),
                ("row label", WithRow(crops, 0, cropsRow with { Label = cropsRow.Label + "x" })),
                ("row value", WithRow(crops, 0, cropsRow with { Value = cropsRow.Value + "x" })),
                ("row tooltip", WithRow(crops, 0, cropsRow with { Tooltip = cropsRow.Tooltip + "x" })),
                ("row icon", WithRow(crops, 0, cropsRow with { IconKey = cropsRow.IconKey + "x" })),
            };
            foreach (var variant in rowVariants)
            {
                TestAssert.True(HdbfIndoorPanelSignature.ForSection(variant.Section, data) != cropsHash,
                    "a changed rendered field must rebuild the card: " + variant.Name);
            }

            var output = Section(data, HdbfPanelSectionKind.Output);
            var outputHash = HdbfIndoorPanelSignature.ForSection(output, data);
            var barIndex = -1;
            for (var index = 0; index < output.Rows.Count; index++)
            {
                if (output.Rows[index].HasBar)
                {
                    barIndex = index;
                    break;
                }
            }

            TestAssert.True(barIndex >= 0, "the output card keeps its occupancy progress bar");
            var barRow = output.Rows[barIndex];
            var barVariants = new (string Name, HdbfPanelSection Section)[]
            {
                ("bar fill", WithRow(output, barIndex, barRow with { Bar = barRow.Bar + 0.25f })),
                ("bar text", WithRow(output, barIndex, barRow with { BarText = barRow.BarText + "x" })),
                ("bar marker", WithRow(output, barIndex, barRow with { BarMarker = 0.5f })),
                ("warning marker", WithRow(output, barIndex, barRow with { Marker = "!", IsWarning = true })),
            };
            foreach (var variant in barVariants)
            {
                TestAssert.True(HdbfIndoorPanelSignature.ForSection(variant.Section, data) != outputHash,
                    "a changed rendered bar/warning field must rebuild the card: " + variant.Name);
            }

            var workers = Section(data, HdbfPanelSectionKind.Workers);
            var workersHash = HdbfIndoorPanelSignature.ForSection(workers, data);
            var chips = data.WorkerChips;
            var changedChips = new HdbfWorkerChip[chips.Count];
            for (var index = 0; index < chips.Count; index++)
            {
                changedChips[index] = index == 0 ? chips[index] with { Vacant = !chips[index].Vacant } : chips[index];
            }

            var dataVariants = new (string Name, HdbfIndoorPanelData Data)[]
            {
                ("meta line", data with { MetaLine = data.MetaLine + "x" }),
                ("meta tooltip", data with { MetaTooltip = data.MetaTooltip + "x" }),
                ("mode", data with
                {
                    Mode = data.Mode == IndoorMode.Dense ? IndoorMode.Normal : IndoorMode.Dense,
                }),
                ("transition", data with { ModeTransitionText = data.ModeTransitionText + "x" }),
                ("roster note", data with { WorkerRosterNote = data.WorkerRosterNote + "x" }),
                ("roster chip", data with { WorkerChips = changedChips }),
            };
            foreach (var variant in dataVariants)
            {
                TestAssert.True(
                    HdbfIndoorPanelSignature.ForSection(workers, variant.Data) != workersHash,
                    "a changed worker-card extra must rebuild the card: " + variant.Name);
            }
        }

        /// <summary>
        /// Forward-looking guard against a newly rendered field silently missing from the card
        /// signature (which would leave that card showing stale values): every
        /// <c>data.X</c>/<c>section.X</c>/<c>row.X</c>/<c>chip.X</c> field read by the card renderers
        /// must appear in the signature source. The curated test above proves the current fields
        /// actually change the signature; this one catches a future field added to the renderer but
        /// forgotten in the signature.
        /// </summary>
        public static void TestEveryFieldTheCardRenderersReadIsPartOfTheSignature()
        {
            static string Slice(string text, string from, string to)
            {
                var start = text.IndexOf(from, StringComparison.Ordinal);
                TestAssert.True(start >= 0, "missing source anchor: " + from);
                var end = text.IndexOf(to, start, StringComparison.Ordinal);
                TestAssert.True(end > start, "missing source anchor: " + to);
                return text.Substring(start, end - start);
            }

            var cardsSource = ReadUiFile("Panels/HdbfFarmPanelFragment.Cards.cs");
            var buildAt = cardsSource.IndexOf("private VisualElement BuildSectionCard", StringComparison.Ordinal);
            TestAssert.True(buildAt >= 0, "BuildSectionCard must exist in the card partial");
            var renderSource = ReadUiFile("Panels/HdbfFarmPanelFragment.Render.cs");
            var renderers = cardsSource.Substring(buildAt)
                            + Slice(renderSource, "private void RenderWorkerRoster", "private void RenderActions")
                            + ReadUiFile("Panels/HdbfPanelControls.cs")
                            + ReadUiFile("Panels/HdbfPanelControls.Widgets.cs");
            var signature = ReadUiFile("Panels/HdbfIndoorPanelSignature.cs");

            // Only tokens that are real read-model properties count: the builders also use local
            // variables named row/section (VisualElement helpers), whose Add()/style calls are not
            // rendered fields.
            var properties = new HashSet<string>(StringComparer.Ordinal);
            foreach (var type in new[]
                     {
                         typeof(HdbfIndoorPanelData), typeof(HdbfPanelSection), typeof(HdbfPanelRow),
                         typeof(HdbfWorkerChip),
                     })
            {
                foreach (var property in type.GetProperties(
                             System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                {
                    properties.Add(property.Name);
                }
            }

            var tokens = new SortedSet<string>(StringComparer.Ordinal);
            foreach (System.Text.RegularExpressions.Match match in
                     System.Text.RegularExpressions.Regex.Matches(
                         renderers, @"\b(?:data|section|row|chip)\.([A-Za-z_][A-Za-z0-9_]*)"))
            {
                var name = match.Groups[1].Value;
                if (properties.Contains(name))
                {
                    tokens.Add(name);
                }
            }

            TestAssert.True(tokens.Count >= 15,
                "the renderer scan must find the rendered fields, found " + tokens.Count);
            foreach (var token in tokens)
            {
                TestAssert.True(signature.Contains("." + token, StringComparison.Ordinal),
                    "a field the card renderers read must be part of the card signature: " + token);
            }
        }

        /// <summary>
        /// L-12/L-15 seam: the Gameplay snapshot builder fills the config-resolved profile and the
        /// live faction crop list of the game-backed catalog, and maps it to Core choices; the UI
        /// mapping reads both (pinned by TestEverySnapshotFieldIsReadByThePanelMapper).
        /// </summary>
        public static void TestGameplayFillsTheLiveProfileAndCropChoices()
        {
            var snapshotSource = ReadEmbeddedGameplaySource("HdbfBoxFarmController.IndoorSnapshot.cs");
            TestAssert.True(snapshotSource.Contains("Profile = _profile", StringComparison.Ordinal),
                "the snapshot must carry the config-resolved tier profile (L-15)");
            TestAssert.True(snapshotSource.Contains("CropChoices = LiveCropChoices()", StringComparison.Ordinal),
                "the snapshot must carry the live faction crop list (L-12)");

            var cropSource = ReadEmbeddedGameplaySource("HdbfBoxFarmController.IndoorSnapshot.Crops.cs");
            TestAssert.True(cropSource.Contains("_cropCatalog.ForFaction(_faction)", StringComparison.Ordinal),
                "the crop list must come from the live game-backed catalog of the building faction");
            TestAssert.True(cropSource.Contains("crop.DisplayNameLocKey", StringComparison.Ordinal),
                "the live display-name key must reach the Core choice");

            var catalogSource = ReadEmbeddedGameplaySource("GameCropCatalog.SpecReader.cs");
            TestAssert.True(catalogSource.Contains("GetSpec<LabeledEntitySpec>()", StringComparison.Ordinal),
                "the catalog must read the blueprint LabeledEntitySpec (L-12)");
            TestAssert.True(catalogSource.Contains("DisplayNameLocKey", StringComparison.Ordinal),
                "the catalog must read LabeledEntitySpec.DisplayNameLocKey");
            TestAssert.True(catalogSource.Contains("catch (Exception)", StringComparison.Ordinal),
                "a broken LabeledEntitySpec must not stop the catalog");
        }

        // ------------------------------------------------------------------ status line / WP-06

        /// <summary>
        /// WP-06/L-03: the very first row of the first card is the single status line. It reads the
        /// snapshot's Core idle reason (a paused snapshot from an older gameplay build still says
        /// Paused) and always carries its explanatory tooltip.
        /// </summary>
        public static void TestStatusLineIsTheFirstRowAndShowsTheIdleReason()
        {
            var data = CreateData(IntermediateFixture(), out _);
            var workers = Section(data, HdbfPanelSectionKind.Workers);
            TestAssert.Equal("Status", workers.Rows[0].Label, "the status line is the first row of the first card");
            TestAssert.Equal("Maintenance due", workers.Rows[0].Value);
            TestAssert.Equal("Maintenance due", data.IdleReasonText);
            TestAssert.True(data.IdleReasonTooltip.Length > 0, "the status line explains what the reason means");
            TestAssert.Equal(data.IdleReasonTooltip, workers.Rows[0].Tooltip);

            var paused = CreateData(IntermediateFixture() with
            {
                Paused = true,
                IdleReason = IndoorIdleReason.Paused,
            }, out _);
            TestAssert.Equal("Paused", Section(paused, HdbfPanelSectionKind.Workers).Rows[0].Value);

            var legacyPaused = CreateData(IntermediateFixture() with
            {
                Paused = true,
                IdleReason = IndoorIdleReason.None,
            }, out _);
            TestAssert.Equal("Paused", legacyPaused.IdleReasonText,
                "a snapshot that predates the idle-reason field still reports Paused");

            var working = CreateData(IntermediateFixture() with
            {
                Paused = false,
                IdleReason = IndoorIdleReason.None,
                Warnings = IndoorWarningFlags.None,
                MaintenanceCyclesMissed = 0,
                Coverage = 0.73f,
            }, out _);
            TestAssert.Equal("Working", working.IdleReasonText);

            // Even a card with warning rows keeps the status line as its very first row.
            var warn = CreateData(IntermediateFixture() with
            {
                Workers = 0,
                Warnings = IndoorWarningFlags.NoWorkers | IndoorWarningFlags.EntranceBlocked,
                IdleReason = IndoorIdleReason.NoWorkers,
            }, out _);
            var warnWorkers = Section(warn, HdbfPanelSectionKind.Workers);
            TestAssert.Equal("Status", warnWorkers.Rows[0].Label,
                "the status line stays first even when warning rows exist");
            TestAssert.Equal("No workers on duty", warnWorkers.Rows[0].Value);
            TestAssert.Equal(2, warnWorkers.Rows.Count(row => row.IsWarning),
                "both warning rows survive in their own order behind the status line");
        }

        // ------------------------------------------------------------------ WP-01 / WP-02

        /// <summary>
        /// WP-02: the maintenance card has an always-visible automatic-maintenance On/Off row, and
        /// the read model carries the boolean + tooltip the action-card toggle renders.
        /// </summary>
        public static void TestAutoMaintenanceStateAndTooltipFollowTheSnapshot()
        {
            var on = CreateData(IntermediateFixture(), out _);
            TestAssert.True(on.AutoMaintenance, "the snapshot default is automatic maintenance on");
            TestAssert.True(on.AutoMaintenanceTooltip.Length > 0);
            TestAssert.True(on.AutoMaintenanceTooltip.Contains("material cost", StringComparison.Ordinal),
                "the tooltip says the automatic bill costs the same as the manual one");

            var off = CreateData(IntermediateFixture() with { AutoMaintenance = false }, out _);
            TestAssert.False(off.AutoMaintenance);
            TestAssert.Equal("Off",
                Row(Section(off, HdbfPanelSectionKind.Maintenance), "Automatic maintenance").Value);
        }

        /// <summary>WP-01: the catch-up hint only appears while cycles are actually owed.</summary>
        public static void TestCatchUpRowIsHiddenWhenNothingIsDue()
        {
            var notDue = CreateData(IntermediateFixture() with
            {
                MaintenanceMaterialMissing = false,
                MaintenanceCyclesMissed = 0,
                MaintenanceCyclesToCatchUp = 0,
                MaintenanceCatchUpMaterialAmount = 0,
                Warnings = IndoorWarningFlags.None,
            }, out _);
            var maintenance = Section(notDue, HdbfPanelSectionKind.Maintenance);
            TestAssert.False(maintenance.Rows.Any(row => row.Label == "Catch-up bill"),
                "no catch-up hint when nothing is due");
        }

        // ------------------------------------------------------------------ water paths / L-10

        /// <summary>WP-06/L-03: the water card spells out the three refill paths.</summary>
        public static void TestWaterCardShowsTheThreeRefillPaths()
        {
            var data = CreateData(IntermediateFixture(), out _);
            var water = Section(data, HdbfPanelSectionKind.Water);
            var paths = water.Rows.Single(row => row.Label.StartsWith("Water refill paths", StringComparison.Ordinal));
            TestAssert.Equal("—", paths.Value, "an informational row carries the shared dash value");
            TestAssert.True(paths.Label.Contains("pipe network", StringComparison.Ordinal)
                             && paths.Label.Contains("manual refill", StringComparison.Ordinal)
                             && paths.Label.Contains("hauling water", StringComparison.Ordinal),
                "all three refill paths are named: " + paths.Label);
            TestAssert.Equal(paths.Label, data.WaterPathText);
        }

        /// <summary>
        /// L-10: 0 workers must not render a green 100 % coverage/efficiency (the farm is not
        /// working). Both values become the shared "—" glyph and lose their bars.
        /// </summary>
        public static void TestZeroWorkersRenderTheDashInsteadOfGreenHundred()
        {
            var noCrew = IntermediateFixture() with
            {
                Workers = 0,
                Warnings = IndoorWarningFlags.NoWorkers,
                IdleReason = IndoorIdleReason.NoWorkers,
            };
            var data = CreateData(noCrew, out _);
            var workers = Section(data, HdbfPanelSectionKind.Workers);
            TestAssert.Equal("—", Row(workers, "Growth efficiency").Value);
            TestAssert.False(Row(workers, "Growth efficiency").HasBar, "no green bar without a crew");
            TestAssert.Equal("No workers on duty", data.IdleReasonText);

            var water = Section(data, HdbfPanelSectionKind.Water);
            TestAssert.Equal("—", Row(water, "Coverage C").Value);
            TestAssert.False(Row(water, "Coverage C").HasBar, "no coverage bar without a crew");
            TestAssert.Equal("60%", Row(water, "Minimum C_min").Value,
                "C_min is a tier constant and stays visible");
        }

        // ------------------------------------------------------------------ WP-05 / RE-05 pipes

        /// <summary>
        /// WP-05/RE-05: the pipe port row names the probe reason (no more single misleading
        /// "Not connected"), maps PortOff to Disabled and keeps the legacy fallback only when no
        /// probe ran at all.
        /// </summary>
        public static void TestPipePortRowNamesTheProbeReason()
        {
            var scenarios = new[]
            {
                (PipeProbeReason.Connected, "Connected"),
                (PipeProbeReason.NoPipeNetwork, "No pipe network"),
                (PipeProbeReason.NoPipeTank, "No water tank in pipe network"),
                (PipeProbeReason.TankDry, "Pipe tank empty"),
                (PipeProbeReason.AdapterUnavailable, "Timber Pipes unavailable"),
                (PipeProbeReason.BindingFailed, "Pipe binding failed"),
                (PipeProbeReason.PortOff, "Disabled"),
            };
            foreach (var scenario in scenarios)
            {
                var snapshot = IntermediateFixture() with
                {
                    PipeProbeState = new PipeProbe(scenario.Item1, 2, 1, string.Empty),
                };
                var output = Section(CreateData(snapshot, out _), HdbfPanelSectionKind.Output);
                TestAssert.Equal(scenario.Item2, Row(output, "Pipe network (auto-refill)").Value,
                    "probe reason " + scenario.Item1);
                TestAssert.True(Row(output, "Pipe network (auto-refill)").Tooltip
                        .StartsWith("Auto-refill draws only from", StringComparison.Ordinal),
                    "the pipe row tooltip explains where auto-refill draws from");
            }

            // Unknown = no probe ran: keep the pre-probe fallback (pipe supply says connected).
            var unprobed = Section(CreateData(IntermediateFixture(), out _), HdbfPanelSectionKind.Output);
            TestAssert.Equal("Connected", Row(unprobed, "Pipe network (auto-refill)").Value);

            var noPort = IntermediateFixture() with
            {
                PipePort = false,
                WaterSupply = WaterSupplyKind.None,
                PipeProbeState = PipeProbe.Unknown,
            };
            TestAssert.Equal("Not applicable",
                Row(Section(CreateData(noPort, out _), HdbfPanelSectionKind.Output),
                    "Pipe network (auto-refill)").Value);
        }

        public static void TestOutputCapacityMatchesEveryShippedBlueprint()
        {
            var blueprints = Directory.GetFiles(RepoPath("data/Buildings"), "*.blueprint.json",
                SearchOption.AllDirectories);
            TestAssert.True(blueprints.Length >= 8, "expected the eight shipped tier blueprints");
            var inventoryPattern = new Regex(
                "\"SimpleOutputInventorySpec\"\\s*:\\s*\\{[^}]*\"Capacity\"\\s*:\\s*(?<capacity>\\d+)",
                RegexOptions.Singleline);
            var slotPattern = new Regex(
                "\"HdbfBoxFarmSpec\"\\s*:\\s*\\{[^}]*\"PlotCapacity\"\\s*:\\s*(?<slots>\\d+)",
                RegexOptions.Singleline);
            var checkedCount = 0;
            foreach (var blueprint in blueprints)
            {
                var text = File.ReadAllText(blueprint);
                var match = inventoryPattern.Match(text);
                var slots = slotPattern.Match(text);
                if (!match.Success || !slots.Success)
                {
                    continue;
                }

                checkedCount++;
                var slotCapacity = int.Parse(slots.Groups["slots"].Value,
                    System.Globalization.CultureInfo.InvariantCulture);
                TestAssert.Equal(HdbfIndoorPanelLimits.OutputCapacity(slotCapacity),
                    int.Parse(match.Groups["capacity"].Value, System.Globalization.CultureInfo.InvariantCulture),
                    "panel output capacity must mirror " + Path.GetFileName(blueprint));
            }

            TestAssert.True(checkedCount >= 8, "every tier blueprint must declare the output capacity");
        }

        // ------------------------------------------------------------------ warnings / reasons

        public static void TestCleanFarmHasNoWarningRows()
        {
            var clean = IntermediateFixture() with
            {
                Warnings = IndoorWarningFlags.None,
                MaintenanceMaterialMissing = false,
                MaintenanceCyclesMissed = 0,
            };
            var data = CreateData(clean, out _);
            foreach (var section in data.Sections)
            {
                TestAssert.Equal(0, section.Rows.Count(row => row.IsWarning),
                    "clean farm warning in card " + section.Kind);
            }
        }

        public static void TestWarningsKeepTheirIconAndTextMarker()
        {
            var flagged = IntermediateFixture() with
            {
                Warnings = IndoorWarningFlags.NoPower | IndoorWarningFlags.OutputFull,
                Power = PowerConnection.Grid,
                PowerConnected = false,
            };
            var data = CreateData(flagged, out _);
            var warnings = data.Sections.SelectMany(section => section.Rows).Where(row => row.IsWarning).ToArray();
            TestAssert.True(warnings.Length >= 2);
            foreach (var row in warnings)
            {
                TestAssert.True(row.IconKey.Length > 0, "warning row must carry an icon key");
                TestAssert.True(row.Marker.Length > 0, "warning row must carry a text marker");
                TestAssert.True(row.Label.Length > 0, "warning row must carry text");
            }

            // The pure model has no colour property at all (colour may never be the only carrier).
            foreach (var property in typeof(HdbfPanelRow).GetProperties())
            {
                TestAssert.False(property.Name.Contains("Color", StringComparison.OrdinalIgnoreCase),
                    "the read model must not encode severity by colour only: " + property.Name);
            }
        }

        public static void TestActionButtonsCarryInlineBlockedReasons()
        {
            var pipe = CreateData(IntermediateFixture(), out _);
            TestAssert.False(pipe.CanRefill, "a pipe source is not the manual refill path");
            TestAssert.True(pipe.RefillReasonText.StartsWith("Auto-refill draws only from", StringComparison.Ordinal),
                "the pipe refill reason must explain where auto-refill draws from: " + pipe.RefillReasonText);
            TestAssert.Equal(pipe.RefillReasonText, pipe.RefillTooltip);

            var noWorkers = CreateData(IntermediateFixture() with
            {
                WaterSupply = WaterSupplyKind.Manual,
                Workers = 0,
                MaxWorkers = 4,
            }, out _);
            TestAssert.False(noWorkers.CanRefill);
            TestAssert.Equal("No worker is on duty to fetch water.", noWorkers.RefillReasonText);

            var blocked = CreateData(IntermediateFixture() with
            {
                WaterSupply = WaterSupplyKind.Manual,
                Warnings = IndoorWarningFlags.EntranceBlocked,
            }, out _);
            TestAssert.False(blocked.CanRefill);
            TestAssert.Equal("The entrance is blocked; a worker cannot leave for water.", blocked.RefillReasonText);

            var usable = CreateData(IntermediateFixture() with
            {
                WaterSupply = WaterSupplyKind.Manual,
                Warnings = IndoorWarningFlags.MissingMaterial,
            }, out _);
            TestAssert.True(usable.CanRefill);
            TestAssert.Equal(string.Empty, usable.RefillReasonText);
            TestAssert.Equal("Requests one manual refill trip.", usable.RefillTooltip);

            var material = CreateData(IntermediateFixture(), out _);
            TestAssert.False(material.CanPerformMaintenance);
            TestAssert.Equal("Missing materials: Plank ×8", material.MaintenanceReasonText);
            TestAssert.True(material.MaintenanceUrgent);

            // WP-01/L-08: 0 cycles means "not due yet" and the button is disabled with its own
            // specific reason; having cycles and material enables the (catch-up) click again.
            var notDue = CreateData(IntermediateFixture() with
            {
                MaintenanceMaterialMissing = false,
                MaintenanceCyclesMissed = 0,
                MaintenanceCyclesToCatchUp = 0,
                Warnings = IndoorWarningFlags.None,
            }, out _);
            TestAssert.False(notDue.CanPerformMaintenance);
            TestAssert.Equal("Maintenance is not due yet.", notDue.MaintenanceReasonText);
            TestAssert.Equal(notDue.MaintenanceReasonText, notDue.MaintenanceTooltip,
                "a disabled maintenance button explains itself in its tooltip");

            var due = CreateData(IntermediateFixture() with
            {
                MaintenanceMaterialMissing = false,
                Warnings = IndoorWarningFlags.None,
                MaintenanceCyclesToCatchUp = 3,
            }, out _);
            TestAssert.True(due.CanPerformMaintenance, "material + due cycles enable the catch-up click");
            TestAssert.Equal(string.Empty, due.MaintenanceReasonText);
            TestAssert.True(due.MaintenanceTooltip.Contains("every overdue cycle", StringComparison.Ordinal),
                "the enabled maintenance tooltip describes the catch-up behaviour: " + due.MaintenanceTooltip);
        }

        // ------------------------------------------------------------------ formatting rules

        public static void TestEveryRowCarriesIconLabelAndSafeFill()
        {
            var data = CreateData(IntermediateFixture(), out _);
            var labelledRows = 0;
            foreach (var section in data.Sections)
            {
                foreach (var row in section.Rows)
                {
                    TestAssert.True(row.IconKey.Length > 0, "every row names an icon: " + section.Kind);
                    if (row.IsWarning)
                    {
                        continue;
                    }

                    labelledRows++;
                    TestAssert.True(row.Label.Length > 0, "row label empty in " + section.Kind);
                    TestAssert.True(row.Value.Length > 0, "row value empty in " + section.Kind);
                    if (row.HasBar)
                    {
                        TestAssert.True(row.Bar >= 0f && row.Bar <= 1f, "bar fill must be in [0,1]");
                        TestAssert.True(row.BarText.Length > 0, "a bar always carries its value text");
                    }
                }
            }

            TestAssert.True(labelledRows >= 20, "the panel renders all chapter-19.11 rows: " + labelledRows);
        }

        public static void TestNoRenderedTextLeaksBracesRawKeysNanOrInfinity()
        {
            var data = CreateData(IntermediateFixture(), out _);
            var values = AllStrings(data);
            foreach (var value in values)
            {
                TestAssert.False(value.Contains("{", StringComparison.Ordinal)
                                 || value.Contains("}", StringComparison.Ordinal),
                    "the panel must never render an unformatted template: " + value);
                TestAssert.False(value.Contains("HDBF.", StringComparison.Ordinal),
                    "the panel must never render a raw localization key: " + value);
                TestAssert.False(value.Contains("NaN", StringComparison.Ordinal)
                                 || value.Contains("Infinity", StringComparison.Ordinal),
                    "unknown numbers render as the dash: " + value);
            }
        }

        public static void TestUnknownValuesRenderTheDashAndNeverNanOrInfinity()
        {
            var unknown = IntermediateFixture() with
            {
                TankDaysRemaining = float.PositiveInfinity,
                MaintenanceDueDay = float.NaN,
                CropPolicy = CropPolicy.Rotation,
                FixedCropId = string.Empty,
                RotationSequence = string.Empty,
            };
            var data = CreateData(unknown, out _);
            var water = Section(data, HdbfPanelSectionKind.Water);
            TestAssert.True(Row(water, "Tank").BarText.Contains("—", StringComparison.Ordinal),
                Row(water, "Tank").BarText);
            TestAssert.Equal("—", Row(Section(data, HdbfPanelSectionKind.Maintenance), "Next due day").Value);
            TestAssert.Equal("—",
                Section(data, HdbfPanelSectionKind.Crops).Rows[1].Value);
        }

        public static void TestEmptySnapshotProducesTheHiddenModel()
        {
            var empty = HdbfIndoorPanelFactory.Create(null, KeyEcho);
            TestAssert.False(empty.HasData);
            TestAssert.Equal(string.Empty, empty.MetaLine);
            TestAssert.Equal(0, empty.Sections.Count);
        }

        public static void TestFailureReasonsAreWhitelistedAndSpecific()
        {
            var en = LoadTexts("enUS");
            var localize = Localizer(en, new List<string>());
            var specificKeys = new[]
            {
                HdbfUiLocKeys.ErrorMaterialsMissing, HdbfUiLocKeys.ErrorDenseDisabled,
                HdbfUiLocKeys.ErrorNotBoxFarm, HdbfUiLocKeys.ErrorFarmNotActive,
                HdbfUiLocKeys.ErrorRefillUnavailable, HdbfUiLocKeys.ErrorCropNotAvailable,
                HdbfUiLocKeys.ErrorMaintenanceUnavailable, HdbfUiLocKeys.ErrorInvalidMode,
                HdbfUiLocKeys.ErrorMaintenanceNotDue, HdbfUiLocKeys.ErrorRefillTankFull,
                HdbfUiLocKeys.ErrorMissingMod, HdbfUiLocKeys.ModeSwitchCooldown,
                HdbfUiLocKeys.PortNotApplicable,
                HdbfUiLocKeys.ErrorNoWorkers, HdbfUiLocKeys.ErrorEntranceBlocked,
                HdbfUiLocKeys.ErrorNoWaterSource, HdbfUiLocKeys.ErrorWaterSourceUnreachable,
                HdbfUiLocKeys.ErrorWaterContaminated, HdbfUiLocKeys.ErrorNoActionsLeft,
                HdbfUiLocKeys.ErrorRefillPending, HdbfUiLocKeys.ErrorOutputFull,
            };
            foreach (var key in specificKeys)
            {
                TestAssert.Equal(en[key], HdbfIndoorPanelFormat.FormatCommandFailure(localize, key),
                    "failure key must render its own sentence: " + key);
            }

            var generic = en[HdbfUiLocKeys.ErrorActionFailed];
            TestAssert.Equal(generic, HdbfIndoorPanelFormat.FormatCommandFailure(localize, null));
            TestAssert.Equal(generic, HdbfIndoorPanelFormat.FormatCommandFailure(localize, "HDBF.Error.Unknown"));
            TestAssert.Equal(generic, HdbfIndoorPanelFormat.FormatCommandFailure(localize, string.Empty));
        }

        // ------------------------------------------------------------------ source rules

        public static void TestRefreshGateIsFourHertzAndFingerprintDriven()
        {
            var source = ReadUiFile("Panels/HdbfFarmPanelFragment.cs");
            TestAssert.True(source.Contains("RefreshIntervalSeconds = 0.25f", StringComparison.Ordinal),
                "REQ-V2-UI-01: the panel must refresh at most 4 times per second");
            TestAssert.True(source.Contains("Time.unscaledTime", StringComparison.Ordinal),
                "the refresh gate must use the unscaled wall clock");
            TestAssert.True(source.Contains("HdbfIndoorPanelFingerprint", StringComparison.Ordinal),
                "the dirty check must compare a pure value fingerprint");
            TestAssert.True(source.Contains("UpdateFragment", StringComparison.Ordinal));
            foreach (var forbidden in new[] { "override void Update", "Time.deltaTime", "void Tick(" })
            {
                TestAssert.False(source.Contains(forbidden, StringComparison.Ordinal),
                    "the fragment must not simulate per frame: " + forbidden);
            }
        }

        public static void TestPanelUsesTheVanillaControlVocabulary()
        {
            var sources = ReadUiPanelSources(onlyIndoorMapping: false);
            foreach (var token in new[]
                     {
                         "NineSliceVisualElement", "entity-sub-panel", "bg-sub-box--green",
                         "bg-sub-box--blue", "bg-sub-box--purple-striped", "button-game",
                         "entity-fragment__button", "worker-view", "progress-bar",
                         "status-list-element", "game-text-heading", "entity-panel__text",
                         "game-dropdown", "new Dropdown", "CreateLocalizable", "ProgressBar",
                         "RadioToggle", "ScrollerVisibility.Hidden", "ApplyLightText",
                         "CreateMetaRow", "CreateWarningRow", "CreateBar",
                     })
            {
                TestAssert.True(sources.Contains(token, StringComparison.Ordinal),
                    "the panel must compose the original control/class vocabulary; missing " + token);
            }

            // No invented full-screen background / no hand-drawn texture / no bare Unity dropdown.
            // Requirement C: the six known vanilla sub-box tones are the only card backgrounds.
            foreach (var forbidden in new[]
                     {
                         "new Texture2D", "GUI.DrawTexture", "DropdownField",
                         "bg-sub-box--pale-purple", "style.backgroundImage = new StyleBackground(new Color",
                     })
            {
                TestAssert.False(sources.Contains(forbidden, StringComparison.Ordinal),
                    "the panel must not invent its own look: " + forbidden);
            }

            // The only raw background colour the panel ever sets is the 3 px warning accent strip
            // (requirement I); the fragment/root itself stays transparent (requirement C).
            TestAssert.Equal(1, CountOccurrences(sources, "backgroundColor"),
                "only the warning accent strip may set a raw background colour");
            TestAssert.True(sources.Contains("strip.style.backgroundColor", StringComparison.Ordinal),
                "the warning accent strip is the one allowed colour-only encoding");

            // The view templates used for the gold buttons / worker chips are the shipped ones.
            TestAssert.Equal("Game/EntityPanel/DebugButton", HdbfIndoorPanelStyle.ButtonTemplate);
            TestAssert.Equal("Game/EntityPanel/WorkerView", HdbfIndoorPanelStyle.WorkerChipTemplate);
        }

        private static int CountOccurrences(string text, string token)
        {
            var count = 0;
            var index = 0;
            while ((index = text.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += token.Length;
            }

            return count;
        }

        public static void TestUiSourcesCarryNoRemovedV1PlotSurface()
        {
            var sources = ReadUiPanelSources(onlyIndoorMapping: false);
            foreach (var term in new[]
                     {
                         "工作范围", "托管格", "开垦", "地块高亮", "框选", "高亮", "Plot", "plot",
                         "Tillable", "tillable", "Highlight", "highlight", "RegisteredPlots",
                         "IBoxFarmWarningQuery", "IBoxFarmLocator", "TrySetPlotHighlight",
                         "GetWarningGroups", "TryExcludePlot", "TryIncludePlot",
                     })
            {
                TestAssert.False(sources.Contains(term, StringComparison.Ordinal),
                    "v2 UI must not reference the removed v1 plot surface: " + term);
            }
        }

        // ------------------------------------------------------------------ command bridge

        public static void TestCommandBridgeUsesTheRegisteredV2ServiceMembers()
        {
            var query = new FakeQueryService { Snapshot = IntermediateFixture() };
            var command = new FakeCommandService();
            var bridge = new HdbfIndoorCommandBridge(query, command);
            TestAssert.True(bridge.IsAvailable, "the bridge resolves GetIndoorSnapshot by name");

            var snapshot = bridge.GetIndoorSnapshot(EntityId);
            TestAssert.NotNull(snapshot);
            TestAssert.Equal(150, snapshot!.Capacity);
            TestAssert.Null(bridge.GetIndoorSnapshot(Guid.Empty));

            command.CropFailureKey = HdbfUiLocKeys.ErrorCropNotAvailable;
            TestAssert.False(bridge.TrySetCropPolicy(EntityId, CropPolicy.Fixed, "Bogus", out var cropFailure));
            TestAssert.Equal(HdbfUiLocKeys.ErrorCropNotAvailable, cropFailure);
            TestAssert.Equal(1, command.CropCalls);

            command.CropFailureKey = null;
            TestAssert.True(bridge.TrySetCropPolicy(EntityId, CropPolicy.Rotation, null, out var none));
            TestAssert.Null(none);
            TestAssert.Equal(CropPolicy.Rotation, command.LastPolicy);

            command.RefillFailureKey = HdbfUiLocKeys.ErrorNoWaterSource;
            TestAssert.False(bridge.TryRequestManualRefill(EntityId, out var refillFailure));
            TestAssert.Equal(HdbfUiLocKeys.ErrorNoWaterSource, refillFailure);
            TestAssert.Equal(1, command.RefillCalls);

            // WP-02: the auto-maintenance toggle binds the Guid wrapper exactly like the others.
            command.AutoMaintenanceFailureKey = HdbfUiLocKeys.ErrorFarmNotActive;
            TestAssert.False(bridge.TrySetAutoMaintenance(EntityId, false, out var autoFailure));
            TestAssert.Equal(HdbfUiLocKeys.ErrorFarmNotActive, autoFailure);
            TestAssert.Equal(1, command.AutoMaintenanceCalls);
            TestAssert.False(command.LastAutoMaintenance);

            command.AutoMaintenanceFailureKey = null;
            TestAssert.True(bridge.TrySetAutoMaintenance(EntityId, true, out var autoNone));
            TestAssert.Null(autoNone);
            TestAssert.True(command.LastAutoMaintenance);
        }

        public static void TestCommandBridgeDegradesWithoutTheV2Members()
        {
            var bridge = new HdbfIndoorCommandBridge(new object(), new object());
            TestAssert.False(bridge.IsAvailable);
            TestAssert.Null(bridge.GetIndoorSnapshot(EntityId));
            TestAssert.False(bridge.TrySetCropPolicy(EntityId, CropPolicy.Rotation, null, out var cropKey));
            TestAssert.Equal(HdbfUiLocKeys.ErrorActionFailed, cropKey);
            TestAssert.False(bridge.TryRequestManualRefill(EntityId, out var refillKey));
            TestAssert.Equal(HdbfUiLocKeys.ErrorActionFailed, refillKey);
            TestAssert.False(bridge.TrySetAutoMaintenance(EntityId, true, out var autoKey));
            TestAssert.Equal(HdbfUiLocKeys.ErrorActionFailed, autoKey);

            var nullBridge = new HdbfIndoorCommandBridge(null, null);
            TestAssert.False(nullBridge.IsAvailable);
            TestAssert.Null(nullBridge.GetIndoorSnapshot(EntityId));
        }

        // ------------------------------------------------------------------ helpers

        private static string PowerValue(HdbfIndoorPanelData data)
        {
            return Row(Section(data, HdbfPanelSectionKind.Power), "Power").Value;
        }

        private static HdbfPanelSection Section(HdbfIndoorPanelData data, HdbfPanelSectionKind kind)
        {
            return data.Sections.Single(section => section.Kind == kind);
        }

        private static HdbfPanelRow Row(HdbfPanelSection section, string label)
        {
            return section.Rows.Single(row => string.Equals(row.Label, label, StringComparison.Ordinal));
        }

        private static List<string> AllStrings(HdbfIndoorPanelData data)
        {
            var values = new List<string>
            {
                data.MetaLine, data.MetaTooltip, data.ModeTransitionText,
                data.WorkerRosterNote, data.CropPolicyLabel, data.CropPolicyText,
                data.CropTooltip, data.RefillTooltip, data.RefillReasonText, data.MaintenanceTooltip,
                data.MaintenanceReasonText, data.IdleReasonText, data.IdleReasonTooltip,
                data.AutoMaintenanceTooltip, data.WaterPathText,
            };

            foreach (var chip in data.WorkerChips)
            {
                values.Add(chip.Name);
                values.Add(chip.Tooltip);
            }

            foreach (var choice in data.CropChoices)
            {
                values.Add(choice.Text);
            }

            foreach (var section in data.Sections)
            {
                values.Add(section.Title);
                foreach (var row in section.Rows)
                {
                    values.Add(row.Label);
                    values.Add(row.Value);
                    values.Add(row.Tooltip);
                    values.Add(row.BarText);
                }
            }

            return values.Where(value => !string.IsNullOrEmpty(value)).ToList();
        }

        private static IndoorFarmSnapshot IntermediateFixture()
        {
            return new IndoorFarmSnapshot(
                HdbfTier.Intermediate,
                HdbfFaction.IronTeeth,
                Workers: 3,
                MaxWorkers: 4,
                Mode: IndoorMode.Dense,
                ModeTransitioning: true,
                Capacity: 150,
                ConfiguredSlots: 120,
                EffectiveSlots: 114,
                EmptySlots: 40,
                GrowingSlots: 70,
                MatureSlots: 10,
                BacklogActions: 2,
                Tank: 2.4f,
                TankCapacity: 10.8f,
                TankDaysRemaining: 2.0f,
                Coverage: 0.73f,
                MinCoverage: 0.60f,
                WaterSupply: WaterSupplyKind.Pipe,
                MaintenanceDueDay: 24.5f,
                MaintenanceCyclesMissed: 1,
                MaintenanceMaterialMissing: true,
                Power: PowerConnection.NotRequired,
                PowerConnected: false,
                CropPolicy: CropPolicy.Fixed,
                FixedCropId: "Corn",
                RotationSequence: "Carrot → Potato",
                OutputOccupied: 12,
                OutputPortConnected: true,
                PipePort: true,
                LogisticsPort: true,
                PowerPort: false,
                Warnings: IndoorWarningFlags.MissingMaterial,
                OutputInventoryPortState: "Ok")
            {
                // WP-01/02/05/06 init properties (plan section 3.6): one owed cycle with a known
                // bill, automatic maintenance on and an unprobed pipe (legacy fallback).
                Paused = false,
                AutoMaintenance = true,
                MaintenanceCyclesToCatchUp = 1,
                MaintenanceCatchUpMaterialAmount = 8,
                IdleReason = IndoorIdleReason.MaintenanceDue,
                ManualRefillSourceAvailable = false,
                PipeProbeState = PipeProbe.Unknown,
            };
        }

        private static HdbfIndoorPanelData CreateData(IndoorFarmSnapshot snapshot, out List<string> missingKeys)
        {
            var en = LoadTexts("enUS");
            var missing = new List<string>();
            var data = HdbfIndoorPanelFactory.Create(snapshot, Localizer(en, missing));
            missingKeys = missing;
            return data;
        }

        private static string KeyEcho(string key) => key;

        private static Func<string, string> Localizer(Dictionary<string, string> texts, List<string> missing)
        {
            return key =>
            {
                if (texts.TryGetValue(key, out var text))
                {
                    return text;
                }

                missing.Add(key);
                return "!" + key;
            };
        }

        // ------------------------------------------------------------------ file helpers

        private static string ReadUiPanelSources(bool onlyIndoorMapping)
        {
            var directory = Path.Combine(RepoRoot(), UiPanelFolder, "Panels");
            var builder = new StringBuilder();
            foreach (var file in Directory.GetFiles(directory, "*.cs", SearchOption.TopDirectoryOnly)
                         .OrderBy(path => path, StringComparer.Ordinal))
            {
                var name = Path.GetFileName(file);
                if (onlyIndoorMapping && !name.StartsWith("HdbfIndoor", StringComparison.Ordinal))
                {
                    continue;
                }

                builder.Append(File.ReadAllText(file, Encoding.UTF8)).Append('\n');
            }

            return builder.ToString();
        }

        private static string ReadUiFile(string relativePath)
        {
            return File.ReadAllText(Path.Combine(RepoRoot(), UiPanelFolder, relativePath), Encoding.UTF8);
        }

        /// <summary>
        /// Reads one embedded Gameplay source file (the game-facing half references Timberborn and
        /// is embedded as text, so its contract can still be asserted offline, REQ-V2-TEST-01).
        /// </summary>
        private static string ReadEmbeddedGameplaySource(string fileName)
        {
            var assembly = typeof(V2PanelContractTests).Assembly;
            var suffix = "." + fileName;
            foreach (var resource in assembly.GetManifestResourceNames())
            {
                if (!resource.EndsWith(suffix, StringComparison.Ordinal))
                {
                    continue;
                }

                using var stream = assembly.GetManifestResourceStream(resource);
                if (stream is null)
                {
                    continue;
                }

                using var reader = new StreamReader(stream, Encoding.UTF8);
                return reader.ReadToEnd();
            }

            throw new TestFailureException("missing embedded gameplay source: " + fileName);
        }

        private static string RepoPath(string relativePath)
        {
            return Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static string RepoRoot()
        {
            var metadata = typeof(V2PanelContractTests).Assembly
                .GetCustomAttributes(typeof(AssemblyMetadataAttribute), inherit: false)
                .OfType<AssemblyMetadataAttribute>()
                .FirstOrDefault(attribute => string.Equals(attribute.Key, RepoRootMetadataKey,
                    StringComparison.Ordinal));
            if (metadata is null || string.IsNullOrWhiteSpace(metadata.Value))
            {
                throw new TestFailureException("missing HdbfRepoRoot assembly metadata");
            }

            return metadata.Value!;
        }

        private static Dictionary<string, string> LoadTexts(string language)
        {
            var resource = "HDBF.Tests.Localizations." + language + ".csv";
            var assembly = typeof(V2PanelContractTests).Assembly;
            using var stream = assembly.GetManifestResourceStream(resource);
            if (stream is null)
            {
                throw new TestFailureException("missing embedded localization resource " + resource);
            }

            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return ParseCsv(reader.ReadToEnd());
        }

        private static Dictionary<string, string> ParseCsv(string text)
        {
            var texts = new Dictionary<string, string>(StringComparer.Ordinal);
            var lines = text.Replace("\r\n", "\n").Split('\n');
            for (var i = 1; i < lines.Length; i++)
            {
                if (lines[i].Trim().Length == 0)
                {
                    continue;
                }

                var fields = SplitCsvLine(lines[i]);
                if (fields.Count >= 2)
                {
                    texts[fields[0].Trim()] = fields[1];
                }
            }

            return texts;
        }

        private static List<string> SplitCsvLine(string line)
        {
            var fields = new List<string>();
            var builder = new StringBuilder();
            var inQuotes = false;
            for (var index = 0; index < line.Length; index++)
            {
                var character = line[index];
                if (inQuotes)
                {
                    if (character == '"')
                    {
                        if (index + 1 < line.Length && line[index + 1] == '"')
                        {
                            builder.Append('"');
                            index++;
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        builder.Append(character);
                    }
                }
                else if (character == '"')
                {
                    inQuotes = true;
                }
                else if (character == ',')
                {
                    fields.Add(builder.ToString());
                    builder.Clear();
                }
                else
                {
                    builder.Append(character);
                }
            }

            fields.Add(builder.ToString());
            return fields;
        }

        // ------------------------------------------------------------------ fakes

        private sealed class FakeQueryService
        {
            public IndoorFarmSnapshot Snapshot { get; init; } = new IndoorFarmSnapshot(
                HdbfTier.Basic, HdbfFaction.Folktails, 0, 2, IndoorMode.Normal, false, 60, 0, 0,
                60, 0, 0, 0, 0f, 3.6f, float.PositiveInfinity, 1f, 0.5f, WaterSupplyKind.None,
                0f, 0, false, PowerConnection.NotRequired, false, CropPolicy.Rotation, string.Empty,
                string.Empty, 0, false, true, false, false, IndoorWarningFlags.None, string.Empty);

            public IndoorFarmSnapshot? GetIndoorSnapshot(Guid entityId)
            {
                return entityId == EntityId ? Snapshot : null;
            }
        }

        private sealed class FakeCommandService
        {
            public string? CropFailureKey { get; set; }

            public string? RefillFailureKey { get; set; }

            public string? AutoMaintenanceFailureKey { get; set; }

            public int CropCalls { get; private set; }

            public int RefillCalls { get; private set; }

            public int AutoMaintenanceCalls { get; private set; }

            public CropPolicy LastPolicy { get; private set; }

            public bool LastAutoMaintenance { get; private set; }

            public bool TrySetCropPolicy(Guid entityId, CropPolicy policy, string fixedCropId,
                out string? failureKey)
            {
                CropCalls++;
                LastPolicy = policy;
                failureKey = CropFailureKey;
                return failureKey is null;
            }

            public bool TryRequestManualRefill(Guid entityId, out string? failureKey)
            {
                RefillCalls++;
                failureKey = RefillFailureKey ?? HdbfUiLocKeys.ErrorRefillUnavailable;
                return false;
            }

            public bool TrySetAutoMaintenance(Guid entityId, bool enabled, out string? failureKey)
            {
                AutoMaintenanceCalls++;
                LastAutoMaintenance = enabled;
                failureKey = AutoMaintenanceFailureKey;
                return failureKey is null;
            }
        }
    }
}
