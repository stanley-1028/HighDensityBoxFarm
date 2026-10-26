using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using HDBF.Core.Compat;
using HDBF.Core.Config;

namespace HDBF.Tests
{
    /// <summary>
    /// L1 contract of the REL-01 release gates (G-08 "发布包不含调试命令、测试蓝图与作弊开关",
    /// G-09 localization parity, G-05 compatibility report of table 10-4).
    ///
    /// These are pure data/source contracts: the gate manifest that build.ps1 enforces
    /// (<c>tools/release/release-gates.json</c>), the single dev-diagnostics build property
    /// (<c>Directory.Build.props</c>), the shipped diagnostics partial, the patch whitelist and
    /// the shipped data config are embedded as text by HDBF.Tests.csproj. No Timberborn or Unity
    /// assembly is referenced (REQ-TEST-03); deleting a release gate from build.ps1 without
    /// updating the contract, or re-enabling the diagnostics unconditionally, fails L1 here.
    /// </summary>
    public static class ReleaseGateContractTests
    {
        private const string ManifestResource = "HDBF.Tests.Release.release-gates.json";
        private const string BuildPropsResource = "HDBF.Tests.Release.Directory.Build.props";
        private const string DataConfigResource = "HDBF.Tests.Release.HDBF_Config.json";
        private const string DiagnosticsResource = "HDBF.Tests.Source.Diagnostics.cs";
        private const string PatchCatalogResource = "HDBF.Tests.Source.GrowthPatchCatalog.cs";
        private const string BootstrapResource = "HDBF.Tests.Source.HarmonyBootstrap.cs";

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
            TestAssert.Equal(1, manifest.GetProperty("patches").GetProperty("maxCount").GetInt32(),
                "ADR-009 allows at most one patch");
            TestAssert.True(manifest.GetProperty("l1").GetProperty("minTests").GetInt32() >= 1,
                "the L1 gate must require a non-empty suite");
            TestAssert.Equal("FAILED 0", manifest.GetProperty("l1").GetProperty("mustContain").GetString());
        }

        // ------------------------------------------------------------------ dev diagnostics

        /// <summary>
        /// "Diagnostics default off" is proven twice: the manifest/Property default must be false,
        /// and the shipped partial must keep every diag.* token inside the <c>#if
        /// HDBF_DEV_DIAGNOSTICS</c> branch while the <c>#else</c> branch only carries the empty
        /// call-site seams. The data config must NOT expose a features.devDiagnostics switch: the
        /// chosen mechanism is compile-time, so a player edit cannot re-enable the evidence code
        /// (REQ-BAL-27 / G-08).
        /// </summary>
        public static void TestDevDiagnosticsAreCompileTimeOnlyAndOffByDefault()
        {
            var manifest = ParseJsonResource(ManifestResource);
            var diagnostics = manifest.GetProperty("devDiagnostics");

            TestAssert.False(diagnostics.GetProperty("defaultEnabled").GetBoolean(),
                "dev diagnostics must default to off");
            TestAssert.Equal("HdbfDevDiagnostics", diagnostics.GetProperty("buildProperty").GetString());
            TestAssert.Equal("HDBF_DEV_DIAGNOSTICS", diagnostics.GetProperty("define").GetString());

            var buildProps = ReadResource(BuildPropsResource);
            TestAssert.True(buildProps.Contains(
                    "<HdbfDevDiagnostics Condition=\" '$(HdbfDevDiagnostics)' == '' \">false</HdbfDevDiagnostics>",
                    StringComparison.Ordinal),
                "Directory.Build.props must default HdbfDevDiagnostics to false");
            TestAssert.True(buildProps.Contains(
                    "$(DefineConstants);HDBF_DEV_DIAGNOSTICS", StringComparison.Ordinal),
                "Directory.Build.props must translate the property into the conditional define");
            TestAssert.True(buildProps.Contains(
                    "Condition=\" '$(HdbfDevDiagnostics)' == 'true' \"", StringComparison.Ordinal),
                "the define must only be added when the property is explicitly true");

            var source = ReadResource(DiagnosticsResource);
            // Line-anchored so a comment that merely mentions "#else" can never be mistaken for
            // the preprocessor directive itself.
            var devStartMatch = System.Text.RegularExpressions.Regex.Match(
                source, @"(?m)^#if HDBF_DEV_DIAGNOSTICS\s*$");
            var releaseStartMatch = System.Text.RegularExpressions.Regex.Match(source, @"(?m)^#else\s*$");
            var devEndMatch = System.Text.RegularExpressions.Regex.Match(source, @"(?m)^#endif\s*$");
            TestAssert.True(devStartMatch.Success,
                "diagnostics source must be guarded by #if HDBF_DEV_DIAGNOSTICS");
            TestAssert.True(releaseStartMatch.Success && devEndMatch.Success
                            && releaseStartMatch.Index > devStartMatch.Index
                            && devEndMatch.Index > releaseStartMatch.Index,
                "the release stub branch (#else ... #endif) is missing or out of order");

            var devStart = devStartMatch.Index;
            var releaseStart = releaseStartMatch.Index;
            var devEnd = devEndMatch.Index;

            var devBranch = source.Substring(devStart, releaseStart - devStart);
            var releaseBranch = source.Substring(releaseStart, devEnd - releaseStart);

            var marker = diagnostics.GetProperty("marker").GetString()!;
            TestAssert.True(devBranch.Contains(marker, StringComparison.Ordinal),
                "the dev build carries the auditable assembly marker " + marker);
            TestAssert.False(releaseBranch.Contains(marker, StringComparison.Ordinal),
                "the release branch must not carry the dev marker");

            foreach (var token in new[] { "diag.", "scan.probe", "dryCells", "farm.plan" })
            {
                TestAssert.True(devBranch.Contains(token, StringComparison.Ordinal),
                    "the dev branch must still contain the evidence token '" + token + "'");
                TestAssert.False(releaseBranch.Contains(token, StringComparison.Ordinal),
                    "the release branch must not contain the dev-only token '" + token + "'");
            }

            // The unconditional call sites must still compile against safe empty implementations.
            foreach (var seam in new[] { "DiagScanBegin", "DiagScanEnd", "DiagDaily", "DiagFarmPlanProbe" })
            {
                TestAssert.True(releaseBranch.Contains(seam, StringComparison.Ordinal),
                    "release branch is missing the empty seam " + seam);
            }

            var data = ParseJsonResource(DataConfigResource);
            TestAssert.False(data.GetProperty("features").TryGetProperty("devDiagnostics", out _),
                "no features.devDiagnostics switch: diagnostics are compile-time, not player-editable");
        }

