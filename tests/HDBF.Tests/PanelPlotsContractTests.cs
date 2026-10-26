using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using HDBF.Core;
using HDBF.Core.Abstractions;
using HDBF.Core.Balance;
using HDBF.Core.Formatting;
using HDBF.UI.Localization;

namespace HDBF.Tests
{
    /// <summary>
    /// L1 contract tests for the panel-plot card (chapter 11.3 risk note: the panel must show
    /// 已注册格 / 有效格 / 可开垦格 side by side; chapter 14.1: work-range area is detail-only;
    /// chapter 14.4: key naming and label length; chapter 14.5: number formatting).
    ///
    /// The real panel factory (<c>HdbfFarmPanelDataFactory</c>) references Timberborn.Localization
    /// and cannot load outside the game (REQ-TEST-03), so the card moved every testable number rule
    /// into <see cref="PanelNumbers"/> / <see cref="PanelPlotFigures"/> (HDBF.Core, no game
    /// reference). This class asserts that production source, the UI's compile-time key constants,
    /// and the shipped CSVs (embedded as resources) all agree.
    ///
    /// Deliberately no game assembly: this is data/display evidence, not in-game verification.
    /// </summary>
    public static class PanelPlotsContractTests
    {
        private const string RegisteredKey = "HDBF.Panel.RegisteredPlots";
        private const string TillableKey = "HDBF.Panel.TillablePlots";
        private const string TillableTooltipKey = "HDBF.Tooltip.TillablePlots";

        // ------------------------------------------------------- snapshot data layer

        public static void TestSnapshotTillableDefaultsToUnknown()
        {
            // A fresh snapshot (and HdbfFarmQueryService.EmptySnapshot) must say "unknown", not 0,
            // until a scan has completed: 0 is a real, observable scan result (chapter 14.1).
            var snapshot = new BoxFarmSnapshot();
            TestAssert.Equal(-1, BoxFarmSnapshot.UnknownTillablePlots);
            TestAssert.Equal(BoxFarmSnapshot.UnknownTillablePlots, snapshot.TillablePlots);
            TestAssert.Equal(0, snapshot.RegisteredPlots);
            TestAssert.Equal(0, snapshot.EffectivePlots);
        }

        public static void TestSnapshotCarriesTheScanCandidateCount()
        {
            var snapshot = BasicSnapshot(registered: 60, effective: 60, workers: 2, tillable: 121);
            TestAssert.Equal(121, snapshot.TillablePlots);
            TestAssert.Equal(60, snapshot.RegisteredPlots, "registered semantics unchanged by this card");
            TestAssert.Equal(60, snapshot.EffectivePlots, "effective semantics unchanged by this card");
        }

        // ------------------------------------------------------- the three panel figures

        public static void TestRegisteredZeroIsShownAsZeroNotCapacity()
        {
            // The P0 symptom: an empty registry used to be masked by the capacity-derived
            // "有效托管格 60/60". Registered must now be its own figure: 0/60.
            var snapshot = BasicSnapshot(registered: 0, effective: 60, workers: 2, tillable: 121);
            var figures = PanelPlotFigures.FromSnapshot(snapshot, TierCatalog.Get(HdbfTier.Basic));

            TestAssert.Equal(0, figures.RegisteredPlots);
            TestAssert.Equal(60, figures.PlotCapacity);
            TestAssert.Equal(60, figures.EffectivePlots);
            TestAssert.Equal("0/60", figures.RegisteredRatio);
            TestAssert.Equal(60, figures.EffectivePlots);
            TestAssert.Equal("0/60", PanelNumbers.PlotRatio(figures.RegisteredPlots, figures.PlotCapacity));
        }

        public static void TestRegisteredRatioUsesRegisteredFirstAndCapacitySecond()
        {
            var snapshot = BasicSnapshot(registered: 3, effective: 76, workers: 2, tillable: 121);
            var figures = PanelPlotFigures.FromSnapshot(snapshot, TierCatalog.Get(HdbfTier.Basic));
            TestAssert.Equal("3/60", figures.RegisteredRatio);
            TestAssert.Equal(76, figures.EffectivePlots, "effective figures stay worker-derived");
        }

