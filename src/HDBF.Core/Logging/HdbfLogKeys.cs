namespace HDBF.Core.Logging
{
    /// <summary>
    /// Stable log keys used by every HDBF assembly.
    ///
    /// The key is the throttle identity: per REQ-SAFE-14 / REQ-ARCH-15 the same key is emitted
    /// at most once per 30 s window, and suppressed repeats are counted instead of lost.
    /// Keys are part of the diagnostics contract (players and support paste them), so they
    /// must not be renamed after release.
    /// </summary>
    public static class HdbfLogKeys
    {
        /// <summary>Configuration JSON missing, unreadable or with invalid values; defaults used (chapter 10.3).</summary>
        public const string ConfigInvalid = "config.invalid";

        /// <summary>Configuration file absent; built-in defaults used (chapter 10.3).</summary>
        public const string ConfigMissing = "config.missing";

        /// <summary>Save node degraded: checksum mismatch, migration fallback or per-building repair (chapter 12.4).</summary>
        public const string SaveDegraded = "save.degraded";

        /// <summary>One building record could not be used and was rebuilt from safe defaults (REQ-SAFE-08).</summary>
        public const string SaveRecordReset = "save.record-reset";

        /// <summary>An optional compatibility mod is missing; the port degrades instead of erroring (REQ-COMPAT-09).</summary>
        public const string CompatMissing = "compat.missing";

        /// <summary>An adapter failed its detection/initialization contract (REQ-COMPAT-07).</summary>
        public const string CompatFailed = "compat.failed";

        /// <summary>
        /// An adapter resolved its anchors and is active. Emitted once per adapter under
        /// <c>HDBF/Compat</c>; the compatibility matrix parser in
        /// <c>tools/dev/CompatMatrix.ps1 -CheckLog</c> keys on this prefix, so keep it stable.
        /// </summary>
        public const string CompatEnabled = "compat.enabled";

        /// <summary>A connection port faulted and will be retried (chapter 8.3).</summary>
        public const string PortFault = "port.fault";

        /// <summary>Plot scanning hit its per-frame budget and continues next frame (REQ-SAFE-18).</summary>
        public const string PlotScanBudget = "plot.scan-budget";

        /// <summary>
        /// A completed scan registered no managed plots at all, so irrigation can never be
        /// non-zero (P0 visibility: an empty registry used to be invisible in the panel). The
        /// message carries the scanned/accepted/rejected breakdown and the entity id
        /// (<see cref="HDBF.Core.Plots.PlotScanStats"/>).
        /// </summary>
        public const string PlotScanEmpty = "plot.scan-empty";

        /// <summary>
        /// A completed scan did register plots, but the reachability filter rejected more cells
        /// than the scan accepted; the farm is plot-starved and coverage will be far below its
        /// capacity. Same message shape as <see cref="PlotScanEmpty"/>.
        /// </summary>
        public const string PlotScanReachability = "plot.scan-reachability";

        /// <summary>
        /// One completed scan had per-cell reachability queries throw (round-28 in-game log:
        /// <c>InvalidOperationException: There's no cached flow field at 1187834</c>); those cells
        /// stay unreachable and are excluded from the scan, exactly as a <c>false</c> answer would
        /// be. This is a recoverable pathing-filter event, not a compatibility-adapter failure, so
        /// it must never use <see cref="CompatFailed"/> and must never carry a stack. The
        /// gameplay caller appends the building entity id to the key (per-building throttle) and
        /// emits the summary at most once per completed scan, never once per cell — the round-28
        /// 27x27 Advanced scan flooded the log with one full-stack warning per failed cell.
        /// The message carries entityId, scanned, reachFailures, accepted, the first failed cell
        /// and the exception type (<see cref="HDBF.Core.Plots.PlotScanStats"/>).
        /// </summary>
        public const string ScanReachability = "gameplay.scan-reachability";

        /// <summary>
        /// The crop probe of the retained-plot path threw at least once during a scan. A probe
        /// failure keeps the previously registered plot (never drop a registry on a probe error)
        /// and the scan continues, so this is the same recoverable per-cell class as
        /// <see cref="ScanReachability"/>: one throttled <c>HDBF/Warn</c> per scan per building,
        /// appending the entity id to the key, with the exception type and never a stack.
        /// </summary>
        public const string ScanRetentionProbe = "gameplay.scan-retention-probe";

        /// <summary>
        /// One managed plot's real path query failed (round-9 in-game P0:
        /// <c>InvalidOperationException: There's no cached flow field</c>), so that plot is treated
        /// as unreachable for the current farm plan instead of faulting/aborting the plan
        /// (<see cref="HDBF.Core.Farming.FarmPathProbe"/>). The gameplay caller appends the
        /// building entity id to the key, which makes the 30 s throttle per building: one broken
        /// farm can neither hide another farm's warning nor flood the log with one line per cell.
        /// The message carries entityId, plotId/cell and the exception type (never a stack).
        /// </summary>
        public const string FarmPathUnreachable = "gameplay.path-unreachable";

        /// <summary>Performance protection entered Reduced mode (REQ-SAFE-19).</summary>
        public const string PerfReduced = "perf.reduced";

        /// <summary>
        /// BUILD-05 daily observability line for the managed-crop visual hide: one throttled
        /// <c>HDBF/Info</c> per building per game day carrying the hidden crop count, the restore
        /// failure count and the registered plot count. The gameplay caller appends the building
        /// entity id to the key, so each farm's line is independent.
        /// </summary>
        public const string CropVisibility = "gameplay.crop-visibility";

        /// <summary>
        /// One crop visual could not be hidden/restored (destroyed object, renderer query threw).
        /// Per-cell failure isolation: the sweep keeps going and the next frame retries. The
        /// gameplay caller appends the building entity id to the key so the 30 s throttle is per
        /// building and one broken cell cannot flood the log.
        /// </summary>
        public const string CropVisibilityFailed = "gameplay.crop-visibility-failed";

        /// <summary>
        /// The vanilla floating-status-icon components (<c>StatusSubject</c>/<c>StatusIconCycler</c>)
        /// were not present on a farm entity, so the chapter-14.2 head-top icons are disabled for
        /// that building while the panel keeps working. One throttled warning; never an error.
        /// </summary>
        public const string WorldIconsUnavailable = "ui.world-icons";

        /// <summary>
        /// One P0/P1 world-icon toggle could not be registered (sprite/asset issue or an unexpected
        /// vanilla status-system failure). The remaining icons and the panel stay functional.
        /// </summary>
        public const string WorldIconToggleFailed = "ui.world-icon-toggle";

        /// <summary>
        /// The single approved Harmony Postfix of ADR-009 was installed. Emitted once per game
        /// session under <c>HDBF/Compat</c>; the message carries the patch id, the target
        /// type/method and the Harmony instance id, so an in-game log alone proves what was patched.
        /// </summary>
        public const string HarmonyPatchInstalled = "compat.harmony-patch-installed";

        /// <summary>
        /// The patch could not be installed (Harmony missing, target signature changed, or another
        /// mod already owns the method). The mod keeps running the pre-patch D-2 behaviour; one
        /// throttled <c>HDBF/Warn</c> line, never an error dialog (REQ-COMPAT-09/17).
        /// </summary>
        public const string HarmonyPatchUnavailable = "compat.harmony-patch-unavailable";

        /// <summary>
        /// One patch invocation threw and was caught; the crop kept vanilla behaviour. Reaching
        /// <see cref="HDBF.Core.Compat.PatchFailureStateMachine.FailureThreshold"/> emits
        /// <see cref="HarmonyPatchUnloaded"/> (REQ-COMPAT-18).
        /// </summary>
        public const string HarmonyPatchFailed = "compat.harmony-patch-failed";

        /// <summary>
        /// The patch was uninstalled (three failures, or session unload); the message names the
        /// patch and target method and states that vanilla growth time is back (REQ-COMPAT-18).
        /// </summary>
        public const string HarmonyPatchUnloaded = "compat.harmony-patch-unloaded";

#if HDBF_DEV_DIAGNOSTICS
        /// <summary>
        /// TEMPORARY in-game verification diagnostics for ADR-009: one line per rescaled crop start
        /// (first few only), carrying the managed cell, the computed growth rate and the vanilla /
        /// scaled growth time. This is the only in-game proof that the four multipliers really
        /// stretch growth. Compiled in only for dev-diagnostics builds (REL-01 / G-08): the release
        /// package neither emits the line nor contains the key string.
        /// </summary>
        public const string HarmonyPatchApplied = "compat.harmony-patch-applied";
#endif
    }
}
