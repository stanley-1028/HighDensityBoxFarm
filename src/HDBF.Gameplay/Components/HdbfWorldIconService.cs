using System;
using System.Collections.Generic;
using HDBF.Core;
using HDBF.Core.Abstractions;
using HDBF.Core.Balance;
using HDBF.Core.Logging;
using HDBF.Core.Warnings;
using Timberborn.BaseComponentSystem;
using Timberborn.CameraSystem;
using Timberborn.Localization;
using Timberborn.SingletonSystem;
using Timberborn.StatusSystem;
using UnityEngine;

namespace HDBF.Gameplay.Components
{
    /// <summary>
    /// Owns the chapter-14.2/14.6 world-space P0/P1 warning icons by reusing the **vanilla**
    /// floating-status-icon pipeline instead of drawing anything itself:
    ///
    /// * every vanilla building already carries <see cref="StatusSubject"/>, <see cref="StatusIconCycler"/>
    ///   and <c>BuildingStatusIconOffsetter</c> (decorated on <c>BuildingSpec</c> by
    ///   <c>Timberborn.BuildingStatuses.BuildingStatusesConfigurator</c>, IL evidence in the
    ///   BUILD-04 report);
    /// * this service registers one <see cref="StatusToggle"/> per P0/P1 warning kind on the farm's
    ///   subject and only ever keeps the highest-priority one active, so the vanilla cycler shows
    ///   exactly one icon (camera facing, sprite material, scaling and screen-space collision are
    ///   all vanilla);
    /// * "hide" (distance/off-screen/blink) is done through the vanilla
    ///   <see cref="StatusVisibilityToggle"/>, so the status itself stays active for the vanilla
    ///   status aggregation while only the floating icon disappears.
    ///
    /// Performance (chapter 14.6): one pass every <c>ui.worldIconIntervalSeconds</c> (default
    /// 0.5 s); only farms inside the camera viewport and within <c>ui.worldIconMaxDistance</c>
    /// cells of the camera focus get a snapshot rebuilt; at most <c>ui.worldIconFarmsPerTick</c>
    /// farms per pass (round-robin). Off-screen farms are culled with a cheap transform test and
    /// their aggregation entry expires. No per-frame work in <c>Update</c>, no Find/GetComponent in
    /// the update path (components are resolved once at registration).
    ///
    /// Failure policy: an icon problem only disables icons for that farm; the panel and the farm
    /// simulation are untouched (REQ-SAFE-01/02).
    /// </summary>
    public sealed class HdbfWorldIconService : IUpdatableSingleton, IUnloadableSingleton
    {
        /// <summary>Vanilla status sprite names (IL literals from vanilla status classes).</summary>
        private const string SpriteShutdown = "UnspecifiedRecipe";

        private const string SpriteNoWater = "BuildingNeedsWater";
        private const string SpriteIrrigationLow = "BuildingNeedsWater";
        private const string SpriteNoPower = "NoPower";
        private const string SpriteAdapterFailed = "UnconnectedBuilding";

        /// <summary>Vanilla status icons fade by screen-space size, not by a mod-side scale.</summary>
        private const float FloatingIconDelayHours = 0f;

        private readonly IBoxFarmQuery _query;
        private readonly IHdbfGameConfigProvider _configProvider;
        private readonly IHdbfLog _log;
        private readonly ILoc _loc;
        private readonly CameraService _cameraService;
        private readonly HdbfWarningBoard _board;

        private readonly Dictionary<Guid, IconHandle> _handles = new Dictionary<Guid, IconHandle>();
        private readonly List<Guid> _order = new List<Guid>();

        private Camera? _camera;
        private double _nextIconPass;
        private int _cursor;

        public HdbfWorldIconService(
            IBoxFarmQuery query,
            IHdbfGameConfigProvider configProvider,
            IHdbfLog log,
            ILoc loc,
            CameraService cameraService,
            HdbfWarningBoard board)
        {
            _query = query ?? throw new ArgumentNullException(nameof(query));
            _configProvider = configProvider ?? throw new ArgumentNullException(nameof(configProvider));
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _loc = loc ?? throw new ArgumentNullException(nameof(loc));
            _cameraService = cameraService ?? throw new ArgumentNullException(nameof(cameraService));
            _board = board ?? throw new ArgumentNullException(nameof(board));
        }

