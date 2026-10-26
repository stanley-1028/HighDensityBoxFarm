using System;
using System.Collections.Generic;
using HDBF.Core;
using HDBF.Core.Abstractions;
using HDBF.Core.Logging;
using HDBF.Core.Warnings;
using HDBF.UI.Localization;
using Timberborn.BaseComponentSystem;
using Timberborn.EntityPanelSystem;
using Timberborn.EntitySystem;
using Timberborn.Localization;
using UnityEngine;
using UnityEngine.UIElements;

namespace HDBF.UI.Panels
{
    /// <summary>
    /// Entity panel fragment for the four BoxFarm tiers (chapter 14.1).
    ///
    /// The whole fragment is authored in code with UIElements: it loads no UXML/USS asset and
    /// needs no Unity prefab, which is why it can live in a compiled mod assembly. It is added to
    /// the vanilla panel through the game's own <see cref="EntityPanelModule"/> multi-binding
    /// (see <see cref="HdbfEntityPanelModuleProvider"/>).
    ///
    /// Behaviour:
    /// * read-only rows render from <see cref="IBoxFarmQuery"/> / <see cref="BoxFarmSnapshot"/>;
    /// * mode buttons, the maintenance button, the managed-plot highlight toggle and the three
    ///   port toggles write through <see cref="IBoxFarmCommand"/>, which gameplay implements
    ///   (REQ-UI-06/07);
    /// * the managed-plot overlay button/legend only mirror the snapshot's highlight state, show the
    ///   four-state legend and box-select hint while active, and close the overlay when the panel
    ///   closes or another building is selected (REQ-UI-05, chapter 14.3);
    /// * every rejected command and every rejected world-space plot action shows its localized
    ///   failure key; nothing is guessed;
    /// * a failed command leaves the panel showing the authoritative snapshot (no optimistic UI);
    /// * invisible for every entity that is not a box farm (so it adds nothing to other buildings);
    /// * re-queries at most four times per second (REQ-UI-12) and re-renders only when a
    ///   numeric/value fingerprint changed (same values are not re-formatted);
    /// * never throws into the panel: a failing query, formatter or command hides the fragment or
    ///   logs a throttled warning instead (REQ-SAFE-01).
    ///
    /// Keyboard access comes from UIElements: <see cref="Button"/> and <see cref="Toggle"/> are
    /// focusable by default and handle <c>NavigationSubmit</c> (Enter), so Tab/Enter work with the
    /// game's default focus styling; no custom USS is required.
    /// </summary>
    public sealed class HdbfFarmPanelFragment : IEntityPanelFragment
    {
        /// <summary>Quarter-second refresh cap; matches the default <c>perf.uiRefreshHz = 4</c>.</summary>
        private const float RefreshIntervalSeconds = 0.25f;

        private const int StatusRowCapacity = HdbfFarmPanelDataFactory.MaxVisibleWarnings + 1;

        /// <summary>Throttled log key: a command was rejected by gameplay for a normal reason.</summary>
        private const string LogKeyCommandRejected = "ui.command-rejected";

        /// <summary>Throttled log key: a command implementation threw (must never break the panel).</summary>
        private const string LogKeyCommandFailed = "ui.command-exception";

        /// <summary>Throttled log key: formatting/rendering failed and the fragment hid itself.</summary>
        private const string LogKeyRenderFailed = "ui.render";

        /// <summary>Throttled log key: the cross-building aggregation cache could not be read.</summary>
        private const string LogKeyAggregateFailed = "ui.warning-aggregate";

        private readonly IBoxFarmQuery _query;
        private readonly IBoxFarmCommand _command;
        private readonly IBoxFarmWarningQuery _warningQuery;
        private readonly IBoxFarmLocator _locator;
        private readonly ILoc _loc;
        private readonly IHdbfLog _log;
        private readonly List<Label> _statusRowLabels = new(StatusRowCapacity);
        private readonly HashSet<string> _expandedAggregateKeys = new(StringComparer.Ordinal);

        private VisualElement? _root;
        private Label? _title;
        private Label? _statusHeader;
        private Label? _workers;
        private Label? _registeredPlots;
        private Label? _effectivePlots;
        private Label? _tillablePlots;
        private Label? _irrigation;
        private Label? _fatigueAverage;
        private Label? _fatigueMax;
        private Label? _maintenance;
        private Label? _tankWater;
        private Label? _power;
        private Label? _modeHeader;
        private Button? _modeNormal;
        private Button? _modeDense;
        private Button? _modeFallow;
        private Label? _portsHeader;
        private Toggle? _pipePort;
        private Toggle? _powerPort;
        private Toggle? _logisticsPort;
        private Label? _pipePortState;
        private Label? _powerPortState;
        private Label? _logisticsPortState;
        private Button? _maintenanceButton;
        private Button? _highlightButton;
        private Label? _actionStatus;
        private Label? _highlightLegendHeader;
        private readonly List<Label> _highlightLegendRows = new(HdbfPlotHighlightPalette.LegendOrder.Count);
        private Label? _highlightHint;
        private VisualElement? _warningAggregateRoot;

