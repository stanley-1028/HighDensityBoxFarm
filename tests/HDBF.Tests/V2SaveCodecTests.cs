// ============================================================================
// HDBF v2 | Layer: L1 test | Owner: V2-CORE-02
// Purpose: Schema-v2 codec tests: byte-stable round trips, fixed field order, tolerant
//          decoding of missing/bad/unknown fields, empty and malformed text, truncation,
//          slot-array bounds, non-finite floats and MeasureBytes (REQ-V2-SAVE-01/06).
// Requirements: REQ-V2-SAVE-01, REQ-V2-SAVE-06, REQ-V2-SAVE-07
// ============================================================================
using System;
using System.Text;
using HDBF.Core;
using HDBF.Core.Indoor;

namespace HDBF.Tests
{
    public static class V2SaveCodecTests
    {
        private static readonly string[] FieldOrder =
        {
            "schemaVersion", "entityId", "tier", "faction", "mode", "cropPolicy", "fixedCrop", "tank",
            "maintenance", "power", "ports", "adapters", "crops", "slots",
        };

        public static void TestEncodeIsDeterministicAcrossThreeRoundTrips()
        {
            var record = V2SaveFixtures.RichRecord(HdbfTier.Top);
            var first = IndoorSaveCodec.Encode(record);
            var current = V2SaveFixtures.DecodeStrict(first);
            TestAssert.Equal(first, IndoorSaveCodec.Encode(current), "round-trip pass 1");
            for (var pass = 2; pass <= 3; pass++)
            {
                current = V2SaveFixtures.DecodeStrict(IndoorSaveCodec.Encode(current));
                TestAssert.Equal(first, IndoorSaveCodec.Encode(current), "round-trip pass " + pass);
            }

            V2SaveFixtures.AssertRecordsEqual(record, current, "three-pass round trip");
        }

        public static void TestEncodedFieldOrderIsFixed()
        {
            var text = IndoorSaveCodec.Encode(V2SaveFixtures.RichRecord(HdbfTier.Basic));
            var cursor = -1;
            foreach (var name in FieldOrder)
            {
                var position = text.IndexOf("\"" + name + "\"", cursor + 1, StringComparison.Ordinal);
                TestAssert.True(position > cursor, "field out of order or missing: " + name);
                cursor = position;
            }

            TestAssert.True(text.StartsWith("{\"schemaVersion\":2,", StringComparison.Ordinal),
                "header must start with the schema version: " + text.Substring(0, 40));
            TestAssert.False(text.Contains("timestamp", StringComparison.Ordinal), "no timestamps in the save");
        }

        public static void TestRoundTripPreservesEveryField()
        {
            var record = V2SaveFixtures.RichRecord(HdbfTier.Advanced, HdbfFaction.IronTeeth);
            record.Mode = IndoorMode.Dense;
            record.CropPolicy = CropPolicy.Fixed;
            record.FixedCropId = "Cassava";
            record.MaintenanceCyclesMissed = 2;
            record.PipePort = false;
            record.LogisticsPort = true;
            record.TimberPipesAdapter = IndoorSaveLimits.AdapterStateDegraded;

            var decoded = V2SaveFixtures.DecodeStrict(IndoorSaveCodec.Encode(record));

            V2SaveFixtures.AssertRecordsEqual(record, decoded, "field round trip");
            TestAssert.Equal(CropPolicy.Fixed, decoded.CropPolicy);
            TestAssert.Equal("Cassava", decoded.FixedCropId);
            TestAssert.Equal(IndoorSaveLimits.AdapterStateDegraded, decoded.TimberPipesAdapter);
        }

        public static void TestEncodeWritesOneSlotRowPerCapacity()
        {
            foreach (var tier in new[] { HdbfTier.Basic, HdbfTier.Top })
            {
                var decoded = V2SaveFixtures.DecodeStrict(
                    IndoorSaveCodec.Encode(V2SaveFixtures.RichRecord(tier, populate: false)));
                TestAssert.Equal(V2SaveFixtures.Profile(tier).SlotCapacity, decoded.Slots.Length, tier + ": slot rows");
                for (var index = 0; index < decoded.Slots.Length; index++)
                {
                    TestAssert.Equal(string.Empty, decoded.Slots[index].CropId, tier + ": row " + index);
                }
            }
        }

