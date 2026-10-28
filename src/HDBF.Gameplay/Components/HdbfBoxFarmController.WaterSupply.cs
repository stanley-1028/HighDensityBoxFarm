// ============================================================================
// HDBF v2 | Layer: Gameplay/Components | Owner: V2-GAMEPLAY-02
// Purpose: Water delivery of one farm (section 19.6): enables the vanilla water input inventory
//          (the owner building must do that; its initializer leaves it disabled), converts the
//          water goods the farm workers carried into it into the simulated internal tank, and
//          keeps the panel water-supply state fresh after loading and while the panel is open.
// Requirements: REQ-V2-WATER-02, REQ-V2-WATER-04, REQ-V2-LABOR-03, REQ-V2-SAFE-01
// ============================================================================
using System;
using HDBF.Core;
using HDBF.Core.Indoor;
using HDBF.Core.Logging;
using Timberborn.Goods;
using Timberborn.InventorySystem;

namespace HDBF.Gameplay.Components
{
    public partial class HdbfBoxFarmController
    {
        /// <summary>Snapshot reads between two panel water-supply re-probes (~4 s at the panel's 4 Hz).</summary>
        private const int SupplyProbeSnapshotInterval = 16;

        /// <summary>Pipe state read by the activation/daily tick, reused by the panel re-probes.</summary>
        private bool _hasPipeWaterCached;

        /// <summary>Snapshot reads left before the panel re-probes the water source again.</summary>
        private int _supplyProbeCountdown = 4;

        /// <summary>The farm's vanilla water input inventory the haulers deliver into.</summary>
        private Inventory? _waterInventory;

        /// <summary>
        /// Finds the water input inventory created by <see cref="HdbfWaterInventoryInitializer"/>.
        /// Runs in Awake (the inventory initializers run before component Awake), so the hauling
        /// hand-off is known before the first day tick or panel read.
        /// </summary>
        private void ResolveWaterInventory()
        {
            try
            {
                _waterInventory = null;
                if (!HasComponent<Inventories>())
                {
                    return;
                }

                var inventories = GetComponent<Inventories>();
                var waterId = IndoorConstants.WaterGoodId;
                // AllInventories, not EnabledInventories: the vanilla initializer creates the
                // inventory disabled and only the owner building enables it (see
                // EnsureWaterInventoryEnabled), so an enabled-only scan would never find it.
                var all = inventories.AllInventories;
                for (var index = 0; index < all.Count; index++)
                {
                    var inventory = all[index];
                    if (inventory is null)
                    {
                        continue;
                    }

                    if (string.Equals(inventory.ComponentName, HdbfWaterInventoryInitializer.InventoryComponentName,
                            StringComparison.Ordinal))
                    {
                        _waterInventory = inventory;
                        return;
                    }

                    // Fallback for an older save/renamed component: the only inventory of the farm
                    // that accepts water as an input good.
                    if (_waterInventory is null && inventory.Takes(waterId) && inventory.Capacity > 0)
                    {
                        _waterInventory = inventory;
                    }
                }
            }
            catch (Exception exception)
            {
                _waterInventory = null;
                _log.Error(HdbfLogCategory.Warn, IndoorLogKeys.WaterSettle,
                    "water input inventory lookup failed; hauling stays disconnected this session",
                    exception, _entityId.ToString());
            }
        }

        /// <summary>
        /// Turns the water input inventory on. The vanilla inventory initializer creates the
        /// component disabled and the owner building has to enable it when it becomes finished
        /// (SimpleOutputInventory, Manufactory, Stockpile and FireworkLauncher all do this in their
        /// OnEnterFinishedState) - a disabled inventory never joins
        /// <c>Inventories.EnabledInventories</c>, so the vanilla fill-input chain silently finds no
        /// target and no water is ever hauled. The inventory is re-resolved here because Awake may
        /// run before the component it decorates is registered.
        /// </summary>
        private void EnsureWaterInventoryEnabled()
        {
            try
            {
                var inventory = _waterInventory;
                if (inventory is null || !inventory.Enabled)
                {
                    ResolveWaterInventory();
                    inventory = _waterInventory;
                }

                if (inventory is null || inventory.Enabled)
                {
                    return;
                }

                inventory.Enable();
                _log.Write(HdbfLogCategory.Info, IndoorLogKeys.WaterSettle,
                    "water input inventory enabled: capacity=" + inventory.Capacity
                    + ", entityId=" + _entityId);
            }
            catch (Exception exception)
            {
                // REQ-V2-SAFE-01: hauled water is a convenience, never a reason to break activation.
                _log.Error(HdbfLogCategory.Warn, IndoorLogKeys.RefillFailed,
                    "water input inventory could not be enabled; hauling stays idle", exception,
                    _entityId.ToString());
            }
        }

