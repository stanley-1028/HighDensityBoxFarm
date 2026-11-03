// ============================================================================
// HDBF v2 | Layer: Gameplay/Components | Owner: V2-GAMEPLAY-02
// Purpose: World-icon pass scheduling/unloading plus vanilla status-toggle registration and
//          icon tooltip text. v2 draws only four icon classes and refreshes at most once per
//          second (section 19.11/19.13). Split from the service core for the 300-line gate.
// Requirements: REQ-V2-BAL-03, REQ-V2-WATER-03, REQ-V2-SAFE-01, REQ-V2-READ-02
// ============================================================================
using System;
using HDBF.Core;
using HDBF.Core.Indoor;
using HDBF.Core.Logging;
using Timberborn.StatusSystem;
using UnityEngine;

namespace HDBF.Gameplay.Components
{
    public sealed partial class HdbfWorldIconService
    {
        /// <summary>True once every registered toggle was settled after the icons were switched off.</summary>
        private bool _iconsDisabledSettled;

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
                var iconsEnabled = ui.WorldIcons;

                if (!iconsEnabled)
                {
                    // L-14: the whole feature is off, so stop paying for the per-farm camera math and
                    // viewport tests every frame. The registered toggles are settled exactly once
                    // (no icon may survive the switch) and the next enabled pass starts immediately.
                    if (!_iconsDisabledSettled)
                    {
                        foreach (var pair in _handles)
                        {
                            DeactivateAll(pair.Value);
                        }

                        _iconsDisabledSettled = true;
                        _nextIconPass = 0d;
                    }

                    return;
                }

                _iconsDisabledSettled = false;
                EnsureCamera();
                if (_camera is null)
                {
                    // The camera singleton may not have created its Camera yet in the first frames.
                    return;
                }

                var now = Time.unscaledTime;
                var interval = ui.WorldIconIntervalSeconds > 0f
                    ? ui.WorldIconIntervalSeconds
                    : IndoorIconPolicy.DefaultIconIntervalSeconds;
                // v2 budget: world icons update at most once per second.
                if (interval < MinIconIntervalSeconds)
                {
                    interval = MinIconIntervalSeconds;
                }

                if (!IndoorIconPolicy.IsDue(now, _nextIconPass, interval))
                {
                    return;
                }

                _nextIconPass = now + interval;
                var maxDistance = ui.WorldIconMaxDistance > 0
                    ? ui.WorldIconMaxDistance
                    : IndoorIconPolicy.DefaultMaxDistance;
                var budget = ui.WorldIconFarmsPerTick > 0
                    ? ui.WorldIconFarmsPerTick
                    : IndoorIconPolicy.DefaultFarmsPerPass;
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

                    if (!IndoorIconPolicy.IsWorldIconVisible(onScreen, distance, maxDistance))
                    {
                        SetCullingHidden(handle, true);
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
            _iconsDisabledSettled = false;
            _camera = null;
        }

        // ------------------------------------------------------------------ internals

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

        /// <summary>
        /// Tooltip sentence of one icon class (L-17). Vanilla offers **no writable description**
        /// after registration: <c>StatusSpecification</c> is immutable (private constructor, get-only
        /// properties, only the static <c>Create*</c> factories), <c>StatusInstance.StatusDescription</c>
        /// is get-only and <c>StatusSubject</c> has no unregister/update call, so re-registering would
        /// duplicate the status entries. The sentence is therefore fixed at registration and carries
        /// only the already-localized warning text - no dynamic <c>ui.worldIconMaxDistance</c> value,
        /// so a changed distance setting can never leave a false "hidden beyond N cells" tooltip.
        /// A language switch is picked up on the next registration (finished-state toggle / reload);
        /// the panel itself refreshes immediately through the localization fingerprint.
        /// </summary>
        private string BuildIconDescription(IndoorWarningIconKind kind)
        {
            try
            {
                return _loc.T(IndoorWarningMap.WarningKeyOf(kind));
            }
            catch (Exception)
            {
                // A broken localization provider must not break icon registration (REQ-V2-SAFE-01).
                return string.Empty;
            }
        }
    }
}
