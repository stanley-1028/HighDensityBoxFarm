using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using HDBF.Core;
using HDBF.Core.Balance;
using HDBF.Core.Save;

namespace HDBF.Tests
{
    /// <summary>
    /// Offline budget checks for the save record (chapter 12.7, REQ-SAVE-12/13, REQ-TEST-08/09).
    ///
    /// Two things are measured, with an explicit statement of what each number is:
    /// * SIZE: a compact JSON rendering of one record in the chapter-12.2 shape, built from the
    ///   documented field names. It does NOT elide default values, so it is a conservative upper
    ///   bound of what the shipped save entry can weigh. The chapter-12.7 table is the budget
    ///   (10-14 KB per fully registered top farm; 1.0-1.4 MB for 100 of them).
    /// * TIME: sanitise+migrate+canonicalise of 100 top farms (60 000 plot rows). This is a
    ///   deterministic proxy for the save hot path; it is NOT the game's Json.Net serializer and
    ///   is therefore held to a generous sanity bound (2 s) while the measured value is printed.
    ///
    /// Requirement inconsistency found while writing this:
    /// chapter 12.7 budgets 1.0-1.4 MB for 100 fully registered top farms, while chapter 15.3 lists
    /// "save volume for 100 buildings <= 256 KB". Both cannot hold for 100 top farms with 600 plots
    /// each; the offline estimate sides with 12.7 and the contradiction is recorded in
    /// docs/VERIFICATION.md for the Lead. This test does not silently pick one: it reports both.
    /// </summary>
    public static class SaveBudgetTests
    {
        private static readonly TierProfile Top = TierCatalog.Get(HdbfTier.Top);

