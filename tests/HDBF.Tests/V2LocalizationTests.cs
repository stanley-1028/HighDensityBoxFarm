// ============================================================================
// HDBF v2 | Layer: L1 test | Owner: V2-UI-01
// Purpose: Localization contract of the v2 panel: identical enUS/zhCN/zhTW key sets, every UI
//          key constant shipped, the complete v2 terminology set present, every v1 plot term
//          removed, matching placeholders and no empty/raw-key text.
// Requirements: REQ-V2-UI-03/04, REQ-V2-READ-04, REQ-V2-READ-03, REQ-V2-TEST-01
// ============================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using HDBF.UI.Localization;

namespace HDBF.Tests
{
    /// <summary>
    /// L1 contract of the three shipped language files and of
    /// <see cref="HdbfUiLocKeys"/> (linked into this assembly as production source). The CSVs are
    /// embedded resources, so the test checks exactly the data that ships with the mod.
    /// </summary>
    public static class V2LocalizationTests
    {
        private const int EnglishLabelMaxLength = 24;
        private const int ChineseLabelMaxLength = 12;

        private static readonly string[] Languages = { "enUS", "zhCN", "zhTW" };

        /// <summary>Categories accepted by the release validator (docs/REQ_V2_SECTION_19.md).</summary>
        private static readonly string[] Categories =
        {
            "Building", "Panel", "Mode", "Port", "Action", "Warning", "Compat", "Error", "Tooltip",
        };

        /// <summary>v1 keys that must no longer exist anywhere (REQ-V2-UI-04).</summary>
        private static readonly string[] RemovedV1Keys =
        {
            "HDBF.Panel.RegisteredPlots", "HDBF.Panel.EffectivePlots", "HDBF.Panel.TillablePlots",
            "HDBF.Panel.WorkRange", "HDBF.Panel.HighlightLegend", "HDBF.Panel.HighlightIrrigated",
            "HDBF.Panel.HighlightDry", "HDBF.Panel.HighlightExcluded",
            "HDBF.Panel.HighlightUnreachable", "HDBF.Action.HighlightPlots",
            "HDBF.Tooltip.HighlightPlots", "HDBF.Tooltip.HighlightBoxSelect",
            "HDBF.Tooltip.TillablePlots", "HDBF.Panel.Irrigation", "HDBF.Panel.IrrigationThreshold",
            "HDBF.Panel.Fatigue", "HDBF.Panel.FatigueMax", "HDBF.Warning.FatigueHigh",
            "HDBF.Warning.AdapterDisabled", "HDBF.Warning.TankLow", "HDBF.Error.PlotMinimum",
            "HDBF.Error.PlotNotRegistered", "HDBF.Port.PowerManaged", "HDBF.Action.ConfirmDense",
            "HDBF.Action.DenseConfirmHint",
        };

        /// <summary>v1 terminology that must not appear in any shipped user-visible text.</summary>
        private static readonly string[] RemovedChineseTerms =
        {
            "工作范围", "工作範圍", "托管格", "託管格", "开垦", "開墾", "可开垦", "可開墾",
            "地块", "地塊", "高亮", "框选", "框選", "已注册", "已註冊", "已排除",
        };

        /// <summary>English v1 terminology that must not appear in any shipped text (case-insensitive).</summary>
        private static readonly string[] RemovedEnglishTerms =
        {
            "plot", "work range", "tillable", "managed plot", "highlight", "box-select", "rescan",
            "registered plot",
        };

        // ------------------------------------------------------------------ CSV set contract

        public static void TestThreeLanguageFilesCarryIdenticalKeySets()
        {
            var texts = LoadAll();
            var english = texts["enUS"];
            TestAssert.True(english.Count >= 120, "the v2 panel ships a substantial key set: " + english.Count);
            foreach (var language in Languages)
            {
                var target = texts[language];
                TestAssert.Equal(english.Count, target.Count, language + " key count");
                foreach (var key in english.Keys)
                {
                    TestAssert.True(target.ContainsKey(key), language + " is missing key " + key);
                }

                foreach (var key in target.Keys)
                {
                    TestAssert.True(english.ContainsKey(key), language + " has a key absent from enUS: " + key);
                }
            }
        }

        public static void TestNoEmptyTextsOrRawKeys()
        {
            foreach (var language in Languages)
            {
                foreach (var pair in LoadTexts(language))
                {
                    TestAssert.True(pair.Value.Trim().Length > 0,
                        language + " key '" + pair.Key + "' must have non-empty text");
                    TestAssert.False(pair.Value.Contains("HDBF.", StringComparison.Ordinal),
                        language + " key '" + pair.Key + "' leaks a raw localization key as text");
                }
            }
        }

