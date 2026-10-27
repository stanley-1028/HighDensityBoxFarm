// ============================================================================
// HDBF v2 | Layer: L1 test | Owner: V2-UI-02
// Purpose: Visual contract of the reworked indoor panel (the "textbook defects" round): the
//          duplicate header/empty box/tan background are gone, every text we render uses the one
//          warm-white colour, the card stack spans the full panel width without exposing the tan
//          entity-panel background, water/maintenance/mode/output rows and the vanilla ProgressBar
//          and RadioToggle controls are present, the ports are separate wrapping rows, the vacant
//          worker chip has no warning triangle, and the new keys ship in all three languages.
// Requirements: REQ-V2-UI-01/02/03/04, REQ-V2-READ-04, REQ-V2-WATER-03, REQ-V2-TEST-01
// ============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using HDBF.Core;
using HDBF.Core.Indoor;
using HDBF.UI.Localization;
using HDBF.UI.Panels;

namespace HDBF.Tests
{
    /// <summary>
    /// Source+read-model contract of the V2-UI-02 visual fixes. The fragment itself needs
    /// UIElements and cannot load here (REQ-V2-TEST-01), so the test reads the shipped source text
    /// and drives the real game-free read model; the vanilla classes and templates stay pinned by
    /// <see cref="V2PanelContractTests"/>.
    /// </summary>
    public static class V2PanelVisualContractTests
    {
        private const string RepoRootKey = "HdbfRepoRoot";
        private const string UiRoot = "src/HDBF.UI";
        private const uint ExpectedLightTextRgba = 0xF2EBDDFFu;
        private const uint ExpectedWarningAccentRgba = 0xC4562BFFu;

        private static readonly string[] Languages = { "enUS", "zhCN", "zhTW" };

        // ------------------------------------------------------------------ A + B: no second header

        public static void TestTheDuplicateHeaderAndModeBadgeAreDeleted()
        {
            var fragment = ReadUiSources("Panels/HdbfFarmPanelFragment.cs")
                           + ReadUiSources("Panels/HdbfFarmPanelFragment.Build.cs")
                           + ReadUiSources("Panels/HdbfFarmPanelFragment.Render.cs");
            foreach (var removed in new[]
                     {
                         "HeaderCard", "entity-panel__header", "entity-panel__avatar", "_headerIcon",
                         "_avatarSprite", "EntityBadgeService", "ResolveAvatar", "ModeBadge",
                         "CreateBadge", "HeaderTooltip", "_title =", "RenderHeader",
                     })
            {
                TestAssert.False(fragment.Contains(removed, StringComparison.Ordinal),
                    "the duplicate header/empty badge must be deleted, found: " + removed);
            }

            // The read model no longer carries the name/title/badge of the vanilla header.
            var properties = typeof(HdbfIndoorPanelData).GetProperties()
                .Select(property => property.Name).ToArray();
            foreach (var removed in new[] { "Title", "ModeBadge", "HeaderTooltip" })
            {
                TestAssert.False(properties.Contains(removed, StringComparer.Ordinal),
                    "the read model must not repeat the vanilla header: " + removed);
            }

            // There is no big empty framed placeholder left: the only badge vocabulary is gone.
            var style = ReadUiSources("Panels/HdbfIndoorPanelStyle.cs");
            foreach (var removed in new[] { "bg-box--green", "HeaderBadge", "entity-panel__avatar" })
            {
                TestAssert.False(style.Contains(removed, StringComparison.Ordinal),
                    "the empty framed badge vocabulary must be deleted: " + removed);
            }
        }

        public static void TestCompactSubtitleRowReplacesTheHeaderAndNeverRepeatsTheName()
        {
            var data = CreateData(IntermediateFixture(), "enUS");
            TestAssert.Equal("Intermediate · IronTeeth", data.MetaLine);
            TestAssert.False(data.MetaLine.Contains("Box Farm", StringComparison.Ordinal),
                "the compact row must not repeat the building name: " + data.MetaLine);

            var render = ReadUiSources("Panels/HdbfFarmPanelFragment.Render.cs");
            TestAssert.True(render.Contains("CreateMetaRow(data.MetaLine", StringComparison.Ordinal),
                "the compact tier/faction/mode row must be rendered");
            TestAssert.True(render.Contains("HdbfPanelSectionKind.Workers", StringComparison.Ordinal),
                "the compact row belongs to the first (workers) card, directly below the vanilla header");
        }

