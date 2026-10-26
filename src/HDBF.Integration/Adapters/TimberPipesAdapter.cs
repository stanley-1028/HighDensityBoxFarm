namespace HDBF.Integration.Adapters
{
    /// <summary>
    /// Adapter for <b>Timber Pipes</b> (level A): provides the v2 water/irrigation port (chapter 8.1,
    /// table 8-3, section 19.13 "Timber Pipes 水罐补水，变为关键路径").
    ///
    /// v2 behaviour: the adapter itself only proves that the external mod and its public pipe API
    /// are present (anchors in <see cref="ThirdPartyProbes"/>). The actual daily draw is exposed to
    /// Gameplay as a Core <c>HDBF.Core.Indoor.IWaterSource</c> by
    /// <see cref="IndoorAdapterHost"/>/<see cref="TimberPipesWaterSource"/>; when the mod is missing
    /// or broken the host returns <c>null</c> and the farm falls back to manual refill
    /// (REQ-V2-COMPAT-02). v1's external irrigation sampling is gone: v2 has no soil moisture and
    /// no world plots (REQ-V2-WATER-02, section 19.6).
    ///
    /// The class binds no third-party type at compile time: every name lives in
    /// <see cref="ThirdPartyProbes"/> and is reached through
    /// <see cref="HDBF.Integration.Reflection.ExternalProxy"/> (REQ-COMPAT-05, REQ-V2-SAFE-05).
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
