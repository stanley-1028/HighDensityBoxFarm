// ============================================================================
// HDBF v2 | Layer: UI/Localization | Owner: V2-UI-01
// Purpose: Every localization key the v2 indoor panel formats, as compile-time
//          constants. No v1 field terminology survives here (REQ-V2-UI-04).
// Requirements: REQ-V2-UI-02/03/04, REQ-V2-READ-04
// ============================================================================
namespace HDBF.UI.Localization
{
    /// <summary>
    /// Every localization key the v2 indoor panel uses (REQ-V2-UI-03). Each constant must appear
    /// verbatim in <c>data/Localizations/enUS.csv</c>, <c>zhCN.csv</c> and <c>zhTW.csv</c> (the
    /// three files carry identical key sets); <c>tools/validate</c> cross-checks these constants
    /// against the shipped CSVs, and <c>V2LocalizationTests</c> pins the parity from L1.
    ///
    /// The <c>Error*</c> constants are the failure keys returned by <c>IBoxFarmCommand</c> and by
    /// the v2 indoor command surface; their string values are duplicated in gameplay. Core cannot
    /// reference UI, so the duplication is deliberate (same rule as the v1 contract) and the
    /// <c>V2PanelContractTests</c> whitelist keeps an unknown key from reaching the player.
    /// </summary>
    public static class HdbfUiLocKeys
    {
        // Tier building names and descriptions (blueprint localization).
        public const string BuildingBasicName = "HDBF.Building.BoxFarm.Basic.Name";
        public const string BuildingIntermediateName = "HDBF.Building.BoxFarm.Intermediate.Name";
        public const string BuildingAdvancedName = "HDBF.Building.BoxFarm.Advanced.Name";
        public const string BuildingTopName = "HDBF.Building.BoxFarm.Top.Name";
        public const string BuildingBasicDescription = "HDBF.Building.BoxFarm.Basic.Description";
        public const string BuildingIntermediateDescription = "HDBF.Building.BoxFarm.Intermediate.Description";
        public const string BuildingAdvancedDescription = "HDBF.Building.BoxFarm.Advanced.Description";
        public const string BuildingTopDescription = "HDBF.Building.BoxFarm.Top.Description";

        // Header block: tier, faction, workers, mode and the one-day mode transition.
        public const string PanelTier = "HDBF.Panel.Tier";
        public const string PanelFaction = "HDBF.Panel.Faction";
        public const string PanelWorkers = "HDBF.Panel.Workers";
        public const string PanelMode = "HDBF.Panel.Mode";
        public const string PanelModeTransition = "HDBF.Panel.ModeTransition";
        public const string PanelTierBasic = "HDBF.Panel.TierBasic";
        public const string PanelTierIntermediate = "HDBF.Panel.TierIntermediate";
        public const string PanelTierAdvanced = "HDBF.Panel.TierAdvanced";
        public const string PanelTierTop = "HDBF.Panel.TierTop";
        public const string PanelFactionFolktails = "HDBF.Panel.FactionFolktails";
        public const string PanelFactionIronTeeth = "HDBF.Panel.FactionIronTeeth";

        // Section headers (chapter 19.11 block order).
        public const string PanelSectionSlots = "HDBF.Panel.SectionSlots";
        public const string PanelSectionWater = "HDBF.Panel.SectionWater";
        public const string PanelSectionMaintenance = "HDBF.Panel.SectionMaintenance";
        public const string PanelSectionPower = "HDBF.Panel.SectionPower";
        public const string PanelSectionCrops = "HDBF.Panel.SectionCrops";
        public const string PanelSectionOutput = "HDBF.Panel.SectionOutput";

        // Slot block: capacity / configured / P_eff / empty / growing / mature / backlog.
        public const string PanelSlots = "HDBF.Panel.Slots";
        public const string PanelEffectiveSlots = "HDBF.Panel.EffectiveSlots";
        public const string PanelEmptySlots = "HDBF.Panel.EmptySlots";
        public const string PanelGrowingSlots = "HDBF.Panel.GrowingSlots";
        public const string PanelMatureSlots = "HDBF.Panel.MatureSlots";
        public const string PanelBacklog = "HDBF.Panel.Backlog";

        // Water block: tank units / days, C, C_min, supply kind and the shortage derate.
        public const string PanelTank = "HDBF.Panel.Tank";
        public const string PanelCoverage = "HDBF.Panel.Coverage";
        public const string PanelMinCoverage = "HDBF.Panel.MinCoverage";
        public const string PanelWaterSupply = "HDBF.Panel.WaterSupply";
        public const string PanelWaterShortage = "HDBF.Panel.WaterShortage";
        public const string PanelWaterPipe = "HDBF.Panel.WaterPipe";
        public const string PanelWaterManual = "HDBF.Panel.WaterManual";
        public const string PanelWaterNone = "HDBF.Panel.WaterNone";

