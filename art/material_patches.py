"""HDBF v2 - real vanilla-material texture map accessors (+ ART-07 world UV helpers).

ART-07 (2026-10-27, current)
----------------------------
The user asked for clear, high-quality textures.  ART-06's per-material real
patch was correct but sampled a 32 px/m patch, so 3 m surfaces were blurry.
ART-07 (``art/material_uv_policy.py`` + ``assign_world_tiled_uvs``) gives every
material ONE frozen **world-scale planar projection** at its native-on-atlas
texel density:

  * ``mode == "tile"``  - the whole 1024^2 ``_D`` texture is a full-bleed
    surface texture; canvas = the full texture, density 340.0-357.8 px/m;
  * ``mode == "patch"`` - the texture is an atlas sheet; canvas = the largest
    coherent region (wood plank field, plain sheet metal, framed detail panels,
    flat glass pane), density 82.4-164.0 px/m.

UVs never exceed the material canvas: the game's runtime
``AutoAtlasingPrefabOptimizer`` remaps every mesh UV into the faction auto-atlas
cell (``uv*(1/sizeMultiplier)+cell_offset``, native 1024^2 cells), so a UV
component above 1 would sample another material - see
``art/ART07_PROVENANCE.md``.  The accessors below
(``mode_for`` / ``uv_canvas_for`` / ``px_per_m_for`` / ``expected_uv`` ...) are
the shared API for generators, validators, previews and the L1 tests.

ART-05 (2026-10-27) rewrite
---------------------------
The ART-04 flat-patch idea was right ("all faces of one material must sample one
fixed patch"), but it was expressed in a **fake shared 2048x2048 ART-01 atlas**.
In the real game every vanilla ``MaterialCollection`` material resolves to its
own 1024x1024 ``_D`` albedo PNG plus a material ``_Color`` tint, so those fake
atlas UVs landed on arbitrary content of the material's real texture: each face
read a different sub-rect (the checkerboard the user saw) and the top tier's
shell happened to sample ``PaintedMetal``'s yellow/black hazard stripes.

The single source of truth is now ``art/material_texture_map.json`` (generated
deterministically by ``art/material_texture_analysis.py`` from the real PNGs):

    material asset name -> real _D texture + patch pixel/UV rect + recorded
    mean RGB / std / per-class threshold + selection reason

This module is the small, dependency-free accessor layer shared by:

  * ``art/gen_models_v2.py``     UV assignment (``uv_rect_for(material)``)
  * ``art/export_timbermesh.py`` (indirectly, via the FBX UVs)
  * ``art/preview_materials.py`` / ``art/render_previews.py`` / ``render_icons.py``
    (``texture_path_for`` / ``material_color_tint``)
  * ``art/validate_models.py`` and ``art/validate_timbermesh.py``
  * the L1 contract tests (through the JSON file itself)

Sampling contract (verified from the game's IL, see the ART-05 report)
---------------------------------------------------------------------
``Timberborn.PrefabOptimization.AutoAtlasingPrefabOptimizer`` remaps a mesh's
UVs as ``uv_atlas = uv * fragment.UVScale + fragment.UVOffset`` with
``UVScale = 1 / sizeMultiplier`` and the fragment's texture placed in the
matching atlas cell, i.e. the normalized position inside the material's own
``_D`` texture is preserved (materials that are not part of a faction auto atlas
sample the texture 1:1).  The material ``_Color`` tint multiplies the sampled
albedo.  A patch rectangle expressed in the material's texture UV space is
therefore correct in game **without any atlas correction**.

UV assignment
-------------
``gen_models_v2.assign_flat_patch_uvs`` maps each face into a sub-rect of the
material's patch.  ``PATCH_TEXELS_PER_GRID`` is the fixed texel density used for
those sub-rects.  ART-01/ART-04 used 64 px/grid in a 2048 atlas; the real
textures are 1024, so the *same on-screen density* is 32 px/grid here
(64/2048 == 32/1024); this keeps the "sub-rect density unchanged" requirement.

Legacy preview fallback
-----------------------
The old role table (``PATCH_PIXELS`` / ``ROLE_OF_MATERIAL`` / ``role_of`` /
``uv_rect(role)`` / ``patch_record(role)``) is kept importable so the ART-01
preview fallback path and old callers do not break, but it is **not** used for
the shipped models any more and is marked deprecated below.
"""

