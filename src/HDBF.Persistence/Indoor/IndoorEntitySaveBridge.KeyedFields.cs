// ============================================================================
// HDBF v2 | Layer: Persistence/Indoor | Owner: V2-SAVE-01
// Purpose: Wire format of the keyed bridge: pack the record into scalar properties plus six slot
//          parallel arrays and unpack them into a full capacity-length slot array.
// Requirements: REQ-V2-SAVE-01, REQ-V2-SAVE-04, REQ-V2-SAVE-06, REQ-V2-EXT-03
// ============================================================================
using System.Collections.Generic;
using HDBF.Core.Balance;
using HDBF.Core.Indoor;
using Timberborn.Persistence;

namespace HDBF.Persistence.Indoor
{
    /// <summary>
    /// Keyed wire format of the schema-v2 record (INTERFACES section 8, section 19.10): every
    /// scalar is its own property and the slot array is six parallel lists. No nested object or
    /// table serializer is used, so the record stays inside the four game primitives
    /// (int/float/bool/string, docs/API_FACTS.md section 6) and one save writes it completely.
    /// </summary>
    public static partial class IndoorEntitySaveBridge
    {
        /// <summary>Writes the complete keyed record; the caller has already validated it.</summary>
        /// <param name="component">Target object saver of the entity's v2 component.</param>
        /// <param name="record">Validated record; slot length already equals the tier capacity.</param>
        private static void WriteRecord(IObjectSaver component, IndoorFarmRecord record)
        {
            // Enums are persisted as int and the flag byte as int: the serializer has no enum/byte
            // primitive (API_FACTS section 6, REQ-V2-SAVE-01).
            component.Set(IndoorSaveKeys.SchemaVersion, IndoorFarmRecord.CurrentSchemaVersion);
            component.Set(IndoorSaveKeys.EntityId, record.EntityId ?? string.Empty);
            component.Set(IndoorSaveKeys.Tier, (int)record.Tier);
            component.Set(IndoorSaveKeys.Faction, (int)record.Faction);
            component.Set(IndoorSaveKeys.Mode, (int)record.Mode);
            component.Set(IndoorSaveKeys.CropPolicy, (int)record.CropPolicy);
            component.Set(IndoorSaveKeys.FixedCropId, record.FixedCropId ?? string.Empty);
            component.Set(IndoorSaveKeys.Tank, record.Tank);
            component.Set(IndoorSaveKeys.MaintenanceDueDay, record.MaintenanceDueDay);
            component.Set(IndoorSaveKeys.MaintenanceCyclesMissed, record.MaintenanceCyclesMissed);
            component.Set(IndoorSaveKeys.PowerConnected, record.PowerConnected);
            component.Set(IndoorSaveKeys.PowerMode, (int)record.PowerMode);
            component.Set(IndoorSaveKeys.PortPipe, record.PipePort);
            component.Set(IndoorSaveKeys.PortPower, record.PowerPort);
            component.Set(IndoorSaveKeys.PortLogistics, record.LogisticsPort);
            component.Set(IndoorSaveKeys.TimberPipesAdapter, record.TimberPipesAdapter);

            // Six parallel lists encode the slot array without a nested/table serializer: list i
            // always has exactly SlotCapacity entries so the record is complete in one write.
            var slots = record.Slots ?? new IndoorSlot[0];
            var capacity = slots.Length;
            var cropIds = new List<string>(capacity);
            var growth = new List<float>(capacity);
            var fatigue = new List<float>(capacity);
            var lastCropIds = new List<string>(capacity);
            var stages = new List<int>(capacity);
            var flags = new List<int>(capacity);
            for (var index = 0; index < capacity; index++)
            {
                var slot = slots[index];
                if (slot is null)
                {
                    cropIds.Add(string.Empty);
                    growth.Add(0f);
                    fatigue.Add(0f);
                    lastCropIds.Add(string.Empty);
                    stages.Add((int)SlotStage.Empty);
                    flags.Add(0);
                    continue;
                }

                cropIds.Add(slot.CropId ?? string.Empty);
                growth.Add(slot.GrowthDays);
                fatigue.Add(slot.Fatigue);
                lastCropIds.Add(slot.LastCropId ?? string.Empty);
                stages.Add((int)slot.Stage);
                flags.Add(slot.Flags);
            }

            component.Set(IndoorSaveKeys.SlotCropIds, cropIds);
            component.Set(IndoorSaveKeys.SlotGrowth, growth);
            component.Set(IndoorSaveKeys.SlotFatigue, fatigue);
            component.Set(IndoorSaveKeys.SlotLastCropIds, lastCropIds);
            component.Set(IndoorSaveKeys.SlotStages, stages);
            component.Set(IndoorSaveKeys.SlotFlags, flags);
        }