        /// <summary>Number of farms with registered icon handles; diagnostics/tests only.</summary>
        public int RegisteredFarmCount => _handles.Count;

        /// <summary>
        /// Called by the farm controller when it becomes finished. Resolves the vanilla status
        /// components and registers one toggle per P0/P1 kind. Returns false (and logs) when the
        /// entity lacks the vanilla pipeline; the farm stays fully functional without icons.
        /// </summary>
        public bool Register(Guid entityId, BaseComponent entity)
        {
            if (entityId == Guid.Empty || entity is null)
            {
                return false;
            }

            if (_handles.ContainsKey(entityId))
            {
                // Re-entering the finished state (construction-mode toggle) reuses the toggles that
                // are already registered on the StatusSubject; registering them again would leave
                // duplicate status entries in the vanilla status aggregator.
                _handles[entityId].Active = true;
                return true;
            }

            try
            {
                // The cross-building aggregation (chapter 14.2) must keep working even when the
                // optional world icons are switched off, so the entity is always tracked. The
                // vanilla status pipeline is only needed to draw the icon; when it is missing the
                // farm is still a valid aggregation source.
                StatusSubject? subject = null;
                StatusIconCycler? cycler = null;
                try
                {
                    subject = entity.GetComponent<StatusSubject>();
                    cycler = entity.GetComponent<StatusIconCycler>();
                }
                catch (Exception exception)
                {
                    _log.Error(HdbfLogCategory.Warn,
                        HdbfLogKeys.WorldIconsUnavailable + ":" + entityId,
                        "vanilla status components could not be resolved; this farm contributes to "
                        + "the aggregation but gets no head-top icons", exception, entityId.ToString());
                }

                if (subject is null || cycler is null)
                {
                    _log.Write(HdbfLogCategory.Warn,
                        HdbfLogKeys.WorldIconsUnavailable + ":" + entityId,
                        "vanilla StatusSubject/StatusIconCycler missing; head-top warning icons "
                        + "disabled for this farm (panel and aggregation unaffected), entityId="
                        + entityId);
                }

                var toggles = new StatusToggle?[FarmWarningCatalog.MaxIconKind + 1];
                if (subject is not null)
                {
                    RegisterToggle(entityId, subject, toggles, FarmWarningIconKind.Shutdown,
                        SpriteShutdown, isPriority: true);
                    RegisterToggle(entityId, subject, toggles, FarmWarningIconKind.NoWater,
                        SpriteNoWater, isPriority: true);
                    RegisterToggle(entityId, subject, toggles, FarmWarningIconKind.IrrigationLow,
                        SpriteIrrigationLow, isPriority: false);
                    RegisterToggle(entityId, subject, toggles, FarmWarningIconKind.NoPower,
                        SpriteNoPower, isPriority: false);
                    RegisterToggle(entityId, subject, toggles, FarmWarningIconKind.AdapterFailed,
                        SpriteAdapterFailed, isPriority: false);
                }

                var visibility = subject is not null && cycler is not null
                    ? TryGetVisibilityToggle(entityId, cycler)
                    : null;
                var handle = new IconHandle(entityId, entity, subject, cycler, visibility, toggles);
                _handles[entityId] = handle;
                _order.Add(entityId);
                return true;
            }
            catch (Exception exception)
            {
                _log.Error(HdbfLogCategory.Warn, HdbfLogKeys.WorldIconsUnavailable + ":" + entityId,
                    "world warning icon registration failed; icons disabled for this farm",
                    exception, entityId.ToString());
                return false;
            }
        }

        /// <summary>
        /// Called when the farm leaves the finished state (construction-mode toggle) or is
        /// demolished. Deactivates every toggle so no stale status remains in the vanilla
        /// aggregation, but keeps the registered toggles for a later re-activation (a
        /// <see cref="StatusSubject"/> offers no "unregister status" API, so creating new toggles
        /// on every finished-state toggle would duplicate them).
        /// </summary>
        public void Deactivate(Guid entityId)
        {
            if (entityId == Guid.Empty || !_handles.TryGetValue(entityId, out var handle))
            {
                return;
            }

            handle.Active = false;
            handle.CullingHidden = true;
            handle.BlinkHidden = false;
            _board.Remove(entityId);
            TryDeactivateToggles(handle);
        }

