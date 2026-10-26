using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HDBF.Core.Compat;
using HDBF.Core.Config;

namespace HDBF.Tests
{
    /// <summary>
    /// L1 contract of the REL-01 release gates after the v2 closure: 0 Harmony (section
    /// 19.18.3-B5), config schema 2 with the deleted v1 keys gone, and the unchanged
    /// compatibility-report/localization contract.
    ///
    /// These are pure data/source contracts: the gate manifest that build.ps1 enforces
    /// (<c>tools/release/release-gates.json</c>), the shipped data config and build props are
    /// embedded as text/resources; the source scan uses the repository root baked into this
    /// assembly. No Timberborn or Unity assembly is referenced (REQ-V2-TEST-01); deleting a
    /// release gate from build.ps1 without updating the contract, or reintroducing Harmony,
    /// fails L1 here.
    /// </summary>
    public static class ReleaseGateContractTests
    {
        private const string ManifestResource = "HDBF.Tests.Release.release-gates.json";
        private const string BuildPropsResource = "HDBF.Tests.Release.Directory.Build.props";
        private const string DataConfigResource = "HDBF.Tests.Release.HDBF_Config.json";
        private const string RepoRootKey = "HdbfRepoRoot";

        // ------------------------------------------------------------------ manifest shape

        public static void TestReleaseGateManifestIsWellFormed()
        {
            var manifest = ParseJsonResource(ManifestResource);

            TestAssert.Equal(1, manifest.GetProperty("manifestVersion").GetInt32(),
                "release-gates.json format version");

            var gateIds = StringArray(manifest.GetProperty("gateIds"));
            TestAssert.Equal(9, gateIds.Length, "the manifest lists every chapter-15.5 gate");
            for (var number = 1; number <= 9; number++)
            {
                var expected = "G-0" + number;
                TestAssert.True(gateIds.Contains(expected, StringComparer.Ordinal),
                    "manifest must map release gate " + expected);
            }

            TestAssert.Equal(5, StringArray(manifest.GetProperty("requiredAssemblies")).Length,
                "G-08 stages exactly the five mod assemblies");
            TestAssert.Equal(13, StringArray(manifest.GetProperty("compatReport").GetProperty("fields")).Length,
                "table 10-4 has 13 compatibility-report fields");
            TestAssert.Equal("FAILED 0", manifest.GetProperty("l1").GetProperty("mustContain").GetString());
            TestAssert.True(manifest.GetProperty("l1").GetProperty("minTests").GetInt32() >= 1,
                "the L1 gate must require a non-empty suite");
            TestAssert.Equal(3,
                StringArray(manifest.GetProperty("localizations").GetProperty("languages")).Length,
                "enUS, zhCN and zhTW ship");
        }

        // ------------------------------------------------------------------ 0 Harmony

        public static void TestZeroHarmonyContractMatchesShippedSources()
        {
            var manifest = ParseJsonResource(ManifestResource);
            var harmony = manifest.GetProperty("harmony");

            TestAssert.Equal(0, harmony.GetProperty("maxPatches").GetInt32(),
                "section 19.18.3-B5: v2 allows exactly 0 Harmony patches");

            var forbiddenDirectory = harmony.GetProperty("forbiddenSourceDirectory").GetString()!;
            TestAssert.Equal("src/HDBF.Integration/Harmony", forbiddenDirectory.Replace('\\', '/'));
            TestAssert.False(Directory.Exists(RepoPath(forbiddenDirectory)),
                "the v1 patch source directory must be deleted");

            var forbiddenTokens = StringArray(harmony.GetProperty("forbiddenTokens"));
            foreach (var token in new[] { "HarmonyLib", "HarmonyInstance", "[HarmonyPatch]", "Transpiler" })
            {
                TestAssert.True(forbiddenTokens.Contains(token, StringComparer.Ordinal),
                    "the staged-assembly contract must forbid the token " + token);
            }

            var sourceTokens = StringArray(harmony.GetProperty("forbiddenSourceTokens"));
            var patchCountPattern = harmony.GetProperty("nonzeroPatchCountPattern").GetString()!;
            var sourceRoot = RepoPath(harmony.GetProperty("sourceScanRoot").GetString()!);
            TestAssert.True(Directory.Exists(sourceRoot), "source scan root must exist: " + sourceRoot);

            foreach (var file in Directory.GetFiles(sourceRoot, "*.cs", SearchOption.AllDirectories))
            {
                var text = File.ReadAllText(file, Encoding.UTF8);
                var relative = Path.GetRelativePath(RepoRoot(), file).Replace('\\', '/');
                foreach (var token in sourceTokens)
                {
                    TestAssert.False(text.Contains(token, StringComparison.Ordinal),
                        relative + " must not reference the deleted patch layer token " + token);
                }

                TestAssert.False(Regex.IsMatch(text, patchCountPattern),
                    relative + " must not report a non-zero harmonyPatchCount");
            }

            var compatSource = File.ReadAllText(RepoPath(harmony.GetProperty("compatReportSource").GetString()!),
                Encoding.UTF8);
            TestAssert.True(compatSource.Contains("harmonyPatchCount", StringComparison.Ordinal),
                "the compatibility report keeps its harmonyPatchCount JSON field");
            TestAssert.True(compatSource.Contains("public int HarmonyPatchCount", StringComparison.Ordinal),
                "the compatibility report keeps the field in the table-10-4 shape");

            var configFile = RepoPath(harmony.GetProperty("configFile").GetString()!);
            TestAssert.True(File.Exists(configFile), "shipped config must exist: " + configFile);
            var config = ParseJsonResource(DataConfigResource);
            foreach (var key in StringArray(harmony.GetProperty("forbiddenConfigKeys")))
            {
                TestAssert.False(config.GetProperty("features").TryGetProperty(key, out _),
                    "the shipped config must not expose the patch switch features." + key);
            }
        }

