using System;
using System.Collections.Generic;
using HDBF.Core.Json;

namespace HDBF.Core.Config
{
    /// <summary>
    /// Key vocabulary and schema-1 → schema-2 migration bookkeeping of <c>HDBF_Config.json</c>
    /// (REQ-V2-EXT-04 / REQ-V2-SAFE-06).
    ///
    /// Schema 2 keeps the recognized v1 values, renames the plot-named tier keys to the v2 slot
    /// vocabulary, drops the v1 scan/irrigation/crop-visibility/patch keys and reports every key
    /// it does not know. The old names stay in the vocabulary of a migrating file so a player
    /// file never produces a false "unknown key" warning.
    /// </summary>
    internal static partial class HdbfConfigKeyMap
    {
        /// <summary>Root sections the codec reads in either schema.</summary>
        public static readonly string[] RootKeys =
        {
            "configSchemaVersion", "features", "perf", "ui", "balance",
        };

        /// <summary>Feature flags of schema 2.</summary>
        public static readonly string[] FeatureKeys =
        {
            "densePlanting", "rotation", "maintenance", "powerConnection", "pipeConnection",
            "logisticsOutput", "soilFatigue",
        };

        /// <summary>Perf keys of schema 2 (the v1 scan/irrigation/crop-visibility keys are gone).</summary>
        public static readonly string[] PerfKeys =
        {
            "uiRefreshHz", "reducedBuildingThreshold", "steadyFrameBudgetMs", "warnFrameMs",
            "pauseFrameMs",
        };

        /// <summary>UI keys of schema 2 (the v1 crop-visibility and warning-TTL keys are gone).</summary>
        public static readonly string[] UiKeys =
        {
            "worldIcons", "worldIconBlink", "worldIconMaxDistance", "worldIconIntervalSeconds",
            "worldIconFarmsPerTick",
        };

        /// <summary>Tier override keys written by schema 2.</summary>
        public static readonly string[] TierKeys =
        {
            "slotCapacity", "maxWorkers", "minIrrigationCoverage", "waterPerSlotPerDay",
            "tankCapacityDays", "soilFatigueFactor", "scienceCost",
        };

        /// <summary>Feature keys a schema-1 file may carry (one was deleted in schema 2).</summary>
        public static readonly string[] KnownFeatureKeys =
        {
            "densePlanting", "rotation", "maintenance", "powerConnection", "pipeConnection",
            "logisticsOutput", "soilFatigue", "growthMultiplierPatch",
        };

        /// <summary>Perf keys a schema-1 file may carry (scan/irrigation/visibility were deleted).</summary>
        public static readonly string[] KnownPerfKeys =
        {
            "uiRefreshHz", "reducedBuildingThreshold", "steadyFrameBudgetMs", "warnFrameMs",
            "pauseFrameMs", "scanBudgetPerFrame", "scanIntervalSeconds", "irrigationSampleInterval",
            "cropVisibilityIntervalSeconds", "cropVisibilityCellsPerFrame",
        };

        /// <summary>UI keys a schema-1 file may carry (crop visibility and warning TTL were deleted).</summary>
        public static readonly string[] KnownUiKeys =
        {
            "worldIcons", "worldIconBlink", "worldIconMaxDistance", "worldIconIntervalSeconds",
            "worldIconFarmsPerTick", "warningAggregateTtlSeconds", "hideManagedCrops",
        };

        /// <summary>Balance keys of schema 2 (tier table plus the eight multipliers).</summary>
        public static readonly string[] KnownBalanceKeys =
        {
            "tier", "denseYieldMultiplier", "denseWaterMultiplier", "denseFatigueMultiplier",
            "denseWorkMultiplier", "fallowRecoveryPerDay", "fatiguePenalty", "rotationFactor",
            "rotationRecovery",
        };

        /// <summary>Removed v1 keys that a migrating file may still carry; they are dropped silently.</summary>
        public static readonly string[] RemovedV1Keys =
        {
            "features.growthMultiplierPatch",
            "perf.scanBudgetPerFrame",
            "perf.scanIntervalSeconds",
            "perf.irrigationSampleInterval",
            "perf.cropVisibilityIntervalSeconds",
            "perf.cropVisibilityCellsPerFrame",
            "ui.warningAggregateTtlSeconds",
            "ui.hideManagedCrops",
            "balance.tier[].workRange",
        };

        /// <summary>Renames applied when a schema-1 file is migrated.</summary>
        public static readonly (string From, string To)[] TierRenames =
        {
            ("plotCapacity", "slotCapacity"),
            ("waterPerPlotPerDay", "waterPerSlotPerDay"),
        };

        /// <summary>True when the key is a schema-2 or legacy tier key the codec understands.</summary>
        public static bool IsKnownTierKey(string key)
        {
            if (string.Equals(key, "workRange", StringComparison.Ordinal))
            {
                // Deleted v2 key (the work range is gone with the plot scan): recognized so a
                // migrating file reports config.migrated instead of a bogus unknown-key warning.
                return true;
            }

            for (var index = 0; index < TierKeys.Length; index++)
            {
                if (string.Equals(TierKeys[index], key, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            for (var index = 0; index < TierRenames.Length; index++)
            {
                if (string.Equals(TierRenames[index].From, key, StringComparison.Ordinal)
                    || string.Equals(TierRenames[index].To, key, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Collects the unknown root sections (a misspelled section is ignored, never fatal).</summary>
        public static void CollectUnknownRootKeys(JsonValue root, ICollection<string> sink)
        {
            CollectUnknownKeys(root, RootKeys, string.Empty, sink);
        }

        /// <summary>True when a legacy name is present on one tier object.</summary>
        public static bool HasLegacyKey(JsonValue tierObject, string legacyKey)
        {
            for (var index = 0; index < TierRenames.Length; index++)
            {
                if (string.Equals(TierRenames[index].From, legacyKey, StringComparison.Ordinal))
                {
                    return tierObject.TryGetProperty(legacyKey, out _);
                }
            }

            return false;
        }

    }
}