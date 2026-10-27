// ============================================================================
// HDBF v2 | Layer: UI/Panels | Owner: V2-UI-01
// Purpose: Widget builders of the v2 panel that instantiate SHIPPED vanilla UXML templates:
//          the gold game button (DebugButton.uxml, used for refill/maintenance) and the worker
//          chip (WorkerView.uxml, used by the read-only roster). Vacant chips use the vanilla
//          vacant look (empty beaver avatar, disabled, NO warning triangle - requirement G: the
//          triangle is reserved for real problems). A missing template degrades to a plain
//          Button/worker row instead of throwing.
// Requirements: REQ-V2-UI-01/02/03, REQ-V2-SAFE-01
// ============================================================================
using System;
using Timberborn.CoreUI;
using UnityEngine;
using UnityEngine.UIElements;

namespace HDBF.UI.Panels
{
    public static partial class HdbfPanelControls
    {
        /// <summary>
        /// Creates a vanilla game button: the shipped DebugButton UXML is a plain NineSliceButton
        /// with the <c>button-game</c> class, so the gold nine-slice frame, hover and click sound are
        /// the original ones. A missing template degrades to a plain Button with the same classes.
        /// </summary>
        /// <param name="loader">Vanilla UXML loader from the game container.</param>
        /// <param name="text">Localized button label.</param>
        /// <param name="onClick">Click callback.</param>
        /// <returns>Focusable vanilla button with light text.</returns>
        public static Button CreateButton(VisualElementLoader loader, string text, Action onClick)
        {
            Button? button = null;
            try
            {
                button = loader.LoadVisualElement(HdbfIndoorPanelStyle.ButtonTemplate) as Button;
            }
            catch (Exception)
            {
                // A missing template is not a panel failure; the plain Button below still works.
            }

            button ??= new Button();
            button.text = text;
            button.focusable = true;
            AddClasses(button, HdbfIndoorPanelStyle.Button);
            button.RemoveFromClassList("debug-fragment__button");
            ApplyLightText(button);
            button.style.whiteSpace = WhiteSpace.Normal;
            button.style.minHeight = 26f;
            button.style.fontSize = 13f;
            if (onClick is not null)
            {
                button.clicked += onClick;
            }

            return button;
        }

        /// <summary>
        /// Creates one read-only worker chip from the shipped WorkerView UXML (vanilla avatar slot,
        /// name and vacant state). A staffed chip shows the beaver icon; a vacant chip uses the
        /// vanilla empty beaver ("vacant") avatar and hides the warning triangle (requirement G).
        /// The chip is disabled so it never looks editable, and vanilla ".worker-view:disabled"
        /// keeps opacity 1.
        /// </summary>
        /// <param name="loader">Vanilla UXML loader from the game container.</param>
        /// <param name="chip">Chip model.</param>
        /// <param name="icons">Sprite cache of this panel.</param>
        /// <returns>Worker chip element.</returns>
        public static VisualElement CreateWorkerChip(VisualElementLoader loader, HdbfWorkerChip chip,
            HdbfPanelIcons icons)
        {
            VisualElement? root = null;
            try
            {
                root = loader.LoadVisualElement(HdbfIndoorPanelStyle.WorkerChipTemplate);
            }
            catch (Exception)
            {
                // Fall through to the plain structure below.
            }

            root ??= CreateFallbackWorkerChip();
            var name = root.Q<Label>("Name");
            if (name is not null)
            {
                name.text = chip.Name;
                ApplyLightText(name);
                name.style.whiteSpace = WhiteSpace.Normal;
            }

            // The vacant triangle is never shown: a free slot is normal state, not a problem.
            var vacantIcon = root.Q<VisualElement>("VacantIcon");
            if (vacantIcon is not null)
            {
                vacantIcon.style.display = DisplayStyle.None;
            }

            var avatar = root.Q<Button>("CharacterButton");
            if (avatar is not null)
            {
                var sprite = icons.Get(chip.Vacant ? "vacant" : "worker");
                avatar.style.display = sprite is null ? DisplayStyle.None : DisplayStyle.Flex;
                if (sprite is not null)
                {
                    avatar.style.backgroundImage = new StyleBackground(sprite);
                }
            }

            if (root is Button chipButton)
            {
                chipButton.focusable = false;
                chipButton.SetEnabled(false);
            }

            SetTooltip(root, chip.Tooltip);
            return root;
        }

        private static VisualElement CreateFallbackWorkerChip()
        {
            var root = new VisualElement();
            root.AddToClassList(HdbfIndoorPanelStyle.WorkerView);
            var vacant = new VisualElement();
            vacant.AddToClassList(HdbfIndoorPanelStyle.WorkerVacant);
            vacant.style.display = DisplayStyle.None;
            vacant.pickingMode = PickingMode.Ignore;
            root.Add(vacant);
            var label = CreateLabel(string.Empty, HdbfIndoorPanelStyle.WorkerName, 0f);
            label.style.flexGrow = 1f;
            root.Add(label);
            return root;
        }
    }
}
