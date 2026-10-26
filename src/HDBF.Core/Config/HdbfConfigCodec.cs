using System;
using System.Collections.Generic;
using HDBF.Core.Balance;
using HDBF.Core.Json;

namespace HDBF.Core.Config
{
    /// <summary>
    /// Strict but forgiving loader/writer for <c>HDBF_Config.json</c> (chapter 10.3, REQ-SAFE-10).
    ///
    /// Guarantees:
    /// * <see cref="Parse"/> is a total function: any input (garbage, wrong types, oversized
    ///   numbers, null) returns a usable config and never throws;
    /// * unknown keys are ignored, missing keys keep the compiled-in default;
    /// * every out-of-range value is clamped to its chapter 18 range and reported through
    ///   <c>warn</c>, so a clamp is never silent (REQ-SAFE-13);
    /// * <see cref="ResolveTiers"/> layers overrides on <see cref="TierCatalog"/> and preserves
    ///   the tier <c>BlueprintId</c> that the persistence layer keys on (chapter 9.5).
    /// </summary>
    public static class HdbfConfigCodec
    {
        // ------------------------------------------------------------------ parse

        /// <summary>
        /// Parses player configuration. Never throws; invalid JSON yields
        /// <see cref="HdbfConfig.Default"/> (chapter 10.3).
        /// </summary>
        /// <param name="json">Raw file contents; null/empty is treated as a missing file.</param>
        /// <param name="warn">Optional diagnostics callback; receives one message per issue.</param>
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

            var balance = GetObject(root, "balance", "balance", warn);
            var featuresNode = GetObject(root, "features", "features", warn);
            var perfNode = GetObject(root, "perf", "perf", warn);
            var uiNode = GetObject(root, "ui", "ui", warn);

            var features = new FeatureFlags
            {
                DensePlanting = ReadBool(featuresNode, "densePlanting", defaults.Features.DensePlanting,
                    "features.densePlanting", warn),
                Rotation = ReadBool(featuresNode, "rotation", defaults.Features.Rotation,
                    "features.rotation", warn),
                Maintenance = ReadBool(featuresNode, "maintenance", defaults.Features.Maintenance,
                    "features.maintenance", warn),
                PowerConnection = ReadBool(featuresNode, "powerConnection", defaults.Features.PowerConnection,
                    "features.powerConnection", warn),
                PipeConnection = ReadBool(featuresNode, "pipeConnection", defaults.Features.PipeConnection,
                    "features.pipeConnection", warn),
                LogisticsOutput = ReadBool(featuresNode, "logisticsOutput", defaults.Features.LogisticsOutput,
                    "features.logisticsOutput", warn),
                SoilFatigue = ReadBool(featuresNode, "soilFatigue", defaults.Features.SoilFatigue,
                    "features.soilFatigue", warn),
                GrowthMultiplierPatch = ReadBool(featuresNode, "growthMultiplierPatch",
                    defaults.Features.GrowthMultiplierPatch,
                    "features.growthMultiplierPatch", warn),
            };

