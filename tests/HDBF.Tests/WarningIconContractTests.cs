using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using HDBF.Core;
using HDBF.Core.Abstractions;
using HDBF.Core.Config;
using HDBF.Core.Warnings;
using HDBF.UI.Localization;

namespace HDBF.Tests
{
    /// <summary>
    /// L1 contract tests for the BUILD-04 card (chapter 14.2 "状态与警告", chapter 14.6
    /// "头顶图标与警告列表"): warning priority/aggregation, the P0/P1-only world-icon rule, the
    /// 40-cell culling, the ≤1 Hz blink and the 2 s wall-clock hysteresis.
    ///
    /// Everything here is game-free: the coordinator/renderer live in HDBF.Gameplay and cannot be
    /// loaded by this runner (REQ-TEST-03), so the card puts every rule it can into
    /// <c>HDBF.Core.Warnings</c> and tests that production code; the game-side coordinator only
    /// supplies the camera result and the clock.
    /// </summary>
    public static class WarningIconContractTests
    {
        private const int EnglishLabelMaxLength = 24;
        private const int ChineseLabelMaxLength = 12;

        private static readonly Guid FarmA = new Guid("00000000-0000-0000-0000-00000000000a");
        private static readonly Guid FarmB = new Guid("00000000-0000-0000-0000-00000000000b");
        private static readonly Guid FarmC = new Guid("00000000-0000-0000-0000-00000000000c");

        // ------------------------------------------------------- per-farm summary / priority

        public static void TestSummaryPicksHighestPriorityWarningNotListOrder()
        {
            var subject = Subject(FarmA, HdbfTier.Top,
                Warning(HdbfWarningKeys.IrrigationLow, FarmWarningSeverity.High),
                Warning(HdbfWarningKeys.Shutdown, FarmWarningSeverity.Critical),
                Warning(HdbfWarningKeys.FatigueHigh, FarmWarningSeverity.Medium));

            var summary = FarmWarningSummary.FromSubject(subject);

            TestAssert.Equal(HdbfWarningKeys.Shutdown, summary.HighestWarningKey, "P0 wins over P1/P2");
            TestAssert.Equal(FarmWarningSeverity.Critical, summary.HighestSeverity);
            TestAssert.Equal(1, summary.HighestSeverityCount);
            TestAssert.Equal(3, summary.TotalCount);
            TestAssert.Equal(FarmWarningIconKind.Shutdown, summary.IconKind);
            TestAssert.True(summary.HasWorldIcon, "shutdown is P0 and has an icon");
            TestAssert.True(summary.BlinkAllowed, "chapter 14.5: only P0 blinks");
        }

        public static void TestSummaryCountsEveryWarningAtTheHighestSeverity()
        {
            var subject = Subject(FarmA, HdbfTier.Advanced,
                Warning(HdbfWarningKeys.NoWater, FarmWarningSeverity.Critical),
                Warning(HdbfWarningKeys.Shutdown, FarmWarningSeverity.Critical),
                Warning(HdbfWarningKeys.NoPower, FarmWarningSeverity.High));

            var summary = FarmWarningSummary.FromSubject(subject);

            TestAssert.Equal(2, summary.HighestSeverityCount, "the '影響條數' counts P0 entries");
            TestAssert.Equal(3, summary.TotalCount);
            TestAssert.True(summary.HasWorldIcon);
        }

