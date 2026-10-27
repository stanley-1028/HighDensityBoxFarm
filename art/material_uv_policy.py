"""HDBF ART-07 - world-scale UV policy (single source of truth).

The ART-06 contract put every face into a tiny fixed sub-rect of a material's
real ``_D`` texture (``PATCH_TEXELS_PER_GRID = 32``), so a 3 m roof sampled only
~96 px of a 1024 px texture: the user's 2026-10-27 feedback was "as long as the
texture is clear and high quality, even better".  ART-07 replaces that with a
**world-scale planar projection** at the highest texel density that is legal in
the real game.

Why not simply tile the texture with ``UV = world / tile_meters`` (UV > 1,
Repeat)?  Verified from the installed game's IL (see
``art/ART07_UV_TILING.md`` %1 and ``art/ART07_PROVENANCE.md``):

* every material we ship is a fragment of ``FactionAtlas.Folktails`` /
  ``FactionAtlas.IronTeeth`` (``Configuration/AutoAtlaser.blueprint``);
* ``PrefabOptimizationChain`` runs ``TimbermeshPrefabOptimizer`` (which creates
  our ``MeshRenderer``s) **before** ``AutoAtlasingPrefabOptimizer``, and the
  latter rewrites the mesh UVs as ``uv * (1/sizeMultiplier) + cell_offset`` into
  the combined atlas (each fragment keeps its native 1024^2 cell);
* a UV component above 1 therefore leaves the material's own atlas cell and
  samples another material - Repeat would never be reached.

So the shipped UVs must stay inside the material canvas.  The achievable
density for a texture that has to cover the whole 3 m model is
``1024 / 3.0 = 341 px/m`` for a full-bleed ``tile`` texture, and
``canvas_px / 3.0`` for a curated ``patch`` canvas of an atlas-sheet texture.
That is ~10x / ~5x the ART-06 density and the native resolution of the game's
own material atlas.

This module is imported by:

  * ``art/material_texture_analysis.py`` - decorates ``material_texture_map.json``
  * ``art/gen_models_v2.py``             - ``assign_world_tiled_uvs``
  * ``art/material_patches.py``          - accessors for previews/validators
  * the validators and the L1 tests read the values back from the JSON map.

It is deliberately dependency free (json/ math only) so it can run inside
Blender, the dev python and the pure-python validators.
"""

from __future__ import annotations

SCHEMA = "hdbf.world_uv/v1"
GENERATOR = "art/material_uv_policy.py"

#: REQ target band for tile materials (px/m).
TEXEL_DENSITY_BAND = (300.0, 600.0)

#: 2 texel inset on every canvas edge.  The game's auto-atlas has **no gutter**
#: between fragments (``CombineFragments`` copies them edge to edge), so a UV
#: that lands exactly on the cell border bleeds the neighbouring material at
#: bilinear/mipmapped sampling.  The inset keeps every shipped UV strictly
#: inside its own cell.
UV_GUARD_TEXELS = 2

#: Frozen projection centre in the Blender author space (Z up, front +Y):
#: x/y are model-centred, z runs 0..1 so the vertical centre is 0.5.
WORLD_CENTER = [0.0, 0.0, 0.5]

#: Dominant-face-normal -> (U world axis, V world axis) of the planar projection
#: (Blender Z-up author space; 0=x, 1=y, 2=z).
PROJECTION_AXES = {
    "-X": ["y", "z"], "+X": ["y", "z"],
    "-Y": ["x", "z"], "+Y": ["x", "z"],
    "-Z": ["x", "y"], "+Z": ["x", "y"],
}
PROJECTION_NOTE = (
    "uv = uv_center + (world_uv - world_center) * px_per_m / texture_size; "
    "dominant face normal picks the projection plane (+-X -> U=y,V=z; "
    "+-Y -> U=x,V=z; +-Z -> U=x,V=y); no UV ever leaves the material canvas "
    "because the runtime AutoAtlaser remaps uv into the material's atlas cell "
    "(UV>1 would sample another material)")
