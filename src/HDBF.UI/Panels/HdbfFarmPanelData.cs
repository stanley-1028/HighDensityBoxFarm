using System;
using System.Collections.Generic;
using System.Globalization;
using HDBF.Core;
using HDBF.Core.Abstractions;
using HDBF.Core.Balance;
using HDBF.Core.Formatting;
using HDBF.Core.Warnings;
using HDBF.UI.Localization;
using Timberborn.Localization;

namespace HDBF.UI.Panels
{
    /// <summary>
    /// Everything the entity panel shows for one box farm, already localized and formatted
    /// (chapter 14.1 / 14.5). Built exclusively from <see cref="BoxFarmSnapshot"/> and
    /// <see cref="TierCatalog"/>; it holds no game components and no mutable state, so it can be
    /// rendered by a plain UIElements fragment and reasoned about without the game running.
    /// </summary>
    public sealed record HdbfFarmPanelData
    {
        public bool HasData { get; init; }

        public long EntityId { get; init; }

        public HdbfTier Tier { get; init; }

        /// <summary>Raw mode, used by the panel to mark the active mode button (REQ-UI-06).</summary>
        public HdbfMode ModeValue { get; init; }

        /// <summary>True while maintenance shutdown is active (the P0 status row is present then).</summary>
        public bool IsShutdown { get; init; }

        /// <summary>
        /// False for the irrigation-only tier: the maintenance action is then disabled with the
        /// "no material upkeep" tooltip (REQ-UI-07).
        /// </summary>
        public bool CanPerformMaintenance { get; init; }

        public string Title { get; init; } = string.Empty;

        public string Mode { get; init; } = string.Empty;

        public string Workers { get; init; } = string.Empty;

        /// <summary>Registered plots / capacity, e.g. "0/60". Shows the real registry, never a
        /// capacity-derived stand-in (chapter 11.3 risk note).</summary>
        public string RegisteredPlots { get; init; } = string.Empty;

        public string EffectivePlots { get; init; } = string.Empty;

        public string EffectivePlotsTooltip { get; init; } = string.Empty;

        /// <summary>Accepted scan candidates ("可开垦格"), or the unknown glyph while unknown.</summary>
        public string TillablePlots { get; init; } = string.Empty;

        /// <summary>Tillable explanation + work-range area (121/225/361/729); tooltip only.</summary>
        public string TillablePlotsTooltip { get; init; } = string.Empty;

        public string Irrigation { get; init; } = string.Empty;

        public string IrrigationTooltip { get; init; } = string.Empty;

        public string FatigueAverage { get; init; } = string.Empty;

        public string FatigueMax { get; init; } = string.Empty;

        public string Maintenance { get; init; } = string.Empty;

        public string TankWater { get; init; } = string.Empty;

        public string Power { get; init; } = string.Empty;

        public string PowerPort { get; init; } = string.Empty;

        public string PipePort { get; init; } = string.Empty;

        public string LogisticsPort { get; init; } = string.Empty;

        /// <summary>Raw pipe port state; drives the toggle enabled/checked state (chapter 8.3).</summary>
        public HdbfPortState PipePortState { get; init; }

        /// <summary>Raw power port state; the power port follows the mechanical network.</summary>
        public HdbfPortState PowerPortState { get; init; }

        /// <summary>Raw logistics port state; not exposed by every tier.</summary>
        public HdbfPortState LogisticsPortState { get; init; }

        public string MultiplierBreakdown { get; init; } = string.Empty;

        /// <summary>Warnings sorted by severity (P0 first); already capped at three + an overflow row.</summary>
        public IReadOnlyList<HdbfFarmStatusRow> StatusRows { get; init; } = Array.Empty<HdbfFarmStatusRow>();

        /// <summary>
        /// True while this farm's managed-plot overlay is the session's active highlight
        /// (REQ-UI-05, chapter 14.3). Copied from the authoritative snapshot so the toggle button
        /// and legend keep no local optimistic state: after a rejected command the next snapshot
        /// decides what is shown.
        /// </summary>
        public bool HighlightActive { get; init; }

        /// <summary>
        /// Localized highlight-button tooltip: the click / Shift-drag / Esc operating hint while
        /// inactive, the Shift-drag box-select hint while active (chapter 14.3).
        /// </summary>
        public string HighlightButtonTooltip { get; init; } = string.Empty;

