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
        /// <summary>Reused traversal buffer of the dropdown labels (filled after every rebuild).</summary>
        private readonly List<Label> _dropdownLabels = new(8);

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
                dropdown.style.flexShrink = 1f;
                dropdown.style.fontSize = 13f;
                // Inheritance is NOT enough here: the vanilla .dropdown-item__text rule sets its own
                // grey colour, so PaintDropdownText re-paints the selection label explicitly after
                // every rebuild (and PaintDropdownPopupText does the same for the open popup list).
                HdbfPanelControls.ApplyLightText(dropdown);
                _cropProvider.Selected += OnCropSelected;
                dropdown.Showed += OnCropDropdownShowed;
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

            // The vanilla selection item is (re)built by the calls above and its Label carries the
            // vanilla grey ".dropdown-item__text" colour; re-paint it after every rebuild so the
            // closed panel has 100 % light text (requirement D). The item keeps the vanilla
            // nowrap/ellipsis: the selection is 28 px high and crop names are short, so wrapping it
            // could overflow; the panel rows that may be long already wrap (requirement H).
            PaintDropdownText();
            _cropDropdown.tooltip = data.CropTooltip;
        }

        /// <summary>
        /// Paints every label of the vanilla dropdown selection subtree (selection item, its label
        /// prefix) with the panel's one light text colour. The selection item is recreated by
        /// <c>SetItems</c>/<c>UpdateSelectedValue</c>, so this runs after both of them. The label
        /// list is reused, so a normal refresh allocates nothing.
        /// </summary>
        private void PaintDropdownText()
        {
            if (_cropDropdown is null)
            {
                return;
            }

            _dropdownLabels.Clear();
            CollectLabels(_cropDropdown, _dropdownLabels);
            for (var index = 0; index < _dropdownLabels.Count; index++)
            {
                HdbfPanelControls.ApplyLightText(_dropdownLabels[index]);
            }
        }

        /// <summary>
        /// The open dropdown list lives in the singleton <c>DropdownListDrawer</c>, outside the
        /// Dropdown subtree, and its items are cloned <c>Core/DropdownItem</c> labels with the
        /// vanilla grey <c>.dropdown-item__text</c> colour. The popup is attached during the show
        /// event, so the labels are collected on the next scheduler tick and re-painted light
        /// (colour only - the list rows stay vanilla nowrap/ellipsis). Failures are swallowed: a
        /// popup must never break the entity panel.
        /// </summary>
        private void PaintDropdownPopupText()
        {
            try
            {
                var panel = _cropDropdown?.panel;
                if (panel is null)
                {
                    return;
                }

                _dropdownLabels.Clear();
                CollectDropdownItemLabels(panel.visualTree, _dropdownLabels);
                for (var index = 0; index < _dropdownLabels.Count; index++)
                {
                    HdbfPanelControls.ApplyLightText(_dropdownLabels[index]);
                }
            }
            catch (Exception)
            {
                // A popup that cannot be painted still works; the panel keeps running.
            }
        }

        private void OnCropDropdownShowed(object? sender, EventArgs args)
        {
            // Defer one tick so the drawer has attached the freshly cloned item labels.
            _cropDropdown?.schedule.Execute(PaintDropdownPopupText);
        }

        /// <summary>Collects the labels of a subtree into a reused buffer (depth-first).</summary>
        private static void CollectLabels(VisualElement root, List<Label> labels)
        {
            for (var index = 0; index < root.childCount; index++)
            {
                var child = root[index];
                if (child is Label label)
                {
                    labels.Add(label);
                }

                if (child.childCount > 0)
                {
                    CollectLabels(child, labels);
                }
            }
        }

        /// <summary>Collects the popup list labels (class <c>dropdown-item__text</c>) of one panel.</summary>
        private static void CollectDropdownItemLabels(VisualElement root, List<Label> labels)
        {
            for (var index = 0; index < root.childCount; index++)
            {
                var child = root[index];
                if (child is Label label && label.ClassListContains("dropdown-item__text"))
                {
                    labels.Add(label);
                }

                if (child.childCount > 0)
                {
                    CollectDropdownItemLabels(child, labels);
                }
            }
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