WRAP_EVIDENCE = (
    "Texture2D.m_WrapMode = 0 (Repeat), m_FilterMode = 2 (Trilinear), "
    "m_Aniso = 16 in resources.assets; Repeat is real but never reached in game "
    "because AutoAtlaser remaps the mesh UV into the material's atlas cell "
    "(see art/ART07_UV_TILING.md section 1)")

# --------------------------------------------------------------------------- #
# material policy table
# --------------------------------------------------------------------------- #
#: ``coverage_u_m`` / ``coverage_v_m`` = the world span the canvas has to cover.
#: Measured as 2*max|coordinate - WORLD_CENTER| over every shipped LOD0/1/2
#: model (geometry is unchanged in ART-07).  The values are rounded UP so the
#: generator's containment assert can never fire on float noise.
_MATERIAL_POLICIES = {
    # ---- atlas-sheet wood (canvas = the vertical-plank field) ----
    "BaseWood_Brown.Folktails": dict(
        mode="patch", patch_class="wood", tile_meters=None,
        canvas_pixel_rect=[4, 4, 512, 444], coverage_u_m=3.0, coverage_v_m=3.0,
        reason="vertical-plank field of the wood sheet; the variant bundles, "
               "plank ends and blank canvas margins of the artefact sheet are "
               "excluded"),
    "BaseWood_DarkBrown.IronTeeth": dict(
        mode="patch", patch_class="wood", tile_meters=None,
        canvas_pixel_rect=[4, 4, 512, 444], coverage_u_m=3.0, coverage_v_m=3.0,
        reason="vertical-plank field of the grey wood sheet (vanilla _Color "
               "tints it dark brown); variant bundles and canvas margins "
               "excluded"),
    "BaseWood_Grey.IronTeeth": dict(
        mode="patch", patch_class="wood", tile_meters=None,
        canvas_pixel_rect=[4, 4, 512, 444], coverage_u_m=3.0, coverage_v_m=3.0,
        reason="vertical-plank field of the grey wood sheet (vanilla _Color "
               "tints it grey); variant bundles and canvas margins excluded"),
    "BaseWood_LightBrown.Folktails": dict(
        mode="patch", patch_class="wood", tile_meters=None,
        canvas_pixel_rect=[4, 4, 512, 444], coverage_u_m=3.0, coverage_v_m=3.0,
        reason="vertical-plank field of the whitewashed wood sheet (vanilla "
               "_Color tints it light brown); canvas margins excluded"),
    "BaseWood_White.Folktails": dict(
        mode="patch", patch_class="wood", tile_meters=None,
        canvas_pixel_rect=[4, 4, 512, 444], coverage_u_m=3.0, coverage_v_m=3.0,
        reason="vertical-plank field of the whitewashed wood sheet (vanilla "
               "_Color tints it pale); canvas margins excluded"),
    # ---- atlas-sheet sheet metal (canvas = the plain worn plate) ----
    "BaseMetal.Folktails": dict(
        mode="patch", patch_class="metal", tile_meters=None,
        canvas_pixel_rect=[8, 8, 504, 504], coverage_u_m=3.0, coverage_v_m=3.0,
        reason="plain worn sheet-metal plate of the metal sheet; the grille, "
               "ribs, hexagon grid and tile blocks are excluded"),
    "BaseMetal.IronTeeth": dict(
        mode="patch", patch_class="metal", tile_meters=None,
        canvas_pixel_rect=[8, 8, 504, 504], coverage_u_m=3.0, coverage_v_m=3.0,
        reason="plain worn sheet-metal plate of the second metal sheet (same "
               "atlas layout, so the shared construction UV set stays valid)"),
    # ---- detail atlas (canvas = the framed plank-panel block) ----
    "Details.Folktails": dict(
        mode="patch", patch_class="details", tile_meters=None,
        canvas_pixel_rect=[4, 516, 508, 1012], coverage_u_m=3.0,
        coverage_v_m=3.0,
        reason="framed dark-timber wall/roof panels of the detail atlas; the "
               "faction banner, sack, flag ribbon and crate half are excluded"),
    "Details.IronTeeth": dict(
        mode="patch", patch_class="details", tile_meters=None,
        canvas_pixel_rect=[4, 516, 508, 1012], coverage_u_m=3.0,
        coverage_v_m=3.0,
        reason="framed dark-timber wall/roof panels of the IronTeeth detail "
               "atlas; the faction banner, sack, flag ribbon and crate half "
               "are excluded"),
    # ---- window atlas (canvas = the flat glass pane) ----
    "WindowsAtlas.Folktails": dict(
        mode="patch", patch_class="glass", tile_meters=None,
        canvas_pixel_rect=[772, 772, 1020, 1020], coverage_u_m=2.96,
        coverage_v_m=2.96,
        reason="flat glass gradient pane (bottom-right of the window atlas); "
               "frames, round windows and the atlas backdrop are excluded"),
    "WindowsAtlas.IronTeeth": dict(
        mode="patch", patch_class="glass", tile_meters=None,
        canvas_pixel_rect=[772, 772, 1020, 1020], coverage_u_m=2.96,
        coverage_v_m=2.96,
        reason="flat glass gradient pane (bottom-right of the window atlas); "
               "frames, round windows and the atlas backdrop are excluded"),
    # ---- full-bleed surface textures: whole 1024^2 is a legal canvas ----
    "Plaster_White.Folktails": dict(
        mode="tile", patch_class="plaster", tile_meters=2.6,
        canvas_pixel_rect=None, coverage_u_m=3.0, coverage_v_m=3.0,
        reason="full-bleed smooth plaster (cream flat roof / plaster walls); "
               "tileable surface texture"),
    "Plaster_Orange.IronTeeth": dict(
        mode="tile", patch_class="plaster", tile_meters=2.6,
        canvas_pixel_rect=None, coverage_u_m=3.0, coverage_v_m=3.0,
        reason="full-bleed smooth plaster (the orange IronTeeth variant is the "
               "vanilla _Color tint); tileable surface texture"),
    "Moss.Folktails": dict(
        mode="tile", patch_class="moss", tile_meters=2.2,
        canvas_pixel_rect=None, coverage_u_m=2.99, coverage_v_m=2.99,
        reason="full-bleed moss/foliage surface; tileable"),
    "RoofPlanks.IronTeeth": dict(
        mode="tile", patch_class="roof_planks", tile_meters=2.2,
        canvas_pixel_rect=None, coverage_u_m=2.85, coverage_v_m=2.85,
        reason="full-bleed roof-plank field (IronTeeth basic roof); tileable"),
    "IrregularPlanks_Brown.Folktails": dict(
        mode="tile", patch_class="wood", tile_meters=2.4,
        canvas_pixel_rect=None, coverage_u_m=3.0, coverage_v_m=3.0,
        reason="full-bleed irregular-plank surface (Top tier timber accents); "
               "tileable"),
    "IrregularPlanks_MetalForged.IronTeeth": dict(
        mode="tile", patch_class="forged_planks", tile_meters=2.2,
        canvas_pixel_rect=None, coverage_u_m=3.0, coverage_v_m=3.0,
        reason="full-bleed forged planks with metal straps (Top tier fabric "
               "slot); tileable"),
}