from __future__ import annotations

import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)

import atlas_layout as A  # noqa: E402

#: generated map (single source of truth for the shipped-material patches).
MAP_PATH = os.path.join(HERE, "material_texture_map.json")
MAP_SCHEMA = "hdbf.material_texture_map/v1"

#: LEGACY (ART-06) fixed texel density of the retired flat-patch mapping; ART-07
#: shipped models use the per-material world-scale ``px_per_m`` from the map
#: (340.0-357.8 px/m tile / 82.4-164.0 px/m patch) and never call
#: ``assign_flat_patch_uvs``.  Kept for the import-compatible legacy fallback.
PATCH_TEXELS_PER_GRID = 32

#: 4 px guard band between a packed face sub-rect and the patch border, so
#: bilinear filtering/clamp never samples outside the validated patch.
PATCH_GUARD_PX = 4

_MAP_CACHE = {}


# --------------------------------------------------------------------------- #
# real-texture patch map (primary API)
# --------------------------------------------------------------------------- #
def load_material_texture_map(path=None):
    """Load and cache ``art/material_texture_map.json`` (raises when missing)."""
    resolved = os.path.abspath(path or MAP_PATH)
    if resolved in _MAP_CACHE:
        return _MAP_CACHE[resolved]
    if not os.path.isfile(resolved):
        raise RuntimeError(
            "missing material texture map %r - regenerate it with "
            "`python art/material_texture_analysis.py`" % (resolved,))
    with open(resolved, "r", encoding="utf-8") as handle:
        data = json.load(handle)
    _MAP_CACHE[resolved] = data
    return data


def material_texture_map():
    return load_material_texture_map()


def map_materials():
    """material name -> map entry (dict copy is not made; treat as read-only)."""
    return load_material_texture_map()["materials"]


def material_entry(material_name):
    """Full map entry of one vanilla material asset name (raises KeyError)."""
    try:
        return map_materials()[material_name]
    except KeyError:
        raise KeyError("no real-texture patch for vanilla material %r in %s"
                       % (material_name, MAP_PATH))


def patch_for(material_name):
    """Patch record of one vanilla material asset name (raises KeyError)."""
    return material_entry(material_name)["patch"]


def uv_rect_for(material_name):
    """UV rect (u0, v0, u1, v1) of a material's real-texture patch."""
    return tuple(patch_for(material_name)["uv_rect"])


def pixel_rect_for(material_name):
    """Top-left-origin pixel rect (x0, y0, x1, y1) of a material's patch."""
    return tuple(patch_for(material_name)["pixel_rect"])


def patch_size_for(material_name):
    return tuple(patch_for(material_name)["size_px"])


def texture_size_for(material_name):
    return tuple(material_entry(material_name)["texture_size"])


def texture_name_for(material_name):
    return material_entry(material_name)["texture"]


def material_color_tint(material_name):
    return tuple(material_entry(material_name)["material_color_tint"])


# --------------------------------------------------------------------------- #
# ART-07 world-scale UV accessors
# --------------------------------------------------------------------------- #
def mode_for(material_name):
    """``"tile"`` (full-bleed canvas) or ``"patch"`` (curated atlas region)."""
    entry = material_entry(material_name)
    if "mode" not in entry:
        raise RuntimeError(
            "%r has no ART-07 mode - regenerate art/material_texture_map.json "
            "with art/material_texture_analysis.py" % (material_name,))
    return entry["mode"]