        // ------------------------------------------------------------------ C: no tan background

        public static void TestFragmentRootIsTransparentAndOnlyCardsOwnTheDarkBackground()
        {
            var build = ReadUiSources("Panels/HdbfFarmPanelFragment.Build.cs");
            TestAssert.True(build.Contains("root.style.marginTop = 0f", StringComparison.Ordinal),
                "no top margin may expose the tan entity-panel background");
            TestAssert.False(build.Contains("backgroundColor", StringComparison.Ordinal)
                             || build.Contains("backgroundImage", StringComparison.Ordinal),
                "the fragment root must stay transparent (requirement C)");
            TestAssert.False(build.Contains("bg-sub-box--pale-purple", StringComparison.Ordinal),
                "the tan/pale sub-box class must never be used");

            var style = ReadUiSources("Panels/HdbfIndoorPanelStyle.cs");
            TestAssert.False(style.Contains("backgroundColor", StringComparison.Ordinal)
                             || style.Contains("backgroundImage", StringComparison.Ordinal),
                "the style vocabulary must not define a background of its own");

            // The only raw background colour in the whole panel is the 3 px warning accent strip.
            var allUi = ReadUiSources(string.Empty);
            TestAssert.Equal(1, CountOccurrences(allUi, "backgroundColor"),
                "only the warning accent strip may set a raw background colour");

            // Cards own the dark background: entity-sub-panel + a shipped sub-box tone.
            var controls = ReadUiSources("Panels/HdbfPanelControls.cs");
            TestAssert.True(controls.Contains("card.AddToClassList(HdbfIndoorPanelStyle.Card)",
                StringComparison.Ordinal), "cards must be entity-sub-panel NineSliceVisualElements");
            TestAssert.True(controls.Contains("card.AddToClassList(HdbfIndoorPanelStyle.CardTone(tone))",
                StringComparison.Ordinal), "cards must carry the shipped sub-box tone class");
        }

        public static void TestCardsFillThePanelWithoutGapsOrScrollbarGutter()
        {
            var controls = ReadUiSources("Panels/HdbfPanelControls.cs");
            var createCard = Slice(controls, "public static NineSliceVisualElement CreateCard",
                "public static VisualElement CreateTitleRow");
            TestAssert.False(createCard.Contains("marginBottom", StringComparison.Ordinal),
                "cards must not add vertical margins; only the vanilla padding applies");

            var build = ReadUiSources("Panels/HdbfFarmPanelFragment.Build.cs");
            TestAssert.Equal(2, CountOccurrences(build, "ScrollerVisibility.Hidden"),
                "both scrollers must be hidden so the transparent gutter cannot show the tan panel");
            TestAssert.True(build.Contains("_scroll.style.marginTop = 0f", StringComparison.Ordinal),
                "the scroller must not open a gap below the vanilla fragments");
        }

        // ------------------------------------------------------------------ D: one light colour

