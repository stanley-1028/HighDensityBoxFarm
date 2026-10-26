namespace HDBF.Core.Growth
{
    /// <summary>
    /// Read-only seam between the single approved Harmony Postfix (ADR-009, deviation D-2) and the
    /// live box farms. The implementation lives in HDBF.Gameplay (it owns the plot registry and the
    /// farm state), the consumer is the patch installed by HDBF.Integration.
    ///
    /// Contract:
    /// * one dictionary lookup per call, no scene scan and no frame work: the patch calls this only
    ///   when a crop starts growing (a few dozen calls per farm per day);
    /// * a cell that no live <c>HdbfBoxFarmController</c> registered returns false, which is what
    ///   keeps vanilla farmland and other mods' crops exactly on vanilla growth time
    ///   (REQ-COMPAT-16: the patch must not treat vanilla farmland);
    /// * implementations never throw; the patch treats an exception like "not managed".
    /// </summary>
    public interface IGrowthMultiplierSource
    {
        /// <summary>
        /// <c>features.growthMultiplierPatch</c>: false means the patch must not be installed at all
        /// and the mod runs the pre-patch D-2 behaviour.
        /// </summary>
        bool IsPatchEnabled { get; }

        /// <summary>
        /// Reads the growth-rate multiplier <c>G_irr × G_fat × G_maint × G_power</c> for the managed
        /// plot occupying the given cell, already mapped through
        /// <see cref="HDBF.Core.Balance.BalanceFormulas.GrowthRateMultiplier"/> so the patch can use
        /// it directly as a finite divisor. Returns false when the cell is not a managed plot.
        /// </summary>
        bool TryGetGrowthRate(int cellX, int cellY, int cellZ, out float growthRate);
    }
}