        public static void TestTillableUnknownRendersTheDash()
        {
            var snapshot = BasicSnapshot(registered: 0, effective: 0, workers: 0,
                tillable: BoxFarmSnapshot.UnknownTillablePlots);
            var figures = PanelPlotFigures.FromSnapshot(snapshot, TierCatalog.Get(HdbfTier.Basic));

            TestAssert.False(figures.IsTillableKnown);
            TestAssert.Equal(PanelNumbers.Unknown, figures.TillableText);
            TestAssert.Equal("—", figures.TillableText);
        }

        public static void TestTillableKnownRendersItsNumber()
        {
            var known = PanelPlotFigures.FromSnapshot(
                BasicSnapshot(registered: 60, effective: 60, workers: 2, tillable: 121),
                TierCatalog.Get(HdbfTier.Basic));
            TestAssert.True(known.IsTillableKnown);
            TestAssert.Equal("121", known.TillableText);

            // A completed scan that found no usable cell is a real 0, not "unknown".
            var zero = PanelPlotFigures.FromSnapshot(
                BasicSnapshot(registered: 0, effective: 0, workers: 0, tillable: 0),
                TierCatalog.Get(HdbfTier.Basic));
            TestAssert.True(zero.IsTillableKnown);
            TestAssert.Equal("0", zero.TillableText);
        }

        public static void TestWorkRangeAreasMatchChapter113()
        {
            // 11×11=121, 15×15=225, 19×19=361, 27×27=729 (chapter 11.3), detail/tooltip only.
            var expected = new Dictionary<HdbfTier, int>
            {
                { HdbfTier.Basic, 121 },
                { HdbfTier.Intermediate, 225 },
                { HdbfTier.Advanced, 361 },
                { HdbfTier.Top, 729 },
            };

            foreach (var pair in expected)
            {
                var profile = TierCatalog.Get(pair.Key);
                var figures = PanelPlotFigures.FromSnapshot(
                    BasicSnapshot(0, 0, 0, 0, pair.Key), profile);
                TestAssert.Equal(pair.Value, figures.WorkRangeCells, "work-range area " + pair.Key);
                TestAssert.Equal(profile.WorkRange, figures.WorkRange, "work-range side " + pair.Key);
            }
        }

        public static void TestWorkRangeAreaOnlyAppearsInTheTooltipKey()
        {
            // Chapter 14.1: the main panel shows only the count; the area is a detail figure. The
            // panel template therefore has one placeholder, the tooltip carries the area as {2}.
            var en = LoadTexts("enUS");
            var zh = LoadTexts("zhCN");

            foreach (var key in new[] { RegisteredKey, TillableKey })
            {
                TestAssert.True(Placeholders(en[key]).Count <= 2, "panel key has no area placeholder: " + key);
                TestAssert.True(Placeholders(zh[key]).Count <= 2, "panel key has no area placeholder: " + key);
            }

            var placeholders = Placeholders(en[TillableTooltipKey]);
            TestAssert.Equal(3, placeholders.Count, "tillable tooltip carries side × side + area");
            TestAssert.Equal(3, Placeholders(zh[TillableTooltipKey]).Count, "zh tooltip placeholders match");
        }

        // ------------------------------------------------------- number formats (chapter 14.5)

        public static void TestPercentFormattingUsesZeroDecimalsAndConservativeRounding()
        {
            TestAssert.Equal("73", PanelNumbers.Percent(0.734f, roundUp: false), "irrigation coverage floors");
            TestAssert.Equal("74", PanelNumbers.Percent(0.734f, roundUp: true), "fatigue ceils");
            TestAssert.Equal("100", PanelNumbers.Percent(1f, roundUp: false));
            TestAssert.Equal("0", PanelNumbers.Percent(0f, roundUp: false));
            TestAssert.Equal("100", PanelNumbers.Percent(1.7f, roundUp: false), "clamped to 100");
            TestAssert.Equal("0", PanelNumbers.Percent(-1f, roundUp: true), "clamped to 0");
            TestAssert.Equal("—", PanelNumbers.Percent(float.NaN, roundUp: false));
            TestAssert.Equal("—", PanelNumbers.Percent(float.PositiveInfinity, roundUp: true));
        }

