#!/usr/bin/env python3
"""HDBF Box Farm v2 - real vanilla-material texture patch map generator (ART-05).

Why this exists (user bug 2026-10-27, ``art/references/models/texture-bug-2026-10-27.png``)
--------------------------------------------------------------------------------------------
The shipped v2 models use vanilla ``MaterialCollection`` material asset names and
the ART-04 "flat patch" UV scheme.  ART-04 wrongly assumed every vanilla material
samples ONE shared 2048x2048 ART-01 atlas, so its patch rectangles were expressed
in that fake atlas' pixel space.  In the real game each MaterialCollection
material resolves to its **own 1024x1024 ``_D`` albedo PNG** plus a material
``_Color`` tint (verified from the installed game's ``resources.assets`` with
AssetStudio + a small IL-probe against ``Timberborn.PrefabOptimization`` /
``Timberborn.TimbermeshMaterials``; see ``docs/API_FACTS.md`` and the ART-05
report).  A fake-atlas UV therefore landed on arbitrary content of the real
texture: every face of a material read a different sub-rect (the checkerboard the
user saw), and the top tier's shell sampled ``PaintedMetal``'s yellow/black
hazard stripe texture (the yellow/black box).

What this script produces
-------------------------
``art/material_texture_map.json`` - the single source of truth consumed by
``art/material_patches.py`` (UV assignment), ``art/preview_materials.py``
(preview/icon rendering), ``art/validate_models.py`` and
``art/validate_timbermesh.py`` (validation) and the L1 tests:

    material asset name -> real _D texture + material _Color tint + ONE fixed
    low-variance, visually-correct pixel rect ("patch") inside that texture.

Every face of a material is mapped into that same patch by
``gen_models_v2.assign_flat_patch_uvs`` (per-face sub-rects inside the patch at
the contract texel density), so all faces read the same region of the same real
texture - no patchwork, no hazard stripes.

Ground truth (do not "improve" from memory)
-------------------------------------------
``MATERIAL_TEXTURES`` below is not guessed: it is the ``_MainTex`` / ``_Color``
pair read out of the game's real ``Material`` assets (see
``material_source_provenance`` in the JSON).  Examples that a name-only guess
would get wrong:

* ``BaseWood_LightBrown.Folktails`` = ``BaseWood_1K_D_White`` * tint
  (0.6604, 0.5552, 0.4081) - not a "light brown" texture file;
* ``IrregularPlanks_LightBrown.Folktails`` = ``IrregularPlanks_D_White`` * tint;
* ``IrregularPlanks_MetalForged.IronTeeth`` = ``PlanksWithMetal_D``;
* ``Plaster_Orange.IronTeeth`` = ``Plaster_D_White.IronTeeth`` * orange tint.

Patch choice
------------
For every texture the script slides a square window over the decoded PNG and
picks the **largest** window that satisfies

* ``std <= std_max`` (per-material class threshold; ``std`` = worst RGB channel
  population std over the window, raw sRGB 0..255 bytes - exactly what
  ``art/png_stats.py`` recomputes in the validators), and
* ``colordist(window mean, target) <= 40`` where ``target`` is the material's
  representative colour (median-ish tone taken from the texture itself, chosen
  per material after visual inspection).  This keeps a *uniform* patch from
  being a shadow/cover/repair-plate corner: the patch must look like the
  material.

Window score is ``std + colordist`` (both in 0..255 units).  Ties are broken by
``(score, y, x)`` so the result is fully deterministic.  Explicit
``EXCLUDE_RECTS`` reject decorative/hazard/brand art (faction banners/logos,
canvas margins, repair plates, painted grilles) that must never be used as a
"material patch"; additionally a hazard guard rejects any window whose pixels are
warning-yellow or saturated faction-banner colours.  ``PaintedMetal.*`` is not
usable at all (its whole texture is yellow/black hazard stripes) and is recorded
under ``unused_materials`` with a hazard reference rect used by the validators'
negative controls.

Patch sizes stay in the contract range (128..512 px, see
``PATCH_SIZE_RANGE``); 128 px is the minimum that still fits the largest 3x3
grid face at the contract density (``PATCH_TEXELS_PER_GRID`` in
``material_patches.py``), so every face's sub-rect fits with its 4 px guard band.

Determinism / reproducibility
-----------------------------
* No timestamps, no locale/random/filesystem-order input.
* Textures are READ-ONLY inputs: they are decoded, hashed (sha256) and recorded,
  never copied into the repository or the release package.
* ``--verify`` re-derives the whole JSON and fails on any difference; the
  validators additionally re-check the recorded statistics from the PNG bytes.

Output
------
    art/material_texture_map.json     (committed)

QA (optional, not shipped):

    --swatches DIR    writes one PNG per material: full texture with the chosen
                      patch outlined + the enlarged patch crop.

Run:

    python art/material_texture_analysis.py                 # regenerate map
    python art/material_texture_analysis.py --verify        # fail if not reproducible
    python art/material_texture_analysis.py --swatches .tmp-art05/swatches

Requires numpy (present in the Blender 4.2 Python and in the dev Python; the
validators do NOT need this script - they use the small pure decoder in
``art/png_stats.py``).
"""

from __future__ import annotations

import argparse
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
if HERE not in sys.path:
    sys.path.insert(0, HERE)

import material_uv_policy as UVP

MAP_PATH = os.path.join(HERE, "material_texture_map.json")

SCHEMA = "hdbf.material_texture_map/v1"
GENERATOR = "art/material_texture_analysis.py"

DEFAULT_TEXTURE_ROOTS = {
    "vanilla-tex-all": os.path.join(os.path.expanduser("~"), ".hdbf", "sdk",
                                    "vanilla-tex-all"),
    "vanilla-tex-extra": os.path.join(os.path.expanduser("~"), ".hdbf", "sdk",
                                      "vanilla-tex-extra"),
}
TEXTURE_ROOT_ENV = {
    "vanilla-tex-all": "HDBF_VANILLA_TEX_ALL",
    "vanilla-tex-extra": "HDBF_VANILLA_TEX_EXTRA",
}

PROVENANCE = (
    "Ground truth read from the installed game's Material assets "
    "(Timberborn_Data/resources.assets, 660 assemblies / 1536 Texture2D): each "
    "material's m_SavedProperties._MainTex (Texture2D name) and _Color tint, "
    "extracted 2026-10-27 with AssetStudioModCLI (materials enumerated via a "
    "small AssetStudio probe) and cross-checked against the runtime UV contract "
    "in Timberborn.PrefabOptimization.AutoAtlaser / AutoAtlasingPrefabOptimizer "
    "IL: a mesh UV is remapped as uv_atlas = uv_material * UVScale + UVOffset "
    "with UVScale = 1/sizeMultiplier, so normalized (u,v) inside the material's "
    "own _D texture is preserved (AutoAtlas or direct material, identical "
    "sampling). The _Color tint multiplies the sampled albedo. ART-07 addition "
    "(2026-10-27): AutoAtlaser.GenerateAutoAtlasFragments builds one atlas of "
    "1024*sizeMultiplier and CombineFragments copies every fragment at its "
    "native 1024x1024 resolution, so a UV component > 1 leaves the material's "
    "own cell and samples another material - hence the ART-07 world UVs never "
    "leave the material canvas (see art/ART07_UV_TILING.md)."
)

