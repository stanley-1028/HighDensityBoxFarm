// ============================================================================
// HDBF v2 | Layer: Gameplay/Components | Owner: V2-GAMEPLAY-01
// Purpose: Spec-reading half of GameCropCatalog: active template names, one crop's
//          GrowableSpec/CuttableSpec extraction, fallback-ordered table assembly and the
//          one-shot indoor.crop-spec-missing report.
// Requirements: REQ-V2-CORE-05, REQ-V2-EXT-01, REQ-V2-SAFE-01/06
// ============================================================================
using System;
using System.Collections.Generic;
using HDBF.Core;
using HDBF.Core.Indoor;
using Timberborn.BlueprintSystem;
using Timberborn.Cutting;
using Timberborn.Growing;
using Timberborn.Planting;
using Timberborn.TemplateSystem;

namespace HDBF.Gameplay.Components
{
    public sealed partial class GameCropCatalog
    {
        private HashSet<string> ReadActiveTemplateNames()
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                var all = _templates.AllTemplates;
                for (var index = 0; index < all.Length; index++)
                {
                    var blueprint = all[index];
                    if (blueprint is null)
                    {
                        continue;
                    }

                    try
                    {
                        var template = blueprint.GetSpec<TemplateSpec>();
                        if (!string.IsNullOrEmpty(template.TemplateName))
                        {
                            names.Add(template.TemplateName);
                        }
                    }
                    catch (Exception)
                    {
                        // Blueprints without TemplateSpec are not templates; ignore them.
                    }
                }
            }
            catch (Exception exception)
            {
                _log.Error(HdbfLogCategory.Warn, IndoorLogKeys.CropSpecMissing,
                    "active template collection unavailable; taking every Farmhouse crop spec", exception);
            }

            return names;
        }

        private bool TryReadCrop(PlantableSpec spec, string cropId, out CropDefinition crop)
        {
            crop = null!;
            var blueprint = spec.Blueprint;
            if (blueprint is null)
            {
                LogMissingSpec(cropId, null);
                return false;
            }

            try
            {
                var growable = blueprint.GetSpec<GrowableSpec>();
                var cuttable = blueprint.GetSpec<CuttableSpec>();
                var yield = cuttable?.Yielder?.Yield;
                if (growable is null || yield is null || string.IsNullOrEmpty(yield.Id)
                    || !(growable.GrowthTimeInDays > 0f) || yield.Amount <= 0)
                {
                    LogMissingSpec(cropId, null);
                    return false;
                }

                crop = new CropDefinition(cropId, growable.GrowthTimeInDays, yield.Id, yield.Amount);
                return true;
            }
            catch (Exception exception)
            {
                LogMissingSpec(cropId, exception);
                return false;
            }
        }

        private IReadOnlyList<CropDefinition> BuildTable(HdbfFaction faction,
            IReadOnlyDictionary<string, CropDefinition> activeSpecs)
        {
            var fallback = FallbackCropCatalog.Default.ForFaction(faction);
            var table = new List<CropDefinition>(fallback.Count + 2);
            var known = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < fallback.Count; index++)
            {
                var fallbackCrop = fallback[index];
                known.Add(fallbackCrop.Id);
                if (activeSpecs.TryGetValue(fallbackCrop.Id, out var live) && live is not null)
                {
                    table.Add(live);
                }
                else
                {
                    LogMissingSpec(fallbackCrop.Id, null);
                    table.Add(fallbackCrop);
                }
            }

            foreach (var pair in activeSpecs)
            {
                if (!known.Contains(pair.Key) && pair.Value is not null)
                {
                    table.Add(pair.Value);
                }
            }

            return table.Count == 0 ? fallback : table.ToArray();
        }

        private void LogMissingSpec(string? cropId, Exception? exception)
        {
            if (_missingSpecLogged)
            {
                return;
            }

            _missingSpecLogged = true;
            var message = "crop spec missing for '" + (cropId ?? "<unknown>")
                          + "' in faction " + _activeFaction + "; built-in fallback values used";
            if (exception is null)
            {
                _log.Write(HdbfLogCategory.Warn, IndoorLogKeys.CropSpecMissing, message);
            }
            else
            {
                _log.Error(HdbfLogCategory.Warn, IndoorLogKeys.CropSpecMissing, message, exception);
            }
        }
    }
}
