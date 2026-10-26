// ============================================================================
// HDBF v2 | Layer: L1 test | Owner: V2-CORE-02
// Purpose: Save fault injection: 200-round deterministic corruption sweep (same seed,
//          same result), mutated/truncated text, random record damage, single-building
//          isolation, oversized input and a bounded repair report (section 19.18.3-A7).
// Requirements: REQ-V2-SAVE-06, REQ-V2-SAVE-07, REQ-V2-SAFE-01, REQ-V2-SAFE-08
// ============================================================================
using System;
using System.Collections.Generic;
using System.Text;
using HDBF.Core;
using HDBF.Core.Balance;
using HDBF.Core.Indoor;

namespace HDBF.Tests
{
    public static class V2SaveFaultInjectionTests
    {
        public static void TestTwoHundredRoundDeterministicCorruptionSweep()
        {
            var first = Sweep(0xC0FFEEu);
            var second = Sweep(0xC0FFEEu);

            TestAssert.Equal(200, first.Count);
            TestAssert.Equal(first.Count, second.Count);
            for (var iteration = 0; iteration < first.Count; iteration++)
            {
                TestAssert.Equal(first[iteration], second[iteration],
                    "same seed must produce the same repair result at iteration " + iteration);
            }
        }

        public static void TestCorruptedEncodedTextNeverThrows()
        {
            var text = IndoorSaveCodec.Encode(V2SaveFixtures.RichRecord(HdbfTier.Basic));
            var state = 0x5EEDu;
            for (var iteration = 0; iteration < 80; iteration++)
            {
                var mutated = Mutate(text, ref state);
                var ok = IndoorSaveCodec.TryDecode(mutated, out var record, out var error);
                if (!ok)
                {
                    TestAssert.True(error.Length > 0, "rejection " + iteration + " must carry a reason");
                    continue;
                }

                var tier = Enum.IsDefined(typeof(HdbfTier), record.Tier) ? record.Tier : HdbfTier.Basic;
                var profile = V2SaveFixtures.Profile(tier);
                V2SaveFixtures.Repair(record, profile);
                V2SaveFixtures.AssertInvariants(record, profile, "mutated text " + iteration);
            }
        }

        public static void TestRandomRecordDamageAlwaysRepairsToValidRecord()
        {
            var state = 0xBADF00Du;
            for (var iteration = 0; iteration < 120; iteration++)
            {
                var tier = (HdbfTier)V2SaveFixtures.Pick(ref state, 4);
                var profile = V2SaveFixtures.Profile(tier);
                var record = V2SaveFixtures.RichRecord(tier);
                Damage(record, ref state);

                var result = V2SaveFixtures.Repair(record, profile);

                var second = IndoorSaveValidator.ValidateAndRepair(record, profile,
                    FallbackCropCatalog.Default, null);
                TestAssert.False(second.Repaired,
                    "iteration " + iteration + " repair is not idempotent: " + second + " (first " + result + ")");
            }
        }

        public static void TestDamagedBuildingDoesNotAffectHealthyBuilding()
        {
            var healthy = V2SaveFixtures.RichRecord(HdbfTier.Basic);
            var healthyText = IndoorSaveCodec.Encode(healthy);
            var damaged = V2SaveFixtures.RichRecord(HdbfTier.Basic);
            damaged.Slots = null!;
            damaged.Tank = float.NaN;
            damaged.Mode = (IndoorMode)77;

            var damagedResult = V2SaveFixtures.Repair(damaged, V2SaveFixtures.Basic);
            var healthyResult = IndoorSaveValidator.ValidateAndRepair(healthy, V2SaveFixtures.Basic,
                FallbackCropCatalog.Default, healthy.EntityId);

            TestAssert.True(damagedResult.Repaired);
            TestAssert.False(healthyResult.Repaired, "the healthy building stays clean: " + healthyResult);
            TestAssert.Equal(healthyText, IndoorSaveCodec.Encode(healthy), "repairing one building changed another");
        }