UV_CONVENTION = (
    "pixel_rect = [x0, y0, x1, y1], TOP-LEFT pixel origin, exclusive x1/y1 "
    "(same as art/atlas_layout.REGIONS and art/png_stats.py). "
    "uv_rect = [u0, v0, u1, v1] with u0=x0/W, u1=x1/W, v0=1-y1/H, v1=1-y0/H "
    "(bottom-left UV origin, i.e. art/atlas_layout.px_rect_to_uv flips v)."
)

#: contract patch size range in pixels (requirement ART-05: 128..512 equivalent).
PATCH_SIZE_RANGE = (128, 512)

#: per patch-class std budget (raw sRGB 0..255, worst RGB channel std).
#: These are BUDGETS, not fitted values: most patches land far below them.  The
#: "wood"/"details"/"forged_planks" classes carry painted/grainy vanilla textures
#: whose entire surface has high frequency detail; their budget is set from the
#: texture family's inherent flat-window minimum (documented per material in
#: ``patch.reason``) and a hard global cap (see HARD_STD_CAP) guarantees a
#: hazard-stripe or banner region can never pass.
STD_MAX_BY_CLASS = {
    "wood": 18.0,
    "metal": 14.0,
    "glass": 12.0,
    "plaster": 10.0,
    "details": 26.0,
    "moss": 16.0,
    "roof_planks": 16.0,
    "forged_planks": 27.0,
}
DEFAULT_STD_MAX = 18.0
#: absolute upper bound no patch may ever exceed (hazard/Banner windows score
#: ~45..60, so the negative control in the validators is rejected by this cap).
HARD_STD_CAP = 30.0

#: maximum allowed colour distance between window mean and the material target
#: (default; a hint may override it for atlases where the representative colour
#: sits in a narrow band, e.g. the glass-pane area of WindowsAtlas).  Keeping
#: this tight is what makes the patch *representative* rather than just dark and
#: flat: a shadow/cover corner that happens to have low variance is rejected
#: because its mean is far from the texture's own representative tone.
MAX_COLOR_DISTANCE = 18.0

#: Cross-faction "shared UV window" groups.
#:
#: The construction-stage model is ONE shared source mesh that is exported for
#: both factions (same geometry + same UVs); only the material names differ per
#: faction (Folktails vs IronTeeth materials).  Every UV vertex must therefore
#: lie inside BOTH factions' patch for the corresponding slot.  Instead of
#: duplicating the construction source model, these texture pairs are searched
#: JOINTLY and receive the same pixel rect, so one UV set satisfies both
#: factions.  Each group lists the texture pair and the faction slots it serves.
CONSTRUCTION_SHARED_GROUPS = {
    "construction_wood": dict(
        textures=("BaseWood_1K_D_Brown.png", "RoofPlanks_D.png"),
        reason="construction stage shares one UV set across factions: "
               "BaseWood_Brown.Folktails (wood slot, FT) and RoofPlanks.IronTeeth "
               "(wood slot, IT) must sample the same normalized window; both are "
               "representative dark raw-timber tones and both stay under their "
               "class std budget inside this window",
    ),
    "construction_metal": dict(
        textures=("BaseMetal_D1.png", "BaseMetal_D2.png"),
        reason="construction stage shares one UV set across factions: "
               "BaseMetal.Folktails and BaseMetal.IronTeeth (metal slot) must "
               "sample the same normalized window; the two vanilla sheet-metal "
               "textures have the same atlas layout, so one plain-sheet window "
               "is flat and representative in both",
    ),
}

#: texture file -> construction shared group name (derived from the table above)
SHARED_GROUP_OF_TEXTURE = {
    texture: group
    for group, info in CONSTRUCTION_SHARED_GROUPS.items()
    for texture in info["textures"]
}

# --------------------------------------------------------------------------- #
# ground truth: vanilla material asset -> real _D texture / _Color tint
# --------------------------------------------------------------------------- #
#: material name -> (texture file, texture_source root key, _Color tint rgb)
MATERIAL_TEXTURES = {
    # ---- Folktails -----------------------------------------------------
    "BaseWood_Brown.Folktails":
        ("BaseWood_1K_D_Brown.png", "vanilla-tex-all", (1.0, 1.0, 1.0)),
    "BaseWood_LightBrown.Folktails":
        ("BaseWood_1K_D_White.png", "vanilla-tex-all", (0.6604, 0.5552, 0.4081)),
    "BaseWood_White.Folktails":
        ("BaseWood_1K_D_White.png", "vanilla-tex-all", (0.8868, 0.8641, 0.8324)),
    "IrregularPlanks_Brown.Folktails":
        ("IrregularPlanks_D_Brown.png", "vanilla-tex-all", (1.0, 1.0, 1.0)),
    "BaseMetal.Folktails":
        ("BaseMetal_D1.png", "vanilla-tex-extra", (1.0, 1.0, 1.0)),
    "WindowsAtlas.Folktails":
        ("WindowsAtlas_D.Folktails.png", "vanilla-tex-extra", (1.0, 1.0, 1.0)),
    "Plaster_White.Folktails":
        ("Plaster_D_White.Folktails.png", "vanilla-tex-extra", (1.0, 1.0, 1.0)),
    "Details.Folktails":
        ("Details_D.Folktails.png", "vanilla-tex-extra", (1.0, 1.0, 1.0)),
    "Moss.Folktails":
        ("Moss_D.Folktails.png", "vanilla-tex-all", (1.0, 1.0, 1.0)),
    # ---- IronTeeth -----------------------------------------------------
    "BaseWood_DarkBrown.IronTeeth":
        ("BaseWood_1K_D_Grey.png", "vanilla-tex-all", (0.3113, 0.2673, 0.2071)),
    "BaseWood_Grey.IronTeeth":
        ("BaseWood_1K_D_Grey.png", "vanilla-tex-all", (0.6226, 0.5796, 0.5257)),
    "IrregularPlanks_MetalForged.IronTeeth":
        ("PlanksWithMetal_D.png", "vanilla-tex-all", (1.0, 1.0, 1.0)),
    "BaseMetal.IronTeeth":
        ("BaseMetal_D2.png", "vanilla-tex-extra", (1.0, 1.0, 1.0)),
    "WindowsAtlas.IronTeeth":
        ("WindowsAtlas_D.IronTeeth.png", "vanilla-tex-extra", (1.0, 1.0, 1.0)),
    "RoofPlanks.IronTeeth":
        ("RoofPlanks_D.png", "vanilla-tex-extra", (1.0, 1.0, 1.0)),
    "Plaster_Orange.IronTeeth":
        ("Plaster_D_White.IronTeeth.png", "vanilla-tex-extra",
         (0.8353, 0.4824, 0.2588)),
    "Details.IronTeeth":
        ("Details_D.IronTeeth.png", "vanilla-tex-extra", (1.0, 1.0, 1.0)),
}