        // ------------------------------------------------------------------ config schema 2

        public static void TestShippedConfigIsSchemaTwoWithoutDeletedV1Keys()
        {
            var manifest = ParseJsonResource(ManifestResource);
            var contract = manifest.GetProperty("configContract");
            var config = ParseJsonResource(DataConfigResource);

            var schemaKey = contract.GetProperty("schemaVersionKey").GetString()!;
            TestAssert.Equal(contract.GetProperty("schemaVersion").GetInt32(),
                config.GetProperty(schemaKey).GetInt32(), "shipped config schema version");
            TestAssert.Equal(2, config.GetProperty(schemaKey).GetInt32(), "v2 config schema is 2");

            foreach (var section in StringArray(contract.GetProperty("requiredSections")))
            {
                TestAssert.True(config.TryGetProperty(section, out var node) && node.ValueKind == JsonValueKind.Object,
                    "shipped config is missing the v2 section " + section);
            }

            var forbidden = StringArray(contract.GetProperty("forbiddenKeys"));
            foreach (var section in new[] { "features", "perf", "ui" })
            {
                var node = config.GetProperty(section);
                foreach (var key in forbidden)
                {
                    TestAssert.False(node.TryGetProperty(key, out _),
                        "deleted v1 key " + section + "." + key + " must not be shipped");
                }
            }

            foreach (var key in forbidden)
            {
                TestAssert.False(config.TryGetProperty(key, out _),
                    "deleted v1 top-level key " + key + " must not be shipped");
            }

            var warnings = new List<string>();
            var parsed = HdbfConfigCodec.Parse(ReadResource(DataConfigResource), warnings.Add);
            TestAssert.Equal(0, warnings.Count,
                "the shipped config must parse cleanly: " + string.Join(" | ", warnings));
            TestAssert.Equal(HdbfConfig.CurrentSchemaVersion, parsed.ConfigSchemaVersion);
            TestAssert.Equal(0, parsed.TierOverrides.Count(tier => tier is not null),
                "the shipped file keeps the four empty tier overrides");
        }

        // ------------------------------------------------------------------ dev diagnostics gone

