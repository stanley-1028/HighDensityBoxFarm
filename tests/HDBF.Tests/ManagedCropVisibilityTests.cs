using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using HDBF.Core.Config;
using HDBF.Core.Plots;

namespace HDBF.Tests
{
    /// <summary>
    /// L1 contract tests for BUILD-05 ("托管格上的農作物視覺完全隱藏"): the game-free
    /// <see cref="ManagedCropVisibilityState"/> decision/cache machine in HDBF.Core.Plots and the
    /// three new configuration keys (ui.hideManagedCrops, perf.cropVisibilityIntervalSeconds,
    /// perf.cropVisibilityCellsPerFrame).
    ///
    /// Everything here is game-free (System + HDBF.Core only, REQ-TEST-03). The game-side
    /// controller that toggles the real renderers cannot be loaded by this runner, so the card
    /// puts every decidable rule into Core and these tests pin it: per-plot hide/restore
    /// decisions keyed by crop identity, registration-cancellation restores, the switch, the
    /// sharded refresh walk and the config codec. No Timberborn or Unity assembly is touched.
    /// </summary>
    public static class ManagedCropVisibilityTests
    {
        private const string DataConfigResource = "HDBF.Tests.Release.HDBF_Config.json";

        private static readonly Guid CropA = new Guid("00000000-0000-0000-0000-0000000000a1");
        private static readonly Guid CropB = new Guid("00000000-0000-0000-0000-0000000000b2");
        private static readonly Guid CropC = new Guid("00000000-0000-0000-0000-0000000000c3");

        // ------------------------------------------------------------ A. Observe decisions

        /// <summary>Registered plot + crop + hiding on: hide once, remember the identity.</summary>
        public static void TestObserveHidesCropOnRegisteredPlot()
        {
            var state = new ManagedCropVisibilityState();
            state.SetRegisteredPlots(new[] { 1 });

            TestAssert.Equal(ManagedCropVisualAction.None, state.Observe(1, false, Guid.Empty),
                "an empty cell never hides anything");
            TestAssert.Equal(ManagedCropVisualAction.Hide, state.Observe(1, true, CropA));
            TestAssert.True(state.IsHidden(1));
            TestAssert.Equal(1, state.HiddenCount);
            TestAssert.True(state.TryGetHiddenIdentity(1, out var identity));
            TestAssert.Equal(CropA, identity);
            TestAssert.False(state.TryGetHiddenIdentity(2, out _), "no cache entry for an unobserved plot");
        }

        /// <summary>Repeating the same observation is idempotent: the cache is what keeps a sweep cheap.</summary>
        public static void TestObserveIsIdempotentForSameCropIdentity()
        {
            var state = new ManagedCropVisibilityState();
            state.SetRegisteredPlots(new[] { 3 });

            TestAssert.Equal(ManagedCropVisualAction.Hide, state.Observe(3, true, CropA));
            TestAssert.Equal(ManagedCropVisualAction.None, state.Observe(3, true, CropA));
            TestAssert.Equal(ManagedCropVisualAction.None, state.Observe(3, true, CropA));
            TestAssert.Equal(1, state.HiddenCount);
            TestAssert.True(state.TryGetHiddenIdentity(3, out var identity));
            TestAssert.Equal(CropA, identity);
        }

        /// <summary>A replanted crop has a new entity id, so the plot must be hidden again.</summary>
        public static void TestObserveHidesReplacementCropWithNewIdentity()
        {
            var state = new ManagedCropVisibilityState();
            state.SetRegisteredPlots(new[] { 4 });

            TestAssert.Equal(ManagedCropVisualAction.Hide, state.Observe(4, true, CropA));
            TestAssert.Equal(ManagedCropVisualAction.Hide, state.Observe(4, true, CropB));
            TestAssert.Equal(1, state.HiddenCount, "still one plot, not two cache entries");
            TestAssert.True(state.TryGetHiddenIdentity(4, out var identity));
            TestAssert.Equal(CropB, identity, "the cache must track the replacement entity");
            TestAssert.Equal(ManagedCropVisualAction.None, state.Observe(4, true, CropB));
        }

