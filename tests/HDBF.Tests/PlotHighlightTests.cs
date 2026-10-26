using System;
using System.Collections.Generic;
using HDBF.Core.Abstractions;
using HDBF.Core.Plots;

namespace HDBF.Tests
{
    /// <summary>
    /// L1 tests for the game-free managed-plot highlight rules (REQ-UI-05, chapter 14.3/14.6):
    /// the four-state classifier, the world-space box selection, the last-plot exclusion policy, the
    /// per-frame paint budget, the colour/legend contract and the dirty-diff that avoids rebuilding
    /// all 600 quads on every update.
    ///
    /// The renderer and the gameplay probes are the only untested layer (they need a real game
    /// session, 待實機); everything they decide with a rule lives in HDBF.Core and is pinned here.
    /// </summary>
    public static class PlotHighlightTests
    {
        // ------------------------------------------------------------------ classifier

        /// <summary>Each of the four states is produced from its own flag combination (chapter 14.3).</summary>
        public static void TestClassifierProducesAllFourStates()
        {
            AssertClassified(HdbfPlotHighlightState.Irrigated,
                PlotHighlightClassifier.TryClassify(true, false, true, false, out var irrigated), irrigated);
            AssertClassified(HdbfPlotHighlightState.Dry,
                PlotHighlightClassifier.TryClassify(true, false, false, false, out var dry), dry);
            AssertClassified(HdbfPlotHighlightState.Excluded,
                PlotHighlightClassifier.TryClassify(false, true, false, false, out var excluded), excluded);
            AssertClassified(HdbfPlotHighlightState.Unreachable,
                PlotHighlightClassifier.TryClassify(false, false, false, true, out var unreachable),
                unreachable);
        }

        /// <summary>Excluded wins over unreachable, registration and moisture: gray is always visible.</summary>
        public static void TestClassifierExcludedWinsOverEveryOtherFlag()
        {
            var classified = PlotHighlightClassifier.TryClassify(true, true, true, true, out var state);
            AssertClassified(HdbfPlotHighlightState.Excluded, classified, state);
        }

        /// <summary>Unreachable wins over the registered green/yellow colour (chapter 11.3 diagnostic).</summary>
        public static void TestClassifierUnreachableWinsOverRegisteredColour()
        {
            var classified = PlotHighlightClassifier.TryClassify(true, false, true, true, out var state);
            AssertClassified(HdbfPlotHighlightState.Unreachable, classified, state);
        }

        /// <summary>A cell that is neither registered, excluded nor unreachable has nothing to paint.</summary>
        public static void TestClassifierNothingToPaintReturnsFalse()
        {
            var classified = PlotHighlightClassifier.TryClassify(false, false, false, false, out var state);
            TestAssert.False(classified, "an unregistered, unexcluded, reachable cell has nothing to paint");
            TestAssert.Equal(default(HdbfPlotHighlightState), state, "false must leave the state at default");
        }

        /// <summary>Soil moisture alone does not paint: only a registered plot becomes green.</summary>
        public static void TestClassifierDoesNotPaintUnregisteredMoisture()
        {
            var classified = PlotHighlightClassifier.TryClassify(false, false, true, false, out var state);
            TestAssert.False(classified, "moisture on a cell this farm does not manage is not painted");
            TestAssert.Equal(default(HdbfPlotHighlightState), state);
        }

        /// <summary>Excluded and unreachable cells are painted even when they are not registered.</summary>
        public static void TestClassifierPaintsExcludedAndUnreachableWithoutRegistration()
        {
            TestAssert.True(PlotHighlightClassifier.TryClassify(false, true, false, false, out var excluded),
                "an excluded plot is painted gray even when it is no longer registered");
            TestAssert.Equal(HdbfPlotHighlightState.Excluded, excluded);

            TestAssert.True(PlotHighlightClassifier.TryClassify(false, false, false, true, out var unreachable),
                "the optional unreachable diagnostic is painted red");
            TestAssert.Equal(HdbfPlotHighlightState.Unreachable, unreachable);
        }

        // ------------------------------------------------------------------ box selection

