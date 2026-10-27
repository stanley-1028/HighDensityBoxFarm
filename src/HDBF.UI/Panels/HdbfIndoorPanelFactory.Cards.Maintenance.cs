// ============================================================================
// HDBF v2 | Layer: UI/Panels | Owner: V2-UI-01
// Purpose: Maintenance card builder of the v2 indoor panel read model: next due day, overdue
//          cycles, the upkeep-material requirement row (8 plank / 10 days, 6 metal / 10 days,
//          off-grid top 15 metal / 8 days), the missing-material detail and the stalled/overdue
//          warnings. Split out of HdbfIndoorPanelFactory.Cards.cs to stay inside the 300 line gate.
// Requirements: REQ-V2-UI-01/02/03, REQ-V2-MAINT-02, REQ-V2-READ-04
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
        private static HdbfPanelSection BuildMaintenanceSection(IndoorFarmSnapshot snapshot,
            TierProfile? profile, Func<string, string> localize)
        {
            var materialFree = profile is null || profile.MaintenanceKind == HdbfMaintenanceKind.IrrigationOnly;
            var materialMissing = !materialFree
                && (snapshot.MaintenanceMaterialMissing
                    || (snapshot.Warnings & IndoorWarningFlags.MissingMaterial) != 0);
            var rows = new List<HdbfPanelRow>(3)
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
                new()
                {
                    IconKey = "material",
                    Label = HdbfIndoorPanelFormat.Text(localize,
                        HdbfUiLocKeys.PanelMaintenanceMaterialLabel),
                    Value = MaintenanceMaterialValue(snapshot, profile, localize),
                    Tooltip = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.TooltipMaintenance),
                },
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

        /// <summary>
        /// Upkeep-material requirement of the tier: material name x count / cycle days. A
        /// material-free tier (Basic irrigation-only, or a top tier on grid power) says so instead
        /// of hiding the row, so the maintenance card always explains itself (requirement F).
        /// </summary>
        private static string MaintenanceMaterialValue(IndoorFarmSnapshot snapshot, TierProfile? profile,
            Func<string, string> localize)
        {
            if (!HdbfIndoorPanelFormat.TryGetMaintenanceMaterial(profile, snapshot.Power,
                    snapshot.PowerConnected, out var materialKey, out var count))
            {
                return HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelMaintenanceNone);
            }

            var cycleDays = profile is not null
                            && profile.MaintenanceKind == HdbfMaintenanceKind.PowerOrOffGridMetal
                ? profile.OffGridMaintenanceCycleDays
                : profile?.MaintenanceCycleDays ?? 0f;
            return HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelMaintenanceMaterialValue,
                HdbfIndoorPanelFormat.Text(localize, materialKey), count,
                HdbfIndoorPanelFormat.Count(PanelNumbers.RoundedCount(cycleDays)));
        }
    }
}
