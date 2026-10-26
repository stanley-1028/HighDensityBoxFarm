// ============================================================================
// HDBF v2 | Layer: L1 test | Owner: V2-UI-01
// Purpose: Contract of the v2 indoor panel: every Core snapshot field reaches a shipped
//          localization key, the four warning categories, the whitelisted failure reasons,
//          the "—" rule, the text-not-colour rule and the 4 Hz / fingerprint refresh gate.
//          It drives the REAL game-free panel mapping (linked HdbfIndoor*.cs sources).
// Requirements: REQ-V2-UI-01/02/03/04, REQ-V2-WATER-03, REQ-V2-READ-04, REQ-V2-TEST-01
// ============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using HDBF.Core;
using HDBF.Core.Abstractions;
using HDBF.Core.Formatting;
using HDBF.Core.Indoor;
using HDBF.UI.Localization;
using HDBF.UI.Panels;

namespace HDBF.Tests
{
    /// <summary>
    /// L1 contract of the V2-UI-01 card. The fragment itself references UIElements/Timberborn and
    /// cannot load here (REQ-V2-TEST-01), so the test links the game-free read model and pins both
    /// the source-level UI rules and the rendered text of a fully populated Core snapshot.
    /// </summary>
    public static class V2PanelContractTests
    {
        private const string RepoRootMetadataKey = "HdbfRepoRoot";
        private const string UiPanelFolder = "src/HDBF.UI";

        private static readonly Guid EntityId = new Guid("00000000-0000-0000-0000-0000000000aa");

        // ------------------------------------------------------------------ snapshot field map

        /// <summary>
        /// The frozen v2 panel DTO (docs/v2/INTERFACES.md section 9). Adding/removing a field must
        /// update the panel and this list in the same change, otherwise the panel silently stops
        /// showing real state.
        /// </summary>
        private static readonly string[] SnapshotFields =
        {
            "Tier", "Faction", "Workers", "MaxWorkers", "Mode", "ModeTransitioning",
            "Capacity", "ConfiguredSlots", "EffectiveSlots", "EmptySlots", "GrowingSlots",
            "MatureSlots", "BacklogActions", "Tank", "TankCapacity", "TankDaysRemaining",
            "Coverage", "MinCoverage", "WaterSupply", "MaintenanceDueDay",
            "MaintenanceCyclesMissed", "MaintenanceMaterialMissing", "Power", "PowerConnected",
            "CropPolicy", "FixedCropId", "RotationSequence", "OutputOccupied", "OutputPortConnected",
            "PipePort", "LogisticsPort", "PowerPort", "Warnings", "OutputInventoryPortState",
        };

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

        // ------------------------------------------------------------------ full read model

        public static void TestHeaderBlockRendersTierFactionWorkersAndMode()
        {
            var data = CreateData(IntermediateFixture(), out var missing);
            TestAssert.Equal(0, missing.Count, "unmapped keys: " + string.Join(", ", missing));
            TestAssert.Equal("High-Density Box Farm (Intermediate)", data.Title);
            TestAssert.Equal("Tier: Intermediate", data.TierText);
            TestAssert.Equal("Faction: IronTeeth", data.FactionText);
            TestAssert.Equal("Workers 3/4", data.WorkersText);
            TestAssert.Equal("Mode: Dense planting", data.ModeText);
            TestAssert.Equal("Mode changing; output is reduced today.", data.ModeTransitionText);
            TestAssert.Equal(IndoorMode.Dense, data.ModeValue);
        }

        public static void TestTransitionRowIsEmptyWhenNoTransitionRuns()
        {
            var data = CreateData(IntermediateFixture() with { ModeTransitioning = false }, out _);
            TestAssert.Equal(string.Empty, data.ModeTransitionText);
        }

