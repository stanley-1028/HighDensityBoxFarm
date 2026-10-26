using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using HDBF.Core.Abstractions;
using HDBF.UI.Localization;

namespace HDBF.Tests
{
    /// <summary>
    /// L1 contract tests for the managed-plot overlay UI card (REQ-UI-05, chapter 14.3):
    /// the highlight button, the four-state legend and the Shift-drag box-select hint must be
    /// rendered from shipped <c>HDBF.*</c> keys, the three localization files must carry identical
    /// key sets, and the legend order / colours must stay single-sourced in Core.
    ///
    /// The real fragment and factory (<c>HdbfFarmPanelFragment</c> /
    /// <c>HdbfFarmPanelDataFactory</c>) reference Timberborn.Localization / UIElements and cannot
    /// load outside the game (REQ-TEST-03), so this class asserts the parts that are game-free:
    /// the compile-time UI key constants (linked into this assembly), the four Core palette
    /// states, the whitelisted command-failure keys, and the shipped CSVs embedded as resources.
    ///
    /// Deliberately no game assembly: this is data/display evidence, not in-game verification.
    /// </summary>
    public static class HighlightUiContractTests
    {
        private const int EnglishLabelMaxLength = 24;
        private const int ChineseLabelMaxLength = 12;

        /// <summary>The five chapter-14.4 label keys (legend header + one label per overlay state).</summary>
        private static readonly string[] LabelKeys =
        {
            HdbfUiLocKeys.PanelHighlightLegend,
            HdbfUiLocKeys.PanelHighlightIrrigated,
            HdbfUiLocKeys.PanelHighlightDry,
            HdbfUiLocKeys.PanelHighlightExcluded,
            HdbfUiLocKeys.PanelHighlightUnreachable,
        };

        /// <summary>The two description-style keys (exempt from the label length limit).</summary>
        private static readonly string[] DescriptionKeys =
        {
            HdbfUiLocKeys.TooltipHighlightPlots,
            HdbfUiLocKeys.TooltipHighlightBoxSelect,
        };

        // ------------------------------------------------------- key constants / shipped keys

        public static void TestHighlightKeyConstantsHaveTheChapter144Values()
        {
            // The validator reads these constants from src/HDBF.UI and requires them in enUS/zhCN;
            // this pins the exact key text so a rename is caught here as well as in the build.
            TestAssert.Equal("HDBF.Panel.HighlightLegend", HdbfUiLocKeys.PanelHighlightLegend);
            TestAssert.Equal("HDBF.Panel.HighlightIrrigated", HdbfUiLocKeys.PanelHighlightIrrigated);
            TestAssert.Equal("HDBF.Panel.HighlightDry", HdbfUiLocKeys.PanelHighlightDry);
            TestAssert.Equal("HDBF.Panel.HighlightExcluded", HdbfUiLocKeys.PanelHighlightExcluded);
            TestAssert.Equal("HDBF.Panel.HighlightUnreachable", HdbfUiLocKeys.PanelHighlightUnreachable);
            TestAssert.Equal("HDBF.Tooltip.HighlightPlots", HdbfUiLocKeys.TooltipHighlightPlots);
            TestAssert.Equal("HDBF.Tooltip.HighlightBoxSelect", HdbfUiLocKeys.TooltipHighlightBoxSelect);

            // The button label was already shipped for this feature; the card only wires it up.
            TestAssert.Equal("HDBF.Action.HighlightPlots", HdbfUiLocKeys.ActionHighlightPlots);
        }

        public static void TestHighlightKeysExistInAllThreeLanguages()
        {
            var en = LoadTexts("enUS");
            var zh = LoadTexts("zhCN");
            var tw = LoadTexts("zhTW");

            foreach (var key in HighlightKeys())
            {
                TestAssert.True(en.ContainsKey(key), "enUS is missing " + key);
                TestAssert.True(zh.ContainsKey(key), "zhCN is missing " + key);
                TestAssert.True(tw.ContainsKey(key), "zhTW is missing " + key);
            }
        }

        public static void TestHighlightKeySetsAreIdenticalAcrossLanguages()
        {
            // Chapter 14.4 / REQ-UI-09: the three shipped files must carry exactly the same keys.
            var en = LoadTexts("enUS");
            var zh = LoadTexts("zhCN");
            var tw = LoadTexts("zhTW");

            AssertSameKeySet(en, zh, "enUS vs zhCN");
            AssertSameKeySet(en, tw, "enUS vs zhTW");
        }

