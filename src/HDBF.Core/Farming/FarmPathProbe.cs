using System;

namespace HDBF.Core.Farming
{
    /// <summary>
    /// Per-cell tolerance rule of the farm plan's real path measurement (REQ-IMPL-16 real path
    /// length, REQ-SAFE-01 / REQ-IMPL-06 isolation).
    ///
    /// Why this type exists: the round-9 in-game log (2026-10-25, deployed build before BUG-01)
    /// showed
    /// <code>
    /// [HDBF/Error] gameplay.farm: farm loop fault (path length); ... exception=
    ///   System.InvalidOperationException: There's no cached flow field at 1187869;
    ///   stack=... PathfindingService.FindTerrainPathCached ... Accessible.FindTerrainPath ...
    ///   at HDBF.Gameplay.Components.HdbfBoxFarmController.ResolvePathLength
    /// </code>
    /// One managed plot whose coordinate has no navigation flow field made the path query throw;
    /// the old code counted that exception as a <b>farm-loop fault</b>, so a few such plots
    /// incremented the fault counter, wrote an <c>HDBF/Error</c> line and eventually disabled the
    /// whole crop loop for the building. A path query is a per-cell filter, not a plan-fatal
    /// error: the documented result of a failed query is "this cell is unreachable for this
    /// plan" (<see cref="Unreachable"/>), every other plot keeps its plan entry, and the failure
    /// is reported once per building through a throttled <c>HDBF/Warn</c> line.
    ///
    /// The rules live in Core so L1 can pin them without a game session (REQ-TEST-03): the
    /// gameplay caller passes the game-side query as a delegate and only ever sees an <c>int</c>
    /// path length or <see cref="Unreachable"/>.
    /// </summary>
    public static class FarmPathProbe
    {
        /// <summary>
        /// Sentinel for "no usable path in this plan". Never a valid path length; every consumer
        /// treats a negative value as "do not dispatch this plot" (the farm planner already
        /// sorts unmeasured/negative candidates last and skips them).
        /// </summary>
        public const int Unreachable = -1;

        /// <summary>True when <paramref name="pathLength"/> is a real measured path (0..n cells).</summary>
        public static bool IsReachable(int pathLength)
        {
            return pathLength >= 0;
        }

        /// <summary>
        /// Converts one successful game path query to whole cells: the distance is rounded up, a
        /// missing, negative, NaN or infinite distance means unreachable. Game path queries report
        /// the length as a <c>float</c>; returning a negative sentinel for an untrustworthy number
        /// keeps "measured" and "not measured" distinguishable at the type boundary.
        /// </summary>
        public static int FromQuery(bool found, float distance)
        {
            if (!found || float.IsNaN(distance) || float.IsInfinity(distance) || distance < 0f)
            {
                return Unreachable;
            }

            var rounded = Math.Ceiling(distance);
            if (rounded >= int.MaxValue)
            {
                return int.MaxValue;
            }

            return (int)rounded;
        }

        /// <summary>
        /// Runs one per-cell path measurement and converts a thrown query into the unreachable
        /// semantics. The exception never reaches the caller; when one was thrown it is reported
        /// through <paramref name="failure"/> so the caller can write the throttled warning. A
        /// null delegate or a negative result also means unreachable, so a caller can never treat
        /// "no measurement" as a valid path.
        /// </summary>
        public static int Measure(Func<int>? query, out Exception? failure)
        {
            failure = null;
            if (query is null)
            {
                return Unreachable;
            }

            try
            {
                var measured = query();
                return IsReachable(measured) ? measured : Unreachable;
            }
            catch (Exception exception)
            {
                failure = exception;
                return Unreachable;
            }
        }
    }
}