        public static void TestEveryTextUsesTheSingleWarmWhiteColour()
        {
            TestAssert.True(ExpectedLightTextRgba == HdbfIndoorPanelStyle.LightTextRgba,
                "the panel text colour must be #F2EBDD");
            var style = ReadUiSources("Panels/HdbfIndoorPanelStyle.cs");
            TestAssert.True(style.Contains("0xF2EBDDFF", StringComparison.Ordinal),
                "the warm-white colour must live in exactly one constant");

            var controls = ReadUiSources("Panels/HdbfPanelControls.cs")
                           + ReadUiSources("Panels/HdbfPanelControls.Widgets.cs");
            TestAssert.True(controls.Contains("element.style.color = new StyleColor(LightText)",
                StringComparison.Ordinal),
                "the one colour must be applied through ApplyLightText");
            TestAssert.True(CountOccurrences(ReadUiSources(string.Empty), "ApplyLightText(") >= 5,
                "every text builder must paint the light colour");

            // No dark text hard-coded anywhere in the panel sources.
            var allUi = ReadUiSources(string.Empty);
            foreach (var dark in new[]
                     {
                         "Color.black", "rgb(0, 0, 0)", "rgb(30, 30, 30)", "0xFF000000", "game-text--black",
                     })
            {
                TestAssert.False(allUi.Contains(dark, StringComparison.Ordinal),
                    "the panel must not hard-code dark text: " + dark);
            }

            // Labels are only built through the light-colour helper.
            var render = ReadUiSources("Panels/HdbfFarmPanelFragment.Render.cs");
            var build = ReadUiSources("Panels/HdbfFarmPanelFragment.Build.cs");
            TestAssert.False(render.Contains("new Label(", StringComparison.Ordinal)
                             || build.Contains("new Label(", StringComparison.Ordinal),
                "the fragment must create text through HdbfPanelControls.CreateLabel/CreateNoteLabel");
        }

        // ------------------------------------------------------------------ E + F: rows and controls

        public static void TestPortStatesAreSeparateRowsAndNeverOverlap()
        {
            var output = Section(CreateData(IntermediateFixture(), "enUS"), HdbfPanelSectionKind.Output);
            var labels = output.Rows.Select(row => row.Label).ToArray();
            foreach (var expected in new[]
                     {
                         "Output stock", "Output port", "Pipe port", "Logistics port", "Power port",
                     })
            {
                TestAssert.True(labels.Contains(expected), "missing separate row: " + expected);
            }

            TestAssert.False(labels.Contains("Ports"), "the combined ports row must be gone");
            foreach (var row in output.Rows)
            {
                TestAssert.False(row.Value.Contains("·", StringComparison.Ordinal),
                    "a port row must only carry its own state: " + row.Label + " = " + row.Value);
            }

            var builder = ReadUiSources("Panels/HdbfIndoorPanelFactory.Cards.Output.cs");
            TestAssert.False(builder.Contains("FormatPorts", StringComparison.Ordinal),
                "the combined port formatter must be deleted");
            TestAssert.Equal(4, CountOccurrences(builder, "PortRow("),
                "three separate port rows are built (plus the PortRow helper definition)");
        }

        public static void TestWaterMaintenanceModeAndOutputRowsAreAllPresent()
        {
            var data = CreateData(IntermediateFixture(), "enUS");
            var water = Section(data, HdbfPanelSectionKind.Water);
            TestAssert.True(RowExists(water, "Tank"), "water tank row");
            TestAssert.True(RowExists(water, "Coverage C"), "coverage C row");
            TestAssert.True(RowExists(water, "Minimum C_min"), "C_min row");
            TestAssert.True(RowExists(water, "Water source"), "supply source row");
            TestAssert.True(RowExists(water, "Tank") && Row(water, "Tank").HasBar,
                "tank row owns a ProgressBar");
            TestAssert.True(Row(water, "Coverage C").HasBar, "coverage row owns a ProgressBar");
            TestAssert.False(float.IsNaN(Row(water, "Coverage C").BarMarker),
                "the coverage bar keeps the C_min marker");

            var maintenance = Section(data, HdbfPanelSectionKind.Maintenance);
            TestAssert.True(RowExists(maintenance, "Next due day"), "maintenance due row");
            TestAssert.True(RowExists(maintenance, "Overdue cycles"), "overdue cycles row");
            TestAssert.True(RowExists(maintenance, "Upkeep material"), "material requirement row");
            TestAssert.Equal("Plank ×8 / 10 days", Row(maintenance, "Upkeep material").Value);

            var output = Section(data, HdbfPanelSectionKind.Output);
            TestAssert.True(Row(output, "Output stock").HasBar,
                "requirement F: output occupancy is the vanilla ProgressBar");
            TestAssert.Equal("12/50 stored", Row(output, "Output stock").BarText);

            TestAssert.Equal(IndoorMode.Dense, data.Mode);
            TestAssert.True(data.ModeTransitionText.Length > 0, "1-day mode transition hint");
        }