        public static void TestWorldIconIsOnlyForP0AndP1()
        {
            // P2 maintenance overdue / fatigue high: status bar only, no head-top icon.
            var p2 = FarmWarningSummary.FromSubject(Subject(FarmA, HdbfTier.Basic,
                Warning(HdbfWarningKeys.MaintenanceOverdue, FarmWarningSeverity.Medium)));
            TestAssert.False(p2.HasWorldIcon, "P2 has no world icon");
            TestAssert.Equal(FarmWarningIconKind.None, p2.IconKind);
            TestAssert.False(p2.BlinkAllowed);

            var p3 = FarmWarningSummary.FromSubject(Subject(FarmA, HdbfTier.Basic,
                Warning(HdbfWarningKeys.AdapterDisabled, FarmWarningSeverity.Low)));
            TestAssert.False(p3.HasWorldIcon, "P3 has no world icon");

            var p4 = FarmWarningSummary.FromSubject(Subject(FarmA, HdbfTier.Basic,
                Warning(HdbfWarningKeys.TankLow, FarmWarningSeverity.Info)));
            TestAssert.False(p4.HasWorldIcon, "P4 has no world icon");

            var p1 = FarmWarningSummary.FromSubject(Subject(FarmA, HdbfTier.Top,
                Warning(HdbfWarningKeys.NoPower, FarmWarningSeverity.High)));
            TestAssert.True(p1.HasWorldIcon, "P1 has a world icon");
            TestAssert.False(p1.BlinkAllowed, "P1 icons are static (chapter 14.5)");
        }

        public static void TestUnknownOrMalformedWarningNeverGetsAnIcon()
        {
            var unknownKey = FarmWarningSummary.FromSubject(Subject(FarmA, HdbfTier.Basic,
                Warning("HDBF.Warning.Unknown", FarmWarningSeverity.Critical)));
            TestAssert.False(unknownKey.HasWorldIcon, "unknown key must not display a raw-icon guess");

            // A malformed pairing (P2 severity carrying a P0 key) must also stay hidden.
            var malformed = FarmWarningSummary.FromSubject(Subject(FarmA, HdbfTier.Basic,
                Warning(HdbfWarningKeys.Shutdown, FarmWarningSeverity.Medium)));
            TestAssert.False(malformed.HasWorldIcon, "severity and key must both be P0/P1");
        }

        public static void TestEmptyAndNullSubjectsAreSafe()
        {
            var empty = FarmWarningSummary.FromSubject(Subject(FarmA, HdbfTier.Basic));
            TestAssert.True(empty.IsEmpty, "no warnings is empty");
            TestAssert.Equal(0, empty.TotalCount);
            TestAssert.False(empty.HasWorldIcon);

            var nullSummary = FarmWarningSummary.FromSubject(null);
            TestAssert.True(nullSummary.IsEmpty);
            TestAssert.Equal(Guid.Empty, nullSummary.EntityId);
        }

        public static void TestIconKindMappingIsSingleSourced()
        {
            TestAssert.Equal(FarmWarningIconKind.Shutdown, FarmWarningCatalog.IconKindOf(HdbfWarningKeys.Shutdown));
            TestAssert.Equal(FarmWarningIconKind.NoWater, FarmWarningCatalog.IconKindOf(HdbfWarningKeys.NoWater));
            TestAssert.Equal(FarmWarningIconKind.IrrigationLow, FarmWarningCatalog.IconKindOf(HdbfWarningKeys.IrrigationLow));
            TestAssert.Equal(FarmWarningIconKind.NoPower, FarmWarningCatalog.IconKindOf(HdbfWarningKeys.NoPower));
            TestAssert.Equal(FarmWarningIconKind.AdapterFailed, FarmWarningCatalog.IconKindOf(HdbfWarningKeys.AdapterFailed));
            TestAssert.Equal(FarmWarningIconKind.None, FarmWarningCatalog.IconKindOf(HdbfWarningKeys.FatigueHigh));
            TestAssert.Equal(FarmWarningIconKind.None, FarmWarningCatalog.IconKindOf(null));
        }

