// ============================================================================
// HDBF v2 | Layer: Gameplay/Components | Owner: V2-GAMEPLAY-02
// Purpose: World-icon pass scheduling/unloading plus vanilla status-toggle registration and
//          icon tooltip text. v2 draws only four icon classes and refreshes at most once per
//          second (section 19.11/19.13). Split from the service core for the 300-line gate.
// Requirements: REQ-V2-BAL-03, REQ-V2-WATER-03, REQ-V2-SAFE-01, REQ-V2-READ-02
// ============================================================================
using System;
using System.Collections.Generic;
using HDBF.Core;
using HDBF.Core.Abstractions;
using HDBF.Core.Indoor;
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
    public sealed partial class HdbfWorldIconService
    {
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
                // v2 budget: world icons update at most once per second (the configured interval may
                // be smaller for the retained v1 warning framework; the icon pass itself is capped).
                if (interval < MinIconIntervalSeconds)
                {
                    interval = MinIconIntervalSeconds;
                }

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
                // One broken camera/status call must never escape into the game loop (REQ-V2-SAFE-02).
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

        /// <summary>Localization key of the world-icon hover tooltip.</summary>
        private const string TooltipWorldIcon = "HDBF.Tooltip.WorldIcon";

        private void RegisterToggle(Guid entityId, StatusSubject subject, StatusToggle?[] toggles,
            IndoorWarningIconKind kind, string spriteName, bool isPriority)
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
                toggles[IndoorWarningMap.SlotOf(kind)] = toggle;
            }
            catch (Exception exception)
            {
                // A missing sprite/asset only costs this one icon class; the panel and the other
                // warnings stay. Per-farm throttle key keeps one broken building from hiding others.
                _log.Error(HdbfLogCategory.Warn,
                    HdbfLogKeys.WorldIconToggleFailed + ":" + entityId + ":" + kind,
                    "world warning icon toggle '" + kind + "' could not be registered (sprite '"
                    + spriteName + "' or vanilla status system)", exception, entityId.ToString());
            }
        }

        private string BuildIconDescription(IndoorWarningIconKind kind)
        {
            // Chapter 14.4/14.5: the tooltip is a HDBF.* sentence, never a raw key; the warning
            // text is passed as {0} and the distance setting as {1}. Localized once at registration.
            try
            {
                return _loc.T(TooltipWorldIcon, _loc.T(WarningKeyOf(kind)),
                    _configProvider.Config.Ui.WorldIconMaxDistance);
            }
            catch (Exception)
            {
                return _loc.T(TooltipWorldIcon);
            }
        }

        private static string WarningKeyOf(IndoorWarningIconKind kind)
        {
            switch (kind)
            {
                case IndoorWarningIconKind.Water:
                    return HdbfWarningKeys.NoWater;
                case IndoorWarningIconKind.Maintenance:
                    return HdbfWarningKeys.MaintenanceOverdue;
                case IndoorWarningIconKind.NoPower:
                    return HdbfWarningKeys.NoPower;
                default:
                    return IndoorWarningMap.EntranceBlockedKey;
            }
        }
    }
}
