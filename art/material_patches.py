"""HDBF ART-04 — flat-patch UV contract (one fixed texture patch per material).

Background (user instruction 2026-10-25): stop letting every face sample a
different spot of the vanilla material atlas ("patchwork").  Keep the vanilla
`MaterialCollection` asset names (the game resolves them by name), but map
**every face of one material onto ONE fixed UV patch** so wood / metal / glass /
roof each read as a single consistent colour+texture.

How the patch is used
---------------------
* The patch is a `texel` rect of the 2048x2048 ART-01 atlas (top-left pixel
  origin, exactly like `atlas_layout.REGIONS`).  It sits in the middle of a
  semantic region painted by `gen_textures.py`, so the offline preview / icon
  renders sample a representative wood / metal / glass / plaster / thatch area.
* `gen_models.py` maps each face into a sub-rect of that patch at a fixed texel
  density (64 px per grid unit) with a deterministic offset, so all faces of a
  material stay inside the same patch but are not pixel-identical.  Small
  tiling inside one patch is intended; sampling a *different material's* region
  is not.
* The same UV numbers are what the game receives.  The vanilla material maps
  UV [0,1] onto its own atlas region, so in game the patch becomes a small,
  fixed area of that material texture (consistent look).  From the material
  asset names alone we cannot know the exact vanilla texel content: whether the
  patch reads as the intended colour is **待實機確認** (see the ART-04 report).

Module is pure Python (no bpy / numpy) so the ART-01 generator, the ART-02
exporter and both validators can import it.
"""

from __future__ import annotations

import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)

import atlas_layout as A  # noqa: E402

#: fixed texel density used when a face is projected into its material patch.
#: 64 px per grid unit == the low end of the ART-01 / LOD2 density; every ART-04
#: face is small enough to fit its patch at this density (largest face
#: 3.0 x 0.66 grid = 192 x 43 texels; smallest patch is 88 x 88).
PATCH_TEXELS_PER_GRID = 64

#: role -> (semantic atlas region, pixel rect x0,y0,x1,y1 top-left origin).
#: 4 px inset from the region so a packed sub-rect can never bleed across the
#: region gutter; roles are spaced so no two overlap.
PATCH_PIXELS = {
    # ---- wood band -------------------------------------------------------
    "wood_plain":  ("wood",   (96, 352, 480, 736)),
    "wood_light":  ("wood",   (96, 1056, 480, 1440)),
    "planks_light": ("wood",  (96, 1568, 480, 1952)),
    "planks_brown": ("wood",  (528, 352, 784, 608)),
    # ---- metal band ------------------------------------------------------
    "metal_base":    ("metal", (1056, 384, 1440, 768)),
    "metal_painted": ("metal", (1056, 1088, 1440, 1472)),
    "metal_details": ("metal", (1056, 1568, 1312, 1824)),
    # ---- glass band ------------------------------------------------------
    "glass":         ("glass", (1744, 704, 1832, 792)),
    # ---- fabric band -----------------------------------------------------
    "plaster":       ("canvas", (1872, 288, 2000, 416)),
    "roof":          ("thatch", (1872, 1472, 2000, 1600)),
}