#: REQ chapter class -> allowed nominal ``tile_meters`` band.  Wood/planks
#: 2.2-2.6, plaster 2.4-2.8, metal 1.8-2.2, roof 2.0-2.4 (moss: 2.2 chosen).
TILE_METERS_BAND = {
    "wood": (2.2, 2.6),
    "planks": (2.2, 2.6),
    "forged_planks": (2.2, 2.6),
    "plaster": (2.4, 2.8),
    "metal": (1.8, 2.2),
    "roof_planks": (2.0, 2.4),
    "moss": (2.0, 2.4),
}

#: std budget for a curated PATCH canvas.  These canvases deliberately contain
#: real detail (plank seams, frame boards, wear), so the ART-06 "uniform small
#: window" class budgets (<=18/255) do not apply; the ART-05 hard cap still does.
PATCH_CANVAS_STD_MAX = 30.0

#: max warning/banner pixel fraction allowed on a mapping canvas.
CANVAS_HAZARD_FRACTION_MAX = 0.005


# --------------------------------------------------------------------------- #
# accessors
# --------------------------------------------------------------------------- #
def materials():
    """Sorted vanilla material names this policy covers."""
    return sorted(_MATERIAL_POLICIES)


def policy_for(material):
    """Policy dict of one vanilla material (raises KeyError)."""
    try:
        return dict(_MATERIAL_POLICIES[material])
    except KeyError:
        raise KeyError(
            "no ART-07 UV policy for vanilla material %r (known: %s)"
            % (material, ", ".join(materials())))


