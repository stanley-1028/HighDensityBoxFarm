using System;
using System.Collections.Generic;
using HDBF.Core;
using HDBF.Core.Abstractions;
using HDBF.Core.Logging;
using HDBF.Core.Plots;
using Timberborn.AssetSystem;
using Timberborn.BaseComponentSystem;
using Timberborn.BlueprintSystem;
using Timberborn.CameraSystem;
using Timberborn.EntitySystem;
using Timberborn.InputSystem;
using Timberborn.SelectionSystem;
using Timberborn.SingletonSystem;
using Timberborn.TerrainQueryingSystem;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HDBF.Gameplay.Components
{
    /// <summary>
    /// Session singleton that owns the managed-plot overlay of one farm (REQ-UI-05, chapter 14.3):
    /// the world-space input (single click exclude/restore, Shift+drag box exclude, Esc exit), the
    /// frame-budgeted state refresh and the renderer lifetime.
    ///
    /// Why the overlay is input-driven instead of a vanilla tool:
    /// * the panel button only opens the overlay; the selected farm must stay selected, so the game
    ///   must not switch the selected entity. The verified input chain is
    ///   <c>InputService.CallInputProcessors</c>: priority processors run first (and cannot consume),
    ///   then ordinary <c>IInputProcessor</c>s run in reverse registration order and the first one
    ///   returning <c>true</c> wins. The default tool (<c>CursorTool</c>) adds itself on
    ///   <c>Enter()</c> and is what selects/unselects buildings on a main-mouse click
    ///   (<c>CursorTool.ProcessSelectObject</c> calls <c>SelectableObjectRaycaster</c>). This service
    ///   adds itself through the public <c>InputService.AddInputProcessor</c> when the overlay opens,
    ///   i.e. after the cursor tool, so it is evaluated first and returns <c>true</c> for the click:
    ///   the click belongs to the plot operation and never switches buildings (chapter 14.3
    ///   "高亮模式与建筑选择互斥"). A light re-registration guard keeps that precedence if a tool was
    ///   entered later (for example through the bottom bar).
    /// * the cell under the mouse is resolved through the verified public picking path used by the
    ///   vanilla Fields tool: <c>CameraService.ScreenPointToRayInGridSpace</c> +
    ///   <c>TerrainPicker.PickTerrainCoordinates</c>, then the controller's own
    ///   <c>ResolvePlotCell</c> z-rule (chapter 11.3 / BUG-04). No physics colliders, no per-frame
    ///   map scan.
    ///
    /// Budget (chapter 14.6): the plot state is refreshed in slices of at most
    /// <see cref="HighlightFrameBudget.CellsPerFrame"/> (200) cells per frame, so 600 cells complete
    /// in 3 frames; only cells whose state changed cause a matrix update, and the renderer issues at
    /// most one instanced draw call per non-empty state (four total).
    /// </summary>
    public sealed class HdbfPlotHighlightService : ILoadableSingleton, IUnloadableSingleton,
        IUpdatableSingleton, IInputProcessor
    {
        private const string LogKey = "gameplay.highlight";

        /// <summary>Plot colours should follow the 4 Hz panel, but one refresh per 0.5 s is enough.</summary>
        private const float RefreshIntervalSeconds = 0.5f;

        /// <summary>How often the overlay re-asserts input precedence (a tool may add a processor).</summary>
        private const float InputPrecedenceRefreshSeconds = 1f;

        /// <summary>Screen-space drag threshold (squared pixels); below it a Shift press is a click.</summary>
        private const float DragThresholdSquared = 16f;

        private readonly InputService _input;
        private readonly CameraService _camera;
        private readonly TerrainPicker _terrainPicker;
        private readonly EntitySelectionService _selection;
        private readonly HdbfFarmQueryService _farms;
        private readonly HdbfPlotHighlightSession _state;
        private readonly IHdbfLog _log;
        private readonly HdbfPlotHighlightRenderer _renderer;

        private readonly List<PlotHighlightCell> _cellScratch = new List<PlotHighlightCell>(1024);

        private HdbfBoxFarmController? _controller;
        private bool _active;
        private bool _inputRegistered;

        private int[] _workPlotIds = Array.Empty<int>();
        private int _workCursor;
        private bool _refreshCycleActive;
        private float _nextRefreshAt;
        private float _nextInputPrecedenceAt;

        private bool _pointerDown;
        private bool _pointerDownShift;
        private bool _hasPointerDownCell;
        private Vector3 _pointerDownScreen;
        private Vector3Int _pointerDownCell;
        private bool _dragging;

        public HdbfPlotHighlightService(
            InputService input,
            CameraService camera,
            TerrainPicker terrainPicker,
            EntitySelectionService selection,
            IAssetLoader assetLoader,
            HdbfFarmQueryService farms,
            HdbfPlotHighlightSession state,
            IHdbfLog log)
        {
            _input = input ?? throw new ArgumentNullException(nameof(input));
            _camera = camera ?? throw new ArgumentNullException(nameof(camera));
            _terrainPicker = terrainPicker ?? throw new ArgumentNullException(nameof(terrainPicker));
            _selection = selection ?? throw new ArgumentNullException(nameof(selection));
            _farms = farms ?? throw new ArgumentNullException(nameof(farms));
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _renderer = new HdbfPlotHighlightRenderer(assetLoader, log);
        }

        /// <summary>True while an overlay is open (diagnostics/panel toggle read model).</summary>
        public bool IsActive => _active;

        /// <inheritdoc />
        public void Load()
        {
            // Nothing to warm up: assets are resolved lazily on the first draw so the overlay
        }

        /// <inheritdoc />
        public void Unload()
        {
            Deactivate();
        }

        /// <summary>
        /// Opens the overlay for one farm. Returns false for an entity that is not a live box farm;
        /// opening another farm replaces the previous overlay (only one at a time, chapter 14.3).
        /// </summary>
        public bool Activate(Guid entityId)
        {
            if (!_farms.TryGetSource(entityId, out var source) || source is not HdbfBoxFarmController controller)
            {
                return false;
            }

            if (_active && _state.IsActiveFor(entityId))
            {
                return true;
            }

            DeactivateInternal(clearState: true);
            _controller = controller;
            _active = true;
            _state.Activate(entityId);
            EnsureInputRegistered();
            StartRefreshCycle();
            _nextRefreshAt = Time.unscaledTime + RefreshIntervalSeconds;
            _nextInputPrecedenceAt = Time.unscaledTime + InputPrecedenceRefreshSeconds;
            _log.Write(HdbfLogCategory.Info, LogKey, "managed-plot overlay opened, entityId=" + entityId);
            return true;
        }

        /// <summary>Closes the overlay; safe to call when closed (panel close/building switch).</summary>
        public void Deactivate()
        {
            DeactivateInternal(clearState: true);
        }

        /// <inheritdoc />
        public void UpdateSingleton()
        {
            if (!_active || _controller is null)
            {
                return;
            }

            // The panel contract is authoritative: when the farm is no longer selected the overlay
            // must disappear (chapter 14.3 "关闭面板或切换建筑自动清除"). The UI fragment also closes
            // it through IBoxFarmCommand; this check covers a deselection that never reaches the
            // fragment (world unload, panel closed by the game).
            if (!SelectionMatchesActiveFarm())
            {
                DeactivateInternal(clearState: true);
                return;
            }

            var now = Time.unscaledTime;
            MaintainInputPrecedence(now);

            if (!_refreshCycleActive && now >= _nextRefreshAt)
            {
                StartRefreshCycle();
                _nextRefreshAt = now + RefreshIntervalSeconds;
            }

            if (_refreshCycleActive)
            {
                StepRefreshCycle();
            }

            _renderer.Draw();
        }

        // ------------------------------------------------------------------ input

        /// <inheritdoc />
        public bool ProcessInput()
        {
            if (!_active || _controller is null)
            {
                return false;
            }

            // Panel button clicks and other UI controls must keep working.
            if (_input.MouseOverUI)
            {
                return false;
            }

            // Esc closes the overlay first; the game then handles the next Esc (panel close),
            // chapter 14.3 "Esc 层退出". Consuming Cancel also stops the cursor tool from
            // deselecting the farm (which would close the panel early).
            if (_input.Cancel)
            {
                DeactivateInternal(clearState: true);
                return true;
            }

            if (_input.MainMouseButtonDown && !_pointerDown)
            {
                _pointerDown = true;
                _pointerDownShift = ShiftHeld();
                _pointerDownScreen = _input.MousePosition;
                _hasPointerDownCell = TryPickPlantCell(out _pointerDownCell);
                _dragging = false;
                // Consume the press even over empty terrain: in highlight mode a click is a plot
                // operation, never a building-selection click (chapter 14.3).
                return true;
            }

            if (!_pointerDown)
            {
                return false;
            }

            // The gesture was lost (focus change, pointer left the world, release over the UI which
            // is swallowed by the MouseOverUI early-return above): drop it instead of treating the
            // next click as a continuation of a stale drag.
            if (!_input.MainMouseButtonHeld && !_input.MainMouseButtonUp)
            {
                ResetPointer();
                return false;
            }

            if (_input.MainMouseButtonHeld)
            {
                if (_pointerDownShift && _hasPointerDownCell && !_dragging)
                {
                    var delta = _input.MousePosition - _pointerDownScreen;
                    if (delta.sqrMagnitude >= DragThresholdSquared)
                    {
                        _dragging = true;
                    }
                }

                return true;
            }

            if (_input.MainMouseButtonUp)
            {
                _pointerDown = false;
                var wasDragging = _dragging;
                _dragging = false;

                if (wasDragging)
                {
                    if (_hasPointerDownCell && TryPickPlantCell(out var endCell))
                    {
                        ApplyBoxExclusion(_pointerDownCell, endCell);
                    }
                }
                else
                {
                    // Single click: use the release cell when it resolves, else the press cell.
                    if (TryPickPlantCell(out var clickCell))
                    {
                        ApplyClick(clickCell);
                    }
                    else if (_hasPointerDownCell)
                    {
                        ApplyClick(_pointerDownCell);
                    }
                }

                return true;
            }

            // Swallow the rest of the gesture so no other processor reacts mid-drag.
            return true;
        }

        /// <summary>
        /// Keeps this processor ahead of the game's tool processors. The input service evaluates
        /// ordinary processors from the end of its registration list, so re-adding moves us to the
        /// front; doing it once per second is enough because a tool can only be entered through the
        /// UI (while the pointer is over the panel, this processor is inactive anyway).
        /// </summary>
        private void MaintainInputPrecedence(float now)
        {
            if (now < _nextInputPrecedenceAt)
            {
                return;
            }

            _nextInputPrecedenceAt = now + InputPrecedenceRefreshSeconds;
            _input.RemoveInputProcessor(this);
            _input.AddInputProcessor(this);
        }

        // ------------------------------------------------------------------ frame-budgeted refresh

        private void StartRefreshCycle()
        {
            if (_controller is null)
            {
                return;
            }

            _workPlotIds = _controller.BuildHighlightPlotIdList();
            _workCursor = 0;
            _refreshCycleActive = true;
            _renderer.BeginRefresh();
        }

        private void StepRefreshCycle()
        {
            var budget = HighlightFrameBudget.CellsPerFrame;
            var processed = 0;
            while (processed < budget && _workCursor < _workPlotIds.Length)
            {
                OfferCell(_workPlotIds[_workCursor]);
                _workCursor++;
                processed++;
            }

            if (_workCursor >= _workPlotIds.Length)
            {
                _refreshCycleActive = false;
                _renderer.EndRefresh();
            }
        }

        /// <summary>Classifies one plot and applies it to the renderer; called in budget slices.</summary>
        private void OfferCell(int plotId)
        {
            if (_controller is null)
            {
                return;
            }

            if (TryClassifyPlot(plotId, out var state))
            {
                _renderer.Offer(PlotHighlightCell.FromPlotId(plotId, state));
            }
        }

        /// <summary>Immediate refresh of one cell after a click (outside the slice loop).</summary>
        private void UpdateCellNow(int plotId)
        {
            if (TryClassifyPlot(plotId, out var state))
            {
                _renderer.Offer(PlotHighlightCell.FromPlotId(plotId, state));
            }
            else
            {
                _renderer.Remove(plotId);
            }
        }

        private bool TryClassifyPlot(int plotId, out HdbfPlotHighlightState state)
        {
            state = default;
            if (_controller is null)
            {
                return false;
            }

            var registered = _controller.IsRegisteredForHighlight(plotId);
            var excluded = _controller.IsPlotExcluded(plotId);
            var irrigated = registered && _controller.IsPlotIrrigated(plotId);
            // Unreachable (red) is the optional chapter-11.3 diagnostic and is never produced by
            // the current scan: a cell that fails the reachability test is not registered, and the
            // scan does not keep the rejected candidate list. The contract and the red bucket are
            // ready; populating it is a follow-up scan change (see the workstream report).
            return PlotHighlightClassifier.TryClassify(registered, excluded, irrigated, false, out state);
        }

        // ------------------------------------------------------------------ plot operations

        private void ApplyClick(Vector3Int plantCell)
        {
            if (_controller is null)
            {
                return;
            }

            var plotId = HdbfPlotCellCodec.Encode(plantCell);
            if (!_controller.HasPlotForHighlight(plotId))
            {
                // Empty cell: silent no-op (nothing to explain to the player).
                return;
            }

            if (_controller.TryTogglePlotExclusion(plotId, out var failureKey))
            {
                _state.ReportSuccess();
            }
            else
            {
                _state.ReportFailure(failureKey);
            }

            UpdateCellNow(plotId);
            // Let the next slice cycle pick up the registry change (restore triggers a rescan).
            _nextRefreshAt = Math.Min(_nextRefreshAt, Time.unscaledTime + 0.05f);
        }

        private void ApplyBoxExclusion(Vector3Int startCell, Vector3Int endCell)
        {
            if (_controller is null)
            {
                return;
            }

            // The rectangle is the two clicked ground positions; level = the drag-start plant cell
            // (plots on other z levels are outside a level-selected rectangle).
            _renderer.CopyCells(_cellScratch);
            var plotIds = PlotBoxSelection.RegisteredCellsInBox(
                _cellScratch, startCell.x, startCell.y, endCell.x, endCell.y, startCell.z);
            if (plotIds.Count == 0)
            {
                return;
            }

            if (_controller.TryExcludePlots(plotIds, out var failureKey))
            {
                _state.ReportSuccess();
            }
            else
            {
                _state.ReportFailure(failureKey);
            }

            for (var i = 0; i < plotIds.Count; i++)
            {
                UpdateCellNow(plotIds[i]);
            }

            _nextRefreshAt = Math.Min(_nextRefreshAt, Time.unscaledTime + 0.05f);
        }

        // ------------------------------------------------------------------ helpers

        private void DeactivateInternal(bool clearState)
        {
            var wasActive = _active || _inputRegistered;
            _active = false;
            _controller = null;
            _refreshCycleActive = false;
            _workPlotIds = Array.Empty<int>();
            _workCursor = 0;
            ResetPointer();

            if (_inputRegistered)
            {
                try
                {
                    _input.RemoveInputProcessor(this);
                }
                catch (Exception exception)
                {
                    _log.Error(HdbfLogCategory.Warn, LogKey,
                        "input processor removal failed", exception);
                }

                _inputRegistered = false;
            }

            _renderer.Hide();
            if (clearState)
            {
                _state.Clear();
            }

            if (wasActive)
            {
                _log.Write(HdbfLogCategory.Info, LogKey, "managed-plot overlay closed");
            }
        }

        private void ResetPointer()
        {
            _pointerDown = false;
            _pointerDownShift = false;
            _hasPointerDownCell = false;
            _dragging = false;
        }

        private void EnsureInputRegistered()
        {
            if (_inputRegistered)
            {
                return;
            }

            _input.AddInputProcessor(this);
            _inputRegistered = true;
        }

        private bool SelectionMatchesActiveFarm()
        {
            var selected = _selection.SelectedObject;
            if (selected is null)
            {
                return false;
            }

            return ResolveEntityId(selected) == _state.ActiveEntityId;
        }

        private static Guid ResolveEntityId(BaseComponent component)
        {
            var components = component.AllComponents;
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
        /// Resolves the plant cell under the mouse through the vanilla picking path (verified IL:
        /// the planting tool's <c>TerrainAreaService.InMapLeveledCoordinates</c> levels the terrain
        /// voxel to <c>z + 1</c>; <c>ResolvePlotCell</c> applies the same plant-cell rule the scan
        /// uses). No Physics collider and no per-frame map traversal beyond the ray march.
        /// </summary>
        private bool TryPickPlantCell(out Vector3Int plantCell)
        {
            plantCell = default;
            if (_controller is null)
            {
                return false;
            }

            try
            {
                var mouse = _input.MousePosition;
                var ray = _camera.ScreenPointToRayInGridSpace(new Vector2(mouse.x, mouse.y));
                var picked = _terrainPicker.PickTerrainCoordinates(ray);
                if (!picked.HasValue)
                {
                    return false;
                }

                var coords = picked.Value.Coordinates;
                return _controller.TryResolvePlantCell(coords.x, coords.y, out plantCell);
            }
            catch (Exception exception)
            {
                _log.Error(HdbfLogCategory.Warn, LogKey, "plot picking failed", exception);
                return false;
            }
        }

        private static bool ShiftHeld()
        {
            // The game runs on the Unity Input System (KeyboardListener IL: UnityEngine.InputSystem),
            // so reading the keyboard device is safe and does not depend on the legacy Input class
            // (which may be disabled by the player's input-handling setting).
            var keyboard = Keyboard.current;
            return keyboard is not null
                   && (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed);
        }
    }
}
