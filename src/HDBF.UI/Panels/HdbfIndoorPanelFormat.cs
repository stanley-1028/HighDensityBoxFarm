// ============================================================================
// HDBF v2 | Layer: UI/Panels | Owner: V2-UI-01
// Purpose: Pure formatting helpers of the indoor panel: template resolution, enum -> localization
//          key mapping, maintenance-material lookup and the command-failure whitelist. The warning
//          and problem row builders live in HdbfIndoorPanelWarnings.cs.
// Requirements: REQ-V2-UI-02/03/04, REQ-V2-READ-04
// ============================================================================
using System;
using System.Globalization;
using HDBF.Core;
using HDBF.Core.Abstractions;
using HDBF.Core.Balance;
using HDBF.Core.Formatting;
using HDBF.Core.Indoor;
using HDBF.UI.Localization;

namespace HDBF.UI.Panels
{
    /// <summary>
    /// Pure helpers behind <see cref="HdbfIndoorPanelFactory"/>. Everything here takes the
    /// localization resolver as a plain <see cref="Func{T,TResult}"/> (the fragment passes
    /// <c>ILoc.T</c>), so the whole panel mapping runs in the game-free L1 tests.
    /// </summary>
    public static class HdbfIndoorPanelFormat
    {
        /// <summary>Resolution not ready yet; the same glyph in every language (chapter 19.18.1-C).</summary>
        public const string Unknown = PanelNumbers.Unknown;

        /// <summary>Separator between two localized fragments of one problem row.</summary>
        public const string ProblemSeparator = " · ";

        /// <summary>Integer count with invariant culture and no group separators (chapter 19.18.1-C).</summary>
        /// <param name="value">Count to render.</param>
        /// <returns>Invariant decimal text, e.g. "120".</returns>
        public static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);

        /// <summary>Failure keys the panel is willing to show; anything else falls back to the generic text.</summary>
        private static readonly string[] KnownFailureKeys =
        {
            HdbfUiLocKeys.ErrorMaterialsMissing,
            HdbfUiLocKeys.ErrorDenseDisabled,
            HdbfUiLocKeys.ErrorNotBoxFarm,
            HdbfUiLocKeys.ErrorFarmNotActive,
            HdbfUiLocKeys.ErrorRefillUnavailable,
            HdbfUiLocKeys.ErrorCropNotAvailable,
            HdbfUiLocKeys.ErrorMaintenanceUnavailable,
            HdbfUiLocKeys.ErrorInvalidMode,
            HdbfUiLocKeys.ErrorMissingMod,
            HdbfUiLocKeys.ErrorNoWorkers,
            HdbfUiLocKeys.ErrorEntranceBlocked,
            HdbfUiLocKeys.ErrorNoWaterSource,
            HdbfUiLocKeys.ErrorWaterSourceUnreachable,
            HdbfUiLocKeys.ErrorWaterContaminated,
            HdbfUiLocKeys.ErrorNoActionsLeft,
            HdbfUiLocKeys.ErrorRefillPending,
            HdbfUiLocKeys.ErrorOutputFull,
            HdbfUiLocKeys.ModeSwitchCooldown,
            HdbfUiLocKeys.PortNotApplicable,
            HdbfUiLocKeys.ErrorActionFailed,
        };

        /// <summary>Resolves one template and formats positional arguments (invariant culture).</summary>
        /// <param name="localize">Resolver supplied by the fragment; never null.</param>
        /// <param name="key">Localization key.</param>
        /// <param name="args">Template arguments; none returns the template unchanged.</param>
        /// <returns>Never null; a broken template is returned unformatted instead of throwing.</returns>
        public static string Text(Func<string, string> localize, string key, params object[] args)
        {
            var template = localize is null ? key : localize(key);
            if (string.IsNullOrEmpty(template))
            {
                template = key;
            }

            if (args is null || args.Length == 0)
            {
                return template;
            }

            try
            {
                return string.Format(CultureInfo.InvariantCulture, template, args);
            }
            catch (FormatException)
            {
                // A translation with mismatched placeholders must not throw into the panel.
                return template;
            }
        }

        /// <summary>Short tier label key ("Basic".."Top").</summary>
        public static string TierKey(HdbfTier tier)
        {
            switch (tier)
            {
                case HdbfTier.Intermediate: return HdbfUiLocKeys.PanelTierIntermediate;
                case HdbfTier.Advanced: return HdbfUiLocKeys.PanelTierAdvanced;
                case HdbfTier.Top: return HdbfUiLocKeys.PanelTierTop;
                default: return HdbfUiLocKeys.PanelTierBasic;
            }
        }

        /// <summary>Faction label key.</summary>
        public static string FactionKey(HdbfFaction faction)
        {
            return faction == HdbfFaction.IronTeeth
                ? HdbfUiLocKeys.PanelFactionIronTeeth
                : HdbfUiLocKeys.PanelFactionFolktails;
        }

        /// <summary>Operating-mode label key (also used by the three mode buttons).</summary>
        public static string ModeKey(IndoorMode mode)
        {
            switch (mode)
            {
                case IndoorMode.Dense: return HdbfUiLocKeys.ModeDense;
                case IndoorMode.Fallow: return HdbfUiLocKeys.ModeFallow;
                default: return HdbfUiLocKeys.ModeNormal;
            }
        }

