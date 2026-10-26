// ============================================================================
// HDBF v2 | Layer: UI/Panels | Owner: V2-UI-01
// Purpose: Game-free read-model of one v2 indoor panel (INTERFACES section 9 DTO only).
//          It carries already-localized strings so the UIElements fragment is a dumb renderer
//          and the whole mapping is testable offline. No game type appears here.
// Requirements: REQ-V2-UI-01/02/03, REQ-V2-READ-04, REQ-V2-WATER-03
// ============================================================================
using System;
using System.Collections.Generic;
using HDBF.Core.Indoor;

namespace HDBF.UI.Panels
{
    /// <summary>
    /// One localized warning row. The severity is also encoded as a text <see cref="Marker"/> so
    /// colour is never the only information carrier (chapter 19.18.1-C).
    /// </summary>
    public sealed record HdbfPanelStatusRow
    {
        /// <summary>Plain-text severity marker ("▲" critical, "!" high, "•" medium); never colour-only.</summary>
        public string Marker { get; init; } = string.Empty;

        /// <summary>Already-localized warning sentence.</summary>
        public string Text { get; init; } = string.Empty;

        /// <summary>P0..P4 priority used for ordering.</summary>
        public IndoorWarningSeverity Severity { get; init; }
    }

    /// <summary>One entry of the fixed-crop dropdown. An empty <see cref="Id"/> means auto rotation.</summary>
    public sealed record HdbfCropChoice
    {
        /// <summary>Crop id; empty selects the automatic rotation policy.</summary>
        public string Id { get; init; } = string.Empty;

        /// <summary>Already-localized crop name.</summary>
        public string Text { get; init; } = string.Empty;
    }

    /// <summary>
    /// Everything the v2 indoor entity panel shows, already localized and formatted from
    /// <see cref="IndoorFarmSnapshot"/> + the Core tier/crop tables (REQ-V2-UI-01/02). The fragment
    /// only applies these strings; it never formats a raw snapshot value itself.
    /// </summary>
    public sealed record HdbfIndoorPanelData
    {
        /// <summary>False when the snapshot was missing; the fragment stays hidden then.</summary>
        public bool HasData { get; init; }

        // ----- header: tier, faction, workers, mode + transition -------------------------------
        /// <summary>Localized building name of the tier (the panel title).</summary>
        public string Title { get; init; } = string.Empty;

        /// <summary>"Tier: Basic" row.</summary>
        public string TierText { get; init; } = string.Empty;

        /// <summary>"Faction: Folktails" row.</summary>
        public string FactionText { get; init; } = string.Empty;

        /// <summary>"Workers W/W_max" row.</summary>
        public string WorkersText { get; init; } = string.Empty;

        /// <summary>"Mode: Dense planting" row.</summary>
        public string ModeText { get; init; } = string.Empty;

        /// <summary>Transition sentence, empty when no transition is running (row hidden).</summary>
        public string ModeTransitionText { get; init; } = string.Empty;

        /// <summary>Raw mode, so the fragment can mark the active mode button.</summary>
        public IndoorMode ModeValue { get; init; }

        // ----- slots: capacity / configured / P_eff / empty / growing / mature / backlog --------
        /// <summary>"Slots configured/capacity" row.</summary>
        public string SlotsText { get; init; } = string.Empty;

        /// <summary>"Effective slots P_eff" row.</summary>
        public string EffectiveSlotsText { get; init; } = string.Empty;

        /// <summary>Tooltip carrying the P_eff = min(capacity, W × 38) formula and inputs.</summary>
        public string EffectiveSlotsTooltip { get; init; } = string.Empty;

        /// <summary>"Empty slots N" row.</summary>
        public string EmptySlotsText { get; init; } = string.Empty;

        /// <summary>"Growing N" row.</summary>
        public string GrowingSlotsText { get; init; } = string.Empty;

        /// <summary>"Ready to harvest N" row.</summary>
        public string MatureSlotsText { get; init; } = string.Empty;

        /// <summary>"Action backlog N" row.</summary>
        public string BacklogText { get; init; } = string.Empty;

        // ----- water: tank units + days, C, C_min, supply, shortage derate ----------------------
        /// <summary>"Tank x/y units (about d days)" row.</summary>
        public string TankText { get; init; } = string.Empty;

        /// <summary>"Coverage C p%" row.</summary>
        public string CoverageText { get; init; } = string.Empty;

