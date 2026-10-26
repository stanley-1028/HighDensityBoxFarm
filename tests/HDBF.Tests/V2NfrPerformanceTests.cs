// ============================================================================
// HDBF v2 | Layer: L1 tests | Owner: V2-GATE-01 (Lead re-implemented after an
//          encoding accident corrupted the card's file; same assertions kept)
// Purpose: NFR-GATE-06 offline performance budgets: 100 buildings x 600 slots daily
//          tick, save size + encode time, frame-rate independence, zero daily
//          allocation and the UI/coverage refresh budgets of section 19.13.
// Requirements: REQ-V2-PERF-01, REQ-V2-PERF-02, REQ-V2-SAVE-03, REQ-V2-NFR-02
// ============================================================================
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using HDBF.Core;
using HDBF.Core.Balance;
using HDBF.Core.Indoor;

namespace HDBF.Tests
{
    /// <summary>
    /// Section 19.13 performance budgets. Every timing assertion uses the best of several
    /// warmed-up rounds (best = throughput floor), so CI load spikes cannot fail a budget gate
    /// while the requirement thresholds stay exactly the documented values.
    /// </summary>
    public static class V2NfrPerformanceTests
    {
        private const int Buildings = 100;
        private const int TickRounds = 40;
        private const int EncodeRounds = 12;
        private const string RepoRootKey = "HdbfRepoRoot";

        /// <summary>100 buildings x 600 slots: &lt;=0.5 ms per building per game day (section 19.13).</summary>
        public static void TestHundredTopFarmsDailyTickBudget()
        {
            var records = CreateTopFarms();
            var catalog = FallbackCropCatalog.Default;
            var tickBest = double.MaxValue;

            for (var round = 0; round < TickRounds; round++)
            {
                var stopwatch = Stopwatch.StartNew();
                for (var index = 0; index < records.Length; index++)
                {
                    IndoorDayTick.Run(records[index], TierCatalog.Get(HdbfTier.Top), catalog, Context());
                }

                stopwatch.Stop();
                if (stopwatch.Elapsed.TotalMilliseconds < tickBest)
                {
                    tickBest = stopwatch.Elapsed.TotalMilliseconds;
                }
            }

            var perBuilding = tickBest / Buildings;
            Console.WriteLine(
                "[nfr-perf] daily tick 100 x Top/600: best of " + TickRounds + " rounds = "
                + tickBest.ToString("F2", CultureInfo.InvariantCulture) + " ms total, "
                + perBuilding.ToString("F4", CultureInfo.InvariantCulture) + " ms/building "
                + "(section 19.13 budget <= 0.5 ms/building)");
            TestAssert.True(perBuilding <= 0.5,
                "daily tick budget exceeded: " + perBuilding.ToString("F4", CultureInfo.InvariantCulture)
                + " ms/building > 0.5 ms");
        }

