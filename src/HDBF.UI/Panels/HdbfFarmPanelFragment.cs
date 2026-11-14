// ============================================================================
// HDBF v2 | Layer: UI/Panels | Owner: V2-UI-01
// Purpose: Entity panel fragment of the v2 indoor box farm: lifecycle, the 4 Hz refresh gate,
//          the call into the pure value fingerprint (HdbfIndoorPanelFingerprint) and the
//          read-model refresh. Rendering lives in .Render.cs, element construction in .Build.cs,
//          the per-card content diff / card-stack reconciliation in .Cards.cs, the vanilla crop
//          dropdown in .Crop.cs and command callbacks (mode/crop/refill/maintenance/
//          auto-maintenance) in .Commands.cs.
// Requirements: REQ-V2-UI-01/02/03/04, REQ-V2-UI-05, REQ-V2-READ-04, REQ-V2-SAFE-01
// Size: above the 300-line soft gate with a written reason (REQ-V2-MAINT-02): the lifecycle, the
//       fixed 4 Hz gate and the refresh path stay in one file so they are reviewed as one unit;
//       rendering, build, crop dropdown, commands and the fingerprint already live in partials.
// ============================================================================
using System;
using HDBF.Core;
using HDBF.Core.Abstractions;
using HDBF.Core.Indoor;
using HDBF.Core.Logging;
using HDBF.UI.Localization;
using Timberborn.BaseComponentSystem;
using Timberborn.CoreUI;
using Timberborn.DropdownSystem;
using Timberborn.EntityPanelSystem;
using Timberborn.EntitySystem;
using Timberborn.Localization;
using Timberborn.SingletonSystem;
using Timberborn.TooltipSystem;
using UnityEngine;
using UnityEngine.UIElements;

namespace HDBF.UI.Panels
{
    /// <summary>
    /// Entity panel fragment for the four BoxFarm tiers (chapter 19.11). The fragment renders the
    /// game-free <see cref="HdbfIndoorPanelData"/> read model (cards + rows) onto vanilla
    /// UIElements controls: cards are NineSliceVisualElements with the shipped sub-box classes,
    /// rows are icon + label + value, bars are the core ProgressBar control and buttons/dropdowns
    /// are the vanilla ones. Read-only rows render strictly from the Core
    /// <see cref="IndoorFarmSnapshot"/>; actions go through the frozen Core command contract or the
    /// v2 indoor command bridge; it hides itself for every non-farm entity and never throws into the
    /// entity panel (REQ-V2-SAFE-01).
    /// </summary>
    public sealed partial class HdbfFarmPanelFragment : IEntityPanelFragment
    {
        /// <summary>Quarter-second refresh cap (4 Hz), matching the chapter 19.13 UI budget.</summary>
        private const float RefreshIntervalSeconds = 0.25f;
        /// <summary>Lower clamp of the card scroller height in UI points (720p window safety).</summary>
        private const float MinScrollHeight = 160f;
        /// <summary>Upper clamp of the card scroller height in UI points.</summary>
        private const float MaxScrollHeight = 620f;
        /// <summary>Space kept free under the action card (bottom hotbar of the game UI).</summary>
        private const float BottomReserve = 26f;
        /// <summary>
        /// Seconds the Showed-driven "popup open" flag is trusted while the drawer reports the popup
        /// closed (the drawer reads the previous layout pass). A whole second is far longer than a
        /// layout pass and still guarantees a stale flag can never freeze the panel (a fixed frame
        /// budget would mean seconds at low frame rates).
        /// </summary>
        private const float CropPopupStaleSeconds = 1f;

        private const string LogKeyCommandRejected = "ui.command-rejected";
        private const string LogKeyCommandFailed = "ui.command-exception";
        private const string LogKeyRenderFailed = "ui.render";
        /// <summary>The vanilla crop dropdown asset could not be created (panel degrades to text).</summary>
        private const string LogKeyCropDropdown = "ui.crop-dropdown";

