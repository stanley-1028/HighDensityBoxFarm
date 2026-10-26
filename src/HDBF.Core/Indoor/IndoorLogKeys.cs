// ============================================================================
// HDBF v2 | Layer: Core/Indoor | Owner: V2-CORE-01
// Purpose: Stable v2 log keys (indoor.*/water.*/save.*/ui.*/compat.*) registered in
//          ARCHITECTURE section 6. Keys are a diagnostics contract; never rename.
// Requirements: REQ-V2-READ-03, REQ-V2-READ-02
// ============================================================================
namespace HDBF.Core.Indoor
{
    /// <summary>
    /// Stable log keys of the indoor farm (ARCHITECTURE section 6, REQ-V2-READ-03). The key is
    /// the throttle identity: the same key is emitted at most once per 30 s window, and callers
    /// append the entity id / slot index to the message, never to the key.
    /// </summary>
    public static class IndoorLogKeys
    {
        // ---------------------------------------------------------------- indoor.*

        /// <summary>One completed daily tick summary (workers, actions, coverage, warnings).</summary>
        public const string Day = "indoor.day";

        /// <summary>One slot threw during the tick and was isolated; carries slot index and exception type.</summary>
        public const string SlotFault = "indoor.slot-fault";

        /// <summary>A subsystem was disabled after repeated failures (REQ-V2-SAFE-02).</summary>
        public const string SlotSubsystemDisabled = "indoor.slot-subsystem-disabled";

        /// <summary>Player/command changed the operating mode.</summary>
        public const string Mode = "indoor.mode";

        /// <summary>A crop has no vanilla spec and the built-in fallback table is used (section 19.5).</summary>
        public const string CropSpecMissing = "indoor.crop-spec-missing";

        /// <summary>The daily action budget is exhausted while mature slots wait (backlog).</summary>
        public const string ActionBacklog = "indoor.action-backlog";

        // ---------------------------------------------------------------- water.*

        /// <summary>Daily tank settlement: demand, deducted amount, coverage C and G_irr.</summary>
        public const string WaterSettle = "water.settle";

        /// <summary>A pipe or manual refill failed (no source, source unavailable, draw refused).</summary>
        public const string RefillFailed = "water.refill-failed";

        /// <summary>Coverage C is below the tier minimum C_min (yield is derated).</summary>
        public const string Low = "water.low";

        /// <summary>The pipe is connected but degraded/unavailable; the farm falls back to manual refill.</summary>
        public const string PipeUnavailable = "water.pipe-unavailable";

        // ---------------------------------------------------------------- save.*

        /// <summary>One farm record was written successfully.</summary>
        public const string SaveWrite = "save.write";

        /// <summary>Pre-write validation failed; the old record is kept and the write is retried later.</summary>
        public const string ValidationFailed = "save.validation-failed";

        /// <summary>A v1 record was migrated to schema v2.</summary>
        public const string Migrated = "save.migrated";

        /// <summary>v1 -> v2 migration failed; the building loads in the safe default state.</summary>
        public const string MigratedFailed = "save.migrated-failed";

        // ---------------------------------------------------------------- ui.*

        /// <summary>A panel command failed; carries the concrete failure reason.</summary>
        public const string CommandFailed = "ui.command-failed";

        /// <summary>A panel snapshot was rebuilt (throttled; never emitted above 4 Hz).</summary>
        public const string Snapshot = "ui.snapshot";

        // ---------------------------------------------------------------- compat.*

        /// <summary>An adapter failed its detection/initialization contract (REQ-V2-COMPAT-01).</summary>
        public const string CompatFailed = "compat.failed";

        /// <summary>An adapter is missing or degraded; the port falls back to a safe path.</summary>
        public const string CompatDegraded = "compat.degraded";
    }
}
