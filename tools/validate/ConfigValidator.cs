using System.Text.Json;

namespace HDBF.Tools.Validate;

/// <summary>
/// Validates <c>HDBF_Config.json</c> against the exact shape read by
/// <c>HDBF.Core.Config.HdbfConfigCodec</c> (chapter 18 / INTERFACES.md). Keys are camelCase;
/// only the knobs the codec actually consumes are allowed. Documented chapter-18 default
/// values are checked as warnings so intentional balance tuning is not a build failure.
/// </summary>
internal sealed class ConfigValidator
{
    private static readonly HashSet<string> RootKeys =
        new(StringComparer.Ordinal) { "configSchemaVersion", "features", "perf", "ui", "balance" };

    private static readonly (string Key, bool Expected)[] FeatureKeys =
    {
        ("densePlanting", true),
        ("rotation", true),
        ("maintenance", true),
        ("powerConnection", true),
        ("pipeConnection", true),
        ("logisticsOutput", false),
        ("soilFatigue", true),
        // ADR-009: one approved Postfix; on by default (docs/ARCHITECTURE.md, HdbfConfig).
        ("growthMultiplierPatch", true),
    };

    private static readonly (string Key, bool Integer)[] PerfKeys =
    {
        ("scanBudgetPerFrame", true),
        ("scanIntervalSeconds", false),
        ("irrigationSampleInterval", false),
        ("uiRefreshHz", true),
        ("reducedBuildingThreshold", true),
        ("steadyFrameBudgetMs", false),
        ("warnFrameMs", false),
        ("pauseFrameMs", false),
        // BUILD-05: managed-crop visual refresh (low-frequency interval + per-frame cell slice).
        ("cropVisibilityIntervalSeconds", false),
        ("cropVisibilityCellsPerFrame", true),
    };

    /// <summary>
    /// Additive chapter-14.2/14.6 UI section (BUILD-04). Integer flags match
    /// <c>HdbfConfigCodec.Parse</c>; the values are not asserted here because the shipped file is
    /// allowed to tune the first-version constants (40 cells / 0.5 s / 1 Hz blink).
    /// </summary>
    private static readonly (string Key, bool Integer)[] UiKeys =
    {
        ("worldIcons", false),
        ("worldIconBlink", false),
        ("worldIconMaxDistance", true),
        ("worldIconIntervalSeconds", false),
        ("worldIconFarmsPerTick", true),
        ("warningAggregateTtlSeconds", false),
        // BUILD-05: hide the crop models on managed plots (default true, user design 2026-10-25).
        ("hideManagedCrops", false),
    };

    /// <summary>Boolean <c>ui.*</c> keys; every other <see cref="UiKeys"/> entry is numeric.</summary>
    private static readonly HashSet<string> UiBooleanKeys =
        new(StringComparer.Ordinal) { "worldIcons", "worldIconBlink", "hideManagedCrops" };

    private static readonly (string Key, double Expected)[] BalanceDefaults =
    {
        ("denseYieldMultiplier", 1.35),
        ("denseWaterMultiplier", 1.5),
        ("denseFatigueMultiplier", 1.5),
        ("denseWorkMultiplier", 1.2),
        ("fallowRecoveryPerDay", 0.03),
        ("fatiguePenalty", 0.6),
        ("rotationFactor", 0.4),
        ("rotationRecovery", 0.05),
    };

    private static readonly HashSet<string> TierOverrideKeys =
        new(StringComparer.Ordinal)
        {
            "plotCapacity", "maxWorkers", "workRange", "minIrrigationCoverage",
            "waterPerPlotPerDay", "tankCapacityDays", "soilFatigueFactor", "scienceCost",
        };

    private readonly Report _report;

    public ConfigValidator(Report report)
    {
        _report = report;
    }