        public static void TestSubjectUsesRuntimeEntityIdForLocatableMembers()
        {
            var snapshot = new BoxFarmSnapshot
            {
                EntityId = 1234L,
                RuntimeEntityId = FarmB,
                Tier = HdbfTier.Top,
                Warnings = new[] { Warning(HdbfWarningKeys.NoPower, FarmWarningSeverity.High) },
            };

            var subject = FarmWarningSubject.FromSnapshot(snapshot);
            TestAssert.Equal(FarmB, subject.EntityId, "the locate action needs the runtime Guid");

            // Legacy snapshots without the runtime id still aggregate (not locatable).
            var legacy = FarmWarningSubject.FromSnapshot(snapshot with { RuntimeEntityId = Guid.Empty });
            TestAssert.True(legacy.EntityId != Guid.Empty, "a stable fallback id keeps aggregation working");
        }

        // ------------------------------------------------------- cross-building aggregation

        public static void TestAggregatorMergesSameWarningAcrossBuildings()
        {
            var groups = FarmWarningAggregator.BuildGroups(new[]
            {
                Subject(FarmA, HdbfTier.Basic, Warning(HdbfWarningKeys.IrrigationLow, FarmWarningSeverity.High)),
                Subject(FarmB, HdbfTier.Intermediate, Warning(HdbfWarningKeys.IrrigationLow, FarmWarningSeverity.High)),
                Subject(FarmC, HdbfTier.Top, Warning(HdbfWarningKeys.IrrigationLow, FarmWarningSeverity.High)),
            });

            TestAssert.Equal(1, groups.Count);
            TestAssert.Equal(HdbfWarningKeys.IrrigationLow, groups[0].CodeKey);
            TestAssert.Equal(3, groups[0].Count, "'3 座建築灌溉不足' is one group");
            TestAssert.Equal(FarmWarningSeverity.High, groups[0].Severity);
            TestAssert.Equal(3, FarmWarningAggregator.FindGroup(groups, HdbfWarningKeys.IrrigationLow)!.Count);
            TestAssert.True(FarmWarningAggregator.FindGroup(groups, HdbfWarningKeys.NoPower) is null);
        }

        public static void TestAggregatorOrdersP0BeforeP1AndByCount()
        {
            var groups = FarmWarningAggregator.BuildGroups(new[]
            {
                Subject(FarmA, HdbfTier.Basic, Warning(HdbfWarningKeys.IrrigationLow, FarmWarningSeverity.High)),
                Subject(FarmB, HdbfTier.Basic, Warning(HdbfWarningKeys.IrrigationLow, FarmWarningSeverity.High)),
                Subject(FarmC, HdbfTier.Basic, Warning(HdbfWarningKeys.IrrigationLow, FarmWarningSeverity.High),
                    Warning(HdbfWarningKeys.Shutdown, FarmWarningSeverity.Critical)),
            });

            TestAssert.Equal(2, groups.Count);
            TestAssert.Equal(HdbfWarningKeys.Shutdown, groups[0].CodeKey, "P0 first");
            TestAssert.Equal(HdbfWarningKeys.IrrigationLow, groups[1].CodeKey);
            TestAssert.Equal(3, groups[1].Count);
        }

        public static void TestAggregatorSortsEqualSeverityByCountThenKey()
        {
            var groups = FarmWarningAggregator.BuildGroups(new[]
            {
                // AdapterFailed: 1 farm; NoPower: 2 farms; both P1.
                Subject(FarmA, HdbfTier.Basic, Warning(HdbfWarningKeys.AdapterFailed, FarmWarningSeverity.High)),
                Subject(FarmB, HdbfTier.Basic, Warning(HdbfWarningKeys.NoPower, FarmWarningSeverity.High)),
                Subject(FarmC, HdbfTier.Basic, Warning(HdbfWarningKeys.NoPower, FarmWarningSeverity.High)),
            });

            TestAssert.Equal(2, groups.Count);
            TestAssert.Equal(HdbfWarningKeys.NoPower, groups[0].CodeKey, "larger group first");
            TestAssert.Equal(HdbfWarningKeys.AdapterFailed, groups[1].CodeKey);

            var tie = FarmWarningAggregator.BuildGroups(new[]
            {
                Subject(FarmA, HdbfTier.Basic, Warning(HdbfWarningKeys.NoPower, FarmWarningSeverity.High)),
                Subject(FarmA, HdbfTier.Basic, Warning(HdbfWarningKeys.AdapterFailed, FarmWarningSeverity.High)),
            });
            TestAssert.Equal(HdbfWarningKeys.AdapterFailed, tie[0].CodeKey, "equal count falls back to key order");
            TestAssert.Equal(HdbfWarningKeys.NoPower, tie[1].CodeKey);
        }

