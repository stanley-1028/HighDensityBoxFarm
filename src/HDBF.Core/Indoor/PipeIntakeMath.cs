// ============================================================================
// HDBF v2 | Layer: Core/Indoor | Owner: V2-CORE-01
// Purpose: Pure geometry of the farm pipe intake: turns the occupied footprint cells into the
//          unique face-adjacent grid cells a Timber Pipes transport pipe could sit on. No game
//          type, no allocation on an empty footprint, deterministic order for tests.
// Requirements: REQ-V2-WATER-02, REQ-V2-COMPAT-01, REQ-V2-TEST-01
// ============================================================================
using System;
using System.Collections.Generic;

namespace HDBF.Core.Indoor
{
    /// <summary>
    /// Pure helper of the farm pipe intake (REQ-V2-WATER-02): a farm accepts water from a
    /// Timber Pipes transport pipe that sits on a cell adjacent to the farm's occupied cells.
    /// This class only computes that neighbour set; it never asks the external mod anything.
    /// </summary>
    public static class PipeIntakeMath
    {
        /// <summary>
        /// Returns every unique grid cell that shares a face with an occupied cell and is not
        /// itself occupied. The result is sorted by z, then y, then x so tests and diagnostics
        /// see a stable order. A null/empty footprint answers an empty array, never null.
        /// </summary>
        /// <param name="footprint">Occupied world cells of the farm; null/empty when unknown.</param>
        /// <returns>Face-adjacent cells outside the footprint; never null.</returns>
        public static PipeCell[] NeighborCells(IReadOnlyList<PipeCell>? footprint)
        {
            if (footprint is null || footprint.Count == 0)
            {
                return Array.Empty<PipeCell>();
            }

            var occupied = new HashSet<PipeCell>();
            for (var index = 0; index < footprint.Count; index++)
            {
                occupied.Add(footprint[index]);
            }

            var neighbors = new HashSet<PipeCell>();
            for (var index = 0; index < footprint.Count; index++)
            {
                var cell = footprint[index];
                AddNeighbor(neighbors, occupied, cell.X - 1, cell.Y, cell.Z);
                AddNeighbor(neighbors, occupied, cell.X + 1, cell.Y, cell.Z);
                AddNeighbor(neighbors, occupied, cell.X, cell.Y - 1, cell.Z);
                AddNeighbor(neighbors, occupied, cell.X, cell.Y + 1, cell.Z);
                AddNeighbor(neighbors, occupied, cell.X, cell.Y, cell.Z - 1);
                AddNeighbor(neighbors, occupied, cell.X, cell.Y, cell.Z + 1);
            }

            var result = new PipeCell[neighbors.Count];
            neighbors.CopyTo(result);
            Array.Sort(result, CompareCells);
            return result;
        }

        private static void AddNeighbor(HashSet<PipeCell> neighbors, HashSet<PipeCell> occupied,
            int x, int y, int z)
        {
            var candidate = new PipeCell(x, y, z);
            if (!occupied.Contains(candidate))
            {
                neighbors.Add(candidate);
            }
        }

        private static int CompareCells(PipeCell left, PipeCell right)
        {
            if (left.Z != right.Z)
            {
                return left.Z.CompareTo(right.Z);
            }

            if (left.Y != right.Y)
            {
                return left.Y.CompareTo(right.Y);
            }

            return left.X.CompareTo(right.X);
        }
    }
}