        /// <summary>Box-select hint line; the fragment shows it only while <see cref="HighlightActive"/>.</summary>
        public string HighlightHint { get; init; } = string.Empty;

        /// <summary>
        /// Localized reason of the last rejected world-space plot action (click exclude/restore or
        /// box select), or empty when the last action succeeded / none ran. Rendered in the same
        /// action-status line as local command feedback (chapter 14.3: 注册格不足必须提示).
        /// </summary>
        public string HighlightFailure { get; init; } = string.Empty;

        /// <summary>
        /// The four overlay states in <see cref="HdbfPlotHighlightPalette.LegendOrder"/>, shown as a
        /// legend while the overlay is active. Fixed order, never filtered by what is on screen.
        /// </summary>
        public IReadOnlyList<HdbfHighlightLegendRow> HighlightLegend { get; init; } =
            Array.Empty<HdbfHighlightLegendRow>();

        /// <summary>Rendered when the selected entity is not (yet) a readable box farm.</summary>
        public static HdbfFarmPanelData Empty { get; } = new();

        /// <summary>
        /// Cross-building warning rows (chapter 14.2 "同类警告跨建筑聚合", chapter 14.6 cached /
        /// stably sorted). One row per warning of this farm that other on-screen farms also report;
        /// each row can be expanded to a list of locatable buildings. Empty when no other farm has
        /// the same warning or when the aggregation cache is empty.
        /// </summary>
        public IReadOnlyList<HdbfWarningAggregateRow> WarningAggregates { get; init; } =
            Array.Empty<HdbfWarningAggregateRow>();

        /// <summary>Explain text of the aggregation rows (why only on-screen farms are counted).</summary>
        public string WarningAggregateTooltip { get; init; } = string.Empty;
    }

    /// <summary>
    /// One cross-building aggregation row. <see cref="Key"/> is the warning localization key and
    /// the fragment's expand/collapse identity; <see cref="Members"/> are the *other* farms
    /// (never the selected one) sorted by entity id by the Core aggregator.
    /// </summary>
    public sealed record HdbfWarningAggregateRow
    {
        public string Key { get; init; } = string.Empty;

        public FarmWarningSeverity Severity { get; init; }

        /// <summary>Localized "Also N buildings: &lt;warning&gt;" row text.</summary>
        public string Text { get; init; } = string.Empty;

        /// <summary>Number of other farms in this group (the N of the row text).</summary>
        public int OtherFarmCount { get; init; }

        public IReadOnlyList<HdbfWarningAggregateMemberRow> Members { get; init; } =
            Array.Empty<HdbfWarningAggregateMemberRow>();
    }

    /// <summary>One locatable member row of an aggregate group; the whole row is a Button.</summary>
    public sealed record HdbfWarningAggregateMemberRow
    {
        public Guid EntityId { get; init; }

        public FarmWarningSeverity Severity { get; init; }

        /// <summary>Localized tier name of the other farm.</summary>
        public string Text { get; init; } = string.Empty;
    }

    /// <summary>
    /// One row of the managed-plot legend (REQ-UI-05). Deliberately holds only a string and the raw
    /// palette value: <see cref="Rgba"/> is the Core <c>0xRRGGBBAA</c> constant and the fragment
    /// converts it to a UIElements colour, so this data layer stays free of UnityEngine types while
    /// the legend can never drift from the world overlay's palette (chapter 14.3 single source).
    /// </summary>
    public sealed record HdbfHighlightLegendRow
    {
        public string Text { get; init; } = string.Empty;

        /// <summary>State colour as <c>0xRRGGBBAA</c>, exactly <see cref="HdbfPlotHighlightPalette.Rgba"/>.</summary>
        public uint Rgba { get; init; }
    }

    /// <summary>
    /// Pure mapping from the Core snapshot to panel strings (REQ-UI-01/02/11). Formatting rules:
    /// percentages are integers (irrigation floored, fatigue ceiled), days have one decimal,
    /// multipliers two decimals; unknown/NaN/Infinity values render as the "unknown" key instead
    /// of a number (chapter 14.5).
    /// </summary>
    public static class HdbfFarmPanelDataFactory
    {
        /// <summary>At most three warnings are visible, the rest collapse into one row (REQ-UI-03).</summary>
        public const int MaxVisibleWarnings = 3;

        /// <summary>At most three cross-building aggregate rows (keeps the status area bounded).</summary>
        public const int MaxWarningAggregates = 3;

