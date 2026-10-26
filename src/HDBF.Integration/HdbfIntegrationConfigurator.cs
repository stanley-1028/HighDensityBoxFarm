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
    /// * <c>ICompatRegistry</c> is what the v1 compatibility panel and the compatibility report
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
    /// <see cref="HarmonyBootstrap"/> is the single register/unregister point of the one approved
    /// v1 Harmony patch (ADR-009, REQ-COMPAT-16); v2 adds no patch (REQ-V2-SAFE-05). It depends only
    /// on Core interfaces: the growth-rate provider is implemented by HDBF.Gameplay and bound
    /// there, so this assembly still references HDBF.Core only (REQ-COMPAT-05,
    /// INTERFACES.md assembly graph).
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

            // One approved Postfix (ADR-009). Everything HarmonyLib-related is reflection inside
            // the installer: no HarmonyLib type appears in this assembly's metadata, so a missing
            // Harmony mod cannot break Bindito's assembly type scan. v2 ships no new patch.
            Bind<HarmonyBootstrap>().AsSingleton();
        }
    }
}
