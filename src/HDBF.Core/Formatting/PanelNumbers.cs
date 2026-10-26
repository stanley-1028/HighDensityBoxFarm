using System;
using System.Globalization;
using HDBF.Core.Abstractions;
using HDBF.Core.Balance;

namespace HDBF.Core.Formatting
{
    /// <summary>
    /// The three plot figures the entity panel shows side by side (chapter 11.3 risk note:
    /// "面板必须同时显示 已注册格 / 有效格 / 可开垦格 三个数字，让玩家看得见限制") plus the display
    /// inputs the effective-plot formula needs (chapter 14.1: "必须同时显示公式与输入项 W × 38").
    ///
    /// Pure Core record with no game, Unity or localization dependency, so the L1 runner can assert
    /// exactly what the panel derives from a <see cref="BoxFarmSnapshot"/> (REQ-TEST-03). The
    /// localization templates themselves stay in the CSVs; this type only computes the numbers,
    /// which is what makes "registered 0 must show 0, not 60/60" testable offline.
    ///
    /// It never changes an existing snapshot field: <see cref="EffectivePlots"/> is copied verbatim
    /// (gameplay already computed min(capacity, W × 38)), and <see cref="RegisteredPlots"/> is
    /// copied verbatim so a new scan cannot be mistaken for a capacity-derived value.
    /// </summary>
    public sealed record PanelPlotFigures
    {
        /// <summary>Plot cells in the farm registry right now (may legitimately be 0).</summary>
        public int RegisteredPlots { get; init; }

        /// <summary>Tier plot capacity (60/150/300/600), the denominator of the plot ratios.</summary>
        public int PlotCapacity { get; init; }

        /// <summary>Snapshot's worker-derived effective plots: min(capacity, W × plots-per-farmer).</summary>
        public int EffectivePlots { get; init; }

        /// <summary>On-duty workers, the first input of the effective-plot formula.</summary>
        public int ActiveWorkers { get; init; }

        /// <summary>Plots one farmer can tend, the second input of the effective-plot formula.</summary>
        public int PlotsPerFarmer { get; init; }

        /// <summary>Accepted scan candidates; negative means "scan not completed" (unknown).</summary>
        public int TillablePlots { get; init; } = BoxFarmSnapshot.UnknownTillablePlots;

        /// <summary>Square work-range edge length (11/15/19/27).</summary>
        public int WorkRange { get; init; }

        /// <summary>True only after a completed scan reported a real candidate count (0 is known).</summary>
        public bool IsTillableKnown => TillablePlots >= 0;

        /// <summary>Work-range area in cells (121/225/361/729), chapter 11.3. Tooltip/detail only.</summary>
        public int WorkRangeCells => WorkRange * WorkRange;

        /// <summary>"registered/capacity", e.g. "0/60" or "150/150".</summary>
        public string RegisteredRatio => PanelNumbers.PlotRatio(RegisteredPlots, PlotCapacity);

        /// <summary>"—" while the scan has not completed, otherwise the accepted candidate count.</summary>
        public string TillableText => PanelNumbers.PlotCountOrUnknown(TillablePlots);

        /// <summary>
        /// Projects one read-only snapshot onto the panel figures. Null arguments are programmer
        /// errors (the game-free query layer already replaces a missing snapshot with an empty one),
        /// so they throw instead of silently rendering zeroes.
        /// </summary>
        public static PanelPlotFigures FromSnapshot(BoxFarmSnapshot snapshot, TierProfile profile)
        {
            if (snapshot is null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            if (profile is null)
            {
                throw new ArgumentNullException(nameof(profile));
            }

            var tillable = snapshot.TillablePlots;

            return new PanelPlotFigures
            {
                RegisteredPlots = NonNegative(snapshot.RegisteredPlots),
                PlotCapacity = NonNegative(profile.PlotCapacity),
                EffectivePlots = NonNegative(snapshot.EffectivePlots),
                ActiveWorkers = NonNegative(snapshot.ActiveWorkers),
                PlotsPerFarmer = TierCatalog.PlotsPerFarmer,
                TillablePlots = tillable < 0 ? BoxFarmSnapshot.UnknownTillablePlots : tillable,
                WorkRange = NonNegative(profile.WorkRange),
            };
        }

        private static int NonNegative(int value) => value < 0 ? 0 : value;
    }

