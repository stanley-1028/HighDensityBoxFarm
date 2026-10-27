// ============================================================================
// HDBF v2 | Layer: UI/Panels | Owner: V2-UI-01
// Purpose: Water card builder of the v2 indoor panel read model: live tank units + days bar,
//          live coverage C bar with the C_min marker, the flat C_min row, the supply source and
//          the live shortage derate warning. Split out of HdbfIndoorPanelFactory.Cards.cs so every
//          file stays inside the 300 line soft gate; the demand/coverage math stays in HDBF.Core.
// Requirements: REQ-V2-UI-01/02/03, REQ-V2-WATER-03, REQ-V2-READ-04
// ============================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using HDBF.Core.Formatting;
using HDBF.Core.Indoor;
using HDBF.UI.Localization;

namespace HDBF.UI.Panels
{
    public static partial class HdbfIndoorPanelFactory
    {
        private static HdbfPanelSection BuildWaterSection(IndoorFarmSnapshot snapshot, float coverage,
            float minCoverage, float irrigation, Func<string, string> localize)
        {
            var rows = new List<HdbfPanelRow>(4)
            {
                new()
                {
                    IconKey = "tank",
                    Label = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelTankLabel),
                    // Requirement F: the tank line carries "3.6/3.6 units" and the bar below it
                    // carries "(about 3.0 days)"; the generated panel never drops either half.
                    Value = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelTankValue,
                        PanelNumbers.Days(snapshot.Tank, roundUp: false),
                        PanelNumbers.Days(snapshot.TankCapacity, roundUp: false)),
                    Tooltip = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.TooltipTank),
                    HasBar = true,
                    Bar = HdbfIndoorPanelLimits.Ratio(snapshot.Tank, snapshot.TankCapacity),
                    BarText = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelBarTank,
                        PanelNumbers.Days(snapshot.TankDaysRemaining, roundUp: false)),
                },
                new()
                {
                    IconKey = "coverage",
                    Label = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelCoverageLabel),
                    Value = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelPercentValue,
                        PanelNumbers.Percent(coverage, roundUp: false)),
                    Tooltip = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.TooltipCoverage),
                    HasBar = true,
                    Bar = HdbfIndoorPanelLimits.Fill(coverage),
                    BarText = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelBarCoverage,
                        PanelNumbers.Percent(coverage, roundUp: false),
                        PanelNumbers.Percent(minCoverage, roundUp: false)),
                    BarMarker = minCoverage,
                },
                new()
                {
                    IconKey = "coverage",
                    Label = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelMinCoverageLabel),
                    Value = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelPercentValue,
                        PanelNumbers.Percent(minCoverage, roundUp: false)),
                    Tooltip = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.TooltipCoverage),
                },
                new()
                {
                    IconKey = "supply",
                    Label = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelSupplyLabel),
                    Value = HdbfIndoorPanelFormat.Text(localize,
                        HdbfIndoorPanelFormat.SupplyKey(snapshot.WaterSupply)),
                    Tooltip = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.TooltipTank),
                },
            };

            var warnings = new List<HdbfPanelRow>(HdbfIndoorPanelWarnings.BuildWaterWarnings(coverage,
                minCoverage, snapshot.WaterSupply, snapshot.PipePort, localize));
            if (irrigation < 0.999f)
            {
                // Growth is floored for the conservative direction, so the shown loss is the
                // complement (33 % growth -> 67 % loss) and the pair always adds up to 100 %.
                var growthPercent = PanelNumbers.Percent(irrigation, roundUp: false);
                var lostPercent = HdbfIndoorPanelFormat.Count(100
                    - int.Parse(growthPercent, CultureInfo.InvariantCulture));
                warnings.Add(new HdbfPanelRow
                {
                    IconKey = "no-water",
                    Label = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelWaterShortage,
                        growthPercent, lostPercent),
                    IsWarning = true,
                    Marker = HdbfIndoorPanelWarnings.MarkerFor(IndoorWarningSeverity.High),
                    Tooltip = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.TooltipCoverage),
                });
            }

            var cmin = Math.Max(0f, float.IsNaN(minCoverage) ? 0f : minCoverage);
            return new HdbfPanelSection
            {
                Kind = HdbfPanelSectionKind.Water,
                Title = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelSectionWater),
                IconKey = "tank",
                Tone = coverage < cmin ? "red" : "blue",
                Rows = WithWarnings(warnings, rows),
            };
        }
    }
}
