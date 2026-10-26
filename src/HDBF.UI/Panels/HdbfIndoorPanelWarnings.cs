// ============================================================================
// HDBF v2 | Layer: UI/Panels | Owner: V2-UI-01
// Purpose: Pure builders of the panel status rows, the concrete problem rows and the fixed-crop
//          dropdown choices. The four v2 warning categories and every blocking reason are derived
//          from the Core snapshot flags only (REQ-V2-UI-02, chapter 19.18.1-C).
// Requirements: REQ-V2-UI-02/03, REQ-V2-BAL-03, REQ-V2-READ-04
// ============================================================================
using System;
using System.Collections.Generic;
using HDBF.Core.Abstractions;
using HDBF.Core.Indoor;
using HDBF.UI.Localization;

namespace HDBF.UI.Panels
{
    /// <summary>
    /// Pure row builders of the v2 indoor panel. They take the localization resolver as a plain
    /// <see cref="Func{T,TResult}"/> so the whole mapping is exercised by the game-free L1 suite.
    /// </summary>
    public static class HdbfIndoorPanelWarnings
    {
        /// <summary>
        /// The four v2 warning categories (chapter 19.11): water shortage, maintenance overdue,
        /// power cut and blocked entrance. The extra flags (output full, no workers, missing
        /// material) become concrete problem rows instead of world-icon warnings.
        /// </summary>
        public static IReadOnlyList<HdbfPanelStatusRow> BuildStatusRows(IndoorFarmSnapshot snapshot,
            Func<string, string> localize)
        {
            var rows = new List<HdbfPanelStatusRow>(4);
            var flags = snapshot.Warnings;
            if ((flags & IndoorWarningFlags.NoWater) != 0)
            {
                AddRow(rows, HdbfUiLocKeys.WarningNoWater, FarmWarningSeverity.Critical, localize);
            }
            else if ((flags & IndoorWarningFlags.LowWater) != 0)
            {
                AddRow(rows, HdbfUiLocKeys.WarningIrrigationLow, FarmWarningSeverity.High, localize);
            }

            if ((flags & IndoorWarningFlags.MaintenanceStalled) != 0)
            {
                AddRow(rows, HdbfUiLocKeys.WarningMaintenanceOverdue, FarmWarningSeverity.Critical, localize);
            }
            else if ((flags & IndoorWarningFlags.MaintenanceDue) != 0)
            {
                AddRow(rows, HdbfUiLocKeys.WarningMaintenanceOverdue, FarmWarningSeverity.High, localize);
            }

            if ((flags & IndoorWarningFlags.NoPower) != 0)
            {
                AddRow(rows, HdbfUiLocKeys.WarningNoPower, FarmWarningSeverity.High, localize);
            }

            if ((flags & IndoorWarningFlags.EntranceBlocked) != 0)
            {
                AddRow(rows, HdbfUiLocKeys.WarningEntranceBlocked, FarmWarningSeverity.High, localize);
            }

            return rows;
        }

        /// <summary>
        /// Concrete blocking reasons (chapter 19.18.1-C): missing material with its count, water
        /// shortage with its source state, no workers, blocked entrance, full output and an
        /// invalid fixed crop.
        /// </summary>
        public static IReadOnlyList<string> BuildProblemRows(IndoorFarmSnapshot snapshot,
            HDBF.Core.Balance.TierProfile? profile, Func<string, string> localize)
        {
            var rows = new List<string>(6);
            var flags = snapshot.Warnings;

            if ((flags & IndoorWarningFlags.MissingMaterial) != 0)
            {
                var text = HdbfIndoorPanelFormat.TryGetMaintenanceMaterial(profile, snapshot.Power,
                    snapshot.PowerConnected, out var materialKey, out var count)
                    ? HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelMaintenanceMaterials,
                        HdbfIndoorPanelFormat.Text(localize, materialKey), count)
                    : HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.WarningMissingMaterial);
                rows.Add(text);
            }

            if ((flags & (IndoorWarningFlags.NoWater | IndoorWarningFlags.LowWater)) != 0)
            {
                var noWater = (flags & IndoorWarningFlags.NoWater) != 0;
                var water = HdbfIndoorPanelFormat.Text(localize,
                    noWater ? HdbfUiLocKeys.WarningNoWater : HdbfUiLocKeys.WarningIrrigationLow);
                if (snapshot.WaterSupply == WaterSupplyKind.None)
                {
                    water += HdbfIndoorPanelFormat.ProblemSeparator
                             + HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.TooltipNoWaterSource);
                }
                else if (snapshot.WaterSupply == WaterSupplyKind.Pipe && !snapshot.PipePort)
                {
                    water += HdbfIndoorPanelFormat.ProblemSeparator
                             + HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelWaterNone);
                }

                rows.Add(water);
            }

            if ((flags & IndoorWarningFlags.NoWorkers) != 0)
            {
                rows.Add(HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.WarningNoWorkers));
            }

            if ((flags & IndoorWarningFlags.EntranceBlocked) != 0)
            {
                rows.Add(HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.WarningEntranceBlocked));
            }

            if ((flags & IndoorWarningFlags.OutputFull) != 0)
            {
                rows.Add(HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.WarningOutputFull));
            }

            if (snapshot.CropPolicy == CropPolicy.Fixed
                && !FallbackCropCatalog.Default.TryGet(snapshot.Faction, snapshot.FixedCropId, out _))
            {
                rows.Add(HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.WarningInvalidCrop));
            }

            return rows;
        }

        /// <summary>Dropdown items: automatic rotation first, then the faction crop table.</summary>
        public static IReadOnlyList<HdbfCropChoice> BuildCropChoices(IndoorFarmSnapshot snapshot,
            Func<string, string> localize)
        {
            var crops = FallbackCropCatalog.Default.ForFaction(snapshot.Faction);
            var choices = new List<HdbfCropChoice>(crops.Count + 1)
            {
                new HdbfCropChoice
                {
                    Id = string.Empty,
                    Text = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelPolicyRotation),
                },
            };

            for (var index = 0; index < crops.Count; index++)
            {
                var crop = crops[index];
                if (crop is null || string.IsNullOrEmpty(crop.Id))
                {
                    continue;
                }

                choices.Add(new HdbfCropChoice
                {
                    Id = crop.Id,
                    Text = HdbfIndoorPanelFormat.CropName(localize, crop.Id),
                });
            }

            return choices;
        }

        /// <summary>Text severity marker: the second encoding that keeps colour from being the only carrier.</summary>
        public static string MarkerFor(FarmWarningSeverity severity)
        {
            switch (severity)
            {
                case FarmWarningSeverity.Critical: return "▲";
                case FarmWarningSeverity.High: return "!";
                default: return "•";
            }
        }

        private static void AddRow(List<HdbfPanelStatusRow> rows, string key, FarmWarningSeverity severity,
            Func<string, string> localize)
        {
            var row = new HdbfPanelStatusRow
            {
                Marker = MarkerFor(severity),
                Text = HdbfIndoorPanelFormat.Text(localize, key),
                Severity = severity,
            };

            // Stable insertion: P0 rows stay above P1/P2 rows, equal severities keep category order.
            var index = rows.Count;
            while (index > 0 && rows[index - 1].Severity > row.Severity)
            {
                index--;
            }

            rows.Insert(index, row);
        }
    }
}
