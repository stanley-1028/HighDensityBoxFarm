// ============================================================================
// HDBF v2 | Layer: UI/Panels | Owner: V2-UI-01
// Purpose: Maps one Core IndoorFarmSnapshot onto the localized panel read-model
//          (HdbfIndoorPanelData). Read-only: no game object, no command, no allocation in the
//          per-frame path (the fragment calls it only when the value fingerprint changed).
// Requirements: REQ-V2-UI-01/02/03, REQ-V2-WATER-03, REQ-V2-READ-04
// ============================================================================
using System;
using HDBF.Core;
using HDBF.Core.Balance;
using HDBF.Core.Formatting;
using HDBF.Core.Indoor;
using HDBF.UI.Localization;

namespace HDBF.UI.Panels
{
    /// <summary>
    /// Pure factory of <see cref="HdbfIndoorPanelData"/> (chapter 19.11 field map). The fragment
    /// passes <c>ILoc.T</c> as the resolver; L1 tests pass the shipped CSV templates, so every row
    /// can be asserted without loading Timberborn (REQ-V2-TEST-01).
    /// </summary>
    public static class HdbfIndoorPanelFactory
    {
        /// <summary>Empty read-model: the fragment stays hidden (no error text on a non-farm entity).</summary>
        public static HdbfIndoorPanelData Empty { get; } = new();