        private const string Unknown = PanelNumbers.Unknown;

        public static HdbfFarmPanelData Create(BoxFarmSnapshot snapshot, ILoc loc) =>
            Create(snapshot, loc, null);

        /// <summary>
        /// Builds the panel data, including the cross-building warning rows from the aggregation
        /// cache. <paramref name="groups"/> may be null/empty (no other farm reports this warning);
        /// the parameter keeps the factory pure so L1 tests can pass synthetic groups.
        /// </summary>
        public static HdbfFarmPanelData Create(BoxFarmSnapshot snapshot, ILoc loc,
            IReadOnlyList<FarmWarningGroup>? groups)
        {
            if (loc is null)
            {
                throw new ArgumentNullException(nameof(loc));
            }

            if (snapshot is null)
            {
                return HdbfFarmPanelData.Empty;
            }

            var profile = TierCatalog.Get(snapshot.Tier);
            var multipliers = snapshot.Multipliers ?? new FarmMultipliers();

            // The three plot figures of chapter 11.3 are derived in Core (game-free) so the L1
            // runner can verify that registered=0 shows 0 and not a capacity-derived 60/60.
            var plots = PanelPlotFigures.FromSnapshot(snapshot, profile);

            return new HdbfFarmPanelData
            {
                HasData = true,
                EntityId = snapshot.EntityId,
                Tier = snapshot.Tier,
                ModeValue = snapshot.Mode,
                IsShutdown = snapshot.IsShutdown,
                CanPerformMaintenance = profile.MaintenanceKind != HdbfMaintenanceKind.IrrigationOnly,
                Title = loc.T(NameKey(snapshot.Tier)),
                Mode = loc.T(ModeKey(snapshot.Mode)),
                Workers = loc.T(HdbfUiLocKeys.PanelWorkers, snapshot.ActiveWorkers, snapshot.MaxWorkers),
                RegisteredPlots = loc.T(HdbfUiLocKeys.PanelRegisteredPlots,
                    plots.RegisteredPlots, plots.PlotCapacity),
                EffectivePlots = loc.T(HdbfUiLocKeys.PanelEffectivePlots, snapshot.EffectivePlots, profile.PlotCapacity),
                EffectivePlotsTooltip = loc.T(HdbfUiLocKeys.TooltipEffectivePlots,
                    profile.PlotCapacity, snapshot.ActiveWorkers, TierCatalog.PlotsPerFarmer),
                TillablePlots = loc.T(HdbfUiLocKeys.PanelTillablePlots, plots.TillableText),
                TillablePlotsTooltip = loc.T(HdbfUiLocKeys.TooltipTillablePlots,
                    plots.WorkRange, plots.WorkRange, plots.WorkRangeCells),
                Irrigation = FormatPercent(loc, HdbfUiLocKeys.PanelIrrigation, snapshot.IrrigationCoverage, roundUp: false),
                IrrigationTooltip = FormatThreshold(loc, snapshot.MinIrrigationCoverage),
                FatigueAverage = FormatPercent(loc, HdbfUiLocKeys.PanelFatigue, snapshot.FatigueAverage, roundUp: true),
                FatigueMax = FormatPercent(loc, HdbfUiLocKeys.PanelFatigueMax, snapshot.FatigueMax, roundUp: true),
                Maintenance = FormatMaintenance(loc, snapshot, profile),
                TankWater = FormatDays(loc, HdbfUiLocKeys.PanelTankWater, snapshot.TankDaysLeft, roundUp: false),
                Power = FormatPower(loc, snapshot),
                PowerPort = FormatPort(loc, HdbfUiLocKeys.PortPower, snapshot.PowerPort),
                PipePort = FormatPort(loc, HdbfUiLocKeys.PortPipe, snapshot.PipePort),
                LogisticsPort = FormatPort(loc, HdbfUiLocKeys.PortLogistics, snapshot.LogisticsPort),
                PipePortState = snapshot.PipePort,
                PowerPortState = snapshot.PowerPort,
                LogisticsPortState = snapshot.LogisticsPort,
                MultiplierBreakdown = FormatMultiplierBreakdown(loc, multipliers),
                StatusRows = BuildStatusRows(loc, snapshot.Warnings),
                WarningAggregates = BuildWarningAggregates(loc, snapshot, groups),
                WarningAggregateTooltip = loc.T(HdbfUiLocKeys.PanelWarningAggregateHint),
                HighlightActive = snapshot.IsPlotHighlightActive,
                HighlightButtonTooltip = HighlightTooltip(loc, snapshot.IsPlotHighlightActive),
                HighlightHint = loc.T(HdbfUiLocKeys.TooltipHighlightBoxSelect),
                // A world-space rejection is authoritative snapshot data; a null key means the last
                // action succeeded, so nothing is shown (no stale "failure" line).
                HighlightFailure = snapshot.PlotActionFailureKey is null
                    ? string.Empty
                    : FormatCommandFailure(loc, snapshot.PlotActionFailureKey),
                HighlightLegend = BuildHighlightLegend(loc),
            };
        }