            var perf = new PerfSettings
            {
                ScanBudgetPerFrame = ReadInt(perfNode, "scanBudgetPerFrame", defaults.Perf.ScanBudgetPerFrame,
                    HdbfConfigRanges.ScanBudgetPerFrameMin, HdbfConfigRanges.ScanBudgetPerFrameMax,
                    "perf.scanBudgetPerFrame", warn),
                ScanIntervalSeconds = ReadFloat(perfNode, "scanIntervalSeconds", defaults.Perf.ScanIntervalSeconds,
                    HdbfConfigRanges.ScanIntervalSecondsMin, HdbfConfigRanges.ScanIntervalSecondsMax,
                    "perf.scanIntervalSeconds", warn),
                IrrigationSampleInterval = ReadFloat(perfNode, "irrigationSampleInterval",
                    defaults.Perf.IrrigationSampleInterval,
                    HdbfConfigRanges.IrrigationSampleIntervalMin, HdbfConfigRanges.IrrigationSampleIntervalMax,
                    "perf.irrigationSampleInterval", warn),
                UiRefreshHz = ReadInt(perfNode, "uiRefreshHz", defaults.Perf.UiRefreshHz,
                    HdbfConfigRanges.UiRefreshHzMin, HdbfConfigRanges.UiRefreshHzMax,
                    "perf.uiRefreshHz", warn),
                ReducedBuildingThreshold = ReadInt(perfNode, "reducedBuildingThreshold",
                    defaults.Perf.ReducedBuildingThreshold,
                    HdbfConfigRanges.ReducedBuildingThresholdMin, HdbfConfigRanges.ReducedBuildingThresholdMax,
                    "perf.reducedBuildingThreshold", warn),
                SteadyFrameBudgetMs = ReadFloat(perfNode, "steadyFrameBudgetMs", defaults.Perf.SteadyFrameBudgetMs,
                    HdbfConfigRanges.SteadyFrameBudgetMsMin, HdbfConfigRanges.SteadyFrameBudgetMsMax,
                    "perf.steadyFrameBudgetMs", warn),
                WarnFrameMs = ReadFloat(perfNode, "warnFrameMs", defaults.Perf.WarnFrameMs,
                    HdbfConfigRanges.WarnFrameMsMin, HdbfConfigRanges.WarnFrameMsMax,
                    "perf.warnFrameMs", warn),
                PauseFrameMs = ReadFloat(perfNode, "pauseFrameMs", defaults.Perf.PauseFrameMs,
                    HdbfConfigRanges.PauseFrameMsMin, HdbfConfigRanges.PauseFrameMsMax,
                    "perf.pauseFrameMs", warn),
                // BUILD-05: low-frequency, per-frame-sliced managed-crop visual refresh.
                CropVisibilityIntervalSeconds = ReadFloat(perfNode, "cropVisibilityIntervalSeconds",
                    defaults.Perf.CropVisibilityIntervalSeconds,
                    HdbfConfigRanges.CropVisibilityIntervalSecondsMin,
                    HdbfConfigRanges.CropVisibilityIntervalSecondsMax,
                    "perf.cropVisibilityIntervalSeconds", warn),
                CropVisibilityCellsPerFrame = ReadInt(perfNode, "cropVisibilityCellsPerFrame",
                    defaults.Perf.CropVisibilityCellsPerFrame,
                    HdbfConfigRanges.CropVisibilityCellsPerFrameMin,
                    HdbfConfigRanges.CropVisibilityCellsPerFrameMax,
                    "perf.cropVisibilityCellsPerFrame", warn),
            };

            var ui = new UiSettings
            {
                WorldIcons = ReadBool(uiNode, "worldIcons", defaults.Ui.WorldIcons,
                    "ui.worldIcons", warn),
                WorldIconBlink = ReadBool(uiNode, "worldIconBlink", defaults.Ui.WorldIconBlink,
                    "ui.worldIconBlink", warn),
                WorldIconMaxDistance = ReadInt(uiNode, "worldIconMaxDistance",
                    defaults.Ui.WorldIconMaxDistance,
                    HdbfConfigRanges.WorldIconMaxDistanceMin, HdbfConfigRanges.WorldIconMaxDistanceMax,
                    "ui.worldIconMaxDistance", warn),
                WorldIconIntervalSeconds = ReadFloat(uiNode, "worldIconIntervalSeconds",
                    defaults.Ui.WorldIconIntervalSeconds,
                    HdbfConfigRanges.WorldIconIntervalSecondsMin,
                    HdbfConfigRanges.WorldIconIntervalSecondsMax,
                    "ui.worldIconIntervalSeconds", warn),
                WorldIconFarmsPerTick = ReadInt(uiNode, "worldIconFarmsPerTick",
                    defaults.Ui.WorldIconFarmsPerTick,
                    HdbfConfigRanges.WorldIconFarmsPerTickMin, HdbfConfigRanges.WorldIconFarmsPerTickMax,
                    "ui.worldIconFarmsPerTick", warn),
                WarningAggregateTtlSeconds = ReadFloat(uiNode, "warningAggregateTtlSeconds",
                    defaults.Ui.WarningAggregateTtlSeconds,
                    HdbfConfigRanges.WarningAggregateTtlSecondsMin,
                    HdbfConfigRanges.WarningAggregateTtlSecondsMax,
                    "ui.warningAggregateTtlSeconds", warn),
                // BUILD-05: hide the crop models on managed plots (default on, see UiSettings).
                HideManagedCrops = ReadBool(uiNode, "hideManagedCrops", defaults.Ui.HideManagedCrops,
                    "ui.hideManagedCrops", warn),
            };

