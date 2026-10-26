// ============================================================================
// HDBF v2 | Layer: Core/Indoor | Owner: V2-CORE-02
// Purpose: Deterministic schema-v2 text codec of one indoor farm record: Encode,
//          tolerant TryDecode and MeasureBytes (INTERFACES section 8). Slot payload lives
//          in IndoorSaveSlotCodec; validation/migration live in their own files.
// Requirements: REQ-V2-SAVE-01, REQ-V2-SAVE-03, REQ-V2-SAVE-06, REQ-V2-EXT-03
// ============================================================================
// Namespace: HDBF.Core.Indoor (frozen INTERFACES section 8 contract; no .Save sub-namespace).
using System;
using System.Text;
using HDBF.Core.Balance;
using HDBF.Core.Json;

namespace HDBF.Core.Indoor
{
    /// <summary>
    /// Deterministic text form of the persisted schema-v2 indoor farm record (INTERFACES section 8,
    /// REQ-V2-SAVE-01). The shape follows the section 19.10 building node: record fields first
    /// (<c>schemaVersion</c>, <c>entityId</c>, <c>tier</c>, <c>faction</c>, <c>mode</c>,
    /// <c>cropPolicy</c>, <c>fixedCrop</c>, <c>tank</c>, <c>maintenance</c>, <c>power</c>,
    /// <c>ports</c>, <c>adapters</c>), then the interned <c>crops</c> table and the compact
    /// <c>slots</c> rows. Field order and float text are fixed, nothing is time- or
    /// culture-dependent, so <c>Encode</c> is byte-stable across machines and repeated passes.
    /// </summary>
    public static class IndoorSaveCodec
    {
        /// <summary>Schema version this codec writes (INTERFACES section 8, section 19.10).</summary>
        public const int SchemaVersion = IndoorSaveLimits.CurrentSchemaVersion;

        /// <summary>StringBuilder floor for one encoded record (header fields + crop table).</summary>
        private const int EstimateBaseChars = 512;

        /// <summary>Upper estimate of one slot row in the compact encoding (usually ~20 chars).</summary>
        private const int EstimateCharsPerSlot = 24;

        /// <summary>Cap of the pre-sized builder; a damaged record must not pre-allocate for it.</summary>
        private const int EstimateMaxChars = 65536;

        /// <summary>
        /// Encodes one record into the canonical schema-v2 text (REQ-V2-SAVE-01). The method is
        /// total: a null record returns the empty string and non-finite floats are written as
        /// <c>0</c> (callers must run <see cref="IndoorSaveValidator"/> before persisting).
        /// </summary>
        /// <param name="record">Record to encode; null returns <see cref="string.Empty"/>.</param>
        /// <returns>Deterministic UTF-16 text of the record.</returns>
        public static string Encode(IndoorFarmRecord record)
        {
            if (record is null)
            {
                return string.Empty;
            }

            // REQ-V2-SAVE-01: fixed field order and fixed float text; no timestamp/culture input.
            var slotCount = record.Slots is null ? 0 : record.Slots.Length;
            var builder = new StringBuilder(EstimateCapacity(slotCount));
            builder.Append('{');
            IndoorSaveFormat.AppendInt(builder, "schemaVersion", record.SchemaVersion, true);
            IndoorSaveFormat.AppendString(builder, "entityId", record.EntityId, false);
            IndoorSaveFormat.AppendString(builder, "tier", IndoorSaveEnums.TierName(record.Tier), false);
            IndoorSaveFormat.AppendString(builder, "faction", IndoorSaveEnums.FactionName(record.Faction), false);
            IndoorSaveFormat.AppendString(builder, "mode", IndoorSaveEnums.ModeName(record.Mode), false);
            IndoorSaveFormat.AppendString(builder, "cropPolicy",
                IndoorSaveEnums.CropPolicyName(record.CropPolicy), false);
            IndoorSaveFormat.AppendString(builder, "fixedCrop", record.FixedCropId, false);
            IndoorSaveFormat.AppendFloat(builder, "tank", record.Tank, false);

            IndoorSaveFormat.StartField(builder, "maintenance", false);
            builder.Append('{');
            IndoorSaveFormat.AppendFloat(builder, "dueDay", record.MaintenanceDueDay, true);
            IndoorSaveFormat.AppendInt(builder, "cyclesMissed", record.MaintenanceCyclesMissed, false);
            builder.Append('}');

            IndoorSaveFormat.StartField(builder, "power", false);
            builder.Append('{');
            IndoorSaveFormat.AppendBool(builder, "connected", record.PowerConnected, true);
            IndoorSaveFormat.AppendString(builder, "mode",
                IndoorSaveEnums.PowerModeName(record.PowerMode), false);
            builder.Append('}');

            IndoorSaveFormat.StartField(builder, "ports", false);
            builder.Append('{');
            IndoorSaveFormat.AppendBool(builder, "pipe", record.PipePort, true);
            IndoorSaveFormat.AppendBool(builder, "logistics", record.LogisticsPort, false);
            IndoorSaveFormat.AppendBool(builder, "power", record.PowerPort, false);
            builder.Append('}');

            IndoorSaveFormat.StartField(builder, "adapters", false);
            builder.Append('{');
            IndoorSaveFormat.AppendString(builder, IndoorSaveLimits.AdapterTimberPipes,
                record.TimberPipesAdapter, true);
            builder.Append('}');

            IndoorSaveSlotCodec.Write(builder, record.Slots);
            builder.Append('}');
            return builder.ToString();
        }