    /// <summary>
    /// Number formatting rules of chapter 14.5, extracted from the UIElements fragment so the L1
    /// runner can verify them without loading Timberborn/Unity assemblies. The UI factory feeds
    /// these strings into the localized templates (<c>HDBF.Panel.*</c>), which keeps the
    /// "数字与单位由格式化层注入" rule of chapter 14.4 intact.
    ///
    /// Rules implemented: percentages are whole numbers (irrigation floor, fatigue ceil), days have
    /// one decimal, multipliers two, plot counts are integers; unknown (NaN/Infinity) renders as
    /// "—" instead of a number; no group separators and invariant culture, so a locale change cannot
    /// change the panel width or the canonical save text (chapter 14.5, chapter 12.4).
    /// </summary>
    public static class PanelNumbers
    {
        /// <summary>The single "unknown value" glyph used by every panel formatter (chapter 14.5).</summary>
        public const string Unknown = "—";

        /// <summary>Integer plot/worker count; no group separators (chapter 14.5).</summary>
        public static string PlotCount(int value) => value.ToString(CultureInfo.InvariantCulture);

        /// <summary>Integer count, or "—" for a negative "not ready / unknown" sentinel.</summary>
        public static string PlotCountOrUnknown(int value) => value < 0 ? Unknown : PlotCount(value);

        /// <summary>"value/capacity", the plot ratio form ("0/60", "150/150").</summary>
        public static string PlotRatio(int value, int capacity) =>
            PlotCount(value) + "/" + PlotCount(capacity);

        /// <summary>
        /// Coverage/fatigue percentage with zero decimals. <paramref name="roundUp"/> selects the
        /// conservative direction per chapter 14.5 (irrigation floors, fatigue ceils).
        /// </summary>
        public static string Percent(float ratio, bool roundUp)
        {
            if (!IsFinite(ratio))
            {
                return Unknown;
            }

            var clamped = Math.Max(0f, Math.Min(1f, ratio)) * 100f;
            var percent = roundUp ? (int)MathF.Ceiling(clamped) : (int)MathF.Floor(clamped);
            return PlotCount(percent);
        }

        /// <summary>Days with one decimal, floored or ceiled per chapter 14.5.</summary>
        public static string Days(float days, bool roundUp)
        {
            if (!IsFinite(days))
            {
                return Unknown;
            }

            var positive = Math.Max(0f, days);
            var rounded = roundUp
                ? MathF.Ceiling(positive * 10f) / 10f
                : MathF.Floor(positive * 10f) / 10f;
            return rounded.ToString("0.0", CultureInfo.InvariantCulture);
        }

        /// <summary>Days with one decimal, rounded half away from zero (maintenance countdown).</summary>
        public static string DaysRounded(float days)
        {
            if (!IsFinite(days))
            {
                return Unknown;
            }

            var rounded = MathF.Round(Math.Max(0f, days), 1);
            return rounded.ToString("0.0", CultureInfo.InvariantCulture);
        }

        /// <summary>Yield multiplier with two decimals (chapter 14.5: 产出倍率 2 位).</summary>
        public static string Multiplier(float value)
        {
            if (!IsFinite(value))
            {
                return Unknown;
            }

            return value.ToString("0.00", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Nearest integer for warning arguments that are percent points or counts. Rounds half away
        /// from zero; a non-finite value degrades to 0 so a warning line can never contain "NaN"
        /// (the surrounding formatter still renders unknown input as "—").
        /// </summary>
        public static int RoundedCount(float value)
        {
            if (!IsFinite(value))
            {
                return 0;
            }

            return (int)MathF.Round(value, MidpointRounding.AwayFromZero);
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
