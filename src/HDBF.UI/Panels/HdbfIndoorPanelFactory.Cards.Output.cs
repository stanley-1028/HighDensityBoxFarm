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
                        HdbfIndoorPanelFormat.CropName(localize, snapshot.CropChoices,
                            snapshot.FixedCropId), localize)
                    : SimpleTextRow("rotation", HdbfUiLocKeys.PanelRotationLabel,
                        FormatRotationSequence(snapshot.RotationSequence, snapshot.CropChoices, localize),
                        localize),
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
            // 2026-10-29 bug fix: the in-farm output buffer scales with the tier's slot capacity
            // (one whole-box harvest, see IndoorConstants.OutputCapacityFor); the panel mirrors the
            // shipped blueprint value instead of the old fixed 50.
            var outputCapacity = HdbfIndoorPanelLimits.OutputCapacity(snapshot.Capacity);
            // Requirement E/F: the output port and the three physical ports are SEPARATE rows, so a
            // long state list can never overlap the line above it; every value wraps.
            var rows = new List<HdbfPanelRow>(5)
            {
                new()
                {
                    IconKey = "output",
                    Label = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelOutputLabel),
                    Value = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelCountValue,
                        snapshot.OutputOccupied, outputCapacity),
                    Tooltip = tooltip,
                    // Requirement F: the occupancy is the vanilla ProgressBar, not a text button.
                    HasBar = true,
                    Bar = HdbfIndoorPanelLimits.OutputFill(snapshot.OutputOccupied, snapshot.Capacity),
                    BarText = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelBarOutput,
                        snapshot.OutputOccupied, outputCapacity),
                },
                new()
                {
                    IconKey = "port",
                    Label = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelOutputPortLabel),
                    // L-09: HDBF has no implementation that hands produce to a conveyor, so the
                    // output-port row never claims "Connected"; it reports the detected module (or
                    // "not connected" when nothing is detected) and the tooltip says so.
                    Value = HdbfIndoorPanelFormat.Text(localize, LogisticsPortStateKey(snapshot)),
                    Tooltip = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.TooltipLogisticsPort),
                },
                // WP-05/RE-05: the pipe row reports the probe reason (network/tank/dry/adapter)
                // instead of the old single misleading "Not connected" state.
                PipePortRow(snapshot, localize),
                LogisticsPortRow(snapshot, localize),
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

        /// <summary>
        /// Logistics-interface row (L-09). HDBF has no implementation that hands produce to a
        /// conveyor or another logistics mod, so the row never says "connected": a tier that owns
        /// the port reports the detected module (or "not connected" when none is detected) and the
        /// tooltip tells the player to move the produce with vanilla haulers or warehouses. A tier
        /// without the port says "not applicable".
        /// </summary>
        private static HdbfPanelRow LogisticsPortRow(IndoorFarmSnapshot snapshot,
            Func<string, string> localize)
        {
            return new HdbfPanelRow
            {
                IconKey = "port",
                Label = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PortLogistics),
                Value = HdbfIndoorPanelFormat.Text(localize, LogisticsPortStateKey(snapshot)),
                Tooltip = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.TooltipLogisticsPort),
            };
        }

        /// <summary>
        /// State key of the logistics interface (L-09). No port -&gt; not applicable; a detected
        /// module -&gt; the honest "detected, produce not handed over yet" text; a port without a
        /// detected module -&gt; not connected. The result is never <c>PortConnected</c>, because
        /// HDBF does not transfer its produce over the logistics interface.
        /// </summary>
        /// <param name="snapshot">Read-only farm snapshot.</param>
        /// <returns>Localization key of the state to render.</returns>
        internal static string LogisticsPortStateKey(IndoorFarmSnapshot snapshot)
        {
            if (!snapshot.LogisticsPort)
            {
                return HdbfUiLocKeys.PortNotApplicable;
            }

            return snapshot.OutputPortConnected
                ? HdbfUiLocKeys.PortLogisticsDetected
                : HdbfUiLocKeys.PortNotConnected;
        }

        /// <summary>
        /// Pipe-network row (WP-05/RE-05): the state comes from the read-only probe reason, so a
        /// farm whose pipe is not usable names WHY (no network, no water tank, dry tank, missing or
        /// broken adapter, player-disabled port) instead of the old single "Not connected" string.
        /// </summary>
        private static HdbfPanelRow PipePortRow(IndoorFarmSnapshot snapshot,
            Func<string, string> localize)
        {
            return new HdbfPanelRow
            {
                IconKey = "port",
                Label = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PortPipe),
                Value = HdbfIndoorPanelFormat.Text(localize, PipePortStateKey(snapshot)),
                Tooltip = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.TooltipPipeWater),
            };
        }

        /// <summary>
        /// Maps the snapshot's pipe probe onto a port state key. A probed reason always wins (so a
        /// genuine problem is named); the only "unprobed" state keeps the pre-probe behaviour:
        /// no port at all -> not applicable, legacy pipe supply -> connected, otherwise not
        /// connected.
        /// </summary>
        /// <param name="snapshot">Read-only farm snapshot.</param>
        /// <returns>Localization key of the state to render.</returns>
        internal static string PipePortStateKey(IndoorFarmSnapshot snapshot)
        {
            switch (snapshot.PipeProbeState.Reason)
            {
                case PipeProbeReason.Connected:
                    return HdbfUiLocKeys.PortConnected;
                case PipeProbeReason.NoPipeNetwork:
                    return HdbfUiLocKeys.PortNoPipeNetwork;
                case PipeProbeReason.NoPipeTank:
                    return HdbfUiLocKeys.PortNoPipeTank;
                case PipeProbeReason.TankDry:
                    return HdbfUiLocKeys.PortTankDry;
                case PipeProbeReason.AdapterUnavailable:
                    return HdbfUiLocKeys.PortAdapterUnavailable;
                case PipeProbeReason.BindingFailed:
                    return HdbfUiLocKeys.PortBindingFailed;
                case PipeProbeReason.PortOff:
                    return HdbfUiLocKeys.PortDisabled;
            }

            if (!snapshot.PipePort)
            {
                return HdbfUiLocKeys.PortNotApplicable;
            }

            return snapshot.WaterSupply == WaterSupplyKind.Pipe
                ? HdbfUiLocKeys.PortConnected
                : HdbfUiLocKeys.PortNotConnected;
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
