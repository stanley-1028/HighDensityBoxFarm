// ============================================================================
// HDBF v2 | Layer: Core/Indoor | Owner: V2-CORE-02
// Purpose: The slot-array part of the pre-write validation (section 19.18.3-A2):
//          length == SlotCapacity (trim/pad), crop/lastCrop ids, growth/fatigue ranges,
//          stage state machine consistency and slot flag bits. Called by IndoorSaveValidator.
// Requirements: REQ-V2-SAVE-04, REQ-V2-SAVE-06, REQ-V2-CORE-02
// ============================================================================
using System;
using System.Globalization;
using HDBF.Core.Balance;

namespace HDBF.Core.Indoor
{
    /// <summary>
    /// Repairs one record's slot array in place (REQ-V2-CORE-02: length must equal
    /// <c>SlotCapacity</c>; a mismatch is trimmed/padded and reported, never thrown). Every slot
    /// rule is conservative: an unusable crop becomes an empty slot, progress is clamped to the
    /// crop growth time, and the persisted fatigue is kept because it is player state.
    /// </summary>
    internal static class IndoorSlotRepair
    {
        /// <summary>Enforces capacity and repairs every slot; never throws.</summary>
        /// <param name="record">Record whose <see cref="IndoorFarmRecord.Slots"/> is fixed in place.</param>
        /// <param name="tier">Tier profile that supplies the authoritative slot capacity.</param>
        /// <param name="catalog">Crop catalog (fallback table second); may be null.</param>
        /// <param name="accumulator">Repair summary collector.</param>
        internal static void Repair(IndoorFarmRecord record, TierProfile tier, ICropCatalog? catalog,
            IndoorRepairAccumulator accumulator)
        {
            // REQ-V2-CORE-02: the persisted array must equal SlotCapacity; mismatch is trimmed/padded.
            var capacity = tier.SlotCapacity;
            var slots = record.Slots;
            if (slots is null)
            {
                record.Slots = EmptySlots(capacity);
                accumulator.AddPadded(capacity);
                accumulator.Note("slots-missing:" + capacity.ToString(CultureInfo.InvariantCulture));
                return;
            }

            if (slots.Length > capacity)
            {
                var trimmed = new IndoorSlot[capacity];
                Array.Copy(slots, trimmed, capacity);
                accumulator.AddTrimmed(slots.Length - capacity);
                accumulator.Note("slots-trimmed:" + slots.Length.ToString(CultureInfo.InvariantCulture) + "->" +
                    capacity.ToString(CultureInfo.InvariantCulture));
                record.Slots = trimmed;
                slots = trimmed;
            }
            else if (slots.Length < capacity)
            {
                var padded = new IndoorSlot[capacity];
                Array.Copy(slots, padded, slots.Length);
                for (var index = slots.Length; index < capacity; index++)
                {
                    padded[index] = new IndoorSlot();
                }

                accumulator.AddPadded(capacity - slots.Length);
                accumulator.Note("slots-padded:" + slots.Length.ToString(CultureInfo.InvariantCulture) + "->" +
                    capacity.ToString(CultureInfo.InvariantCulture));
                record.Slots = padded;
                slots = padded;
            }

            for (var index = 0; index < slots.Length; index++)
            {
                var slot = slots[index];
                if (slot is null)
                {
                    slots[index] = new IndoorSlot();
                    accumulator.Clamp(Prefix(index) + "missing");
                    continue;
                }

                try
                {
                    RepairSlot(slot, index, record.Faction, catalog, accumulator);
                }
                catch (Exception exception)
                {
                    // REQ-V2-SAFE-02: one broken slot must not stop the repair of the others.
                    slots[index] = new IndoorSlot();
                    accumulator.Clamp(Prefix(index) + "exception:" + exception.GetType().Name);
                }
            }
        }

        private static void RepairSlot(IndoorSlot slot, int index, HdbfFaction faction, ICropCatalog? catalog,
            IndoorRepairAccumulator accumulator)
        {
            var prefix = Prefix(index);

            var flags = (byte)(slot.Flags & IndoorSaveLimits.AllowedSlotFlags);
            if (flags != slot.Flags)
            {
                accumulator.Clamp(prefix + "flags:" + slot.Flags.ToString(CultureInfo.InvariantCulture) + "->" +
                    flags.ToString(CultureInfo.InvariantCulture));
                slot.Flags = flags;
            }

            slot.Fatigue = ClampFatigue(slot.Fatigue, prefix + "fatigue", accumulator);

            if (!IndoorMath.IsFinite(slot.GrowthDays))
            {
                accumulator.Clamp(prefix + "growth-nonfinite");
                slot.GrowthDays = 0f;
            }
            else if (slot.GrowthDays < 0f)
            {
                accumulator.Clamp(prefix + "growth-negative:" + Format(slot.GrowthDays));
                slot.GrowthDays = 0f;
            }

            RepairLastCrop(slot, prefix, faction, catalog, accumulator);
            RepairCurrentCrop(slot, prefix, faction, catalog, accumulator);
        }

