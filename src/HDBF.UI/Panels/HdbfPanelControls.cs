// ============================================================================
// HDBF v2 | Layer: UI/Panels | Owner: V2-UI-01
// Purpose: Builders of the individual vanilla UIElements the v2 panel is composed from: cards
//          (NineSliceVisualElement + sub-box classes), section titles, the compact tier/faction
//          subtitle row, icon+label+value rows, progress bars (core ProgressBar with its value
//          label and C_min marker) and warning rows (status-list structure + a colour accent
//          strip). Buttons and worker chips live in HdbfPanelControls.Widgets.cs.
//          Every text element is painted with ONE light colour (HdbfPanelControls.ApplyLightText),
//          never with per-row hard-coded colours, and every class list is applied one class at a
//          time (VisualElement.AddToClassList takes a single class name).
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
        /// <summary>
        /// Panel text colour: warm white #F2EBDD. Vanilla ships no ".text--white" class, so the
        /// fragment sets this one colour explicitly on every text it creates; this keeps the
        /// requirement-D rule in a single place (no per-row colours, no dark fallback).
        /// </summary>
        public static readonly Color LightText = FromRgba(HdbfIndoorPanelStyle.LightTextRgba);

        /// <summary>
        /// Warning accent strip colour #C4562B: the second encoding that survives "all text is
        /// light" (the icon and the "▲/!" marker are the first and third, requirement I).
        /// </summary>
        public static readonly Color WarningAccent = FromRgba(HdbfIndoorPanelStyle.WarningAccentRgba);

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

        /// <summary>
        /// Paints one element (and, through USS inheritance, its children) with the light panel
        /// text colour. Called on every text element this factory creates so a missing/renamed
        /// vanilla colour class can never leave dark text on a dark card (requirement D).
        /// </summary>
        /// <param name="element">Element to paint.</param>
        public static void ApplyLightText(VisualElement element)
        {
            if (element is not null)
            {
                element.style.color = new StyleColor(LightText);
            }
        }

        /// <summary>Creates a label with the vanilla class list, the light colour and wrapping.</summary>
        /// <param name="text">Localized text.</param>
        /// <param name="classNames">Vanilla class list or empty.</param>
        /// <param name="fontSize">Font size in points; 0 keeps the vanilla class size.</param>
        /// <returns>Ready-to-add label.</returns>
        public static Label CreateLabel(string text, string? classNames, float fontSize)
        {
            var label = new Label(text);
            AddClasses(label, classNames);
            ApplyLightText(label);
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.flexShrink = 1f;
            if (fontSize > 0f)
            {
                label.style.fontSize = fontSize;
            }

            return label;
        }

        /// <summary>Creates a vanilla sub-panel card with the requested tone (no gap between cards).</summary>
        /// <param name="tone">"green", "blue", "purple" or "red".</param>
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
            var label = CreateLabel(title, HdbfIndoorPanelStyle.SectionTitle, 15f);
            label.style.flexGrow = 1f;
            row.Add(label);
            row.style.marginBottom = 4f;
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
            var label = CreateLabel(text, HdbfIndoorPanelStyle.RowValue, 12f);
            label.style.flexGrow = 1f;
            row.Add(label);
            SetTooltip(row, tooltip);
            row.style.marginBottom = 2f;
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
            var label = CreateLabel(row.IsWarning ? row.Marker + " " + row.Label : row.Label,
                row.IsWarning ? HdbfIndoorPanelStyle.WarningText : HdbfIndoorPanelStyle.RowLabel, 0f);
            label.style.flexGrow = 1f;
            root.Add(label);

            valueLabel = CreateLabel(row.Value, HdbfIndoorPanelStyle.RowValue, 0f);
            valueLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            valueLabel.style.flexShrink = 1f;
            valueLabel.style.minWidth = 0f;
            valueLabel.style.marginLeft = 6f;
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
            bar.AddToClassList(HdbfIndoorPanelStyle.ProgressBar);
            bar.AddToClassList(critical
                ? HdbfIndoorPanelStyle.ProgressBarRed
                : HdbfIndoorPanelStyle.ProgressBarGreen);
            bar.style.marginTop = 2f;
            bar.style.marginBottom = 2f;
            bar.style.minHeight = 24f;
            bar.SetProgress(HdbfIndoorPanelLimits.Fill(row.Bar));

            var text = CreateLabel(row.BarText, HdbfIndoorPanelStyle.ProgressText, 12f);
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
        /// Creates a vanilla status row: a colour accent strip, the warning icon and the localized
        /// sentence. Colour is never the only carrier - the row keeps its icon and its "▲/!" text
        /// marker, and the strip is only the third encoding (requirement I).
        /// </summary>
        /// <param name="row">Warning row model.</param>
        /// <param name="icons">Sprite cache of this panel.</param>
        /// <returns>Status row element.</returns>
        public static VisualElement CreateWarningRow(HdbfPanelRow row, HdbfPanelIcons icons)
        {
            var root = new VisualElement();
            root.AddToClassList(HdbfIndoorPanelStyle.WarningRow);

            var strip = new VisualElement();
            strip.style.width = 3f;
            strip.style.marginRight = 6f;
            strip.style.alignSelf = Align.Stretch;
            strip.style.backgroundColor = new StyleColor(WarningAccent);
            strip.pickingMode = PickingMode.Ignore;
            root.Add(strip);

            var icon = new Image { sprite = icons.Get(row.IconKey) };
            icon.AddToClassList(HdbfIndoorPanelStyle.WarningIcon);
            icon.pickingMode = PickingMode.Ignore;
            root.Add(icon);

            var text = CreateLabel(row.Marker + " " + row.Label, HdbfIndoorPanelStyle.WarningText, 0f);
            text.style.flexGrow = 1f;
            root.Add(text);
            SetTooltip(root, row.Tooltip);
            return root;
        }

        /// <summary>Creates a hidden-by-default note label for inline reasons and the action result.</summary>
        /// <param name="text">Initial localized text.</param>
        /// <returns>Light, wrapping label with display:none.</returns>
        public static Label CreateNoteLabel(string text)
        {
            var label = CreateLabel(text, HdbfIndoorPanelStyle.SubText, 12f);
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

        private static Color FromRgba(uint rgba)
        {
            return new Color32(
                (byte)(rgba >> 24),
                (byte)(rgba >> 16),
                (byte)(rgba >> 8),
                (byte)rgba);
        }
    }
}