        public static string NameKey(HdbfTier tier)
        {
            switch (tier)
            {
                case HdbfTier.Intermediate:
                    return HdbfUiLocKeys.BuildingIntermediateName;
                case HdbfTier.Advanced:
                    return HdbfUiLocKeys.BuildingAdvancedName;
                case HdbfTier.Top:
                    return HdbfUiLocKeys.BuildingTopName;
                default:
                    return HdbfUiLocKeys.BuildingBasicName;
            }
        }

        public static string ModeKey(HdbfMode mode)
        {
            switch (mode)
            {
                case HdbfMode.Dense:
                    return HdbfUiLocKeys.ModeDense;
                case HdbfMode.Fallow:
                    return HdbfUiLocKeys.ModeFallow;
                default:
                    return HdbfUiLocKeys.ModeNormal;
            }
        }

        /// <summary>Builds the ordered P0..P4 status rows, capped at three plus an overflow row.</summary>
        public static IReadOnlyList<HdbfFarmStatusRow> BuildStatusRows(ILoc loc,
            IReadOnlyList<FarmWarning>? warnings)
        {
            if (warnings is null || warnings.Count == 0)
            {
                return Array.Empty<HdbfFarmStatusRow>();
            }

            var ordered = new List<FarmWarning>(warnings.Count);
            foreach (var warning in warnings)
            {
                if (warning is not null)
                {
                    ordered.Add(warning);
                }
            }

            ordered.Sort(CompareWarnings);

            var rows = new List<HdbfFarmStatusRow>(MaxVisibleWarnings + 1);
            var visible = Math.Min(MaxVisibleWarnings, ordered.Count);
            for (var i = 0; i < visible; i++)
            {
                rows.Add(new HdbfFarmStatusRow
                {
                    Text = FormatWarning(loc, ordered[i]),
                    Severity = ordered[i].Severity,
                });
            }

            if (ordered.Count > MaxVisibleWarnings)
            {
                rows.Add(new HdbfFarmStatusRow
                {
                    Text = loc.T(HdbfUiLocKeys.WarningMoreWarnings, ordered.Count - MaxVisibleWarnings),
                    Severity = FarmWarningSeverity.Info,
                });
            }

            return rows;
        }

