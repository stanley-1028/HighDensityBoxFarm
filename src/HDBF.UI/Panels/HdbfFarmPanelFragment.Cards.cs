// ============================================================================
// HDBF v2 | Layer: UI/Panels | Owner: V2-UI-01
// Purpose: Card-stack reconciliation of the indoor entity panel. The 4 Hz refresh must update
//          values without tearing down the controls the player can interact with: the vanilla crop
//          Dropdown hides its open popup when it is detached from the panel, and a click that
//          straddles a re-parent loses its pointer capture. Each card therefore carries a content
//          signature (HdbfIndoorPanelSignature); only a card whose signature changed is rebuilt and
//          replaced at its index, every other card - including the one that owns the dropdown - is
//          left attached. The legacy "clear the whole stack and rebuild all cards" path is kept as
//          the fallback for the first render or when the section layout itself changed.
// Requirements: REQ-V2-UI-01/02/03/04, REQ-V2-UI-05, REQ-V2-READ-04, REQ-V2-PERF-02
// ============================================================================
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace HDBF.UI.Panels
{
    public sealed partial class HdbfFarmPanelFragment
    {
        /// <summary>
        /// One mounted card: the element, the signature of what it currently shows and the section
        /// it belongs to. Keeping the three values in one struct makes the "all three lists must
        /// stay in sync" bug impossible.
        /// </summary>
        private readonly struct CardState
        {
            public CardState(VisualElement view, long signature, HdbfPanelSectionKind kind)
            {
                View = view;
                Signature = signature;
                Kind = kind;
            }

            /// <summary>The mounted card element.</summary>
            public VisualElement View { get; }

            /// <summary>Content signature of the mounted card (see HdbfIndoorPanelSignature).</summary>
            public long Signature { get; }

            /// <summary>Section kind the card was built for.</summary>
            public HdbfPanelSectionKind Kind { get; }
        }

        /// <summary>Cards currently in the card scroller, one per section, in order.</summary>
        private readonly List<CardState> _cardStates = new(8);

        /// <summary>
        /// Applies the ordered cards of the read model onto the scroller. A card is rebuilt only
        /// when its rendered content changed, so the vanilla controls living inside a card (crop
        /// dropdown, mode radio group) stay attached across the 4 Hz value refresh.
        /// </summary>
        /// <param name="data">Localized read model of the current snapshot.</param>
        private void RenderCards(HdbfIndoorPanelData data)
        {
            if (_cards is null)
            {
                return;
            }

            var cards = _cards;
            var sections = data.Sections;
            if (sections is null || sections.Count == 0)
            {
                ClearCardCache();
                if (cards.childCount > 0)
                {
                    cards.Clear();
                }

                return;
            }

            if (CanUpdateCardsInPlace(cards, sections))
            {
                UpdateChangedCards(cards, sections, data);
            }
            else
            {
                RebuildCards(cards, sections, data);
            }

            // The dropdown lives inside the crop card; only re-sync its items/value when the card
            // (and therefore the dropdown) is actually mounted. panel != null is the real "attached
            // to a live panel" test; parent != null would also accept a card that is already
            // detached from the panel.
            if (_cropHost is not null && _cropHost.panel is not null)
            {
                RenderCrop(data);
            }
        }

        /// <summary>
        /// True when the current card elements still map 1:1 onto the new sections, so individual
        /// cards may be replaced in place. A mismatch (first render, different section layout,
        /// unexpected child count) falls back to the full rebuild.
        /// </summary>
        private bool CanUpdateCardsInPlace(VisualElement cards, IReadOnlyList<HdbfPanelSection> sections)
        {
            if (_cardStates.Count != sections.Count || cards.childCount != sections.Count)
            {
                return false;
            }

            for (var index = 0; index < sections.Count; index++)
            {
                var state = _cardStates[index];
                if (state.Kind != sections[index].Kind)
                {
                    return false;
                }

                if (!ReferenceEquals(cards[index], state.View))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Replaces every card whose content signature changed; leaves all others attached.</summary>
        private void UpdateChangedCards(VisualElement cards, IReadOnlyList<HdbfPanelSection> sections,
            HdbfIndoorPanelData data)
        {
            for (var index = 0; index < sections.Count; index++)
            {
                var section = sections[index];
                var signature = HdbfIndoorPanelSignature.ForSection(section, data);
                if (signature == _cardStates[index].Signature)
                {
                    continue;
                }

                // Build first: BuildSectionCard re-parents the persistent hosts (_cropHost,
                // _modeHost/_modeTransition) into the new card, so removing the old card afterwards
                // can never take a host with it.
                var card = BuildSectionCard(section, data);
                cards.RemoveAt(index);
                cards.Insert(index, card);
                _cardStates[index] = new CardState(card, signature, section.Kind);
            }
        }

        /// <summary>
        /// Builds the whole stack and records the cache. This is the one path that really detaches
        /// every card; it runs on the first render (and only again if the section layout changed).
        /// </summary>
        private void RebuildCards(VisualElement cards, IReadOnlyList<HdbfPanelSection> sections,
            HdbfIndoorPanelData data)
        {
            cards.Clear();
            ClearCardCache();
            for (var index = 0; index < sections.Count; index++)
            {
                var section = sections[index];
                var card = BuildSectionCard(section, data);
                cards.Add(card);
                _cardStates.Add(new CardState(card, HdbfIndoorPanelSignature.ForSection(section, data),
                    section.Kind));
            }
        }

        private void ClearCardCache()
        {
            _cardStates.Clear();
        }

        /// <summary>
        /// Builds one card exactly as the panel always rendered it (title row, warning rows, value
        /// rows, progress bars, the worker roster/mode group and the crop dropdown). Side effect:
        /// the persistent worker-mode host and crop-dropdown host are re-parented into the new
        /// card, which is why this is only called for cards that really changed.
        /// </summary>
        /// <param name="section">Card model.</param>
        /// <param name="data">Whole read model (worker roster/meta extras).</param>
        /// <returns>A fresh card element ready to be inserted into the scroller.</returns>
        private VisualElement BuildSectionCard(HdbfPanelSection section, HdbfIndoorPanelData data)
        {
            var card = HdbfPanelControls.CreateCard(section.Tone);
            if (section.Kind == HdbfPanelSectionKind.Workers)
            {
                // Requirement A: the compact tier/faction line sits directly under the vanilla
                // header as the first card row; the building name is never repeated.
                card.Add(HdbfPanelControls.CreateMetaRow(data.MetaLine, data.MetaTooltip, _icons));
            }

            card.Add(HdbfPanelControls.CreateTitleRow(section.Title, section.IconKey, _icons));
            var critical = section.Tone == "red";
            VisualElement? cropPolicyRow = null;
            for (var rowIndex = 0; rowIndex < section.Rows.Count; rowIndex++)
            {
                var row = section.Rows[rowIndex];
                if (row.IsWarning)
                {
                    var warningRow = HdbfPanelControls.CreateWarningRow(row, _icons);
                    RegisterTooltip(warningRow, row.Tooltip);
                    card.Add(warningRow);
                    continue;
                }

                var rowElement = HdbfPanelControls.CreateRow(row, _icons, out var valueLabel);
                RegisterTooltip(rowElement, row.Tooltip);
                card.Add(rowElement);
                if (row.HasBar)
                {
                    card.Add(HdbfPanelControls.CreateBar(row, critical));
                }

                if (section.Kind == HdbfPanelSectionKind.Crops && cropPolicyRow is null)
                {
                    cropPolicyRow = rowElement;
                    if (_cropDropdown is not null)
                    {
                        // The vanilla dropdown shows the current policy itself; hiding the static
                        // value avoids the duplicated "auto rotation [auto rotation]".
                        valueLabel.style.display = DisplayStyle.None;
                    }
                }
            }

            if (section.Kind == HdbfPanelSectionKind.Workers)
            {
                RenderWorkerRoster(card, data);
            }

            if (section.Kind == HdbfPanelSectionKind.Crops && _cropHost is not null)
            {
                // Requirement: one row = icon/label + current strategy text + the vanilla dropdown.
                // The fallback keeps the dropdown mounted even if a future crop card lost its
                // non-warning policy row (today the factory always provides one).
                (cropPolicyRow ?? card).Add(_cropHost);
            }

            return card;
        }
    }
}
