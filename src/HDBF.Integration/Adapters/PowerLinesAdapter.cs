namespace HDBF.Integration.Adapters
{
    /// <summary>
    /// Adapter for <b>Power Lines</b> (level A): provides the power port of the top tier
    /// (chapter 8.1, table 8-3). When active the top tier can run on grid power at
    /// <c>G_power = 1.0</c>; when missing or failed the port is unavailable and the tier stays on
    /// the off-grid fallback (<c>G_power = 0.6</c>, REQ-COMPAT-14). A power cut is a port fault for
    /// gameplay, not an adapter failure.
    ///
    /// Reflection only; all candidate names are in <see cref="ThirdPartyProbes"/> (REQ-COMPAT-05).
    /// </summary>
    public sealed class PowerLinesAdapter : ReflectionCompatAdapter
    {
        /// <summary>Creates the adapter; the registry attaches the shared session detector on registration.</summary>
        public PowerLinesAdapter()
            : base(ThirdPartyProbes.PowerLines)
        {
        }
    }
}
