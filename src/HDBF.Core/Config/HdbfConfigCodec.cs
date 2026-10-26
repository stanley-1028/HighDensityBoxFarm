using System;
using System.Collections.Generic;
using HDBF.Core.Balance;
using HDBF.Core.Json;

namespace HDBF.Core.Config
{
    /// <summary>
    /// Strict but forgiving loader/writer for <c>HDBF_Config.json</c> (chapter 10.3, REQ-SAFE-10,
    /// config schema 2 / REQ-V2-EXT-04).
    ///
    /// Guarantees:
    /// * <see cref="Parse"/> is a total function: any input (garbage, wrong types, oversized
    ///   numbers, null) returns a usable config and never throws;
    /// * schema 1 (and missing/unversioned) files are read and migrated to schema 2: recognized
    ///   values are preserved, the v1 plot-named tier keys are renamed, the deleted v1
    ///   scan/irrigation/crop-visibility/patch keys are dropped, and one <c>config.migrated</c>
    ///   line reports the migration;
    /// * unknown keys are ignored and reported as <c>config.ignored-key</c>;
    /// * missing keys keep the compiled-in default; every out-of-range value is clamped to its
    ///   chapter-18 range and reported through <c>warn</c>, so a clamp is never silent
    ///   (REQ-SAFE-13);
    /// * <see cref="ResolveTiers"/> layers overrides on <see cref="TierCatalog"/> and preserves
    ///   the tier <c>BlueprintId</c> the persistence layer keys on (chapter 9.5).
    /// </summary>
    public static partial class HdbfConfigCodec
    {
        private const int MaxReportedUnknownKeys = 12;

        // ------------------------------------------------------------------ parse

