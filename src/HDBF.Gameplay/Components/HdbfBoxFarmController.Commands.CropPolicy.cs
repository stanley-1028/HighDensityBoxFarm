// ============================================================================
// HDBF v2 | Layer: Gameplay/Components | Owner: V2-GAMEPLAY-01 / V2-GAMEPLAY-02
// Purpose: Crop-policy command of one indoor farm (rotation or one fixed crop of the building
//          faction's catalog, REQ-V2-EXT-01), validated before any state changes, plus the L-18
//          config feature-flag wiring (pipe/logistics ports, rotation fallback, one-time
//          indoor.feature-disabled log). Split out of HdbfBoxFarmController.Commands.cs to keep
//          that file inside the 300-line soft gate.
// Requirements: REQ-V2-EXT-01, REQ-V2-UI-02, REQ-V2-SAFE-01, REQ-V2-MAINT-02, REQ-ARCH-09
// ============================================================================
using System;
using HDBF.Core;
using HDBF.Core.Abstractions;
using HDBF.Core.Config;
using HDBF.Core.Indoor;
using HDBF.Core.Logging;

namespace HDBF.Gameplay.Components
{
    public partial class HdbfBoxFarmController
    {
        /// <summary>True once the L-18 one-time disabled-feature diagnostics were emitted.</summary>
        private bool _featureFlagsLogged;

        /// <summary>
        /// Sets the crop policy. A fixed crop must belong to the building faction's catalog
        /// (REQ-V2-EXT-01); an invalid choice changes nothing and returns a specific failure key.
        /// L-18: while <c>features.rotation=false</c> the rotation policy is rejected with the
        /// existing invalid-mode key and the farm keeps running its fixed policy.
        /// </summary>
        /// <param name="policy">Rotation or fixed.</param>
        /// <param name="fixedCropId">Required crop id for <see cref="CropPolicy.Fixed"/>.</param>
        /// <param name="failureKey">Specific failure key when false.</param>
        /// <returns>True when the policy was applied.</returns>
        public bool TrySetCropPolicy(CropPolicy policy, string? fixedCropId, out string? failureKey)
        {
            failureKey = null;
            var record = _record;
            if (record is null || !_activated)
            {
                failureKey = HdbfIndoorCommandFailures.NotActive;
                return false;
            }

            if (!Enum.IsDefined(typeof(CropPolicy), policy))
            {
                failureKey = HdbfIndoorCommandFailures.InvalidMode;
                return false;
            }

            if (policy == CropPolicy.Rotation && !SafeFeatures().Rotation)
            {
                // L-18: the rotation feature is disabled by config. The activation pass already
                // converted a rotation record to a fixed crop, so this only rejects a manual switch
                // back and explains it once through the shared feature-disabled key.
                failureKey = HdbfIndoorCommandFailures.InvalidMode;
                _log.Write(HdbfLogCategory.Info, IndoorLogKeys.FeatureDisabled,
                    "config feature disabled: features.rotation (rotation policy rejected), entityId="
                    + _entityId);
                return false;
            }

            if (policy == CropPolicy.Fixed
                && !FallbackCropCatalog.TryResolve(_cropCatalog, _faction, fixedCropId, out _))
            {
                failureKey = HdbfIndoorCommandFailures.CropNotAvailable;
                _log.Write(HdbfLogCategory.Warn, IndoorLogKeys.CropPolicy,
                    "fixed crop rejected (not in the faction catalog): '" + (fixedCropId ?? "<null>")
                    + "', entityId=" + _entityId);
                return false;
            }

            try
            {
                record.CropPolicy = policy;
                record.FixedCropId = policy == CropPolicy.Fixed ? fixedCropId ?? string.Empty : string.Empty;
                _saveDirty = true;
                _log.Write(HdbfLogCategory.Info, IndoorLogKeys.CropPolicy,
                    "indoor crop policy set to " + policy + ", entityId=" + _entityId);
                return true;
            }
            catch (Exception exception)
            {
                failureKey = HdbfCommandFailures.Unknown;
                _log.Error(HdbfLogCategory.Error, IndoorLogKeys.CommandFailed,
                    "setting the indoor crop policy failed; no state changed", exception, _entityId.ToString());
                return false;
            }
        }

