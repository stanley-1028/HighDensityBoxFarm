using System;
using HDBF.Core;
using HDBF.Core.Config;
using HDBF.Core.Growth;
using HDBF.Core.Logging;
using UnityEngine;

namespace HDBF.Gameplay.Components
{
    /// <summary>
    /// Gameplay implementation of <see cref="IGrowthMultiplierSource"/>: the cell → farm lookup the
    /// one approved Harmony Postfix of ADR-009 queries when a vanilla crop starts growing.
    ///
    /// Why an indirection instead of the patch reaching into gameplay:
    /// * the patch lives in HDBF.Integration, which may only reference HDBF.Core (assembly graph of
    ///   ADR-002); it cannot name <see cref="HdbfBoxFarmController"/> and must not do per-frame
    ///   scene scans;
    /// * the lookup itself is a plain dictionary read: the plot-owner table is maintained by the
    ///   scan/registration path of each farm, and this class only translates a cell to a plot id
    ///   and forwards to that farm's controller.
    ///
    /// The provider holds no milestone state: every answer is derived from the live registry and the
    /// farm's cached daily multipliers, so a farm that is deleted or a plot that is released stops
    /// being slowed on the next query.
    /// </summary>
    public sealed class HdbfGrowthRateProvider : IGrowthMultiplierSource
    {
        private const string LogKeyLookup = "gameplay.growth-rate";

        private readonly IHdbfFarmQueryService _queries;
        private readonly IHdbfFarmRegistry _registry;
        private readonly IHdbfGameConfigProvider _configProvider;
        private readonly IHdbfLog _log;

        public HdbfGrowthRateProvider(IHdbfFarmQueryService queries, IHdbfFarmRegistry registry,
            IHdbfGameConfigProvider configProvider, IHdbfLog log)
        {
            _queries = queries ?? throw new ArgumentNullException(nameof(queries));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _configProvider = configProvider ?? throw new ArgumentNullException(nameof(configProvider));
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        /// <inheritdoc />
        public bool IsPatchEnabled
        {
            get
            {
                try
                {
                    var config = _configProvider.Config;
                    return config.Features is not null && config.Features.GrowthMultiplierPatch;
                }
                catch (Exception)
                {
                    // A broken config provider must not install the patch; defaults are handled by
                    // the registrar, this is the last-resort answer.
                    return false;
                }
            }
        }

        /// <inheritdoc />
        public bool TryGetGrowthRate(int cellX, int cellY, int cellZ, out float growthRate)
        {
            growthRate = 1f;
            try
            {
                var plotId = HdbfPlotCellCodec.Encode(new Vector3Int(cellX, cellY, cellZ));
                if (!_registry.TryGetPlotOwner(plotId, out var ownerEntityId))
                {
                    return false;
                }

                if (!_queries.TryGetSource(ownerEntityId, out var source))
                {
                    return false;
                }

                return source.TryGetPlotGrowthRate(plotId, out growthRate);
            }
            catch (Exception exception)
            {
                // The patch treats an exception as "not managed": vanilla growth time is kept for
                // this crop and the game loop never sees the fault (REQ-SAFE-02, REQ-COMPAT-18).
                _log.Error(HdbfLogCategory.Warn, LogKeyLookup,
                    "growth-rate lookup failed for a crop; vanilla growth time is kept", exception);
                growthRate = 1f;
                return false;
            }
        }
    }
}
