// ============================================================================
// HDBF v2 | Layer: L1 test | Owner: V2-CORE-01
// Purpose: Contract of the panel's live per-crop status list (REQ-V2-UI-05): slot split,
//          average progress, mature = 100 %, stable conditioning-table order, unknown crop ids,
//          non-finite saves, broken catalogs, the row cap and determinism.
// Requirements: REQ-V2-UI-05, REQ-V2-CORE-04, REQ-V2-READ-04, REQ-V2-SAFE-01, REQ-V2-TEST-01
// ============================================================================
using System;
using System.Collections.Generic;
using HDBF.Core;
using HDBF.Core.Indoor;

namespace HDBF.Tests
{
    public static class V2CropReportTests
    {
        // ---------------------------------------------------------------- empty inputs

        public static void TestNothingPlantedReportsNoRows()
        {
            var record = Record(HdbfFaction.Folktails, Slot(string.Empty, 0f, SlotStage.Empty));
            TestAssert.Equal(0, IndoorCropReport.Build(record, FallbackCropCatalog.Default).Count);
        }

        public static void TestNullRecordOrSlotsReportsNoRows()
        {
            TestAssert.Equal(0, IndoorCropReport.Build(null, FallbackCropCatalog.Default).Count);
            TestAssert.Equal(0, IndoorCropReport.Build(new IndoorFarmRecord(), null).Count);
        }

        public static void TestNullCatalogStillReportsTheCropId()
        {
            var rows = IndoorCropReport.Build(
                Record(HdbfFaction.Folktails, Slot("Carrot", 1f, SlotStage.Growing)), null);
            TestAssert.Equal(1, rows.Count);
            TestAssert.Equal("Carrot", rows[0].CropId);
            TestAssert.Equal(string.Empty, rows[0].YieldItemId);
            TestAssert.Equal(0f, rows[0].GrowthTimeInDays);
            TestAssert.Equal(0f, rows[0].AverageProgress, "no growth time means no measurable progress");
        }

        // ---------------------------------------------------------------- counts and progress

        public static void TestGrowingAndMatureSlotsAreSplitAndAveraged()
        {
            var rows = IndoorCropReport.Build(
                Record(HdbfFaction.Folktails,
                    Slot("Carrot", 1f, SlotStage.Growing),
                    Slot("Carrot", 2f, SlotStage.Growing),
                    Slot("Carrot", 3f, SlotStage.Growing),
                    Slot("Carrot", 4f, SlotStage.Mature),
                    Slot("Carrot", 4f, SlotStage.Mature)),
                FallbackCropCatalog.Default);

            TestAssert.Equal(1, rows.Count);
            var carrot = rows[0];
            TestAssert.Equal("Carrot", carrot.CropId);
            TestAssert.Equal("Carrot", carrot.YieldItemId);
            TestAssert.Equal(3, carrot.GrowingSlots);
            TestAssert.Equal(2, carrot.MatureSlots);
            TestAssert.Equal(5, carrot.TotalSlots);
            TestAssert.Equal(4f, carrot.GrowthTimeInDays);
            // (0.25 + 0.50 + 0.75 + 1 + 1) / 5 = 0.70
            TestAssert.Equal(0.70f, carrot.AverageProgress, 1e-4f);
        }

        public static void TestAverageProgressNeverExceedsOne()
        {
            // A damaged save can hold GrowthDays above the growth time; the panel must never render
            // more than 100 % (REQ-V2-READ-04).
            var rows = IndoorCropReport.Build(
                Record(HdbfFaction.Folktails,
                    Slot("Carrot", 99f, SlotStage.Growing),
                    Slot("Carrot", 99f, SlotStage.Growing)),
                FallbackCropCatalog.Default);
            TestAssert.Equal(1f, rows[0].AverageProgress, 1e-6f);
        }

        public static void TestEmptyStageWithCropIdIsIgnored()
        {
            // Stage is the authority, not the crop id: an Empty slot never counts as growing, which
            // keeps the card consistent with the panel's slot counters.
            var rows = IndoorCropReport.Build(
                Record(HdbfFaction.Folktails,
                    new IndoorSlot { CropId = "Carrot", GrowthDays = 2f, Stage = SlotStage.Empty }),
                FallbackCropCatalog.Default);
            TestAssert.Equal(0, rows.Count);
        }

        public static void TestNonFiniteSaveValuesNeverLeakIntoTheRow()
        {
            var rows = IndoorCropReport.Build(
                Record(HdbfFaction.Folktails,
                    Slot("Carrot", float.NaN, SlotStage.Growing),
                    Slot("Carrot", float.PositiveInfinity, SlotStage.Growing),
                    Slot("Carrot", -3f, SlotStage.Growing)),
                FallbackCropCatalog.Default);

            TestAssert.Equal(1, rows.Count);
            TestAssert.Equal(3, rows[0].GrowingSlots);
            TestAssert.Equal(0f, rows[0].AverageProgress, 1e-6f, "invalid progress counts as 0");
            TestAssert.False(float.IsNaN(rows[0].AverageProgress) || float.IsInfinity(rows[0].AverageProgress),
                "the DTO must never carry NaN/Infinity to the panel");
        }

