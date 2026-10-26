using System.Collections.Generic;

namespace HDBF.Core.Compat
{
    /// <summary>
    /// Session-scoped view of all optional-mod adapters (chapter 8.2). Consumed by HDBF.Gameplay
    /// and HDBF.UI through DI; the implementation is <c>HDBF.Integration.Adapters.CompatAdapterRegistry</c>.
    ///
    /// Rules the implementation must honour and callers may rely on:
    /// * <see cref="Register"/> isolates every adapter: one failure never stops the others and
    ///   never blocks mod loading (REQ-ARCH-08, REQ-COMPAT-07);
    /// * "port available" is the only question gameplay asks, so mod detection stays out of the
    ///   simulation code (chapter 8.2, chapter 8.3);
    /// * detection results are cached for the session and rebuilt per save load (REQ-COMPAT-06).
    /// </summary>
    public interface ICompatRegistry
    {
        /// <summary>Adapters currently initialized and active, in registration order.</summary>
        IReadOnlyList<string> EnabledAdapters { get; }

        /// <summary>Adapters that reached the failure threshold; no more retries this session (REQ-COMPAT-07).</summary>
        IReadOnlyList<string> FailedAdapters { get; }

        /// <summary>External mods discovered during detection; environment snapshot for the report.</summary>
        IReadOnlyList<string> DetectedMods { get; }

        /// <summary>True when the adapter is initialized and may serve traffic.</summary>
        bool IsAdapterActive(string adapterId);

        /// <summary>True when any active adapter can provide the given connection port (chapter 8.3).</summary>
        bool IsPortAvailable(HdbfPortKind port);

        /// <summary>Registers one adapter: detect then initialize, isolated and logged (REQ-COMPAT-07/08).</summary>
        void Register(ICompatAdapter adapter);

        /// <summary>Re-runs detection and refreshes adapter state for the current environment.</summary>
        void RefreshDetection();

        /// <summary>
        /// Records how many Harmony patches are currently installed by this mod. The value is the
        /// <c>harmonyPatchCount</c> field of the chapter 10.4 compatibility report (REQ-COMPAT-17:
        /// publish the total patch count); the patch bootstrap is the only writer.
        /// </summary>
        void ReportHarmonyPatchCount(int patchCount);
    }
}