            return new HdbfConfig
            {
                ConfigSchemaVersion = ReadInt(root, "configSchemaVersion", defaults.ConfigSchemaVersion,
                    HdbfConfigRanges.SchemaVersionMin, HdbfConfigRanges.SchemaVersionMax,
                    "configSchemaVersion", warn),
                TierOverrides = ReadTierOverrides(balance, warn),
                Features = features,
                Perf = perf,
                Ui = ui,
                DenseYieldMultiplier = ReadFloat(balance, "denseYieldMultiplier", defaults.DenseYieldMultiplier,
                    HdbfConfigRanges.DenseYieldMin, HdbfConfigRanges.DenseYieldMax,
                    "balance.denseYieldMultiplier", warn),
                DenseWaterMultiplier = ReadFloat(balance, "denseWaterMultiplier", defaults.DenseWaterMultiplier,
                    HdbfConfigRanges.DenseWaterMin, HdbfConfigRanges.DenseWaterMax,
                    "balance.denseWaterMultiplier", warn),
                DenseFatigueMultiplier = ReadFloat(balance, "denseFatigueMultiplier",
                    defaults.DenseFatigueMultiplier,
                    HdbfConfigRanges.DenseFatigueMin, HdbfConfigRanges.DenseFatigueMax,
                    "balance.denseFatigueMultiplier", warn),
                DenseWorkMultiplier = ReadFloat(balance, "denseWorkMultiplier", defaults.DenseWorkMultiplier,
                    HdbfConfigRanges.DenseWorkMin, HdbfConfigRanges.DenseWorkMax,
                    "balance.denseWorkMultiplier", warn),
                FallowRecoveryPerDay = ReadFloat(balance, "fallowRecoveryPerDay", defaults.FallowRecoveryPerDay,
                    HdbfConfigRanges.FallowRecoveryMin, HdbfConfigRanges.FallowRecoveryMax,
                    "balance.fallowRecoveryPerDay", warn),
                FatiguePenalty = ReadFloat(balance, "fatiguePenalty", defaults.FatiguePenalty,
                    HdbfConfigRanges.FatiguePenaltyMin, HdbfConfigRanges.FatiguePenaltyMax,
                    "balance.fatiguePenalty", warn),
                RotationFactor = ReadFloat(balance, "rotationFactor", defaults.RotationFactor,
                    HdbfConfigRanges.RotationFactorMin, HdbfConfigRanges.RotationFactorMax,
                    "balance.rotationFactor", warn),
                RotationRecovery = ReadFloat(balance, "rotationRecovery", defaults.RotationRecovery,
                    HdbfConfigRanges.RotationRecoveryMin, HdbfConfigRanges.RotationRecoveryMax,
                    "balance.rotationRecovery", warn),
            };
        }

        // ------------------------------------------------------------------ serialize

