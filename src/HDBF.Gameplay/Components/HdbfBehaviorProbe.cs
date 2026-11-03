// ============================================================================
// HDBF v2 | Layer: Gameplay/Components | Owner: V2-GAMEPLAY-01
// Purpose: Read-only startup probe for the beaver work-behavior decision path (WP-07 / RE-03).
//          The player crash report is a NullReferenceException inside a third-party replacement of
//          the vanilla decision method; this probe records which patch runtimes are loaded and
//          whether the vanilla method is reachable, so the next report can be triaged from the log
//          instead of guessing. It registers no patch, calls no patch API and changes no state.
// Requirements: REQ-V2-READ-02, REQ-V2-SAFE-01, REQ-V2-SAFE-05
// ============================================================================
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HDBF.Core;
using HDBF.Core.Indoor;
using HDBF.Core.Logging;
using Timberborn.SingletonSystem;
using Timberborn.WorkSystem;

namespace HDBF.Gameplay.Components
{
    /// <summary>
    /// One-shot, read-only diagnostics of the loaded patch runtimes (WP-07/RE-03). The v1 crash
    /// report showed <c>WorkerRootBehavior.Decide</c> replaced by a dynamic wrapper from a Harmony /
    /// MonoMod patch; HDBF ships no patch, so the useful fact is which patch framework is loaded in
    /// the session. The probe never throws and logs at most one line per load (REQ-V2-SAFE-01).
    /// </summary>
    public sealed class HdbfBehaviorProbe : ILoadableSingleton
    {
        private const int MaxFrameworkNames = 8;
        private const int MaxNameLength = 120;

        private readonly IHdbfLog _log;

        /// <summary>Creates the probe from the throttled mod log (game DI singleton).</summary>
        /// <param name="log">Throttled mod log sink; never null.</param>
        public HdbfBehaviorProbe(IHdbfLog log)
        {
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        /// <inheritdoc />
        public void Load()
        {
            BehaviorProbeResult result;
            try
            {
                result = Inspect();
            }
            catch (Exception exception)
            {
                // Diagnostics must never break module load (REQ-V2-SAFE-01).
                _log.Error(HdbfLogCategory.Warn, IndoorLogKeys.BehaviorProbe,
                    "behavior probe failed; diagnostics unavailable", exception);
                return;
            }

            if (result.FrameworkCount > 0)
            {
                _log.Write(HdbfLogCategory.Info, IndoorLogKeys.BehaviorProbePatched,
                    "behavior probe: patch framework loaded, frameworks=" + result.Frameworks
                    + ", behaviorMethodFound=" + result.BehaviorMethodFound);
                return;
            }

            _log.Write(HdbfLogCategory.Info, IndoorLogKeys.BehaviorProbe,
                "behavior probe: no patch framework loaded, behaviorMethodFound="
                + result.BehaviorMethodFound);
        }

        /// <summary>
        /// Inspects the loaded assemblies without touching any patch API: it records the loaded
        /// Harmony/MonoMod runtimes and whether the vanilla decision method can be resolved. A
        /// replacement installed by a dynamic method cannot be proven from reflection alone, so the
        /// result is diagnostic evidence, not a verdict.
        /// </summary>
        /// <returns>Read-only probe result; never throws.</returns>
        internal static BehaviorProbeResult Inspect()
        {
            var frameworks = new StringBuilder();
            var count = 0;
            var behaviorMethodFound = false;
            try
            {
                behaviorMethodFound = FindBehaviorMethod() is not null;
            }
            catch (Exception)
            {
                behaviorMethodFound = false;
            }

            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (var index = 0; index < assemblies.Length; index++)
            {
                Assembly? assembly = assemblies[index];
                if (assembly is null)
                {
                    continue;
                }

                try
                {
                    var name = assembly.GetName().Name;
                    if (string.IsNullOrEmpty(name) || !LooksLikePatchRuntime(name!))
                    {
                        continue;
                    }

                    if (count > 0)
                    {
                        frameworks.Append('|');
                    }

                    frameworks.Append(name!.Length <= MaxNameLength ? name : name!.Substring(0, MaxNameLength));
                    count++;
                    if (count >= MaxFrameworkNames)
                    {
                        break;
                    }
                }
                catch (Exception)
                {
                    // An unreadable assembly name only costs this one entry.
                }
            }

            return new BehaviorProbeResult(count > 0, count, frameworks.ToString(), behaviorMethodFound);
        }

        /// <summary>Resolves the vanilla decision method read-only (private instance method).</summary>
        private static MethodInfo? FindBehaviorMethod()
        {
            return typeof(WorkerRootBehavior).GetMethod("Decide",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        }

        /// <summary>True when one assembly name looks like a loaded patch runtime (name contains a known marker).</summary>
        private static bool LooksLikePatchRuntime(string name)
        {
            // Neutral markers: the two runtime assemblies the crash report's stack implies.
            return name.IndexOf("Harmony", StringComparison.OrdinalIgnoreCase) >= 0
                   || name.IndexOf("MonoMod", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>Read-only result of one probe pass (testable without a game session).</summary>
        internal readonly struct BehaviorProbeResult
        {
            /// <summary>Creates a probe result.</summary>
            /// <param name="patchRuntimeLoaded">True when a patch runtime assembly was found.</param>
            /// <param name="frameworkCount">Number of patch runtime assemblies found (bounded).</param>
            /// <param name="frameworks">Bounded "|"-joined runtime names.</param>
            /// <param name="behaviorMethodFound">True when the vanilla decision method resolves.</param>
            public BehaviorProbeResult(bool patchRuntimeLoaded, int frameworkCount, string frameworks,
                bool behaviorMethodFound)
            {
                PatchRuntimeLoaded = patchRuntimeLoaded;
                FrameworkCount = frameworkCount;
                Frameworks = frameworks ?? string.Empty;
                BehaviorMethodFound = behaviorMethodFound;
            }

            /// <summary>True when at least one loaded assembly looks like a patch runtime.</summary>
            public bool PatchRuntimeLoaded { get; }

            /// <summary>Bounded number of patch runtime assemblies found.</summary>
            public int FrameworkCount { get; }

            /// <summary>Bounded "|"-joined names of the patch runtime assemblies.</summary>
            public string Frameworks { get; }

            /// <summary>True when <c>WorkerRootBehavior.Decide</c> resolves on the vanilla type.</summary>
            public bool BehaviorMethodFound { get; }
        }
    }
}
