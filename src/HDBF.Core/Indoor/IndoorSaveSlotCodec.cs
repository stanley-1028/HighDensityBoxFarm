// ============================================================================
// HDBF v2 | Layer: Core/Indoor | Owner: V2-CORE-02
// Purpose: The slot part of the schema-v2 save text: deterministic crop dictionary,
//          compact slot rows [cropCode,growth,fatigue,lastCropCode,stage,flags] and the
//          tolerant reader for them. Does not touch record-level fields (IndoorSaveCodec).
// Requirements: REQ-V2-SAVE-01, REQ-V2-SAVE-06, REQ-V2-CORE-02
// ============================================================================
using System;
using System.Collections.Generic;
using System.Text;
using HDBF.Core.Json;

namespace HDBF.Core.Indoor
{
    /// <summary>
    /// Bounded, non-throwing repair/read notes of <see cref="IndoorSaveCodec.TryDecode"/>.
    /// The decoder keeps reading after a bad field (REQ-V2-SAVE-06) and reports what it defaulted,
    /// so <c>save.*</c> logging can name the damaged node instead of silently swallowing it.
    /// </summary>
    internal sealed class IndoorSaveDecodeNotes
    {
        private readonly List<string> _notes = new List<string>(4);
        private int _suppressed;

        /// <summary>Adds one note, counting (but not storing) notes beyond the bounded report.</summary>
        internal void Add(string note)
        {
            if (_notes.Count < IndoorSaveLimits.MaxRepairNotes)
            {
                _notes.Add(note);
            }
            else
            {
                _suppressed++;
            }
        }

        /// <summary>True when at least one field was defaulted or repaired while reading.</summary>
        internal bool Any => _notes.Count > 0 || _suppressed > 0;

        /// <summary>Joined, length-capped diagnostic text; empty for a clean decode.</summary>
        internal string Build()
        {
            if (!Any)
            {
                return string.Empty;
            }

            var text = string.Join(";", _notes);
            if (_suppressed > 0)
            {
                text = text.Length == 0
                    ? "+" + _suppressed + " more"
                    : text + ";+" + _suppressed + " more";
            }

            return text.Length <= IndoorSaveLimits.MaxDiagnosticLength
                ? text
                : text.Substring(0, IndoorSaveLimits.MaxDiagnosticLength);
        }
    }

    /// <summary>
    /// Writes and reads the <c>crops</c>/<c>slots</c> members of one schema-v2 record. Crop ids are
    /// interned once and slot rows reference them by index: section 19.10 allows dictionary
    /// interning to stay inside the 14 KB/building band without per-slot field-name overhead.
    /// </summary>
    internal static class IndoorSaveSlotCodec
    {
        /// <summary>Appends <c>,"crops":[...],"slots":[...]</c> in a fixed, deterministic order.</summary>
        internal static void Write(StringBuilder builder, IndoorSlot[]? slots)
        {
            var count = slots is null ? 0 : slots.Length;
            var codebook = new CropCodebook();
            for (var index = 0; index < count; index++)
            {
                var slot = slots![index];
                if (slot is null)
                {
                    continue;
                }

                codebook.CodeFor(slot.CropId);
                codebook.CodeFor(slot.LastCropId);
            }

            builder.Append(",\"crops\":[");
            var cropCount = codebook.Count;
            for (var index = 0; index < cropCount; index++)
            {
                if (index > 0)
                {
                    builder.Append(',');
                }

                IndoorSaveFormat.AppendQuoted(builder, codebook.CropAt(index));
            }
            builder.Append("],\"slots\":[");
            for (var index = 0; index < count; index++)
            {
                if (index > 0)
                {
                    builder.Append(',');
                }

                var slot = slots![index];
                if (slot is null)
                {
                    builder.Append("[0,0,0,0,0,0]");
                    continue;
                }

                builder.Append('[')
                    .Append(codebook.CodeFor(slot.CropId).ToString(System.Globalization.CultureInfo.InvariantCulture))
                    .Append(',')
                    .Append(IndoorSaveFormat.FormatFloat(slot.GrowthDays))
                    .Append(',')
                    .Append(IndoorSaveFormat.FormatFloat(slot.Fatigue))
                    .Append(',')
                    .Append(codebook.CodeFor(slot.LastCropId).ToString(System.Globalization.CultureInfo.InvariantCulture))
                    .Append(',')
                    .Append(((int)slot.Stage).ToString(System.Globalization.CultureInfo.InvariantCulture))
                    .Append(',')
                    .Append(((int)slot.Flags).ToString(System.Globalization.CultureInfo.InvariantCulture))
                    .Append(']');
            }

            builder.Append(']');
        }