        /// <summary>
        /// Builds the cross-building rows (chapter 14.2/14.6) from the cached Core groups.
        ///
        /// For each warning of the selected farm the matching group is looked up; the row shows
        /// "Also N buildings: &lt;warning&gt;" where N excludes the selected farm itself. Group
        /// members the player can jump to are exposed as member rows (the selected farm is never a
        /// member row). Rows are ordered by the same P0..P4 warning order as the status rows and
        /// capped at <see cref="MaxWarningAggregates"/>.
        /// </summary>
        public static IReadOnlyList<HdbfWarningAggregateRow> BuildWarningAggregates(ILoc loc,
            BoxFarmSnapshot? snapshot, IReadOnlyList<FarmWarningGroup>? groups)
        {
            if (loc is null || snapshot is null || groups is null || groups.Count == 0
                || snapshot.Warnings is null || snapshot.Warnings.Count == 0)
            {
                return Array.Empty<HdbfWarningAggregateRow>();
            }

            var ordered = new List<FarmWarning>(snapshot.Warnings.Count);
            for (var i = 0; i < snapshot.Warnings.Count; i++)
            {
                if (snapshot.Warnings[i] is not null)
                {
                    ordered.Add(snapshot.Warnings[i]);
                }
            }

            ordered.Sort(CompareWarnings);
            var rows = new List<HdbfWarningAggregateRow>(MaxWarningAggregates);
            var seenKeys = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < ordered.Count && rows.Count < MaxWarningAggregates; i++)
            {
                var warning = ordered[i];
                if (!seenKeys.Add(warning.CodeKey))
                {
                    continue;
                }

                var group = FarmWarningAggregator.FindGroup(groups, warning.CodeKey);
                if (group is null || group.Members is null || group.Members.Count == 0)
                {
                    continue;
                }

                var members = new List<HdbfWarningAggregateMemberRow>(group.Members.Count);
                var containsSelected = false;
                var selectedId = snapshot.RuntimeEntityId;
                for (var m = 0; m < group.Members.Count; m++)
                {
                    var member = group.Members[m];
                    if (member is null)
                    {
                        continue;
                    }

                    if (selectedId != Guid.Empty && member.EntityId == selectedId)
                    {
                        containsSelected = true;
                        continue;
                    }

                    if (member.EntityId == Guid.Empty)
                    {
                        continue;
                    }

                    members.Add(new HdbfWarningAggregateMemberRow
                    {
                        EntityId = member.EntityId,
                        Severity = member.Severity,
                        Text = loc.T(NameKey(member.Tier)),
                    });
                }

                var others = group.Count - (containsSelected ? 1 : 0);
                if (others <= 0 || members.Count == 0)
                {
                    continue;
                }

                rows.Add(new HdbfWarningAggregateRow
                {
                    Key = warning.CodeKey,
                    Severity = warning.Severity,
                    Text = loc.T(HdbfUiLocKeys.PanelWarningAggregate, others, FormatWarning(loc, warning)),
                    OtherFarmCount = others,
                    Members = members,
                });
            }

            return rows;
        }

        /// <summary>Localizes one warning. Unknown keys degrade to a generic localized row, never a raw key.</summary>
        public static string FormatWarning(ILoc loc, FarmWarning warning)
        {
            switch (warning.CodeKey)
            {
                case HdbfUiLocKeys.WarningShutdown:
                case HdbfUiLocKeys.WarningEntranceBlocked:
                    return loc.T(warning.CodeKey);

                case HdbfUiLocKeys.WarningIrrigationLow:
                case HdbfUiLocKeys.WarningNoWater:
                    return loc.T(warning.CodeKey, AsPercent(warning.NumberArg0), AsPercent(warning.NumberArg1));

                case HdbfUiLocKeys.WarningMaintenanceOverdue:
                    return loc.T(warning.CodeKey, AsCount(warning.NumberArg0));

                case HdbfUiLocKeys.WarningFatigueHigh:
                case HdbfUiLocKeys.WarningNoPower:
                    return loc.T(warning.CodeKey, AsPercent(warning.NumberArg0));

                case HdbfUiLocKeys.WarningTankLow:
                    return loc.T(warning.CodeKey, FormatDaysValue(warning.NumberArg0, roundUp: false));

                // Gameplay passes the number of affected adapters, not an adapter id: the Core
                // snapshot contract has no string payload. Show the count; see README "Known gaps".
                case HdbfUiLocKeys.WarningAdapterDisabled:
                case HdbfUiLocKeys.WarningAdapterFailed:
                    return loc.T(warning.CodeKey, AsCount(warning.NumberArg0));

                default:
                    return loc.T(HdbfUiLocKeys.WarningGeneric, Unknown);
            }
        }

        private static int CompareWarnings(FarmWarning left, FarmWarning right)
        {
            var bySeverity = left.Severity.CompareTo(right.Severity);
            if (bySeverity != 0)
            {
                return bySeverity;
            }

            return string.CompareOrdinal(left.CodeKey, right.CodeKey);
        }

        private static string FormatPercent(ILoc loc, string key, float value, bool roundUp)
        {
            // Percent(value) already renders non-finite input as "—" (chapter 14.5).
            return loc.T(key, PanelNumbers.Percent(value, roundUp));
        }

        private static string FormatThreshold(ILoc loc, float minimumCoverage)
        {
            return loc.T(HdbfUiLocKeys.PanelIrrigationThreshold, PanelNumbers.Percent(minimumCoverage, roundUp: false));
        }

        private static string FormatMaintenance(ILoc loc, BoxFarmSnapshot snapshot, TierProfile profile)
        {
            if (profile.MaintenanceKind == HdbfMaintenanceKind.IrrigationOnly)
            {
                return loc.T(HdbfUiLocKeys.PanelMaintenanceNone);
            }

            return loc.T(HdbfUiLocKeys.PanelMaintenanceDue, PanelNumbers.DaysRounded(snapshot.MaintenanceDaysLeft));
        }

