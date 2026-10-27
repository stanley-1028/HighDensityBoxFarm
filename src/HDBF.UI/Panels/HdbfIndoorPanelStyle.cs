// ============================================================================
// HDBF v2 | Layer: UI/Panels | Owner: V2-UI-01
// Purpose: The visual vocabulary of the v2 indoor panel: the real vanilla USS class names and
//          vanilla sprite paths the fragment composes (read-only reference from
//          %USERPROFILE%\.hdbf\ref\UI\Views, a currently installed game unpack), the semantic icon
//          keys used by the read model, and the tier -> Icons/BoxFarm* mapping. The fragment never
//          invents a look; it addToClassList()es these names onto the vanilla controls.
// Requirements: REQ-V2-UI-01/02/03, REQ-V2-READ-04, REQ-V2-UI-04
// ============================================================================
using HDBF.Core;

namespace HDBF.UI.Panels
{
    /// <summary>
    /// Vanilla class names / sprite keys used by the v2 panel. Keeping them in one place makes the
    /// "copied from the original UXML/USS" claim audit-able and keeps ad-hoc styling out of the
    /// fragment (REQ-V2-UI-01: no invented skin).
    /// </summary>
    public static class HdbfIndoorPanelStyle
    {
        // ----- cards (CoreStyle / CommonStyle / EntityPanelCommonStyle) -----------------------
        /// <summary>Vanilla sub-panel card padding class (EntityPanelCommonStyle).</summary>
        public const string Card = "entity-sub-panel";

        /// <summary>Green sub-box card frame.</summary>
        public const string CardGreen = "bg-sub-box--green";

        /// <summary>Blue sub-box card frame (information cards).</summary>
        public const string CardBlue = "bg-sub-box--blue";

        /// <summary>Purple striped sub-box card frame (header/action cards).</summary>
        public const string CardPurple = "bg-sub-box--purple-striped";

        /// <summary>Red striped sub-box card frame (warning cards).</summary>
        public const string CardRed = "bg-sub-box--red-striped";

        // ----- header (EntityPanel.uxml structure) -------------------------------------------
        /// <summary>Vanilla entity header card class.</summary>
        public const string HeaderCard = "entity-panel__header bg-sub-box--purple-striped";

        /// <summary>Vanilla 56 px entity avatar frame.</summary>
        public const string HeaderAvatar = "entity-panel__avatar";

        /// <summary>Name/subtitle column of the vanilla header.</summary>
        public const string HeaderTexts = "grow-centered entity_panel__name-and-subtitle";

        /// <summary>Vanilla header title text.</summary>
        public const string HeaderName = "entity-panel__heading-text entity-panel__name text--bold";

        /// <summary>Vanilla header subtitle text.</summary>
        public const string HeaderSubtitle = "entity-panel__sub-text entity-panel__sub-item";

        /// <summary>Mode badge: vanilla small green box + bold text.</summary>
        public const string HeaderBadge = "entity-panel__text text--bold bg-box--green";

        // ----- section titles and rows -------------------------------------------------------
        /// <summary>Vanilla heading text used for a card title.</summary>
        public const string SectionTitle = "game-text-heading text--bold";

        /// <summary>Vanilla row container (EntityPanelCommonStyle .entity-panel__row).</summary>
        public const string Row = "entity-panel__row";

        /// <summary>Vanilla 24 px row icon.</summary>
        public const string RowIcon = "inventory-row__image";

        /// <summary>Vanilla 13 px row label.</summary>
        public const string RowLabel = "entity-panel__text";

        /// <summary>Vanilla bold row value.</summary>
        public const string RowValue = "entity-panel__text text--bold";

        /// <summary>Vanilla small secondary text (notes, tooltips on screen).</summary>
        public const string SubText = "entity-panel__sub-text";

        // ----- warning rows (StatusListElement.uxml structure) --------------------------------
        /// <summary>Vanilla status row container.</summary>
        public const string WarningRow = "status-list-element";

        /// <summary>Vanilla 34 px status icon.</summary>
        public const string WarningIcon = "status-list-element__icon";

        /// <summary>Vanilla status row text.</summary>
        public const string WarningText = "entity-panel__text status-list-element__text";

        // ----- progress bars (Core/ProgressBar.uxml structure) -------------------------------
        /// <summary>Vanilla progress bar root.</summary>
        public const string ProgressBar = "progress-bar";

        /// <summary>Vanilla green fill progress bar.</summary>
        public const string ProgressBarGreen = "progress-bar progress-bar--green";

        /// <summary>Vanilla red fill progress bar (critical water).</summary>
        public const string ProgressBarRed = "progress-bar progress-bar--red";

        /// <summary>Vanilla progress bar content label.</summary>
        public const string ProgressText = "game-text-normal";

        /// <summary>Vanilla thin progress bar marker (C_min position).</summary>
        public const string ProgressMarker = "need-view__image--marker";

        // ----- buttons (CommonStyle .button-game) --------------------------------------------
        /// <summary>Vanilla game button with click sound, stretched to the card width.</summary>
        public const string Button = "button-game entity-fragment__button";

