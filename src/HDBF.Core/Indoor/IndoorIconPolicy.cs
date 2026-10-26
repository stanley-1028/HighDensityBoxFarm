// ============================================================================
// HDBF v2 | Layer: Core/Indoor | Owner: V2-GAMEPLAY-02
// Purpose: Pure scheduling/culling rules of the four v2 world-space warning icons (section
//          19.11/19.13): distance + viewport visibility, the <=1 Hz blink phase and the icon
//          pass interval. Game-free and L1-tested; gameplay only supplies the camera facts.
// Requirements: REQ-V2-BAL-03, REQ-V2-PERF-01, REQ-V2-SAFE-01
// ============================================================================
using System;

namespace HDBF.Core.Indoor
{
    /// <summary>
    /// Pure policy of the world-space v2 warning icons. The gameplay coordinator supplies the
    /// camera result and the wall clock; this class owns every threshold so the rules stay
    /// testable without Unity and no magic number leaks into the game loop.
    /// </summary>
    public static class IndoorIconPolicy
    {
        /// <summary>Default distance culling in cells (section 19.11 first-version value).</summary>
        public const float DefaultMaxDistance = 40f;

        /// <summary>v2 icon pass interval: at most once per second (section 19.13).</summary>
        public const float DefaultIconIntervalSeconds = 1f;

        /// <summary>Blink rate of a P0 icon: at most one on/off cycle per second.</summary>
        public const float DefaultBlinkHz = 1f;

        /// <summary>Farms whose snapshot is rebuilt per icon pass (round-robin budget).</summary>
        public const int DefaultFarmsPerPass = 24;

        /// <summary>
        /// Distance/viewport culling. A building shows its icon only while it is inside the camera
        /// viewport and within the configured cell distance. Non-finite input is treated as "not
        /// visible" so NaN can never leak a live icon.
        /// </summary>
        /// <param name="isOnScreen">True when the building origin is inside the camera viewport.</param>
        /// <param name="distanceToCamera">Distance to the camera focus in cells.</param>
        /// <param name="maxDistance">Configured culling distance in cells.</param>
        /// <returns>True when the icon may be shown this pass.</returns>
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

        /// <summary>Only a P0 category blinks, and only while the player setting is on.</summary>
        /// <param name="critical">True when the current warning is P0.</param>
        /// <param name="blinkEnabled">Configured blink switch.</param>
        /// <returns>True when the icon should blink.</returns>
        public static bool ShouldBlink(bool critical, bool blinkEnabled) => blinkEnabled && critical;

        /// <summary>
        /// Blink phase as a pure function of elapsed time. With <paramref name="blinkHz"/> = 1 the
        /// icon is visible for the first half of every second and hidden for the second half, i.e.
        /// one on/off cycle per second (&lt;=1 Hz). A non-positive rate means "no blinking".
        /// </summary>
        /// <param name="elapsedSeconds">Unscaled wall clock in seconds.</param>
        /// <param name="blinkHz">Blink rate in cycles per second.</param>
        /// <returns>True while the icon is in its visible half-cycle.</returns>
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
        /// <param name="nowSeconds">Unscaled wall clock in seconds.</param>
        /// <param name="nextPassSeconds">Scheduled time of the next pass.</param>
        /// <param name="intervalSeconds">Configured pass interval.</param>
        /// <returns>True when the coordinator may run the pass now.</returns>
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
}
