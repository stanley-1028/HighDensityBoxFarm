namespace HDBF.Integration.Adapters
{
    /// <summary>
    /// Adapter for <b>Conveyor Belts V2</b> (level A): provides the logistics output port for
    /// intermediate tiers and above (chapter 8.1, table 8-3). When active, produce may leave on a
    /// belt; when missing or failed the port is hidden and produce falls back to the vanilla
    /// inventory so nothing is lost (REQ-COMPAT-15).
    ///
    /// Reflection only; all candidate names are in <see cref="ThirdPartyProbes"/> (REQ-COMPAT-05).
    /// </summary>
    public sealed class ConveyorBeltsV2Adapter : ReflectionCompatAdapter
    {
        /// <summary>Creates the adapter; the registry attaches the shared session detector on registration.</summary>
        public ConveyorBeltsV2Adapter()
            : base(ThirdPartyProbes.ConveyorBeltsV2)
        {
        }
    }
}