        /// <summary>Harvested crop: one Restore, then nothing (the entity may already be gone).</summary>
        public static void TestObserveRestoresHarvestedCropExactlyOnce()
        {
            var state = new ManagedCropVisibilityState();
            state.SetRegisteredPlots(new[] { 7 });

            TestAssert.Equal(ManagedCropVisualAction.Hide, state.Observe(7, true, CropA));
            TestAssert.Equal(ManagedCropVisualAction.Restore, state.Observe(7, false, Guid.Empty),
                "the hidden visual must be restored exactly once");
            TestAssert.False(state.IsHidden(7));
            TestAssert.Equal(0, state.HiddenCount);
            TestAssert.Equal(ManagedCropVisualAction.None, state.Observe(7, false, Guid.Empty),
                "the harvested entity is already gone; a second poll must be a safe None");
            TestAssert.Equal(ManagedCropVisualAction.None, state.Observe(7, false, CropA),
                "a stale entity id on an empty cell is still None");
        }

        /// <summary>ADR-009 isolation: a crop outside the registered set is never touched or cached.</summary>
        public static void TestObserveIgnoresUnregisteredPlotAndDoesNotCacheIt()
        {
            var state = new ManagedCropVisibilityState();
            state.SetRegisteredPlots(new[] { 1 });

            TestAssert.Equal(ManagedCropVisualAction.None, state.Observe(2, true, CropA));
            TestAssert.False(state.IsHidden(2));
            TestAssert.False(state.TryGetHiddenIdentity(2, out _), "an unmanaged crop must never be cached");
            TestAssert.Equal(0, state.HiddenCount);

            // The registered plot still works afterwards.
            TestAssert.Equal(ManagedCropVisualAction.Hide, state.Observe(1, true, CropB));
            TestAssert.Equal(1, state.HiddenCount);
        }

        /// <summary>Guid.Empty is an allowed fallback identity and stays idempotent.</summary>
        public static void TestObserveWithEmptyGuidIdentityIsIdempotent()
        {
            var state = new ManagedCropVisibilityState();
            state.SetRegisteredPlots(new[] { 5 });

            TestAssert.Equal(ManagedCropVisualAction.Hide, state.Observe(5, true, Guid.Empty));
            TestAssert.True(state.IsHidden(5));
            TestAssert.True(state.TryGetHiddenIdentity(5, out var identity));
            TestAssert.Equal(Guid.Empty, identity);
            TestAssert.Equal(ManagedCropVisualAction.None, state.Observe(5, true, Guid.Empty),
                "a fallback identity must not re-hide every sweep");
            TestAssert.Equal(1, state.HiddenCount);
        }

        // ------------------------------------------------------------ B. registration cancellation

        /// <summary>Dropping a plot from the registration returns it so the caller can restore it now.</summary>
        public static void TestRegistrationCancellationRestoresOnlyRemovedPlots()
        {
            var state = new ManagedCropVisibilityState();
            state.SetRegisteredPlots(new[] { 1, 2 });
            TestAssert.Equal(2, state.RegisteredCount);
            TestAssert.Equal(ManagedCropVisualAction.Hide, state.Observe(1, true, CropA));
            TestAssert.Equal(ManagedCropVisualAction.Hide, state.Observe(2, true, CropB));
            TestAssert.Equal(2, state.HiddenCount);

            var restored = state.SetRegisteredPlots(new[] { 2 });

            TestAssert.NotNull(restored);
            TestAssert.Equal(1, restored.Length);
            TestAssert.Equal(1, restored[0]);
            TestAssert.False(state.IsHidden(1), "the cancelled plot must be restored");
            TestAssert.True(state.IsHidden(2), "the still-registered plot stays hidden");
            TestAssert.Equal(1, state.RegisteredCount);
            TestAssert.Equal(1, state.HiddenCount);

            // Multiple removed plots come back in ascending order for a deterministic restore batch.
            var sorted = new ManagedCropVisibilityState();
            sorted.SetRegisteredPlots(new[] { 9, 3, 5 });
            sorted.Observe(9, true, CropA);
            sorted.Observe(3, true, CropB);
            sorted.Observe(5, true, CropC);
            var drained = sorted.SetRegisteredPlots(new[] { 5 });
            TestAssert.NotNull(drained);
            TestAssert.Equal(2, drained.Length);
            TestAssert.Equal(3, drained[0], "removed ids are sorted ascending");
            TestAssert.Equal(9, drained[1]);
            TestAssert.True(sorted.IsHidden(5));
        }