        /// <summary>
        /// Applies the L-18 config feature flags to the persisted record once per activation. A
        /// disabled pipe/logistics feature forces its port off (the panel then renders its existing
        /// "not applicable/disabled" state without any UI change); a disabled rotation feature
        /// converts a rotation record to the first live faction crop. Saved fields are never deleted
        /// (REQ-ARCH-09): re-enabling a feature restores its documented behaviour.
        /// </summary>
        private void ApplyFeatureFlags()
        {
            var record = _record;
            if (record is null)
            {
                return;
            }

            var flags = SafeFeatures();
            var changed = false;
            if (!flags.PipeConnection && record.PipePort)
            {
                record.PipePort = false;
                changed = true;
            }

            if (!flags.LogisticsOutput && record.LogisticsPort)
            {
                record.LogisticsPort = false;
                changed = true;
            }

            if (!flags.Rotation && record.CropPolicy == CropPolicy.Rotation && TryConvertRotationToFixed(record))
            {
                changed = true;
            }

            if (changed)
            {
                _saveDirty = true;
            }

            LogDisabledFeaturesOnce(flags);
        }

        /// <summary>
        /// L-18 <c>features.rotation=false</c> startup fallback: a rotation record becomes a fixed
        /// record of the first live faction crop. Without a live crop table the rotation record is
        /// kept (fixed crop id unknown), and the Core scheduler then plants the first fallback crop
        /// without ever rotating.
        /// </summary>
        /// <param name="record">Record to convert in place.</param>
        /// <returns>True when the record changed.</returns>
        private bool TryConvertRotationToFixed(IndoorFarmRecord record)
        {
            var firstCrop = FirstLiveFactionCropId();
            if (firstCrop is null)
            {
                return false;
            }

            record.CropPolicy = CropPolicy.Fixed;
            record.FixedCropId = firstCrop;
            _log.Write(HdbfLogCategory.Info, IndoorLogKeys.CropPolicy,
                "features.rotation disabled; crop policy converted to fixed '" + firstCrop + "', entityId="
                + _entityId);
            return true;
        }

        /// <summary>First crop id of the live faction catalog, or null when there is none.</summary>
        /// <returns>Crop id of the first live entry; null for an empty/broken catalog.</returns>
        private string? FirstLiveFactionCropId()
        {
            try
            {
                var crops = _cropCatalog.ForFaction(_faction);
                if (crops is null)
                {
                    return null;
                }

                for (var index = 0; index < crops.Count; index++)
                {
                    var crop = crops[index];
                    if (crop is not null && !string.IsNullOrEmpty(crop.Id))
                    {
                        return crop.Id;
                    }
                }
            }
            catch (Exception)
            {
                // A broken live catalog degrades to "no crop table"; the fallback table still plants.
            }

            return null;
        }

        /// <summary>
        /// Emits the one-time <c>indoor.feature-disabled</c> diagnostic of this building (L-18): every
        /// config feature that changes behaviour is named with its effect, so a farm that behaves
        /// differently from the docs is explainable from the player log.
        /// </summary>
        /// <param name="flags">Resolved feature flags of the session.</param>
        private void LogDisabledFeaturesOnce(FeatureFlags flags)
        {
            if (_featureFlagsLogged)
            {
                return;
            }

            _featureFlagsLogged = true;
            if (!flags.Maintenance)
            {
                LogFeatureDisabled("features.maintenance",
                    "material maintenance is free and never expires (G_maint stays 1)");
            }

            if (!flags.PipeConnection)
            {
                LogFeatureDisabled("features.pipeConnection",
                    "the pipe port is forced off and pipe water is not used");
            }

            if (!flags.LogisticsOutput)
            {
                LogFeatureDisabled("features.logisticsOutput", "the logistics port is forced off");
            }

            if (!flags.Rotation)
            {
                LogFeatureDisabled("features.rotation", "crop rotation is disabled; a fixed crop is planted");
            }

            if (!flags.SoilFatigue)
            {
                LogFeatureDisabled("features.soilFatigue",
                    "soil fatigue does not accumulate and never reduces yield (G_fat stays 1)");
            }
        }

        private void LogFeatureDisabled(string flag, string effect)
        {
            _log.Write(HdbfLogCategory.Info, IndoorLogKeys.FeatureDisabled,
                "config feature disabled: " + flag + " (" + effect + "), entityId=" + _entityId);
        }
    }
}
