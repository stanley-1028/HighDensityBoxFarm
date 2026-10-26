// ---------------------------------------------------------------------------------------------
// HDBF v2 | Layer: Core/Config | Owner: V2-CONFIG-02
// Purpose: Schema-1 to schema-2 tier-key migration (renames, dropped keys) and the tier override
//          reader of HdbfConfigKeyMap. Split from HdbfConfigKeyMap.cs for the 300-line gate.
// Requirements: REQ-V2-EXT-04, REQ-V2-SAFE-06
// ---------------------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using HDBF.Core.Json;

namespace HDBF.Core.Config
{
    internal static partial class HdbfConfigKeyMap
    {
        /// <summary>Collects the removed v1 keys a migrating file still carries (migration log).</summary>
        public static void CollectPresentRemovedKeys(JsonValue? features, JsonValue? perf, JsonValue? ui,
            JsonValue? balance, ICollection<string> sink)
        {
            AddPresent(features, "growthMultiplierPatch", "features.growthMultiplierPatch", sink);
            AddPresent(perf, "scanBudgetPerFrame", "perf.scanBudgetPerFrame", sink);
            AddPresent(perf, "scanIntervalSeconds", "perf.scanIntervalSeconds", sink);
            AddPresent(perf, "irrigationSampleInterval", "perf.irrigationSampleInterval", sink);
            AddPresent(perf, "cropVisibilityIntervalSeconds", "perf.cropVisibilityIntervalSeconds", sink);
            AddPresent(perf, "cropVisibilityCellsPerFrame", "perf.cropVisibilityCellsPerFrame", sink);
            AddPresent(ui, "warningAggregateTtlSeconds", "ui.warningAggregateTtlSeconds", sink);
            AddPresent(ui, "hideManagedCrops", "ui.hideManagedCrops", sink);
            if (balance is null || !balance.TryGetProperty("tier", out var tierNode)
                || tierNode.Kind != JsonValueKind.Array)
            {
                return;
            }

            var items = tierNode.Items;
            for (var index = 0; index < items.Count && index < 4; index++)
            {
                var item = items[index];
                if (item.Kind != JsonValueKind.Object)
                {
                    continue;
                }

                AddPresent(item, "workRange", "balance.tier[" + index + "].workRange", sink);
            }
        }

        /// <summary>Collects the schema-1 tier keys that schema 2 renames.</summary>
        public static void CollectPresentRenames(JsonValue? balance, ICollection<string> sink)
        {
            if (balance is null || !balance.TryGetProperty("tier", out var tierNode)
                || tierNode.Kind != JsonValueKind.Array)
            {
                return;
            }

            var items = tierNode.Items;
            for (var index = 0; index < items.Count && index < 4; index++)
            {
                var item = items[index];
                if (item.Kind != JsonValueKind.Object)
                {
                    continue;
                }

                AddPresent(item, "plotCapacity", "balance.tier[" + index + "].plotCapacity", sink);
                AddPresent(item, "waterPerPlotPerDay", "balance.tier[" + index + "].waterPerPlotPerDay", sink);
            }
        }

        /// <summary>Reads the four optional tier override objects, including schema-1 key renames.</summary>
        public static TierOverride?[] ReadTierOverrides(JsonValue? balance, Action<string>? warn,
            ICollection<string> unknownKeys)
        {
            var result = new TierOverride?[4];
            if (balance is null || !balance.TryGetProperty("tier", out var tierNode))
            {
                return result;
            }

            if (tierNode.Kind != JsonValueKind.Array)
            {
                warn?.Invoke("config.invalid: balance.tier is not an array; tier defaults kept");
                return result;
            }

            var items = tierNode.Items;
            if (items.Count > result.Length)
            {
                warn?.Invoke("config.invalid: balance.tier has " + items.Count + " entries; only 4 used");
            }

            var count = items.Count < result.Length ? items.Count : result.Length;
            for (var index = 0; index < count; index++)
            {
                var item = items[index];
                if (item.Kind != JsonValueKind.Object)
                {
                    warn?.Invoke("config.invalid: balance.tier[" + index
                                 + "] is not an object; tier default kept");
                    continue;
                }

                var prefix = "balance.tier[" + index + "].";
                var names = item.PropertyNames;
                for (var nameIndex = 0; nameIndex < names.Count; nameIndex++)
                {
                    if (!IsKnownTierKey(names[nameIndex]))
                    {
                        unknownKeys.Add(prefix + names[nameIndex]);
                    }
                }

                var tierOverride = new TierOverride
                {
                    SlotCapacity = ReadTierInt(item, "slotCapacity", "plotCapacity",
                        HdbfConfigRanges.SlotCapacityMin, HdbfConfigRanges.SlotCapacityMax,
                        prefix, warn),
                    MaxWorkers = HdbfConfigReaders.ReadNullableInt(item, "maxWorkers",
                        HdbfConfigRanges.MaxWorkersMin, HdbfConfigRanges.MaxWorkersMax,
                        prefix + "maxWorkers", warn),
                    MinIrrigationCoverage = HdbfConfigReaders.ReadNullableFloat(item, "minIrrigationCoverage",
                        HdbfConfigRanges.MinIrrigationMin, HdbfConfigRanges.MinIrrigationMax,
                        prefix + "minIrrigationCoverage", warn),
                    WaterPerSlotPerDay = ReadTierFloat(item, "waterPerSlotPerDay", "waterPerPlotPerDay",
                        HdbfConfigRanges.WaterPerSlotPerDayMin, HdbfConfigRanges.WaterPerSlotPerDayMax,
                        prefix, warn),
                    TankCapacityDays = HdbfConfigReaders.ReadNullableInt(item, "tankCapacityDays",
                        HdbfConfigRanges.TankCapacityDaysMin, HdbfConfigRanges.TankCapacityDaysMax,
                        prefix + "tankCapacityDays", warn),
                    SoilFatigueFactor = HdbfConfigReaders.ReadNullableFloat(item, "soilFatigueFactor",
                        HdbfConfigRanges.SoilFatigueFactorMin, HdbfConfigRanges.SoilFatigueFactorMax,
                        prefix + "soilFatigueFactor", warn),
                    ScienceCost = HdbfConfigReaders.ReadNullableInt(item, "scienceCost",
                        HdbfConfigRanges.ScienceCostMin, HdbfConfigRanges.ScienceCostMax,
                        prefix + "scienceCost", warn),
                };

                // An empty object means "keep every tier default"; normalising it to null keeps
                // Serialize/Parse round trips exactly equal to HdbfConfig.Default.
                result[index] = HasAnyOverride(tierOverride) ? tierOverride : null;
            }

            return result;
        }

