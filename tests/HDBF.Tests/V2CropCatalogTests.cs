// ============================================================================
// HDBF v2 | Layer: L1 test | Owner: V2-CORE-01
// Purpose: The built-in fallback crop table and the primary-catalog-first resolver
//          (INTERFACES section 7, REQ-V2-EXT-01 / REQ-V2-CORE-05).
// Requirements: REQ-V2-EXT-01, REQ-V2-CORE-05, REQ-V2-TEST-01
// ============================================================================
using System;
using System.Collections.Generic;
using HDBF.Core;
using HDBF.Core.Indoor;

namespace HDBF.Tests
{
    public static class V2CropCatalogTests
    {
        public static void TestFallbackTableHasTenCrops()
        {
            var catalog = FallbackCropCatalog.Default;
            TestAssert.Equal(4, catalog.ForFaction(HdbfFaction.Folktails).Count);
            TestAssert.Equal(6, catalog.ForFaction(HdbfFaction.IronTeeth).Count);
        }

        public static void TestFolktailsCropTable()
        {
            AssertCrop(HdbfFaction.Folktails, "Carrot", 4f, "Carrot", 3);
            AssertCrop(HdbfFaction.Folktails, "Potato", 6f, "Potato", 1);
            AssertCrop(HdbfFaction.Folktails, "Wheat", 10f, "Wheat", 3);
            AssertCrop(HdbfFaction.Folktails, "Sunflower", 5f, "SunflowerSeeds", 2);
        }

        public static void TestIronTeethCropTable()
        {
            AssertCrop(HdbfFaction.IronTeeth, "Canola", 9f, "CanolaSeeds", 3);
            AssertCrop(HdbfFaction.IronTeeth, "Cassava", 5f, "Cassava", 1);
            AssertCrop(HdbfFaction.IronTeeth, "Corn", 10f, "Corn", 2);
            AssertCrop(HdbfFaction.IronTeeth, "Eggplant", 12f, "Eggplant", 3);
            AssertCrop(HdbfFaction.IronTeeth, "Kohlrabi", 3f, "Kohlrabi", 2);
            AssertCrop(HdbfFaction.IronTeeth, "Soybean", 8f, "Soybean", 2);
        }

        public static void TestLookupIsFactionScoped()
        {
            var catalog = FallbackCropCatalog.Default;
            TestAssert.True(catalog.TryGet(HdbfFaction.Folktails, "Carrot", out var carrot));
            TestAssert.Equal("Carrot", carrot.Id);
            TestAssert.False(catalog.TryGet(HdbfFaction.IronTeeth, "Carrot", out _));
            TestAssert.True(catalog.TryGet(HdbfFaction.IronTeeth, "Soybean", out _));
            TestAssert.False(catalog.TryGet(HdbfFaction.Folktails, "Soybean", out _));
        }

        public static void TestLookupRejectsUnknownAndNullIds()
        {
            var catalog = FallbackCropCatalog.Default;
            TestAssert.False(catalog.TryGet(HdbfFaction.Folktails, "Bogus", out _));
            TestAssert.False(catalog.TryGet(HdbfFaction.Folktails, string.Empty, out _));
            TestAssert.False(catalog.TryGet(HdbfFaction.Folktails, null!, out _));
        }

        public static void TestRotationOrderIsStable()
        {
            var catalog = FallbackCropCatalog.Default;
            var first = catalog.ForFaction(HdbfFaction.Folktails);
            var second = catalog.ForFaction(HdbfFaction.Folktails);
            TestAssert.True(ReferenceEquals(first, second), "the cached read-only table is returned");
            TestAssert.Equal("Carrot", first[0].Id);
            TestAssert.Equal("Potato", first[1].Id);
            TestAssert.Equal("Wheat", first[2].Id);
            TestAssert.Equal("Sunflower", first[3].Id);
        }

        public static void TestUnknownFactionReturnsEmptyTable()
        {
            TestAssert.Equal(0, FallbackCropCatalog.Default.ForFaction((HdbfFaction)9).Count);
        }

        public static void TestTryResolvePrefersPrimaryCatalog()
        {
            var custom = new FixedCatalog(new CropDefinition("Carrot", 99f, "Carrot", 9));
            TestAssert.True(FallbackCropCatalog.TryResolve(custom, HdbfFaction.Folktails, "Carrot", out var crop));
            TestAssert.Equal(99f, crop.GrowthTimeInDays);
            TestAssert.Equal(9, crop.BaseYield);
        }