        public static void TestHighlightTextsMatchTheContractLiterals()
        {
            // The legend text is visible in the panel, so it is pinned verbatim: English labels stay
            // short, Chinese/Traditional Chinese labels stay within the chapter-14.4 width budget.
            var en = LoadTexts("enUS");
            var zh = LoadTexts("zhCN");
            var tw = LoadTexts("zhTW");

            TestAssert.Equal("Managed Plots", en[HdbfUiLocKeys.PanelHighlightLegend]);
            TestAssert.Equal("Irrigated", en[HdbfUiLocKeys.PanelHighlightIrrigated]);
            TestAssert.Equal("Not Irrigated", en[HdbfUiLocKeys.PanelHighlightDry]);
            TestAssert.Equal("Excluded", en[HdbfUiLocKeys.PanelHighlightExcluded]);
            TestAssert.Equal("Unreachable", en[HdbfUiLocKeys.PanelHighlightUnreachable]);
            TestAssert.Equal("Click plots to exclude or restore them; Shift+drag to box-select; Esc closes the overlay.",
                en[HdbfUiLocKeys.TooltipHighlightPlots]);
            TestAssert.Equal("Shift+drag to exclude or restore several plots at once.",
                en[HdbfUiLocKeys.TooltipHighlightBoxSelect]);

            TestAssert.Equal("托管格图例", zh[HdbfUiLocKeys.PanelHighlightLegend]);
            TestAssert.Equal("已灌溉", zh[HdbfUiLocKeys.PanelHighlightIrrigated]);
            TestAssert.Equal("未灌溉", zh[HdbfUiLocKeys.PanelHighlightDry]);
            TestAssert.Equal("已排除", zh[HdbfUiLocKeys.PanelHighlightExcluded]);
            TestAssert.Equal("不可达", zh[HdbfUiLocKeys.PanelHighlightUnreachable]);

            TestAssert.Equal("託管格圖例", tw[HdbfUiLocKeys.PanelHighlightLegend]);
            TestAssert.Equal("已灌溉", tw[HdbfUiLocKeys.PanelHighlightIrrigated]);
            TestAssert.Equal("未灌溉", tw[HdbfUiLocKeys.PanelHighlightDry]);
            TestAssert.Equal("已排除", tw[HdbfUiLocKeys.PanelHighlightExcluded]);
            TestAssert.Equal("不可達", tw[HdbfUiLocKeys.PanelHighlightUnreachable]);
        }

        public static void TestHighlightLabelsRespectTheChapter144Lengths()
        {
            // Chapter 14.4: English labels ≤24 characters, Chinese labels ≤12 characters.
            var en = LoadTexts("enUS");
            var zh = LoadTexts("zhCN");
            var tw = LoadTexts("zhTW");

            foreach (var key in LabelKeys)
            {
                AssertLabelLength(en[key], EnglishLabelMaxLength, "enUS " + key);
                AssertLabelLength(zh[key], ChineseLabelMaxLength, "zhCN " + key);
                AssertLabelLength(tw[key], ChineseLabelMaxLength, "zhTW " + key);
            }
        }

        public static void TestHighlightTextsCarryNoPlaceholders()
        {
            // These are fixed sentences: the panel formats them with no arguments, and chapter 14.4
            // requires placeholders to match across languages (the validator enforces that rule).
            var en = LoadTexts("enUS");
            var zh = LoadTexts("zhCN");
            var tw = LoadTexts("zhTW");

            foreach (var key in HighlightKeys())
            {
                TestAssert.Equal(0, Placeholders(en[key]).Count, "enUS " + key + " must not have placeholders");
                TestAssert.Equal(0, Placeholders(zh[key]).Count, "zhCN " + key + " must not have placeholders");
                TestAssert.Equal(0, Placeholders(tw[key]).Count, "zhTW " + key + " must not have placeholders");
                AssertSamePlaceholders(en[key], zh[key], "enUS/zhCN " + key);
                AssertSamePlaceholders(en[key], tw[key], "enUS/zhTW " + key);
            }
        }

        // ------------------------------------------------------- Core palette / legend order

        public static void TestLegendKeepsTheFourChapter143StatesInOrder()
        {
            // The panel legend is built from Core's LegendOrder, so this order is what players see:
            // irrigated (green), dry (yellow), excluded (gray), unreachable (red).
            TestAssert.Equal(4, HdbfPlotHighlightPalette.LegendOrder.Count, "one legend row per state");
            TestAssert.Equal(HdbfPlotHighlightState.Irrigated, HdbfPlotHighlightPalette.LegendOrder[0]);
            TestAssert.Equal(HdbfPlotHighlightState.Dry, HdbfPlotHighlightPalette.LegendOrder[1]);
            TestAssert.Equal(HdbfPlotHighlightState.Excluded, HdbfPlotHighlightPalette.LegendOrder[2]);
            TestAssert.Equal(HdbfPlotHighlightState.Unreachable, HdbfPlotHighlightPalette.LegendOrder[3]);

            // Four distinct colours keep the overlay/legend unambiguous even before reading the text.
            var colours = new HashSet<uint>();
            foreach (var state in HdbfPlotHighlightPalette.LegendOrder)
            {
                colours.Add(HdbfPlotHighlightPalette.Rgba(state));
            }

            TestAssert.Equal(4, colours.Count, "the four overlay states must not share a colour");
        }