        public static void TestAggregatorCountsEachFarmOncePerKeyAndSortsMembers()
        {
            var groups = FarmWarningAggregator.BuildGroups(new[]
            {
                Subject(FarmC, HdbfTier.Basic,
                    Warning(HdbfWarningKeys.NoPower, FarmWarningSeverity.High),
                    Warning(HdbfWarningKeys.NoPower, FarmWarningSeverity.High)),
                Subject(FarmA, HdbfTier.Basic, Warning(HdbfWarningKeys.NoPower, FarmWarningSeverity.High)),
                Subject(FarmB, HdbfTier.Basic, Warning(HdbfWarningKeys.NoPower, FarmWarningSeverity.High)),
            });

            TestAssert.Equal(1, groups.Count);
            TestAssert.Equal(3, groups[0].Count, "a duplicated key on one farm must not inflate the count");
            TestAssert.Equal(FarmA, groups[0].Members[0].EntityId, "members sorted by entity id");
            TestAssert.Equal(FarmB, groups[0].Members[1].EntityId);
            TestAssert.Equal(FarmC, groups[0].Members[2].EntityId);
        }

        public static void TestAggregatorHandlesEmptyAndNullInput()
        {
            TestAssert.Equal(0, FarmWarningAggregator.BuildGroups(null).Count);
            TestAssert.Equal(0, FarmWarningAggregator.BuildGroups(Array.Empty<FarmWarningSubject>()).Count);
            TestAssert.Equal(0, FarmWarningAggregator.BuildGroups(new[]
            {
                Subject(FarmA, HdbfTier.Basic),
            }).Count);
            TestAssert.True(FarmWarningAggregator.FindGroup(null, HdbfWarningKeys.NoPower) is null);
        }

        // ------------------------------------------------------- distance / blink / schedule

        public static void TestDistanceCullingUses40CellDefaultAndBoundary()
        {
            TestAssert.Equal(40f, WorldIconPolicy.DefaultMaxDistance, "chapter 14.2 first-version distance");

            TestAssert.True(WorldIconPolicy.IsWorldIconVisible(true, 0f, 40f), "same cell");
            TestAssert.True(WorldIconPolicy.IsWorldIconVisible(true, 39.9f, 40f));
            TestAssert.True(WorldIconPolicy.IsWorldIconVisible(true, 40f, 40f), "exactly 40 is still visible");
            TestAssert.False(WorldIconPolicy.IsWorldIconVisible(true, 40.001f, 40f), ">40 is hidden");
            TestAssert.False(WorldIconPolicy.IsWorldIconVisible(false, 1f, 40f), "off screen is hidden");
            TestAssert.False(WorldIconPolicy.IsWorldIconVisible(true, float.NaN, 40f), "NaN is hidden");
            TestAssert.False(WorldIconPolicy.IsWorldIconVisible(true, float.PositiveInfinity, 40f));
            TestAssert.False(WorldIconPolicy.IsWorldIconVisible(true, 1f, -1f), "negative budget is hidden");
        }

        public static void TestBlinkOnlyForCriticalAndOnlyWhenEnabled()
        {
            TestAssert.True(WorldIconPolicy.ShouldBlink(FarmWarningSeverity.Critical, true));
            TestAssert.False(WorldIconPolicy.ShouldBlink(FarmWarningSeverity.Critical, false),
                "chapter 14.2: blink can be turned off");
            TestAssert.False(WorldIconPolicy.ShouldBlink(FarmWarningSeverity.High, true),
                "P1 does not blink");
        }

