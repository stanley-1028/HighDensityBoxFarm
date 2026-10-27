// ============================================================================
// HDBF v2 | Layer: UI/Panels | Owner: V2-UI-01
// Purpose: Lazy cache of the vanilla sprites the panel rows use. The read model only carries a
//          semantic icon key (HdbfIndoorPanelStyle.IconResource); this class resolves it once with
//          UnityEngine.Resources and never throws when a sprite is missing (the row then renders
//          without its icon). One instance per panel fragment, main thread only.
// Requirements: REQ-V2-UI-01/02, REQ-V2-SAFE-01, REQ-V2-READ-04
// ============================================================================
using System;
using System.Collections.Generic;
using UnityEngine;

namespace HDBF.UI.Panels
{
    /// <summary>
    /// Sprite cache of the panel. Vanilla sprites are Resources under <c>UI/Images/...</c>; a key
    /// without a sprite resolves to null and the renderer simply omits the Image element.
    /// </summary>
    public sealed class HdbfPanelIcons
    {
        private readonly Dictionary<string, Sprite?> _cache = new(StringComparer.Ordinal);

        /// <summary>
        /// Resolves a semantic icon key once and caches the result. Missing/broken resources become
        /// null instead of throwing (REQ-V2-SAFE-01).
        /// </summary>
        /// <param name="iconKey">Semantic icon key from the read model, e.g. "tank".</param>
        /// <returns>The vanilla sprite, or null when the key has no icon.</returns>
        public Sprite? Get(string? iconKey)
        {
            if (string.IsNullOrEmpty(iconKey))
            {
                return null;
            }

            var path = HdbfIndoorPanelStyle.IconResource(iconKey!);
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            if (_cache.TryGetValue(path, out var cached))
            {
                return cached;
            }

            Sprite? sprite = null;
            try
            {
                sprite = Resources.Load<Sprite>(path);
            }
            catch (Exception)
            {
                // A missing sprite is not a panel failure: the row renders without the icon.
            }

            _cache[path] = sprite;
            return sprite;
        }
    }
}