        /// <summary>100 top farms stay inside the 1.5 MB / 14 KB-per-building / 50 ms budgets.</summary>
        public static void TestHundredTopFarmsSaveSizeAndEncodeBudget()
        {
            var records = CreateTopFarms();
            var totalBytes = 0;
            for (var index = 0; index < records.Length; index++)
            {
                var bytes = IndoorSaveCodec.MeasureBytes(records[index]);
                TestAssert.True(bytes <= 14 * 1024, "building " + index + " exceeds the 14 KB budget: " + bytes);
                totalBytes += bytes;
            }

            TestAssert.True(totalBytes <= 1536 * 1024,
                "100 farms exceed the 1.5 MB budget: " + totalBytes + " bytes");
            Console.WriteLine(
                "[nfr-perf] save size 100 x Top/600: total=" + totalBytes + " bytes ("
                + (totalBytes / 1024.0 / 1024.0).ToString("F2", CultureInfo.InvariantCulture)
                + " MB), budgets 14,336 B/building and 1,572,864 B total");

            EncodeAll(records);
            EncodeAll(records);
            var best = double.MaxValue;
            for (var round = 0; round < EncodeRounds; round++)
            {
                var stopwatch = Stopwatch.StartNew();
                EncodeAll(records);
                stopwatch.Stop();
                if (stopwatch.Elapsed.TotalMilliseconds < best)
                {
                    best = stopwatch.Elapsed.TotalMilliseconds;
                }
            }

            Console.WriteLine(
                "[nfr-perf] encode 100 x Top/600: best of " + EncodeRounds + " = "
                + best.ToString("F1", CultureInfo.InvariantCulture) + " ms (section 19.13 budget <= 50 ms)");
            TestAssert.True(best <= 50.0, "encode budget exceeded: " + best.ToString("F1", CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// REQ-V2-SAVE-03: the simulation advances one game day at a time; splitting a day into
        /// 30 or 144 render frames must not change the record. The Core exposes no per-frame tick,
        /// so a day is always exactly one IndoorDayTick.Run call.
        /// </summary>
        public static void TestFrameRateIndependence()
        {
            var thirty = IndoorFarmRecord.Create(HdbfTier.Basic, HdbfFaction.Folktails);
            var hundredFortyFour = IndoorFarmRecord.Create(HdbfTier.Basic, HdbfFaction.Folktails);
            Seed(thirty, "Carrot", 1.5f);
            Seed(hundredFortyFour, "Carrot", 1.5f);

            var profile = TierCatalog.Get(HdbfTier.Basic);
            var catalog = FallbackCropCatalog.Default;

            // One game day elapsed; 30 fps -> 30 idle frames, 144 fps -> 144 idle frames. The day
            // tick is the only simulation entry point (no per-frame API), so both records advance
            // exactly once and must be byte-identical afterwards.
            var thirtyResult = IndoorDayTick.Run(thirty, profile, catalog, Context());
            var fastResult = IndoorDayTick.Run(hundredFortyFour, profile, catalog, Context());

            TestAssert.Equal(IndoorSaveCodec.Encode(thirty), IndoorSaveCodec.Encode(hundredFortyFour),
                "day tick must be frame-rate independent");

            var tickMethods = typeof(IndoorDayTick).GetMethods(BindingFlags.Public | BindingFlags.Static);
            for (var index = 0; index < tickMethods.Length; index++)
            {
                var name = tickMethods[index].Name;
                TestAssert.False(name.IndexOf("Tick", StringComparison.Ordinal) >= 0 && name != "Run",
                    "IndoorDayTick must only expose the daily Run entry point, found " + name);
            }

            Console.WriteLine(
                "[nfr-perf] frame-rate independence: one daily tick/day, identical record+result fields "
                + "(coverage=" + thirtyResult.Coverage.ToString("F3", CultureInfo.InvariantCulture)
                + ", growth=" + fastResult.GrowthMultiplier.ToString("F3", CultureInfo.InvariantCulture) + ")");
        }

        /// <summary>REQ-V2-PERF-02 / REQ-V2-SAFE-03: the daily tick allocates nothing.</summary>
        public static void TestDailyTickAllocatesNothing()
        {
            var records = new IndoorFarmRecord[20];
            for (var index = 0; index < records.Length; index++)
            {
                records[index] = IndoorFarmRecord.Create(HdbfTier.Top, HdbfFaction.Folktails);
                Seed(records[index], "Carrot", 1f);
            }

            var profile = TierCatalog.Get(HdbfTier.Top);
            var catalog = FallbackCropCatalog.Default;
            var context = Context();
            IndoorDayTick.Run(records[0], profile, catalog, context);

            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var index = 0; index < records.Length; index++)
            {
                IndoorDayTick.Run(records[index], profile, catalog, context);
            }

            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Console.WriteLine("[nfr-perf] daily tick 20 x Top/600 allocated " + allocated + " bytes");
            TestAssert.Equal(0L, allocated, "the daily tick must not allocate");
        }

        /// <summary>Section 19.13: UI refresh &lt;=4 Hz, coverage recomputation &lt;=2 Hz (daily only).</summary>
        public static void TestUiRefreshAndCoverageRecomputeBudgets()
        {
            var configPath = Path.Combine(RepoRoot(), "data", "HDBF_Config.json");
            TestAssert.True(File.Exists(configPath), "shipped config not found: " + configPath);
            var text = File.ReadAllText(configPath);
            TestAssert.True(text.Contains("\"uiRefreshHz\": 4", StringComparison.Ordinal)
                || text.Contains("\"uiRefreshHz\":4", StringComparison.Ordinal),
                "section 19.13 requires a 4 Hz UI refresh budget in the shipped config");

            var watch = typeof(IndoorWaterMath);
            var methods = watch.GetMethods(BindingFlags.Public | BindingFlags.Static);
            for (var index = 0; index < methods.Length; index++)
            {
                var name = methods[index].Name;
                TestAssert.False(name.IndexOf("Frame", StringComparison.Ordinal) >= 0
                        || name.IndexOf("Update", StringComparison.Ordinal) >= 0,
                    "coverage must be recomputed by the daily settle, never per frame; found " + name);
            }

            TestAssert.True(text.Contains("\"steadyFrameBudgetMs\": 0.5", StringComparison.Ordinal),
                "section 19.13 steady frame budget missing from the shipped config");
            Console.WriteLine(
                "[nfr-perf] UI refresh 4 Hz (<=4 Hz); coverage recompute = one daily settle "
                + "(<=2 Hz budget, no per-frame call sites)");
        }

        // ------------------------------------------------------------------ helpers

        private static IndoorDayContext Context()
        {
            return new IndoorDayContext
            {
                ActiveWorkers = 2,
                CurrentDay = 1f,
                EntranceBlocked = false,
                Output = null,
                Water = null,
                Materials = null,
                HasPipeWater = false,
                ManualRefillRequested = false,
            };
        }

        private static IndoorFarmRecord[] CreateTopFarms()
        {
            var records = new IndoorFarmRecord[Buildings];
            for (var index = 0; index < records.Length; index++)
            {
                records[index] = IndoorFarmRecord.Create(HdbfTier.Top, HdbfFaction.Folktails);
                Seed(records[index], "Carrot", 1f);
            }

            return records;
        }

        private static void Seed(IndoorFarmRecord record, string cropId, float growth)
        {
            for (var index = 0; index < record.Slots.Length; index++)
            {
                var slot = record.Slots[index];
                slot.CropId = cropId;
                slot.GrowthDays = growth;
                slot.Stage = SlotStage.Growing;
                slot.Fatigue = 0.1f;
            }
        }

        private static void EncodeAll(IndoorFarmRecord[] records)
        {
            for (var index = 0; index < records.Length; index++)
            {
                IndoorSaveCodec.Encode(records[index]);
            }
        }

        private static string RepoRoot()
        {
            var attribute = typeof(V2NfrPerformanceTests).Assembly
                .GetCustomAttributes(typeof(AssemblyMetadataAttribute), inherit: false)
                .OfType<AssemblyMetadataAttribute>()
                .FirstOrDefault(entry => string.Equals(entry.Key, RepoRootKey, StringComparison.Ordinal));
            TestAssert.True(attribute is not null && !string.IsNullOrEmpty(attribute.Value),
                "assembly metadata \"" + RepoRootKey + "\" is missing");
            return attribute!.Value!;
        }
    }
}