        /// <summary>The box includes both edge columns/rows (inclusive rectangle, chapter 14.3).</summary>
        public static void TestBoxSelectionIncludesBothEdges()
        {
            var cells = new[]
            {
                Cell(0, 0, 3, HdbfPlotHighlightState.Irrigated),
                Cell(1, 0, 3, HdbfPlotHighlightState.Irrigated),
                Cell(2, 0, 3, HdbfPlotHighlightState.Dry),
                Cell(0, 1, 3, HdbfPlotHighlightState.Dry),
                Cell(1, 1, 3, HdbfPlotHighlightState.Irrigated),
                Cell(2, 1, 3, HdbfPlotHighlightState.Irrigated),
                Cell(0, 2, 3, HdbfPlotHighlightState.Irrigated),
                Cell(1, 2, 3, HdbfPlotHighlightState.Dry),
                Cell(2, 2, 3, HdbfPlotHighlightState.Dry),
                Cell(3, 3, 3, HdbfPlotHighlightState.Irrigated), // outside the box
            };

            var selected = PlotBoxSelection.RegisteredCellsInBox(cells, 0, 0, 2, 2, 3);

            TestAssert.Equal(9, selected.Count, "a fully covered 3x3 box must include both edges");
            TestAssert.False(Contains(selected, PlotCellCodec.Encode(3, 3, 3)),
                "a cell outside the inclusive rectangle must not be selected");
        }

        /// <summary>Dragging the rectangle in any direction selects the same cells.</summary>
        public static void TestBoxSelectionIsIndependentOfDragDirection()
        {
            var cells = new[]
            {
                Cell(0, 0, 3, HdbfPlotHighlightState.Irrigated),
                Cell(2, 2, 3, HdbfPlotHighlightState.Dry),
                Cell(-1, 2, 3, HdbfPlotHighlightState.Irrigated),
                Cell(4, 4, 3, HdbfPlotHighlightState.Irrigated),
            };

            var forward = PlotBoxSelection.RegisteredCellsInBox(cells, -1, 0, 2, 2, 3);
            var reversed = PlotBoxSelection.RegisteredCellsInBox(cells, 2, 2, -1, 0, 3);
            var mixed = PlotBoxSelection.RegisteredCellsInBox(cells, -1, 2, 2, 0, 3);

            AssertIntSequence(forward, reversed, "opposite drag direction");
            AssertIntSequence(forward, mixed, "mixed corner order");
            TestAssert.Equal(3, forward.Count, "all three in-range cells are selected");
        }

        /// <summary>Only cells on the selected height are considered.</summary>
        public static void TestBoxSelectionFiltersByHeight()
        {
            var cells = new[]
            {
                Cell(0, 0, 2, HdbfPlotHighlightState.Irrigated),
                Cell(0, 0, 3, HdbfPlotHighlightState.Irrigated),
                Cell(1, 1, 3, HdbfPlotHighlightState.Dry),
                Cell(1, 1, 4, HdbfPlotHighlightState.Dry),
            };

            var atZ3 = PlotBoxSelection.RegisteredCellsInBox(cells, 0, 0, 5, 5, 3);
            var atZ4 = PlotBoxSelection.RegisteredCellsInBox(cells, 0, 0, 5, 5, 4);
            var atZ9 = PlotBoxSelection.RegisteredCellsInBox(cells, 0, 0, 5, 5, 9);

            AssertIntSequence(
                new[] { PlotCellCodec.Encode(0, 0, 3), PlotCellCodec.Encode(1, 1, 3) }, atZ3, "z=3 cells");
            AssertIntSequence(new[] { PlotCellCodec.Encode(1, 1, 4) }, atZ4, "z=4 cells");
            TestAssert.Equal(0, atZ9.Count, "a height without cells selects nothing");
        }

        /// <summary>Excluded cells are not re-excluded and unreachable cells are not registered plots.</summary>
        public static void TestBoxSelectionSkipsExcludedAndUnreachableStates()
        {
            var cells = new[]
            {
                Cell(1, 1, 3, HdbfPlotHighlightState.Irrigated),
                Cell(2, 1, 3, HdbfPlotHighlightState.Dry),
                Cell(3, 1, 3, HdbfPlotHighlightState.Excluded),
                Cell(4, 1, 3, HdbfPlotHighlightState.Unreachable),
            };

            var selected = PlotBoxSelection.RegisteredCellsInBox(cells, 0, 0, 10, 10, 3);

            AssertIntSequence(
                new[] { PlotCellCodec.Encode(1, 1, 3), PlotCellCodec.Encode(2, 1, 3) },
                selected, "only registered (green/yellow) cells enter an exclusion batch");
        }