        public static void TestEveryKeyHasTheValidatedShape()
        {
            foreach (var key in LoadTexts("enUS").Keys)
            {
                TestAssert.True(key.StartsWith("HDBF.", StringComparison.Ordinal),
                    "key must start with HDBF.: " + key);
                var parts = key.Split('.');
                TestAssert.True(parts.Length >= 3, "key must be HDBF.<Category>.<Name>: " + key);
                TestAssert.True(Categories.Contains(parts[1], StringComparer.Ordinal),
                    "unsupported key category in " + key);
                for (var index = 2; index < parts.Length; index++)
                {
                    TestAssert.True(parts[index].Length > 0, "empty key segment in " + key);
                    foreach (var character in parts[index])
                    {
                        TestAssert.True(char.IsAsciiLetterOrDigit(character),
                            "key segments must be ASCII PascalCase: " + key);
                    }
                }
            }
        }

        public static void TestPlaceholdersMatchAcrossLanguages()
        {
            var english = LoadTexts("enUS");
            var chinese = LoadTexts("zhCN");
            var traditional = LoadTexts("zhTW");
            foreach (var pair in english)
            {
                var expected = Placeholders(pair.Value);
                AssertSamePlaceholders(expected, Placeholders(chinese[pair.Key]), "enUS/zhCN " + pair.Key);
                AssertSamePlaceholders(expected, Placeholders(traditional[pair.Key]), "enUS/zhTW " + pair.Key);
            }
        }

        // ------------------------------------------------------------------ key constants

        public static void TestEveryUiKeyConstantIsShippedInAllThreeLanguages()
        {
            var texts = LoadAll();
            var constants = typeof(HdbfUiLocKeys)
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(field => field.IsLiteral && field.FieldType == typeof(string))
                .Select(field => (string)field.GetRawConstantValue()!)
                .ToArray();
            TestAssert.True(constants.Length >= 120, "the panel constant table is not empty: " + constants.Length);
            foreach (var key in constants)
            {
                foreach (var language in Languages)
                {
                    TestAssert.True(texts[language].ContainsKey(key),
                        language + " is missing the UI key constant " + key);
                }
            }
        }

        public static void TestV2TerminologySetIsComplete()
        {
            var en = LoadTexts("enUS");
            var required = new[]
            {
                HdbfUiLocKeys.PanelSlots, HdbfUiLocKeys.PanelEffectiveSlots,
                HdbfUiLocKeys.PanelEmptySlots, HdbfUiLocKeys.PanelGrowingSlots,
                HdbfUiLocKeys.PanelMatureSlots, HdbfUiLocKeys.PanelBacklog,
                HdbfUiLocKeys.PanelTank, HdbfUiLocKeys.PanelCoverage, HdbfUiLocKeys.PanelMinCoverage,
                HdbfUiLocKeys.PanelWaterSupply, HdbfUiLocKeys.PanelWaterShortage,
                HdbfUiLocKeys.ActionRefill, HdbfUiLocKeys.ActionMaintenance,
                HdbfUiLocKeys.PanelCropPolicy, HdbfUiLocKeys.PanelRotation,
                HdbfUiLocKeys.PanelFixedCrop, HdbfUiLocKeys.PanelPolicyRotation,
                HdbfUiLocKeys.PanelPolicyFixed, HdbfUiLocKeys.PanelPower,
                HdbfUiLocKeys.PanelPowerGrid, HdbfUiLocKeys.PanelPowerOffGrid,
                HdbfUiLocKeys.PanelPowerNoPower, HdbfUiLocKeys.TooltipEffectiveSlots,
                HdbfUiLocKeys.WarningNoWater, HdbfUiLocKeys.WarningMaintenanceOverdue,
                HdbfUiLocKeys.WarningNoPower, HdbfUiLocKeys.WarningEntranceBlocked,
                HdbfUiLocKeys.PanelOutputStock, HdbfUiLocKeys.PanelOutputPort,
            };
            foreach (var key in required)
            {
                TestAssert.True(en.ContainsKey(key), "missing v2 key " + key);
            }

            // The requirement asks for these terms to be visible somewhere in the panel text.
            var joined = string.Join("\n", en.Values);
            foreach (var term in new[] { "P_eff", "C_min", "backlog", "rotation", "fixed crop" })
            {
                TestAssert.True(joined.Contains(term, StringComparison.OrdinalIgnoreCase),
                    "panel texts must show the term '" + term + "'");
            }
        }

