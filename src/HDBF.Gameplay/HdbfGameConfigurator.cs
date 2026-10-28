// ============================================================================
// HDBF v2 | Layer: Gameplay | Owner: V2-GAMEPLAY-01
// Purpose: Game-context DI for the v2 indoor farm: controller, crop catalog, query/command
//          services, world warning icons, the vanilla haul-out chain and the hauler-fed water
//          input inventory (vanilla fill-input behavior). The v1 plot highlight/scan/warning-board
//          registrations are gone with the world-plot chain (section 19.2); the mod ships zero
//          Harmony patches (section 19.18.3-B).
// Requirements: REQ-V2-BLD-06, REQ-V2-CORE-03, REQ-V2-SAFE-05, REQ-V2-MAINT-03, REQ-V2-WATER-02
// ============================================================================
using System;
using Bindito.Core;
using HDBF.Core.Abstractions;
using HDBF.Core.Indoor;
using HDBF.Core.Logging;
using HDBF.Gameplay.Components;
using HDBF.Gameplay.Farming;
using HDBF.Gameplay.Specs;
using Timberborn.Emptying;
using Timberborn.Hauling;
using Timberborn.InventorySystem;
using Timberborn.SimpleOutputBuildings;
using Timberborn.TemplateInstantiation;
using Timberborn.Workshops;
using Timberborn.WorkSystem;

namespace HDBF.Gameplay
{
    /// <summary>
    /// Game-context registrations for HDBF. The controller and workplace behavior are transient
    /// because every building entity owns an instance; the services are session singletons bound
    /// through interfaces with <c>ToExisting</c> so UI and gameplay observe the same instance.
    ///
    /// <c>IIndoorAdapterHost</c> is deliberately not bound here: HDBF.Integration binds it and
    /// Gameplay consumes only the Core interface, so no Integration type leaks into this assembly.
    /// </summary>
    [Context("Game")]
    public class HdbfGameConfigurator : Configurator
    {
        /// <inheritdoc />
        protected override void Configure()
        {
            Bind<HdbfSystemRegistrar>().AsSingleton();
            Bind<IHdbfLog>().ToExisting<HdbfSystemRegistrar>();
            Bind<IHdbfGameConfigProvider>().ToExisting<HdbfSystemRegistrar>();

            Bind<HdbfFarmQueryService>().AsSingleton();
            Bind<IBoxFarmQuery>().ToExisting<HdbfFarmQueryService>();

            // Vanilla-spec crop catalogue with the built-in fallback table (REQ-V2-EXT-01).
            Bind<GameCropCatalog>().AsSingleton();
            Bind<ICropCatalog>().ToExisting<GameCropCatalog>();

            Bind<HdbfFarmCommandService>().AsSingleton();
            Bind<IBoxFarmCommand>().ToExisting<HdbfFarmCommandService>();

            // Manual refill probe (V2-GAMEPLAY-02): real water body / water-good inventories within
            // 12 cells plus the worker walk-reachability check. Consumed by the controller only; a
            // broken water/navigation API degrades to "no source" inside the probe itself.
            Bind<HdbfManualWaterSource>().AsSingleton();

            // Continuous water drain (player request 2026-10-28): a per-frame service turns the
            // elapsed game time into water slices; finished farms register while they are active.
            Bind<HdbfWaterFlowService>().AsSingleton();

            // Four-class world warning icons driven by the Core IndoorWarningFlags (section 19.11).
            Bind<HdbfWorldIconService>().AsSingleton();

            Bind<HdbfBoxFarmController>().AsTransient();
            Bind<IndoorFarmWorkplaceBehavior>().AsTransient();

            MultiBind<TemplateModule>().ToProvider<TemplateModuleProvider>().AsSingleton();
        }

        /// <summary>
        /// Template module provider: unlike a static lambda it can take the vanilla inventory
        /// initializer factory from DI, which the water input inventory initializer needs.
        /// </summary>
        public sealed class TemplateModuleProvider : IProvider<TemplateModule>
        {
            private readonly InventoryInitializerFactory _inventoryInitializerFactory;

            /// <summary>Creates the provider; every dependency is a DI singleton.</summary>
            /// <param name="inventoryInitializerFactory">Vanilla inventory initializer factory.</param>
            public TemplateModuleProvider(InventoryInitializerFactory inventoryInitializerFactory)
            {
                _inventoryInitializerFactory = inventoryInitializerFactory
                    ?? throw new ArgumentNullException(nameof(inventoryInitializerFactory));
            }

            /// <inheritdoc />
            public TemplateModule Get()
            {
                return BuildTemplateModule(_inventoryInitializerFactory);
            }
        }

        /// <summary>
        /// Declares the components every box farm gains on top of its blueprint data:
        /// the indoor controller, the minimal workplace behavior (employment only), the hauler-fed
        /// water input inventory and the vanilla output-hauling chain for the harvest inventory.
        /// </summary>
        /// <param name="inventoryInitializerFactory">Vanilla inventory initializer factory.</param>
        internal static TemplateModule BuildTemplateModule(InventoryInitializerFactory inventoryInitializerFactory)
        {
            var builder = new TemplateModule.Builder();
            builder.AddDecorator<HdbfBoxFarmSpec, HdbfBoxFarmController>();
            builder.AddDecorator<HdbfBoxFarmSpec, IndoorFarmWorkplaceBehavior>();

            // Player request 2026-10-28: the farmers do their (simulated) planting/harvesting work
            // inside the building. This vanilla workplace behavior walks them through the entrance
            // and keeps them waiting inside while they are on duty, instead of idle-wandering
            // outside the farm; the building already provides Workplace + Enterable.
            builder.AddDecorator<HdbfBoxFarmSpec, WaitInsideIdlyWorkplaceBehavior>();

            // Hauled water (section 19.6): a vanilla public input inventory plus the vanilla
            // fill-input workplace behavior. District haulers carry real water goods from the water
            // pumps/water tanks into it, using the normal hauling priority rules; the controller
            // converts the delivered goods into the simulated internal tank.
            builder.AddDedicatedDecorator(new HdbfWaterInventoryInitializer(inventoryInitializerFactory));
            builder.AddDecorator<HdbfBoxFarmSpec, FillInputWorkplaceBehavior>();
            builder.AddDecorator<HdbfBoxFarmSpec, FillInputHaulBehaviorProvider>();

            // Vanilla haul-out chain; the inventory itself comes from SimpleOutputInventorySpec.
            builder.AddDecorator<HdbfBoxFarmSpec, AutoEmptiable>();
            builder.AddDecorator<HdbfBoxFarmSpec, Emptiable>();
            builder.AddDecorator<HdbfBoxFarmSpec, HaulCandidate>();
            builder.AddDecorator<HdbfBoxFarmSpec, SimpleOutputInventoryHaulBehaviorProvider>();
            builder.AddDecorator<HdbfBoxFarmSpec, EmptyOutputWorkplaceBehavior>();
            return builder.Build();
        }
    }
}