        public static void TestTryResolveFallsBackWhenPrimaryMisses()
        {
            var empty = new EmptyCatalog();
            TestAssert.True(FallbackCropCatalog.TryResolve(empty, HdbfFaction.Folktails, "Carrot", out var crop));
            TestAssert.Equal(4f, crop.GrowthTimeInDays);
        }

        public static void TestTryResolveNullPrimaryUsesFallback()
        {
            TestAssert.True(FallbackCropCatalog.TryResolve(null, HdbfFaction.IronTeeth, "Corn", out var crop));
            TestAssert.Equal(10f, crop.GrowthTimeInDays);
        }

        public static void TestTryResolveRejectsEmptyId()
        {
            TestAssert.False(FallbackCropCatalog.TryResolve(null, HdbfFaction.Folktails, string.Empty, out _));
            TestAssert.False(FallbackCropCatalog.TryResolve(null, HdbfFaction.Folktails, null, out _));
        }

        public static void TestTryResolveSurvivesThrowingPrimary()
        {
            TestAssert.True(FallbackCropCatalog.TryResolve(new ThrowingCatalog(), HdbfFaction.Folktails, "Wheat", out var crop));
            TestAssert.Equal(10f, crop.GrowthTimeInDays);
        }

        public static void TestCropDefinitionRecordSemantics()
        {
            var left = new CropDefinition("Carrot", 4f, "Carrot", 3);
            var right = new CropDefinition("Carrot", 4f, "Carrot", 3);
            var other = new CropDefinition("Carrot", 4f, "Carrot", 4);
            TestAssert.True(left.Equals(right));
            TestAssert.False(left.Equals(other));
            TestAssert.Equal("Carrot", left.Id);
        }

        public static void TestAllFallbackValuesArePositive()
        {
            foreach (var faction in new[] { HdbfFaction.Folktails, HdbfFaction.IronTeeth })
            {
                var crops = FallbackCropCatalog.Default.ForFaction(faction);
                for (var index = 0; index < crops.Count; index++)
                {
                    var crop = crops[index];
                    TestAssert.NotNull(crop);
                    TestAssert.True(!string.IsNullOrEmpty(crop.Id), "crop id");
                    TestAssert.True(!string.IsNullOrEmpty(crop.YieldItemId), "yield item id");
                    TestAssert.True(crop.GrowthTimeInDays > 0f, "growth time of " + crop.Id);
                    TestAssert.True(crop.BaseYield > 0, "base yield of " + crop.Id);
                }
            }
        }

        public static void TestDefaultInstanceIsSingular()
        {
            TestAssert.True(ReferenceEquals(FallbackCropCatalog.Default, FallbackCropCatalog.Default));
        }

        // ---------------------------------------------------------------- helpers

        private sealed class EmptyCatalog : ICropCatalog
        {
            public IReadOnlyList<CropDefinition> ForFaction(HdbfFaction faction) => new List<CropDefinition>();
            public bool TryGet(HdbfFaction faction, string cropId, out CropDefinition crop)
            {
                crop = null!;
                return false;
            }
        }

        private sealed class FixedCatalog : ICropCatalog
        {
            private readonly CropDefinition _crop;
            public FixedCatalog(CropDefinition crop) => _crop = crop;
            public IReadOnlyList<CropDefinition> ForFaction(HdbfFaction faction) => new[] { _crop };
            public bool TryGet(HdbfFaction faction, string cropId, out CropDefinition crop)
            {
                crop = _crop;
                return true;
            }
        }

        private sealed class ThrowingCatalog : ICropCatalog
        {
            public IReadOnlyList<CropDefinition> ForFaction(HdbfFaction faction)
                => throw new InvalidOperationException("catalog failed");
            public bool TryGet(HdbfFaction faction, string cropId, out CropDefinition crop)
                => throw new InvalidOperationException("catalog failed");
        }

        private static void AssertCrop(HdbfFaction faction, string id, float growthDays, string item, int yield)
        {
            TestAssert.True(FallbackCropCatalog.Default.TryGet(faction, id, out var crop), "missing crop " + id);
            TestAssert.Equal(id, crop.Id);
            TestAssert.Equal(growthDays, crop.GrowthTimeInDays, 1e-6f, "growth days of " + id);
            TestAssert.Equal(item, crop.YieldItemId);
            TestAssert.Equal(yield, crop.BaseYield, "base yield of " + id);
        }
    }
}
