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

            var cards = ReadUiSources("Panels/HdbfFarmPanelFragment.Cards.cs");
            TestAssert.True(cards.Contains("CreateMetaRow(data.MetaLine", StringComparison.Ordinal),
                "the compact tier/faction/mode row must be rendered");
            TestAssert.True(cards.Contains("HdbfPanelSectionKind.Workers", StringComparison.Ordinal),
                "the compact row belongs to the first (workers) card, directly below the vanilla header");
        }

        /// <summary>
        /// Regression contract of the 2026-11-06 player report ("the drop down to change from
        /// rotation to a specific crop doesn't open for me"): the 4 Hz refresh must not clear and
        /// rebuild the whole card stack, because that detaches the vanilla crop Dropdown and its
        /// DetachFromPanel callback hides the open popup. Cards are keyed by a content signature and
        /// only the changed ones are replaced in place; the crop card (the one that owns the
        /// dropdown) therefore survives a value-only refresh attached to the panel.
        /// </summary>
        public static void TestRefreshRebuildsOnlyChangedCardsAndKeepsTheDropdownAttached()
        {
            var cards = ReadUiSources("Panels/HdbfFarmPanelFragment.Cards.cs");
            var renderCards = Slice(cards, "private void RenderCards", "private bool CanUpdateCardsInPlace");
            TestAssert.True(renderCards.Contains("UpdateChangedCards", StringComparison.Ordinal),
                "the refresh path must update changed cards only");
            TestAssert.Equal(1, CountOccurrences(renderCards, "cards.Clear()"),
                "the only stack clear left is the empty-layout fallback, not the 4 Hz value refresh");
            TestAssert.True(renderCards.Contains("_cropHost.panel is not null", StringComparison.Ordinal),
                "the crop dropdown is re-synced only while it is mounted in the crop card");

            var update = Slice(cards, "private void UpdateChangedCards", "private void RebuildCards");
            TestAssert.True(update.Contains("HdbfIndoorPanelSignature.ForSection", StringComparison.Ordinal),
                "the per-card update must decide from the rendered-content signature");
            TestAssert.True(update.Contains("if (signature == _cardStates[index].Signature)", StringComparison.Ordinal),
                "a card with an unchanged signature must stay attached");
            TestAssert.True(update.Contains("cards.RemoveAt(index)", StringComparison.Ordinal)
                             && update.Contains("cards.Insert(index, card)", StringComparison.Ordinal),
                "a changed card is replaced at its own index, never by a full rebuild");

            var lifecycle = ReadUiSources("Panels/HdbfFarmPanelFragment.cs");
            TestAssert.True(lifecycle.Contains("IsCropDropdownOpen()", StringComparison.Ordinal),
                "the periodic refresh must stop while a dropdown popup is open");
            TestAssert.True(lifecycle.Contains("_dropdownListDrawer.DropdownVisible", StringComparison.Ordinal),
                "the guard must ask the vanilla drawer whether a popup is visible");

            var signature = ReadUiSources("Panels/HdbfIndoorPanelSignature.cs");
            foreach (var rendered in new[]
                     {
                         "row.Label", "row.Value", "row.Tooltip", "row.IsWarning", "Quantize(row.Bar)",
                         "row.BarText", "Quantize(row.BarMarker)", "row.Marker", "section.Tone",
                         "data.ModeTransitionText", "chip.Vacant",
                     })
            {
                TestAssert.True(signature.Contains(rendered, StringComparison.Ordinal),
                    "the card signature must cover every field BuildSectionCard renders: " + rendered);
            }
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

            // Vanilla-isation: the panel never sets a raw background colour; every surface is a
            // shipped nine-slice/sub-box class (sprites are painted through backgroundImage only).
            var allUi = ReadUiSources(string.Empty);
            TestAssert.Equal(0, CountOccurrences(allUi, "backgroundColor"),
                "no raw background colour may remain in the panel sources");

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
            TestAssert.Equal(1, CountOccurrences(build, "ScrollerVisibility.Hidden"),
                "only the horizontal bar stays suppressed; the vertical bar is the vanilla one");
            TestAssert.True(build.Contains("HdbfPanelControls.AddClasses(_scroll, HdbfIndoorPanelStyle.Scroll)",
                    StringComparison.Ordinal),
                "the scroller carries the vanilla game-scroll-view class");
            TestAssert.Equal("game-scroll-view", HdbfIndoorPanelStyle.Scroll,
                "the scroller class must be the shipped game-scroll-view");
            TestAssert.True(build.Contains("_scroll.style.marginTop = 0f", StringComparison.Ordinal),
                "the scroller must not open a gap below the vanilla fragments");
        }

        // ------------------------------------------------------------------ D: vanilla text colour

        public static void TestEveryTextUsesTheShippedVanillaTextColour()
        {
            // Vanilla panel text is rgb(204,204,204) from .entity-panel__text / .game-text-*; the
            // panel must not paint a colour of its own (the old invented warm white is gone).
            var style = ReadUiSources("Panels/HdbfIndoorPanelStyle.cs");
            TestAssert.False(style.Contains("F2EBDD", StringComparison.Ordinal),
                "the invented warm-white colour must be deleted from the vocabulary");
            TestAssert.False(style.Contains("LightText", StringComparison.Ordinal),
                "no self-invented text-colour constant may exist");

            var allUi = ReadUiSources(string.Empty);
            TestAssert.False(allUi.Contains("style.color =", StringComparison.Ordinal),
                "text colour must come from the vanilla USS classes, never from code");
            TestAssert.False(allUi.Contains("ApplyLightText", StringComparison.Ordinal),
                "the light-text repaint pass must be deleted");

            // No dark text hard-coded anywhere either.
            foreach (var dark in new[]
                     {
                         "Color.black", "rgb(0, 0, 0)", "rgb(30, 30, 30)", "0xFF000000", "game-text--black",
                     })
            {
                TestAssert.False(allUi.Contains(dark, StringComparison.Ordinal),
                    "the panel must not hard-code dark text: " + dark);
            }

            // Labels are only built through the class-driven helper.
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
                         "Output stock", "Output port", "Pipe network (auto-refill)", "Logistics port",
                         "Power port",
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

            // L-09: HDBF never hands its produce to a conveyor, so the logistics rows must not
            // claim the port is connected any more.
            foreach (var label in new[] { "Output port", "Logistics port" })
            {
                TestAssert.False(string.Equals(Row(output, label).Value, "Connected",
                        StringComparison.Ordinal),
                    "the logistics interface must not claim a connection: " + label);
            }

            var builder = ReadUiSources("Panels/HdbfIndoorPanelFactory.Cards.Output.cs");
            TestAssert.False(builder.Contains("FormatPorts", StringComparison.Ordinal),
                "the combined port formatter must be deleted");
            // Three port-row call/definition pairs (plain, pipe and logistics) keep every port state
            // on its own row (L-09 split the logistics row out of the plain PortRow helper).
            TestAssert.Equal(6, CountOccurrences(builder, "PortRow("),
                "the plain/power row, the pipe row and the logistics row each own a helper");
            TestAssert.True(builder.Contains("snapshot.PipeProbeState.Reason", StringComparison.Ordinal),
                "the pipe row must map the read-only probe reason");
            TestAssert.True(builder.Contains("HdbfUiLocKeys.TooltipLogisticsPort", StringComparison.Ordinal),
                "the logistics row must carry the honest logistics tooltip (L-09)");
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
            TestAssert.Equal("12/" + HdbfIndoorPanelLimits.OutputCapacity(150) + " stored",
                Row(output, "Output stock").BarText);

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

            // WP-02: the automatic-maintenance toggle is a reachable vanilla button whose text and
            // enabled state come from the read model, and its click goes through the bridge.
            TestAssert.True(build.Contains("OnAutoMaintenanceClicked", StringComparison.Ordinal),
                "the action card must build the auto-maintenance toggle");
            TestAssert.True(render.Contains("AutoMaintenanceText(data.AutoMaintenance)", StringComparison.Ordinal),
                "the toggle label must follow the read model");
            TestAssert.True(render.Contains("data.AutoMaintenanceTooltip", StringComparison.Ordinal),
                "the toggle carries its tooltip");
            var commands = ReadUiSources("Panels/HdbfFarmPanelFragment.Commands.cs");
            TestAssert.True(commands.Contains("TrySetAutoMaintenance", StringComparison.Ordinal),
                "the toggle must call the auto-maintenance command through the bridge");
            TestAssert.True(commands.Contains("Refresh(force: true)", StringComparison.Ordinal),
                "the toggle re-reads the authoritative snapshot after the command");
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

        // ------------------------------------------------------------------ I: vanilla status rows

        /// <summary>
        /// Warnings are rendered with the game's own StatusListElement template (icon slot + text
        /// label), i.e. exactly like every vanilla status list. The old invented colour accent strip
        /// and the "▲/!" text markers are gone: severity is carried by the vanilla red-striped card
        /// and the status icon, not by a hand-drawn colour bar.
        /// </summary>
        public static void TestWarningRowsUseTheVanillaStatusTemplate()
        {
            TestAssert.Equal("Game/EntityPanel/StatusListElement", HdbfIndoorPanelStyle.StatusRowTemplate);
            var style = ReadUiSources("Panels/HdbfIndoorPanelStyle.cs");
            TestAssert.False(style.Contains("C4562B", StringComparison.Ordinal),
                "the invented warning accent colour must be deleted from the vocabulary");
            TestAssert.False(style.Contains("WarningAccent", StringComparison.Ordinal),
                "no self-invented warning colour may exist");

            var controls = ReadUiSources("Panels/HdbfPanelControls.cs");
            TestAssert.True(controls.Contains(
                    "loader.LoadVisualElement(HdbfIndoorPanelStyle.StatusRowTemplate)", StringComparison.Ordinal),
                "the warning row must be the shipped StatusListElement template");
            TestAssert.True(controls.Contains("element.Q<VisualElement>(\"Icon\")", StringComparison.Ordinal),
                "the template icon slot must be painted with the row sprite");
            TestAssert.True(controls.Contains("element.Q<Label>(\"Text\")", StringComparison.Ordinal),
                "the template text label must carry the localized sentence");
            TestAssert.False(controls.Contains("strip.style.backgroundColor", StringComparison.Ordinal),
                "the invented colour accent strip must be deleted");
            TestAssert.False(controls.Contains("row.Marker + \" \" + row.Label", StringComparison.Ordinal),
                "the invented ▲/! text marker must be deleted");

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
                TestAssert.True(row.IconKey.Length > 0 && row.Label.Length > 0,
                    "warning row must keep icon + text");
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
                         HdbfUiLocKeys.PanelAutoMaintenanceLabel,
                         HdbfUiLocKeys.PanelAutoMaintenanceOn,
                         HdbfUiLocKeys.PanelAutoMaintenanceOff,
                         HdbfUiLocKeys.TooltipAutoMaintenance,
                         HdbfUiLocKeys.PanelMaintenanceCatchUpLabel,
                         HdbfUiLocKeys.PanelMaintenanceCatchUpValue,
                         HdbfUiLocKeys.ErrorMaintenanceNotDue,
                         HdbfUiLocKeys.ErrorRefillTankFull,
                         HdbfUiLocKeys.PanelWaterPaths,
                         HdbfUiLocKeys.TooltipIdleReason,
                         HdbfUiLocKeys.PortNoPipeTank,
                         HdbfUiLocKeys.PortNoPipeNetwork,
                         HdbfUiLocKeys.PortTankDry,
                         HdbfUiLocKeys.PortAdapterUnavailable,
                         HdbfUiLocKeys.PortBindingFailed,
                         // L-09: the honest logistics-module state and its tooltip.
                         HdbfUiLocKeys.PortLogisticsDetected,
                         HdbfUiLocKeys.TooltipLogisticsPort,
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
            var catchUp = en[HdbfUiLocKeys.PanelMaintenanceCatchUpValue];
            TestAssert.True(catchUp.Contains("{0}") && catchUp.Contains("{1}") && catchUp.Contains("{2}"),
                "the catch-up value must format cycles, count and material: " + catchUp);
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

        // ------------------------------------------------------------------ regressions (audit round)

        /// <summary>
        /// The exact trap that hid the progress-bar tone: a space separated class list passed to
        /// <c>AddToClassList</c> becomes ONE invalid class. No multi-class style constant may ever
        /// be passed directly - only through <c>HdbfPanelControls.AddClasses</c>.
        /// </summary>
        public static void TestNoMultiClassConstantIsPassedDirectlyToAddToClassList()
        {
            var style = ReadUiSources("Panels/HdbfIndoorPanelStyle.cs");
            var constants = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(
                         style, @"public const string (?<name>[A-Za-z_]\w*) = ""(?<value>[^""]*)"";"))
            {
                constants[match.Groups["name"].Value] = match.Groups["value"].Value;
            }

            TestAssert.True(constants.Count >= 20, "the style constant table must be parsed");
            var multiClass = constants.Where(pair => pair.Value.Contains(' ')).ToArray();
            TestAssert.True(multiClass.Length >= 3,
                "expected the multi-class vocabulary to stay documented: " + multiClass.Length);

            var allUi = ReadUiSources(string.Empty);
            var calls = FindAddToClassListArguments(allUi);
            TestAssert.True(calls.Count >= 10, "the AddToClassList scan found too few call sites: " + calls.Count);
            foreach (var argument in calls)
            {
                foreach (var pair in multiClass)
                {
                    TestAssert.False(argument.Contains("HdbfIndoorPanelStyle." + pair.Key, StringComparison.Ordinal),
                        "multi-class constant passed to AddToClassList: " + pair.Key + " = \"" + pair.Value
                        + "\" (use HdbfPanelControls.AddClasses; argument: " + argument + ")");
                }
            }

            // Regression of the reported defect: the ternary form must be caught by this scanner too.
            var synthetic = FindAddToClassListArguments(
                "bar.AddToClassList(critical ? HdbfIndoorPanelStyle.ProgressBarRed"
                + " : HdbfIndoorPanelStyle.ProgressBarGreen);");
            TestAssert.Equal(1, synthetic.Count, "synthetic ternary call must be found");
            TestAssert.True(synthetic[0].Contains("HdbfIndoorPanelStyle.ProgressBarRed", StringComparison.Ordinal),
                "the scanner must see constant references inside a ternary argument");

            // Every DIRECT AddToClassList(style constant) must be a single class name.
            foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(
                         allUi, @"AddToClassList\(HdbfIndoorPanelStyle\.(?<name>[A-Za-z_]\w*)\)"))
            {
                var name = match.Groups["name"].Value;
                TestAssert.True(constants.TryGetValue(name, out var value),
                    "unknown style constant referenced: " + name);
                TestAssert.False(value!.Contains(' '),
                    "direct AddToClassList of a multi-class constant: " + name);
            }
        }

        /// <summary>
        /// Extracts the argument text of every <c>AddToClassList(</c> call (balanced parentheses),
        /// so a multi-class constant hidden inside a ternary/expression is detected as well.
        /// </summary>
        private static List<string> FindAddToClassListArguments(string source)
        {
            var calls = new List<string>();
            const string token = "AddToClassList(";
            var index = 0;
            while ((index = source.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
            {
                var start = index + token.Length;
                var depth = 1;
                var end = start;
                while (end < source.Length && depth > 0)
                {
                    var character = source[end];
                    if (character == '(')
                    {
                        depth++;
                    }
                    else if (character == ')')
                    {
                        depth--;
                    }

                    end++;
                }

                if (depth == 0 && end > start)
                {
                    calls.Add(source.Substring(start, end - 1 - start));
                }

                index = start;
            }

            return calls;
        }

        /// <summary>The progress bars must carry the real vanilla tone class on the bar root.</summary>
        public static void TestProgressBarToneClassesAreSingleVanillaClasses()
        {
            TestAssert.Equal("progress-bar--green", HdbfIndoorPanelStyle.ProgressBarGreen);
            TestAssert.Equal("progress-bar--red", HdbfIndoorPanelStyle.ProgressBarRed);
            TestAssert.False(HdbfIndoorPanelStyle.ProgressBarGreen.Contains(' '),
                "the green tone must be one class (fill selector: .progress-bar--green .progress-bar__progress)");
            TestAssert.False(HdbfIndoorPanelStyle.ProgressBarRed.Contains(' '),
                "the red tone must be one class");

            var createBar = Slice(ReadUiSources("Panels/HdbfPanelControls.cs"),
                "public static CoreProgressBar CreateBar", "public static VisualElement CreateWarningRow");
            TestAssert.True(createBar.Contains("AddClasses(bar, HdbfIndoorPanelStyle.ProgressBar)",
                StringComparison.Ordinal), "the base progress-bar class is added");
            TestAssert.True(createBar.Contains("AddClasses(bar, critical", StringComparison.Ordinal),
                "the tone class is applied through AddClasses (one class per add)");
        }

        /// <summary>
        /// Vanilla-isation of the crop dropdown: the control keeps the shipped Dropdown text colours
        /// (no light repaint of the selection or of the open popup list). The panel only sets the
        /// items and the selected value; everything visual belongs to the vanilla control.
        /// </summary>
        public static void TestCropDropdownKeepsTheVanillaTextColours()
        {
            var crop = ReadUiSources("Panels/HdbfFarmPanelFragment.Crop.cs");
            TestAssert.True(crop.Contains("new Dropdown()", StringComparison.Ordinal),
                "the crop control must be the vanilla Dropdown");
            TestAssert.True(crop.Contains("dropdown.Initialize(_dropdownListDrawer)", StringComparison.Ordinal),
                "the vanilla dropdown needs the game popup drawer");
            TestAssert.True(crop.Contains("SetItems(_cropDropdown", StringComparison.Ordinal),
                "the items still come from the vanilla DropdownItemsSetter");
            TestAssert.True(crop.Contains("UpdateSelectedValue()", StringComparison.Ordinal),
                "a value-only refresh still re-syncs the vanilla selection");

            TestAssert.False(crop.Contains("ApplyLightText", StringComparison.Ordinal),
                "the dropdown must not repaint the vanilla text colour");
            TestAssert.False(crop.Contains("PaintDropdown", StringComparison.Ordinal),
                "the light-text repaint helpers must be deleted");
            TestAssert.False(crop.Contains("style.color", StringComparison.Ordinal),
                "no code-set text colour in the crop block");
        }

        /// <summary>
        /// Player report 2026-11-14 ("the crop selector closes all the time"): the guard that
        /// silences the 4 Hz refresh while the popup is open must not depend on
        /// <c>DropdownListDrawer.DropdownVisible</c> alone, because that property reads
        /// <c>resolvedStyle.display</c> - the value of the previous layout pass - and can still
        /// report "closed" on the frame the popup opens. The exact state therefore comes from the
        /// dropdown's synchronous <c>Showed</c> event (open) and the drawer's
        /// <c>DropdownHiddenEvent</c> (closed), and both the crop-card rebuild and the items re-set
        /// are deferred while that state says open.
        /// </summary>
        public static void TestCropPopupGuardUsesTheExactShowHideSignal()
        {
            var crop = ReadUiSources("Panels/HdbfFarmPanelFragment.Crop.cs");
            TestAssert.True(crop.Contains("dropdown.Showed += OnCropDropdownShowed", StringComparison.Ordinal),
                "the open state must come from the dropdown's synchronous Showed event");
            TestAssert.True(crop.Contains("_cropPopupOpen = true", StringComparison.Ordinal),
                "Showed must set the exact open flag");
            TestAssert.True(crop.Contains("_cropPopupOpen = false", StringComparison.Ordinal),
                "the hidden event must clear the exact open flag");
            TestAssert.True(crop.Contains("[OnEvent]", StringComparison.Ordinal)
                             && crop.Contains("OnDropdownHidden(DropdownHiddenEvent", StringComparison.Ordinal),
                "the close signal must be the drawer's DropdownHiddenEvent");

            // RenderCrop must not call SetItems (-> Dropdown.ClearItems -> HideDropdown) while open.
            var renderCrop = Slice(crop, "private void RenderCrop", "private static string CropSignature");
            var deferAt = renderCrop.IndexOf("if (IsCropDropdownOpen())", StringComparison.Ordinal);
            var setItemsAt = renderCrop.IndexOf("SetItems(", StringComparison.Ordinal);
            TestAssert.True(deferAt >= 0 && setItemsAt > deferAt,
                "SetItems must be deferred while the popup is open (it would close the popup)");

            var lifecycle = ReadUiSources("Panels/HdbfFarmPanelFragment.cs");
            TestAssert.True(lifecycle.Contains("_eventBus.Register(this)", StringComparison.Ordinal),
                "the fragment must subscribe to the drawer's hidden event");
            TestAssert.True(lifecycle.Contains("if (_cropPopupOpen)", StringComparison.Ordinal),
                "the refresh guard must consult the exact open flag before DropdownVisible");
            TestAssert.True(lifecycle.Contains("_cropPopupOpen = false", StringComparison.Ordinal),
                "selecting another entity must not inherit a stale open flag");

            var cards = ReadUiSources("Panels/HdbfFarmPanelFragment.Cards.cs");
            TestAssert.True(cards.Contains(
                    "section.Kind == HdbfPanelSectionKind.Crops && IsCropDropdownOpen()",
                    StringComparison.Ordinal),
                "a force refresh must not rebuild the crop card while the popup is open");
        }

        /// <summary>
        /// Collapsible sections are the game's own collapse idiom: the shipped .extension-clamp
        /// Button (<c>extension-clamp--hidden</c> = folded) and a display:none body. The fold state
        /// lives in the fragment, never in the read model or the card signature, because a signature
        /// change would rebuild the card and detach the vanilla crop dropdown.
        /// </summary>
        public static void TestCardsFoldWithTheVanillaClampOutsideTheModel()
        {
            TestAssert.Equal("extension-clamp", HdbfIndoorPanelStyle.Clamp);
            TestAssert.Equal("extension-clamp--hidden", HdbfIndoorPanelStyle.ClampHidden);

            var widgets = ReadUiSources("Panels/HdbfPanelControls.Widgets.cs");
            TestAssert.True(widgets.Contains("clamp.AddToClassList(HdbfIndoorPanelStyle.Clamp)",
                    StringComparison.Ordinal),
                "the fold control must be the shipped extension-clamp Button");
            TestAssert.True(widgets.Contains(
                    "clamp.EnableInClassList(HdbfIndoorPanelStyle.ClampHidden, !open)", StringComparison.Ordinal),
                "the folded state must be the shipped extension-clamp--hidden class");

            var cards = ReadUiSources("Panels/HdbfFarmPanelFragment.Cards.cs");
            TestAssert.True(cards.Contains("HdbfPanelControls.CreateClampButton", StringComparison.Ordinal),
                "every card must mount the vanilla clamp");
            TestAssert.True(cards.Contains("ApplyFold(body, clamp, IsFoldOpen(section.Kind, hasWarnings))",
                    StringComparison.Ordinal),
                "the fold state must be re-applied when a card is rebuilt");
            TestAssert.True(cards.Contains("header.RegisterCallback<ClickEvent>", StringComparison.Ordinal),
                "the vanilla title row is the second fold target");

            var fold = ReadUiSources("Panels/HdbfFarmPanelFragment.Fold.cs");
            TestAssert.True(fold.Contains("_foldOverrides", StringComparison.Ordinal),
                "the fold state must live in the fragment");
            TestAssert.True(fold.Contains(
                    "body.style.display = open ? DisplayStyle.Flex : DisplayStyle.None", StringComparison.Ordinal),
                "folding must only switch the body display, never remove the card");
            TestAssert.True(fold.Contains("if (hasWarnings)", StringComparison.Ordinal),
                "a card with warnings is always open (a hidden warning would be a lie)");

            // State separation: the fold must not enter the read model or the card signature.
            var signature = ReadUiSources("Panels/HdbfIndoorPanelSignature.cs");
            TestAssert.False(signature.Contains("Fold", StringComparison.Ordinal)
                             || signature.Contains("Collapsed", StringComparison.Ordinal),
                "the fold state must not enter the card signature (it would detach the dropdown)");
            var model = ReadUiSources("Panels/HdbfIndoorPanelData.cs");
            TestAssert.False(model.Contains("Collapsed", StringComparison.Ordinal)
                             || model.Contains("Fold", StringComparison.Ordinal),
                "the fold state must not enter the read model");

            var lifecycle = ReadUiSources("Panels/HdbfFarmPanelFragment.cs");
            TestAssert.True(CountOccurrences(lifecycle, "ResetFolds();") >= 2,
                "a newly selected entity must reset to the default fold layout");
        }

        /// <summary>A missing WorkerView template still yields a chip whose name label is filled.</summary>
        public static void TestWorkerChipFallbackCarriesTheNameHook()
        {
            var widgets = ReadUiSources("Panels/HdbfPanelControls.Widgets.cs");
            var fallback = Slice(widgets, "private static VisualElement CreateFallbackWorkerChip",
                "}\n    }\n}");
            TestAssert.True(fallback.Contains("label.name = \"Name\"", StringComparison.Ordinal),
                "the fallback chip label must be findable via Q<Label>(\"Name\")");
        }

        /// <summary>
        /// The shipped CSV contains quoted tooltips with commas; the test reader must not truncate
        /// them (the previous naive Split(',') hid the truncation because no brace survived).
        /// </summary>
        public static void TestQuotedTooltipsAreParsedCompletely()
        {
            var en = LoadTexts("enUS");
            var effective = en[HdbfUiLocKeys.TooltipEffectiveSlots];
            TestAssert.True(effective.StartsWith("Effective slots", StringComparison.Ordinal),
                "quoted tooltip truncated or mis-parsed: " + effective);
            TestAssert.True(effective.Contains("{0}", StringComparison.Ordinal)
                             && effective.Contains("{1}", StringComparison.Ordinal)
                             && effective.Contains("{2}", StringComparison.Ordinal),
                "all three placeholders of the quoted tooltip must survive parsing: " + effective);
            TestAssert.False(effective.Contains('"'), "leading/trailing quotes must be stripped: " + effective);

            var slots = Section(CreateData(IntermediateFixture(), "enUS"), HdbfPanelSectionKind.Slots);
            var row = slots.Rows.Single(candidate => candidate.Label == "Effective P_eff");
            TestAssert.True(row.Tooltip.Contains("min(150, 3 × 38)", StringComparison.Ordinal),
                "the formatted tooltip must show capacity, crew and slots per farmer: " + row.Tooltip);
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

        /// <summary>
        /// Minimal RFC-4180 reader (quoted fields may contain commas): the shipped CSV really has
        /// quoted comma-bearing tooltips, a naive Split(',') silently truncates them.
        /// </summary>
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
            var rows = ParseCsv(reader.ReadToEnd());
            var texts = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var index = 1; index < rows.Count; index++)
            {
                var row = rows[index];
                if (row.Length < 2 || row[0].Trim().Length == 0)
                {
                    continue;
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
            for (var index = 0; index < text.Length; index++)
            {
                var character = text[index];
                if (inQuotes)
                {
                    if (character == '"')
                    {
                        if (index + 1 < text.Length && text[index + 1] == '"')
                        {
                            field.Append('"');
                            index++;
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
                    // Normalized away.
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
