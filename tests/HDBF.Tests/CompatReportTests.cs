using System;
using System.Collections.Generic;
using HDBF.Core.Compat;

namespace HDBF.Tests
{
    /// <summary>
    /// L1 tests for the exportable compatibility report (chapter 10.4 table 10-4,
    /// REQ-SAFE-16): every mandated field present, hand-written JSON valid and stable, and
    /// string escaping safe for bug reports.
    /// </summary>
    public static class CompatReportTests
    {
        public static void TestReportContainsEveryChapterTenField()
        {
            var json = new CompatReport().ToJson();
            var required = new[]
            {
                "reportVersion", "modVersion", "gameVersion", "schemaVersion", "configSchemaVersion",
                "detectedMods", "enabledAdapters", "failedAdapters", "detectionSource",
                "detectionDurationMs", "harmonyPatchCount", "performanceMode", "lastError",
            };

            TestAssert.Equal(13, required.Length, "table 10-4 has 13 data fields");
            foreach (var field in required)
            {
                TestAssert.True(json.Contains("\"" + field + "\":"), "missing report field " + field);
            }
        }

        public static void TestReportDefaults()
        {
            var report = new CompatReport();

            TestAssert.Equal(1, report.ReportVersion);
            TestAssert.Equal("0.1.0", report.ModVersion);
            TestAssert.Equal(2, report.SchemaVersion);
            TestAssert.Equal(2, report.ConfigSchemaVersion);
            TestAssert.Equal("Normal", report.PerformanceMode);
            TestAssert.Equal(0, report.DetectionDurationMs);
            TestAssert.Equal(0, report.HarmonyPatchCount);
            TestAssert.Null(report.LastError);
            TestAssert.Equal(0, report.DetectedMods.Count);
            TestAssert.Equal(0, report.EnabledAdapters.Count);
            TestAssert.Equal(0, report.FailedAdapters.Count);
        }

        public static void TestReportExactJson()
        {
            var report = new CompatReport
            {
                ReportVersion = 1,
                ModVersion = "0.1.0",
                GameVersion = "1.1.2.8",
                SchemaVersion = 2,
                ConfigSchemaVersion = 2,
                DetectedMods = new[] { "TimberPipes" },
                EnabledAdapters = new[] { "HDBF.Adapter.TimberPipes" },
                FailedAdapters = Array.Empty<string>(),
                DetectionSource = "AssemblyScan",
                DetectionDurationMs = 120,
                HarmonyPatchCount = 0,
                PerformanceMode = "Reduced",
                LastError = "boom",
            };

            var expected = string.Join(Environment.NewLine, new[]
            {
                "{",
                "  \"reportVersion\": 1,",
                "  \"modVersion\": \"0.1.0\",",
                "  \"gameVersion\": \"1.1.2.8\",",
                "  \"schemaVersion\": 2,",
                "  \"configSchemaVersion\": 2,",
                "  \"detectedMods\": [\"TimberPipes\"],",
                "  \"enabledAdapters\": [\"HDBF.Adapter.TimberPipes\"],",
                "  \"failedAdapters\": [],",
                "  \"detectionSource\": \"AssemblyScan\",",
                "  \"detectionDurationMs\": 120,",
                "  \"harmonyPatchCount\": 0,",
                "  \"performanceMode\": \"Reduced\",",
                "  \"lastError\": \"boom\"",
                "}",
            });

            TestAssert.Equal(expected, report.ToJson());
        }

        public static void TestReportJsonIsStableAndValidJsonShape()
        {
            var report = new CompatReport
            {
                DetectedMods = new[] { "A", "B" },
                EnabledAdapters = new[] { "HDBF.Adapter.A" },
            };

            var first = report.ToJson();
            var second = report.ToJson();

            TestAssert.Equal(first, second);
            TestAssert.True(first.StartsWith("{", StringComparison.Ordinal));
            TestAssert.True(first.EndsWith("}", StringComparison.Ordinal));
            TestAssert.True(first.Contains("\"detectedMods\": [\"A\", \"B\"]"), first);
        }

        public static void TestReportEscapesStringsAndControlCharacters()
        {
            var report = new CompatReport
            {
                LastError = "a\"b\\c\nd\te",
                DetectionSource = "ModList",
            };

            var json = report.ToJson();

            TestAssert.True(json.Contains("\"a\\\"b\\\\c\\nd\\te\""),
                "escaped lastError not found in: " + json);
            TestAssert.False(json.Contains("a\"b"), "raw quote must not survive escaping");
        }

        public static void TestReportEscapesNonPrintableCharacters()
        {
            var report = new CompatReport { LastError = "bell:\u0007" };
            TestAssert.True(report.ToJson().Contains("\\u0007"), report.ToJson());
        }

        public static void TestReportHandlesNullArraysAndNullStrings()
        {
            var report = new CompatReport
            {
                DetectedMods = null!,
                EnabledAdapters = null!,
                FailedAdapters = null!,
                ModVersion = null!,
                LastError = null,
            };

            var json = report.ToJson();

            TestAssert.True(json.Contains("\"detectedMods\": []"), json);
            TestAssert.True(json.Contains("\"enabledAdapters\": []"), json);
            TestAssert.True(json.Contains("\"failedAdapters\": []"), json);
            TestAssert.True(json.Contains("\"modVersion\": null"), json);
            TestAssert.True(json.Contains("\"lastError\": null"), json);
        }

        public static void TestReportUsesAdapterIdsNotAssemblyNames()
        {
            var report = new CompatReport
            {
                EnabledAdapters = new[] { "HDBF.Adapter.PowerLines" },
                FailedAdapters = new[] { "HDBF.Adapter.ConveyorBeltsV2" },
            };
            var json = report.ToJson();

            TestAssert.True(json.Contains("HDBF.Adapter.PowerLines"), json);
            TestAssert.True(json.Contains("HDBF.Adapter.ConveyorBeltsV2"), json);
            TestAssert.False(json.Contains("System.Reflection"), "report must not leak type names");
        }
    }
}
