using System;
using System.Collections.Generic;
using HDBF.Core.Abstractions;
using HDBF.Core.Balance;
using HDBF.Core.Warnings;

namespace HDBF.Gameplay.Components
{
    /// <summary>
    /// Thresholds of the player-visible warning states (chapter 14.2, table "状态与警告").
    /// Kept in one place so the UI description, the tests and the evaluator cannot drift.
    /// </summary>
    public static class HdbfWarningRules
    {
        /// <summary>Coverage ratio below which growth stops completely (C &lt; 0.25 × C_min).</summary>
        public const float NoWaterCoverageRatio = 0.25f;

        /// <summary>Fatigue at which the P2 fatigue warning appears (chapter 14.2: F_max ≥ 0.70).</summary>
        public const float FatigueHighThreshold = 0.70f;

        /// <summary>Tank-days ratio that raises the P4 low-tank warning (chapter 14.2: &lt; 25 %).</summary>
        public const float TankLowRatio = 0.25f;

        /// <summary>Seconds a warning must persist before it toggles (chapter 14.2 hysteresis).</summary>
        public const float HysteresisSeconds = 2f;
    }

    /// <summary>
    /// Computes and hysteresis-filters the warnings of one farm (chapter 14.2, REQ-UI-03).
    ///
    /// Design:
    /// * one instance per farm, owned by <see cref="HdbfFarmQueryService"/>; the only mutable
    ///   state is the Core <see cref="WarningHysteresis"/> gate, so nothing global times anything
    ///   (chapter 16.2 red line 6);
    /// * a warning appears only after it has been observed for the configured wall-clock window
    ///   (default 2 s, chapter 14.2) and disappears the same way, which stops threshold flicker;
    ///   wall-clock (not sample count) keeps that window exact now that both the panel and the
    ///   world-icon pass evaluate the same farm at different rates;
    /// * output is sorted P0 → P4 and then by key, so the status bar is deterministic;
    /// * allocation is acceptable here because evaluation runs at UI refresh rate (≤ 4 Hz) plus
    ///   the 2 Hz icon pass, never in the simulation tick (REQ-UI-12).
    /// </summary>
    internal sealed class HdbWarningEvaluator
    {
        private const int Shutdown = 0;
        private const int NoWater = 1;
        private const int IrrigationLow = 2;
        private const int NoPower = 3;
        private const int AdapterFailed = 4;
        private const int MaintenanceOverdue = 5;
        private const int FatigueHigh = 6;
        private const int AdapterDisabled = 7;
        private const int TankLow = 8;
        private const int WarningCount = 9;

        private static readonly Comparison<FarmWarning> CompareWarnings = Compare;

        private readonly WarningHysteresis _hysteresis = new WarningHysteresis(WarningCount);

