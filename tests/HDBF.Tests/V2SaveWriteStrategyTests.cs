// ============================================================================
// HDBF v2 | Layer: L1 test | Owner: V2-CORE-02 (L-04 round)
// Purpose: Write-strategy contract of L-04: every "not writable" validation outcome reports
//          CannotWrite, every repaired-but-writable outcome reports Repaired without CannotWrite,
//          the repaired copy itself stays encodable/decodable, and the constructor keeps the
//          pre-L-04 call sites compiling. The bridge source contract lives in
//          V2PersistenceBridgeContractTests (it can only be pinned as text).
// Requirements: REQ-V2-SAVE-04, REQ-V2-SAVE-06, REQ-V2-CORE-02
// ============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HDBF.Core;
using HDBF.Core.Balance;
using HDBF.Core.Indoor;

namespace HDBF.Tests
{
    /// <summary>
    /// L-04 unit tests: the validator result is the only input of the bridge write strategy, so the
    /// split between "write the repaired copy" (<see cref="IndoorRepairResult.Repaired"/>) and "do
    /// not write at all" (<see cref="IndoorRepairResult.CannotWrite"/>) is pinned here on real Core
    /// behavior.
    /// </summary>
    public static class V2SaveWriteStrategyTests
    {
        private const string RepoRootKey = "HdbfRepoRoot";

        public static void TestCleanResultIsWritable()
        {
            TestAssert.False(IndoorRepairResult.Clean.CannotWrite, "the shared clean result is writable");
            TestAssert.False(IndoorRepairResult.Clean.Repaired, "the shared clean result is unmodified");
            TestAssert.Equal(0, IndoorRepairResult.Clean.Notes.Count, "the shared clean result has no notes");

            foreach (var profile in TierCatalog.All)
            {
                var record = V2SaveFixtures.RichRecord(profile.Tier);
                var result = IndoorSaveValidator.ValidateAndRepair(record, profile,
                    FallbackCropCatalog.Default, record.EntityId);
                TestAssert.False(result.Repaired, profile.Tier + " clean record: " + result);
                TestAssert.False(result.CannotWrite, profile.Tier + " clean record must stay writable: " + result);
            }
        }

        public static void TestEveryCannotWriteConditionIsReported()
        {
            // record-missing: the validator is total, a null record is "not writable", never a throw.
            var missingRecord = IndoorSaveValidator.ValidateAndRepair(null!, V2SaveFixtures.Basic,
                FallbackCropCatalog.Default, null);
            AssertCannotWrite(missingRecord, "record-missing", "missing record");

            // tier-missing: no resolved profile means no capacity/crop contract to validate against.
            var missingTier = IndoorSaveValidator.ValidateAndRepair(
                V2SaveFixtures.RichRecord(HdbfTier.Basic), null!, null!, null);
            AssertCannotWrite(missingTier, "tier-missing", "missing tier");

            // tier-profile-invalid: an enum value outside HdbfTier can never resolve to a profile.
            var brokenProfile = V2SaveFixtures.Basic with { Tier = (HdbfTier)9 };
            var badProfile = IndoorSaveValidator.ValidateAndRepair(V2SaveFixtures.RichRecord(HdbfTier.Basic),
                brokenProfile, FallbackCropCatalog.Default, null);
            AssertCannotWrite(badProfile, "tier-profile-invalid", "invalid tier profile");

            // capacity-invalid: a profile without positive slot capacity cannot describe a record.
            var badCapacity = V2SaveFixtures.Basic with { PlotCapacity = 0 };
            var noCapacity = IndoorSaveValidator.ValidateAndRepair(V2SaveFixtures.RichRecord(HdbfTier.Basic),
                badCapacity, FallbackCropCatalog.Default, null);
            AssertCannotWrite(noCapacity, "capacity-invalid", "invalid slot capacity");

            // A broken live catalog is NOT a write refusal: FallbackCropCatalog.TryResolve treats a
            // throwing catalog as "spec missing" and the built-in table keeps the record writable.
            var brokenCatalog = IndoorSaveValidator.ValidateAndRepair(V2SaveFixtures.RichRecord(HdbfTier.Basic),
                V2SaveFixtures.Basic, new ThrowingCatalog(), null);
            TestAssert.False(brokenCatalog.CannotWrite,
                "a broken catalog degrades to the built-in crop table, it is not a write refusal: " + brokenCatalog);
            TestAssert.False(brokenCatalog.Repaired,
                "the built-in table covers the clean record, so no repair is needed: " + brokenCatalog);
        }