        /// <summary>
        /// Projects one snapshot onto the panel. Null snapshots return <see cref="Empty"/> instead
        /// of throwing (REQ-V2-SAFE-01: player state must never break the panel).
        /// </summary>
        /// <param name="snapshot">Core read-only snapshot; null means "not ready".</param>
        /// <param name="localize">Localization resolver, normally <c>ILoc.T</c>.</param>
        /// <returns>The complete localized panel model.</returns>
        public static HdbfIndoorPanelData Create(IndoorFarmSnapshot? snapshot, Func<string, string> localize)
        {
            if (snapshot is null)
            {
                return Empty;
            }

            var profile = FindProfile(snapshot.Tier);
            var flags = snapshot.Warnings;
            var stalled = (flags & IndoorWarningFlags.MaintenanceStalled) != 0;
            var materialFree = profile is not null && profile.MaintenanceKind == HdbfMaintenanceKind.IrrigationOnly;

            var data = new HdbfIndoorPanelData
            {
                HasData = true,
                Title = HdbfIndoorPanelFormat.Text(localize, BuildingKey(snapshot.Tier)),
                TierText = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelTier,
                    HdbfIndoorPanelFormat.Text(localize, HdbfIndoorPanelFormat.TierKey(snapshot.Tier))),
                FactionText = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelFaction,
                    HdbfIndoorPanelFormat.Text(localize, HdbfIndoorPanelFormat.FactionKey(snapshot.Faction))),
                WorkersText = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelWorkers,
                    snapshot.Workers, snapshot.MaxWorkers),
                ModeText = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelMode) + ": "
                    + HdbfIndoorPanelFormat.Text(localize, HdbfIndoorPanelFormat.ModeKey(snapshot.Mode)),
                ModeTransitionText = snapshot.ModeTransitioning
                    ? HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelModeTransition)
                    : string.Empty,
                ModeValue = snapshot.Mode,

                SlotsText = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelSlots,
                    snapshot.ConfiguredSlots, snapshot.Capacity),
                EffectiveSlotsText = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelEffectiveSlots,
                    snapshot.EffectiveSlots),
                EffectiveSlotsTooltip = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.TooltipEffectiveSlots,
                    snapshot.Capacity, snapshot.Workers, TierCatalog.SlotsPerFarmer),
                EmptySlotsText = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelEmptySlots,
                    snapshot.EmptySlots),
                GrowingSlotsText = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelGrowingSlots,
                    snapshot.GrowingSlots),
                MatureSlotsText = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelMatureSlots,
                    snapshot.MatureSlots),
                BacklogText = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelBacklog,
                    snapshot.BacklogActions),

                TankText = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelTank,
                    PanelNumbers.Days(snapshot.Tank, roundUp: false),
                    PanelNumbers.Days(snapshot.TankCapacity, roundUp: false),
                    PanelNumbers.Days(snapshot.TankDaysRemaining, roundUp: false)),
                CoverageText = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelCoverage,
                    PanelNumbers.Percent(snapshot.Coverage, roundUp: false)),
                MinCoverageText = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelMinCoverage,
                    PanelNumbers.Percent(snapshot.MinCoverage, roundUp: false)),
                WaterSupplyText = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelWaterSupply,
                    HdbfIndoorPanelFormat.Text(localize, HdbfIndoorPanelFormat.SupplyKey(snapshot.WaterSupply))),
                TankTooltip = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.TooltipTank),
                CoverageTooltip = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.TooltipCoverage),

                MaintenanceDueText = materialFree
                    ? HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelMaintenanceNone)
                    : HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelMaintenanceDue,
                        PanelNumbers.Days(snapshot.MaintenanceDueDay, roundUp: false)),
                MaintenanceCyclesText = HdbfIndoorPanelFormat.Text(localize,
                    HdbfUiLocKeys.PanelMaintenanceCycles, HdbfIndoorPanelFormat.Count(snapshot.MaintenanceCyclesMissed)),
                MaintenanceTooltip = HdbfIndoorPanelFormat.Text(localize, materialFree
                    ? HdbfUiLocKeys.PanelMaintenanceNone
                    : stalled
                        ? HdbfUiLocKeys.TooltipMaintenanceShutdown
                        : HdbfUiLocKeys.TooltipMaintenance),
                CanPerformMaintenance = profile is not null && !materialFree,
                MaintenanceUrgent = snapshot.MaintenanceCyclesMissed > 0
                    || snapshot.MaintenanceMaterialMissing
                    || (flags & (IndoorWarningFlags.MaintenanceDue | IndoorWarningFlags.MaintenanceStalled
                                 | IndoorWarningFlags.MissingMaterial)) != 0,

                PowerText = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelPower,
                    HdbfIndoorPanelFormat.Text(localize,
                        HdbfIndoorPanelFormat.PowerKey(snapshot.Power, snapshot.PowerConnected))),
                PowerTooltip = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.TooltipPower),

                CropPolicyText = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelCropPolicy,
                    HdbfIndoorPanelFormat.Text(localize, HdbfIndoorPanelFormat.PolicyKey(snapshot.CropPolicy))),
                RotationText = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelRotation,
                    string.IsNullOrEmpty(snapshot.RotationSequence)
                        ? HdbfIndoorPanelFormat.Unknown
                        : snapshot.RotationSequence),
                CropTooltip = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.TooltipRotation),
                SelectedCropId = snapshot.CropPolicy == CropPolicy.Fixed ? snapshot.FixedCropId : string.Empty,
                CropChoices = HdbfIndoorPanelWarnings.BuildCropChoices(snapshot, localize),

                OutputStockText = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelOutputStock,
                    HdbfIndoorPanelFormat.Count(snapshot.OutputOccupied)),
                OutputPortText = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelOutputPort,
                    HdbfIndoorPanelFormat.Text(localize, snapshot.OutputPortConnected
                        ? HdbfUiLocKeys.PortConnected
                        : HdbfUiLocKeys.PortNotConnected)),
                PortsText = FormatPorts(snapshot, localize),
                OutputTooltip = OutputTooltip(snapshot, localize),
                StatusRows = HdbfIndoorPanelWarnings.BuildStatusRows(snapshot, localize),
                ProblemRows = HdbfIndoorPanelWarnings.BuildProblemRows(snapshot, profile, localize),
            };

            var irrigation = IndoorWaterMath.IrrigationMultiplier(snapshot.Coverage, snapshot.MinCoverage);
            var waterShortage = irrigation < 0.999f
                ? HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelWaterShortage,
                    PanelNumbers.Percent(irrigation, roundUp: false))
                : string.Empty;

            return data with
            {
                WaterShortageText = waterShortage,
                RefillTooltip = RefillTooltip(snapshot, localize),
                CanRefill = snapshot.WaterSupply == WaterSupplyKind.Manual,
                FixedCropText = FormatFixedCrop(snapshot, localize),
                MaintenanceMaterialsText = FormatMissingMaterials(snapshot, profile, localize),
            };
        }

        private static string OutputTooltip(IndoorFarmSnapshot snapshot, Func<string, string> localize)
        {
            var tooltip = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.TooltipOutput);
            if (!string.IsNullOrEmpty(snapshot.OutputInventoryPortState))
            {
                tooltip += HdbfIndoorPanelFormat.ProblemSeparator + snapshot.OutputInventoryPortState;
            }

            return tooltip;
        }

        private static string FormatFixedCrop(IndoorFarmSnapshot snapshot, Func<string, string> localize)
        {
            if (snapshot.CropPolicy != CropPolicy.Fixed || string.IsNullOrEmpty(snapshot.FixedCropId))
            {
                return string.Empty;
            }

            var key = HdbfIndoorPanelFormat.CropNameKey(snapshot.FixedCropId);
            var name = key is null ? snapshot.FixedCropId : HdbfIndoorPanelFormat.Text(localize, key);
            return HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelFixedCrop, name);
        }

        private static string FormatMissingMaterials(IndoorFarmSnapshot snapshot, TierProfile? profile,
            Func<string, string> localize)
        {
            if ((snapshot.Warnings & IndoorWarningFlags.MissingMaterial) == 0)
            {
                return string.Empty;
            }

            if (!HdbfIndoorPanelFormat.TryGetMaintenanceMaterial(profile, snapshot.Power, snapshot.PowerConnected,
                    out var materialKey, out var count))
            {
                return HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.WarningMissingMaterial);
            }

            return HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelMaintenanceMaterials,
                HdbfIndoorPanelFormat.Text(localize, materialKey), count);
        }

        private static string RefillTooltip(IndoorFarmSnapshot snapshot, Func<string, string> localize)
        {
            switch (snapshot.WaterSupply)
            {
                case WaterSupplyKind.Manual:
                    return HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.TooltipRefill);
                case WaterSupplyKind.Pipe:
                    return HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.TooltipPipeWater);
                default:
                    return HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.TooltipNoWaterSource);
            }
        }

        private static string FormatPorts(IndoorFarmSnapshot snapshot, Func<string, string> localize)
        {
            var pipe = !snapshot.PipePort
                ? HdbfUiLocKeys.PortNotApplicable
                : snapshot.WaterSupply == WaterSupplyKind.Pipe
                    ? HdbfUiLocKeys.PortConnected
                    : HdbfUiLocKeys.PortNotConnected;
            var logistics = !snapshot.LogisticsPort
                ? HdbfUiLocKeys.PortNotApplicable
                : snapshot.OutputPortConnected
                    ? HdbfUiLocKeys.PortConnected
                    : HdbfUiLocKeys.PortNotConnected;
            var power = !snapshot.PowerPort
                ? HdbfUiLocKeys.PortNotApplicable
                : snapshot.PowerConnected
                    ? HdbfUiLocKeys.PortConnected
                    : HdbfUiLocKeys.PortNotConnected;

            return HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelPorts) + ": "
                   + HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PortPipe) + " "
                   + HdbfIndoorPanelFormat.Text(localize, pipe) + HdbfIndoorPanelFormat.ProblemSeparator
                   + HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PortLogistics) + " "
                   + HdbfIndoorPanelFormat.Text(localize, logistics) + HdbfIndoorPanelFormat.ProblemSeparator
                   + HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PortPower) + " "
                   + HdbfIndoorPanelFormat.Text(localize, power);
        }

        private static string BuildingKey(HdbfTier tier)
        {
            switch (tier)
            {
                case HdbfTier.Intermediate: return HdbfUiLocKeys.BuildingIntermediateName;
                case HdbfTier.Advanced: return HdbfUiLocKeys.BuildingAdvancedName;
                case HdbfTier.Top: return HdbfUiLocKeys.BuildingTopName;
                default: return HdbfUiLocKeys.BuildingBasicName;
            }
        }

        private static TierProfile? FindProfile(HdbfTier tier)
        {
            try
            {
                return TierCatalog.Get(tier);
            }
            catch (ArgumentOutOfRangeException)
            {
                // A corrupt snapshot must not break the panel (REQ-V2-SAFE-01).
                return null;
            }
        }
    }
}



