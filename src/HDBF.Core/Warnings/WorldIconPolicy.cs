using System;
using HDBF.Core.Abstractions;

namespace HDBF.Core.Warnings
{
    /// <summary>
    /// Pure scheduling/culling rules of the world-space warning icons (chapter 14.2/14.6,
    /// REQ-UI-03/04/12). Everything here is game-free and L1-tested; the gameplay coordinator only
    /// supplies the camera result and the wall clock.
    /// </summary>
    public static class WorldIconPolicy
    {
        /// <summary>Chapter 14.2 first-version distance: farther than 40 cells hides the icon.</summary>
        public const float DefaultMaxDistance = 40f;

        /// <summary>Chapter 14.6: the icon set is aggregated at most twice per second.</summary>
        public const float DefaultIconIntervalSeconds = 0.5f;

        /// <summary>Chapter 14.6: the warning list is re-sorted at most once per second.</summary>
        public const float DefaultWarningListIntervalSeconds = 1f;

        /// <summary>Chapter 14.2/14.5: blink rate is at most 1 Hz (one on/off cycle per second).</summary>
        public const float DefaultBlinkHz = 1f;

        /// <summary>
        /// Farms whose warning snapshot is rebuilt per icon pass. Keeps one pass below the
        /// chapter-14.6 warning budget even when a hundred farms are on screen at once; the
        /// round-robin cursor in the coordinator visits the rest on the following passes.
        /// </summary>
        public const int DefaultFarmsPerPass = 24;

        /// <summary>
        /// How long a farm stays in the cross-building aggregation cache after its last on-screen
        /// evaluation. Chapter 14.6 only processes on-screen buildings, so the entry expires
        /// instead of keeping a whole-map warning list alive.
        /// </summary>
        public const float DefaultAggregateTtlSeconds = 3f;

        /// <summary>
        /// Distance culling. A building is shown only while it is inside the camera viewport
        /// (chapter 14.6 "只處理屏幕內建築") and within the configured cell distance.
        /// Non-finite input is treated as "not visible" so NaN can never leak a live icon.
        /// </summary>
        public static bool IsWorldIconVisible(bool isOnScreen, float distanceToCamera, float maxDistance)
        {
            if (!isOnScreen || float.IsNaN(distanceToCamera) || float.IsInfinity(distanceToCamera))
            {
                return false;
            }

            if (float.IsNaN(maxDistance) || maxDistance < 0f)
            {
                return false;
            }

            return distanceToCamera <= maxDistance;
        }

        /// <summary>Chapter 14.2/14.5: only P0 (Critical) blinks, and only when the setting is on.</summary>
        public static bool ShouldBlink(FarmWarningSeverity severity, bool blinkEnabled) =>
            blinkEnabled && severity == FarmWarningSeverity.Critical;

        /// <summary>
        /// Blink phase as a pure function of elapsed time. With <paramref name="blinkHz"/> = 1 the
        /// icon is visible for the first half of every second and hidden for the second half, i.e.
        /// one on/off cycle per second (≤1 Hz). A non-positive rate means "no blinking".
        /// </summary>
        public static bool IsBlinkOn(double elapsedSeconds, float blinkHz)
        {
            if (blinkHz <= 0f || float.IsNaN(blinkHz))
            {
                return true;
            }

            if (double.IsNaN(elapsedSeconds) || double.IsInfinity(elapsedSeconds))
            {
                return true;
            }

            if (elapsedSeconds < 0d)
            {
                elapsedSeconds = -elapsedSeconds;
            }

            var phase = elapsedSeconds * blinkHz;
            var fraction = phase - Math.Floor(phase);
            return fraction < 0.5d;
        }

        /// <summary>Returns true when the next scheduled pass is due; a non-positive interval is due.</summary>
        public static bool IsDue(double nowSeconds, double nextPassSeconds, float intervalSeconds)
        {
            if (intervalSeconds <= 0f || float.IsNaN(intervalSeconds))
            {
                return true;
            }

            if (double.IsNaN(nowSeconds) || double.IsNaN(nextPassSeconds))
            {
                return true;
            }

            return nowSeconds >= nextPassSeconds;
        }
    }

    /// <summary>
    /// Wall-clock hysteresis gate for one warning (chapter 14.2: a warning appears and disappears
    /// only after it has persisted for the configured window, default 2 s).
    ///
    /// The earlier implementation counted evaluation samples, so adding a second consumer of the
    /// snapshot (the world-icon pass) would have shortened the 2 s window. This gate is
    /// time-based: it is independent of how often the farm happens to be sampled, which makes the
    /// behaviour testable at L1 with an explicit clock. One instance per farm, slots indexed by
    /// warning kind; the only mutable state is the two timestamps and the visible flag per slot
    /// (no global/static state, chapter 16.2).
    /// </summary>
    public sealed class WarningHysteresis
    {
        private readonly double[] _activeSince;
        private readonly double[] _inactiveSince;
        private readonly bool[] _visible;

        public WarningHysteresis(int slotCount)
        {
            if (slotCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(slotCount));
            }

            _activeSince = new double[slotCount];
            _inactiveSince = new double[slotCount];
            _visible = new bool[slotCount];
            Reset();
        }

        public int SlotCount => _visible.Length;

        /// <summary>
        /// Observes one slot at <paramref name="nowSeconds"/> and returns whether the warning
        /// should currently be shown. The first active/inactive observation starts the timer; the
        /// state flips only once <paramref name="thresholdSeconds"/> has elapsed (or immediately
        /// when the threshold is zero). Never throws for an out-of-range slot.
        /// </summary>
        public bool Observe(int slot, bool active, double nowSeconds, double thresholdSeconds)
        {
            if (slot < 0 || slot >= _visible.Length)
            {
                return false;
            }

            if (double.IsNaN(nowSeconds) || double.IsInfinity(nowSeconds))
            {
                nowSeconds = 0d;
            }

            if (thresholdSeconds <= 0d || double.IsNaN(thresholdSeconds))
            {
                _visible[slot] = active;
                _activeSince[slot] = -1d;
                _inactiveSince[slot] = -1d;
                return active;
            }

            if (active)
            {
                _inactiveSince[slot] = -1d;
                if (_visible[slot])
                {
                    return true;
                }

                _activeSince[slot] = StartTimer(_activeSince[slot], nowSeconds);
                if (nowSeconds - _activeSince[slot] >= thresholdSeconds)
                {
                    _visible[slot] = true;
                    _activeSince[slot] = -1d;
                }

                return _visible[slot];
            }

            _activeSince[slot] = -1d;
            if (!_visible[slot])
            {
                return false;
            }

            _inactiveSince[slot] = StartTimer(_inactiveSince[slot], nowSeconds);
            if (nowSeconds - _inactiveSince[slot] >= thresholdSeconds)
            {
                _visible[slot] = false;
                _inactiveSince[slot] = -1d;
            }

            return _visible[slot];
        }

        /// <summary>True when the slot is currently visible (diagnostics/tests).</summary>
        public bool IsVisible(int slot) => slot >= 0 && slot < _visible.Length && _visible[slot];

        /// <summary>Drops all timers and visibility; used when a farm is removed.</summary>
        public void Reset()
        {
            for (var i = 0; i < _visible.Length; i++)
            {
                _visible[i] = false;
                _activeSince[i] = -1d;
                _inactiveSince[i] = -1d;
            }
        }

        private static double StartTimer(double since, double nowSeconds)
        {
            // A clock that jumped backwards (scene reload, test seam) restarts the timer instead of
            // leaving it stuck in the future.
            return since < 0d || nowSeconds < since ? nowSeconds : since;
        }
    }
}