        /// <summary>
        /// Reads the six slot parallel lists into a full capacity-length slot array. A missing,
        /// wrong-typed, shorter or longer list degrades to default values / truncation and marks
        /// the record damaged; the validator then repairs the slot state machine (REQ-V2-SAVE-06).
        /// </summary>
        /// <param name="component">Source object loader of the entity's v2 component.</param>
        /// <param name="tier">Resolved tier profile; supplies the authoritative slot capacity.</param>
        /// <param name="damaged">Set when a list was missing, mistyped or not capacity-long.</param>
        /// <returns>A slot array of exactly <see cref="TierProfile.SlotCapacity"/> entries.</returns>
        private static IndoorSlot[] ReadSlots(IObjectLoader component, TierProfile tier, ref bool damaged)
        {
            var capacity = tier.SlotCapacity;
            var cropIds = ReadStrings(component, IndoorSaveKeys.SlotCropIds, ref damaged);
            var growth = ReadFloats(component, IndoorSaveKeys.SlotGrowth, ref damaged);
            var fatigue = ReadFloats(component, IndoorSaveKeys.SlotFatigue, ref damaged);
            var lastCropIds = ReadStrings(component, IndoorSaveKeys.SlotLastCropIds, ref damaged);
            var stages = ReadInts(component, IndoorSaveKeys.SlotStages, ref damaged);
            var flags = ReadInts(component, IndoorSaveKeys.SlotFlags, ref damaged);
            if (cropIds.Count != capacity || growth.Count != capacity || fatigue.Count != capacity
                || lastCropIds.Count != capacity || stages.Count != capacity || flags.Count != capacity)
            {
                damaged = true;
            }

            var slots = new IndoorSlot[capacity];
            for (var index = 0; index < capacity; index++)
            {
                var cropId = StringAt(cropIds, index);
                slots[index] = new IndoorSlot
                {
                    CropId = cropId,
                    GrowthDays = FloatAt(growth, index),
                    Fatigue = FloatAt(fatigue, index),
                    LastCropId = StringAt(lastCropIds, index),
                    Stage = StageAt(stages, index, cropId, ref damaged),
                    Flags = FlagsAt(flags, index, ref damaged),
                };
            }

            return slots;
        }

        // ---------------------------------------------------------------- slot-array value helpers

        /// <summary>Value at a slot index, or the empty-crop default when the list is truncated.</summary>
        private static string StringAt(List<string> values, int index)
        {
            return index < values.Count ? values[index] : string.Empty;
        }

        /// <summary>Value at a slot index, or 0 when the list is truncated.</summary>
        private static float FloatAt(List<float> values, int index)
        {
            return index < values.Count ? values[index] : 0f;
        }

        /// <summary>
        /// Stage at a slot index: a missing/truncated list derives Growing for a planted slot and
        /// Empty otherwise (never silently drops a persisted crop); an undefined value is damage.
        /// </summary>
        private static SlotStage StageAt(List<int> stages, int index, string cropId, ref bool damaged)
        {
            if (index >= stages.Count)
            {
                return cropId.Length > 0 ? SlotStage.Growing : SlotStage.Empty;
            }

            var raw = stages[index];
            if (raw == (int)SlotStage.Empty || raw == (int)SlotStage.Growing || raw == (int)SlotStage.Mature)
            {
                return (SlotStage)raw;
            }

            damaged = true;
            return cropId.Length > 0 ? SlotStage.Growing : SlotStage.Empty;
        }

        /// <summary>Flag bits at a slot index clamped to a byte; the validator keeps the known bits only.</summary>
        private static byte FlagsAt(List<int> flags, int index, ref bool damaged)
        {
            if (index >= flags.Count)
            {
                return 0;
            }

            var raw = flags[index];
            if (raw < 0 || raw > byte.MaxValue)
            {
                damaged = true;
            }

            return raw < 0 ? (byte)0 : raw > byte.MaxValue ? byte.MaxValue : (byte)raw;
        }
    }
}
