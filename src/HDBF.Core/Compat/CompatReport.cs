using System;
using System.Collections.Generic;
using HDBF.Core.Json;

namespace HDBF.Core.Compat
{
    /// <summary>
    /// Exportable compatibility/health snapshot, a one-to-one mapping of the fields required by
    /// chapter 10.4 table 10-4 (REQ-SAFE-16). Built by the gameplay layer at startup and on
    /// demand, then serialized for player bug reports.
    ///
    /// The report deliberately contains no player paths and no third-party type names: adapter
    /// ids only (REQ-COMPAT-10, privacy rule of chapter 10.4).
    /// </summary>
    public sealed class CompatReport
    {
        /// <summary>Report format version; bump only when the field set changes (table 10-4).</summary>
        public int ReportVersion { get; init; } = 1;

        /// <summary>HDBF mod version, e.g. <c>0.1.0</c> (chapter 9.4).</summary>
        public string ModVersion { get; init; } = "0.1.0";

        /// <summary>Timberborn version string used to reproduce the environment.</summary>
        public string GameVersion { get; init; } = "1.1.2.8";

        /// <summary>Save schema version read from the loaded save (REQ-SAVE-05).</summary>
        public int SchemaVersion { get; init; } = 1;

        /// <summary>Config schema version read from <c>HDBF_Config.json</c> (REQ-ARCH-10).</summary>
        public int ConfigSchemaVersion { get; init; } = 1;

        /// <summary>External mods discovered by the detector; environment snapshot.</summary>
        public IReadOnlyList<string> DetectedMods { get; init; } = Array.Empty<string>();

        /// <summary>Adapters that are active and providing capabilities.</summary>
        public IReadOnlyList<string> EnabledAdapters { get; init; } = Array.Empty<string>();

        /// <summary>Adapters that reached the failure threshold (REQ-COMPAT-07).</summary>
        public IReadOnlyList<string> FailedAdapters { get; init; } = Array.Empty<string>();

        /// <summary>How detection ran: <c>ModList</c>, <c>AssemblyScan</c> or <c>Unknown</c>.</summary>
        public string DetectionSource { get; init; } = "Unknown";

        /// <summary>Detection wall time in milliseconds, for timeout diagnostics (chapter 8.2).</summary>
        public int DetectionDurationMs { get; init; }

        /// <summary>Number of Harmony patches still registered; zero is the design target.</summary>
        public int HarmonyPatchCount { get; init; }

        /// <summary>Performance protection mode: <c>Normal</c> or <c>Reduced</c> (REQ-SAFE-19).</summary>
        public string PerformanceMode { get; init; } = "Normal";

        /// <summary>Last recorded error summary, if any; null when the session is clean (REQ-SAFE-15).</summary>
        public string? LastError { get; init; }

        /// <summary>
        /// Serializes the report as hand-written JSON (deterministic, no Newtonsoft in Core).
        /// Field order follows table 10-4 so pasted reports diff cleanly (REQ-SAFE-16).
        /// </summary>
        public string ToJson()
        {
            return new JsonTextWriter()
                .StartObject()
                .Property("reportVersion").IntegerValue(ReportVersion)
                .Property("modVersion").StringValue(ModVersion)
                .Property("gameVersion").StringValue(GameVersion)
                .Property("schemaVersion").IntegerValue(SchemaVersion)
                .Property("configSchemaVersion").IntegerValue(ConfigSchemaVersion)
                .Property("detectedMods").StringArray(DetectedMods)
                .Property("enabledAdapters").StringArray(EnabledAdapters)
                .Property("failedAdapters").StringArray(FailedAdapters)
                .Property("detectionSource").StringValue(DetectionSource)
                .Property("detectionDurationMs").IntegerValue(DetectionDurationMs)
                .Property("harmonyPatchCount").IntegerValue(HarmonyPatchCount)
                .Property("performanceMode").StringValue(PerformanceMode)
                .Property("lastError").StringValue(LastError)
                .EndObject()
                .ToString();
        }
    }
}