        /// <summary>
        /// Parses player configuration. Never throws; invalid JSON yields
        /// <see cref="HdbfConfig.Default"/> (chapter 10.3). A schema-1 file is migrated to schema 2
        /// and a schema newer than this build is read with the schema-2 vocabulary.
        /// </summary>
        /// <param name="json">Raw file contents; null/empty is treated as a missing file.</param>
        /// <param name="warn">Optional diagnostics callback; receives one message per issue.</param>
        /// <returns>Resolved configuration with <c>ConfigSchemaVersion = 2</c>.</returns>
        public static HdbfConfig Parse(string json, Action<string>? warn = null)
        {
            var defaults = HdbfConfig.Default;

            if (string.IsNullOrWhiteSpace(json))
            {
                warn?.Invoke("config.missing: empty configuration; built-in defaults used");
                return defaults;
            }

            if (!JsonParser.TryParse(json, out var root) || root is null || root.Kind != JsonValueKind.Object)
            {
                warn?.Invoke("config.invalid: configuration is not a readable JSON object; built-in defaults used");
                return defaults;
            }

            var schema = ReadSchemaVersion(root, warn);
            var migrating = schema.HasValue && schema.Value < HdbfConfig.CurrentSchemaVersion;

            var balance = HdbfConfigReaders.GetObject(root, "balance", "balance", warn);
            var featuresNode = HdbfConfigReaders.GetObject(root, "features", "features", warn);
            var perfNode = HdbfConfigReaders.GetObject(root, "perf", "perf", warn);
            var uiNode = HdbfConfigReaders.GetObject(root, "ui", "ui", warn);

            var unknownKeys = new List<string>();
            HdbfConfigKeyMap.CollectUnknownRootKeys(root, unknownKeys);
            HdbfConfigKeyMap.CollectUnknownKeys(featuresNode, HdbfConfigKeyMap.KnownFeatureKeys,
                "features.", unknownKeys);
            HdbfConfigKeyMap.CollectUnknownKeys(perfNode, HdbfConfigKeyMap.KnownPerfKeys,
                "perf.", unknownKeys);
            HdbfConfigKeyMap.CollectUnknownKeys(uiNode, HdbfConfigKeyMap.KnownUiKeys,
                "ui.", unknownKeys);
            HdbfConfigKeyMap.CollectUnknownKeys(balance, HdbfConfigKeyMap.KnownBalanceKeys,
                "balance.", unknownKeys);
            var tierOverrides = HdbfConfigKeyMap.ReadTierOverrides(balance, warn, unknownKeys);

            if (migrating)
            {
                var dropped = new List<string>();
                HdbfConfigKeyMap.CollectPresentRemovedKeys(featuresNode, perfNode, uiNode, balance, dropped);
                var renamed = new List<string>();
                HdbfConfigKeyMap.CollectPresentRenames(balance, renamed);
                warn?.Invoke("config.migrated: schema " + schema + " -> "
                             + HdbfConfig.CurrentSchemaVersion + "; dropped="
                             + (dropped.Count == 0 ? "none" : Join(dropped)) + "; renamed="
                             + (renamed.Count == 0 ? "none" : Join(renamed)));
            }

            if (unknownKeys.Count > 0)
            {
                warn?.Invoke("config.ignored-key: " + Join(unknownKeys, MaxReportedUnknownKeys));
            }

            return new HdbfConfig
            {
                ConfigSchemaVersion = HdbfConfig.CurrentSchemaVersion,
                TierOverrides = tierOverrides,
                Features = ReadFeatures(featuresNode, defaults, warn),
                Perf = ReadPerf(perfNode, defaults, warn),
                Ui = ReadUi(uiNode, defaults, warn),
                DenseYieldMultiplier = HdbfConfigReaders.ReadFloat(balance, "denseYieldMultiplier",
                    defaults.DenseYieldMultiplier, HdbfConfigRanges.DenseYieldMin,
                    HdbfConfigRanges.DenseYieldMax, "balance.denseYieldMultiplier", warn),
                DenseWaterMultiplier = HdbfConfigReaders.ReadFloat(balance, "denseWaterMultiplier",
                    defaults.DenseWaterMultiplier, HdbfConfigRanges.DenseWaterMin,
                    HdbfConfigRanges.DenseWaterMax, "balance.denseWaterMultiplier", warn),
                DenseFatigueMultiplier = HdbfConfigReaders.ReadFloat(balance, "denseFatigueMultiplier",
                    defaults.DenseFatigueMultiplier, HdbfConfigRanges.DenseFatigueMin,
                    HdbfConfigRanges.DenseFatigueMax, "balance.denseFatigueMultiplier", warn),
                DenseWorkMultiplier = HdbfConfigReaders.ReadFloat(balance, "denseWorkMultiplier",
                    defaults.DenseWorkMultiplier, HdbfConfigRanges.DenseWorkMin,
                    HdbfConfigRanges.DenseWorkMax, "balance.denseWorkMultiplier", warn),
                FallowRecoveryPerDay = HdbfConfigReaders.ReadFloat(balance, "fallowRecoveryPerDay",
                    defaults.FallowRecoveryPerDay, HdbfConfigRanges.FallowRecoveryMin,
                    HdbfConfigRanges.FallowRecoveryMax, "balance.fallowRecoveryPerDay", warn),
                FatiguePenalty = HdbfConfigReaders.ReadFloat(balance, "fatiguePenalty",
                    defaults.FatiguePenalty, HdbfConfigRanges.FatiguePenaltyMin,
                    HdbfConfigRanges.FatiguePenaltyMax, "balance.fatiguePenalty", warn),
                RotationFactor = HdbfConfigReaders.ReadFloat(balance, "rotationFactor",
                    defaults.RotationFactor, HdbfConfigRanges.RotationFactorMin,
                    HdbfConfigRanges.RotationFactorMax, "balance.rotationFactor", warn),
                RotationRecovery = HdbfConfigReaders.ReadFloat(balance, "rotationRecovery",
                    defaults.RotationRecovery, HdbfConfigRanges.RotationRecoveryMin,
                    HdbfConfigRanges.RotationRecoveryMax, "balance.rotationRecovery", warn),
            };
        }

        // ------------------------------------------------------------------ serialize

