// ============================================================================
// HDBF v2 | Layer: Gameplay/Components | Owner: V2-GAMEPLAY-01 / V2-GAMEPLAY-02
// Purpose: Crop-policy command of one indoor farm: rotation or one fixed crop of the building
//          faction's catalog (REQ-V2-EXT-01), validated before any state changes. Split out of
//          HdbfBoxFarmController.Commands.cs to keep that file inside the 300-line soft gate.
// Requirements: REQ-V2-EXT-01, REQ-V2-UI-02, REQ-V2-SAFE-01, REQ-V2-MAINT-02
// ============================================================================
using System;
using HDBF.Core;
using HDBF.Core.Abstractions;
using HDBF.Core.Indoor;
using HDBF.Core.Logging;

namespace HDBF.Gameplay.Components
{
    public partial class HdbfBoxFarmController
    {
        /// <summary>
        /// Sets the crop policy. A fixed crop must belong to the building faction's catalog
        /// (REQ-V2-EXT-01); an invalid choice changes nothing and returns a specific failure key.
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
    }
}