        /// <summary>
        /// Writes the canonical configuration JSON. Round-trips through <see cref="Parse"/> with
        /// no warnings, and emits only non-null tier override fields so a default file stays
        /// readable (chapter 10.3).
        /// </summary>
        public static string Serialize(HdbfConfig? config)
        {
            var value = config ?? HdbfConfig.Default;
            var features = value.Features ?? new FeatureFlags();
            var perf = value.Perf ?? new PerfSettings();
            var ui = value.Ui ?? new UiSettings();
            var overrides = value.TierOverrides;

            var writer = new JsonTextWriter();
            writer.StartObject();

            writer.Property("configSchemaVersion").IntegerValue(value.ConfigSchemaVersion);

            writer.Property("features").StartObject()
                .Property("densePlanting").BooleanValue(features.DensePlanting)
                .Property("rotation").BooleanValue(features.Rotation)
                .Property("maintenance").BooleanValue(features.Maintenance)
                .Property("powerConnection").BooleanValue(features.PowerConnection)
                .Property("pipeConnection").BooleanValue(features.PipeConnection)
                .Property("logisticsOutput").BooleanValue(features.LogisticsOutput)
                .Property("soilFatigue").BooleanValue(features.SoilFatigue)
                .Property("growthMultiplierPatch").BooleanValue(features.GrowthMultiplierPatch)
                .EndObject();

            writer.Property("perf").StartObject()
                .Property("scanBudgetPerFrame").IntegerValue(perf.ScanBudgetPerFrame)
                .Property("scanIntervalSeconds").FloatValue(perf.ScanIntervalSeconds)
                .Property("irrigationSampleInterval").FloatValue(perf.IrrigationSampleInterval)
                .Property("uiRefreshHz").IntegerValue(perf.UiRefreshHz)
                .Property("reducedBuildingThreshold").IntegerValue(perf.ReducedBuildingThreshold)
                .Property("steadyFrameBudgetMs").FloatValue(perf.SteadyFrameBudgetMs)
                .Property("warnFrameMs").FloatValue(perf.WarnFrameMs)
                .Property("pauseFrameMs").FloatValue(perf.PauseFrameMs)
                // BUILD-05 managed-crop visual refresh budget (additive perf keys).
                .Property("cropVisibilityIntervalSeconds").FloatValue(perf.CropVisibilityIntervalSeconds)
                .Property("cropVisibilityCellsPerFrame").IntegerValue(perf.CropVisibilityCellsPerFrame)
                .EndObject();

            // Additive chapter-14.2/14.6 UI section (world warning icons). Kept separate from
            // perf.* so the existing keys keep their exact meaning.
            writer.Property("ui").StartObject()
                .Property("worldIcons").BooleanValue(ui.WorldIcons)
                .Property("worldIconBlink").BooleanValue(ui.WorldIconBlink)
                .Property("worldIconMaxDistance").IntegerValue(ui.WorldIconMaxDistance)
                .Property("worldIconIntervalSeconds").FloatValue(ui.WorldIconIntervalSeconds)
                .Property("worldIconFarmsPerTick").IntegerValue(ui.WorldIconFarmsPerTick)
                .Property("warningAggregateTtlSeconds").FloatValue(ui.WarningAggregateTtlSeconds)
                // BUILD-05: crop models on managed plots are hidden by default (user design).
                .Property("hideManagedCrops").BooleanValue(ui.HideManagedCrops)
                .EndObject();

            writer.Property("balance").StartObject();
            writer.Property("tier").StartArray();
            for (var i = 0; i < 4; i++)
            {
                var tierOverride = overrides is not null && i < overrides.Count ? overrides[i] : null;
                writer.StartObject();
                if (tierOverride is not null)
                {
                    WriteOverride(writer, "plotCapacity", tierOverride.PlotCapacity);
                    WriteOverride(writer, "maxWorkers", tierOverride.MaxWorkers);
                    WriteOverride(writer, "workRange", tierOverride.WorkRange);
                    WriteOverride(writer, "minIrrigationCoverage", tierOverride.MinIrrigationCoverage);
                    WriteOverride(writer, "waterPerPlotPerDay", tierOverride.WaterPerPlotPerDay);
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
        /// fields (build cost, maintenance kind, power demand) always come from the catalog.
        /// </summary>
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

                var prefix = "balance.tier[" + index.ToString(System.Globalization.CultureInfo.InvariantCulture) + "].";
                resolved[index] = profile with
                {
                    PlotCapacity = ResolveInt(tierOverride.PlotCapacity, profile.PlotCapacity,
                        HdbfConfigRanges.PlotCapacityMin, HdbfConfigRanges.PlotCapacityMax,
                        prefix + "maxPlots", warn),
                    MaxWorkers = ResolveInt(tierOverride.MaxWorkers, profile.MaxWorkers,
                        HdbfConfigRanges.MaxWorkersMin, HdbfConfigRanges.MaxWorkersMax,
                        prefix + "maxWorkers", warn),
                    WorkRange = ResolveInt(tierOverride.WorkRange, profile.WorkRange,
                        HdbfConfigRanges.WorkRangeMin, HdbfConfigRanges.WorkRangeMax,
                        prefix + "range", warn),
                    MinIrrigationCoverage = ResolveFloat(tierOverride.MinIrrigationCoverage,
                        profile.MinIrrigationCoverage,
                        HdbfConfigRanges.MinIrrigationMin, HdbfConfigRanges.MinIrrigationMax,
                        prefix + "minIrrigation", warn),
                    WaterPerPlotPerDay = ResolveFloat(tierOverride.WaterPerPlotPerDay,
                        profile.WaterPerPlotPerDay,
                        HdbfConfigRanges.WaterPerPlotPerDayMin, HdbfConfigRanges.WaterPerPlotPerDayMax,
                        prefix + "waterPerPlotDay", warn),
                    TankCapacityDays = ResolveInt(tierOverride.TankCapacityDays, profile.TankCapacityDays,
                        HdbfConfigRanges.TankCapacityDaysMin, HdbfConfigRanges.TankCapacityDaysMax,
                        prefix + "tankDays", warn),
                    SoilFatigueFactor = ResolveFloat(tierOverride.SoilFatigueFactor,
                        profile.SoilFatigueFactor,
                        HdbfConfigRanges.SoilFatigueFactorMin, HdbfConfigRanges.SoilFatigueFactorMax,
                        prefix + "fatigueRate", warn),
                    ScienceCost = ResolveInt(tierOverride.ScienceCost, profile.ScienceCost,
                        HdbfConfigRanges.ScienceCostMin, HdbfConfigRanges.ScienceCostMax,
                        prefix + "scienceCost", warn),
                };
            }

            return resolved;
        }

        // ------------------------------------------------------------------ readers

        private static JsonValue? GetObject(JsonValue parent, string key, string path, Action<string>? warn)
        {
            if (!parent.TryGetProperty(key, out var node))
            {
                return null;
            }

            if (node.Kind != JsonValueKind.Object)
            {
                warn?.Invoke("config.invalid: " + path + " is not an object; section defaults used");
                return null;
            }

            return node;
        }

        private static int ReadInt(JsonValue? parent, string key, int fallback, int min, int max,
            string path, Action<string>? warn)
        {
            if (parent is null || !parent.TryGetProperty(key, out var node))
            {
                return fallback;
            }

            if (!node.TryGetDouble(out var raw) || double.IsNaN(raw) || double.IsInfinity(raw)
                || Math.Floor(raw) != raw)
            {
                warn?.Invoke("config.invalid: " + path + " is not an integer; default "
                             + fallback.ToString(System.Globalization.CultureInfo.InvariantCulture) + " used");
                return fallback;
            }

            if (raw < min)
            {
                warn?.Invoke(ClampMessage(path, raw, min, max, min));
                return min;
            }

            if (raw > max)
            {
                warn?.Invoke(ClampMessage(path, raw, min, max, max));
                return max;
            }

            return (int)raw;
        }

        private static int? ReadNullableInt(JsonValue parent, string key, int min, int max,
            string path, Action<string>? warn)
        {
            if (!parent.TryGetProperty(key, out var node))
            {
                return null;
            }

            if (node.Kind == JsonValueKind.Null)
            {
                return null;
            }

            if (!node.TryGetDouble(out var raw) || double.IsNaN(raw) || double.IsInfinity(raw)
                || Math.Floor(raw) != raw)
            {
                warn?.Invoke("config.invalid: " + path + " is not an integer; tier default kept");
                return null;
            }

            if (raw < min)
            {
                warn?.Invoke(ClampMessage(path, raw, min, max, min));
                return min;
            }

            if (raw > max)
            {
                warn?.Invoke(ClampMessage(path, raw, min, max, max));
                return max;
            }

            return (int)raw;
        }

        private static float ReadFloat(JsonValue? parent, string key, float fallback, float min, float max,
            string path, Action<string>? warn)
        {
            if (parent is null || !parent.TryGetProperty(key, out var node))
            {
                return fallback;
            }

            if (!node.TryGetDouble(out var raw) || double.IsNaN(raw) || double.IsInfinity(raw))
            {
                warn?.Invoke("config.invalid: " + path + " is not a number; default "
                             + JsonTextWriter.FormatFloat(fallback) + " used");
                return fallback;
            }

            var numeric = (float)raw;
            if (float.IsNaN(numeric) || float.IsInfinity(numeric))
            {
                warn?.Invoke("config.invalid: " + path + " is out of float range; default "
                             + JsonTextWriter.FormatFloat(fallback) + " used");
                return fallback;
            }

            if (numeric < min)
            {
                warn?.Invoke(ClampMessage(path, raw, min, max, min));
                return min;
            }

            if (numeric > max)
            {
                warn?.Invoke(ClampMessage(path, raw, min, max, max));
                return max;
            }

            return numeric;
        }

        private static float? ReadNullableFloat(JsonValue parent, string key, float min, float max,
            string path, Action<string>? warn)
        {
            if (!parent.TryGetProperty(key, out var node) || node.Kind == JsonValueKind.Null)
            {
                return null;
            }

            if (!node.TryGetDouble(out var raw) || double.IsNaN(raw) || double.IsInfinity(raw))
            {
                warn?.Invoke("config.invalid: " + path + " is not a number; tier default kept");
                return null;
            }

            var numeric = (float)raw;
            if (float.IsNaN(numeric) || float.IsInfinity(numeric))
            {
                warn?.Invoke("config.invalid: " + path + " is out of float range; tier default kept");
                return null;
            }

            if (numeric < min)
            {
                warn?.Invoke(ClampMessage(path, raw, min, max, min));
                return min;
            }

            if (numeric > max)
            {
                warn?.Invoke(ClampMessage(path, raw, min, max, max));
                return max;
            }

            return numeric;
        }

        private static bool ReadBool(JsonValue? parent, string key, bool fallback, string path,
            Action<string>? warn)
        {
            if (parent is null || !parent.TryGetProperty(key, out var node))
            {
                return fallback;
            }

            if (!node.TryGetBoolean(out var value))
            {
                warn?.Invoke("config.invalid: " + path + " is not a boolean; default "
                             + (fallback ? "true" : "false") + " used");
                return fallback;
            }

            return value;
        }

        private static TierOverride?[] ReadTierOverrides(JsonValue? balance, Action<string>? warn)
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

            var count = Math.Min(items.Count, result.Length);
            for (var index = 0; index < count; index++)
            {
                var item = items[index];
                if (item.Kind != JsonValueKind.Object)
                {
                    warn?.Invoke("config.invalid: balance.tier[" + index + "] is not an object; tier default kept");
                    continue;
                }

                var prefix = "balance.tier[" + index + "].";
                var tierOverride = new TierOverride
                {
                    PlotCapacity = ReadNullableInt(item, "plotCapacity", HdbfConfigRanges.PlotCapacityMin,
                        HdbfConfigRanges.PlotCapacityMax, prefix + "maxPlots", warn),
                    MaxWorkers = ReadNullableInt(item, "maxWorkers", HdbfConfigRanges.MaxWorkersMin,
                        HdbfConfigRanges.MaxWorkersMax, prefix + "maxWorkers", warn),
                    WorkRange = ReadNullableInt(item, "workRange", HdbfConfigRanges.WorkRangeMin,
                        HdbfConfigRanges.WorkRangeMax, prefix + "range", warn),
                    MinIrrigationCoverage = ReadNullableFloat(item, "minIrrigationCoverage",
                        HdbfConfigRanges.MinIrrigationMin, HdbfConfigRanges.MinIrrigationMax,
                        prefix + "minIrrigation", warn),
                    WaterPerPlotPerDay = ReadNullableFloat(item, "waterPerPlotPerDay",
                        HdbfConfigRanges.WaterPerPlotPerDayMin, HdbfConfigRanges.WaterPerPlotPerDayMax,
                        prefix + "waterPerPlotDay", warn),
                    TankCapacityDays = ReadNullableInt(item, "tankCapacityDays",
                        HdbfConfigRanges.TankCapacityDaysMin, HdbfConfigRanges.TankCapacityDaysMax,
                        prefix + "tankDays", warn),
                    SoilFatigueFactor = ReadNullableFloat(item, "soilFatigueFactor",
                        HdbfConfigRanges.SoilFatigueFactorMin, HdbfConfigRanges.SoilFatigueFactorMax,
                        prefix + "fatigueRate", warn),
                    ScienceCost = ReadNullableInt(item, "scienceCost", HdbfConfigRanges.ScienceCostMin,
                        HdbfConfigRanges.ScienceCostMax, prefix + "scienceCost", warn),
                };

                // An empty object means "keep every tier default"; normalising it to null keeps
                // Serialize/Parse round trips exactly equal to HdbfConfig.Default.
                result[index] = HasAnyOverride(tierOverride) ? tierOverride : null;
            }

            return result;
        }

