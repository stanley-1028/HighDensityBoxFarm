// ============================================================================
// HDBF v2 | Layer: UI/Panels | Owner: V2-UI-01
// Purpose: Pure builders of the panel warning rows and the fixed-crop dropdown choices. Every
//          warning row carries an icon key AND a plain-text marker, so colour is never the only
//          information carrier (chapter 19.18.1-C). Water warnings are derived from the live
//          coverage C against C_min, never from the previous day-tick flag cache.
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
    /// Pure warning/choice builders of the v2 indoor panel. They take the localization resolver as
    /// a plain <see cref="Func{T,TResult}"/> so the whole mapping runs in the game-free L1 suite.
    /// </summary>
    public static class HdbfIndoorPanelWarnings
    {
        /// <summary>
        /// Water warning rows from the live coverage (REQ-V2-WATER-03): C = 0 is "no water", any
        /// 0 &lt; C &lt; C_min is "water low". A full tank therefore shows no warning even before the
        /// first daily tick, which removes the stale 0 % shortage the players reported.
        /// </summary>
        /// <param name="coverage">Live coverage C in [0,1].</param>
        /// <param name="minCoverage">Tier minimum coverage C_min.</param>
        /// <param name="supply">Water source kind, used for the concrete source sentence.</param>
        /// <param name="pipePort">True when the pipe port exists.</param>
        /// <param name="localize">Localization resolver.</param>
        /// <returns>Warning rows, critical first.</returns>
        public static IReadOnlyList<HdbfPanelRow> BuildWaterWarnings(float coverage, float minCoverage,
            WaterSupplyKind supply, bool pipePort, Func<string, string> localize)
        {
            var rows = new List<HdbfPanelRow>(2);
            if (float.IsNaN(coverage))
            {
                return rows;
            }

            var cmin = float.IsNaN(minCoverage) || minCoverage <= 0f ? 0f : minCoverage;
            if (coverage <= 0f)
            {
                AddRow(rows, "no-water", HdbfUiLocKeys.WarningNoWater, IndoorWarningSeverity.Critical,
                    SupplyDetail(supply, pipePort, localize), localize);
                return rows;
            }

            if (coverage >= cmin)
            {
                return rows;
            }

            AddRow(rows, "no-water", HdbfUiLocKeys.WarningIrrigationLow, IndoorWarningSeverity.High,
                SupplyDetail(supply, pipePort, localize), localize);
            return rows;
        }

        /// <summary>
        /// Maintenance warning rows: stalled farm, overdue cycle or missing upkeep material.
        /// </summary>
        /// <param name="snapshot">Read-only farm snapshot.</param>
        /// <param name="materialMissing">True when the upkeep material cannot be paid.</param>
        /// <param name="localize">Localization resolver.</param>
        /// <returns>Warning rows, critical first.</returns>
        public static IReadOnlyList<HdbfPanelRow> BuildMaintenanceWarnings(IndoorFarmSnapshot snapshot,
            bool materialMissing, Func<string, string> localize)
        {
            var rows = new List<HdbfPanelRow>(2);
            var flags = snapshot.Warnings;
            var stalled = (flags & IndoorWarningFlags.MaintenanceStalled) != 0;
            if (stalled)
            {
                AddRow(rows, "maintenance", HdbfUiLocKeys.WarningMaintenanceOverdue,
                    IndoorWarningSeverity.Critical, HdbfUiLocKeys.TooltipMaintenanceShutdown, localize);
                return rows;
            }

            if (materialMissing)
            {
                AddRow(rows, "material", HdbfUiLocKeys.WarningMissingMaterial,
                    IndoorWarningSeverity.Critical, null, localize);
                return rows;
            }

            if ((flags & IndoorWarningFlags.MaintenanceDue) != 0 || snapshot.MaintenanceCyclesMissed > 0)
            {
                AddRow(rows, "maintenance", HdbfUiLocKeys.WarningMaintenanceOverdue,
                    IndoorWarningSeverity.High, HdbfUiLocKeys.TooltipMaintenance, localize);
            }

            return rows;
        }

        /// <summary>Power cut warning row of a tier that requires grid power.</summary>
        /// <param name="snapshot">Read-only farm snapshot.</param>
        /// <param name="localize">Localization resolver.</param>
        /// <returns>Zero or one warning rows.</returns>
        public static IReadOnlyList<HdbfPanelRow> BuildPowerWarnings(IndoorFarmSnapshot snapshot,
            Func<string, string> localize)
        {
            var rows = new List<HdbfPanelRow>(1);
            if ((snapshot.Warnings & IndoorWarningFlags.NoPower) != 0)
            {
                AddRow(rows, "power", HdbfUiLocKeys.WarningNoPower, IndoorWarningSeverity.High,
                    HdbfUiLocKeys.TooltipPower, localize);
            }

            return rows;
        }

        /// <summary>Worker/entrance warning rows: no crew on duty or a blocked entrance.</summary>
        /// <param name="snapshot">Read-only farm snapshot.</param>
        /// <param name="entranceBlocked">True when the entrance is blocked.</param>
        /// <param name="localize">Localization resolver.</param>
        /// <returns>Warning rows, critical first.</returns>
        public static IReadOnlyList<HdbfPanelRow> BuildWorkerWarnings(IndoorFarmSnapshot snapshot,
            bool entranceBlocked, Func<string, string> localize)
        {
            var rows = new List<HdbfPanelRow>(2);
            if (snapshot.Workers <= 0 || (snapshot.Warnings & IndoorWarningFlags.NoWorkers) != 0)
            {
                AddRow(rows, "no-workers", HdbfUiLocKeys.WarningNoWorkers, IndoorWarningSeverity.Critical,
                    HdbfUiLocKeys.ErrorNoWorkers, localize);
            }

            if (entranceBlocked)
            {
                AddRow(rows, "entrance", HdbfUiLocKeys.WarningEntranceBlocked,
                    IndoorWarningSeverity.High, HdbfUiLocKeys.ErrorEntranceBlocked, localize);
            }

            return rows;
        }

        /// <summary>Output warning rows: a full output inventory blocks harvesting.</summary>
        /// <param name="snapshot">Read-only farm snapshot.</param>
        /// <param name="localize">Localization resolver.</param>
        /// <returns>Zero or one warning rows.</returns>
        public static IReadOnlyList<HdbfPanelRow> BuildOutputWarnings(IndoorFarmSnapshot snapshot,
            Func<string, string> localize)
        {
            var rows = new List<HdbfPanelRow>(1);
            if ((snapshot.Warnings & IndoorWarningFlags.OutputFull) != 0)
            {
                AddRow(rows, "output-full", HdbfUiLocKeys.WarningOutputFull,
                    IndoorWarningSeverity.High, HdbfUiLocKeys.ErrorOutputFull, localize);
            }

            return rows;
        }

        /// <summary>Crop warning rows: the fixed crop is not in the faction crop table.</summary>
        /// <param name="snapshot">Read-only farm snapshot.</param>
        /// <param name="localize">Localization resolver.</param>
        /// <returns>Zero or one warning rows.</returns>
        public static IReadOnlyList<HdbfPanelRow> BuildCropWarnings(IndoorFarmSnapshot snapshot,
            Func<string, string> localize)
        {
            var rows = new List<HdbfPanelRow>(1);
            if (snapshot.CropPolicy == CropPolicy.Fixed
                && !FallbackCropCatalog.Default.TryGet(snapshot.Faction, snapshot.FixedCropId, out _))
            {
                AddRow(rows, "invalid-crop", HdbfUiLocKeys.WarningInvalidCrop,
                    IndoorWarningSeverity.High, HdbfUiLocKeys.TooltipRotation, localize);
            }

            return rows;
        }

        /// <summary>Dropdown items: automatic rotation first, then the faction crop table.</summary>
        /// <param name="snapshot">Read-only farm snapshot.</param>
        /// <param name="localize">Localization resolver.</param>
        /// <returns>Ordered dropdown choices; item text is the localized display text.</returns>
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
        /// <param name="severity">Row severity.</param>
        /// <returns>"▲" for critical, "!" for high, "•" otherwise.</returns>
        public static string MarkerFor(IndoorWarningSeverity severity)
        {
            switch (severity)
            {
                case IndoorWarningSeverity.Critical: return "▲";
                case IndoorWarningSeverity.High: return "!";
                default: return "•";
            }
        }

        private static void AddRow(List<HdbfPanelRow> rows, string iconKey, string textKey,
            IndoorWarningSeverity severity, string? tooltipKey, Func<string, string> localize)
        {
            var text = HdbfIndoorPanelFormat.Text(localize, textKey);
            if (!string.IsNullOrEmpty(tooltipKey))
            {
                text += HdbfIndoorPanelFormat.ProblemSeparator
                        + HdbfIndoorPanelFormat.Text(localize, tooltipKey!);
            }

            rows.Add(new HdbfPanelRow
            {
                IconKey = iconKey,
                Label = text,
                IsWarning = true,
                Marker = MarkerFor(severity),
                Tooltip = text,
            });
        }

        private static string? SupplyDetail(WaterSupplyKind supply, bool pipePort,
            Func<string, string> localize)
        {
            switch (supply)
            {
                case WaterSupplyKind.None:
                    return HdbfUiLocKeys.TooltipNoWaterSource;
                case WaterSupplyKind.Pipe when !pipePort:
                    return HdbfUiLocKeys.PanelWaterNone;
                default:
                    return null;
            }
        }
    }
}
