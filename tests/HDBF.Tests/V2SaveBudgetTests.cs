// ============================================================================
// HDBF v2 | Layer: L1 test | Owner: V2-CORE-02
// Purpose: Save size/time budget of section 19.10/19.13: 100 x Top/600 slots, per-building
//          14 KB and 1.5 MB total, printed measured values plus line-size scaling and
//          measure stability (REQ-V2-SAVE-07, NFR-GATE-06).
// Requirements: REQ-V2-SAVE-07, REQ-V2-PERF-02
// ============================================================================
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using HDBF.Core;
using HDBF.Core.Indoor;

namespace HDBF.Tests
{
    public static class V2SaveBudgetTests
    {
        private const int BuildingCount = 100;
        private const int TopSlots = 600;

        public static void TestHundredTopFarmsSizeAndTimeBudget()
        {
            var records = new List<IndoorFarmRecord>(BuildingCount);
            for (var building = 0; building < BuildingCount; building++)
            {
                records.Add(BuildTopFarm(building));
            }

            long total = 0;
            long minimum = long.MaxValue;
            long maximum = 0;
            for (var index = 0; index < records.Count; index++)
            {
                var bytes = IndoorSaveCodec.MeasureBytes(records[index]);
                total += bytes;
                if (bytes < minimum) minimum = bytes;
                if (bytes > maximum) maximum = bytes;
            }

            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "[save-budget] {0} x Top/{1}: total={2:N0} bytes ({3:F2} MB), per building min={4:N0} avg={5:N0} max={6:N0}, per slot(max)={7:F1} bytes",
                BuildingCount, TopSlots, total, total / (1024.0 * 1024.0), minimum, total / BuildingCount, maximum,
                (double)maximum / TopSlots));
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "[save-budget] section-19.10 bands: per building <= {0:N0} bytes, 100 buildings <= {1:N0} bytes ({2:F2} MB)",
                IndoorSaveLimits.BudgetBytesPerBuilding, IndoorSaveLimits.BudgetBytesForHundredBuildings,
                IndoorSaveLimits.BudgetBytesForHundredBuildings / (1024.0 * 1024.0)));

            TestAssert.True(maximum <= IndoorSaveLimits.BudgetBytesPerBuilding,
                "per-building save text " + maximum + " B exceeds the 14 KB budget");
            TestAssert.True(minimum > 0, "a measured record must not be empty");
            TestAssert.True(total <= IndoorSaveLimits.BudgetBytesForHundredBuildings,
                "100-building save text " + total + " B exceeds the 1.5 MB budget");

            for (var warmup = 0; warmup < 2; warmup++)
            {
                EncodeAll(records);
            }

            var best = MeasureBest(3, () => EncodeAll(records));
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "[save-budget] encode 100 x Top/600 best={0:F1} ms (section 19.13 serializer target <= {1} ms; CI bound 2000 ms)",
                best.TotalMilliseconds, IndoorSaveLimits.BudgetSerializeMilliseconds));
            TestAssert.True(best.TotalMilliseconds < 2000.0,
                "encoding 100 top farms took " + best.TotalMilliseconds + " ms");

            var decoded = V2SaveFixtures.DecodeStrict(IndoorSaveCodec.Encode(records[0]));
            V2SaveFixtures.AssertRecordsEqual(records[0], decoded, "budget record round trip");
        }

        public static void TestEmptyTopFarmSizeBudget()
        {
            var record = V2SaveFixtures.RichRecord(HdbfTier.Top, populate: false);
            var bytes = IndoorSaveCodec.MeasureBytes(record);
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "[save-budget] empty Top/600: {0:N0} bytes ({1:F1} bytes per empty slot)",
                bytes, (double)bytes / TopSlots));

            TestAssert.True(bytes > 0);
            TestAssert.True(bytes <= IndoorSaveLimits.BudgetBytesPerBuilding,
                "empty top-tier record " + bytes + " B exceeds the 14 KB budget");
        }

        public static void TestSizeScalesWithSlotCapacity()
        {
            var basicBytes = IndoorSaveCodec.MeasureBytes(V2SaveFixtures.RichRecord(HdbfTier.Basic));
            var topBytes = IndoorSaveCodec.MeasureBytes(V2SaveFixtures.RichRecord(HdbfTier.Top));
            var basicPerSlot = (double)basicBytes / V2SaveFixtures.Basic.SlotCapacity;
            var topPerSlot = (double)topBytes / TopSlots;
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "[save-budget] per-slot bytes: Basic/60={0:F1}, Top/600={1:F1}, size ratio={2:F2}x (slot ratio 10x)",
                basicPerSlot, topPerSlot, (double)topBytes / basicBytes));

            TestAssert.True(topBytes > basicBytes, "a 600-slot record must be larger than a 60-slot record");
            TestAssert.True(topPerSlot <= basicPerSlot * 1.5,
                "per-slot cost grew to " + topPerSlot + " B (Basic " + basicPerSlot + " B)");
            TestAssert.True(topBytes <= basicBytes * 12, "unexpected super-linear size growth");
        }

        public static void TestMeasureBytesIsStableAndMatchesUtf8()
        {
            var record = V2SaveFixtures.RichRecord(HdbfTier.Advanced, HdbfFaction.IronTeeth);
            var text = IndoorSaveCodec.Encode(record);
            var bytes = IndoorSaveCodec.MeasureBytes(record);

            TestAssert.Equal(bytes, IndoorSaveCodec.MeasureBytes(record), "MeasureBytes must be deterministic");
            TestAssert.Equal(Encoding.UTF8.GetByteCount(text), bytes, "MeasureBytes must be the UTF-8 size");
            var decoded = V2SaveFixtures.DecodeStrict(text);
            TestAssert.Equal(bytes, IndoorSaveCodec.MeasureBytes(decoded), "a decoded record keeps its size");
        }

        private static IndoorFarmRecord BuildTopFarm(int building)
        {
            var record = IndoorFarmRecord.Create(HdbfTier.Top, HdbfFaction.Folktails);
            record.EntityId = "farm-" + building.ToString(CultureInfo.InvariantCulture);
            record.MaintenanceCyclesMissed = building % 3;
            var crops = V2SaveFixtures.FolktailsCrops;
            for (var index = 0; index < record.Slots.Length; index++)
            {
                var slot = record.Slots[index];
                slot.CropId = crops[(building + index) % crops.Length];
                slot.GrowthDays = ((building * 7 + index) % 6) * 0.4f;
                slot.Fatigue = ((building + index) % 20) * 0.05f;
                slot.LastCropId = crops[(building + index + 1) % crops.Length];
                slot.Stage = SlotStage.Growing;
            }

            return record;
        }

        private static void EncodeAll(List<IndoorFarmRecord> records)
        {
            for (var index = 0; index < records.Count; index++)
            {
                IndoorSaveCodec.Encode(records[index]);
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
                if (stopwatch.Elapsed < best)
                {
                    best = stopwatch.Elapsed;
                }
            }

            return best;
        }
    }
}
