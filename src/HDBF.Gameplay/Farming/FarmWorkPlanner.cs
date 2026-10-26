using System;
using System.Collections.Generic;
using HDBF.Core;
using HDBF.Core.Balance;

namespace HDBF.Gameplay.Farming
{
    /// <summary>What a farmer does on one managed plot: plant an empty plot or harvest a mature crop.</summary>
    public enum FarmActionKind
    {
        Plant = 0,
        Harvest = 1,
    }

    /// <summary>
    /// One plot the farm may work today. <see cref="PathLength"/> is -1 until the real entrance-to-plot
    /// path has been measured (see <see cref="FarmWorkPlanner"/> for ordering); it is never a straight
    /// line approximation once measured.
    /// </summary>
    public readonly struct FarmWorkCandidate
    {
        public FarmWorkCandidate(int plotId, FarmActionKind kind, string cropTemplate)
            : this(plotId, kind, cropTemplate, -1)
        {
        }

        public FarmWorkCandidate(int plotId, FarmActionKind kind, string cropTemplate, int pathLength)
        {
            PlotId = plotId;
            Kind = kind;
            CropTemplate = cropTemplate ?? string.Empty;
            PathLength = pathLength;
        }

        public int PlotId { get; }

        public FarmActionKind Kind { get; }

        public string CropTemplate { get; }

        public int PathLength { get; }

        /// <summary>Returns a copy with the measured path length attached.</summary>
        public FarmWorkCandidate WithPathLength(int pathLength)
        {
            return new FarmWorkCandidate(PlotId, Kind, CropTemplate, pathLength);
        }
    }

    /// <summary>One dispatch decision handed to the workplace behavior.</summary>
    public readonly struct FarmWorkItem
    {
        public FarmWorkItem(int plotId, UnityEngine.Vector3Int cell, FarmActionKind kind, string cropTemplate,
            int pathLength, float workCost)
        {
            PlotId = plotId;
            Cell = cell;
            Kind = kind;
            CropTemplate = cropTemplate ?? string.Empty;
            PathLength = pathLength;
            WorkCost = workCost;
        }

        public int PlotId { get; }

        public UnityEngine.Vector3Int Cell { get; }

        public FarmActionKind Kind { get; }

        public string CropTemplate { get; }

        public int PathLength { get; }

        public float WorkCost { get; }
    }

    /// <summary>
    /// Per-farm daily labour budget (chapter 6.2 / REQ-IMPL-16):
    /// * at most <c>activeWorkers × 13</c> plot actions per day;
    /// * every action costs <c>0.08 × (1 + pathLength / 20)</c> workdays (times the mode work
    ///   multiplier); the daily workday pool is <c>actionsAtZeroPath × 0.08</c> per day, so a long
    ///   commute genuinely reduces how many plots can be serviced.
    /// </summary>
    public sealed class FarmDailyBudget
    {
        private FarmDailyBudget(int actions, float workdays)
        {
            ActionsRemaining = actions;
            WorkdaysRemaining = workdays;
        }

        public int ActionsRemaining { get; private set; }

        public float WorkdaysRemaining { get; private set; }

        public static FarmDailyBudget Create(int activeWorkers)
        {
            var actions = BalanceFormulas.DailyPlotActions(activeWorkers);
            // 13 actions × 0.08 workdays = 1.04 workdays per farmer per day; the path term of
            // BalanceFormulas.WorkCost then consumes that pool faster, which is exactly the
            // "range makes commuting expensive" rule of REQ-BAL-04.
            return new FarmDailyBudget(actions, actions * TierCatalog.WorkCostPerPlotDay);
        }

        public bool CanAfford(float workCost)
        {
            return ActionsRemaining > 0 && workCost <= WorkdaysRemaining + 0.0001f;
        }

        public bool TryConsume(float workCost)
        {
            if (!CanAfford(workCost))
            {
                return false;
            }

            ActionsRemaining--;
            WorkdaysRemaining -= workCost;
            if (WorkdaysRemaining < 0f)
            {
                WorkdaysRemaining = 0f;
            }

            return true;
        }
    }

