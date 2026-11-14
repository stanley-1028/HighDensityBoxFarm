// ============================================================================
// HDBF v2 | Layer: UI/Panels | Owner: V2-UI-01
// Purpose: Widget builders of the v2 panel: the vanilla gold game button (NineSliceButton with the
//          shipped .button-game / .entity-fragment__button classes - the same class pair every
//          vanilla action button uses), the vanilla fold clamp (.extension-clamp, the game's own
//          expand/collapse affordance) and the worker chip loaded from the shipped WorkerView UXML.
//          Vacant chips use the vanilla vacant look (empty beaver avatar, disabled, NO warning
//          triangle - the triangle is reserved for real problems). A missing template degrades to a
//          plain control with the same vanilla classes instead of throwing.
// Requirements: REQ-V2-UI-01/02/03, REQ-V2-SAFE-01
// ============================================================================
using System;
using Timberborn.CoreUI;
using UnityEngine.UIElements;

namespace HDBF.UI.Panels
{
    public static partial class HdbfPanelControls
    {
        /// <summary>
        /// Creates the vanilla game button from the shipped <c>DebugButton</c> nine-slice template
        /// (the only generic gold-button UXML the game ships): the template root is a
        /// NineSliceButton, so the nine-slice frame and click sound are the original ones. The
        /// debug-only class is removed and the button keeps the vanilla action-button class pair
        /// <c>button-game entity-fragment__button</c>; no font size or height is forced - the
        /// vanilla classes own the geometry. A missing template degrades to a plain Button with the
        /// same classes.
        /// </summary>
        /// <param name="loader">Vanilla UXML loader from the game container.</param>
        /// <param name="text">Localized button label.</param>
        /// <param name="onClick">Click callback.</param>
        /// <returns>Focusable vanilla button.</returns>
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
            AddClasses(button, HdbfIndoorPanelStyle.RowLabel);
            button.RemoveFromClassList("debug-fragment__button");
            button.style.whiteSpace = WhiteSpace.Normal;
            if (onClick is not null)
            {
                button.clicked += onClick;
            }

            return button;
        }

        /// <summary>
        /// Creates the vanilla fold clamp: the shipped <c>.extension-clamp</c> Button the game uses
        /// on every collapsible panel (top bar counters, district panel, notifications). The caller
        /// owns the open/closed state through <see cref="ApplyClampState"/>.
        /// </summary>
        /// <param name="onClick">Click callback.</param>
        /// <returns>The clamp button.</returns>
        public static Button CreateClampButton(Action onClick)
        {
            var clamp = new Button();
            clamp.AddToClassList(HdbfIndoorPanelStyle.Clamp);
            clamp.focusable = true;
            if (onClick is not null)
            {
                clamp.clicked += onClick;
            }

            return clamp;
        }

        /// <summary>
        /// Applies the vanilla clamp state: open shows the clamp-up strip
        /// (<c>.extension-clamp</c>), folded adds the shipped <c>.extension-clamp--hidden</c>
        /// class which swaps to the clamp-down sprite and the smaller geometry.
        /// </summary>
        /// <param name="clamp">Clamp button created by <see cref="CreateClampButton"/>.</param>
        /// <param name="open">True when the card body is visible.</param>
        public static void ApplyClampState(Button? clamp, bool open)
        {
            if (clamp is null)
            {
                return;
            }

            clamp.EnableInClassList(HdbfIndoorPanelStyle.ClampHidden, !open);
        }

        /// <summary>
        /// Creates one read-only worker chip from the shipped WorkerView UXML (vanilla avatar slot,
        /// name and vacant state). A staffed chip shows the beaver icon; a vacant chip uses the
        /// vanilla empty beaver ("vacant") avatar and hides the warning triangle.
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
            var label = CreateLabel(string.Empty, HdbfIndoorPanelStyle.WorkerName);
            // Must carry the vanilla template name: CreateWorkerChip looks the name label up by it.
            label.name = "Name";
            label.style.flexGrow = 1f;
            root.Add(label);
            return root;
        }
    }
}