        // Maintenance block: due day, overdue cycles, missing material list.
        public const string PanelMaintenanceDue = "HDBF.Panel.MaintenanceDue";
        public const string PanelMaintenanceCycles = "HDBF.Panel.MaintenanceCycles";
        public const string PanelMaintenanceMaterials = "HDBF.Panel.MaintenanceMaterials";
        public const string PanelMaintenanceNone = "HDBF.Panel.MaintenanceNone";
        public const string PanelMaterialPlank = "HDBF.Panel.MaterialPlank";
        public const string PanelMaterialMetalBlock = "HDBF.Panel.MaterialMetalBlock";

        // Power block: grid / off-grid / power cut / not required.
        public const string PanelPower = "HDBF.Panel.Power";
        public const string PanelPowerGrid = "HDBF.Panel.PowerGrid";
        public const string PanelPowerOffGrid = "HDBF.Panel.PowerOffGrid";
        public const string PanelPowerNoPower = "HDBF.Panel.PowerNoPower";
        public const string PanelPowerNotRequired = "HDBF.Panel.PowerNotRequired";

        // Crop strategy block: rotation (default) / fixed crop dropdown / rotation sequence.
        public const string PanelCropPolicy = "HDBF.Panel.CropPolicy";
        public const string PanelFixedCrop = "HDBF.Panel.FixedCrop";
        public const string PanelRotation = "HDBF.Panel.Rotation";
        public const string PanelPolicyRotation = "HDBF.Panel.PolicyRotation";
        public const string PanelPolicyFixed = "HDBF.Panel.PolicyFixed";

        // Output block: stock and port states.
        public const string PanelOutputStock = "HDBF.Panel.OutputStock";
        public const string PanelOutputPort = "HDBF.Panel.OutputPort";
        public const string PanelPorts = "HDBF.Panel.Ports";

        // Status area: warning rows an the concrete "why nothing happens" reasons.
        public const string PanelStatus = "HDBF.Panel.Status";
        public const string PanelProblems = "HDBF.Panel.Problems";

        /// <summary>The single "no data yet" glyph; identical in all three languages.</summary>
        public const string PanelUnknown = "HDBF.Panel.Unknown";

        // Crop names for the fixed-crop dropdown (REQ-V2-UI-03 "固定作物").
        public const string CropCarrot = "HDBF.Panel.CropCarrot";
        public const string CropPotato = "HDBF.Panel.CropPotato";
        public const string CropWheat = "HDBF.Panel.CropWheat";
        public const string CropSunflower = "HDBF.Panel.CropSunflower";
        public const string CropCanola = "HDBF.Panel.CropCanola";
        public const string CropCassava = "HDBF.Panel.CropCassava";
        public const string CropCorn = "HDBF.Panel.CropCorn";
        public const string CropEggplant = "HDBF.Panel.CropEggplant";
        public const string CropKohlrabi = "HDBF.Panel.CropKohlrabi";
        public const string CropSoybean = "HDBF.Panel.CropSoybean";

        // Modes (chapter 19.11 header).
        public const string ModeNormal = "HDBF.Mode.Normal";
        public const string ModeDense = "HDBF.Mode.Dense";
        public const string ModeFallow = "HDBF.Mode.Fallow";
        public const string ModeSwitchCooldown = "HDBF.Mode.SwitchCooldown";

        // Port names and shared port states.
        public const string PortPower = "HDBF.Port.Power";
        public const string PortPipe = "HDBF.Port.Pipe";
        public const string PortLogistics = "HDBF.Port.Logistics";
        public const string PortConnected = "HDBF.Port.Connected";
        public const string PortNotConnected = "HDBF.Port.NotConnected";
        public const string PortDisabled = "HDBF.Port.Disabled";
        public const string PortNotApplicable = "HDBF.Port.NotApplicable";

        // Actions: refill, maintenance, mode switch, crop policy.
        public const string ActionMaintenance = "HDBF.Action.Maintenance";
        public const string ActionMaintenanceDone = "HDBF.Action.MaintenanceDone";
        public const string ActionRefill = "HDBF.Action.Refill";
        public const string ActionRefillRequested = "HDBF.Action.RefillRequested";
        public const string ActionModeChanged = "HDBF.Action.ModeChanged";
        public const string ActionCropPolicyChanged = "HDBF.Action.CropPolicyChanged";

        // The four v2 warning categories. Maintenance and entrance blocking are also the text of
        // the two frozen icon slots (Shutdown / AdapterFailed); see HdbfFarmQueryService.
        public const string WarningNoWater = "HDBF.Warning.NoWater";
        public const string WarningIrrigationLow = "HDBF.Warning.IrrigationLow";
        public const string WarningMaintenanceOverdue = "HDBF.Warning.MaintenanceOverdue";
        public const string WarningNoPower = "HDBF.Warning.NoPower";
        public const string WarningEntranceBlocked = "HDBF.Warning.EntranceBlocked";
        public const string WarningShutdown = "HDBF.Warning.Shutdown";
        public const string WarningAdapterFailed = "HDBF.Warning.AdapterFailed";
        public const string WarningOutputFull = "HDBF.Warning.OutputFull";
        public const string WarningNoWorkers = "HDBF.Warning.NoWorkers";
        public const string WarningMissingMaterial = "HDBF.Warning.MissingMaterial";
        public const string WarningInvalidCrop = "HDBF.Warning.InvalidCrop";
        public const string WarningMoreWarnings = "HDBF.Warning.MoreWarnings";
        public const string WarningGeneric = "HDBF.Warning.Generic";