        private readonly IBoxFarmCommand _command;
        private readonly HdbfIndoorCommandBridge _indoor;
        private readonly ILoc _loc;
        private readonly IHdbfLog _log;
        private readonly RadioToggleFactory _radioToggleFactory;
        private readonly DropdownItemsSetter _dropdownItemsSetter;
        private readonly DropdownListDrawer _dropdownListDrawer;
        private readonly EventBus _eventBus;
        private readonly VisualElementLoader _visualElementLoader;
        private readonly ITooltipRegistrar _tooltips;
        private readonly HdbfPanelIcons _icons = new();

        private BaseComponent? _entity;
        private Guid _entityId;
        private bool _dropdownHiddenListenerRegistered;
        /// <summary>When the drawer first reported "closed" while the exact flag says "open"; -1 = not yet.</summary>
        private float _cropPopupStaleSince = -1f;
        private long _lastFingerprint;
        private bool _hasSnapshot;
        private float _nextRefreshTime;
        private float _lastScrollHeight;
        private bool _resetScrollOnNextRender;
        private string _actionMessage = string.Empty;
        private HdbfIndoorPanelData? _lastData;
        /// <summary>
        /// Creates the fragment. The v2 read/command members are resolved once through
        /// <see cref="HdbfIndoorCommandBridge"/> because UI references HDBF.Core only; the four
        /// injected UI services are vanilla singletons (radio toggle factory and the dropdown
        /// system) resolved from the game container.
        /// </summary>
        /// <param name="query">Core read contract instance (gameplay-owned singleton).</param>
        /// <param name="command">Core write contract for mode/maintenance.</param>
        /// <param name="loc">Game localization service.</param>
        /// <param name="log">Throttled mod log.</param>
        /// <param name="radioToggleFactory">Vanilla radio-button group factory (mode switch).</param>
        /// <param name="dropdownItemsSetter">Vanilla dropdown item setter (crop strategy).</param>
        /// <param name="dropdownListDrawer">Vanilla dropdown popup drawer.</param>
        /// <param name="eventBus">Vanilla event bus (the drawer posts DropdownHiddenEvent when a popup closes).</param>
        /// <param name="visualElementLoader">Vanilla UXML loader (status-row and worker-chip templates).</param>
        /// <param name="visualElementInitializer">Vanilla initializer pass (click sounds, nine-slice scrollbars, localization).</param>
        /// <param name="tooltips">Vanilla tooltip registrar (row/button hover texts).</param>
        public HdbfFarmPanelFragment(IBoxFarmQuery query, IBoxFarmCommand command, ILoc loc, IHdbfLog log,
            RadioToggleFactory radioToggleFactory,
            DropdownItemsSetter dropdownItemsSetter, DropdownListDrawer dropdownListDrawer,
            EventBus eventBus,
            VisualElementLoader visualElementLoader, VisualElementInitializer visualElementInitializer,
            ITooltipRegistrar tooltips)
        {
            _command = command ?? throw new ArgumentNullException(nameof(command));
            _indoor = new HdbfIndoorCommandBridge(query, command);
            _loc = loc ?? throw new ArgumentNullException(nameof(loc));
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _radioToggleFactory = radioToggleFactory ?? throw new ArgumentNullException(nameof(radioToggleFactory));
            _dropdownItemsSetter = dropdownItemsSetter ?? throw new ArgumentNullException(nameof(dropdownItemsSetter));
            _dropdownListDrawer = dropdownListDrawer ?? throw new ArgumentNullException(nameof(dropdownListDrawer));
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            _visualElementLoader = visualElementLoader ?? throw new ArgumentNullException(nameof(visualElementLoader));
            // Not null-checked: an absent initializer only costs the optional click sounds and the
            // nine-slice scrollbar draggers, and the panel must never fail to build because of it.
            _visualElementInitializer = visualElementInitializer;
            _tooltips = tooltips ?? throw new ArgumentNullException(nameof(tooltips));
        }
        /// <summary>True once the fragment rendered a farm snapshot (diagnostics/tests).</summary>
        public bool HasData => _hasSnapshot;
        /// <inheritdoc />
        public VisualElement InitializeFragment()
        {
            RegisterDropdownHiddenListener();
            try
            {
                return BuildRoot();
            }
            catch (Exception exception)
            {
                // A missing game UI asset must not break the entity panel for every building.
                _log.Error(HdbfLogCategory.Error, LogKeyRenderFailed,
                    "indoor box farm panel failed to build; the fragment stays hidden", exception);
                var fallback = new VisualElement();
                fallback.style.display = DisplayStyle.None;
                _root = fallback;
                return fallback;
            }
        }

