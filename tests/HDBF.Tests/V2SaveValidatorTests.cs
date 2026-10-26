// ============================================================================
// HDBF v2 | Layer: L1 test | Owner: V2-CORE-02
// Purpose: Pre-write validation tests, one per section 19.18.3-A2 rule: array length, enum
//          whitelist, finite floats, clamp ranges, crop ids, strings, ports, adapter state,
//          locatable summary, null inputs and idempotent repair (REQ-V2-SAVE-04/06).
// Requirements: REQ-V2-SAVE-04, REQ-V2-SAVE-06, REQ-V2-CORE-02
// ============================================================================
using System;
using HDBF.Core;
using HDBF.Core.Balance;
using HDBF.Core.Indoor;

namespace HDBF.Tests
{
    public static class V2SaveValidatorTests
    {
        private static bool HasNote(IndoorRepairResult result, string fragment) =>
            V2SaveFixtures.HasNote(result, fragment);

        public static void TestCleanRecordNeedsNoRepair()
        {
            foreach (var profile in TierCatalog.All)
            {
                var record = V2SaveFixtures.RichRecord(profile.Tier);
                var result = IndoorSaveValidator.ValidateAndRepair(record, profile,
                    FallbackCropCatalog.Default, record.EntityId);
                TestAssert.False(result.Repaired, profile.Tier + " clean record was repaired: " + result);
                TestAssert.Equal(0, result.SlotsTrimmed, profile.Tier + ": trimmed");
                TestAssert.Equal(0, result.SlotsPadded, profile.Tier + ": padded");
                TestAssert.Equal(0, result.ValuesClamped, profile.Tier + ": clamped");
                TestAssert.Equal(0, result.Notes.Count, profile.Tier + ": notes");
            }
        }




        public static void TestInvalidEnumsAreWhitelisted()
        {
            var record = V2SaveFixtures.RichRecord(HdbfTier.Advanced);
            record.Tier = HdbfTier.Basic;
            record.Faction = (HdbfFaction)42;
            record.Mode = (IndoorMode)9;
            record.CropPolicy = (CropPolicy)7;
            var result = V2SaveFixtures.Repair(record, V2SaveFixtures.Advanced);
            TestAssert.True(result.Repaired);
            TestAssert.Equal(HdbfTier.Advanced, record.Tier, "profile tier wins");
            TestAssert.Equal(HdbfFaction.Folktails, record.Faction, "unknown faction -> Folktails");
            TestAssert.Equal(IndoorMode.Normal, record.Mode, "unknown mode -> Normal");
            TestAssert.Equal(CropPolicy.Rotation, record.CropPolicy, "unknown policy -> Rotation");
        }

        public static void TestSlotStageAndReservedFlagBitsAreRepaired()
        {
            var record = V2SaveFixtures.RichRecord(HdbfTier.Basic);
            record.Slots[0].Stage = (SlotStage)9;
            record.Slots[1].Flags = 0xFF;
            record.Slots[2].Stage = SlotStage.Empty;
            record.Slots[3].GrowthDays = 99f;
            var result = V2SaveFixtures.Repair(record, V2SaveFixtures.Basic);
            TestAssert.True(result.Repaired);
            TestAssert.Equal(SlotStage.Growing, record.Slots[0].Stage, "invalid stage repaired by progress");
            TestAssert.Equal(1, record.Slots[1].Flags, "only bit 0 is a known flag");
            TestAssert.Equal(SlotStage.Growing, record.Slots[2].Stage, "empty stage with a crop repaired");
            TestAssert.Equal(SlotStage.Mature, record.Slots[3].Stage, "progress at growth time matures");
            TestAssert.True(HasNote(result, "slot[0]"), "stage note locatable");
            TestAssert.True(HasNote(result, "flags"), "flags note locatable");
        }

        public static void TestTankIsClampedAndNonFiniteRejected()
        {
            var record = V2SaveFixtures.RichRecord(HdbfTier.Basic);
            record.Tank = 999f;
            var result = V2SaveFixtures.Repair(record, V2SaveFixtures.Basic);
            TestAssert.Equal(V2SaveFixtures.Basic.TankCapacity, record.Tank, 1e-4f);
            TestAssert.True(HasNote(result, "tank-clamped"), "note: " + string.Join("|", result.Notes));
            foreach (var broken in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -1f })
            {
                record = V2SaveFixtures.RichRecord(HdbfTier.Basic);
                record.Tank = broken;
                V2SaveFixtures.Repair(record, V2SaveFixtures.Basic);
                TestAssert.Equal(0f, record.Tank, 0f, "tank " + broken);
            }
        }