        // Cross-building aggregation + world-icon settings (retained; the panel no longer renders
        // the aggregation rows, but the world icons and the settings labels still ship).
        public const string PanelWarningAggregate = "HDBF.Panel.WarningAggregate";
        public const string PanelWarningAggregateHint = "HDBF.Panel.WarningAggregateHint";
        public const string PanelSettingWorldIcons = "HDBF.Panel.SettingWorldIcons";
        public const string PanelSettingWorldIconBlink = "HDBF.Panel.SettingWorldIconBlink";
        public const string PanelSettingWorldIconDistance = "HDBF.Panel.SettingWorldIconDistance";
        public const string TooltipWorldIcon = "HDBF.Tooltip.WorldIcon";

        // Compatibility adapter labels (Core's compat report keeps resolving these keys).
        public const string CompatMissingMod = "HDBF.Compat.MissingMod";
        public const string CompatShowDetails = "HDBF.Compat.ShowDetails";
        public const string CompatAdapterConveyorBeltsV2 = "HDBF.Compat.Adapter.ConveyorBeltsV2";
        public const string CompatAdapterHoomanStairs = "HDBF.Compat.Adapter.HoomanStairs";
        public const string CompatAdapterMoreMines = "HDBF.Compat.Adapter.MoreMines";
        public const string CompatAdapterPowerLines = "HDBF.Compat.Adapter.PowerLines";
        public const string CompatAdapterTimberPipes = "HDBF.Compat.Adapter.TimberPipes";

        // Command failures (values must equal the gameplay failure keys).
        public const string ErrorActionFailed = "HDBF.Error.ActionFailed";
        public const string ErrorMaterialsMissing = "HDBF.Error.MaterialsMissing";
        public const string ErrorDenseDisabled = "HDBF.Error.DenseDisabled";
        public const string ErrorNotBoxFarm = "HDBF.Error.NotBoxFarm";
        public const string ErrorFarmNotActive = "HDBF.Error.FarmNotActive";
        public const string ErrorRefillUnavailable = "HDBF.Error.RefillUnavailable";
        public const string ErrorCropNotAvailable = "HDBF.Error.CropNotAvailable";
        public const string ErrorMaintenanceUnavailable = "HDBF.Error.MaintenanceUnavailable";
        public const string ErrorInvalidMode = "HDBF.Error.InvalidMode";
        public const string ErrorMissingMod = "HDBF.Error.MissingMod";

        // Specific v2 action failures (values must equal HdbfIndoorCommandFailures in gameplay):
        // the refill/crop commands explain no workers, a blocked entrance, a missing/unreachable/
        // contaminated water source, an empty daily action budget, a pending trip or a full output.
        public const string ErrorNoWorkers = "HDBF.Error.NoWorkers";
        public const string ErrorEntranceBlocked = "HDBF.Error.EntranceBlocked";
        public const string ErrorNoWaterSource = "HDBF.Error.NoWaterSource";
        public const string ErrorWaterSourceUnreachable = "HDBF.Error.WaterSourceUnreachable";
        public const string ErrorWaterContaminated = "HDBF.Error.WaterContaminated";
        public const string ErrorNoActionsLeft = "HDBF.Error.NoActionsLeft";
        public const string ErrorRefillPending = "HDBF.Error.RefillPending";
        public const string ErrorOutputFull = "HDBF.Error.OutputFull";

        // Tooltips (description-style; exempt from the label length rule).
        public const string TooltipEffectiveSlots = "HDBF.Tooltip.EffectiveSlots";
        public const string TooltipTank = "HDBF.Tooltip.Tank";
        public const string TooltipCoverage = "HDBF.Tooltip.Coverage";
        public const string TooltipMaintenance = "HDBF.Tooltip.Maintenance";
        public const string TooltipMaintenanceShutdown = "HDBF.Tooltip.MaintenanceShutdown";
        public const string TooltipPower = "HDBF.Tooltip.Power";
        public const string TooltipRotation = "HDBF.Tooltip.Rotation";
        public const string TooltipRefill = "HDBF.Tooltip.Refill";
        public const string TooltipPipeWater = "HDBF.Tooltip.PipeWater";
        public const string TooltipNoWaterSource = "HDBF.Tooltip.NoWaterSource";
        public const string TooltipOutput = "HDBF.Tooltip.Output";
        public const string TooltipCurrentMode = "HDBF.Tooltip.CurrentMode";
        public const string TooltipModeNormal = "HDBF.Tooltip.ModeNormal";
        public const string TooltipModeDense = "HDBF.Tooltip.ModeDense";
        public const string TooltipModeFallow = "HDBF.Tooltip.ModeFallow";
    }
}
