namespace HDBF.Core.Logging
{
    /// <summary>
    /// Stable log keys used by every HDBF assembly.
    ///
    /// The key is the throttle identity: per REQ-SAFE-14 / REQ-ARCH-15 the same key is emitted
    /// at most once per 30 s window, and suppressed repeats are counted instead of lost.
    /// Keys are part of the diagnostics contract (players and support paste them), so they
    /// must not be renamed after release.
    ///
    /// v2 has no world-plot scan, no managed-crop visibility and no Harmony patch, so those
    /// keys were deleted with their subsystem (section 19.2/19.18.3-B).
    /// </summary>
    public static class HdbfLogKeys
    {
        /// <summary>Configuration JSON missing, unreadable or with invalid values; defaults used (chapter 10.3).</summary>
        public const string ConfigInvalid = "config.invalid";

        /// <summary>Configuration file absent; built-in defaults used (chapter 10.3).</summary>
        public const string ConfigMissing = "config.missing";

        /// <summary>
        /// A schema-1 configuration file was read and migrated to schema 2 (REQ-V2-SAFE-06).
        /// Emitted once per parse; recognized values are preserved, deleted v1 keys are dropped.
        /// </summary>
        public const string ConfigMigrated = "config.migrated";

        /// <summary>
        /// Unknown configuration keys were ignored (REQ-V2-EXT-04). One throttled line lists them;
        /// the parse result is unaffected, so a hand-edited file can never break the mod.
        /// </summary>
        public const string ConfigIgnoredKey = "config.ignored-key";

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

        /// <summary>
        /// The vanilla floating-status-icon components (<c>StatusSubject</c>/<c>StatusIconCycler</c>)
        /// were not present on a farm entity, so the section-19.11 head-top icons are disabled for
        /// that building while the panel keeps working. One throttled warning; never an error.
        /// </summary>
        public const string WorldIconsUnavailable = "ui.world-icons";

        /// <summary>
        /// One P0/P1 world-icon toggle could not be registered (sprite/asset issue or an unexpected
        /// vanilla status-system failure). The remaining icons and the panel stay functional.
        /// </summary>
        public const string WorldIconToggleFailed = "ui.world-icon-toggle";
    }
}
