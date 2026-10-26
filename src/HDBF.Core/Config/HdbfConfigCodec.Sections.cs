// ---------------------------------------------------------------------------------------------
// HDBF v2 | Layer: Core/Config | Owner: V2-CONFIG-02
// Purpose: Section readers of the schema-2 configuration codec (features/perf/ui/schema version).
//          Split from HdbfConfigCodec.cs so every implementation file stays within the
//          300-line gate (REQ-V2-MAINT-02); the public codec API is unchanged.
// Requirements: REQ-V2-EXT-04, REQ-V2-SAFE-06
// ---------------------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using HDBF.Core.Json;

namespace HDBF.Core.Config
{
    public static partial class HdbfConfigCodec
    {
        private static FeatureFlags ReadFeatures(JsonValue? node, HdbfConfig defaults, Action<string>? warn) =>
            new FeatureFlags
            {
                DensePlanting = HdbfConfigReaders.ReadBool(node, "densePlanting",
                    defaults.Features.DensePlanting, "features.densePlanting", warn),
                Rotation = HdbfConfigReaders.ReadBool(node, "rotation", defaults.Features.Rotation,
                    "features.rotation", warn),
                Maintenance = HdbfConfigReaders.ReadBool(node, "maintenance", defaults.Features.Maintenance,
                    "features.maintenance", warn),
                PowerConnection = HdbfConfigReaders.ReadBool(node, "powerConnection",
                    defaults.Features.PowerConnection, "features.powerConnection", warn),
                PipeConnection = HdbfConfigReaders.ReadBool(node, "pipeConnection",
                    defaults.Features.PipeConnection, "features.pipeConnection", warn),
                LogisticsOutput = HdbfConfigReaders.ReadBool(node, "logisticsOutput",
                    defaults.Features.LogisticsOutput, "features.logisticsOutput", warn),
                SoilFatigue = HdbfConfigReaders.ReadBool(node, "soilFatigue",
                    defaults.Features.SoilFatigue, "features.soilFatigue", warn),
            };

        private static PerfSettings ReadPerf(JsonValue? node, HdbfConfig defaults, Action<string>? warn) =>
            new PerfSettings
            {
                UiRefreshHz = HdbfConfigReaders.ReadInt(node, "uiRefreshHz", defaults.Perf.UiRefreshHz,
                    HdbfConfigRanges.UiRefreshHzMin, HdbfConfigRanges.UiRefreshHzMax,
                    "perf.uiRefreshHz", warn),
                ReducedBuildingThreshold = HdbfConfigReaders.ReadInt(node, "reducedBuildingThreshold",
                    defaults.Perf.ReducedBuildingThreshold, HdbfConfigRanges.ReducedBuildingThresholdMin,
                    HdbfConfigRanges.ReducedBuildingThresholdMax, "perf.reducedBuildingThreshold", warn),
                SteadyFrameBudgetMs = HdbfConfigReaders.ReadFloat(node, "steadyFrameBudgetMs",
                    defaults.Perf.SteadyFrameBudgetMs, HdbfConfigRanges.SteadyFrameBudgetMsMin,
                    HdbfConfigRanges.SteadyFrameBudgetMsMax, "perf.steadyFrameBudgetMs", warn),
                WarnFrameMs = HdbfConfigReaders.ReadFloat(node, "warnFrameMs", defaults.Perf.WarnFrameMs,
                    HdbfConfigRanges.WarnFrameMsMin, HdbfConfigRanges.WarnFrameMsMax,
                    "perf.warnFrameMs", warn),
                PauseFrameMs = HdbfConfigReaders.ReadFloat(node, "pauseFrameMs", defaults.Perf.PauseFrameMs,
                    HdbfConfigRanges.PauseFrameMsMin, HdbfConfigRanges.PauseFrameMsMax,
                    "perf.pauseFrameMs", warn),
            };

        private static UiSettings ReadUi(JsonValue? node, HdbfConfig defaults, Action<string>? warn) =>
            new UiSettings
            {
                WorldIcons = HdbfConfigReaders.ReadBool(node, "worldIcons", defaults.Ui.WorldIcons,
                    "ui.worldIcons", warn),
                WorldIconBlink = HdbfConfigReaders.ReadBool(node, "worldIconBlink",
                    defaults.Ui.WorldIconBlink, "ui.worldIconBlink", warn),
                WorldIconMaxDistance = HdbfConfigReaders.ReadInt(node, "worldIconMaxDistance",
                    defaults.Ui.WorldIconMaxDistance, HdbfConfigRanges.WorldIconMaxDistanceMin,
                    HdbfConfigRanges.WorldIconMaxDistanceMax, "ui.worldIconMaxDistance", warn),
                WorldIconIntervalSeconds = HdbfConfigReaders.ReadFloat(node, "worldIconIntervalSeconds",
                    defaults.Ui.WorldIconIntervalSeconds, HdbfConfigRanges.WorldIconIntervalSecondsMin,
                    HdbfConfigRanges.WorldIconIntervalSecondsMax, "ui.worldIconIntervalSeconds", warn),
                WorldIconFarmsPerTick = HdbfConfigReaders.ReadInt(node, "worldIconFarmsPerTick",
                    defaults.Ui.WorldIconFarmsPerTick, HdbfConfigRanges.WorldIconFarmsPerTickMin,
                    HdbfConfigRanges.WorldIconFarmsPerTickMax, "ui.worldIconFarmsPerTick", warn),
            };

        private static int? ReadSchemaVersion(JsonValue root, Action<string>? warn)
        {
            if (!root.TryGetProperty("configSchemaVersion", out var node))
            {
                return null;
            }

            if (!node.TryGetDouble(out var raw) || double.IsNaN(raw) || double.IsInfinity(raw)
                || Math.Floor(raw) != raw)
            {
                warn?.Invoke("config.invalid: configSchemaVersion is not an integer; current schema used");
                return null;
            }

            var version = (int)raw;
            if (version < HdbfConfigRanges.SchemaVersionMin || version > HdbfConfigRanges.SchemaVersionMax)
            {
                warn?.Invoke("config.invalid: configSchemaVersion=" + version
                             + " is not supported; current schema vocabulary used");
                return null;
            }

            return version;
        }

        private static string Join(IReadOnlyList<string> values, int limit = 0)
        {
            var count = limit > 0 && values.Count > limit ? limit : values.Count;
            var text = string.Empty;
            for (var index = 0; index < count; index++)
            {
                if (index > 0)
                {
                    text += ", ";
                }

                text += values[index];
            }

            return count < values.Count ? text + ", ...(+" + (values.Count - count) + ")" : text;
        }

    }
}