#: vanilla `MaterialCollection` asset name -> flat-patch role.  Keys cover every
#: name the ART-02 MATERIAL_MAP uses (20 of the 21 whitelisted candidates) plus
#: the unused-but-whitelisted BaseWood_LightBrown so a future tier mapping stays
#: importable.  FT and IT variants of the same semantic material share a role on
#: purpose: the shared ConstructionStage0 source mesh has one UV set and must
#: satisfy both factions.
ROLE_OF_MATERIAL = {
    # Folktails
    "BaseWood_Brown.Folktails": "wood_plain",
    "BaseWood_LightBrown.Folktails": "wood_light",
    "BaseWood_White.Folktails": "wood_light",
    "IrregularPlanks_Brown.Folktails": "planks_brown",
    "IrregularPlanks_LightBrown.Folktails": "planks_light",
    "PaintedMetal.Folktails": "metal_painted",
    "BaseMetal.Folktails": "metal_base",
    "WindowsAtlas.Folktails": "glass",
    "ThatchedRoof.Folktails": "roof",
    "Plaster_White.Folktails": "plaster",
    "Details.Folktails": "metal_details",
    # IronTeeth
    "BaseWood_DarkBrown.IronTeeth": "wood_plain",
    "BaseWood_Grey.IronTeeth": "wood_light",
    "IrregularPlanks_DarkBrown.IronTeeth": "planks_light",
    "IrregularPlanks_MetalForged.IronTeeth": "metal_details",
    "PaintedMetal.IronTeeth": "metal_painted",
    "BaseMetal.IronTeeth": "metal_base",
    "WindowsAtlas.IronTeeth": "glass",
    "RoofPlanks.IronTeeth": "roof",
    "Plaster_Orange.IronTeeth": "plaster",
    "Details.IronTeeth": "metal_details",
}


def role_of(material_name):
    """Flat-patch role of a vanilla material asset name (raises when unknown)."""
    try:
        return ROLE_OF_MATERIAL[material_name]
    except KeyError:
        raise KeyError("no ART-04 flat-patch role for vanilla material %r"
                       % (material_name,))


def uv_rect(role):
    """UV rect (u0, v0, u1, v1) of a role patch."""
    region, pixel_rect = PATCH_PIXELS[role]
    return A.px_rect_to_uv(pixel_rect, A.ATLAS_SIZE)


def patch_record(role):
    """JSON-ready patch record: id, semantic region, pixel + uv rect, texels."""
    region, pixel_rect = PATCH_PIXELS[role]
    u0, v0, u1, v1 = A.px_rect_to_uv(pixel_rect, A.ATLAS_SIZE)
    return {
        "id": role,
        "atlas_region": region,
        "pixel_rect": list(pixel_rect),
        "uv_rect": [round(u0, 6), round(v0, 6), round(u1, 6), round(v1, 6)],
        "texels": [pixel_rect[2] - pixel_rect[0], pixel_rect[3] - pixel_rect[1]],
        "texels_per_grid": PATCH_TEXELS_PER_GRID,
    }


def validate_contract():
    """Raise AssertionError if the patch table is inconsistent with the atlas."""
    seen = {}
    for role, (region, rect) in PATCH_PIXELS.items():
        assert region in A.REGIONS, role
        x0, y0, x1, y1 = rect
        rx0, ry0, rx1, ry1 = A.REGIONS[region][1]
        assert rx0 + 4 <= x0 < x1 <= rx1 - 4, (role, rect, A.REGIONS[region][1])
        assert ry0 + 4 <= y0 < y1 <= ry1 - 4, (role, rect, A.REGIONS[region][1])
        assert x1 - x0 >= 80 and y1 - y0 >= 80, (role, "patch too small for a face")
        u0, v0, u1, v1 = uv_rect(role)
        assert 0.0 <= u0 < u1 <= 1.0 and 0.0 <= v0 < v1 <= 1.0, role
        for other, orect in seen.items():
            assert x1 <= orect[0] or orect[2] <= x0 or y1 <= orect[1] or orect[3] <= y0, (
                "patch rects overlap: %s %s" % (role, other))
        seen[role] = rect
    for name, role in ROLE_OF_MATERIAL.items():
        assert role in PATCH_PIXELS, (name, role)
    return True


if __name__ == "__main__":
    validate_contract()
    print("material_patches contract OK: %d roles, %d material names"
          % (len(PATCH_PIXELS), len(ROLE_OF_MATERIAL)))
    for role in PATCH_PIXELS:
        rec = patch_record(role)
        print("  %-14s %-6s px=%-22s uv=%s texels=%dx%d"
              % (role, rec["atlas_region"], tuple(rec["pixel_rect"]),
                 tuple(rec["uv_rect"]), rec["texels"][0], rec["texels"][1]))