        /// <summary>
        /// Tolerant reader of the schema-v2 text (REQ-V2-SAVE-06). A missing field, a wrong-typed
        /// value, an unknown field or an unknown crop code is defaulted and recorded, never thrown;
        /// the method returns false only when the text is not a usable JSON object at all.
        /// </summary>
        /// <param name="text">Persisted text; null/empty fails cleanly.</param>
        /// <param name="record">Decoded record when the method returns true; null otherwise.</param>
        /// <param name="error">Empty on a clean decode, otherwise the repair summary or fatal reason.</param>
        /// <returns>True when a record could be produced.</returns>
        public static bool TryDecode(string text, out IndoorFarmRecord record, out string error)
        {
            // REQ-V2-SAVE-06: bad fields are defaulted and recorded, never thrown into the load path.
            record = null!;
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(text))
            {
                error = "empty";
                return false;
            }

            if (text.Length > IndoorSaveLimits.MaxDecodedTextChars)
            {
                error = "too-large";
                return false;
            }

            try
            {
                if (!JsonParser.TryParse(text, out var root) || root is null || root.Kind != JsonValueKind.Object)
                {
                    error = "not-json-object";
                    return false;
                }

                var notes = new IndoorSaveDecodeNotes();
                record = ReadRecord(root, notes);
                error = notes.Build();
                return true;
            }
            catch (Exception exception)
            {
                record = null!;
                error = "decode-exception:" + exception.GetType().Name;
                return false;
            }
        }

        /// <summary>
        /// UTF-8 byte size of the encoded record: the actual on-disk weight of one building entry,
        /// used by the 100-building budget test (section 19.10, REQ-V2-SAVE-07).
        /// </summary>
        /// <param name="record">Record to measure; null measures as 0 bytes.</param>
        /// <returns>UTF-8 byte count of <see cref="Encode"/>.</returns>
        public static int MeasureBytes(IndoorFarmRecord record)
        {
            var text = Encode(record);
            return text.Length == 0 ? 0 : Encoding.UTF8.GetByteCount(text);
        }

