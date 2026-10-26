using Bindito.Core;
using Timberborn.EntityPanelSystem;

namespace HDBF.UI.Panels
{
    /// <summary>
    /// Contributes the box-farm fragment to the game's entity panel. This mirrors the pattern the
    /// vanilla UI assemblies use: each one multi-binds <see cref="EntityPanelModule"/> with an
    /// <see cref="IProvider{T}"/> that builds a module from its fragments (verified against
    /// <c>Timberborn.FieldsUI.FieldsUIConfigurator+EntityPanelModuleProvider::Get</c>).
    /// </summary>
    public sealed class HdbfEntityPanelModuleProvider : IProvider<EntityPanelModule>
    {
        /// <summary>
        /// Order inside the content area. Vanilla farm-house content uses order 1, so keeping the
        /// same slot places the box-farm rows next to the other building rows.
        /// </summary>
        private const int ContentOrder = 1;

        private readonly HdbfFarmPanelFragment _fragment;

        public HdbfEntityPanelModuleProvider(HdbfFarmPanelFragment fragment)
        {
            _fragment = fragment;
        }

        public EntityPanelModule Get()
        {
            var builder = new EntityPanelModule.Builder();
            builder.AddMiddleFragment(_fragment, ContentOrder);
            return builder.Build();
        }
    }
}