        public static void TestOversizedAndNullTextIsRejectedWithoutThrowing()
        {
            var oversized = new string('x', IndoorSaveLimits.MaxDecodedTextChars + 1);
            var rejected = IndoorSaveCodec.TryDecode(oversized, out _, out var error);
            TestAssert.False(rejected);
            TestAssert.Equal("too-large", error);

            TestAssert.False(IndoorSaveCodec.TryDecode(null!, out _, out var nullError));
            TestAssert.True(nullError.Length > 0);
        }

        public static void TestTruncatedTextNeverProducesAnInvalidRecord()
        {
            var text = IndoorSaveCodec.Encode(V2SaveFixtures.RichRecord(HdbfTier.Basic));
            for (var cut = 1; cut < text.Length; cut += 11)
            {
                if (!IndoorSaveCodec.TryDecode(text.Substring(0, cut), out var record, out _))
                {
                    continue;
                }

                var tier = Enum.IsDefined(typeof(HdbfTier), record.Tier) ? record.Tier : HdbfTier.Basic;
                var profile = V2SaveFixtures.Profile(tier);
                V2SaveFixtures.Repair(record, profile);
                V2SaveFixtures.AssertInvariants(record, profile, "truncated text " + cut);
            }
        }

        public static void TestRepairReportIsBoundedForAFullyCorruptRecord()
        {
            var record = V2SaveFixtures.RichRecord(HdbfTier.Top);
            for (var index = 0; index < record.Slots.Length; index++)
            {
                var slot = record.Slots[index];
                slot.CropId = "UnknownCrop" + index;
                slot.LastCropId = "AlsoUnknown";
                slot.GrowthDays = float.NaN;
                slot.Fatigue = 9f;
                slot.Stage = (SlotStage)42;
                slot.Flags = 0xFF;
            }

            var result = V2SaveFixtures.Repair(record, V2SaveFixtures.Top);

            TestAssert.True(result.Repaired);
            TestAssert.True(result.ValuesClamped > 0);
            TestAssert.True(result.Notes.Count <= IndoorSaveLimits.MaxRepairNotes + 2,
                "note list must stay bounded, got " + result.Notes.Count);
            TestAssert.True(result.ToString().Length <= IndoorSaveLimits.MaxDiagnosticLength * 2);
        }

        // ---------------------------------------------------------------- deterministic damage

        private static List<string> Sweep(uint seed)
        {
            var results = new List<string>(200);
            var state = seed;
            for (var iteration = 0; iteration < 200; iteration++)
            {
                var tier = (HdbfTier)V2SaveFixtures.Pick(ref state, 4);
                var profile = V2SaveFixtures.Profile(tier);
                var record = V2SaveFixtures.RichRecord(tier);
                Damage(record, ref state);

                var text = IndoorSaveCodec.Encode(record);
                var ok = IndoorSaveCodec.TryDecode(text, out var decoded, out var error);
                var summary = new StringBuilder();
                summary.Append(ok ? 'T' : 'F').Append('|').Append(error).Append('|');
                if (ok)
                {
                    var repair = IndoorSaveValidator.ValidateAndRepair(decoded, profile,
                        FallbackCropCatalog.Default, null);
                    summary.Append(repair.Repaired ? '1' : '0').Append('|')
                        .Append(repair.SlotsTrimmed).Append('|')
                        .Append(repair.SlotsPadded).Append('|')
                        .Append(repair.ValuesClamped).Append('|')
                        .Append(IndoorSaveCodec.Encode(decoded));
                    V2SaveFixtures.AssertInvariants(decoded, profile, "sweep iteration " + iteration);
                }

                results.Add(summary.ToString());
            }

            return results;
        }

