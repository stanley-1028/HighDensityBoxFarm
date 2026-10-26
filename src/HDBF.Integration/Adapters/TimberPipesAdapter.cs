namespace HDBF.Integration.Adapters
{
    /// <summary>
    /// Adapter for <b>Timber Pipes</b> (level A): provides the water/irrigation port (chapter 8.1,
    /// table 8-3). When detected and its pipe API is found, the farm may sample irrigation coverage
    /// through the pipe network; when missing or broken the port stays unavailable and the core
    /// falls back to vanilla irrigation range plus the internal tank (REQ-COMPAT-13).
    ///
    /// The class binds no third-party type at compile time: every name lives in
    /// <see cref="ThirdPartyProbes"/> and is reached through
    /// <see cref="HDBF.Integration.Reflection.ExternalProxy"/> (REQ-COMPAT-05).
    /// </summary>
    public sealed class TimberPipesAdapter : ReflectionCompatAdapter
    {
        /// <summary>Creates the adapter; the registry attaches the shared session detector on registration.</summary>
        public TimberPipesAdapter()
            : base(ThirdPartyProbes.TimberPipes)
        {
        }
    }
}
