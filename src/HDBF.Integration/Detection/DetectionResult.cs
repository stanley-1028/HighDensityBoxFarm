using System;

namespace HDBF.Integration.Detection
{
    /// <summary>
    /// Result of one adapter detection, persisted in the session cache with the five fields
    /// required by chapter 8.2 (<c>adapterId</c> is the cache key; the rest live here).
    ///
    /// The struct is immutable so a cached result can be copied out without letting callers
    /// mutate shared state (REQ-COMPAT-06).
    /// </summary>
    public readonly struct DetectionResult
    {
        /// <summary>Creates one detection result.</summary>
        /// <param name="detected">True when the external mod was found.</param>
        /// <param name="detectedMod">Human-readable mod name, or null when absent.</param>
        /// <param name="modVersion">Best-effort version string from assembly metadata, or null.</param>
        /// <param name="detectedAtTicks">UTC ticks of the probe, for the session cache audit.</param>
        /// <param name="failureReason">Short stable reason when not detected; null on success.</param>
        public DetectionResult(bool detected, string? detectedMod, string? modVersion, long detectedAtTicks,
            string? failureReason)
        {
            Detected = detected;
            DetectedMod = detectedMod;
            ModVersion = modVersion;
            DetectedAtTicks = detectedAtTicks;
            FailureReason = failureReason;
        }

        /// <summary>True when the external mod is present and its API surface is probeable.</summary>
        public bool Detected { get; }

        /// <summary>Mod name used in the compatibility report's <c>detectedMods</c> array.</summary>
        public string? DetectedMod { get; }

        /// <summary>Version of the detected mod when the assembly exposes one, otherwise null.</summary>
        public string? ModVersion { get; }

        /// <summary>UTC timestamp of the probe (REQ-COMPAT-06 session cache field <c>detectedAt</c>).</summary>
        public long DetectedAtTicks { get; }

        /// <summary>Diagnostic reason for a miss, e.g. <c>not-installed</c> or <c>detect-timeout</c>.</summary>
        public string? FailureReason { get; }
    }
}