        /// <summary>
        /// Turns the water input inventory off again when the building leaves the finished state, so
        /// a reverted construction site is no hauling target (owner-side parity with vanilla).
        /// </summary>
        private void DisableWaterInventory()
        {
            try
            {
                var inventory = _waterInventory;
                if (inventory is not null && inventory.Enabled)
                {
                    inventory.Disable();
                }
            }
            catch (Exception exception)
            {
                _log.Error(HdbfLogCategory.Warn, IndoorLogKeys.RefillFailed,
                    "water input inventory could not be disabled; it stays active", exception,
                    _entityId.ToString());
            }
        }

        /// <summary>
        /// Moves whole water goods the workers carried into the internal tank (one good is one
        /// water unit) and only up to the tank headroom, so the goods stay in the inventory when the
        /// tank is already full. Runs once per day and, cheaply, on the throttled panel refresh so a
        /// freshly delivered load shows up immediately.
        /// </summary>
        private void ApplyHauledWater()
        {
            if (_record is null || !_activated)
            {
                return;
            }

            // Self-heal: with the vanilla chain the inventory must be enabled before it can be
            // filled; doing it here also covers a farm that was already finished when loaded.
            EnsureWaterInventoryEnabled();
            var inventory = _waterInventory;
            if (inventory is null)
            {
                return;
            }

            try
            {
                var capacity = _profile.TankCapacity;
                var tank = SanitizeTank(_record.Tank, capacity);
                var headroom = capacity - tank;
                if (!(headroom >= 1f))
                {
                    return;
                }

                var waterId = IndoorConstants.WaterGoodId;
                var stock = inventory.UnreservedAmountInStock(waterId);
                if (stock <= 0)
                {
                    return;
                }

                var take = (int)headroom;
                if (take > stock)
                {
                    take = stock;
                }

                if (take <= 0)
                {
                    return;
                }

                inventory.TakeExisting(new GoodAmount(waterId, take));
                _record.Tank = tank + take;
                _saveDirty = true;
                _log.Write(HdbfLogCategory.Info, IndoorLogKeys.WaterSettle,
                    "hauled water accepted: +" + take + " units, tank=" + Format(_record.Tank)
                    + ", entityId=" + _entityId);
            }
            catch (Exception exception)
            {
                // REQ-V2-SAFE-01: a broken inventory must not break the daily water settlement.
                _log.Error(HdbfLogCategory.Warn, IndoorLogKeys.RefillFailed,
                    "hauled water conversion failed; the goods stay in the input inventory", exception,
                    _entityId.ToString());
            }
        }

        /// <summary>
        /// Keeps the panel's water-supply state live while the game is paused or freshly loaded: the
        /// player can move/build a water tank next to the farm and look at the panel immediately,
        /// while the daily tick is the only other refresh point. Throttled by snapshot reads (the
        /// panel reads the snapshot at 4 Hz), so the probe stays cheap.
        /// </summary>
        private void RefreshSupplyForPanel()
        {
            if (!_activated || _record is null)
            {
                return;
            }

            ApplyHauledWater();
            if (_supplyProbeCountdown > 0)
            {
                _supplyProbeCountdown--;
                return;
            }

            _supplyProbeCountdown = SupplyProbeSnapshotInterval;
            try
            {
                RefreshWaterSupply(_hasPipeWaterCached);
            }
            catch (Exception exception)
            {
                _log.Error(HdbfLogCategory.Warn, IndoorLogKeys.ManualRefill,
                    "panel water-supply refresh failed; keeping the last state", exception,
                    _entityId.ToString());
            }
        }

        /// <summary>
        /// Re-probes the supply once after the whole save finished loading: activation itself runs
        /// during the entity initialization phase, where a nearby water tank may not be registered
        /// in its district inventory yet - which used to leave a freshly loaded farm on "no water
        /// source" until the next daily tick.
        /// </summary>
        private void RefreshSupplyAfterLoad()
        {
            if (!_activated || _record is null)
            {
                return;
            }

            try
            {
                ApplyHauledWater();
                _ = ReadPipeWater(out var hasPipeWater);
                _hasPipeWaterCached = hasPipeWater;
                RefreshWaterSupply(hasPipeWater);
            }
            catch (Exception exception)
            {
                _log.Error(HdbfLogCategory.Warn, IndoorLogKeys.ManualRefill,
                    "post-load water-supply refresh failed; the daily tick retries", exception,
                    _entityId.ToString());
            }
        }

        /// <summary>Clamps a persisted tank value into [0, capacity]; NaN/infinity become 0.</summary>
        private static float SanitizeTank(float tank, float capacity)
        {
            if (float.IsNaN(tank) || float.IsInfinity(tank) || tank <= 0f)
            {
                return 0f;
            }

            return capacity > 0f && tank > capacity ? capacity : tank;
        }
    }
}
