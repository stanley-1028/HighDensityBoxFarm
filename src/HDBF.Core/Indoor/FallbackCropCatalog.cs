// ============================================================================
// HDBF v2 | Layer: Core/Indoor | Owner: V2-CORE-01
// Purpose: Built-in fallback crop table (INTERFACES section 7) used when a vanilla
//          spec is missing, plus the "primary catalog first, fallback second" resolver.
//          It never reads game objects and never leaves the Core assembly.
// Requirements: REQ-V2-CORE-05, REQ-V2-EXT-01, REQ-V2-SAVE-06
// ============================================================================
using System;
using System.Collections.Generic;

namespace HDBF.Core.Indoor
{
    /// <summary>
    /// Built-in fallback crop table (INTERFACES section 7). The values are the v1 target values
    /// copied verbatim; a missing vanilla spec falls back here and the gameplay layer logs
    /// <c>indoor.crop-spec-missing</c> once (REQ-V2-CORE-05).
    /// </summary>
    public sealed class FallbackCropCatalog : ICropCatalog
    {
        private static readonly IReadOnlyList<CropDefinition> NoCrops = Array.Empty<CropDefinition>();

        private static readonly IReadOnlyList<CropDefinition> FolktailsCrops = Array.AsReadOnly(new[]
        {
            new CropDefinition("Carrot", 4f, "Carrot", 3),
            new CropDefinition("Potato", 6f, "Potato", 1),
            new CropDefinition("Wheat", 10f, "Wheat", 3),
            new CropDefinition("Sunflower", 5f, "SunflowerSeeds", 2),
        });

        private static readonly IReadOnlyList<CropDefinition> IronTeethCrops = Array.AsReadOnly(new[]
        {
            new CropDefinition("Canola", 9f, "CanolaSeeds", 3),
            new CropDefinition("Cassava", 5f, "Cassava", 1),
            new CropDefinition("Corn", 10f, "Corn", 2),
            new CropDefinition("Eggplant", 12f, "Eggplant", 3),
            new CropDefinition("Kohlrabi", 3f, "Kohlrabi", 2),
            new CropDefinition("Soybean", 8f, "Soybean", 2),
        });

        /// <summary>Shared immutable instance (no mutable static state; safe to hand to any caller).</summary>
        public static FallbackCropCatalog Default { get; } = new FallbackCropCatalog();

        /// <summary>Returns the faction's built-in crop table in rotation order.</summary>
        /// <param name="faction">Faction whose table is requested.</param>
        /// <returns>Cached read-only list; empty for an unknown faction.</returns>
        public IReadOnlyList<CropDefinition> ForFaction(HdbfFaction faction)
        {
            switch (faction)
            {
                case HdbfFaction.Folktails:
                    return FolktailsCrops;
                case HdbfFaction.IronTeeth:
                    return IronTeethCrops;
                default:
                    return NoCrops;
            }
        }

        /// <summary>Looks up a crop id in the faction fallback table; never throws.</summary>
        /// <param name="faction">Faction that must know the crop.</param>
        /// <param name="cropId">Crop template id.</param>
        /// <param name="crop">Resolved definition when the method returns true.</param>
        /// <returns>True when the fallback table contains the crop.</returns>
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

        /// <summary>
        /// Resolves a crop id against the game catalog first and the built-in fallback second
        /// (section 19.5: a missing spec must not stop the farm). A throwing primary catalog is
        /// treated as "spec missing" so a broken adapter cannot break the daily tick.
        /// </summary>
        /// <param name="primary">Game-backed catalog, or null when the game layer has none.</param>
        /// <param name="faction">Faction that must know the crop.</param>
        /// <param name="cropId">Crop template id; null/empty never resolves.</param>
        /// <param name="crop">Resolved definition when the method returns true.</param>
        /// <returns>True when either catalog knows the crop.</returns>
        public static bool TryResolve(ICropCatalog? primary, HdbfFaction faction, string? cropId, out CropDefinition crop)
        {
            crop = null!;
            if (string.IsNullOrEmpty(cropId))
            {
                return false;
            }

            if (primary is not null)
            {
                try
                {
                    if (primary.TryGet(faction, cropId!, out var found) && found is not null)
                    {
                        crop = found;
                        return true;
                    }
                }
                catch
                {
                    // Missing/broken spec: fall through to the built-in table (REQ-V2-CORE-05).
                }
            }

            return Default.TryGet(faction, cropId!, out crop);
        }
    }
}
