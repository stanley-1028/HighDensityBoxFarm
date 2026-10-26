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

        /// <summary>One material maintenance cycle was performed (v2 wiring card).</summary>
        public const string Maintenance = "indoor.maintenance";

        /// <summary>The upkeep material is missing; nothing was deducted (section 19.8).</summary>
        public const string MaintenanceMissing = "indoor.maintenance-missing";

        /// <summary>The top tier lost grid power; G_power 0.60 and x2 upkeep apply.</summary>
        public const string PowerCut = "indoor.power-cut";

        /// <summary>The persisted power mode/connection of the top tier was refreshed.</summary>
        public const string Power = "indoor.power";

        /// <summary>A port state degraded (adapter missing/failed); the farm itself keeps running.</summary>
        public const string Port = "indoor.port";

        /// <summary>The front entrance is blocked; farm actions pause until it clears.</summary>
        public const string EntranceBlocked = "indoor.entrance-blocked";

        /// <summary>The crop policy (rotation/fixed) was accepted or rejected.</summary>
        public const string CropPolicy = "indoor.crop-policy";

        // ---------------------------------------------------------------- water.*

        /// <summary>Daily tank settlement: demand, deducted amount, coverage C and G_irr.</summary>
        public const string WaterSettle = "water.settle";

        /// <summary>A pipe or manual refill failed (no source, source unavailable, draw refused).</summary>
        public const string RefillFailed = "water.refill-failed";

        /// <summary>A manual refill trip was requested/performed (2 farm actions, 1/3 tank).</summary>
        public const string ManualRefill = "water.manual-refill";

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
