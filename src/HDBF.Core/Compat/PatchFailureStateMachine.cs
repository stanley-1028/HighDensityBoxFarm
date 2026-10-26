using System;

namespace HDBF.Core.Compat
{
    /// <summary>
    /// Failure budget of one Harmony patch (REQ-COMPAT-18): every patch body is wrapped in
    /// try/catch and counts its exceptions; after <see cref="FailureThreshold"/> cumulative
    /// failures the patch must be uninstalled and the mod falls back to vanilla behaviour.
    ///
    /// Pure state machine (no game, no Harmony, no logging), so the whole policy is L1-testable;
    /// the caller owns the side effects (log entry, <c>Harmony.Unpatch</c>). One instance per patch,
    /// owned by the session patch bridge — never static state (REQ-ARCH-06).
    /// </summary>
    public sealed class PatchFailureStateMachine
    {
        /// <summary>Cumulative failures after which the patch is unloaded (REQ-COMPAT-18).</summary>
        public const int FailureThreshold = 3;

        private readonly int _threshold;
        private int _failures;
        private bool _unloaded;

        /// <summary>
        /// Creates the state machine for one patch.
        /// </summary>
        /// <param name="patchId">Patch class/id used in logs and the compatibility report.</param>
        /// <param name="targetMethod">Whitelisted target method, e.g. <c>Timberborn.Growing.Growable.ResumeGrowing</c>.</param>
        /// <param name="threshold">Failure budget; values below 1 are clamped to 1.</param>
        public PatchFailureStateMachine(string? patchId, string? targetMethod, int threshold = FailureThreshold)
        {
            PatchId = patchId ?? string.Empty;
            TargetMethod = targetMethod ?? string.Empty;
            _threshold = threshold < 1 ? 1 : threshold;
        }

        /// <summary>Patch class/id (stable diagnostics identity).</summary>
        public string PatchId { get; }

        /// <summary>Whitelisted target method this patch is bound to.</summary>
        public string TargetMethod { get; }

        /// <summary>Cumulative failures observed since the last <see cref="Reset"/>.</summary>
        public int FailureCount => _failures;

        /// <summary>True once the failure budget is exhausted; the patch must be uninstalled.</summary>
        public bool IsUnloaded => _unloaded;

        /// <summary>
        /// Records one caught exception. Returns true exactly once: when this failure exhausted the
        /// budget and the caller must uninstall the patch. Further calls are no-ops (no overflow,
        /// no second unload request).
        /// </summary>
        public bool RecordFailure()
        {
            if (_unloaded)
            {
                return false;
            }

            if (_failures < _threshold)
            {
                _failures++;
            }

            if (_failures < _threshold)
            {
                return false;
            }

            _unloaded = true;
            return true;
        }

        /// <summary>Clears the budget for a fresh install (new game session after a reload).</summary>
        public void Reset()
        {
            _failures = 0;
            _unloaded = false;
        }
    }
}
