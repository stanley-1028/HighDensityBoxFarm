using System;
using HDBF.Core;
using HDBF.Core.Balance;

namespace HDBF.Gameplay.Components
{
    /// <summary>
    /// Partial part: read-only growth-rate query for the one approved Harmony Postfix (ADR-009,
    /// deviation D-2).
    ///
    /// The four constraint layers are already resolved by the time a crop starts growing: the
    /// daily simulation caches <c>_coverage</c> / <c>_maintenanceMultiplier</c> /
    /// <c>_powerMultiplier</c>, and the per-plot fatigue lives in the persisted record. This part
    /// exposes the product as the *rate* the patch applies to the vanilla crop timer (1.0 = vanilla
    /// speed). It is deliberately a plain instance read: no scan, no allocation, no logging on the
    /// happy path, because the patch calls it once per newly spawned crop.
    ///
    /// This is the single point where the patch's four-layer product is computed
    /// (<see cref="BalanceFormulas.GrowthMultiplier"/>); the patch itself never re-implements the
    /// formula (card constraint: "不要在補丁裡重算公式").
    /// </summary>
    public partial class HdbfBoxFarmController
    {
        private const string LogKeyGrowthRate = "gameplay.growth-rate";

        /// <summary>
        /// Growth-rate multiplier for one registered plot, clamped to the patch-safe range
        /// [<see cref="BalanceFormulas.MinimumGrowthRate"/>, 1].
        ///
        /// Returns false when the farm is not active, the plot is not in this farm's registry, or
        /// a fault was caught; callers then leave vanilla growth time untouched. Never throws.
        /// </summary>
        public bool TryGetPlotGrowthRate(int plotId, out float growthRate)
        {
            growthRate = 1f;
            try
            {
                if (!_activated || _record is null)
                {
                    return false;
                }

                var ids = _record.PlotIds;
                var index = Array.IndexOf(ids, plotId);
                if (index < 0)
                {
                    return false;
                }

                var config = _configProvider.Config;
                var fatigue = _record.Fatigue;
                var fatigueValue = fatigue is not null && index < fatigue.Length ? fatigue[index] : 0f;

                var product = BalanceFormulas.GrowthMultiplier(
                    BalanceFormulas.IrrigationMultiplier(_coverage, _profile.MinIrrigationCoverage),
                    BalanceFormulas.FatigueMultiplier(fatigueValue, config.FatiguePenalty),
                    _maintenanceMultiplier,
                    _powerMultiplier);
                growthRate = BalanceFormulas.GrowthRateMultiplier(product);
                return true;
            }
            catch (Exception exception)
            {
                // A patch query must never fault the simulation or the patch: degrade to "not
                // managed" so the crop keeps vanilla growth time (REQ-SAFE-02, REQ-COMPAT-18).
                _log.Error(HdbfLogCategory.Warn, LogKeyGrowthRate,
                    "plot growth-rate query failed; crop keeps vanilla growth time", exception,
                    _entityId.ToString());
                growthRate = 1f;
                return false;
            }
        }
    }
}
