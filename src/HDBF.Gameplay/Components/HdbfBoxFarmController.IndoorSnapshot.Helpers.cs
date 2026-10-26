// ============================================================================
// HDBF v2 | Layer: Gameplay/Components | Owner: V2-GAMEPLAY-02
// Purpose: Port/output helpers of the v2 panel snapshot. Split from .IndoorSnapshot so both
//          files stay within the 300-line gate; every game-object read is isolated so a broken
//          adapter can never break the panel (REQ-V2-SAFE-01/07).
// Requirements: REQ-V2-COMPAT-02, REQ-V2-UI-01/02, REQ-V2-SAFE-01
// ============================================================================
using System;

namespace HDBF.Gameplay.Components
{
    public partial class HdbfBoxFarmController
    {
        private bool LogisticsConnected()
        {
            try
            {
                return _adapterHost.IsLogisticsConnected;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private int OutputOccupied()
        {
            try
            {
                var inventory = _outputInventory?.Inventory;
                return inventory is null ? 0 : Math.Max(0, inventory.TotalAmountInStock);
            }
            catch (Exception)
            {
                return 0;
            }
        }
    }
}