        public static void TestSlotBlockRendersEveryCountAndTheEffectiveFormula()
        {
            var data = CreateData(IntermediateFixture(), out _);
            TestAssert.Equal("Slots 120/150", data.SlotsText);
            TestAssert.Equal("Effective slots 114", data.EffectiveSlotsText);
            TestAssert.Equal("Effective slots P_eff = min(150, 3 × 38).", data.EffectiveSlotsTooltip);
            TestAssert.Equal("Empty slots 40", data.EmptySlotsText);
            TestAssert.Equal("Growing 70", data.GrowingSlotsText);
            TestAssert.Equal("Ready to harvest 10", data.MatureSlotsText);
            TestAssert.Equal("Action backlog 2", data.BacklogText);
        }

        public static void TestWaterBlockRendersUnitsDaysCoverageAndSupply()
        {
            var data = CreateData(IntermediateFixture(), out _);
            TestAssert.Equal("Tank 2.4/10.8 units (about 2.0 days)", data.TankText);
            TestAssert.Equal("Coverage C 73%", data.CoverageText);
            TestAssert.Equal("Minimum C_min 60%", data.MinCoverageText);
            TestAssert.Equal("Water source: pipe", data.WaterSupplyText);
            TestAssert.Equal("Tank units and remaining days; coverage C must stay above C_min.", data.TankTooltip);
            TestAssert.Equal(string.Empty, data.WaterShortageText, "C >= C_min must not derate growth");
            TestAssert.True(data.CanRefill == false, "a pipe source is not the manual refill path");
            TestAssert.Equal("The pipe port supplies water automatically.", data.RefillTooltip);
        }

        public static void TestWaterShortageShowsTheGrowthPenalty()
        {
            var dry = IntermediateFixture() with
            {
                Coverage = 0.30f,
                MinCoverage = 0.60f,
                Warnings = IndoorWarningFlags.LowWater,
                WaterSupply = WaterSupplyKind.Manual,
            };
            var data = CreateData(dry, out _);
            // G_irr = (0.30 - 0.25*0.60) / (0.75*0.60) = 0.3333 -> floored 33 %.
            TestAssert.Equal("Water shortage: growth 33%", data.WaterShortageText);
            TestAssert.True(data.CanRefill, "manual supply enables the refill action");
            TestAssert.Equal("Requests one manual refill trip.", data.RefillTooltip);
        }

        public static void TestMaintenanceBlockListsMaterialAndCount()
        {
            var data = CreateData(IntermediateFixture(), out _);
            TestAssert.Equal("Maintenance due day 24.5", data.MaintenanceDueText);
            TestAssert.Equal("Overdue cycles 1", data.MaintenanceCyclesText);
            TestAssert.Equal("Missing materials: Plank ×8", data.MaintenanceMaterialsText);
            TestAssert.True(data.CanPerformMaintenance, "plank tier supports the maintenance action");
            TestAssert.True(data.MaintenanceUrgent, "missing material marks the action urgent");
        }

        public static void TestMaintenanceBlockForMaterialFreeTier()
        {
            var basic = IntermediateFixture() with
            {
                Tier = HdbfTier.Basic,
                Faction = HdbfFaction.Folktails,
                Warnings = IndoorWarningFlags.None,
                MaintenanceMaterialMissing = false,
            };
            var data = CreateData(basic, out _);
            TestAssert.Equal("No material upkeep", data.MaintenanceDueText);
            TestAssert.False(data.CanPerformMaintenance, "the basic tier has no material upkeep");
            TestAssert.Equal(string.Empty, data.MaintenanceMaterialsText);
        }

        public static void TestPowerRowMapsEveryContractState()
        {
            var top = IntermediateFixture() with { Tier = HdbfTier.Top, Power = PowerConnection.Grid, PowerConnected = false };
            TestAssert.Equal("Power: no power", CreateData(top, out _).PowerText);
            TestAssert.Equal("Power: grid", CreateData(top with { PowerConnected = true }, out _).PowerText);
            TestAssert.Equal("Power: off-grid", CreateData(top with { Power = PowerConnection.OffGrid }, out _).PowerText);
            TestAssert.Equal("Power: not required",
                CreateData(IntermediateFixture() with { Power = PowerConnection.NotRequired }, out _).PowerText);
        }