        public static void TestV1KeysWereRemoved()
        {
            var texts = LoadAll();
            foreach (var key in RemovedV1Keys)
            {
                foreach (var language in Languages)
                {
                    TestAssert.False(texts[language].ContainsKey(key),
                        language + " still ships the removed v1 key " + key);
                }
            }
        }

        public static void TestV1TerminologyIsGoneFromShippedTexts()
        {
            foreach (var language in Languages)
            {
                foreach (var pair in LoadTexts(language))
                {
                    foreach (var term in RemovedChineseTerms)
                    {
                        TestAssert.False(pair.Value.Contains(term, StringComparison.Ordinal),
                            language + " key '" + pair.Key + "' still contains the v1 term '" + term + "'");
                    }

                    if (!string.Equals(language, "enUS", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    foreach (var term in RemovedEnglishTerms)
                    {
                        TestAssert.False(pair.Value.Contains(term, StringComparison.OrdinalIgnoreCase),
                            "enUS key '" + pair.Key + "' still contains the v1 term '" + term + "'");
                    }
                }
            }
        }

        public static void TestTheUnknownGlyphIsTheSameDashInEveryLanguage()
        {
            foreach (var language in Languages)
            {
                var texts = LoadTexts(language);
                TestAssert.Equal("—", texts[HdbfUiLocKeys.PanelUnknown]);
            }
        }

        // ------------------------------------------------------------------ terminology / lengths

        public static void TestPanelTermsMatchTheirTooltips()
        {
            var en = LoadTexts("enUS");
            var zh = LoadTexts("zhCN");
            var tw = LoadTexts("zhTW");

            AssertContains(en, HdbfUiLocKeys.PanelTank, "Tank");
            AssertContains(zh, HdbfUiLocKeys.PanelTank, "水罐");
            AssertContains(tw, HdbfUiLocKeys.PanelTank, "水罐");
            AssertContains(zh, HdbfUiLocKeys.TooltipTank, "水罐");

            AssertContains(en, HdbfUiLocKeys.PanelCoverage, "Coverage");
            AssertContains(zh, HdbfUiLocKeys.PanelCoverage, "覆盖率");
            AssertContains(tw, HdbfUiLocKeys.PanelCoverage, "覆蓋率");
            AssertContains(zh, HdbfUiLocKeys.TooltipCoverage, "覆盖率");

            foreach (var language in Languages)
            {
                AssertContains(LoadTexts(language), HdbfUiLocKeys.TooltipEffectiveSlots, "P_eff");
                AssertContains(LoadTexts(language), HdbfUiLocKeys.PanelMinCoverage, "C_min");
            }

            AssertContains(en, HdbfUiLocKeys.PanelBacklog, "backlog");
            AssertContains(zh, HdbfUiLocKeys.PanelBacklog, "积压");
            AssertContains(tw, HdbfUiLocKeys.PanelBacklog, "積壓");

            AssertContains(en, HdbfUiLocKeys.ActionMaintenance, "maintenance");
            AssertContains(zh, HdbfUiLocKeys.ActionMaintenance, "维护");

            AssertContains(en, HdbfUiLocKeys.ActionRefill, "Refill");
            AssertContains(zh, HdbfUiLocKeys.ActionRefill, "补水");
            AssertContains(tw, HdbfUiLocKeys.ActionRefill, "補水");

            AssertContains(zh, HdbfUiLocKeys.PanelCropPolicy, "作物策略");
            AssertContains(zh, HdbfUiLocKeys.PanelRotation, "轮作");
            AssertContains(tw, HdbfUiLocKeys.PanelRotation, "輪作");
            AssertContains(zh, HdbfUiLocKeys.PanelFixedCrop, "固定作物");

            AssertContains(zh, HdbfUiLocKeys.PanelPowerGrid, "接电");
            AssertContains(zh, HdbfUiLocKeys.PanelPowerOffGrid, "离网");
            AssertContains(zh, HdbfUiLocKeys.PanelPowerNoPower, "断电");
            AssertContains(tw, HdbfUiLocKeys.PanelPowerGrid, "接電");
            AssertContains(tw, HdbfUiLocKeys.PanelPowerOffGrid, "離網");
            AssertContains(tw, HdbfUiLocKeys.PanelPowerNoPower, "斷電");

            AssertContains(zh, HdbfUiLocKeys.WarningNoWater, "缺水");
            AssertContains(zh, HdbfUiLocKeys.WarningMaintenanceOverdue, "维护");
            AssertContains(zh, HdbfUiLocKeys.WarningNoPower, "断电");
            AssertContains(zh, HdbfUiLocKeys.WarningEntranceBlocked, "入口");
        }

        public static void TestSectionHeadersRespectTheChapter144Lengths()
        {
            var headerKeys = new[]
            {
                HdbfUiLocKeys.PanelSectionSlots, HdbfUiLocKeys.PanelSectionWater,
                HdbfUiLocKeys.PanelSectionMaintenance, HdbfUiLocKeys.PanelSectionPower,
                HdbfUiLocKeys.PanelSectionCrops, HdbfUiLocKeys.PanelSectionOutput,
                HdbfUiLocKeys.PanelStatus, HdbfUiLocKeys.PanelProblems,
            };
            var en = LoadTexts("enUS");
            var zh = LoadTexts("zhCN");
            var tw = LoadTexts("zhTW");
            foreach (var key in headerKeys)
            {
                TestAssert.True(en[key].Length <= EnglishLabelMaxLength,
                    "enUS header too long (" + en[key].Length + "): " + key);
                TestAssert.True(zh[key].Length <= ChineseLabelMaxLength,
                    "zhCN header too long (" + zh[key].Length + "): " + key);
                TestAssert.True(tw[key].Length <= ChineseLabelMaxLength,
                    "zhTW header too long (" + tw[key].Length + "): " + key);
            }
        }

        public static void TestWarningTextsAreIconTooltipSafe()
        {
            // The world-icon tooltip passes the warning sentence as {0}, so the four category
            // sentences themselves must not carry positional placeholders.
            var warningKeys = new[]
            {
                HdbfUiLocKeys.WarningNoWater, HdbfUiLocKeys.WarningIrrigationLow,
                HdbfUiLocKeys.WarningMaintenanceOverdue, HdbfUiLocKeys.WarningNoPower,
                HdbfUiLocKeys.WarningEntranceBlocked, HdbfUiLocKeys.WarningShutdown,
                HdbfUiLocKeys.WarningAdapterFailed,
            };
            foreach (var language in Languages)
            {
                var texts = LoadTexts(language);
                foreach (var key in warningKeys)
                {
                    TestAssert.Equal(0, Placeholders(texts[key]).Count,
                        language + " warning text must not carry placeholders: " + key);
                }
            }
        }

        // ------------------------------------------------------------------ helpers

        private static void AssertContains(Dictionary<string, string> texts, string key, string term)
        {
            TestAssert.True(texts[key].Contains(term, StringComparison.Ordinal),
                "'" + key + "' must contain the term '" + term + "' (got '" + texts[key] + "')");
        }

        private static void AssertSamePlaceholders(List<int> expected, List<int> actual, string label)
        {
            TestAssert.Equal(expected.Count, actual.Count, label + " placeholder count");
            for (var index = 0; index < expected.Count; index++)
            {
                TestAssert.Equal(expected[index], actual[index], label + " placeholder order");
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
                if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
                {
                    result.Add(number);
                }

                index = end + 1;
            }

            result.Sort();
            return result;
        }

        private static Dictionary<string, Dictionary<string, string>> LoadAll()
        {
            var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
            foreach (var language in Languages)
            {
                result[language] = LoadTexts(language);
            }

            return result;
        }

        /// <summary>Loads one shipped CSV from the embedded test resource (ID,Text,Comment).</summary>
        private static Dictionary<string, string> LoadTexts(string language)
        {
            var resource = "HDBF.Tests.Localizations." + language + ".csv";
            var assembly = typeof(V2LocalizationTests).Assembly;
            using var stream = assembly.GetManifestResourceStream(resource);
            if (stream is null)
            {
                throw new TestFailureException("missing embedded localization resource " + resource);
            }

            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var rows = ParseCsv(reader.ReadToEnd());
            TestAssert.True(rows.Count > 1, language + ".csv must contain a header and rows");
            TestAssert.Equal("ID,Text,Comment",
                string.Join(",", rows[0].Select(field => field.Trim())), language + " header");
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
                        + row.Length + " fields; the game requires exactly 3");
                }

                var id = row[0].Trim();
                if (texts.ContainsKey(id))
                {
                    throw new TestFailureException(language + ".csv has duplicate key '" + id + "'");
                }

                texts[id] = row[1];
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
                    // Normalised away.
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
