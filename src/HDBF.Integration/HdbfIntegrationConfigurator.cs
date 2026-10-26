using Bindito.Core;
using HDBF.Core.Compat;
using HDBF.Integration.Adapters;

namespace HDBF.Integration
{
    /// <summary>
    /// Registers the compatibility layer in the game DI container (chapter 8.2).
    ///
    /// Only the compatibility contract is bound here: <c>ICompatRegistry</c> is what Gameplay and
    /// UI consume, and the concrete <see cref="CompatAdapterRegistry"/> is bound so diagnostics
    /// (report export, refresh) can reach it without a second singleton. <c>ToExisting</c> makes
    /// the interface resolve to the same instance as the concrete binding, so both paths observe
    /// one session cache.
    ///
    /// <see cref="HarmonyBootstrap"/> is the single register/unregister point of the one approved
    /// Harmony patch (ADR-009, REQ-COMPAT-16). It depends only on Core interfaces: the growth-rate
    /// provider is implemented by HDBF.Gameplay and bound there, so this assembly still references
    /// HDBF.Core only (REQ-COMPAT-05, INTERFACES.md assembly graph).
    /// </summary>
    [Context("Game")]
    public sealed class HdbfIntegrationConfigurator : Configurator
    {
        /// <inheritdoc />
        protected override void Configure()
        {
            Bind<CompatAdapterRegistry>().AsSingleton();
            Bind<ICompatRegistry>().ToExisting<CompatAdapterRegistry>();

            // One approved Postfix (ADR-009). Everything HarmonyLib-related is reflection inside
            // the installer: no HarmonyLib type appears in this assembly's metadata, so a missing
            // Harmony mod cannot break Bindito's assembly type scan.
            Bind<HarmonyBootstrap>().AsSingleton();
        }
    }
}