        private static IndoorFarmRecord ReadRecord(JsonValue root, IndoorSaveDecodeNotes notes)
        {
            var record = new IndoorFarmRecord();
            if (IndoorSaveFormat.TryReadInt(root, "schemaVersion", out var schemaVersion))
            {
                record.SchemaVersion = schemaVersion;
            }
            else
            {
                record.SchemaVersion = IndoorSaveLimits.CurrentSchemaVersion;
                notes.Add("schemaVersion-default");
            }

            record.EntityId = ReadString(root, "entityId", "entityId-default", notes);
            if (record.EntityId.Length > IndoorSaveLimits.MaxEntityIdLength)
            {
                record.EntityId = record.EntityId.Substring(0, IndoorSaveLimits.MaxEntityIdLength);
                notes.Add("entityId-truncated");
            }

            record.Tier = IndoorSaveEnums.ParseTier(ReadString(root, "tier", "tier-default", notes));
            record.Faction = IndoorSaveEnums.ParseFaction(ReadString(root, "faction", "faction-default", notes));
            record.Mode = IndoorSaveEnums.ParseMode(ReadString(root, "mode", "mode-default", notes));
            record.CropPolicy = IndoorSaveEnums.ParseCropPolicy(
                ReadString(root, "cropPolicy", "cropPolicy-default", notes));
            record.FixedCropId = ReadString(root, "fixedCrop", "fixedCrop-default", notes);
            record.Tank = ReadFloat(root, "tank", "tank-default", notes);

            var maintenance = ObjectMember(root, "maintenance", "maintenance-default", notes);
            record.MaintenanceDueDay = ReadFloat(maintenance, "dueDay", "maintenance.dueDay-default", notes);
            record.MaintenanceCyclesMissed =
                ReadInt(maintenance, "cyclesMissed", "maintenance.cyclesMissed-default", notes);

            var power = ObjectMember(root, "power", "power-default", notes);
            record.PowerConnected = ReadBool(power, "connected", "power.connected-default", notes);
            var powerModeText = ReadString(power, "mode", "power.mode-default", notes);
            record.PowerMode = string.IsNullOrEmpty(powerModeText)
                ? (IsPowerTier(record.Tier) ? PowerConnection.Grid : PowerConnection.NotRequired)
                : IndoorSaveEnums.ParsePowerMode(powerModeText);

            var ports = ObjectMember(root, "ports", "ports-default", notes);
            record.PipePort = ReadBool(ports, "pipe", "ports.pipe-default", notes);
            record.LogisticsPort = ReadBool(ports, "logistics", "ports.logistics-default", notes);
            record.PowerPort = ReadBool(ports, "power", "ports.power-default", notes);

            var adapters = ObjectMember(root, "adapters", "adapters-default", notes);
            record.TimberPipesAdapter =
                ReadString(adapters, IndoorSaveLimits.AdapterTimberPipes, "adapters.timberPipes-default", notes);
            if (string.IsNullOrEmpty(record.TimberPipesAdapter))
            {
                record.TimberPipesAdapter = IndoorSaveLimits.AdapterStateNone;
            }

            record.Slots = IndoorSaveSlotCodec.Read(root, CapacityOf(record.Tier), notes);
            return record;
        }

        private static int CapacityOf(HdbfTier tier)
        {
            return Enum.IsDefined(typeof(HdbfTier), tier)
                ? TierCatalog.Get(tier).SlotCapacity
                : IndoorSaveLimits.MaxDecodedSlots;
        }

        private static bool IsPowerTier(HdbfTier tier)
        {
            return Enum.IsDefined(typeof(HdbfTier), tier) && TierCatalog.Get(tier).NeedsPower;
        }

        private static JsonValue? ObjectMember(JsonValue root, string name, string note,
            IndoorSaveDecodeNotes notes)
        {
            if (IndoorSaveFormat.TryGetMember(root, name, out var value) && value.Kind == JsonValueKind.Object)
            {
                return value;
            }

            notes.Add(note);
            return null;
        }

        private static string ReadString(JsonValue? parent, string name, string note, IndoorSaveDecodeNotes notes)
        {
            if (IndoorSaveFormat.TryReadString(parent, name, out var value))
            {
                return value;
            }

            notes.Add(note);
            return string.Empty;
        }

        private static float ReadFloat(JsonValue? parent, string name, string note, IndoorSaveDecodeNotes notes)
        {
            if (IndoorSaveFormat.TryReadFloat(parent, name, out var value))
            {
                return value;
            }

            notes.Add(note);
            return 0f;
        }

        private static int ReadInt(JsonValue? parent, string name, string note, IndoorSaveDecodeNotes notes)
        {
            if (IndoorSaveFormat.TryReadInt(parent, name, out var value))
            {
                return value;
            }

            notes.Add(note);
            return 0;
        }

        private static bool ReadBool(JsonValue? parent, string name, string note, IndoorSaveDecodeNotes notes)
        {
            if (IndoorSaveFormat.TryReadBool(parent, name, out var value))
            {
                return value;
            }

            notes.Add(note);
            return false;
        }

        private static int EstimateCapacity(int slotCount)
        {
            var estimate = EstimateBaseChars + (long)slotCount * EstimateCharsPerSlot;
            return estimate > EstimateMaxChars ? EstimateMaxChars : (int)estimate;
        }
    }
}