        public static void TestCropStrategyRendersRotationAndFixedCrop()
        {
            var data = CreateData(IntermediateFixture(), out _);
            TestAssert.Equal("Crop policy: fixed crop", data.CropPolicyText);
            TestAssert.Equal("Fixed crop: Corn", data.FixedCropText);
            TestAssert.Equal("Rotation: Carrot → Potato", data.RotationText);
            TestAssert.Equal("Corn", data.SelectedCropId);
            TestAssert.Equal(7, data.CropChoices.Count, "auto rotation + six IronTeeth crops");
            TestAssert.Equal(string.Empty, data.CropChoices[0].Id, "the first choice is automatic rotation");
            TestAssert.Equal("auto rotation", data.CropChoices[0].Text);
            TestAssert.True(data.CropChoices.Any(choice => choice.Id == "Corn" && choice.Text == "Corn"),
                "the fixed crop is one of the dropdown choices");
        }

        public static void TestCropStrategyDefaultsToRotationAndDashSequence()
        {
            var rotation = IntermediateFixture() with
            {
                CropPolicy = CropPolicy.Rotation,
                FixedCropId = string.Empty,
                RotationSequence = string.Empty,
            };
            var data = CreateData(rotation, out _);
            TestAssert.Equal("Crop policy: auto rotation", data.CropPolicyText);
            TestAssert.Equal(string.Empty, data.FixedCropText);
            TestAssert.Equal("Rotation: —", data.RotationText);
            TestAssert.Equal(string.Empty, data.SelectedCropId);
        }

        public static void TestOutputBlockRendersStockAndPortStates()
        {
            var data = CreateData(IntermediateFixture(), out _);
            TestAssert.Equal("Output stock 12", data.OutputStockText);
            TestAssert.Equal("Output port: Connected", data.OutputPortText);
            TestAssert.True(data.PortsText.Contains("Pipe port Connected", StringComparison.Ordinal), data.PortsText);
            TestAssert.True(data.PortsText.Contains("Logistics port Connected", StringComparison.Ordinal), data.PortsText);
            TestAssert.True(data.PortsText.Contains("Power port Not applicable", StringComparison.Ordinal), data.PortsText);
            TestAssert.True(data.OutputTooltip.Contains("Ok", StringComparison.Ordinal),
                "the output adapter state is shown in the tooltip: " + data.OutputTooltip);
        }

        // ------------------------------------------------------------------ warnings / problems

        public static void TestFourWarningCategoriesCarryTextMarkers()
        {
            var flagged = IntermediateFixture() with
            {
                Warnings = IndoorWarningFlags.NoWater | IndoorWarningFlags.MaintenanceStalled
                           | IndoorWarningFlags.NoPower | IndoorWarningFlags.EntranceBlocked,
            };
            var data = CreateData(flagged, out _);
            TestAssert.Equal(4, data.StatusRows.Count, "the four v2 warning categories");
            TestAssert.Equal("Water shortage", data.StatusRows[0].Text);
            TestAssert.Equal("Maintenance overdue", data.StatusRows[1].Text);
            TestAssert.Equal("No power", data.StatusRows[2].Text);
            TestAssert.Equal("Entrance blocked", data.StatusRows[3].Text);
            TestAssert.Equal(IndoorWarningSeverity.Critical, data.StatusRows[0].Severity);
            TestAssert.Equal(IndoorWarningSeverity.Critical, data.StatusRows[1].Severity);

            // Colours must not be the only carrier: every row carries a text marker, and the pure
            // read model has no colour property at all.
            foreach (var row in data.StatusRows)
            {
                TestAssert.True(row.Marker.Length > 0, "warning row must carry a text marker");
                TestAssert.True(row.Text.Length > 0, "warning row must carry text");
            }

            foreach (var property in typeof(HdbfPanelStatusRow).GetProperties())
            {
                TestAssert.False(property.Name.Contains("Color", StringComparison.OrdinalIgnoreCase)
                                 || property.Name.Contains("Rgba", StringComparison.OrdinalIgnoreCase),
                    "the read model must not encode severity by colour only: " + property.Name);
            }
        }