        public static void TestModeGroupUsesTheVanillaRadioToggle()
        {
            var build = ReadUiSources("Panels/HdbfFarmPanelFragment.Build.cs");
            TestAssert.True(build.Contains("_radioToggleFactory.CreateLocalizable", StringComparison.Ordinal),
                "mode switch must be the vanilla RadioToggleFactory group");
            TestAssert.True(build.Contains("HdbfUiLocKeys.ModeNormal", StringComparison.Ordinal)
                             && build.Contains("HdbfUiLocKeys.ModeDense", StringComparison.Ordinal)
                             && build.Contains("HdbfUiLocKeys.ModeFallow", StringComparison.Ordinal),
                "all three modes must be offered");
            TestAssert.True(build.Contains("RadioButtonSelected += OnModeSelected", StringComparison.Ordinal),
                "the vanilla radio selection must be wired");
            var render = ReadUiSources("Panels/HdbfFarmPanelFragment.Render.cs");
            TestAssert.True(render.Contains("_modeToggle?.Update((int)data.Mode)", StringComparison.Ordinal),
                "the selected radio must follow the authoritative snapshot");
        }

        public static void TestRefillAndMaintenanceButtonsExistWithInlineReasons()
        {
            var build = ReadUiSources("Panels/HdbfFarmPanelFragment.Build.cs");
            TestAssert.True(build.Contains("HdbfUiLocKeys.ActionRefill", StringComparison.Ordinal),
                "the refill button must exist");
            TestAssert.True(build.Contains("HdbfUiLocKeys.ActionMaintenance", StringComparison.Ordinal),
                "the maintenance button must exist");
            TestAssert.True(build.Contains("HdbfPanelControls.CreateButton", StringComparison.Ordinal),
                "actions must be the shipped vanilla button template");
            var render = ReadUiSources("Panels/HdbfFarmPanelFragment.Render.cs");
            TestAssert.True(render.Contains("data.CanRefill", StringComparison.Ordinal)
                             && render.Contains("data.CanPerformMaintenance", StringComparison.Ordinal),
                "both action buttons must be enabled from the read model");
            TestAssert.True(render.Contains("data.RefillReasonText", StringComparison.Ordinal)
                             && render.Contains("data.MaintenanceReasonText", StringComparison.Ordinal),
                "both inline rejection reasons must be rendered");
        }

        // ------------------------------------------------------------------ G: vacant chips

        public static void TestVacantWorkerChipUsesVanillaVacantStyleWithoutWarningTriangle()
        {
            var widgets = ReadUiSources("Panels/HdbfPanelControls.Widgets.cs");
            TestAssert.True(widgets.Contains("icons.Get(chip.Vacant ? \"vacant\" : \"worker\")",
                StringComparison.Ordinal), "a vacant chip shows the vanilla empty-beaver avatar");
            TestAssert.True(widgets.Contains("vacantIcon.style.display = DisplayStyle.None",
                StringComparison.Ordinal), "the vacant warning triangle must not be shown");
            TestAssert.False(widgets.Contains("chip.Vacant ? DisplayStyle.Flex", StringComparison.Ordinal),
                "a free worker slot is normal state, not a warning (requirement G)");
        }

        // ------------------------------------------------------------------ H: wrapping

        public static void TestRowsAndValuesWrapInsteadOfBeingTruncated()
        {
            var controls = ReadUiSources("Panels/HdbfPanelControls.cs");
            TestAssert.True(CountOccurrences(controls, "whiteSpace = WhiteSpace.Normal") >= 2,
                "row labels, values and bar text must wrap");
            TestAssert.True(controls.Contains("valueLabel.style.flexShrink = 1f", StringComparison.Ordinal)
                             && controls.Contains("valueLabel.style.minWidth = 0f", StringComparison.Ordinal),
                "long values must shrink/wrap instead of being clipped");
            TestAssert.False(controls.Contains("text-overflow", StringComparison.Ordinal)
                             || controls.Contains("overflow = Overflow.Hidden", StringComparison.Ordinal),
                "the panel must not clip text");
        }

        // ------------------------------------------------------------------ I: colour is not the only carrier