        private BaseComponent? _entity;
        private Guid _entityId;
        private long _lastFingerprint;
        private long _lastAggregateFingerprint;
        private HdbfFarmPanelData? _lastPanelData;
        private bool _hasSnapshot;
        private float _nextRefreshTime;
        private HdbfMode _currentMode = HdbfMode.Normal;
        private bool _denseConfirmationPending;
        private string _actionMessage = string.Empty;

        /// <summary>
        /// Authoritative highlight flag of the last rendered snapshot. It is what the toggle button
        /// inverts; the fragment never stores the clicked target as if it had succeeded.
        /// </summary>
        private bool _lastHighlightActive;

        public HdbfFarmPanelFragment(IBoxFarmQuery query, IBoxFarmCommand command,
            IBoxFarmWarningQuery warningQuery, IBoxFarmLocator locator, ILoc loc, IHdbfLog log)
        {
            _query = query ?? throw new ArgumentNullException(nameof(query));
            _command = command ?? throw new ArgumentNullException(nameof(command));
            _warningQuery = warningQuery ?? throw new ArgumentNullException(nameof(warningQuery));
            _locator = locator ?? throw new ArgumentNullException(nameof(locator));
            _loc = loc ?? throw new ArgumentNullException(nameof(loc));
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        /// <summary>True once the fragment has rendered a farm snapshot.</summary>
        public bool HasData => _hasSnapshot;

        public VisualElement InitializeFragment()
        {
            var root = new VisualElement();
            root.style.flexDirection = FlexDirection.Column;
            root.style.marginTop = 4f;
            root.style.display = DisplayStyle.None;

            // ---- title + status strip (chapter 14.1: 标题区 + 状态条) ----
            _title = AddLabel(root);

            _statusHeader = AddLabel(root);
            _statusHeader.text = _loc.T(HdbfUiLocKeys.PanelStatus);
            for (var i = 0; i < StatusRowCapacity; i++)
            {
                var label = AddLabel(root);
                label.style.display = DisplayStyle.None;
                _statusRowLabels.Add(label);
            }

            // Cross-building warning aggregate rows (chapter 14.2/14.6). Rows are created on demand
            // below the status rows; each row is a focusable Button that expands its member list.
            _warningAggregateRoot = new VisualElement();
            _warningAggregateRoot.style.marginTop = 2f;
            _warningAggregateRoot.style.display = DisplayStyle.None;
            root.Add(_warningAggregateRoot);

            // ---- core data rows ----
            _workers = AddLabel(root);

            // Chapter 11.3: 已注册格 / 有效格 / 可开垦格 are shown together, so an empty registry
            // can never hide behind the capacity-derived effective count.
            _registeredPlots = AddLabel(root);
            _effectivePlots = AddLabel(root);
            _tillablePlots = AddLabel(root);
            _irrigation = AddLabel(root);
            _fatigueAverage = AddLabel(root);
            _fatigueMax = AddLabel(root);
            _maintenance = AddLabel(root);
            _tankWater = AddLabel(root);
            _power = AddLabel(root);

            // ---- mode + ports (chapter 14.1: 模式与端口区, REQ-UI-06/07) ----
            _modeHeader = AddSectionHeader(root, _loc.T(HdbfUiLocKeys.PanelMode));
            var modeRow = new VisualElement();
            modeRow.style.flexDirection = FlexDirection.Row;
            modeRow.style.alignItems = Align.Center;
            _modeNormal = AddModeButton(modeRow, HdbfMode.Normal);
            _modeDense = AddModeButton(modeRow, HdbfMode.Dense);
            _modeFallow = AddModeButton(modeRow, HdbfMode.Fallow);
            root.Add(modeRow);

            _portsHeader = AddSectionHeader(root, _loc.T(HdbfUiLocKeys.PanelPorts));
            _pipePort = AddPortToggle(root, HdbfUiLocKeys.PortPipe, out _pipePortState);
            _powerPort = AddPortToggle(root, HdbfUiLocKeys.PortPower, out _powerPortState);
            _logisticsPort = AddPortToggle(root, HdbfUiLocKeys.PortLogistics, out _logisticsPortState);

            _pipePort.RegisterValueChangedCallback(evt => OnPortChanged(HdbfPortKind.Pipe, evt.newValue));
            _powerPort.RegisterValueChangedCallback(evt => OnPortChanged(HdbfPortKind.Power, evt.newValue));
            _logisticsPort.RegisterValueChangedCallback(evt => OnPortChanged(HdbfPortKind.Logistics, evt.newValue));

            // ---- action area (chapter 14.1: 操作区, REQ-UI-07) ----
            // Maintenance and the managed-plot highlight toggle share one row; both are plain
            // focusable Buttons (Tab reachable, NavigationSubmit on Enter) with no UXML/USS.
            var actionRow = new VisualElement();
            actionRow.style.flexDirection = FlexDirection.Row;
            actionRow.style.alignItems = Align.Center;
            actionRow.style.marginTop = 6f;

            _maintenanceButton = new Button(OnMaintenanceClicked)
            {
                text = _loc.T(HdbfUiLocKeys.ActionMaintenance),
                focusable = true,
            };
            _maintenanceButton.style.marginRight = 4f;
            actionRow.Add(_maintenanceButton);

            _highlightButton = new Button(OnHighlightClicked)
            {
                text = _loc.T(HdbfUiLocKeys.ActionHighlightPlots),
                focusable = true,
            };
            actionRow.Add(_highlightButton);

            root.Add(actionRow);

            _actionStatus = AddLabel(root);
            _actionStatus.style.display = DisplayStyle.None;
            _actionStatus.style.whiteSpace = WhiteSpace.Normal;

            // ---- managed-plot legend + box-select hint (REQ-UI-05, chapter 14.3) ----
            // Hidden until the authoritative snapshot says this farm's overlay is active. Every row
            // carries the state as text; the palette colour is only a redundant second encoding.
            _highlightLegendHeader = AddSectionHeader(root, _loc.T(HdbfUiLocKeys.PanelHighlightLegend));
            _highlightLegendHeader.style.display = DisplayStyle.None;
            for (var i = 0; i < HdbfPlotHighlightPalette.LegendOrder.Count; i++)
            {
                var label = AddLabel(root);
                label.style.marginLeft = 8f;
                label.style.display = DisplayStyle.None;
                _highlightLegendRows.Add(label);
            }

            _highlightHint = AddLabel(root);
            _highlightHint.style.display = DisplayStyle.None;
            _highlightHint.style.whiteSpace = WhiteSpace.Normal;

            _root = root;
            return root;
        }

        public void ShowFragment(BaseComponent entity)
        {
            var nextEntityId = ResolveEntityId(entity);

            // Chapter 14.3: switching to another building closes the previous farm's overlay.
            // Only an overlay this panel knew to be active is closed; closing always succeeds and
            // must never throw into the panel, so it is guarded like every other command call.
            if (_entityId != Guid.Empty && _entityId != nextEntityId && _lastHighlightActive)
            {
                ClosePlotHighlight(_entityId);
            }

            _entity = entity;
            _entityId = nextEntityId;
            _hasSnapshot = false;
            _lastFingerprint = 0L;
            _lastAggregateFingerprint = 0L;
            _lastPanelData = null;
            _expandedAggregateKeys.Clear();
            _nextRefreshTime = 0f;
            _denseConfirmationPending = false;
            _actionMessage = string.Empty;
            _lastHighlightActive = false;
            Refresh(force: true);
        }

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
            Refresh(force: false);
        }

