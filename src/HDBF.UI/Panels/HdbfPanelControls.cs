// ============================================================================
// HDBF v2 | Layer: UI/Panels | Owner: V2-UI-01
// Purpose: Builders of the individual vanilla UIElements the v2 panel is composed from: cards
//          (NineSliceVisualElement + sub-box classes), section titles, icon+label+value rows,
//          progress bars (core ProgressBar with its value label and C_min marker), warning rows
//          (status-list structure), the badge, game buttons and worker chips. Buttons and worker
//          chips are instantiated from the SHIPPED vanilla UXML templates (DebugButton.uxml /
//          WorkerView.uxml) through VisualElementLoader, so the gold frame/hover/green chip styling
//          is the original one; only addToClassList() and layout styles are added on top.
// Requirements: REQ-V2-UI-01/02/03, REQ-V2-READ-04
// ============================================================================
using System;
using HDBF.UI.Localization;
using Timberborn.CoreUI;
using UnityEngine;
using UnityEngine.UIElements;
using CoreProgressBar = Timberborn.CoreUI.ProgressBar;

namespace HDBF.UI.Panels
{
    /// <summary>
    /// Factory of the vanilla controls and row structures of the v2 panel. Every class name comes
    /// from <see cref="HdbfIndoorPanelStyle"/> (copied from the shipped vanilla UXML/USS).
    /// </summary>
    public static class HdbfPanelControls
    {
        /// <summary>Creates a vanilla sub-panel card with the requested tone.</summary>
        /// <param name="tone">"green", "blue", "purple" or "red".</param>
        /// <returns>A NineSliceVisualElement that draws the vanilla card frame.</returns>
        public static NineSliceVisualElement CreateCard(string tone)
        {
            var card = new NineSliceVisualElement();
            card.AddToClassList(HdbfIndoorPanelStyle.Card);
            card.AddToClassList(HdbfIndoorPanelStyle.CardTone(tone));
            card.style.marginBottom = 4f;
            return card;
        }
        /// <summary>Creates the vanilla badge box (green sub-box frame + bold text).</summary>
        /// <param name="text">Localized badge text (the current mode).</param>
        /// <returns>Badge element.</returns>
        public static VisualElement CreateBadge(string text, out Label label)
        {
            var badge = new NineSliceVisualElement();
            badge.AddToClassList("bg-box--green");
            badge.style.paddingLeft = 6f;
            badge.style.paddingRight = 6f;
            badge.style.paddingTop = 1f;
            badge.style.paddingBottom = 1f;
            badge.style.marginLeft = 4f;
            badge.style.alignSelf = Align.Center;

            label = new Label(text);
            label.AddToClassList(HdbfIndoorPanelStyle.RowValue);
            badge.Add(label);
            return badge;
        }
        /// <summary>Creates the title row of a card (vanilla 24 px icon + heading text).</summary>
        /// <param name="section">Card model with its title and semantic icon key.</param>
        /// <param name="icons">Sprite cache of this panel.</param>
        /// <returns>Row element ready to be added to the card.</returns>
        public static VisualElement CreateSectionTitle(HdbfPanelSection section, HdbfPanelIcons icons)
        {
            var row = CreateRowContainer();
            AddIcon(row, section.IconKey, icons);
            var title = new Label(section.Title);
            title.AddToClassList(HdbfIndoorPanelStyle.SectionTitle);
            title.style.flexGrow = 1f;
            title.style.flexShrink = 1f;
            row.Add(title);
            row.style.marginBottom = 4f;
            return row;
        }
        /// <summary>
        /// Creates one "icon + label + value" row. The value label is returned so the caller can
        /// re-set it without rebuilding the tree (no per-frame allocation).
        /// </summary>
        /// <param name="row">Row model.</param>
        /// <param name="icons">Sprite cache of this panel.</param>
        /// <param name="valueLabel">The created value label (never null).</param>
        /// <returns>Row element with vanilla classes applied.</returns>
        public static VisualElement CreateRow(HdbfPanelRow row, HdbfPanelIcons icons, out Label valueLabel)
        {
            var root = CreateRowContainer();
            AddIcon(root, row.IconKey, icons);
            var label = new Label(row.IsWarning ? row.Marker + " " + row.Label : row.Label);
            label.AddToClassList(row.IsWarning
                ? HdbfIndoorPanelStyle.WarningText
                : HdbfIndoorPanelStyle.RowLabel);
            label.style.flexGrow = 1f;
            label.style.flexShrink = 1f;
            root.Add(label);

            valueLabel = new Label(row.Value);
            valueLabel.AddToClassList(HdbfIndoorPanelStyle.RowValue);
            valueLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            valueLabel.style.flexShrink = 0f;
            valueLabel.style.marginLeft = 4f;
            root.Add(valueLabel);

            SetTooltip(root, row.Tooltip);
            return root;
        }
        /// <summary>
        /// Creates the vanilla progress bar of a row, including its in-bar value/unit label and the
        /// optional C_min marker. The returned bar is ready to add below the row element.
        /// </summary>
        /// <param name="row">Row model with <see cref="HdbfPanelRow.HasBar"/> set.</param>
        /// <param name="critical">True for the red fill variant (critical water).</param>
        /// <returns>The vanilla ProgressBar control.</returns>
        public static CoreProgressBar CreateBar(HdbfPanelRow row, bool critical)
        {
            var bar = new CoreProgressBar();
            bar.AddToClassList(HdbfIndoorPanelStyle.ProgressBar);
            bar.AddToClassList(critical
                ? HdbfIndoorPanelStyle.ProgressBarRed
                : HdbfIndoorPanelStyle.ProgressBarGreen);
            bar.style.marginTop = 2f;
            bar.style.marginBottom = 2f;
            bar.SetProgress(HdbfIndoorPanelLimits.Fill(row.Bar));

            var text = new Label(row.BarText);
            text.AddToClassList(HdbfIndoorPanelStyle.ProgressText);
            bar.contentContainer.Add(text);

            if (!float.IsNaN(row.BarMarker))
            {
                var marker = new VisualElement();
                marker.AddToClassList(HdbfIndoorPanelStyle.ProgressMarker);
                marker.style.left = Length.Percent(HdbfIndoorPanelLimits.Fill(row.BarMarker) * 100f);
                marker.pickingMode = PickingMode.Ignore;
                bar.Add(marker);
            }

            return bar;
        }
        /// <summary>Creates a vanilla status row: warning icon + the localized sentence.</summary>
        /// <param name="row">Warning row model.</param>
        /// <param name="icons">Sprite cache of this panel.</param>
        /// <returns>Status row element.</returns>
        public static VisualElement CreateWarningRow(HdbfPanelRow row, HdbfPanelIcons icons)
        {
            var root = new VisualElement();
            root.AddToClassList(HdbfIndoorPanelStyle.WarningRow);
            var icon = new Image { sprite = icons.Get(row.IconKey) };
            icon.AddToClassList(HdbfIndoorPanelStyle.WarningIcon);
            icon.pickingMode = PickingMode.Ignore;
            root.Add(icon);

            var text = new Label(row.Marker + " " + row.Label);
            text.AddToClassList(HdbfIndoorPanelStyle.WarningText);
            text.style.flexGrow = 1f;
            text.style.flexShrink = 1f;
            root.Add(text);
            SetTooltip(root, row.Tooltip);
            return root;
        }