#: materials that must never be used on a large shell (yellow/black hazard
#: stripe albedo).  Recorded in the JSON with a reference rect for the
#: validators' negative controls; they may stay in MATERIAL_WHITELIST.
UNUSED_MATERIALS = {
    "PaintedMetal.Folktails": {
        "texture": "PaintedMetal_D.Folktails.png",
        "texture_source": "vanilla-tex-all",
        "reason": "whole albedo is yellow/black hazard stripe paint - the "
                  "texture that made the top tier look like a warning sign",
        "policy": "never use on a shell or any large face (ART-05 user bug)",
        "hazard_reference_pixel_rect": [64, 64, 320, 320],
    },
    "PaintedMetal.IronTeeth": {
        "texture": "PaintedMetal_D.IronTeeth.png",
        "texture_source": "vanilla-tex-all",
        "reason": "whole albedo is blue/black hazard stripe paint",
        "policy": "never use on a shell or any large face (ART-05 user bug)",
        "hazard_reference_pixel_rect": [64, 64, 320, 320],
    },
}

# --------------------------------------------------------------------------- #
# per-texture patch hints (art direction; the search does the pixel work)
# --------------------------------------------------------------------------- #
#: texture file -> hint dict.  ``target`` is the representative RGB the patch
#: mean must be close to (chosen from the actual texture, see the ART-05 report);
#: ``sizes`` are the candidate window sizes, largest first (a larger patch wins
#: when it still satisfies the std/colour budgets); ``exclude`` are decoration /
#: repair-plate / canvas-margin rectangles that must never be sampled.
TEXTURE_HINTS = {
    "BaseWood_1K_D_Brown.png": dict(
        patch_class="wood",
        target=(56.0, 43.0, 30.0),
        sizes=(256, 224, 192, 160, 144, 128),
        exclude=[(700, 700, 1024, 1024)],  # light repair plate, bottom-right
        reason="dark-brown timber grain, mid-plank window; avoids the bottom-right "
               "repair plate, the plank seams and the white canvas margins",
    ),
    "BaseWood_1K_D_White.png": dict(
        patch_class="wood",
        target=(152.0, 139.0, 121.0),
        sizes=(192, 160, 144, 128),
        exclude=[(440, 760, 1024, 1024)],  # blank canvas + knots + repair plate
        reason="whitewashed timber grain (vanilla tint supplies the pale/warm "
               "variant); avoids the blank canvas corner, knots and plates",
    ),
    "BaseWood_1K_D_Grey.png": dict(
        patch_class="wood",
        target=(138.0, 133.0, 124.0),
        sizes=(160, 144, 128),
        exclude=[(440, 760, 1024, 1024)],
        reason="grey timber grain; vanilla _Color tints it dark-brown "
               "(DarkBrown) or grey (Grey); avoids canvas/knots/plate regions",
    ),
    "IrregularPlanks_D_Brown.png": dict(
        patch_class="wood",
        target=(52.0, 39.0, 27.0),
        sizes=(256, 224, 192, 160, 144, 128),
        exclude=[],
        reason="dark irregular plank field, single-plank tone",
    ),
    "PlanksWithMetal_D.png": dict(
        patch_class="forged_planks",
        target=(95.0, 74.0, 58.0),
        max_color_distance=30.0,
        sizes=(224, 192, 160, 144, 128),
        exclude=[],
        reason="copper/forged plank field between the forged steel bands "
               "(small detail/accent material on the advanced/top tiers); the "
               "class budget 27 is the texture's inherent flat-window minimum "
               "for painted planks + steel bands",
    ),
    "BaseMetal_D1.png": dict(
        patch_class="metal",
        target=(112.0, 100.0, 78.0),
        max_color_distance=22.0,
        sizes=(320, 256, 224, 192),
        exclude=[
            (760, 200, 1024, 520),   # painted ring / pipe motif
            (560, 500, 900, 780),    # triangle + honeycomb painted grilles
            (0, 500, 520, 1024),     # corrugated sheet + ribs
            (500, 760, 1024, 1024),  # painted pattern tiles
        ],
        reason="plain painted sheet-metal area (avoids the painted ring, grille, "
               "corrugated and pattern tiles)",
    ),
    "BaseMetal_D2.png": dict(
        patch_class="metal",
        target=(112.0, 107.0, 107.0),
        max_color_distance=22.0,
        sizes=(320, 256, 224, 192),
        exclude=[
            (760, 200, 1024, 520),
            (560, 500, 900, 780),
            (0, 500, 520, 1024),
            (500, 760, 1024, 1024),
        ],
        reason="plain grey-blue sheet-metal area (IronTeeth industrial steel; "
               "avoids painted grille/ring/corrugated tiles)",
    ),
    "Details_D.Folktails.png": dict(
        patch_class="details",
        target=(105.0, 81.0, 48.0),
        max_color_distance=25.0,
        sizes=(256, 224, 192, 160),
        exclude=[(0, 0, 1024, 512)],  # faction banner / logo / sack / flag strip
        reason="dark timber floor-plank block from the detail atlas; the whole "
               "top half (faction banner + logo + sack + flag strip) is excluded",
    ),
    "Details_D.IronTeeth.png": dict(
        patch_class="details",
        target=(88.0, 76.0, 58.0),
        max_color_distance=25.0,
        sizes=(256, 224, 192, 160),
        exclude=[(0, 0, 1024, 512)],
        reason="dark timber plank block from the IronTeeth detail atlas; the "
               "banner/sack/flag half is excluded",
    ),
    "WindowsAtlas_D.Folktails.png": dict(
        patch_class="glass",
        target=(140.0, 154.0, 166.0),
        max_color_distance=45.0,
        sizes=(192, 160, 144, 128),
        exclude=[],
        reason="interior of a flat glass pane (bottom-right pane gradient area), "
               "not the tan atlas backdrop and not a window frame",
    ),
    "WindowsAtlas_D.IronTeeth.png": dict(
        patch_class="glass",
        target=(140.0, 154.0, 166.0),
        max_color_distance=45.0,
        sizes=(192, 160, 144, 128),
        exclude=[],
        reason="interior of a flat glass pane (bottom-right pane gradient area), "
               "not the tan atlas backdrop and not a window frame",
    ),
    "Plaster_D_White.Folktails.png": dict(
        patch_class="plaster",
        target=(174.0, 157.0, 138.0),
        max_color_distance=12.0,
        sizes=(384, 320, 256, 224),
        exclude=[],
        reason="large smooth white-plaster area (cream flat roof / plaster walls)",
    ),
    "Plaster_D_White.IronTeeth.png": dict(
        patch_class="plaster",
        target=(174.0, 157.0, 138.0),
        max_color_distance=12.0,
        sizes=(384, 320, 256, 224),
        exclude=[],
        reason="large smooth white-plaster area; the orange IronTeeth plaster "
               "variant is supplied by the material _Color tint",
    ),
    "Moss_D.Folktails.png": dict(
        patch_class="moss",
        target=(43.0, 49.0, 21.0),
        sizes=(256, 224, 192, 160),
        exclude=[],
        reason="uniform moss/foliage area (green accents of the basic tier)",
    ),
    "RoofPlanks_D.png": dict(
        patch_class="roof_planks",
        target=(38.0, 40.0, 31.0),
        sizes=(320, 256, 224, 192),
        exclude=[],
        reason="dark blue-grey roof-plank field (IronTeeth basic roof)",
    ),
}


