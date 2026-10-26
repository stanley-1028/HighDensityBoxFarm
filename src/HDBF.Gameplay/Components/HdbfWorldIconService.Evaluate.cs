// ============================================================================
// HDBF v2 | Layer: Gameplay/Components | Owner: V2-GAMEPLAY-02
// Purpose: Per-farm icon evaluation, culling/blink visibility and camera resolution of the
//          world-warning icon coordinator, plus its private handle state. The icon class is
//          derived from the retained warning rows through the pure Core IndoorWarningMap so
//          only four v2 visuals can ever appear (section 19.11).
// Requirements: REQ-V2-BAL-03, REQ-V2-UI-02, REQ-V2-SAFE-01, REQ-V2-READ-02
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
        private void Evaluate(IconHandle handle, double now, HDBF.Core.Config.UiSettings ui,
            bool iconsEnabled)
        {
            if (!_query.TryGetSnapshot(handle.EntityId, out var snapshot))
            {
                return;
            }

            var subject = FarmWarningSubject.FromSnapshot(snapshot);
            // Aggregation is independent of the icon switch: always publish the subject.
            _board.Report(subject);
            var target = HighestIconKind(subject, out var severity);
            handle.TargetKind = target;

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

            for (var kind = 1; kind < handle.Toggles.Length; kind++)
            {
                var toggle = handle.Toggles[kind];
                if (toggle is null)
                {
                    continue;
                }

                var wanted = kind == IndoorWarningMap.SlotOf(target);
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

            var blink = WorldIconPolicy.ShouldBlink(severity, ui.WorldIconBlink)
                        && target != IndoorWarningIconKind.None;
            handle.Blinking = blink;
            handle.BlinkHidden = blink && !WorldIconPolicy.IsBlinkOn(now, WorldIconPolicy.DefaultBlinkHz);
            ApplyVisibility(handle);
        }

        /// <summary>
        /// First (highest-priority) warning row whose key belongs to one of the four v2 icon
        /// classes. The shared mapper already sorts rows P0 first, then by key, so the first hit is
        /// the highest-severity icon. Only the four classes can be returned.
        /// </summary>
        private static IndoorWarningIconKind HighestIconKind(FarmWarningSubject? subject,
            out FarmWarningSeverity severity)
        {
            severity = FarmWarningSeverity.Info;
            if (subject?.Warnings is null)
            {
                return IndoorWarningIconKind.None;
            }

            for (var index = 0; index < subject.Warnings.Count; index++)
            {
                var warning = subject.Warnings[index];
                if (warning is null)
                {
                    continue;
                }

                var kind = IndoorWarningMap.IconKindOf(warning.CodeKey);
                if (kind == IndoorWarningIconKind.None)
                {
                    continue;
                }

                severity = warning.Severity;
                return kind;
            }

            return IndoorWarningIconKind.None;
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

            /// <summary>Currently targeted v2 icon class (one of the four, or None).</summary>
            public IndoorWarningIconKind TargetKind { get; set; } = IndoorWarningIconKind.None;

            public bool CullingHidden { get; set; }

            public bool BlinkHidden { get; set; }

            public bool Blinking { get; set; }

            public bool VisibilityHidden { get; set; }
        }
    }
}