        /// <summary>Water-supply label key (pipe / manual / none).</summary>
        public static string SupplyKey(WaterSupplyKind supply)
        {
            switch (supply)
            {
                case WaterSupplyKind.Pipe: return HdbfUiLocKeys.PanelWaterPipe;
                case WaterSupplyKind.Manual: return HdbfUiLocKeys.PanelWaterManual;
                default: return HdbfUiLocKeys.PanelWaterNone;
            }
        }

        /// <summary>
        /// Power label key: a top tier on the grid is "grid", a deliberately off-grid top tier is
        /// "off-grid", a grid tier without supply is "no power", lower tiers are "not required".
        /// </summary>
        public static string PowerKey(PowerConnection power, bool connected)
        {
            switch (power)
            {
                case PowerConnection.OffGrid: return HdbfUiLocKeys.PanelPowerOffGrid;
                case PowerConnection.Grid:
                    return connected ? HdbfUiLocKeys.PanelPowerGrid : HdbfUiLocKeys.PanelPowerNoPower;
                default:
                    return HdbfUiLocKeys.PanelPowerNotRequired;
            }
        }

        /// <summary>Crop-policy label key (auto rotation / fixed crop).</summary>
        public static string PolicyKey(CropPolicy policy)
        {
            return policy == CropPolicy.Fixed
                ? HdbfUiLocKeys.PanelPolicyFixed
                : HdbfUiLocKeys.PanelPolicyRotation;
        }

        /// <summary>Crop-name key of a fixed-crop id; null for an id outside the shipped tables.</summary>
        /// <param name="cropId">Crop template id, e.g. "Carrot".</param>
        /// <returns>The localization key, or null when the id is unknown/empty.</returns>
        public static string? CropNameKey(string? cropId)
        {
            switch (cropId)
            {
                case "Carrot": return HdbfUiLocKeys.CropCarrot;
                case "Potato": return HdbfUiLocKeys.CropPotato;
                case "Wheat": return HdbfUiLocKeys.CropWheat;
                case "Sunflower": return HdbfUiLocKeys.CropSunflower;
                case "Canola": return HdbfUiLocKeys.CropCanola;
                case "Cassava": return HdbfUiLocKeys.CropCassava;
                case "Corn": return HdbfUiLocKeys.CropCorn;
                case "Eggplant": return HdbfUiLocKeys.CropEggplant;
                case "Kohlrabi": return HdbfUiLocKeys.CropKohlrabi;
                case "Soybean": return HdbfUiLocKeys.CropSoybean;
                default: return null;
            }
        }

        /// <summary>Localized name of a crop id; unknown ids fall back to the raw id (never a raw key).</summary>
        public static string CropName(Func<string, string> localize, string cropId)
        {
            var key = CropNameKey(cropId);
            return key is null ? cropId : Text(localize, key);
        }

        /// <summary>
        /// Maintenance material of the current upkeep path (REQ-V2-UI-02: "缺料（列出材料与数量）"):
        /// Plank tier -&gt; Plank, metal tiers -&gt; metal block, off-grid top tier -&gt; metal block.
        /// </summary>
        /// <param name="profile">Tier profile, or null when the snapshot is not ready.</param>
        /// <param name="power">Power contract of the snapshot.</param>
        /// <param name="connected">Whether grid power is up.</param>
        /// <param name="materialKey">Localization key of the material name.</param>
        /// <param name="count">Required count of one maintenance cycle.</param>
        /// <returns>False when this tier/state has no material upkeep at all.</returns>
        public static bool TryGetMaintenanceMaterial(TierProfile? profile, PowerConnection power, bool connected,
            out string materialKey, out int count)
        {
            materialKey = string.Empty;
            count = 0;
            if (profile is null)
            {
                return false;
            }

            switch (profile.MaintenanceKind)
            {
                case HdbfMaintenanceKind.Plank:
                    materialKey = HdbfUiLocKeys.PanelMaterialPlank;
                    count = profile.MaintenanceMaterialCount;
                    break;
                case HdbfMaintenanceKind.Metal:
                    materialKey = HdbfUiLocKeys.PanelMaterialMetalBlock;
                    count = profile.MaintenanceMaterialCount;
                    break;
                case HdbfMaintenanceKind.PowerOrOffGridMetal:
                    if (power == PowerConnection.Grid && connected)
                    {
                        return false;
                    }

                    materialKey = HdbfUiLocKeys.PanelMaterialMetalBlock;
                    count = profile.OffGridMaintenanceMaterial;
                    break;
                default:
                    return false;
            }

            return count > 0;
        }

        /// <summary>
        /// Localizes a command failure key, whitelisted: a key the panel does not know (or null)
        /// degrades to the generic "action failed" sentence, so a raw key can never reach the player.
        /// </summary>
        /// <param name="localize">Localization resolver.</param>
        /// <param name="failureKey">Key returned by the command implementation.</param>
        /// <returns>Localized, non-empty failure sentence.</returns>
        public static string FormatCommandFailure(Func<string, string> localize, string? failureKey)
        {
            if (!string.IsNullOrEmpty(failureKey))
            {
                for (var index = 0; index < KnownFailureKeys.Length; index++)
                {
                    if (string.Equals(KnownFailureKeys[index], failureKey, StringComparison.Ordinal))
                    {
                        return Text(localize, failureKey!);
                    }
                }
            }

            return Text(localize, HdbfUiLocKeys.ErrorActionFailed);
        }
    }
}