        /// <summary>
        /// Drops all references to a farm. Called from <c>DeleteEntity</c>: the entity tree is going
        /// away, so keeping its components would leak them. Idempotent.
        /// </summary>
        public void Forget(Guid entityId)
        {
            if (entityId == Guid.Empty || !_handles.TryGetValue(entityId, out var handle))
            {
                return;
            }

            Deactivate(entityId);
            _handles.Remove(entityId);
            _order.Remove(entityId);
            if (handle.Visibility is null)
            {
                return;
            }

            try
            {
                handle.Visibility.Show();
            }
            catch (Exception)
            {
                // The entity tree is being destroyed; nothing to repair.
            }
        }

        private StatusVisibilityToggle? TryGetVisibilityToggle(Guid entityId, StatusIconCycler cycler)
        {
            try
            {
                return cycler.GetStatusVisibilityToggle();
            }
            catch (Exception exception)
            {
                _log.Error(HdbfLogCategory.Warn,
                    HdbfLogKeys.WorldIconsUnavailable + ":" + entityId,
                    "vanilla status visibility toggle could not be created; icons cannot be "
                    + "hidden by distance/blink for this farm", exception, entityId.ToString());
                return null;
            }
        }

        private void TryDeactivateToggles(IconHandle handle)
        {
            try
            {
                for (var kind = 1; kind < handle.Toggles.Length; kind++)
                {
                    var toggle = handle.Toggles[kind];
                    if (toggle is not null && toggle.IsActive)
                    {
                        toggle.Deactivate();
                    }
                }
            }
            catch (Exception exception)
            {
                _log.Error(HdbfLogCategory.Warn,
                    HdbfLogKeys.WorldIconsUnavailable + ":" + handle.EntityId,
                    "world warning icon deactivation failed", exception, handle.EntityId.ToString());
            }
        }

        /// <inheritdoc />
        public void UpdateSingleton()
        {
            if (_handles.Count == 0)
            {
                return;
            }

            try
            {
                var config = _configProvider.Config;
                var ui = config.Ui;
                // The world icons are optional (chapter 14.2 allows hiding them), but the
                // cross-building aggregation is part of the warning list and must keep running;
                // with icons off the pass still evaluates on-screen farms and publishes to the
                // board, it just leaves every status toggle inactive.
                var iconsEnabled = ui.WorldIcons;

                EnsureCamera();
                if (_camera is null)
                {
                    // The camera singleton may not have created its Camera yet in the first frames.
                    return;
                }

                var now = Time.unscaledTime;
                var interval = ui.WorldIconIntervalSeconds > 0f
                    ? ui.WorldIconIntervalSeconds
                    : WorldIconPolicy.DefaultIconIntervalSeconds;
                if (!WorldIconPolicy.IsDue(now, _nextIconPass, interval))
                {
                    return;
                }

                _nextIconPass = now + interval;
                var maxDistance = ui.WorldIconMaxDistance > 0
                    ? ui.WorldIconMaxDistance
                    : WorldIconPolicy.DefaultMaxDistance;
                var budget = ui.WorldIconFarmsPerTick > 0
                    ? ui.WorldIconFarmsPerTick
                    : WorldIconPolicy.DefaultFarmsPerPass;
                var focus = _cameraService.Target;

                var count = _order.Count;
                if (count == 0)
                {
                    return;
                }

                var start = _cursor % count;
                var evaluated = 0;
                for (var step = 0; step < count; step++)
                {
                    var index = (start + step) % count;
                    if (!_handles.TryGetValue(_order[index], out var handle) || !handle.Active)
                    {
                        continue;
                    }

                    var worldPosition = handle.Entity.Transform.position;
                    var viewport = _camera.WorldToViewportPoint(worldPosition);
                    var onScreen = viewport.z > 0f
                                   && viewport.x >= 0f && viewport.x <= 1f
                                   && viewport.y >= 0f && viewport.y <= 1f;
                    // "distance" is measured to the camera focus in cells, not to the camera; the
                    // camera's own distance depends on zoom (32 cells at the default zoom), so a
                    // camera-distance budget would hide every icon when zoomed out.
                    var distance = Vector3.Distance(focus, worldPosition);

                    if (!WorldIconPolicy.IsWorldIconVisible(onScreen, distance, maxDistance))
                    {
                        SetCullingHidden(handle, true);
                        _board.Remove(handle.EntityId);
                        continue;
                    }

                    SetCullingHidden(handle, false);
                    if (evaluated >= budget)
                    {
                        continue;
                    }

                    evaluated++;
                    Evaluate(handle, now, ui, iconsEnabled);
                }

                _cursor = (start + Math.Max(1, evaluated)) % count;
            }
            catch (Exception exception)
            {
                // One broken camera/status call must never escape into the game loop (REQ-SAFE-02).
                _log.Error(HdbfLogCategory.Error, HdbfLogKeys.WorldIconsUnavailable,
                    "world warning icon pass failed; icons are skipped this pass", exception);
            }
        }