        /// <summary>A null registration list is an empty registration and drains cached hides.</summary>
        public static void TestNullRegistrationDrainsHiddenCropsWhenSwitchOn()
        {
            var state = new ManagedCropVisibilityState();
            state.SetRegisteredPlots(new[] { 2, 1 });
            state.Observe(1, true, CropA);
            state.Observe(2, true, CropB);

            var restored = state.SetRegisteredPlots(null);

            TestAssert.NotNull(restored);
            TestAssert.Equal(2, restored.Length);
            TestAssert.Equal(1, restored[0]);
            TestAssert.Equal(2, restored[1]);
            TestAssert.Equal(0, state.RegisteredCount);
            TestAssert.Equal(0, state.HiddenCount);
            TestAssert.False(state.IsHidden(1));
            TestAssert.False(state.IsHidden(2));
        }

        /// <summary>An empty list behaves like null: safe, and it drains only while the switch is on.</summary>
        public static void TestEmptyRegistrationIsSafeAndDrainsOnlyWhenEnabled()
        {
            var enabled = new ManagedCropVisibilityState();
            enabled.SetRegisteredPlots(new[] { 1 });
            enabled.Observe(1, true, CropA);

            var drained = enabled.SetRegisteredPlots(Array.Empty<int>());
            TestAssert.NotNull(drained);
            TestAssert.Equal(1, drained.Length);
            TestAssert.Equal(1, drained[0]);
            TestAssert.Equal(0, enabled.RegisteredCount);
            TestAssert.Equal(0, enabled.HiddenCount);

            var disabled = new ManagedCropVisibilityState();
            TestAssert.NotNull(disabled.SetHideEnabled(false));
            var nothing = disabled.SetRegisteredPlots(Array.Empty<int>());
            TestAssert.NotNull(nothing);
            TestAssert.Equal(0, nothing.Length, "a disabled state never has hidden ids to return");
            TestAssert.Equal(0, disabled.RegisteredCount);
        }

        // ------------------------------------------------------------ C. switch

        /// <summary>BUILD-05 ships with the hiding switch on.</summary>
        public static void TestHideSwitchDefaultsToEnabled()
        {
            var state = new ManagedCropVisibilityState();
            TestAssert.True(state.HideEnabled, "ui.hideManagedCrops defaults to true");

            state.SetRegisteredPlots(new[] { 1 });
            TestAssert.True(state.HideEnabled, "registration must not change the switch");
        }

        /// <summary>Turning the switch off returns the hidden ids and stops new hides.</summary>
        public static void TestDisablingSwitchReturnsHiddenIdsAndStopsHiding()
        {
            var state = new ManagedCropVisibilityState();
            state.SetRegisteredPlots(new[] { 1, 2 });
            state.Observe(2, true, CropB);
            state.Observe(1, true, CropA);

            var restored = state.SetHideEnabled(false);

            TestAssert.NotNull(restored);
            TestAssert.Equal(2, restored.Length);
            TestAssert.Equal(1, restored[0], "restore batches are sorted");
            TestAssert.Equal(2, restored[1]);
            TestAssert.False(state.HideEnabled);
            TestAssert.Equal(0, state.HiddenCount);
            TestAssert.False(state.IsHidden(1));
            TestAssert.False(state.IsHidden(2));

            TestAssert.Equal(ManagedCropVisualAction.None, state.Observe(1, true, CropA),
                "a disabled state never hides, even on a registered plot");
            TestAssert.Equal(ManagedCropVisualAction.None, state.Observe(2, true, CropB));
            TestAssert.Equal(0, state.HiddenCount);
        }

        /// <summary>A state created with hiding off never returns Hide.</summary>
        public static void TestDisabledFromStartNeverHides()
        {
            var state = new ManagedCropVisibilityState();
            var none = state.SetHideEnabled(false);
            TestAssert.NotNull(none);
            TestAssert.Equal(0, none.Length);

            state.SetRegisteredPlots(new[] { 1, 2, 3 });
            TestAssert.Equal(ManagedCropVisualAction.None, state.Observe(1, true, CropA));
            TestAssert.Equal(ManagedCropVisualAction.None, state.Observe(2, true, CropB));
            TestAssert.Equal(ManagedCropVisualAction.None, state.Observe(3, true, CropC));
            TestAssert.Equal(0, state.HiddenCount);
        }

        /// <summary>Re-enabling the switch makes following observations hide again.</summary>
        public static void TestReEnablingSwitchHidesAgain()
        {
            var state = new ManagedCropVisibilityState();
            state.SetRegisteredPlots(new[] { 1 });
            state.SetHideEnabled(false);

            var unchanged = state.SetHideEnabled(true);
            TestAssert.NotNull(unchanged);
            TestAssert.Equal(0, unchanged.Length);
            TestAssert.True(state.HideEnabled);

            TestAssert.Equal(ManagedCropVisualAction.Hide, state.Observe(1, true, CropA));
            TestAssert.True(state.IsHidden(1));
            TestAssert.Equal(1, state.HiddenCount);
        }

