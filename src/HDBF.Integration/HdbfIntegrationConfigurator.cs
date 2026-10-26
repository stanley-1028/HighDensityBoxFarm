using Bindito.Core;
using HDBF.Core.Compat;
using HDBF.Core.Indoor;
using HDBF.Integration.Adapters;

namespace HDBF.Integration
{
    /// <summary>
    /// Registers the compatibility layer in the game DI container (chapter 8.2, v2 section 19.13).
    ///
    /// Two contracts are bound here:
    /// * <c>ICompatRegistry</c> is what the compatibility panel and the compatibility report
    ///   consume; the concrete <see cref="CompatAdapterRegistry"/> is bound so diagnostics (report
    ///   export, refresh) can reach it without a second singleton. <c>ToExisting</c> makes the
    ///   interface resolve to the same instance as the concrete binding, so both paths observe one
    ///   session cache.
    /// * <c>IIndoorAdapterHost</c> (Core, INTERFACES.md section 6) is the v2 water/port surface the
    ///   indoor farm consumes. It is implemented by <see cref="IndoorAdapterHost"/> in this
    ///   assembly and resolves to that same single host, so Gameplay never references a Timber
    ///   Pipes type (REQ-V2-COMPAT-01). The host is bound before Gameplay creates any farm; a
    ///   missing/broken external mod only makes it answer null/false (REQ-V2-COMPAT-02).
    ///
    /// v2 ships **zero Harmony patches** (section 19.18.3-B5): the v1 ADR-009 patch
    /// session/bootstrap was deleted with the patch sources, so this configurator has no patch
    /// registration and the compatibility report always publishes <c>harmonyPatchCount = 0</c>.
    /// </summary>
    [Context("Game")]
    public sealed class HdbfIntegrationConfigurator : Configurator
    {
        /// <inheritdoc />
        protected override void Configure()
        {
            Bind<CompatAdapterRegistry>().AsSingleton();
            Bind<ICompatRegistry>().ToExisting<CompatAdapterRegistry>();

            // v2 indoor adaptor host (pipe water + pipe/logistics port health). Bindito binds
            // IContainer in every container (ContainerCreator.ConfigureContainer IL), which the
            // host uses to look up the external mod's own registry instance at draw time.
            Bind<IndoorAdapterHost>().AsSingleton();
            Bind<IIndoorAdapterHost>().ToExisting<IndoorAdapterHost>();
        }
    }
}