        private static string FormatDays(ILoc loc, string key, float days, bool roundUp)
        {
            return loc.T(key, PanelNumbers.Days(days, roundUp));
        }

        private static string FormatDaysValue(float days, bool roundUp)
        {
            return PanelNumbers.Days(days, roundUp);
        }

        private static string FormatPower(ILoc loc, BoxFarmSnapshot snapshot)
        {
            if (!snapshot.TierNeedsPower)
            {
                return loc.T(HdbfUiLocKeys.PanelPowerNotRequired);
            }

            return snapshot.PowerConnected
                ? loc.T(HdbfUiLocKeys.PanelPowerConnected)
                : loc.T(HdbfUiLocKeys.PanelPowerOffGrid);
        }

        private static string FormatPort(ILoc loc, string labelKey, HdbfPortState state)
        {
            var label = loc.T(labelKey);
            var stateText = loc.T(PortStateKey(state));
            return label + ": " + stateText;
        }

        /// <summary>Maps the port state machine to the four player-visible port states (chapter 8.3).</summary>
        public static string PortStateKey(HdbfPortState state)
        {
            switch (state)
            {
                case HdbfPortState.Connected:
                    return HdbfUiLocKeys.PortConnected;
                case HdbfPortState.Available:
                    return HdbfUiLocKeys.PortNotConnected;
                case HdbfPortState.Fault:
                    return HdbfUiLocKeys.PortDisabled;
                default:
                    return HdbfUiLocKeys.PortNotApplicable;
            }
        }

        /// <summary>
        /// True when the port state supports a player toggle. Hidden (tier has no such port) and
        /// Fault (port disabled) render as a disabled control with the state text as explanation
        /// (chapter 8.3, REQ-UI-07).
        /// </summary>
        public static bool IsPortToggleEnabled(HdbfPortState state) =>
            state == HdbfPortState.Available || state == HdbfPortState.Connected;

        /// <summary>Checked state of a port toggle.</summary>
        public static bool IsPortConnected(HdbfPortState state) => state == HdbfPortState.Connected;

        /// <summary>
        /// Localized hover hint for a mode button: the cost preview required before switching
        /// (REQ-UI-06). The active button gets the "current mode" hint instead.
        /// </summary>
        public static string ModeTooltip(ILoc loc, HdbfMode mode, HdbfMode currentMode)
        {
            if (mode == currentMode)
            {
                return loc.T(HdbfUiLocKeys.TooltipCurrentMode);
            }

            switch (mode)
            {
                case HdbfMode.Dense:
                    return loc.T(HdbfUiLocKeys.TooltipModeDense);
                case HdbfMode.Fallow:
                    return loc.T(HdbfUiLocKeys.TooltipModeFallow);
                default:
                    return loc.T(HdbfUiLocKeys.TooltipModeNormal);
            }
        }

        /// <summary>
        /// Localized hover hint for a port toggle. Disabled toggles fall back to the state text so
        /// the reason is visible even though a disabled UIElements control receives no hover.
        /// </summary>
        public static string PortTooltip(ILoc loc, string labelKey, HdbfPortKind kind, HdbfPortState state)
        {
            if (!IsPortToggleEnabled(state))
            {
                return loc.T(PortStateKey(state));
            }

            if (kind == HdbfPortKind.Power)
            {
                return loc.T(HdbfUiLocKeys.TooltipPortPower);
            }

            return loc.T(HdbfUiLocKeys.TooltipPortToggle, loc.T(labelKey));
        }

        /// <summary>Localized hover hint for the maintenance button (REQ-UI-07).</summary>
        public static string MaintenanceTooltip(ILoc loc, bool canPerformMaintenance, bool isShutdown)
        {
            if (!canPerformMaintenance)
            {
                return loc.T(HdbfUiLocKeys.PanelMaintenanceNone);
            }

            return isShutdown
                ? loc.T(HdbfUiLocKeys.TooltipMaintenanceShutdown)
                : loc.T(HdbfUiLocKeys.TooltipMaintenance);
        }

