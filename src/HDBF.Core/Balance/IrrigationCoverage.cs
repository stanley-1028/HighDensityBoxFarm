namespace HDBF.Core.Balance
{
    /// <summary>
    /// One irrigation-coverage sample: the registered plot count, how many plots are moist, how
    /// many are fed by the internal tank, and the resulting clamped counts + coverage ratio.
    /// </summary>
    public readonly struct IrrigationCoverageSample
    {
        /// <summary>Creates an already-clamped sample (use <see cref="IrrigationCoverage.Compute"/>).</summary>
        internal IrrigationCoverageSample(int registeredPlots, int moistPlots, int tankFedPlots, int irrigatedPlots)
        {
            RegisteredPlots = registeredPlots;
            MoistPlots = moistPlots;
            TankFedPlots = tankFedPlots;
            IrrigatedPlots = irrigatedPlots;
            Coverage = BalanceFormulas.Coverage(irrigatedPlots, registeredPlots);
        }

        /// <summary>Plots currently registered by the farm (coverage denominator).</summary>
        public int RegisteredPlots { get; }

        /// <summary>Registered plots reported moist by the soil-moisture service.</summary>
        public int MoistPlots { get; }

        /// <summary>Dry registered plots the internal tank waters this day.</summary>
        public int TankFedPlots { get; }

        /// <summary>Moist + tank-fed plots, clamped to the registered count.</summary>
        public int IrrigatedPlots { get; }

        /// <summary>Coverage C = irrigated / registered, in 0..1 (REQ-IMPL-11).</summary>
        public float Coverage { get; }
    }

    /// <summary>
    /// Edge-safe irrigation coverage snapshot (chapter 6.3, REQ-IMPL-11).
    ///
    /// The daily simulation step and the "resample right after registration" path both need the
    /// same rule: an empty registry means coverage 0 with no division, and untrusted counters
    /// (a stale tank-fed count, a sampler glitch) must be clamped to the registered count instead
    /// of producing &gt;100 % or negative coverage. Extracting it keeps the two call sites
    /// identical and lets L1 tests cover the boundaries (no plots, all dry, all moist, partial,
    /// excluded plot) without a game session.
    /// </summary>
    public static class IrrigationCoverage
    {
        /// <summary>Computes the clamped sample; never divides by zero and never throws.</summary>
        public static IrrigationCoverageSample Compute(int registeredPlots, int moistPlots, int tankFedPlots)
        {
            if (registeredPlots <= 0)
            {
                return new IrrigationCoverageSample(0, 0, 0, 0);
            }

            var moist = Clamp(moistPlots, registeredPlots);
            var tankFed = Clamp(tankFedPlots, registeredPlots);
            var irrigated = moist + tankFed;
            if (irrigated > registeredPlots)
            {
                irrigated = registeredPlots;
            }

            return new IrrigationCoverageSample(registeredPlots, moist, tankFed, irrigated);
        }

        private static int Clamp(int value, int registeredPlots)
        {
            if (value < 0)
            {
                return 0;
            }

            return value > registeredPlots ? registeredPlots : value;
        }
    }
}