        public void ClearFragment()
        {
            // Chapter 14.3: closing the panel always clears the managed-plot overlay, so a closed
            // panel can never leave the world tinted. Done before the entity id is cleared, and
            // isolated because a broken command implementation must not break the panel (REQ-SAFE-01).
            if (_entityId != Guid.Empty)
            {
                ClosePlotHighlight(_entityId);
            }

            _entity = null;
            _entityId = Guid.Empty;
            _hasSnapshot = false;
            _lastFingerprint = 0L;
            _lastAggregateFingerprint = 0L;
            _lastPanelData = null;
            _expandedAggregateKeys.Clear();
            _denseConfirmationPending = false;
            _actionMessage = string.Empty;
            _lastHighlightActive = false;

            if (_root is not null)
            {
                _root.style.display = DisplayStyle.None;
            }
        }

        // ------------------------------------------------------------------ command callbacks

        /// <summary>
        /// Mode button handler (REQ-UI-06). The first Dense click arms the confirmation only; a
        /// second Dense click (or any other mode) executes. Success/cooldown/missing-mod reasons
        /// come from the command result, never from local guesses.
        /// </summary>
        private void OnModeClicked(HdbfMode mode)
        {
            if (_entityId == Guid.Empty)
            {
                return;
            }

            try
            {
                if (mode == HdbfMode.Dense && !_denseConfirmationPending && _currentMode != HdbfMode.Dense)
                {
                    _denseConfirmationPending = true;
                    SetActionMessage(_loc.T(HdbfUiLocKeys.ActionDenseConfirmHint));
                    Refresh(force: true);
                    return;
                }

                _denseConfirmationPending = false;
                if (_command.TrySetMode(_entityId, mode, out var failureKey))
                {
                    SetActionMessage(_loc.T(HdbfUiLocKeys.ActionModeChanged));
                }
                else
                {
                    SetActionMessage(HdbfFarmPanelDataFactory.FormatCommandFailure(_loc, failureKey));
                    LogRejected("set mode " + mode, failureKey);
                }
            }
            catch (Exception exception)
            {
                // A broken command implementation must never break the entity panel (REQ-SAFE-01).
                SetActionMessage(_loc.T(HdbfUiLocKeys.ErrorActionFailed));
                LogCommandException("set mode " + mode, exception);
            }

            Refresh(force: true);
        }

