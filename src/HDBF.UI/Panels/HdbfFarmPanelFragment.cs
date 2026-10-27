// ============================================================================
// HDBF v2 | Layer: UI/Panels | Owner: V2-UI-01
// Purpose: Entity panel fragment of the v2 indoor box farm: lifecycle, the 4 Hz refresh gate,
//          the value fingerprint (dirty check) and the read-model refresh. Rendering lives in
//          .Render.cs, element construction in .Build.cs, the vanilla crop dropdown in .Crop.cs
//          and command callbacks in .Commands.cs.
// Requirements: REQ-V2-UI-01/02/03/04, REQ-V2-READ-04, REQ-V2-SAFE-01
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

        private const string LogKeyCommandRejected = "ui.command-rejected";
        private const string LogKeyCommandFailed = "ui.command-exception";
        private const string LogKeyRenderFailed = "ui.render";

        private readonly IBoxFarmCommand _command;
        private readonly HdbfIndoorCommandBridge _indoor;
        private readonly ILoc _loc;
        private readonly IHdbfLog _log;
        private readonly RadioToggleFactory _radioToggleFactory;
        private readonly DropdownItemsSetter _dropdownItemsSetter;
        private readonly DropdownListDrawer _dropdownListDrawer;
        private readonly VisualElementLoader _visualElementLoader;
        private readonly ITooltipRegistrar _tooltips;
        private readonly HdbfPanelIcons _icons = new();

        private BaseComponent? _entity;
        private Guid _entityId;
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
        /// <param name="visualElementLoader">Vanilla UXML loader (button and worker-chip templates).</param>
        /// <param name="tooltips">Vanilla tooltip registrar (row/button hover texts).</param>
        public HdbfFarmPanelFragment(IBoxFarmQuery query, IBoxFarmCommand command, ILoc loc, IHdbfLog log,
            RadioToggleFactory radioToggleFactory,
            DropdownItemsSetter dropdownItemsSetter, DropdownListDrawer dropdownListDrawer,
            VisualElementLoader visualElementLoader, ITooltipRegistrar tooltips)
        {
            _command = command ?? throw new ArgumentNullException(nameof(command));
            _indoor = new HdbfIndoorCommandBridge(query, command);
            _loc = loc ?? throw new ArgumentNullException(nameof(loc));
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _radioToggleFactory = radioToggleFactory ?? throw new ArgumentNullException(nameof(radioToggleFactory));
            _dropdownItemsSetter = dropdownItemsSetter ?? throw new ArgumentNullException(nameof(dropdownItemsSetter));
            _dropdownListDrawer = dropdownListDrawer ?? throw new ArgumentNullException(nameof(dropdownListDrawer));
            _visualElementLoader = visualElementLoader ?? throw new ArgumentNullException(nameof(visualElementLoader));
            _tooltips = tooltips ?? throw new ArgumentNullException(nameof(tooltips));
        }
        /// <summary>True once the fragment rendered a farm snapshot (diagnostics/tests).</summary>
        public bool HasData => _hasSnapshot;
        /// <inheritdoc />
        public VisualElement InitializeFragment()
        {
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
        /// <inheritdoc />
        public void ShowFragment(BaseComponent entity)
        {
            _entity = entity;
            _entityId = ResolveEntityId(entity);
            _hasSnapshot = false;
            _lastFingerprint = 0L;
            _nextRefreshTime = 0f;
            _actionMessage = string.Empty;
            _lastData = null;
            _cropSignature = string.Empty;
            _resetScrollOnNextRender = true;
            _lastScrollHeight = 0f;
            Refresh(force: true);
        }
        /// <inheritdoc />
        public void UpdateFragment()
        {
            if (_root is null)
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
            _resetScrollOnNextRender = true;
            _lastScrollHeight = 0f;
            if (_root is not null)
            {
                _root.style.display = DisplayStyle.None;
            }
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

                var fingerprint = ComputeFingerprint(snapshot);
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
        /// <summary>
        /// Cheap fingerprint over every displayed snapshot value. It is the dirty check that keeps
        /// the panel from re-formatting identical data: a collision only costs one skipped refresh,
        /// an unchanged fingerprint never touches a label (REQ-V2-UI-01).
        /// </summary>
        private long ComputeFingerprint(IndoorFarmSnapshot snapshot)
        {
            unchecked
            {
                // A localization switch changes every rendered string without changing the snapshot;
                // probing one translated section title makes the dirty check catch it.
                var hash = 17L + _loc.T(HdbfUiLocKeys.PanelSectionSlots).GetHashCode();
                hash = hash * 31 + (int)snapshot.Tier;
                hash = hash * 31 + (int)snapshot.Faction;
                hash = hash * 31 + snapshot.Workers;
                hash = hash * 31 + snapshot.MaxWorkers;
                hash = hash * 31 + (int)snapshot.Mode;
                hash = hash * 31 + (snapshot.ModeTransitioning ? 1 : 0);
                hash = hash * 31 + snapshot.Capacity;
                hash = hash * 31 + snapshot.ConfiguredSlots;
                hash = hash * 31 + snapshot.EffectiveSlots;
                hash = hash * 31 + snapshot.EmptySlots;
                hash = hash * 31 + snapshot.GrowingSlots;
                hash = hash * 31 + snapshot.MatureSlots;
                hash = hash * 31 + snapshot.BacklogActions;
                hash = hash * 31 + Quantize(snapshot.Tank);
                hash = hash * 31 + Quantize(snapshot.TankCapacity);
                hash = hash * 31 + Quantize(snapshot.TankDaysRemaining);
                hash = hash * 31 + Quantize(snapshot.Coverage);
                hash = hash * 31 + Quantize(snapshot.MinCoverage);
                hash = hash * 31 + (int)snapshot.WaterSupply;
                hash = hash * 31 + Quantize(snapshot.MaintenanceDueDay);
                hash = hash * 31 + snapshot.MaintenanceCyclesMissed;
                hash = hash * 31 + (snapshot.MaintenanceMaterialMissing ? 1 : 0);
                hash = hash * 31 + (int)snapshot.Power;
                hash = hash * 31 + (snapshot.PowerConnected ? 1 : 0);
                hash = hash * 31 + (int)snapshot.CropPolicy;
                hash = hash * 31 + (snapshot.FixedCropId is null ? 0 : snapshot.FixedCropId.GetHashCode());
                hash = hash * 31 + (snapshot.RotationSequence is null ? 0 : snapshot.RotationSequence.GetHashCode());
                hash = hash * 31 + snapshot.OutputOccupied;
                hash = hash * 31 + (snapshot.OutputPortConnected ? 1 : 0);
                hash = hash * 31 + (snapshot.PipePort ? 1 : 0);
                hash = hash * 31 + (snapshot.LogisticsPort ? 1 : 0);
                hash = hash * 31 + (snapshot.PowerPort ? 1 : 0);
                hash = hash * 31 + (int)snapshot.Warnings;
                hash = hash * 31 + (snapshot.OutputInventoryPortState is null
                    ? 0
                    : snapshot.OutputInventoryPortState.GetHashCode());
                return hash;
            }
        }

        private static long Quantize(float value)
        {
            if (float.IsNaN(value))
            {
                return long.MinValue;
            }

            if (float.IsPositiveInfinity(value))
            {
                return long.MaxValue;
            }

            return (long)(value * 1000f);
        }
    }
}
