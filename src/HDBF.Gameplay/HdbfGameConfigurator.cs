using Bindito.Core;
using HDBF.Core.Abstractions;
using HDBF.Core.Growth;
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
    /// Game-context registrations for HDBF (docs/API_FACTS.md §2/§4):
    /// * the controller is transient because every building entity gets its own instance;
    /// * registrar, query service and the shared interfaces are session singletons, and the
    ///   interface bindings use <c>ToExisting</c> so UI and gameplay observe the same instance
    ///   (Bindito's singleton scope is per bound type, so two independent bindings would create
    ///   two separate registries);
    /// * the template module adds the controller to every entity that has
    ///   <see cref="HdbfBoxFarmSpec"/>, which is the verified decorator pattern from
    ///   <c>WaterBuildingsConfigurator</c> and <c>FieldsConfigurator</c>.
    ///
    /// No Harmony patch is registered by this assembly: ADR-009's single approved Postfix lives in
    /// HDBF.Integration (it consumes <see cref="IGrowthMultiplierSource"/>, bound here).
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
            Bind<IHdbfFarmRegistry>().ToExisting<HdbfFarmQueryService>();

            // ADR-009 growth-rate lookup: the one approved Harmony Postfix (HDBF.Integration)
            // consumes the Core interface, so Integration never references this assembly. The
            // implementation reads the live plot registry plus the farm's cached multipliers and
            // performs no work while the patch is disabled or no farm exists.
            Bind<HdbfGrowthRateProvider>().AsSingleton();
            Bind<IGrowthMultiplierSource>().ToExisting<HdbfGrowthRateProvider>();

            // Write-side panel contract (chapter 14.3): the entity panel resolves the same
            // IBoxFarmCommand instance as the command service that owns the material transaction.
            Bind<HdbfFarmCommandService>().AsSingleton();
            Bind<IBoxFarmCommand>().ToExisting<HdbfFarmCommandService>();

            // Managed-plot overlay (REQ-UI-05): the session state is read by every controller
            // snapshot (authoritative toggle/failure read model) and the overlay service owns the
            // world-space input + the instanced renderer. Both are game-context singletons; the
            // controller and the command service inject the same state instance.
            Bind<HdbfPlotHighlightSession>().AsSingleton();
            Bind<HdbfPlotHighlightService>().AsSingleton();

            // World-space P0/P1 warning icons (chapter 14.2/14.6). The board is the 1 s
            // cross-building aggregation cache consumed by the panel through a Core contract; the
            // icon service is the 0.5 s, on-screen-only coordinator that registers vanilla status
            // toggles on each farm. The locator is the panel's "click a member row" action, backed
            // by the vanilla selection service.
            Bind<HdbfWarningBoard>().AsSingleton();
            Bind<IBoxFarmWarningQuery>().ToExisting<HdbfWarningBoard>();
            Bind<HdbfWorldIconService>().AsSingleton();
            Bind<HdbfFarmLocator>().AsSingleton();
            Bind<IBoxFarmLocator>().ToExisting<HdbfFarmLocator>();

            Bind<HdbfBoxFarmController>().AsTransient();

            // P1 crop loop: the work dispatcher is a workplace behaviour component of the building.
            // It is transient because every entity gets its own instance, exactly like the
            // controller and like vanilla farmhouse behaviours.
            Bind<HdbfFarmWorkplaceBehavior>().AsTransient();

            MultiBind<TemplateModule>().ToProvider(ProvideTemplateModule).AsSingleton();
        }

        /// <summary>
        /// Declares the components every box farm gains on top of its blueprint data:
        /// * the shared controller (REQ-ARCH-07);
        /// * the P1 work dispatcher (zero-patch route B, see docs/workstreams/farm-loop.md);
        /// * the vanilla output-hauling chain for the harvest inventory, so harvested crops are
        ///   carried out by the normal hauler system instead of HDBF modifying global resources.
        /// </summary>
        private static TemplateModule ProvideTemplateModule()
        {
            var builder = new TemplateModule.Builder();
            builder.AddDecorator<HdbfBoxFarmSpec, HdbfBoxFarmController>();
            builder.AddDecorator<HdbfBoxFarmSpec, HdbfFarmWorkplaceBehavior>();

            // Vanilla haul-out chain. The receiving inventory itself comes from the blueprint's
            // SimpleOutputInventorySpec (data requirement); these decorators make the stock
            // emptyable/haulable, matching what vanilla does for farmhouses and workshops.
            builder.AddDecorator<HdbfBoxFarmSpec, AutoEmptiable>();
            builder.AddDecorator<HdbfBoxFarmSpec, Emptiable>();
            builder.AddDecorator<HdbfBoxFarmSpec, HaulCandidate>();
            builder.AddDecorator<HdbfBoxFarmSpec, SimpleOutputInventoryHaulBehaviorProvider>();
            builder.AddDecorator<HdbfBoxFarmSpec, EmptyOutputWorkplaceBehavior>();
            return builder.Build();
        }
    }
}