        public static void TestBlinkPhaseIsAtMostOneHertz()
        {
            // 1 Hz = one on/off cycle per second: on during [0, 0.5), off during [0.5, 1).
            TestAssert.True(WorldIconPolicy.IsBlinkOn(0d, 1f));
            TestAssert.True(WorldIconPolicy.IsBlinkOn(0.25d, 1f));
            TestAssert.False(WorldIconPolicy.IsBlinkOn(0.5d, 1f));
            TestAssert.False(WorldIconPolicy.IsBlinkOn(0.75d, 1f));
            TestAssert.True(WorldIconPolicy.IsBlinkOn(1.0d, 1f), "next cycle starts at 1 s");
            TestAssert.False(WorldIconPolicy.IsBlinkOn(1.5d, 1f));

            // A disabled/zero rate never hides the icon.
            TestAssert.True(WorldIconPolicy.IsBlinkOn(0.5d, 0f));
            TestAssert.True(WorldIconPolicy.IsBlinkOn(double.NaN, 1f));
        }

        public static void TestIconPassIsDueOnTheHalfSecondBoundary()
        {
            TestAssert.Equal(0.5f, WorldIconPolicy.DefaultIconIntervalSeconds, "chapter 14.6: 0.5 s");
            TestAssert.False(WorldIconPolicy.IsDue(0.49d, 0.5d, 0.5f));
            TestAssert.True(WorldIconPolicy.IsDue(0.5d, 0.5d, 0.5f));
            TestAssert.True(WorldIconPolicy.IsDue(0.1d, 0.5d, 0f), "zero interval is always due");
        }

        // ------------------------------------------------------- 2 s hysteresis

        public static void TestHysteresisAppearsOnlyAfterTheWindow()
        {
            var gate = new WarningHysteresis(1);
            TestAssert.False(gate.Observe(0, true, 0d, 2d), "first active sample starts the timer");
            TestAssert.False(gate.Observe(0, true, 1.0d, 2d));
            TestAssert.False(gate.Observe(0, true, 1.999d, 2d));
            TestAssert.True(gate.Observe(0, true, 2.0d, 2d), "visible after 2 s");
            TestAssert.True(gate.Observe(0, true, 2.5d, 2d), "stays visible while active");
        }

        public static void TestHysteresisHidesOnlyAfterTheWindow()
        {
            var gate = new WarningHysteresis(1);
            gate.Observe(0, true, 10d, 2d);
            TestAssert.True(gate.Observe(0, true, 12d, 2d));

            TestAssert.True(gate.Observe(0, false, 13d, 2d), "inactive sample starts the hide timer");
            TestAssert.True(gate.Observe(0, false, 14.999d, 2d));
            TestAssert.False(gate.Observe(0, false, 15.0d, 2d), "hidden after 2 s inactive");
        }

        public static void TestHysteresisIsWallClockNotSampleCount()
        {
            // The icon pass (2 Hz) and the panel (4 Hz) sample the same farm at different rates;
            // the window must stay 2 s regardless.
            var sparse = new WarningHysteresis(1);
            TestAssert.False(sparse.Observe(0, true, 0d, 2d));
            TestAssert.True(sparse.Observe(0, true, 2.5d, 2d), "2 samples, 2.5 s apart is enough");

            var dense = new WarningHysteresis(1);
            for (var i = 0; i < 16; i++)
            {
                var visible = dense.Observe(0, true, i * 0.125d, 2d);
                TestAssert.False(visible, "16 fast samples before 2 s must not show the warning");
            }

            TestAssert.True(dense.Observe(0, true, 2.0d, 2d));
        }

