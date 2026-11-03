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

        /// <summary>The player paused the building; daily tick, water drain and icons stopped.</summary>
        public const string Paused = "indoor.paused";

        /// <summary>The player resumed the building; daily tick, water drain and icons restarted.</summary>
        public const string Resumed = "indoor.resumed";

        /// <summary>Automatic maintenance paid one catch-up bill and advanced the due day (WP-02).</summary>
        public const string MaintenanceAuto = "indoor.maintenance-auto";

        /// <summary>Automatic maintenance was due but the material stockpile could not pay it (WP-02).</summary>
        public const string MaintenanceAutoMissing = "indoor.maintenance-auto-missing";

        /// <summary>The 1 Hz session singleton reconciled residual enterers of paused farms (RE-04 R1).</summary>
        public const string PausedReconcile = "indoor.paused.reconcile";

        /// <summary>A beaver walked into a blocked paused farm and was ejected immediately (RE-04 R2).</summary>
        public const string PausedResidualEnterer = "indoor.paused.residual-enterer";

        /// <summary>One paused-farm ejection pass finished; carries requested/ejected/skipped/remaining.</summary>
        public const string PausedEjectSummary = "indoor.paused.eject-summary";

        /// <summary>An ejection could not be confirmed and needs the force-remove fallback (RE-04 R3/R4).</summary>
        public const string PausedEjectFailed = "indoor.paused.eject-failed";

        /// <summary>The force-remove fallback removed the remaining enterers of a paused farm (RE-04 R3).</summary>
        public const string PausedForceRemove = "indoor.paused.force-remove";

        /// <summary>The farm was already blocked when it was paused; a second blocker was added (RE-04 R6).</summary>
        public const string PausedAlreadyBlocked = "indoor.paused.already-blocked";

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

        /// <summary>Throttled read-only pipe probe result (tank/network/water counts and reason).</summary>
        public const string PipeProbe = "water.pipe-probe";

        /// <summary>One settlement-wide manual-refill pass finished (WP-08; carries requested/refilled/skipped).</summary>
        public const string RefillAll = "water.refill-all";

        // ---------------------------------------------------------------- feature.*
        // Lead-owned keys for the second repair round; indoor.* keeps every gameplay diagnostic.

        /// <summary>
        /// A config feature flag turned a gameplay feature off (L-18); carries the flag name and the
        /// affected behaviour so the log explains why the farm behaves differently from the docs.
        /// </summary>
        public const string FeatureDisabled = "indoor.feature-disabled";

        /// <summary>
        /// Read-only startup probe of the beaver work-behavior decision method (WP-07/RE-03): it
        /// records whether a third-party patch replaced it, without registering any patch.
        /// </summary>
        public const string BehaviorProbe = "indoor.behavior-probe";

        /// <summary>The startup probe found a third-party replacement of the beaver behavior method (WP-07).</summary>
        public const string BehaviorProbePatched = "indoor.behavior-probe-patched";

        // ---------------------------------------------------------------- save.*

        /// <summary>One farm record was written successfully.</summary>
        public const string SaveWrite = "save.write";

        /// <summary>
        /// A repaired record was written as the repaired copy instead of being refused (L-04); carries
        /// the repair summary and notes.
        /// </summary>
        public const string SaveWriteRepaired = "save.write-repaired";

        /// <summary>
        /// The pre-repair rollback copy of a repaired write was kept under the PreviousPayload key
        /// (L-04); emitted only when that copy really exists.
        /// </summary>
        public const string SavePreviousSnapshot = "save.previous-snapshot";

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
