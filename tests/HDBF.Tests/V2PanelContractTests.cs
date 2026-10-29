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
            TestAssert.Equal("70", Row(slots, "Growing").Value);
            TestAssert.Equal("10", Row(slots, "Ready").Value);
            TestAssert.Equal("2", Row(slots, "Backlog").Value);
            TestAssert.True(Row(slots, "Effective P_eff").Tooltip.Contains("P_eff", StringComparison.Ordinal),
                "the P_eff formula stays in the row tooltip");
        }

        // ------------------------------------------------------------------ growing now (UI-05)

        /// <summary>
        /// REQ-V2-UI-05: the live per-crop card renders one row per crop in snapshot order (name,
        /// "growing · ready" split, growth bar/percent and the slots/average/days tooltip), and an
        /// empty farm keeps the card with the single "nothing planted yet" row.
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
            TestAssert.Equal("4 growing · 2 ready", growing.Rows[1].Value);
            TestAssert.True(growing.Rows[1].HasBar, "each crop row owns a growth ProgressBar");
            TestAssert.Equal(0.5f, growing.Rows[1].Bar, 1e-4f);
            TestAssert.Equal("50%", growing.Rows[1].BarText);
            TestAssert.Equal("6 slots in use · average growth 50% · matures in 3.0 days",
                growing.Rows[1].Tooltip);
            TestAssert.Equal("Wheat", growing.Rows[2].Label, "the card keeps the snapshot order");
            TestAssert.Equal("0 growing · 1 ready", growing.Rows[2].Value);
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
                Warnings = IndoorWarningFlags.LowWater,
            };
            var data = CreateData(dry, out _);
            var water = Section(data, HdbfPanelSectionKind.Water);
            // G_irr = (0.30 - 0.25*0.60) / (0.75*0.60) = 1/3 -> growth 33 %, yield loss 67 %.
            var shortage = water.Rows.Single(row => row.Label.StartsWith("Water shortage", StringComparison.Ordinal));
            TestAssert.Equal("Water shortage: growth 33% (yield -67%)", shortage.Label);
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
            // 2026-10-29 bug fix: the panel capacity is tier-scaled (150 slots -> 600), not the old 50.
            TestAssert.Equal("12/600", Row(output, "Output stock").Value, "occupancy/capacity stays complete");
            TestAssert.Equal("12/600 stored", Row(output, "Output stock").BarText);
            TestAssert.True(Row(output, "Output stock").HasBar,
                "requirement F: the occupancy is a vanilla ProgressBar");
            TestAssert.Equal("Connected", Row(output, "Output port").Value);

            // Requirement E: the output port and the three physical ports are independent rows;
            // no combined "Pipe port Connected · ..." value can overlap the line above any more.
            TestAssert.Equal("Connected", Row(output, "Pipe port").Value);
            TestAssert.Equal("Connected", Row(output, "Logistics port").Value);
            TestAssert.Equal("Not applicable", Row(output, "Power port").Value);
            TestAssert.False(output.Rows.Any(row => row.Label == "Ports"),
                "the combined ports row must be gone (requirement E)");
            TestAssert.Equal(5, output.Rows.Count, "stock + output port + three port rows");
            TestAssert.True(Row(output, "Output stock").Tooltip.Contains("Ok", StringComparison.Ordinal),
                "the output adapter state stays in the tooltip");
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
            TestAssert.Equal("The pipe port supplies water automatically.", pipe.RefillReasonText);
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
            TestAssert.True(source.Contains("ComputeFingerprint", StringComparison.Ordinal),
                "the dirty check must compare a value fingerprint");
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
                data.MaintenanceReasonText,
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
                OutputInventoryPortState: "Ok");
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

            public int CropCalls { get; private set; }

            public int RefillCalls { get; private set; }

            public CropPolicy LastPolicy { get; private set; }

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
        }
    }
}
