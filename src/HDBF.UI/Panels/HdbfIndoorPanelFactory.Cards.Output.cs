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
            var rows = new List<HdbfPanelRow>(3)
            {
                new()
                {
                    IconKey = "output",
                    Label = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelOutputLabel),
                    Value = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelCountValue,
                        snapshot.OutputOccupied, HdbfIndoorPanelLimits.OutputCapacity),
                    Tooltip = OutputTooltip(snapshot, localize),
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
                    Tooltip = OutputTooltip(snapshot, localize),
                },
                new()
                {
                    IconKey = "port",
                    Label = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelPortsLabel),
                    Value = FormatPorts(snapshot, localize),
                    Tooltip = OutputTooltip(snapshot, localize),
                },
            };

            var warnings = HdbfIndoorPanelWarnings.BuildOutputWarnings(snapshot, localize);
            return new HdbfPanelSection
            {
                Kind = HdbfPanelSectionKind.Output,
                Title = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelSectionOutput),
                IconKey = "output",
                Tone = "green",
                Rows = WithWarnings(warnings, rows),
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

            return HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PortPipe) + " "
                   + HdbfIndoorPanelFormat.Text(localize, pipe) + HdbfIndoorPanelFormat.ProblemSeparator
                   + HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PortLogistics) + " "
                   + HdbfIndoorPanelFormat.Text(localize, logistics) + HdbfIndoorPanelFormat.ProblemSeparator
                   + HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PortPower) + " "
                   + HdbfIndoorPanelFormat.Text(localize, power);
        }
    }
}
