using System.Text.Json;
using HDBF.Core.Data;

namespace HDBF.Tools.Validate;

/// <summary>
/// L-16 hard gate over the published template-name/footprint history of the eight BoxFarm blueprints
/// (BUG_REPAIR_20261102 §4.3 L-16). Reads <c>tools/validate/boxfarm-history.json</c> (developer data,
/// never staged into the mod), then enforces via <see cref="BlueprintHistoryContract"/>:
/// (a) the shipped TemplateName is in the recorded published names;
/// (b) every historical name other than the current one is in BackwardCompatibleTemplateNames;
/// (c) the shipped BlockObjectSpec.Size equals the last published footprint, so a footprint change
///     cannot ship before it is recorded and save-migrated.
/// </summary>
internal sealed class BlueprintHistoryChecks
{
    private const string HistoryRelativePath = "tools/validate/boxfarm-history.json";
    private const string BoxFarmRelativeDir = "Buildings/Food/BoxFarm";

    private readonly Report _report;

    public BlueprintHistoryChecks(Report report)
    {
        _report = report;
    }

    /// <summary>Runs the L-16 gate for every shipped BoxFarm blueprint.</summary>
    /// <param name="dataDir">The shippable data directory.</param>
    /// <param name="modRoot">Repository/mod root that also contains tools/.</param>
    public void Validate(string dataDir, string modRoot)
    {
        var historyPath = Path.Combine(modRoot, "tools", "validate", "boxfarm-history.json");
        if (!File.Exists(historyPath))
        {
            _report.Error("history", $"{HistoryRelativePath} is missing; the L-16 template/footprint gate needs it");
            return;
        }

        var history = ParseHistory(historyPath, out var readmeProblems);
        foreach (var problem in readmeProblems)
        {
            _report.Error("history", problem);
        }

        var blueprintDir = Path.Combine(dataDir, BoxFarmRelativeDir.Replace('/', Path.DirectorySeparatorChar));
        if (!Directory.Exists(blueprintDir))
        {
            _report.Error("history", $"BoxFarm blueprint directory not found: {blueprintDir}");
            return;
        }

        var used = new HashSet<string>(StringComparer.Ordinal);
        var files = Directory.GetFiles(blueprintDir, "*.blueprint.json", SearchOption.TopDirectoryOnly);
        Array.Sort(files, StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            var relative = BoxFarmRelativeDir + "/" + Path.GetFileName(file);
            if (!history.TryGetValue(relative, out var expectation))
            {
                _report.Error("history", $"{relative} has no entry in {HistoryRelativePath}; record its TemplateName and Size history before shipping it");
                continue;
            }

            used.Add(relative);
            CheckBlueprint(file, relative, expectation);
        }

        foreach (var recorded in history)
        {
            if (!used.Contains(recorded.Key))
            {
                _report.Error("history", $"{HistoryRelativePath} records '{recorded.Key}', but that blueprint does not exist under data/{BoxFarmRelativeDir}");
            }
        }
    }

