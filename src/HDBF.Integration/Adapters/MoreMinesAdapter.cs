namespace HDBF.Integration.Adapters
{
    /// <summary>
    /// Adapter for <b>More Mines</b> (level A, read-only): HDBF never defines or rewrites resource
    /// types, so this adapter only confirms the mod is present and exposes no port (chapter 8.1,
    /// table 8-2). Missing it costs no functionality; it therefore does not require an API anchor
    /// to report active (REQ-COMPAT-02: never patch unknown types).
    /// </summary>
    public sealed class MoreMinesAdapter : ReflectionCompatAdapter
    {
        /// <summary>Creates the adapter; the registry attaches the shared session detector on registration.</summary>
        public MoreMinesAdapter()
            : base(ThirdPartyProbes.MoreMines)
        {
        }
    }
}