        public static void TestLegendColoursComeFromTheSharedPalette()
        {
            // HdbfFarmPanelDataFactory.BuildHighlightLegend copies Rgba(state); pinning the palette
            // values here means the legend and the world overlay can never disagree silently.
            TestAssert.Equal(0x4CAF50A6u, HdbfPlotHighlightPalette.Rgba(HdbfPlotHighlightState.Irrigated));
            TestAssert.Equal(0xFFC107A6u, HdbfPlotHighlightPalette.Rgba(HdbfPlotHighlightState.Dry));
            TestAssert.Equal(0x9E9E9EA6u, HdbfPlotHighlightPalette.Rgba(HdbfPlotHighlightState.Excluded));
            TestAssert.Equal(0xF44336A6u, HdbfPlotHighlightPalette.Rgba(HdbfPlotHighlightState.Unreachable));
        }

        // ------------------------------------------------------- world-action failure line

        public static void TestWorldActionFailureKeysAreWhitelistedAndShipped()
        {
            // The snapshot's PlotActionFailureKey is rendered through FormatCommandFailure, whose
            // whitelist covers PlotMinimum / PlotNotRegistered (the command constants). Both must be
            // shipped keys in all three languages, otherwise a raw key could reach the player.
            var en = LoadTexts("enUS");
            var zh = LoadTexts("zhCN");
            var tw = LoadTexts("zhTW");

            foreach (var key in new[] { HdbfCommandFailures.PlotMinimum, HdbfCommandFailures.PlotNotRegistered })
            {
                TestAssert.True(en.ContainsKey(key), "enUS is missing " + key);
                TestAssert.True(zh.ContainsKey(key), "zhCN is missing " + key);
                TestAssert.True(tw.ContainsKey(key), "zhTW is missing " + key);
            }

            TestAssert.Equal("HDBF.Error.PlotMinimum", HdbfCommandFailures.PlotMinimum);
            TestAssert.Equal("HDBF.Error.PlotNotRegistered", HdbfCommandFailures.PlotNotRegistered);
        }

        // ------------------------------------------------------- helpers

        private static List<string> HighlightKeys()
        {
            var keys = new List<string>(LabelKeys.Length + DescriptionKeys.Length + 1)
            {
                HdbfUiLocKeys.ActionHighlightPlots,
            };
            keys.AddRange(LabelKeys);
            keys.AddRange(DescriptionKeys);
            return keys;
        }

        private static void AssertLabelLength(string text, int maximum, string label)
        {
            TestAssert.True(text.Length <= maximum,
                label + ": expected ≤" + maximum + " characters, got " + text.Length + " ('" + text + "')");
        }

        private static void AssertSameKeySet(Dictionary<string, string> left, Dictionary<string, string> right,
            string label)
        {
            TestAssert.Equal(left.Count, right.Count, label + " key count");
            foreach (var key in left.Keys)
            {
                TestAssert.True(right.ContainsKey(key), label + " is missing key " + key);
            }
        }

        private static void AssertSamePlaceholders(string left, string right, string label)
        {
            var leftPlaceholders = Placeholders(left);
            var rightPlaceholders = Placeholders(right);
            TestAssert.Equal(leftPlaceholders.Count, rightPlaceholders.Count, label + " placeholder count");
            for (var i = 0; i < leftPlaceholders.Count; i++)
            {
                TestAssert.Equal(leftPlaceholders[i], rightPlaceholders[i], label + " placeholder order");
            }
        }

        /// <summary>Positional placeholders in order, e.g. "x {0} y {2}" → [0, 2].</summary>
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

            return result;
        }

        /// <summary>Loads one shipped CSV from the embedded test resource (ID,Text,Comment).</summary>
        private static Dictionary<string, string> LoadTexts(string language)
        {
            var resource = "HDBF.Tests.Localizations." + language + ".csv";
            var assembly = typeof(HighlightUiContractTests).Assembly;
            using var stream = assembly.GetManifestResourceStream(resource);
            if (stream is null)
            {
                throw new TestFailureException("missing embedded localization resource " + resource);
            }

            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var rows = ParseCsv(reader.ReadToEnd());
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
                    throw new TestFailureException(resource + " line " + (i + 1) + " has " + row.Length
                        + " fields; the game requires exactly 3");
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