        /// <summary>
        /// Subscribes this fragment to the drawer's <c>DropdownHiddenEvent</c>: that event is
        /// posted from <c>HideDropdown</c>, so it is the exact, layout-independent "the open popup
        /// is gone" signal the refresh guard needs. An already-registered or unavailable bus only
        /// costs the exact signal (the guard falls back to <c>DropdownVisible</c>), never the panel.
        /// </summary>
        private void RegisterDropdownHiddenListener()
        {
            if (_dropdownHiddenListenerRegistered)
            {
                return;
            }

            try
            {
                _eventBus.Register(this);
                _dropdownHiddenListenerRegistered = true;
            }
            catch (Exception)
            {
                // Swallowed on purpose: panel construction must not depend on the event bus.
            }
        }
        /// <inheritdoc />
        public void ShowFragment(BaseComponent entity)
        {
            // Retry the subscription here as well: InitializeFragment may run before the event bus
            // itself is loadable in some load orders, and the exact popup-close signal matters.
            RegisterDropdownHiddenListener();
            _entity = entity;
            _entityId = ResolveEntityId(entity);
            _hasSnapshot = false;
            _lastFingerprint = 0L;
            _nextRefreshTime = 0f;
            _actionMessage = string.Empty;
            _lastData = null;
            _cropSignature = string.Empty;
            CloseCropPopup();
            _resetScrollOnNextRender = true;
            _lastScrollHeight = 0f;
            ResetFolds();
            Refresh(force: true);
        }
        /// <inheritdoc />
        public void UpdateFragment()
        {
            if (_root is null)
            {
                return;
            }

            // The vanilla crop dropdown closes its open popup when the card that owns it is
            // rebuilt (DetachFromPanelEvent -> DropdownListDrawer.HideDropdown). While a dropdown
            // popup is open, skip the periodic refresh entirely: the popup stays open, and the
            // values catch up on the first refresh after it closes. This is the hard guarantee
            // behind the per-card content diff - even a rebuild triggered by an unexpected content
            // change cannot close a popup the player is using.
            if (IsCropDropdownOpen())
            {
                return;
            }

            var now = Time.unscaledTime;
            if (now < _nextRefreshTime)
            {
                return;
            }

            _nextRefreshTime = now + RefreshIntervalSeconds;
            // The scroller height follows the window size at the 4 Hz gate (never per frame); the
            // value is only written when it changed (requirement K: no idle allocation/churn).
            UpdateScrollHeight();
            Refresh(force: false);
        }