        /// <summary>"Minimum C_min p%" row.</summary>
        public string MinCoverageText { get; init; } = string.Empty;

        /// <summary>"Water source: pipe/manual/none" row.</summary>
        public string WaterSupplyText { get; init; } = string.Empty;

        /// <summary>"Water shortage: growth p%" row, empty while irrigation is not derated.</summary>
        public string WaterShortageText { get; init; } = string.Empty;

        /// <summary>Hover hint of the tank row (units + C_min rule).</summary>
        public string TankTooltip { get; init; } = string.Empty;

        /// <summary>Hover hint of the coverage row (C vs C_min behaviour).</summary>
        public string CoverageTooltip { get; init; } = string.Empty;

        /// <summary>True when the refill button may be pressed (manual water source).</summary>
        public bool CanRefill { get; init; }

        /// <summary>Why refill is enabled/disabled; always set.</summary>
        public string RefillTooltip { get; init; } = string.Empty;

        // ----- maintenance: due day, overdue cycles, missing material list ----------------------
        /// <summary>"Maintenance due day D" or the no-material-upkeep sentence.</summary>
        public string MaintenanceDueText { get; init; } = string.Empty;

        /// <summary>"Overdue cycles N" row.</summary>
        public string MaintenanceCyclesText { get; init; } = string.Empty;

        /// <summary>"Missing materials: Plank ×8" row, empty when the stockpile can pay.</summary>
        public string MaintenanceMaterialsText { get; init; } = string.Empty;

        /// <summary>Hover hint of the maintenance block.</summary>
        public string MaintenanceTooltip { get; init; } = string.Empty;

        /// <summary>False for the material-free basic tier: the maintenance action is disabled.</summary>
        public bool CanPerformMaintenance { get; init; }

        /// <summary>True while overdue/material-missing: the fragment emphasises the action.</summary>
        public bool MaintenanceUrgent { get; init; }

        // ----- power: grid / off-grid / power cut / not required ---------------------------------
        /// <summary>"Power: grid/off-grid/no power/not required" row.</summary>
        public string PowerText { get; init; } = string.Empty;

        /// <summary>Hover hint of the power row.</summary>
        public string PowerTooltip { get; init; } = string.Empty;

        // ----- crop strategy: rotation (default) / fixed crop / rotation sequence ----------------
        /// <summary>"Crop policy: auto rotation/fixed crop" row.</summary>
        public string CropPolicyText { get; init; } = string.Empty;

        /// <summary>"Fixed crop: Carrot" row, empty while the policy is rotation.</summary>
        public string FixedCropText { get; init; } = string.Empty;

        /// <summary>"Rotation: ..." row; "—" when the sequence is not known yet.</summary>
        public string RotationText { get; init; } = string.Empty;

        /// <summary>Hover hint explaining rotation vs fixed crop.</summary>
        public string CropTooltip { get; init; } = string.Empty;

        /// <summary>Currently selected dropdown id ("" = auto rotation).</summary>
        public string SelectedCropId { get; init; } = string.Empty;

        /// <summary>Dropdown items: auto rotation first, then the faction crop table.</summary>
        public IReadOnlyList<HdbfCropChoice> CropChoices { get; init; } = Array.Empty<HdbfCropChoice>();

        // ----- output: stock and port state ------------------------------------------------------
        /// <summary>"Output stock N" row.</summary>
        public string OutputStockText { get; init; } = string.Empty;

        /// <summary>"Output port: Connected/Not connected" row.</summary>
        public string OutputPortText { get; init; } = string.Empty;

        /// <summary>Pipe/logistics/power port state sentence.</summary>
        public string PortsText { get; init; } = string.Empty;

        /// <summary>Hover hint of the output block.</summary>
        public string OutputTooltip { get; init; } = string.Empty;

        // ----- status: the four v2 warning categories + concrete blocking reasons ----------------
        /// <summary>Warning rows (water, maintenance, power, entrance), P0 first.</summary>
        public IReadOnlyList<HdbfPanelStatusRow> StatusRows { get; init; } = Array.Empty<HdbfPanelStatusRow>();

        /// <summary>Concrete "why nothing happens" reasons (water/material/workers/entrance/output/crop).</summary>
        public IReadOnlyList<string> ProblemRows { get; init; } = Array.Empty<string>();
    }
}
