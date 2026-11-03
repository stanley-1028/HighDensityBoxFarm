// ============================================================================
// HDBF v2 | Layer: L1 test | Owner: V2-CONFIG-02 (L-16)
// Purpose: L-16 blueprint history contract over the REAL shipped data: every BoxFarm blueprint
//          must (a) use a TemplateName recorded as published, (b) list every historical old
//          TemplateName in BackwardCompatibleTemplateNames, and (c) keep a BlockObjectSpec.Size
//          equal to the last published footprint in tools/validate/boxfarm-history.json. The same
//          pure rules (HDBF.Core.Data.BlueprintHistoryContract) are enforced by tools/validate, and
//          the negative controls below prove the gate is not vacuous.
// Requirements: REQ-V2-BLD-01, REQ-V2-SAVE-06, REQ-V2-EXT-04, L-16
// ============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using HDBF.Core.Data;

namespace HDBF.Tests
{
    public static class V2BlueprintHistoryContractTests
    {
        private const string RepoRootKey = "HdbfRepoRoot";
        private const string BoxFarmRelativeDir = "Buildings/Food/BoxFarm";
        private const string HistoryRelativePath = "tools/validate/boxfarm-history.json";

        // ---------------------------------------------------------------- shipped data

        public static void TestShippedBlueprintsPassTheL16HistoryContract()
        {
            var root = RepoRoot();
            var history = ReadHistory(root);
            var blueprints = ReadBlueprints(root);
            TestAssert.Equal(8, blueprints.Count, "the eight BoxFarm blueprints must all be readable");
            TestAssert.Equal(8, history.Count, "the history file must cover the eight blueprints");

            foreach (var blueprint in blueprints)
            {
                if (!history.TryGetValue(blueprint.RelativePath, out var expectation))
                {
                    throw new TestFailureException(blueprint.RelativePath
                                                   + " has no entry in " + HistoryRelativePath);
                }

                var problems = BlueprintHistoryContract.Evaluate(expectation, blueprint.TemplateName,
                    blueprint.BackwardNames, blueprint.Size);
                TestAssert.Equal(0, problems.Count,
                    blueprint.RelativePath + ": " + string.Join(" | ", problems));

                // The release entries are the single source of the current footprint; pin it explicitly
                // so a missing last entry cannot slip through a silently empty list.
                TestAssert.True(expectation.Releases.Count > 0);
                TestAssert.True(expectation.Releases[expectation.Releases.Count - 1].Size.Equals(blueprint.Size),
                    blueprint.RelativePath + ": current size must be the last published footprint");
                foreach (var name in blueprint.BackwardNames)
                {
                    TestAssert.True(expectation.PublishedTemplateNames.Contains(name),
                        blueprint.RelativePath + ": invented backward name '" + name
                        + "' is not a recorded historical TemplateName");
                }
            }
        }

        public static void TestHistoryFileDocumentsTheUpdateProcedureAndCoversNoStaleEntries()
        {
            var root = RepoRoot();
            var historyPath = Path.Combine(root, HistoryRelativePath.Replace('/', Path.DirectorySeparatorChar));
            TestAssert.True(File.Exists(historyPath), "missing " + HistoryRelativePath);
            using var document = JsonDocument.Parse(File.ReadAllText(historyPath));
            var readme = document.RootElement.GetProperty("_readme").EnumerateArray()
                .Select(item => item.GetString() ?? string.Empty).ToList();
            var readmeText = string.Join("\n", readme);
            TestAssert.True(readme.Count >= 5, "the update procedure must be documented in _readme");
            foreach (var required in new[]
                     {
                         "BackwardCompatibleTemplateNames", "BlockObjectSpec.Size", "save-migration",
                         "releases", "UPDATE PROCEDURE",
                     })
            {
                TestAssert.True(readmeText.Contains(required, StringComparison.OrdinalIgnoreCase),
                    "the history update procedure must document " + required);
            }

            var history = ReadHistory(root);
            foreach (var relative in history.Keys)
            {
                var path = Path.Combine(root, "data",
                    relative.Replace('/', Path.DirectorySeparatorChar));
                TestAssert.True(File.Exists(path), "stale history entry (blueprint not found): " + relative);
            }
        }

