namespace HDBF.UI.Localization
{
    /// <summary>
    /// Every localization key the UI code uses, as compile-time constants (chapter 14.4,
    /// REQ-UI-08). Each constant must appear verbatim in <c>data/Localizations/enUS.csv</c>,
    /// <c>zhCN.csv</c> and <c>zhTW.csv</c> (the three files carry identical key sets); the
    /// validator cross-checks these constants against enUS/zhCN, so a typo or a renamed key fails
    /// the build pipeline instead of showing a raw key to the player.
    ///
    /// The <c>Error*</c> constants at the bottom are the failure keys returned by
    /// <c>HDBF.Core.Abstractions.IBoxFarmCommand</c>; their values are duplicated in
    /// <c>HdbfCommandFailures</c> (Core cannot reference UI, so the duplication is deliberate and
    /// listed in the UI report).
    /// </summary>
    public static class HdbfUiLocKeys
    {
        // Tier names (derived from the blueprint id, chapter 14.4).
        public const string BuildingBasicName = "HDBF.Building.BoxFarm.Basic.Name";
        public const string BuildingIntermediateName = "HDBF.Building.BoxFarm.Intermediate.Name";
        public const string BuildingAdvancedName = "HDBF.Building.BoxFarm.Advanced.Name";
        public const string BuildingTopName = "HDBF.Building.BoxFarm.Top.Name";

        // Panel rows (chapter 14.1).
        public const string PanelWorkers = "HDBF.Panel.Workers";

        /// <summary>Registered plots / capacity; chapter 11.3 risk note requires this next to the
        /// effective count so an empty registry cannot hide behind a capacity-derived number.</summary>
        public const string PanelRegisteredPlots = "HDBF.Panel.RegisteredPlots";

        public const string PanelEffectivePlots = "HDBF.Panel.EffectivePlots";

        /// <summary>Accepted candidates of the last scan ("可开垦格"); "—" while unknown.</summary>
        public const string PanelTillablePlots = "HDBF.Panel.TillablePlots";

        public const string PanelIrrigation = "HDBF.Panel.Irrigation";
        public const string PanelIrrigationThreshold = "HDBF.Panel.IrrigationThreshold";
        public const string PanelFatigue = "HDBF.Panel.Fatigue";
        public const string PanelFatigueMax = "HDBF.Panel.FatigueMax";
        public const string PanelMaintenanceDue = "HDBF.Panel.MaintenanceDue";
        public const string PanelMaintenanceNone = "HDBF.Panel.MaintenanceNone";
        public const string PanelTankWater = "HDBF.Panel.TankWater";
        public const string PanelPowerConnected = "HDBF.Panel.PowerConnected";
        public const string PanelPowerOffGrid = "HDBF.Panel.PowerOffGrid";
        public const string PanelPowerNotRequired = "HDBF.Panel.PowerNotRequired";
        public const string PanelMode = "HDBF.Panel.Mode";
        public const string PanelPorts = "HDBF.Panel.Ports";
        public const string PanelStatus = "HDBF.Panel.Status";
        public const string PanelUnknown = "HDBF.Panel.Unknown";

        // Managed-plot overlay legend (REQ-UI-05, chapter 14.3). The header plus one label per
        // HdbfPlotHighlightState are shown only while the overlay is active; the text carries the
        // state and the palette colour is a second encoding (chapter 14.5, never colour-only).
        public const string PanelHighlightLegend = "HDBF.Panel.HighlightLegend";
        public const string PanelHighlightIrrigated = "HDBF.Panel.HighlightIrrigated";
        public const string PanelHighlightDry = "HDBF.Panel.HighlightDry";
        public const string PanelHighlightExcluded = "HDBF.Panel.HighlightExcluded";
        public const string PanelHighlightUnreachable = "HDBF.Panel.HighlightUnreachable";

        // Modes (chapter 14.4).
        public const string ModeNormal = "HDBF.Mode.Normal";
        public const string ModeDense = "HDBF.Mode.Dense";
        public const string ModeFallow = "HDBF.Mode.Fallow";

        // Mode interaction (REQ-UI-06): cost preview, confirmation and cooldown reason.
        public const string ActionConfirmDense = "HDBF.Action.ConfirmDense";
        public const string ActionDenseConfirmHint = "HDBF.Action.DenseConfirmHint";
        public const string ActionModeChanged = "HDBF.Action.ModeChanged";
        public const string ModeSwitchCooldown = "HDBF.Mode.SwitchCooldown";

        // Port labels + port states (chapter 8.3 / 14.4).
        public const string PortPower = "HDBF.Port.Power";
        public const string PortPipe = "HDBF.Port.Pipe";
        public const string PortLogistics = "HDBF.Port.Logistics";
        public const string PortConnected = "HDBF.Port.Connected";
        public const string PortNotConnected = "HDBF.Port.NotConnected";
        public const string PortDisabled = "HDBF.Port.Disabled";
        public const string PortNotApplicable = "HDBF.Port.NotApplicable";

        /// <summary>Port switch failure: the power port follows the mechanical network.</summary>
        public const string PortPowerManaged = "HDBF.Port.PowerManaged";