        /// <summary>The result is de-duplicated and sorted ascending for a deterministic batch update.</summary>
        public static void TestBoxSelectionReturnsDistinctSortedIds()
        {
            var cells = new[]
            {
                CellWithId(900, 0, 0, 3, HdbfPlotHighlightState.Irrigated),
                CellWithId(100, 1, 0, 3, HdbfPlotHighlightState.Dry),
                CellWithId(500, 2, 0, 3, HdbfPlotHighlightState.Irrigated),
                CellWithId(100, 0, 1, 3, HdbfPlotHighlightState.Irrigated), // duplicate id, same plot
            };

            var selected = PlotBoxSelection.RegisteredCellsInBox(cells, 0, 0, 2, 1, 3);

            AssertIntSequence(new[] { 100, 500, 900 }, selected, "distinct ids sorted ascending");
        }

        /// <summary>Negative horizontal coordinates (map cells can be negative) are handled.</summary>
        public static void TestBoxSelectionHandlesNegativeCoordinates()
        {
            var cells = new[]
            {
                Cell(-3, -2, 3, HdbfPlotHighlightState.Irrigated),
                Cell(-2, -2, 3, HdbfPlotHighlightState.Dry),
                Cell(-1, -2, 3, HdbfPlotHighlightState.Irrigated),
                Cell(0, 0, 3, HdbfPlotHighlightState.Irrigated),
            };

            var selected = PlotBoxSelection.RegisteredCellsInBox(cells, -2, -2, -3, -2, 3);

            TestAssert.Equal(2, selected.Count, "the inclusive box (-3..-2, y=-2) selects two cells");
            TestAssert.False(Contains(selected, PlotCellCodec.Encode(0, 0, 3)),
                "the cell outside the box is not selected");
            TestAssert.True(Contains(selected, PlotCellCodec.Encode(-3, -2, 3)),
                "the negative-x edge cell is selected");
            TestAssert.True(Contains(selected, PlotCellCodec.Encode(-2, -2, 3)),
                "the second negative-x edge cell is selected");
        }

        /// <summary>A null or empty cell list is a safe empty selection, never null.</summary>
        public static void TestBoxSelectionNullAndEmptyAreSafe()
        {
            TestAssert.NotNull(PlotBoxSelection.RegisteredCellsInBox(null, 0, 0, 5, 5, 3));
            TestAssert.Equal(0, PlotBoxSelection.RegisteredCellsInBox(null, 0, 0, 5, 5, 3).Count);
            TestAssert.Equal(0,
                PlotBoxSelection.RegisteredCellsInBox(Array.Empty<PlotHighlightCell>(), 0, 0, 5, 5, 3).Count);
        }

        /// <summary>A box bigger than the cell set selects every registered cell in it.</summary>
        public static void TestBoxSelectionLargerThanCellsSelectsAllRegistered()
        {
            var cells = new[]
            {
                Cell(10, 10, 3, HdbfPlotHighlightState.Irrigated),
                Cell(20, 20, 3, HdbfPlotHighlightState.Dry),
                Cell(30, 30, 3, HdbfPlotHighlightState.Excluded),
            };

            var selected = PlotBoxSelection.RegisteredCellsInBox(cells, -1000, -1000, 1000, 1000, 3);

            AssertIntSequence(
                new[] { PlotCellCodec.Encode(10, 10, 3), PlotCellCodec.Encode(20, 20, 3) },
                selected, "every registered cell inside the oversized box");
        }

        // ------------------------------------------------------------------ exclusion policy

        /// <summary>A subset request excludes exactly the requested registered plots, sorted.</summary>
        public static void TestExclusionSubsetPlan()
        {
            var plan = PlotExclusionPolicy.Plan(
                new[] { 50, 10, 30, 20, 40 }, new[] { 30, 10 });

            TestAssert.False(plan.RefusedByMinimum, "a subset request is not refused");
            AssertIntSequence(new[] { 10, 30 }, plan.ExcludedPlotIds, "excluded ids sorted ascending");
            TestAssert.Equal(3, plan.RemainingPlots, "five registered minus two excluded");
            TestAssert.Equal(2, plan.RequestedCount, "two registered plots were requested");
        }

