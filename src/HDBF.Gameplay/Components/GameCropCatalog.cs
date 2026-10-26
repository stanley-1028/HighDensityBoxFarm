// ============================================================================
// HDBF v2 | Layer: Gameplay/Components | Owner: V2-GAMEPLAY-01
// Purpose: ICropCatalog implementation backed by the real vanilla FarmHouse plantables:
//          PlantableSpec.ResourceGroup == "Farmhouse", GrowableSpec.GrowthTimeInDays and
//          CuttableSpec.Yielder.Yield. FallbackCropCatalog covers missing specs.
// Requirements: REQ-V2-CORE-05, REQ-V2-EXT-01, REQ-V2-SAFE-01, REQ-V2-SAFE-06
// ============================================================================
using System;
using System.Collections.Generic;
using HDBF.Core;
using HDBF.Core.Indoor;
using HDBF.Core.Logging;
using Timberborn.BlueprintSystem;
using Timberborn.Cutting;
using Timberborn.GameFactionSystem;
using Timberborn.Growing;
using Timberborn.Planting;
using Timberborn.SingletonSystem;
using Timberborn.TemplateCollectionSystem;
using Timberborn.TemplateSystem;

namespace HDBF.Gameplay.Components
{
    /// <summary>
    /// Read-only crop catalog built from the running game's specs (section 19.5). The catalog
    /// derives the active faction's table from the actual FarmHouse-plantable crop specs and falls
    /// back to <see cref="FallbackCropCatalog"/> for missing specs or the inactive faction. The
    /// table is built lazily on first query (after the game's loadables, including the current
    /// faction, are ready), the cache is resettable (<see cref="Reset"/>) and no static mutable
    /// state is held.
    /// </summary>
    public sealed partial class GameCropCatalog : ICropCatalog, IUnloadableSingleton
    {
        private const string FarmhouseResourceGroup = "Farmhouse";

        private readonly ISpecService _specService;
        private readonly TemplateCollectionService _templates;
        private readonly FactionService _factions;
        private readonly IHdbfLog _log;

        private IReadOnlyList<CropDefinition> _folktails = FallbackCropCatalog.Default.ForFaction(HdbfFaction.Folktails);
        private IReadOnlyList<CropDefinition> _ironTeeth = FallbackCropCatalog.Default.ForFaction(HdbfFaction.IronTeeth);
        private HdbfFaction _activeFaction = HdbfFaction.Folktails;
        private bool _loaded;
        private bool _missingSpecLogged;