        // Action area (chapter 14.1 "操作区", REQ-UI-06/07).
        public const string ActionMaintenance = "HDBF.Action.Maintenance";
        public const string ActionMaintenanceDone = "HDBF.Action.MaintenanceDone";
        public const string ActionPortUpdated = "HDBF.Action.PortUpdated";

        /// <summary>Managed-plot highlight toggle button label (REQ-UI-05, chapter 14.3).</summary>
        public const string ActionHighlightPlots = "HDBF.Action.HighlightPlots";

        // Warnings, in P0..P4 order (chapter 14.2).
        public const string WarningShutdown = "HDBF.Warning.Shutdown";
        public const string WarningNoWater = "HDBF.Warning.NoWater";
        public const string WarningIrrigationLow = "HDBF.Warning.IrrigationLow";
        public const string WarningNoPower = "HDBF.Warning.NoPower";
        public const string WarningAdapterFailed = "HDBF.Warning.AdapterFailed";
        public const string WarningMaintenanceOverdue = "HDBF.Warning.MaintenanceOverdue";
        public const string WarningFatigueHigh = "HDBF.Warning.FatigueHigh";
        public const string WarningAdapterDisabled = "HDBF.Warning.AdapterDisabled";
        public const string WarningTankLow = "HDBF.Warning.TankLow";
        public const string WarningEntranceBlocked = "HDBF.Warning.EntranceBlocked";
        public const string WarningMoreWarnings = "HDBF.Warning.MoreWarnings";
        public const string WarningGeneric = "HDBF.Warning.Generic";

        // Cross-building warning aggregation (chapter 14.2: "3 座建築灌溉不足" merged into one row;
        // chapter 14.6: only on-screen buildings, cached and stably sorted).
        public const string PanelWarningAggregate = "HDBF.Panel.WarningAggregate";
        public const string PanelWarningAggregateHint = "HDBF.Panel.WarningAggregateHint";

        // World-icon settings labels (chapter 14.2/14.5, REQ-UI-10). The shipped switch lives in
        // HDBF_Config.json `ui.*`; these labels are the future settings-card text and are shipped
        // now so a settings UI cannot introduce untranslated strings later.
        public const string PanelSettingWorldIcons = "HDBF.Panel.SettingWorldIcons";
        public const string PanelSettingWorldIconBlink = "HDBF.Panel.SettingWorldIconBlink";
        public const string PanelSettingWorldIconDistance = "HDBF.Panel.SettingWorldIconDistance";

        // Error + tooltip text.
        public const string ErrorLoadFailed = "HDBF.Error.LoadFailed";

        // Command failures (values must equal HDBF.Core.Abstractions.HdbfCommandFailures).
        public const string ErrorActionFailed = "HDBF.Error.ActionFailed";
        public const string ErrorDenseDisabled = "HDBF.Error.DenseDisabled";
        public const string ErrorMaterialsMissing = "HDBF.Error.MaterialsMissing";
        public const string ErrorMissingMod = "HDBF.Error.MissingMod";
        public const string ErrorNotBoxFarm = "HDBF.Error.NotBoxFarm";
        public const string ErrorPlotMinimum = "HDBF.Error.PlotMinimum";
        public const string ErrorPlotNotRegistered = "HDBF.Error.PlotNotRegistered";

        // Tooltips (chapter 14.3 hover hints; description-style keys are exempt from the label
        // length limit of chapter 14.4).
        public const string TooltipMultiplierBreakdown = "HDBF.Tooltip.MultiplierBreakdown";
        public const string TooltipEffectivePlots = "HDBF.Tooltip.EffectivePlots";

        /// <summary>Explains the tillable count and carries the work-range area (detail only,
        /// chapter 14.1: 工作范围面积只在详情中展示).</summary>
        public const string TooltipTillablePlots = "HDBF.Tooltip.TillablePlots";
        public const string TooltipModeNormal = "HDBF.Tooltip.ModeNormal";
        public const string TooltipModeDense = "HDBF.Tooltip.ModeDense";
        public const string TooltipModeFallow = "HDBF.Tooltip.ModeFallow";
        public const string TooltipCurrentMode = "HDBF.Tooltip.CurrentMode";
        public const string TooltipMaintenance = "HDBF.Tooltip.Maintenance";
        public const string TooltipMaintenanceShutdown = "HDBF.Tooltip.MaintenanceShutdown";
        public const string TooltipPortToggle = "HDBF.Tooltip.PortToggle";
        public const string TooltipPortPower = "HDBF.Tooltip.PortPower";

        /// <summary>Inactive highlight button tooltip: click / Shift-drag / Esc operating hint.</summary>
        public const string TooltipHighlightPlots = "HDBF.Tooltip.HighlightPlots";

        /// <summary>Shift-drag box-select hint, shown as the active tooltip and the hint line.</summary>
        public const string TooltipHighlightBoxSelect = "HDBF.Tooltip.HighlightBoxSelect";

        /// <summary>
        /// World-space warning icon hover text (chapter 14.2): <c>{0}</c> is the localized warning
        /// text, <c>{1}</c> the configured visibility distance in cells.
        /// </summary>
        public const string TooltipWorldIcon = "HDBF.Tooltip.WorldIcon";
    }
}