        public static void TestHundredTopFarmsSixHundredPlotsSizeAndTimeBudget()
        {
            const int buildingCount = 100;
            const int plotsPerBuilding = 600;

            var records = new List<HdbfBuildingRecord>(buildingCount);
            for (var building = 0; building < buildingCount; building++)
            {
                records.Add(BuildTopRecord(building, plotsPerBuilding));
            }

            long totalBytes = 0;
            long minBytes = long.MaxValue;
            long maxBytes = 0;
            foreach (var record in records)
            {
                var estimate = EstimateRecordJsonBytes(record, plotsPerBuilding);
                totalBytes += estimate;
                if (estimate < minBytes) { minBytes = estimate; }
                if (estimate > maxBytes) { maxBytes = estimate; }
            }

            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "[budget] 100 x Top/{0}: estimated JSON bytes total={1:N0} ({2:F2} MB), per building min={3:N0} max={4:N0}",
                plotsPerBuilding, totalBytes, totalBytes / (1024.0 * 1024.0), minBytes, maxBytes));
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "[budget] chapter 12.7 bands: per building 10-14 KB, 100 buildings 1.0-1.4 MB; chapter 15.3 says <=256 KB for 100 buildings (contradiction, see docs)"));

            TestAssert.True(maxBytes <= 14 * 1024, "per-building estimate " + maxBytes + " B exceeds the chapter-12.7 14 KB band");
            TestAssert.True(minBytes >= 10 * 1024, "per-building estimate " + minBytes + " B is below the chapter-12.7 10 KB band");
            TestAssert.True(totalBytes <= 1400 * 1024, "100-building estimate " + totalBytes + " B exceeds the chapter-12.7 1.4 MB band");
            TestAssert.True(totalBytes <= 2L * 1024 * 1024, "100-building estimate exceeded the hard 2 MB ceiling");

            // Memory-shape contract: the save must stay array based (chapter 12.7 forbids
            // object-per-plot). A refactor to IEnumerable<PlotState> would silently blow the budget.
            AssertParallelArrayShape();

            // Scaling check: 600 plots must not cost more than 4.2x of 150 plots (linear is ~4.0x
            // because of the fixed record header); a per-plot object would inflate this ratio.
            var small = EstimateRecordJsonBytes(BuildTopRecord(0, 150), 150);
            var medium = EstimateRecordJsonBytes(BuildTopRecord(0, 300), 300);
            var large = EstimateRecordJsonBytes(BuildTopRecord(0, 600), 600);
            TestAssert.True(medium > small && large > medium, "estimate must grow with plot count");
            var ratio = (double)large / small;
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "[budget] size scaling 600/150 plots = {0:F2}x (linear reference 4.0x)", ratio));
            TestAssert.True(ratio <= 4.2, "size scaling ratio " + ratio + " suggests per-plot object overhead");

            // ---- time proxy --------------------------------------------------------------
            // Warm up JIT and the formatting paths first, then take the best of three rounds.
            for (var warmup = 0; warmup < 2; warmup++)
            {
                RunHotPath(records, Top);
            }

            var sanitizeCanonicalize = MeasureBest(3, () => RunHotPath(records, Top));
            var canonicalOnly = MeasureBest(3, () =>
            {
                for (var i = 0; i < records.Count; i++)
                {
                    HdbfSaveCodec.Canonicalize(records[i]);
                }
            });

            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "[budget] 100 buildings x 600 plots: sanitize+migrate+canonicalize best={0:F1} ms; canonicalize-only best={1:F1} ms; chapter-12.7 serializer budget=50 ms (proxy only, not the game serializer)",
                sanitizeCanonicalize.TotalMilliseconds, canonicalOnly.TotalMilliseconds));

            // Generous CI-safe bound: catches an O(n^2) regression without making the suite flaky
            // on a loaded machine. The real 50 ms serializer budget is an L3/in-game measurement.
            TestAssert.True(sanitizeCanonicalize.TotalMilliseconds < 2000.0,
                "sanitize+migrate+canonicalize of 100 top farms took " + sanitizeCanonicalize.TotalMilliseconds + " ms");
            TestAssert.True(canonicalOnly.TotalMilliseconds < 2000.0,
                "canonicalize of 100 top farms took " + canonicalOnly.TotalMilliseconds + " ms");
        }

        public static void TestHundredBasicFarmsSixtyPlotsSizeBudget()
        {
            // Typical early-game save: 100 basic farms with 60 plots each. Chapter 12.7 table:
            // 0.10-0.15 MB. This is the case chapter 15.3's 256 KB line plausibly meant.
            var basic = TierCatalog.Get(HdbfTier.Basic);
            long total = 0;
            for (var building = 0; building < 100; building++)
            {
                total += EstimateRecordJsonBytes(BuildRecord(HdbfTier.Basic, 60, building), 60);
            }

            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "[budget] 100 x Basic/60: estimated JSON bytes total={0:N0} ({1:F2} MB); chapter 12.7 band 0.10-0.15 MB",
                total, total / (1024.0 * 1024.0)));

            TestAssert.True(total <= 200 * 1024, "100 basic farms estimate " + total + " B exceeds 200 KB");
            TestAssert.True(total > 0);
            TestAssert.True(basic.PlotCapacity == 60);
        }

        // --------------------------------------------------------------- helpers

        private static void RunHotPath(List<HdbfBuildingRecord> records, TierProfile profile)
        {
            for (var i = 0; i < records.Count; i++)
            {
                HdbfSaveCodec.TrySanitize(records[i], profile, 50f, out var repaired, out _);
                HdbfSaveCodec.Canonicalize(repaired);
            }
        }

        private static TimeSpan MeasureBest(int rounds, Action action)
        {
            var best = TimeSpan.MaxValue;
            for (var round = 0; round < rounds; round++)
            {
                var stopwatch = Stopwatch.StartNew();
                action();
                stopwatch.Stop();
                if (stopwatch.Elapsed < best) { best = stopwatch.Elapsed; }
            }

            return best;
        }

        private static HdbfBuildingRecord BuildTopRecord(int building, int plots)
        {
            return BuildRecord(HdbfTier.Top, plots, building);
        }

        private static HdbfBuildingRecord BuildRecord(HdbfTier tier, int plots, int building)
        {
            var ids = new int[plots];
            var crops = new string[plots];
            var fatigue = new float[plots];
            for (var i = 0; i < plots; i++)
            {
                // 5-digit cell ids: the chapter-12.7 band is sized for this order of magnitude.
                // One extra id digit costs ~600 bytes per building / ~60 KB per 100 buildings,
                // see the caveat in docs/VERIFICATION.md.
                ids[i] = 10000 + (building * 7 + i) % 90000;
                crops[i] = i % 3 == 0 ? "Carrot" : i % 3 == 1 ? "Potato" : "Wheat";
                fatigue[i] = (i % 20) * 0.05f;
            }

            return new HdbfBuildingRecord
            {
                SchemaVersion = HdbfBuildingRecord.CurrentSchemaVersion,
                EntityId = 100000 + building,
                Tier = tier,
                Mode = HdbfMode.Normal,
                PlotIds = ids,
                CropIds = crops,
                Fatigue = fatigue,
                ExcludedPlotIds = new[] { 900000, 900001 },
                DueDay = 42.5f,
                CyclesMissed = 1,
                TankWater = 0.5f * TierCatalog.Get(tier).TankCapacity,
                PortPower = false,
                PortPipe = true,
                PortLogistics = false,
                Adapters = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["pipes"] = "active",
                    ["belts"] = "failed",
                },
            };
        }

        private static void AssertParallelArrayShape()
        {
            var type = typeof(HdbfBuildingRecord);
            TestAssert.True(type.GetProperty("PlotIds")!.PropertyType == typeof(int[]), "PlotIds must stay int[]");
            TestAssert.True(type.GetProperty("CropIds")!.PropertyType == typeof(string[]), "CropIds must stay string[]");
            TestAssert.True(type.GetProperty("Fatigue")!.PropertyType == typeof(float[]), "Fatigue must stay float[]");
            TestAssert.True(type.GetProperty("ExcludedPlotIds")!.PropertyType == typeof(int[]), "ExcludedPlotIds must stay int[]");
        }

        /// <summary>
        /// Conservative byte estimate of one record serialized in the chapter-12.2 shape with
        /// compact separators; default values are deliberately not elided.
        /// </summary>
        private static long EstimateRecordJsonBytes(HdbfBuildingRecord record, int capacity)
        {
            var builder = new StringBuilder(16384);
            builder.Append("{\"entityId\":").Append(record.EntityId.ToString(CultureInfo.InvariantCulture));
            builder.Append(",\"tier\":\"").Append(record.Tier).Append('"');
            builder.Append(",\"mode\":\"").Append(record.Mode).Append('"');
            builder.Append(",\"plots\":{\"capacity\":").Append(capacity.ToString(CultureInfo.InvariantCulture));
            builder.Append(",\"ids\":[");
            for (var i = 0; i < record.PlotIds.Length; i++)
            {
                if (i > 0) { builder.Append(','); }
                builder.Append(record.PlotIds[i].ToString(CultureInfo.InvariantCulture));
            }

            builder.Append("],\"crops\":[");
            for (var i = 0; i < record.CropIds.Length; i++)
            {
                if (i > 0) { builder.Append(','); }
                builder.Append('"').Append(record.CropIds[i]).Append('"');
            }

            builder.Append("],\"excludedIds\":[");
            for (var i = 0; i < record.ExcludedPlotIds.Length; i++)
            {
                if (i > 0) { builder.Append(','); }
                builder.Append(record.ExcludedPlotIds[i].ToString(CultureInfo.InvariantCulture));
            }

            builder.Append("]},\"fatigue\":[");
            for (var i = 0; i < record.Fatigue.Length; i++)
            {
                if (i > 0) { builder.Append(','); }
                builder.Append(record.Fatigue[i].ToString("F3", CultureInfo.InvariantCulture));
            }

            builder.Append("],\"maintenance\":{\"dueDay\":")
                .Append(record.DueDay.ToString("F3", CultureInfo.InvariantCulture))
                .Append(",\"cyclesMissed\":").Append(record.CyclesMissed.ToString(CultureInfo.InvariantCulture))
                .Append("},\"water\":{\"tank\":")
                .Append(record.TankWater.ToString("F3", CultureInfo.InvariantCulture))
                .Append("},\"ports\":{\"power\":").Append(record.PortPower ? "true" : "false")
                .Append(",\"pipe\":").Append(record.PortPipe ? "true" : "false")
                .Append(",\"logistics\":").Append(record.PortLogistics ? "true" : "false")
                .Append("},\"adapters\":{");

            var keys = new List<string>(record.Adapters.Keys);
            keys.Sort(StringComparer.Ordinal);
            for (var i = 0; i < keys.Count; i++)
            {
                if (i > 0) { builder.Append(','); }
                builder.Append('"').Append(keys[i]).Append("\":\"").Append(record.Adapters[keys[i]]).Append('"');
            }

            builder.Append("}}");
            return Encoding.UTF8.GetByteCount(builder.ToString());
        }
    }
}
