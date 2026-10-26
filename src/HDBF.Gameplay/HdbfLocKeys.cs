using HDBF.Core.Warnings;

namespace HDBF.Gameplay
{

    /// <summary>
    /// Every localization key HDBF gameplay code emits (chapter 14.4 / REQ-UI-08, REQ-UI-09).
    ///
    /// Rules baked into these constants:
    /// * prefix <c>HDBF.</c>, category is one of Building/Panel/Mode/Port/Action/Warning/
    ///   Compat/Error/Tooltip, key body is ASCII PascalCase (chapter 14.4);
    /// * the four tier keys are derived from the blueprint id, so <c>&lt;Tier&gt;</c> must match
    ///   the blueprint id's last segment exactly ("Basic", "Intermediate", "Advanced", "Top");
    /// * placeholders are positional <c>{0}</c>/<c>{1}</c>; no percentage, unit or colour is
    ///   baked into a key (the formatting layer injects it, chapter 14.5).
    ///
    /// The chapter 14.4 table lists 30 keys. It omits keys for three states that chapter 14.2
    /// still requires the UI to render (shutdown, critical water shortage, low tank), so those
    /// three are added here with the same naming scheme instead of hard-coding text.
    /// </summary>
    public static class HdbfLocKeys
    {
        // ---- Building: names -------------------------------------------------------------

        public const string BuildingBasicName = "HDBF.Building.BoxFarm.Basic.Name";
        public const string BuildingIntermediateName = "HDBF.Building.BoxFarm.Intermediate.Name";
        public const string BuildingAdvancedName = "HDBF.Building.BoxFarm.Advanced.Name";
        public const string BuildingTopName = "HDBF.Building.BoxFarm.Top.Name";

        // ---- Building: descriptions ------------------------------------------------------

        public const string BuildingBasicDescription = "HDBF.Building.BoxFarm.Basic.Description";
        public const string BuildingIntermediateDescription = "HDBF.Building.BoxFarm.Intermediate.Description";
        public const string BuildingAdvancedDescription = "HDBF.Building.BoxFarm.Advanced.Description";
        public const string BuildingTopDescription = "HDBF.Building.BoxFarm.Top.Description";

        // ---- Panel -----------------------------------------------------------------------

        public const string PanelWorkers = "HDBF.Panel.Workers";
        public const string PanelEffectivePlots = "HDBF.Panel.EffectivePlots";
        public const string PanelIrrigation = "HDBF.Panel.Irrigation";
        public const string PanelFatigue = "HDBF.Panel.Fatigue";
        public const string PanelMaintenanceDue = "HDBF.Panel.MaintenanceDue";

        // ---- Mode ------------------------------------------------------------------------

        public const string ModeNormal = "HDBF.Mode.Normal";
        public const string ModeDense = "HDBF.Mode.Dense";
        public const string ModeFallow = "HDBF.Mode.Fallow";

        // ---- Ports -----------------------------------------------------------------------

        public const string PortPower = "HDBF.Port.Power";
        public const string PortPipe = "HDBF.Port.Pipe";
        public const string PortLogistics = "HDBF.Port.Logistics";

        // ---- Actions ---------------------------------------------------------------------

        public const string ActionMaintenance = "HDBF.Action.Maintenance";
        public const string ActionHighlightPlots = "HDBF.Action.HighlightPlots";

        // ---- Warnings (chapter 14.2 / 14.4) ----------------------------------------------
        // The canonical strings live in HDBF.Core.Warnings.HdbfWarningKeys because the Core
        // aggregation/world-icon rules map the same keys; gameplay keeps these aliases so existing
        // call sites read unchanged.

        public const string WarningIrrigationLow = HdbfWarningKeys.IrrigationLow;
        public const string WarningMaintenanceOverdue = HdbfWarningKeys.MaintenanceOverdue;
        public const string WarningFatigueHigh = HdbfWarningKeys.FatigueHigh;
        public const string WarningNoPower = HdbfWarningKeys.NoPower;
        public const string WarningAdapterDisabled = HdbfWarningKeys.AdapterDisabled;
        public const string WarningAdapterFailed = HdbfWarningKeys.AdapterFailed;

        /// <summary>P0: shutdown after two missed maintenance cycles (chapter 14.2 first row).</summary>
        public const string WarningShutdown = HdbfWarningKeys.Shutdown;

        /// <summary>P0: coverage below 0.25 × C_min, growth fully stopped (chapter 14.2).</summary>
        public const string WarningNoWater = HdbfWarningKeys.NoWater;

        /// <summary>P4: tank holds less than 25 % of its full-load days (chapter 14.2 last row).</summary>
        public const string WarningTankLow = HdbfWarningKeys.TankLow;

        // ---- Compat / Error / Tooltip -----------------------------------------------------

        public const string CompatMissingMod = "HDBF.Compat.MissingMod";
        public const string ErrorLoadFailed = "HDBF.Error.LoadFailed";
        public const string TooltipMultiplierBreakdown = "HDBF.Tooltip.MultiplierBreakdown";

        /// <summary>
        /// World-icon tooltip (chapter 14.2): <c>{0}</c> is the localized warning text, <c>{1}</c>
        /// the configured distance in cells. The vanilla status description is what the player
        /// hovers over the head-top icon, so it must be a localized sentence, never a raw key.
        /// </summary>
        public const string TooltipWorldIcon = "HDBF.Tooltip.WorldIcon";

        // ---- Display-name helper ----------------------------------------------------------

        /// <summary>
        /// Maps a tier to its name key. Unknown tiers fall back to the basic name so a broken
        /// blueprint never prints a raw key to the player (REQ-UI-09).
        /// </summary>
        public static string BuildingName(HDBF.Core.HdbfTier tier)
        {
            switch (tier)
            {
                case HDBF.Core.HdbfTier.Intermediate:
                    return BuildingIntermediateName;
                case HDBF.Core.HdbfTier.Advanced:
                    return BuildingAdvancedName;
                case HDBF.Core.HdbfTier.Top:
                    return BuildingTopName;
                default:
                    return BuildingBasicName;
            }
        }
    }
}