        public static void TestDevDiagnosticsCompileSwitchWasDeleted()
        {
            var manifest = ParseJsonResource(ManifestResource);
            TestAssert.False(manifest.TryGetProperty("devDiagnostics", out _),
                "the dev-diagnostics gate section is deleted with the source");

            var buildProps = ReadResource(BuildPropsResource);
            TestAssert.False(buildProps.Contains("HdbfDevDiagnostics", StringComparison.Ordinal),
                "Directory.Build.props must no longer expose the dev-diagnostics property");
            TestAssert.False(buildProps.Contains("HDBF_DEV_DIAGNOSTICS", StringComparison.Ordinal),
                "Directory.Build.props must no longer define the dev-diagnostics constant");

            var sourceRoot = RepoPath("src");
            foreach (var file in Directory.GetFiles(sourceRoot, "*.cs", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(RepoRoot(), file).Replace('\\', '/');
                TestAssert.False(relative.EndsWith("HdbfBoxFarmController.Diagnostics.cs", StringComparison.Ordinal),
                    "the v1 diagnostics partial must be deleted: " + relative);

                var text = File.ReadAllText(file, Encoding.UTF8);
                TestAssert.False(text.Contains("HDBF_DEV_DIAGNOSTICS", StringComparison.Ordinal),
                    relative + " still references the deleted compile switch");
            }
        }

        // ------------------------------------------------------------------ gate / build sync

        public static void TestGateManifestKeysAreReadByBuildScript()
        {
            var buildScript = File.ReadAllText(RepoPath("build.ps1"), Encoding.UTF8);

            // Every manifest section/field the contract relies on must be named in build.ps1 (a
            // renamed manifest field otherwise degrades to $null in PowerShell and the gate
            // silently stops enforcing).
            foreach (var token in new[]
                     {
                         "configContract", "schemaVersionKey", "requiredSections", "forbiddenKeys",
                         "harmony", "maxPatches", "forbiddenSourceDirectory", "forbiddenTokens",
                         "forbiddenSourceTokens", "nonzeroPatchCountPattern", "stagedAssembly",
                         "stagedFile", "minTests", "requiredAssemblies",
                     })
            {
                TestAssert.True(buildScript.Contains(token, StringComparison.Ordinal),
                    "build.ps1 must read the manifest field '" + token + "'");
            }

            TestAssert.True(buildScript.Contains("RELEASE GATES: PASSED", StringComparison.Ordinal),
                "build.ps1 still prints the gate verdict");
            TestAssert.False(buildScript.Contains("DevDiagnostics", StringComparison.Ordinal),
                "build.ps1 must no longer expose a dev-diagnostics mode");
            TestAssert.False(buildScript.Contains("HarmonyBootstrap", StringComparison.Ordinal),
                "build.ps1 must no longer read a patch bootstrap source");
        }

        // ------------------------------------------------------------------ compatibility report

        public static void TestCompatReportCarriesEveryReleaseGateField()
        {
            var manifest = ParseJsonResource(ManifestResource);
            var compat = manifest.GetProperty("compatReport");
            var expected = StringArray(compat.GetProperty("fields"));
            TestAssert.Equal(13, expected.Length, "table 10-4 field count");
            TestAssert.Equal(expected.Length, expected.Distinct(StringComparer.Ordinal).Count(),
                "the manifest must not list a report field twice");
            TestAssert.True(expected.Contains("harmonyPatchCount", StringComparer.Ordinal),
                "the report keeps the harmonyPatchCount field (value always 0)");

            var report = new CompatReport();
            TestAssert.Equal(compat.GetProperty("formatVersion").GetInt32(), report.ReportVersion,
                "the manifest and CompatReport must agree on the report format version");
            TestAssert.Equal(0, report.HarmonyPatchCount, "0 Harmony: the default patch count is zero");

            using var document = JsonDocument.Parse(report.ToJson());
            var actual = new List<string>();
            foreach (var property in document.RootElement.EnumerateObject())
            {
                actual.Add(property.Name);
            }

            TestAssert.Equal(expected.Length, actual.Count, "report field count");
            for (var index = 0; index < expected.Length; index++)
            {
                TestAssert.Equal(expected[index], actual[index],
                    "report field order and name at index " + index + " (table 10-4)");
            }
        }

        // ------------------------------------------------------------------ helpers

        private static JsonElement ParseJsonResource(string name)
        {
            using var document = JsonDocument.Parse(ReadResource(name));
            return document.RootElement.Clone();
        }

        private static string[] StringArray(JsonElement element)
        {
            var values = new List<string>();
            foreach (var item in element.EnumerateArray())
            {
                values.Add(item.GetString()
                           ?? throw new TestFailureException("expected a JSON string array entry"));
            }

            return values.ToArray();
        }

        private static string ReadResource(string name)
        {
            var assembly = typeof(ReleaseGateContractTests).Assembly;
            using var stream = assembly.GetManifestResourceStream(name);
            if (stream is null)
            {
                throw new TestFailureException("embedded resource not found: " + name);
            }

            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        }

        private static string RepoRoot()
        {
            var metadata = typeof(ReleaseGateContractTests).Assembly
                .GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), inherit: false)
                .OfType<System.Reflection.AssemblyMetadataAttribute>()
                .FirstOrDefault(attribute => string.Equals(attribute.Key, RepoRootKey, StringComparison.Ordinal));
            if (metadata is null || string.IsNullOrWhiteSpace(metadata.Value))
            {
                throw new TestFailureException("missing HdbfRepoRoot assembly metadata");
            }

            return metadata.Value!;
        }

        private static string RepoPath(string relativePath) =>
            Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
    }
}