        private static void RepairLastCrop(IndoorSlot slot, string prefix, HdbfFaction faction,
            ICropCatalog? catalog, IndoorRepairAccumulator accumulator)
        {
            var last = slot.LastCropId;
            if (last is null)
            {
                accumulator.Clamp(prefix + "last-crop-null");
                slot.LastCropId = string.Empty;
                return;
            }

            if (last.Length == 0)
            {
                return;
            }

            if (last.Length > IndoorSaveLimits.MaxCropIdLength
                || !IndoorSaveFieldRepair.IsKnownCrop(catalog, faction, last))
            {
                accumulator.Clamp(prefix + "last-crop-invalid:" + last);
                slot.LastCropId = string.Empty;
            }
        }

        private static void RepairCurrentCrop(IndoorSlot slot, string prefix, HdbfFaction faction,
            ICropCatalog? catalog, IndoorRepairAccumulator accumulator)
        {
            var cropId = slot.CropId;
            if (cropId is null)
            {
                accumulator.Clamp(prefix + "crop-null");
                cropId = string.Empty;
                slot.CropId = string.Empty;
            }

            var growthTime = 0f;
            var cropKnown = false;
            if (cropId.Length > 0)
            {
                if (cropId.Length > IndoorSaveLimits.MaxCropIdLength)
                {
                    accumulator.Clamp(prefix + "crop-too-long");
                }
                else if (!TryGrowthTime(catalog, faction, cropId, out growthTime))
                {
                    accumulator.Clamp(prefix + "crop-unknown:" + cropId);
                }
                else
                {
                    cropKnown = true;
                }
            }

            if (!cropKnown)
            {
                ClearSlot(slot, prefix, accumulator);
                return;
            }

            var growth = slot.GrowthDays;
            if (growth > growthTime)
            {
                accumulator.Clamp(prefix + "growth-clamped:" + Format(growth) + "->" + Format(growthTime));
                growth = growthTime;
            }

            var stage = slot.Stage;
            if (stage != SlotStage.Empty && stage != SlotStage.Growing && stage != SlotStage.Mature)
            {
                accumulator.Clamp(prefix + "stage-invalid:" + ((int)stage).ToString(CultureInfo.InvariantCulture));
                stage = growth >= growthTime ? SlotStage.Mature : SlotStage.Growing;
            }

            if (stage == SlotStage.Empty)
            {
                accumulator.Clamp(prefix + "stage-empty-with-crop");
                stage = growth >= growthTime ? SlotStage.Mature : SlotStage.Growing;
            }

            if (stage == SlotStage.Mature && growth < growthTime)
            {
                accumulator.Clamp(prefix + "mature-progress:" + Format(growth) + "->" + Format(growthTime));
                growth = growthTime;
            }
            else if (stage != SlotStage.Mature && growth >= growthTime)
            {
                accumulator.Clamp(prefix + "mature-by-progress");
                stage = SlotStage.Mature;
                growth = growthTime;
            }

            slot.GrowthDays = growth;
            slot.Stage = stage;
        }

        private static void ClearSlot(IndoorSlot slot, string prefix, IndoorRepairAccumulator accumulator)
        {
            if (slot.CropId.Length > 0)
            {
                slot.CropId = string.Empty;
            }

            if (slot.Stage != SlotStage.Empty)
            {
                accumulator.Clamp(prefix + "stage:" + StageName(slot.Stage) + "->Empty");
                slot.Stage = SlotStage.Empty;
            }

            if (slot.GrowthDays != 0f)
            {
                accumulator.Clamp(prefix + "growth-cleared");
                slot.GrowthDays = 0f;
            }
        }

        private static float ClampFatigue(float fatigue, string prefix, IndoorRepairAccumulator accumulator)
        {
            if (!IndoorMath.IsFinite(fatigue))
            {
                accumulator.Clamp(prefix + "-nonfinite");
                return 0f;
            }

            if (fatigue < 0f)
            {
                accumulator.Clamp(prefix + "-negative:" + Format(fatigue));
                return 0f;
            }

            if (fatigue > IndoorConstants.MaxFatigue)
            {
                accumulator.Clamp(prefix + "-clamped:" + Format(fatigue));
                return IndoorConstants.MaxFatigue;
            }

            return fatigue;
        }

        private static bool TryGrowthTime(ICropCatalog? catalog, HdbfFaction faction, string cropId,
            out float growthTime)
        {
            growthTime = 0f;
            if (!FallbackCropCatalog.TryResolve(catalog, faction, cropId, out var crop) || crop is null)
            {
                return false;
            }

            if (!IndoorMath.IsPositiveFinite(crop.GrowthTimeInDays))
            {
                return false;
            }

            growthTime = crop.GrowthTimeInDays;
            return true;
        }

        private static IndoorSlot[] EmptySlots(int capacity)
        {
            var slots = new IndoorSlot[capacity];
            for (var index = 0; index < capacity; index++)
            {
                slots[index] = new IndoorSlot();
            }

            return slots;
        }

        private static string Prefix(int index) => "slot[" + index.ToString(CultureInfo.InvariantCulture) + "].";

        private static string Format(float value) => IndoorSaveFormat.FormatFloat(value);

        private static string StageName(SlotStage stage)
        {
            return stage == SlotStage.Empty ? "Empty"
                : stage == SlotStage.Growing ? "Growing"
                : stage == SlotStage.Mature ? "Mature"
                : ((int)stage).ToString(CultureInfo.InvariantCulture);
        }
    }
}