        public static void TestHysteresisZeroThresholdIsImmediateAndResetWorks()
        {
            var gate = new WarningHysteresis(2);
            TestAssert.True(gate.Observe(0, true, 5d, 0d), "threshold 0 = no debounce");
            TestAssert.False(gate.Observe(1, false, 5d, 0d));

            gate.Observe(1, true, 10d, 2d);
            TestAssert.False(gate.IsVisible(1), "the 2 s timer starts on the first active sample");
            gate.Reset();
            TestAssert.False(gate.IsVisible(1));

            // A backwards clock restarts the timer instead of latching forever.
            gate.Observe(1, true, 100d, 2d);
            TestAssert.False(gate.IsVisible(1));
            TestAssert.False(gate.Observe(1, true, 0.5d, 2d), "clock moved back: the timer restarts");
            TestAssert.True(gate.Observe(1, true, 2.5d, 2d), "visible 2 s after the restarted timer");
        }

        public static void TestHysteresisSlotsAreIndependent()
        {
            var gate = new WarningHysteresis(3);
            TestAssert.False(gate.Observe(0, true, 0d, 2d));
            TestAssert.False(gate.Observe(0, true, 1d, 2d));
            TestAssert.True(gate.Observe(0, true, 2d, 2d));
            TestAssert.False(gate.IsVisible(1));
            TestAssert.False(gate.IsVisible(2));
            TestAssert.False(gate.Observe(99, true, 2d, 2d), "out-of-range slot is ignored");
        }

        // ------------------------------------------------------- config ui.* defaults / codec

        public static void TestUiConfigDefaultsMatchChapter142()
        {
            var ui = HdbfConfig.Default.Ui;
            TestAssert.True(ui.WorldIcons);
            TestAssert.True(ui.WorldIconBlink);
            TestAssert.Equal(40, ui.WorldIconMaxDistance, "40 cells");
            TestAssert.Equal(0.5f, ui.WorldIconIntervalSeconds, "0.5 s aggregation");
            TestAssert.Equal(24, ui.WorldIconFarmsPerTick);
            TestAssert.Equal(3f, ui.WarningAggregateTtlSeconds);
        }

        public static void TestUiConfigParsesOverridesAndClamps()
        {
            const string json = @"{
              ""configSchemaVersion"": 1,
              ""features"": {},
              ""perf"": {},
              ""ui"": {
                ""worldIcons"": false,
                ""worldIconBlink"": false,
                ""worldIconMaxDistance"": 5,
                ""worldIconIntervalSeconds"": 9.0,
                ""worldIconFarmsPerTick"": 0,
                ""warningAggregateTtlSeconds"": 0.5
              },
              ""balance"": {}
            }";

            var warnings = new List<string>();
            var config = HdbfConfigCodec.Parse(json, warnings.Add);

            TestAssert.False(config.Ui.WorldIcons);
            TestAssert.False(config.Ui.WorldIconBlink);
            TestAssert.Equal(HdbfConfigRanges.WorldIconMaxDistanceMin, config.Ui.WorldIconMaxDistance,
                "5 is clamped up to the minimum");
            TestAssert.Equal(HdbfConfigRanges.WorldIconIntervalSecondsMax,
                config.Ui.WorldIconIntervalSeconds, "9 s is clamped down");
            TestAssert.Equal(HdbfConfigRanges.WorldIconFarmsPerTickMin,
                config.Ui.WorldIconFarmsPerTick, "0 is clamped up");
            TestAssert.Equal(HdbfConfigRanges.WarningAggregateTtlSecondsMin,
                config.Ui.WarningAggregateTtlSeconds, "0.5 s TTL is clamped up");
            TestAssert.True(warnings.Exists(message => message.Contains("ui.worldIconMaxDistance")),
                string.Join(" | ", warnings));
        }

