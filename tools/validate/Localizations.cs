using System.Text.RegularExpressions;

namespace HDBF.Tools.Validate;

/// <summary>
/// Cross-checks the two shipped localization files against each other and against every
/// localization key referenced by the data JSON (chapter 14.4 / REQ-UI-08/09).
/// </summary>
internal sealed class Localizations
{
    private static readonly string[] RequiredLanguages = { "enUS", "zhCN" };

    private static readonly Regex UiKeyConstantPattern = new(
        "const\\s+string\\s+\\w+\\s*=\\s*\"(HDBF\\.[^\"]+)\"", RegexOptions.Compiled);

    /// <summary>
    /// The v2 keys mandated by requirement section 19.11 (indoor slot / tank / maintenance /
    /// power / crop-policy / output / command panel plus the four world-warning reasons and the
    /// concrete failure reasons of REQ-V2-UI-02). The v1 chapter-14.4 plot/highlight keys are
    /// deliberately NOT required any more: section 19.11 deletes managed plots, plot highlighting,
    /// box selection and the "registered / tillable" counters (REQ-V2-UI-04).
    /// Extras are allowed, but every key in this list must exist in enUS and zhCN.
    /// </summary>
    private static readonly string[] CanonicalKeys =
    {
        // Building names / descriptions (eight blueprints).
        "HDBF.Building.BoxFarm.Basic.Name",
        "HDBF.Building.BoxFarm.Intermediate.Name",
        "HDBF.Building.BoxFarm.Advanced.Name",
        "HDBF.Building.BoxFarm.Top.Name",
        "HDBF.Building.BoxFarm.Basic.Description",
        "HDBF.Building.BoxFarm.Intermediate.Description",
        "HDBF.Building.BoxFarm.Advanced.Description",
        "HDBF.Building.BoxFarm.Top.Description",
        // Panel: header + slots + water + maintenance + power + crop policy + output (section 19.11).
        "HDBF.Panel.Workers",
        "HDBF.Panel.Slots",
        "HDBF.Panel.EffectiveSlots",
        "HDBF.Panel.EmptySlots",
        "HDBF.Panel.GrowingSlots",
        "HDBF.Panel.MatureSlots",
        "HDBF.Panel.Backlog",
        "HDBF.Panel.Tank",
        "HDBF.Panel.Coverage",
        "HDBF.Panel.MinCoverage",
        "HDBF.Panel.WaterSupply",
        "HDBF.Panel.WaterPipe",
        "HDBF.Panel.WaterManual",
        "HDBF.Panel.WaterNone",
        "HDBF.Panel.MaintenanceDue",
        "HDBF.Panel.MaintenanceCycles",
        "HDBF.Panel.MaintenanceMaterials",
        "HDBF.Panel.Power",
        "HDBF.Panel.CropPolicy",
        "HDBF.Panel.Rotation",
        "HDBF.Panel.FixedCrop",
        "HDBF.Panel.OutputStock",
        "HDBF.Panel.OutputPort",
        "HDBF.Panel.Ports",
        "HDBF.Panel.Tier",
        "HDBF.Panel.Faction",
        "HDBF.Panel.Mode",
        // Modes and ports.
        "HDBF.Mode.Normal",
        "HDBF.Mode.Dense",
        "HDBF.Mode.Fallow",
        "HDBF.Mode.SwitchCooldown",
        "HDBF.Port.Power",
        "HDBF.Port.Pipe",
        "HDBF.Port.Logistics",
        "HDBF.Port.Connected",
        "HDBF.Port.NotConnected",
        // Commands (section 19.11 operation block).
        "HDBF.Action.Refill",
        "HDBF.Action.Maintenance",
        "HDBF.Action.ModeChanged",
        "HDBF.Action.CropPolicyChanged",
        // Warnings: only water shortage, maintenance overdue/stalled, power loss and a blocked
        // entrance are world-space icons (section 19.11); the rest are panel reasons.
        "HDBF.Warning.NoWater",
        "HDBF.Warning.IrrigationLow",
        "HDBF.Warning.MaintenanceOverdue",
        "HDBF.Warning.Shutdown",
        "HDBF.Warning.NoPower",
        "HDBF.Warning.EntranceBlocked",
        "HDBF.Warning.OutputFull",
        "HDBF.Warning.NoWorkers",
        "HDBF.Warning.MissingMaterial",
        "HDBF.Warning.InvalidCrop",
        // Concrete failure reasons (REQ-V2-UI-02).
        "HDBF.Error.NoWaterSource",
        "HDBF.Error.WaterSourceUnreachable",
        "HDBF.Error.MaterialsMissing",
        "HDBF.Error.NoWorkers",
        "HDBF.Error.EntranceBlocked",
        "HDBF.Error.OutputFull",
        "HDBF.Error.CropNotAvailable",
        "HDBF.Error.ActionFailed",
        "HDBF.Error.RefillUnavailable",
        // Compatibility degradation (REQ-V2-COMPAT-02: no "missing mod" error, only a hint).
        "HDBF.Compat.MissingMod",
        "HDBF.Compat.ShowDetails",
        // Tooltips (single-source terminology, REQ-V2-UI-03/04).
        "HDBF.Tooltip.Tank",
        "HDBF.Tooltip.Coverage",
        "HDBF.Tooltip.Maintenance",
        "HDBF.Tooltip.Refill",
        "HDBF.Tooltip.Rotation",
        "HDBF.Tooltip.EffectiveSlots",
        "HDBF.Tooltip.Output",
        "HDBF.Tooltip.Power",
        "HDBF.Tooltip.WorldIcon",
        // World-warning icon settings + aggregation (kept from v1 BUILD-04).
        "HDBF.Panel.WarningAggregate",
        "HDBF.Panel.WarningAggregateHint",
        "HDBF.Panel.SettingWorldIcons",
        "HDBF.Panel.SettingWorldIconBlink",
        "HDBF.Panel.SettingWorldIconDistance",
    };