        /// <summary>Excluding the last registered plot is refused as a whole (REQ-UI-05).</summary>
        public static void TestExclusionLastPlotRefusal()
        {
            var plan = PlotExclusionPolicy.Plan(new[] { 7 }, new[] { 7 });

            TestAssert.True(plan.RefusedByMinimum, "the farm must always keep at least one plot");
            TestAssert.Equal(0, plan.ExcludedPlotIds.Count, "a refused request excludes nothing");
            TestAssert.Equal(1, plan.RemainingPlots, "the refusal reports the unchanged registered count");
            TestAssert.Equal(1, plan.RequestedCount, "the refused request still reports what was asked");
        }

        /// <summary>Leaving exactly one registered plot is allowed; requesting all of them is refused.</summary>
        public static void TestExclusionKeepsOnePlotButRefusesAll()
        {
            var leavingOne = PlotExclusionPolicy.Plan(new[] { 1, 2, 3 }, new[] { 1, 2 });
            TestAssert.False(leavingOne.RefusedByMinimum, "leaving one registered plot is allowed");
            TestAssert.Equal(1, leavingOne.RemainingPlots);
            AssertIntSequence(new[] { 1, 2 }, leavingOne.ExcludedPlotIds, "the two requested plots");

            var excludingAll = PlotExclusionPolicy.Plan(new[] { 1, 2, 3 }, new[] { 3, 1, 2 });
            TestAssert.True(excludingAll.RefusedByMinimum, "excluding all registered plots is refused");
            TestAssert.Equal(0, excludingAll.ExcludedPlotIds.Count);
            TestAssert.Equal(3, excludingAll.RemainingPlots);
            TestAssert.Equal(3, excludingAll.RequestedCount);
        }

        /// <summary>Duplicate and unknown requested ids are filtered out (stale box selection is safe).</summary>
        public static void TestExclusionFiltersDuplicatesAndUnknownIds()
        {
            var plan = PlotExclusionPolicy.Plan(
                new[] { 10, 20, 30, 40 }, new[] { 20, 99, 20, 40, 40, -5 });

            AssertIntSequence(new[] { 20, 40 }, plan.ExcludedPlotIds, "duplicates collapse, unknown ids ignored");
            TestAssert.Equal(2, plan.RemainingPlots);
            TestAssert.Equal(2, plan.RequestedCount, "only distinct registered requests count");
            TestAssert.False(plan.RefusedByMinimum);
        }

        /// <summary>An empty or wholly unknown request is a no-op plan, not a refusal.</summary>
        public static void TestExclusionEmptyOrUnknownRequestIsNoOp()
        {
            var registered = new[] { 1, 2, 3 };

            var empty = PlotExclusionPolicy.Plan(registered, Array.Empty<int>());
            TestAssert.False(empty.RefusedByMinimum, "an empty request is never a refusal");
            TestAssert.Equal(0, empty.ExcludedPlotIds.Count);
            TestAssert.Equal(3, empty.RemainingPlots, "an empty request changes nothing");
            TestAssert.Equal(0, empty.RequestedCount);

            var unknown = PlotExclusionPolicy.Plan(registered, new[] { 99, 98 });
            TestAssert.False(unknown.RefusedByMinimum, "an unknown request is never a refusal");
            TestAssert.Equal(0, unknown.ExcludedPlotIds.Count);
            TestAssert.Equal(3, unknown.RemainingPlots);
            TestAssert.Equal(0, unknown.RequestedCount, "unknown ids are not effective requests");
        }

        /// <summary>Null inputs are treated as empty lists and never throw.</summary>
        public static void TestExclusionNullInputsAreSafe()
        {
            var nothing = PlotExclusionPolicy.Plan(null, null);
            TestAssert.NotNull(nothing.ExcludedPlotIds);
            TestAssert.False(nothing.RefusedByMinimum);
            TestAssert.Equal(0, nothing.RemainingPlots);
            TestAssert.Equal(0, nothing.RequestedCount);

            var noRequest = PlotExclusionPolicy.Plan(new[] { 1, 2 }, null);
            TestAssert.False(noRequest.RefusedByMinimum);
            TestAssert.Equal(2, noRequest.RemainingPlots);

            var noRegistration = PlotExclusionPolicy.Plan(null, new[] { 1 });
            TestAssert.False(noRegistration.RefusedByMinimum, "nothing registered cannot be a minimum violation");
            TestAssert.Equal(0, noRegistration.RemainingPlots);
            TestAssert.Equal(0, noRegistration.ExcludedPlotIds.Count);
        }

