// ============================================================================
// HDBF v2 | Layer: UI/Panels | Owner: V2-UI-01
// Purpose: Game-free read model of the v2 indoor entity panel (INTERFACES section 9 DTO in,
//          localized card/row tree out). Every visible string is already localized here, and the
//          rows carry a semantic icon key, so the UIElements fragment is a dumb renderer and the
//          whole mapping stays testable offline. No game type appears in this file.
// Requirements: REQ-V2-UI-01/02/03/04, REQ-V2-READ-04, REQ-V2-WATER-03
// ============================================================================
using System;
using System.Collections.Generic;
using HDBF.Core;
using HDBF.Core.Indoor;

namespace HDBF.UI.Panels
{
    /// <summary>
    /// Card order of the panel (docs/REQ_V2_SECTION_19.md 19.11 plus the worker/efficiency card).
    /// The fragment renders the list in order, so the order is part of the contract.
    /// </summary>
    public enum HdbfPanelSectionKind
    {
        /// <summary>Workers and the irrigation growth efficiency.</summary>
        Workers,

        /// <summary>Slot capacity, configured slots, P_eff, empty/growing/mature/backlog.</summary>
        Slots,

        /// <summary>Tank, days, coverage C, minimum C_min, supply, shortage derate.</summary>
        Water,

        /// <summary>Maintenance due day, overdue cycles, missing material.</summary>
        Maintenance,

        /// <summary>Power contract of the tier (grid/off-grid/no power/not required).</summary>
        Power,

        /// <summary>Crop strategy: the current policy row owns the vanilla dropdown.</summary>
        Crops,

        /// <summary>Output inventory occupancy and logistics/pipe/power port states.</summary>
        Output,
    }

    /// <summary>
    /// One panel row: a semantic icon key, a localized label, a localized value, an optional
    /// progress bar and an optional warning marker. The icon key is resolved to a vanilla sprite
    /// by the game-facing renderer; the pure model only names the meaning.
    /// </summary>
    public sealed record HdbfPanelRow
    {
        /// <summary>Semantic icon name, see <see cref="HdbfIndoorPanelStyle"/>; empty for no icon.</summary>
        public string IconKey { get; init; } = string.Empty;

        /// <summary>Localized row label (left of the value).</summary>
        public string Label { get; init; } = string.Empty;

        /// <summary>Localized row value (right aligned).</summary>
        public string Value { get; init; } = string.Empty;

        /// <summary>Localized hover text; empty when the row has no tooltip.</summary>
        public string Tooltip { get; init; } = string.Empty;

        /// <summary>True when the row is a warning sentence rendered with an icon + text marker.</summary>
        public bool IsWarning { get; init; }

        /// <summary>Plain-text severity marker ("▲"/"!"/"•") when <see cref="IsWarning"/> is set.</summary>
        public string Marker { get; init; } = string.Empty;

        /// <summary>True when the row owns a vanilla progress bar.</summary>
        public bool HasBar { get; init; }

        /// <summary>Progress bar fill in [0,1]; 0 when there is no bar.</summary>
        public float Bar { get; init; }

        /// <summary>Text drawn inside the progress bar (value + unit).</summary>
        public string BarText { get; init; } = string.Empty;

        /// <summary>Optional marker position on the bar (C_min on the coverage bar), NaN for none.</summary>
        public float BarMarker { get; init; } = float.NaN;
    }

    /// <summary>One card of the panel: title, tone and the ordered rows inside it.</summary>
    public sealed record HdbfPanelSection
    {
        /// <summary>Card identity; the fragment uses it to attach the crop dropdown.</summary>
        public HdbfPanelSectionKind Kind { get; init; }

        /// <summary>Localized card title.</summary>
        public string Title { get; init; } = string.Empty;

        /// <summary>Semantic icon key of the card title.</summary>
        public string IconKey { get; init; } = string.Empty;

        /// <summary>
        /// Vanilla card tone: "green" (default sub-box), "blue" (info), "purple" (header) or
        /// "red" (warning). Resolved to the real USS class by <see cref="HdbfIndoorPanelStyle"/>.
        /// </summary>
        public string Tone { get; init; } = "green";

        /// <summary>Ordered rows of the card.</summary>
        public IReadOnlyList<HdbfPanelRow> Rows { get; init; } = Array.Empty<HdbfPanelRow>();
    }

    /// <summary>One read-only roster chip: a worker name or a vacant position.</summary>
    public sealed record HdbfWorkerChip
    {
        /// <summary>Worker name, or the localized "—" when the name is not exposed to mods.</summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>True for an unstaffed position.</summary>
        public bool Vacant { get; init; }

        /// <summary>Localized hover text (assigns the read-only roster behaviour).</summary>
        public string Tooltip { get; init; } = string.Empty;
    }

