// ============================================================================
// HDBF v2 | Layer: UI/Panels | Owner: V2-UI-01
// Purpose: Maps one Core IndoorFarmSnapshot onto the localized panel read model: header, the
//          read-only worker roster, the crop-strategy block, the action availability/reasons and
//          the ordered card list (card rows live in HdbfIndoorPanelFactory.Cards.cs). Read-only:
//          no game object, no command, no allocation in the idle refresh path.
// Requirements: REQ-V2-UI-01/02/03, REQ-V2-WATER-03, REQ-V2-READ-04
// ============================================================================
using System;
using System.Collections.Generic;
using HDBF.Core;
using HDBF.Core.Balance;
using HDBF.Core.Formatting;
using HDBF.Core.Indoor;
using HDBF.UI.Localization;

namespace HDBF.UI.Panels
{
    /// <summary>
    /// Pure factory of <see cref="HdbfIndoorPanelData"/> (chapter 19.11). The fragment passes
    /// <c>ILoc.T</c> as the resolver; L1 tests pass the shipped CSV templates, so every card and
    /// row can be asserted without loading Timberborn (REQ-V2-TEST-01).
    /// </summary>
    public static partial class HdbfIndoorPanelFactory
    {
        /// <summary>Empty read model: the fragment stays hidden (non-farm entity or no data).</summary>
        public static HdbfIndoorPanelData Empty { get; } = new();

        /// <summary>
        /// Projects one snapshot onto the panel. A null snapshot returns <see cref="Empty"/> instead
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
            var coverage = snapshot.Coverage;
            var minCoverage = snapshot.MinCoverage;
            var irrigation = IndoorWaterMath.IrrigationMultiplier(coverage, minCoverage);
            var flags = snapshot.Warnings;
            var entranceBlocked = (flags & IndoorWarningFlags.EntranceBlocked) != 0;

            // REQ-V2-UI-02: a refill needs a manual source, a crew and a free entrance.
            var refillReason = RefillBlockedReason(snapshot, entranceBlocked, localize);
            var maintenanceReason = MaintenanceBlockedReason(snapshot, profile, localize);
            // WP-06/L-03: one single "why nothing happens" reason for the status line at the top
            // of the first card; a paused snapshot that predates the reason field still says so.
            var idleReason = ResolveIdleReason(snapshot);