        /// <summary>Creates the catalog from the game spec services; all arguments are DI singletons.</summary>
        /// <param name="specService">Game blueprint/spec service.</param>
        /// <param name="templates">Active faction template collection service.</param>
        /// <param name="factions">Current faction of the running settlement.</param>
        /// <param name="log">Throttled diagnostics sink.</param>
        public GameCropCatalog(ISpecService specService, TemplateCollectionService templates,
            FactionService factions, IHdbfLog log)
        {
            _specService = specService ?? throw new ArgumentNullException(nameof(specService));
            _templates = templates ?? throw new ArgumentNullException(nameof(templates));
            _factions = factions ?? throw new ArgumentNullException(nameof(factions));
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        /// <summary>Active faction the catalog was built for (diagnostics and tests).</summary>
        public HdbfFaction ActiveFaction => _activeFaction;

        /// <inheritdoc />
        public IReadOnlyList<CropDefinition> ForFaction(HdbfFaction faction)
        {
            EnsureLoaded();
            switch (faction)
            {
                case HdbfFaction.Folktails: return _folktails;
                case HdbfFaction.IronTeeth: return _ironTeeth;
                default: return Array.Empty<CropDefinition>();
            }
        }

        /// <inheritdoc />
        public bool TryGet(HdbfFaction faction, string cropId, out CropDefinition crop)
        {
            crop = null!;
            if (string.IsNullOrEmpty(cropId))
            {
                return false;
            }

            var crops = ForFaction(faction);
            for (var index = 0; index < crops.Count; index++)
            {
                var candidate = crops[index];
                if (candidate is not null && string.Equals(candidate.Id, cropId, StringComparison.Ordinal))
                {
                    crop = candidate;
                    return true;
                }
            }

            return false;
        }

        /// <summary>Drops the cached tables when the session unloads; the next call rebuilds lazily.</summary>
        public void Unload()
        {
            Reset();
        }

        /// <summary>Drops the cached tables so the next query rebuilds from the live specs.</summary>
        public void Reset()
        {
            _folktails = FallbackCropCatalog.Default.ForFaction(HdbfFaction.Folktails);
            _ironTeeth = FallbackCropCatalog.Default.ForFaction(HdbfFaction.IronTeeth);
            _activeFaction = HdbfFaction.Folktails;
            _loaded = false;
            _missingSpecLogged = false;
        }

        /// <summary>
        /// Resolves a building faction from its blueprint or template name
        /// (e.g. <c>BoxFarm.Top.Folktails</c>). An unknown name falls back to Folktails
        /// (section 19.5 / the v2 blueprint naming contract).
        /// </summary>
        public static HdbfFaction ResolveFaction(string? blueprintOrTemplateName)
        {
            if (!string.IsNullOrEmpty(blueprintOrTemplateName)
                && blueprintOrTemplateName.IndexOf("IronTeeth", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return HdbfFaction.IronTeeth;
            }

            return HdbfFaction.Folktails;
        }

        private void EnsureLoaded()
        {
            if (_loaded)
            {
                return;
            }

            _loaded = true;
            try
            {
                Rebuild();
            }
            catch (Exception exception)
            {
                // REQ-V2-SAFE-01/06: a broken spec layer degrades to the built-in fallback table.
                _log.Error(HdbfLogCategory.Warn, IndoorLogKeys.CropSpecMissing,
                    "crop spec scan failed; using the built-in fallback crop table", exception);
                Reset();
                _loaded = true;
            }
        }

        private void Rebuild()
        {
            _activeFaction = ResolveActiveFaction();
            var activeSpecs = ReadActiveSpecs();
            _folktails = _activeFaction == HdbfFaction.Folktails
                ? BuildTable(HdbfFaction.Folktails, activeSpecs)
                : FallbackCropCatalog.Default.ForFaction(HdbfFaction.Folktails);
            _ironTeeth = _activeFaction == HdbfFaction.IronTeeth
                ? BuildTable(HdbfFaction.IronTeeth, activeSpecs)
                : FallbackCropCatalog.Default.ForFaction(HdbfFaction.IronTeeth);
        }

        private HdbfFaction ResolveActiveFaction()
        {
            try
            {
                var current = _factions.Current;
                return ResolveFaction(current?.Id);
            }
            catch (Exception)
            {
                return HdbfFaction.Folktails;
            }
        }

        private Dictionary<string, CropDefinition> ReadActiveSpecs()
        {
            var result = new Dictionary<string, CropDefinition>(StringComparer.Ordinal);
            var activeTemplates = ReadActiveTemplateNames();
            if (activeTemplates.Count == 0)
            {
                // Without the active faction's template collection we cannot tell the two crop
                // tables apart; fall back to the built-in faction tables instead of leaking the
                // other faction's crops into this building.
                LogMissingSpec(null, null);
                return result;
            }

            IEnumerable<PlantableSpec> specs;
            try
            {
                specs = _specService.GetSpecs<PlantableSpec>();
            }
            catch (Exception exception)
            {
                _log.Error(HdbfLogCategory.Warn, IndoorLogKeys.CropSpecMissing,
                    "PlantableSpec enumeration failed; fallback table used", exception);
                return result;
            }

            foreach (var spec in specs)
            {
                if (spec is null
                    || !string.Equals(spec.ResourceGroup, FarmhouseResourceGroup, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    var templateName = spec.TemplateName;
                    if (string.IsNullOrEmpty(templateName))
                    {
                        continue;
                    }

                    if (activeTemplates.Count > 0 && !activeTemplates.Contains(templateName!))
                    {
                        continue;
                    }

                    if (TryReadCrop(spec, templateName!, out var crop))
                    {
                        result[crop.Id] = crop;
                    }
                }
                catch (Exception exception)
                {
                    // A mod crop with a broken spec must not hide the vanilla catalogue.
                    LogMissingSpec(null, exception);
                }
            }

            return result;
        }
    }
}