        public static void TestDecodeMissingFieldsAppliesDocumentedDefaults()
        {
            var ok = IndoorSaveCodec.TryDecode("{\"schemaVersion\":2}", out var record, out var error);

            TestAssert.True(ok, "a minimal object must still decode: " + error);
            TestAssert.Equal(HdbfTier.Basic, record.Tier);
            TestAssert.Equal(IndoorMode.Normal, record.Mode);
            TestAssert.Equal(CropPolicy.Rotation, record.CropPolicy);
            TestAssert.Equal(0, record.Slots.Length);
            TestAssert.Equal(0f, record.Tank);
            TestAssert.Equal(IndoorSaveLimits.AdapterStateNone, record.TimberPipesAdapter);
            TestAssert.True(error.Length > 0, "defaulted fields must be recorded");
            TestAssert.True(error.Contains("tank", StringComparison.Ordinal), "recorded tank default: " + error);
        }

        public static void TestDecodeBadTypesAreIgnoredAndRecorded()
        {
            var record = V2SaveFixtures.RichRecord(HdbfTier.Basic);
            record.Tank = 1.8f;
            var text = IndoorSaveCodec.Encode(record)
                .Replace("\"tank\":1.8", "\"tank\":\"oops\"", StringComparison.Ordinal)
                .Replace("\"pipe\":true", "\"pipe\":\"yes\"", StringComparison.Ordinal);

            var ok = IndoorSaveCodec.TryDecode(text, out var decoded, out var error);

            TestAssert.True(ok, "wrong-typed fields must not fail the decode: " + error);
            TestAssert.Equal(0f, decoded.Tank, 0f, "wrong-typed tank defaults to 0");
            TestAssert.False(decoded.PipePort, "wrong-typed port defaults to false");
            TestAssert.True(error.Contains("tank", StringComparison.Ordinal), "tank recorded: " + error);
            TestAssert.True(error.Contains("ports.pipe", StringComparison.Ordinal), "port recorded: " + error);
        }

        public static void TestDecodeUnknownFieldsAreIgnored()
        {
            var record = V2SaveFixtures.RichRecord(HdbfTier.Intermediate);
            var text = IndoorSaveCodec.Encode(record)
                .Insert(1, "\"futureField\":{\"nested\":[1,2,3]},\"secondFuture\":null,");

            var ok = IndoorSaveCodec.TryDecode(text, out var decoded, out var error);

            TestAssert.True(ok, "unknown fields are forward compatibility, not an error: " + error);
            TestAssert.Equal(string.Empty, error);
            V2SaveFixtures.AssertRecordsEqual(record, decoded, "unknown-field decode");
        }

        public static void TestDecodeEmptyOrMalformedTextDoesNotThrow()
        {
            var inputs = new[] { null!, string.Empty, "   ", "garbage", "[1,2,3]", "{\"slots\":[", "{\"tank\":}" };
            foreach (var input in inputs)
            {
                var ok = IndoorSaveCodec.TryDecode(input, out var record, out var error);
                TestAssert.False(ok, "unusable text must be rejected: " + (input ?? "<null>"));
                TestAssert.True(error.Length > 0, "rejection must carry a reason");
            }
        }

        public static void TestDecodeTruncatedTextNeverThrows()
        {
            var text = IndoorSaveCodec.Encode(V2SaveFixtures.RichRecord(HdbfTier.Basic));
            for (var cut = 1; cut < text.Length; cut += 13)
            {
                var prefix = text.Substring(0, cut);
                var ok = IndoorSaveCodec.TryDecode(prefix, out var record, out _);
                if (!ok)
                {
                    continue;
                }

                var expected = V2SaveFixtures.Profile(record.Tier);
                V2SaveFixtures.Repair(record, expected);
                V2SaveFixtures.AssertInvariants(record, expected, "truncated prefix " + cut);
            }
        }

