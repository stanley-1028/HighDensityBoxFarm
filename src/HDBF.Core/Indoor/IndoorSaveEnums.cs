// ============================================================================
// HDBF v2 | Layer: Core/Indoor | Owner: V2-CORE-02
// Purpose: Schema-v2 enum <-> name maps used by the save text. Names are part of the
//          persisted contract: they are stable, ordinal and never renumbered by accident.
//          Numeric fallback keeps a corrupted/future record readable (validator repairs it).
// Requirements: REQ-V2-SAVE-01, REQ-V2-SAVE-06, REQ-V2-EXT-03
// ============================================================================
using System.Globalization;

namespace HDBF.Core.Indoor
{
    /// <summary>
    /// Enum text maps of the schema-v2 save (REQ-V2-SAVE-01). A name is written for readability and
    /// long-term compatibility; an unknown name or numeric stamp is read as "unknown" so the record
    /// still loads and <see cref="IndoorSaveValidator"/> can repair it (REQ-V2-SAVE-06).
    /// </summary>
    internal static class IndoorSaveEnums
    {
        /// <summary>Stable schema name of a tier; unknown enum values fall back to numeric text.</summary>
        internal static string TierName(HdbfTier tier)
        {
            switch (tier)
            {
                case HdbfTier.Basic: return "Basic";
                case HdbfTier.Intermediate: return "Intermediate";
                case HdbfTier.Advanced: return "Advanced";
                case HdbfTier.Top: return "Top";
                default: return ((int)tier).ToString(CultureInfo.InvariantCulture);
            }
        }

        /// <summary>Tolerant tier reader: name first, then a numeric stamp; unknown defaults to Basic.</summary>
        internal static HdbfTier ParseTier(string? text)
        {
            switch (text)
            {
                case "Basic": return HdbfTier.Basic;
                case "Intermediate": return HdbfTier.Intermediate;
                case "Advanced": return HdbfTier.Advanced;
                case "Top": return HdbfTier.Top;
            }

            return TryNumber(text, out var number) ? (HdbfTier)number : HdbfTier.Basic;
        }

        /// <summary>Stable schema name of a faction; unknown enum values fall back to numeric text.</summary>
        internal static string FactionName(HdbfFaction faction)
        {
            switch (faction)
            {
                case HdbfFaction.Folktails: return "Folktails";
                case HdbfFaction.IronTeeth: return "IronTeeth";
                default: return ((int)faction).ToString(CultureInfo.InvariantCulture);
            }
        }

        /// <summary>Tolerant faction reader; unknown defaults to Folktails.</summary>
        internal static HdbfFaction ParseFaction(string? text)
        {
            switch (text)
            {
                case "Folktails": return HdbfFaction.Folktails;
                case "IronTeeth": return HdbfFaction.IronTeeth;
            }

            return TryNumber(text, out var number) ? (HdbfFaction)number : HdbfFaction.Folktails;
        }

        /// <summary>Stable schema name of an operating mode; unknown enum values fall back to numeric text.</summary>
        internal static string ModeName(IndoorMode mode)
        {
            switch (mode)
            {
                case IndoorMode.Normal: return "Normal";
                case IndoorMode.Dense: return "Dense";
                case IndoorMode.Fallow: return "Fallow";
                default: return ((int)mode).ToString(CultureInfo.InvariantCulture);
            }
        }

        /// <summary>Tolerant mode reader; unknown defaults to Normal.</summary>
        internal static IndoorMode ParseMode(string? text)
        {
            switch (text)
            {
                case "Normal": return IndoorMode.Normal;
                case "Dense": return IndoorMode.Dense;
                case "Fallow": return IndoorMode.Fallow;
            }

            return TryNumber(text, out var number) ? (IndoorMode)number : IndoorMode.Normal;
        }

        /// <summary>Stable schema name of a crop policy; unknown enum values fall back to numeric text.</summary>
        internal static string CropPolicyName(CropPolicy policy)
        {
            switch (policy)
            {
                case CropPolicy.Rotation: return "Rotation";
                case CropPolicy.Fixed: return "Fixed";
                default: return ((int)policy).ToString(CultureInfo.InvariantCulture);
            }
        }

        /// <summary>Tolerant crop-policy reader; unknown defaults to Rotation.</summary>
        internal static CropPolicy ParseCropPolicy(string? text)
        {
            switch (text)
            {
                case "Rotation": return CropPolicy.Rotation;
                case "Fixed": return CropPolicy.Fixed;
            }

            return TryNumber(text, out var number) ? (CropPolicy)number : CropPolicy.Rotation;
        }

        /// <summary>Stable schema name of a power contract; unknown enum values fall back to numeric text.</summary>
        internal static string PowerModeName(PowerConnection mode)
        {
            switch (mode)
            {
                case PowerConnection.NotRequired: return "NotRequired";
                case PowerConnection.Grid: return "Grid";
                case PowerConnection.OffGrid: return "OffGrid";
                default: return ((int)mode).ToString(CultureInfo.InvariantCulture);
            }
        }

        /// <summary>Tolerant power-contract reader; unknown defaults to NotRequired (validator fixes power tiers).</summary>
        internal static PowerConnection ParsePowerMode(string? text)
        {
            switch (text)
            {
                case "NotRequired": return PowerConnection.NotRequired;
                case "Grid": return PowerConnection.Grid;
                case "OffGrid": return PowerConnection.OffGrid;
            }

            return TryNumber(text, out var number) ? (PowerConnection)number : PowerConnection.NotRequired;
        }

        private static bool TryNumber(string? text, out int number)
        {
            number = 0;
            return !string.IsNullOrEmpty(text)
                && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out number);
        }
    }
}