        /// <summary>No-op switch writes return an empty non-null array.</summary>
        public static void TestSetHideEnabledReturnsEmptyNonNullArrayWhenNothingChanges()
        {
            var state = new ManagedCropVisibilityState();

            var sameOn = state.SetHideEnabled(true);
            TestAssert.NotNull(sameOn);
            TestAssert.Equal(0, sameOn.Length, "default value repeated");

            var turnedOff = state.SetHideEnabled(false);
            TestAssert.NotNull(turnedOff);
            TestAssert.Equal(0, turnedOff.Length, "nothing hidden yet");

            var sameOff = state.SetHideEnabled(false);
            TestAssert.NotNull(sameOff);
            TestAssert.Equal(0, sameOff.Length);

            var turnedOn = state.SetHideEnabled(true);
            TestAssert.NotNull(turnedOn);
            TestAssert.Equal(0, turnedOn.Length);
        }

        // ------------------------------------------------------------ D. RestoreAll

        /// <summary>RestoreAll drains every cache entry and returns sorted ids.</summary>
        public static void TestRestoreAllReturnsSortedIdsAndClearsTheCache()
        {
            var state = new ManagedCropVisibilityState();
            state.SetRegisteredPlots(new[] { 1, 2, 3 });
            state.Observe(3, true, CropC);
            state.Observe(1, true, CropA);
            state.Observe(2, true, CropB);
            TestAssert.Equal(3, state.HiddenCount);

            var restored = state.RestoreAll();

            TestAssert.NotNull(restored);
            TestAssert.Equal(3, restored.Length);
            TestAssert.Equal(1, restored[0]);
            TestAssert.Equal(2, restored[1]);
            TestAssert.Equal(3, restored[2]);
            TestAssert.Equal(0, state.HiddenCount);
            TestAssert.False(state.IsHidden(1));
            TestAssert.False(state.IsHidden(2));
            TestAssert.False(state.IsHidden(3));
        }

        /// <summary>A second RestoreAll is empty, and RestoreAll never returns null.</summary>
        public static void TestRestoreAllIsIdempotentAndNeverNull()
        {
            var state = new ManagedCropVisibilityState();
            TestAssert.NotNull(state.RestoreAll());

            state.SetRegisteredPlots(new[] { 1 });
            state.Observe(1, true, CropA);
            TestAssert.NotNull(state.RestoreAll());

            var second = state.RestoreAll();
            TestAssert.NotNull(second);
            TestAssert.Equal(0, second.Length);
        }

        // ------------------------------------------------------------ E. sharded refresh

        /// <summary>600 plots at 64 cells/frame = exactly 10 shards, each plot visited once.</summary>
        public static void TestRefreshShardsCoverEveryPlotExactlyOnce()
        {
            var ids = new int[600];
            for (var i = 0; i < ids.Length; i++)
            {
                ids[i] = i + 1;
            }

            var state = new ManagedCropVisibilityState();
            state.SetRegisteredPlots(ids);
            TestAssert.Equal(600, state.RegisteredCount);
            state.BeginRefreshCycle();
            TestAssert.Equal(0, state.RefreshCursor);

            var buffer = new int[64];
            var seen = new HashSet<int>();
            var shardSizes = new List<int>();
            var completedCount = 0;
            while (shardSizes.Count <= 10)
            {
                var taken = state.TakeRefreshShard(buffer, 64, out var cycleCompleted);
                if (taken == 0)
                {
                    break;
                }

                TestAssert.True(taken <= buffer.Length, "a shard must never overrun the buffer");
                shardSizes.Add(taken);
                for (var i = 0; i < taken; i++)
                {
                    TestAssert.True(seen.Add(buffer[i]), "plot id visited twice: " + buffer[i]);
                }

                if (cycleCompleted)
                {
                    completedCount++;
                    break;
                }
            }

            TestAssert.Equal(10, shardSizes.Count, "ceil(600/64) = 10 shards");
            TestAssert.Equal(1, completedCount, "completed is true only on the final shard");
            for (var i = 0; i < 9; i++)
            {
                TestAssert.Equal(64, shardSizes[i], "full shard " + i);
            }

            TestAssert.Equal(24, shardSizes[9], "the last shard carries the remainder");
            TestAssert.Equal(600, seen.Count);
            for (var i = 0; i < ids.Length; i++)
            {
                TestAssert.True(seen.Contains(ids[i]), "missing plot id " + ids[i]);
            }

            TestAssert.Equal(0, state.RefreshCursor, "the cursor wraps to the start after the final shard");
        }