        public static void TestWarningRowsKeepIconMarkerAndAccentStrip()
        {
            TestAssert.True(ExpectedWarningAccentRgba == HdbfIndoorPanelStyle.WarningAccentRgba,
                "the warning accent strip colour must stay the single #C4562B constant");
            var controls = ReadUiSources("Panels/HdbfPanelControls.cs");
            TestAssert.True(controls.Contains("strip.style.backgroundColor", StringComparison.Ordinal),
                "the warning row carries a colour accent strip");
            TestAssert.True(controls.Contains("row.Marker + \" \" + row.Label", StringComparison.Ordinal),
                "the warning row carries the text marker");
            TestAssert.True(controls.Contains("icons.Get(row.IconKey)", StringComparison.Ordinal),
                "the warning row carries an icon");

            var flagged = IntermediateFixture() with
            {
                Warnings = IndoorWarningFlags.NoPower | IndoorWarningFlags.OutputFull,
                Power = PowerConnection.Grid,
                PowerConnected = false,
            };
            var warnings = CreateData(flagged, "enUS").Sections
                .SelectMany(section => section.Rows).Where(row => row.IsWarning).ToArray();
            TestAssert.True(warnings.Length >= 2, "the fixture must produce warning rows");
            foreach (var row in warnings)
            {
                TestAssert.True(row.IconKey.Length > 0 && row.Marker.Length > 0 && row.Label.Length > 0,
                    "warning row must keep icon + text marker + text");
            }
        }

        // ------------------------------------------------------------------ J: three languages

        public static void TestNewMaintenanceKeysShipInAllThreeLanguages()
        {
            var en = LoadTexts("enUS");
            foreach (var key in new[]
                     {
                         HdbfUiLocKeys.PanelMaintenanceMaterialLabel,
                         HdbfUiLocKeys.PanelMaintenanceMaterialValue,
                     })
            {
                foreach (var language in Languages)
                {
                    TestAssert.True(LoadTexts(language).ContainsKey(key),
                        language + " is missing the new key " + key);
                }

                TestAssert.True(en[key].Trim().Length > 0, "empty text for " + key);
            }

            var value = en[HdbfUiLocKeys.PanelMaintenanceMaterialValue];
            TestAssert.True(value.Contains("{0}") && value.Contains("{1}") && value.Contains("{2}"),
                "the upkeep-material value must format material, count and cycle days: " + value);
        }

        public static void TestLocalizedMetaLineUsesTheThreeLanguageTerms()
        {
            TestAssert.Equal("Intermediate · IronTeeth",
                CreateData(IntermediateFixture(), "enUS").MetaLine);
            TestAssert.Equal("中级 · 铁牙", CreateData(IntermediateFixture(), "zhCN").MetaLine);
            TestAssert.Equal("中級 · 鐵牙", CreateData(IntermediateFixture(), "zhTW").MetaLine);
        }

        public static void TestNoRenderedTextLeaksBracesRawKeysOrTheBuildingNameTwice()
        {
            foreach (var language in Languages)
            {
                var data = CreateData(IntermediateFixture(), language);
                foreach (var value in AllStrings(data))
                {
                    TestAssert.False(value.Contains("{", StringComparison.Ordinal)
                                     || value.Contains("}", StringComparison.Ordinal),
                        language + ": unformatted template rendered: " + value);
                    TestAssert.False(value.Contains("HDBF.", StringComparison.Ordinal),
                        language + ": raw localization key rendered: " + value);
                }
            }
        }

        // ------------------------------------------------------------------ K: refresh budget