        private static void Damage(IndoorFarmRecord record, ref uint state)
        {
            switch (V2SaveFixtures.Pick(ref state, 12))
            {
                case 0:
                    record.Slots = null!;
                    break;
                case 1:
                    record.Slots = Truncate(record.Slots, V2SaveFixtures.Pick(ref state, record.Slots.Length + 1));
                    break;
                case 2:
                    record.Slots = Pad(record.Slots, record.Slots.Length + 1 + V2SaveFixtures.Pick(ref state, 20));
                    break;
                case 3:
                    record.Tier = (HdbfTier)V2SaveFixtures.Pick(ref state, 9);
                    record.Faction = (HdbfFaction)V2SaveFixtures.Pick(ref state, 5);
                    record.Mode = (IndoorMode)V2SaveFixtures.Pick(ref state, 7);
                    record.CropPolicy = (CropPolicy)V2SaveFixtures.Pick(ref state, 5);
                    break;
                case 4:
                    record.Tank = V2SaveFixtures.PickFloat(ref state);
                    break;
                case 5:
                    record.MaintenanceDueDay = V2SaveFixtures.PickFloat(ref state);
                    break;
                case 6:
                    record.MaintenanceCyclesMissed = V2SaveFixtures.Pick(ref state, 5000) - 50;
                    break;
                case 7:
                    DamageSlot(record.Slots, ref state);
                    break;
                case 8:
                    record.CropPolicy = CropPolicy.Fixed;
                    record.FixedCropId = Pick(ref state, 2) == 0 ? "Durian" : string.Empty;
                    break;
                case 9:
                    record.TimberPipesAdapter = Pick(ref state, 3) == 0 ? "Exploded" : string.Empty;
                    break;
                case 10:
                    record.EntityId = new string('e', 100 + V2SaveFixtures.Pick(ref state, 100));
                    break;
                default:
                    record.PowerConnected = Pick(ref state, 2) == 0;
                    record.PowerMode = (PowerConnection)V2SaveFixtures.Pick(ref state, 5);
                    record.PipePort = Pick(ref state, 2) == 0;
                    record.LogisticsPort = Pick(ref state, 2) == 0;
                    record.PowerPort = Pick(ref state, 2) == 0;
                    break;
            }
        }

        private static void DamageSlot(IndoorSlot[]? slots, ref uint state)
        {
            if (slots is null || slots.Length == 0)
            {
                return;
            }

            var index = V2SaveFixtures.Pick(ref state, slots.Length);
            var slot = slots[index];
            slot.CropId = Pick(ref state, 2) == 0 ? "Unknown" : string.Empty;
            slot.LastCropId = Pick(ref state, 2) == 0 ? "Unknown" : string.Empty;
            slot.GrowthDays = V2SaveFixtures.PickFloat(ref state);
            slot.Fatigue = V2SaveFixtures.PickFloat(ref state);
            slot.Stage = (SlotStage)V2SaveFixtures.Pick(ref state, 7);
            slot.Flags = (byte)V2SaveFixtures.Pick(ref state, 256);
        }

        private static string Mutate(string text, ref uint state)
        {
            var position = V2SaveFixtures.Pick(ref state, text.Length);
            switch (V2SaveFixtures.Pick(ref state, 4))
            {
                case 0:
                    return text.Substring(0, position);
                case 1:
                    return text.Substring(0, position) + '"' + text.Substring(position + 1);
                case 2:
                    return text.Substring(0, position) + "}{[]" + text.Substring(position);
                default:
                    return position + 1 >= text.Length
                        ? text.Substring(0, position)
                        : text.Substring(0, position) + text.Substring(position + 1);
            }
        }

        private static int Pick(ref uint state, int bound) => V2SaveFixtures.Pick(ref state, bound);

        private static IndoorSlot[] Truncate(IndoorSlot[] slots, int length)
        {
            var result = new IndoorSlot[length];
            Array.Copy(slots, result, Math.Min(length, slots.Length));
            return result;
        }

        private static IndoorSlot[] Pad(IndoorSlot[] slots, int length)
        {
            var result = new IndoorSlot[length];
            Array.Copy(slots, result, slots.Length);
            for (var index = slots.Length; index < length; index++)
            {
                result[index] = new IndoorSlot();
            }

            return result;
        }
    }
}
