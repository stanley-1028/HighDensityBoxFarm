using System;
using System.Collections.Generic;

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
    /// <item><see cref="HdbfAdapterHealth.Failed"/>: three failures inside
    /// <see cref="FailureWindowSeconds"/>, no retry (REQ-COMPAT-07).</item>
    /// </list>
    ///
    /// L-02(c) time window: one external-mod update used to trip three failures in the same frame
    /// (one per farm) and disable the adapter for the whole session. The threshold now only counts
    /// failures that fall inside a rolling <see cref="FailureWindowSeconds"/> window, and a success
    /// clears the window. The clock is supplied by the caller (<see cref="Fail(string, double)"/>),
    /// so the policy stays a pure function and unit-testable (L1); the timestampless
    /// <see cref="Fail(string)"/> overload keeps the legacy "any three failures" rule for callers
    /// that have no clock.
    ///
    /// The class has no game dependencies, so the whole failure policy is unit-testable (L1).
    /// </summary>
    public sealed class CompatStateMachine
    {
        /// <summary>Consecutive failures after which the adapter is disabled for the session (chapter 18.8).</summary>
        public const int FailureThreshold = 3;

        /// <summary>
        /// Rolling window (seconds) in which <see cref="FailureThreshold"/> failures must fall for the
        /// adapter to fail (L-02(c)). 60 s is well above one game day at normal speed but short enough
        /// that a transient reflection miss cannot disable the adapter for the rest of the session.
        /// </summary>
        public const double FailureWindowSeconds = 60.0;

        private enum Phase
        {
            Unknown = 0,
            Detected = 1,
            Active = 2,
            Failed = 3,
        }

        private readonly Queue<double> _failureTimes = new Queue<double>();

        private Phase _phase = Phase.Unknown;
        private bool _knownMissing;
        private double _lastFailureTime = double.NaN;

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

        /// <summary>
        /// Failures inside the current window (L-02(c)); reset by a successful initialization. With
        /// the timestampless <see cref="Fail(string)"/> overload this is the legacy consecutive count.
        /// </summary>
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
                    _failureTimes.Clear();
                    _lastFailureTime = double.NaN;
                    LastFailureReason = null;
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Records one failure without a caller clock (legacy rule, REQ-COMPAT-07): deactivates the
        /// adapter, counts the failure and reaches <see cref="HdbfAdapterHealth.Failed"/> at
        /// <see cref="FailureThreshold"/>. Below the threshold health is
        /// <see cref="HdbfAdapterHealth.Degraded"/> and a retry is still allowed. Prefer
        /// <see cref="Fail(string, double)"/>: only that overload applies the L-02(c) time window.
        /// </summary>
        /// <param name="reason">Short stable failure reason recorded for the compatibility report.</param>
        public void Fail(string reason)
        {
            RecordFailure(reason, double.NaN);
        }

        /// <summary>
        /// Records one failure at a caller-supplied monotonic time (L-02(c)). Failures older than
        /// <see cref="FailureWindowSeconds"/> are dropped before counting, so only three failures
        /// inside one window disable the adapter; a non-finite timestamp falls back to the legacy
        /// rule and a clock that jumps backwards is clamped to the newest observed timestamp.
        /// A single failure (e.g. one farm's pipe draw) therefore degrades to
        /// <see cref="HdbfAdapterHealth.Degraded"/>, never straight to session-wide
        /// <see cref="HdbfAdapterHealth.Failed"/>.
        /// </summary>
        /// <param name="reason">Short stable failure reason recorded for the compatibility report.</param>
        /// <param name="timestampSeconds">Monotonic time in seconds (any origin); NaN/∞ = no clock.</param>
        public void Fail(string reason, double timestampSeconds)
        {
            RecordFailure(reason, timestampSeconds);
        }

        private void RecordFailure(string reason, double timestampSeconds)
        {
            var now = double.IsNaN(timestampSeconds) || double.IsInfinity(timestampSeconds)
                ? double.NaN
                : timestampSeconds;
            if (!double.IsNaN(now) && !double.IsNaN(_lastFailureTime) && now < _lastFailureTime)
            {
                // Monotonic clamp: a wall clock jumping backwards must not widen the window.
                now = _lastFailureTime;
            }

            if (_phase == Phase.Failed)
            {
                LastFailureReason = reason;
                return;
            }

            if (!double.IsNaN(now))
            {
                PruneOldFailures(now);
                _lastFailureTime = now;
            }

            if (_failureTimes.Count < FailureThreshold)
            {
                _failureTimes.Enqueue(now);
            }

            FailureCount = _failureTimes.Count;
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

        /// <summary>Drops failures that fell out of the rolling window before this failure.</summary>
        private void PruneOldFailures(double now)
        {
            while (_failureTimes.Count > 0)
            {
                var oldest = _failureTimes.Peek();
                if (double.IsNaN(oldest) || now - oldest <= FailureWindowSeconds)
                {
                    // A timestampless (legacy) failure has no age and keeps counting until a success.
                    return;
                }

                _failureTimes.Dequeue();
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
