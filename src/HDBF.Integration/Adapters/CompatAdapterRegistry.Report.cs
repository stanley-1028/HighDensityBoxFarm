using System;
using HDBF.Core.Compat;

namespace HDBF.Integration.Adapters
{
    /// <summary>
    /// Report export half of <see cref="CompatAdapterRegistry"/> (chapter 10.4 table 10-4,
    /// REQ-SAFE-16). Kept in a partial file so the registry stays inside the 300-line gate; the
    /// report always publishes <c>harmonyPatchCount = 0</c> (0 Harmony, section 19.18.3-B5).
    /// </summary>
    public sealed partial class CompatAdapterRegistry
    {
        /// <summary>
        /// Builds the exportable compatibility report with every field of chapter 10.4 table 10-4
        /// (REQ-SAFE-16). Exported to JSON by <see cref="CompatReport.ToJson"/>.
        /// </summary>
        public CompatReport BuildReport()
        {
            try
            {
                return new CompatReport
                {
                    ReportVersion = 1,
                    ModVersion = ModVersion,
                    GameVersion = GameVersion,
                    SchemaVersion = SchemaVersion,
                    ConfigSchemaVersion = ConfigSchemaVersion,
                    DetectedMods = _detector.DetectedMods,
                    EnabledAdapters = _enabled,
                    FailedAdapters = _failed,
                    DetectionSource = _detector.LastDetectionSource,
                    DetectionDurationMs = _detector.LastDetectionDurationMs,
                    // 0 Harmony (section 19.18.3-B5): the field stays in the report contract but
                    // can never be anything else because the mod ships no patch bootstrap.
                    HarmonyPatchCount = 0,
                    PerformanceMode = "Normal",
                    LastError = _lastError,
                };
            }
            catch (Exception exception)
            {
                RecordError("report-failed", exception);
                return new CompatReport
                {
                    DetectedMods = _detector.DetectedMods,
                    EnabledAdapters = _enabled,
                    FailedAdapters = _failed,
                    DetectionSource = _detector.LastDetectionSource,
                    DetectionDurationMs = _detector.LastDetectionDurationMs,
                    HarmonyPatchCount = 0,
                    LastError = _lastError ?? exception.GetType().Name,
                };
            }
        }
    }
}