            var subtitle = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelSubtitle,
                HdbfIndoorPanelFormat.Text(localize, HdbfIndoorPanelFormat.TierKey(snapshot.Tier)),
                HdbfIndoorPanelFormat.Text(localize, HdbfIndoorPanelFormat.FactionKey(snapshot.Faction)));
            var data = new HdbfIndoorPanelData
            {
                HasData = true,
                // Requirement A: the vanilla entity panel header already shows the building icon,
                // name and district. The fragment only adds the compact "tier · faction" subtitle
                // row, so no second building name is ever rendered; the mode is the radio group.
                MetaLine = subtitle,
                MetaTooltip = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.TooltipCurrentMode),
                ModeTransitionText = snapshot.ModeTransitioning
                    ? HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelModeTransition)
                    : string.Empty,
                IdleReasonText = HdbfIndoorPanelFormat.Text(localize,
                    IndoorIdleReasonMath.LocalizationKey(idleReason)),
                IdleReasonTooltip = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.TooltipIdleReason),
                AutoMaintenance = snapshot.AutoMaintenance,
                AutoMaintenanceTooltip = HdbfIndoorPanelFormat.Text(localize,
                    HdbfUiLocKeys.TooltipAutoMaintenance),
                WaterPathText = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelWaterPaths),
                Tier = snapshot.Tier,
                Faction = snapshot.Faction,
                Mode = snapshot.Mode,

                WorkerChips = BuildWorkerChips(snapshot, localize),
                WorkerRosterNote = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelWorkerNote),

                CropPolicyLabel = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelCropPolicyLabel),
                CropPolicyText = CropPolicyText(snapshot, localize),
                SelectedCropId = snapshot.CropPolicy == CropPolicy.Fixed ? snapshot.FixedCropId : string.Empty,
                CropChoices = HdbfIndoorPanelWarnings.BuildCropChoices(snapshot, localize),
                CropTooltip = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.TooltipRotation),

                CanRefill = string.IsNullOrEmpty(refillReason),
                RefillTooltip = RefillTooltip(snapshot, refillReason, localize),
                RefillReasonText = refillReason,

                CanPerformMaintenance = string.IsNullOrEmpty(maintenanceReason),
                MaintenanceTooltip = MaintenanceTooltip(snapshot, profile, maintenanceReason, localize),
                MaintenanceReasonText = maintenanceReason ?? string.Empty,
                MaintenanceUrgent = (flags & (IndoorWarningFlags.MaintenanceDue
                                             | IndoorWarningFlags.MaintenanceStalled
                                             | IndoorWarningFlags.MissingMaterial)) != 0,

                Sections = new[]
                {
                    BuildWorkersSection(snapshot, irrigation, entranceBlocked,
                        HdbfIndoorPanelFormat.Text(localize,
                            IndoorIdleReasonMath.LocalizationKey(idleReason)),
                        HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.TooltipIdleReason), localize),
                    // REQ-V2-UI-05: the live crop rows sit directly under the worker/header card so a
                    // partly planted farm is visible without scrolling the 325 px panel.
                    BuildGrowingSection(snapshot, localize),
                    BuildSlotsSection(snapshot, localize),
                    BuildWaterSection(snapshot, coverage, minCoverage, irrigation, localize),
                    BuildMaintenanceSection(snapshot, profile, localize),
                    BuildPowerSection(snapshot, localize),
                    BuildCropsSection(snapshot, localize),
                    BuildOutputSection(snapshot, localize),
                },
            };

            return data;
        }

        // ------------------------------------------------------------------ worker roster

        private static IReadOnlyList<HdbfWorkerChip> BuildWorkerChips(IndoorFarmSnapshot snapshot,
            Func<string, string> localize)
        {
            var filled = snapshot.Workers < 0 ? 0 : snapshot.Workers;
            if (filled > snapshot.MaxWorkers)
            {
                filled = snapshot.MaxWorkers;
            }

            var chips = new List<HdbfWorkerChip>(snapshot.MaxWorkers < 0 ? 0 : snapshot.MaxWorkers);
            var note = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.TooltipWorkerName);
            for (var index = 0; index < snapshot.MaxWorkers; index++)
            {
                var assigned = index < filled;
                chips.Add(new HdbfWorkerChip
                {
                    // The snapshot does not carry beaver names (UI may not read game objects), so a
                    // staffed chip shows the shared "—" glyph and explains it in the note.
                    Name = assigned
                        ? HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelUnknown)
                        : HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelWorkerVacant),
                    Vacant = !assigned,
                    Tooltip = note,
                });
            }

            return chips;
        }

        // ------------------------------------------------------------------ crop strategy

        private static string CropPolicyText(IndoorFarmSnapshot snapshot, Func<string, string> localize)
        {
            if (snapshot.CropPolicy != CropPolicy.Fixed || string.IsNullOrEmpty(snapshot.FixedCropId))
            {
                return HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelPolicyRotation);
            }

            return HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelFixedCrop,
                HdbfIndoorPanelFormat.CropName(localize, snapshot.FixedCropId));
        }

        /// <summary>
        /// Rotation sequence with localized crop names; the snapshot carries ids ("Carrot → Potato").
        /// </summary>
        internal static string FormatRotationSequence(string? sequence, Func<string, string> localize)
        {
            if (string.IsNullOrEmpty(sequence))
            {
                return HdbfIndoorPanelFormat.Unknown;
            }

            var parts = sequence!.Split(new[] { " > ", " → " }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length <= 1)
            {
                return HdbfIndoorPanelFormat.CropName(localize, sequence);
            }

            var names = new List<string>(parts.Length);
            for (var index = 0; index < parts.Length; index++)
            {
                names.Add(HdbfIndoorPanelFormat.CropName(localize, parts[index].Trim()));
            }

            // ASCII arrow keeps the sequence readable in every font shipped by the game.
            return string.Join(" > ", names);
        }

        // ------------------------------------------------------------------ action reasons

        private static string RefillBlockedReason(IndoorFarmSnapshot snapshot, bool entranceBlocked,
            Func<string, string> localize)
        {
            switch (snapshot.WaterSupply)
            {
                case WaterSupplyKind.Pipe:
                    return HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.TooltipPipeWater);
                case WaterSupplyKind.None:
                    return HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.ErrorNoWaterSource);
            }

            if (snapshot.Workers <= 0)
            {
                return HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.ErrorNoWorkers);
            }

            return entranceBlocked
                ? HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.ErrorEntranceBlocked)
                : string.Empty;
        }

        private static string RefillTooltip(IndoorFarmSnapshot snapshot, string blockedReason,
            Func<string, string> localize)
        {
            if (!string.IsNullOrEmpty(blockedReason))
            {
                return blockedReason;
            }

            return HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.TooltipRefill);
        }

        /// <summary>
        /// Reason shown by the first-card status line (WP-06). The snapshot reason is authoritative
        /// (gameplay resolves it with <see cref="IndoorIdleReasonMath.Resolve"/>); a paused snapshot
        /// from an older gameplay build that never filled the field still reports Paused instead of
        /// a misleading "Working".
        /// </summary>
        /// <param name="snapshot">Read-only farm snapshot.</param>
        /// <returns>The single reason the status line renders.</returns>
        internal static IndoorIdleReason ResolveIdleReason(IndoorFarmSnapshot snapshot)
        {
            if (snapshot.IdleReason == IndoorIdleReason.None && snapshot.Paused)
            {
                return IndoorIdleReason.Paused;
            }

            return snapshot.IdleReason;
        }

        // ------------------------------------------------------------------ shared helpers

        internal static string BuildingKey(HdbfTier tier)
        {
            switch (tier)
            {
                case HdbfTier.Intermediate: return HdbfUiLocKeys.BuildingIntermediateName;
                case HdbfTier.Advanced: return HdbfUiLocKeys.BuildingAdvancedName;
                case HdbfTier.Top: return HdbfUiLocKeys.BuildingTopName;
                default: return HdbfUiLocKeys.BuildingBasicName;
            }
        }

        internal static TierProfile? FindProfile(HdbfTier tier)
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
