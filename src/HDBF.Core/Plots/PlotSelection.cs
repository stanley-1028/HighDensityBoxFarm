using System;
using System.Collections.Generic;

namespace HDBF.Core.Plots
{
    /// <summary>
    /// One plantable managed cell offered to the plot selector (chapter 11.3).
    /// Distance is the 2D entrance-to-cell distance; <see cref="PlotId"/> is the game cell id.
    /// </summary>
    public readonly struct PlotCandidate
    {
        /// <summary>Creates a candidate.</summary>
        public PlotCandidate(int plotId, int distance)
        {
            PlotId = plotId;
            Distance = distance;
        }

        /// <summary>Stable cell identity used by the save file and the registry.</summary>
        public int PlotId { get; }

        /// <summary>Distance from the building entrance cell; sorting primary key.</summary>
        public int Distance { get; }
    }

    /// <summary>
    /// Deterministic managed-plot selection (REQ-IMPL-07, REQ-BLD-04, chapter 11.3).
    ///
    /// Selection rules:
    /// * candidates are ordered by distance ascending, then plot id ascending, so a rescan never
    ///   shuffles cells whose distance is equal;
    /// * at most <c>capacity</c> cells are chosen; a short candidate list registers fewer cells
    ///   and the UI reports the shortage (TC-24);
    /// * cells that were already registered and are still valid candidates are kept first and in
    ///   their previous order, so raising/lowering the worker count never loses reclaimed plots
    ///   (chapter 11.3) and previously assigned identities stay stable;
    /// * duplicate candidate ids are collapsed, keeping the shortest distance.
    /// </summary>
    public static class PlotSelection
    {
        /// <summary>
        /// Selects the managed plot ids to register.
        /// </summary>
        /// <param name="candidates">Plantable, reachable, non-excluded cells in the work range.</param>
        /// <param name="capacity">Plot capacity of the tier (60/150/300/600 by default).</param>
        /// <param name="previouslyRegistered">Ids registered by the previous scan, in order.</param>
        /// <returns>Chosen ids: kept registrations first, then new ones in distance order.</returns>
        public static int[] Select(IReadOnlyList<PlotCandidate>? candidates, int capacity,
            IReadOnlyList<int>? previouslyRegistered)
        {
            if (candidates is null || candidates.Count == 0 || capacity <= 0)
            {
                return Array.Empty<int>();
            }

            // Collapse duplicate ids: a cell must never occupy two registration slots.
            var distanceById = new Dictionary<int, int>(candidates.Count);
            for (var i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                if (!distanceById.TryGetValue(candidate.PlotId, out var known) || candidate.Distance < known)
                {
                    distanceById[candidate.PlotId] = candidate.Distance;
                }
            }

            var take = Math.Min(capacity, distanceById.Count);
            var chosen = new List<int>(take);
            var chosenSet = new HashSet<int>();

            // 1. Keep every still-valid previously registered cell, in registration order.
            if (previouslyRegistered is not null)
            {
                for (var i = 0; i < previouslyRegistered.Count && chosen.Count < take; i++)
                {
                    var plotId = previouslyRegistered[i];
                    if (distanceById.ContainsKey(plotId) && chosenSet.Add(plotId))
                    {
                        chosen.Add(plotId);
                    }
                }
            }

            // 2. Fill the remaining slots in scan priority order.
            if (chosen.Count < take)
            {
                var ordered = new PlotCandidate[distanceById.Count];
                var index = 0;
                foreach (var pair in distanceById)
                {
                    ordered[index++] = new PlotCandidate(pair.Key, pair.Value);
                }

                Array.Sort(ordered, Compare);

                for (var i = 0; i < ordered.Length && chosen.Count < take; i++)
                {
                    if (chosenSet.Add(ordered[i].PlotId))
                    {
                        chosen.Add(ordered[i].PlotId);
                    }
                }
            }

            return chosen.ToArray();
        }

        /// <summary>Distance ascending, then plot id ascending; fully deterministic.</summary>
        private static int Compare(PlotCandidate left, PlotCandidate right)
        {
            var byDistance = left.Distance.CompareTo(right.Distance);
            return byDistance != 0 ? byDistance : left.PlotId.CompareTo(right.PlotId);
        }
    }
}