        /// <inheritdoc />
        public void Unload()
        {
            foreach (var pair in _handles)
            {
                try
                {
                    for (var kind = 1; kind < pair.Value.Toggles.Length; kind++)
                    {
                        pair.Value.Toggles[kind]?.Deactivate();
                    }

                    if (pair.Value.Visibility is not null)
                    {
                        pair.Value.Visibility.Show();
                    }
                }
                catch (Exception)
                {
                    // Unloading: the entity tree is going away, nothing to repair.
                }
            }

            _handles.Clear();
            _order.Clear();
            _cursor = 0;
            _nextIconPass = 0d;
            _camera = null;
            _board.Unload();
        }

        // ------------------------------------------------------------------ internals

        private void RegisterToggle(Guid entityId, StatusSubject subject, StatusToggle?[] toggles,
            FarmWarningIconKind kind, string spriteName, bool isPriority)
        {
            try
            {
                var description = BuildIconDescription(kind);
                var toggle = isPriority
                    ? StatusToggle.CreatePriorityStatusWithFloatingIcon(spriteName, description,
                        FloatingIconDelayHours)
                    : StatusToggle.CreateNormalStatusWithFloatingIcon(spriteName, description,
                        FloatingIconDelayHours);
                subject.RegisterStatus(toggle);
                toggles[(int)kind] = toggle;
            }
            catch (Exception exception)
            {
                // A missing sprite/asset only costs this one icon kind; the panel and the other
                // warnings stay. Per-farm throttle key keeps one broken building from hiding others.
                _log.Error(HdbfLogCategory.Warn,
                    HdbfLogKeys.WorldIconToggleFailed + ":" + entityId + ":" + kind,
                    "world warning icon toggle '" + kind + "' could not be registered (sprite '"
                    + spriteName + "' or vanilla status system)", exception, entityId.ToString());
            }
        }

        private string BuildIconDescription(FarmWarningIconKind kind)
        {
            // Chapter 14.4/14.5: the tooltip is a HDBF.* sentence, never a raw key; the P0/P1
            // warning text is passed as {0} and the distance setting as {1}. Localized once at
            // registration (vanilla status descriptions are set at creation too).
            try
            {
                return _loc.T(HdbfLocKeys.TooltipWorldIcon, _loc.T(WarningKeyOf(kind)),
                    _configProvider.Config.Ui.WorldIconMaxDistance);
            }
            catch (Exception)
            {
                return _loc.T(HdbfLocKeys.TooltipWorldIcon);
            }
        }

        private static string WarningKeyOf(FarmWarningIconKind kind)
        {
            switch (kind)
            {
                case FarmWarningIconKind.Shutdown:
                    return HdbfWarningKeys.Shutdown;
                case FarmWarningIconKind.NoWater:
                    return HdbfWarningKeys.NoWater;
                case FarmWarningIconKind.IrrigationLow:
                    return HdbfWarningKeys.IrrigationLow;
                case FarmWarningIconKind.NoPower:
                    return HdbfWarningKeys.NoPower;
                default:
                    return HdbfWarningKeys.AdapterFailed;
            }
        }