        public static void TestScrollHeightIsMeasuredInsideTheFourHertzGateWithoutAllocation()
        {
            var fragment = ReadUiSources("Panels/HdbfFarmPanelFragment.cs");
            TestAssert.True(fragment.Contains("RefreshIntervalSeconds = 0.25f", StringComparison.Ordinal),
                "the refresh gate stays 4 Hz");
            TestAssert.True(fragment.Contains("UpdateScrollHeight();", StringComparison.Ordinal),
                "the scroller height is re-measured at the gate");
            TestAssert.False(fragment.Contains("void Update(", StringComparison.Ordinal)
                             || fragment.Contains("Time.deltaTime", StringComparison.Ordinal),
                "the fragment must not run per frame");

            var render = ReadUiSources("Panels/HdbfFarmPanelFragment.Render.cs");
            TestAssert.True(render.Contains("_lastScrollHeight", StringComparison.Ordinal),
                "the scroller height is only written when it changed");
            var scrollMethod = Slice(render, "private void UpdateScrollHeight()", "private void RegisterTooltip");
            TestAssert.False(scrollMethod.Contains("new List", StringComparison.Ordinal)
                             || scrollMethod.Contains("new Label", StringComparison.Ordinal),
                "the scroll measurement must not allocate");
        }

        // ------------------------------------------------------------------ fixtures / helpers

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

        private static HdbfIndoorPanelData CreateData(IndoorFarmSnapshot snapshot, string language)
        {
            var texts = LoadTexts(language);
            return HdbfIndoorPanelFactory.Create(snapshot,
                key => texts.TryGetValue(key, out var text) ? text : "!" + key);
        }

        private static HdbfPanelSection Section(HdbfIndoorPanelData data, HdbfPanelSectionKind kind)
        {
            return data.Sections.Single(section => section.Kind == kind);
        }

        private static bool RowExists(HdbfPanelSection section, string label)
        {
            return section.Rows.Any(row => string.Equals(row.Label, label, StringComparison.Ordinal));
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
                data.WorkerRosterNote, data.CropPolicyLabel, data.CropPolicyText, data.CropTooltip,
                data.RefillTooltip, data.RefillReasonText, data.MaintenanceTooltip,
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

        private static string Slice(string text, string from, string to)
        {
            var start = text.IndexOf(from, StringComparison.Ordinal);
            TestAssert.True(start >= 0, "missing source anchor: " + from);
            var end = text.IndexOf(to, start, StringComparison.Ordinal);
            TestAssert.True(end > start, "missing source anchor: " + to);
            return text.Substring(start, end - start);
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

        private static string ReadUiSources(string relativePath)
        {
            var root = Path.Combine(RepoRoot(), UiRoot.Replace('/', Path.DirectorySeparatorChar));
            if (string.IsNullOrEmpty(relativePath))
            {
                var builder = new StringBuilder();
                foreach (var file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
                             .OrderBy(path => path, StringComparer.Ordinal))
                {
                    builder.Append(File.ReadAllText(file, Encoding.UTF8)).Append('\n');
                }

                return builder.ToString();
            }

            return File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)),
                Encoding.UTF8);
        }

        private static Dictionary<string, string> LoadTexts(string language)
        {
            var resource = "HDBF.Tests.Localizations." + language + ".csv";
            var assembly = typeof(V2PanelVisualContractTests).Assembly;
            using var stream = assembly.GetManifestResourceStream(resource);
            if (stream is null)
            {
                throw new TestFailureException("missing embedded localization resource " + resource);
            }

            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var texts = new Dictionary<string, string>(StringComparer.Ordinal);
            var lines = reader.ReadToEnd().Replace("\r\n", "\n").Split('\n');
            for (var index = 1; index < lines.Length; index++)
            {
                var line = lines[index];
                if (line.Trim().Length == 0)
                {
                    continue;
                }

                var comma = line.IndexOf(',');
                if (comma <= 0)
                {
                    continue;
                }

                texts[line.Substring(0, comma).Trim()] = line.Substring(comma + 1).Split(',')[0];
            }

            return texts;
        }

        private static string RepoRoot()
        {
            var metadata = typeof(V2PanelVisualContractTests).Assembly
                .GetCustomAttributes(typeof(AssemblyMetadataAttribute), inherit: false)
                .OfType<AssemblyMetadataAttribute>();
            foreach (var attribute in metadata)
            {
                if (string.Equals(attribute.Key, RepoRootKey, StringComparison.Ordinal)
                    && !string.IsNullOrWhiteSpace(attribute.Value))
                {
                    return attribute.Value!;
                }
            }

            throw new TestFailureException("missing HdbfRepoRoot assembly metadata");
        }
    }
}
