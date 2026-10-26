// ============================================================================
// HDBF v2 | Layer: L1 test helper | Owner: V2-CORE-02
// Purpose: Shared fixtures of the v2 save tests: rich valid records, strict decode,
//          invariant assertions (section 19.18.3-A2), deterministic LCG damage source and a
//          capturing IHdbfLog. Contains no [Test] methods; TestRunner ignores it.
// Requirements: REQ-V2-SAVE-01, REQ-V2-SAVE-04, REQ-V2-SAVE-07
// ============================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using HDBF.Core;
using HDBF.Core.Balance;
using HDBF.Core.Indoor;
using HDBF.Core.Logging;

namespace HDBF.Tests
{
    internal static class V2SaveFixtures
    {
        internal static readonly TierProfile Basic = TierCatalog.Get(HdbfTier.Basic);
        internal static readonly TierProfile Intermediate = TierCatalog.Get(HdbfTier.Intermediate);
        internal static readonly TierProfile Advanced = TierCatalog.Get(HdbfTier.Advanced);
        internal static readonly TierProfile Top = TierCatalog.Get(HdbfTier.Top);

        internal static readonly string[] FolktailsCrops = { "Carrot", "Potato", "Wheat", "Sunflower" };
        internal static readonly string[] IronTeethCrops = { "Canola", "Cassava", "Corn", "Eggplant", "Kohlrabi", "Soybean" };

        internal static TierProfile Profile(HdbfTier tier) => TierCatalog.Get(tier);

        internal static string[] Crops(HdbfFaction faction) =>
            faction == HdbfFaction.IronTeeth ? IronTeethCrops : FolktailsCrops;

        /// <summary>Clean record: every slot planted with a valid crop and in-range values.</summary>
        internal static IndoorFarmRecord RichRecord(HdbfTier tier, HdbfFaction faction = HdbfFaction.Folktails,
            bool populate = true, string entityId = "entity-1")
        {
            var record = IndoorFarmRecord.Create(tier, faction);
            record.EntityId = entityId;
            record.TimberPipesAdapter = IndoorSaveLimits.AdapterStateOk;
            if (!populate)
            {
                return record;
            }

            var crops = Crops(faction);
            for (var index = 0; index < record.Slots.Length; index++)
            {
                var slot = record.Slots[index];
                slot.CropId = crops[index % crops.Length];
                slot.GrowthDays = (index % 6) * 0.4f; // < the shortest faction growth time (3 days)
                slot.Fatigue = (index % 20) * 0.05f;
                slot.LastCropId = crops[(index + 1) % crops.Length];
                slot.Stage = SlotStage.Growing;
            }

            return record;
        }

        internal static IndoorFarmRecord DecodeStrict(string text)
        {
            var ok = IndoorSaveCodec.TryDecode(text, out var record, out var error);
            TestAssert.True(ok, "decode failed: " + error);
            TestAssert.NotNull(record);
            return record;
        }

        /// <summary>Repairs a record and asserts it is immediately valid (the write-path contract).</summary>
        internal static IndoorRepairResult Repair(IndoorFarmRecord record, TierProfile tier, string? expectedEntityId = null)
        {
            var result = IndoorSaveValidator.ValidateAndRepair(record, tier, FallbackCropCatalog.Default, expectedEntityId);
            AssertInvariants(record, tier, "repair");
            return result;
        }

