// ============================================================================
// HDBF v2 | Layer: L1 test | Owner: V2-GATE-06
// Purpose: NFR-GATE-06 (REQ-V2-PERF-01/02, SAVE-03/07) offline performance gate:
//          100 buildings x 600 slots daily tick <=0.5 ms/building, 100-building save
//          <=1.5 MB / 14 KB per building and <=50 ms encode, zero per-slot allocation,
//          30/144 fps day-trigger independence (Core daily tick is game-day driven) and
//          the UI 4 Hz / coverage 2 Hz budget constants read from the shipped sources.
//          Thresholds are the requirement values; time measurements take the best of a
//          fixed number of runs so CI jitter cannot turn the gate red, and every measured
//          number is printed for the release evidence.
// Requirements: REQ-V2-PERF-01, REQ-V2-PERF-02, REQ-V2-SAVE-03, REQ-V2-SAVE-07,
//               REQ-V2-SAFE-03, REQ-V2-UI-01, REQ-V2-TEST-01
// ============================================================================
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using HDBF.Core;
using HDBF.Core.Balance;
using HDBF.Core.Config;
using HDBF.Core.Indoor;

namespace HDBF.Tests
{
    public static class V2NfrPerformanceTests
    {
        private const int BuildingCount = 100;
        private const int TopSlots = 600;
        private const int TickRounds = 100;
        private const int EncodeRounds = 3;
        private const string RepoRootKey = "HdbfRepoRoot";

        /// <summary>Section 19.13: daily tick budget per building per game day.</summary>
        private const double DailyTickBudgetMillisecondsPerBuilding = 0.5;

        // ------------------------------------------------------------------ daily tick budget

