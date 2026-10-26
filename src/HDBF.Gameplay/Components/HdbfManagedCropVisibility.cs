using System;
using Timberborn.BaseComponentSystem;
using Timberborn.EntitySystem;
using UnityEngine;

namespace HDBF.Gameplay.Components
{
    /// <summary>
    /// Renderer-only crop visual helper of BUILD-05: the real vanilla <c>Crop</c>/<c>Growable</c>
    /// entities keep growing, yielding and being harvested on this farm's managed plots, but their
    /// models must not be visible from the ground (user design decision 2026-10-25).
    ///
    /// Hiding does exactly what vanilla
    /// <c>Timberborn.NaturalResourcesModelSystem.NaturalResourceModel.SetVisibility(false)</c> does:
    /// every <see cref="MeshRenderer"/> under the crop entity is disabled. Nothing else is touched —
    /// no <c>GameObject.SetActive(false)</c> (that would stop the <c>Growable</c> ticks),
    /// no collider disabling and no <c>BlockObjectModel.HideFullModelPermanently</c> (that also
    /// disables colliders, i.e. more than "visual only").
    ///
    /// Vanilla growth-stage switching (<c>Growable.ShowSeedlingModel</c>/<c>ShowMatureModel</c>)
    /// only calls <c>GameObject.SetActive</c> on the lifecycle model children and never re-enables
    /// <see cref="Renderer.enabled"/>, so a disabled renderer stays disabled across
    /// seedling → mature → dried/dead transitions. Enabling the same renderer set is therefore the
    /// exact inverse and is what every restore path calls.
    ///
    /// All methods are failure-tolerant on purpose: a destroyed or partially initialised crop is
    /// reported through <c>failure</c> (never an exception into the game loop), so one broken cell
    /// can never abort a sweep or a frame (REQ-SAFE-01/02).
    /// </summary>
    internal static class HdbfCropVisuals
    {
        /// <summary>
        /// Disables every model renderer of the crop entity. A crop whose <c>GameObject</c> is
        /// already gone (harvested/destroyed) is treated as a successful no-op.
        /// </summary>
        internal static bool TryHide(BaseComponent crop, out Exception? failure)
        {
            return TrySetRenderersEnabled(crop, false, out failure);
        }

        /// <summary>
        /// Re-enables every model renderer of the crop entity (vanilla visible crop again). Same
        /// set of renderers as <see cref="TryHide"/>; a destroyed crop is a successful no-op.
        /// </summary>
        internal static bool TryRestore(BaseComponent crop, out Exception? failure)
        {
            return TrySetRenderersEnabled(crop, true, out failure);
        }

        /// <summary>
        /// Reads the crop entity identity used as the cache key by
        /// <see cref="HDBF.Core.Plots.ManagedCropVisibilityState.Observe"/>:
        /// <c>EntityComponent.EntityId</c>. Returns false for a missing component, a
        /// <see cref="Guid.Empty"/> id or any access failure; the controller then uses its own
        /// synthetic identity.
        /// </summary>
        internal static bool TryGetCropIdentity(BaseComponent crop, out Guid entityId)
        {
            entityId = Guid.Empty;
            try
            {
                var entityComponent = crop.GetComponent<EntityComponent>();
                if (entityComponent is null)
                {
                    return false;
                }

                entityId = entityComponent.EntityId;
                return entityId != Guid.Empty;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool TrySetRenderersEnabled(BaseComponent crop, bool enabled, out Exception? failure)
        {
            failure = null;
            try
            {
                var cropGameObject = crop.GameObject;
                if (cropGameObject == null)
                {
                    // The entity was already removed (e.g. the crop was harvested between the
                    // observation and this call): nothing to hide/restore, so this is a success.
                    return true;
                }

                // Include inactive children: vanilla stage switching toggles ActiveSelf on the
                // lifecycle model roots, and the ground view must hide every stage model.
                var renderers = cropGameObject.GetComponentsInChildren<MeshRenderer>(true);
                for (var i = 0; i < renderers.Length; i++)
                {
                    var renderer = renderers[i];
                    if (renderer is not null)
                    {
                        renderer.enabled = enabled;
                    }
                }

                return true;
            }
            catch (Exception exception)
            {
                failure = exception;
                return false;
            }
        }
    }
}
