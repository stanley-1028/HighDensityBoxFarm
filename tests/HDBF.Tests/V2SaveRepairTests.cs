// ============================================================================
// HDBF v2 | Layer: L1 test | Owner: V2-CORE-02
// Purpose: Read-repair tests of REQ-V2-CORE-02: slot array trimming/padding to Capacity,
//          locatable repair summary, idempotent repair and storage-key reconciliation.
//          Split from V2SaveValidatorTests to keep each file inside the 300-line limit.
// Requirements: REQ-V2-CORE-02, REQ-V2-SAVE-04, REQ-V2-SAVE-06
// ============================================================================
using System;
using HDBF.Core;
using HDBF.Core.Indoor;

namespace HDBF.Tests
{
    public static class V2SaveRepairTests
    {
        public static void TestSlotArrayIsPaddedToCapacity()
        {
            var record = V2SaveFixtures.RichRecord(HdbfTier.Basic);
            record.Slots = new[] { record.Slots[0], record.Slots[1] };
            var result = V2SaveFixtures.Repair(record, V2SaveFixtures.Basic);
            TestAssert.True(result.Repaired);
            TestAssert.Equal(58, result.SlotsPadded);
            TestAssert.Equal(60, record.Slots.Length);
            TestAssert.True(V2SaveFixtures.HasNote(result, "slots-padded"),
                "note: " + string.Join("|", result.Notes));
            TestAssert.Equal("Carrot", record.Slots[0].CropId, "surviving rows keep their data");
        }

        public static void TestSlotArrayIsTrimmedToCapacity()
        {
            var record = V2SaveFixtures.RichRecord(HdbfTier.Basic, populate: false);
            var longSlots = new IndoorSlot[100];
            for (var index = 0; index < longSlots.Length; index++)
            {
                longSlots[index] = new IndoorSlot();
            }

            record.Slots = longSlots;
            var result = V2SaveFixtures.Repair(record, V2SaveFixtures.Basic);
            TestAssert.True(result.Repaired);
            TestAssert.Equal(40, result.SlotsTrimmed);
            TestAssert.Equal(60, record.Slots.Length);
            TestAssert.True(V2SaveFixtures.HasNote(result, "slots-trimmed"),
                "note: " + string.Join("|", result.Notes));
        }

        public static void TestNullSlotArrayIsPaddedWithEmptySlots()
        {
            var record = V2SaveFixtures.RichRecord(HdbfTier.Basic);
            record.Slots = null!;
            var result = V2SaveFixtures.Repair(record, V2SaveFixtures.Basic);
            TestAssert.True(result.Repaired);
            TestAssert.Equal(60, result.SlotsPadded);
            TestAssert.Equal(60, record.Slots.Length);
            TestAssert.Equal(string.Empty, record.Slots[0].CropId);
            TestAssert.True(V2SaveFixtures.HasNote(result, "slots-missing"),
                "note: " + string.Join("|", result.Notes));
        }

        public static void TestRepairSummaryIsLocatable()
        {
            var record = V2SaveFixtures.RichRecord(HdbfTier.Basic);
            record.Tank = float.NaN;
            record.Mode = (IndoorMode)9;
            record.Slots[7].CropId = "Durian";
            var result = V2SaveFixtures.Repair(record, V2SaveFixtures.Basic);
            TestAssert.True(result.Repaired);
            TestAssert.True(result.ValuesClamped >= 3, "every repair is counted: " + result);
            TestAssert.True(V2SaveFixtures.HasNote(result, "entity:entity-1"),
                "entity is named: " + string.Join("|", result.Notes));
            TestAssert.True(V2SaveFixtures.HasNote(result, "slot[7]"),
                "slot index is named: " + string.Join("|", result.Notes));
            TestAssert.True(V2SaveFixtures.HasNote(result, "tank"), "field is named");
            TestAssert.True(V2SaveFixtures.HasNote(result, "mode"), "field is named");
            TestAssert.True(result.ToString().Contains("clamped=", StringComparison.Ordinal));
        }

        public static void TestRepairIsIdempotent()
        {
            var record = V2SaveFixtures.RichRecord(HdbfTier.Advanced);
            record.Tank = float.NaN;
            record.Slots[3].Fatigue = 7f;
            record.Slots[9].CropId = "Durian";
            record.Mode = (IndoorMode)5;
            record.TimberPipesAdapter = "Exploded";
            var first = V2SaveFixtures.Repair(record, V2SaveFixtures.Advanced);
            TestAssert.True(first.Repaired);
            var text = IndoorSaveCodec.Encode(record);
            var second = IndoorSaveValidator.ValidateAndRepair(record, V2SaveFixtures.Advanced,
                FallbackCropCatalog.Default, null);
            TestAssert.False(second.Repaired, "a repaired record must be clean on the second pass: " + second);
            TestAssert.Equal(text, IndoorSaveCodec.Encode(record), "repair must not drift the record");
        }

        public static void TestExpectedEntityIdIsApplied()
        {
            var record = V2SaveFixtures.RichRecord(HdbfTier.Basic);
            record.EntityId = string.Empty;
            IndoorSaveValidator.ValidateAndRepair(record, V2SaveFixtures.Basic, FallbackCropCatalog.Default, "key-9");
            TestAssert.Equal("key-9", record.EntityId, "missing id is filled from the storage key");

            record = V2SaveFixtures.RichRecord(HdbfTier.Basic);
            record.EntityId = "other";
            var result = IndoorSaveValidator.ValidateAndRepair(record, V2SaveFixtures.Basic,
                FallbackCropCatalog.Default, "key-9");
            TestAssert.Equal("key-9", record.EntityId, "mismatched id is reconciled with the storage key");
            TestAssert.True(V2SaveFixtures.HasNote(result, "entityId-mismatch"),
                "note: " + string.Join("|", result.Notes));
        }
    }
}