        // ------------------------------------------------------------------ writers + clamps

        private static bool HasAnyOverride(TierOverride tierOverride)
        {
            return tierOverride.PlotCapacity.HasValue
                   || tierOverride.MaxWorkers.HasValue
                   || tierOverride.WorkRange.HasValue
                   || tierOverride.MinIrrigationCoverage.HasValue
                   || tierOverride.WaterPerPlotPerDay.HasValue
                   || tierOverride.TankCapacityDays.HasValue
                   || tierOverride.SoilFatigueFactor.HasValue
                   || tierOverride.ScienceCost.HasValue;
        }

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

        private static int ResolveInt(int? value, int fallback, int min, int max, string path,
            Action<string>? warn)
        {
            if (!value.HasValue)
            {
                return fallback;
            }

            var raw = value.Value;
            if (raw < min)
            {
                warn?.Invoke(ClampMessage(path, raw, min, max, min));
                return min;
            }

            if (raw > max)
            {
                warn?.Invoke(ClampMessage(path, raw, min, max, max));
                return max;
            }

            return raw;
        }

        private static float ResolveFloat(float? value, float fallback, float min, float max, string path,
            Action<string>? warn)
        {
            if (!value.HasValue)
            {
                return fallback;
            }

            var raw = value.Value;
            if (float.IsNaN(raw) || float.IsInfinity(raw))
            {
                warn?.Invoke("config.invalid: " + path + " is not a finite number; default "
                             + JsonTextWriter.FormatFloat(fallback) + " used");
                return fallback;
            }

            if (raw < min)
            {
                warn?.Invoke(ClampMessage(path, raw, min, max, min));
                return min;
            }

            if (raw > max)
            {
                warn?.Invoke(ClampMessage(path, raw, min, max, max));
                return max;
            }

            return raw;
        }

        private static string ClampMessage(string path, double raw, double min, double max, double clamped)
        {
            return "config.clamp: " + path + "=" + raw.ToString("G9", System.Globalization.CultureInfo.InvariantCulture)
                   + " outside [" + min.ToString("G9", System.Globalization.CultureInfo.InvariantCulture) + ", "
                   + max.ToString("G9", System.Globalization.CultureInfo.InvariantCulture) + "]; clamped to "
                   + clamped.ToString("G9", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
