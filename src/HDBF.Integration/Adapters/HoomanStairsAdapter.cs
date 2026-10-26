namespace HDBF.Integration.Adapters
{
    /// <summary>
    /// Adapter for <b>Hooman Stairs</b> (level B, read-only): it exists so the farm can avoid
    /// blocking the stairs/platform mod's paths and expose an entrance warning instead of a hard
    /// failure (chapter 8.1, table 8-2). It never modifies platform or stair state and exposes no
    /// port, so detection alone is enough to report active (REQ-COMPAT-05/12).
    /// </summary>
    public sealed class HoomanStairsAdapter : ReflectionCompatAdapter
    {
        /// <summary>Creates the adapter; the registry attaches the shared session detector on registration.</summary>
        public HoomanStairsAdapter()
            : base(ThirdPartyProbes.HoomanStairs)
        {
        }
    }
}