        public static void TestUiConfigRoundTripsAndIsOptionalForOldFiles()
        {
            var json = HdbfConfigCodec.Serialize(HdbfConfig.Default);
            TestAssert.True(json.Contains("\"ui\""), "serializer writes the additive ui section");
            TestAssert.True(json.Contains("\"worldIconMaxDistance\": 40"), "json: " + json);

            var warnings = new List<string>();
            var parsed = HdbfConfigCodec.Parse(json, warnings.Add);
            TestAssert.Equal(0, warnings.Count, string.Join(" | ", warnings));
            TestAssert.Equal(40, parsed.Ui.WorldIconMaxDistance);
            TestAssert.True(parsed.Ui.WorldIcons);

            // A pre-BUILD-04 file (no ui section) must still parse with the new defaults.
            var legacy = HdbfConfigCodec.Parse(
                "{\"configSchemaVersion\":1,\"features\":{},\"perf\":{},\"balance\":{}}", warnings.Add);
            TestAssert.Equal(40, legacy.Ui.WorldIconMaxDistance, "old files get 40 cells");
            TestAssert.True(legacy.Ui.WorldIconBlink, "old files get the requirement default");
        }

        // ------------------------------------------------------- localization

        public static void TestNewWarningKeysExistInAllThreeLanguages()
        {
            var en = LoadTexts("enUS");
            var zh = LoadTexts("zhCN");
            var tw = LoadTexts("zhTW");

            foreach (var key in NewKeys())
            {
                TestAssert.True(en.ContainsKey(key), "enUS is missing " + key);
                TestAssert.True(zh.ContainsKey(key), "zhCN is missing " + key);
                TestAssert.True(tw.ContainsKey(key), "zhTW is missing " + key);
            }

            AssertSameKeySet(en, zh, "enUS vs zhCN");
            AssertSameKeySet(en, tw, "enUS vs zhTW");
        }

        public static void TestNewTextsMatchTheContractLiterals()
        {
            var en = LoadTexts("enUS");
            var zh = LoadTexts("zhCN");
            var tw = LoadTexts("zhTW");

            TestAssert.Equal("Also {0} buildings: {1}", en[HdbfUiLocKeys.PanelWarningAggregate]);
            TestAssert.Equal("另有 {0} 座建筑：{1}", zh[HdbfUiLocKeys.PanelWarningAggregate]);
            TestAssert.Equal("另有 {0} 座建築：{1}", tw[HdbfUiLocKeys.PanelWarningAggregate]);

            TestAssert.Equal("World warning icons", en[HdbfUiLocKeys.PanelSettingWorldIcons]);
            TestAssert.Equal("Blink warning icons", en[HdbfUiLocKeys.PanelSettingWorldIconBlink]);
            TestAssert.Equal("Icon distance (cells)", en[HdbfUiLocKeys.PanelSettingWorldIconDistance]);

            // The icon tooltip is a description-style key: {0} warning text, {1} distance.
            TestAssert.Equal("{0} (P0/P1 world icon; hidden beyond {1} cells)",
                en[HdbfUiLocKeys.TooltipWorldIcon]);
            AssertSamePlaceholders(en[HdbfUiLocKeys.TooltipWorldIcon],
                zh[HdbfUiLocKeys.TooltipWorldIcon], "enUS/zhCN tooltip");
            AssertSamePlaceholders(en[HdbfUiLocKeys.PanelWarningAggregate],
                tw[HdbfUiLocKeys.PanelWarningAggregate], "enUS/zhTW aggregate row");
        }

        public static void TestNewKeysRespectTheChapter144LabelLengths()
        {
            var en = LoadTexts("enUS");
            var zh = LoadTexts("zhCN");
            var tw = LoadTexts("zhTW");

            foreach (var key in new[]
                     {
                         HdbfUiLocKeys.PanelSettingWorldIcons,
                         HdbfUiLocKeys.PanelSettingWorldIconBlink,
                         HdbfUiLocKeys.PanelSettingWorldIconDistance,
                     })
            {
                TestAssert.True(en[key].Length <= EnglishLabelMaxLength,
                    "enUS " + key + " label too long: " + en[key].Length);
                TestAssert.True(zh[key].Length <= ChineseLabelMaxLength,
                    "zhCN " + key + " label too long: " + zh[key].Length);
                TestAssert.True(tw[key].Length <= ChineseLabelMaxLength,
                    "zhTW " + key + " label too long: " + tw[key].Length);
            }
        }