        /// <summary>A buffer smaller than the budget caps the shard and never overruns.</summary>
        public static void TestRefreshShardHonoursSmallerBuffer()
        {
            var ids = new int[12];
            for (var i = 0; i < ids.Length; i++)
            {
                ids[i] = i + 1;
            }

            var state = new ManagedCropVisibilityState();
            state.SetRegisteredPlots(ids);
            state.BeginRefreshCycle();
            var buffer = new int[5];

            var first = state.TakeRefreshShard(buffer, 64, out var firstCompleted);
            TestAssert.Equal(5, first);
            TestAssert.False(firstCompleted, "12 plots are not covered by 5 cells");
            TestAssert.Equal(5, state.RefreshCursor);
            for (var i = 0; i < 5; i++)
            {
                TestAssert.Equal(i + 1, buffer[i], "first shard keeps registration order at " + i);
            }

            var second = state.TakeRefreshShard(buffer, 64, out var secondCompleted);
            TestAssert.Equal(5, second);
            TestAssert.False(secondCompleted);
            TestAssert.Equal(10, state.RefreshCursor);
            for (var i = 0; i < 5; i++)
            {
                TestAssert.Equal(i + 6, buffer[i], "second shard continues the walk at " + i);
            }
        }

        /// <summary>Non-positive budgets and empty/null buffers are safe and do not advance the walk.</summary>
        public static void TestRefreshShardRejectsInvalidInputs()
        {
            var ids = new int[10];
            for (var i = 0; i < ids.Length; i++)
            {
                ids[i] = i + 1;
            }

            var state = new ManagedCropVisibilityState();
            state.SetRegisteredPlots(ids);
            state.BeginRefreshCycle();
            var buffer = new int[4];

            var zeroBudget = state.TakeRefreshShard(buffer, 0, out var zeroCompleted);
            TestAssert.Equal(0, zeroBudget);
            TestAssert.False(zeroCompleted);
            TestAssert.Equal(0, state.RefreshCursor);

            var negativeBudget = state.TakeRefreshShard(buffer, -5, out var negativeCompleted);
            TestAssert.Equal(0, negativeBudget);
            TestAssert.False(negativeCompleted);
            TestAssert.Equal(0, state.RefreshCursor);

            var nullBuffer = state.TakeRefreshShard(null, 64, out var nullCompleted);
            TestAssert.Equal(0, nullBuffer);
            TestAssert.False(nullCompleted);
            TestAssert.Equal(0, state.RefreshCursor);

            var emptyBuffer = state.TakeRefreshShard(Array.Empty<int>(), 64, out var emptyCompleted);
            TestAssert.Equal(0, emptyBuffer);
            TestAssert.False(emptyCompleted);
            TestAssert.Equal(0, state.RefreshCursor);

            // The walk is still healthy after the rejected calls.
            var valid = state.TakeRefreshShard(buffer, 4, out var validCompleted);
            TestAssert.Equal(4, valid);
            TestAssert.False(validCompleted);
            TestAssert.Equal(4, state.RefreshCursor);
            TestAssert.Equal(1, buffer[0], "no rejected call may consume the cursor");
        }

        /// <summary>An empty registration returns 0 and never reports a completed cycle.</summary>
        public static void TestRefreshShardWithEmptyRegistrationIsSafe()
        {
            var state = new ManagedCropVisibilityState();
            TestAssert.Equal(0, state.RegisteredCount);
            state.BeginRefreshCycle();
            var buffer = new int[16];

            var taken = state.TakeRefreshShard(buffer, 8, out var completed);
            TestAssert.Equal(0, taken);
            TestAssert.False(completed, "there is no cycle to complete");
            TestAssert.Equal(0, state.RefreshCursor);

            var nullRegistered = state.SetRegisteredPlots(null);
            TestAssert.NotNull(nullRegistered);
            TestAssert.Equal(0, nullRegistered.Length);
            var takenAgain = state.TakeRefreshShard(buffer, 8, out var completedAgain);
            TestAssert.Equal(0, takenAgain);
            TestAssert.False(completedAgain);
            TestAssert.Equal(0, state.RefreshCursor);
        }

