using System;
using System.Globalization;

namespace HDBF.Core.Formatting
{
    /// <summary>
    /// Number formatting rules of chapter 14.5, extracted from the UIElements fragment so the L1
    /// runner can verify them without loading Timberborn/Unity assemblies. The UI factory feeds
    /// these strings into the localized templates (<c>HDBF.Panel.*</c>), which keeps the
    /// "数字与单位由格式化层注入" rule of chapter 14.4 intact.
    ///
    /// Rules implemented: percentages are whole numbers (floor), days have one decimal,
    /// multipliers two; unknown (NaN/Infinity) renders as "—" instead of a number; no group
    /// separators and invariant culture, so a locale change cannot change the panel width or the
    /// canonical save text (chapter 14.5, chapter 12.4). The v1 plot figures were deleted with the
    /// world-plot chain (section 19.2); v2 panels show slot/tank numbers only.
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