        /// <summary>True when at least one tier override field was supplied.</summary>
        public static bool HasAnyOverride(TierOverride? tierOverride) =>
            tierOverride is not null
            && (tierOverride.SlotCapacity.HasValue
                || tierOverride.MaxWorkers.HasValue
                || tierOverride.MinIrrigationCoverage.HasValue
                || tierOverride.WaterPerSlotPerDay.HasValue
                || tierOverride.TankCapacityDays.HasValue
                || tierOverride.SoilFatigueFactor.HasValue
                || tierOverride.ScienceCost.HasValue);

        private static void AddPresent(JsonValue? section, string key, string fullName,
            ICollection<string> sink)
        {
            if (section is not null && section.TryGetProperty(key, out _))
            {
                sink.Add(fullName);
            }
        }

        /// <summary>Collects every property of one section that the schema-2 codec does not know.</summary>
        /// <param name="section">Object JSON node, may be null.</param>
        /// <param name="known">Accepted key names.</param>
        /// <param name="prefix">Path prefix used in the log message (e.g. <c>features.</c>).</param>
        /// <param name="sink">Receives the full prefixed key of every unknown property.</param>
        public static void CollectUnknownKeys(JsonValue? section, IReadOnlyList<string> known, string prefix,
            ICollection<string> sink)
        {
            if (section is null || section.Kind != JsonValueKind.Object)
            {
                return;
            }

            var names = section.PropertyNames;
            for (var index = 0; index < names.Count; index++)
            {
                var name = names[index];
                var isKnown = false;
                for (var knownIndex = 0; knownIndex < known.Count; knownIndex++)
                {
                    if (string.Equals(known[knownIndex], name, StringComparison.Ordinal))
                    {
                        isKnown = true;
                        break;
                    }
                }

                if (!isKnown)
                {
                    sink.Add(prefix + name);
                }
            }
        }


        /// <summary>
        /// Reads one optional tier value preferring the schema-2 key and falling back to the legacy
        /// plot-named key of a migrating file.
        /// </summary>
        public static int? ReadTierInt(JsonValue tierObject, string key, string legacyKey, int min,
            int max, string prefix, Action<string>? warn)
        {
            return ReadTierValue(tierObject, key, legacyKey, min, max, prefix, warn,
                HdbfConfigReaders.ReadNullableInt);
        }

        /// <summary>Float counterpart of <see cref="ReadTierInt"/>.</summary>
        public static float? ReadTierFloat(JsonValue tierObject, string key, string legacyKey, float min,
            float max, string prefix, Action<string>? warn)
        {
            return ReadTierValue(tierObject, key, legacyKey, min, max, prefix, warn,
                HdbfConfigReaders.ReadNullableFloat);
        }

        private static T? ReadTierValue<T>(JsonValue tierObject, string key, string legacyKey, T min,
            T max, string prefix, Action<string>? warn,
            Func<JsonValue, string, T, T, string, Action<string>?, T?> read)
            where T : struct, IComparable<T>
        {
            if (tierObject.TryGetProperty(key, out _))
            {
                return read(tierObject, key, min, max, prefix + key, warn);
            }

            if (!string.IsNullOrEmpty(legacyKey) && tierObject.TryGetProperty(legacyKey, out _))
            {
                return read(tierObject, legacyKey, min, max, prefix + legacyKey, warn);
            }

            return null;
        }
    }
}