        /// <summary>BeginRefreshCycle restarts the walk at the first registered plot.</summary>
        public static void TestBeginRefreshCycleRestartsTheWalk()
        {
            var ids = new int[10];
            for (var i = 0; i < ids.Length; i++)
            {
                ids[i] = i + 1;
            }

            var state = new ManagedCropVisibilityState();
            state.SetRegisteredPlots(ids);
            var buffer = new int[4];

            state.BeginRefreshCycle();
            TestAssert.Equal(4, state.TakeRefreshShard(buffer, 4, out _));
            TestAssert.Equal(4, state.RefreshCursor);
            TestAssert.Equal(4, state.TakeRefreshShard(buffer, 4, out _));
            TestAssert.Equal(8, state.RefreshCursor);

            state.BeginRefreshCycle();
            TestAssert.Equal(0, state.RefreshCursor);
            TestAssert.Equal(4, state.TakeRefreshShard(buffer, 4, out _));
            TestAssert.Equal(4, state.RefreshCursor);
            TestAssert.Equal(1, buffer[0], "a restarted cycle starts at the first registered plot");
        }

        // ------------------------------------------------------------ F. config keys

        /// <summary>The compiled-in defaults of the three BUILD-05 config keys.</summary>
        public static void TestCropVisibilityConfigDefaultsAreShipped()
        {
            var config = HdbfConfig.Default;

            TestAssert.True(config.Ui.HideManagedCrops, "ui.hideManagedCrops defaults to true");
            TestAssert.Equal(2f, config.Perf.CropVisibilityIntervalSeconds, 1e-5f,
                "perf.cropVisibilityIntervalSeconds defaults to 2 s");
            TestAssert.Equal(64, config.Perf.CropVisibilityCellsPerFrame,
                "perf.cropVisibilityCellsPerFrame defaults to 64");
        }

        /// <summary>An explicit false parses without any warning.</summary>
        public static void TestParseHideManagedCropsFalseWithoutWarnings()
        {
            var warnings = new List<string>();
            var config = HdbfConfigCodec.Parse("{\"ui\":{\"hideManagedCrops\":false}}", warnings.Add);

            TestAssert.False(config.Ui.HideManagedCrops);
            TestAssert.Equal(0, warnings.Count, string.Join(" | ", warnings));
            TestAssert.Equal(2f, config.Perf.CropVisibilityIntervalSeconds, 1e-5f,
                "unrelated keys keep their defaults");
            TestAssert.Equal(64, config.Perf.CropVisibilityCellsPerFrame);
        }

        /// <summary>A wrong type keeps the true default and warns with the full key path.</summary>
        public static void TestParseHideManagedCropsWrongTypeWarnsAndKeepsDefault()
        {
            var warnings = new List<string>();
            var config = HdbfConfigCodec.Parse("{\"ui\":{\"hideManagedCrops\":\"no\"}}", warnings.Add);

            TestAssert.True(config.Ui.HideManagedCrops, "an invalid switch must never silently disable hiding");
            TestAssert.Equal(1, warnings.Count);
            TestAssert.True(warnings.Exists(message => message.Contains("ui.hideManagedCrops")),
                "warning must name the key: " + string.Join(" | ", warnings));
        }

        /// <summary>Both new perf values clamp to the chapter-18 window and warn when they do.</summary>
        public static void TestParseClampsCropVisibilitySettingsToConfiguredRange()
        {
            var lowWarnings = new List<string>();
            var low = HdbfConfigCodec.Parse(
                "{\"perf\":{\"cropVisibilityIntervalSeconds\":0.2,\"cropVisibilityCellsPerFrame\":1}}",
                lowWarnings.Add);
            TestAssert.Equal(1f, low.Perf.CropVisibilityIntervalSeconds, 1e-5f,
                "interval clamps up to 1 s");
            TestAssert.Equal(8, low.Perf.CropVisibilityCellsPerFrame, "cells clamp up to 8");
            TestAssert.Equal(2, lowWarnings.Count, string.Join(" | ", lowWarnings));
            TestAssert.True(lowWarnings.Exists(message => message.Contains("perf.cropVisibilityIntervalSeconds")),
                "interval clamp must name its path: " + string.Join(" | ", lowWarnings));
            TestAssert.True(lowWarnings.Exists(message => message.Contains("perf.cropVisibilityCellsPerFrame")),
                "cells clamp must name its path: " + string.Join(" | ", lowWarnings));

            var highWarnings = new List<string>();
            var high = HdbfConfigCodec.Parse(
                "{\"perf\":{\"cropVisibilityIntervalSeconds\":99,\"cropVisibilityCellsPerFrame\":9999}}",
                highWarnings.Add);
            TestAssert.Equal(10f, high.Perf.CropVisibilityIntervalSeconds, 1e-5f,
                "interval clamps down to 10 s");
            TestAssert.Equal(512, high.Perf.CropVisibilityCellsPerFrame, "cells clamp down to 512");
            TestAssert.Equal(2, highWarnings.Count, string.Join(" | ", highWarnings));
            TestAssert.True(highWarnings.Exists(message => message.Contains("perf.cropVisibilityIntervalSeconds")),
                "interval clamp must name its path: " + string.Join(" | ", highWarnings));
            TestAssert.True(highWarnings.Exists(message => message.Contains("perf.cropVisibilityCellsPerFrame")),
                "cells clamp must name its path: " + string.Join(" | ", highWarnings));
        }