def is_tile(material):
    return policy_for(material)["mode"] == "tile"


def canvas_pixel_rect(material, texture_size=(1024, 1024)):
    """Canvas pixel rect (top-left origin) of one material.

    ``tile`` -> the whole texture; ``patch`` -> the curated coherent region.
    """
    policy = policy_for(material)
    if policy["mode"] == "tile":
        width, height = int(texture_size[0]), int(texture_size[1])
        return [0, 0, width, height]
    rect = list(policy["canvas_pixel_rect"])
    width, height = int(texture_size[0]), int(texture_size[1])
    if not (0 <= rect[0] < rect[2] <= width and 0 <= rect[1] < rect[3] <= height):
        raise ValueError("%s: canvas rect %r outside the %dx%d texture"
                         % (material, rect, width, height))
    return rect


def uv_rect_from_pixel_rect(pixel_rect, width=1024, height=1024):
    """Top-left pixel rect -> bottom-left UV rect (same convention as the map)."""
    x0, y0, x1, y1 = pixel_rect
    return [x0 / float(width), 1.0 - y1 / float(height),
            x1 / float(width), 1.0 - y0 / float(height)]


def uv_canvas(material, texture_size=(1024, 1024)):
    """Canvas record used by the generator + the map."""
    rect = canvas_pixel_rect(material, texture_size)
    width, height = int(texture_size[0]), int(texture_size[1])
    size = [rect[2] - rect[0], rect[3] - rect[1]]
    return {
        "pixel_rect": list(rect),
        "uv_rect": [round(v, 9) for v in uv_rect_from_pixel_rect(rect, width, height)],
        "size_px": list(size),
    }


def usable_canvas_px(material, texture_size=(1024, 1024)):
    """Canvas size minus the 2 texel guard band on every edge (px)."""
    canvas = uv_canvas(material, texture_size)
    return (canvas["size_px"][0] - 2 * UV_GUARD_TEXELS,
            canvas["size_px"][1] - 2 * UV_GUARD_TEXELS)


def nominal_px_per_m(material):
    """Density implied by the REQ tile_meters (tile) / the canvas fit (patch)."""
    policy = policy_for(material)
    if policy["mode"] == "tile":
        return 1024.0 / float(policy["tile_meters"])
    usable_w, usable_h = usable_canvas_px(material)
    return min(usable_w / float(policy["coverage_u_m"]),
               usable_h / float(policy["coverage_v_m"]))


def px_per_m(material):
    """Final uniform texel density of the material (px per world metre).

    ``min(REQ nominal, usable canvas / required world coverage)``, floored to
    0.1 px/m so the value is reproducible from the JSON map in every language.
    """
    policy = policy_for(material)
    usable_w, usable_h = usable_canvas_px(material)
    fit = min(usable_w / float(policy["coverage_u_m"]),
              usable_h / float(policy["coverage_v_m"]))
    density = min(nominal_px_per_m(material), fit)
    return float(int(density * 10.0) / 10.0)


def canvas_meters(material, texture_size=(1024, 1024)):
    """World size [u, v] covered by the full canvas at ``px_per_m`` (m)."""
    canvas = uv_canvas(material, texture_size)
    density = px_per_m(material)
    return [round(canvas["size_px"][0] / density, 4),
            round(canvas["size_px"][1] / density, 4)]


def uv_center(material, texture_size=(1024, 1024)):
    canvas = uv_canvas(material, texture_size)
    u0, v0, u1, v1 = canvas["uv_rect"]
    return [round((u0 + u1) * 0.5, 9), round((v0 + v1) * 0.5, 9)]