        public static void TestMatureSlotIsFullProgressEvenWithAnInvalidNumber()
        {
            var rows = IndoorCropReport.Build(
                Record(HdbfFaction.Folktails,
                    new IndoorSlot { CropId = "Carrot", GrowthDays = float.NaN, Stage = SlotStage.Mature }),
                FallbackCropCatalog.Default);
            TestAssert.Equal(1f, rows[0].AverageProgress, 1e-6f);
            TestAssert.Equal(1, rows[0].MatureSlots);
        }

        public static void TestSlotProgressIsRangeChecked()
        {
            var growing = Slot("Carrot", 2f, SlotStage.Growing);
            TestAssert.Equal(0.5f, IndoorCropReport.SlotProgress(growing, 4f), 1e-5f);
            TestAssert.Equal(0f, IndoorCropReport.SlotProgress(growing, 0f), 1e-5f, "no growth time -> no progress");
            TestAssert.Equal(0f, IndoorCropReport.SlotProgress(null, 4f));
            TestAssert.Equal(0f, IndoorCropReport.SlotProgress(Slot("Carrot", 2f, SlotStage.Empty), 4f));
        }

        // ---------------------------------------------------------------- ordering

        public static void TestRowsFollowTheFactionCropTableThenUnknownIds()
        {
            var rows = IndoorCropReport.Build(
                Record(HdbfFaction.Folktails,
                    Slot("Wheat", 2f, SlotStage.Growing),
                    Slot("Mystery", 1f, SlotStage.Growing),
                    Slot("Carrot", 1f, SlotStage.Growing)),
                FallbackCropCatalog.Default);

            // Carrot and Wheat come from the Folktails table in its shipped order; the unknown id is
            // appended ordinally so the same record always renders the same list.
            TestAssert.Equal("Carrot", rows[0].CropId);
            TestAssert.Equal("Wheat", rows[1].CropId);
            TestAssert.Equal("Mystery", rows[2].CropId);
        }

        public static void TestResultIsStableAcrossCallsAndCatalogInstances()
        {
            var record = Record(HdbfFaction.IronTeeth,
                Slot("Corn", 3f, SlotStage.Growing),
                Slot("Canola", 6f, SlotStage.Growing),
                Slot("Corn", 10f, SlotStage.Mature));

            var first = IndoorCropReport.Build(record, FallbackCropCatalog.Default);
            var second = IndoorCropReport.Build(record, FallbackCropCatalog.Default);
            TestAssert.Equal(first.Count, second.Count);
            for (var index = 0; index < first.Count; index++)
            {
                TestAssert.Equal(first[index].CropId, second[index].CropId, "row " + index + " crop id");
                TestAssert.Equal(first[index].GrowingSlots, second[index].GrowingSlots, "row " + index + " growing");
                TestAssert.Equal(first[index].MatureSlots, second[index].MatureSlots, "row " + index + " mature");
                TestAssert.Equal(first[index].AverageProgress, second[index].AverageProgress, 1e-6f,
                    "row " + index + " progress");
            }
        }

        // ---------------------------------------------------------------- defensive paths

        public static void TestRowCountIsCapped()
        {
            var catalog = new ListCatalog();
            var slots = new List<IndoorSlot>();
            for (var index = 0; index < IndoorCropReport.MaxCrops + 5; index++)
            {
                var id = "Crop" + index.ToString("00", System.Globalization.CultureInfo.InvariantCulture);
                catalog.Add(new CropDefinition(id, 4f, id, 1));
                slots.Add(Slot(id, 1f, SlotStage.Growing));
            }

            var rows = IndoorCropReport.Build(Record(HdbfFaction.Folktails, slots.ToArray()), catalog);
            TestAssert.Equal(IndoorCropReport.MaxCrops, rows.Count);
        }

        public static void TestThrowingCatalogDegradesInsteadOfThrowing()
        {
            var rows = IndoorCropReport.Build(
                Record(HdbfFaction.Folktails, Slot("Carrot", 1f, SlotStage.Growing)), new ThrowingCatalog());
            TestAssert.Equal(1, rows.Count);
            TestAssert.Equal("Carrot", rows[0].CropId);
        }

        // ---------------------------------------------------------------- helpers

        private static IndoorSlot Slot(string cropId, float growthDays, SlotStage stage)
        {
            return new IndoorSlot { CropId = cropId, GrowthDays = growthDays, Stage = stage };
        }

        private static IndoorFarmRecord Record(HdbfFaction faction, params IndoorSlot[] slots)
        {
            return new IndoorFarmRecord { Tier = HdbfTier.Basic, Faction = faction, Slots = slots };
        }

        private sealed class ListCatalog : ICropCatalog
        {
            private readonly List<CropDefinition> _crops = new();

            public void Add(CropDefinition crop) => _crops.Add(crop);

            public IReadOnlyList<CropDefinition> ForFaction(HdbfFaction faction) => _crops;

            public bool TryGet(HdbfFaction faction, string cropId, out CropDefinition crop)
            {
                for (var index = 0; index < _crops.Count; index++)
                {
                    if (string.Equals(_crops[index].Id, cropId, StringComparison.Ordinal))
                    {
                        crop = _crops[index];
                        return true;
                    }
                }

                crop = null!;
                return false;
            }
        }

        private sealed class ThrowingCatalog : ICropCatalog
        {
            public IReadOnlyList<CropDefinition> ForFaction(HdbfFaction faction)
                => throw new InvalidOperationException("broken spec reader");

            public bool TryGet(HdbfFaction faction, string cropId, out CropDefinition crop)
                => throw new InvalidOperationException("broken spec reader");
        }
    }
}