        /// <summary>The shipped data/HDBF_Config.json resource carries the three new keys.</summary>
        public static void TestShippedDataConfigCarriesCropVisibilityDefaults()
        {
            var json = ReadResource(DataConfigResource);
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            var ui = root.GetProperty("ui");
            TestAssert.True(ui.GetProperty("hideManagedCrops").GetBoolean(),
                "the shipped switch must match the design default (true)");

            var perf = root.GetProperty("perf");
            TestAssert.Equal(2.0, perf.GetProperty("cropVisibilityIntervalSeconds").GetDouble(), 1e-9d,
                "shipped refresh interval");
            TestAssert.Equal(64, perf.GetProperty("cropVisibilityCellsPerFrame").GetInt32(),
                "shipped per-frame cell budget");

            // The shipped file must also parse through the real codec without diagnostics.
            var warnings = new List<string>();
            var config = HdbfConfigCodec.Parse(json, warnings.Add);
            TestAssert.Equal(0, warnings.Count, string.Join(" | ", warnings));
            TestAssert.True(config.Ui.HideManagedCrops);
            TestAssert.Equal(2f, config.Perf.CropVisibilityIntervalSeconds, 1e-5f);
            TestAssert.Equal(64, config.Perf.CropVisibilityCellsPerFrame);
        }

        /// <summary>Serialize writes the new keys; parse of that output warns not and keeps the values.</summary>
        public static void TestSerializeWritesAndRoundTripsCropVisibilityKeys()
        {
            var json = HdbfConfigCodec.Serialize(HdbfConfig.Default);

            TestAssert.True(json.Contains("\"hideManagedCrops\": true"), "json: " + json);
            TestAssert.True(json.Contains("\"cropVisibilityIntervalSeconds\""), "json: " + json);
            TestAssert.True(json.Contains("\"cropVisibilityCellsPerFrame\""), "json: " + json);

            var warnings = new List<string>();
            var parsed = HdbfConfigCodec.Parse(json, warnings.Add);
            TestAssert.Equal(0, warnings.Count, string.Join(" | ", warnings));
            TestAssert.True(parsed.Ui.HideManagedCrops);
            TestAssert.Equal(2f, parsed.Perf.CropVisibilityIntervalSeconds, 1e-5f);
            TestAssert.Equal(64, parsed.Perf.CropVisibilityCellsPerFrame);

            // Non-default values survive the same round trip.
            var custom = new HdbfConfig
            {
                Perf = new PerfSettings
                {
                    CropVisibilityIntervalSeconds = 5f,
                    CropVisibilityCellsPerFrame = 128,
                },
                Ui = new UiSettings { HideManagedCrops = false },
            };

            var customWarnings = new List<string>();
            var customParsed = HdbfConfigCodec.Parse(HdbfConfigCodec.Serialize(custom), customWarnings.Add);
            TestAssert.Equal(0, customWarnings.Count, string.Join(" | ", customWarnings));
            TestAssert.False(customParsed.Ui.HideManagedCrops);
            TestAssert.Equal(5f, customParsed.Perf.CropVisibilityIntervalSeconds, 1e-5f);
            TestAssert.Equal(128, customParsed.Perf.CropVisibilityCellsPerFrame);
        }