        public static void TestValidatorExceptionIsReportedThroughTheFailPath()
        {
            // validator-exception is the one CannotWrite condition that cannot be triggered from a
            // test through the public API (every interior failure is isolated by design), so the
            // path is pinned as source contract: the catch must go through Fail, and Fail must set
            // the unwritable flag that Build propagates into IndoorRepairResult.CannotWrite.
            var validator = RepoRead("src/HDBF.Core/Indoor/IndoorSaveValidator.cs");
            TestAssert.True(validator.Contains("accumulator.Fail(\"validator-exception:\"", StringComparison.Ordinal),
                "the validator catch must route through Fail so a thrown pass cannot be written");

            var result = RepoRead("src/HDBF.Core/Indoor/IndoorRepairResult.cs");
            TestAssert.True(result.Contains("_cannotWrite = true;", StringComparison.Ordinal),
                "IndoorRepairAccumulator.Fail must mark the pass unwritable");
            TestAssert.True(result.Contains("notes, _cannotWrite)", StringComparison.Ordinal),
                "Build must propagate the unwritable flag into the immutable result");
        }

        public static void TestRepairedFieldAndSlotFixesStayWritable()
        {
            // Slot array padded to capacity: a repairable structural fix, not a write refusal.
            var padded = V2SaveFixtures.RichRecord(HdbfTier.Basic);
            padded.Slots = new[] { padded.Slots[0], padded.Slots[1] };
            AssertRepairedWritable(padded, V2SaveFixtures.Basic, "slot padding", "slots-padded");

            // Unknown crop cleared: the player's data is repaired, the farm stays playable.
            var unknownCrop = V2SaveFixtures.RichRecord(HdbfTier.Basic);
            unknownCrop.Slots[0].CropId = "Durian";
            AssertRepairedWritable(unknownCrop, V2SaveFixtures.Basic, "unknown crop", "crop-unknown");

            // Non-finite tank clamped into [0, capacity].
            var badTank = V2SaveFixtures.RichRecord(HdbfTier.Basic);
            badTank.Tank = float.NaN;
            AssertRepairedWritable(badTank, V2SaveFixtures.Basic, "tank clamp", "tank");

            // Entity id reconciled with the storage key stays a repairable field (L-04 decision: the
            // id mismatch is fixed and the repaired copy is written, it is not a write refusal).
            var mismatched = V2SaveFixtures.RichRecord(HdbfTier.Basic);
            mismatched.EntityId = "other";
            var result = IndoorSaveValidator.ValidateAndRepair(mismatched, V2SaveFixtures.Basic,
                FallbackCropCatalog.Default, "key-9");
            TestAssert.True(result.Repaired, "an id mismatch is repaired: " + result);
            TestAssert.False(result.CannotWrite, "an id mismatch stays writable: " + result);
            TestAssert.Equal("key-9", mismatched.EntityId, "the id is reconciled with the storage key");
            TestAssert.True(V2SaveFixtures.HasNote(result, "entityId-mismatch"),
                "note: " + string.Join("|", result.Notes));
        }

        public static void TestRepairedCopyEncodesAndDecodes()
        {
            var record = V2SaveFixtures.RichRecord(HdbfTier.Intermediate);
            record.Slots[3].CropId = "Durian";
            record.Slots[4].GrowthDays = -5f;
            var validation = IndoorSaveValidator.ValidateAndRepair(record, V2SaveFixtures.Intermediate,
                FallbackCropCatalog.Default, record.EntityId);
            TestAssert.True(validation.Repaired, "the record needed repair: " + validation);
            TestAssert.False(validation.CannotWrite, "the repaired record is writable: " + validation);

            // The bridge writes this repaired copy: it must encode and decode back cleanly (L-04).
            var text = IndoorSaveCodec.Encode(record);
            TestAssert.True(text.Length > 0, "a repaired record must encode");
            TestAssert.True(IndoorSaveCodec.TryDecode(text, out var decoded, out var error),
                "a repaired record must decode: " + error);
            TestAssert.Equal(string.Empty, error ?? string.Empty);
            TestAssert.Equal(string.Empty, decoded!.Slots[3].CropId, "the unknown crop stays cleared");
            TestAssert.Equal(0f, decoded.Slots[4].GrowthDays, 1e-6f, "the negative growth stays clamped");
            TestAssert.Equal(text, IndoorSaveCodec.Encode(decoded), "the repaired payload is byte-stable");
        }