        public static void TestDaysFormattingUsesOneDecimal()
        {
            TestAssert.Equal("3.4", PanelNumbers.Days(3.46f, roundUp: false), "tank days floor");
            TestAssert.Equal("3.5", PanelNumbers.Days(3.41f, roundUp: true));
            TestAssert.Equal("3.5", PanelNumbers.DaysRounded(3.46f), "maintenance countdown rounds");
            TestAssert.Equal("0.0", PanelNumbers.Days(0f, roundUp: false));
            TestAssert.Equal("—", PanelNumbers.Days(float.NaN, roundUp: false));
            TestAssert.Equal("—", PanelNumbers.DaysRounded(float.PositiveInfinity));
        }

        public static void TestMultiplierFormattingUsesTwoDecimals()
        {
            TestAssert.Equal("1.35", PanelNumbers.Multiplier(1.35f));
            TestAssert.Equal("0.60", PanelNumbers.Multiplier(0.6f));
            TestAssert.Equal("1.00", PanelNumbers.Multiplier(1f));
            TestAssert.Equal("—", PanelNumbers.Multiplier(float.NaN));
        }

        public static void TestPlotCountsAreInvariantIntegersWithoutGroupSeparators()
        {
            // Chapter 14.5: no trailing zeros, no group separators, invariant culture.
            TestAssert.Equal("0", PanelNumbers.PlotCount(0));
            TestAssert.Equal("600", PanelNumbers.PlotCount(600));
            TestAssert.Equal("60/600", PanelNumbers.PlotRatio(60, 600));
            TestAssert.False(PanelNumbers.PlotRatio(60, 600).Contains(",", StringComparison.Ordinal),
                "no group separator in plot ratios");
        }

        public static void TestUnknownIsOnlyTheNegativeSentinel()
        {
            TestAssert.Equal("—", PanelNumbers.PlotCountOrUnknown(-1));
            TestAssert.Equal("—", PanelNumbers.PlotCountOrUnknown(int.MinValue));
            TestAssert.Equal("0", PanelNumbers.PlotCountOrUnknown(0), "a known zero is not unknown");
            TestAssert.Equal("601", PanelNumbers.PlotCountOrUnknown(601));
        }

        public static void TestRoundedCountIsSafeForWarningArguments()
        {
            TestAssert.Equal(75, PanelNumbers.RoundedCount(74.5f), "rounds half away from zero");
            TestAssert.Equal(0, PanelNumbers.RoundedCount(float.NaN));
            TestAssert.Equal(0, PanelNumbers.RoundedCount(float.PositiveInfinity));
        }

        // ------------------------------------------------------- localization data contract

        public static void TestNewKeysExistInAllThreeLanguagesWithIdenticalSets()
        {
            var en = LoadTexts("enUS");
            var zh = LoadTexts("zhCN");
            var tw = LoadTexts("zhTW");

            foreach (var key in new[] { RegisteredKey, TillableKey, TillableTooltipKey })
            {
                TestAssert.True(en.ContainsKey(key), "enUS is missing " + key);
                TestAssert.True(zh.ContainsKey(key), "zhCN is missing " + key);
                TestAssert.True(tw.ContainsKey(key), "zhTW is missing " + key);
            }

            // All three files must carry exactly the same key set (chapter 14.4: 中英键集合必须一致).
            AssertSameKeySet(en, zh, "enUS vs zhCN");
            AssertSameKeySet(en, tw, "enUS vs zhTW");
        }

        public static void TestNewKeysKeepPlaceholdersIdenticalAcrossLanguages()
        {
            var en = LoadTexts("enUS");
            var zh = LoadTexts("zhCN");
            var tw = LoadTexts("zhTW");

            foreach (var key in new[] { RegisteredKey, TillableKey, TillableTooltipKey })
            {
                AssertSamePlaceholders(en[key], zh[key], "enUS/zhCN " + key);
                AssertSamePlaceholders(en[key], tw[key], "enUS/zhTW " + key);
            }
        }

        public static void TestPanelLabelLengthLimitsAreRespected()
        {
            // Chapter 14.4: Chinese labels ≤12 characters, English labels ≤24 characters.
            var en = LoadTexts("enUS");
            var zh = LoadTexts("zhCN");
            var tw = LoadTexts("zhTW");

            AssertLabelLength(en[RegisteredKey], 24, "enUS " + RegisteredKey);
            AssertLabelLength(zh[RegisteredKey], 12, "zhCN " + RegisteredKey);
            AssertLabelLength(tw[RegisteredKey], 12, "zhTW " + RegisteredKey);

            AssertLabelLength(en[TillableKey], 24, "enUS " + TillableKey);
            AssertLabelLength(zh[TillableKey], 12, "zhCN " + TillableKey);
            AssertLabelLength(tw[TillableKey], 12, "zhTW " + TillableKey);
        }

