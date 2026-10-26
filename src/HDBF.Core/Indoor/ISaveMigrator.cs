// ============================================================================
// HDBF v2 | Layer: Core/Indoor | Owner: V2-CORE-01
// Purpose: v1 -> v2 save migration contract plus the minimal legacy record DTO the
//          signature needs (INTERFACES section 6/8). No migration implementation here.
// Requirements: REQ-V2-SAVE-02, REQ-V2-SAVE-05, REQ-V2-EXT-03
// ============================================================================
using HDBF.Core.Balance;

namespace HDBF.Core.Indoor
{
    /// <summary>
    /// Migration contract of the v1 -> v2 -> v3 save chain (INTERFACES section 6). Implemented by
    /// the persistence card; Core only declares the entry point so callers can be written first.
    /// </summary>
    public interface ISaveMigrator
    {
        /// <summary>
        /// Tries to convert a legacy v1 farm record into a v2 record. A failed migration must return
        /// false (the caller then loads the safe default record) and never throw (REQ-V2-SAVE-05).
        /// </summary>
        /// <param name="legacy">Legacy v1 record read from the old save node.</param>
        /// <param name="tier">Resolved tier profile of the building.</param>
        /// <param name="migrated">Converted v2 record when the method returns true.</param>
        /// <returns>True when a complete v2 record could be produced.</returns>
        bool TryMigrateV1(IndoorLegacyV1Record legacy, TierProfile tier, out IndoorFarmRecord migrated);
    }

    /// <summary>
    /// Minimal read-only DTO of the v1 persisted farm fields needed by
    /// <see cref="ISaveMigrator.TryMigrateV1"/> (INTERFACES section 8, REQ-V2-SAVE-02: the v1
    /// <c>plots.ids/crops/fatigue</c> arrays map onto v2 slots in order). It deliberately carries no
    /// behaviour: the persistence card owns decoding, validation and the actual migration.
    /// </summary>
    public sealed class IndoorLegacyV1Record
    {
        /// <summary>Building entity id of the v1 record; diagnostics only.</summary>
        public string EntityId { get; set; } = string.Empty;

        /// <summary>v1 tier of the building.</summary>
        public HdbfTier Tier { get; set; }

        /// <summary>v1 faction of the building.</summary>
        public HdbfFaction Faction { get; set; }

        /// <summary>v1 mode (the v1 enum is kept verbatim in the legacy record).</summary>
        public HdbfMode Mode { get; set; }

        /// <summary>v1 registered plot ids, in registration order (unused by v2 except for count).</summary>
        public int[] PlotIds { get; set; } = new int[0];

        /// <summary>v1 crop template ids per registered plot; mapped onto the leading v2 slots.</summary>
        public string[] Crops { get; set; } = new string[0];

        /// <summary>v1 per-plot fatigue values; mapped onto the leading v2 slots.</summary>
        public float[] Fatigue { get; set; } = new float[0];

        /// <summary>v1 internal tank content in water units.</summary>
        public float Tank { get; set; }

        /// <summary>v1 maintenance due day; 0 when no material maintenance was scheduled.</summary>
        public float MaintenanceDueDay { get; set; }

        /// <summary>v1 missed maintenance cycles.</summary>
        public int MaintenanceCyclesMissed { get; set; }

        /// <summary>v1 grid power flag of the top tier.</summary>
        public bool PowerConnected { get; set; }

        /// <summary>v1 water pipe port flag.</summary>
        public bool PipePort { get; set; }

        /// <summary>v1 logistics output port flag.</summary>
        public bool LogisticsPort { get; set; }

        /// <summary>v1 power port flag.</summary>
        public bool PowerPort { get; set; }

        /// <summary>Last v1 Timber Pipes adapter state string; empty when absent.</summary>
        public string TimberPipesAdapter { get; set; } = string.Empty;
    }
}