        public static void TestLowWaterWarningIsHighNotCritical()
        {
            var data = CreateData(IntermediateFixture() with { Warnings = IndoorWarningFlags.LowWater }, out _);
            TestAssert.Equal(1, data.StatusRows.Count);
            TestAssert.Equal("Water low", data.StatusRows[0].Text);
            TestAssert.Equal(IndoorWarningSeverity.High, data.StatusRows[0].Severity);
        }

        public static void TestProblemRowsListConcreteReasons()
        {
            var broken = IntermediateFixture() with
            {
                FixedCropId = "BogusCrop",
                WaterSupply = WaterSupplyKind.None,
                Warnings = IndoorWarningFlags.MissingMaterial | IndoorWarningFlags.NoWater
                           | IndoorWarningFlags.NoWorkers | IndoorWarningFlags.EntranceBlocked
                           | IndoorWarningFlags.OutputFull,
            };
            var data = CreateData(broken, out _);
            TestAssert.Equal(6, data.ProblemRows.Count, "material/water/workers/entrance/output/crop");
            TestAssert.Equal("Missing materials: Plank ×8", data.ProblemRows[0]);
            TestAssert.True(data.ProblemRows[1].StartsWith("Water shortage", StringComparison.Ordinal),
                data.ProblemRows[1]);
            TestAssert.True(data.ProblemRows[1].Contains("No water source", StringComparison.Ordinal),
                "water shortage must name the missing source: " + data.ProblemRows[1]);
            TestAssert.Equal("No workers on duty", data.ProblemRows[2]);
            TestAssert.Equal("Entrance blocked", data.ProblemRows[3]);
            TestAssert.Equal("Output inventory full", data.ProblemRows[4]);
            TestAssert.Equal("Invalid fixed crop", data.ProblemRows[5]);
        }

        public static void TestCleanFarmHasNoWarningOrProblemRows()
        {
            var data = CreateData(IntermediateFixture() with { Warnings = IndoorWarningFlags.None }, out _);
            TestAssert.Equal(0, data.StatusRows.Count);
            TestAssert.Equal(0, data.ProblemRows.Count);
        }

        // ------------------------------------------------------------------ dash / failure rules

        public static void TestUnknownValuesRenderTheDashAndNeverNanOrInfinity()
        {
            var unknown = IntermediateFixture() with
            {
                TankDaysRemaining = float.PositiveInfinity,
                MaintenanceDueDay = float.NaN,
                RotationSequence = string.Empty,
            };
            var data = CreateData(unknown, out _);
            TestAssert.Equal("Tank 2.4/10.8 units (about — days)", data.TankText);
            TestAssert.Equal("Maintenance due day —", data.MaintenanceDueText);
            TestAssert.Equal("Rotation: —", data.RotationText);

            foreach (var property in typeof(HdbfIndoorPanelData).GetProperties())
            {
                if (property.PropertyType != typeof(string))
                {
                    continue;
                }

                var text = (string?)property.GetValue(data) ?? string.Empty;
                TestAssert.False(text.Contains("NaN", StringComparison.Ordinal),
                    property.Name + " must render unknown values as the dash");
                TestAssert.False(text.Contains("Infinity", StringComparison.Ordinal),
                    property.Name + " must render unknown values as the dash");
                TestAssert.False(text.Contains("!", StringComparison.Ordinal) && text.Contains("HDBF.", StringComparison.Ordinal),
                    property.Name + " must not leak a raw localization key: " + text);
            }
        }