        // ------------------------------------------------------------------ patch whitelist

        /// <summary>
        /// ADR-009 / REQ-COMPAT-16/17: at most one patch, it must be a Postfix, it must use the one
        /// fixed Harmony instance id, it must not rewrite IL, and it must be switchable off through
        /// features.growthMultiplierPatch (the bootstrap returns before installation when off).
        /// </summary>
        public static void TestPatchGateMatchesShippedCatalogAndIsDisableable()
        {
            var manifest = ParseJsonResource(ManifestResource);
            var patches = manifest.GetProperty("patches");
            var maxCount = patches.GetProperty("maxCount").GetInt32();
            TestAssert.Equal(1, maxCount, "ADR-009: at most one patch");

            var catalog = ReadResource(PatchCatalogResource);
            TestAssert.Equal(maxCount, CountOccurrences(catalog, "GrowthPatchKind = \"Postfix\""),
                "the whitelist contains exactly the approved Postfix");
            TestAssert.Equal(1, CountOccurrences(catalog,
                    "HarmonyInstanceId = \"" + patches.GetProperty("harmonyInstanceId").GetString() + "\""),
                "REQ-COMPAT-16 fixes one Harmony instance id");
            TestAssert.Equal(1, CountOccurrences(catalog, "GrowthPatchTargetMethod"),
                "the whitelist names exactly one target method");
            TestAssert.False(catalog.Contains("Transpiler", StringComparison.Ordinal),
                "v1 must not rewrite IL (REQ-IMPL-15/17)");

            var bootstrap = ReadResource(BootstrapResource);
            TestAssert.True(bootstrap.Contains("IsPatchEnabled", StringComparison.Ordinal),
                "the bootstrap must consult the feature switch before installing the patch");
            TestAssert.True(bootstrap.Contains("Unload(", StringComparison.Ordinal),
                "the patch must be unregisterable on session unload (REQ-COMPAT-08 spirit)");

            // The switch must exist in the SHIPPED config and be a boolean, so it can always be
            // turned off without editing code.
            var data = ParseJsonResource(DataConfigResource);
            var switchName = patches.GetProperty("disableSwitch").GetString()!;
            var features = data.GetProperty("features");
            TestAssert.True(features.TryGetProperty(switchName, out var switchValue),
                "shipped HDBF_Config.json must expose features." + switchName);
            TestAssert.True(switchValue.ValueKind is JsonValueKind.True or JsonValueKind.False,
                "features." + switchName + " must be a boolean");

            // ... and the codec really applies it both ways (pure Core, no game assembly).
            var off = HdbfConfigCodec.Parse("{\"features\":{\"" + switchName + "\":false}}");
            TestAssert.False(off.Features.GrowthMultiplierPatch,
                "features." + switchName + "=false must reach the config object");
            var on = HdbfConfigCodec.Parse("{\"features\":{\"" + switchName + "\":true}}");
            TestAssert.True(on.Features.GrowthMultiplierPatch,
                "features." + switchName + "=true must reach the config object");
        }

        // ------------------------------------------------------------------ compatibility report