        /// <summary>A refusal still reports the effective (known, de-duplicated) requested count.</summary>
        public static void TestExclusionRefusalCountsEffectiveRequest()
        {
            var plan = PlotExclusionPolicy.Plan(new[] { 1, 2 }, new[] { 1, 2, 77, 2 });

            TestAssert.True(plan.RefusedByMinimum);
            TestAssert.Equal(2, plan.RequestedCount, "unknown/duplicate ids do not inflate the count");
            TestAssert.Equal(0, plan.ExcludedPlotIds.Count, "a refusal excludes nothing (whole-request rollback)");
        }

        // ------------------------------------------------------------------ frame budget

        /// <summary>The 600-cell top tier paints in 3 frames at 200 cells/frame (chapter 14.6).</summary>
        public static void TestFrameBudgetPaints600CellsInThreeFrames()
        {
            TestAssert.Equal(200, HighlightFrameBudget.CellsPerFrame);
            TestAssert.Equal(3, HighlightFrameBudget.MaxFrames);
            TestAssert.Equal(3, HighlightFrameBudget.FramesNeeded(600), "600 / 200 = 3");
            TestAssert.True(HighlightFrameBudget.FramesNeeded(600) <= HighlightFrameBudget.MaxFrames,
                "the top tier must fit the documented 3-frame budget");
            TestAssert.Equal(2, HighlightFrameBudget.FramesNeeded(400));
            TestAssert.Equal(1, HighlightFrameBudget.FramesNeeded(200));
        }

        /// <summary>Frame counts use ceiling division so no cell is ever dropped.</summary>
        public static void TestFrameBudgetUsesCeilingDivision()
        {
            TestAssert.Equal(1, HighlightFrameBudget.FramesNeeded(1));
            TestAssert.Equal(1, HighlightFrameBudget.FramesNeeded(199));
            TestAssert.Equal(2, HighlightFrameBudget.FramesNeeded(201));
            TestAssert.Equal(4, HighlightFrameBudget.FramesNeeded(601), "601 cells need a fourth frame");
        }

        /// <summary>Nothing to paint costs no frame; negative counts and per-frame budgets are defensive.</summary>
        public static void TestFrameBudgetHandlesZeroAndNonPositiveBudgets()
        {
            TestAssert.Equal(0, HighlightFrameBudget.FramesNeeded(0));
            TestAssert.Equal(0, HighlightFrameBudget.FramesNeeded(-5));
            TestAssert.Equal(3, HighlightFrameBudget.FramesNeeded(3, 0), "perFrame<=0 is treated as 1");
            TestAssert.Equal(600, HighlightFrameBudget.FramesNeeded(600, -1), "perFrame<=0 is treated as 1");
        }

        /// <summary>A frame paints at most the budget and never more cells than remain.</summary>
        public static void TestFrameBudgetBatchSize()
        {
            TestAssert.Equal(200, HighlightFrameBudget.BatchSize(600));
            TestAssert.Equal(200, HighlightFrameBudget.BatchSize(201));
            TestAssert.Equal(200, HighlightFrameBudget.BatchSize(200));
            TestAssert.Equal(199, HighlightFrameBudget.BatchSize(199));
            TestAssert.Equal(0, HighlightFrameBudget.BatchSize(0));
            TestAssert.Equal(0, HighlightFrameBudget.BatchSize(-3));
            TestAssert.Equal(1, HighlightFrameBudget.BatchSize(5, 0), "perFrame<=0 makes progress of one cell");
        }

        /// <summary>Draining 600 cells with BatchSize takes exactly 3 frames and each batch is legal.</summary>
        public static void TestFrameBudgetBatchesDrain600CellsInThreeFrames()
        {
            var remaining = 600;
            var frames = 0;
            while (remaining > 0)
            {
                var batch = HighlightFrameBudget.BatchSize(remaining);
                TestAssert.True(batch > 0, "a frame with pending cells must paint at least one");
                TestAssert.True(batch <= HighlightFrameBudget.CellsPerFrame,
                    "a frame must never paint more than the budget");
                remaining -= batch;
                frames++;
            }

            TestAssert.Equal(3, frames);
            TestAssert.Equal(0, remaining);
        }