def is_tile_mode(material_name):
    return mode_for(material_name) == "tile"


def uv_canvas_for(material_name):
    """§4 canvas record (pixel_rect / uv_rect / size_px / stats)."""
    return dict(material_entry(material_name)["uv_canvas"])


def canvas_uv_rect_for(material_name):
    return tuple(uv_canvas_for(material_name)["uv_rect"])


def canvas_pixel_rect_for(material_name):
    return tuple(uv_canvas_for(material_name)["pixel_rect"])


def canvas_size_px_for(material_name):
    return tuple(uv_canvas_for(material_name)["size_px"])


def px_per_m_for(material_name):
    return float(material_entry(material_name)["px_per_m"])


def coverage_for(material_name):
    entry = material_entry(material_name)
    return (float(entry["coverage_u_m"]), float(entry["coverage_v_m"]))


def tile_meters_for(material_name):
    return material_entry(material_name).get("tile_meters")


def uv_mapping_record():
    return dict(load_material_texture_map().get("uv_mapping") or {})


_WORLD_AXIS_INDEX = {"x": 0, "y": 1, "z": 2}


def expected_uv(material_name, world_xyz, dominant_axis, texture_size=None):
    """Recompute the ART-07 UV of one vertex (contract §2).

    ``world_xyz`` is the vertex in the Blender author space (Z up, front +Y,
    model centred on x/y, z in 0..1); ``dominant_axis`` is ``"X"``/``"Y"``/``"Z"``
    (the face normal's dominant axis, sign irrelevant).  This mirrors
    ``gen_models_v2.assign_world_tiled_uvs`` exactly, so validators and the L1
    tests can re-derive every shipped UV.
    """
    entry = material_entry(material_name)
    width, height = (texture_size or entry["texture_size"])
    projection = entry["projection"]
    axes = projection["axes"]
    key = "+%s" % (str(dominant_axis).upper(),)
    if key not in axes:
        raise KeyError("bad dominant axis %r for %r" % (dominant_axis, material_name))
    u_axis, v_axis = axes[key]
    center_u, center_v = projection["uv_center"]
    center = projection["world_center"]
    density = float(entry["px_per_m"])
    u_index = _WORLD_AXIS_INDEX[u_axis]
    v_index = _WORLD_AXIS_INDEX[v_axis]
    u = center_u + (float(world_xyz[u_index]) - center[u_index]) * density / float(width)
    v = center_v + (float(world_xyz[v_index]) - center[v_index]) * density / float(height)
    return u, v


def patch_class_of(material_name):
    return material_entry(material_name)["patch_class"]


def texture_path_for(material_name, roots=None):
    """Absolute path of the material's real ``_D`` albedo PNG (read-only).

    Resolution order: the recorded absolute path, then the recorded
    ``texture_source`` root from the map, then ``roots`` override, then the
    ``HDBF_VANILLA_TEX_ALL`` / ``HDBF_VANILLA_TEX_EXTRA`` environment variables.
    Returns ``None`` when the texture is not available on this machine (the
    validators then report a SKIP for the statistics re-check but still enforce
    every non-image contract).
    """
    entry = material_entry(material_name)
    candidates = []
    if entry.get("texture_path"):
        candidates.append(entry["texture_path"])
    roots = dict(roots or {})
    map_roots = dict(load_material_texture_map().get("texture_roots") or {})
    source = entry.get("texture_source")
    env_names = {"vanilla-tex-all": "HDBF_VANILLA_TEX_ALL",
                 "vanilla-tex-extra": "HDBF_VANILLA_TEX_EXTRA"}
    if source in map_roots:
        candidates.append(os.path.join(map_roots[source], entry["texture"]))
    if roots.get(source):
        candidates.append(os.path.join(roots[source], entry["texture"]))
    if os.environ.get(env_names.get(source, "")):
        candidates.append(os.path.join(os.environ[env_names[source]],
                                       entry["texture"]))
    for candidate in candidates:
        if candidate and os.path.isfile(candidate):
            return candidate
    return None


