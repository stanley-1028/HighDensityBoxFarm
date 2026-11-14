// ============================================================================
// HDBF v2 | Layer: UI/Panels | Owner: V2-UI-01
// Purpose: Builders of the individual vanilla UIElements the v2 panel is composed from: cards
//          (NineSliceVisualElement + sub-box classes), section titles, the compact tier/faction
//          subtitle row, icon+label+value rows, progress bars (core ProgressBar with its value
//          label and C_min marker) and warning rows (the shipped StatusListElement template).
//          Buttons, the fold clamp and worker chips live in HdbfPanelControls.Widgets.cs.
//          Every class name and every template path comes from HdbfIndoorPanelStyle; no colour,
//          font size or spacing is invented here (vanilla text is rgb(204,204,204) from the
//          shipped classes, so the panel never paints text).
// Requirements: REQ-V2-UI-01/02/03, REQ-V2-READ-04
// ============================================================================
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
    public static partial class HdbfPanelControls
    {
        /// <summary>Adds every class of a space separated vanilla class list, one call per name.</summary>
        /// <param name="element">Target element.</param>
        /// <param name="classNames">Vanilla class list, e.g. "entity-panel__text text--bold".</param>
        public static void AddClasses(VisualElement element, string? classNames)
        {
            if (element is null)
            {
                return;
            }

            var names = HdbfIndoorPanelStyle.Split(classNames);
            for (var index = 0; index < names.Length; index++)
            {
                element.AddToClassList(names[index]);
            }
        }

        /// <summary>Creates a label with the vanilla class list. The colour comes from the class.</summary>
        /// <param name="text">Localized text.</param>
        /// <param name="classNames">Vanilla class list or empty.</param>
        /// <returns>Ready-to-add label.</returns>
        public static Label CreateLabel(string text, string? classNames)
        {
            var label = new Label(text);
            AddClasses(label, classNames);
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.flexShrink = 1f;
            return label;
        }

        /// <summary>Creates a vanilla sub-panel card with the requested tone (no gap between cards).</summary>
        /// <param name="tone">"green", "blue" or "red".</param>
        /// <returns>A NineSliceVisualElement that draws the vanilla card frame.</returns>
        public static NineSliceVisualElement CreateCard(string tone)
        {
            var card = new NineSliceVisualElement();
            card.AddToClassList(HdbfIndoorPanelStyle.Card);
            card.AddToClassList(HdbfIndoorPanelStyle.CardTone(tone));
            return card;
        }

        /// <summary>Creates the title row of a card (vanilla 24 px icon + heading text).</summary>
        /// <param name="title">Localized card title.</param>
        /// <param name="iconKey">Semantic icon key of the card title.</param>
        /// <param name="icons">Sprite cache of this panel.</param>
        /// <returns>Row element ready to be added to the card.</returns>
        public static VisualElement CreateTitleRow(string title, string iconKey, HdbfPanelIcons icons)
        {
            var row = CreateRowContainer();
            AddIcon(row, iconKey, icons);
            var label = CreateLabel(title, HdbfIndoorPanelStyle.SectionTitle);
            label.style.flexGrow = 1f;
            row.Add(label);
            return row;
        }

        /// <summary>
        /// Creates the compact subtitle row that replaces the deleted duplicate header: one line
        /// "tier · faction · mode" under the vanilla entity header (requirement A). The building
        /// name is NOT repeated here; the vanilla header already shows it.
        /// </summary>
        /// <param name="text">Localized "tier · faction · mode" line.</param>
        /// <param name="tooltip">Localized hover text; empty clears nothing.</param>
        /// <param name="icons">Sprite cache of this panel.</param>
        /// <returns>The subtitle row.</returns>
        public static VisualElement CreateMetaRow(string text, string? tooltip, HdbfPanelIcons icons)
        {
            var row = CreateRowContainer();
            AddIcon(row, "mode", icons);
            var label = CreateLabel(text, HdbfIndoorPanelStyle.SubText);
            label.style.flexGrow = 1f;
            row.Add(label);
            SetTooltip(row, tooltip);
            return row;
        }

        /// <summary>
        /// Creates one "icon + label + value" row. Both text elements wrap and shrink, so a long
        /// localized value (ports, rotation sequence) is always fully visible instead of being cut
        /// off or overlapped (requirement H).
        /// </summary>
        /// <param name="row">Row model.</param>
        /// <param name="icons">Sprite cache of this panel.</param>
        /// <param name="valueLabel">The created value label (never null).</param>
        /// <returns>Row element with vanilla classes applied.</returns>
        public static VisualElement CreateRow(HdbfPanelRow row, HdbfPanelIcons icons, out Label valueLabel)
        {
            var root = CreateRowContainer();
            AddIcon(root, row.IconKey, icons);
            var label = CreateLabel(row.Label,
                row.IsWarning ? HdbfIndoorPanelStyle.WarningText : HdbfIndoorPanelStyle.RowLabel);
            label.style.flexGrow = 1f;
            root.Add(label);

            valueLabel = CreateLabel(row.Value, HdbfIndoorPanelStyle.RowValue);
            valueLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            valueLabel.style.flexShrink = 1f;
            valueLabel.style.minWidth = 0f;
            root.Add(valueLabel);

            SetTooltip(root, row.Tooltip);
            return root;
        }

        /// <summary>
        /// Creates the vanilla progress bar of a row, including its in-bar value/unit label and the
        /// optional C_min marker. The returned bar is ready to add below the row element.
        /// </summary>
        /// <param name="row">Row model with <see cref="HdbfPanelRow.HasBar"/> set.</param>
        /// <param name="critical">True for the red fill variant (critical coverage).</param>
        /// <returns>The vanilla ProgressBar control.</returns>
        public static CoreProgressBar CreateBar(HdbfPanelRow row, bool critical)
        {
            var bar = new CoreProgressBar();
            AddClasses(bar, HdbfIndoorPanelStyle.ProgressBar);
            // The tone classes are single vanilla classes; AddClasses adds each name on its own so
            // ".progress-bar--green .progress-bar__progress" (GameStyle.uss) really matches.
            AddClasses(bar, critical
                ? HdbfIndoorPanelStyle.ProgressBarRed
                : HdbfIndoorPanelStyle.ProgressBarGreen);
            bar.SetProgress(HdbfIndoorPanelLimits.Fill(row.Bar));

            var text = CreateLabel(row.BarText, HdbfIndoorPanelStyle.ProgressText);
            text.style.unityTextAlign = TextAnchor.MiddleCenter;
            text.style.whiteSpace = WhiteSpace.Normal;
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

        /// <summary>
        /// Creates a vanilla status row from the shipped <c>StatusListElement</c> template: 34 px
        /// icon slot + the localized sentence. The template is the same one the game uses for its
        /// own status lists, so warning rows are indistinguishable from vanilla ones. A missing
        /// template degrades to a hand-built row that keeps the same vanilla classes.
        /// </summary>
        /// <param name="row">Warning row model.</param>
        /// <param name="icons">Sprite cache of this panel.</param>
        /// <param name="loader">Vanilla UXML loader from the game container.</param>
        /// <returns>Status row element.</returns>
        public static VisualElement CreateWarningRow(HdbfPanelRow row, HdbfPanelIcons icons,
            VisualElementLoader loader)
        {
            VisualElement? element = null;
            try
            {
                element = loader.LoadVisualElement(HdbfIndoorPanelStyle.StatusRowTemplate);
            }
            catch (System.Exception)
            {
                // A missing status template must not break the entity panel.
            }

            if (element is null)
            {
                element = CreateFallbackWarningRow(row, icons);
                return element;
            }

            var icon = element.Q<VisualElement>("Icon");
            var sprite = icons.Get(row.IconKey);
            if (icon is not null && sprite is not null)
            {
                icon.style.backgroundImage = new StyleBackground(sprite);
            }

            var text = element.Q<Label>("Text");
            if (text is not null)
            {
                text.text = row.Label;
            }

            return element;
        }

        /// <summary>Hand-built status row used only when the shipped template cannot be loaded.</summary>
        private static VisualElement CreateFallbackWarningRow(HdbfPanelRow row, HdbfPanelIcons icons)
        {
            var root = new VisualElement();
            root.AddToClassList(HdbfIndoorPanelStyle.WarningRow);
            var icon = new Image { sprite = icons.Get(row.IconKey) };
            icon.AddToClassList(HdbfIndoorPanelStyle.WarningIcon);
            icon.pickingMode = PickingMode.Ignore;
            root.Add(icon);
            var text = CreateLabel(row.Label, HdbfIndoorPanelStyle.WarningText);
            text.style.flexGrow = 1f;
            root.Add(text);
            return root;
        }

        /// <summary>Creates a hidden-by-default note label for inline reasons and the action result.</summary>
        /// <param name="text">Initial localized text.</param>
        /// <returns>Vanilla sub-text label with display:none.</returns>
        public static Label CreateNoteLabel(string text)
        {
            var label = CreateLabel(text, HdbfIndoorPanelStyle.SubText);
            label.style.display = DisplayStyle.None;
            return label;
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

        /// <summary>Creates a vanilla row container with the standard row class.</summary>
        /// <returns>Empty row container.</returns>
        public static VisualElement CreateRowContainer()
        {
            var row = new VisualElement();
            row.AddToClassList(HdbfIndoorPanelStyle.Row);
            return row;
        }

        /// <summary>Adds the vanilla 24 px icon of a row; a missing sprite simply omits the Image.</summary>
        /// <param name="parent">Row element.</param>
        /// <param name="iconKey">Semantic icon key.</param>
        /// <param name="icons">Sprite cache of this panel.</param>
        public static void AddIcon(VisualElement parent, string iconKey, HdbfPanelIcons icons)
        {
            var sprite = icons.Get(iconKey);
            if (sprite is null)
            {
                return;
            }

            var image = new Image { sprite = sprite };
            image.AddToClassList(HdbfIndoorPanelStyle.RowIcon);
            image.pickingMode = PickingMode.Ignore;
            parent.Add(image);
        }
    }
}