def tile_meters_band(material):
    """Allowed ``tile_meters`` band for a tile material (raises for patch)."""
    policy = policy_for(material)
    if policy["mode"] != "tile":
        raise ValueError("%r is not a tile material" % (material,))
    return TILE_METERS_BAND[policy["patch_class"]]


def projection_record():
    return {
        "axes": {key: list(value) for key, value in PROJECTION_AXES.items()},
        "world_center": list(WORLD_CENTER),
        "uv_center": None,      # filled per material by decorate_map_data
        "note": PROJECTION_NOTE,
    }


def canvas_record_for_map(material, texture_size=(1024, 1024),
                          canvas_stats=None):
    """JSON-ready 禮4 canvas record (pixel/uv rect + optional PNG statistics)."""
    texture_size = (int(texture_size[0]), int(texture_size[1]))
    canvas = uv_canvas(material, texture_size)
    record = {
        "pixel_rect": list(canvas["pixel_rect"]),
        "uv_rect": list(canvas["uv_rect"]),
        "size_px": list(canvas["size_px"]),
        "meaning": ("the whole vanilla _D texture (full-bleed tileable surface "
                    "texture)" if is_tile(material) else
                    "curated coherent region of the vanilla _D texture sheet "
                    "(atlas-sheet material)"),
    }
    if canvas_stats is not None:
        record["stats"] = canvas_stats
    return record


def decorate_map_data(map_data, canvas_stats_for=None):
    """In-place ART-07 decoration of ``material_texture_map.json`` data.

    ``canvas_stats_for(material) -> dict`` (optional) returns the decoded-PNG
    statistics of that material's canvas (mean_rgb / std / std_by_channel /
    std_max / hazard_fraction / color_distance / target_rgb) so the validators
    can re-verify the canvas content exactly like C13 does for the probe patch.

    Returns a list of human-readable warnings (empty == contract clean).
    """
    warnings = []
    materials = map_data.get("materials") or {}
    missing = sorted(set(materials) - set(_MATERIAL_POLICIES))
    if missing:
        raise SystemExit("ART-07 policy is missing material(s) %r - update "
                         "art/material_uv_policy.py" % (missing,))
    densities = {}
    for material in sorted(materials):
        entry = materials[material]
        policy = policy_for(material)
        texture_size = tuple(entry.get("texture_size") or (1024, 1024))
        rect = canvas_pixel_rect(material, texture_size)
        if policy["mode"] == "patch":
            if entry.get("patch_class") != policy["patch_class"]:
                warnings.append(
                    "%s: patch_class %r != policy %r"
                    % (material, entry.get("patch_class"), policy["patch_class"]))
            stats = canvas_stats_for(material) if canvas_stats_for else None
            if stats is not None:
                canvas_record = canvas_record_for_map(
                    material, texture_size, stats)
                std = float(stats.get("std", 0.0))
                std_max = float(stats.get("std_max", PATCH_CANVAS_STD_MAX))
                if std > std_max + 1e-9:
                    warnings.append("%s: patch canvas std %.2f > std_max %.2f"
                                    % (material, std, std_max))
                hazard = float(stats.get("hazard_fraction", 0.0))
                if hazard > CANVAS_HAZARD_FRACTION_MAX:
                    warnings.append("%s: patch canvas hazard fraction %.4f"
                                    % (material, hazard))
            else:
                canvas_record = canvas_record_for_map(material, texture_size)
            entry["mode"] = "patch"
            entry["wrap"] = "clamp"
            entry["tile_meters"] = None
            entry["tile_meters_band"] = None
        else:
            band = tile_meters_band(material)
            tile_meters = float(policy["tile_meters"])
            if not (band[0] - 1e-9 <= tile_meters <= band[1] + 1e-9):
                warnings.append("%s: tile_meters %.2f outside REQ band %r"
                                % (material, tile_meters, band))
            if stats_check := (canvas_stats_for(material) if canvas_stats_for else None):
                hazard = float(stats_check.get("hazard_fraction", 0.0))
                if hazard > CANVAS_HAZARD_FRACTION_MAX:
                    warnings.append("%s: tile canvas hazard fraction %.4f"
                                    % (material, hazard))
            canvas_record = canvas_record_for_map(
                material, texture_size,
                canvas_stats_for(material) if canvas_stats_for else None)
            entry["mode"] = "tile"
            entry["wrap"] = "repeat"
            entry["tile_meters"] = tile_meters
            entry["tile_meters_band"] = list(band)
        density = px_per_m(material)
        coverage = (float(policy["coverage_u_m"]), float(policy["coverage_v_m"]))
        entry["texture_size"] = list(texture_size)
        entry["uv_canvas"] = canvas_record
        entry["projection"] = {
            "axes": {key: list(value) for key, value in PROJECTION_AXES.items()},
            "world_center": list(WORLD_CENTER),
            "uv_center": uv_center(material, texture_size),
            "note": PROJECTION_NOTE,
        }
        entry["px_per_m"] = density
        entry["coverage_u_m"] = coverage[0]
        entry["coverage_v_m"] = coverage[1]
        entry["canvas_meters"] = canvas_meters(material, texture_size)
        entry["wrap_evidence"] = WRAP_EVIDENCE
        entry["uv_reason"] = policy["reason"]
        densities[material] = {
            "mode": entry["mode"],
            "tile_meters": entry["tile_meters"],
            "px_per_m": density,
            "canvas_pixels": list(canvas_record["size_px"]),
            "canvas_meters": entry["canvas_meters"],
        }
        if density < TEXEL_DENSITY_BAND[0] and entry["mode"] == "tile":
            warnings.append("%s: tile px_per_m %.1f below REQ band %r"
                            % (material, density, TEXEL_DENSITY_BAND))
        if density <= 64.0:
            warnings.append("%s: px_per_m %.1f not clearly above the retired "
                            "ART-06 density (32 px/m)" % (material, density))
    map_data["uv_mapping"] = {
        "schema": SCHEMA,
        "generator": GENERATOR,
        "contract": "art/ART07_UV_TILING.md",
        "texel_density_target_px_per_m": list(TEXEL_DENSITY_BAND),
        "uv_guard_texels": UV_GUARD_TEXELS,
        "world_center": list(WORLD_CENTER),
        "atlas_cell_safe": True,
        "no_uv_outside_canvas": True,
        "wrap_evidence": WRAP_EVIDENCE,
        "densities": densities,
    }
    return warnings