        /// <summary>
        /// Writes the canonical schema-2 configuration JSON. Round-trips through
        /// <see cref="Parse"/> with no warnings, and emits only non-null tier override fields so
        /// a default file stays readable (chapter 10.3). Deleted v1 keys are never written.
        /// </summary>
        /// <param name="config">Configuration to write; null writes <see cref="HdbfConfig.Default"/>.</param>
        /// <returns>Canonical JSON text.</returns>
        public static string Serialize(HdbfConfig? config)
        {
            var value = config ?? HdbfConfig.Default;
            var features = value.Features ?? new FeatureFlags();
            var perf = value.Perf ?? new PerfSettings();
            var ui = value.Ui ?? new UiSettings();
            var overrides = value.TierOverrides;

            var writer = new JsonTextWriter();
            writer.StartObject();
            writer.Property("configSchemaVersion").IntegerValue(HdbfConfig.CurrentSchemaVersion);

            writer.Property("features").StartObject()
                .Property("densePlanting").BooleanValue(features.DensePlanting)
                .Property("rotation").BooleanValue(features.Rotation)
                .Property("maintenance").BooleanValue(features.Maintenance)
                .Property("powerConnection").BooleanValue(features.PowerConnection)
                .Property("pipeConnection").BooleanValue(features.PipeConnection)
                .Property("logisticsOutput").BooleanValue(features.LogisticsOutput)
                .Property("soilFatigue").BooleanValue(features.SoilFatigue)
                .EndObject();

            writer.Property("perf").StartObject()
                .Property("uiRefreshHz").IntegerValue(perf.UiRefreshHz)
                .Property("reducedBuildingThreshold").IntegerValue(perf.ReducedBuildingThreshold)
                .Property("steadyFrameBudgetMs").FloatValue(perf.SteadyFrameBudgetMs)
                .Property("warnFrameMs").FloatValue(perf.WarnFrameMs)
                .Property("pauseFrameMs").FloatValue(perf.PauseFrameMs)
                .EndObject();

            writer.Property("ui").StartObject()
                .Property("worldIcons").BooleanValue(ui.WorldIcons)
                .Property("worldIconBlink").BooleanValue(ui.WorldIconBlink)
                .Property("worldIconMaxDistance").IntegerValue(ui.WorldIconMaxDistance)
                .Property("worldIconIntervalSeconds").FloatValue(ui.WorldIconIntervalSeconds)
                .Property("worldIconFarmsPerTick").IntegerValue(ui.WorldIconFarmsPerTick)
                .EndObject();

            writer.Property("balance").StartObject();
            writer.Property("tier").StartArray();
            for (var i = 0; i < 4; i++)
            {
                var tierOverride = overrides is not null && i < overrides.Count ? overrides[i] : null;
                writer.StartObject();
                if (tierOverride is not null)
                {
                    WriteOverride(writer, "slotCapacity", tierOverride.SlotCapacity);
                    WriteOverride(writer, "maxWorkers", tierOverride.MaxWorkers);
                    WriteOverride(writer, "minIrrigationCoverage", tierOverride.MinIrrigationCoverage);
                    WriteOverride(writer, "waterPerSlotPerDay", tierOverride.WaterPerSlotPerDay);
                    WriteOverride(writer, "tankCapacityDays", tierOverride.TankCapacityDays);
                    WriteOverride(writer, "soilFatigueFactor", tierOverride.SoilFatigueFactor);
                    WriteOverride(writer, "scienceCost", tierOverride.ScienceCost);
                }

                writer.EndObject();
            }

            writer.EndArray();
            writer.Property("denseYieldMultiplier").FloatValue(value.DenseYieldMultiplier);
            writer.Property("denseWaterMultiplier").FloatValue(value.DenseWaterMultiplier);
            writer.Property("denseFatigueMultiplier").FloatValue(value.DenseFatigueMultiplier);
            writer.Property("denseWorkMultiplier").FloatValue(value.DenseWorkMultiplier);
            writer.Property("fallowRecoveryPerDay").FloatValue(value.FallowRecoveryPerDay);
            writer.Property("fatiguePenalty").FloatValue(value.FatiguePenalty);
            writer.Property("rotationFactor").FloatValue(value.RotationFactor);
            writer.Property("rotationRecovery").FloatValue(value.RotationRecovery);
            writer.EndObject(); // balance

            writer.EndObject(); // root
            return writer.ToString();
        }

        // ------------------------------------------------------------------ tiers