    private void CheckBlueprint(string file, string relative, BlueprintHistoryExpectation expectation)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            var root = document.RootElement;
            var templateName = ReadString(root, "TemplateSpec", "TemplateName");
            var backwardNames = ReadStringArray(root, "TemplateSpec", "BackwardCompatibleTemplateNames");
            var size = ReadSize(root);
            foreach (var problem in BlueprintHistoryContract.Evaluate(expectation, templateName, backwardNames, size))
            {
                _report.Error("history", $"{relative}: {problem}");
            }
        }
        catch (Exception exception) when (exception is IOException or JsonException or InvalidOperationException
                                              or KeyNotFoundException or FormatException)
        {
            _report.Error("history", $"{relative}: could not evaluate the template/footprint history ({exception.Message})");
        }
    }

    private Dictionary<string, BlueprintHistoryExpectation> ParseHistory(string historyPath,
        out List<string> readmeProblems)
    {
        readmeProblems = new List<string>();
        var result = new Dictionary<string, BlueprintHistoryExpectation>(StringComparer.Ordinal);
        using var document = JsonDocument.Parse(File.ReadAllText(historyPath));
        var root = document.RootElement;

        // The update procedure is part of the deliverable: a history file without it is a silent trap.
        var readme = ReadRootStringArray(root, "_readme");
        var readmeText = string.Join("\n", readme);
        if (readme.Count < 5)
        {
            readmeProblems.Add($"{HistoryRelativePath} must carry the update-procedure '_readme' block (got {readme.Count} line(s))");
        }

        foreach (var required in new[] { "BackwardCompatibleTemplateNames", "BlockObjectSpec.Size", "save-migration", "releases" })
        {
            if (readmeText.IndexOf(required, StringComparison.OrdinalIgnoreCase) < 0)
            {
                readmeProblems.Add($"{HistoryRelativePath} update procedure must mention '{required}'");
            }
        }

        if (!root.TryGetProperty("blueprints", out var blueprints) || blueprints.ValueKind != JsonValueKind.Array)
        {
            readmeProblems.Add($"{HistoryRelativePath} must have a 'blueprints' array");
            return result;
        }

        foreach (var entry in blueprints.EnumerateArray())
        {
            var path = entry.TryGetProperty("blueprint", out var pathNode) ? pathNode.GetString() : null;
            if (string.IsNullOrEmpty(path))
            {
                readmeProblems.Add($"{HistoryRelativePath} has a blueprint entry without a 'blueprint' path");
                continue;
            }

            var names = new List<string>();
            if (entry.TryGetProperty("publishedTemplateNames", out var namesNode)
                && namesNode.ValueKind == JsonValueKind.Array)
            {
                foreach (var nameNode in namesNode.EnumerateArray())
                {
                    var name = nameNode.GetString();
                    if (!string.IsNullOrEmpty(name))
                    {
                        names.Add(name!);
                    }
                }
            }

            var releases = new List<BlueprintPublishedRelease>();
            if (entry.TryGetProperty("releases", out var releasesNode)
                && releasesNode.ValueKind == JsonValueKind.Array)
            {
                var index = 0;
                foreach (var releaseNode in releasesNode.EnumerateArray())
                {
                    index++;
                    var version = releaseNode.TryGetProperty("version", out var versionNode)
                        ? versionNode.GetString()
                        : null;
                    var size = ReadReleaseSize(releaseNode, path!, index, readmeProblems);
                    var note = releaseNode.TryGetProperty("note", out var noteNode) ? noteNode.GetString() : null;
                    releases.Add(new BlueprintPublishedRelease(version ?? ("release-" + index), size, note));
                }
            }

            if (result.ContainsKey(path!))
            {
                readmeProblems.Add($"{HistoryRelativePath} lists '{path}' more than once");
                continue;
            }

            result[path!] = new BlueprintHistoryExpectation(path!, names, releases);
        }

        return result;
    }

    private BlueprintBlockSize ReadReleaseSize(JsonElement releaseNode, string path, int index,
        List<string> problems)
    {
        if (!releaseNode.TryGetProperty("size", out var sizeNode) || sizeNode.ValueKind != JsonValueKind.Array
            || sizeNode.GetArrayLength() != 3)
        {
            problems.Add($"{HistoryRelativePath} '{path}' release #{index} needs a [X, Y, Z] 'size' array");
            return new BlueprintBlockSize(-1, -1, -1);
        }

        return new BlueprintBlockSize(sizeNode[0].GetInt32(), sizeNode[1].GetInt32(), sizeNode[2].GetInt32());
    }

    private static string? ReadString(JsonElement root, string spec, string property)
    {
        if (!root.TryGetProperty(spec, out var specNode) || specNode.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return specNode.TryGetProperty(property, out var node) && node.ValueKind == JsonValueKind.String
            ? node.GetString()
            : null;
    }

    private static List<string> ReadStringArray(JsonElement root, string spec, string property)
    {
        var result = new List<string>();
        if (!root.TryGetProperty(spec, out var specNode) || specNode.ValueKind != JsonValueKind.Object)
        {
            return result;
        }

        if (!specNode.TryGetProperty(property, out var node) || node.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var item in node.EnumerateArray())
        {
            var value = item.ValueKind == JsonValueKind.String ? item.GetString() : null;
            if (!string.IsNullOrEmpty(value))
            {
                result.Add(value!);
            }
        }

        return result;
    }

    private static List<string> ReadRootStringArray(JsonElement root, string property)
    {
        var result = new List<string>();
        if (!root.TryGetProperty(property, out var node) || node.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var item in node.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                result.Add(item.GetString() ?? string.Empty);
            }
        }

        return result;
    }

    private static BlueprintBlockSize ReadSize(JsonElement root)
    {
        if (!root.TryGetProperty("BlockObjectSpec", out var specNode) || specNode.ValueKind != JsonValueKind.Object
            || !specNode.TryGetProperty("Size", out var sizeNode) || sizeNode.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("missing BlockObjectSpec.Size");
        }

        return new BlueprintBlockSize(sizeNode.GetProperty("X").GetInt32(), sizeNode.GetProperty("Y").GetInt32(),
            sizeNode.GetProperty("Z").GetInt32());
    }
}