    public void Validate(string dataDir)
    {
        var path = Path.Combine(dataDir, "HDBF_Config.json");
        if (!File.Exists(path))
        {
            _report.Error("config", "data/HDBF_Config.json is missing");
            return;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(File.ReadAllText(path));
        }
        catch (JsonException ex)
        {
            _report.Error("config", $"HDBF_Config.json is not valid JSON: {ex.Message}");
            return;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                _report.Error("config", "HDBF_Config.json root must be an object");
                return;
            }

            foreach (var property in root.EnumerateObject())
            {
                if (!RootKeys.Contains(property.Name))
                {
                    _report.Warn("config", $"unknown top-level key '{property.Name}' is ignored by HdbfConfigCodec");
                }
            }

            ValidateSchemaVersion(root);
            ValidateFeatures(root);
            ValidatePerf(root);
            ValidateUi(root);
            ValidateBalance(root);
        }
    }

    private void ValidateSchemaVersion(JsonElement root)
    {
        if (!root.TryGetProperty("configSchemaVersion", out var value))
        {
            _report.Error("config", "missing 'configSchemaVersion' (expected 1)");
            return;
        }

        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var version))
        {
            _report.Error("config", "'configSchemaVersion' must be an integer");
            return;
        }

        if (version != 1)
        {
            _report.Error("config", $"'configSchemaVersion' must be 1 (found {version})");
        }
    }

    private void ValidateFeatures(JsonElement root)
    {
        if (!root.TryGetProperty("features", out var features) || features.ValueKind != JsonValueKind.Object)
        {
            _report.Error("config", "missing 'features' object");
            return;
        }

        foreach (var (key, expected) in FeatureKeys)
        {
            if (!features.TryGetProperty(key, out var value))
            {
                _report.Error("config", $"missing 'features.{key}'");
                continue;
            }

            if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                _report.Error("config", $"'features.{key}' must be a boolean");
                continue;
            }

            if (value.GetBoolean() != expected)
            {
                _report.Warn("config", $"'features.{key}' is {value.GetBoolean().ToString().ToLowerInvariant()} "
                                        + $"but chapter 9.3 documents {expected.ToString().ToLowerInvariant()}");
            }
        }

        foreach (var property in features.EnumerateObject())
        {
            if (FeatureKeys.All(f => !string.Equals(f.Key, property.Name, StringComparison.Ordinal)))
            {
                _report.Warn("config", $"unknown key 'features.{property.Name}' is ignored by HdbfConfigCodec");
            }
        }
    }

    private void ValidatePerf(JsonElement root)
    {
        if (!root.TryGetProperty("perf", out var perf) || perf.ValueKind != JsonValueKind.Object)
        {
            _report.Error("config", "missing 'perf' object");
            return;
        }

        foreach (var (key, integer) in PerfKeys)
        {
            if (!perf.TryGetProperty(key, out var value))
            {
                _report.Error("config", $"missing 'perf.{key}'");
                continue;
            }

            if (value.ValueKind != JsonValueKind.Number)
            {
                _report.Error("config", $"'perf.{key}' must be a number");
                continue;
            }

            if (integer && !value.TryGetInt32(out _))
            {
                _report.Error("config", $"'perf.{key}' must be an integer");
            }
        }

        foreach (var property in perf.EnumerateObject())
        {
            if (PerfKeys.All(f => !string.Equals(f.Key, property.Name, StringComparison.Ordinal)))
            {
                _report.Warn("config", $"unknown key 'perf.{property.Name}' is ignored by HdbfConfigCodec");
            }
        }
    }

    /// <summary>
    /// Validates the additive BUILD-04 <c>ui</c> section. A missing section is an error here
    /// because the shipped file must expose the chapter-14.2/14.6 switches; the codec itself
    /// treats it as optional for backward-compatible player files.
    /// </summary>
    private void ValidateUi(JsonElement root)
    {
        if (!root.TryGetProperty("ui", out var ui) || ui.ValueKind != JsonValueKind.Object)
        {
            _report.Error("config", "missing 'ui' object (world warning icon switches, BUILD-04)");
            return;
        }

        foreach (var (key, integer) in UiKeys)
        {
            if (!ui.TryGetProperty(key, out var value))
            {
                _report.Error("config", $"missing 'ui.{key}'");
                continue;
            }

            if (UiBooleanKeys.Contains(key))
            {
                if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                {
                    _report.Error("config", $"'ui.{key}' must be a boolean");
                }

                continue;
            }

            if (value.ValueKind != JsonValueKind.Number)
            {
                _report.Error("config", $"'ui.{key}' must be a number");
                continue;
            }

            if (integer && !value.TryGetInt32(out _))
            {
                _report.Error("config", $"'ui.{key}' must be an integer");
            }
        }

        foreach (var property in ui.EnumerateObject())
        {
            if (UiKeys.All(f => !string.Equals(f.Key, property.Name, StringComparison.Ordinal)))
            {
                _report.Warn("config", $"unknown key 'ui.{property.Name}' is ignored by HdbfConfigCodec");
            }
        }
    }

    private void ValidateBalance(JsonElement root)
    {
        if (!root.TryGetProperty("balance", out var balance) || balance.ValueKind != JsonValueKind.Object)
        {
            _report.Error("config", "missing 'balance' object");
            return;
        }

        ValidateTierOverrides(balance);

        foreach (var (key, expected) in BalanceDefaults)
        {
            if (!balance.TryGetProperty(key, out var value))
            {
                _report.Error("config", $"missing 'balance.{key}'");
                continue;
            }

            if (value.ValueKind != JsonValueKind.Number)
            {
                _report.Error("config", $"'balance.{key}' must be a number");
                continue;
            }

            var actual = value.GetDouble();
            if (Math.Abs(actual - expected) > 1e-6)
            {
                _report.Warn("config", $"'balance.{key}' is {actual.ToString("G9", System.Globalization.CultureInfo.InvariantCulture)} "
                                        + $"but chapter 18 documents {expected.ToString("G9", System.Globalization.CultureInfo.InvariantCulture)}");
            }
        }

        foreach (var property in balance.EnumerateObject())
        {
            if (property.Name == "tier")
            {
                continue;
            }

            if (BalanceDefaults.All(b => !string.Equals(b.Key, property.Name, StringComparison.Ordinal)))
            {
                _report.Warn("config", $"unknown key 'balance.{property.Name}' is ignored by HdbfConfigCodec");
            }
        }
    }

    private void ValidateTierOverrides(JsonElement balance)
    {
        if (!balance.TryGetProperty("tier", out var tier))
        {
            _report.Error("config", "missing 'balance.tier' array (four entries, empty = tier defaults)");
            return;
        }

        if (tier.ValueKind != JsonValueKind.Array)
        {
            _report.Error("config", "'balance.tier' must be an array");
            return;
        }

        var count = 0;
        foreach (var entry in tier.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object)
            {
                _report.Error("config", $"'balance.tier[{count}]' must be an object");
                count++;
                continue;
            }

            foreach (var property in entry.EnumerateObject())
            {
                if (!TierOverrideKeys.Contains(property.Name))
                {
                    _report.Error("config", $"'balance.tier[{count}].{property.Name}' is not a known tier override key");
                }
            }

            if (entry.EnumerateObject().Any())
            {
                _report.Warn("config", $"'balance.tier[{count}]' overrides tier defaults; "
                                       + "the shipped file is expected to keep chapter 18 defaults (empty objects)");
            }

            count++;
        }

        if (count != 4)
        {
            _report.Error("config", $"'balance.tier' must have exactly 4 entries (found {count})");
        }
    }
}