    private static readonly HashSet<string> Categories =
        new(StringComparer.Ordinal)
        {
            "Building", "Panel", "Mode", "Port", "Action", "Warning", "Compat", "Error", "Tooltip",
        };

    private readonly Report _report;
    private readonly Dictionary<string, Dictionary<string, string>> _texts =
        new(StringComparer.OrdinalIgnoreCase);

    public Localizations(Report report)
    {
        _report = report;
    }

    public void Validate(string dataDir, IReadOnlyCollection<string> usedLocKeys, string modRoot)
    {
        var localizationDir = Path.Combine(dataDir, "Localizations");

        foreach (var language in RequiredLanguages)
        {
            var path = Path.Combine(localizationDir, language + ".csv");
            if (!File.Exists(path))
            {
                _report.Error("localization", $"missing Localizations/{language}.csv (the game's language file names are enUS/zhCN)");
                continue;
            }

            LoadLanguage(language, path);
        }

        if (_texts.Count < 2)
        {
            return;
        }

        var english = _texts["enUS"];
        var chinese = _texts["zhCN"];

        foreach (var key in english.Keys)
        {
            if (!chinese.ContainsKey(key))
            {
                _report.Error("localization", $"key '{key}' exists in enUS.csv but not in zhCN.csv");
            }
        }

        foreach (var key in chinese.Keys)
        {
            if (!english.ContainsKey(key))
            {
                _report.Error("localization", $"key '{key}' exists in zhCN.csv but not in enUS.csv");
            }
        }

        foreach (var key in CanonicalKeys)
        {
            if (!english.ContainsKey(key))
            {
                _report.Error("localization", $"v2 panel key '{key}' is missing from enUS.csv");
            }

            if (!chinese.ContainsKey(key))
            {
                _report.Error("localization", $"v2 panel key '{key}' is missing from zhCN.csv");
            }
        }

        foreach (var key in usedLocKeys)
        {
            if (!english.ContainsKey(key))
            {
                _report.Error("localization", $"blueprint/data references '{key}' but enUS.csv has no such key");
            }

            if (!chinese.ContainsKey(key))
            {
                _report.Error("localization", $"blueprint/data references '{key}' but zhCN.csv has no such key");
            }
        }

        foreach (var key in english.Keys.Intersect(chinese.Keys, StringComparer.Ordinal))
        {
            ValidateKeyShape(key);

            var en = english[key];
            var zh = chinese[key];

            if (en.Length == 0)
            {
                _report.Error("localization", $"'{key}' has an empty English text");
            }

            if (zh.Length == 0)
            {
                _report.Error("localization", $"'{key}' has an empty Chinese text");
            }

            var enPlaceholders = Placeholders(en);
            var zhPlaceholders = Placeholders(zh);
            if (!enPlaceholders.SequenceEqual(zhPlaceholders))
            {
                _report.Error("localization",
                    $"'{key}' placeholder mismatch: enUS has {{{string.Join(",", enPlaceholders)}}} "
                    + $"but zhCN has {{{string.Join(",", zhPlaceholders)}}}");
            }
        }

        ValidateUiKeyConstants(modRoot);
    }