        /// <summary>
        /// Evaluates one sample at <paramref name="nowSeconds"/> (unscaled wall clock) and returns
        /// the warnings that passed hysteresis, sorted by severity and then key. Never throws.
        /// </summary>
        public FarmWarning[] Evaluate(in HdbfWarningInputs inputs, double nowSeconds)
        {
            var threshold = inputs.HysteresisSeconds > 0f ? inputs.HysteresisSeconds : 0f;
            var zeroPoint = inputs.MinIrrigationCoverage * HdbfWarningRules.NoWaterCoverageRatio;

            var warnings = new List<FarmWarning>(WarningCount);

            if (Confirm(Shutdown, inputs.IsShutdown, threshold, nowSeconds))
            {
                warnings.Add(new FarmWarning
                {
                    CodeKey = HdbfWarningKeys.Shutdown,
                    Severity = FarmWarningSeverity.Critical,
                });
            }

            if (Confirm(NoWater, IsNoWater(in inputs, zeroPoint), threshold, nowSeconds))
            {
                warnings.Add(new FarmWarning
                {
                    CodeKey = HdbfWarningKeys.NoWater,
                    Severity = FarmWarningSeverity.Critical,
                    NumberArg0 = inputs.IrrigationCoverage * 100f,
                    NumberArg1 = inputs.MinIrrigationCoverage * 100f,
                });
            }

            if (Confirm(IrrigationLow, IsIrrigationLow(in inputs, zeroPoint), threshold, nowSeconds))
            {
                warnings.Add(new FarmWarning
                {
                    CodeKey = HdbfWarningKeys.IrrigationLow,
                    Severity = FarmWarningSeverity.High,
                    NumberArg0 = inputs.IrrigationCoverage * 100f,
                    NumberArg1 = inputs.MinIrrigationCoverage * 100f,
                });
            }

            if (Confirm(NoPower, inputs.RequiresPower && !inputs.PowerConnected, threshold, nowSeconds))
            {
                warnings.Add(new FarmWarning
                {
                    CodeKey = HdbfWarningKeys.NoPower,
                    Severity = FarmWarningSeverity.High,
                    NumberArg0 = TierCatalog.OffGridEfficiency * 100f,
                });
            }

            if (Confirm(AdapterFailed, inputs.AdapterFailedCount > 0, threshold, nowSeconds))
            {
                warnings.Add(new FarmWarning
                {
                    CodeKey = HdbfWarningKeys.AdapterFailed,
                    Severity = FarmWarningSeverity.High,
                    NumberArg0 = inputs.AdapterFailedCount,
                });
            }

            // Cycle 1 is the P2 "overdue" state; from cycle 2 the farm is shut down and the P0
            // warning takes over (chapter 6.5 / REQ-IMPL-20).
            if (Confirm(MaintenanceOverdue, inputs.CyclesMissed == 1, threshold, nowSeconds))
            {
                warnings.Add(new FarmWarning
                {
                    CodeKey = HdbfWarningKeys.MaintenanceOverdue,
                    Severity = FarmWarningSeverity.Medium,
                    NumberArg0 = inputs.CyclesMissed,
                });
            }

            if (Confirm(FatigueHigh, inputs.FatigueMax >= HdbfWarningRules.FatigueHighThreshold, threshold, nowSeconds))
            {
                warnings.Add(new FarmWarning
                {
                    CodeKey = HdbfWarningKeys.FatigueHigh,
                    Severity = FarmWarningSeverity.Medium,
                    NumberArg0 = inputs.FatigueMax * 100f,
                });
            }

            if (Confirm(AdapterDisabled, inputs.AdapterDisabledCount > 0, threshold, nowSeconds))
            {
                warnings.Add(new FarmWarning
                {
                    CodeKey = HdbfWarningKeys.AdapterDisabled,
                    Severity = FarmWarningSeverity.Low,
                    NumberArg0 = inputs.AdapterDisabledCount,
                });
            }

            if (Confirm(TankLow, IsTankLow(in inputs), threshold, nowSeconds))
            {
                warnings.Add(new FarmWarning
                {
                    CodeKey = HdbfWarningKeys.TankLow,
                    Severity = FarmWarningSeverity.Info,
                    NumberArg0 = inputs.TankDaysLeft,
                    NumberArg1 = inputs.TankCapacityDays,
                });
            }

            warnings.Sort(CompareWarnings);
            return warnings.ToArray();
        }

        /// <summary>Drops all hysteresis state; used when the farm is removed and by tests.</summary>
        public void Reset()
        {
            _hysteresis.Reset();
        }

        private static bool IsNoWater(in HdbfWarningInputs inputs, float zeroPoint)
        {
            return inputs.HasRegisteredPlots
                   && inputs.MinIrrigationCoverage > 0f
                   && inputs.IrrigationCoverage < zeroPoint;
        }

        private static bool IsIrrigationLow(in HdbfWarningInputs inputs, float zeroPoint)
        {
            return inputs.HasRegisteredPlots
                   && inputs.MinIrrigationCoverage > 0f
                   && inputs.IrrigationCoverage >= zeroPoint
                   && inputs.IrrigationCoverage < inputs.MinIrrigationCoverage;
        }

        private static bool IsTankLow(in HdbfWarningInputs inputs)
        {
            if (!inputs.HasRegisteredPlots || inputs.TankCapacityDays <= 0 || inputs.TankFedPlots <= 0)
            {
                // With no tank-fed plots the tank is not draining, so "days left" is meaningless;
                // an empty tank on dry plots is already covered by the P0/P1 coverage warnings.
                return false;
            }

            var lowThreshold = inputs.TankCapacityDays * HdbfWarningRules.TankLowRatio;
            return inputs.TankDaysLeft < lowThreshold;
        }

        /// <summary>
        /// Applies the wall-clock hysteresis to one warning slot and returns whether it should be
        /// shown. The rule itself lives in <see cref="WarningHysteresis"/> (Core, L1-tested).
        /// </summary>
        private bool Confirm(int index, bool active, double thresholdSeconds, double nowSeconds)
        {
            return _hysteresis.Observe(index, active, nowSeconds, thresholdSeconds);
        }

        private static int Compare(FarmWarning left, FarmWarning right)
        {
            var bySeverity = left.Severity.CompareTo(right.Severity);
            return bySeverity != 0
                ? bySeverity
                : string.CompareOrdinal(left.CodeKey, right.CodeKey);
        }
    }
}