        public static void TestPanelTemplatesRenderTheThreeFigures()
        {
            // End-to-end offline proof of the displayed text: production figures + shipped template.
            var en = LoadTexts("enUS");
            var zh = LoadTexts("zhCN");
            var figures = PanelPlotFigures.FromSnapshot(
                BasicSnapshot(registered: 0, effective: 60, workers: 2,
                    tillable: BoxFarmSnapshot.UnknownTillablePlots),
                TierCatalog.Get(HdbfTier.Basic));

            TestAssert.Equal("Registered Plots 0/60", string.Format(CultureInfo.InvariantCulture,
                en[RegisteredKey], figures.RegisteredPlots, figures.PlotCapacity));
            TestAssert.Equal("已注册格 0/60", string.Format(CultureInfo.InvariantCulture,
                zh[RegisteredKey], figures.RegisteredPlots, figures.PlotCapacity));
            TestAssert.Equal("Tillable Plots —", string.Format(CultureInfo.InvariantCulture,
                en[TillableKey], figures.TillableText));
            TestAssert.Equal("可开垦格 —", string.Format(CultureInfo.InvariantCulture,
                zh[TillableKey], figures.TillableText));

            var known = PanelPlotFigures.FromSnapshot(
                BasicSnapshot(registered: 60, effective: 60, workers: 2, tillable: 121),
                TierCatalog.Get(HdbfTier.Basic));
            TestAssert.Equal("Tillable Plots 121", string.Format(CultureInfo.InvariantCulture,
                en[TillableKey], known.TillableText));
            TestAssert.Equal("可开垦格 121", string.Format(CultureInfo.InvariantCulture,
                zh[TillableKey], known.TillableText));
        }

        public static void TestUiKeyConstantsMatchTheShippedKeys()
        {
            // HdbfUiLocKeys.cs is compiled into this test assembly (pure constants). A renamed or
            // mistyped constant would otherwise fail only in a running game as a raw key.
            TestAssert.Equal(RegisteredKey, HdbfUiLocKeys.PanelRegisteredPlots);
            TestAssert.Equal(TillableKey, HdbfUiLocKeys.PanelTillablePlots);
            TestAssert.Equal(TillableTooltipKey, HdbfUiLocKeys.TooltipTillablePlots);

            var en = LoadTexts("enUS");
            TestAssert.True(en.ContainsKey(HdbfUiLocKeys.PanelRegisteredPlots), "UI constant not in CSV");
            TestAssert.True(en.ContainsKey(HdbfUiLocKeys.PanelTillablePlots), "UI constant not in CSV");
            TestAssert.True(en.ContainsKey(HdbfUiLocKeys.TooltipTillablePlots), "UI constant not in CSV");
        }

        public static void TestRegisteredAndEffectiveKeysStayUnchanged()
        {
            // This card must not change the existing fields' meaning: the effective row keeps its
            // established key and the registered row is new. (No silent renaming of 14.4 keys.)
            TestAssert.Equal("HDBF.Panel.EffectivePlots", HdbfUiLocKeys.PanelEffectivePlots);
            TestAssert.Equal("HDBF.Panel.Workers", HdbfUiLocKeys.PanelWorkers);
        }

        // ------------------------------------------------------- helpers

        private static BoxFarmSnapshot BasicSnapshot(int registered, int effective, int workers, int tillable,
            HdbfTier tier = HdbfTier.Basic)
        {
            return new BoxFarmSnapshot
            {
                EntityId = 1L,
                Tier = tier,
                Mode = HdbfMode.Normal,
                RegisteredPlots = registered,
                EffectivePlots = effective,
                ActiveWorkers = workers,
                MaxWorkers = TierCatalog.Get(tier).MaxWorkers,
                TillablePlots = tillable,
            };
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
            var assembly = typeof(PanelPlotsContractTests).Assembly;
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

        /// <summary>
        /// Minimal RFC-4180 CSV reader: the shipped files quote texts that contain commas
        /// (<c>HDBF.Tooltip.EffectivePlots</c>) and the validator rejects any other field count.
        /// </summary>
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
