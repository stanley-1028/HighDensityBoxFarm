// ============================================================================
// HDBF v2 | Layer: Gameplay/Components | Owner: V2-GAMEPLAY-01 / V2-GAMEPLAY-02
// Purpose: Port/occupancy/fatigue helpers of the v2 panel snapshot and of the retained v1
//          snapshot adapter. Split from .IndoorSnapshot so both files stay within the gate;
//          port states follow Hidden/Available/Connected/Fault (REQ-V2-COMPAT-02).
// Requirements: REQ-V2-WATER-03, REQ-V2-UI-01/02, REQ-V2-COMPAT-02, REQ-V2-SAFE-01
// ============================================================================
using System;
using HDBF.Core;
using HDBF.Core.Indoor;

namespace HDBF.Gameplay.Components
{
    public partial class HdbfBoxFarmController
    {
        private float FatigueMax()
        {
            var slots = _record?.Slots;
            if (slots is null)
            {
                return 0f;
            }

            var maximum = 0f;
            for (var index = 0; index < slots.Length; index++)
            {
                var slot = slots[index];
                if (slot is null)
                {
                    continue;
                }

                var value = Clamp01(slot.Fatigue);
                if (value > maximum)
                {
                    maximum = value;
                }
            }

            return maximum;
        }

        private float MaintenanceDaysLeft()
        {
            var record = _record;
            if (record is null || _profile.MaintenanceKind == HdbfMaintenanceKind.IrrigationOnly)
            {
                return 0f;
            }

            try
            {
                return record.MaintenanceDueDay - _dayNightCycle.PartialDayNumber;
            }
            catch (Exception)
            {
                return 0f;
            }
        }

        private HdbfPortState PortState(HdbfPortKind port)
        {
            var enabled = port switch
            {
                HdbfPortKind.Pipe => _spec.HasPipePort,
                HdbfPortKind.Power => _spec.HasPowerPort && _profile.NeedsPower,
                HdbfPortKind.Logistics => _spec.HasLogisticsPort,
                _ => false,
            };
            if (!enabled)
            {
                return HdbfPortState.Hidden;
            }

            var record = _record;
            if (record is null)
            {
                return HdbfPortState.Hidden;
            }

            switch (port)
            {
                case HdbfPortKind.Pipe:
                    if (!record.PipePort)
                    {
                        return HdbfPortState.Hidden;
                    }

                    if (ReadAdapterHealth() == HdbfAdapterHealth.Failed)
                    {
                        return HdbfPortState.Fault;
                    }

                    return PipeConnected() ? HdbfPortState.Connected : HdbfPortState.Available;
                case HdbfPortKind.Logistics:
                    if (!record.LogisticsPort)
                    {
                        return HdbfPortState.Hidden;
                    }

                    if (ReadAdapterHealth() == HdbfAdapterHealth.Failed)
                    {
                        return HdbfPortState.Fault;
                    }

                    return LogisticsConnected() ? HdbfPortState.Connected : HdbfPortState.Available;
                default:
                    if (!record.PowerPort)
                    {
                        return HdbfPortState.Hidden;
                    }

                    if (record.PowerConnected)
                    {
                        return HdbfPortState.Connected;
                    }

                    // A wired-but-unpowered on-grid top tier is the "power cut" fault state.
                    return record.PowerMode == PowerConnection.Grid
                        ? HdbfPortState.Fault
                        : HdbfPortState.Available;
            }
        }

        private bool PipeConnected()
        {
            try
            {
                return _adapterHost.IsPipeConnected && _adapterHost.PipeWaterSource is not null;
            }
            catch (Exception)
            {
                return false;
            }
        }

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

        private int AdapterCount(HdbfAdapterHealth wanted)
        {
            try
            {
                return _adapterHost.PipeHealth == wanted ? 1 : 0;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        private static float Clamp01(float value)
        {
            if (float.IsNaN(value) || value <= 0f)
            {
                return 0f;
            }

            return value >= 1f ? 1f : value;
        }
    }
}