        public static void TestUiWarningConstantsMatchCoreWarningKeys()
        {
            // UI and Core each need the key strings (UI cannot reference gameplay) but they must
            // never drift: the Core aggregator groups by the same literal the evaluator emits.
            TestAssert.Equal(HdbfWarningKeys.Shutdown, HdbfUiLocKeys.WarningShutdown);
            TestAssert.Equal(HdbfWarningKeys.NoWater, HdbfUiLocKeys.WarningNoWater);
            TestAssert.Equal(HdbfWarningKeys.IrrigationLow, HdbfUiLocKeys.WarningIrrigationLow);
            TestAssert.Equal(HdbfWarningKeys.NoPower, HdbfUiLocKeys.WarningNoPower);
            TestAssert.Equal(HdbfWarningKeys.AdapterFailed, HdbfUiLocKeys.WarningAdapterFailed);
            TestAssert.Equal(HdbfWarningKeys.MaintenanceOverdue, HdbfUiLocKeys.WarningMaintenanceOverdue);
            TestAssert.Equal(HdbfWarningKeys.FatigueHigh, HdbfUiLocKeys.WarningFatigueHigh);
            TestAssert.Equal(HdbfWarningKeys.AdapterDisabled, HdbfUiLocKeys.WarningAdapterDisabled);
            TestAssert.Equal(HdbfWarningKeys.TankLow, HdbfUiLocKeys.WarningTankLow);
        }

        public static void TestNewKeyConstantValues()
        {
            TestAssert.Equal("HDBF.Panel.WarningAggregate", HdbfUiLocKeys.PanelWarningAggregate);
            TestAssert.Equal("HDBF.Panel.WarningAggregateHint", HdbfUiLocKeys.PanelWarningAggregateHint);
            TestAssert.Equal("HDBF.Panel.SettingWorldIcons", HdbfUiLocKeys.PanelSettingWorldIcons);
            TestAssert.Equal("HDBF.Panel.SettingWorldIconBlink", HdbfUiLocKeys.PanelSettingWorldIconBlink);
            TestAssert.Equal("HDBF.Panel.SettingWorldIconDistance", HdbfUiLocKeys.PanelSettingWorldIconDistance);
            TestAssert.Equal("HDBF.Tooltip.WorldIcon", HdbfUiLocKeys.TooltipWorldIcon);
        }

        // ------------------------------------------------------- helpers

        private static string[] NewKeys() => new[]
        {
            HdbfUiLocKeys.PanelWarningAggregate,
            HdbfUiLocKeys.PanelWarningAggregateHint,
            HdbfUiLocKeys.PanelSettingWorldIcons,
            HdbfUiLocKeys.PanelSettingWorldIconBlink,
            HdbfUiLocKeys.PanelSettingWorldIconDistance,
            HdbfUiLocKeys.TooltipWorldIcon,
        };

        private static FarmWarning Warning(string key, FarmWarningSeverity severity) => new()
        {
            CodeKey = key,
            Severity = severity,
            NumberArg0 = 0f,
            NumberArg1 = 0f,
        };

        private static FarmWarningSubject Subject(Guid entityId, HdbfTier tier, params FarmWarning[] warnings) =>
            FarmWarningSubject.FromWarnings(entityId, tier, warnings);

        private static void AssertSameKeySet(Dictionary<string, string> left,
            Dictionary<string, string> right, string label)
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

        private static Dictionary<string, string> LoadTexts(string language)
        {
            var resource = "HDBF.Tests.Localizations." + language + ".csv";
            var assembly = typeof(WarningIconContractTests).Assembly;
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