        /// <summary>Maintenance button handler (REQ-UI-07): shows the returned reason on failure.</summary>
        private void OnMaintenanceClicked()
        {
            if (_entityId == Guid.Empty)
            {
                return;
            }

            try
            {
                if (_command.TryPerformMaintenance(_entityId, out var failureKey))
                {
                    SetActionMessage(_loc.T(HdbfUiLocKeys.ActionMaintenanceDone));
                }
                else
                {
                    SetActionMessage(HdbfFarmPanelDataFactory.FormatCommandFailure(_loc, failureKey));
                    LogRejected("perform maintenance", failureKey);
                }
            }
            catch (Exception exception)
            {
                SetActionMessage(_loc.T(HdbfUiLocKeys.ErrorActionFailed));
                LogCommandException("perform maintenance", exception);
            }

            Refresh(force: true);
        }

        /// <summary>
        /// Managed-plot highlight toggle (REQ-UI-05, chapter 14.3). The target state is the inverse
        /// of the authoritative <c>BoxFarmSnapshot.IsPlotHighlightActive</c>; a rejected toggle keeps
        /// the snapshot's state and shows the returned reason, never an optimistic flip.
        /// </summary>
        private void OnHighlightClicked()
        {
            if (_entityId == Guid.Empty)
            {
                return;
            }

            // Read the last rendered snapshot, not a locally remembered click: if a previous toggle
            // was rejected this still asks for the state opposite to the truth.
            var wantActive = !_lastHighlightActive;
            try
            {
                if (_command.TrySetPlotHighlight(_entityId, wantActive, out var failureKey))
                {
                    // Opening/closing the overlay is its own feedback (world tint + legend), so the
                    // result line is cleared instead of inventing a success sentence.
                    SetActionMessage(string.Empty);
                }
                else
                {
                    SetActionMessage(HdbfFarmPanelDataFactory.FormatCommandFailure(_loc, failureKey));
                    LogRejected("set plot highlight " + wantActive, failureKey);
                }
            }
            catch (Exception exception)
            {
                SetActionMessage(_loc.T(HdbfUiLocKeys.ErrorActionFailed));
                LogCommandException("set plot highlight " + wantActive, exception);
            }

            // Re-read the authoritative snapshot so the button/legend only follow gameplay.
            Refresh(force: true);
        }

        /// <summary>Port toggle handler (REQ-UI-07): an unsupported port reports its reason.</summary>
        private void OnPortChanged(HdbfPortKind port, bool connected)
        {
            if (_entityId == Guid.Empty)
            {
                return;
            }

            try
            {
                if (_command.TrySetPort(_entityId, port, connected, out var failureKey))
                {
                    SetActionMessage(_loc.T(HdbfUiLocKeys.ActionPortUpdated));
                }
                else
                {
                    SetActionMessage(HdbfFarmPanelDataFactory.FormatCommandFailure(_loc, failureKey));
                    LogRejected("set port " + port, failureKey);
                }
            }
            catch (Exception exception)
            {
                SetActionMessage(_loc.T(HdbfUiLocKeys.ErrorActionFailed));
                LogCommandException("set port " + port, exception);
            }

            // Re-read the authoritative snapshot so a rejected switch visibly rolls back.
            Refresh(force: true);
        }

        // ------------------------------------------------------------------ refresh / render

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

                if (!_query.TryGetSnapshot(_entityId, out var snapshot))
                {
                    Hide();
                    return;
                }

                var groups = ReadWarningGroups();
                var fingerprint = ComputeFingerprint(snapshot);
                var aggregateFingerprint = ComputeAggregateFingerprint(snapshot, groups);
                if (!force && _hasSnapshot && fingerprint == _lastFingerprint
                    && aggregateFingerprint == _lastAggregateFingerprint)
                {
                    return;
                }

