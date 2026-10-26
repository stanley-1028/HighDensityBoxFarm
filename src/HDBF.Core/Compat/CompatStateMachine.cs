using System;

namespace HDBF.Core.Compat
{
    /// <summary>
    /// Pure per-adapter state machine implementing chapter 8.2 lifecycle
    /// <c>Unknown → Detected → Active</c> with the three-strikes disable rule of REQ-COMPAT-07.
    ///
    /// Health mapping (frozen; <see cref="ICompatAdapter"/> and the registry code against it):
    /// <list type="bullet">
    /// <item><see cref="HdbfAdapterHealth.Unknown"/>: never probed in this session;</item>
    /// <item><see cref="HdbfAdapterHealth.Ok"/>: active and healthy;</item>
    /// <item><see cref="HdbfAdapterHealth.Degraded"/>: one or two failures, retry allowed;</item>
    /// <item><see cref="HdbfAdapterHealth.Disabled"/>: known to exist but not active (not yet
    /// initialized, shut down, or the mod is missing) — this is a neutral state, never a red
    /// error (REQ-COMPAT-12);</item>
    /// <item><see cref="HdbfAdapterHealth.Failed"/>: three failures this session, no retry
    /// (REQ-COMPAT-07).</item>
    /// </list>
    ///
    /// The class has no game dependencies, so the whole failure policy is unit-testable (L1).
    /// </summary>
    public sealed class CompatStateMachine
    {
        /// <summary>Consecutive failures after which the adapter is disabled for the session (chapter 18.8).</summary>
        public const int FailureThreshold = 3;

        private enum Phase
        {
            Unknown = 0,
            Detected = 1,
            Active = 2,
            Failed = 3,
        }

        private Phase _phase = Phase.Unknown;
        private bool _knownMissing;

        /// <summary>Current health, derived from the phase and the failure counter.</summary>
        public HdbfAdapterHealth Health
        {
            get
            {
                if (_phase == Phase.Failed)
                {
                    return HdbfAdapterHealth.Failed;
                }

                if (FailureCount > 0)
                {
                    return HdbfAdapterHealth.Degraded;
                }

                if (_phase == Phase.Active)
                {
                    return HdbfAdapterHealth.Ok;
                }

                if (_phase == Phase.Detected || _knownMissing)
                {
                    return HdbfAdapterHealth.Disabled;
                }

                return HdbfAdapterHealth.Unknown;
            }
        }

        /// <summary>Failures observed this session; reset by a successful initialization.</summary>
        public int FailureCount { get; private set; }

        /// <summary>True once <see cref="Detect"/> succeeded and the adapter was not failed.</summary>
        public bool IsDetected => _phase == Phase.Detected || _phase == Phase.Active;

        /// <summary>True while the adapter is initialized and serving traffic.</summary>
        public bool IsActive => _phase == Phase.Active;

        /// <summary>Reason of the most recent failure, for the compatibility report (chapter 10.4).</summary>
        public string? LastFailureReason { get; private set; }

        /// <summary>
        /// True when another detect/initialize attempt is allowed. Active adapters need no retry
        /// and failed ones are disabled until the session ends (REQ-COMPAT-07).
        /// </summary>
        public bool CanRetry => _phase != Phase.Active && _phase != Phase.Failed;

        /// <summary>
        /// Probes the environment. Returns true when the adapter is now detected (or already
        /// active); false only when it is failed for this session.
        /// </summary>
        public bool Detect()
        {
            switch (_phase)
            {
                case Phase.Failed:
                    return false;
                case Phase.Active:
                    return true;
                default:
                    _phase = Phase.Detected;
                    _knownMissing = false;
                    return true;
            }
        }

        /// <summary>
        /// Records a successful initialization: <c>Detected → Active</c>, health <c>Ok</c>, and
        /// clears the consecutive-failure counter. Returns false when detection has not run.
        /// </summary>
        public bool InitializeSucceeded()
        {
            switch (_phase)
            {
                case Phase.Active:
                    return true;
                case Phase.Detected:
                    _phase = Phase.Active;
                    _knownMissing = false;
                    FailureCount = 0;
                    LastFailureReason = null;
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Records one failure (REQ-COMPAT-07): deactivates the adapter, counts the failure and
        /// reaches <see cref="HdbfAdapterHealth.Failed"/> at <see cref="FailureThreshold"/>.
        /// Below the threshold health is <see cref="HdbfAdapterHealth.Degraded"/> and a retry is
        /// still allowed.
        /// </summary>
        public void Fail(string reason)
        {
            if (FailureCount < FailureThreshold)
            {
                FailureCount++;
            }

            LastFailureReason = reason;

            if (FailureCount >= FailureThreshold)
            {
                _phase = Phase.Failed;
                return;
            }

            // A failed call leaves the adapter initialized but unsafe to use: drop back to
            // Detected so the registry stops routing work through it while a retry is pending.
            if (_phase == Phase.Active)
            {
                _phase = Phase.Detected;
            }
        }

        /// <summary>
        /// Marks the adapter as known but unavailable, e.g. the external mod is missing
        /// (REQ-COMPAT-09/12). Neutral state: no failure is counted and no error is raised.
        /// </summary>
        /// <param name="reason">Optional diagnostic reason for the compatibility report.</param>
        public void MarkMissing(string? reason = null)
        {
            if (_phase == Phase.Failed)
            {
                return;
            }

            _phase = Phase.Unknown;
            _knownMissing = true;
            LastFailureReason = reason;
        }

        /// <summary>
        /// Deactivates the adapter: <c>Active → Detected</c>. Idempotent for every other phase;
        /// a failed adapter stays failed because retries are disabled for the session.
        /// </summary>
        public void Shutdown()
        {
            if (_phase == Phase.Active)
            {
                _phase = Phase.Detected;
                _knownMissing = false;
            }
        }
    }
}