        // ------------------------------------------------------------------ palette + cell codec

        /// <summary>The palette has four distinct RGBA values, all semi-transparent (chapter 14.3).</summary>
        public static void TestPaletteHasFourDistinctSemiTransparentValues()
        {
            var values = new[]
            {
                HdbfPlotHighlightPalette.IrrigatedRgba,
                HdbfPlotHighlightPalette.DryRgba,
                HdbfPlotHighlightPalette.ExcludedRgba,
                HdbfPlotHighlightPalette.UnreachableRgba,
            };

            var distinct = new HashSet<uint>(values);
            TestAssert.Equal(4, distinct.Count, "the four states must have four distinct colours");
            for (var i = 0; i < values.Length; i++)
            {
                var alpha = values[i] & 0xFFu;
                TestAssert.True(alpha < 0xFFu, "state " + i + " must stay semi-transparent (alpha<0xFF)");
            }

            TestAssert.Equal((long)0x4CAF50A6u, (long)HdbfPlotHighlightPalette.IrrigatedRgba);
            TestAssert.Equal((long)0xFFC107A6u, (long)HdbfPlotHighlightPalette.DryRgba);
            TestAssert.Equal((long)0x9E9E9EA6u, (long)HdbfPlotHighlightPalette.ExcludedRgba);
            TestAssert.Equal((long)0xF44336A6u, (long)HdbfPlotHighlightPalette.UnreachableRgba);
        }

        /// <summary>The legend lists the four states in the order the requirement text lists them.</summary>
        public static void TestPaletteLegendOrderIsRequirementOrder()
        {
            var legend = HdbfPlotHighlightPalette.LegendOrder;

            TestAssert.Equal(4, legend.Count);
            TestAssert.Equal(HdbfPlotHighlightState.Irrigated, legend[0]);
            TestAssert.Equal(HdbfPlotHighlightState.Dry, legend[1]);
            TestAssert.Equal(HdbfPlotHighlightState.Excluded, legend[2]);
            TestAssert.Equal(HdbfPlotHighlightState.Unreachable, legend[3]);
        }

        /// <summary>Rgba(state) maps every state to its own documented colour.</summary>
        public static void TestPaletteRgbaMatchesEachStateColour()
        {
            TestAssert.Equal((long)HdbfPlotHighlightPalette.IrrigatedRgba,
                (long)HdbfPlotHighlightPalette.Rgba(HdbfPlotHighlightState.Irrigated));
            TestAssert.Equal((long)HdbfPlotHighlightPalette.DryRgba,
                (long)HdbfPlotHighlightPalette.Rgba(HdbfPlotHighlightState.Dry));
            TestAssert.Equal((long)HdbfPlotHighlightPalette.ExcludedRgba,
                (long)HdbfPlotHighlightPalette.Rgba(HdbfPlotHighlightState.Excluded));
            TestAssert.Equal((long)HdbfPlotHighlightPalette.UnreachableRgba,
                (long)HdbfPlotHighlightPalette.Rgba(HdbfPlotHighlightState.Unreachable));
        }

        /// <summary>FromPlotId decodes the same plant cell the codec encoded, including negative x/y.</summary>
        public static void TestHighlightCellFromPlotIdRoundTripsCodec()
        {
            var coordinates = new[]
            {
                (0, 0, 0),
                (5, 5, 3),
                (-7, -3, 2),
                (-1024, 1023, 1023),
                (1023, -1024, 0),
            };

            foreach (var (x, y, z) in coordinates)
            {
                var plotId = PlotCellCodec.Encode(x, y, z);
                var cell = PlotHighlightCell.FromPlotId(plotId, HdbfPlotHighlightState.Dry);

                TestAssert.Equal(plotId, cell.PlotId, "plot id round-trip");
                TestAssert.Equal(x, cell.X, "x round-trip for (" + x + "," + y + "," + z + ")");
                TestAssert.Equal(y, cell.Y, "y round-trip for (" + x + "," + y + "," + z + ")");
                TestAssert.Equal(z, cell.Z, "z round-trip for (" + x + "," + y + "," + z + ")");
                TestAssert.Equal(HdbfPlotHighlightState.Dry, cell.State);
            }
        }