        /// <summary>
        /// Builds the four legend rows from <see cref="HdbfPlotHighlightPalette.LegendOrder"/>, so the
        /// panel order can never drift from the documented chapter-14.3 order
        /// (irrigated, dry, excluded, unreachable) or from the world overlay's colours.
        /// </summary>
        public static IReadOnlyList<HdbfHighlightLegendRow> BuildHighlightLegend(ILoc loc)
        {
            var order = HdbfPlotHighlightPalette.LegendOrder;
            var rows = new List<HdbfHighlightLegendRow>(order.Count);
            for (var i = 0; i < order.Count; i++)
            {
                var state = order[i];
                rows.Add(new HdbfHighlightLegendRow
                {
                    Text = loc.T(HighlightStateKey(state)),
                    Rgba = HdbfPlotHighlightPalette.Rgba(state),
                });
            }

            return rows;
        }

        /// <summary>Localization key of one overlay state's legend label (chapter 14.4 category Panel).</summary>
        public static string HighlightStateKey(HdbfPlotHighlightState state)
        {
            switch (state)
            {
                case HdbfPlotHighlightState.Irrigated:
                    return HdbfUiLocKeys.PanelHighlightIrrigated;
                case HdbfPlotHighlightState.Dry:
                    return HdbfUiLocKeys.PanelHighlightDry;
                case HdbfPlotHighlightState.Excluded:
                    return HdbfUiLocKeys.PanelHighlightExcluded;
                default:
                    return HdbfUiLocKeys.PanelHighlightUnreachable;
            }
        }

        /// <summary>
        /// Highlight button tooltip. While the overlay is active the next useful interaction is the
        /// Shift-drag box select, otherwise the click / Shift-drag / Esc operating hint is shown.
        /// Both are shipped description-style keys, so no hint is hard-coded in the fragment.
        /// </summary>
        public static string HighlightTooltip(ILoc loc, bool active)
        {
            return loc.T(active
                ? HdbfUiLocKeys.TooltipHighlightBoxSelect
                : HdbfUiLocKeys.TooltipHighlightPlots);
        }

        /// <summary>
        /// Localizes a failure key returned by <c>IBoxFarmCommand</c>. Only keys this assembly
        /// ships are formatted; an unexpected/null key degrades to the generic action-failed text,
        /// so a raw localization key can never reach the player (REQ-UI-08/09).
        /// </summary>
        public static string FormatCommandFailure(ILoc loc, string? failureKey)
        {
            switch (failureKey)
            {
                case HdbfUiLocKeys.ErrorMaterialsMissing:
                case HdbfUiLocKeys.ErrorDenseDisabled:
                case HdbfUiLocKeys.ErrorMissingMod:
                case HdbfUiLocKeys.ErrorNotBoxFarm:
                case HdbfUiLocKeys.ErrorPlotMinimum:
                case HdbfUiLocKeys.ErrorPlotNotRegistered:
                case HdbfUiLocKeys.ErrorActionFailed:
                case HdbfUiLocKeys.ModeSwitchCooldown:
                case HdbfUiLocKeys.PortNotApplicable:
                case HdbfUiLocKeys.PortPowerManaged:
                    return loc.T(failureKey);
                default:
                    return loc.T(HdbfUiLocKeys.ErrorActionFailed);
            }
        }

        private static string FormatMultiplierBreakdown(ILoc loc, FarmMultipliers multipliers)
        {
            // ILoc only formats up to three arguments, so the five-placeholder tooltip is
            // formatted from its localized template with invariant culture (chapter 14.5).
            var template = loc.T(HdbfUiLocKeys.TooltipMultiplierBreakdown);
            return string.Format(CultureInfo.InvariantCulture, template,
                PanelNumbers.Multiplier(multipliers.Irrigation),
                PanelNumbers.Multiplier(multipliers.Fatigue),
                PanelNumbers.Multiplier(multipliers.Maintenance),
                PanelNumbers.Multiplier(multipliers.Power),
                PanelNumbers.Multiplier(multipliers.Mode));
        }

        /// <summary>
        /// Warning arguments for percent placeholders are percent points (0..100): gameplay
        /// multiplies coverage/fatigue/off-grid efficiency by 100 before writing
        /// <see cref="FarmWarning"/>, see HDBF.Gameplay.Components.HdbWarningEvaluator.
        /// </summary>
        private static int AsPercent(float value) => PanelNumbers.RoundedCount(value);

        private static int AsCount(float value) => PanelNumbers.RoundedCount(value);
    }
}