        public static void TestFatigueAndGrowthRangesAreClamped()
        {
            var record = V2SaveFixtures.RichRecord(HdbfTier.Basic);
            record.Slots[0].Fatigue = 5f;
            record.Slots[1].Fatigue = -2f;
            record.Slots[2].Fatigue = float.NaN;
            record.Slots[3].GrowthDays = -1f;
            record.Slots[4].GrowthDays = float.PositiveInfinity;
            var result = V2SaveFixtures.Repair(record, V2SaveFixtures.Basic);
            TestAssert.True(result.Repaired);
            TestAssert.Equal(1f, record.Slots[0].Fatigue, 1e-6f);
            TestAssert.Equal(0f, record.Slots[1].Fatigue, 1e-6f);
            TestAssert.Equal(0f, record.Slots[2].Fatigue, 1e-6f);
            TestAssert.Equal(0f, record.Slots[3].GrowthDays, 1e-6f);
            TestAssert.Equal(0f, record.Slots[4].GrowthDays, 1e-6f);
            TestAssert.True(HasNote(result, "fatigue"), "fatigue note locatable");
            TestAssert.True(HasNote(result, "growth"), "growth note locatable");
        }

        public static void TestUnknownCropIdsAreCleared()
        {
            var record = V2SaveFixtures.RichRecord(HdbfTier.Basic);
            record.Slots[0].CropId = "Durian";
            record.Slots[1].LastCropId = "Durian";
            var result = V2SaveFixtures.Repair(record, V2SaveFixtures.Basic);
            TestAssert.True(result.Repaired);
            TestAssert.Equal(string.Empty, record.Slots[0].CropId);
            TestAssert.Equal(SlotStage.Empty, record.Slots[0].Stage);
            TestAssert.Equal(0f, record.Slots[0].GrowthDays, 1e-6f);
            TestAssert.Equal(string.Empty, record.Slots[1].LastCropId);
            TestAssert.True(HasNote(result, "crop-unknown"), "crop note: " + string.Join("|", result.Notes));
        }

        public static void TestFixedCropRules()
        {
            var record = V2SaveFixtures.RichRecord(HdbfTier.Basic, HdbfFaction.Folktails);
            record.CropPolicy = CropPolicy.Fixed;
            record.FixedCropId = "Cassava"; // IronTeeth crop on a Folktails farm
            V2SaveFixtures.Repair(record, V2SaveFixtures.Basic);
            TestAssert.Equal(string.Empty, record.FixedCropId, "cross-faction fixed crop cleared");

            record = V2SaveFixtures.RichRecord(HdbfTier.Basic);
            record.CropPolicy = CropPolicy.Fixed;
            record.FixedCropId = "Carrot";
            var result = V2SaveFixtures.Repair(record, V2SaveFixtures.Basic);
            TestAssert.False(result.Repaired, "a valid fixed crop is clean");
            TestAssert.Equal("Carrot", record.FixedCropId);

            record.CropPolicy = CropPolicy.Rotation;
            V2SaveFixtures.Repair(record, V2SaveFixtures.Basic);
            TestAssert.Equal(string.Empty, record.FixedCropId, "rotation carries no fixed crop");
        }

        public static void TestPortsAndPowerFollowTierCapabilities()
        {
            var basic = V2SaveFixtures.RichRecord(HdbfTier.Basic);
            basic.PowerPort = true;
            basic.LogisticsPort = true;
            basic.PowerMode = PowerConnection.Grid;
            basic.PowerConnected = true;
            var result = V2SaveFixtures.Repair(basic, V2SaveFixtures.Basic);
            TestAssert.True(result.Repaired);
            TestAssert.False(basic.PowerPort);
            TestAssert.False(basic.LogisticsPort);
            TestAssert.Equal(PowerConnection.NotRequired, basic.PowerMode);
            TestAssert.False(basic.PowerConnected);

            var top = V2SaveFixtures.RichRecord(HdbfTier.Top);
            top.PowerMode = PowerConnection.NotRequired;
            V2SaveFixtures.Repair(top, V2SaveFixtures.Top);
            TestAssert.Equal(PowerConnection.Grid, top.PowerMode, "a power tier must stay on grid or off-grid");
        }