        /// <summary>WithState keeps the decoded identity and only changes the painted state.</summary>
        public static void TestHighlightCellWithStateKeepsCoordinates()
        {
            var plotId = PlotCellCodec.Encode(-3, 4, 2);
            var cell = PlotHighlightCell.FromPlotId(plotId, HdbfPlotHighlightState.Irrigated)
                .WithState(HdbfPlotHighlightState.Excluded);

            TestAssert.Equal(plotId, cell.PlotId);
            TestAssert.Equal(-3, cell.X);
            TestAssert.Equal(4, cell.Y);
            TestAssert.Equal(2, cell.Z);
            TestAssert.Equal(HdbfPlotHighlightState.Excluded, cell.State);
        }

        // ------------------------------------------------------------------ dirty diff

        /// <summary>A first paint adds every current cell, in snapshot order.</summary>
        public static void TestDeltaFirstPaintAddsEveryCell()
        {
            var previous = new Dictionary<int, HdbfPlotHighlightState>();
            var current = new[]
            {
                Cell(0, 0, 3, HdbfPlotHighlightState.Irrigated),
                Cell(1, 0, 3, HdbfPlotHighlightState.Dry),
            };

            var delta = PlotHighlightDiff.Diff(previous, current);

            TestAssert.False(delta.IsEmpty, "a first paint is not empty");
            TestAssert.Equal(2, delta.AddedOrChanged.Count);
            AssertCell(current[0], delta.AddedOrChanged[0], "first cell order");
            AssertCell(current[1], delta.AddedOrChanged[1], "second cell order");
            TestAssert.Equal(0, delta.Removed.Count, "a first paint removes nothing");
        }

        /// <summary>An unchanged snapshot yields an empty delta so the renderer skips the frame.</summary>
        public static void TestDeltaIsEmptyWhenNothingChanged()
        {
            var cells = new[]
            {
                Cell(0, 0, 3, HdbfPlotHighlightState.Irrigated),
                Cell(1, 0, 3, HdbfPlotHighlightState.Dry),
            };
            var previous = new Dictionary<int, HdbfPlotHighlightState>
            {
                [cells[0].PlotId] = cells[0].State,
                [cells[1].PlotId] = cells[1].State,
            };

            var delta = PlotHighlightDiff.Diff(previous, cells);

            TestAssert.True(delta.IsEmpty, "an identical snapshot changes nothing");
            TestAssert.NotNull(delta.AddedOrChanged);
            TestAssert.NotNull(delta.Removed);
            TestAssert.Equal(0, delta.AddedOrChanged.Count);
            TestAssert.Equal(0, delta.Removed.Count);
        }

        /// <summary>A state change repaints only that cell; it is not reported as removed.</summary>
        public static void TestDeltaReportsChangedStateAsAddedOrChanged()
        {
            var plotId = PlotCellCodec.Encode(2, 2, 3);
            var previous = new Dictionary<int, HdbfPlotHighlightState>
            {
                [plotId] = HdbfPlotHighlightState.Irrigated,
            };
            var current = new[] { new PlotHighlightCell(plotId, 2, 2, 3, HdbfPlotHighlightState.Dry) };

            var delta = PlotHighlightDiff.Diff(previous, current);

            TestAssert.Equal(1, delta.AddedOrChanged.Count, "a changed state must be repainted");
            AssertCell(current[0], delta.AddedOrChanged[0], "changed cell carries the new state");
            TestAssert.Equal(0, delta.Removed.Count, "a changed plot is not removed");
        }

        /// <summary>Plots absent from the current snapshot are removed, sorted ascending.</summary>
        public static void TestDeltaReportsRemovedIdsSortedAscending()
        {
            var previous = new Dictionary<int, HdbfPlotHighlightState>
            {
                [900] = HdbfPlotHighlightState.Irrigated,
                [100] = HdbfPlotHighlightState.Dry,
                [500] = HdbfPlotHighlightState.Excluded,
            };

            var delta = PlotHighlightDiff.Diff(previous, Array.Empty<PlotHighlightCell>());

            AssertIntSequence(new[] { 100, 500, 900 }, delta.Removed, "removed ids sorted ascending");
            TestAssert.Equal(0, delta.AddedOrChanged.Count);
            TestAssert.False(delta.IsEmpty);
        }

