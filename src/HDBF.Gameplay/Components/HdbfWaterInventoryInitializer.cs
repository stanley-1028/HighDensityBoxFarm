// ============================================================================
// HDBF v2 | Layer: Gameplay/Components | Owner: V2-GAMEPLAY-02
// Purpose: The vanilla water input inventory of every box farm (section 19.6): haulers carry real
//          water goods from the district's water pumps and water tanks into this input inventory,
//          driven by the normal hauling priorities, and the controller converts the delivered goods
//          into the simulated internal tank. Capacity is the tier tank capacity in whole goods.
// Requirements: REQ-V2-WATER-02, REQ-V2-WATER-04, REQ-V2-LABOR-03, REQ-V2-SAFE-01
// ============================================================================
using System;
using HDBF.Core.Indoor;
using HDBF.Gameplay.Specs;
using Timberborn.Goods;
using Timberborn.InventorySystem;
using Timberborn.TemplateInstantiation;

namespace HDBF.Gameplay.Components
{
    /// <summary>
    /// Creates and initializes the farm's water input inventory (a vanilla public input inventory,
    /// so the game's own haulers can deliver water into it). One dedicated decorator initializer is
    /// registered in the mod template module; the spec of the building decides the capacity.
    /// </summary>
    public sealed class HdbfWaterInventoryInitializer : IDedicatedDecoratorInitializer<HdbfBoxFarmSpec, Inventory>
    {
        /// <summary>Vanilla inventory component name: visible in saves and the hauling UI.</summary>
        public const string InventoryComponentName = "Inventory:Water";

        private readonly InventoryInitializerFactory _initializerFactory;

        /// <summary>Creates the initializer with the vanilla factory used by every inventory owner.</summary>
        /// <param name="initializerFactory">Vanilla inventory initializer factory.</param>
        public HdbfWaterInventoryInitializer(InventoryInitializerFactory initializerFactory)
        {
            _initializerFactory = initializerFactory
                ?? throw new ArgumentNullException(nameof(initializerFactory));
        }

        /// <summary>
        /// Whole water goods the farm can buffer: the tier tank capacity in units (one good is one
        /// water unit in the mod's mapping), rounded up so the buffer never undershoots the tank.
        /// </summary>
        /// <param name="spec">Farm spec of the building being instantiated.</param>
        /// <returns>Inventory capacity in goods; at least 1 for a valid spec.</returns>
        public static int CapacityGoods(HdbfBoxFarmSpec spec)
        {
            var slots = Math.Max(0, spec.PlotCapacity);
            var days = Math.Max(0, spec.TankCapacityDays);
            var units = spec.WaterPerPlotPerDay * slots * days;
            if (float.IsNaN(units) || float.IsInfinity(units) || units <= 0f)
            {
                return 1;
            }

            return Math.Max(1, (int)Math.Ceiling(units));
        }

        /// <inheritdoc />
        public void Initialize(HdbfBoxFarmSpec subject, Inventory decorator)
        {
            var capacity = CapacityGoods(subject);
            var initializer = _initializerFactory.Create(decorator, capacity, InventoryComponentName);
            initializer.AddAllowedGood(new StorableGoodAmount(
                StorableGood.CreateGiveableAndTakeable(IndoorConstants.WaterGoodId), capacity));
            initializer.HasPublicInput();
            initializer.Initialize();
        }
    }
}
