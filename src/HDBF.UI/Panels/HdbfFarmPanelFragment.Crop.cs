// ============================================================================
// HDBF v2 | Layer: UI/Panels | Owner: V2-UI-01
// Purpose: Crop-strategy block of the indoor panel: rotation/fixed label rows and the fixed-crop
//          dropdown. The choices are rebuilt only when their signature changed, so a 4 Hz
//          refresh never reallocates the dropdown list (REQ-V2-UI-01).
// Requirements: REQ-V2-UI-01/02/03
// ============================================================================
using System;
using System.Collections.Generic;
using HDBF.UI.Localization;

namespace HDBF.UI.Panels
{
    public sealed partial class HdbfFarmPanelFragment
    {
        private void RenderCrop(HdbfIndoorPanelData data)
        {
            SetHeader(_cropsHeader, HdbfUiLocKeys.PanelSectionCrops);
            Set(_cropPolicy, data.CropPolicyText);
            SetOptional(_fixedCrop, data.FixedCropText);
            SetOptional(_rotation, data.RotationText);
            if (_cropDropdown is null)
            {
                return;
            }

            _cropDropdown.tooltip = data.CropTooltip;
            var signature = CropSignature(data.CropChoices);
            if (!string.Equals(signature, _cropSignature, StringComparison.Ordinal))
            {
                _cropSignature = signature;
                _cropChoiceIds.Clear();
                var texts = new List<string>(data.CropChoices.Count);
                for (var index = 0; index < data.CropChoices.Count; index++)
                {
                    texts.Add(data.CropChoices[index].Text);
                    _cropChoiceIds.Add(data.CropChoices[index].Id);
                }

                _suppressCropChange = true;
                _cropDropdown.choices = texts;
                _suppressCropChange = false;
            }

            var selected = IndexOfCrop(data.SelectedCropId);
            if (selected >= 0 && selected < _cropChoiceIds.Count && _cropDropdown.index != selected)
            {
                _suppressCropChange = true;
                _cropDropdown.index = selected;
                _suppressCropChange = false;
            }
        }

        private int IndexOfCrop(string? cropId)
        {
            var wanted = cropId ?? string.Empty;
            for (var index = 0; index < _cropChoiceIds.Count; index++)
            {
                if (string.Equals(_cropChoiceIds[index], wanted, StringComparison.Ordinal))
                {
                    return index;
                }
            }

            return -1;
        }

        private static string CropSignature(IReadOnlyList<HdbfCropChoice> choices)
        {
            if (choices is null || choices.Count == 0)
            {
                return string.Empty;
            }

            var builder = new System.Text.StringBuilder(choices.Count * 12);
            for (var index = 0; index < choices.Count; index++)
            {
                builder.Append(choices[index].Id).Append('|').Append(choices[index].Text).Append(';');
            }

            return builder.ToString();
        }
    }
}