        /// <summary>Asserts every section 19.18.3-A2 invariant on an already repaired record.</summary>
        internal static void AssertInvariants(IndoorFarmRecord record, TierProfile tier, string context)
        {
            TestAssert.NotNull(record, context + ": record");
            TestAssert.True(Enum.IsDefined(typeof(HdbfTier), record.Tier), context + ": tier");
            TestAssert.True(Enum.IsDefined(typeof(HdbfFaction), record.Faction), context + ": faction");
            TestAssert.True(Enum.IsDefined(typeof(IndoorMode), record.Mode), context + ": mode");
            TestAssert.True(Enum.IsDefined(typeof(CropPolicy), record.CropPolicy), context + ": cropPolicy");
            TestAssert.NotNull(record.Slots, context + ": slots");
            TestAssert.Equal(tier.SlotCapacity, record.Slots.Length, context + ": slot length");

            TestAssert.True(Finite(record.Tank) && record.Tank >= 0f && record.Tank <= tier.TankCapacity,
                context + ": tank " + record.Tank);
            TestAssert.True(Finite(record.MaintenanceDueDay) && record.MaintenanceDueDay >= 0f
                && record.MaintenanceDueDay <= IndoorSaveLimits.MaxMaintenanceDueDay, context + ": dueDay");
            if (tier.MaintenanceKind == HdbfMaintenanceKind.IrrigationOnly)
            {
                TestAssert.Equal(0f, record.MaintenanceDueDay, 0f, context + ": irrigation-only due day");
            }

            TestAssert.True(record.MaintenanceCyclesMissed >= 0
                && record.MaintenanceCyclesMissed <= IndoorSaveLimits.MaxMaintenanceCyclesMissed,
                context + ": cycles");

            if (tier.NeedsPower)
            {
                TestAssert.True(record.PowerMode == PowerConnection.Grid || record.PowerMode == PowerConnection.OffGrid,
                    context + ": power mode");
            }
            else
            {
                TestAssert.Equal(PowerConnection.NotRequired, record.PowerMode, context + ": power mode");
                TestAssert.False(record.PowerConnected, context + ": connected without power tier");
            }

            var adapter = record.TimberPipesAdapter;
            TestAssert.True(IsKnownAdapter(adapter), context + ": adapter " + adapter);
            if (record.CropPolicy == CropPolicy.Rotation)
            {
                TestAssert.Equal(string.Empty, record.FixedCropId, context + ": rotation fixed crop");
            }

            for (var index = 0; index < record.Slots.Length; index++)
            {
                var slot = record.Slots[index];
                var label = context + ": slot " + index.ToString(CultureInfo.InvariantCulture);
                TestAssert.NotNull(slot, label);
                TestAssert.True(Enum.IsDefined(typeof(SlotStage), slot.Stage), label + " stage");
                TestAssert.True(Finite(slot.Fatigue) && slot.Fatigue >= 0f && slot.Fatigue <= 1f, label + " fatigue");
                TestAssert.True(Finite(slot.GrowthDays) && slot.GrowthDays >= 0f, label + " growth");
                TestAssert.Equal(0, slot.Flags & ~IndoorSaveLimits.AllowedSlotFlags, label + " flags");
                TestAssert.True(slot.LastCropId is not null, label + " lastCrop null");
                if (string.IsNullOrEmpty(slot.CropId))
                {
                    TestAssert.Equal(SlotStage.Empty, slot.Stage, label + " empty stage");
                    TestAssert.Equal(0f, slot.GrowthDays, 0f, label + " empty growth");
                    continue;
                }

                TestAssert.True(FallbackCropCatalog.Default.TryGet(record.Faction, slot.CropId, out var crop),
                    label + " crop known: " + slot.CropId);
                TestAssert.True(slot.Stage != SlotStage.Empty, label + " stage with crop");
                TestAssert.True(slot.GrowthDays <= crop.GrowthTimeInDays + 1e-4f, label + " growth bound");
                if (slot.GrowthDays >= crop.GrowthTimeInDays)
                {
                    TestAssert.Equal(SlotStage.Mature, slot.Stage, label + " mature by progress");
                }
            }
        }