        /// <summary>Vanilla narrow game button (two actions on one row).</summary>
        public const string ButtonNarrow = "button-game entity-fragment__button entity-fragment__button--narrow";

        // ----- worker roster (WorkplaceFragment/WorkerView.uxml structure) --------------------
        /// <summary>Vanilla wrapping worker list container.</summary>
        public const string WorkerRoster = "workplace-user-fragment";

        /// <summary>Vanilla half-width worker chip.</summary>
        public const string WorkerView = "worker-view";

        /// <summary>Vanilla worker name inside a chip.</summary>
        public const string WorkerName = "worker-view__item entity-panel__sub-text worker-view__name";

        /// <summary>Vanilla warning triangle of a vacant worker position.</summary>
        public const string WorkerVacant = "worker-view__vacant-icon";

        // ----- crop strategy (Core/Dropdown.uxml structure) ----------------------------------
        /// <summary>Vanilla dropdown flex wrapper.</summary>
        public const string Dropdown = "game-dropdown";

        /// <summary>Vanilla dark, green-decorated scrollbar for the section scroller.</summary>
        public const string Scroll = "scroll--green-decorated";

        /// <summary>Shipped vanilla UXML template of a plain gold game button (root = NineSliceButton).</summary>
        public const string ButtonTemplate = "Game/EntityPanel/DebugButton";

        /// <summary>Shipped vanilla UXML template of a worker chip (avatar + name + vacant icon).</summary>
        public const string WorkerChipTemplate = "Game/EntityPanel/WorkerView";

        /// <summary>Vanilla card tone name resolved to a sub-box class.</summary>
        /// <param name="tone">"green", "blue", "purple" or "red".</param>
        /// <returns>The vanilla sub-box USS class; green for unknown tones.</returns>
        public static string CardTone(string tone)
        {
            switch (tone)
            {
                case "blue": return CardBlue;
                case "purple": return CardPurple;
                case "red": return CardRed;
                default: return CardGreen;
            }
        }

        /// <summary>
        /// Vanilla sprite resource path of a semantic row icon (resolved with Resources.Load by the
        /// game-facing renderer). Empty when the row has no icon; the renderer omits the Image then.
        /// </summary>
        /// <param name="iconKey">Semantic key from the read model, e.g. "tank".</param>
        /// <returns>Unity Resources path of a shipped vanilla sprite, or empty.</returns>
        public static string IconResource(string iconKey)
        {
            switch (iconKey)
            {
                case "workers": return "UI/Images/Game/ico-workers";
                case "worker": return "UI/Images/Game/ico-work-beaver";
                case "vacant": return "UI/Images/Game/ico-work-empty-beaver";
                case "efficiency": return "UI/Images/Game/productivity-high";
                case "slots": return "UI/Images/Game/plants-growth";
                case "capacity": return "UI/Images/Game/ico-planting";
                case "configured": return "UI/Images/Game/ico-planting";
                case "effective": return "UI/Images/Game/ico-workers";
                case "empty": return "UI/Images/Game/empty-icon";
                case "growing": return "UI/Images/Game/plants-growth";
                case "harvest": return "UI/Images/Game/ico-harvesting";
                case "backlog": return "UI/Images/Game/ico-clock";
                case "tank": return "UI/Images/Game/unfiltered-fluid";
                case "coverage": return "UI/Images/Game/productivity-medium";
                case "supply": return "UI/Images/Game/ico-water-source-regulator-open";
                case "maintenance": return "UI/Images/Game/ico-durability";
                case "due": return "UI/Images/Game/ico-clock";
                case "material": return "UI/Images/Game/storage-goods";
                case "power": return "UI/Images/Game/ico-power";
                case "crop": return "UI/Images/Game/ico-planting";
                case "rotation": return "UI/Images/Game/replant-dead-trees";
                case "output": return "UI/Images/Game/output";
                case "port": return "UI/Images/Game/workplace-goods";
                case "warning": return "UI/Images/Core/warning_triangle";
                case "no-water": return "UI/Images/Game/ico-contamination";
                case "no-workers": return "UI/Images/Game/ico-no-work-beaver";
                case "entrance": return "UI/Images/Game/ico-gate-closed";
                case "output-full": return "UI/Images/Game/storage-goods";
                case "invalid-crop": return "UI/Images/Game/question-mark";
                case "mode": return "UI/Images/Game/ico-automated";
                default: return string.Empty;
            }
        }

        /// <summary>
        /// Tier icon of the blueprint (<c>Icons/BoxFarmBasic|Intermediate|Advanced|Top</c>). The
        /// header avatar itself comes from the vanilla badge service (the same sprite the build
        /// menu uses); this mapping documents/pins the asset name per tier.
        /// </summary>
        /// <param name="tier">Building tier.</param>
        /// <returns>Blueprint icon asset name without extension.</returns>
        public static string TierIconAsset(HdbfTier tier)
        {
            switch (tier)
            {
                case HdbfTier.Intermediate: return "Icons/BoxFarmIntermediate";
                case HdbfTier.Advanced: return "Icons/BoxFarmAdvanced";
                case HdbfTier.Top: return "Icons/BoxFarmTop";
                default: return "Icons/BoxFarmBasic";
            }
        }
    }
}