        public static void TestDecodeBoundsSlotsByTierCapacity()
        {
            var topText = IndoorSaveCodec.Encode(V2SaveFixtures.RichRecord(HdbfTier.Top));
            var basicText = topText.Replace("\"tier\":\"Top\"", "\"tier\":\"Basic\"", StringComparison.Ordinal);
            TestAssert.False(string.Equals(topText, basicText, StringComparison.Ordinal), "tier text must be replaced");

            var ok = IndoorSaveCodec.TryDecode(basicText, out var record, out var error);

            TestAssert.True(ok, error);
            TestAssert.Equal(HdbfTier.Basic, record.Tier);
            TestAssert.Equal(V2SaveFixtures.Basic.SlotCapacity, record.Slots.Length, "decode must bound by capacity");
            TestAssert.True(error.Contains("slots-bounded", StringComparison.Ordinal), "bound recorded: " + error);
        }

        public static void TestDecodeCorruptSlotRowsFallBackToEmpty()
        {
            var valid = IndoorSaveCodec.Encode(V2SaveFixtures.RichRecord(HdbfTier.Basic, populate: false))
                .Replace("\"crops\":[]", "\"crops\":[\"Carrot\"]", StringComparison.Ordinal);
            var slotsIndex = valid.IndexOf("\"slots\":[", StringComparison.Ordinal);
            TestAssert.True(slotsIndex > 0, "encoded record must expose the slots member");
            var text = valid.Substring(0, slotsIndex)
                + "\"slots\":[{},[1,1.5,0.2,0,1,0],[9,0,0,0,1,0],[1,0,0,0,9,300]]}";

            var ok = IndoorSaveCodec.TryDecode(text, out var record, out var error);

            TestAssert.True(ok, error);
            TestAssert.Equal(4, record.Slots.Length);
            TestAssert.Equal(string.Empty, record.Slots[0].CropId, "non-array row is an empty slot");
            TestAssert.Equal("Carrot", record.Slots[1].CropId);
            TestAssert.Equal(SlotStage.Growing, record.Slots[1].Stage);
            TestAssert.Equal(string.Empty, record.Slots[2].CropId, "unknown crop code is an empty slot");
            TestAssert.Equal(0, record.Slots[3].Flags, "out-of-byte flags fall back to 0");
            TestAssert.True(error.Contains("slot[", StringComparison.Ordinal), "slot repairs recorded: " + error);

            V2SaveFixtures.Repair(record, V2SaveFixtures.Basic);
            V2SaveFixtures.AssertInvariants(record, V2SaveFixtures.Basic, "corrupt rows repaired");
        }

        public static void TestMeasureBytesMatchesEncodedUtf8Size()
        {
            var record = V2SaveFixtures.RichRecord(HdbfTier.Top);
            var expected = Encoding.UTF8.GetByteCount(IndoorSaveCodec.Encode(record));

            TestAssert.Equal(expected, IndoorSaveCodec.MeasureBytes(record));
            TestAssert.True(expected > 0);
            TestAssert.Equal(0, IndoorSaveCodec.MeasureBytes(null!));
            TestAssert.Equal(string.Empty, IndoorSaveCodec.Encode(null!));
        }

        public static void TestEncodeNonFiniteFloatsStaysDeterministic()
        {
            var record = V2SaveFixtures.RichRecord(HdbfTier.Basic);
            record.Tank = float.NaN;
            record.Slots[0].GrowthDays = float.PositiveInfinity;
            record.Slots[1].Fatigue = float.NegativeInfinity;

            var first = IndoorSaveCodec.Encode(record);
            TestAssert.Equal(first, IndoorSaveCodec.Encode(record), "encode must be deterministic for damaged input");
            TestAssert.False(first.Contains("NaN", StringComparison.Ordinal), "NaN may never reach the save text");
            TestAssert.False(first.Contains("Infinity", StringComparison.Ordinal), "Infinity may never reach the save text");

            var decoded = V2SaveFixtures.DecodeStrict(first);
            TestAssert.Equal(0f, decoded.Tank, 0f);
            TestAssert.Equal(0f, decoded.Slots[0].GrowthDays, 0f);

            V2SaveFixtures.Repair(decoded, V2SaveFixtures.Basic);
            V2SaveFixtures.AssertInvariants(decoded, V2SaveFixtures.Basic, "non-finite repaired");
        }
    }
}