    /// <summary>One entry of the fixed-crop dropdown. An empty <see cref="Id"/> means auto rotation.</summary>
    public sealed record HdbfCropChoice
    {
        /// <summary>Crop id; empty selects the automatic rotation policy.</summary>
        public string Id { get; init; } = string.Empty;

        /// <summary>Already-localized crop name; this is also the dropdown display text.</summary>
        public string Text { get; init; } = string.Empty;
    }

    /// <summary>
    /// Everything the v2 indoor entity panel shows, already localized and formatted from
    /// <see cref="IndoorFarmSnapshot"/> plus the Core tier/crop tables (REQ-V2-UI-01/02). The
    /// fragment only applies these values; it never formats a raw snapshot value itself.
    /// </summary>
    public sealed record HdbfIndoorPanelData
    {
        /// <summary>False when the snapshot was missing; the fragment stays hidden then.</summary>
        public bool HasData { get; init; }

        // ----- header: tier icon, name, subtitle, mode badge --------------------------------
        /// <summary>Localized building name of the tier (the panel title).</summary>
        public string Title { get; init; } = string.Empty;

        /// <summary>Localized subtitle "{tier} · {faction}".</summary>
        public string Subtitle { get; init; } = string.Empty;

        /// <summary>Localized current mode (rendered as the vanilla badge).</summary>
        public string ModeBadge { get; init; } = string.Empty;

        /// <summary>One-day mode transition sentence, empty when no transition is running.</summary>
        public string ModeTransitionText { get; init; } = string.Empty;

        /// <summary>Hover hint of the header card.</summary>
        public string HeaderTooltip { get; init; } = string.Empty;

        /// <summary>Raw tier, for the game-facing tier-icon lookup.</summary>
        public HdbfTier Tier { get; init; }

        /// <summary>Raw faction, for diagnostics and the subtitle.</summary>
        public HdbfFaction Faction { get; init; }

        /// <summary>Raw mode, so the fragment can mark the active mode radio button.</summary>
        public IndoorMode Mode { get; init; }

        // ----- worker roster (read-only) -----------------------------------------------------
        /// <summary>One chip per worker slot; filled chips show "—" plus the explanation.</summary>
        public IReadOnlyList<HdbfWorkerChip> WorkerChips { get; init; } = Array.Empty<HdbfWorkerChip>();

        /// <summary>Localized explanation of why names are not shown.</summary>
        public string WorkerRosterNote { get; init; } = string.Empty;

        // ----- crop strategy (one row + vanilla dropdown) ------------------------------------
        /// <summary>Localized label of the crop-policy row.</summary>
        public string CropPolicyLabel { get; init; } = string.Empty;

        /// <summary>Localized current policy sentence ("Auto rotation" / "Fixed crop").</summary>
        public string CropPolicyText { get; init; } = string.Empty;

        /// <summary>Currently selected dropdown id ("" = auto rotation).</summary>
        public string SelectedCropId { get; init; } = string.Empty;

        /// <summary>Dropdown items: auto rotation first, then the faction crop table.</summary>
        public IReadOnlyList<HdbfCropChoice> CropChoices { get; init; } = Array.Empty<HdbfCropChoice>();

        /// <summary>Hover hint explaining rotation versus fixed crop.</summary>
        public string CropTooltip { get; init; } = string.Empty;

        // ----- cards -------------------------------------------------------------------------
        /// <summary>Ordered cards (worker, slots, water, maintenance, power, crops, output).</summary>
        public IReadOnlyList<HdbfPanelSection> Sections { get; init; } = Array.Empty<HdbfPanelSection>();

        // ----- actions -----------------------------------------------------------------------
        /// <summary>True when the refill button may be pressed (manual source, crew, entrance).</summary>
        public bool CanRefill { get; init; }

        /// <summary>Hover hint of the refill button; set even while it is disabled.</summary>
        public string RefillTooltip { get; init; } = string.Empty;

        /// <summary>Inline reason shown under a disabled refill button; empty when enabled.</summary>
        public string RefillReasonText { get; init; } = string.Empty;

        /// <summary>True when the maintenance button may be pressed.</summary>
        public bool CanPerformMaintenance { get; init; }

        /// <summary>Hover hint of the maintenance button; set even while it is disabled.</summary>
        public string MaintenanceTooltip { get; init; } = string.Empty;

        /// <summary>Inline reason shown under a disabled maintenance button; empty when enabled.</summary>
        public string MaintenanceReasonText { get; init; } = string.Empty;

        /// <summary>True while overdue/material-missing: the fragment marks the action as urgent.</summary>
        public bool MaintenanceUrgent { get; init; }
    }
}
