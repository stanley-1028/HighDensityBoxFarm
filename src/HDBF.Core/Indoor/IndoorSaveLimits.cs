// ============================================================================
// HDBF v2 | Layer: Core/Indoor | Owner: V2-CORE-02
// Purpose: Every numeric/text limit of the schema-v2 save path (array bounds,
//          string caps, clamp ranges, adapter whitelist, size budget) in one place.
//          Holds constants only; no encoding, validation or migration logic.
// Requirements: REQ-V2-SAVE-01, REQ-V2-SAVE-04, REQ-V2-EXT-03, REQ-V2-READ-02
// ============================================================================
using HDBF.Core.Balance;

namespace HDBF.Core.Indoor
{
    /// <summary>
    /// Hard limits of the schema-v2 indoor save (INTERFACES section 8, REQ-V2-SAVE-04). The save
    /// codec, the validator and the migrator read these names instead of literals, so a limit can
    /// be changed in exactly one place (REQ-V2-EXT-03: new fields get defaults, old records stay
    /// readable). Namespace is <c>HDBF.Core.Indoor</c> on purpose: the save types are part of the
    /// frozen Core contract in INTERFACES section 8.
    /// </summary>
    public static class IndoorSaveLimits
    {
        // ------------------------------------------------------------ schema versions

        /// <summary>Schema written by <see cref="IndoorSaveCodec"/>; same as the record constant.</summary>
        public const int CurrentSchemaVersion = IndoorFarmRecord.CurrentSchemaVersion;

        /// <summary>v1 is the first shipped schema and the only migration source (REQ-V2-SAVE-02).</summary>
        public const int LegacySchemaVersion = 1;

        // ------------------------------------------------------------ array bounds

        /// <summary>
        /// Slots the codec reads when the persisted tier is unknown: the largest tier capacity
        /// (REQ-V2-SAVE-06: never truncate on load, the validator trims against the real tier).
        /// </summary>
        public const int MaxDecodedSlots = TierCatalog.MaxPlotCapacity;

        /// <summary>
        /// Entries the codec accepts in the crop dictionary. A faction table has at most ten crops;
        /// the bound only keeps a corrupted save from allocating an unbounded table.
        /// </summary>
        public const int MaxCropTableEntries = 256;

        /// <summary>Characters accepted by <see cref="IndoorSaveCodec.TryDecode"/> (DoS bound).</summary>
        public const int MaxDecodedTextChars = 2000000;

        // ------------------------------------------------------------ string caps

        /// <summary>Maximum persisted entity id length; longer ids are truncated and reported.</summary>
        public const int MaxEntityIdLength = 128;

        /// <summary>Maximum persisted crop id length; longer ids can never be a valid faction crop.</summary>
        public const int MaxCropIdLength = 64;

        /// <summary>Maximum persisted adapter state length (only the five whitelisted states are kept).</summary>
        public const int MaxAdapterStateLength = 32;

        // ------------------------------------------------------------ adapter whitelist

        /// <summary>Adapter map key of the Timber Pipes water adapter (INTERFACES section 8).</summary>
        public const string AdapterTimberPipes = "TimberPipes";

        /// <summary>Adapter never talked to the farm (safe default of a missing adapter field).</summary>
        public const string AdapterStateNone = IndoorConstants.AdapterStateNone;

        /// <summary>Adapter detected and working.</summary>
        public const string AdapterStateOk = "Ok";

        /// <summary>Adapter present but degraded (water/ports may still work).</summary>
        public const string AdapterStateDegraded = "Degraded";

        /// <summary>Adapter deliberately disabled by the player/config.</summary>
        public const string AdapterStateDisabled = "Disabled";

        /// <summary>Adapter failed detection or initialization.</summary>
        public const string AdapterStateFailed = "Failed";

        // ------------------------------------------------------------ clamp ranges

        /// <summary>Slot flag bits the schema knows (bit 0 = reserved by a worker); others are cleared.</summary>
        public const byte AllowedSlotFlags = IndoorConstants.SlotFlagReservedByWorker;

        /// <summary>
        /// Upper sanity bound of a persisted maintenance due day (game days). A value above it is a
        /// corrupted record, not a long-running colony: it is clamped and reported.
        /// </summary>
        public const float MaxMaintenanceDueDay = 100000f;

        /// <summary>Upper sanity bound of persisted missed maintenance cycles (0..1000).</summary>
        public const int MaxMaintenanceCyclesMissed = 1000;

        // ------------------------------------------------------------ size / time budget

        /// <summary>Per-building save budget from section 19.10 and section 19.13 (14 KB).</summary>
        public const int BudgetBytesPerBuilding = 14 * 1024;

        /// <summary>Budget of 100 top-tier buildings (section 19.10: 1.2-1.5 MB).</summary>
        public const int BudgetBytesForHundredBuildings = 1536 * 1024;

        /// <summary>Serializer budget of one 100-building save (section 19.13: 50 ms target).</summary>
        public const int BudgetSerializeMilliseconds = 50;

        // ------------------------------------------------------------ repair summary

        /// <summary>Maximum notes carried by one <see cref="IndoorRepairResult"/> (bounded report).</summary>
        public const int MaxRepairNotes = 16;

        /// <summary>Maximum length of one diagnostic/error/note string produced by the save layer.</summary>
        public const int MaxDiagnosticLength = 256;
    }
}
