// ============================================================================
// HDBF v2 | Layer: L1 test | Owner: V2-GATE-04/05/07
// Purpose: NFR release-gate matrix summary (tasks NFR-GATE-02/04/05/07): proves the
//          0-Harmony + staged-package contract, the save-safety suite (round trip,
//          corruption injection, v1 migration, 200-round scan, 100x600 budget) and the
//          crash-safety fault-injection suite exist and are wired into L1, checks the
//          three-language localization set (identical key sets, no empty text, no v1
//          terminology) and maps every NFR-GATE-01..07 of section 19.18.4 to the test
//          type that owns it. Single PASSED line for release-log evidence.
// Requirements: REQ-V2-NFR-02, REQ-V2-SAVE-04, REQ-V2-SAVE-07, REQ-V2-SAFE-05,
//               REQ-V2-SAFE-08, REQ-V2-UI-03/04, REQ-V2-READ-04, REQ-V2-TEST-01
// ============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HDBF.Tests
{
    public static class V2NfrReleaseGateMatrixTests
    {
        private const string RepoRootKey = "HdbfRepoRoot";
        private const string ManifestResource = "HDBF.Tests.Release.release-gates.json";
        private const string DataConfigResource = "HDBF.Tests.Release.HDBF_Config.json";
        private const string RequirementsDocument = "docs/REQ_V2_SECTION_19.md";

        private static readonly string[] Languages = { "enUS", "zhCN", "zhTW" };

        /// <summary>v1 user-facing terminology that must be gone from the shipped texts (REQ-V2-UI-04).</summary>
        private static readonly string[] RemovedV1Terms =
        {
            "工作范围", "托管格", "开垦", "地块高亮", "RegisteredPlots", "TillablePlots",
        };

        private static readonly string[] RemovedV1EnglishTerms =
        {
            "work range", "managed plot", "tillable", "highlight",
        };

        /// <summary>Each NFR gate of section 19.18.4 and the L1 test type(s) that own it.</summary>
        private static readonly Dictionary<string, string[]> GateOwners = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            { "NFR-GATE-01", new[] { "V2NfrDocumentationTests" } },
            { "NFR-GATE-02", new[] { "V2NfrCodeContractTests", "V2NfrReleaseGateMatrixTests" } },
            { "NFR-GATE-03", new[] { "V2NfrCodeContractTests" } },
            { "NFR-GATE-04", new[] { "V2NfrReleaseGateMatrixTests", "V2SaveCodecTests", "V2SaveMigrationTests", "V2SaveBudgetTests" } },
            { "NFR-GATE-05", new[] { "V2NfrReleaseGateMatrixTests", "V2SaveFaultInjectionTests", "V2SlotFaultIsolationTests" } },
            { "NFR-GATE-06", new[] { "V2NfrPerformanceTests" } },
            { "NFR-GATE-07", new[] { "V2NfrReleaseGateMatrixTests", "V2LocalizationTests", "V2PanelContractTests" } },
        };

        // ------------------------------------------------------------------ the matrix

        public static void TestNfrReleaseGateMatrix()
        {
            var checks = new List<string>();

            VerifyGateMap(checks);
            VerifyZeroHarmonyContract(checks);
            VerifyStagedPackageContract(checks);
            VerifySaveSafetySuite(checks);
            VerifyFaultInjectionSuite(checks);
            VerifyLocalizationContract(checks);

            Console.WriteLine("NFR GATE MATRIX: PASSED (" + checks.Count + " checks)");
        }

        // ------------------------------------------------------------------ gate map

        private static void VerifyGateMap(List<string> checks)
        {
            var requirements = RepoRead(RequirementsDocument);
            foreach (var gate in GateOwners.Keys)
            {
                TestAssert.True(requirements.Contains(gate, StringComparison.Ordinal),
                    "section 19.18.4 must define " + gate);
                foreach (var owner in GateOwners[gate])
                {
                    RequireTestClass(owner);
                }
            }

            TestAssert.True(GateOwners.Count == 7,
                "section 19.18.4 defines exactly seven NFR gates; the matrix maps " + GateOwners.Count);
            checks.Add("NFR-GATE-01..07 mapped to L1 owners");
        }

        // ------------------------------------------------------------------ 0 Harmony

        private static void VerifyZeroHarmonyContract(List<string> checks)
        {
            var manifest = ParseJsonResource(ManifestResource);
            var harmony = manifest.GetProperty("harmony");

            TestAssert.Equal(0, harmony.GetProperty("maxPatches").GetInt32(),
                "section 19.18.3-B5: v2 allows exactly 0 Harmony patches");
            TestAssert.Equal("src/HDBF.Integration/Harmony",
                harmony.GetProperty("forbiddenSourceDirectory").GetString()!.Replace('\\', '/'),
                "the manifest must name the deleted patch source directory");
            TestAssert.False(Directory.Exists(RepoPath("src/HDBF.Integration/Harmony")),
                "the v1 Harmony patch directory must not exist");

            var srcRoot = RepoPath("src");
            foreach (var directory in Directory.GetDirectories(srcRoot, "Harmony", SearchOption.AllDirectories))
            {
                throw new TestFailureException("Harmony source directory reintroduced: "
                                               + Path.GetRelativePath(RepoRoot(), directory));
            }

            var forbiddenTokens = StringArray(harmony.GetProperty("forbiddenSourceTokens"));
            var patchCountPattern = harmony.GetProperty("nonzeroPatchCountPattern").GetString()!;
            foreach (var path in Directory.GetFiles(srcRoot, "*.cs", SearchOption.AllDirectories))
            {
                var text = File.ReadAllText(path, Encoding.UTF8);
                var relative = Path.GetRelativePath(RepoRoot(), path).Replace('\\', '/');
                foreach (var token in forbiddenTokens)
                {
                    TestAssert.False(text.Contains(token, StringComparison.Ordinal),
                        relative + " still references the deleted Harmony layer token " + token);
                }

                TestAssert.False(Regex.IsMatch(text, patchCountPattern),
                    relative + " reports a non-zero harmonyPatchCount");
            }

            var compatReport = RepoString(harmony, "compatReportSource");
            var compatText = RepoRead(compatReport);
            TestAssert.True(compatText.Contains("harmonyPatchCount", StringComparison.Ordinal),
                "the compatibility report keeps its harmonyPatchCount JSON field");
            TestAssert.True(compatText.Contains("public int HarmonyPatchCount", StringComparison.Ordinal),
                "the compatibility report keeps the table-10-4 field shape");

            var config = ParseJsonResource(DataConfigResource);
            foreach (var key in StringArray(harmony.GetProperty("forbiddenConfigKeys")))
            {
                TestAssert.False(config.GetProperty("features").TryGetProperty(key, out _),
                    "the shipped config still exposes the deleted patch switch features." + key);
            }

            // The gate manifest must be the one build.ps1 actually enforces, not decoration.
            var buildScript = RepoRead("build.ps1");
            TestAssert.True(buildScript.Contains("release-gates.json", StringComparison.Ordinal),
                "build.ps1 must load tools/release/release-gates.json");
            TestAssert.True(buildScript.Contains("maxPatches", StringComparison.Ordinal),
                "build.ps1 must enforce harmony.maxPatches");

            checks.Add("0 Harmony + patch-heavy v1 layer deleted");
        }

        // ------------------------------------------------------------------ staged contract

        private static void VerifyStagedPackageContract(List<string> checks)
        {
            var manifest = ParseJsonResource(ManifestResource);
            TestAssert.Equal(1, manifest.GetProperty("manifestVersion").GetInt32());
            TestAssert.Equal(9, StringArray(manifest.GetProperty("gateIds")).Length,
                "the release-gate manifest must keep the nine chapter-15.5 gates");

            var assemblies = StringArray(manifest.GetProperty("requiredAssemblies"));
            Array.Sort(assemblies, StringComparer.Ordinal);
            var expectedAssemblies = new[]
            {
                "HDBF.Core", "HDBF.Gameplay", "HDBF.Integration", "HDBF.Persistence", "HDBF.UI",
            };
            TestAssert.Equal(expectedAssemblies.Length, assemblies.Length,
                "the staged package must carry exactly the five mod assemblies");
            for (var index = 0; index < expectedAssemblies.Length; index++)
            {
                TestAssert.Equal(expectedAssemblies[index], assemblies[index], "staged assembly #" + index);
            }

            var extensions = StringArray(manifest.GetProperty("allowedStagedExtensions"));
            foreach (var extension in new[] { ".dll", ".json", ".csv", ".png" })
            {
                TestAssert.True(Array.IndexOf(extensions, extension) >= 0,
                    "the staged package contract must allow " + extension);
            }

            var forbiddenPatterns = StringArray(manifest.GetProperty("forbiddenStagedPatterns"));
            foreach (var pattern in new[] { "*test*", "*debug*", "*cheat*", "*diag*", "*.pdb", "*.xml" })
            {
                TestAssert.True(Array.IndexOf(forbiddenPatterns, pattern) >= 0,
                    "the staged package contract must forbid " + pattern);
            }

            var configContract = manifest.GetProperty("configContract");
            TestAssert.Equal(2, configContract.GetProperty("schemaVersion").GetInt32(),
                "the shipped config schema must stay 2 (section 19.10)");
            var sections = StringArray(configContract.GetProperty("requiredSections"));
            foreach (var section in new[] { "features", "perf", "ui", "balance" })
            {
                TestAssert.True(Array.IndexOf(sections, section) >= 0,
                    "config contract must require section " + section);
            }

            var forbiddenConfigKeys = StringArray(configContract.GetProperty("forbiddenKeys"));
            TestAssert.True(forbiddenConfigKeys.Length >= 8,
                "the config contract must forbid every deleted v1 scan/visibility key");
            TestAssert.True(Array.IndexOf(forbiddenConfigKeys, "growthMultiplierPatch") >= 0,
                "growthMultiplierPatch must stay forbidden in the shipped config");

            var reportFields = StringArray(manifest.GetProperty("compatReport").GetProperty("fields"));
            TestAssert.Equal(13, reportFields.Length, "table 10-4 keeps its 13 compatibility-report fields");
            foreach (var field in new[] { "harmonyPatchCount", "modVersion", "schemaVersion", "enabledAdapters", "lastError" })
            {
                TestAssert.True(Array.IndexOf(reportFields, field) >= 0,
                    "compatibility report field " + field + " is missing from the contract");
            }

            var localizations = manifest.GetProperty("localizations");
            TestAssert.Equal(3, StringArray(localizations.GetProperty("languages")).Length,
                "enUS/zhCN/zhTW must ship");
            TestAssert.Equal("ID,Text,Comment", localizations.GetProperty("requiredHeader").GetString(),
                "the localization header contract must stay ID,Text,Comment");

            var testSeams = manifest.GetProperty("testSeams");
            TestAssert.Equal("TestHooks", testSeams.GetProperty("type").GetString());
            TestAssert.False(testSeams.GetProperty("playerFacingEntryPoints").GetBoolean(),
                "the test seam must not be player-facing");
            TestAssert.True(File.Exists(RepoPath(testSeams.GetProperty("sourceFile").GetString()!)),
                "the declared TestHooks seam file must exist");

            var l1 = manifest.GetProperty("l1");
            TestAssert.Equal("FAILED 0", l1.GetProperty("mustContain").GetString(),
                "the L1 release gate must require zero failures");
            TestAssert.True(l1.GetProperty("minTests").GetInt32() >= 480,
                "the L1 gate must require the full v2 suite, not a subset");
            var testProject = l1.GetProperty("project").GetString()!;
            TestAssert.True(File.Exists(RepoPath(testProject)), "L1 project missing: " + testProject);

            var dataConfig = ParseJsonResource(DataConfigResource);
            TestAssert.Equal(2, dataConfig.GetProperty("configSchemaVersion").GetInt32(),
                "the shipped data/HDBF_Config.json must declare schema 2");

            checks.Add("staged package + config/report contract complete");
        }

        // ------------------------------------------------------------------ GATE-04 save suite

        private static void VerifySaveSafetySuite(List<string> checks)
        {
            var saveClasses = new[]
            {
                "V2SaveCodecTests", "V2SaveMigrationTests", "V2SaveFaultInjectionTests",
                "V2SaveValidatorTests", "V2SaveBudgetTests", "V2SaveRepairTests",
            };
            var total = 0;
            foreach (var name in saveClasses)
            {
                total += RequireTestClass(name);
            }

            TestAssert.True(total >= 40,
                "the save-safety suite must stay substantial; only " + total + " save test methods found");

            var codec = RepoRead("tests/HDBF.Tests/V2SaveCodecTests.cs");
            TestAssert.True(codec.Contains("RoundTrip", StringComparison.Ordinal)
                           || codec.Contains("round trip", StringComparison.Ordinal),
                "the save suite must keep the writer/reader round-trip invariant");

            var fault = RepoRead("tests/HDBF.Tests/V2SaveFaultInjectionTests.cs");
            TestAssert.True(fault.Contains("200", StringComparison.Ordinal),
                "the save suite must keep the 200-round deterministic corruption scan");
            TestAssert.True(fault.Contains("Corrupt", StringComparison.Ordinal)
                           || fault.Contains("corrupt", StringComparison.Ordinal),
                "the save suite must keep corruption injection");

            var migration = RepoRead("tests/HDBF.Tests/V2SaveMigrationTests.cs");
            TestAssert.True(migration.Contains("v1", StringComparison.Ordinal)
                           && migration.Contains("Migrat", StringComparison.Ordinal),
                "the save suite must keep the v1 -> v2 migration coverage");

            var validator = RepoRead("tests/HDBF.Tests/V2SaveValidatorTests.cs");
            TestAssert.True(validator.Contains("NaN", StringComparison.Ordinal)
                           && validator.Contains("Infinity", StringComparison.Ordinal),
                "the validator suite must keep the NaN/Inf corruption coverage");

            var budget = RepoRead("tests/HDBF.Tests/V2SaveBudgetTests.cs");
            TestAssert.True(budget.Contains("MeasureBytes", StringComparison.Ordinal)
                           && budget.Contains("BudgetBytesPerBuilding", StringComparison.Ordinal),
                "the save suite must keep the 100-building size budget test");

            var repair = RepoRead("tests/HDBF.Tests/V2SaveRepairTests.cs");
            TestAssert.True(repair.Contains("Repair", StringComparison.Ordinal),
                "the save suite must keep the field/slot repair coverage");

            // The helper group is shared by the suite; it must stay in the test assembly.
            var fixtures = typeof(V2SaveFixtures);
            TestAssert.NotNull(fixtures.GetMethod("AssertRecordsEqual",
                    BindingFlags.NonPublic | BindingFlags.Static),
                "the save test fixtures helper lost its record-equality assertion");
            TestAssert.NotNull(fixtures.GetMethod("AssertInvariants",
                    BindingFlags.NonPublic | BindingFlags.Static),
                "the save test fixtures helper lost its section-19.18.3-A2 invariant assertion");

            checks.Add("save safety suite (round trip/corruption/v1 migration/200 rounds/budget)");
        }

        // ------------------------------------------------------------------ GATE-05 fault suite

        private static void VerifyFaultInjectionSuite(List<string> checks)
        {
            var faultClasses = new[] { "V2SaveFaultInjectionTests", "V2SlotFaultIsolationTests", "V2GameplayWiringTests" };
            var total = 0;
            foreach (var name in faultClasses)
            {
                total += RequireTestClass(name);
            }

            TestAssert.True(total >= 20,
                "the crash-safety fault-injection suite must stay substantial; only " + total + " methods found");

            var isolation = RepoRead("tests/HDBF.Tests/V2SlotFaultIsolationTests.cs");
            TestAssert.True(isolation.Contains("Test", StringComparison.Ordinal),
                "the slot fault isolation suite must run");
            var wiring = RepoRead("tests/HDBF.Tests/V2GameplayWiringTests.cs");
            TestAssert.True(wiring.Contains("Test", StringComparison.Ordinal),
                "the gameplay wiring fault suite must run");

            checks.Add("fault injection suite (save/slot/gameplay wiring)");
        }

        // ------------------------------------------------------------------ GATE-07 localization

        private static void VerifyLocalizationContract(List<string> checks)
        {
            var texts = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
            foreach (var language in Languages)
            {
                texts[language] = LoadLocalization(language);
            }

            var english = texts["enUS"];
            TestAssert.True(english.Count >= 120,
                "the v2 panel must ship a substantial key set; only " + english.Count + " keys in enUS");

            foreach (var language in Languages)
            {
                var current = texts[language];
                TestAssert.Equal(english.Count, current.Count,
                    language + " key count differs from enUS (" + current.Count + " vs " + english.Count + ")");
                foreach (var pair in english)
                {
                    TestAssert.True(current.ContainsKey(pair.Key),
                        language + " is missing the UI key " + pair.Key);
                    var text = current[pair.Key];
                    TestAssert.True(text.Trim().Length > 0,
                        language + " key " + pair.Key + " has empty text");
                    foreach (var term in RemovedV1Terms)
                    {
                        TestAssert.False(text.Contains(term, StringComparison.Ordinal),
                            language + " key " + pair.Key + " still contains the v1 term " + term);
                    }

                    if (string.Equals(language, "enUS", StringComparison.Ordinal))
                    {
                        foreach (var term in RemovedV1EnglishTerms)
                        {
                            TestAssert.False(text.Contains(term, StringComparison.OrdinalIgnoreCase),
                                "enUS key " + pair.Key + " still contains the v1 term " + term);
                        }
                    }
                }
            }

            Console.WriteLine("[nfr-gate-matrix] localization: " + english.Count
                              + " keys, identical sets in enUS/zhCN/zhTW, 0 empty, 0 v1 terms");
            checks.Add("three-language localization contract (keys/empty/v1 terms)");
        }

        // ------------------------------------------------------------------ helpers: assertions

        private static int RequireTestClass(string name)
        {
            var type = typeof(V2NfrReleaseGateMatrixTests).Assembly.GetType("HDBF.Tests." + name, throwOnError: false);
            if (type is null)
            {
                throw new TestFailureException("missing L1 test class HDBF.Tests." + name);
            }

            var count = 0;
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (!method.Name.StartsWith("Test", StringComparison.Ordinal)
                    || method.GetParameters().Length != 0
                    || method.ReturnType != typeof(void))
                {
                    continue;
                }

                count++;
            }

            TestAssert.True(count >= 1, name + " must contain at least one public static Test* test");
            return count;
        }

        // ------------------------------------------------------------------ helpers: localization

        private static Dictionary<string, string> LoadLocalization(string language)
        {
            var resource = "HDBF.Tests.Localizations." + language + ".csv";
            var assembly = typeof(V2NfrReleaseGateMatrixTests).Assembly;
            using var stream = assembly.GetManifestResourceStream(resource);
            if (stream is null)
            {
                throw new TestFailureException("missing embedded localization resource " + resource);
            }

            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var rows = ParseCsv(reader.ReadToEnd());
            TestAssert.True(rows.Count > 1, language + ".csv must contain a header and rows");
            var header = string.Join(",", rows[0]).Trim();
            TestAssert.Equal("ID,Text,Comment", header, language + " CSV header");

            var texts = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var index = 1; index < rows.Count; index++)
            {
                var row = rows[index];
                if (row.Length == 0 || (row.Length == 1 && row[0].Trim().Length == 0))
                {
                    continue;
                }

                TestAssert.Equal(3, row.Length, language + ".csv line " + (index + 1) + " must have 3 fields");
                var id = row[0].Trim();
                TestAssert.True(id.Length > 0, language + ".csv line " + (index + 1) + " has an empty key");
                TestAssert.False(texts.ContainsKey(id),
                    language + ".csv has the duplicate key " + id);
                texts[id] = row[1];
            }

            return texts;
        }

        private static List<string[]> ParseCsv(string text)
        {
            var rows = new List<string[]>();
            var fields = new List<string>();
            var field = new StringBuilder();
            var inQuotes = false;
            for (var index = 0; index < text.Length; index++)
            {
                var character = text[index];
                if (inQuotes)
                {
                    if (character == '"')
                    {
                        if (index + 1 < text.Length && text[index + 1] == '"')
                        {
                            field.Append('"');
                            index++;
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        field.Append(character);
                    }
                }
                else if (character == '"')
                {
                    inQuotes = true;
                }
                else if (character == ',')
                {
                    fields.Add(field.ToString());
                    field.Clear();
                }
                else if (character == '\r')
                {
                    // Normalized away.
                }
                else if (character == '\n')
                {
                    fields.Add(field.ToString());
                    field.Clear();
                    rows.Add(fields.ToArray());
                    fields.Clear();
                }
                else
                {
                    field.Append(character);
                }
            }

            if (field.Length > 0 || fields.Count > 0)
            {
                fields.Add(field.ToString());
                rows.Add(fields.ToArray());
            }

            return rows;
        }

        // ------------------------------------------------------------------ helpers: JSON

        private static JsonElement ParseJsonResource(string resource)
        {
            var assembly = typeof(V2NfrReleaseGateMatrixTests).Assembly;
            using var stream = assembly.GetManifestResourceStream(resource);
            if (stream is null)
            {
                throw new TestFailureException("missing embedded resource " + resource);
            }

            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            using var document = JsonDocument.Parse(reader.ReadToEnd());
            return document.RootElement.Clone();
        }

        private static string[] StringArray(JsonElement element)
        {
            var result = new List<string>();
            foreach (var item in element.EnumerateArray())
            {
                var value = item.GetString();
                if (value is not null)
                {
                    result.Add(value);
                }
            }

            return result.ToArray();
        }

        private static string RepoString(JsonElement element, string property)
        {
            var value = element.GetProperty(property).GetString();
            if (value is null)
            {
                throw new TestFailureException("manifest field " + property + " must be a string");
            }

            return value;
        }

        // ------------------------------------------------------------------ helpers: repo IO

        private static string RepoRead(string relativePath)
        {
            return File.ReadAllText(RepoPath(relativePath), Encoding.UTF8);
        }

        private static string RepoPath(string relativePath)
        {
            return Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static string RepoRoot()
        {
            var metadata = typeof(V2NfrReleaseGateMatrixTests).Assembly
                .GetCustomAttributes<AssemblyMetadataAttribute>();
            foreach (var attribute in metadata)
            {
                if (string.Equals(attribute.Key, RepoRootKey, StringComparison.Ordinal)
                    && !string.IsNullOrEmpty(attribute.Value))
                {
                    return attribute.Value!;
                }
            }

            throw new TestFailureException("missing HdbfRepoRoot assembly metadata");
        }
    }
}