        /// <summary>
        /// Table 10-4 / REQ-SAFE-16: the serialized report carries exactly the gate manifest's
        /// field set, in the documented order. This is the L1 half of the build.ps1 gate that
        /// cross-checks <c>CompatReport.ToJson()</c> against the same manifest.
        /// </summary>
        public static void TestCompatReportCarriesEveryReleaseGateField()
        {
            var manifest = ParseJsonResource(ManifestResource);
            var compat = manifest.GetProperty("compatReport");
            var expected = StringArray(compat.GetProperty("fields"));
            TestAssert.Equal(13, expected.Length, "table 10-4 field count");
            TestAssert.Equal(expected.Length, expected.Distinct(StringComparer.Ordinal).Count(),
                "the manifest must not list a report field twice");

            var report = new CompatReport();
            TestAssert.Equal(compat.GetProperty("formatVersion").GetInt32(), report.ReportVersion,
                "the manifest and CompatReport must agree on the report format version");

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

        // ------------------------------------------------------------------ localization

        /// <summary>
        /// G-09: the three shipped language files must have identical key sets (missing = 0 and
        /// extra = 0, zhTW included) and every value must be non-empty. This complements the
        /// build.ps1 gate (which checks the staged files) and tools/validate (enUS/zhCN + used keys).
        /// </summary>
        public static void TestAllShippedLocalizationsHaveTheSameKeys()
        {
            var manifest = ParseJsonResource(ManifestResource);
            var localizations = manifest.GetProperty("localizations");
            var languages = StringArray(localizations.GetProperty("languages"));
            TestAssert.Equal(3, languages.Length, "enUS, zhCN and zhTW ship");
            TestAssert.True(languages.Contains("enUS", StringComparer.Ordinal)
                            && languages.Contains("zhCN", StringComparer.Ordinal)
                            && languages.Contains("zhTW", StringComparer.Ordinal),
                "the manifest must list every shipped language");

            var header = localizations.GetProperty("requiredHeader").GetString()!;
            var texts = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
            foreach (var language in languages)
            {
                var rows = ParseCsv(ReadResource("HDBF.Tests.Localizations." + language + ".csv"));
                TestAssert.True(rows.Count > 1, language + ".csv must contain a header and rows");
                TestAssert.Equal(header, string.Join(",", rows[0].Select(field => field.Trim())),
                    language + ".csv header must be ID,Text,Comment (the game rejects anything else)");
                texts[language] = LoadTexts(language);
            }

            var english = texts["enUS"];
            TestAssert.True(english.Count > 0, "enUS.csv must not be empty");

            foreach (var language in languages)
            {
                if (string.Equals(language, "enUS", StringComparison.Ordinal))
                {
                    continue;
                }

                var target = texts[language];
                var missing = english.Keys.Where(key => !target.ContainsKey(key))
                    .OrderBy(key => key, StringComparer.Ordinal).ToArray();
                var extra = target.Keys.Where(key => !english.ContainsKey(key))
                    .OrderBy(key => key, StringComparer.Ordinal).ToArray();

                TestAssert.Equal(0, missing.Length,
                    language + " is missing keys: " + string.Join(", ", missing));
                TestAssert.Equal(0, extra.Length,
                    language + " has keys absent from enUS: " + string.Join(", ", extra));
            }

            foreach (var language in languages)
            {
                foreach (var pair in texts[language])
                {
                    TestAssert.True(pair.Value.Length > 0,
                        language + " key '" + pair.Key + "' must have non-empty text");
                }
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

        private static int CountOccurrences(string text, string needle)
        {
            var count = 0;
            var index = 0;
            while (true)
            {
                index = text.IndexOf(needle, index, StringComparison.Ordinal);
                if (index < 0)
                {
                    return count;
                }

                count++;
                index += needle.Length;
            }
        }

        /// <summary>Loads one shipped CSV from the embedded test resource (ID,Text,Comment).</summary>
        private static Dictionary<string, string> LoadTexts(string language)
        {
            var text = ReadResource("HDBF.Tests.Localizations." + language + ".csv");
            var rows = ParseCsv(text);
            var texts = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 1; i < rows.Count; i++)
            {
                var row = rows[i];
                if (row.Length == 0 || (row.Length == 1 && row[0].Trim().Length == 0))
                {
                    continue;
                }

                if (row.Length != 3)
                {
                    throw new TestFailureException(language + ".csv line " + (i + 1) + " has "
                        + row.Length + " fields; the game requires exactly 3 (ID,Text,Comment)");
                }

                texts[row[0].Trim()] = row[1];
            }

            return texts;
        }

        /// <summary>Minimal RFC-4180 CSV reader (quoted fields may contain commas and doubled quotes).</summary>
        private static List<string[]> ParseCsv(string text)
        {
            var rows = new List<string[]>();
            var fields = new List<string>();
            var field = new StringBuilder();
            var inQuotes = false;
            for (var i = 0; i < text.Length; i++)
            {
                var character = text[i];
                if (inQuotes)
                {
                    if (character == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"')
                        {
                            field.Append('"');
                            i++;
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
                    // Normalised away; the files are read on Windows and Linux.
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
    }
}
