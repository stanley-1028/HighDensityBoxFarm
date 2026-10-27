// ============================================================================
// HDBF v2 | Layer: UI/Panels | Owner: V2-UI-01
// Purpose: Card builders of the v2 indoor panel read model (first half): workers/efficiency,
//          slots, water and maintenance cards plus the shared row helpers. The power, crop and
//          output cards live in .Cards.Output.cs so both files stay inside the 300 line gate.
// Requirements: REQ-V2-UI-01/02/03, REQ-V2-WATER-03, REQ-V2-READ-04
// ============================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using HDBF.Core;
using HDBF.Core.Balance;
using HDBF.Core.Formatting;
using HDBF.Core.Indoor;
using HDBF.UI.Localization;

namespace HDBF.UI.Panels
{
    public static partial class HdbfIndoorPanelFactory
    {
        private static HdbfPanelSection BuildWorkersSection(IndoorFarmSnapshot snapshot, float irrigation,
            bool entranceBlocked, Func<string, string> localize)
        {
            var rows = new List<HdbfPanelRow>(2)
            {
                new()
                {
                    IconKey = "workers",
                    Label = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelCrewLabel),
                    Value = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelCountValue,
                        snapshot.Workers, snapshot.MaxWorkers),
                    Tooltip = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.TooltipWorkers),
                },
                new()
                {
                    IconKey = "efficiency",
                    Label = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelEfficiencyLabel),
                    Value = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelPercentValue,
                        PanelNumbers.Percent(irrigation, roundUp: false)),
                    Tooltip = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.TooltipEfficiency),
                    HasBar = true,
                    Bar = HdbfIndoorPanelLimits.Fill(irrigation),
                    BarText = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelPercentValue,
                        PanelNumbers.Percent(irrigation, roundUp: false)),
                },
            };

            var warnings = HdbfIndoorPanelWarnings.BuildWorkerWarnings(snapshot, entranceBlocked, localize);
            return new HdbfPanelSection
            {
                Kind = HdbfPanelSectionKind.Workers,
                Title = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelSectionWorkers),
                IconKey = "workers",
                Tone = "green",
                Rows = WithWarnings(warnings, rows),
            };
        }

        private static HdbfPanelSection BuildSlotsSection(IndoorFarmSnapshot snapshot,
            Func<string, string> localize)
        {
            var rows = new List<HdbfPanelRow>(7)
            {
                new()
                {
                    IconKey = "capacity",
                    Label = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelCapacityLabel),
                    Value = HdbfIndoorPanelFormat.Count(snapshot.Capacity),
                    Tooltip = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.TooltipSlots),
                },
                new()
                {
                    IconKey = "configured",
                    Label = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelConfiguredLabel),
                    Value = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelCountValue,
                        snapshot.ConfiguredSlots, snapshot.Capacity),
                    Tooltip = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.TooltipSlots),
                    HasBar = true,
                    Bar = HdbfIndoorPanelLimits.Ratio(snapshot.ConfiguredSlots, snapshot.Capacity),
                    BarText = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelBarSlots,
                        snapshot.ConfiguredSlots, snapshot.Capacity),
                },
                new()
                {
                    IconKey = "effective",
                    Label = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelEffectiveLabel),
                    Value = HdbfIndoorPanelFormat.Count(snapshot.EffectiveSlots),
                    Tooltip = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.TooltipEffectiveSlots,
                        snapshot.Capacity, snapshot.Workers, TierCatalog.SlotsPerFarmer),
                },
                SimpleRow("empty", HdbfUiLocKeys.PanelEmptyLabel, snapshot.EmptySlots, localize),
                SimpleRow("growing", HdbfUiLocKeys.PanelGrowingLabel, snapshot.GrowingSlots, localize),
                SimpleRow("harvest", HdbfUiLocKeys.PanelHarvestLabel, snapshot.MatureSlots, localize),
                SimpleRow("backlog", HdbfUiLocKeys.PanelBacklogLabel, snapshot.BacklogActions, localize),
            };

            return new HdbfPanelSection
            {
                Kind = HdbfPanelSectionKind.Slots,
                Title = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelSectionSlots),
                IconKey = "slots",
                Tone = "green",
                Rows = rows,
            };
        }

        private static HdbfPanelSection BuildWaterSection(IndoorFarmSnapshot snapshot, float coverage,
            float minCoverage, float irrigation, Func<string, string> localize)
        {
            var rows = new List<HdbfPanelRow>(4)
            {
                new()
                {
                    IconKey = "tank",
                    Label = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelTankLabel),
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

            var cmin = float.IsNaN(minCoverage) || minCoverage <= 0f ? 0f : minCoverage;
            return new HdbfPanelSection
            {
                Kind = HdbfPanelSectionKind.Water,
                Title = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelSectionWater),
                IconKey = "tank",
                Tone = coverage < cmin ? "red" : "blue",
                Rows = WithWarnings(warnings, rows),
            };
        }

        private static HdbfPanelSection BuildMaintenanceSection(IndoorFarmSnapshot snapshot,
            TierProfile? profile, Func<string, string> localize)
        {
            var materialFree = profile is null || profile.MaintenanceKind == HdbfMaintenanceKind.IrrigationOnly;
            var materialMissing = !materialFree
                && (snapshot.MaintenanceMaterialMissing
                    || (snapshot.Warnings & IndoorWarningFlags.MissingMaterial) != 0);
            var rows = new List<HdbfPanelRow>(2)
            {
                new()
                {
                    IconKey = "due",
                    Label = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelMaintenanceDueLabel),
                    Value = materialFree
                        ? HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelMaintenanceNone)
                        : PanelNumbers.Days(snapshot.MaintenanceDueDay, roundUp: false),
                    Tooltip = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.TooltipMaintenance),
                },
                SimpleRow("maintenance", HdbfUiLocKeys.PanelMaintenanceCyclesLabel,
                    snapshot.MaintenanceCyclesMissed, localize),
            };

            var warnings = new List<HdbfPanelRow>(
                HdbfIndoorPanelWarnings.BuildMaintenanceWarnings(snapshot, materialMissing, localize));
            if (materialMissing)
            {
                warnings = new List<HdbfPanelRow>(
                    AppendMissingMaterial(warnings, snapshot, profile, localize));
            }

            return new HdbfPanelSection
            {
                Kind = HdbfPanelSectionKind.Maintenance,
                Title = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelSectionMaintenance),
                IconKey = "maintenance",
                Tone = warnings.Count > 0 ? "red" : "green",
                Rows = WithWarnings(warnings, rows),
            };
        }

        // ------------------------------------------------------------------ row helpers

        private static HdbfPanelRow SimpleRow(string iconKey, string labelKey, int value,
            Func<string, string> localize)
        {
            return new HdbfPanelRow
            {
                IconKey = iconKey,
                Label = HdbfIndoorPanelFormat.Text(localize, labelKey),
                Value = HdbfIndoorPanelFormat.Count(value),
            };
        }

        private static HdbfPanelRow SimpleTextRow(string iconKey, string labelKey, string value,
            Func<string, string> localize)
        {
            return new HdbfPanelRow
            {
                IconKey = iconKey,
                Label = HdbfIndoorPanelFormat.Text(localize, labelKey),
                Value = value,
            };
        }

        private static IReadOnlyList<HdbfPanelRow> WithWarnings(IReadOnlyList<HdbfPanelRow> warnings,
            List<HdbfPanelRow> rows)
        {
            if (warnings.Count == 0)
            {
                return rows;
            }

            var merged = new List<HdbfPanelRow>(warnings.Count + rows.Count);
            for (var index = 0; index < warnings.Count; index++)
            {
                merged.Add(warnings[index]);
            }

            merged.AddRange(rows);
            return merged;
        }

        private static IReadOnlyList<HdbfPanelRow> AppendMissingMaterial(IReadOnlyList<HdbfPanelRow> warnings,
            IndoorFarmSnapshot snapshot, TierProfile? profile, Func<string, string> localize)
        {
            var detail = HdbfIndoorPanelFormat.TryGetMaintenanceMaterial(profile, snapshot.Power,
                snapshot.PowerConnected, out var materialKey, out var count)
                ? HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelMaintenanceMaterials,
                    HdbfIndoorPanelFormat.Text(localize, materialKey), count)
                : HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.WarningMissingMaterial);

            var merged = new List<HdbfPanelRow>(warnings.Count);
            for (var index = 0; index < warnings.Count; index++)
            {
                var row = warnings[index];
                merged.Add(row.IsWarning
                    ? row with
                    {
                        Label = row.Label + HdbfIndoorPanelFormat.ProblemSeparator + detail,
                        Tooltip = row.Label + HdbfIndoorPanelFormat.ProblemSeparator + detail,
                    }
                    : row);
            }

            return merged;
        }
    }
}