# --------------------------------------------------------------------------- #
# deterministic low-variance window search
# --------------------------------------------------------------------------- #
def _load_numpy():
    try:
        import numpy as np
    except Exception as exc:  # pragma: no cover - environment specific
        raise SystemExit("material_texture_analysis needs numpy (Blender python "
                         "and the dev python both provide it): %r" % (exc,))
    return np


def _integral(np, values):
    out = np.zeros((values.shape[0] + 1, values.shape[1] + 1), dtype=np.float64)
    out[1:, 1:] = np.cumsum(np.cumsum(values, axis=0), axis=1)
    return out


class Texture(object):
    """Decoded RGB texture plus the integral images used by the window search."""

    def __init__(self, np, path, source):
        from PIL import Image  # fast local decode; validators use png_stats
        self.np = np
        self.path = path
        self.source = source
        img = Image.open(path)
        if img.mode != "RGB":
            img = img.convert("RGB")
        self.arr = np.asarray(img, dtype=np.uint8)
        self.height, self.width = self.arr.shape[:2]
        f = self.arr.astype(np.float64)
        self._sums = [_integral(np, f[:, :, c]) for c in range(3)]
        self._sq = [_integral(np, f[:, :, c] ** 2) for c in range(3)]
        flat = f.reshape(-1, 3)
        self.median = [float(v) for v in np.percentile(flat, 50, axis=0)]

    def window_stats(self, size, x, y):
        np = self.np
        n = float(size * size)
        means = []
        stds = []
        for c in range(3):
            s = (self._sums[c][y + size, x + size] - self._sums[c][y, x + size]
                 - self._sums[c][y + size, x] + self._sums[c][y, x])
            q = (self._sq[c][y + size, x + size] - self._sq[c][y, x + size]
                 - self._sq[c][y + size, x] + self._sq[c][y, x])
            mean = s / n
            var = q / n - mean * mean
            means.append(mean)
            stds.append(var ** 0.5 if var > 0.0 else 0.0)
        return means, stds

    def window_rgb(self, size, x, y):
        return self.arr[y:y + size, x:x + size, :]


def _rect_intersects(a, b):
    ax0, ay0, ax1, ay1 = a
    bx0, by0, bx1, by1 = b
    return not (ax1 <= bx0 or bx1 <= ax0 or ay1 <= by0 or by1 <= ay0)


def _hazard_fraction(window):
    """Fraction of warning-yellow and saturated banner/logo pixels in a window.

    The yellow/black hazard texture and the faction banner/logo art in the
    Details atlases are exactly the content the user complained about, so a
    patch must not contain them even when their local std happens to be low.
    The rules are deliberately narrow so ordinary wood / copper / thatch colours
    are not flagged:
      * warning yellow: bright yellow-orange with almost no blue;
      * banner blue/teal/purple: blue clearly dominant;
      * banner green: green clearly dominant;
      * banner crimson: red dominant with blue >= green (wood/copper has g > b).
    """
    np = _load_numpy()
    r = window[:, :, 0].astype(np.int32)
    g = window[:, :, 1].astype(np.int32)
    b = window[:, :, 2].astype(np.int32)
    hazard_yellow = ((r >= 160) & (g >= 130) & (b <= 85)
                     & ((r - g) <= 70) & ((g - b) >= 70))
    banner_blue = (b >= r + 30) & (b >= g + 10) & (b >= 70)
    banner_green = (g >= r + 40) & (g >= b + 40) & (g >= 70)
    banner_crimson = (r >= g + 80) & (b >= g + 5) & (r >= 120)
    return float((hazard_yellow | banner_blue | banner_green | banner_crimson).mean())


def _rect_stats(np, tex, pixel_rect, target):
    """Decoded-PNG statistics of an arbitrary (non-square) canvas rect."""
    x0, y0, x1, y1 = [int(v) for v in pixel_rect]
    window = tex.arr[y0:y1, x0:x1, :]
    flat = window.reshape(-1, 3).astype(np.float64)
    means = flat.mean(axis=0)
    stds = flat.std(axis=0)
    dist = float(np.sqrt(((means - np.asarray(target, dtype=np.float64)) ** 2)
                         .sum()))
    return {
        "mean_rgb": [round(float(v), 2) for v in means],
        "std": round(float(stds.max()), 2),
        "std_by_channel": [round(float(v), 2) for v in stds],
        "color_distance": round(dist, 2),
        "target_rgb": [float(v) for v in target],
        "hazard_fraction": round(float(_hazard_fraction(window)), 5),
    }


def _canvas_stats_factory(np, materials, textures):
    """Canvas-statistics callback for ``material_uv_policy.decorate_map_data``."""
    cache = {}

    def stats_for(material):
        entry = materials[material]
        texture = entry["texture"]
        rect = tuple(UVP.canvas_pixel_rect(material, entry["texture_size"]))
        key = (texture, rect)
        if key in cache:
            return dict(cache[key])
        hint = TEXTURE_HINTS.get(texture, {})
        target = hint.get("target", (128.0, 128.0, 128.0))
        found = _rect_stats(np, textures[texture], rect, target)
        if UVP.is_tile(material):
            found["std_max"] = None      # full-bleed canvas: no uniformity gate
        else:
            found["std_max"] = float(UVP.PATCH_CANVAS_STD_MAX)
        cache[key] = found
        return dict(found)

    return stats_for


