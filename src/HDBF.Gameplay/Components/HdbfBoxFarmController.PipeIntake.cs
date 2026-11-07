// ============================================================================
// HDBF v2 | Layer: Gameplay/Components | Owner: V2-GAMEPLAY-02
// Purpose: Publishes the farm's occupied footprint cells to the pipe water adapter before any
//          pipe query/draw, so a Timber Pipes water pipe attached to the farm shell supplies the
//          farm (player report 2026-11-07). The footprint list is cached until the block origin
//          changes; a failure only disables the optional local intake path.
// Requirements: REQ-V2-WATER-02, REQ-V2-COMPAT-01, REQ-V2-SAFE-01
// ============================================================================
using System;
using System.Collections.Generic;
using HDBF.Core.Indoor;
using UnityEngine;

namespace HDBF.Gameplay.Components
{
    public partial class HdbfBoxFarmController
    {
        /// <summary>Cached occupied world cells of this farm; null while unknown.</summary>
        private IReadOnlyList<PipeCell>? _waterFootprint;

        /// <summary>Block origin the cached <see cref="_waterFootprint"/> was built from.</summary>
        private Vector3Int _waterFootprintOrigin;

        /// <summary>
        /// Publishes the farm footprint to the optional <see cref="IIndoorPipeWaterAnchor"/> companion
        /// of the adapter host. Called before every pipe query/draw of this farm (daily tick,
        /// activation, panel snapshot), so the single shared session host always knows which
        /// footprint is asking. The list is cached until the block origin changes, so the 4 Hz panel
        /// path allocates nothing and the adapter can cache the derived neighbourhood. Never throws.
        /// </summary>
        private void PublishWaterAnchor()
        {
            try
            {
                var anchor = _adapterHost as IIndoorPipeWaterAnchor;
                if (anchor is null)
                {
                    return;
                }

                anchor.SetWaterAnchor(ResolveWaterFootprint());
            }
            catch (Exception)
            {
                // The anchor is an optional supply input: it must never break the day tick/panel.
            }
        }

        /// <summary>
        /// Occupied world cells of this farm, or null when the block object is unavailable or its
        /// cells cannot be read. The game object's own positioned blocks are the source of truth, so
        /// a stacked/rotated farm publishes exactly the cells it occupies.
        /// </summary>
        private IReadOnlyList<PipeCell>? ResolveWaterFootprint()
        {
            var blockObject = _blockObject;
            if (blockObject is null)
            {
                return null;
            }

            try
            {
                var origin = blockObject.Coordinates;
                if (_waterFootprint is not null && _waterFootprintOrigin == origin)
                {
                    return _waterFootprint;
                }

                var cells = new List<PipeCell>();
                foreach (var cell in blockObject.PositionedBlocks.GetOccupiedCoordinates())
                {
                    cells.Add(new PipeCell(cell.x, cell.y, cell.z));
                }

                _waterFootprint = cells.Count > 0 ? cells : null;
                _waterFootprintOrigin = origin;
                return _waterFootprint;
            }
            catch (Exception)
            {
                // A moved/partially initialized block object: retry on the next call, degrade now.
                _waterFootprint = null;
                _waterFootprintOrigin = default;
                return null;
            }
        }
    }
}