        public static void TestConstructorKeepsPreL04CallSitesAndDefaultsWritable()
        {
            var notes = new List<string> { "slot[1].growth-clamped" };

            // Pre-L-04 five-argument call: still compiles and means "repaired, writable".
            var repaired = new IndoorRepairResult(true, 0, 0, 1, notes);
            TestAssert.True(repaired.Repaired);
            TestAssert.False(repaired.CannotWrite, "the default must stay writable");
            TestAssert.True(repaired.ToString().Contains("cannotWrite=False", StringComparison.Ordinal),
                "ToString keeps the strategy flag: " + repaired);

            var refused = new IndoorRepairResult(true, 0, 0, 1, notes, cannotWrite: true);
            TestAssert.True(refused.CannotWrite, "the explicit flag marks the record unwritable");
            TestAssert.True(refused.ToString().Contains("cannotWrite=True", StringComparison.Ordinal),
                "ToString keeps the strategy flag: " + refused);

            var empty = new IndoorRepairResult(false, -1, -1, -1, null);
            TestAssert.False(empty.Repaired, "negative counters are clamped to zero");
            TestAssert.False(empty.CannotWrite);
            TestAssert.Equal(0, empty.Notes.Count, "a null note list is treated as empty");
        }

        public static void TestL04LogKeysStayInTheSaveFamily()
        {
            TestAssert.Equal("save.write-repaired", IndoorLogKeys.SaveWriteRepaired);
            TestAssert.Equal("save.previous-snapshot", IndoorLogKeys.SavePreviousSnapshot);
            TestAssert.True(IndoorLogKeys.SaveWriteRepaired.StartsWith("save.", StringComparison.Ordinal)
                            && IndoorLogKeys.SavePreviousSnapshot.StartsWith("save.", StringComparison.Ordinal),
                "new keys keep the save.* namespace");
            var all = new HashSet<string>(StringComparer.Ordinal)
            {
                IndoorLogKeys.SaveWrite, IndoorLogKeys.ValidationFailed, IndoorLogKeys.Migrated,
                IndoorLogKeys.MigratedFailed, IndoorLogKeys.SaveWriteRepaired, IndoorLogKeys.SavePreviousSnapshot,
            };
            TestAssert.Equal(6, all.Count, "the save.* keys stay distinct");
        }

        private static void AssertCannotWrite(IndoorRepairResult result, string note, string context)
        {
            TestAssert.True(result.Repaired, context + " must be marked repaired: " + result);
            TestAssert.True(result.CannotWrite, context + " must be marked cannot-write: " + result);
            TestAssert.True(V2SaveFixtures.HasNote(result, note),
                context + " note missing: " + string.Join("|", result.Notes));
        }

        private static void AssertRepairedWritable(IndoorFarmRecord record, HDBF.Core.Balance.TierProfile tier,
            string context, string note)
        {
            var result = IndoorSaveValidator.ValidateAndRepair(record, tier, FallbackCropCatalog.Default,
                record.EntityId);
            TestAssert.True(result.Repaired, context + " must be repaired: " + result);
            TestAssert.False(result.CannotWrite, context + " must stay writable: " + result);
            TestAssert.True(V2SaveFixtures.HasNote(result, note),
                context + " note missing: " + string.Join("|", result.Notes));
        }

        /// <summary>Catalog stub that violates the "never throws" catalog contract on any lookup.</summary>
        private sealed class ThrowingCatalog : ICropCatalog
        {
            public IReadOnlyList<CropDefinition> ForFaction(HdbfFaction faction)
            {
                throw new InvalidOperationException("test catalog is broken");
            }

            public bool TryGet(HdbfFaction faction, string cropId, out CropDefinition crop)
            {
                throw new InvalidOperationException("test catalog is broken");
            }
        }

        /// <summary>Reads one repository source file through the baked-in repo root metadata.</summary>
        private static string RepoRead(string relativePath)
        {
            var attribute = typeof(V2SaveWriteStrategyTests).Assembly
                .GetCustomAttributes(typeof(AssemblyMetadataAttribute), inherit: false)
                .OfType<AssemblyMetadataAttribute>()
                .FirstOrDefault(entry => string.Equals(entry.Key, RepoRootKey, StringComparison.Ordinal));
            TestAssert.True(attribute is not null && !string.IsNullOrEmpty(attribute.Value),
                "assembly metadata \"" + RepoRootKey + "\" is missing");
            var path = Path.Combine(attribute!.Value!, relativePath);
            TestAssert.True(File.Exists(path), "source file missing: " + path);
            return File.ReadAllText(path);
        }
    }
}