        internal static void AssertRecordsEqual(IndoorFarmRecord expected, IndoorFarmRecord actual, string context,
            float tolerance = 1e-3f)
        {
            TestAssert.Equal(expected.SchemaVersion, actual.SchemaVersion, context + ": schemaVersion");
            TestAssert.Equal(expected.EntityId, actual.EntityId, context + ": entityId");
            TestAssert.Equal(expected.Tier, actual.Tier, context + ": tier");
            TestAssert.Equal(expected.Faction, actual.Faction, context + ": faction");
            TestAssert.Equal(expected.Mode, actual.Mode, context + ": mode");
            TestAssert.Equal(expected.CropPolicy, actual.CropPolicy, context + ": cropPolicy");
            TestAssert.Equal(expected.FixedCropId, actual.FixedCropId, context + ": fixedCrop");
            TestAssert.Equal(expected.Tank, actual.Tank, tolerance, context + ": tank");
            TestAssert.Equal(expected.MaintenanceDueDay, actual.MaintenanceDueDay, tolerance, context + ": dueDay");
            TestAssert.Equal(expected.MaintenanceCyclesMissed, actual.MaintenanceCyclesMissed, context + ": cycles");
            TestAssert.Equal(expected.PowerConnected, actual.PowerConnected, context + ": powerConnected");
            TestAssert.Equal(expected.PowerMode, actual.PowerMode, context + ": powerMode");
            TestAssert.Equal(expected.PipePort, actual.PipePort, context + ": pipePort");
            TestAssert.Equal(expected.LogisticsPort, actual.LogisticsPort, context + ": logisticsPort");
            TestAssert.Equal(expected.PowerPort, actual.PowerPort, context + ": powerPort");
            TestAssert.Equal(expected.TimberPipesAdapter, actual.TimberPipesAdapter, context + ": adapter");
            TestAssert.Equal(expected.Slots.Length, actual.Slots.Length, context + ": slot count");
            for (var index = 0; index < expected.Slots.Length; index++)
            {
                var left = expected.Slots[index];
                var right = actual.Slots[index];
                var label = context + ": slot " + index.ToString(CultureInfo.InvariantCulture);
                TestAssert.Equal(left.CropId, right.CropId, label + " crop");
                TestAssert.Equal(left.GrowthDays, right.GrowthDays, tolerance, label + " growth");
                TestAssert.Equal(left.Fatigue, right.Fatigue, tolerance, label + " fatigue");
                TestAssert.Equal(left.LastCropId, right.LastCropId, label + " lastCrop");
                TestAssert.Equal(left.Stage, right.Stage, label + " stage");
                TestAssert.Equal((int)left.Flags, (int)right.Flags, label + " flags");
            }
        }

        /// <summary>Fixed LCG: identical seeds must produce identical damage sweeps on every machine.</summary>
        internal static uint Next(ref uint state)
        {
            state = unchecked(state * 1664525u + 1013904223u);
            return state;
        }

        internal static int Pick(ref uint state, int bound)
        {
            return bound <= 0 ? 0 : (int)(Next(ref state) % (uint)bound);
        }

        internal static float PickFloat(ref uint state)
        {
            switch (Next(ref state) % 8u)
            {
                case 0: return float.NaN;
                case 1: return float.PositiveInfinity;
                case 2: return float.NegativeInfinity;
                case 3: return -2f;
                case 4: return 2f;
                case 5: return 1.0001f;
                case 6: return 0f;
                default: return (Next(ref state) % 100u) / 100f;
            }
        }

        internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        internal static bool HasNote(IndoorRepairResult result, string fragment)
        {
            for (var index = 0; index < result.Notes.Count; index++)
            {
                if (result.Notes[index].Contains(fragment, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        internal static bool IsKnownAdapter(string? state)
        {
            return state is IndoorSaveLimits.AdapterStateNone or IndoorSaveLimits.AdapterStateOk
                or IndoorSaveLimits.AdapterStateDegraded or IndoorSaveLimits.AdapterStateDisabled
                or IndoorSaveLimits.AdapterStateFailed;
        }
    }

    /// <summary>Captures throttled log calls so migration tests can assert the save.* keys.</summary>
    internal sealed class CapturingLog : IHdbfLog
    {
        internal readonly List<string> Keys = new List<string>();
        internal readonly List<string> Messages = new List<string>();

        public void Write(HdbfLogCategory category, string key, string message)
        {
            Keys.Add(key);
            Messages.Add(message);
        }

        public void Error(HdbfLogCategory category, string key, string message, Exception? exception,
            string? entityId = null)
        {
            Write(category, key, message);
        }

        public int SuppressedCount(string key) => 0;

        public void Reset()
        {
            Keys.Clear();
            Messages.Clear();
        }

        internal bool Has(string key) => Keys.Contains(key);
    }
}