def forbidden_materials():
    """Materials the map explicitly bans from large shells (hazard stripes)."""
    policy = load_material_texture_map().get("large_shell_policy") or {}
    return tuple(policy.get("forbidden_materials") or ())


def unused_material_records():
    return load_material_texture_map().get("unused_materials") or {}


def std_limits():
    """(default, class->limit, hard cap) thresholds from the map."""
    thresholds = load_material_texture_map().get("thresholds") or {}
    return (float(thresholds.get("std_max_default", 18.0)),
            dict(thresholds.get("std_max_by_class") or {}),
            float(thresholds.get("hard_std_cap", 30.0)))


def materials_used_by_material_map():
    """Sorted material names that timbermesh_common.MATERIAL_MAP assigns."""
    import timbermesh_common as TC
    used = set()
    for mapping in TC.MATERIAL_MAP.values():
        used.update(mapping.values())
    return sorted(used)


def validate_contract():
    """Raise AssertionError if map + shipped material mapping are inconsistent."""
    data = load_material_texture_map()
    assert data.get("schema") == MAP_SCHEMA, data.get("schema")
    default_std, class_std, hard_cap = std_limits()
    assert hard_cap > 0.0
    import timbermesh_common as TC
    entries = map_materials()
    for material in materials_used_by_material_map():
        assert material in entries, ("MATERIAL_MAP uses %r but the texture map has "
                                     "no patch for it" % (material,))
        record = entries[material]
        assert record.get("material") == material, (material, record.get("material"))
        texture = record.get("texture")
        assert texture and texture.lower().endswith(".png"), (material, texture)
        width, height = record["texture_size"]
        assert width >= 128 and height >= 128, (material, record["texture_size"])
        sha = record.get("texture_sha256") or ""
        assert len(sha) == 64 and all(c in "0123456789abcdef" for c in sha), (
            material, "bad texture_sha256")
        tint = record.get("material_color_tint")
        assert tint and len(tint) == 3 and all(0.0 <= c <= 1.0 for c in tint), (
            material, tint)
        patch = record["patch"]
        x0, y0, x1, y1 = patch["pixel_rect"]
        assert 0 <= x0 < x1 <= width and 0 <= y0 < y1 <= height, (
            material, patch["pixel_rect"], record["texture_size"])
        size_x, size_y = x1 - x0, y1 - y0
        assert list(patch["size_px"]) == [size_x, size_y], (material, "size_px")
        lo, hi = (data.get("thresholds") or {}).get("patch_size_range_px", [128, 512])
        assert lo <= size_x <= hi and lo <= size_y <= hi, (
            material, "patch outside contract size range", patch["size_px"])
        u0, v0, u1, v1 = patch["uv_rect"]
        assert 0.0 <= u0 < u1 <= 1.0 and 0.0 <= v0 < v1 <= 1.0, (
            material, patch["uv_rect"])
        for got, want, what in (
                (u0, x0 / float(width), "u0"),
                (u1, x1 / float(width), "u1"),
                (v0, 1.0 - y1 / float(height), "v0"),
                (v1, 1.0 - y0 / float(height), "v1")):
            assert abs(got - want) <= 1e-5, (material, what, got, want)
        mean = patch["mean_rgb"]
        assert len(mean) == 3 and all(0.0 <= c <= 255.0 for c in mean), (
            material, mean)
        assert "texture" not in (patch.get("reject_tags") or [])
        std = float(patch["std"])
        std_max = float(patch["std_max"])
        assert 0.0 <= std, (material, std)
        assert std <= std_max + 1e-9, (
            "%s patch std %.2f exceeds its budget %.2f" % (material, std, std_max))
        assert std_max <= hard_cap + 1e-9, (
            "%s std_max %.2f exceeds the hard cap %.2f" % (material, std_max, hard_cap))
        if record.get("mode") != "patch" or not patch.get("art07_canvas"):
            # ART-05 uniform small window: the class budget must stay tighter
            # than the hard cap.  ART-07 curated PATCH canvases deliberately
            # carry real surface detail, so their only std gate is the hard cap
            # (checked above); their content safety is the hazard_fraction == 0.
            assert class_std.get(record["patch_class"], default_std) >= std_max - 1e-9, (
                material, "patch_class budget smaller than the material budget")
        # ---- ART-07 world-scale UV contract ----
        mode = record.get("mode")
        assert mode in ("tile", "patch"), (material, "ART-07 mode", mode)
        assert record.get("wrap") == ("repeat" if mode == "tile" else "clamp"), (
            material, "ART-07 wrap", record.get("wrap"))
        canvas = record.get("uv_canvas") or {}
        cx0, cy0, cx1, cy1 = canvas.get("pixel_rect") or (0, 0, 0, 0)
        assert 0 <= cx0 < cx1 <= width and 0 <= cy0 < cy1 <= height, (
            material, "ART-07 uv_canvas outside the texture", canvas)
        assert list(canvas.get("size_px") or []) == [cx1 - cx0, cy1 - cy0], (
            material, "ART-07 canvas size_px")
        cu0, cv0, cu1, cv1 = canvas.get("uv_rect") or (0.0, 0.0, 0.0, 0.0)
        assert abs(cu0 - cx0 / float(width)) <= 1e-5
        assert abs(cu1 - cx1 / float(width)) <= 1e-5
        assert abs(cv0 - (1.0 - cy1 / float(height))) <= 1e-5
        assert abs(cv1 - (1.0 - cy0 / float(height))) <= 1e-5
        if mode == "patch":
            assert [cx0, cy0, cx1, cy1] == list(patch["pixel_rect"]), (
                material, "ART-07 patch canvas must equal the sampled patch rect")
        else:
            assert [cx0, cy0, cx1, cy1] == [0, 0, width, height], (
                material, "ART-07 tile canvas must be the whole texture")
            tile_meters = record.get("tile_meters")
            assert tile_meters is not None and 1.0 < tile_meters < 4.0, (
                material, "ART-07 tile_meters", tile_meters)
        density = float(record["px_per_m"])
        assert density > 0.0
        usable_w = (cx1 - cx0) - 2 * 2
        usable_h = (cy1 - cy0) - 2 * 2
        fit = min(usable_w / float(record["coverage_u_m"]),
                  usable_h / float(record["coverage_v_m"]))
        assert abs(min(fit, density) - density) <= 0.11, (
            material, "ART-07 px_per_m not reproducible", density, fit)
        projection = record.get("projection") or {}
        assert projection.get("world_center") == [0.0, 0.0, 0.5], (
            material, "ART-07 projection centre")
        assert len(projection.get("uv_center") or []) == 2, (
            material, "ART-07 uv_center")
    forbidden = set(forbidden_materials())
    for mapping in TC.MATERIAL_MAP.values():
        overlap = forbidden & set(mapping.values())
        assert not overlap, ("forbidden hazard material used in MATERIAL_MAP: %r"
                             % (sorted(overlap),))
    for material, record in unused_material_records().items():
        stats = record.get("hazard_reference_stats") or {}
        assert float(stats.get("std", 0.0)) > hard_cap, (
            "%s hazard reference std must exceed the hard cap (negative control)"
            % (material,))
    # ART-05/ART-07: the construction stage is ONE shared source mesh + ONE baked
    # UV set exported for both factions.  ART-07 bakes it from the Folktails
    # construction materials, so the shared window is the Folktails material's
    # canvas; the IronTeeth material of the same slot must resolve that window
    # (its own canvas has to contain it).  A regression here silently breaks the
    # IronTeeth construction model in game.
    for slot, ft_material, it_material in construction_slot_pairs():
        ft_uv = canvas_uv_rect_for(ft_material)
        it_uv = canvas_uv_rect_for(it_material)
        assert _rect_contains(it_uv, ft_uv, 1e-6), (
            "construction slot %s: the IronTeeth canvas must contain the shared "
            "Folktails construction window: %r -> %r vs %r -> %r"
            % (slot, ft_material, ft_uv, it_material, it_uv))
    shared = construction_shared_patches()
    for group, info in sorted(shared.items()):
        ft_material = info.get("folktails_material")
        it_material = info.get("ironteeth_material")
        assert ft_material and it_material, (group, "ART-07 shared window pair")
        window = tuple(round(float(v), 9) for v in info["uv_rect"])
        assert window == tuple(round(float(v), 9)
                               for v in canvas_uv_rect_for(ft_material)), (
            group, "shared window must be the Folktails ART-07 canvas")
        assert _rect_contains(canvas_uv_rect_for(it_material), window, 1e-6), (
            group, "IronTeeth canvas must contain the shared construction window")
        for name, stats in sorted((info.get("per_texture") or {}).items()):
            std = float(stats["std"])
            std_max = float(stats["std_max"])
            assert 0.0 <= std <= std_max + 1e-9 <= hard_cap + 1e-9, (
                group, name, "shared-window std budget", std, std_max)
            assert float(stats.get("hazard_fraction", 0.0)) <= 0.005, (
                group, name, "hazard content in the shared construction window")
    return True