def validate_policy():
    """Self-check of the table (used by ``python art/material_uv_policy.py``)."""
    problems = []
    for material in materials():
        policy = policy_for(material)
        if policy["mode"] not in ("tile", "patch"):
            problems.append("%s: bad mode %r" % (material, policy["mode"]))
            continue
        if policy["mode"] == "tile":
            if policy["tile_meters"] is None:
                problems.append("%s: tile material without tile_meters"
                                % (material,))
            else:
                band = tile_meters_band(material)
                if not band[0] <= policy["tile_meters"] <= band[1]:
                    problems.append("%s: tile_meters outside band %r"
                                    % (material, band))
        else:
            if policy["canvas_pixel_rect"] is None:
                problems.append("%s: patch material without canvas_pixel_rect"
                                % (material,))
        density = px_per_m(material)
        if not (0.0 < density < 2000.0):
            problems.append("%s: implausible px_per_m %.1f" % (material, density))
        if not (policy["coverage_u_m"] > 0.0 and policy["coverage_v_m"] > 0.0):
            problems.append("%s: bad coverage" % (material,))
    return problems


if __name__ == "__main__":
    problems = validate_policy()
    for problem in problems:
        print("FAIL", problem)
    for material in materials():
        policy = policy_for(material)
        canvas = uv_canvas(material)
        print("%-40s %-5s tile_m=%-5s px/m=%6.1f canvas=%-22s px=%s "
              "canvas_m=%s" % (
                  material, policy["mode"], policy["tile_meters"],
                  px_per_m(material), str(canvas["pixel_rect"]),
                  canvas["size_px"], canvas_meters(material)))
    print("RESULT:", "FAIL" if problems else "OK",
          "(%d materials)" % len(materials()))
    raise SystemExit(1 if problems else 0)