        public static void TestValidatorRunsTheSameHistoryGate()
        {
            var root = RepoRoot();
            var validator = Read(root, "tools/validate/BlueprintHistoryChecks.cs");
            TestAssert.True(validator.Contains("boxfarm-history.json", StringComparison.Ordinal),
                "tools/validate must read the recorded blueprint history");
            TestAssert.True(validator.Contains("BlueprintHistoryContract.Evaluate", StringComparison.Ordinal),
                "tools/validate must run the shared Core history contract (not a copy)");
            TestAssert.True(validator.Contains("no entry in", StringComparison.Ordinal),
                "tools/validate must reject a blueprint without a recorded history entry");
            var program = Read(root, "tools/validate/Program.cs");
            TestAssert.True(program.Contains("new BlueprintHistoryChecks(report).Validate(dataDir, modRoot)",
                StringComparison.Ordinal), "the validator entry point must run the L-16 gate");
        }

        // ---------------------------------------------------------------- negative controls

        public static void TestHistoryContractRejectsEachViolation()
        {
            var size = new BlueprintBlockSize(3, 3, 1);
            var good = new BlueprintHistoryExpectation("Buildings/Food/BoxFarm/BoxFarm.Basic.Folktails.blueprint.json",
                new[] { "BoxFarm.Basic.Folktails" },
                new[]
                {
                    new BlueprintPublishedRelease("0.1.0", new BlueprintBlockSize(3, 1, 2)),
                    new BlueprintPublishedRelease("2.0.0", size),
                });
            TestAssert.Equal(0,
                BlueprintHistoryContract.Evaluate(good, "BoxFarm.Basic.Folktails", Array.Empty<string>(), size)
                    .Count,
                "a recorded current name/size must pass");

            // (a) the current TemplateName is not recorded -> old save ids would not resolve.
            var renamed = BlueprintHistoryContract.Evaluate(good, "BoxFarm.Basic.Folktails.Renamed",
                Array.Empty<string>(), size);
            TestAssert.True(renamed.Any(problem => problem.Contains("not in the recorded published names",
                StringComparison.Ordinal)), "an unrecorded TemplateName must fail gate (a)");

            // (b) a historical name that is missing from the backward list.
            var oldNameMissing = BlueprintHistoryContract.Evaluate(good, "BoxFarm.Basic.Folktails",
                Array.Empty<string>(), size);
            var historical = new BlueprintHistoryExpectation(good.BlueprintPath,
                new[] { "BoxFarm.Basic.Old", "BoxFarm.Basic.Folktails" }, good.Releases);
            var oldNameProblem = BlueprintHistoryContract.Evaluate(historical, "BoxFarm.Basic.Folktails",
                Array.Empty<string>(), size);
            TestAssert.Equal(0, oldNameMissing.Count, "no historical old name in the shipped history yet");
            TestAssert.True(oldNameProblem.Any(problem =>
                    problem.Contains("BackwardCompatibleTemplateNames", StringComparison.Ordinal)),
                "a historical name missing from the backward list must fail gate (b)");
            TestAssert.Equal(0,
                BlueprintHistoryContract.Evaluate(historical, "BoxFarm.Basic.Folktails",
                    new[] { "BoxFarm.Basic.Old" }, size).Count,
                "listing the historical name makes gate (b) pass");

            // (c) a footprint that differs from the last published one -> migration review required.
            var footprintChange = BlueprintHistoryContract.Evaluate(good, "BoxFarm.Basic.Folktails",
                Array.Empty<string>(), new BlueprintBlockSize(3, 4, 1));
            TestAssert.True(footprintChange.Any(problem =>
                    problem.Contains("migration review", StringComparison.Ordinal)),
                "a footprint change must fail gate (c) until the history and migration review are updated");

            // A blueprint without any recorded history is a violation, never a silent pass.
            TestAssert.Equal(1, BlueprintHistoryContract.Evaluate(null, "BoxFarm.Basic.Folktails",
                Array.Empty<string>(), size).Count);
        }