    /// <summary>
    /// Pure, deterministic ordering and cost rules of the P1 farm loop. Kept free of game objects so
    /// the rules can be asserted with the L1 style checks (no Unity types except the shell structs
    /// above that the game-side caller fills in).
    ///
    /// Ordering (REQ-IMPL-16): mature-to-harvest before empty-to-plant; within one priority the
    /// measured path length ascending; then plot id ascending so equal inputs always produce the
    /// same sequence.
    /// </summary>
    public static class FarmWorkPlanner
    {
        public const int HarvestPriority = 0;
        public const int PlantPriority = 1;

        /// <summary>Upper bound of path searches spent per frame while building one day's plan.</summary>
        public const int PathMeasurementsPerTick = 4;

        public static int PriorityOf(FarmActionKind kind)
        {
            return kind == FarmActionKind.Harvest ? HarvestPriority : PlantPriority;
        }

        /// <summary>workCost = 0.08 × (1 + pathLength/20) × mode work multiplier (chapter 6.2/6.7).</summary>
        public static float ActionWorkCost(int pathLength, HdbfMode mode,
            float denseWorkMultiplier = TierCatalog.DenseWorkMultiplier)
        {
            return BalanceFormulas.WorkCost(pathLength) * BalanceFormulas.ModeWorkMultiplier(mode, denseWorkMultiplier);
        }

        /// <summary>
        /// Orders measured candidates. Unmeasured candidates (path -1) sort last so a partially
        /// measured plan never dispatches a plot whose real path is still unknown.
        /// </summary>
        public static int CompareMeasured(FarmWorkCandidate left, FarmWorkCandidate right)
        {
            var byPriority = PriorityOf(left.Kind).CompareTo(PriorityOf(right.Kind));
            if (byPriority != 0)
            {
                return byPriority;
            }

            var leftPath = left.PathLength < 0 ? int.MaxValue : left.PathLength;
            var rightPath = right.PathLength < 0 ? int.MaxValue : right.PathLength;
            var byPath = leftPath.CompareTo(rightPath);
            if (byPath != 0)
            {
                return byPath;
            }

            return left.PlotId.CompareTo(right.PlotId);
        }

        /// <summary>
        /// Cheap pre-ordering used only to decide which candidates deserve the expensive real path
        /// query first: priority, then horizontal distance to the entrance, then plot id.
        /// </summary>
        public static int CompareEstimated(FarmWorkCandidate left, FarmWorkCandidate right, int leftDistance,
            int rightDistance)
        {
            var byPriority = PriorityOf(left.Kind).CompareTo(PriorityOf(right.Kind));
            if (byPriority != 0)
            {
                return byPriority;
            }

            var byDistance = leftDistance.CompareTo(rightDistance);
            return byDistance != 0 ? byDistance : left.PlotId.CompareTo(right.PlotId);
        }

        /// <summary>
        /// How many candidates get a real path query this day. At least twice the daily action
        /// budget (so the sorted window always covers every plot that can possibly be worked) and a
        /// fixed slack, capped to keep the planning cost bounded (chapter 15.3 frame budget).
        /// </summary>
        public static int MeasurementQuota(int actionsRemaining, int cap = 512)
        {
            if (actionsRemaining <= 0 || cap <= 0)
            {
                return 0;
            }

            var quota = actionsRemaining * 2 + 16;
            return quota > cap ? cap : quota;
        }

        /// <summary>Orders a measured candidate list in place.</summary>
        public static void SortMeasured(List<FarmWorkCandidate> candidates)
        {
            if (candidates is null || candidates.Count < 2)
            {
                return;
            }

            candidates.Sort(CompareMeasured);
        }

        /// <summary>
        /// Picks the first measured, unclaimed candidate the remaining budget can pay for.
        /// Returns -1 when nothing can be dispatched now.
        /// </summary>
        public static int FindNext(
            IReadOnlyList<FarmWorkCandidate> orderedCandidates,
            FarmDailyBudget budget,
            ISet<int> claimedPlotIds,
            HdbfMode mode,
            out float workCost)
        {
            workCost = 0f;
            if (orderedCandidates is null || budget is null)
            {
                return -1;
            }

            for (var i = 0; i < orderedCandidates.Count; i++)
            {
                var candidate = orderedCandidates[i];
                if (candidate.PathLength < 0 || claimedPlotIds.Contains(candidate.PlotId))
                {
                    continue;
                }

                var cost = ActionWorkCost(candidate.PathLength, mode);
                if (!budget.CanAfford(cost))
                {
                    continue;
                }

                workCost = cost;
                return i;
            }

            return -1;
        }
    }
}