def _apply_canvas_patch_records(result):
    """For patch-mode materials the sampled window IS the large canvas.

    ``entry["patch"]`` documents what the shipped UVs actually sample (ART-07);
    tile materials keep the legacy small uniform probe patch (texture identity /
    C13 variance re-verification) because their canvas is the whole texture.
    """
    materials = result["materials"]
    texture_patches = result["texture_patches"]
    for material in sorted(materials):
        if UVP.policy_for(material)["mode"] != "patch":
            continue
        entry = materials[material]
        canvas = entry["uv_canvas"]
        stats = canvas.get("stats") or {}
        hint = TEXTURE_HINTS.get(entry["texture"], {})
        tint = entry["material_color_tint"]
        mean = stats.get("mean_rgb") or [0.0, 0.0, 0.0]
        tinted = [round(max(0.0, min(255.0, m * t)), 2)
                  for m, t in zip(mean, tint)]
        patch = {
            "pixel_rect": list(canvas["pixel_rect"]),
            "size_px": list(canvas["size_px"]),
            "uv_rect": list(canvas["uv_rect"]),
            "mean_rgb": list(mean),
            "mean_rgb_tinted": tinted,
            "std": float(stats.get("std", 0.0)),
            "std_by_channel": list(stats.get("std_by_channel") or []),
            "std_max": float(UVP.PATCH_CANVAS_STD_MAX),
            "hard_std_cap": float(HARD_STD_CAP),
            "color_distance": float(stats.get("color_distance", 0.0)),
            "color_distance_max": float(hint.get("max_color_distance",
                                                 MAX_COLOR_DISTANCE)),
            "target_rgb": list(stats.get("target_rgb") or hint.get("target") or []),
            "hazard_fraction": float(stats.get("hazard_fraction", 0.0)),
            "reason": ("ART-07 large canvas: %s" % entry["uv_reason"]),
            "reject_tags": ["hazard-stripes", "faction-banner/logo", "text",
                            "uv-margin/canvas", "repair-plate", "plank-seam",
                            "grille/rib/pattern"],
            "art07_canvas": True,
        }
        entry["patch"] = patch
        texture = entry["texture"]
        if texture in texture_patches:
            texture_patches[texture]["patch"] = dict(patch)
    return result



def search_texture(tex, hint, size, std_max, step=8):
    """Deterministic best window of one size for one texture hint.

    Two candidates are returned:
      * ``ok``: the most *representative* window of this size, i.e. the one
        minimising ``std + color_dist`` among windows that satisfy
        ``std <= std_max``, ``color_dist <= hint.max_color_distance`` and the
        hazard guard.  Combining the two terms is deliberate: picking the
        minimum-std window alone lands on shadow/cover corners that are flat but
        not representative (they made the Basic tier read as a black box);
      * ``any``: best ``std + color_dist`` window regardless of budgets
        (fallback only - reported as a generation warning and rejected by
        --verify / the validators).
    """
    np = tex.np
    target = np.asarray(hint["target"], dtype=np.float64)
    max_color = float(hint.get("max_color_distance", MAX_COLOR_DISTANCE))
    exclude = hint.get("exclude", [])
    # keep a 4 px margin from the texture border: a patch must never sit on a
    # clamp/wrap seam line (validator reject tag "uv-border/seam").
    margin = 4
    best_ok = None
    best_any = None
    for y in range(margin, tex.height - size - margin + 1, step):
        for x in range(margin, tex.width - size - margin + 1, step):
            rect = (x, y, x + size, y + size)
            if any(_rect_intersects(rect, ex) for ex in exclude):
                continue
            means, stds = tex.window_stats(size, x, y)
            std = max(stds)
            color_dist = float(np.linalg.norm(np.asarray(means) - target))
            score = std + color_dist
            cand_any = (score, y, x, std, color_dist, means, stds)
            if best_any is None or cand_any[:3] < best_any[:3]:
                best_any = cand_any
            if std <= std_max and color_dist <= max_color:
                cand_ok = (score, y, x, std, color_dist, means, stds)
                if best_ok is None or cand_ok[:3] < best_ok[:3]:
                    best_ok = cand_ok
    out = {"ok": None, "any": None}
    for key, cand in (("ok", best_ok), ("any", best_any)):
        if cand is None:
            continue
        if key == "ok":
            score, y, x, std, color_dist, means, stds = cand
        else:
            score, y, x, std, color_dist, means, stds = cand
        hazard = _hazard_fraction(tex.window_rgb(size, x, y))
        out[key] = dict(size=size, x=x, y=y, std=std, color_dist=color_dist,
                        hazard=hazard, means=means, stds=stds, score=score)
    return out


def choose_shared_patch(textures, hints, budgets):
    """Joint window for a cross-faction shared group (largest acceptable size).

    Returns ``(size, x, y, per_texture)`` where ``per_texture`` maps the texture
    name to its own measured stats inside the shared window.  Raises when no
    window satisfies every texture's own std/colour/hazard budget.
    """
    np = _load_numpy()
    sizes = sorted({size for hint in hints.values() for size in hint["sizes"]},
                   reverse=True)
    probe = textures[0][1]
    for size in sizes:
        if not PATCH_SIZE_RANGE[0] <= size <= PATCH_SIZE_RANGE[1]:
            continue
        if size > min(max(h["sizes"]) for h in hints.values()):
            continue
        best = None
        for y in range(4, probe.height - size - 4 + 1, 8):
            for x in range(4, probe.width - size - 4 + 1, 8):
                worst_std = 0.0
                worst_color = 0.0
                detail = {}
                ok = True
                for name, tex in textures:
                    hint = hints[name]
                    max_color = float(hint.get("max_color_distance",
                                               MAX_COLOR_DISTANCE))
                    means, stds = tex.window_stats(size, x, y)
                    std = max(stds)
                    color_dist = float(np.linalg.norm(
                        np.asarray(means) - np.asarray(hint["target"])))
                    if any(_rect_intersects((x, y, x + size, y + size), ex)
                           for ex in hint.get("exclude", [])):
                        ok = False
                        break
                    if std > budgets[name] or color_dist > max_color:
                        ok = False
                        break
                    hazard = _hazard_fraction(tex.window_rgb(size, x, y))
                    if hazard > 0.005:
                        ok = False
                        break
                    detail[name] = dict(std=std, color_dist=color_dist,
                                        hazard=hazard, means=means, stds=stds)
                    worst_std = max(worst_std, std)
                    worst_color = max(worst_color, color_dist)
                if not ok:
                    continue
                # representative first: keep the combined "std + colour" spirit
                score = worst_std + worst_color
                cand = (score, y, x, worst_std, worst_color, detail)
                if best is None or cand[:3] < best[:3]:
                    best = cand
        if best is not None:
            score, y, x, worst_std, worst_color, detail = best
            return size, x, y, detail
    raise SystemExit("no shared construction window satisfies every texture's "
                     "budget: %r" % (sorted(hints),))