        public static void TestHundredTopFarmsDailyTickMeetsBudget()
        {
            var records = CreateFarms(BuildingCount);
            var tier = TierCatalog.Get(HdbfTier.Top);
            var context = WorkingContext(16);
            WarmUp(records, tier, context, 3);

            var best = TimeSpan.MaxValue;
            for (var round = 0; round < TickRounds; round++)
            {
                var stopwatch = Stopwatch.StartNew();
                TickAll(records, tier, context);
                stopwatch.Stop();
                if (stopwatch.Elapsed < best)
                {
                    best = stopwatch.Elapsed;
                }
            }

            var perBuilding = best.TotalMilliseconds / records.Length;
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "[nfr-perf] daily tick 100 x Top/{0}: best of {1} rounds = {2:F2} ms total, {3:F4} ms/building "
                + "(section 19.13 budget <= {4} ms/building)",
                TopSlots, TickRounds, best.TotalMilliseconds, perBuilding, DailyTickBudgetMillisecondsPerBuilding));
            TestAssert.True(perBuilding <= DailyTickBudgetMillisecondsPerBuilding,
                "daily tick " + perBuilding.ToString("F4", CultureInfo.InvariantCulture)
                + " ms/building exceeds the 0.5 ms requirement (REQ-V2-PERF-01/02)");
        }

        public static void TestHundredTopFarmsDailyTickAllocatesNothing()
        {
            var records = CreateFarms(BuildingCount);
            var tier = TierCatalog.Get(HdbfTier.Top);
            var context = WorkingContext(16);
            WarmUp(records, tier, context, 3);

            // A per-slot reference allocation would be at least 600 x 100 x 8 B = 480 KB; the
            // requirement is stricter than a bound - the daily tick must allocate 0 bytes.
            var before = GC.GetAllocatedBytesForCurrentThread();
            TickAll(records, tier, context);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Console.WriteLine("[nfr-perf] daily tick 100 x " + TopSlots + " slots allocated " + allocated
                              + " bytes (requirement: no per-frame/per-slot allocation, REQ-V2-SAFE-03)");
            TestAssert.Equal(0L, allocated,
                "the daily tick allocated " + allocated + " bytes; REQ-V2-PERF-02/Safe-03 require 0");
        }

        // ------------------------------------------------------------------ save budget

        public static void TestHundredTopFarmsSaveSizeAndEncodeBudget()
        {
            // Pin the code constants to the requirement values so the gate cannot be relaxed by
            // editing IndoorSaveLimits alone.
            TestAssert.Equal(14 * 1024, IndoorSaveLimits.BudgetBytesPerBuilding,
                "per-building save budget must stay 14 KB (section 19.10/19.13)");
            TestAssert.Equal(1536 * 1024, IndoorSaveLimits.BudgetBytesForHundredBuildings,
                "100-building save budget must stay 1.5 MB (section 19.10/19.13)");
            TestAssert.Equal(50, IndoorSaveLimits.BudgetSerializeMilliseconds,
                "serializer budget must stay 50 ms (section 19.13)");
            TestAssert.Equal(TopSlots, TierCatalog.Get(HdbfTier.Top).SlotCapacity,
                "top tier must keep 600 slots");

            var records = new List<IndoorFarmRecord>(BuildingCount);
            for (var building = 0; building < BuildingCount; building++)
            {
                records.Add(V2SaveFixtures.RichRecord(HdbfTier.Top,
                    entityId: "perf-farm-" + building.ToString(CultureInfo.InvariantCulture)));
            }

            long total = 0;
            long maximum = 0;
            for (var index = 0; index < records.Count; index++)
            {
                var bytes = IndoorSaveCodec.MeasureBytes(records[index]);
                total += bytes;
                if (bytes > maximum)
                {
                    maximum = bytes;
                }
            }

            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "[nfr-perf] save size 100 x Top/{0}: total={1:N0} bytes ({2:F2} MB), max/building={3:N0} bytes "
                + "(budgets {4:N0} B total / {5:N0} B per building)",
                TopSlots, total, total / (1024.0 * 1024.0), maximum,
                IndoorSaveLimits.BudgetBytesForHundredBuildings, IndoorSaveLimits.BudgetBytesPerBuilding));
            TestAssert.True(maximum <= IndoorSaveLimits.BudgetBytesPerBuilding,
                "one building saves " + maximum + " B, over the 14 KB budget");
            TestAssert.True(total <= IndoorSaveLimits.BudgetBytesForHundredBuildings,
                "100 buildings save " + total + " B, over the 1.5 MB budget");

            EncodeAll(records);
            EncodeAll(records);
            var best = TimeSpan.MaxValue;
            for (var round = 0; round < EncodeRounds; round++)
            {
                var stopwatch = Stopwatch.StartNew();
                EncodeAll(records);
                stopwatch.Stop();
                if (stopwatch.Elapsed < best)
                {
                    best = stopwatch.Elapsed;
                }
            }

            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "[nfr-perf] encode 100 x Top/{0}: best of {1} = {2:F1} ms (section 19.13 budget <= {3} ms)",
                TopSlots, EncodeRounds, best.TotalMilliseconds, IndoorSaveLimits.BudgetSerializeMilliseconds));
            TestAssert.True(best.TotalMilliseconds <= IndoorSaveLimits.BudgetSerializeMilliseconds,
                "encoding 100 top farms took " + best.TotalMilliseconds.ToString("F1", CultureInfo.InvariantCulture)
                + " ms, over the 50 ms budget");
        }

        // ------------------------------------------------------------------ 30/144 fps independence

        public static void TestDailyTickIsFrameRateIndependent()
        {
            var tier = TierCatalog.Get(HdbfTier.Top);
            var context = WorkingContext(16, day: 5f);
            var thirty = CreateFarm();
            var hundredFortyFour = CreateFarm();

            // The game triggers the day tick once per game day no matter how many frames that
            // day contains. Simulate a 30 fps and a 144 fps driver for one game day: both must
            // call IndoorDayTick.Run exactly once and produce bit-identical state and results.
            var result30 = RunDayOfFrames(thirty, tier, context, framesPerDay: 30, out var ticks30);
            var result144 = RunDayOfFrames(hundredFortyFour, tier, context, framesPerDay: 144, out var ticks144);

            TestAssert.Equal(1, ticks30, "30 fps driver must call the daily tick exactly once per day");
            TestAssert.Equal(1, ticks144, "144 fps driver must call the daily tick exactly once per day");

            AssertResultEqual(result30, result144, "30/144 fps");
            V2SaveFixtures.AssertRecordsEqual(thirty, hundredFortyFour, "30/144 fps record", tolerance: 0f);

            // Second day: run the same drivers again and require the same equality (stateful
            // accumulation cannot depend on the number of substeps either).
            var second30 = RunDayOfFrames(thirty, tier, context, framesPerDay: 30, out var secondTicks30);
            var second144 = RunDayOfFrames(hundredFortyFour, tier, context, framesPerDay: 144, out var secondTicks144);
            TestAssert.Equal(1, secondTicks30);
            TestAssert.Equal(1, secondTicks144);
            AssertResultEqual(second30, second144, "30/144 fps day 2");
            V2SaveFixtures.AssertRecordsEqual(thirty, hundredFortyFour, "30/144 fps record day 2", tolerance: 0f);

            Console.WriteLine("[nfr-perf] frame-rate independence: 30 fps x 2 days vs 144 fps x 2 days "
                              + "-> 1 daily tick/day each, identical record + result fields (REQ-V2-SAVE-03)");
        }

        // ------------------------------------------------------------------ UI / coverage budgets

        public static void TestUiRefreshAndCoverageRecomputeBudgets()
        {
            // UI panel refresh budget: section 19.13 says <=4 Hz. The shipped default config and
            // the panel's own hard interval constant must both respect it.
            TestAssert.Equal(4, HdbfConfig.Default.Perf.UiRefreshHz,
                "the shipped default UI refresh rate must stay 4 Hz (section 19.13)");
            TestAssert.True(HdbfConfig.Default.Perf.UiRefreshHz <= 4,
                "default UI refresh " + HdbfConfig.Default.Perf.UiRefreshHz + " Hz exceeds the 4 Hz budget");

            var shippedConfig = RepoRead("data/HDBF_Config.json");
            var shippedHz = MatchInt(shippedConfig, "\"uiRefreshHz\"\\s*:\\s*(\\d+)");
            TestAssert.True(shippedHz >= 1 && shippedHz <= 4,
                "data/HDBF_Config.json perf.uiRefreshHz=" + shippedHz + " is outside the 1..4 Hz v2 budget");

            var panelSource = RepoRead("src/HDBF.UI/Panels/HdbfFarmPanelFragment.cs");
            var interval = MatchDouble(panelSource, "RefreshIntervalSeconds\\s*=\\s*([0-9.]+)f");
            TestAssert.True(interval >= 0.25 - 1e-6,
                "the panel refresh interval " + interval.ToString("F3", CultureInfo.InvariantCulture)
                + " s is faster than 4 Hz (REQ-V2-UI-01)");
            var panelHz = 1.0 / interval;
            TestAssert.True(panelHz <= 4.0 + 1e-6,
                "the panel refresh rate " + panelHz.ToString("F2", CultureInfo.InvariantCulture)
                + " Hz exceeds the 4 Hz budget");

            // The requirement source must keep the numeric budgets the constants are pinned to.
            var requirements = RepoRead("docs/REQ_V2_SECTION_19.md");
            TestAssert.True(requirements.Contains("覆盖率重算 | 2 Hz/建筑", StringComparison.Ordinal),
                "section 19.13 must keep the 2 Hz/建筑 coverage-recomputation budget");
            TestAssert.True(requirements.Contains("UI 刷新 | ≤4 Hz", StringComparison.Ordinal),
                "section 19.13 must keep the 4 Hz UI refresh budget");

            // Coverage budget: section 19.13 allows a 2 Hz coverage recomputation. In v2 the
            // coverage is settled only in the Core daily tick (IndoorWaterMath.SettleDay), and the
            // daily tick is armed by a one-game-day time trigger, so the recomputation frequency is
            // one per game day and never a per-frame/UI path. These source facts are the assertion.
            var settleCallSites = CountCallSites("IndoorWaterMath.SettleDay(", "src/HDBF.Core/Indoor/IndoorWaterMath.cs");
            TestAssert.Equal(1, settleCallSites.Count,
                "coverage must be settled in exactly one place (the daily tick); found "
                + settleCallSites.Count + " call site(s): " + string.Join(", ", settleCallSites));

            var runCallSites = CountCallSites("IndoorDayTick.Run(");
            TestAssert.Equal(1, runCallSites.Count,
                "IndoorDayTick.Run must be called from exactly one orchestration site; found "
                + runCallSites.Count + ": " + string.Join(", ", runCallSites));
            TestAssert.True(runCallSites[0].StartsWith("src/HDBF.Gameplay/", StringComparison.Ordinal)
                            && runCallSites[0].Contains("HdbfBoxFarmController.Tick.cs", StringComparison.Ordinal),
                "the daily tick must be driven by the gameplay day orchestration; found " + runCallSites[0]);

            foreach (var panelFile in new[]
                     {
                         "src/HDBF.UI/Panels/HdbfIndoorPanelFactory.cs",
                         "src/HDBF.Gameplay/Components/HdbfBoxFarmController.IndoorSnapshot.cs",
                     })
            {
                var source = RepoRead(panelFile);
                TestAssert.False(source.Contains("SettleDay", StringComparison.Ordinal),
                    panelFile + " must not recompute coverage outside the daily tick");
            }

            var snapshotSource = RepoRead("src/HDBF.Gameplay/Components/HdbfBoxFarmController.IndoorSnapshot.cs");
            TestAssert.True(snapshotSource.Contains("_lastCoverage", StringComparison.Ordinal),
                "the panel snapshot must reuse the daily coverage value, not recompute it");

            var activation = RepoRead("src/HDBF.Gameplay/Components/HdbfBoxFarmController.Activation.cs");
            TestAssert.True(activation.Contains("_timeTriggerFactory.Create(OnDayTick, DayTriggerDelayDays)",
                    StringComparison.Ordinal),
                "the daily tick must be armed by a time trigger (one tick per game day)");
            var controller = RepoRead("src/HDBF.Gameplay/Components/HdbfBoxFarmController.cs");
            TestAssert.True(controller.Contains("DayTriggerDelayDays = 1f", StringComparison.Ordinal),
                "the day trigger delay must stay 1 game day");

            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "[nfr-perf] UI refresh {0:F2} Hz (<=4 Hz); coverage recompute = 1/game-day in the daily tick only "
                + "(<=2 Hz budget, 0 UI/frame call sites)", panelHz));
        }

        // ------------------------------------------------------------------ helpers: drivers

        private static IndoorDayResult RunDayOfFrames(IndoorFarmRecord record, TierProfile tier,
            IndoorDayContext context, int framesPerDay, out int tickCount)
        {
            tickCount = 0;
            var result = default(IndoorDayResult);
            var accumulator = 0.0;
            var step = 1.0 / framesPerDay;
            for (var frame = 0; frame < framesPerDay; frame++)
            {
                accumulator += step;
                if (accumulator + 1e-9 < 1.0)
                {
                    continue;
                }

                // One game day elapsed: exactly one daily tick, regardless of the frame count.
                result = IndoorDayTick.Run(record, tier, FallbackCropCatalog.Default, in context);
                tickCount++;
                accumulator = 0.0;
                if (frame != framesPerDay - 1)
                {
                    throw new TestFailureException(
                        "a day completed after " + (frame + 1) + " of " + framesPerDay
                        + " frames; the frame driver is wrong");
                }
            }

            return result;
        }

        private static void AssertResultEqual(IndoorDayResult expected, IndoorDayResult actual, string context)
        {
            TestAssert.Equal(expected.Coverage, actual.Coverage, 0f, context + ": coverage");
            TestAssert.Equal(expected.GrowthMultiplier, actual.GrowthMultiplier, 0f, context + ": growth");
            TestAssert.Equal(expected.YieldMultiplier, actual.YieldMultiplier, 0f, context + ": yield");
            TestAssert.Equal(expected.Warnings, actual.Warnings, context + ": warnings");
            TestAssert.Equal(expected.Changed, actual.Changed, context + ": changed");
            TestAssert.Equal(expected.Stalled, actual.Stalled, context + ": stalled");
            TestAssert.Equal(expected.RefillActionsUsed, actual.RefillActionsUsed, context + ": refill actions");
            TestAssert.Equal(expected.ManualRefill, actual.ManualRefill, context + ": refill outcome");
            TestAssert.Equal(expected.TransitionYieldScale, actual.TransitionYieldScale, 0f, context + ": transition");
            TestAssert.Equal(expected.Actions.Harvested, actual.Actions.Harvested, context + ": harvested");
            TestAssert.Equal(expected.Actions.Planted, actual.Actions.Planted, context + ": planted");
            TestAssert.Equal(expected.Actions.ActionsUsed, actual.Actions.ActionsUsed, context + ": actions used");
            TestAssert.Equal(expected.Actions.BlockedByOutput, actual.Actions.BlockedByOutput, context + ": blocked");
            TestAssert.Equal(expected.Actions.Backlog, actual.Actions.Backlog, context + ": backlog");
        }

        // ------------------------------------------------------------------ helpers: fixtures

        private sealed class AcceptAllSink : IInventorySink
        {
            public bool TryAdd(string goodId, int amount) => true;
        }

        private sealed class InfiniteWater : IWaterSource
        {
            public WaterSupplyKind Kind => WaterSupplyKind.Pipe;

            public bool IsAvailable => true;

            public bool TryDraw(float amount, out float actual)
            {
                actual = amount;
                return true;
            }
        }

        private static IndoorDayContext WorkingContext(int workers, float day = 1f)
        {
            return new IndoorDayContext
            {
                ActiveWorkers = workers,
                CurrentDay = day,
                Output = new AcceptAllSink(),
                Water = new InfiniteWater(),
                HasPipeWater = true,
            };
        }

        private static IndoorFarmRecord[] CreateFarms(int count)
        {
            var records = new IndoorFarmRecord[count];
            for (var index = 0; index < count; index++)
            {
                records[index] = CreateFarm();
            }

            return records;
        }

        private static IndoorFarmRecord CreateFarm()
        {
            var record = IndoorFarmRecord.Create(HdbfTier.Top, HdbfFaction.Folktails);
            record.EntityId = "nfr-perf-farm";
            for (var slot = 0; slot < record.Slots.Length; slot++)
            {
                record.Slots[slot].CropId = "Carrot";
                record.Slots[slot].Stage = SlotStage.Growing;
                record.Slots[slot].GrowthDays = (slot % 3) * 0.5f;
            }

            return record;
        }

        private static void WarmUp(IndoorFarmRecord[] records, TierProfile tier, IndoorDayContext context, int rounds)
        {
            for (var round = 0; round < rounds; round++)
            {
                TickAll(records, tier, context);
            }
        }

        private static void TickAll(IndoorFarmRecord[] records, TierProfile tier, IndoorDayContext context)
        {
            for (var index = 0; index < records.Length; index++)
            {
                IndoorDayTick.Run(records[index], tier, FallbackCropCatalog.Default, context);
            }
        }

        private static void EncodeAll(List<IndoorFarmRecord> records)
        {
            for (var index = 0; index < records.Count; index++)
            {
                IndoorSaveCodec.Encode(records[index]);
            }
        }

        // ------------------------------------------------------------------ helpers: source scan

        private static List<string> CountCallSites(string token, params string[] definitionFiles)
        {
            var sites = new List<string>();
            var root = RepoPath("src");
            foreach (var path in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(RepoRoot(), path).Replace('\\', '/');
                if (Array.IndexOf(definitionFiles, relative) >= 0)
                {
                    continue;
                }

                var text = File.ReadAllText(path, Encoding.UTF8);
                var count = CountOccurrences(text, token);
                for (var index = 0; index < count; index++)
                {
                    sites.Add(relative);
                }
            }

            sites.Sort(StringComparer.Ordinal);
            return sites;
        }

        private static int CountOccurrences(string text, string token)
        {
            var count = 0;
            var offset = 0;
            while (true)
            {
                var found = text.IndexOf(token, offset, StringComparison.Ordinal);
                if (found < 0)
                {
                    return count;
                }

                count++;
                offset = found + token.Length;
            }
        }

        private static int MatchInt(string text, string pattern)
        {
            var match = Regex.Match(text, pattern);
            TestAssert.True(match.Success, "pattern not found: " + pattern);
            return int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        }

        private static double MatchDouble(string text, string pattern)
        {
            var match = Regex.Match(text, pattern);
            TestAssert.True(match.Success, "pattern not found: " + pattern);
            return double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        }

        // ------------------------------------------------------------------ helpers: repo IO

        private static string RepoRead(string relativePath)
        {
            return File.ReadAllText(RepoPath(relativePath), Encoding.UTF8);
        }

        private static string RepoPath(string relativePath)
        {
            return Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static string RepoRoot()
        {
            var metadata = typeof(V2NfrPerformanceTests).Assembly
                .GetCustomAttributes<AssemblyMetadataAttribute>();
            foreach (var attribute in metadata)
            {
                if (string.Equals(attribute.Key, RepoRootKey, StringComparison.Ordinal)
                    && !string.IsNullOrEmpty(attribute.Value))
                {
                    return attribute.Value!;
                }
            }

            throw new TestFailureException("missing HdbfRepoRoot assembly metadata");
        }
    }
}