        public static void TestMaintenanceRangesAreRepaired()
        {
            var intermediate = V2SaveFixtures.RichRecord(HdbfTier.Intermediate);
            var cycle = MaintenanceDefaults.DefaultCycleDays(V2SaveFixtures.Intermediate);
            intermediate.MaintenanceDueDay = float.NaN;
            V2SaveFixtures.Repair(intermediate, V2SaveFixtures.Intermediate);
            TestAssert.Equal(cycle, intermediate.MaintenanceDueDay, 1e-4f, "missing due day rescheduled one cycle");
            intermediate.MaintenanceDueDay = 0f;
            V2SaveFixtures.Repair(intermediate, V2SaveFixtures.Intermediate);
            TestAssert.Equal(cycle, intermediate.MaintenanceDueDay, 1e-4f, "never-scheduled due day rescheduled");
            intermediate.MaintenanceDueDay = IndoorSaveLimits.MaxMaintenanceDueDay + 100f;
            V2SaveFixtures.Repair(intermediate, V2SaveFixtures.Intermediate);
            TestAssert.Equal(IndoorSaveLimits.MaxMaintenanceDueDay, intermediate.MaintenanceDueDay, 1e-4f);
            intermediate.MaintenanceCyclesMissed = -3;
            V2SaveFixtures.Repair(intermediate, V2SaveFixtures.Intermediate);
            TestAssert.Equal(0, intermediate.MaintenanceCyclesMissed);
            intermediate.MaintenanceCyclesMissed = 5000;
            V2SaveFixtures.Repair(intermediate, V2SaveFixtures.Intermediate);
            TestAssert.Equal(IndoorSaveLimits.MaxMaintenanceCyclesMissed, intermediate.MaintenanceCyclesMissed);

            var basic = V2SaveFixtures.RichRecord(HdbfTier.Basic);
            basic.MaintenanceDueDay = 12f;
            V2SaveFixtures.Repair(basic, V2SaveFixtures.Basic);
            TestAssert.Equal(0f, basic.MaintenanceDueDay, 0f, "irrigation-only tier has no due day");
        }

        public static void TestStringLengthsAreCapped()
        {
            var record = V2SaveFixtures.RichRecord(HdbfTier.Basic);
            record.EntityId = new string('x', 200);
            var result = V2SaveFixtures.Repair(record, V2SaveFixtures.Basic);
            TestAssert.Equal(IndoorSaveLimits.MaxEntityIdLength, record.EntityId.Length);
            TestAssert.True(HasNote(result, "entityId-truncated"), "note: " + string.Join("|", result.Notes));

            record = V2SaveFixtures.RichRecord(HdbfTier.Basic);
            record.CropPolicy = CropPolicy.Fixed;
            record.FixedCropId = new string('y', 100);
            V2SaveFixtures.Repair(record, V2SaveFixtures.Basic);
            TestAssert.Equal(string.Empty, record.FixedCropId);
        }

        public static void TestAdapterStateIsWhitelisted()
        {
            var record = V2SaveFixtures.RichRecord(HdbfTier.Basic);
            record.TimberPipesAdapter = "Exploded";
            var result = V2SaveFixtures.Repair(record, V2SaveFixtures.Basic);
            TestAssert.Equal(IndoorSaveLimits.AdapterStateNone, record.TimberPipesAdapter);
            TestAssert.True(HasNote(result, "adapter"), "note: " + string.Join("|", result.Notes));
            foreach (var state in new[]
                     {
                         IndoorSaveLimits.AdapterStateNone, IndoorSaveLimits.AdapterStateOk,
                         IndoorSaveLimits.AdapterStateDegraded, IndoorSaveLimits.AdapterStateDisabled,
                         IndoorSaveLimits.AdapterStateFailed,
                     })
            {
                record = V2SaveFixtures.RichRecord(HdbfTier.Basic);
                record.TimberPipesAdapter = state;
                var clean = V2SaveFixtures.Repair(record, V2SaveFixtures.Basic);
                TestAssert.False(clean.Repaired, state + " must be accepted");
            }
        }


        public static void TestValidatorNeverThrowsOnNullInputs()
        {
            var missingRecord = IndoorSaveValidator.ValidateAndRepair(null!, V2SaveFixtures.Basic,
                FallbackCropCatalog.Default, null);
            TestAssert.True(missingRecord.Repaired);
            TestAssert.True(HasNote(missingRecord, "record-missing"), "note: " + string.Join("|", missingRecord.Notes));

            var record = V2SaveFixtures.RichRecord(HdbfTier.Basic);
            var missingTier = IndoorSaveValidator.ValidateAndRepair(record, null!, null!, null);
            TestAssert.True(missingTier.Repaired);
            TestAssert.True(HasNote(missingTier, "tier-missing"), "note: " + string.Join("|", missingTier.Notes));

            var noCatalog = V2SaveFixtures.RichRecord(HdbfTier.Basic);
            var result = IndoorSaveValidator.ValidateAndRepair(noCatalog, V2SaveFixtures.Basic, null!, null);
            TestAssert.False(result.Repaired, "the built-in fallback covers a null catalog: " + result);

            var brokenProfile = V2SaveFixtures.Basic with { Tier = (HdbfTier)9 };
            var badProfile = IndoorSaveValidator.ValidateAndRepair(V2SaveFixtures.RichRecord(HdbfTier.Basic),
                brokenProfile, FallbackCropCatalog.Default, null);
            TestAssert.True(badProfile.Repaired, "an unresolvable tier profile must block the write");
            TestAssert.True(HasNote(badProfile, "tier-profile-invalid"),
                "note: " + string.Join("|", badProfile.Notes));
        }
    }
}