    /// <summary>
    /// HDBF.UI uses compile-time constants for every key it formats. Those constants must exist
    /// in both CSVs, otherwise a typo in C# would only surface in game as a raw key.
    /// </summary>
    private void ValidateUiKeyConstants(string modRoot)
    {
        var uiDirectory = Path.Combine(modRoot, "src", "HDBF.UI");
        if (!Directory.Exists(uiDirectory))
        {
            _report.Warn("localization", "src/HDBF.UI not found; compile-time UI key constants were not cross-checked");
            return;
        }

        var keys = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.GetFiles(uiDirectory, "*.cs", SearchOption.AllDirectories))
        {
            foreach (Match match in UiKeyConstantPattern.Matches(File.ReadAllText(file)))
            {
                keys.Add(match.Groups[1].Value);
            }
        }

        if (keys.Count == 0)
        {
            _report.Error("localization", "no HDBF.* localization key constants found under src/HDBF.UI");
            return;
        }

        var english = _texts.TryGetValue("enUS", out var en) ? en : null;
        var chinese = _texts.TryGetValue("zhCN", out var zh) ? zh : null;
        if (english is null || chinese is null)
        {
            return;
        }

        foreach (var key in keys)
        {
            if (!english.ContainsKey(key))
            {
                _report.Error("localization", $"src/HDBF.UI key constant '{key}' is missing from enUS.csv");
            }

            if (!chinese.ContainsKey(key))
            {
                _report.Error("localization", $"src/HDBF.UI key constant '{key}' is missing from zhCN.csv");
            }
        }
    }

    private void LoadLanguage(string language, string path)
    {
        var rows = Csv.Parse(File.ReadAllText(path));
        if (rows.Count == 0)
        {
            _report.Error("localization", $"Localizations/{language}.csv is empty");
            return;
        }

        var header = rows[0];
        if (header.Length < 3
            || header[0].Trim() != "ID"
            || header[1].Trim() != "Text"
            || header[2].Trim() != "Comment")
        {
            _report.Error("localization",
                $"Localizations/{language}.csv header must be 'ID,Text,Comment' (found '{string.Join(",", header)}')");
        }

        var texts = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 1; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row.Length == 0 || (row.Length == 1 && row[0].Trim().Length == 0))
            {
                continue;
            }

            if (row.Length < 2)
            {
                _report.Error("localization", $"Localizations/{language}.csv line {i + 1} has no text column");
                continue;
            }

            // The game's LocalizationCsvValidator rejects rows whose field count is not exactly 3
            // with "Unnecessary comma found" and then fails the whole localization load, which
            // crashes the world load. Catch that here: any comma inside Text MUST be quoted.
            if (row.Length != 3)
            {
                _report.Error("localization",
                    $"Localizations/{language}.csv line {i + 1} has {row.Length} CSV fields; the game requires exactly 3 (ID,Text,Comment). Quote any Text value that contains a comma.");
                continue;
            }

            var id = row[0].Trim();
            if (id.Length == 0)
            {
                _report.Error("localization", $"Localizations/{language}.csv line {i + 1} has an empty ID");
                continue;
            }

            if (!texts.TryAdd(id, row[1]))
            {
                _report.Error("localization", $"duplicate key '{id}' in Localizations/{language}.csv line {i + 1}");
            }
        }

        _texts[language] = texts;
    }

    private void ValidateKeyShape(string key)
    {
        if (!key.StartsWith("HDBF.", StringComparison.Ordinal))
        {
            _report.Error("localization", $"key '{key}' must start with the 'HDBF.' prefix");
            return;
        }

        var parts = key.Split('.');
        if (parts.Length < 3)
        {
            _report.Error("localization", $"key '{key}' must be HDBF.<Category>.<SemanticName>");
            return;
        }

        if (!Categories.Contains(parts[1]))
        {
            _report.Error("localization",
                $"key '{key}' uses unknown category '{parts[1]}' "
                + "(allowed: Building, Panel, Mode, Port, Action, Warning, Compat, Error, Tooltip)");
        }

        for (var i = 2; i < parts.Length; i++)
        {
            var segment = parts[i];
            if (segment.Length == 0)
            {
                _report.Error("localization", $"key '{key}' contains an empty segment");
                return;
            }

            foreach (var ch in segment)
            {
                if (!char.IsAsciiLetterOrDigit(ch))
                {
                    _report.Error("localization", $"key '{key}' must use ASCII PascalCase segments (offending '{segment}')");
                    return;
                }
            }
        }
    }

    private static List<int> Placeholders(string text)
    {
        var result = new List<int>();
        var index = 0;
        while (index < text.Length)
        {
            var start = text.IndexOf('{', index);
            if (start < 0)
            {
                break;
            }

            var end = text.IndexOf('}', start + 1);
            if (end < 0)
            {
                break;
            }

            var token = text.Substring(start + 1, end - start - 1);
            if (int.TryParse(token, out var number))
            {
                result.Add(number);
            }

            index = end + 1;
        }

        result.Sort();
        return result;
    }
}