                var data = HdbfFarmPanelDataFactory.Create(snapshot, _loc, groups);
                _lastFingerprint = fingerprint;
                _lastAggregateFingerprint = aggregateFingerprint;
                _hasSnapshot = true;
                Render(data);
            }
            catch (Exception exception)
            {
                Hide();
                _log.Error(HdbfLogCategory.Error, LogKeyRenderFailed,
                    "box farm panel failed to render; hiding the fragment", exception,
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

        private void Render(HdbfFarmPanelData data)
        {
            if (_root is null)
            {
                return;
            }

            if (!data.HasData)
            {
                Hide();
                return;
            }

            _lastPanelData = data;
            Set(_title, data.Title);
            Set(_workers, data.Workers);
            Set(_registeredPlots, data.RegisteredPlots);
            Set(_effectivePlots, data.EffectivePlots);
            Set(_tillablePlots, data.TillablePlots);
            Set(_irrigation, data.Irrigation);
            Set(_fatigueAverage, data.FatigueAverage);
            Set(_fatigueMax, data.FatigueMax);
            Set(_maintenance, data.Maintenance);
            Set(_tankWater, data.TankWater);
            Set(_power, data.Power);
            Set(_modeHeader, _loc.T(HdbfUiLocKeys.PanelMode));
            Set(_portsHeader, _loc.T(HdbfUiLocKeys.PanelPorts));

            if (_effectivePlots is not null)
            {
                _effectivePlots.tooltip = data.EffectivePlotsTooltip;
            }

            if (_tillablePlots is not null)
            {
                // Work-range area (121/225/361/729) lives in this hover hint, never in the main list
                // (chapter 14.1: 工作范围面积只在详情中展示).
                _tillablePlots.tooltip = data.TillablePlotsTooltip;
            }

            if (_irrigation is not null)
            {
                _irrigation.tooltip = data.IrrigationTooltip;
            }

            RenderStatusRows(data);
            RenderWarningAggregates(data);
            RenderModeButtons(data);
            RenderPorts(data);
            RenderMaintenance(data);
            RenderHighlight(data);

            // Local command feedback (this frame) wins over the snapshot's world-action failure;
            // the snapshot value is the fallback so a rejected click/box-select still explains
            // itself in the same line (chapter 14.3: 注册格不足必须提示).
            RenderActionMessage(_actionMessage.Length > 0 ? _actionMessage : data.HighlightFailure);

            _root.tooltip = data.MultiplierBreakdown;
            _root.style.display = DisplayStyle.Flex;
        }

        private void RenderStatusRows(HdbfFarmPanelData data)
        {
            for (var i = 0; i < _statusRowLabels.Count; i++)
            {
                var label = _statusRowLabels[i];
                if (i < data.StatusRows.Count)
                {
                    label.text = data.StatusRows[i].Text;
                    label.style.display = DisplayStyle.Flex;
                }
                else
                {
                    label.style.display = DisplayStyle.None;
                }
            }
        }

        /// <summary>
        /// Renders the cross-building warning rows (chapter 14.2/14.6). Each row is a focusable
        /// Button; clicking it expands the locatable member buildings, clicking a member selects and
        /// focuses that farm through <see cref="IBoxFarmLocator"/> (vanilla selection service). The
        /// row set is rebuilt only when the aggregate fingerprint changed, and the expanded keys are
        /// kept across renders so a refresh does not collapse an open row.
        /// </summary>
        private void RenderWarningAggregates(HdbfFarmPanelData data)
        {
            if (_warningAggregateRoot is null)
            {
                return;
            }

            _warningAggregateRoot.Clear();
            if (data.WarningAggregates.Count == 0)
            {
                _warningAggregateRoot.style.display = DisplayStyle.None;
                return;
            }

            for (var i = 0; i < data.WarningAggregates.Count; i++)
            {
                var row = data.WarningAggregates[i];
                var key = row.Key;
                var header = new Button(() => ToggleAggregateRow(key))
                {
                    text = row.Text,
                    focusable = true,
                    tooltip = data.WarningAggregateTooltip,
                };
                header.style.whiteSpace = WhiteSpace.Normal;
                header.style.unityTextAlign = TextAnchor.MiddleLeft;
                _warningAggregateRoot.Add(header);

                if (!_expandedAggregateKeys.Contains(row.Key))
                {
                    continue;
                }

                for (var m = 0; m < row.Members.Count; m++)
                {
                    var member = row.Members[m];
                    var entityId = member.EntityId;
                    var memberButton = new Button(() => OnLocateAggregateMember(entityId))
                    {
                        text = "↳ " + member.Text,
                        focusable = true,
                    };
                    memberButton.style.marginLeft = 12f;
                    _warningAggregateRoot.Add(memberButton);
                }
            }

            _warningAggregateRoot.style.display = DisplayStyle.Flex;
        }

        private void ToggleAggregateRow(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            if (!_expandedAggregateKeys.Add(key))
            {
                _expandedAggregateKeys.Remove(key);
            }

            if (_lastPanelData is not null)
            {
                RenderWarningAggregates(_lastPanelData);
            }
        }

        /// <summary>
        /// Member-row click: select/focus another farm that reports the same warning. The vanilla
        /// selection service is behind the Core contract, so the UI keeps no game component. A
        /// vanished building degrades to the generic action-failed text (never a raw key).
        /// </summary>
        private void OnLocateAggregateMember(Guid entityId)
        {
            try
            {
                if (_locator.TryLocate(entityId))
                {
                    SetActionMessage(string.Empty);
                    return;
                }
            }
            catch (Exception exception)
            {
                LogCommandException("locate warning aggregate member", exception);
            }

            SetActionMessage(_loc.T(HdbfUiLocKeys.ErrorActionFailed));
        }

        private void RenderModeButtons(HdbfFarmPanelData data)
        {
            _currentMode = data.ModeValue;
            if (_currentMode == HdbfMode.Dense)
            {
                _denseConfirmationPending = false;
            }

            RenderModeButton(_modeNormal, HdbfMode.Normal, data.ModeValue);
            RenderModeButton(_modeFallow, HdbfMode.Fallow, data.ModeValue);

            if (_modeDense is null)
            {
                return;
            }

            _modeDense.text = _loc.T(_denseConfirmationPending
                ? HdbfUiLocKeys.ActionConfirmDense
                : HdbfFarmPanelDataFactory.ModeKey(HdbfMode.Dense));
            _modeDense.tooltip = HdbfFarmPanelDataFactory.ModeTooltip(_loc, HdbfMode.Dense, data.ModeValue);
            _modeDense.SetEnabled(data.ModeValue != HdbfMode.Dense);
        }

        private void RenderModeButton(Button? button, HdbfMode mode, HdbfMode currentMode)
        {
            if (button is null)
            {
                return;
            }

            button.text = _loc.T(HdbfFarmPanelDataFactory.ModeKey(mode));
            button.tooltip = HdbfFarmPanelDataFactory.ModeTooltip(_loc, mode, currentMode);
            // The current mode is the "selected" segment; the other two stay operable.
            button.SetEnabled(currentMode != mode);
        }

        private void RenderPorts(HdbfFarmPanelData data)
        {
            RenderPort(_pipePort, _pipePortState, HdbfUiLocKeys.PortPipe, HdbfPortKind.Pipe, data.PipePortState);
            RenderPort(_powerPort, _powerPortState, HdbfUiLocKeys.PortPower, HdbfPortKind.Power, data.PowerPortState);
            RenderPort(_logisticsPort, _logisticsPortState, HdbfUiLocKeys.PortLogistics, HdbfPortKind.Logistics,
                data.LogisticsPortState);
        }

        private void RenderPort(Toggle? toggle, Label? stateLabel, string labelKey, HdbfPortKind kind,
            HdbfPortState state)
        {
            if (toggle is null || stateLabel is null)
            {
                return;
            }

            toggle.SetValueWithoutNotify(HdbfFarmPanelDataFactory.IsPortConnected(state));
            toggle.SetEnabled(HdbfFarmPanelDataFactory.IsPortToggleEnabled(state));
            toggle.label = _loc.T(labelKey);
            toggle.tooltip = HdbfFarmPanelDataFactory.PortTooltip(_loc, labelKey, kind, state);

            // Text carries the state so it is never encoded by the checkbox colour alone
            // (chapter 14.5 triple encoding).
            stateLabel.text = _loc.T(HdbfFarmPanelDataFactory.PortStateKey(state));
        }

        private void RenderMaintenance(HdbfFarmPanelData data)
        {
            if (_maintenanceButton is null)
            {
                return;
            }

            _maintenanceButton.text = _loc.T(HdbfUiLocKeys.ActionMaintenance);
            _maintenanceButton.tooltip =
                HdbfFarmPanelDataFactory.MaintenanceTooltip(_loc, data.CanPerformMaintenance, data.IsShutdown);
            _maintenanceButton.SetEnabled(data.CanPerformMaintenance);

            // Shutdown makes maintenance the primary action (chapter 14.3); bold text is the
            // asset-free emphasis and the P0 status row above already names the state.
            var emphasized = data.CanPerformMaintenance && data.IsShutdown;
            _maintenanceButton.style.unityFontStyleAndWeight = emphasized ? FontStyle.Bold : FontStyle.Normal;
        }

        /// <summary>
        /// Renders the managed-plot toggle emphasis, the four-row legend and the box-select hint.
        /// Everything except the toggle is shown only while the snapshot says the overlay is active,
        /// and <see cref="_lastHighlightActive"/> is refreshed here from authoritative data only.
        /// </summary>
        private void RenderHighlight(HdbfFarmPanelData data)
        {
            _lastHighlightActive = data.HighlightActive;

            if (_highlightButton is not null)
            {
                _highlightButton.text = _loc.T(HdbfUiLocKeys.ActionHighlightPlots);
                _highlightButton.tooltip = data.HighlightButtonTooltip;

                // Active emphasis is bold text (asset-free), the same pattern as the shutdown
                // maintenance button; the legend below and its text carry the state, never colour alone.
                _highlightButton.style.unityFontStyleAndWeight =
                    data.HighlightActive ? FontStyle.Bold : FontStyle.Normal;
            }

            if (_highlightLegendHeader is not null)
            {
                _highlightLegendHeader.text = _loc.T(HdbfUiLocKeys.PanelHighlightLegend);
                _highlightLegendHeader.style.display =
                    data.HighlightActive ? DisplayStyle.Flex : DisplayStyle.None;
            }

            for (var i = 0; i < _highlightLegendRows.Count; i++)
            {
                var label = _highlightLegendRows[i];
                if (data.HighlightActive && i < data.HighlightLegend.Count)
                {
                    var row = data.HighlightLegend[i];
                    label.text = row.Text;
                    // Second encoding only: the same state is always written out as text.
                    label.style.color = ToColor(row.Rgba);
                    label.style.display = DisplayStyle.Flex;
                }
                else
                {
                    label.style.display = DisplayStyle.None;
                }
            }

            if (_highlightHint is not null)
            {
                _highlightHint.text = data.HighlightHint;
                _highlightHint.style.display = data.HighlightActive && data.HighlightHint.Length > 0
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
            }
        }

        private void RenderActionMessage(string message)
        {
            if (_actionStatus is null)
            {
                return;
            }

            _actionStatus.text = message;
            _actionStatus.style.display = message.Length == 0 ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private void SetActionMessage(string message)
        {
            _actionMessage = message ?? string.Empty;
            RenderActionMessage(_actionMessage);
        }

        // ------------------------------------------------------------------ helpers

        private void LogRejected(string operation, string? failureKey)
        {
            _log.Write(HdbfLogCategory.Warn, LogKeyCommandRejected,
                operation + " rejected (" + (failureKey ?? "(no key)") + "), entityId=" + _entityId);
        }

        private void LogCommandException(string operation, Exception exception)
        {
            _log.Error(HdbfLogCategory.Error, LogKeyCommandFailed,
                operation + " threw; the panel ignored it", exception, _entityId.ToString());
        }

        /// <summary>
        /// Best-effort close of the managed-plot overlay for <paramref name="entityId"/> (chapter
        /// 14.3: closing the panel or switching buildings clears the highlight automatically).
        /// Closing always succeeds per <see cref="IBoxFarmCommand"/>; any exception is logged,
        /// never rethrown, so a broken command cannot break the entity panel (REQ-SAFE-01).
        /// </summary>
        private void ClosePlotHighlight(Guid entityId)
        {
            try
            {
                _command.TrySetPlotHighlight(entityId, false, out _);
            }
            catch (Exception exception)
            {
                LogCommandException("close plot highlight", exception);
            }
        }

        /// <summary>Converts a Core palette value (<c>0xRRGGBBAA</c>) to a UIElements style colour.</summary>
        private static Color ToColor(uint rgba)
        {
            return new Color(
                ((rgba >> 24) & 0xFF) / 255f,
                ((rgba >> 16) & 0xFF) / 255f,
                ((rgba >> 8) & 0xFF) / 255f,
                (rgba & 0xFF) / 255f);
        }

        private static void Set(Label? label, string text)
        {
            if (label is not null)
            {
                label.text = text;
            }
        }

        private static Label AddLabel(VisualElement parent)
        {
            var label = new Label();
            parent.Add(label);
            return label;
        }

        private static Label AddSectionHeader(VisualElement parent, string localizedText)
        {
            var label = AddLabel(parent);
            label.text = localizedText;
            label.style.marginTop = 6f;
            return label;
        }

        private Button AddModeButton(VisualElement parent, HdbfMode mode)
        {
            var button = new Button(() => OnModeClicked(mode))
            {
                // Focusable + NavigationSubmit (Enter) are UIElements defaults; set explicitly so a
                // future style refactor cannot silently drop keyboard access (chapter 14.3/14.5).
                focusable = true,
            };
            button.style.marginRight = 4f;
            parent.Add(button);
            return button;
        }

        private Toggle AddPortToggle(VisualElement parent, string labelKey, out Label stateLabel)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;

            var toggle = new Toggle
            {
                label = _loc.T(labelKey),
            };
            toggle.style.marginRight = 8f;
            row.Add(toggle);

            stateLabel = new Label();
            row.Add(stateLabel);

            parent.Add(row);
            return toggle;
        }

        private static Guid ResolveEntityId(BaseComponent entity)
        {
            var components = entity.AllComponents;
            for (var i = 0; i < components.Count; i++)
            {
                if (components[i] is EntityComponent entityComponent)
                {
                    return entityComponent.EntityId;
                }
            }

            return Guid.Empty;
        }

        /// <summary>
        /// Reads the cached cross-building groups through the Core contract. A broken
        /// implementation is isolated: the panel falls back to "no aggregate rows" and logs a
        /// throttled error instead of hiding the whole panel (REQ-SAFE-01).
        /// </summary>
        private IReadOnlyList<FarmWarningGroup> ReadWarningGroups()
        {
            try
            {
                return _warningQuery.GetWarningGroups() ?? Array.Empty<FarmWarningGroup>();
            }
            catch (Exception exception)
            {
                _log.Error(HdbfLogCategory.Error, LogKeyAggregateFailed,
                    "cross-building warning aggregation could not be read; rows hidden", exception,
                    _entityId.ToString());
                return Array.Empty<FarmWarningGroup>();
            }
        }

        /// <summary>
        /// Fingerprint of the aggregate inputs (selected farm id + group membership). It is
        /// separate from <see cref="ComputeFingerprint"/> because other farms' warnings can change
        /// while this farm's snapshot is unchanged; without it an open aggregate row would freeze.
        /// </summary>
        private static long ComputeAggregateFingerprint(BoxFarmSnapshot snapshot,
            IReadOnlyList<FarmWarningGroup> groups)
        {
            unchecked
            {
                var hash = 23L;
                hash = hash * 31 + snapshot.RuntimeEntityId.GetHashCode();
                if (groups is null)
                {
                    return hash;
                }

                hash = hash * 31 + groups.Count;
                for (var i = 0; i < groups.Count; i++)
                {
                    var group = groups[i];
                    if (group is null)
                    {
                        continue;
                    }

                    hash = hash * 31 + group.CodeKey.GetHashCode();
                    hash = hash * 31 + group.Count;
                    var members = group.Members;
                    if (members is null)
                    {
                        continue;
                    }

                    for (var m = 0; m < members.Count; m++)
                    {
                        var member = members[m];
                        if (member is null)
                        {
                            continue;
                        }

                        hash = hash * 31 + member.EntityId.GetHashCode();
                        hash = hash * 31 + (int)member.Tier;
                    }
                }

                return hash;
            }
        }

        /// <summary>
        /// Cheap value fingerprint used to skip re-rendering identical snapshots. It only has to
        /// react to every displayed quantity, so collisions are harmless (a collision just costs
        /// one skipped refresh).
        /// </summary>
        private static long ComputeFingerprint(BoxFarmSnapshot snapshot)
        {
            unchecked
            {
                var hash = 17L;
                hash = hash * 31 + (int)snapshot.Tier;
                hash = hash * 31 + (int)snapshot.Mode;
                hash = hash * 31 + snapshot.RegisteredPlots;
                hash = hash * 31 + snapshot.EffectivePlots;
                hash = hash * 31 + snapshot.TillablePlots;
                hash = hash * 31 + snapshot.ActiveWorkers;
                hash = hash * 31 + snapshot.MaxWorkers;
                hash = hash * 31 + (long)(snapshot.IrrigationCoverage * 1000f);
                hash = hash * 31 + (long)(snapshot.FatigueAverage * 1000f);
                hash = hash * 31 + (long)(snapshot.FatigueMax * 1000f);
                hash = hash * 31 + (long)(snapshot.MaintenanceDaysLeft * 10f);
                hash = hash * 31 + snapshot.CyclesMissed;
                hash = hash * 31 + (long)(snapshot.TankDaysLeft * 10f);
                hash = hash * 31 + (snapshot.TierNeedsPower ? 1 : 0);
                hash = hash * 31 + (snapshot.PowerConnected ? 1 : 0);
                hash = hash * 31 + (int)snapshot.PipePort;
                hash = hash * 31 + (int)snapshot.PowerPort;
                hash = hash * 31 + (int)snapshot.LogisticsPort;
                hash = hash * 31 + (snapshot.IsShutdown ? 1 : 0);
                hash = hash * 31 + (snapshot.IsEntranceBlocked ? 1 : 0);
                hash = hash * 31 + (snapshot.IsPlotHighlightActive ? 1 : 0);
                // A changed world-action failure must re-render the result line, so it is part of
                // the fingerprint; null means "no failure" and hashes as zero.
                hash = hash * 31 + (snapshot.PlotActionFailureKey is null
                    ? 0
                    : snapshot.PlotActionFailureKey.GetHashCode());

                var warnings = snapshot.Warnings;
                if (warnings is not null)
                {
                    hash = hash * 31 + warnings.Count;
                    for (var i = 0; i < warnings.Count; i++)
                    {
                        var warning = warnings[i];
                        if (warning is null)
                        {
                            continue;
                        }

                        hash = hash * 31 + warning.CodeKey.GetHashCode();
                        hash = hash * 31 + (int)warning.Severity;
                        hash = hash * 31 + (long)(warning.NumberArg0 * 100f);
                        hash = hash * 31 + (long)(warning.NumberArg1 * 100f);
                    }
                }

                return hash;
            }
        }
    }
}
