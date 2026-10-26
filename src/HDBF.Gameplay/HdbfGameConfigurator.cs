// ============================================================================
// HDBF v2 | Layer: Gameplay | Owner: V2-GAMEPLAY-01
// Purpose: Game-context DI for the v2 indoor farm: controller, crop catalog, query/command
//          services, world warning icons and the vanilla haul-out chain. The v1 plot
//          highlight/scan/warning-board registrations are gone with the world-plot chain
//          (section 19.2); the mod ships zero Harmony patches (section 19.18.3-B).
// Requirements: REQ-V2-BLD-06, REQ-V2-CORE-03, REQ-V2-SAFE-05, REQ-V2-MAINT-03
// ============================================================================
using Bindito.Core;
using HDBF.Core.Abstractions;
using HDBF.Core.Indoor;
using HDBF.Core.Logging;
using HDBF.Gameplay.Components;
using HDBF.Gameplay.Farming;
using HDBF.Gameplay.Specs;
using Timberborn.Emptying;
using Timberborn.Hauling;
using Timberborn.SimpleOutputBuildings;
using Timberborn.TemplateInstantiation;

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

            // Four-class world warning icons driven by the Core IndoorWarningFlags (section 19.11).
            Bind<HdbfWorldIconService>().AsSingleton();

            Bind<HdbfBoxFarmController>().AsTransient();
            Bind<IndoorFarmWorkplaceBehavior>().AsTransient();

            MultiBind<TemplateModule>().ToProvider(ProvideTemplateModule).AsSingleton();
        }

        /// <summary>
        /// Declares the components every box farm gains on top of its blueprint data:
        /// the indoor controller, the minimal workplace behavior (employment only) and the
        /// vanilla output-hauling chain for the harvest inventory.
        /// </summary>
        private static TemplateModule ProvideTemplateModule()
        {
            var builder = new TemplateModule.Builder();
            builder.AddDecorator<HdbfBoxFarmSpec, HdbfBoxFarmController>();
            builder.AddDecorator<HdbfBoxFarmSpec, IndoorFarmWorkplaceBehavior>();

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