def _rect_contains(outer, inner, tol=0.0):
    """True when ``inner`` uv/pixel rect lies inside ``outer`` (both 4-tuples)."""
    return (inner[0] >= outer[0] - tol and inner[1] >= outer[1] - tol
            and inner[2] <= outer[2] + tol and inner[3] <= outer[3] + tol)


def construction_shared_patches():
    """Cross-faction shared construction UV windows recorded in the map."""
    return load_material_texture_map().get("construction_shared_patches") or {}


def construction_slot_pairs():
    """[(slot, folktails material, ironteeth material)] for the construction map."""
    import timbermesh_common as TC
    folktails = TC.MATERIAL_MAP[("Basic", "Folktails", True)]
    ironteeth = TC.MATERIAL_MAP[("Basic", "IronTeeth", True)]
    assert set(folktails) == set(ironteeth), (sorted(folktails), sorted(ironteeth))
    return [(slot, folktails[slot], ironteeth[slot]) for slot in sorted(folktails)]


# --------------------------------------------------------------------------- #
# legacy ART-04 role table - PREVIEW FALLBACK ONLY (deprecated)
# --------------------------------------------------------------------------- #
#: LEGACY / preview fallback: role -> (semantic atlas region, pixel rect).
#: The shipped models no longer use these; they existed for the fake ART-01
#: 2048 atlas that caused the 2026-10-27 in-game texture bug.  Kept importable so
#: old preview tooling and the ART-01 generator contract do not break.
PATCH_PIXELS = {
    "wood_plain":  ("wood",   (96, 352, 480, 736)),
    "wood_light":  ("wood",   (96, 1056, 480, 1440)),
    "planks_light": ("wood",  (96, 1568, 480, 1952)),
    "planks_brown": ("wood",  (528, 352, 784, 608)),
    "planks_dark":  ("wood",  (528, 700, 784, 956)),
    "metal_base":    ("metal", (1056, 384, 1440, 768)),
    "metal_painted": ("metal", (1056, 1088, 1440, 1472)),
    "metal_details": ("metal", (1056, 1568, 1312, 1824)),
    "glass":         ("glass", (1744, 704, 1832, 792)),
    "plaster":       ("plaster", (1872, 288, 2000, 416)),
    "roof":          ("thatch", (1872, 1472, 2000, 1600)),
    "moss":          ("moss",   (1900, 940, 2020, 1060)),
}