        /// <summary>
        /// Reads the slot array tolerantly (REQ-V2-SAVE-06): missing/wrong-typed values default to an
        /// empty slot, unknown crop codes become an empty crop, and the array is bounded by the tier
        /// capacity. The array is never padded here: <see cref="IndoorSaveValidator"/> owns the
        /// "length == Capacity" invariant (REQ-V2-CORE-02), so a short read is reported, not hidden.
        /// </summary>
        internal static IndoorSlot[] Read(JsonValue root, int capacity, IndoorSaveDecodeNotes notes)
        {
            // REQ-V2-SAVE-06: tolerant row reading; the capacity bound is enforced by the caller.
            var crops = ReadCropTable(root, notes);
            if (!IndoorSaveFormat.TryGetMember(root, "slots", out var slotsValue)
                || slotsValue.Kind != JsonValueKind.Array)
            {
                notes.Add("slots-default");
                return Array.Empty<IndoorSlot>();
            }

            var declared = slotsValue.Count;
            var bound = capacity > 0 ? capacity : 0;
            var count = declared < bound ? declared : bound;
            if (declared > bound)
            {
                notes.Add("slots-bounded:" + declared + ">" + bound);
            }

            if (count < bound)
            {
                notes.Add("slots-short:" + count + "<" + bound);
            }

            var slots = new IndoorSlot[count];
            for (var index = 0; index < count; index++)
            {
                var slot = new IndoorSlot();
                slots[index] = slot;
                var row = slotsValue.Items[index];
                if (row.Kind != JsonValueKind.Array)
                {
                    notes.Add("slot[" + index + "].row-default");
                    continue;
                }

                var parts = row.Items;
                var code = ReadIntAt(parts, 0);
                if (code > 0 && code <= crops.Count)
                {
                    slot.CropId = crops[code - 1];
                }
                else if (code != 0)
                {
                    notes.Add("slot[" + index + "].crop-code:" + code);
                }

                slot.GrowthDays = ReadFloatAt(parts, 1);
                slot.Fatigue = ReadFloatAt(parts, 2);
                var lastCode = ReadIntAt(parts, 3);
                if (lastCode > 0 && lastCode <= crops.Count)
                {
                    slot.LastCropId = crops[lastCode - 1];
                }
                else if (lastCode != 0)
                {
                    notes.Add("slot[" + index + "].last-crop-code:" + lastCode);
                }

                slot.Stage = (SlotStage)ReadIntAt(parts, 4);
                var flags = ReadIntAt(parts, 5);
                if (flags < 0 || flags > byte.MaxValue)
                {
                    notes.Add("slot[" + index + "].flags:" + flags);
                    flags = 0;
                }

                slot.Flags = (byte)flags;
            }

            return slots;
        }

        private static List<string> ReadCropTable(JsonValue root, IndoorSaveDecodeNotes notes)
        {
            var crops = new List<string>(4);
            if (!IndoorSaveFormat.TryGetMember(root, "crops", out var cropsValue))
            {
                notes.Add("crops-default");
                return crops;
            }

            if (cropsValue.Kind != JsonValueKind.Array)
            {
                notes.Add("crops-type");
                return crops;
            }

            var declared = cropsValue.Count;
            if (declared > IndoorSaveLimits.MaxCropTableEntries)
            {
                notes.Add("crops-bounded:" + declared);
                declared = IndoorSaveLimits.MaxCropTableEntries;
            }

            for (var index = 0; index < declared; index++)
            {
                var entry = cropsValue.Items[index];
                if (entry.TryGetString(out var cropId) && !string.IsNullOrEmpty(cropId))
                {
                    crops.Add(cropId.Length <= IndoorSaveLimits.MaxCropIdLength
                        ? cropId
                        : cropId.Substring(0, IndoorSaveLimits.MaxCropIdLength));
                }
                else
                {
                    crops.Add(string.Empty);
                    notes.Add("crops[" + index + "]-default");
                }
            }

            return crops;
        }

        private static int ReadIntAt(IReadOnlyList<JsonValue> parts, int index)
        {
            if (index >= parts.Count || !parts[index].TryGetDouble(out var number))
            {
                return 0;
            }

            return IndoorSaveFormat.ToInt(number);
        }

        private static float ReadFloatAt(IReadOnlyList<JsonValue> parts, int index)
        {
            if (index >= parts.Count || !parts[index].TryGetDouble(out var number))
            {
                return 0f;
            }

            return (float)number;
        }

        /// <summary>First-seen-order crop dictionary: code 0 means "no crop", code n maps to entry n-1.</summary>
        private sealed class CropCodebook
        {
            private readonly List<string> _crops = new List<string>(4);
            private readonly Dictionary<string, int> _codes = new Dictionary<string, int>(StringComparer.Ordinal);

            internal int Count => _crops.Count;

            internal string CropAt(int index) => _crops[index];

            internal int CodeFor(string? cropId)
            {
                if (string.IsNullOrEmpty(cropId))
                {
                    return 0;
                }

                if (_codes.TryGetValue(cropId!, out var code))
                {
                    return code;
                }

                _crops.Add(cropId!);
                code = _crops.Count;
                _codes.Add(cropId!, code);
                return code;
            }
        }
    }
}