        /// <summary>
        /// Creates a vanilla game button: the shipped DebugButton UXML is a plain NineSliceButton
        /// with the <c>button-game</c> class, so the gold nine-slice frame, hover and click sound are
        /// the original ones. A missing template degrades to a plain Button with the same classes.
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
            button.AddToClassList(HdbfIndoorPanelStyle.Button);
            button.RemoveFromClassList("debug-fragment__button");
            if (onClick is not null)
            {
                button.clicked += onClick;
            }

            return button;
        }

        /// <summary>
        /// Creates one read-only worker chip from the shipped WorkerView UXML (vanilla avatar slot,
        /// name and vacant warning triangle). The chip is disabled so it never looks editable, and
        /// a missing template falls back to a plain worker-view row.
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
            }

            var vacantIcon = root.Q<VisualElement>("VacantIcon");
            if (vacantIcon is not null)
            {
                vacantIcon.style.display = chip.Vacant ? DisplayStyle.Flex : DisplayStyle.None;
            }

            var avatar = root.Q<Button>("CharacterButton");
            if (avatar is not null)
            {
                var sprite = chip.Vacant ? null : icons.Get("worker");
                avatar.style.display = sprite is null ? DisplayStyle.None : DisplayStyle.Flex;
                if (sprite is not null)
                {
                    avatar.style.backgroundImage = new StyleBackground(sprite);
                }
            }

            if (root is Button chipButton)
            {
                // Vanilla .worker-view:disabled keeps opacity 1, so the chip reads as a normal row
                // while being honestly read-only (no click action is registered).
                chipButton.focusable = false;
                chipButton.SetEnabled(false);
            }

            SetTooltip(root, chip.Tooltip);
            return root;
        }

        /// <summary>Adds the localized tooltip to an element, ignoring empty text.</summary>
        /// <param name="element">Target element.</param>
        /// <param name="tooltip">Localized tooltip; empty clears nothing.</param>
        public static void SetTooltip(VisualElement element, string? tooltip)
        {
            if (element is not null)
            {
                element.tooltip = tooltip ?? string.Empty;
            }
        }

        private static VisualElement CreateFallbackWorkerChip()
        {
            var root = new VisualElement();
            root.AddToClassList(HdbfIndoorPanelStyle.WorkerView);
            var vacant = new VisualElement();
            vacant.AddToClassList(HdbfIndoorPanelStyle.WorkerVacant);
            vacant.pickingMode = PickingMode.Ignore;
            root.Add(vacant);
            var label = new Label();
            label.AddToClassList(HdbfIndoorPanelStyle.WorkerName);
            label.style.flexGrow = 1f;
            label.style.flexShrink = 1f;
            root.Add(label);
            return root;
        }

        private static VisualElement CreateRowContainer()
        {
            var row = new VisualElement();
            row.AddToClassList(HdbfIndoorPanelStyle.Row);
            return row;
        }

        private static void AddIcon(VisualElement parent, string iconKey, HdbfPanelIcons icons)
        {
            var sprite = icons.Get(iconKey);
            if (sprite is null)
            {
                // Icon missing is not an error: the row still renders, just without the picture.
                return;
            }

            var image = new Image { sprite = sprite };
            image.AddToClassList(HdbfIndoorPanelStyle.RowIcon);
            image.pickingMode = PickingMode.Ignore;
            parent.Add(image);
        }
    }
}
