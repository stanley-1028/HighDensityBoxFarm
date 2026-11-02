// ============================================================================
// HDBF v2 | Layer: UI/Panels | Owner: V2-UI-01
// Purpose: Maintenance card builder of the v2 indoor panel read model: next due day, overdue
//          cycles, the automatic-maintenance On/Off row (WP-02), the upkeep-material requirement
//          row (8 plank / 10 days, 6 metal / 10 days, off-grid top 15 metal / 8 days), the
//          catch-up bill of the next click (WP-01: every overdue cycle at once), the
//          missing-material detail and the stalled/overdue warnings. The action gate and reason
//          also live here so the button and the card can never disagree. Split out of
//          HdbfIndoorPanelFactory.Cards.cs to stay inside the 300 line gate.
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
            var rows = new List<HdbfPanelRow>(5)
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
                // WP-02: the per-building automatic-maintenance choice, always visible (On/Off).
                new()
                {
                    IconKey = "maintenance",
                    Label = HdbfIndoorPanelFormat.Text(localize,
                        HdbfUiLocKeys.PanelAutoMaintenanceLabel),
                    Value = HdbfIndoorPanelFormat.Text(localize, snapshot.AutoMaintenance
                        ? HdbfUiLocKeys.PanelAutoMaintenanceOn
                        : HdbfUiLocKeys.PanelAutoMaintenanceOff),
                    Tooltip = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.TooltipAutoMaintenance),
                },
                new()
                {
                    IconKey = "material",
                    Label = HdbfIndoorPanelFormat.Text(localize,
                        HdbfUiLocKeys.PanelMaintenanceMaterialLabel),
                    Value = MaintenanceMaterialValue(snapshot, profile, localize),
                    Tooltip = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.TooltipMaintenance),
                },
            };

            // WP-01/L-08: while cycles are owed, spell out the whole catch-up bill of one click
            // (every overdue cycle at once), so the player knows what the button will consume.
            if (!materialFree && snapshot.MaintenanceCyclesToCatchUp > 0
                && HdbfIndoorPanelFormat.TryGetMaintenanceMaterial(profile, snapshot.Power,
                    snapshot.PowerConnected, out var catchUpMaterialKey, out _))
            {
                rows.Add(new HdbfPanelRow
                {
                    IconKey = "due",
                    Label = HdbfIndoorPanelFormat.Text(localize,
                        HdbfUiLocKeys.PanelMaintenanceCatchUpLabel),
                    Value = HdbfIndoorPanelFormat.Text(localize,
                        HdbfUiLocKeys.PanelMaintenanceCatchUpValue,
                        snapshot.MaintenanceCyclesToCatchUp,
                        snapshot.MaintenanceCatchUpMaterialAmount,
                        HdbfIndoorPanelFormat.Text(localize, catchUpMaterialKey)),
                    Tooltip = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.TooltipMaintenance),
                });
            }

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
        /// Why the maintenance action is unavailable, or null when it may run. Order follows the
        /// player's ability to act: a material-free tier can never maintain, a missing material must
        /// be replenished first, and a not-yet-due cycle is rejected with its own specific reason
        /// (WP-01/L-08: a click must pay every overdue cycle, so 0 cycles means "not due").
        /// </summary>
        private static string? MaintenanceBlockedReason(IndoorFarmSnapshot snapshot, TierProfile? profile,
            Func<string, string> localize)
        {
            if (profile is null || profile.MaintenanceKind == HdbfMaintenanceKind.IrrigationOnly)
            {
                return HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelMaintenanceNone);
            }

            if (snapshot.MaintenanceMaterialMissing
                || (snapshot.Warnings & IndoorWarningFlags.MissingMaterial) != 0)
            {
                return FormatMissingMaterials(snapshot, profile, localize);
            }

            if (snapshot.MaintenanceCyclesToCatchUp <= 0)
            {
                return HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.ErrorMaintenanceNotDue);
            }

            return null;
        }

        private static string MaintenanceTooltip(IndoorFarmSnapshot snapshot, TierProfile? profile,
            string? blockedReason, Func<string, string> localize)
        {
            if (!string.IsNullOrEmpty(blockedReason))
            {
                return blockedReason;
            }

            var materialFree = profile is null || profile.MaintenanceKind == HdbfMaintenanceKind.IrrigationOnly;
            var stalled = (snapshot.Warnings & IndoorWarningFlags.MaintenanceStalled) != 0;
            return HdbfIndoorPanelFormat.Text(localize, materialFree
                ? HdbfUiLocKeys.PanelMaintenanceNone
                : stalled
                    ? HdbfUiLocKeys.TooltipMaintenanceShutdown
                    : HdbfUiLocKeys.TooltipMaintenance);
        }

        private static string FormatMissingMaterials(IndoorFarmSnapshot snapshot, TierProfile? profile,
            Func<string, string> localize)
        {
            if (!HdbfIndoorPanelFormat.TryGetMaintenanceMaterial(profile, snapshot.Power,
                    snapshot.PowerConnected, out var materialKey, out var count))
            {
                return HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.WarningMissingMaterial);
            }

            return HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelMaintenanceMaterials,
                HdbfIndoorPanelFormat.Text(localize, materialKey), count);
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
