// ============================================================================
// HDBF v2 | Layer: Gameplay | Owner: V2-GAMEPLAY-01
// Purpose: Game-context DI for the v2 indoor farm: controller, crop catalog, query/command
//          services, retained warning/world-icon framework and the vanilla haul-out chain.
//          The plot highlight/scan registrations are gone with the v1 world-plot pipeline.
// Requirements: REQ-V2-BLD-06, REQ-V2-CORE-03, REQ-V2-SAFE-05, REQ-V2-MAINT-03
// ============================================================================
using Bindito.Core;
using HDBF.Core.Abstractions;
using HDBF.Core.Growth;
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
            Bind<IHdbfFarmQueryService>().ToExisting<HdbfFarmQueryService>();

            // Vanilla-spec crop catalogue with the built-in fallback table (REQ-V2-EXT-01).
            Bind<GameCropCatalog>().AsSingleton();
            Bind<ICropCatalog>().ToExisting<GameCropCatalog>();

            // The v2 world has no plots, so the one approved v1 patch is permanently disabled.
            // The binding stays until V2-GAMEPLAY-02 removes the patch itself.
            Bind<ReadOnlyGrowthRateProvider>().AsSingleton();
            Bind<IGrowthMultiplierSource>().ToExisting<ReadOnlyGrowthRateProvider>();

            Bind<HdbfFarmCommandService>().AsSingleton();
            Bind<IBoxFarmCommand>().ToExisting<HdbfFarmCommandService>();

            // Manual refill probe (V2-GAMEPLAY-02): real water body / water-good inventories within
            // 12 cells plus the worker walk-reachability check. Consumed by the controller only; a
            // broken water/navigation API degrades to "no source" inside the probe itself.
            Bind<HdbfManualWaterSource>().AsSingleton();

            Bind<HdbfWarningBoard>().AsSingleton();
            Bind<IBoxFarmWarningQuery>().ToExisting<HdbfWarningBoard>();
            Bind<HdbfWorldIconService>().AsSingleton();
            Bind<HdbfFarmLocator>().AsSingleton();
            Bind<IBoxFarmLocator>().ToExisting<HdbfFarmLocator>();

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