        public static void TestEmptySnapshotProducesTheHiddenModel()
        {
            var empty = HdbfIndoorPanelFactory.Create(null, KeyEcho);
            TestAssert.False(empty.HasData);
            TestAssert.Equal(string.Empty, empty.Title);
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
                // Specific refill/action reasons returned by the v2 gameplay commands.
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

        // ------------------------------------------------------------------ refresh / no-v1 rules

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

        // ------------------------------------------------------------------ reusable number rules

        public static void TestPanelNumbersKeepTheChapter145Formats()
        {
            TestAssert.Equal("73", PanelNumbers.Percent(0.734f, roundUp: false));
            TestAssert.Equal("74", PanelNumbers.Percent(0.734f, roundUp: true));
            TestAssert.Equal("—", PanelNumbers.Percent(float.NaN, roundUp: false));
            TestAssert.Equal("3.4", PanelNumbers.Days(3.46f, roundUp: false));
            TestAssert.Equal("3.5", PanelNumbers.Days(3.41f, roundUp: true));
            TestAssert.Equal("—", PanelNumbers.Days(float.PositiveInfinity, roundUp: false));
            TestAssert.Equal("—", PanelNumbers.DaysRounded(float.PositiveInfinity));
            TestAssert.Equal("1.35", PanelNumbers.Multiplier(1.35f));
            TestAssert.Equal("600", PanelNumbers.PlotCount(600));
            TestAssert.Equal("—", PanelNumbers.PlotCountOrUnknown(-1));
            TestAssert.Equal(0, PanelNumbers.RoundedCount(float.NaN));
        }

        // ------------------------------------------------------------------ fixtures

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

        // ------------------------------------------------------------------ helpers

        private sealed class FakeQueryService
        {
            public IndoorFarmSnapshot Snapshot { get; init; } = new IndoorFarmSnapshot(
                HdbfTier.Basic, HdbfFaction.Folktails, 0, 2, IndoorMode.Normal, false, 60, 0, 0,
                60, 0, 0, 0, 0f, 3.6f, float.PositiveInfinity, 0f, 0.5f, WaterSupplyKind.None,
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

        /// <summary>Loads one shipped CSV from the embedded test resource (ID,Text,Comment).</summary>
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
            var rows = ParseCsv(reader.ReadToEnd());
            var texts = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 1; i < rows.Count; i++)
            {
                var row = rows[i];
                if (row.Length == 0 || (row.Length == 1 && row[0].Trim().Length == 0))
                {
                    continue;
                }

                if (row.Length != 3)
                {
                    throw new TestFailureException(resource + " line " + (i + 1) + " has " + row.Length + " fields");
                }

                texts[row[0].Trim()] = row[1];
            }

            return texts;
        }

        private static List<string[]> ParseCsv(string text)
        {
            var rows = new List<string[]>();
            var fields = new List<string>();
            var field = new StringBuilder();
            var inQuotes = false;
            for (var i = 0; i < text.Length; i++)
            {
                var character = text[i];
                if (inQuotes)
                {
                    if (character == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"')
                        {
                            field.Append('"');
                            i++;
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        field.Append(character);
                    }
                }
                else if (character == '"')
                {
                    inQuotes = true;
                }
                else if (character == ',')
                {
                    fields.Add(field.ToString());
                    field.Clear();
                }
                else if (character == '\r')
                {
                    // Normalised away.
                }
                else if (character == '\n')
                {
                    fields.Add(field.ToString());
                    field.Clear();
                    rows.Add(fields.ToArray());
                    fields.Clear();
                }
                else
                {
                    field.Append(character);
                }
            }

            if (field.Length > 0 || fields.Count > 0)
            {
                fields.Add(field.ToString());
                rows.Add(fields.ToArray());
            }

            return rows;
        }
    }
}