#: LEGACY / preview fallback: vanilla material asset name -> ART-04 role.
ROLE_OF_MATERIAL = {
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
    "Moss.Folktails": "moss",
    "BaseWood_DarkBrown.IronTeeth": "wood_plain",
    "BaseWood_Grey.IronTeeth": "wood_light",
    "IrregularPlanks_DarkBrown.IronTeeth": "planks_light",
    "IrregularPlanks_MetalForged.IronTeeth": "metal_details",
    "PaintedMetal.IronTeeth": "metal_painted",
    "BaseMetal.IronTeeth": "metal_base",
    "WindowsAtlas.IronTeeth": "glass",
    "RoofPlanks.IronTeeth": "planks_dark",
    "Plaster_Orange.IronTeeth": "plaster",
    "Details.IronTeeth": "metal_details",
}


def role_of(material_name):
    """LEGACY compatibility: ART-04 flat-patch role of a material (deprecated)."""
    try:
        return ROLE_OF_MATERIAL[material_name]
    except KeyError:
        raise KeyError("no legacy ART-04 flat-patch role for vanilla material %r"
                       % (material_name,))


def uv_rect(role):
    """LEGACY: UV rect (u0, v0, u1, v1) of an ART-04 role patch (preview only)."""
    _region, pixel_rect = PATCH_PIXELS[role]
    return A.px_rect_to_uv(pixel_rect, A.ATLAS_SIZE)