def choose_patch(tex, hint, std_max):
    """Largest window satisfying the std/colour/hazard budgets (deterministic)."""
    attempts = []
    for size in hint["sizes"]:
        if not PATCH_SIZE_RANGE[0] <= size <= PATCH_SIZE_RANGE[1]:
            continue
        found = search_texture(tex, hint, size, std_max)
        if found["any"] is None:
            continue
        attempts.append(found["any"])
        if found["ok"] is not None and found["ok"]["hazard"] <= 0.005:
            return found["ok"]
    # fall back to the best deterministic candidate; the caller reports FAIL and
    # --verify/validators reject it, so never silently weaken the contract.
    best = min(attempts, key=lambda c: (c["score"], -c["size"]))
    return best


def _resolve_texture(roots, texture, source):
    root = roots[source]
    path = os.path.join(root, texture)
    if not os.path.isfile(path):
        raise SystemExit("missing vanilla texture %r under %r; set %s or pass "
                         "--tex-root-%s" % (texture, root,
                                            TEXTURE_ROOT_ENV.get(source, "?"),
                                            source.replace("vanilla-tex-", "")))
    return path


def _uv_rect(pixel_rect, width, height):
    x0, y0, x1, y1 = pixel_rect
    return [round(x0 / float(width), 6), round(1.0 - y1 / float(height), 6),
            round(x1 / float(width), 6), round(1.0 - y0 / float(height), 6)]


def _used_in_from_material_map():
    """material -> ["Tier.Faction.slot", ...] from timbermesh_common.MATERIAL_MAP."""
    import timbermesh_common as TC
    used = {}
    for (tier, faction, construction), mapping in sorted(TC.MATERIAL_MAP.items()):
        suffix = ".Construction" if construction else ""
        for slot, material in sorted(mapping.items()):
            used.setdefault(material, []).append(
                "%s.%s.%s%s" % (tier, faction, slot, suffix))
    for names in sorted(used):
        used[names] = sorted(used[names])
    return used