        /// <summary>A pre-BUILD-05 file (without the new keys) keeps the new defaults without warnings.</summary>
        public static void TestLegacyConfigWithoutCropVisibilityKeysUsesDefaults()
        {
            var warnings = new List<string>();
            var withOldUi = HdbfConfigCodec.Parse(
                "{\"configSchemaVersion\":1,\"features\":{\"densePlanting\":true},"
                + "\"perf\":{\"scanBudgetPerFrame\":128},\"ui\":{\"worldIcons\":true},\"balance\":{}}",
                warnings.Add);

            TestAssert.Equal(0, warnings.Count, string.Join(" | ", warnings));
            TestAssert.True(withOldUi.Ui.HideManagedCrops, "old files get the shipped default true");
            TestAssert.Equal(2f, withOldUi.Perf.CropVisibilityIntervalSeconds, 1e-5f);
            TestAssert.Equal(64, withOldUi.Perf.CropVisibilityCellsPerFrame);
            TestAssert.True(withOldUi.Ui.WorldIcons, "old ui keys keep working");
            TestAssert.Equal(128, withOldUi.Perf.ScanBudgetPerFrame);

            var noUiWarnings = new List<string>();
            var noUi = HdbfConfigCodec.Parse(
                "{\"configSchemaVersion\":1,\"features\":{},\"perf\":{},\"balance\":{}}", noUiWarnings.Add);
            TestAssert.Equal(0, noUiWarnings.Count, string.Join(" | ", noUiWarnings));
            TestAssert.True(noUi.Ui.HideManagedCrops, "a file without a ui section gets the default");
            TestAssert.Equal(2f, noUi.Perf.CropVisibilityIntervalSeconds, 1e-5f);
            TestAssert.Equal(64, noUi.Perf.CropVisibilityCellsPerFrame);
        }

        // ------------------------------------------------------------ G. cheap safety edges

        /// <summary>Duplicate registration ids are accepted as given; the walk keeps registration order.</summary>
        public static void TestDuplicatePlotRegistrationDoesNotThrow()
        {
            var state = new ManagedCropVisibilityState();
            var duplicateIds = new[] { 5, 5, 7, 5 };

            var drained = state.SetRegisteredPlots(duplicateIds);
            TestAssert.NotNull(drained);
            TestAssert.Equal(0, drained.Length);
            TestAssert.Equal(4, state.RegisteredCount, "duplicates are counted as given");

            state.BeginRefreshCycle();
            var buffer = new int[4];
            var taken = state.TakeRefreshShard(buffer, 4, out var completed);
            TestAssert.Equal(4, taken);
            TestAssert.True(completed);
            TestAssert.Equal(5, buffer[0]);
            TestAssert.Equal(5, buffer[1]);
            TestAssert.Equal(7, buffer[2]);
            TestAssert.Equal(5, buffer[3], "the walk preserves registration order, duplicates included");

            TestAssert.Equal(ManagedCropVisualAction.Hide, state.Observe(5, true, CropA));
            TestAssert.Equal(1, state.HiddenCount, "a duplicated registration is still one plot id");
        }

        // ------------------------------------------------------------ H. cache invalidation (hide failure retry)

        /// <summary>
        /// The gameplay layer caches the hide decision before it toggles the renderers. If that
        /// toggle fails it calls Forget, so the next sweep must hide the same crop again instead of
        /// answering None ("already hidden") forever. Unknown ids are a harmless false.
        /// </summary>
        public static void TestForgetDropsCachedHideSoTheNextObserveHidesAgain()
        {
            var state = new ManagedCropVisibilityState();
            state.SetRegisteredPlots(new[] { 1, 2 });

            TestAssert.Equal(ManagedCropVisualAction.Hide, state.Observe(1, true, CropA));
            TestAssert.True(state.IsHidden(1));
            TestAssert.True(state.Forget(1), "the cached hide entry exists and is dropped");
            TestAssert.False(state.IsHidden(1), "forgetting removes the entry");
            TestAssert.Equal(0, state.HiddenCount);

            TestAssert.Equal(ManagedCropVisualAction.Hide, state.Observe(1, true, CropA),
                "the retried observation must hide again, not report the stale cached state");
            TestAssert.True(state.IsHidden(1));

            TestAssert.False(state.Forget(999), "forgetting an unknown plot is a benign false");
            TestAssert.False(state.Forget(2), "forgetting a plot that was never hidden is a benign false");
        }

        // ------------------------------------------------------------ helpers

        private static string ReadResource(string name)
        {
            var assembly = typeof(ManagedCropVisibilityTests).Assembly;
            using var stream = assembly.GetManifestResourceStream(name);
            if (stream is null)
            {
                throw new TestFailureException("embedded resource not found: " + name);
            }

            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        }
    }
}