def patch_record(role):
    """LEGACY: JSON-ready ART-04 role patch record (preview fallback only)."""
    region, pixel_rect = PATCH_PIXELS[role]
    u0, v0, u1, v1 = A.px_rect_to_uv(pixel_rect, A.ATLAS_SIZE)
    return {
        "id": role,
        "atlas_region": region,
        "pixel_rect": list(pixel_rect),
        "uv_rect": [round(u0, 6), round(v0, 6), round(u1, 6), round(v1, 6)],
        "texels": [pixel_rect[2] - pixel_rect[0], pixel_rect[3] - pixel_rect[1]],
        "texels_per_grid": PATCH_TEXELS_PER_GRID,
        "legacy": True,
    }


def validate_legacy_contract():
    """LEGACY self-check of the ART-04 role table (kept for compatibility)."""
    seen = {}
    for role, (region, rect) in PATCH_PIXELS.items():
        assert region in A.REGIONS, role
        x0, y0, x1, y1 = rect
        rx0, ry0, rx1, ry1 = A.REGIONS[region][1]
        assert rx0 + 4 <= x0 < x1 <= rx1 - 4, (role, rect, A.REGIONS[region][1])
        assert ry0 + 4 <= y0 < y1 <= ry1 - 4, (role, rect, A.REGIONS[region][1])
        assert x1 - x0 >= 80 and y1 - y0 >= 80, (role, "patch too small for a face")
        for other, orect in seen.items():
            assert (x1 <= orect[0] or orect[2] <= x0 or y1 <= orect[1]
                    or orect[3] <= y0), ("patch rects overlap: %s %s"
                                         % (role, other))
        seen[role] = rect
    for name, role in ROLE_OF_MATERIAL.items():
        assert role in PATCH_PIXELS, (name, role)
    return True


if __name__ == "__main__":
    validate_contract()
    validate_legacy_contract()
    print("material_patches contract OK: %d real-texture materials, %d legacy "
          "roles (fallback only)" % (len(map_materials()), len(PATCH_PIXELS)))
    for material in materials_used_by_material_map():
        entry = material_entry(material)
        patch = entry["patch"]
        print("  %-38s %-30s px=%-20s uv=%-44s mean=(%3.0f,%3.0f,%3.0f) "
              "std=%4.1f/%4.1f"
              % (material, entry["texture"], tuple(patch["pixel_rect"]),
                 tuple(patch["uv_rect"]), *patch["mean_rgb"], patch["std"],
                 patch["std_max"]))