        /// <summary>
        /// Applies <see cref="HdbfConfig.TierOverrides"/> on top of <see cref="TierCatalog"/>.
        /// Every override is clamped to its chapter 18 range (REQ-BAL-18) and every clamp is
        /// reported through <c>warn</c>; the tier <c>BlueprintId</c> and all non-overridable
        /// fields (build cost, maintenance kind, power demand, v1-only work range) always come
        /// from the catalog.
        /// </summary>
        /// <param name="config">Configuration to resolve; null uses the defaults.</param>
        /// <param name="warn">Optional diagnostics callback; receives one message per clamp.</param>
        /// <returns>Four resolved tier profiles indexed by <see cref="HdbfTier"/>.</returns>
        public static IReadOnlyList<TierProfile> ResolveTiers(HdbfConfig? config, Action<string>? warn = null)
        {
            var value = config ?? HdbfConfig.Default;
            var overrides = value.TierOverrides;
            var resolved = new TierProfile[4];

            for (var index = 0; index < resolved.Length; index++)
            {
                var tier = (HdbfTier)index;
                var profile = TierCatalog.Get(tier);
                var tierOverride = overrides is not null && index < overrides.Count ? overrides[index] : null;
                if (tierOverride is null)
                {
                    resolved[index] = profile;
                    continue;
                }

                var prefix = "balance.tier[" + index + "].";
                resolved[index] = profile with
                {
                    PlotCapacity = HdbfConfigReaders.ResolveInt(tierOverride.SlotCapacity,
                        profile.PlotCapacity, HdbfConfigRanges.SlotCapacityMin,
                        HdbfConfigRanges.SlotCapacityMax, prefix + "slotCapacity", warn),
                    MaxWorkers = HdbfConfigReaders.ResolveInt(tierOverride.MaxWorkers,
                        profile.MaxWorkers, HdbfConfigRanges.MaxWorkersMin,
                        HdbfConfigRanges.MaxWorkersMax, prefix + "maxWorkers", warn),
                    MinIrrigationCoverage = HdbfConfigReaders.ResolveFloat(tierOverride.MinIrrigationCoverage,
                        profile.MinIrrigationCoverage, HdbfConfigRanges.MinIrrigationMin,
                        HdbfConfigRanges.MinIrrigationMax, prefix + "minIrrigationCoverage", warn),
                    WaterPerPlotPerDay = HdbfConfigReaders.ResolveFloat(tierOverride.WaterPerSlotPerDay,
                        profile.WaterPerPlotPerDay, HdbfConfigRanges.WaterPerSlotPerDayMin,
                        HdbfConfigRanges.WaterPerSlotPerDayMax, prefix + "waterPerSlotPerDay", warn),
                    TankCapacityDays = HdbfConfigReaders.ResolveInt(tierOverride.TankCapacityDays,
                        profile.TankCapacityDays, HdbfConfigRanges.TankCapacityDaysMin,
                        HdbfConfigRanges.TankCapacityDaysMax, prefix + "tankCapacityDays", warn),
                    SoilFatigueFactor = HdbfConfigReaders.ResolveFloat(tierOverride.SoilFatigueFactor,
                        profile.SoilFatigueFactor, HdbfConfigRanges.SoilFatigueFactorMin,
                        HdbfConfigRanges.SoilFatigueFactorMax, prefix + "soilFatigueFactor", warn),
                    ScienceCost = HdbfConfigReaders.ResolveInt(tierOverride.ScienceCost,
                        profile.ScienceCost, HdbfConfigRanges.ScienceCostMin,
                        HdbfConfigRanges.ScienceCostMax, prefix + "scienceCost", warn),
                };
            }

            return resolved;
        }

        // ------------------------------------------------------------------ helpers

        private static void WriteOverride(JsonTextWriter writer, string name, int? value)
        {
            if (value.HasValue)
            {
                writer.Property(name).IntegerValue(value.Value);
            }
        }

        private static void WriteOverride(JsonTextWriter writer, string name, float? value)
        {
            if (value.HasValue)
            {
                writer.Property(name).FloatValue(value.Value);
            }
        }
    }
}