def build_map(roots):
    np = _load_numpy()
    import png_stats as PS
    used_in = _used_in_from_material_map()

    # 0. resolve + decode every texture once
    texture_source = {}
    texture_path = {}
    textures = {}
    budgets = {}
    for texture, hint in sorted(TEXTURE_HINTS.items()):
        source = None
        for _material, (tex_name, tex_source, _tint) in sorted(
                MATERIAL_TEXTURES.items()):
            if tex_name == texture:
                source = tex_source
                break
        if source is None:
            raise SystemExit("TEXTURE_HINTS has %r but no material references it"
                             % (texture,))
        path = _resolve_texture(roots, texture, source)
        texture_source[texture] = source
        texture_path[texture] = path
        textures[texture] = Texture(np, path, source)
        budgets[texture] = min(
            STD_MAX_BY_CLASS.get(hint["patch_class"], DEFAULT_STD_MAX),
            HARD_STD_CAP)

    # 1. joint windows for the cross-faction construction stage (one shared UV set)
    shared_rects = {}
    shared_groups = {}
    for group, info in sorted(CONSTRUCTION_SHARED_GROUPS.items()):
        names = list(info["textures"])
        hint_by_name = {name: TEXTURE_HINTS[name] for name in names}
        group_textures = [(name, textures[name]) for name in names]
        size, x, y, detail = choose_shared_patch(group_textures, hint_by_name,
                                                 {n: budgets[n] for n in names})
        rect = [x, y, x + size, y + size]
        shared_groups[group] = dict(
            group=group,
            textures=names,
            pixel_rect=rect,
            size_px=[size, size],
            uv_rect=_uv_rect(rect, textures[names[0]].width,
                             textures[names[0]].height),
            texture_uv_rect={
                name: _uv_rect(rect, textures[name].width, textures[name].height)
                for name in names},
            per_texture={
                name: dict(
                    mean_rgb=[round(float(v), 2) for v in detail[name]["means"]],
                    std=round(float(detail[name]["std"]), 2),
                    std_by_channel=[round(float(v), 2)
                                    for v in detail[name]["stds"]],
                    std_max=float(budgets[name]),
                    color_distance=round(float(detail[name]["color_dist"]), 2),
                    target_rgb=[float(v) for v in hint_by_name[name]["target"]],
                ) for name in names},
            reason=info["reason"],
        )
        for name in names:
            shared_rects[name] = (rect, size, detail[name])

    # 2. one patch per unique texture (materials sharing a texture share the patch)
    texture_patches = {}
    warns = []
    for texture, hint in sorted(TEXTURE_HINTS.items()):
        tex = textures[texture]
        std_max = STD_MAX_BY_CLASS.get(hint["patch_class"], DEFAULT_STD_MAX)
        threshold = min(float(std_max), HARD_STD_CAP)
        if texture in shared_rects:
            rect, size, detail = shared_rects[texture]
            found = dict(x=rect[0], y=rect[1], size=size,
                         means=detail["means"], stds=detail["stds"],
                         std=detail["std"],
                         color_dist=detail["color_dist"],
                         hazard=detail["hazard"])
            shared_group = SHARED_GROUP_OF_TEXTURE[texture]
        else:
            found = choose_patch(tex, hint, std_max)
            size = found["size"]
            shared_group = None
        pixel_rect = [found["x"], found["y"],
                      found["x"] + size, found["y"] + size]
        off = {}
        for material, (tex_name, _source, tint) in sorted(MATERIAL_TEXTURES.items()):
            if tex_name != texture:
                continue
            tinted = [max(0.0, min(255.0, m * t))
                      for m, t in zip(found["means"], tint)]
            off[material] = tinted
        uv = _uv_rect(pixel_rect, tex.width, tex.height)
        reason = hint.get("reason", "")
        if shared_group:
            reason = ("shared construction-stage UV window (%s): %s"
                      % (shared_group,
                         CONSTRUCTION_SHARED_GROUPS[shared_group]["reason"]))
        texture_patches[texture] = dict(
            texture=texture,
            texture_source=texture_source[texture],
            texture_path=texture_path[texture],
            texture_size=[tex.width, tex.height],
            texture_sha256=PS.texture_sha256(texture_path[texture]),
            patch_class=hint["patch_class"],
            shared_uv_group=shared_group,
            patch=dict(
                pixel_rect=pixel_rect,
                size_px=[size, size],
                uv_rect=uv,
                mean_rgb=[round(float(v), 2) for v in found["means"]],
                mean_rgb_tinted={
                    material: [round(v, 2) for v in tinted]
                    for material, tinted in off.items()},
                std=round(float(found["std"]), 2),
                std_by_channel=[round(float(v), 2) for v in found["stds"]],
                std_max=threshold,
                hard_std_cap=float(HARD_STD_CAP),
                color_distance=round(float(found["color_dist"]), 2),
                color_distance_max=float(hint.get("max_color_distance",
                                                  MAX_COLOR_DISTANCE)),
                target_rgb=[float(v) for v in hint["target"]],
                hazard_fraction=round(float(found["hazard"]), 5),
                reason=reason,
                reject_tags=["hazard-stripes", "faction-banner/logo", "text",
                             "uv-margin/canvas", "repair-plate", "plank-seam",
                             "grille/rib/pattern"],
            ),
        )
        if found["std"] > threshold:
            warns.append("%s: std %.2f exceeds %r budget %.2f (patch rejected)"
                         % (texture, found["std"], hint["patch_class"], threshold))
        if found["std"] > HARD_STD_CAP:
            warns.append("%s: std %.2f exceeds HARD cap %.2f" % (
                texture, found["std"], HARD_STD_CAP))
        if found["color_dist"] > float(hint.get("max_color_distance",
                                                MAX_COLOR_DISTANCE)):
            warns.append("%s: colour distance %.1f too large (patch rejected)" % (
                texture, found["color_dist"]))
        if found["hazard"] > 0.005:
            warns.append("%s: hazard/banner pixel fraction %.4f" % (
                texture, found["hazard"]))

    # 3. material -> texture patch record
    materials = {}
    for material, (texture, source, tint) in sorted(MATERIAL_TEXTURES.items()):
        patch = texture_patches[texture]
        entry = {
            "material": material,
            "texture": texture,
            "texture_source": source,
            "texture_path": patch["texture_path"],
            "texture_size": list(patch["texture_size"]),
            "texture_sha256": patch["texture_sha256"],
            "material_color_tint": [float(v) for v in tint],
            "patch_class": patch["patch_class"],
            "shared_uv_group": patch["shared_uv_group"],
            "patch": dict(patch["patch"]),
            "used_in_material_map": used_in.get(material, []),
        }
        entry["patch"]["mean_rgb_tinted"] = (
            patch["patch"]["mean_rgb_tinted"].get(material))
        materials[material] = entry

    # 4. unused / banned materials with a hazard reference rect for negative controls
    unused = {}
    for material, info in sorted(UNUSED_MATERIALS.items()):
        path = _resolve_texture(roots, info["texture"], info["texture_source"])
        rect = info["hazard_reference_pixel_rect"]
        decoded = PS.decode_png(path)
        stats = PS.patch_stats(decoded, rect)
        unused[material] = dict(
            material=material,
            texture=info["texture"],
            texture_source=info["texture_source"],
            texture_path=path,
            texture_size=[decoded["width"], decoded["height"]],
            texture_sha256=PS.texture_sha256(path),
            reason=info["reason"],
            policy=info["policy"],
            hazard_reference_pixel_rect=rect,
            hazard_reference_uv_rect=_uv_rect(rect, decoded["width"],
                                              decoded["height"]),
            hazard_reference_stats={
                "mean_rgb": [round(float(v), 2) for v in stats["mean_rgb"]],
                "std": round(float(stats["std"]), 2),
                "std_by_channel": stats["std_by_channel"],
            },
        )

    result = {
        "schema": SCHEMA,
        "generated_by": GENERATOR,
        "material_source_provenance": PROVENANCE,
        "texture_roots": dict(roots),
        "uv_convention": UV_CONVENTION,
        "thresholds": {
            "std_max_default": DEFAULT_STD_MAX,
            "std_max_by_class": dict(STD_MAX_BY_CLASS),
            "hard_std_cap": HARD_STD_CAP,
            "color_distance_max": MAX_COLOR_DISTANCE,
            "patch_size_range_px": list(PATCH_SIZE_RANGE),
            "std_definition": "max over R,G,B of the population std of the patch "
                              "pixels (raw sRGB 0..255 bytes) - recomputed by "
                              "art/png_stats.py",
        },
        "large_shell_policy": {
            "forbidden_materials": sorted(UNUSED_MATERIALS),
            "reason": "their entire albedo is warning-stripe paint; any large face "
                      "would read as a hazard sign (user bug 2026-10-27). They stay "
                      "in MATERIAL_WHITELIST for completeness but must never appear "
                      "in a shipped model.",
        },
        "construction_shared_patches": shared_groups,
        "materials": materials,
        "unused_materials": unused,
        "texture_patches": texture_patches,
        "generation_warnings": warns,
    }
    # ART-07: world-scale UV policy (mode / canvas / texel density).  The
    # decoded textures are already in memory, so the canvas statistics are
    # recomputed from the real PNG bytes here as well.
    stats_for = _canvas_stats_factory(np, materials, textures)
    warns.extend(UVP.decorate_map_data(result, stats_for))
    _apply_canvas_patch_records(result)
    _apply_construction_windows(result, np, textures, shared_groups)
    return result


def _apply_construction_windows(result, np, textures, shared_groups):
    """ART-07: the construction shared window is the Folktails material canvas.

    The construction stage is ONE shared source mesh / ONE baked UV set exported
    for both factions (unchanged ART-05 design).  ART-07 bakes it from the
    Folktails construction materials, so the shared UV window is the Folktails
    material's ART-07 canvas; the IronTeeth materials must be able to resolve
    that same window (their own canvas has to contain it).  The recorded
    per-texture statistics are recomputed inside that window from the real PNGs.
    """
    pairs = {
        "construction_wood": ("BaseWood_Brown.Folktails", "RoofPlanks.IronTeeth"),
        "construction_metal": ("BaseMetal.Folktails", "BaseMetal.IronTeeth"),
    }
    materials = result["materials"]
    for group, info in shared_groups.items():
        ft_material, it_material = pairs[group]
        canvas = materials[ft_material]["uv_canvas"]
        rect = list(canvas["pixel_rect"])
        per_texture = {}
        texture_uv_rect = {}
        for name in info["textures"]:
            tex = textures[name]
            hint = TEXTURE_HINTS[name]
            found = _rect_stats(np, tex, rect, hint["target"])
            per_texture[name] = dict(
                mean_rgb=found["mean_rgb"],
                std=found["std"],
                std_by_channel=found["std_by_channel"],
                std_max=float(UVP.PATCH_CANVAS_STD_MAX),
                hazard_fraction=found["hazard_fraction"],
                color_distance=found["color_distance"],
                target_rgb=found["target_rgb"],
            )
            texture_uv_rect[name] = [
                round(v, 9) for v in UVP.uv_rect_from_pixel_rect(
                    rect, tex.width, tex.height)]
        info["pixel_rect"] = rect
        info["size_px"] = list(canvas["size_px"])
        info["uv_rect"] = list(canvas["uv_rect"])
        info["texture_uv_rect"] = texture_uv_rect
        info["per_texture"] = per_texture
        info["folktails_material"] = ft_material
        info["ironteeth_material"] = it_material
        info["art07_shared_window"] = True
        info["reason"] = (
            "ART-07 shared construction-stage UV window: the construction stage "
            "is one shared source mesh with ONE baked UV set (Folktails "
            "materials), so both factions' materials must resolve the same UV "
            "window; the window is the %s ART-07 canvas and %s's own canvas "
            "contains it (full-bleed tile texture)." % (ft_material, it_material))
    result["construction_shared_patches"] = shared_groups