        private void Evaluate(IconHandle handle, double now, HDBF.Core.Config.UiSettings ui,
            bool iconsEnabled)
        {
            if (!_query.TryGetSnapshot(handle.EntityId, out var snapshot))
            {
                return;
            }

            var subject = FarmWarningSubject.FromSnapshot(snapshot);
            var summary = FarmWarningSummary.FromSubject(subject);
            handle.Summary = summary;
            // Aggregation is independent of the icon switch: always publish the subject.
            _board.Report(subject);

            if (!iconsEnabled || handle.Subject is null)
            {
                // Icons are off (or this entity has no vanilla status pipeline): make sure no stale
                // toggle stays active so the game never shows a status this pass did not decide.
                for (var kind = 1; kind < handle.Toggles.Length; kind++)
                {
                    var toggle = handle.Toggles[kind];
                    if (toggle is not null && toggle.IsActive)
                    {
                        toggle.Deactivate();
                    }
                }

                handle.Blinking = false;
                handle.BlinkHidden = false;
                handle.VisibilityHidden = false;
                return;
            }

            var targetKind = summary.HasWorldIcon ? summary.IconKind : FarmWarningIconKind.None;
            for (var kind = 1; kind < handle.Toggles.Length; kind++)
            {
                var toggle = handle.Toggles[kind];
                if (toggle is null)
                {
                    continue;
                }

                var wanted = kind == (int)targetKind;
                if (toggle.IsActive != wanted)
                {
                    if (wanted)
                    {
                        toggle.Activate();
                    }
                    else
                    {
                        toggle.Deactivate();
                    }
                }
            }

            var blink = WorldIconPolicy.ShouldBlink(summary.HighestSeverity, ui.WorldIconBlink);
            handle.Blinking = blink && summary.HasWorldIcon;
            handle.BlinkHidden = handle.Blinking
                                 && !WorldIconPolicy.IsBlinkOn(now, WorldIconPolicy.DefaultBlinkHz);
            ApplyVisibility(handle);
        }

        private void SetCullingHidden(IconHandle handle, bool hidden)
        {
            if (handle.CullingHidden == hidden)
            {
                return;
            }

            handle.CullingHidden = hidden;
            ApplyVisibility(handle);
        }

        private void ApplyVisibility(IconHandle handle)
        {
            if (handle.Visibility is null)
            {
                return;
            }

            var hidden = handle.CullingHidden || handle.BlinkHidden;
            if (handle.VisibilityHidden == hidden)
            {
                return;
            }

            try
            {
                if (hidden)
                {
                    handle.Visibility.Hide();
                }
                else
                {
                    handle.Visibility.Show();
                }

                handle.VisibilityHidden = hidden;
            }
            catch (Exception exception)
            {
                _log.Error(HdbfLogCategory.Warn,
                    HdbfLogKeys.WorldIconsUnavailable + ":" + handle.EntityId,
                    "world warning icon visibility toggle failed", exception,
                    handle.EntityId.ToString());
            }
        }

        /// <summary>
        /// Resolves the game's Camera once. <c>CameraService.Transform</c> exists only after the
        /// camera singleton's Load(); the first pass retries, then the cached reference is used
        /// (no Find/GetComponent in the update path).
        /// </summary>
        private void EnsureCamera()
        {
            if (_camera is not null)
            {
                return;
            }

            try
            {
                var transform = _cameraService.Transform;
                if (transform is null)
                {
                    return;
                }

                _camera = transform.GetComponent<Camera>();
            }
            catch (Exception)
            {
                _camera = null;
            }
        }

        private sealed class IconHandle
        {
            public IconHandle(Guid entityId, BaseComponent entity, StatusSubject? subject,
                StatusIconCycler? cycler, StatusVisibilityToggle? visibility, StatusToggle?[] toggles)
            {
                EntityId = entityId;
                Entity = entity;
                Subject = subject;
                Cycler = cycler;
                Visibility = visibility;
                Toggles = toggles;
            }

            public Guid EntityId { get; }

            public BaseComponent Entity { get; }

            /// <summary>Null when the entity lacks the vanilla status pipeline (aggregation only).</summary>
            public StatusSubject? Subject { get; }

            /// <summary>Null when the entity lacks the vanilla status pipeline (aggregation only).</summary>
            public StatusIconCycler? Cycler { get; }

            /// <summary>Null when the entity lacks the vanilla status pipeline (aggregation only).</summary>
            public StatusVisibilityToggle? Visibility { get; }

            public StatusToggle?[] Toggles { get; }

            /// <summary>False between OnExitFinishedState and the next OnEnterFinishedState.</summary>
            public bool Active { get; set; } = true;

            public FarmWarningSummary Summary { get; set; } = new();

            public bool CullingHidden { get; set; }

            public bool BlinkHidden { get; set; }

            public bool Blinking { get; set; }

            public bool VisibilityHidden { get; set; }
        }
    }
}