        /// <summary>Null snapshots are safe and never produce null lists.</summary>
        public static void TestDeltaHandlesNullInputs()
        {
            var none = PlotHighlightDiff.Diff(null, null);
            TestAssert.True(none.IsEmpty, "two empty snapshots have no delta");
            TestAssert.NotNull(none.AddedOrChanged);
            TestAssert.NotNull(none.Removed);

            var currentOnly = PlotHighlightDiff.Diff(null, new[] { Cell(0, 0, 3, HdbfPlotHighlightState.Dry) });
            TestAssert.Equal(1, currentOnly.AddedOrChanged.Count, "a null previous is a first paint");
            TestAssert.Equal(0, currentOnly.Removed.Count);

            var previousOnly = PlotHighlightDiff.Diff(
                new Dictionary<int, HdbfPlotHighlightState> { [42] = HdbfPlotHighlightState.Dry }, null);
            AssertIntSequence(new[] { 42 }, previousOnly.Removed, "a null current removes every previous plot");
            TestAssert.Equal(0, previousOnly.AddedOrChanged.Count);
        }

        /// <summary>A mixed update reports added/changed in current order and removed separately.</summary>
        public static void TestDeltaMixedUpdateKeepsCurrentOrderAndSeparatesRemovals()
        {
            var unchanged = Cell(0, 0, 3, HdbfPlotHighlightState.Irrigated);
            var beforeChange = Cell(1, 0, 3, HdbfPlotHighlightState.Irrigated);
            var afterChange = beforeChange.WithState(HdbfPlotHighlightState.Dry);
            var added = Cell(2, 0, 3, HdbfPlotHighlightState.Dry);
            var removed = Cell(3, 0, 3, HdbfPlotHighlightState.Irrigated);

            var previous = new Dictionary<int, HdbfPlotHighlightState>
            {
                [unchanged.PlotId] = unchanged.State,
                [beforeChange.PlotId] = beforeChange.State,
                [removed.PlotId] = removed.State,
            };
            var current = new[] { unchanged, afterChange, added };

            var delta = PlotHighlightDiff.Diff(previous, current);

            TestAssert.Equal(2, delta.AddedOrChanged.Count);
            AssertCell(afterChange, delta.AddedOrChanged[0], "changed cell first (current order)");
            AssertCell(added, delta.AddedOrChanged[1], "added cell second (current order)");
            AssertIntSequence(new[] { removed.PlotId }, delta.Removed, "only the vanished plot is removed");
        }

        // ------------------------------------------------------------------ helpers

        private static PlotHighlightCell Cell(int x, int y, int z, HdbfPlotHighlightState state)
        {
            return new PlotHighlightCell(PlotCellCodec.Encode(x, y, z), x, y, z, state);
        }

        private static PlotHighlightCell CellWithId(int plotId, int x, int y, int z,
            HdbfPlotHighlightState state)
        {
            return new PlotHighlightCell(plotId, x, y, z, state);
        }

        private static bool Contains(IReadOnlyList<int> ids, int plotId)
        {
            for (var i = 0; i < ids.Count; i++)
            {
                if (ids[i] == plotId)
                {
                    return true;
                }
            }

            return false;
        }

        private static void AssertClassified(HdbfPlotHighlightState expected, bool classified,
            HdbfPlotHighlightState actual)
        {
            TestAssert.True(classified, "expected the cell to be painted as " + expected);
            TestAssert.Equal(expected, actual);
        }

        private static void AssertIntSequence(IReadOnlyList<int> expected, IReadOnlyList<int> actual,
            string what)
        {
            TestAssert.NotNull(actual, what + ": result must never be null");
            TestAssert.Equal(expected.Count, actual.Count, what + ": count");
            for (var i = 0; i < expected.Count; i++)
            {
                TestAssert.Equal(expected[i], actual[i], what + ": element " + i);
            }
        }

        private static void AssertCell(PlotHighlightCell expected, PlotHighlightCell actual, string what)
        {
            TestAssert.Equal(expected.PlotId, actual.PlotId, what + ": plot id");
            TestAssert.Equal(expected.X, actual.X, what + ": x");
            TestAssert.Equal(expected.Y, actual.Y, what + ": y");
            TestAssert.Equal(expected.Z, actual.Z, what + ": z");
            TestAssert.Equal(expected.State, actual.State, what + ": state");
        }
    }
}