        // ---------------------------------------------------------------- helpers

        private sealed class BlueprintUnderTest
        {
            public BlueprintUnderTest(string relativePath, string? templateName,
                IReadOnlyList<string> backwardNames, BlueprintBlockSize size)
            {
                RelativePath = relativePath;
                TemplateName = templateName;
                BackwardNames = backwardNames;
                Size = size;
            }

            public string RelativePath { get; }

            public string? TemplateName { get; }

            public IReadOnlyList<string> BackwardNames { get; }

            public BlueprintBlockSize Size { get; }
        }

        private static List<BlueprintUnderTest> ReadBlueprints(string root)
        {
            var directory = Path.Combine(root, "data",
                BoxFarmRelativeDir.Replace('/', Path.DirectorySeparatorChar));
            var result = new List<BlueprintUnderTest>();
            foreach (var file in Directory.GetFiles(directory, "*.blueprint.json", SearchOption.TopDirectoryOnly)
                         .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                using var document = JsonDocument.Parse(File.ReadAllText(file));
                var template = document.RootElement.GetProperty("TemplateSpec");
                var spec = document.RootElement.GetProperty("BlockObjectSpec");
                var size = spec.GetProperty("Size");
                result.Add(new BlueprintUnderTest(
                    BoxFarmRelativeDir + "/" + Path.GetFileName(file),
                    template.GetProperty("TemplateName").GetString(),
                    ReadStringArray(template, "BackwardCompatibleTemplateNames"),
                    new BlueprintBlockSize(size.GetProperty("X").GetInt32(), size.GetProperty("Y").GetInt32(),
                        size.GetProperty("Z").GetInt32())));
            }

            return result;
        }

        private static Dictionary<string, BlueprintHistoryExpectation> ReadHistory(string root)
        {
            var path = Path.Combine(root, HistoryRelativePath.Replace('/', Path.DirectorySeparatorChar));
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var result = new Dictionary<string, BlueprintHistoryExpectation>(StringComparer.Ordinal);
            foreach (var entry in document.RootElement.GetProperty("blueprints").EnumerateArray())
            {
                var blueprint = entry.GetProperty("blueprint").GetString() ?? string.Empty;
                var names = ReadStringArray(entry, "publishedTemplateNames");
                var releases = new List<BlueprintPublishedRelease>();
                foreach (var release in entry.GetProperty("releases").EnumerateArray())
                {
                    var size = release.GetProperty("size");
                    releases.Add(new BlueprintPublishedRelease(
                        release.GetProperty("version").GetString() ?? string.Empty,
                        new BlueprintBlockSize(size[0].GetInt32(), size[1].GetInt32(), size[2].GetInt32()),
                        release.TryGetProperty("note", out var note) ? note.GetString() : null));
                }

                result[blueprint] = new BlueprintHistoryExpectation(blueprint, names, releases);
            }

            return result;
        }

        private static List<string> ReadStringArray(JsonElement element, string property)
        {
            var result = new List<string>();
            if (!element.TryGetProperty(property, out var node) || node.ValueKind != JsonValueKind.Array)
            {
                return result;
            }

            foreach (var item in node.EnumerateArray())
            {
                var value = item.GetString();
                if (!string.IsNullOrEmpty(value))
                {
                    result.Add(value!);
                }
            }

            return result;
        }

        private static string Read(string root, string relativePath)
        {
            var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
            {
                throw new TestFailureException("source file not found: " + path);
            }

            return File.ReadAllText(path);
        }

        private static string RepoRoot()
        {
            var metadata = typeof(V2BlueprintHistoryContractTests).Assembly
                .GetCustomAttributes(typeof(AssemblyMetadataAttribute), inherit: false)
                .OfType<AssemblyMetadataAttribute>()
                .FirstOrDefault(attribute => string.Equals(attribute.Key, RepoRootKey, StringComparison.Ordinal));
            if (metadata is null || string.IsNullOrWhiteSpace(metadata.Value))
            {
                throw new TestFailureException("assembly metadata \"" + RepoRootKey + "\" is missing");
            }

            return metadata.Value!;
        }
    }
}
