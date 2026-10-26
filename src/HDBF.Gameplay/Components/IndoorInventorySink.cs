// ============================================================================
// HDBF v2 | Layer: Gameplay/Components | Owner: V2-GAMEPLAY-01
// Purpose: IInventorySink over the building's vanilla SimpleOutputInventory. The only
//          place harvested indoor produce enters the game inventory; no global resource
//          mutation and no exception into the daily tick.
// Requirements: REQ-V2-SAVE-04, REQ-V2-CORE-03, REQ-V2-SAFE-01
// ============================================================================
using System;
using HDBF.Core.Indoor;
using Timberborn.Goods;
using Timberborn.SimpleOutputBuildings;

namespace HDBF.Gameplay.Components
{
    /// <summary>
    /// Output sink of one indoor farm. The building blueprint owns a
    /// <see cref="SimpleOutputInventorySpec"/>; this adapter writes harvested goods through its
    /// vanilla <see cref="Inventory"/> so the normal hauler/emptier chain carries them out
    /// (section 19.1/19.10) instead of HDBF touching district resources.
    /// </summary>
    internal sealed class IndoorInventorySink : IInventorySink
    {
        private readonly SimpleOutputInventory _output;

        /// <summary>Creates a sink over a live output-inventory component.</summary>
        /// <param name="output">The building's vanilla output inventory; never null here.</param>
        internal IndoorInventorySink(SimpleOutputInventory output)
        {
            _output = output ?? throw new ArgumentNullException(nameof(output));
        }

        /// <summary>True when the output inventory can currently accept the full amount.</summary>
        public bool TryAdd(string goodId, int amount)
        {
            if (string.IsNullOrEmpty(goodId) || amount <= 0)
            {
                return false;
            }

            try
            {
                var inventory = _output.Inventory;
                if (inventory is null || inventory.UnreservedCapacity(goodId) < amount)
                {
                    // Section 19.5: a full output is not a harvest; the slot stays mature.
                    return false;
                }

                inventory.GiveProduced(new GoodAmount(goodId, amount));
                return true;
            }
            catch (Exception)
            {
                // A throwing/disabled inventory means "not stored"; the slot stays mature and the
                // daily tick reports an output backlog (REQ-V2-SAFE-01).
                return false;
            }
        }
    }
}
