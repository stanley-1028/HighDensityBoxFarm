// ============================================================================
// HDBF v2 | Layer: Integration/Adapters | Owner: V2-INTEG-01
// Purpose: WP-05/L-02 per-farm honesty check: the read-only probe asks the mod's own
//          PipeRegistry.TryGetGraph anchor whether the farm's pipe port sits on a pipe graph,
//          so "connected to the settlement-wide network" can be distinguished from "this farm
//          is on the network". Read-only: no draw, no health strike, every failure falls back.
// Requirements: REQ-V2-WATER-02, REQ-V2-COMPAT-01, REQ-V2-COMPAT-02, REQ-V2-SAFE-05
// ============================================================================
using System;
using HDBF.Integration.Reflection;
using UnityEngine;

namespace HDBF.Integration.Adapters
{
    internal sealed partial class TimberPipesWaterBinding
    {
        /// <summary>
        /// Asks the mod's <c>PipeRegistry.TryGetGraph(Vector3Int, out PipeGraph)</c> anchor whether one
        /// grid cell carries a pipe graph. The method and its signature are the documented
        /// <see cref="HDBF.Core.Compat.CompatAnchorTable"/> Timber Pipes anchor, resolved through the
        /// cached <see cref="ExternalProxy"/> binding; nothing third-party appears at compile time.
        ///
        /// Returns false when the answer is unknown (anchor missing, registry unresolved, reflection
        /// failed) so the caller keeps the census result; a true return always carries a definite
        /// <paramref name="hasGraph"/>. It never draws water and never reports an adaptor failure.
        /// </summary>
        /// <param name="x">Grid x of the farm pipe port.</param>
        /// <param name="y">Grid y of the farm pipe port.</param>
        /// <param name="z">Grid z of the farm pipe port.</param>
        /// <param name="hasGraph">True when a pipe graph exists at that cell and the answer is known.</param>
        /// <returns>True when the graph question was answered; false when the caller must fall back.</returns>
        internal bool TryProbeFarmGraph(int x, int y, int z, out bool hasGraph)
        {
            hasGraph = false;
            try
            {
                if (_tryGetGraph is null || !TryResolveRegistry() || _registry is null)
                {
                    // Older mod / hot reloaded binding: no anchor, so the census answer stays honest
                    // enough ("pipe water is available") and no failure is counted (R-04).
                    return false;
                }

                var arguments = new object?[] { new Vector3Int(x, y, z), null };
                var result = _tryGetGraph.Invoke(_registry, arguments);
                if (result is not bool)
                {
                    return false;
                }

                // The out PipeGraph is the second argument; a null graph means "this cell is not on a
                // network", which is exactly the settlement-wide-supply case the probe reports.
                hasGraph = arguments[1] is not null;
                return true;
            }
            catch (Exception exception)
            {
                // REQ-V2-SAFE-01: a read-only diagnostic must never escape into the panel/render path.
                SetFailure("water-farm-graph-exception", exception);
                return false;
            }
        }
    }
}
