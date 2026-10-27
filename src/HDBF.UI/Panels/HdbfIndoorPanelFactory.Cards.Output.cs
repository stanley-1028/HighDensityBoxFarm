// ============================================================================
// HDBF v2 | Layer: UI/Panels | Owner: V2-UI-01
// Purpose: Card builders of the v2 indoor panel read model (second half): power, crop strategy
//          and output cards plus their port/tooltip helpers. Split from .Cards.cs so both files
//          stay inside the section-19.18.2-C 300 line gate.
// Requirements: REQ-V2-UI-01/02/03, REQ-V2-READ-04
// ============================================================================
using System;
using System.Collections.Generic;
using HDBF.Core.Indoor;
using HDBF.UI.Localization;

namespace HDBF.UI.Panels
{
    public static partial class HdbfIndoorPanelFactory
    {
        private static HdbfPanelSection BuildPowerSection(IndoorFarmSnapshot snapshot,
            Func<string, string> localize)
        {
            var warnings = HdbfIndoorPanelWarnings.BuildPowerWarnings(snapshot, localize);
            var rows = new List<HdbfPanelRow>(1)
            {
                new()
                {
                    IconKey = "power",
                    Label = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelPowerLabel),
                    Value = HdbfIndoorPanelFormat.Text(localize,
                        HdbfIndoorPanelFormat.PowerKey(snapshot.Power, snapshot.PowerConnected)),
                    Tooltip = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.TooltipPower),
                },
            };

            return new HdbfPanelSection
            {
                Kind = HdbfPanelSectionKind.Power,
                Title = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelSectionPower),
                IconKey = "power",
                Tone = "green",
                Rows = WithWarnings(warnings, rows),
            };
        }

        private static HdbfPanelSection BuildCropsSection(IndoorFarmSnapshot snapshot,
            Func<string, string> localize)
        {
            var fixedCrop = snapshot.CropPolicy == CropPolicy.Fixed && !string.IsNullOrEmpty(snapshot.FixedCropId);
            var rows = new List<HdbfPanelRow>(2)
            {
                // The fragment appends the vanilla dropdown to this row (label + policy + box in one row).
                new()
                {
                    IconKey = "crop",
                    Label = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelCropPolicyLabel),
                    Value = CropPolicyText(snapshot, localize),
                    Tooltip = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.TooltipRotation),
                },
                fixedCrop
                    ? SimpleTextRow("crop", HdbfUiLocKeys.PanelFixedCropLabel,
                        HdbfIndoorPanelFormat.CropName(localize, snapshot.FixedCropId), localize)
                    : SimpleTextRow("rotation", HdbfUiLocKeys.PanelRotationLabel,
                        FormatRotationSequence(snapshot.RotationSequence, localize), localize),
            };

            var warnings = HdbfIndoorPanelWarnings.BuildCropWarnings(snapshot, localize);
            return new HdbfPanelSection
            {
                Kind = HdbfPanelSectionKind.Crops,
                Title = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelSectionCrops),
                IconKey = "crop",
                Tone = warnings.Count > 0 ? "red" : "green",
                Rows = WithWarnings(warnings, rows),
            };
        }

        private static HdbfPanelSection BuildOutputSection(IndoorFarmSnapshot snapshot,
            Func<string, string> localize)
        {
            var tooltip = OutputTooltip(snapshot, localize);
            // Requirement E/F: the output port and the three physical ports are SEPARATE rows, so a
            // long state list can never overlap the line above it; every value wraps.
            var rows = new List<HdbfPanelRow>(5)
            {
                new()
                {
                    IconKey = "output",
                    Label = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelOutputLabel),
                    Value = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelCountValue,
                        snapshot.OutputOccupied, HdbfIndoorPanelLimits.OutputCapacity),
                    Tooltip = tooltip,
                    // Requirement F: the occupancy is the vanilla ProgressBar, not a text button.
                    HasBar = true,
                    Bar = HdbfIndoorPanelLimits.OutputFill(snapshot.OutputOccupied),
                    BarText = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelBarOutput,
                        snapshot.OutputOccupied, HdbfIndoorPanelLimits.OutputCapacity),
                },
                new()
                {
                    IconKey = "port",
                    Label = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelOutputPortLabel),
                    Value = HdbfIndoorPanelFormat.Text(localize, snapshot.OutputPortConnected
                        ? HdbfUiLocKeys.PortConnected
                        : HdbfUiLocKeys.PortNotConnected),
                    Tooltip = tooltip,
                },
                PortRow(snapshot.PipePort, snapshot.WaterSupply == WaterSupplyKind.Pipe,
                    HdbfUiLocKeys.PortPipe, localize, tooltip),
                PortRow(snapshot.LogisticsPort, snapshot.OutputPortConnected,
                    HdbfUiLocKeys.PortLogistics, localize, tooltip),
                PortRow(snapshot.PowerPort, snapshot.PowerConnected,
                    HdbfUiLocKeys.PortPower, localize, tooltip),
            };

            var warnings = HdbfIndoorPanelWarnings.BuildOutputWarnings(snapshot, localize);
            return new HdbfPanelSection
            {
                Kind = HdbfPanelSectionKind.Output,
                Title = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelSectionOutput),
                IconKey = "output",
                Tone = warnings.Count > 0 ? "red" : "green",
                Rows = WithWarnings(warnings, rows),
            };
        }

        /// <summary>
        /// One physical-port row: "pipe/logistics/power interface" + connected/not-connected or
        /// "not applicable" when the tier has no such port. A separate row keeps the state text on
        /// its own line (requirement E).
        /// </summary>
        private static HdbfPanelRow PortRow(bool hasPort, bool connected, string labelKey,
            Func<string, string> localize, string tooltip)
        {
            var stateKey = !hasPort
                ? HdbfUiLocKeys.PortNotApplicable
                : connected
                    ? HdbfUiLocKeys.PortConnected
                    : HdbfUiLocKeys.PortNotConnected;
            return new HdbfPanelRow
            {
                IconKey = "port",
                Label = HdbfIndoorPanelFormat.Text(localize, labelKey),
                Value = HdbfIndoorPanelFormat.Text(localize, stateKey),
                Tooltip = tooltip,
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
    }
}
