using System;
using System.Collections.Generic;
using HDBF.Core.Logging;
using HDBF.Integration.Detection;
using HDBF.Integration.Reflection;

namespace HDBF.Integration
{
    /// <summary>
    /// Narrow test seam for the compatibility layer.
    ///
    /// Why it exists: the five optional mods are not installed on the development machine, so the
    /// real detection always misses. Verifying the required degraded paths (REQ-COMPAT-07/09,
    /// table 10-6 "empty value injection" and "exception injection") needs a way to simulate
    /// "mod present but API missing" and to capture log output without the Unity player.
    ///
    /// Rules:
    /// * this is the only static mutable state in HDBF.Integration (the convention allows test
    ///   seams and the <see cref="ExternalProxy"/> cache, nothing else);
    /// * it is inert in production: nothing installs an override unless a test calls it;
    /// * <see cref="Reset"/> restores production defaults and clears the reflection cache.
    /// </summary>
    public static class TestHooks
    {
        private static IHdbfLog? _log;
        private static Func<string, DetectionResult?>? _detectionOverride;
        private static Func<IReadOnlyList<string>>? _modListProvider;

        /// <summary>Overrides the log sink used by newly created registry/adapter instances.</summary>
        public static void InjectLog(IHdbfLog? log)
        {
            _log = log;
        }

        /// <summary>
        /// Overrides detection per adapter id: return a result to simulate an installed (or
        /// deliberately broken) mod, return null to fall through to the real scan.
        /// </summary>
        public static void InjectDetectionOverride(Func<string, DetectionResult?>? detectionOverride)
        {
            _detectionOverride = detectionOverride;
        }

        /// <summary>Supplies a synthetic mod-manager list to exercise the <c>ModList</c> source.</summary>
        public static void InjectModListProvider(Func<IReadOnlyList<string>>? modListProvider)
        {
            _modListProvider = modListProvider;
        }

        /// <summary>Restores production defaults and clears every reflection cache.</summary>
        public static void Reset()
        {
            _log = null;
            _detectionOverride = null;
            _modListProvider = null;
            ExternalProxy.ClearCache();
        }

        internal static IHdbfLog? LogOverride => _log;

        internal static Func<string, DetectionResult?>? DetectionOverride => _detectionOverride;

        internal static Func<IReadOnlyList<string>>? ModListProviderOverride => _modListProvider;
    }
}
