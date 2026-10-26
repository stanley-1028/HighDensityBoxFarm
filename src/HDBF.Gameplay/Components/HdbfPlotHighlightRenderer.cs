using System;
using System.Collections.Generic;
using HDBF.Core;
using HDBF.Core.Abstractions;
using HDBF.Core.Logging;
using Timberborn.AssetSystem;
using Timberborn.Coordinates;
using Timberborn.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace HDBF.Gameplay.Components
{
    /// <summary>
    /// World-space renderer for the managed-plot overlay (REQ-UI-05, chapter 14.3/14.6).
    ///
    /// Path choice (full IL evidence in docs/workstreams/build-02-plot-highlight.md):
    /// * the vanilla cell highlighter <c>Timberborn.SelectionSystem.AreaHighlightingService</c> is a
    ///   public singleton with exactly the needed API — <c>DrawTile(Vector3Int, Color)</c> — but its
    ///   IL calls <c>MeshDrawer.DrawAtCoordinates</c>, which issues one <c>Graphics.DrawMesh</c> per
    ///   tile. Vanilla therefore spends one draw call per cell; 600 cells would violate the
    ///   chapter-14.6 budget of ≤4 draw calls.
    /// * <c>MeshDrawer.DrawMultipleInstanced</c> does batch (one
    ///   <c>Graphics.DrawMeshInstanced</c>) but drops the per-tile material-property block, so it
    ///   cannot colour four states.
    /// * <c>MarkerDrawerFactory.CreateTileDrawer(Color)</c> is public and returns a coloured
    ///   <c>MeshDrawer</c>, but the mesh/material it captured are private fields and the owning spec
    ///   type is internal in 1.1.2.8.
    ///
    /// This renderer therefore reuses the *vanilla assets by their verified asset paths* — the same
    /// <c>Markers/TileMarker</c> mesh and <c>Markers/TileMarkerSmall</c> material that
    /// <c>AreaHighlightingService</c>/<c>AreaTileDrawerFactory</c> load from
    /// <c>MarkerDrawerFactory.blueprint.json</c> / <c>AreaTileDrawerFactory.blueprint.json</c> — and
    /// submits them with the same instanced Graphics API vanilla uses
    /// (<c>Graphics.DrawMeshInstanced</c>, one call per state, four calls total) on the same
    /// <c>Layers.UILayer</c> and at the same 0.02 vertical offset as <c>DrawTile</c>. No GameObject,
    /// Material, Mesh or LineRenderer is created or leaked by the mod; only the session state exists.
    ///
    /// Update budget: <see cref="Offer"/> is called only for cells whose state actually changed
    /// (frame-budgeted by <see cref="HdbfPlotHighlightService"/> at 200 cells per frame), the
    /// transform is cached per plot, and the per-state draw arrays are rebuilt only when a cell
    /// changed. A frame with no changes costs four instanced draws and no allocations.
    ///
    /// Failure policy: when the two vanilla assets cannot be loaded the overlay hides itself, logs
    /// one throttled warning and gameplay keeps running (REQ-SAFE-01). It never falls back to a
    /// per-cell draw path, because that would silently break the draw-call budget.
    /// </summary>
    internal sealed class HdbfPlotHighlightRenderer
    {
        /// <summary>Vanilla marker tile mesh path (MarkerDrawerFactory.blueprint.json "TileMesh").</summary>
        private const string TileMeshPath = "Markers/TileMarker";

        /// <summary>Vanilla marker tile material path (MarkerDrawerFactory.blueprint.json "TileMaterial").</summary>
        private const string TileMaterialPath = "Markers/TileMarkerSmall";

        /// <summary>Vanilla tile material colour property (MeshDrawer.ColorProperty IL: "_BaseColor").</summary>
        private static readonly int ColorProperty = Shader.PropertyToID("_BaseColor");

        /// <summary>Same vertical offset as AreaHighlightingService.DrawTile (IL: ldc.r4 0.02).</summary>
        private const float TileVerticalOffset = 0.02f;

        /// <summary>Graphics.DrawMeshInstanced cannot batch more than 1023 matrices per call.</summary>
        private const int MaxInstancesPerDrawCall = 1023;

        private readonly IAssetLoader _assetLoader;
        private readonly IHdbfLog _log;

        private readonly Dictionary<int, PlotHighlightCell> _cells =
            new Dictionary<int, PlotHighlightCell>();

        /// <summary>Cached transform per plot; recomputed only when the cell is offered again.</summary>
        private readonly Dictionary<int, Matrix4x4> _matrices = new Dictionary<int, Matrix4x4>();

        private readonly List<Matrix4x4>[] _drawMatrices =
        {
            new List<Matrix4x4>(),
            new List<Matrix4x4>(),
            new List<Matrix4x4>(),
            new List<Matrix4x4>(),
        };

        private readonly MaterialPropertyBlock[] _properties =
        {
            new MaterialPropertyBlock(),
            new MaterialPropertyBlock(),
            new MaterialPropertyBlock(),
            new MaterialPropertyBlock(),
        };

        private readonly HashSet<int> _seen = new HashSet<int>();
        private readonly List<int> _removeScratch = new List<int>();

        /// <summary>Fixed scratch buffer for the instanced draw; sized once, no per-frame allocation.</summary>
        private readonly Matrix4x4[] _instanceBuffer = new Matrix4x4[MaxInstancesPerDrawCall];

        private Mesh? _tileMesh;
        private Material? _tileMaterial;
        private bool _assetsResolved;
        private bool _assetFailureReported;
        private bool _visible;
        private bool _drawListsDirty;

        public HdbfPlotHighlightRenderer(IAssetLoader assetLoader, IHdbfLog log)
        {
            _assetLoader = assetLoader ?? throw new ArgumentNullException(nameof(assetLoader));
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        /// <summary>True once at least one cell is being displayed (diagnostics seam).</summary>
        public bool HasCells => _cells.Count > 0;

        /// <summary>
        /// Starts a frame-budgeted rebuild cycle. Cells offered until <see cref="EndRefresh"/> are
        /// kept; cells that were not offered are removed at the end (a restored plot disappears
        /// from the overlay until the next scan registers it again).
        /// </summary>
        public void BeginRefresh()
        {
            _visible = true;
            _seen.Clear();
        }

        /// <summary>Adds/updates one cell; the matrix is recomputed only when its state changed.</summary>
        public void Offer(PlotHighlightCell cell)
        {
            _seen.Add(cell.PlotId);

            if (_cells.TryGetValue(cell.PlotId, out var current) && current.State == cell.State)
            {
                return;
            }

            _cells[cell.PlotId] = cell;
            _matrices[cell.PlotId] = ToMatrix(cell);
            _drawListsDirty = true;
        }

        /// <summary>
        /// Copies the currently displayed cells into <paramref name="target"/> (input for the Core
        /// box-selection rule). Only called on a drag release, never per frame.
        /// </summary>
        public void CopyCells(List<PlotHighlightCell> target)
        {
            target.Clear();
            foreach (var pair in _cells)
            {
                target.Add(pair.Value);
            }
        }

        /// <summary>Removes one plot immediately (used after a restore/scan change).</summary>
        public void Remove(int plotId)
        {
            if (_cells.Remove(plotId))
            {
                _matrices.Remove(plotId);
                _drawListsDirty = true;
            }
        }

        /// <summary>Prunes cells that the completed refresh cycle no longer reported.</summary>
        public void EndRefresh()
        {
            if (_seen.Count == _cells.Count)
            {
                return;
            }

            _removeScratch.Clear();
            foreach (var pair in _cells)
            {
                if (!_seen.Contains(pair.Key))
                {
                    _removeScratch.Add(pair.Key);
                }
            }

            for (var i = 0; i < _removeScratch.Count; i++)
            {
                _cells.Remove(_removeScratch[i]);
                _matrices.Remove(_removeScratch[i]);
            }

            if (_removeScratch.Count > 0)
            {
                _drawListsDirty = true;
            }
        }

        /// <summary>Hides the overlay and drops all cached cells.</summary>
        public void Hide()
        {
            _visible = false;
            if (_cells.Count > 0)
            {
                _cells.Clear();
                _matrices.Clear();
                _drawListsDirty = true;
            }
        }

        /// <summary>
        /// Submits the overlay for this frame. Returns the number of instanced draw calls issued
        /// (0 when hidden/empty), the direct evidence for the four-draw-call budget of chapter 14.6.
        /// </summary>
        public int Draw()
        {
            if (!_visible || _cells.Count == 0 || !ResolveAssets())
            {
                return 0;
            }

            if (_drawListsDirty)
            {
                RebuildDrawLists();
            }

            var drawCalls = 0;
            for (var state = 0; state < _drawMatrices.Length; state++)
            {
                var matrices = _drawMatrices[state];
                var offset = 0;
                while (offset < matrices.Count)
                {
                    var count = Math.Min(MaxInstancesPerDrawCall, matrices.Count - offset);
                    matrices.CopyTo(offset, _instanceBuffer, 0, count);
                    Submit(_instanceBuffer, count, _properties[state]);
                    offset += count;
                    drawCalls++;
                }
            }

            return drawCalls;
        }

        private void Submit(Matrix4x4[] matrices, int count, MaterialPropertyBlock properties)
        {
            if (_tileMesh is null || _tileMaterial is null)
            {
                return;
            }

            // Same instanced API MeshDrawer.DrawMultipleInstanced uses (verified IL). It is marked
            // obsolete in newer Unity versions in favour of RenderMeshInstanced, but it still ships
            // and is the exact call the game itself uses for marker instancing.
#pragma warning disable 0618
            Graphics.DrawMeshInstanced(
                _tileMesh,
                0,
                _tileMaterial,
                matrices,
                count,
                properties,
                ShadowCastingMode.Off,
                false,
                Layers.UILayer,
                null,
                LightProbeUsage.Off,
                null);
#pragma warning restore 0618
        }

        private void RebuildDrawLists()
        {
            for (var state = 0; state < _drawMatrices.Length; state++)
            {
                _drawMatrices[state].Clear();
            }

            foreach (var pair in _cells)
            {
                var stateIndex = (int)pair.Value.State;
                if (stateIndex < 0 || stateIndex >= _drawMatrices.Length)
                {
                    stateIndex = (int)HdbfPlotHighlightState.Unreachable;
                }

                if (_matrices.TryGetValue(pair.Key, out var matrix))
                {
                    _drawMatrices[stateIndex].Add(matrix);
                }
            }

            _drawListsDirty = false;
        }

        private bool ResolveAssets()
        {
            if (_assetsResolved)
            {
                return _tileMesh is not null && _tileMaterial is not null;
            }

            _assetsResolved = true;
            try
            {
                // The same two assets the vanilla highlighter loads through MarkerDrawerFactorySpec;
                // the paths are copied from the shipped vanilla blueprint (verified reference data in
                // %USERPROFILE%\.hdbf\ref\Blueprints\Configurations\).
                _tileMesh = _assetLoader.Load<Mesh>(TileMeshPath);
                _tileMaterial = _assetLoader.Load<Material>(TileMaterialPath);
                if (_tileMesh is null || _tileMaterial is null)
                {
                    ReportAssetFailure("vanilla marker asset path returned null");
                }
                else
                {
                    InitializeColorProperties();
                }
            }
            catch (Exception exception)
            {
                ReportAssetFailure(exception);
            }

            return _tileMesh is not null && _tileMaterial is not null;
        }

        private void InitializeColorProperties()
        {
            for (var state = 0; state < _properties.Length; state++)
            {
                var rgba = HdbfPlotHighlightPalette.Rgba((HdbfPlotHighlightState)state);
                var color = new Color32(
                    (byte)(rgba >> 24),
                    (byte)(rgba >> 16),
                    (byte)(rgba >> 8),
                    (byte)rgba);
                _properties[state].SetColor(ColorProperty, color);
            }
        }

        private static Matrix4x4 ToMatrix(PlotHighlightCell cell)
        {
            var position = CoordinateSystem.GridToWorldCentered(new Vector3Int(cell.X, cell.Y, cell.Z))
                           + new Vector3(0f, TileVerticalOffset, 0f);
            return Matrix4x4.TRS(position, Quaternion.identity, Vector3.one);
        }

        private void ReportAssetFailure(object? detail)
        {
            if (_assetFailureReported)
            {
                return;
            }

            _assetFailureReported = true;
            _log.Error(HdbfLogCategory.Warn, "gameplay.highlight",
                "managed-plot overlay hidden; vanilla marker tile assets could not be loaded: "
                + (detail is Exception exception ? exception.GetType().Name : detail?.ToString() ?? "null"),
                detail as Exception);
        }
    }
}