# --------------------------------------------------------------------------- #
# QA swatches + verification
# --------------------------------------------------------------------------- #
def write_swatches(result, out_dir):
    from PIL import Image, ImageDraw
    os.makedirs(out_dir, exist_ok=True)
    made = 0
    for material, entry in sorted(result["materials"].items()):
        patch = entry["patch"]
        x0, y0, x1, y1 = patch["pixel_rect"]
        img = Image.open(entry["texture_path"]).convert("RGB")
        sw = Image.new("RGB", (img.width + (x1 - x0) + 8,
                               max(img.height, (y1 - y0) + 40)), (22, 22, 22))
        sw.paste(img, (0, 0))
        crop = img.crop((x0, y0, x1, y1)).resize((x1 - x0, y1 - y0), Image.LANCZOS)
        sw.paste(crop, (img.width + 8, 0))
        draw = ImageDraw.Draw(sw)
        draw.rectangle([x0, y0, x1 - 1, y1 - 1], outline=(0, 255, 0), width=3)
        label = "%s  std=%.1f/%s  mean=(%.0f,%.0f,%.0f)" % (
            material, patch["std"], patch["std_max"], *patch["mean_rgb"])
        draw.text((img.width + 10, (y1 - y0) + 6), label, fill=(240, 240, 240))
        sw.save(os.path.join(out_dir, material.replace(".", "_") + ".png"))
        made += 1
    return made


def verify_map(roots):
    """Re-derive the map and compare with the committed JSON (byte-for-byte)."""
    fresh = build_map(roots)
    text = json.dumps(fresh, indent=2, sort_keys=True) + "\n"
    if not os.path.isfile(MAP_PATH):
        print("[material_texture_analysis] --verify FAIL: %s does not exist"
              % MAP_PATH)
        return 1
    with open(MAP_PATH, "r", encoding="utf-8") as handle:
        committed = handle.read()
    if committed != text:
        print("[material_texture_analysis] --verify FAIL: committed map differs "
              "from a fresh deterministic run (regenerate with no --verify)")
        for line_no, (a, b) in enumerate(zip(committed.splitlines(),
                                             text.splitlines()), 1):
            if a != b:
                print("  first difference at line %d:\n    committed: %s\n    fresh    : %s"
                      % (line_no, a, b))
                break
        return 1
    # independent re-check of the recorded statistics from the PNG bytes
    import png_stats as PS
    for material, entry in sorted(fresh["materials"].items()):
        decoded = PS.decode_png(entry["texture_path"])
        stats = PS.patch_stats(decoded, entry["patch"]["pixel_rect"])
        for got, want, what in (
                (stats["mean_rgb"], entry["patch"]["mean_rgb"], "mean_rgb"),
                (stats["std"], entry["patch"]["std"], "std")):
            if isinstance(got, list):
                ok = all(abs(a - b) <= 0.05 for a, b in zip(got, want))
            else:
                ok = abs(got - want) <= 0.05
            if not ok:
                print("[material_texture_analysis] --verify FAIL: %s %s recorded=%r "
                      "recomputed=%r" % (material, what, want, got))
                return 1
        if entry["patch"]["std"] > entry["patch"]["std_max"] + 1e-9:
            print("[material_texture_analysis] --verify FAIL: %s std %.2f > std_max %.1f"
                  % (material, entry["patch"]["std"], entry["patch"]["std_max"]))
            return 1
        if entry["patch"]["std"] > fresh["thresholds"]["hard_std_cap"] + 1e-9:
            print("[material_texture_analysis] --verify FAIL: %s std %.2f > hard cap"
                  % (material, entry["patch"]["std"]))
            return 1
    print("[material_texture_analysis] --verify PASS: %d materials, %d unique "
          "textures, committed JSON is reproducible and the recorded statistics "
          "re-derive from the real PNG bytes"
          % (len(fresh["materials"]), len(fresh["texture_patches"])))
    return 0


def write_result(result, path):
    text = json.dumps(result, indent=2, sort_keys=True) + "\n"
    with open(path, "w", encoding="utf-8", newline="\n") as handle:
        handle.write(text)
    return text


def main(argv=None):
    parser = argparse.ArgumentParser(
        description="Generate art/material_texture_map.json from the real vanilla "
                    "material _D textures (ART-05).")
    parser.add_argument("--tex-root-all", default=None)
    parser.add_argument("--tex-root-extra", default=None)
    parser.add_argument("--out", default=MAP_PATH)
    parser.add_argument("--swatches", default=None)
    parser.add_argument("--verify", action="store_true")
    args = parser.parse_args(argv)

    roots = dict(DEFAULT_TEXTURE_ROOTS)
    for key, env in TEXTURE_ROOT_ENV.items():
        if os.environ.get(env):
            roots[key] = os.environ[env]
    if args.tex_root_all:
        roots["vanilla-tex-all"] = args.tex_root_all
    if args.tex_root_extra:
        roots["vanilla-tex-extra"] = args.tex_root_extra

    if args.verify:
        return verify_map(roots)

    result = build_map(roots)
    text = write_result(result, args.out)
    print("[material_texture_analysis] wrote %s (%d materials, %d unique textures, "
          "%d bytes)" % (args.out, len(result["materials"]),
                         len(result["texture_patches"]), len(text)))
    for material, entry in sorted(result["materials"].items()):
        patch = entry["patch"]
        print("  %-38s %-30s px=%-20s uv=%-46s mean=(%3.0f,%3.0f,%3.0f) "
              "tint=(%.2f,%.2f,%.2f) std=%4.1f/%4.1f"
              % (material, entry["texture"], tuple(patch["pixel_rect"]),
                 tuple(patch["uv_rect"]), *patch["mean_rgb"],
                 *entry["material_color_tint"], patch["std"], patch["std_max"]))
    for warn in result["generation_warnings"]:
        print("  WARNING: " + warn)
    if args.swatches:
        made = write_swatches(result, args.swatches)
        print("[material_texture_analysis] wrote %d QA swatches to %s"
              % (made, args.swatches))
    if result["generation_warnings"]:
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
