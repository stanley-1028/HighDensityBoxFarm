// ============================================================================
// HDBF v2 | Layer: UI/Panels | Owner: V2-UI-01
// Purpose: Card builders of the v2 indoor panel read model (first half): workers/efficiency,
//          slots cards plus the shared row helpers. The water, maintenance, power, crop and output
//          cards live in .Cards.Water/Maintenance/Output.cs so every file stays inside the gate.
// Requirements: REQ-V2-UI-01/02/03, REQ-V2-WATER-03, REQ-V2-READ-04
// ============================================================================
using System;
using System.Collections.Generic;
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
