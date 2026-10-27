// ============================================================================
// HDBF v2 | Layer: UI/Panels | Owner: V2-UI-01
// Purpose: Crop-strategy block of the indoor panel: one row (icon + label + current policy text)
//          with the real vanilla Dropdown control attached. The dropdown works on localized
//          display strings, so the list is rebuilt only when the localized choice signature changed
//          and a 4 Hz refresh never reallocates the items (REQ-V2-UI-01).
// Requirements: REQ-V2-UI-01/02/03
// ============================================================================
using System;
using System.Collections.Generic;
using System.Text;
using HDBF.UI.Localization;
using Timberborn.DropdownSystem;
using UnityEngine.UIElements;

namespace HDBF.UI.Panels
{
    public sealed partial class HdbfFarmPanelFragment
    {
        private VisualElement? _cropHost;
        private Dropdown? _cropDropdown;
        private string _cropSignature = string.Empty;
        private readonly HdbfCropDropdownProvider _cropProvider = new();

        private void BuildCropDropdown()
        {
            _cropHost = new VisualElement();
            _cropHost.AddToClassList(HdbfIndoorPanelStyle.Dropdown);
            _cropHost.style.flexGrow = 1f;
            _cropHost.style.flexShrink = 1f;
            _cropHost.style.marginLeft = 4f;

            try
            {
                // Vanilla Dropdown control: clones UI/Views/Core/Dropdown (NineSliceButton selection
                // + Label + arrow buttons) and needs the popup drawer from the game container.
                var dropdown = new Dropdown();
                dropdown.Initialize(_dropdownListDrawer);
                dropdown.Localize(_loc);
                dropdown.style.flexGrow = 1f;
                _cropProvider.Selected += OnCropSelected;
                _tooltips.Register(dropdown, () => _lastData?.CropTooltip ?? string.Empty);
                _cropDropdown = dropdown;
                _cropHost.Add(dropdown);
            }
            catch (Exception)
            {
                // A missing dropdown asset degrades to the text-only policy row; never throws.
                _cropDropdown = null;
            }
        }

        private void RenderCrop(HdbfIndoorPanelData data)
        {
            if (_cropDropdown is null)
            {
                return;
            }

            _cropProvider.Load(data.CropChoices, data.SelectedCropId);
            var signature = CropSignature(data.CropChoices);
            if (!string.Equals(signature, _cropSignature, StringComparison.Ordinal))
            {
                _cropSignature = signature;
                _dropdownItemsSetter.SetItems(_cropDropdown, _cropProvider);
            }
            else
            {
                _cropDropdown.UpdateSelectedValue();
            }

            _cropDropdown.tooltip = data.CropTooltip;
        }

        /// <summary>
        /// Re-syncs the dropdown display after a command: the vanilla control writes the clicked
        /// text after the change callback returns, so a rejected command needs one deferred refresh.
        /// </summary>
        private void ScheduleCropValueSync()
        {
            if (_cropDropdown is null)
            {
                return;
            }

            _cropDropdown.schedule.Execute(() => _cropDropdown?.UpdateSelectedValue());
        }

        private static string CropSignature(IReadOnlyList<HdbfCropChoice> choices)
        {
            if (choices is null || choices.Count == 0)
            {
                return string.Empty;
            }

            var builder = new StringBuilder(choices.Count * 16);
            for (var index = 0; index < choices.Count; index++)
            {
                builder.Append(choices[index].Id).Append('|').Append(choices[index].Text).Append(';');
            }

            return builder.ToString();
        }

        /// <summary>
        /// Adapter between the vanilla dropdown and the Core crop policy. The vanilla control shows
        /// the strings of <see cref="Items"/> and calls <see cref="SetValue"/> with the clicked
        /// display text, so each item is one localized crop name (or "auto rotation").
        /// </summary>
        private sealed class HdbfCropDropdownProvider : IDropdownProvider
        {
            private readonly List<string> _items = new();
            private readonly List<string> _ids = new();
            private string _value = string.Empty;

            /// <summary>Raised with the index of the clicked choice.</summary>
            public event Action<int>? Selected;

            /// <inheritdoc />
            public IReadOnlyList<string> Items => _items;

            /// <inheritdoc />
            public string GetValue() => _value;

            /// <inheritdoc />
            public void SetValue(string value)
            {
                var index = _items.IndexOf(value);
                if (index < 0)
                {
                    _value = value ?? string.Empty;
                    return;
                }

                _value = value;
                Selected?.Invoke(index);
            }

            /// <summary>Fills the items and syncs the displayed value from the read model.</summary>
            /// <param name="choices">Localized dropdown choices of the current snapshot.</param>
            /// <param name="selectedId">Selected crop id ("" = auto rotation).</param>
            public void Load(IReadOnlyList<HdbfCropChoice> choices, string? selectedId)
            {
                _items.Clear();
                _ids.Clear();
                _value = string.Empty;
                if (choices is null)
                {
                    return;
                }

                var wanted = selectedId ?? string.Empty;
                for (var index = 0; index < choices.Count; index++)
                {
                    var choice = choices[index];
                    _items.Add(choice.Text);
                    _ids.Add(choice.Id);
                    if (string.Equals(choice.Id, wanted, StringComparison.Ordinal) && _value.Length == 0)
                    {
                        _value = choice.Text;
                    }
                }

                if (_value.Length == 0 && _items.Count > 0)
                {
                    _value = _items[0];
                }
            }

            /// <summary>Crop id of one dropdown index; empty for auto rotation/out of range.</summary>
            /// <param name="index">Dropdown item index.</param>
            /// <returns>Crop id or empty.</returns>
            public string IdAt(int index)
            {
                return index >= 0 && index < _ids.Count ? _ids[index] : string.Empty;
            }
        }
    }
}