        /// <summary>
        /// True while the vanilla dropdown popup is open. The exact signal is the explicit open flag
        /// driven by the dropdown's synchronous <c>Showed</c> event and cleared by the drawer's
        /// <c>DropdownHiddenEvent</c>; it is consulted first because it never lags a layout pass.
        /// <c>DropdownListDrawer.DropdownVisible</c> (resolvedStyle = the previous layout pass) is
        /// only the fallback: it can still report "closed" on the frame the popup opens, and the
        /// periodic refresh that ran in that window is exactly what closed the popup (any SetItems
        /// clears the drawer, and a crop-card rebuild detaches the selection button whose vanilla
        /// DetachFromPanel callback hides the popup). An unloaded or broken drawer counts as
        /// "closed" so this guard can never make the panel throw or freeze.
        /// </summary>
        private bool IsCropDropdownOpen()
        {
            if (_cropPopupOpen)
            {
                bool visible;
                try
                {
                    visible = _dropdownListDrawer.DropdownVisible;
                }
                catch (Exception)
                {
                    _cropPopupOpen = false;
                    _cropPopupStaleSince = -1f;
                    return false;
                }

                if (visible)
                {
                    _cropPopupStaleSince = -1f;
                    return true;
                }

                // The flag is set synchronously by Showed, but DropdownVisible reads the last layout
                // pass and can still report "closed" on the frame the popup opens, so the flag wins
                // for a bounded grace period. A stale flag must never freeze the panel, and
                // HideDropdown can early-return without posting its event when it already believes
                // the popup is closed, so the grace period is time-based: a frame budget would mean
                // several seconds on a low frame rate.
                if (_cropPopupStaleSince < 0f)
                {
                    _cropPopupStaleSince = Time.unscaledTime;
                    return true;
                }

                if (Time.unscaledTime - _cropPopupStaleSince < CropPopupStaleSeconds)
                {
                    return true;
                }

                _cropPopupOpen = false;
                _cropPopupStaleSince = -1f;
                return false;
            }

            // Fallback for a popup the Showed event did not report (the drawer is a singleton, so a
            // popup owned by another panel is still "a popup is open" for us).
            try
            {
                return _dropdownListDrawer.DropdownVisible;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <inheritdoc />
        public void ClearFragment()
        {
            _entity = null;
            _entityId = Guid.Empty;
            _hasSnapshot = false;
            _lastFingerprint = 0L;
            _actionMessage = string.Empty;
            _lastData = null;
            _cropSignature = string.Empty;
            CloseCropPopup();
            _resetScrollOnNextRender = true;
            _lastScrollHeight = 0f;
            ResetFolds();
            if (_root is not null)
            {
                _root.style.display = DisplayStyle.None;
            }
        }

        /// <summary>
        /// Closes the crop popup and forgets its open state. Hiding the panel root is not enough:
        /// the popup lives in the drawer's own UIDocument, so switching entity (or hiding the
        /// fragment) would otherwise leave an orphan popup on screen and a flag that disagrees
        /// with the drawer. Called on every entity switch; a closed drawer is a no-op.
        /// </summary>
        private void CloseCropPopup()
        {
            try
            {
                _cropDropdown?.Hide();
            }
            catch (Exception)
            {
                // A broken vanilla control must never break the entity panel.
            }

            _cropPopupOpen = false;
            _cropPopupStaleSince = -1f;
        }

        // ------------------------------------------------------------------ refresh / read model

        private void Refresh(bool force)
        {
            if (_root is null)
            {
                return;
            }

            try
            {
                if (_entity is null || _entityId == Guid.Empty)
                {
                    Hide();
                    return;
                }

                // REQ-V2-UI-01/-04: the fragment reads only the Core DTO exposed by the registered
                // gameplay query service; it never reaches into a controller or a game object.
                var snapshot = _indoor.GetIndoorSnapshot(_entityId);
                if (snapshot is null)
                {
                    Hide();
                    return;
                }

                var fingerprint = HdbfIndoorPanelFingerprint.Compute(snapshot, _loc.T);
                if (!force && _hasSnapshot && fingerprint == _lastFingerprint)
                {
                    return;
                }

                _lastFingerprint = fingerprint;
                _hasSnapshot = true;
                Render(HdbfIndoorPanelFactory.Create(snapshot, _loc.T));
            }
            catch (Exception exception)
            {
                Hide();
                _log.Error(HdbfLogCategory.Error, LogKeyRenderFailed,
                    "indoor box farm panel failed to render; hiding the fragment", exception,
                    _entityId.ToString());
            }
        }

        private void Hide()
        {
            _hasSnapshot = false;
            if (_root is not null)
            {
                _root.style.display = DisplayStyle.None;
            }
        }
    }
}
