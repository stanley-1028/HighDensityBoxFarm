using Bindito.Core;
using HDBF.UI.Panels;
using Timberborn.EntityPanelSystem;

namespace HDBF.UI
{
    /// <summary>
    /// Composition root of the UI assembly (chapter 9.2 / 9.4 "UI binding").
    ///
    /// Bindito discovers every non-abstract <see cref="Configurator"/> in the AppDomain and
    /// installs the ones whose <see cref="ContextAttribute"/> matches the active context, so no
    /// manual registration is needed for a mod assembly (verified in docs/API_FACTS.md §2).
    /// The fragment itself is injected into the entity panel through the same
    /// <see cref="EntityPanelModule"/> multi-binding the vanilla UI configurators use.
    /// </summary>
    [Context("Game")]
    public sealed class HdbfUiConfigurator : Configurator
    {
        protected override void Configure()
        {
            Bind<HdbfFarmPanelFragment>().AsSingleton();
            MultiBind<EntityPanelModule>().ToProvider<HdbfEntityPanelModuleProvider>().AsSingleton();
        }
    }
}
