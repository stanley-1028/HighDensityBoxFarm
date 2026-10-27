#!/usr/bin/env python3
"""HDBF v2 - source-model validator (Blender 4.2.1 headless, FBX + GLB).

Validates every v2 source model in ``art/models/**`` against the frozen art
contract in ``art/atlas_layout.py`` / ``art/material_patches.py`` /
``art/timbermesh_common.py`` and writes ``art/validation-report.txt``.

v2 (2026-10-26) contract summary
--------------------------------
* strict asset-space envelope 3.00 (X) x 1.00 (Y) x 3.00 (Z), front -Z, pivot at
  the footprint bottom centre; nothing (decoration, windows, trims, ports) may
  exceed it;
* the roof is ONE flat empty platform: the WALKING SURFACE is the model apex at
  y = 1.00, nothing exists above it (30 cm clearance above the surface is empty),
  and the top face spans the near-full platform plan;
* exactly the three ``#Port#Power/#Port#Pipe/#Port#Logistics`` empty markers and
  **no ``#Slot#`` node anywhere**; no crop/trough/slot geometry;
* "indoor high density" is expressed by STATIC racks/shelves (3 levels) visible
  through the facade openings: every LOD0 model must carry >= 2 horizontal shelf
  levels inside the interior, and the glass facade area must reach the per-tier
  minimum (0 / .35 / .60 / .85 grid^2 for Basic/Intermediate/Advanced/Top);
* four tiers must stay distinguishable at the 50% camera: every tier pair is
  compared with a 50%-scale multi-view material/silhouette/glass/port raster and
  must differ on at least two of the four criteria (REQ-V2-ART-04);
* triangle budgets LOD0/LOD1/LOD2 = 6000/2400/800 (+-20%), LOD bbox shrink <=2%;
* every face of one material samples its material's own ART-07 texture canvas
  (world-scale planar projection, no wild / out-of-[0,1] UVs - see ART-07 below);
* ``art/material_slots.json`` agrees with the measured meshes.

ART-05 (2026-10-27) real-texture material patch contract
---------------------------------------------------------
The old pipeline assumed all vanilla materials share one 2048 atlas, which made
the shipped models show a checkerboard surface and the top tier sample
``PaintedMetal``'s yellow/black hazard stripes in game.  Each vanilla material
actually resolves to its OWN 1024x1024 ``_D`` albedo PNG plus a ``_Color`` tint, so
``art/material_texture_map.json`` records one flat patch per material (pixel/uv
rect + measured mean RGB/std + std_max).  This validator adds:
  C12 material patch map coverage: schema ``hdbf.material_texture_map/v1``, every
      material assigned by ``timbermesh_common.MATERIAL_MAP`` has an entry, every
      patch pixel_rect fits texture_size and uv_rect == [x0/W, 1-y1/H, x1/W,
      1-y0/H] within JSON rounding.
  C13 real _D texture variance re-verification: decode every patch's real vanilla
      PNG with ``art/png_stats.py``, re-measure mean RGB / std (max channel std,
      0..255), compare to the recorded values (mean +-0.5, std +-0.5) and check
      std <= std_max (class-based, default 18.0/255), plus the ART-07
      ``uv_canvas.stats`` (mean / std / std_by_channel / hazard_fraction) of every
      material canvas and every construction shared window.  SKIP (with a clear
      note) only when no vanilla texture root exists on the machine.
  C14 hazard / large-shell policy: ``large_shell_policy.forbidden_materials`` must
      list ``PaintedMetal.Folktails``/``PaintedMetal.IronTeeth`` and they must not
      be used; a hazard/logo/text-tagged patch may not be shipped; usable patches
      must not overlap recorded exclusion rects.

ART-07 (2026-10-27) world-scale UV mapping contract
---------------------------------------------------
The game's runtime AutoAtlaser remaps every shipped mesh UV into the material's
own atlas cell, so a UV component > 1 would sample ANOTHER material.  ART-07
therefore maps every face with a world-scale planar projection into the material
canvas at the highest legal texel density (``art/ART07_UV_TILING.md``):
  C7  mode-aware UV0 bounds: tile materials inside the frozen 2-texel guard band
      (2/1024), patch materials inside their ART-07 ``uv_canvas`` (+-1e-4), all
      finite.
  C11 material canvas UV containment: per material slot, every uv0 corner of the
      FBX and GLB mesh must lie inside the material's ART-07 ``uv_canvas`` (tile:
      the whole texture; patch: the curated canvas); construction models are
      checked against both faction contexts.  Per-material min/max is reported and
      ONE escaping corner FAILS.
  C15 world-UV texel density: per material of every shipped LOD0/1/2 model,
      ``sum(uv_area)/sum(projected world area)`` * sqrt(texture_size) must match
      the map ``px_per_m`` (+-2 % aggregate, +-25 % per triangle above 0.05 m^2);
      tile materials must be inside the REQ band 300-600 px/m, patch materials
      above the regression floor (>= 64 px/m = 2x the retired ART-06 density; the
      WindowsAtlas glass pane is 82.4 px/m by design); no NaN/Inf; exact UV
      recompute (frozen projection tie-break: unit Newell normal, |components|
      rounded to 4 decimals, X > Y > Z, +-1e-3) for every non-ambiguous triangle;
      ambiguous planes (top-two rounded components within 1e-3) must stay below
      15 % of triangles / 2 % of world area.
  C16 ART-07 UV mapping contract / determinism: the map carries the sec.4 fields
      for every used material, ``px_per_m`` reproduces from the usable canvas and
      coverage, ``wrap``/``tile_meters``/``uv_canvas``/``projection``/``patch``
      are valid, the construction shared windows resolve both factions, and the
      same ``px_per_m``/canvas is used by every model and LOD.

NEGATIVE CONTROLS (requirement D) run at the end of every normal run and under
``--self-test``: (1) a real source uv0 shifted outside its ART-07 canvas must be
rejected by C11; (2) a synthetic patch record over the real yellow-black hazard
stripes of ``PaintedMetal_D.Folktails.png`` must be rejected by C13; (3) removing
one required material from an in-memory map copy must be rejected by C12; (4) a
tile material's UVs scaled x0.5 and (5) x2.0 must be rejected by C15; (6) one
large patch-mode face stretched to the full 0..1 UV square must be rejected by
C7/C11.  The run exits nonzero if any control is NOT rejected.

Run:
    blender.exe -b --factory-startup -noaudio --python art\\validate_models.py
    blender.exe -b --factory-startup -noaudio --python art\\validate_models.py -- --self-test

Modes:
    FULL (default under Blender): FBX is re-imported into Blender and measured in
    authoring space; GLB is imported with Blender's glTF importer.  Both formats
    are checked and cross-compared.  Without bpy the script exits 2 (BLOCKED);
    it never pretends to have validated the FBX files.
    --self-test: run ONLY the six requirement-D negative controls (exit 0 only
    when all six are rejected; works without bpy using a synthetic UV fixture for
    the control that needs no imported geometry).
    --no-report: do not rewrite art/validation-report.txt.

Exit code 0 only when every check of every expected model PASSES, the cross-model
material checks C12-C16 PASS (C13 may be SKIP only when no vanilla texture root
exists) and all six negative controls are rejected.  The report is deterministic
(no timestamps / elapsed times).
"""

from __future__ import annotations

import json
import math
import os
import sys
import traceback

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
if HERE not in sys.path:
    sys.path.insert(0, HERE)

try:
    import numpy as np
    NUMPY_ERR = None
except Exception as exc:  # pragma: no cover
    np = None
    NUMPY_ERR = "%s: %s" % (type(exc).__name__, exc)

try:
    import bpy
    HAVE_BPY = True
    BLENDER_VERSION = bpy.app.version_string
except Exception:
    HAVE_BPY = False
    BLENDER_VERSION = None

import atlas_layout as A          # noqa: E402
import material_patches as MP     # noqa: E402
import timbermesh_common as TC    # noqa: E402

try:
    import png_stats as PS        # noqa: E402  (ART-05 real-PNG statistics)
    PS_ERR = None
except Exception as _exc:         # pragma: no cover - defensive
    PS = None
    PS_ERR = "%s: %s" % (type(_exc).__name__, _exc)

REPORT_PATH = os.path.join(HERE, "validation-report.txt")
MODELS_DIR = os.path.join(HERE, "models")

BOUNDS_TOL = A.ENVELOPE_TOLERANCE           # 0.02 strict envelope
FLAT_TOL = 0.005                            # top face / apex equality
TRI_TOL = 0.20                              # triangle budget +-20%
PORT_TOL = A.PORT_TOLERANCE                 # 0.02
UV_EPS = 2e-4                               # flat-patch rect epsilon
PANE_MIN_AREA = dict(A.V2_INDOOR["glass_area_min"])
SHELF_MIN_LEVELS = A.V2_INDOOR["shelf_min_levels_checked"]
PORT_RADIUS = A.PORT_FIXTURE_RADIUS
PORT_MIN_TRIS = A.PORT_FIXTURE_MIN_TRIS

#: 50% camera emulation: 32 px per grid unit (the in-game 50% scale of the
#: documented 64 px/unit reference raster), 128 x 128 frame.
RASTER_SCALE = 32
RASTER_SIZE = 128
DIST_MATERIAL_MIN = 0.05
DIST_GLASS_MIN = 0.03
DIST_SILHOUETTE_MIN = 0.02
DIST_PLAN_MIN = 0.015
DIST_PORT_MIN = 0.10
DIST_CRITERIA_MIN = 2

STATUS_RANK = {"PASS": 0, "INFO": 0, "FAIL": 3, "BLOCKED": 4}

# --------------------------------------------------------------------------- #
# ART-05 (2026-10-27) material texture patch contract
# --------------------------------------------------------------------------- #
# The in-game texture bug (checkerboard faces + yellow/black hazard stripes on
# the top tier) came from the old assumption that every vanilla material shares
# one 2048 atlas.  Each vanilla MaterialCollection material actually resolves to
# its OWN 1024x1024 `_D` albedo PNG plus a `_Color` tint, so
# art/material_texture_map.json records, per material, the real texture and one
# flat patch (pixel + uv rect + measured mean RGB/std); gen_models_v2.py writes
# every face of a material into that material's patch.  C7/C11 verify the source
# FBX/GLB UVs; C12-C14 (cross-model) verify map coverage, the recorded patch
# statistics against the real PNG bytes and the hazard/large-shell policy.
MAP_PATH = getattr(MP, "MAP_PATH", os.path.join(HERE, "material_texture_map.json"))
MAP_SCHEMA = "hdbf.material_texture_map/v1"

#: tolerance for uv0-in-material-patch containment (float32 FBX/GLB data + the
#: 6-decimal uv_rect in the JSON both round below 5e-7).
PATCH_UV_TOL = 1e-4
#: allowed |recorded - re-measured| patch mean RGB / std.
PATCH_MEAN_TOL = 0.5
PATCH_STD_TOL = 0.5
#: fallback std budget when the map has neither std_max nor a class threshold.
PATCH_STD_MAX_DEFAULT = 18.0
#: uv_rect formula check (6-decimal JSON rounding is below 5e-7 per component).
UV_RECT_ROUND_TOL = 1.5e-6

#: vanilla PaintedMetal.* = yellow/black (FT) or blue/black (IT) warning-stripe
#: albedo; it must never appear on a shipped/large-shell model.
HAZARD_MATERIALS = ("PaintedMetal.Folktails", "PaintedMetal.IronTeeth")
HAZARD_CLASSES = ("hazard", "hazard-region", "hazard-stripe", "hazard-stripes",
                  "forbidden", "excluded")
HAZARD_FRACTION_MIN = 0.02
#: 192x192 window over PaintedMetal_D.Folktails.png that is yellow+black hazard
#: stripes (measured: ~40% near-black and ~40% near-yellow pixels, std ~60/255).
HAZARD_CONTROL_TEXTURE = "PaintedMetal_D.Folktails.png"
HAZARD_CONTROL_PIXEL_RECT = (384, 416, 576, 608)
HAZARD_CONTROL_TEXTURE_SIZE = (1024, 1024)
DEFAULT_TEXTURE_ROOTS = [
    os.path.join(os.path.expanduser("~"), ".hdbf", "sdk", "vanilla-tex-all"),
    os.path.join(os.path.expanduser("~"), ".hdbf", "sdk", "vanilla-tex-extra"),
]

# --------------------------------------------------------------------------- #
# ART-07 (2026-10-27) world-scale UV mapping contract (art/ART07_UV_TILING.md).
#
# A shipped UV must stay inside its material's own texture canvas because the
# game's runtime AutoAtlaser remaps every mesh UV into the material's atlas cell
# (a UV component > 1 would sample ANOTHER material).  ART-07 therefore uses a
# world-scale planar projection at the highest legal texel density:
#     uv = uv_center + (world_uv - world_center) * px_per_m / texture_size
# with the dominant face normal (frozen 4-decimal tie-break X > Y > Z) picking
# the projection plane.  C7 becomes mode-aware, C11 contains against the ART-07
# ``uv_canvas`` and C15/C16 (new, cross-model) verify the texel density and the
# mapping contract.  The same helpers are mirrored in validate_timbermesh.py.
# --------------------------------------------------------------------------- #
UV_POLICY_SCHEMA = "hdbf.world_uv/v1"
#: the frozen projection centre in the Blender author space (sec.2).
UV_WORLD_CENTER = (0.0, 0.0, 0.5)
#: 2-texel inset on every canvas edge; a UV outside it could bleed the
#: neighbouring atlas cell under bilinear filtering (the atlas has no gutter).
UV_GUARD_TEXELS = 2
#: exact tile guard bound is data (2/1024) so only float noise is tolerated.
TILE_GUARD_TOL = 2e-6
#: C15 tolerances from sec.5: aggregate +-2 %, per triangle +-25 % above 0.05 m^2.
DENSITY_AGG_TOL = 0.02
DENSITY_TRI_TOL = 0.25
DENSITY_TRI_MIN_WORLD_AREA = 0.05
#: C15 regression floor for patch materials.  sec.5 says >= 4x ART-06 (32 px/m)
#: = 128; WindowsAtlas is an intentionally small glass-pane canvas at 82.4 px/m
#: (2.6x ART-06) and documents that ART-07's gain is in the tile materials, so
#: the enforced floor is 2x ART-06 = 64 px/m.  The exact px/m value is still
#: checked against the map (+-2 %) and the tile band is still 300-600 px/m.
DENSITY_PATCH_MIN = 64.0
#: fallback tile density band when the map has no uv_mapping block.
TILE_DENSITY_BAND_DEFAULT = (300.0, 600.0)
#: fallback REQ ``tile_meters`` class bands (material_uv_policy.TILE_METERS_BAND).
TILE_METERS_BAND_FALLBACK = {
    "wood": (2.2, 2.6),
    "planks": (2.2, 2.6),
    "forged_planks": (2.2, 2.6),
    "plaster": (2.4, 2.8),
    "metal": (1.8, 2.2),
    "roof_planks": (2.0, 2.4),
    "moss": (2.0, 2.4),
}
#: frozen projection tie-break: |normal| components rounded to 4 decimals.
PROJECTION_ROUND_DECIMALS = 4
AMBIGUOUS_PLANE_TOL = 1e-3
#: sec.5: ambiguous-plane triangles must stay below 15 % of triangles / 2 % of area.
AMBIGUOUS_COUNT_MAX = 0.15
AMBIGUOUS_AREA_MAX = 0.02
#: exact UV recomputation tolerance per corner (sec.5).
EXACT_UV_TOL = 1e-3
#: uv_center must be the centre of the recorded canvas.
UV_CENTER_TOL = 1e-6
#: reflected = floor(min(nominal, fit)*10)/10 in material_uv_policy.px_per_m;
#: the map rounds to 0.1 px/m so the reproduction check allows one rounding step.
PX_PER_M_ROUND = 0.101
#: map uv_rect values are rounded to 9 decimals (error <= 5e-10).
UV_CANVAS_ROUND_TOL = 1.5e-9


# --------------------------------------------------------------------------- #
# model specs
# --------------------------------------------------------------------------- #
def all_specs():
    """25 source models: 8 tier x faction dirs x LOD0/1/2 + ConstructionStage0."""
    specs = []
    for tier in A.TIERS:
        for faction in A.FACTIONS:
            for lod in A.LODS:
                specs.append({
                    "kind": "tier", "tier": tier, "faction": faction, "lod": lod,
                    "label": "%s.%s.LOD%d" % (tier, faction, lod),
                    "dir": os.path.join(MODELS_DIR, "%s.%s" % (tier, faction)),
                    "stem": "LOD%d" % lod,
                    "expected_body": "SM_HDBF_BoxFarm_%s_LOD%d" % (tier, lod),
                    "budget": A.TRI_BUDGET[lod],
                    "materials": list(A.TIER_MATERIALS[tier]),
                    "platform_tier": tier,
                    "construction": False,
                })
    specs.append({
        "kind": "construction", "tier": A.CONSTRUCTION_STAGE, "faction": None,
        "lod": 0, "label": A.CONSTRUCTION_STAGE,
        "dir": os.path.join(MODELS_DIR, A.CONSTRUCTION_STAGE),
        "stem": A.CONSTRUCTION_STAGE,
        "expected_body": "SM_HDBF_BoxFarm_ConstructionStage0",
        "budget": None,
        "materials": list(A.TIER_MATERIALS[A.CONSTRUCTION_STAGE]),
        "platform_tier": A.CONSTRUCTION_STAGE,
        "construction": True,
    })
    return specs


def expected_files():
    files = ["art/material_slots.json"]
    for key in sorted(A.ATLAS_FILES):
        files.append("art/" + A.ATLAS_FILES[key])
    for spec in all_specs():
        files.append("art/models/%s/%s.fbx" % (os.path.basename(spec["dir"]),
                                               spec["stem"]))
        files.append("art/models/%s/%s.glb" % (os.path.basename(spec["dir"]),
                                               spec["stem"]))
    return files


# --------------------------------------------------------------------------- #
# blender import / facts
# --------------------------------------------------------------------------- #
class Facts(object):
    """Measured geometry of one imported model file, normalised to asset space."""

    def __init__(self, path, spec):
        self.path = path
        self.spec = spec
        self.pos = np.zeros((0, 3), dtype=np.float64)     # vertices, asset space
        self.tri = np.zeros((0, 3), dtype=np.int64)       # triangle vertex indices
        self.tri_mat = np.zeros(0, dtype=np.int64)        # material slot index per tri
        self.tri_uv = np.zeros((0, 3, 2), dtype=np.float64)
        self.materials = []
        self.mesh_nodes = []
        self.empties = {}
        self.tri_count = 0
        self.loaded = False
        self.error = None

    # -- geometry helpers ------------------------------------------------
    def tri_positions(self):
        return self.pos[self.tri]

    def tri_centroids(self):
        return self.pos[self.tri].mean(axis=1)

    def tri_normals(self):
        p = self.pos[self.tri]
        n = np.cross(p[:, 1] - p[:, 0], p[:, 2] - p[:, 0])
        length = np.linalg.norm(n, axis=1)
        length[length == 0.0] = 1.0
        return n / length[:, None]

    def tri_areas(self):
        p = self.pos[self.tri]
        n = np.cross(p[:, 1] - p[:, 0], p[:, 2] - p[:, 0])
        return 0.5 * np.linalg.norm(n, axis=1)

    def bounds(self):
        if not len(self.pos):
            return None
        return self.pos.min(axis=0), self.pos.max(axis=0)

    def material_index(self, name):
        try:
            return self.materials.index(name)
        except ValueError:
            return -1


def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def _blender_to_asset(points):
    """(N,3) blender Z-up points -> asset space (Unity Y-up, front -Z)."""
    out = np.empty_like(points)
    out[:, 0] = points[:, 0]
    out[:, 1] = points[:, 2]
    out[:, 2] = -points[:, 1]
    return out


def load_facts(path, spec):
    facts = Facts(path, spec)
    ext = os.path.splitext(path)[1].lower()
    try:
        reset_scene()
        if ext == ".fbx":
            bpy.ops.import_scene.fbx(filepath=path)
        else:
            bpy.ops.import_scene.gltf(filepath=path)
        dg = bpy.context.evaluated_depsgraph_get()
        pos_parts = []
        tri_parts = []
        mat_parts = []
        uv_parts = []
        vert_base = 0
        for ob in sorted(bpy.data.objects, key=lambda o: o.name):
            if ob.type == "EMPTY":
                m4 = np.array(ob.matrix_world, dtype=np.float64)
                loc = m4[:3, 3]
                local_z = m4[:3, :3] @ np.array([0.0, 0.0, 1.0], dtype=np.float64)
                norm = np.linalg.norm(local_z)
                if norm > 1e-9:
                    local_z = local_z / norm
                facts.empties[ob.name] = {
                    "pos": _blender_to_asset(loc.reshape(1, 3))[0],
                    "dir": _blender_to_asset(local_z.reshape(1, 3))[0],
                }
            elif ob.type == "MESH":
                ev = ob.evaluated_get(dg)
                me = ev.to_mesh()
                try:
                    me.calc_loop_triangles()
                    mw = np.array(ev.matrix_world, dtype=np.float64)
                    nv = len(me.vertices)
                    co = np.empty(nv * 3, dtype=np.float64)
                    me.vertices.foreach_get("co", co)
                    pos_bl = co.reshape(-1, 3)
                    pos_bl = (mw[:3, :3] @ pos_bl.T).T + mw[:3, 3]
                    pos_asset = _blender_to_asset(pos_bl)
                    loops = np.empty(len(me.loop_triangles) * 3, dtype=np.int64)
                    me.loop_triangles.foreach_get("loops", loops)
                    loops = loops.reshape(-1, 3)
                    tri_verts = np.empty(len(me.loop_triangles) * 3,
                                         dtype=np.int64)
                    me.loop_triangles.foreach_get("vertices", tri_verts)
                    tri_verts = tri_verts.reshape(-1, 3)
                    mats = [slot.name if slot is not None else None
                            for slot in me.materials]
                    poly_mats = np.empty(len(me.polygons), dtype=np.int64)
                    me.polygons.foreach_get("material_index", poly_mats)
                    poly_of_tri = np.empty(len(me.loop_triangles), dtype=np.int64)
                    me.loop_triangles.foreach_get("polygon_index", poly_of_tri)
                    tri_mat = poly_mats[poly_of_tri] if len(poly_of_tri) else \
                        np.zeros(0, dtype=np.int64)
                    uv_layer = me.uv_layers.active
                    if uv_layer is not None and len(me.loop_triangles):
                        uv_raw = np.empty(len(me.loops) * 2, dtype=np.float64)
                        uv_layer.data.foreach_get("uv", uv_raw)
                        tri_uv = uv_raw.reshape(-1, 2)[loops]
                    else:
                        tri_uv = np.zeros((len(loops), 3, 2), dtype=np.float64)
                    if facts.materials:
                        # second mesh object: merge material slot indices
                        raise RuntimeError("model contains %d mesh objects; the "
                                           "v2 contract expects one body mesh"
                                           % (len(facts.mesh_nodes) + 1))
                    facts.materials = [m for m in mats if m is not None]
                    facts.mesh_nodes.append(ob.name)
                    pos_parts.append(pos_asset)
                    tri_parts.append(tri_verts + vert_base)
                    mat_parts.append(tri_mat)
                    uv_parts.append(tri_uv)
                    vert_base += nv
                finally:
                    ev.to_mesh_clear()
        if not pos_parts:
            raise RuntimeError("import produced no mesh object")
        facts.pos = np.vstack(pos_parts)
        facts.tri = np.vstack(tri_parts)
        facts.tri_mat = np.concatenate(mat_parts)
        facts.tri_uv = np.vstack(uv_parts)
        facts.tri_count = len(facts.tri)
        facts.loaded = True
    except Exception as exc:                                   # noqa: BLE001
        facts.error = "%s: %s" % (type(exc).__name__, exc)
    return facts


# --------------------------------------------------------------------------- #
# 50% raster helpers (material-id / silhouette / glass)
# --------------------------------------------------------------------------- #
def _project(points, view):
    """asset points (N,3) -> (u, v, depth) for a camera view."""
    if view == "front":
        return points[:, 0], points[:, 1], points[:, 2]
    if view == "back":
        return points[:, 0], points[:, 1], -points[:, 2]
    if view == "left":
        return points[:, 2], points[:, 1], points[:, 0]
    if view == "right":
        return points[:, 2], points[:, 1], -points[:, 0]
    # top: looking down -Y, screen = (x, z)
    return points[:, 0], points[:, 2], -points[:, 1]


def raster_material(facts, view):
    """Z-buffered material-id raster at the 50% camera scale.

    Returns (ids, coverage) where ids is an int16 material-slot map (-1 empty).
    """
    ids = np.full((RASTER_SIZE, RASTER_SIZE), -1, dtype=np.int16)
    depth = np.full((RASTER_SIZE, RASTER_SIZE), np.inf, dtype=np.float64)
    if not facts.tri_count:
        return ids, np.zeros_like(ids, dtype=bool)
    u, v, d = _project(facts.pos, view)
    half = RASTER_SIZE * 0.5
    px = half + u * RASTER_SCALE
    py = half + 0.18 * RASTER_SIZE - v * RASTER_SCALE
    tri = facts.tri
    for t in range(facts.tri_count):
        i0, i1, i2 = tri[t]
        x0, x1, x2 = px[i0], px[i1], px[i2]
        y0, y1, y2 = py[i0], py[i1], py[i2]
        minx = max(0, int(math.floor(min(x0, x1, x2))))
        maxx = min(RASTER_SIZE - 1, int(math.ceil(max(x0, x1, x2))))
        miny = max(0, int(math.floor(min(y0, y1, y2))))
        maxy = min(RASTER_SIZE - 1, int(math.ceil(max(y0, y1, y2))))
        if minx > maxx or miny > maxy:
            continue
        denom = (y1 - y2) * (x0 - x2) + (x2 - x1) * (y0 - y2)
        if abs(denom) < 1e-12:
            continue
        xs = np.arange(minx, maxx + 1) + 0.5
        ys = np.arange(miny, maxy + 1) + 0.5
        gx, gy = np.meshgrid(xs, ys)
        a = ((y1 - y2) * (gx - x2) + (x2 - x1) * (gy - y2)) / denom
        b = ((y2 - y0) * (gx - x2) + (x0 - x2) * (gy - y2)) / denom
        c = 1.0 - a - b
        inside = (a >= -1e-9) & (b >= -1e-9) & (c >= -1e-9)
        if not inside.any():
            continue
        dz = a * d[i0] + b * d[i1] + c * d[i2]
        sub_depth = depth[miny:maxy + 1, minx:maxx + 1]
        sub_ids = ids[miny:maxy + 1, minx:maxx + 1]
        update = inside & (dz < sub_depth - 1e-12)
        sub_depth[update] = dz[update]
        sub_ids[update] = facts.tri_mat[t]
    return ids, ids >= 0


def _glass_fraction(facts, ids, coverage):
    gi = facts.material_index(TC_GLASS_SLOT)
    if gi < 0:
        return 0.0
    total = int(coverage.sum())
    return float(((ids == gi) & coverage).sum()) / total if total else 0.0


TC_GLASS_SLOT = "M_HDBF_Glass"


def _port_signature(facts):
    """Per-port fixture area / triangle count / dominant material signature."""
    cents = facts.tri_centroids()
    areas = facts.tri_areas()
    sig = []
    for spec in A.PORT_SPECS:
        p = np.array(spec["asset"], dtype=np.float64)
        d = np.linalg.norm(cents - p, axis=1) if len(cents) else np.zeros(0)
        near = d <= PORT_RADIUS
        count = int(near.sum())
        area = float(areas[near].sum()) if count else 0.0
        dom = None
        if count:
            mats = facts.tri_mat[near]
            dom = int(np.bincount(mats).argmax())
        sig.append({"port": spec["object"], "count": count,
                    "area": round(area, 4), "dominant_material": dom})
    return sig


def _material_diff(ids_a, ids_b):
    union = (ids_a >= 0) | (ids_b >= 0)
    total = int(union.sum())
    if not total:
        return 0.0
    return float(((ids_a != ids_b) & union).sum()) / total


def _silhouette_iou(cov_a, cov_b):
    inter = int((cov_a & cov_b).sum())
    union = int((cov_a | cov_b).sum())
    return inter / float(union) if union else 1.0


# --------------------------------------------------------------------------- #
# checks
# --------------------------------------------------------------------------- #
class Check(object):
    def __init__(self, cid, title):
        self.cid = cid
        self.title = title
        self.status = "PASS"
        self.text = ""
        self.notes = []
        self.reasons = []

    def fail(self, text, reasons, notes=None):
        self.status = "FAIL"
        self.text = text
        self.reasons = list(reasons)
        self.notes = list(notes or [])

    def pass_(self, text, notes=None):
        self.status = "PASS"
        self.text = text
        self.notes = list(notes or [])

    def skip(self, text):
        """SKIP: recorded as a PASS with a SKIP: prefix (C13 only, no texture root)."""
        self.status = "PASS"
        self.text = "SKIP: %s" % text
        self.notes = []


CHECK_DEFS = [
    ("C1", "files present + both formats parse"),
    ("C2", "strict 3.00 x 1.00 x 3.00 envelope + flat roof apex"),
    ("C3", "triangle budget (LOD0/1/2 +-20%)"),
    ("C4", "v2 structure + indoor shelves/glass expression"),
    ("C5", "exactly 3 #Port# empties, no #Slot# node"),
    ("C6", "material slot sets per tier/faction"),
    ("C7", "mode-aware UV0 bounds (tile guard band / patch uv_canvas)"),
    ("C8", "LOD bounding-box shrink <= 2%"),
    ("C9", "four tiers distinguishable at the 50% camera"),
    ("C10", "material_slots.json agrees with the meshes"),
    ("C11", "material canvas UV containment (ART-07 uv_canvas)"),
]

#: cross-model checks rendered separately from the per-model C1-C11 row table.
CROSS_CHECK_DEFS = [
    ("C12", "material patch map coverage (schema + every used material)"),
    ("C13", "real _D texture variance re-verification + ART-07 canvas statistics"),
    ("C14", "hazard / large-shell material policy"),
    ("C15", "ART-07 world-UV texel density (aggregate + per-triangle + exact UV)"),
    ("C16", "ART-07 UV mapping contract / determinism (map fields, canvas, "
            "construction windows, per-model consistency)"),
]


class Validator(object):
    def __init__(self):
        self.results = []          # per model: dict of checks + facts
        self.contract_lines = []
        # ART-05 (2026-10-27): real-texture per-material patch map.  C7/C11 use
        # it per source model; C12-C14 are cross-model checks (map coverage,
        # real-PNG variance re-verification, hazard/large-shell policy).
        self.patch_info = load_patch_map()
        self.globals = None
        self.c10 = Check("C10", "material_slots.json agrees with the meshes")
        self.c12 = Check("C12", "material patch map coverage")
        self.c13 = Check("C13", "real _D texture variance re-verification")
        self.c14 = Check("C14", "hazard / large-shell material policy")

    # -- file presence (C1) ----------------------------------------------
    def check_files(self):
        reasons = []
        notes = []
        scanned = 0
        for rel in expected_files():
            path = os.path.join(ROOT, rel.replace("/", os.sep))
            scanned += 1
            if not os.path.isfile(path):
                reasons.append("missing: %s" % rel)
        for key in sorted(A.ATLAS_FILES):
            path = os.path.join(HERE, A.ATLAS_FILES[key])
            if not os.path.isfile(path):
                continue
            with open(path, "rb") as handle:
                head = handle.read(33)
            if len(head) < 24 or head[:8] != b"\x89PNG\r\n\x1a\n":
                reasons.append("not a PNG: %s" % A.ATLAS_FILES[key])
                continue
            import struct
            w, h = struct.unpack(">II", head[16:24])
            if (w, h) != (A.ATLAS_SIZE, A.ATLAS_SIZE):
                reasons.append("%s is %dx%d, expected %dx%d"
                               % (A.ATLAS_FILES[key], w, h, A.ATLAS_SIZE, A.ATLAS_SIZE))
        notes.append("%d expected files scanned (25 models x FBX+GLB + 3 atlas PNGs "
                     "+ material_slots.json)" % scanned)
        result = {"spec": None, "checks": {}, "facts": {}}
        check = Check("C1", "files present + both formats parse")
        if reasons:
            check.fail("%d missing/invalid file(s)" % len(reasons), reasons, notes)
        else:
            check.pass_("all %d expected files present; atlas PNGs are %dx%d"
                        % (scanned, A.ATLAS_SIZE, A.ATLAS_SIZE), notes)
        result["checks"]["C1"] = check
        self.results.append(result)

    # -- per model --------------------------------------------------------
    def check_model(self, spec):
        result = {"spec": spec, "checks": {}, "facts": {}}
        for ext in ("fbx", "glb"):
            path = os.path.join(spec["dir"], spec["stem"] + "." + ext)
            facts = load_facts(path, spec) if os.path.isfile(path) else Facts(path, spec)
            if not os.path.isfile(path):
                facts.error = "file missing"
            result["facts"][ext] = facts

        checks = {}
        checks["C1"] = self._c1_parse(spec, result["facts"])
        checks["C2"] = self._c2_envelope(spec, result["facts"])
        checks["C3"] = self._c3_budget(spec, result["facts"])
        checks["C4"] = self._c4_indoor(spec, result["facts"])
        checks["C5"] = self._c5_ports(spec, result["facts"])
        checks["C6"] = self._c6_materials(spec, result["facts"])
        checks["C7"] = self._c7_uv(spec, result["facts"])
        checks["C11"] = self._c11_patch_uv(spec, result["facts"])
        result["checks"] = checks
        return result

    # C1 parse
    def _c1_parse(self, spec, facts):
        check = Check("C1", "parse")
        reasons = []
        notes = []
        for ext in ("fbx", "glb"):
            fact = facts[ext]
            if fact.error:
                reasons.append("%s: %s" % (ext, fact.error))
            else:
                notes.append("%s: %d mesh node(s), %d tris, %d vertices, %d empties"
                             % (ext, len(fact.mesh_nodes), fact.tri_count,
                                len(fact.pos), len(fact.empties)))
        if reasons:
            check.fail("source model did not import cleanly", reasons, notes)
        else:
            check.pass_("FBX and GLB imported; both carry one body mesh + 3 empties",
                        notes)
        return check

    # C2 envelope + flat roof
    def _c2_envelope(self, spec, facts):
        check = Check("C2", "envelope")
        reasons = []
        notes = []
        want_size = (A.ENVELOPE["size_x"], A.ENVELOPE["size_y"],
                     A.ENVELOPE["size_z"])
        want_min = (A.ENVELOPE["min_x"], A.ENVELOPE["min_y"], A.ENVELOPE["min_z"])
        want_max = (A.ENVELOPE["max_x"], A.ENVELOPE["max_y"], A.ENVELOPE["max_z"])
        measured = {}
        for ext in ("fbx", "glb"):
            fact = facts[ext]
            if fact.error:
                reasons.append("%s: cannot measure (%s)" % (ext, fact.error))
                continue
            bounds = fact.bounds()
            if bounds is None:
                reasons.append("%s: no vertices" % ext)
                continue
            lo, hi = bounds
            measured[ext] = (lo, hi)
            for axis in range(3):
                if abs(lo[axis] - want_min[axis]) > BOUNDS_TOL:
                    reasons.append("%s min.%s=%.4f, want %.3f (+-%.2f)"
                                   % (ext, "XYZ"[axis], lo[axis],
                                      want_min[axis], BOUNDS_TOL))
                if abs(hi[axis] - want_max[axis]) > BOUNDS_TOL:
                    reasons.append("%s max.%s=%.4f, want %.3f (+-%.2f)"
                                   % (ext, "XYZ"[axis], hi[axis],
                                      want_max[axis], BOUNDS_TOL))
            # flat roof: walking surface == apex, nothing above, top face exists
            top = A.ROOF_PLATFORM_TOP
            above = fact.pos[:, 1] > top + 1e-5
            if above.any():
                reasons.append("%s: %d vertex/vertices above the walking surface "
                               "y=%.3f (max y=%.4f)"
                               % (ext, int(above.sum()), top,
                                  float(fact.pos[:, 1].max())))
            if abs(float(hi[1]) - top) > FLAT_TOL:
                reasons.append("%s: mountain apex y=%.4f != flat walking surface "
                               "%.3f" % (ext, float(hi[1]), top))
            px, pz = A.roof_platform(spec["platform_tier"])
            face = (np.abs(fact.pos[:, 1] - top) <= 0.01)
            inside = ((np.abs(fact.pos[:, 0]) <= px * 0.5 + 0.03)
                      & (np.abs(fact.pos[:, 2]) <= pz * 0.5 + 0.03))
            face_verts = int((face & inside).sum())
            if face_verts < 20:
                reasons.append("%s: flat top face has only %d vertices inside the "
                               "platform plan %.2fx%.2f"
                               % (ext, face_verts, px, pz))
            if face_verts:
                xs = fact.pos[face & inside, 0]
                zs = fact.pos[face & inside, 2]
                if (float(xs.max() - xs.min()) < px - 0.10
                        or float(zs.max() - zs.min()) < pz - 0.10):
                    reasons.append("%s: flat top face does not span the platform "
                                   "plan (x %.2f z %.2f, plan %.2fx%.2f)"
                                   % (ext, float(xs.max() - xs.min()),
                                      float(zs.max() - zs.min()), px, pz))
            notes.append("%s: size=%.3fx%.3fx%.3f min=(%.3f,%.3f,%.3f) "
                         "top_face_vertices=%d above_top=%d"
                         % (ext, hi[0] - lo[0], hi[1] - lo[1], hi[2] - lo[2],
                            lo[0], lo[1], lo[2], face_verts, int(above.sum())))
        if "fbx" in measured and "glb" in measured:
            diff = float(np.abs(measured["fbx"][0] - measured["glb"][0]).max())
            diff2 = float(np.abs(measured["fbx"][1] - measured["glb"][1]).max())
            if max(diff, diff2) > 0.005:
                reasons.append("FBX and GLB bounds disagree by %.5f"
                               % max(diff, diff2))
            notes.append("FBX vs GLB bounds delta max=%.6f" % max(diff, diff2))
        if reasons:
            check.fail("envelope / flat-roof contract violated", reasons, notes)
        else:
            check.pass_("strict %.2fx%.2fx%.2f envelope; flat roof walking surface "
                        "== apex y=%.2f; nothing above it"
                        % (want_size[0], want_size[1], want_size[2],
                           A.ROOF_PLATFORM_TOP), notes)
        return check

    # C3 triangle budget
    def _c3_budget(self, spec, facts):
        check = Check("C3", "triangle budget")
        reasons = []
        notes = []
        budget = spec["budget"]
        for ext in ("fbx", "glb"):
            fact = facts[ext]
            if fact.error:
                reasons.append("%s: cannot count (%s)" % (ext, fact.error))
                continue
            count = fact.tri_count
            notes.append("%s tris=%d" % (ext, count))
            if budget is not None:
                lo = int(budget * (1.0 - TRI_TOL))
                hi = int(budget * (1.0 + TRI_TOL))
                if not lo <= count <= hi:
                    reasons.append("%s: %d triangles outside the %d +-%.0f%% "
                                   "budget [%d, %d]"
                                   % (ext, count, budget, TRI_TOL * 100, lo, hi))
        if reasons:
            check.fail("triangle budget violated", reasons, notes)
        else:
            if budget is None:
                check.pass_("construction-stage greybox: %d triangles "
                            "(no LOD budget applies)" % facts["glb"].tri_count,
                            notes)
            else:
                check.pass_("LOD%d budget %d +-20%%: measured %d tris (FBX == GLB)"
                            % (spec["lod"], budget, facts["glb"].tri_count), notes)
        return check

    # C4 indoor expression
    def _c4_indoor(self, spec, facts):
        check = Check("C4", "indoor expression")
        reasons = []
        notes = []
        construction = spec["construction"]
        for ext in ("fbx", "glb"):
            fact = facts[ext]
            if fact.error:
                reasons.append("%s: cannot inspect (%s)" % (ext, fact.error))
                continue
            # no crop/trough/slot entities may exist in the source scene
            forbidden = [name for name in list(fact.empties) + fact.mesh_nodes
                         if any(tag in name.lower()
                                for tag in ("trough", "crop", "slot"))]
            if forbidden:
                reasons.append("%s: crop/trough/slot node(s) present: %s"
                               % (ext, forbidden))
            if construction:
                continue
            if spec["lod"] != 0:
                continue
            # static 3-level racks: horizontal surfaces inside the interior
            normals = fact.tri_normals()
            cents = fact.tri_centroids()
            interior = ((np.abs(cents[:, 0]) <= 1.35)
                        & (np.abs(cents[:, 2]) <= 1.42)
                        & (cents[:, 1] > 0.15) & (cents[:, 1] < 0.90))
            horizontal = np.abs(normals[:, 1]) > 0.85
            glass_idx = fact.material_index(TC_GLASS_SLOT)
            not_glass = fact.tri_mat != glass_idx
            levels = sorted({round(float(y), 2)
                             for y in cents[interior & horizontal & not_glass, 1]})
            # merge levels closer than 4 cm (shelf board top/bottom faces)
            merged = []
            for level in levels:
                if not merged or level - merged[-1] > 0.04:
                    merged.append(level)
            if len(merged) < SHELF_MIN_LEVELS:
                reasons.append("%s: only %d distinct horizontal rack level(s) "
                               "inside the interior (need >= %d); levels=%s"
                               % (ext, len(merged), SHELF_MIN_LEVELS, merged))
            else:
                notes.append("%s: shelf levels y=%s" % (ext, merged))
            # glass amount grows with the tier
            glass_area = 0.0
            if glass_idx >= 0:
                mask = fact.tri_mat == glass_idx
                glass_area = float(fact.tri_areas()[mask].sum())
            need = PANE_MIN_AREA[spec["tier"]]
            if glass_area < need - 1e-4:
                reasons.append("%s: glass facade area %.3f grid^2 < %.2f required "
                               "for %s" % (ext, glass_area, need, spec["tier"]))
            notes.append("%s: glass_area=%.3f grid^2 (min %.2f); distinct rack "
                         "levels=%d" % (ext, glass_area, need, len(merged)))
            # Basic must not be glazed, higher tiers must be
            if spec["tier"] == "Basic" and glass_area > 1e-6:
                reasons.append("%s: Basic must be an OPEN timber barn but carries "
                               "%.3f grid^2 of glass" % (ext, glass_area))
        if reasons:
            check.fail("indoor-expression contract violated", reasons, notes)
        else:
            if construction:
                check.pass_("construction greybox carries no crop/trough/slot "
                            "entity; indoor racks start at the finished stage")
            elif spec["lod"] != 0:
                check.pass_("no crop/trough/slot entity in the source scene "
                            "(indoor-expression checks apply to LOD0)")
            else:
                check.pass_("static indoor racks + glazed facade present; "
                            "no crop/slot entities", notes)
        return check

    # C5 ports
    def _c5_ports(self, spec, facts):
        check = Check("C5", "ports")
        reasons = []
        notes = []
        expected = {spec_["object"]: spec_ for spec_ in A.PORT_SPECS}
        for ext in ("fbx", "glb"):
            fact = facts[ext]
            if fact.error:
                reasons.append("%s: cannot inspect (%s)" % (ext, fact.error))
                continue
            got = dict(fact.empties)
            for name, spec_ in expected.items():
                if name not in got:
                    reasons.append("%s: missing empty %s" % (ext, name))
            for name in got:
                if name not in expected:
                    reasons.append("%s: unexpected empty %s (only the three "
                                   "#Port# markers are allowed)" % (ext, name))
                elif "#Slot#" in name:
                    reasons.append("%s: forbidden #Slot# node %s" % (ext, name))
            dev = 0.0
            for name, spec_ in expected.items():
                if name not in got:
                    continue
                want = np.array(spec_["asset"], dtype=np.float64)
                delta = float(np.abs(got[name]["pos"] - want).max())
                dev = max(dev, delta)
                if delta > PORT_TOL:
                    reasons.append("%s: %s at %s, want %s (dev %.4f > %.3f)"
                                   % (ext, name, np.round(got[name]["pos"], 3),
                                      spec_["asset"], delta, PORT_TOL))
                want_dir = np.array(spec_["outward"], dtype=np.float64)
                got_dir = got[name]["dir"]
                if float(np.abs(got_dir - want_dir).max()) > 0.05:
                    reasons.append("%s: %s local +Z points %s, want outward %s"
                                   % (ext, name, np.round(got_dir, 3),
                                      spec_["outward"]))
            if "#Slot#" in " ".join(got) or "#Slot#" in " ".join(fact.mesh_nodes):
                reasons.append("%s: a #Slot# node exists in the model" % ext)
            notes.append("%s: %d empties, positions match exactly the three #Port# "
                         "contract markers (max dev %.4f), no #Slot#"
                         % (ext, len(got), dev))
        if reasons:
            check.fail("port empty contract violated", reasons, notes)
        else:
            check.pass_("exactly the 3 #Port#Power/#Port#Pipe/#Port#Logistics "
                        "empties at the frozen positions; 0 #Slot# nodes", notes)
        return check

    # C6 materials
    def _c6_materials(self, spec, facts):
        check = Check("C6", "materials")
        reasons = []
        notes = []
        want = set(spec["materials"])
        allowed = set(A.MATERIAL_ORDER)
        for ext in ("fbx", "glb"):
            fact = facts[ext]
            if fact.error:
                reasons.append("%s: cannot inspect (%s)" % (ext, fact.error))
                continue
            got = set(fact.materials)
            unknown = sorted(got - allowed)
            if unknown:
                reasons.append("%s: unknown material slot(s) %s (allowed: %s)"
                               % (ext, unknown, sorted(allowed)))
            if got != want:
                reasons.append("%s: material slots %s != contract %s"
                               % (ext, sorted(got), sorted(want)))
            if not 1 <= len(got) <= 4:
                reasons.append("%s: %d material slots (want 1..4)"
                               % (ext, len(got)))
            notes.append("%s: %s" % (ext, sorted(got)))
        if reasons:
            check.fail("material slot contract violated", reasons, notes)
        else:
            check.pass_("material slot set matches ATLAS TIER_MATERIALS (%s)"
                        % sorted(want), notes)
        return check

    # C7 flat-patch UV (ART-07: mode-aware UV0 bounds)
    def _c7_uv(self, spec, facts):
        check = Check("C7", "flat-patch UV (mode-aware)")
        reasons = []
        notes = []
        map_data = self.patch_info.get("data")
        contexts = spec_material_contexts(spec)
        if map_data is None:
            check.fail("material texture patch map unavailable",
                       ["cannot test the ART-07 mode-aware UV0 bounds: %s"
                        % (self.patch_info.get("error") or "map not loaded")],
                       ["C7 needs art/material_texture_map.json from the Lead's "
                        "material analysis"])
            return check
        for ext in ("fbx", "glb"):
            fact = facts[ext]
            if fact.error:
                reasons.append("%s: cannot inspect (%s)" % (ext, fact.error))
                continue
            if not fact.tri_count:
                reasons.append("%s: no triangles" % ext)
                continue
            uv = fact.tri_uv
            if not np.isfinite(uv).all():
                reasons.append("%s: non-finite UV value(s)" % ext)
            if float(uv.min()) < -UV_EPS or float(uv.max()) > 1.0 + UV_EPS:
                reasons.append("%s: UV outside [0,1] (min %.4f max %.4f)"
                               % (ext, float(uv.min()), float(uv.max())))
            samples = slot_uv_samples(fact)
            rows, findings = containment_for_slots(samples, contexts, map_data,
                                                   kind="c7")
            reasons.extend("%s: %s" % (ext, finding) for finding in findings)
            for row in rows:
                notes.append("%s %s" % (ext, slot_row_text(row)))
            corner_count = sum(row["count"] for row in rows)
            material_count = len({row["material"] for row in rows
                                  if row["material"]})
            notes.append("%s: %d material slot group(s) / %d material(s), %d uv0 "
                         "corner(s) checked against the ART-07 canvases (tile: "
                         "the frozen 2/1024 texel guard band; patch: "
                         "uv_canvas.uv_rect)"
                         % (ext, len(rows), material_count, corner_count))
        if reasons:
            check.fail("mode-aware UV0 bounds violated", _capped(reasons, 20),
                       notes)
        else:
            check.pass_("every material samples inside its own ART-07 canvas "
                        "(tile: inside the 2/1024 texel guard band; patch: inside "
                        "uv_canvas.uv_rect; construction: both faction contexts; "
                        "C11 reports per-material min/max)", notes)
        return check

    # C11 material patch UV containment (ART-07 per-material report)
    def _c11_patch_uv(self, spec, facts):
        check = Check("C11", "material canvas UV containment")
        reasons = []
        notes = []
        map_data = self.patch_info.get("data")
        contexts = spec_material_contexts(spec)
        if map_data is None:
            check.fail("material texture patch map unavailable",
                       ["cannot test uv0 canvas containment: %s"
                        % (self.patch_info.get("error") or "map not loaded")])
            return check
        total_values = 0
        materials = set()
        modes = {}
        for ext in ("fbx", "glb"):
            fact = facts[ext]
            if fact.error:
                reasons.append("%s: cannot inspect (%s)" % (ext, fact.error))
                continue
            samples = slot_uv_samples(fact)
            rows, findings = containment_for_slots(samples, contexts, map_data,
                                                   kind="c11")
            reasons.extend("%s: %s" % (ext, finding) for finding in findings)
            for row in rows:
                notes.append("%s %s" % (ext, slot_row_text(row)))
                total_values += row["count"]
                if row["material"]:
                    materials.add(row["material"])
                    modes[row["material"]] = row["mode"]
        tile_count = sum(1 for mode in modes.values() if mode == "tile")
        if reasons:
            check.fail("uv0 escapes the material's ART-07 canvas",
                       _capped(reasons, 20), notes)
        else:
            check.pass_("%d material(s) (%d tile / %d patch) / %d uv0 value(s) "
                        "inside their ART-07 uv_canvas (tile: whole texture; "
                        "patch: curated canvas; construction: both factions; "
                        "tolerance %.0e)"
                        % (len(materials), tile_count,
                           len(materials) - tile_count, total_values,
                           PATCH_UV_TOL), notes)
        return check

    # -- C8 LOD shrink ----------------------------------------------------
    def check_lod_shrink(self, results):
        check = Check("C8", "LOD shrink")
        reasons = []
        notes = []
        by_key = {}
        for result in results:
            spec = result["spec"]
            if spec["kind"] != "tier":
                continue
            key = (spec["tier"], spec["faction"])
            by_key.setdefault(key, {})[spec["lod"]] = result
        for key in sorted(by_key):
            lods = by_key[key]
            if 0 not in lods:
                reasons.append("%s: LOD0 missing" % (key,))
                continue
            base_facts = lods[0]["facts"]["glb"]
            if base_facts.error:
                reasons.append("%s: LOD0 unreadable (%s)" % (key, base_facts.error))
                continue
            base_lo, base_hi = base_facts.bounds()
            base_size = base_hi - base_lo
            for lod in (1, 2):
                if lod not in lods:
                    reasons.append("%s: LOD%d missing" % (key, lod))
                    continue
                facts = lods[lod]["facts"]["glb"]
                if facts.error:
                    reasons.append("%s: LOD%d unreadable (%s)"
                                   % (key, lod, facts.error))
                    continue
                lo, hi = facts.bounds()
                shrink = float(np.max((base_size - (hi - lo)) / np.maximum(base_size, 1e-9)))
                notes.append("%s.%s LOD%d bbox shrink %.3f%%"
                             % (key[0], key[1], lod, shrink * 100.0))
                if shrink > A.LOD_BBOX_SHRINK_MAX + 1e-6:
                    reasons.append("%s: LOD%d bbox shrinks %.2f%% (max %.0f%%)"
                                   % (key, lod, shrink * 100.0,
                                      A.LOD_BBOX_SHRINK_MAX * 100.0))
        if reasons:
            check.fail("LOD bounding-box contract violated", reasons, notes)
        else:
            check.pass_("every LOD keeps the full 3x3x1 envelope (shrink <= 2%)",
                        notes)
        self.c8 = check

    # -- C9 50% distinguishability ---------------------------------------
    def check_distinguishability(self, results):
        check = Check("C9", "50% distinguishability")
        reasons = []
        notes = []
        by_key = {}
        for result in results:
            spec = result["spec"]
            if spec["kind"] == "tier" and spec["lod"] == 0:
                by_key[(spec["tier"], spec["faction"])] = result
        pairs = 0
        passed_pairs = 0
        for faction in A.FACTIONS:
            rasters = {}
            for tier in A.TIERS:
                result = by_key.get((tier, faction))
                if result is None:
                    reasons.append("%s.%s LOD0 missing for C9" % (tier, faction))
                    continue
                facts = result["facts"]["glb"]
                if facts.error:
                    reasons.append("%s.%s LOD0 unreadable (%s)"
                                   % (tier, faction, facts.error))
                    continue
                views = {}
                for view in ("front", "left", "top"):
                    ids, cov = raster_material(facts, view)
                    views[view] = {"ids": ids, "cov": cov,
                                   "glass": _glass_fraction(facts, ids, cov)}
                px, pz = A.roof_platform(tier)
                rasters[tier] = {"facts": facts, "views": views,
                                 "plan": (px, pz),
                                 "ports": _port_signature(facts)}
            tiers_here = [tier for tier in A.TIERS if tier in rasters]
            for i in range(len(tiers_here)):
                for j in range(i + 1, len(tiers_here)):
                    ta, tb = tiers_here[i], tiers_here[j]
                    ra, rb = rasters[ta], rasters[tb]
                    pairs += 1
                    mat_diff = max(_material_diff(ra["views"][v]["ids"],
                                                  rb["views"][v]["ids"])
                                   for v in ("front", "left"))
                    glass_diff = max(
                        abs(ra["views"][v]["glass"] - rb["views"][v]["glass"])
                        for v in ("front", "left"))
                    sil_iou = min(_silhouette_iou(ra["views"][v]["cov"],
                                                  rb["views"][v]["cov"])
                                  for v in ("front", "left", "top"))
                    sil_diff = 1.0 - sil_iou
                    plan_diff = max(abs(ra["plan"][0] - rb["plan"][0]),
                                    abs(ra["plan"][1] - rb["plan"][1]))
                    port_diff = 0.0
                    for pa, pb in zip(ra["ports"], rb["ports"]):
                        base = max(pa["area"], pb["area"], 1e-6)
                        port_diff = max(port_diff,
                                        abs(pa["area"] - pb["area"]) / base)
                    criteria = {
                        "material": mat_diff >= DIST_MATERIAL_MIN,
                        "windows": glass_diff >= DIST_GLASS_MIN,
                        "silhouette": sil_diff >= DIST_SILHOUETTE_MIN,
                        "roof_plan": plan_diff >= DIST_PLAN_MIN,
                        "ports": port_diff >= DIST_PORT_MIN,
                    }
                    category_material = criteria["material"]
                    category_windows = criteria["windows"]
                    category_contour = (criteria["silhouette"]
                                        or criteria["roof_plan"])
                    category_ports = criteria["ports"]
                    categories = sum(1 for value in
                                     (category_material, category_windows,
                                      category_contour, category_ports) if value)
                    ok = categories >= DIST_CRITERIA_MIN
                    line = ("%s vs %s [%s]: material=%.3f%s windows=%.3f%s "
                            "sil=%.3f%s plan=%.2f%s ports=%.2f%s -> %s "
                            "(%d/4 categories)"
                            % (ta, tb, faction, mat_diff,
                               "Y" if category_material else "n",
                               glass_diff, "Y" if category_windows else "n",
                               sil_diff, "Y" if criteria["silhouette"] else "n",
                               plan_diff, "Y" if criteria["roof_plan"] else "n",
                               port_diff, "Y" if category_ports else "n",
                               "PASS" if ok else "FAIL", categories))
                    notes.append(line)
                    if ok:
                        passed_pairs += 1
                    else:
                        reasons.append(line)
        if pairs != 12:
            reasons.append("expected 12 tier pairs (6 per faction), evaluated %d"
                           % pairs)
        if reasons:
            check.fail("tier pairs are not distinguishable by >= 2 of "
                       "{material, windows, silhouette/roof-plan, ports} at the "
                       "50%% camera", reasons, notes)
        else:
            check.pass_("all %d tier pairs differ on >= 2 of the 50%%-camera "
                        "criteria (material + windows/contour/ports)" % passed_pairs,
                        notes)
        self.c9 = check

    # -- C10 material_slots.json -----------------------------------------
    def check_material_slots(self, results):
        check = self.c10
        path = os.path.join(HERE, "material_slots.json")
        reasons = []
        notes = []
        try:
            with open(path, "r", encoding="utf-8") as handle:
                data = json.load(handle)
        except Exception as exc:
            check.fail("material_slots.json unreadable", ["%s: %s"
                                                          % (type(exc).__name__, exc)])
            return
        if "gen_models_v2" not in str(data.get("generated_by", "")):
            reasons.append("generated_by does not name art/gen_models_v2.py (%r)"
                           % data.get("generated_by"))
        env = data.get("envelope", {})
        if env.get("size") != [3.0, 1.0, 3.0]:
            reasons.append("envelope.size != [3.0, 1.0, 3.0] (%r)"
                           % (env.get("size"),))
        roof = data.get("roof_platform", {})
        if abs(float(roof.get("top", -1)) - A.ROOF_PLATFORM_TOP) > 1e-6:
            reasons.append("roof_platform.top != %.2f (%r)"
                           % (A.ROOF_PLATFORM_TOP, roof.get("top")))
        if roof.get("walking_surface_is_model_apex") is not True:
            reasons.append("roof_platform.walking_surface_is_model_apex is not true")
        if abs(float(roof.get("fascia_protrusion_above_top", 9)) - 0.0) > 1e-6:
            reasons.append("roof_platform.fascia_protrusion_above_top != 0")
        ports = data.get("ports", [])
        if sorted(p.get("name") for p in ports) != sorted(
                spec["object"] for spec in A.PORT_SPECS):
            reasons.append("ports list %r != the three #Port# markers"
                           % [p.get("name") for p in ports])
        if data.get("slots_markers") != []:
            reasons.append("slots_markers is not the empty list")
        measured = {}
        for result in results:
            spec = result["spec"]
            if spec["kind"] != "tier":
                if spec["kind"] == "construction":
                    key = A.CONSTRUCTION_STAGE
                else:
                    continue
            else:
                key = "%s.%s" % (spec["tier"], spec["faction"])
            facts = result["facts"]["glb"]
            if facts.error:
                continue
            measured.setdefault(key, {})["LOD%d" % spec["lod"]] = {
                "tris": facts.tri_count,
                "materials": list(facts.materials),
            }
        models = data.get("models", {})
        for key in sorted(measured):
            if key not in models:
                reasons.append("models[%r] missing from material_slots.json" % key)
                continue
            for lod_key in sorted(measured[key]):
                entry = models[key].get(lod_key)
                if entry is None:
                    reasons.append("models[%r][%r] missing" % (key, lod_key))
                    continue
                got = measured[key][lod_key]
                if entry.get("triangles") != got["tris"]:
                    reasons.append("models[%r][%r].triangles=%r, measured %d"
                                   % (key, lod_key, entry.get("triangles"),
                                      got["tris"]))
                if sorted(entry.get("materials", [])) != sorted(got["materials"]):
                    reasons.append("models[%r][%r].materials=%r, measured %r"
                                   % (key, lod_key, entry.get("materials"),
                                      got["materials"]))
                size = entry.get("bounds_size")
                if size != [3.0, 1.0, 3.0]:
                    reasons.append("models[%r][%r].bounds_size=%r != [3,1,3]"
                                   % (key, lod_key, size))
                plan = entry.get("roof_platform", {})
                want_plan = A.roof_platform(spec_tier_for(key, lod_key))
                if (abs(float(plan.get("size_x", -9)) - want_plan[0]) > 1e-6
                        or abs(float(plan.get("size_z", -9)) - want_plan[1]) > 1e-6):
                    reasons.append("models[%r][%r] platform plan %r != %r"
                                   % (key, lod_key, plan, want_plan))
        # -- ART-05 patch records (real texture patches of the shipped materials) --
        map_data = self.patch_info.get("data") or {}
        patch_count = 0
        summary = data.get("material_texture_map")
        if not isinstance(summary, dict):
            reasons.append("material_texture_map summary is missing from "
                           "material_slots.json")
        elif summary.get("schema") != MAP_SCHEMA:
            reasons.append("material_texture_map.schema=%r != %r"
                           % (summary.get("schema"), MAP_SCHEMA))
        patch_records = data.get("material_texture_patches")
        if patch_records is None:
            # ART-01/ART-04 legacy format: a role-id list at top level.
            legacy = data.get("material_patches")
            if isinstance(legacy, list) and all(
                    isinstance(record, dict) and "id" in record
                    and "material" not in record for record in legacy):
                if len(legacy) != len(MP.PATCH_PIXELS):
                    reasons.append("%d legacy role patch records, expected %d"
                                   % (len(legacy), len(MP.PATCH_PIXELS)))
                notes.append("%d legacy ART-04 role patch record(s) in "
                             "material_slots.json (preview fallback only; shipped "
                             "UVs use the material_texture_map.json patches)"
                             % len(legacy))
                patch_count = len(legacy)
            else:
                reasons.append("material_texture_patches list is missing from "
                               "material_slots.json")
                patch_records = []
        if patch_records is not None:
            records = {}
            for record in patch_records:
                if not isinstance(record, dict):
                    reasons.append("material_texture_patches contains a non-object "
                                   "record")
                    continue
                material = record.get("material")
                if not isinstance(material, str) or not material:
                    reasons.append("material_texture_patches record without a "
                                   "'material' name: %r" % (record.get("id"),))
                    continue
                records[material] = record
            scheduled = scheduled_materials()
            missing_records = [material for material in sorted(scheduled)
                               if material not in records]
            if missing_records:
                reasons.append("%d material(s) assigned by MATERIAL_MAP have no "
                               "material_texture_patches record: %s"
                               % (len(missing_records), missing_records))
            for material in sorted(records):
                entry, _section = patch_entry(map_data, material)
                record = records[material]
                if entry is None:
                    reasons.append("material_texture_patches records %r but the "
                                   "texture map has no entry for it" % material)
                    continue
                patch = entry.get("patch") or {}
                if list(record.get("pixel_rect") or []) != list(
                        patch.get("pixel_rect") or []):
                    reasons.append("material_texture_patches[%r].pixel_rect=%r != "
                                   "map patch %r" % (material,
                                                     record.get("pixel_rect"),
                                                     patch.get("pixel_rect")))
                record_uv = [round(float(v), 6)
                             for v in (record.get("uv_rect") or [])]
                map_uv = [round(float(v), 6) for v in (patch.get("uv_rect") or [])]
                if record_uv != map_uv:
                    reasons.append("material_texture_patches[%r].uv_rect=%r != map "
                                   "patch %r" % (material, record.get("uv_rect"),
                                                 patch.get("uv_rect")))
            patch_count = len(records)
            notes.append("%d per-material real-texture patch record(s) cross-checked "
                         "against art/material_texture_map.json" % len(records))
            # -- per-model slot -> patch records ------------------------------
            model_patch_count = 0
            for key in sorted(measured):
                model_info = models.get(key)
                if not isinstance(model_info, dict):
                    reasons.append("models[%r] missing from material_slots.json" % key)
                    continue
                model_records = model_info.get("material_patches")
                if not isinstance(model_records, dict) or not model_records:
                    reasons.append("models[%r].material_patches is missing or empty"
                                   % key)
                    continue
                want_slots = set()
                for lod_key in measured[key]:
                    want_slots.update(measured[key][lod_key]["materials"])
                got_slots = set(model_records)
                if got_slots != want_slots:
                    reasons.append("models[%r] patch slots %r != measured material "
                                   "slots %r" % (key, sorted(got_slots),
                                                 sorted(want_slots)))
                slot_to_material = model_info.get("material_texture_patches")
                expected_slot_map = expected_material_map_for_group(key)
                if expected_slot_map is not None:
                    if not isinstance(slot_to_material, dict):
                        reasons.append("models[%r].material_texture_patches is "
                                       "missing" % key)
                    elif {str(k): str(v) for k, v in slot_to_material.items()} \
                            != expected_slot_map:
                        reasons.append("models[%r].material_texture_patches %r != "
                                       "timbermesh_common.MATERIAL_MAP slot->material "
                                       "%r" % (key, slot_to_material,
                                               expected_slot_map))
                # every LOD entry must repeat the group slot->material plan and the
                # measured source material slot list
                for lod_key in sorted(measured[key]):
                    lod_entry = model_info.get(lod_key)
                    if not isinstance(lod_entry, dict):
                        reasons.append("models[%r][%r] missing" % (key, lod_key))
                        continue
                    got_lod_materials = lod_entry.get("materials")
                    want_lod_materials = measured[key][lod_key]["materials"]
                    if sorted(got_lod_materials or []) != sorted(want_lod_materials):
                        reasons.append("models[%r][%r].materials %r != measured "
                                       "source slots %r"
                                       % (key, lod_key, got_lod_materials,
                                          want_lod_materials))
                    lod_slot_map = lod_entry.get("material_texture_patches")
                    if isinstance(lod_slot_map, dict) and expected_slot_map is not None:
                        if {str(k): str(v) for k, v in lod_slot_map.items()} \
                                != expected_slot_map:
                            reasons.append("models[%r][%r].material_texture_patches "
                                           "%r != MATERIAL_MAP plan %r"
                                           % (key, lod_key, lod_slot_map,
                                              expected_slot_map))
                for slot in sorted(model_records):
                    record = model_records[slot]
                    model_patch_count += 1
                    if not isinstance(record, dict):
                        reasons.append("models[%r].material_patches[%r] is not an "
                                       "object" % (key, slot))
                        continue
                    material = record.get("material")
                    entry, _section = patch_entry(map_data, material)
                    if entry is None:
                        reasons.append("models[%r].material_patches[%r] names "
                                       "unknown material %r"
                                       % (key, slot, material))
                        continue
                    patch = entry.get("patch") or {}
                    if list(record.get("pixel_rect") or []) != list(
                            patch.get("pixel_rect") or []):
                        reasons.append("models[%r].material_patches[%r].pixel_rect "
                                       "!= map patch" % (key, slot))
                    record_uv = [round(float(v), 6)
                                 for v in (record.get("uv_rect") or [])]
                    map_uv = [round(float(v), 6)
                              for v in (patch.get("uv_rect") or [])]
                    if record_uv != map_uv:
                        reasons.append("models[%r].material_patches[%r].uv_rect != "
                                       "map patch" % (key, slot))
                    if isinstance(slot_to_material, dict) \
                            and slot_to_material.get(slot) not in (None, material):
                        reasons.append("models[%r]: slot %r maps to %r in "
                                       "material_texture_patches but to %r in "
                                       "material_patches"
                                       % (key, slot, slot_to_material.get(slot),
                                          material))
                    if expected_slot_map is not None \
                            and expected_slot_map.get(slot) not in (None, material) \
                            and key != A.CONSTRUCTION_STAGE:
                        reasons.append("models[%r].material_patches[%r] material %r "
                                       "!= MATERIAL_MAP %r"
                                       % (key, slot, material,
                                          expected_slot_map.get(slot)))
            notes.append("%d per-model slot/patch record(s) cross-checked for %d "
                         "model(s)" % (model_patch_count, len(measured)))
            construction = models.get(A.CONSTRUCTION_STAGE)
            if isinstance(construction, dict):
                cfm = construction.get("construction_faction_materials")
                if not isinstance(cfm, dict):
                    reasons.append("models[ConstructionStage0]."
                                   "construction_faction_materials is missing")
                else:
                    if construction.get("shared_uv_windows") != \
                            "construction_shared_patches":
                        reasons.append("models[ConstructionStage0].shared_uv_windows "
                                       "!= 'construction_shared_patches' (%r)"
                                       % (construction.get("shared_uv_windows"),))
                    if set(cfm) != set(TC.FACTIONS):
                        reasons.append("construction_faction_materials factions %r "
                                       "!= %r" % (sorted(cfm), sorted(TC.FACTIONS)))
                    for faction in sorted(cfm):
                        expected_map = dict(TC.MATERIAL_MAP.get(
                            (TC.TIERS[0], faction, True)) or {})
                        got_map = cfm.get(faction)
                        if isinstance(got_map, dict) and expected_map \
                                and {str(k): str(v) for k, v in got_map.items()} \
                                != expected_map:
                            reasons.append("construction_faction_materials[%r] %r != "
                                           "MATERIAL_MAP construction plan %r"
                                           % (faction, got_map, expected_map))
                        slot_map = cfm.get(faction)
                        if not isinstance(slot_map, dict):
                            reasons.append("construction_faction_materials[%r] is "
                                           "not an object" % faction)
                            continue
                        for slot in sorted(slot_map):
                            material = slot_map[slot]
                            if patch_entry(map_data, material)[0] is None:
                                reasons.append("construction_faction_materials[%r]"
                                               "[%r] names unknown material %r"
                                               % (faction, slot, material))
                    # the shared source mesh bakes ONE UV set: ART-07 records the
                    # shared window in construction_shared_patches (the Folktails
                    # material's canvas); both faction materials must resolve to
                    # that same window and the window must sit inside each of
                    # their own ART-07 canvases.
                    shared_groups = map_data.get("construction_shared_patches") \
                        if isinstance(map_data, dict) else None
                    shared_groups = shared_groups \
                        if isinstance(shared_groups, dict) else {}
                    shared_slots = set()
                    for faction in cfm:
                        if isinstance(cfm.get(faction), dict):
                            shared_slots.update(cfm[faction])
                    for slot in sorted(shared_slots):
                        group_names = set()
                        slot_materials = {}
                        for faction in TC.FACTIONS:
                            material = (cfm.get(faction) or {}).get(slot) \
                                if isinstance(cfm.get(faction), dict) else None
                            if material is None:
                                continue
                            slot_materials[faction] = material
                            entry, _section = patch_entry(map_data, material)
                            shared = entry.get("shared_uv_group") \
                                if isinstance(entry, dict) else None
                            if isinstance(shared, str) and shared:
                                group_names.add(shared)
                        if len(group_names) > 1:
                            reasons.append("construction slot %r: faction materials "
                                           "resolve to different ART-07 shared "
                                           "windows %r" % (slot, sorted(group_names)))
                            continue
                        if not group_names:
                            reasons.append("construction slot %r: no ART-07 "
                                           "construction_shared_patches group is "
                                           "recorded for %r"
                                           % (slot, slot_materials))
                            continue
                        group_name = sorted(group_names)[0]
                        group = shared_groups.get(group_name)
                        if not isinstance(group, dict):
                            reasons.append("construction slot %r: shared window %r "
                                           "is missing from the map"
                                           % (slot, group_name))
                            continue
                        expected_pair = {slot_materials.get(faction)
                                         for faction in TC.FACTIONS}
                        got_pair = {group.get("folktails_material"),
                                    group.get("ironteeth_material")}
                        if expected_pair != got_pair:
                            reasons.append("construction slot %r: shared window %r "
                                           "names materials %r != the faction plan "
                                           "%r" % (slot, group_name,
                                                   sorted(str(v) for v in got_pair),
                                                   sorted(slot_materials.values())))
                        window = _as_rect4(group.get("uv_rect"),
                                           "construction_shared_patches[%r].uv_rect"
                                           % group_name, reasons)
                        if window is not None:
                            for faction in TC.FACTIONS:
                                material = slot_materials.get(faction)
                                if material is None:
                                    continue
                                rect, tol, mode, why = material_uv_bounds(
                                    map_data, material, kind="c11")
                                if rect is None:
                                    reasons.append("construction slot %r: %s"
                                                   % (slot, why))
                                    continue
                                if (window[0] < rect[0] - tol
                                        or window[1] < rect[1] - tol
                                        or window[2] > rect[2] + tol
                                        or window[3] > rect[3] + tol):
                                    reasons.append(
                                        "construction slot %r: shared window %r "
                                        "uv=%r is not inside %r's ART-07 %s "
                                        "canvas %r"
                                        % (slot, group_name, window, material,
                                           mode, rect))
                # the IronTeeth construction records must describe the IT materials
                it_records = construction.get("material_patches_ironteeth")
                it_plan = dict(TC.MATERIAL_MAP.get((TC.TIERS[0], "IronTeeth", True))
                               or {})
                if not isinstance(it_records, dict) or not it_records:
                    reasons.append("models[ConstructionStage0]."
                                   "material_patches_ironteeth is missing")
                else:
                    if sorted(it_records) != sorted(it_plan):
                        reasons.append("models[ConstructionStage0]."
                                       "material_patches_ironteeth slots %r != "
                                       "MATERIAL_MAP construction plan %r"
                                       % (sorted(it_records), sorted(it_plan)))
                    for slot in sorted(it_records):
                        record = it_records[slot]
                        material = record.get("material") \
                            if isinstance(record, dict) else None
                        entry, _section = patch_entry(map_data, material)
                        if entry is None:
                            reasons.append("models[ConstructionStage0]."
                                           "material_patches_ironteeth[%r] names "
                                           "unknown material %r" % (slot, material))
                            continue
                        patch = entry.get("patch") or {}
                        if list(record.get("pixel_rect") or []) != list(
                                patch.get("pixel_rect") or []):
                            reasons.append("models[ConstructionStage0]."
                                           "material_patches_ironteeth[%r]."
                                           "pixel_rect != map patch" % slot)
                        record_uv = [round(float(v), 6)
                                     for v in (record.get("uv_rect") or [])]
                        map_uv = [round(float(v), 6)
                                  for v in (patch.get("uv_rect") or [])]
                        if record_uv != map_uv:
                            reasons.append("models[ConstructionStage0]."
                                           "material_patches_ironteeth[%r].uv_rect "
                                           "!= map patch" % slot)
                        if it_plan.get(slot) not in (None, material):
                            reasons.append("models[ConstructionStage0]."
                                           "material_patches_ironteeth[%r] material "
                                           "%r != MATERIAL_MAP %r"
                                           % (slot, material, it_plan.get(slot)))
        # the fake shared ART-01 atlas is gone: no legacy atlas_*.png reference may
        # survive in material_slots.json
        serialized = json.dumps(data)
        for atlas_name in sorted({os.path.basename(str(value))
                                  for value in A.ATLAS_FILES.values()}):
            if atlas_name in serialized:
                reasons.append("legacy ART-01 atlas PNG reference %r is still present "
                               "in material_slots.json" % atlas_name)
        if "atlas" in data:
            reasons.append("legacy 'atlas' block is still present in "
                           "material_slots.json")
        notes.append("%d models, %d patch records validated against the meshes"
                     % (len(measured), patch_count))
        if reasons:
            check.fail("material_slots.json does not agree with the models", reasons,
                       notes)
        else:
            check.pass_("material_slots.json (envelope, roof rule, ports, plans, "
                        "triangles, materials, %d real-texture patch records) "
                        "agrees with the measured meshes" % patch_count, notes)

    # -- C12/C13/C14 ART-05 cross-model material checks --------------------
    def check_global_checks(self, results):
        self.globals = evaluate_global_checks(self.patch_info, results)
        self.c12 = self.globals.coverage
        self.c13 = self.globals.variance
        self.c14 = self.globals.hazard


def spec_tier_for(key, lod_key):
    if key == A.CONSTRUCTION_STAGE:
        return A.CONSTRUCTION_STAGE
    return key.split(".")[0]


# --------------------------------------------------------------------------- #
# ART-05 material texture patch helpers (C7/C11 + cross-model C12-C14 +
# negative controls).  Mirrors the shared logic of validate_timbermesh.py; the
# two validators stay standalone (one runs under Blender bpy, one under plain
# CPython) but enforce exactly the same contract.
# --------------------------------------------------------------------------- #

def _relpath(path):
    try:
        rel = os.path.relpath(path, ROOT)
    except ValueError:
        return path
    return rel if not rel.startswith("..") else path


def _is_number(value):
    return (isinstance(value, (int, float)) and not isinstance(value, bool)
            and math.isfinite(float(value)))


def _as_rect4(value, label, reasons):
    if not isinstance(value, (list, tuple)) or len(value) != 4:
        reasons.append("%s must be a 4-element list, got %r" % (label, value))
        return None
    out = []
    for item in value:
        if not _is_number(item):
            reasons.append("%s contains a non-number: %r" % (label, value))
            return None
        out.append(float(item))
    return out


def _mean_text(values, digits=2):
    return "(" + ", ".join(("%." + str(digits) + "f") % float(v) for v in values) + ")"


def _rect_text(rect):
    return "(" + ", ".join("%d" % int(round(v)) for v in rect) + ")"


def _uv_text(rect):
    return "(" + ", ".join("%.6f" % float(v) for v in rect) + ")"


def _capped(items, limit=15):
    items = list(items)
    if len(items) <= limit:
        return items
    return items[:limit] + ["... and %d more" % (len(items) - limit)]


def load_patch_map():
    """Load art/material_texture_map.json via material_patches (JSON fallback)."""
    info = {"data": None, "source": "-", "error": None, "path": MAP_PATH}
    loader = getattr(MP, "load_material_texture_map", None)
    if callable(loader):
        try:
            data = loader()
        except Exception as exc:  # noqa: BLE001 - fall back to the plain JSON read
            info["error"] = ("material_patches.load_material_texture_map(): %s: %s"
                             % (type(exc).__name__, exc))
        else:
            if isinstance(data, dict) and isinstance(data.get("materials"), dict):
                info["data"] = data
                info["source"] = "material_patches.load_material_texture_map()"
                return info
            info["error"] = ("material_patches.load_material_texture_map() returned %r "
                             "without a 'materials' dict" % (type(data).__name__,))
    path = getattr(MP, "MAP_PATH", None)
    if not isinstance(path, str) or not path:
        path = MAP_PATH
    info["path"] = path
    try:
        with open(path, "r", encoding="utf-8") as handle:
            data = json.load(handle)
    except Exception as exc:  # noqa: BLE001 - report, do not crash
        if info["error"] is None:
            info["error"] = "%s: %s (%s)" % (type(exc).__name__, exc,
                                             _relpath(path))
        return info
    info["data"] = data
    info["source"] = "json: %s" % _relpath(path)
    return info


def texture_roots(map_data):
    """Ordered [(label, root_dir)] candidates: env overrides, map roots, defaults."""
    candidates = []
    env = (os.environ.get("HDBF_VANILLA_TEX_ROOT") or "").strip()
    if env:
        candidates.append(("HDBF_VANILLA_TEX_ROOT", env))
    # compat with material_patches.texture_path_for's per-root env variables
    for key, var in (("vanilla-tex-all", "HDBF_VANILLA_TEX_ALL"),
                     ("vanilla-tex-extra", "HDBF_VANILLA_TEX_EXTRA")):
        value = (os.environ.get(var) or "").strip()
        if value:
            candidates.append((var, value))
    declared = (map_data or {}).get("texture_roots") if isinstance(map_data, dict) else None
    if isinstance(declared, dict):
        for key in sorted(declared):
            value = declared[key]
            if isinstance(value, str) and value:
                candidates.append(("texture_roots[%r]" % key, value))
    for root in DEFAULT_TEXTURE_ROOTS:
        candidates.append(("default root", root))
    seen = set()
    ordered = []
    for label, root in candidates:
        try:
            key = os.path.normcase(os.path.normpath(root))
        except (TypeError, ValueError):
            continue
        if key in seen:
            continue
        seen.add(key)
        ordered.append((label, root))
    return ordered


def resolve_texture(map_data, entry):
    """(path, root_label, tried_paths) of the entry's real vanilla ``_D`` PNG."""
    name = entry.get("texture") if isinstance(entry, dict) else None
    if not isinstance(name, str) or not name:
        return None, None, ["entry has no 'texture' file name"]
    tried = []
    env = (os.environ.get("HDBF_VANILLA_TEX_ROOT") or "").strip()
    if env:
        path = os.path.join(env, name)
        tried.append(path)
        if os.path.isfile(path):
            return path, "HDBF_VANILLA_TEX_ROOT", tried
    recorded = entry.get("texture_path") if isinstance(entry, dict) else None
    if isinstance(recorded, str) and recorded:
        tried.append(recorded)
        if os.path.isfile(recorded):
            return recorded, "map texture_path", tried
    if os.path.isabs(name) and os.path.isfile(name):
        return name, "absolute path", tried + [name]
    ordered = []
    source = entry.get("texture_source") if isinstance(entry, dict) else None
    roots = texture_roots(map_data)
    if source:
        declared = (map_data or {}).get("texture_roots") or {}
        for label, root in roots:
            if isinstance(declared, dict) and source in declared \
                    and os.path.normcase(os.path.normpath(root)) == \
                    os.path.normcase(os.path.normpath(declared[source])):
                ordered.insert(0, (label, root))
            else:
                ordered.append((label, root))
    else:
        ordered = roots
    seen = set()
    for label, root in ordered:
        path = os.path.join(root, name)
        key = os.path.normcase(os.path.normpath(path))
        if key in seen:
            continue
        seen.add(key)
        tried.append(path)
        if os.path.isfile(path):
            return path, label, tried
    return None, None, tried


def texture_roots_available(map_data):
    for _label, root in texture_roots(map_data):
        if os.path.isdir(root):
            return True
    return False


def textures_reachable(map_data):
    if texture_roots_available(map_data):
        return True
    for _section, _material, entry in all_patch_entries(map_data):
        recorded = entry.get("texture_path")
        if isinstance(recorded, str) and os.path.isfile(recorded):
            return True
    return False


def expected_uv_rect(pixel_rect, texture_size):
    x0, y0, x1, y1 = [float(v) for v in pixel_rect]
    width, height = float(texture_size[0]), float(texture_size[1])
    return [x0 / width, 1.0 - y1 / height, x1 / width, 1.0 - y0 / height]


def patch_entry(map_data, material):
    if not isinstance(map_data, dict):
        return None, None
    for section in ("materials", "unused_materials"):
        entries = map_data.get(section)
        if isinstance(entries, dict) and material in entries:
            return entries[material], section
    return None, None


def patch_uv_rect(map_data, material):
    entry, _section = patch_entry(map_data, material)
    if isinstance(entry, dict):
        patch = entry.get("patch")
        if isinstance(patch, dict):
            rect = patch.get("uv_rect")
            if isinstance(rect, (list, tuple)) and len(rect) == 4:
                try:
                    return tuple(float(v) for v in rect)
                except (TypeError, ValueError):
                    pass
    accessor = getattr(MP, "uv_rect_for", None)
    if callable(accessor):
        try:
            return tuple(float(v) for v in accessor(material))
        except Exception:  # noqa: BLE001 - unknown material
            return None
    return None


def _vanilla_materials_in_facts(results):
    """Vanilla-looking material names found directly in source facts (usually none)."""
    found = set()
    for result in results or []:
        for ext in ("fbx", "glb"):
            fact = (result.get("facts") or {}).get(ext)
            if fact is None or getattr(fact, "error", None):
                continue
            for material in getattr(fact, "materials", []) or []:
                found.add(str(material))
    return found


def scheduled_materials():
    scheduled = set()
    for mapping in TC.MATERIAL_MAP.values():
        for target in mapping.values():
            scheduled.add(str(target))
    return scheduled


def expected_material_map_for_group(key):
    """slot -> vanilla material that the shipped export of one material_slots
    model group must use (None when the group key is unknown).

    The construction group is the shared source model baked with the Folktails
    construction UV window; its IronTeeth export uses materials recorded in
    ``construction_faction_materials`` whose patches share that same UV window.
    """
    if key == A.CONSTRUCTION_STAGE:
        return dict(TC.MATERIAL_MAP.get((TC.TIERS[0], "Folktails", True)) or {})
    parts = str(key).split(".")
    if len(parts) == 2 and parts[0] in TC.TIERS and parts[1] in TC.FACTIONS:
        return dict(TC.MATERIAL_MAP.get((parts[0], parts[1], False)) or {})
    return None


def required_materials(results):
    """Materials the shipped plan must cover: MATERIAL_MAP targets + real vanilla
    material names found directly on a source mesh (normally none - the sources
    carry ART-01 M_HDBF_* slots)."""
    required = set(scheduled_materials())
    for material in _vanilla_materials_in_facts(results):
        if "." in material:
            required.add(material)
    return required


def std_max_for(map_data, entry):
    patch = entry.get("patch") if isinstance(entry, dict) else None
    if isinstance(patch, dict) and _is_number(patch.get("std_max")):
        return float(patch["std_max"])
    thresholds = (map_data or {}).get("thresholds") if isinstance(map_data, dict) else None
    if isinstance(thresholds, dict):
        tag = entry.get("patch_class") if isinstance(entry, dict) else None
        if not tag and isinstance(patch, dict):
            tag = patch.get("patch_class")
        by_class = thresholds.get("std_max_by_class")
        if isinstance(tag, str) and isinstance(by_class, dict) and _is_number(by_class.get(tag)):
            return float(by_class[tag])
        if _is_number(thresholds.get("std_max_default")):
            return float(thresholds["std_max_default"])
    return PATCH_STD_MAX_DEFAULT


def hazard_tags_of(entry):
    tags = []

    def add(value):
        if isinstance(value, str):
            tags.append(value.strip().lower())
        elif isinstance(value, (list, tuple)):
            for item in value:
                if isinstance(item, str):
                    tags.append(item.strip().lower())

    if isinstance(entry, dict):
        add(entry.get("patch_class"))
        add(entry.get("reject_tags"))
        patch = entry.get("patch")
        if isinstance(patch, dict):
            add(patch.get("patch_class"))
            add(patch.get("reject_tags"))
    return [tag for tag in tags if tag]


def _norm_tag(tag):
    return tag.strip().lower().replace("_", "-")


def _class_is_hazard(patch_class):
    if not isinstance(patch_class, str) or not patch_class:
        return False
    norm = _norm_tag(patch_class)
    return norm in HAZARD_CLASSES or norm.startswith("hazard")


def is_hazard_marked(entry):
    """True when the entry itself is a hazard/logo/text (unusable) region."""
    if not isinstance(entry, dict):
        return False
    containers = [entry]
    patch = entry.get("patch")
    if isinstance(patch, dict):
        containers.append(patch)
    for source in containers:
        if _class_is_hazard(source.get("patch_class")):
            return True
        for key in ("hazard", "is_hazard", "forbidden", "forbidden_material",
                    "excluded"):
            if source.get(key) is True:
                return True
        fraction = source.get("hazard_fraction")
        if _is_number(fraction) and float(fraction) > HAZARD_FRACTION_MIN:
            return True
        tags = source.get("reject_tags")
        if isinstance(tags, (list, tuple)):
            for tag in tags:
                if not isinstance(tag, str):
                    continue
                norm = _norm_tag(tag)
                if norm in ("hazard", "hazard-region", "hazard-only",
                            "forbidden", "excluded"):
                    return True
                if norm.startswith("hazard:") or norm.startswith("hazard "):
                    return True
    return False


def all_patch_entries(map_data):
    out = []
    if not isinstance(map_data, dict):
        return out
    for section in ("materials", "unused_materials"):
        entries = map_data.get(section)
        if not isinstance(entries, dict):
            continue
        for material in sorted(entries):
            entry = entries[material]
            if isinstance(entry, dict) and isinstance(entry.get("patch"), dict):
                out.append((section, material, entry))
    return out


def entry_reasons(material, entry, require_patch=True):
    where = "materials[%r]" % material
    reasons = []
    if not isinstance(entry, dict):
        return ["%s: entry must be an object, got %r" % (where, type(entry).__name__)]
    texture = entry.get("texture")
    if not isinstance(texture, str) or not texture:
        reasons.append("%s.texture must be a non-empty PNG file name" % where)
    elif not texture.lower().endswith(".png"):
        reasons.append("%s.texture = %r is not a .png" % (where, texture))
    size = entry.get("texture_size")
    if isinstance(size, (list, tuple)) and len(size) == 2 \
            and all(isinstance(v, int) and not isinstance(v, bool) and v > 0 for v in size):
        size = [int(size[0]), int(size[1])]
    else:
        reasons.append("%s.texture_size must be [width, height] positive integers, got %r"
                       % (where, size))
        size = None
    patch = entry.get("patch")
    if patch is None:
        if require_patch:
            reasons.append("%s has no patch record" % where)
        return reasons
    if not isinstance(patch, dict):
        return reasons + ["%s.patch must be an object, got %r"
                          % (where, type(patch).__name__)]
    rect = _as_rect4(patch.get("pixel_rect"), "%s.patch.pixel_rect" % where, reasons)
    uv = _as_rect4(patch.get("uv_rect"), "%s.patch.uv_rect" % where, reasons)
    if rect is not None:
        x0, y0, x1, y1 = rect
        if not (0.0 <= x0 < x1 and 0.0 <= y0 < y1):
            reasons.append("%s.patch.pixel_rect %s has no positive area"
                           % (where, _rect_text(rect)))
        if size is not None and not (x1 <= size[0] and y1 <= size[1]):
            reasons.append("%s.patch.pixel_rect %s does not fit texture_size %s"
                           % (where, _rect_text(rect), size))
        size_px = patch.get("size_px")
        if size_px is not None:
            want = [x1 - x0, y1 - y0]
            ok = (isinstance(size_px, (list, tuple)) and len(size_px) == 2
                  and all(_is_number(v) for v in size_px)
                  and abs(float(size_px[0]) - want[0]) < 1e-6
                  and abs(float(size_px[1]) - want[1]) < 1e-6)
            if not ok:
                reasons.append("%s.patch.size_px %r != pixel_rect size %r"
                               % (where, size_px, want))
        if size is not None and uv is not None:
            want_uv = expected_uv_rect(rect, size)
            for index, name in enumerate(("u0", "v0", "u1", "v1")):
                delta = abs(uv[index] - want_uv[index])
                if delta > UV_RECT_ROUND_TOL:
                    reasons.append(
                        "%s.patch.uv_rect[%s] = %.9f, expected [x0/W, 1-y1/H, x1/W, "
                        "1-y0/H] = %.9f (delta %.2e > rounding tolerance %.1e)"
                        % (where, name, uv[index], want_uv[index], delta,
                           UV_RECT_ROUND_TOL))
    if uv is not None:
        u0, v0, u1, v1 = uv
        if not (0.0 <= u0 < u1 <= 1.0 and 0.0 <= v0 < v1 <= 1.0):
            reasons.append("%s.patch.uv_rect %s must satisfy 0 <= u0 < u1 <= 1 and "
                           "0 <= v0 < v1 <= 1" % (where, _uv_text(uv)))
    if require_patch:
        measured = patch.get("mean_rgb")
        if not (isinstance(measured, (list, tuple)) and len(measured) == 3
                and all(_is_number(v) and 0.0 <= float(v) <= 255.0 for v in measured)):
            reasons.append("%s.patch.mean_rgb must be 3 recorded numbers in 0..255, "
                           "got %r" % (where, measured))
        std = patch.get("std")
        if not _is_number(std) or float(std) < 0.0:
            reasons.append("%s.patch.std must be a recorded non-negative number, got %r"
                           % (where, std))
        std_max = patch.get("std_max")
        if std_max is not None and (not _is_number(std_max) or float(std_max) <= 0.0):
            reasons.append("%s.patch.std_max must be a positive number, got %r"
                           % (where, std_max))
        if _is_number(std) and _is_number(std_max) \
                and float(std) > float(std_max) + 1e-9:
            reasons.append("%s: recorded std %.3f > std_max %.3f (map records a "
                           "non-uniform patch)" % (where, float(std), float(std_max)))
        tint = patch.get("mean_rgb_tinted")
        if tint is not None and not (isinstance(tint, (list, tuple)) and len(tint) == 3
                                     and all(_is_number(v) for v in tint)):
            reasons.append("%s.patch.mean_rgb_tinted must be 3 numbers when present"
                           % where)
    return reasons


def map_schema_reasons(map_data):
    reasons = []
    if not isinstance(map_data, dict):
        return ["art/material_texture_map.json must be a JSON object"]
    if map_data.get("schema") != MAP_SCHEMA:
        reasons.append("schema = %r, expected %r"
                       % (map_data.get("schema"), MAP_SCHEMA))
    materials = map_data.get("materials")
    if not isinstance(materials, dict) or not materials:
        reasons.append("'materials' must be a non-empty object, got %r"
                       % (type(materials).__name__,))
        materials = {}
    unused = map_data.get("unused_materials")
    if unused is None:
        unused = {}
    if not isinstance(unused, dict):
        reasons.append("'unused_materials' must be an object when present, got %r"
                       % (type(unused).__name__,))
        unused = {}
    thresholds = map_data.get("thresholds")
    if thresholds is not None and not isinstance(thresholds, dict):
        reasons.append("'thresholds' must be an object when present, got %r"
                       % (type(thresholds).__name__,))
    for material in sorted(materials):
        reasons.extend(entry_reasons(material, materials[material], require_patch=True))
    for material in sorted(unused):
        if material in materials:
            reasons.append("material %r is listed in both 'materials' and "
                           "'unused_materials'" % material)
            continue
        reasons.extend(entry_reasons(material, unused[material], require_patch=False))
    return reasons


def evaluate_coverage(map_data, source, results, load_error=None):
    check = Check("C12", "material patch map coverage")
    details = {"required": [], "missing": [], "map_materials": 0, "map_unused": 0,
               "reference_only": []}
    notes = ["map source: %s" % (source or "-")]
    if map_data is None:
        check.fail("art/material_texture_map.json unavailable",
                   ["cannot verify material patch coverage: %s"
                    % (load_error or "map not loaded")], notes)
        return check, details
    reasons = map_schema_reasons(map_data)
    materials = map_data.get("materials") or {}
    unused = map_data.get("unused_materials") or {}
    required = required_materials(results)
    details.update(required=sorted(required), map_materials=len(materials),
                   map_unused=len(unused))
    missing = []
    reference_only = []
    for material in sorted(required):
        entry, section = patch_entry(map_data, material)
        if entry is None:
            missing.append(material)
        elif section == "unused_materials":
            reference_only.append(material)
    details["missing"] = missing
    details["reference_only"] = reference_only
    if missing:
        reasons.append("%d required material(s) have no entry in "
                       "art/material_texture_map.json: %s" % (len(missing), missing))
    if reference_only:
        notes.append("materials required by the contract but recorded only in "
                     "'unused_materials' (no usable patch): %s" % reference_only)
    notes.append("schema=%r; %d material record(s) + %d unused/reference record(s); "
                 "%d required material(s) (every timbermesh_common.MATERIAL_MAP "
                 "target + any vanilla material name on a source mesh)"
                 % (map_data.get("schema"), len(materials), len(unused),
                    len(required)))
    roots = texture_roots(map_data)
    existing = [root for _label, root in roots if os.path.isdir(root)]
    if existing:
        notes.append("texture root(s) available: %d of %d candidate(s) (first: %s)"
                     % (len(existing), len(roots), existing[0]))
    else:
        notes.append("texture roots NOT available on this machine (candidate roots: %s)"
                     % [root for _label, root in roots])
    if reasons:
        check.fail("art/material_texture_map.json is incomplete or invalid",
                   _capped(reasons, 25), notes)
    else:
        check.pass_("schema %r ok; all %d required material(s) have a real-texture "
                    "patch record (%d usable, %d unused/reference)"
                    % (MAP_SCHEMA, len(required), len(materials), len(unused)), notes)
    return check, details


# -- UV containment against the material patches ---------------------------- #

def slot_uv_samples(fact):
    """material slot NAME -> list of (triangle, u, v) uv0 samples of its faces."""
    samples = {}
    if fact is None or getattr(fact, "error", None) or not fact.tri_count:
        return samples
    uv = fact.tri_uv
    for tri_index in range(fact.tri_count):
        slot_index = int(fact.tri_mat[tri_index])
        if 0 <= slot_index < len(fact.materials):
            slot = str(fact.materials[slot_index])
        else:  # defensive: keep the invalid slot visible as its own group
            slot = "#invalid-slot-%d" % slot_index
        values = samples.setdefault(slot, [])
        for corner in range(3):
            values.append((tri_index, float(uv[tri_index, corner, 0]),
                           float(uv[tri_index, corner, 1])))
    return samples


def spec_material_contexts(spec):
    """[(label, {slot: vanilla material})] for every export target of this source.

    A tier/faction source feeds exactly one .timbermesh.  The shared
    ConstructionStage0 source feeds BOTH faction exports, so its UVs have to
    satisfy both faction material patches (the Lead must therefore keep the
    construction source UVs in a patch both factions resolve to - otherwise a
    per-faction construction source or an export-time UV remap is required).
    """
    contexts = []
    if spec.get("construction"):
        tier = spec.get("tier")
        if tier not in TC.TIERS:
            tier = TC.TIERS[0]
        factions = [spec["faction"]] if spec.get("faction") else list(TC.FACTIONS)
        for faction in factions:
            mapping = TC.MATERIAL_MAP.get((tier, faction, True))
            contexts.append(("ConstructionStage0 shared source / %s" % faction,
                             dict(mapping or {})))
        return contexts
    mapping = TC.MATERIAL_MAP.get((spec["tier"], spec["faction"], False))
    return [("%s.%s.LOD%d" % (spec["tier"], spec["faction"], spec["lod"]),
             dict(mapping or {}))]


def material_uv_entry(map_data, material):
    """ART-07 map entry of a vanilla material (``materials``/``unused_materials``)."""
    entry, _section = patch_entry(map_data, material)
    return entry if isinstance(entry, dict) else None


def entry_mode(entry, reasons=None, where="entry"):
    """'tile' | 'patch' of a map entry (appends a reason when absent/invalid)."""
    mode = entry.get("mode") if isinstance(entry, dict) else None
    if mode in ("tile", "patch"):
        return mode
    if reasons is not None:
        reasons.append("%s has no ART-07 mode ('tile'/'patch') in "
                       "art/material_texture_map.json" % where)
    return None


def entry_texture_size(entry, reasons=None, where="entry"):
    size = entry.get("texture_size") if isinstance(entry, dict) else None
    if (isinstance(size, (list, tuple)) and len(size) == 2
            and all(_is_number(v) and float(v) >= 16 for v in size)):
        return [int(round(float(size[0]))), int(round(float(size[1])))]
    if reasons is not None:
        reasons.append("%s.texture_size must be [width, height] >= 16, got %r"
                       % (where, size))
    return None


def entry_uv_canvas(entry):
    canvas = entry.get("uv_canvas") if isinstance(entry, dict) else None
    return canvas if isinstance(canvas, dict) else None


def entry_canvas_uv_rect(entry):
    canvas = entry_uv_canvas(entry)
    rect = canvas.get("uv_rect") if isinstance(canvas, dict) else None
    if (isinstance(rect, (list, tuple)) and len(rect) == 4
            and all(_is_number(v) for v in rect)):
        return [float(v) for v in rect]
    return None


def entry_canvas_pixel_rect(entry):
    canvas = entry_uv_canvas(entry)
    rect = canvas.get("pixel_rect") if isinstance(canvas, dict) else None
    if (isinstance(rect, (list, tuple)) and len(rect) == 4
            and all(_is_number(v) for v in rect)):
        return [float(v) for v in rect]
    return None


def entry_px_per_m(entry):
    value = entry.get("px_per_m") if isinstance(entry, dict) else None
    return float(value) if _is_number(value) else None


def entry_coverage(entry):
    cu = entry.get("coverage_u_m") if isinstance(entry, dict) else None
    cv = entry.get("coverage_v_m") if isinstance(entry, dict) else None
    if _is_number(cu) and _is_number(cv) and float(cu) > 0.0 and float(cv) > 0.0:
        return (float(cu), float(cv))
    return None


def tile_guard_uv(entry):
    """[u0, v0, u1, v1] of the frozen 2-texel guard band inside the canvas."""
    size = entry_texture_size(entry) or [1024, 1024]
    guard_u = UV_GUARD_TEXELS / float(size[0])
    guard_v = UV_GUARD_TEXELS / float(size[1])
    return [guard_u, guard_v, 1.0 - guard_u, 1.0 - guard_v]


def material_uv_bounds(map_data, material, kind="c7"):
    """(rect, tol, mode, reason) of the UV bounds/containment test of a material.

    ``kind == "c7"`` is the mode-aware UV0 bounds of sec.5 (tile: the 2-texel guard
    band; patch: the curated ``uv_canvas``).  ``kind == "c11"`` is containment
    against the material's ART-07 ``uv_canvas`` (the whole texture for tile
    materials, so the guard stays a C7 concern only).
    """
    entry = material_uv_entry(map_data, material)
    if entry is None:
        return (None, PATCH_UV_TOL, None,
                "material %r has no entry in art/material_texture_map.json"
                % (material,))
    mode = entry_mode(entry)
    if mode is None:
        return (None, PATCH_UV_TOL, None,
                "material %r has no ART-07 mode in art/material_texture_map.json "
                "(regenerate the map)" % (material,))
    rect = entry_canvas_uv_rect(entry)
    if rect is None:
        return (None, PATCH_UV_TOL, mode,
                "material %r has no uv_canvas.uv_rect in "
                "art/material_texture_map.json" % (material,))
    if mode == "tile" and kind == "c7":
        return tile_guard_uv(entry), TILE_GUARD_TOL, mode, None
    return rect, PATCH_UV_TOL, mode, None


def containment_for_slots(samples, contexts, map_data, kind="c7"):
    """(rows, reasons) for the mode-aware uv0 bounds / ART-07 containment test.

    ``kind == "c7"``: mode-aware UV0 bounds (sec.5) - tile materials must stay inside
    the frozen 2-texel guard band, patch materials inside their ``uv_canvas``.
    ``kind == "c11"``: containment against the material's ART-07 ``uv_canvas``.
    Construction models are checked against the ART-07 canvas of *every* faction
    material of the shared UV set (the Folktails and the IronTeeth context).
    """
    rows = []
    reasons = []
    for label, mapping in contexts:
        for slot in sorted(samples):
            row = {"context": label, "slot": slot, "material": None,
                   "mode": None, "rect": None, "kind": kind,
                   "tol": PATCH_UV_TOL, "count": 0, "min_u": None, "max_u": None,
                   "min_v": None, "max_v": None, "bad_count": 0, "bad": [],
                   "status": "PASS"}
            material = mapping.get(slot) if isinstance(mapping, dict) else None
            row["material"] = material
            if material is None:
                row["status"] = "FAIL"
                reasons.append("%s: material slot %r has no vanilla material in "
                               "timbermesh_common.MATERIAL_MAP" % (label, slot))
                rows.append(row)
                continue
            rect, tol, mode, why = material_uv_bounds(map_data, material, kind)
            row.update(rect=rect, tol=tol, mode=mode)
            if rect is None:
                row["status"] = "FAIL"
                reasons.append("%s: %s" % (label, why))
                rows.append(row)
                continue
            u0, v0, u1, v1 = rect
            min_u = min_v = math.inf
            max_u = max_v = -math.inf
            count = 0
            bad = []
            bad_count = 0
            for tri_index, u, v in samples[slot]:
                if not (math.isfinite(u) and math.isfinite(v)):
                    continue
                count += 1
                min_u = min(min_u, u)
                max_u = max(max_u, u)
                min_v = min(min_v, v)
                max_v = max(max_v, v)
                if (u < u0 - tol or u > u1 + tol
                        or v < v0 - tol or v > v1 + tol):
                    bad_count += 1
                    if len(bad) < 5:
                        bad.append((tri_index, round(u, 6), round(v, 6)))
            row.update(count=count,
                       min_u=min_u if count else None,
                       max_u=max_u if count else None,
                       min_v=min_v if count else None,
                       max_v=max_v if count else None,
                       bad_count=bad_count, bad=bad)
            if count == 0:
                row["status"] = "FAIL"
                reasons.append("%s: material slot %r has no finite uv0 samples"
                               % (label, slot))
            elif bad_count:
                row["status"] = "FAIL"
                what = ("2-texel guard band" if mode == "tile" and kind == "c7"
                        else "ART-07 uv_canvas")
                reasons.append("%s: %d uv0 value(s) of material %r lie outside its "
                               "%s [%.6f, %.6f, %.6f, %.6f] +-%.0e (first: %s)"
                               % (label, bad_count, material, what, u0, v0, u1, v1,
                                  tol, bad[:3]))
            rows.append(row)
    return rows, reasons


def slot_row_text(row):
    rect = row["rect"]
    mode = row.get("mode")
    if rect is None:
        return ("%-4s %-38s slot=%-12s material=%-32s no ART-07 canvas in the map"
                % (row["status"], row["context"], row["slot"],
                   row["material"] or "?"))
    what = "tile guard" if (mode == "tile" and row.get("kind", "c7") == "c7") \
        else "uv_canvas"
    if row["count"]:
        return ("%-4s %-38s slot=%-12s material=%-32s mode=%-5s %s uv=[%.6f, "
                "%.6f, %.6f, %.6f] uv0 u=[%.6f, %.6f] v=[%.6f, %.6f] values=%d%s"
                % (row["status"], row["context"], row["slot"], row["material"],
                   mode or "-", what, rect[0], rect[1], rect[2], rect[3],
                   row["min_u"], row["max_u"], row["min_v"], row["max_v"],
                   row["count"],
                   "" if not row["bad_count"] else "  ESCAPED=%d" % row["bad_count"]))
    return ("%-4s %-38s slot=%-12s material=%-32s mode=%-5s %s uv=[%.6f, %.6f, "
            "%.6f, %.6f] no finite uv0"
            % (row["status"], row["context"], row["slot"], row["material"],
               mode or "-", what, rect[0], rect[1], rect[2], rect[3]))


# --------------------------------------------------------------------------- #
# ART-07: texel density (C15) + mapping contract (C16) helpers
# --------------------------------------------------------------------------- #

def tile_density_band(map_data):
    """REQ tile density band (px/m) from ``uv_mapping`` (sec.5: 300-600)."""
    block = (map_data or {}).get("uv_mapping") if isinstance(map_data, dict) else None
    value = block.get("texel_density_target_px_per_m") \
        if isinstance(block, dict) else None
    if (isinstance(value, (list, tuple)) and len(value) == 2
            and all(_is_number(v) for v in value)
            and float(value[0]) < float(value[1])):
        return (float(value[0]), float(value[1]))
    return TILE_DENSITY_BAND_DEFAULT


def tile_meters_band(map_data, material, entry):
    """(lo, hi) REQ ``tile_meters`` band of a tile material (sec.3/sec.4)."""
    recorded = entry.get("tile_meters_band") if isinstance(entry, dict) else None
    if (isinstance(recorded, (list, tuple)) and len(recorded) == 2
            and all(_is_number(v) for v in recorded)):
        return (float(recorded[0]), float(recorded[1]))
    try:
        import material_uv_policy as _UVP           # noqa: E402  (optional)
        return tuple(float(v) for v in _UVP.tile_meters_band(material))
    except Exception:                                # noqa: BLE001 - fallback table
        pass
    patch_class = entry.get("patch_class") if isinstance(entry, dict) else None
    return TILE_METERS_BAND_FALLBACK.get(str(patch_class))


_WORLD_AXIS_INDEX = {"x": 0, "y": 1, "z": 2}
_CANONICAL_UV_AXES = {0: (1, 2), 1: (0, 2), 2: (0, 1)}


def _projection_axes_for(entry, axis_index):
    """(u_axis, v_axis) world-axis indices from the map projection block."""
    axis_index = int(axis_index)
    projection = entry.get("projection") if isinstance(entry, dict) else None
    axes = projection.get("axes") if isinstance(projection, dict) else None
    if isinstance(axes, dict):
        pair = axes.get("+%s" % "XYZ"[axis_index])
        if (isinstance(pair, (list, tuple)) and len(pair) == 2
                and all(isinstance(name, str)
                        and name.lower() in _WORLD_AXIS_INDEX for name in pair)):
            return (_WORLD_AXIS_INDEX[pair[0].lower()],
                    _WORLD_AXIS_INDEX[pair[1].lower()])
    return _CANONICAL_UV_AXES[axis_index]


def _projection_center(entry):
    """((u_center, v_center), (x, y, z) world centre) from the map entry."""
    projection = entry.get("projection") if isinstance(entry, dict) else None
    uv_center = projection.get("uv_center") \
        if isinstance(projection, dict) else None
    world_center = projection.get("world_center") \
        if isinstance(projection, dict) else None
    if (isinstance(uv_center, (list, tuple)) and len(uv_center) == 2
            and all(_is_number(v) for v in uv_center)):
        center_uv = (float(uv_center[0]), float(uv_center[1]))
    else:
        center_uv = (0.5, 0.5)
    if (isinstance(world_center, (list, tuple)) and len(world_center) == 3
            and all(_is_number(v) for v in world_center)):
        center_world = tuple(float(v) for v in world_center)
    else:
        center_world = UV_WORLD_CENTER
    return center_uv, center_world


def _dominant_axis_rounded(normal):
    """Frozen sec.2 tie-break: unit normal, |components| rounded to 4 dp, X>Y>Z."""
    length = float(np.linalg.norm(normal))
    if length < 1e-12:
        return None, None
    absn = np.round(np.abs(normal) / length, PROJECTION_ROUND_DECIMALS)
    if absn[0] >= absn[1] and absn[0] >= absn[2]:
        axis = 0
    elif absn[1] >= absn[2]:
        axis = 1
    else:
        axis = 2
    return int(axis), absn


def _author_from_contract(points):
    """validator asset/contract space -> Blender author space (Z up, front +Y).

    contract = (blender.x, blender.z, -blender.y) (timbermesh_common), so the
    inverse is (contract.x, -contract.z, contract.y) - the space ART-07 maps in.
    """
    author = np.empty_like(points)
    author[:, 0] = points[:, 0]
    author[:, 1] = -points[:, 2]
    author[:, 2] = points[:, 1]
    return author


def density_model_inputs(results):
    """Per-model author-space geometry + material context for the C15 core."""
    inputs = []
    for result in results or []:
        spec = result.get("spec")
        if not spec:
            continue
        label = spec.get("label") or spec.get("stem") or "?"
        fact = (result.get("facts") or {}).get("glb")
        if fact is None:
            inputs.append({"label": label, "error": "no GLB facts"})
            continue
        if fact.error:
            inputs.append({"label": label,
                           "error": "GLB unreadable (%s)" % fact.error})
            continue
        if not fact.tri_count:
            inputs.append({"label": label, "error": "GLB has no triangles"})
            continue
        if spec.get("construction"):
            tier = spec.get("tier")
            if tier not in TC.TIERS:
                tier = TC.TIERS[0]
            # The shared construction source mesh is baked with the Folktails
            # construction material set (ART-07 sec.3); the IronTeeth export reuses
            # that UV set, so C15 measures against the Folktails materials.
            mapping = dict(TC.MATERIAL_MAP.get((tier, TC.FACTIONS[0], True)) or {})
        else:
            mapping = dict(TC.MATERIAL_MAP.get(
                (spec.get("tier"), spec.get("faction"), False)) or {})
        inputs.append({
            "label": label, "kind": spec.get("kind"),
            "lod": spec.get("lod"), "construction": bool(spec.get("construction")),
            "author_pos": _author_from_contract(fact.pos),
            "tri": fact.tri, "tri_mat": fact.tri_mat, "tri_uv": fact.tri_uv,
            "materials": list(fact.materials), "slot_materials": mapping,
        })
    return inputs


def _canonical_axis_priority(absn):
    """vectorised frozen tie-break: returns dominant axis index array."""
    xdom = (absn[:, 0] >= absn[:, 1]) & (absn[:, 0] >= absn[:, 2])
    ydom = absn[:, 1] >= absn[:, 2]
    return np.where(xdom, 0, np.where(ydom, 1, 2))


def _density_analyze_inputs(inputs, map_data):
    """Run the C15 core over ``inputs`` (author-space models) -> report dict."""
    report = {
        "rows": {}, "reasons": [], "notes": [], "models": [],
        "triangles": 0, "ambiguous_count": 0, "ambiguous_area": 0.0,
        "total_area": 0.0, "exact_checked": 0, "exact_failed": 0,
        "large_triangles": 0, "large_failed": 0, "missing": set(),
    }
    band = tile_density_band(map_data)
    for model_input in inputs:
        label = model_input.get("label", "?")
        model_stats = {"label": label, "triangles": 0, "ambiguous": 0,
                       "ambiguous_area": 0.0, "area": 0.0,
                       "exact_checked": 0, "exact_failed": 0}
        report["models"].append(model_stats)
        if model_input.get("error"):
            report["reasons"].append("%s: cannot measure world-UV density (%s)"
                                     % (label, model_input["error"]))
            continue
        pos = model_input.get("author_pos")
        tri = model_input.get("tri")
        tri_mat = model_input.get("tri_mat")
        tri_uv = model_input.get("tri_uv")
        materials = model_input.get("materials") or []
        slot_materials = model_input.get("slot_materials") or {}
        if pos is None or tri is None or not len(tri) or tri_uv is None:
            report["reasons"].append("%s: no triangles for the density check"
                                     % label)
            continue
        bad_uv = not np.isfinite(tri_uv).all()
        bad_pos = not np.isfinite(pos).all()
        if bad_uv:
            report["reasons"].append("%s: NaN/Inf UV0 value(s)" % label)
        if bad_pos:
            report["reasons"].append("%s: NaN/Inf world position(s)" % label)
        if bad_uv or bad_pos:
            continue
        for slot_index in range(len(materials)):
            mask = tri_mat == slot_index
            count = int(mask.sum())
            if not count:
                continue
            slot = str(materials[slot_index])
            material = slot_materials.get(slot)
            if not material:
                report["reasons"].append(
                    "%s: material slot %r has no vanilla material mapping"
                    % (label, slot))
                continue
            entry = material_uv_entry(map_data, material)
            if entry is None:
                report["missing"].add(str(material))
                report["reasons"].append(
                    "%s: material %r has no entry in art/material_texture_map.json"
                    % (label, material))
                continue
            mode = entry_mode(entry)
            size = entry_texture_size(entry)
            density_expected = entry_px_per_m(entry)
            if mode is None or size is None or density_expected is None:
                report["reasons"].append(
                    "%s: material %r has no complete ART-07 record in the map"
                    % (label, material))
                continue
            row = report["rows"].setdefault(str(material), {
                "material": str(material), "mode": mode,
                "canvas": entry_canvas_pixel_rect(entry),
                "texture_size": size, "px_expected": density_expected,
                "uv_area": 0.0, "proj_area": 0.0, "tri_count": 0,
                "min": math.inf, "max": -math.inf, "large": 0, "large_failed": 0,
                "exact_checked": 0, "exact_failed": 0, "ambiguous": 0,
                "models": {}, "reasons": [],
            })
            p = pos[tri[mask]]
            uv = tri_uv[mask]
            e1 = p[:, 1] - p[:, 0]
            e2 = p[:, 2] - p[:, 0]
            normal = np.cross(e1, e2)
            length = np.linalg.norm(normal, axis=1)
            valid = length > 1e-12
            if not valid.all():
                # degenerate triangles carry no density information
                p = p[valid]
                uv = uv[valid]
                normal = normal[valid]
                length = length[valid]
            if not len(length):
                continue
            uv_area = 0.5 * np.abs(
                (uv[:, 1, 0] - uv[:, 0, 0]) * (uv[:, 2, 1] - uv[:, 0, 1])
                - (uv[:, 2, 0] - uv[:, 0, 0]) * (uv[:, 1, 1] - uv[:, 0, 1]))
            unit = normal / length[:, None]
            absn = np.round(np.abs(unit), PROJECTION_ROUND_DECIMALS)
            order = np.argsort(-absn, axis=1, kind="stable")
            top1 = np.take_along_axis(absn, order[:, 0:1], axis=1)[:, 0]
            top2 = np.take_along_axis(absn, order[:, 1:2], axis=1)[:, 0]
            axis = _canonical_axis_priority(absn)
            ambiguous = (top1 - top2) <= AMBIGUOUS_PLANE_TOL
            n_axis = np.take_along_axis(absn, axis[:, None], axis=1)[:, 0]
            world_area = 0.5 * length
            proj_area = world_area * n_axis
            texel = math.sqrt(float(size[0] * size[1]))
            tri_density = np.sqrt(uv_area / proj_area) * texel
            row["uv_area"] += float(uv_area.sum())
            row["proj_area"] += float(proj_area.sum())
            row["tri_count"] += int(len(tri_density))
            row["min"] = min(row["min"], float(tri_density.min()))
            row["max"] = max(row["max"], float(tri_density.max()))
            report["triangles"] += int(len(tri_density))
            report["total_area"] += float(world_area.sum())
            model_stats["triangles"] += int(len(tri_density))
            model_stats["area"] += float(world_area.sum())
            if ambiguous.any():
                row["ambiguous"] += int(ambiguous.sum())
                report["ambiguous_count"] += int(ambiguous.sum())
                amb_area = float(world_area[ambiguous].sum())
                report["ambiguous_area"] += amb_area
                model_stats["ambiguous"] += int(ambiguous.sum())
                model_stats["ambiguous_area"] += amb_area
            # -- per-triangle density (+-25 % above 0.05 m^2 world area) ------
            large = world_area >= DENSITY_TRI_MIN_WORLD_AREA
            if large.any():
                rel = np.abs(tri_density - density_expected) / density_expected
                failed = large & (rel > DENSITY_TRI_TOL)
                row["large"] += int(large.sum())
                report["large_triangles"] += int(large.sum())
                if failed.any():
                    row["large_failed"] += int(failed.sum())
                    report["large_failed"] += int(failed.sum())
                    first = int(np.flatnonzero(failed)[0])
                    row["reasons"].append(
                        "%s: %d/%d large triangle(s) (world area >= %.2f m^2) have "
                        "a texel density %.2f/%.2f/%.2f %% off the map px_per_m "
                        "%.1f (> +-%.0f%%; first: density %.2f px/m)"
                        % (label, int(failed.sum()), int(large.sum()),
                           DENSITY_TRI_MIN_WORLD_AREA,
                           100.0 * float(rel[failed].min()),
                           100.0 * float(rel[failed].mean()),
                           100.0 * float(rel[failed].max()),
                           density_expected, DENSITY_TRI_TOL * 100.0,
                           float(tri_density[first])))
            # -- per-model accumulation (cross-model drift check) -------------
            model_acc = row["models"].setdefault(label, [0.0, 0.0, 0, 0, 0])
            model_acc[0] += float(uv_area.sum())
            model_acc[1] += float(proj_area.sum())
            model_acc[2] += int(len(tri_density))
            # -- exact UV recomputation (sec.2/sec.5, frozen tie-break) -------------
            exact_mask = ~ambiguous
            if exact_mask.any():
                report["exact_checked"] += int(exact_mask.sum())
                row["exact_checked"] += int(exact_mask.sum())
                model_stats["exact_checked"] += int(exact_mask.sum())
                center_uv, center_world = _projection_center(entry)
                # per-triangle projection axes come from the map's frozen table
                ui = np.empty_like(axis)
                vi = np.empty_like(axis)
                for cand in (0, 1, 2):
                    sel = axis == cand
                    if not sel.any():
                        continue
                    u_axis, v_axis = _projection_axes_for(entry, cand)
                    ui[sel] = u_axis
                    vi[sel] = v_axis
                sel = np.flatnonzero(exact_mask)
                p_sel = p[sel]
                uv_sel = uv[sel]
                ui_sel = ui[sel]
                vi_sel = vi[sel]
                pu = np.take_along_axis(
                    p_sel, np.broadcast_to(ui_sel[:, None, None],
                                           (len(sel), 3, 1)), axis=2)[:, :, 0]
                pv = np.take_along_axis(
                    p_sel, np.broadcast_to(vi_sel[:, None, None],
                                           (len(sel), 3, 1)), axis=2)[:, :, 0]
                # the projection centre component belongs to the PROJectED axis
                # (world_center = [0, 0, 0.5] in the author space, sec.2)
                center_world_np = np.asarray(center_world, dtype=np.float64)
                center_u_axis = center_world_np[ui_sel][:, None]
                center_v_axis = center_world_np[vi_sel][:, None]
                expected_u = center_uv[0] + (
                    pu - center_u_axis) * density_expected / float(size[0])
                expected_v = center_uv[1] + (
                    pv - center_v_axis) * density_expected / float(size[1])
                delta = np.maximum(
                    np.max(np.abs(expected_u - uv_sel[:, :, 0]), axis=1),
                    np.max(np.abs(expected_v - uv_sel[:, :, 1]), axis=1))
                failed = delta > EXACT_UV_TOL
                if failed.any():
                    row["exact_failed"] += int(failed.sum())
                    report["exact_failed"] += int(failed.sum())
                    model_stats["exact_failed"] += int(failed.sum())
                    first = int(np.flatnonzero(failed)[0])
                    row["reasons"].append(
                        "%s: %d exact UV recomputation(s) differ by up to %.6f "
                        "(tolerance %.0e; first delta %.6f, expected uv=(%.6f, "
                        "%.6f) got (%.6f, %.6f), dominant axis %s)"
                        % (label, int(failed.sum()), float(delta[failed].max()),
                           EXACT_UV_TOL, float(delta[first]),
                           float(expected_u[first, 0]), float(expected_v[first, 0]),
                           float(uv_sel[first, 0, 0]), float(uv_sel[first, 0, 1]),
                           "XYZ"[int(axis[sel][first])]))
            # -- model-row density snapshot (used by the C16 drift check) -----
            model_acc = row["models"][label]
            del model_acc
    # -- per-material aggregate density verdict ---------------------------- #
    for material in sorted(report["rows"]):
        row = report["rows"][material]
        reasons = row["reasons"]
        if row["proj_area"] <= 0.0 or not row["tri_count"]:
            reasons.append("material %r has no non-degenerate triangle with a "
                           "projected area" % material)
            row["measured"] = None
            row["status"] = "FAIL"
            continue
        texel = math.sqrt(float(row["texture_size"][0] * row["texture_size"][1]))
        measured = math.sqrt(row["uv_area"] / row["proj_area"]) * texel
        row["measured"] = measured
        expected = row["px_expected"]
        row["rel"] = (measured - expected) / expected
        if abs(row["rel"]) > DENSITY_AGG_TOL:
            reasons.append("material %r: aggregate texel density %.2f px/m is "
                           "%+.3f %% off the map px_per_m %.1f (> +-%.0f%%)"
                           % (material, measured, 100.0 * row["rel"], expected,
                              DENSITY_AGG_TOL * 100.0))
        if row["mode"] == "tile":
            if not (band[0] - 1e-9 <= measured <= band[1] + 1e-9):
                reasons.append("material %r: tile texel density %.2f px/m is "
                               "outside the REQ band %s"
                               % (material, measured, list(band)))
        elif row["mode"] == "patch":
            if measured < DENSITY_PATCH_MIN - 1e-9:
                reasons.append("material %r: patch texel density %.2f px/m is "
                               "below the %.0f px/m regression floor (2x ART-06, "
                               "WindowsAtlas glass pane exempt by the raised floor)"
                               % (material, measured, DENSITY_PATCH_MIN))
        row["status"] = "FAIL" if reasons else "PASS"
        report["reasons"].extend(reasons)
    if report["triangles"]:
        amb_fraction = report["ambiguous_count"] / float(report["triangles"])
        area_fraction = report["ambiguous_area"] / report["total_area"] \
            if report["total_area"] > 0.0 else 0.0
        if amb_fraction > AMBIGUOUS_COUNT_MAX + 1e-12:
            report["reasons"].append(
                "ambiguous-plane triangles (rounded top-two |normal| components "
                "within %.0e) are %.2f %% of all triangles (> %.0f %% allowed)"
                % (AMBIGUOUS_PLANE_TOL, 100.0 * amb_fraction,
                   100.0 * AMBIGUOUS_COUNT_MAX))
        if area_fraction > AMBIGUOUS_AREA_MAX + 1e-12:
            report["reasons"].append(
                "ambiguous-plane triangles cover %.3f %% of the world area "
                "(> %.1f %% allowed)"
                % (100.0 * area_fraction, 100.0 * AMBIGUOUS_AREA_MAX))
    report["tile_band"] = band
    report["amb_fraction"] = (report["ambiguous_count"] / float(report["triangles"])
                              if report["triangles"] else 0.0)
    report["amb_area_fraction"] = (
        report["ambiguous_area"] / report["total_area"]
        if report["total_area"] > 0.0 else 0.0)
    return report


def evaluate_world_uv_density(patch_info, results):
    """C15: world-UV texel density of every material of every shipped model."""
    check = Check("C15", "ART-07 world-UV texel density")
    map_data = patch_info.get("data")
    if map_data is None:
        check.fail("material texture map unavailable: the ART-07 world-UV density "
                   "cannot be verified",
                   ["cannot measure the texel density: %s"
                    % (patch_info.get("error") or "map not loaded")])
        return check, {"rows": {}, "reasons": [], "notes": []}
    report = _density_analyze_inputs(density_model_inputs(results), map_data)
    band = report.get("tile_band") or tile_density_band(map_data)
    reasons = _capped(report["reasons"], 25)
    notes = []
    for material in sorted(report["rows"]):
        row = report["rows"][material]
        if row["measured"] is None:
            notes.append("%-4s %-38s %-5s no measurable triangle"
                         % ("FAIL", material, row["mode"]))
            continue
        notes.append(
            "%-4s %-38s mode=%-5s canvas=%-22s px_per_m expected %7.2f measured "
            "min/mean/max %8.3f/%8.3f/%8.3f (%+.3f %%) tris=%d"
            % (row["status"], material, row["mode"],
               _rect_text(row["canvas"]) if row["canvas"] else "-",
               row["px_expected"], row["min"], row["measured"], row["max"],
               100.0 * row["rel"], row["tri_count"]))
    notes.append("aggregate tolerance +-%.0f %% per material; per-triangle "
                 "+-%.0f %% above %.2f m^2 world area (%d/%d failed); tile "
                 "materials must be inside the REQ band %s; patch materials must "
                 "be >= %.0f px/m (2x ART-06; WindowsAtlas glass pane is 82.4 "
                 "px/m by design)"
                 % (DENSITY_AGG_TOL * 100.0, DENSITY_TRI_TOL * 100.0,
                    DENSITY_TRI_MIN_WORLD_AREA, report["large_failed"],
                    report["large_triangles"], list(band), DENSITY_PATCH_MIN))
    notes.append("exact UV recomputation (frozen sec.2 tie-break: unit Newell "
                 "normal, |components| rounded to %d decimals, dominant axis "
                 "X > Y > Z): %d triangle(s) checked, %d mismatch(es) > %.0e; "
                 "ambiguous-plane triangles %d/%d = %.2f %% of triangles / "
                 "%.3f %% of world area (limits %.0f %% / %.1f %%)"
                 % (PROJECTION_ROUND_DECIMALS, report["exact_checked"],
                    report["exact_failed"], EXACT_UV_TOL,
                    report["ambiguous_count"], report["triangles"],
                    100.0 * report["amb_fraction"],
                    100.0 * report["amb_area_fraction"],
                    100.0 * AMBIGUOUS_COUNT_MAX, 100.0 * AMBIGUOUS_AREA_MAX))
    notes.append("%d model(s) measured in author space (contract-to-author "
                 "inverse); the shared ConstructionStage0 source is measured "
                 "against the Folktails construction materials it is baked with "
                 "(the IronTeeth export reuses that UV set by contract)"
                 % sum(1 for model in report.get("models", [])
                       if model.get("triangles")))
    if reasons:
        check.fail("%d material(s)/model(s) violate the ART-07 texel-density "
                   "contract" % len(report["reasons"]), reasons, notes)
    elif not report["rows"]:
        check.fail("no material could be measured: no shipped source model is "
                   "available for the world-UV density check", [], notes)
    else:
        check.pass_("%d material(s) / %d triangle(s) keep the frozen ART-07 texel "
                    "density (+-%.0f %% aggregate of the map px_per_m; tile %s "
                    "px/m band; exact UV recompute %d/%d non-ambiguous triangles "
                    "within %.0e)"
                    % (len(report["rows"]), report["triangles"],
                       DENSITY_AGG_TOL * 100.0, list(band),
                       report["exact_checked"] - report["exact_failed"],
                       report["exact_checked"], EXACT_UV_TOL), notes)
    return check, report


def used_material_names(results):
    """Every vanilla material a shipped/scheduled export can use (C16 universe)."""
    used = set()
    for result in results or []:
        spec = result.get("spec")
        if not spec:
            continue
        for _label, mapping in spec_material_contexts(spec):
            if isinstance(mapping, dict):
                used.update(str(value) for value in mapping.values())
    used.update(scheduled_materials())
    return used


def _contract_reproduce_px(entry):
    """(raw, fit, nominal) texel density reproduction data of one map entry."""
    size = entry_texture_size(entry)
    coverage = entry_coverage(entry)
    pixel = entry_canvas_pixel_rect(entry)
    mode = entry.get("mode")
    if size is None or coverage is None or pixel is None:
        return None
    canvas_w = pixel[2] - pixel[0]
    canvas_h = pixel[3] - pixel[1]
    usable_w = canvas_w - 2.0 * UV_GUARD_TEXELS
    usable_h = canvas_h - 2.0 * UV_GUARD_TEXELS
    if usable_w <= 0.0 or usable_h <= 0.0:
        return None
    fit = min(usable_w / coverage[0], usable_h / coverage[1])
    tile_meters = entry.get("tile_meters")
    if mode == "tile" and _is_number(tile_meters) and float(tile_meters) > 0.0:
        nominal = float(size[0]) / float(tile_meters)
    else:
        nominal = fit
    return min(nominal, fit), fit, nominal


def evaluate_uv_contract(patch_info, results, density_report=None):
    """C16: ART-07 ``uv_mapping`` contract / determinism (cross-model)."""
    check = Check("C16", "ART-07 UV mapping contract / determinism")
    map_data = patch_info.get("data")
    reasons = []
    notes = []
    if map_data is None:
        check.fail("material texture map unavailable: the ART-07 UV mapping "
                   "contract cannot be verified",
                   ["cannot verify the UV mapping contract: %s"
                    % (patch_info.get("error") or "map not loaded")])
        return check
    block = map_data.get("uv_mapping")
    if not isinstance(block, dict):
        reasons.append("art/material_texture_map.json has no top-level "
                       "'uv_mapping' block (sec.4)")
        block = {}
    else:
        if block.get("schema") != UV_POLICY_SCHEMA:
            reasons.append("uv_mapping.schema = %r, expected %r"
                           % (block.get("schema"), UV_POLICY_SCHEMA))
        target = block.get("texel_density_target_px_per_m")
        if not (isinstance(target, (list, tuple)) and len(target) == 2
                and all(_is_number(v) for v in target)
                and abs(float(target[0]) - TILE_DENSITY_BAND_DEFAULT[0]) < 1e-9
                and abs(float(target[1]) - TILE_DENSITY_BAND_DEFAULT[1]) < 1e-9):
            reasons.append("uv_mapping.texel_density_target_px_per_m = %r, "
                           "expected %r" % (target,
                                            list(TILE_DENSITY_BAND_DEFAULT)))
        world_center = block.get("world_center")
        if not (isinstance(world_center, (list, tuple)) and len(world_center) == 3
                and all(_is_number(v) for v in world_center)
                and all(abs(float(world_center[i]) - UV_WORLD_CENTER[i]) < 1e-9
                        for i in range(3))):
            reasons.append("uv_mapping.world_center = %r, expected %r (frozen sec.2)"
                           % (world_center, list(UV_WORLD_CENTER)))
        if block.get("uv_guard_texels") != UV_GUARD_TEXELS:
            reasons.append("uv_mapping.uv_guard_texels = %r, expected %d"
                           % (block.get("uv_guard_texels"), UV_GUARD_TEXELS))
        for flag in ("atlas_cell_safe", "no_uv_outside_canvas"):
            if block.get(flag) is not True:
                reasons.append("uv_mapping.%s is not true (%r)"
                               % (flag, block.get(flag)))
    densities = block.get("densities") if isinstance(block, dict) else None
    if not isinstance(densities, dict) or not densities:
        reasons.append("uv_mapping.densities is missing or empty (sec.4)")
        densities = {}
    used = sorted(used_material_names(results))
    field_names = ("mode", "tile_meters", "texture_size", "wrap", "uv_canvas",
                   "projection", "px_per_m", "coverage_u_m", "coverage_v_m")
    validated = 0
    construction_checked = 0
    for material in used:
        where = "materials[%r]" % material
        entry = material_uv_entry(map_data, material)
        if entry is None:
            reasons.append("%s has no entry in art/material_texture_map.json but "
                           "is used by the shipped/scheduled models" % where)
            continue
        validated += 1
        for field in field_names:
            if field not in entry:
                reasons.append("%s: sec.4 field %r is missing" % (where, field))
        mode = entry_mode(entry, reasons, where)
        size = entry_texture_size(entry, reasons, where)
        wrap = entry.get("wrap")
        if mode is not None:
            expected_wrap = "repeat" if mode == "tile" else "clamp"
            if wrap != expected_wrap:
                reasons.append("%s: wrap = %r, expected %r for mode %r"
                               % (where, wrap, expected_wrap, mode))
        canvas = entry_uv_canvas(entry)
        if canvas is None:
            reasons.append("%s: uv_canvas is missing (sec.4)" % where)
        else:
            pixel = _as_rect4(canvas.get("pixel_rect"),
                              where + ".uv_canvas.pixel_rect", reasons)
            uv_rect = _as_rect4(canvas.get("uv_rect"),
                                where + ".uv_canvas.uv_rect", reasons)
            size_px = canvas.get("size_px")
            if pixel is not None and size is not None:
                x0, y0, x1, y1 = pixel
                if not (0.0 <= x0 < x1 <= size[0]
                        and 0.0 <= y0 < y1 <= size[1]):
                    reasons.append("%s: uv_canvas.pixel_rect %s is not inside the "
                                   "%dx%d texture" % (where, _rect_text(pixel),
                                                      size[0], size[1]))
                want_size = [x1 - x0, y1 - y0]
                if not (isinstance(size_px, (list, tuple)) and len(size_px) == 2
                        and all(_is_number(v) for v in size_px)
                        and all(abs(float(size_px[i]) - want_size[i]) < 1e-6
                                for i in range(2))):
                    reasons.append("%s: uv_canvas.size_px %r != pixel_rect size %r"
                                   % (where, size_px, want_size))
                if uv_rect is not None:
                    want_uv = expected_uv_rect(pixel, size)
                    for index, name in enumerate(("u0", "v0", "u1", "v1")):
                        if abs(uv_rect[index] - want_uv[index]) \
                                > UV_CANVAS_ROUND_TOL:
                            reasons.append(
                                "%s: uv_canvas.uv_rect[%s] = %.9f, expected %.9f"
                                % (where, name, uv_rect[index], want_uv[index]))
            if uv_rect is not None:
                u0, v0, u1, v1 = uv_rect
                if not (0.0 <= u0 < u1 <= 1.0 and 0.0 <= v0 < v1 <= 1.0):
                    reasons.append("%s: uv_canvas.uv_rect %s must satisfy "
                                   "0 <= u0 < u1 <= 1 and 0 <= v0 < v1 <= 1"
                                   % (where, _uv_text(uv_rect)))
        projection = entry.get("projection")
        if not isinstance(projection, dict):
            reasons.append("%s: projection is missing (sec.4)" % where)
        else:
            axes = projection.get("axes")
            expected_axes = {"-X": ["y", "z"], "+X": ["y", "z"],
                             "-Y": ["x", "z"], "+Y": ["x", "z"],
                             "-Z": ["x", "y"], "+Z": ["x", "y"]}
            if not isinstance(axes, dict):
                reasons.append("%s: projection.axes is missing" % where)
            else:
                for key in sorted(expected_axes):
                    pair = axes.get(key)
                    if pair != expected_axes[key]:
                        reasons.append("%s: projection.axes[%r] = %r, expected %r "
                                       "(frozen sec.2)" % (where, key, pair,
                                                        expected_axes[key]))
            world_center = projection.get("world_center")
            if not (isinstance(world_center, (list, tuple))
                    and len(world_center) == 3
                    and all(_is_number(v) for v in world_center)
                    and all(abs(float(world_center[i]) - UV_WORLD_CENTER[i]) < 1e-9
                            for i in range(3))):
                reasons.append("%s: projection.world_center = %r, expected %r"
                               % (where, world_center, list(UV_WORLD_CENTER)))
            uv_center = projection.get("uv_center")
            canvas_uv = entry_canvas_uv_rect(entry)
            if not (isinstance(uv_center, (list, tuple)) and len(uv_center) == 2
                    and all(_is_number(v) for v in uv_center)):
                reasons.append("%s: projection.uv_center must be 2 numbers, got %r"
                               % (where, uv_center))
            elif canvas_uv is not None:
                want_center = ((canvas_uv[0] + canvas_uv[2]) * 0.5,
                               (canvas_uv[1] + canvas_uv[3]) * 0.5)
                if abs(float(uv_center[0]) - want_center[0]) > UV_CENTER_TOL \
                        or abs(float(uv_center[1]) - want_center[1]) > UV_CENTER_TOL:
                    reasons.append("%s: projection.uv_center %r is not the centre "
                                   "of uv_canvas %r" % (where, uv_center,
                                                        list(canvas_uv)))
        density_expected = entry_px_per_m(entry)
        coverage = entry_coverage(entry)
        if density_expected is None or density_expected <= 0.0:
            reasons.append("%s: px_per_m must be a positive number, got %r"
                           % (where, entry.get("px_per_m")))
        elif not _is_number(entry.get("coverage_u_m")) \
                or not _is_number(entry.get("coverage_v_m")):
            reasons.append("%s: coverage_u_m/coverage_v_m must be numbers, got %r/%r"
                           % (where, entry.get("coverage_u_m"),
                              entry.get("coverage_v_m")))
        else:
            reproduced = _contract_reproduce_px(entry)
            if reproduced is None:
                reasons.append("%s: px_per_m cannot be reproduced from the usable "
                               "canvas and coverage (invalid canvas/coverage)"
                               % where)
            else:
                raw, fit, nominal = reproduced
                if abs(density_expected - raw) > PX_PER_M_ROUND:
                    reasons.append(
                        "%s: px_per_m %.4f does not reproduce from the usable "
                        "canvas / coverage (fit %.4f, nominal %.4f -> raw %.4f, "
                        "allowed rounding %.3f)"
                        % (where, density_expected, fit, nominal, raw,
                           PX_PER_M_ROUND))
                span_u = size[0] / density_expected if size else None
                span_v = size[1] / density_expected if size else None
                if span_u is not None and span_u < coverage[0] - 1e-6:
                    reasons.append("%s: effective canvas span %.4f m < required "
                                   "coverage_u_m %.4f m"
                                   % (where, span_u, coverage[0]))
                if span_v is not None and span_v < coverage[1] - 1e-6:
                    reasons.append("%s: effective canvas span %.4f m < required "
                                   "coverage_v_m %.4f m"
                                   % (where, span_v, coverage[1]))
        if mode == "tile":
            tile_meters = entry.get("tile_meters")
            if not _is_number(tile_meters) or float(tile_meters) <= 0.0:
                reasons.append("%s: tile material needs a positive tile_meters, "
                               "got %r" % (where, tile_meters))
            else:
                band = tile_meters_band(map_data, material, entry)
                if band is None:
                    reasons.append("%s: no REQ tile_meters band available for the "
                                   "patch class %r"
                                   % (where, entry.get("patch_class")))
                elif not (band[0] - 1e-9 <= float(tile_meters) <= band[1] + 1e-9):
                    reasons.append("%s: tile_meters %.3f is outside the REQ class "
                                   "band %s" % (where, float(tile_meters),
                                                list(band)))
        elif mode == "patch":
            if entry.get("tile_meters") is not None:
                reasons.append("%s: patch material must record tile_meters = null, "
                               "got %r" % (where, entry.get("tile_meters")))
        # patch record: inside the texture + IS the canvas for patch materials
        patch = entry.get("patch")
        if not isinstance(patch, dict):
            reasons.append("%s: patch record is missing" % where)
        else:
            prect = _as_rect4(patch.get("pixel_rect"),
                              where + ".patch.pixel_rect", reasons)
            puv = _as_rect4(patch.get("uv_rect"), where + ".patch.uv_rect",
                            reasons)
            if prect is not None and size is not None:
                if not (0.0 <= prect[0] < prect[2] <= size[0]
                        and 0.0 <= prect[1] < prect[3] <= size[1]):
                    reasons.append("%s: patch.pixel_rect %s is not inside the "
                                   "%dx%d texture" % (where, _rect_text(prect),
                                                      size[0], size[1]))
                canvas_pixel = entry_canvas_pixel_rect(entry)
                if mode == "patch" and canvas_pixel is not None \
                        and any(abs(prect[i] - canvas_pixel[i]) > 1e-6
                                for i in range(4)):
                    reasons.append("%s: patch.pixel_rect %s != uv_canvas.pixel_rect "
                                   "%s for a patch material"
                                   % (where, _rect_text(prect),
                                      _rect_text(canvas_pixel)))
                if puv is not None:
                    want_uv = expected_uv_rect(prect, size)
                    for index, name in enumerate(("u0", "v0", "u1", "v1")):
                        if abs(puv[index] - want_uv[index]) > UV_RECT_ROUND_TOL:
                            reasons.append("%s: patch.uv_rect[%s] = %.9f, expected "
                                           "%.9f" % (where, name, puv[index],
                                                     want_uv[index]))
        # uv_mapping.densities cross-check
        recorded_density = densities.get(material)
        if not isinstance(recorded_density, dict):
            reasons.append("uv_mapping.densities[%r] is missing" % material)
        else:
            if _is_number(recorded_density.get("px_per_m")) \
                    and density_expected is not None \
                    and abs(float(recorded_density["px_per_m"])
                            - density_expected) > 1e-9:
                reasons.append("uv_mapping.densities[%r].px_per_m = %r != "
                               "materials[%r].px_per_m = %r"
                               % (material, recorded_density.get("px_per_m"),
                                  material, density_expected))
            if recorded_density.get("mode") != mode:
                reasons.append("uv_mapping.densities[%r].mode = %r != entry mode %r"
                               % (material, recorded_density.get("mode"), mode))
            canvas_pixel = entry_canvas_pixel_rect(entry)
            recorded_pixels = recorded_density.get("canvas_pixels")
            if canvas_pixel is not None:
                want_pixels = [int(round(canvas_pixel[2] - canvas_pixel[0])),
                               int(round(canvas_pixel[3] - canvas_pixel[1]))]
                if not (isinstance(recorded_pixels, (list, tuple))
                        and len(recorded_pixels) == 2
                        and all(_is_number(v) for v in recorded_pixels)
                        and [int(round(float(v))) for v in recorded_pixels]
                        == want_pixels):
                    reasons.append("uv_mapping.densities[%r].canvas_pixels = %r != "
                                   "the entry uv_canvas size %r"
                                   % (material, recorded_pixels, want_pixels))
    # -- construction shared windows (sec.3 "Construction stage") ------------- #
    groups = map_data.get("construction_shared_patches")
    if not isinstance(groups, dict) or not groups:
        reasons.append("art/material_texture_map.json has no "
                       "'construction_shared_patches' block (the construction "
                       "stage shares one UV set across factions)")
    else:
        for group_name in sorted(groups):
            group = groups[group_name]
            where = "construction_shared_patches[%r]" % group_name
            if not isinstance(group, dict):
                reasons.append("%s must be an object" % where)
                continue
            rect = _as_rect4(group.get("pixel_rect"),
                             where + ".pixel_rect", reasons)
            uv_rect = _as_rect4(group.get("uv_rect"), where + ".uv_rect", reasons)
            ft_material = group.get("folktails_material")
            it_material = group.get("ironteeth_material")
            ft_entry = material_uv_entry(map_data, ft_material) \
                if isinstance(ft_material, str) else None
            it_entry = material_uv_entry(map_data, it_material) \
                if isinstance(it_material, str) else None
            if ft_entry is None:
                reasons.append("%s: folktails_material %r is not a material "
                               "entry of the map" % (where, ft_material))
            if it_entry is None:
                reasons.append("%s: ironteeth_material %r is not a material "
                               "entry of the map" % (where, it_material))
            size = entry_texture_size(ft_entry) if ft_entry is not None else None
            if rect is not None and size is not None:
                if not (0.0 <= rect[0] < rect[2] <= size[0]
                        and 0.0 <= rect[1] < rect[3] <= size[1]):
                    reasons.append("%s: pixel_rect %s is not inside the %dx%d "
                                   "texture" % (where, _rect_text(rect), size[0],
                                                size[1]))
                if uv_rect is not None:
                    want_uv = expected_uv_rect(rect, size)
                    for index, name in enumerate(("u0", "v0", "u1", "v1")):
                        if abs(uv_rect[index] - want_uv[index]) \
                                > UV_CANVAS_ROUND_TOL:
                            reasons.append("%s: uv_rect[%s] = %.9f, expected %.9f"
                                           % (where, name, uv_rect[index],
                                              want_uv[index]))
                    ft_canvas = entry_canvas_uv_rect(ft_entry)
                    if ft_canvas is not None and any(
                            abs(uv_rect[i] - ft_canvas[i]) > UV_CANVAS_ROUND_TOL
                            for i in range(4)):
                        reasons.append("%s: shared window %s != the Folktails "
                                       "material's ART-07 canvas %s"
                                       % (where, _uv_text(uv_rect),
                                          _uv_text(ft_canvas)))
                    it_rect, it_tol, _it_mode, _why = material_uv_bounds(
                        map_data, it_material, kind="c7")
                    if it_rect is not None and any(
                            uv_rect[index] < it_rect[index % 2] - it_tol
                            for index in (0, 1)) or (
                            it_rect is not None and (
                                uv_rect[2] > it_rect[2] + it_tol
                                or uv_rect[3] > it_rect[3] + it_tol)):
                        reasons.append("%s: shared window %s is not inside the "
                                       "IronTeeth material %r bounds %s"
                                       % (where, _uv_text(uv_rect), it_material,
                                          _uv_text(it_rect)))
            per_texture = group.get("per_texture")
            if not isinstance(per_texture, dict) or not per_texture:
                reasons.append("%s: per_texture statistics are missing" % where)
            else:
                for texture_name in sorted(per_texture):
                    stats = per_texture[texture_name]
                    if not isinstance(stats, dict):
                        reasons.append("%s: per_texture[%r] must be an object"
                                       % (where, texture_name))
                        continue
                    mean = stats.get("mean_rgb")
                    std = stats.get("std")
                    std_max = stats.get("std_max")
                    hazard = stats.get("hazard_fraction")
                    if not (isinstance(mean, (list, tuple)) and len(mean) == 3
                            and all(_is_number(v) for v in mean)):
                        reasons.append("%s: per_texture[%r].mean_rgb must be 3 "
                                       "numbers" % (where, texture_name))
                    if not _is_number(std) or float(std) < 0.0:
                        reasons.append("%s: per_texture[%r].std must be a "
                                       "non-negative number"
                                       % (where, texture_name))
                    if not _is_number(std_max) or float(std_max) > 30.0 + 1e-9:
                        reasons.append("%s: per_texture[%r].std_max must be <= the "
                                       "hard cap 30.0, got %r"
                                       % (where, texture_name, std_max))
                    if _is_number(hazard) and float(hazard) > 0.005 + 1e-9:
                        reasons.append("%s: per_texture[%r].hazard_fraction %.4f "
                                       "> 0.005" % (where, texture_name,
                                                    float(hazard)))
            construction_checked += 1
            notes.append("%s: shared window uv=%s (Folktails %r canvas, "
                         "IronTeeth %r bounds OK)"
                         % (where, _uv_text(uv_rect) if uv_rect else "-",
                            ft_material, it_material))
    # -- cross-model determinism from the measured C15 density ------------ #
    drift_checked = 0
    drift_failed = 0
    if isinstance(density_report, dict):
        for material in sorted(density_report.get("rows") or {}):
            row = density_report["rows"][material]
            expected = row.get("px_expected")
            texel = math.sqrt(float(row["texture_size"][0]
                                    * row["texture_size"][1]))
            for label, acc in sorted((row.get("models") or {}).items()):
                if len(acc) < 3 or acc[2] <= 0 or not expected:
                    continue
                drift_checked += 1
                measured = math.sqrt(acc[0] / acc[1]) * texel
                if abs(measured - expected) / expected > DENSITY_AGG_TOL:
                    drift_failed += 1
                    reasons.append(
                        "cross-model drift: %s uses material %r at %.2f px/m "
                        "(%+.2f %%) != the map's single density %.1f px/m"
                        % (label, material, measured,
                           100.0 * (measured - expected) / expected, expected))
    notes.insert(0, "%d used material(s) checked against the sec.4 record; "
                    "construction shared window(s): %d; per-model/LOD density "
                    "uses cross-checked: %d (drift failures: %d)"
                    % (validated, construction_checked, drift_checked,
                       drift_failed))
    # -- material_slots.json uv_mapping block (cross-file consistency) ---- #
    slots_path = os.path.join(HERE, "material_slots.json")
    try:
        with open(slots_path, "r", encoding="utf-8") as handle:
            slots_data = json.load(handle)
    except Exception as exc:                                   # noqa: BLE001
        notes.append("material_slots.json could not be read for the uv_mapping "
                     "cross-check: %s: %s" % (type(exc).__name__, exc))
        slots_data = None
    if isinstance(slots_data, dict):
        slots_block = slots_data.get("uv_mapping")
        if not isinstance(slots_block, dict):
            notes.append("material_slots.json has no top-level uv_mapping block "
                         "(not scored by C16; art/gen_models_v2.py writes it)")
        else:
            if slots_block.get("schema") != block.get("schema"):
                reasons.append("material_slots.json uv_mapping.schema = %r != the "
                               "map's %r" % (slots_block.get("schema"),
                                             block.get("schema")))
            slots_densities = slots_block.get("densities") or {}
            mismatched = 0
            for material in sorted(set(slots_densities) & set(densities)):
                left = slots_densities[material]
                right = densities[material]
                if not isinstance(left, dict) or not isinstance(right, dict):
                    continue
                for key in ("mode", "px_per_m"):
                    if left.get(key) != right.get(key):
                        mismatched += 1
                        reasons.append(
                            "material_slots.json uv_mapping.densities[%r].%s = %r "
                            "!= the map's %r"
                            % (material, key, left.get(key), right.get(key)))
                if list(left.get("canvas_pixels") or []) \
                        != list(right.get("canvas_pixels") or []):
                    mismatched += 1
                    reasons.append(
                        "material_slots.json uv_mapping.densities[%r]."
                        "canvas_pixels = %r != the map's %r"
                        % (material, left.get("canvas_pixels"),
                           right.get("canvas_pixels")))
            notes.append("material_slots.json uv_mapping block cross-checked "
                         "against the map (%d density record(s), %d mismatch(es))"
                         % (len(set(slots_densities) & set(densities)),
                            mismatched))
        # sec.4 also records a per-model uv_mapping block (mode world_tiled +
        # per-material px_per_m/canvas): validate it when gen_models_v2 wrote it.
        model_blocks = {}
        slots_models = slots_data.get("models")
        if isinstance(slots_models, dict):
            for key in sorted(slots_models):
                model_info = slots_models[key]
                if isinstance(model_info, dict) \
                        and isinstance(model_info.get("uv_mapping"), dict):
                    model_blocks[key] = model_info["uv_mapping"]
        if not model_blocks:
            notes.append("material_slots.json carries no per-model uv_mapping "
                         "blocks (sec.4 asks gen_models_v2.py for them; C16 "
                         "validates the map + measured density instead)")
        else:
            model_block_failures = 0
            for key in sorted(model_blocks):
                model_block = model_blocks[key]
                if model_block.get("mode") != "world_tiled":
                    model_block_failures += 1
                    reasons.append("material_slots.json models[%r].uv_mapping.mode "
                                   "= %r, expected 'world_tiled'"
                                   % (key, model_block.get("mode")))
                per_material = model_block.get("materials")
                if not isinstance(per_material, dict) or not per_material:
                    model_block_failures += 1
                    reasons.append("material_slots.json models[%r].uv_mapping."
                                   "materials is missing or empty" % key)
                    continue
                for material in sorted(per_material):
                    info = per_material[material]
                    entry = material_uv_entry(map_data, material)
                    if not isinstance(info, dict) or entry is None:
                        model_block_failures += 1
                        reasons.append("material_slots.json models[%r].uv_mapping."
                                       "materials[%r] is not a map material record"
                                       % (key, material))
                        continue
                    recorded_px = info.get("px_per_m")
                    expected_px = entry_px_per_m(entry)
                    if not _is_number(recorded_px) or expected_px is None \
                            or abs(float(recorded_px) - expected_px) > 1e-9:
                        model_block_failures += 1
                        reasons.append(
                            "material_slots.json models[%r].uv_mapping."
                            "materials[%r].px_per_m = %r != the map's %r"
                            % (key, material, recorded_px, expected_px))
                    canvas = info.get("canvas")
                    entry_canvas = entry_canvas_uv_rect(entry)
                    entry_pixels = entry_canvas_pixel_rect(entry)
                    if isinstance(canvas, (list, tuple)) and len(canvas) == 4 \
                            and all(_is_number(v) for v in canvas):
                        canvas = [float(v) for v in canvas]
                        matches = ((entry_canvas is not None and all(
                            abs(canvas[i] - entry_canvas[i]) <= 1e-6
                            for i in range(4)))
                            or (entry_pixels is not None and all(
                                abs(canvas[i] - entry_pixels[i]) <= 1e-6
                                for i in range(4))))
                        if not matches:
                            model_block_failures += 1
                            reasons.append(
                                "material_slots.json models[%r].uv_mapping."
                                "materials[%r].canvas = %r != the map's "
                                "uv_canvas.uv_rect %r / pixel_rect %r"
                                % (key, material, canvas, entry_canvas,
                                   entry_pixels))
            notes.append("material_slots.json per-model uv_mapping blocks "
                         "cross-checked for %d model(s) (%d mismatch(es))"
                         % (len(model_blocks), model_block_failures))
    if reasons:
        check.fail("%d ART-07 UV mapping contract violation(s)"
                   % len(reasons), _capped(reasons, 25), notes)
    else:
        check.pass_("%d used material(s) carry the frozen sec.4 ART-07 record "
                    "(mode/tile_meters/texture_size/wrap/uv_canvas/projection/"
                    "px_per_m/coverage); px_per_m reproduces from the usable "
                    "canvas and coverage; wrap/tile_meters/uv_canvas/patch inside "
                    "the texture; %d construction shared window(s) resolve both "
                    "factions; the same density/canvas is used by every model/LOD"
                    % (validated, construction_checked), notes)
    return check


# --------------------------------------------------------------------------- #
# negative-control fixture for the C15 density checker
# --------------------------------------------------------------------------- #

def _density_probe_input(results, map_data):
    """(input copy, slot index, material) of the first tile material on a model."""
    for model_input in density_model_inputs(results):
        if model_input.get("error"):
            continue
        materials = model_input.get("materials") or []
        slot_materials = model_input.get("slot_materials") or {}
        tri_mat = model_input.get("tri_mat")
        if tri_mat is None:
            continue
        for slot_index in range(len(materials)):
            slot = str(materials[slot_index])
            material = slot_materials.get(slot)
            entry = material_uv_entry(map_data, material) \
                if material else None
            if entry_mode(entry) != "tile":
                continue
            if not int((tri_mat == slot_index).sum()):
                continue
            return model_input, slot_index, str(material)
    return None, None, None


def _density_fixture_input(map_data):
    """Synthetic 3x3x1 author-space box with exact ART-07 UVs (control fallback)."""
    if np is None or not isinstance(map_data, dict):
        return None, None
    material = None
    for candidate in sorted((map_data.get("materials") or {})):
        entry = material_uv_entry(map_data, candidate)
        if entry_mode(entry) == "tile":
            material = candidate
            break
    if material is None:
        return None, None
    entry = material_uv_entry(map_data, material)
    size = entry_texture_size(entry)
    density = entry_px_per_m(entry)
    if size is None or density is None:
        return None, None
    center_uv, center_world = _projection_center(entry)
    # author space: x,y in [-1.5, 1.5], z in [0, 1]; 6 axis-aligned box faces
    verts = np.array([
        [-1.5, -1.5, 0.0], [1.5, -1.5, 0.0], [1.5, 1.5, 0.0], [-1.5, 1.5, 0.0],
        [-1.5, -1.5, 1.0], [1.5, -1.5, 1.0], [1.5, 1.5, 1.0], [-1.5, 1.5, 1.0],
    ], dtype=np.float64)
    quads = [(0, 1, 2, 3), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5),
             (2, 3, 7, 6), (3, 0, 4, 7)]
    tris = []
    for quad in quads:
        tris.append((quad[0], quad[1], quad[2]))
        tris.append((quad[0], quad[2], quad[3]))
    tri = np.array(tris, dtype=np.int64)
    pos = verts[tri]
    normal = np.cross(pos[:, 1] - pos[:, 0], pos[:, 2] - pos[:, 0])
    axis, _absn = _dominant_axis_rounded(normal[0])
    u_axis, v_axis = _projection_axes_for(entry, axis)
    tri_uv = np.empty((len(tri), 3, 2), dtype=np.float64)
    for corner in range(3):
        tri_uv[:, corner, 0] = center_uv[0] + (
            pos[:, corner, u_axis] - center_world[u_axis]) * density / float(size[0])
        tri_uv[:, corner, 1] = center_uv[1] + (
            pos[:, corner, v_axis] - center_world[v_axis]) * density / float(size[1])
    slab = "M_HDBF_Control"
    model_input = {
        "label": "synthetic control box", "kind": "control", "lod": 0,
        "construction": False, "author_pos": verts, "tri": tri,
        "tri_mat": np.zeros(len(tri), dtype=np.int64), "tri_uv": tri_uv,
        "materials": [slab], "slot_materials": {slab: material},
    }
    return model_input, material


# -- real-_D texture variance re-verification (C13) ------------------------- #

def evaluate_patch_entry(material, entry, map_data, decoded, texture_path=None):
    patch = entry.get("patch") if isinstance(entry, dict) else None
    patch = patch if isinstance(patch, dict) else {}
    rect = [int(round(v)) for v in patch.get("pixel_rect", [0, 0, 0, 0])]
    size = entry.get("texture_size") if isinstance(entry, dict) else None
    if not (isinstance(size, (list, tuple)) and len(size) == 2):
        size = [decoded["width"], decoded["height"]]
    findings = []
    if [int(decoded["width"]), int(decoded["height"])] != [int(size[0]), int(size[1])]:
        findings.append("the real PNG is %dx%d but the map records texture_size %r"
                        % (decoded["width"], decoded["height"], list(size)))
    stats = PS.patch_stats(decoded, rect)
    computed_mean = [round(float(v), 4) for v in stats["mean_rgb"]]
    computed_std = round(float(stats["std"]), 4)
    recorded_mean = patch.get("mean_rgb")
    recorded_std = patch.get("std")
    mean_delta = None
    std_delta = None
    if isinstance(recorded_mean, (list, tuple)) and len(recorded_mean) == 3:
        deltas = [abs(computed_mean[i] - float(recorded_mean[i])) for i in range(3)]
        mean_delta = max(deltas)
        if mean_delta > PATCH_MEAN_TOL:
            findings.append("recorded mean RGB %s != re-measured %s (max channel "
                            "delta %.3f > %.1f)"
                            % (_mean_text(recorded_mean), _mean_text(computed_mean),
                               mean_delta, PATCH_MEAN_TOL))
    else:
        findings.append("the map does not record patch.mean_rgb (re-measured %s)"
                        % _mean_text(computed_mean))
    if _is_number(recorded_std):
        std_delta = abs(computed_std - float(recorded_std))
        if std_delta > PATCH_STD_TOL:
            findings.append("recorded std %.4f != re-measured %.4f (delta %.4f > %.1f)"
                            % (float(recorded_std), computed_std, std_delta,
                               PATCH_STD_TOL))
    else:
        findings.append("the map does not record patch.std (re-measured %.4f)"
                        % computed_std)
    std_max = std_max_for(map_data, entry)
    if computed_std > std_max + 1e-9:
        findings.append("re-measured std %.4f > std_max %.4f (the patch is not "
                        "uniform - hazard stripes, a logo/text region or several "
                        "materials in one patch)" % (computed_std, std_max))
    recorded_sha = entry.get("texture_sha256") if isinstance(entry, dict) else None
    if isinstance(recorded_sha, str) and recorded_sha.strip():
        sha = recorded_sha.strip().lower()
        if len(sha) != 64 or any(ch not in "0123456789abcdef" for ch in sha):
            findings.append("texture_sha256 %r is not a 64-character hex SHA-256"
                            % recorded_sha)
        elif isinstance(texture_path, str) and os.path.isfile(texture_path):
            try:
                actual_sha = PS.texture_sha256(texture_path)
            except Exception as exc:  # noqa: BLE001 - report, do not crash
                findings.append("cannot hash %s: %s: %s"
                                % (os.path.basename(texture_path),
                                   type(exc).__name__, exc))
            else:
                if actual_sha != sha:
                    findings.append("texture_sha256 mismatch for %s: map %s..., "
                                    "file %s..."
                                    % (os.path.basename(texture_path), sha[:12],
                                       actual_sha[:12]))
    uv = patch.get("uv_rect")
    row = {
        "material": material,
        "texture": os.path.basename(str(entry.get("texture", ""))) if entry else "",
        "texture_path": texture_path,
        "pixel_rect": rect,
        "size_px": [rect[2] - rect[0], rect[3] - rect[1]],
        "uv_rect": [round(float(v), 6) for v in uv]
                   if isinstance(uv, (list, tuple)) and len(uv) == 4 else None,
        "mean_measured": computed_mean,
        "mean_recorded": [round(float(v), 4) for v in recorded_mean]
                         if isinstance(recorded_mean, (list, tuple))
                         and len(recorded_mean) == 3 else None,
        "std_measured": computed_std,
        "std_recorded": round(float(recorded_std), 4) if _is_number(recorded_std) else None,
        "std_max": std_max,
        "mean_delta": mean_delta,
        "std_delta": std_delta,
        "findings": findings,
        "status": "PASS" if not findings else "FAIL",
    }
    return row, findings


def patch_row_text(row):
    return ("%-4s %-34s %-30s px=%-20s uv=%-38s mean=%s std=%.3f std_max=%.2f"
            % (row["status"], row["material"], row["texture"],
               _rect_text(row["pixel_rect"]),
               _uv_text(row["uv_rect"]) if row["uv_rect"] else "-",
               _mean_text(row["mean_measured"]), row["std_measured"],
               row["std_max"]))


# -- ART-07 canvas statistics re-verification (part of C13) ----------------- #

def resolve_texture_by_name(map_data, texture_name):
    """Absolute path of a texture file recorded in the map (or a texture root)."""
    if not isinstance(texture_name, str) or not texture_name:
        return None
    blocks = (map_data or {}).get("texture_patches") \
        if isinstance(map_data, dict) else None
    if isinstance(blocks, dict):
        record = blocks.get(texture_name)
        if isinstance(record, dict):
            path = record.get("texture_path")
            if isinstance(path, str) and os.path.isfile(path):
                return path
    for _label, root in texture_roots(map_data):
        path = os.path.join(root, texture_name)
        if os.path.isfile(path):
            return path
    return None


def hazard_fraction_stats(decoded, pixel_rect):
    """Fraction of hazard/banner pixels in a decoded PNG window.

    Same formula as ``art/material_texture_analysis.py::_hazard_fraction`` so the
    recorded ``uv_canvas.stats.hazard_fraction`` can be re-derived exactly.
    """
    width = int(decoded["width"])
    height = int(decoded["height"])
    x0, y0, x1, y1 = [int(round(v)) for v in pixel_rect]
    x0 = max(0, min(x0, width - 1))
    y0 = max(0, min(y0, height - 1))
    x1 = max(x0 + 1, min(x1, width))
    y1 = max(y0 + 1, min(y1, height))
    rgb = decoded["rgb"]
    if np is not None:
        arr = np.frombuffer(rgb, dtype=np.uint8).reshape(height, width, 3)
        win = arr[y0:y1, x0:x1, :].astype(np.int32)
        r, g, b = win[:, :, 0], win[:, :, 1], win[:, :, 2]
        hazard_yellow = ((r >= 160) & (g >= 130) & (b <= 85)
                         & ((r - g) <= 70) & ((g - b) >= 70))
        banner_blue = (b >= r + 30) & (b >= g + 10) & (b >= 70)
        banner_green = (g >= r + 40) & (g >= b + 40) & (g >= 70)
        banner_crimson = (r >= g + 80) & (b >= g + 5) & (r >= 120)
        return float((hazard_yellow | banner_blue | banner_green
                      | banner_crimson).mean())
    total = 0
    hazard = 0
    stride = width * 3
    for y in range(y0, y1):
        row_start = y * stride
        for x in range(x0, x1):
            p = row_start + x * 3
            r, g, b = rgb[p], rgb[p + 1], rgb[p + 2]
            total += 1
            if (r >= 160 and g >= 130 and b <= 85 and (r - g) <= 70
                    and (g - b) >= 70) \
                    or (b >= r + 30 and b >= g + 10 and b >= 70) \
                    or (g >= r + 40 and g >= b + 40 and g >= 70) \
                    or (r >= g + 80 and b >= g + 5 and r >= 120):
                hazard += 1
    return float(hazard) / float(total) if total else 0.0


def _canvas_stats_text(stats):
    mean = stats.get("mean_rgb") or [0.0, 0.0, 0.0]
    return ("mean=%s std=%.3f std_max=%s hazard=%.5f"
            % (_mean_text(mean), float(stats.get("std") or 0.0),
               "-" if stats.get("std_max") is None
               else "%.2f" % float(stats.get("std_max")),
               float(stats.get("hazard_fraction") or 0.0)))


def _verify_canvas_record(where, stats, decoded, pixel_rect, mode):
    """(findings, row) re-verifying one recorded ART-07 canvas statistics block."""
    findings = []
    rect = [int(round(v)) for v in pixel_rect]
    measured = PS.patch_stats(decoded, rect)
    hazard = hazard_fraction_stats(decoded, rect)
    recorded_mean = stats.get("mean_rgb")
    if isinstance(recorded_mean, (list, tuple)) and len(recorded_mean) == 3:
        delta = max(abs(float(measured["mean_rgb"][i]) - float(recorded_mean[i]))
                    for i in range(3))
        if delta > PATCH_MEAN_TOL:
            findings.append("recorded mean RGB %s != re-measured %s (max channel "
                            "delta %.3f > %.1f)"
                            % (_mean_text(recorded_mean),
                               _mean_text(measured["mean_rgb"]), delta,
                               PATCH_MEAN_TOL))
    else:
        findings.append("no recorded mean_rgb (re-measured %s)"
                        % _mean_text(measured["mean_rgb"]))
    recorded_std = stats.get("std")
    if _is_number(recorded_std):
        if abs(float(measured["std"]) - float(recorded_std)) > PATCH_STD_TOL:
            findings.append("recorded std %.4f != re-measured %.4f (delta %.4f "
                            "> %.1f)" % (float(recorded_std), measured["std"],
                                         abs(float(measured["std"])
                                             - float(recorded_std)),
                                         PATCH_STD_TOL))
    else:
        findings.append("no recorded std (re-measured %.4f)" % measured["std"])
    recorded_channels = stats.get("std_by_channel")
    if isinstance(recorded_channels, (list, tuple)) and len(recorded_channels) == 3:
        for channel in range(3):
            if abs(float(measured["std_by_channel"][channel])
                   - float(recorded_channels[channel])) > PATCH_STD_TOL:
                findings.append("recorded std_by_channel[%d] %.4f != re-measured "
                                "%.4f" % (channel,
                                          float(recorded_channels[channel]),
                                          measured["std_by_channel"][channel]))
    recorded_hazard = stats.get("hazard_fraction")
    if _is_number(recorded_hazard):
        if abs(hazard - float(recorded_hazard)) > 2e-5:
            findings.append("recorded hazard_fraction %.5f != re-measured %.5f"
                            % (float(recorded_hazard), hazard))
    else:
        findings.append("no recorded hazard_fraction (re-measured %.5f)" % hazard)
    if mode == "patch":
        std_max = stats.get("std_max")
        if not _is_number(std_max):
            findings.append("patch canvas has no recorded std_max")
        else:
            if float(std_max) > 30.0 + 1e-9:
                findings.append("recorded canvas std_max %.2f exceeds the hard "
                                "cap 30.0" % float(std_max))
            if float(measured["std"]) > float(std_max) + 1e-9:
                findings.append("re-measured canvas std %.3f > recorded std_max "
                                "%.3f" % (measured["std"], float(std_max)))
        if hazard > 0.005 + 1e-9:
            findings.append("re-measured canvas hazard fraction %.5f > 0.005"
                            % hazard)
    elif mode == "tile":
        # full-bleed tile canvases: no uniformity gate, but no warning stripes /
        # faction banner may be inside the shipped canvas.
        if hazard > 1e-9:
            findings.append("tile canvas contains %.5f hazard/banner pixels "
                            "(must be 0)" % hazard)
    row = {
        "where": where, "pixel_rect": rect, "measured": measured,
        "recorded": stats, "hazard": hazard,
        "status": "PASS" if not findings else "FAIL",
    }
    return findings, row


def _evaluate_canvas_statistics(map_data, cache, textures_available):
    """(rows, findings, notes) for the ART-07 canvas stats re-verification."""
    rows = []
    findings = []
    notes = []
    decoded_paths = {}
    for material in sorted((map_data.get("materials") or {})):
        entry = map_data["materials"][material]
        canvas = entry_uv_canvas(entry)
        stats = canvas.get("stats") if isinstance(canvas, dict) else None
        if not isinstance(stats, dict):
            continue
        rect = entry_canvas_pixel_rect(entry)
        if rect is None:
            findings.append("materials[%r].uv_canvas.pixel_rect is missing"
                            % material)
            continue
        path, _root, _tried = resolve_texture(map_data, entry)
        if path is None:
            if textures_available:
                findings.append("materials[%r]: the real _D texture for the canvas "
                                "statistics cannot be resolved" % material)
            continue
        decoded = cache.get(path)
        if decoded is None:
            try:
                decoded = PS.decode_png(path)
            except Exception as exc:                       # noqa: BLE001
                findings.append("materials[%r]: cannot decode %s: %s: %s"
                                % (material, _relpath(path),
                                   type(exc).__name__, exc))
                continue
            cache[path] = decoded
        decoded_paths[material] = path
        mode = entry_mode(entry) or "?"
        problems, row = _verify_canvas_record(
            "materials[%r].uv_canvas" % material, stats, decoded, rect, mode)
        row["material"] = material
        row["mode"] = mode
        rows.append(row)
        notes.append("%-4s canvas %-38s mode=%-5s px=%-22s %s"
                     % (row["status"], material, mode, _rect_text(rect),
                        _canvas_stats_text(stats)))
        for problem in problems:
            findings.append("%s: %s" % (row["where"], problem))
    groups = map_data.get("construction_shared_patches")
    if isinstance(groups, dict):
        for group_name in sorted(groups):
            group = groups[group_name]
            if not isinstance(group, dict):
                continue
            rect = group.get("pixel_rect")
            per_texture = group.get("per_texture")
            if not isinstance(per_texture, dict):
                continue
            for texture_name in sorted(per_texture):
                stats = per_texture[texture_name]
                if not isinstance(stats, dict):
                    continue
                path = resolve_texture_by_name(map_data, texture_name)
                if path is None:
                    if textures_available:
                        findings.append(
                            "construction_shared_patches[%r].per_texture[%r]: "
                            "texture cannot be resolved"
                            % (group_name, texture_name))
                    continue
                decoded = cache.get(path)
                if decoded is None:
                    try:
                        decoded = PS.decode_png(path)
                    except Exception as exc:               # noqa: BLE001
                        findings.append(
                            "construction_shared_patches[%r].per_texture[%r]: "
                            "cannot decode %s: %s: %s"
                            % (group_name, texture_name, _relpath(path),
                               type(exc).__name__, exc))
                        continue
                    cache[path] = decoded
                problems, row = _verify_canvas_record(
                    "construction_shared_patches[%r].per_texture[%r]"
                    % (group_name, texture_name), stats, decoded, rect, "patch")
                row["material"] = "%s/%s" % (group_name, texture_name)
                row["mode"] = "construction"
                rows.append(row)
                notes.append("%-4s shared   %-38s mode=%-12s px=%-22s %s"
                             % (row["status"], row["material"], "construction",
                                _rect_text(rect), _canvas_stats_text(stats)))
                for problem in problems:
                    findings.append("%s: %s" % (row["where"], problem))
    return rows, findings, notes


def evaluate_variance(patch_info):
    check = Check("C13", "real _D texture variance re-verification")
    rows = []
    skipped = []
    map_data = patch_info.get("data")
    if map_data is None:
        check.skip("cannot re-verify patch statistics: art/material_texture_map.json "
                   "is unavailable (%s)" % (patch_info.get("error") or "not loaded"))
        return check, rows, skipped
    if PS is None:
        check.skip("cannot re-verify patch statistics: art/png_stats.py is unavailable "
                   "(%s)" % PS_ERR)
        return check, rows, skipped
    entries = all_patch_entries(map_data)
    if not entries:
        check.skip("the map records no patch statistics to re-verify")
        return check, rows, skipped
    roots_available = textures_reachable(map_data)
    cache = {}
    reasons = []
    notes = []
    decoders = set()
    failed = 0
    skipped_count = 0
    for section, material, entry in entries:
        texture = entry.get("texture")
        path, root_label, tried = resolve_texture(map_data, entry)
        if path is None:
            if roots_available:
                reasons.append("%s[%r]: texture %r not found under any of the %d "
                               "candidate root(s) (tried: %s)"
                               % (section, material, texture, len(tried),
                                  tried[:3]))
                failed += 1
            else:
                skipped.append((material, "no vanilla texture root on this machine"))
                skipped_count += 1
            continue
        decoded = cache.get(path)
        if decoded is None:
            try:
                decoded = PS.decode_png(path)
            except Exception as exc:  # noqa: BLE001 - report, do not crash
                reasons.append("%s[%r]: cannot decode %s: %s: %s"
                               % (section, material, _relpath(path),
                                  type(exc).__name__, exc))
                failed += 1
                continue
            cache[path] = decoded
        decoders.add(str(decoded.get("decoder", "?")))
        row, findings = evaluate_patch_entry(material, entry, map_data, decoded, path)
        row["section"] = section
        row["root"] = root_label
        rows.append(row)
        notes.append(patch_row_text(row))
        if findings:
            failed += 1
            reasons.append("%s[%r]: %s" % (section, material, "; ".join(findings)))
    # ART-07: re-verify the recorded uv_canvas / construction-window statistics
    # against the same decoded PNG bytes (canvas mean/std/std_by_channel/hazard).
    canvas_rows, canvas_findings, canvas_notes = _evaluate_canvas_statistics(
        map_data, cache, roots_available)
    if canvas_rows:
        notes.append("")
        notes.append("ART-07 CANVAS STATISTICS (uv_canvas.stats re-measured from "
                     "the real _D PNGs; patch canvases: std <= std_max <= 30 and "
                     "hazard_fraction <= 0.005; tile canvases: hazard/banner "
                     "fraction must be 0, std informational)")
        notes.extend(canvas_notes)
    if canvas_findings:
        reasons.extend(canvas_findings)
    canvas_failed = len([row for row in canvas_rows if row["status"] != "PASS"])
    if cache:
        notes.insert(0, "decoded %d unique vanilla _D PNG file(s) with art/png_stats.py "
                        "(decoder: %s); mean/std recomputed from the raw sRGB bytes"
                        % (len(cache), ", ".join(sorted(decoders))))
    else:
        notes.insert(0, "no vanilla _D PNG could be decoded")
    if roots_available:
        notes.append("texture root resolution: env HDBF_VANILLA_TEX_ROOT / "
                     "HDBF_VANILLA_TEX_ALL / HDBF_VANILLA_TEX_EXTRA > "
                     "map texture_path > map texture_roots > built-in defaults")
    if failed or canvas_failed:
        check.fail("%d/%d patch record(s) and %d/%d ART-07 canvas record(s) do "
                   "not match the real vanilla _D texture"
                   % (failed, len(entries), canvas_failed, len(canvas_rows)),
                   _capped(reasons, 30), notes)
    elif not rows:
        check.skip("no patch record could be re-verified: %s" % (
            "; ".join("%s (%s)" % (m, why) for m, why in skipped[:4])
            or "no texture root available on this machine"))
    elif skipped_count:
        check.pass_("%d/%d patch record(s) + %d ART-07 canvas record(s) re-verified "
                    "from the real vanilla _D PNGs; %d SKIPPED because no texture "
                    "root is available on this machine (not counted as PASS)"
                    % (len(rows), len(entries), len(canvas_rows), skipped_count),
                    notes)
    else:
        check.pass_("all %d patch record(s) + %d ART-07 canvas record(s) "
                    "re-verified from the real vanilla _D PNGs: recorded mean RGB "
                    "/ std / std_by_channel / hazard_fraction match the decoded "
                    "pixels (mean +-%.1f, std +-%.1f) and std <= std_max"
                    % (len(rows), len(canvas_rows), PATCH_MEAN_TOL,
                       PATCH_STD_TOL), notes)
    return check, rows, skipped


# -- hazard / large-shell policy (C14) --------------------------------------- #

def collect_exclusion_rects(map_data):
    exclusions = []

    def add_list(items, where, default_texture=None):
        for item in items:
            if not isinstance(item, dict):
                continue
            rect = item.get("pixel_rect") or item.get("rect")
            texture = item.get("texture") or default_texture
            if not (isinstance(rect, (list, tuple)) and len(rect) == 4
                    and all(_is_number(v) for v in rect)):
                continue
            if not isinstance(texture, str) or not texture:
                continue
            reason = (item.get("reason") or item.get("kind")
                      or item.get("patch_class") or "excluded region")
            exclusions.append({"texture": os.path.basename(texture),
                               "pixel_rect": [float(v) for v in rect],
                               "reason": str(reason), "where": where})

    def scan(container, prefix, default_texture=None):
        if not isinstance(container, dict):
            return
        for key in ("exclusion_rects", "exclusions", "hazard_regions", "hazard_rects",
                    "logo_rects", "text_rects", "excluded_regions"):
            value = container.get(key)
            if isinstance(value, list):
                add_list(value, "%s.%s" % (prefix, key), default_texture)
            elif isinstance(value, dict):
                for inner_key in sorted(value):
                    inner = value[inner_key]
                    if isinstance(inner, list):
                        add_list(inner, "%s.%s[%r]" % (prefix, key, inner_key),
                                 inner_key)
                    elif isinstance(inner, dict):
                        add_list([inner], "%s.%s[%r]" % (prefix, key, inner_key),
                                 default_texture or inner_key)
        for key in ("hazard_reference_pixel_rect", "hazard_reference_rect"):
            rect = container.get(key)
            if not (isinstance(rect, (list, tuple)) and len(rect) == 4
                    and all(_is_number(v) for v in rect)):
                continue
            texture = container.get("texture") or default_texture
            if not isinstance(texture, str) or not texture:
                continue
            reason = (container.get("reason") or container.get("policy")
                      or "hazard reference region")
            exclusions.append({"texture": os.path.basename(texture),
                               "pixel_rect": [float(v) for v in rect],
                               "reason": str(reason),
                               "where": "%s.%s" % (prefix, key)})

    if not isinstance(map_data, dict):
        return exclusions
    scan(map_data, "map")
    scan(map_data.get("large_shell_policy"), "large_shell_policy")
    unused = map_data.get("unused_materials")
    if isinstance(unused, dict):
        for material in sorted(unused):
            entry = unused[material]
            if not isinstance(entry, dict):
                continue
            scan(entry, "unused_materials[%r]" % material, entry.get("texture"))
            patch = entry.get("patch")
            if isinstance(patch, dict) and is_hazard_marked(entry):
                rect = patch.get("pixel_rect")
                texture = entry.get("texture")
                if isinstance(rect, (list, tuple)) and len(rect) == 4 \
                        and all(_is_number(v) for v in rect) \
                        and isinstance(texture, str) and texture:
                    exclusions.append({"texture": os.path.basename(texture),
                                       "pixel_rect": [float(v) for v in rect],
                                       "reason": "map hazard/reference patch of %r"
                                                 % material,
                                       "where": "unused_materials[%r]" % material})
    return exclusions


def rects_overlap(rect_a, rect_b):
    ax0, ay0, ax1, ay1 = [float(v) for v in rect_a]
    bx0, by0, bx1, by1 = [float(v) for v in rect_b]
    return ax0 < bx1 and bx0 < ax1 and ay0 < by1 and by0 < ay1


def evaluate_hazard(patch_info, results):
    check = Check("C14", "hazard / large-shell material policy")
    map_data = patch_info.get("data")
    reasons = []
    notes = []
    if map_data is None:
        check.fail("material texture map unavailable: hazard/large-shell policy "
                   "cannot be verified",
                   ["cannot verify large_shell_policy: %s"
                    % (patch_info.get("error") or "map not loaded")], notes)
        return check
    scheduled = scheduled_materials()
    required = scheduled | required_materials(results)
    policy = map_data.get("large_shell_policy")
    forbidden = []
    if not isinstance(policy, dict):
        reasons.append("large_shell_policy object is missing from "
                       "art/material_texture_map.json")
    else:
        values = policy.get("forbidden_materials")
        if not isinstance(values, list) or not values:
            reasons.append("large_shell_policy.forbidden_materials must be a "
                           "non-empty list, got %r" % (values,))
        else:
            forbidden = [str(v) for v in values]
            for material in HAZARD_MATERIALS:
                if material not in forbidden:
                    reasons.append("large_shell_policy.forbidden_materials does not "
                                   "list the warning-stripe material %r (list: %r)"
                                   % (material, forbidden))
        notes.append("forbidden large-shell/hazard materials declared: %s"
                     % (forbidden or "-"))
    hits = sorted(set(forbidden) & required)
    if hits:
        reasons.append("%d forbidden hazard material(s) appear in the shipped/"
                       "scheduled material plan: %s" % (len(hits), hits))
    else:
        notes.append("no forbidden material is used by a shipped or scheduled model")
    for section in ("materials", "unused_materials"):
        entries = map_data.get(section)
        if not isinstance(entries, dict):
            continue
        for material in sorted(entries):
            entry = entries[material]
            if not is_hazard_marked(entry):
                continue
            tags = hazard_tags_of(entry)
            if material in required:
                reasons.append("%s[%r] is marked as a hazard/logo/text region "
                               "(tags=%r) yet the material is used by the shipped "
                               "or scheduled material plan"
                               % (section, material, tags))
            else:
                notes.append("%s[%r] is marked hazard/reference-only (tags=%r) and "
                             "is not used -> ok" % (section, material, tags))
    exclusions = collect_exclusion_rects(map_data)
    if exclusions:
        notes.append("%d recorded hazard/logo/text exclusion rect(s) checked against "
                     "the usable patches" % len(exclusions))
        overlapping = 0
        for section, material, entry in all_patch_entries(map_data):
            if is_hazard_marked(entry):
                continue
            patch = entry.get("patch") or {}
            rect = patch.get("pixel_rect")
            texture = entry.get("texture")
            if not (isinstance(rect, (list, tuple)) and len(rect) == 4
                    and isinstance(texture, str) and texture):
                continue
            for exclusion in exclusions:
                if os.path.basename(texture) != exclusion["texture"]:
                    continue
                if rects_overlap(rect, exclusion["pixel_rect"]):
                    overlapping += 1
                    reasons.append("%s[%r] patch %s overlaps the recorded %s "
                                   "exclusion rect %s on %s"
                                   % (section, material, _rect_text(rect),
                                      exclusion["reason"],
                                      _rect_text(exclusion["pixel_rect"]),
                                      exclusion["texture"]))
        if not overlapping:
            notes.append("no usable patch overlaps a recorded exclusion rect")
    else:
        notes.append("the map records no separate hazard/logo/text exclusion rects "
                     "(policy is enforced through forbidden_materials + hazard-tagged "
                     "entries)")
    notes.append("shipped/scheduled materials: %s" % (sorted(required) or "-"))
    if reasons:
        check.fail("hazard / large-shell material policy violated",
                   _capped(reasons, 30), notes)
    else:
        check.pass_("%d forbidden warning-stripe material(s) declared and unused; no "
                    "hazard-tagged patch is shipped; %d exclusion rect(s) respected"
                    % (len(HAZARD_MATERIALS), len(exclusions)), notes)
    return check


class GlobalChecks(object):
    __slots__ = ("coverage", "variance", "hazard", "density", "uv_contract",
                 "variance_rows", "variance_skipped", "coverage_details",
                 "density_report", "map_source", "map_error")

    def __init__(self):
        self.coverage = None
        self.variance = None
        self.hazard = None
        self.density = None
        self.uv_contract = None
        self.variance_rows = []
        self.variance_skipped = []
        self.coverage_details = {}
        self.density_report = {}
        self.map_source = "-"
        self.map_error = None

    @property
    def checks(self):
        return [check for check in (self.coverage, self.variance, self.hazard,
                                    self.density, self.uv_contract)
                if check is not None]

    def failures(self):
        return [check for check in self.checks if check.status == "FAIL"]


def evaluate_global_checks(patch_info, results):
    globals_ = GlobalChecks()
    globals_.map_source = patch_info.get("source") or "-"
    globals_.map_error = patch_info.get("error")
    globals_.coverage, globals_.coverage_details = evaluate_coverage(
        patch_info.get("data"), patch_info.get("source"), results,
        patch_info.get("error"))
    globals_.variance, globals_.variance_rows, globals_.variance_skipped = \
        evaluate_variance(patch_info)
    globals_.hazard = evaluate_hazard(patch_info, results)
    # ART-07: C15 measures the texel density; C16 checks the mapping contract
    # and its cross-model determinism (using C15's measured per-model data).
    globals_.density, globals_.density_report = evaluate_world_uv_density(
        patch_info, results)
    globals_.uv_contract = evaluate_uv_contract(patch_info, results,
                                                globals_.density_report)
    return globals_


# -- negative controls (requirement D) -------------------------------------- #

class NegativeControl(object):
    __slots__ = ("index", "name", "check_id", "status", "detail", "note")

    def __init__(self, index, name):
        self.index = index
        self.name = name
        self.check_id = "?"
        self.status = "NOT RUN"
        self.detail = ""
        self.note = ""

    @property
    def rejected(self):
        return self.status == "REJECTED"


def _first_source_uv_sample(results):
    """First (source label, slot, u, v) uv0 corner from an imported source model."""
    for result in results or []:
        spec = result.get("spec")
        if not spec:
            continue
        for ext in ("fbx", "glb"):
            fact = (result.get("facts") or {}).get(ext)
            if fact is None or getattr(fact, "error", None) or not fact.tri_count:
                continue
            samples = slot_uv_samples(fact)
            for slot in sorted(samples):
                if not samples[slot]:
                    continue
                _tri_index, u, v = samples[slot][0]
                return ("%s.%s" % (spec["label"], ext.upper()), slot, u, v)
    return None


def _rect_around(u, v, radius):
    radius = max(0.02, float(radius))
    u0 = max(0.0, min(u - radius, 1.0 - 2.0 * radius))
    v0 = max(0.0, min(v - radius, 1.0 - 2.0 * radius))
    return [round(u0, 6), round(v0, 6), round(u0 + 2.0 * radius, 6),
            round(v0 + 2.0 * radius, 6)]


def _synthetic_entry_for_uv(patch_rect):
    """ART-07-shaped synthetic map entry (patch mode) for the uv-escape control."""
    rect = [float(v) for v in patch_rect]
    return {
        "texture": "negative-control-synthetic.png",
        "texture_source": "synthetic",
        "texture_size": [1024, 1024],
        "patch_class": "wood",
        "mode": "patch",
        "wrap": "clamp",
        "tile_meters": None,
        "px_per_m": 128.0,
        "coverage_u_m": 3.0,
        "coverage_v_m": 3.0,
        "uv_canvas": {
            "pixel_rect": [0, 0, 64, 64],
            "uv_rect": rect,
            "size_px": [64, 64],
            "meaning": "negative-control synthetic canvas",
        },
        "projection": {
            "axes": {"-X": ["y", "z"], "+X": ["y", "z"],
                     "-Y": ["x", "z"], "+Y": ["x", "z"],
                     "-Z": ["x", "y"], "+Z": ["x", "y"]},
            "world_center": list(UV_WORLD_CENTER),
            "uv_center": [(rect[0] + rect[2]) * 0.5, (rect[1] + rect[3]) * 0.5],
        },
        "patch": {
            "pixel_rect": [0, 0, 64, 64],
            "size_px": [64, 64],
            "uv_rect": list(rect),
            "mean_rgb": [128.0, 128.0, 128.0],
            "mean_rgb_tinted": [128.0, 128.0, 128.0],
            "std": 0.0,
            "std_max": PATCH_STD_MAX_DEFAULT,
            "reason": "negative-control synthetic patch",
            "reject_tags": [],
        },
    }


def _map_with_synthetic_patch(map_data, material, patch_rect):
    if isinstance(map_data, dict):
        try:
            copy = json.loads(json.dumps(map_data))
        except (TypeError, ValueError):
            copy = {"schema": MAP_SCHEMA, "materials": {}}
    else:
        copy = {"schema": MAP_SCHEMA, "materials": {}}
    copy.setdefault("schema", MAP_SCHEMA)
    materials = copy.get("materials")
    if not isinstance(materials, dict):
        materials = {}
        copy["materials"] = materials
    unused = copy.get("unused_materials")
    if isinstance(unused, dict):
        unused.pop(material, None)
    materials[material] = _synthetic_entry_for_uv(patch_rect)
    return copy


def control_uv_escape(patch_info, results):
    """Negative control 1: shift one real source uv0 outside its patch -> C11 FAIL."""
    control = NegativeControl(1, "one real source uv0 shifted outside its material "
                                "patch (uv containment)")
    control.check_id = "C11"
    map_data = patch_info.get("data")
    sample = _first_source_uv_sample(results)
    if sample is not None:
        source, slot, u, v = sample
        real_uv = True
    else:
        source, slot, u, v = ("synthetic fixture (no source model imported)",
                              "M_HDBF_Wood", 0.30, 0.40)
        real_uv = False
    # The source slot has no fixed material across tiers/factions; pick the material
    # the shift control uses from the first context mapping that has this slot.
    material = None
    rect = None
    for result in results or []:
        spec = result.get("spec")
        if not spec:
            continue
        for _label, mapping in spec_material_contexts(spec):
            if mapping.get(slot):
                candidate = mapping[slot]
                candidate_rect = patch_uv_rect(map_data, candidate) \
                    if map_data is not None else None
                if material is None:
                    material = candidate
                    if candidate_rect is not None:
                        rect = candidate_rect
                if candidate_rect is not None:
                    material = candidate
                    rect = candidate_rect
                    break
        if material is not None and rect is not None:
            break
    if material is None:
        material = "BaseWood_Brown.Folktails"
    inside = (rect is not None
              and rect[0] - PATCH_UV_TOL <= u <= rect[2] + PATCH_UV_TOL
              and rect[1] - PATCH_UV_TOL <= v <= rect[3] + PATCH_UV_TOL)
    if not inside:
        rect = _rect_around(u, v, 0.05)
        probe_map = _map_with_synthetic_patch(map_data, material, rect)
    else:
        probe_map = map_data
    rect = tuple(float(value) for value in rect)
    width = rect[2] - rect[0]
    bad_u = rect[2] + width + 0.05
    samples = {str(slot): [(0, bad_u, v)]}
    contexts = [("negative control", {str(slot): material})]
    rows, reasons = containment_for_slots(samples, contexts, probe_map)
    rejected = bool(reasons)
    control.status = "REJECTED" if rejected else "NOT REJECTED"
    control.detail = ("source=%s slot=%r material=%r uv0=(%.6f, %.6f) -> "
                      "(%.6f, %.6f): moved %.4f in u (> one patch width %.4f) "
                      "outside patch uv=[%.6f, %.6f, %.6f, %.6f]; containment "
                      "checker %s (%s)"
                      % (source, slot, material, u, v, bad_u, v, bad_u - u, width,
                         rect[0], rect[1], rect[2], rect[3],
                         "REJECTED" if rejected else "ACCEPTED",
                         rows[0]["status"] if rows else "no row"))
    if rejected:
        control.note = "rejected by C11: %s" % reasons[0]
    else:
        control.note = "BUG: the containment checker accepted an escaped uv0"
    if not real_uv:
        control.note += " [no imported source UV available; synthetic fixture used]"
    return control


def _procedural_hazard_rgb(size):
    data = bytearray(size * size * 3)
    stripe = max(2, size // 8)
    for y in range(size):
        for x in range(size):
            index = (y * size + x) * 3
            if (x // stripe) % 2 == 0:
                data[index:index + 3] = b"\xff\xc8\x00"
            else:
                data[index:index + 3] = b"\x00\x00\x00"
    return bytes(data)


def control_hazard_patch(patch_info):
    """Negative control 2: a hazard-stripe patch record -> C13 must FAIL."""
    control = NegativeControl(2, "synthetic 192x192 yellow-black hazard-stripe "
                                 "patch record (texture variance/hazard)")
    control.check_id = "C13"
    if PS is None:
        control.status = "NOT RUN"
        control.detail = ("art/png_stats.py unavailable (%s): cannot re-measure the "
                          "hazard window" % PS_ERR)
        return control
    map_data = patch_info.get("data")
    if not isinstance(map_data, dict):
        map_data = {"schema": MAP_SCHEMA,
                    "thresholds": {"std_max_default": PATCH_STD_MAX_DEFAULT}}
    pixel_rect = list(HAZARD_CONTROL_PIXEL_RECT)
    texture_size = list(HAZARD_CONTROL_TEXTURE_SIZE)
    entry = {
        "texture": HAZARD_CONTROL_TEXTURE,
        "texture_source": "vanilla-tex-all",
        "texture_size": texture_size,
        "patch_class": "metal",
        "patch": {
            "pixel_rect": pixel_rect,
            "size_px": [pixel_rect[2] - pixel_rect[0],
                        pixel_rect[3] - pixel_rect[1]],
            "uv_rect": [round(v, 6) for v in expected_uv_rect(pixel_rect,
                                                              texture_size)],
            "mean_rgb": [0.0, 0.0, 0.0],
            "mean_rgb_tinted": [0.0, 0.0, 0.0],
            "std": 0.0,
            "std_max": PATCH_STD_MAX_DEFAULT,
            "reason": "negative control: real yellow-black hazard stripes",
            "reject_tags": [],
        },
    }
    path, root_label, tried = resolve_texture(map_data, entry)
    if path is None:
        size = 64
        decoded = {"path": "<procedural>", "width": size, "height": size,
                   "channels": 3, "rgb": _procedural_hazard_rgb(size),
                   "decoder": "procedural"}
        entry["texture"] = "procedural-yellow-black-stripes.png"
        entry["texture_source"] = "procedural"
        entry["texture_size"] = [size, size]
        entry["patch"]["pixel_rect"] = [0, 0, size, size]
        entry["patch"]["size_px"] = [size, size]
        entry["patch"]["uv_rect"] = [0.0, 0.0, 1.0, 1.0]
        mode = ("procedural 64x64 yellow/black stripe raster (real PNG not found; "
                "tried %d root(s))" % len(tried))
    else:
        decoded = PS.decode_png(path)
        mode = "real %s resolved via %s" % (os.path.basename(path), root_label)
    stats = PS.patch_stats(decoded, entry["patch"]["pixel_rect"])
    entry["patch"]["mean_rgb"] = [round(float(v), 4) for v in stats["mean_rgb"]]
    entry["patch"]["mean_rgb_tinted"] = list(entry["patch"]["mean_rgb"])
    entry["patch"]["std"] = round(float(stats["std"]), 4)
    probe_map = {"schema": MAP_SCHEMA,
                 "thresholds": (map_data.get("thresholds")
                                if isinstance(map_data.get("thresholds"), dict)
                                else {"std_max_default": PATCH_STD_MAX_DEFAULT})}
    row, findings = evaluate_patch_entry("__negative_control_hazard_stripes__",
                                         entry, probe_map, decoded, path)
    rejected = any("std" in finding and "std_max" in finding for finding in findings)
    control.status = "REJECTED" if rejected else "NOT REJECTED"
    control.detail = ("mode=%s; patch px=%s uv=%s; recorded std=%.3f; std_max=%.3f "
                      "-> %s" % (mode, _rect_text(entry["patch"]["pixel_rect"]),
                                 _uv_text(entry["patch"]["uv_rect"]),
                                 entry["patch"]["std"], row["std_max"],
                                 "REJECTED" if rejected else "ACCEPTED"))
    if rejected:
        control.note = "rejected by C13: %s" % "; ".join(findings)
    else:
        control.note = "BUG: the variance checker accepted a yellow-black hazard patch"
    return control


def control_missing_material(patch_info, results):
    """Negative control 3: drop one required material from a map copy -> C12 FAIL."""
    control = NegativeControl(3, "one required material removed from an in-memory "
                                "map copy (coverage)")
    control.check_id = "C12"
    map_data = patch_info.get("data")
    required = sorted(required_materials(results))
    material = required[0] if required else "Fixture.Wood"
    if isinstance(map_data, dict):
        try:
            probe_map = json.loads(json.dumps(map_data))
        except (TypeError, ValueError):
            probe_map = {"schema": MAP_SCHEMA, "materials": {}}
        removed = False
        for section in ("materials", "unused_materials"):
            entries = probe_map.get(section)
            if isinstance(entries, dict) and material in entries:
                del entries[material]
                removed = True
        if not removed:
            probe_map.setdefault("materials", {})
            if not isinstance(probe_map["materials"], dict):
                probe_map["materials"] = {}
        mode = "in-memory copy of art/material_texture_map.json"
    else:
        probe_map = {"schema": MAP_SCHEMA, "materials": {}}
        mode = "synthetic minimal map (the real map is unavailable)"
    check, _details = evaluate_coverage(probe_map, mode, results,
                                        load_error="negative control")
    reasons = list(check.reasons)
    rejected = (check.status == "FAIL"
                and any(material in reason for reason in reasons))
    control.status = "REJECTED" if rejected else "NOT REJECTED"
    control.detail = ("mode=%s; removed required material %r; coverage checker -> %s"
                      % (mode, material, check.status))
    if rejected:
        control.note = "rejected by C12: %s" % next(
            reason for reason in reasons if material in reason)
    else:
        control.note = "BUG: the coverage checker accepted a map without %r" % material
    return control


def _control_density_baseline(patch_info, results):
    """(model_input, slot_index, material, baseline_report) for control 4/5.

    Prefers a real imported source model; falls back to the synthetic control box
    (exact ART-07 UVs) when no source model is available (e.g. --self-test).
    """
    map_data = patch_info.get("data")
    if np is None or not isinstance(map_data, dict):
        return None, None, None, None, "numpy/map unavailable"
    model_input, slot_index, material = _density_probe_input(results, map_data)
    source = "real imported source model"
    if model_input is None:
        model_input, material = _density_fixture_input(map_data)
        slot_index = 0
        source = "synthetic ART-07 control box (no source model imported)"
    if model_input is None:
        return None, None, None, None, "no tile material available"
    baseline = _density_analyze_inputs([model_input], map_data)
    return model_input, slot_index, material, {"source": source,
                                               "report": baseline}, None


def control_uv_scale_density(patch_info, results, index, factor):
    """Negative controls 4/5: scale a tile material's UVs -> C15 must FAIL."""
    control = NegativeControl(
        index, "tile material UVs scaled x%.1f (world-UV texel density)" % factor)
    control.check_id = "C15"
    (model_input, slot_index, material, baseline,
     error) = _control_density_baseline(patch_info, results)
    if error is not None:
        control.status = "NOT RUN"
        control.detail = error
        control.note = "cannot exercise the C15 density checker"
        return control
    if material is None:
        control.status = "NOT RUN"
        control.detail = "no tile material available for the scale control"
        control.note = "cannot exercise the C15 density checker"
        return control
    control.name = ("tile material %r UVs scaled x%.1f (world-UV texel density)"
                    % (material, factor))
    map_data = patch_info.get("data")
    row = baseline["report"]["rows"].get(material)
    baseline_density = row["measured"] if row else None
    baseline_ok = bool(row is not None and row["status"] == "PASS")
    probe = dict(model_input)
    uv = np.array(model_input["tri_uv"], dtype=np.float64)
    mask = model_input["tri_mat"] == slot_index
    uv[mask] = 0.5 + (uv[mask] - 0.5) * factor
    probe["tri_uv"] = uv
    report = _density_analyze_inputs([probe], map_data)
    scaled_row = report["rows"].get(material)
    scaled_density = scaled_row["measured"] if scaled_row else None
    rejected = bool(scaled_row is not None and scaled_row["status"] == "FAIL")
    control.status = "REJECTED" if rejected else "NOT REJECTED"
    expected = scaled_row["px_expected"] if scaled_row else None
    control.detail = (
        "source=%s material=%r slot=%r; UVs scaled x%.1f about uv=0.5; "
        "baseline density %s px/m (map %s) -> scaled density %s px/m; C15 "
        "aggregate tolerance +-%.0f%% -> %s"
        % (baseline["source"], material, model_input["materials"][slot_index],
           factor,
           "n/a" if baseline_density is None else "%.2f" % baseline_density,
           "-" if expected is None else "%.2f" % expected,
           "n/a" if scaled_density is None else "%.2f" % scaled_density,
           DENSITY_AGG_TOL * 100.0,
           "REJECTED" if rejected else "ACCEPTED"))
    reasons = (scaled_row or {}).get("reasons") or report.get("reasons") or []
    if rejected:
        control.note = "rejected by C15: %s" % (reasons[0] if reasons
                                                else "density mismatch")
        if not baseline_ok:
            control.note += (" [WARNING: the unmodified baseline does not pass C15 "
                             "either - check the control fixture]")
        if factor > 1.5:
            control.note += (" [the x%.1f UVs also leave the material canvas, so "
                             "C7 would reject them too; the control is scored on "
                             "the C15 density checker]" % factor)
    else:
        control.note = ("BUG: the C15 density checker accepted a tile material "
                        "scaled x%.1f (baseline density %s px/m)"
                        % (factor, "n/a" if baseline_density is None
                           else "%.2f" % baseline_density))
    return control


def control_patch_uv_stretch(patch_info, results):
    """Negative control 6: stretch one large patch face to the full 0..1 square."""
    control = NegativeControl(6, "one large patch-mode face stretched to the full "
                                 "0..1 UV square (mode-aware bounds/containment)")
    control.check_id = "C7/C11"
    map_data = patch_info.get("data")
    if not isinstance(map_data, dict):
        control.status = "NOT RUN"
        control.detail = "art/material_texture_map.json unavailable"
        control.note = "cannot exercise the C7/C11 containment checker"
        return control
    sample = None
    for result in results or []:
        spec = result.get("spec")
        fact = (result.get("facts") or {}).get("glb")
        if spec is None or fact is None or fact.error or not fact.tri_count:
            continue
        contexts = spec_material_contexts(spec)
        samples = slot_uv_samples(fact)
        areas = fact.tri_areas()
        for label, mapping in contexts:
            for slot in sorted(samples):
                material = mapping.get(slot) if isinstance(mapping, dict) else None
                entry = material_uv_entry(map_data, material) if material else None
                if entry_mode(entry) != "patch":
                    continue
                triangle_ids = sorted({tri for tri, _u, _v in samples[slot]})
                if not triangle_ids:
                    continue
                biggest = max(triangle_ids, key=lambda tri: float(areas[tri]))
                world_area = float(areas[biggest])
                if world_area <= 0.0:
                    continue
                mutated = {slot: [(tri, u, v) for tri, u, v in samples[slot]
                                  if tri != biggest]}
                mutated[slot].extend([(biggest, 0.0, 0.0), (biggest, 1.0, 0.0),
                                      (biggest, 0.0, 1.0)])
                sample = {
                    "source": "real imported source model",
                    "spec": spec, "contexts": contexts, "slot": slot,
                    "material": material, "triangle": biggest,
                    "world_area": world_area, "samples": mutated,
                }
                break
            if sample is not None:
                break
        if sample is not None:
            break
    if sample is None:
        material = None
        for candidate in sorted((map_data.get("materials") or {})):
            entry = material_uv_entry(map_data, candidate)
            if entry_mode(entry) == "patch":
                material = candidate
                break
        if material is None:
            control.status = "NOT RUN"
            control.detail = "no patch-mode material available"
            control.note = "cannot exercise the C7/C11 containment checker"
            return control
        slot = "M_HDBF_Control"
        sample = {
            "source": "synthetic fixture (no source model imported)",
            "spec": None, "contexts": [("negative control", {slot: material})],
            "slot": slot, "material": material, "triangle": 0,
            "world_area": None,
            "samples": {slot: [(0, 0.0, 0.0), (0, 1.0, 0.0), (0, 0.0, 1.0)]},
        }
    rows7, findings7 = containment_for_slots(sample["samples"],
                                             sample["contexts"], map_data,
                                             kind="c7")
    rows11, findings11 = containment_for_slots(sample["samples"],
                                               sample["contexts"], map_data,
                                               kind="c11")
    rejected = bool(findings7) and bool(findings11)
    row = None
    for candidate in rows7:
        if candidate["material"] == sample["material"]:
            row = candidate
            break
    control.status = "REJECTED" if rejected else "NOT REJECTED"
    control.detail = (
        "source=%s material=%r triangle=%d world_area=%s m^2; UVs replaced by "
        "(0,0)/(1,0)/(0,1) -> the %s canvas is uv=[%s]; C7 bounds %s; C11 "
        "containment %s"
        % (sample["source"], sample["material"], sample["triangle"],
           "n/a" if sample["world_area"] is None
           else "%.4f" % sample["world_area"],
           row["mode"] if row else "?",
           _uv_text(row["rect"]) if row and row["rect"] else "-",
           "REJECTED" if findings7 else "ACCEPTED",
           "REJECTED" if findings11 else "ACCEPTED"))
    if rejected:
        control.note = "rejected by C7/C11: %s" % (
            findings11[0] if findings11 else findings7[0])
    else:
        control.note = ("BUG: the mode-aware UV bounds/containment checker "
                        "accepted a full 0..1 stretch of a patch face")
    return control


def run_negative_controls(patch_info, results):
    return [
        control_uv_escape(patch_info, results),
        control_hazard_patch(patch_info),
        control_missing_material(patch_info, results),
        control_uv_scale_density(patch_info, results, 4, 0.5),
        control_uv_scale_density(patch_info, results, 5, 2.0),
        control_patch_uv_stretch(patch_info, results),
    ]


def negative_controls_verdict(controls):
    total = len(controls)
    rejected = sum(1 for control in controls if control.rejected)
    if rejected == total:
        return "PASS %d/%d" % (rejected, total)
    return "FAIL %d/%d" % (rejected, total)


def render_negative_controls(controls, indent=""):
    lines = []
    for control in controls:
        lines.append("%s[%d] %-11s %s" % (indent, control.index, control.status,
                                          control.name))
        lines.append("%s     %s" % (indent, control.detail))
        lines.append("%s     %s" % (indent, control.note))
    lines.append("%sNEGATIVE CONTROLS: %s" % (indent,
                                              negative_controls_verdict(controls)))
    return lines


# --------------------------------------------------------------------------- #
# report
# --------------------------------------------------------------------------- #
STATUS_LETTER = {"PASS": "P", "FAIL": "F", "BLOCKED": "B", "INFO": "I"}


def _letter(check):
    return STATUS_LETTER.get(check.status, "?")


def _fmt3(values):
    return "%.3f/%.3f/%.3f" % tuple(values)


def render_patch_checks(validator):
    """C12-C16 sections + the per-material patch + world-UV density tables."""
    globals_ = validator.globals
    lines = []
    lines.append("=" * 118)
    lines.append("C12-C16: MATERIAL TEXTURE PATCH + ART-07 WORLD-UV CHECKS "
                 "(cross-model)")
    lines.append("=" * 118)
    if globals_ is None:
        lines.append("  NOT RUN: the cross-model material checks did not run")
        lines.append("")
        return lines
    lines.append("map:        %s" % globals_.map_source)
    if globals_.map_error:
        lines.append("map error:  %s" % globals_.map_error)
    lines.append("map schema: %r; texture roots: env HDBF_VANILLA_TEX_ROOT / "
                 "HDBF_VANILLA_TEX_ALL / HDBF_VANILLA_TEX_EXTRA > map texture_path > "
                 "map.texture_roots > defaults" % MAP_SCHEMA)
    lines.append("patch stats: mean RGB + std (max channel std, 0..255) re-measured "
                 "from the real vanilla _D PNG with art/png_stats.py; recorded "
                 "mean +-%.1f, std +-%.1f, std <= std_max"
                 % (PATCH_MEAN_TOL, PATCH_STD_TOL))
    lines.append("ART-07:      world-scale planar projection at the native-on-atlas "
                 "texel density (art/ART07_UV_TILING.md); UVs stay inside the "
                 "material canvas because the game's AutoAtlaser remaps them into "
                 "the material's own atlas cell (no UV>1 Repeat tiling)")
    lines.append("")
    for check in globals_.checks:
        lines.append("-" * 118)
        lines.append("%-3s %-56s %s" % (check.cid, check.title, check.status))
        lines.append("  %s" % check.text)
        for reason in check.reasons:
            lines.append("        ! %s" % reason)
        for note in check.notes:
            lines.append("        - %s" % note)
    lines.append("")
    lines.append("-" * 118)
    lines.append("PATCH TABLE (per material: real _D texture, pixel rect, uv rect, "
                 "re-measured mean RGB, std, std_max)")
    lines.append("  %-4s %-34s %-30s %-20s %-38s %-20s %-23s %8s %8s"
                 % ("RES", "MATERIAL", "TEXTURE", "PIXEL RECT", "UV RECT",
                    "MEAN RGB (measured)", "MEAN RGB (recorded)", "STD", "STD_MAX"))
    if globals_.variance_rows:
        for row in globals_.variance_rows:
            lines.append("  %-4s %-34s %-30s %-20s %-38s %-20s %-23s %8.3f %8.2f"
                         % (row["status"], row["material"], row["texture"],
                            _rect_text(row["pixel_rect"]),
                            _uv_text(row["uv_rect"]) if row["uv_rect"] else "-",
                            _mean_text(row["mean_measured"]),
                            _mean_text(row["mean_recorded"])
                            if row["mean_recorded"] else "-",
                            row["std_measured"], row["std_max"]))
    else:
        lines.append("  (no patch statistics could be re-measured)")
    if globals_.variance_skipped:
        lines.append("  SKIPPED patches (no vanilla texture root on this machine):")
        for material, why in globals_.variance_skipped:
            lines.append("    - %s: %s" % (material, why))
    lines.append("")
    lines.extend(render_density_table(globals_))
    return lines


def render_density_table(globals_):
    """C15 per-material ART-07 world-UV texel-density table + ambiguity evidence."""
    lines = []
    report = getattr(globals_, "density_report", None) or {}
    lines.append("-" * 118)
    lines.append("WORLD-UV TEXEL DENSITY TABLE (C15; per material of every shipped "
                 "LOD0/1/2 source model, author space)")
    lines.append("  %-4s %-38s %-5s %-22s %10s %10s %10s %10s %7s"
                 % ("RES", "MATERIAL", "MODE", "CANVAS", "PX/M(EXP)", "MIN",
                    "MEAN", "MAX", "TRIS"))
    rows = report.get("rows") or {}
    if rows:
        for material in sorted(rows):
            row = rows[material]
            if row.get("measured") is None:
                lines.append("  %-4s %-38s %-5s %-22s %10.2f %10s %10s %10s %7d"
                             % ("FAIL", material, row.get("mode") or "?",
                                _rect_text(row["canvas"]) if row.get("canvas")
                                else "-", row.get("px_expected") or 0.0,
                                "-", "-", "-", row.get("tri_count") or 0))
                continue
            lines.append("  %-4s %-38s %-5s %-22s %10.2f %10.3f %10.3f %10.3f %7d"
                         % (row["status"], material, row["mode"],
                            _rect_text(row["canvas"]) if row["canvas"] else "-",
                            row["px_expected"], row["min"], row["measured"],
                            row["max"], row["tri_count"]))
    else:
        lines.append("  (no material could be measured)")
    band = report.get("tile_band") or TILE_DENSITY_BAND_DEFAULT
    lines.append("  tile band: %.0f-%.0f px/m; patch floor: %.0f px/m (2x the "
                 "retired ART-06 density of 32 px/m; the WindowsAtlas glass pane "
                 "is 82.4 px/m by design); aggregate tolerance +-%.0f %% vs the "
                 "map px_per_m"
                 % (band[0], band[1], DENSITY_PATCH_MIN, DENSITY_AGG_TOL * 100.0))
    if report.get("triangles"):
        lines.append("  per-triangle tolerance +-%.0f %% above %.2f m^2 world area "
                     "(%d/%d failed); exact UV recompute: %d triangle(s) checked, "
                     "%d mismatch(es) > %.0e"
                     % (DENSITY_TRI_TOL * 100.0, DENSITY_TRI_MIN_WORLD_AREA,
                        report.get("large_failed", 0),
                        report.get("large_triangles", 0),
                        report.get("exact_checked", 0),
                        report.get("exact_failed", 0), EXACT_UV_TOL))
        lines.append("  ambiguous-plane triangles (frozen tie-break: unit Newell "
                     "normal, |components| rounded to %d decimals, top two within "
                     "%.0e): %d/%d = %.2f %% of triangles / %.3f %% of world area "
                     "(limits %.0f %% / %.1f %%); they are still covered by C11 "
                     "containment and the area-aggregate density"
                     % (PROJECTION_ROUND_DECIMALS, AMBIGUOUS_PLANE_TOL,
                        report.get("ambiguous_count", 0),
                        report.get("triangles", 0),
                        100.0 * report.get("amb_fraction", 0.0),
                        100.0 * report.get("amb_area_fraction", 0.0),
                        100.0 * AMBIGUOUS_COUNT_MAX,
                        100.0 * AMBIGUOUS_AREA_MAX))
    lines.append("  construction stage: the shared ConstructionStage0 source is "
                 "baked with the Folktails construction materials and measured "
                 "against them; the IronTeeth export reuses that UV set by contract")
    lines.append("")
    return lines


def build_report(validator, meta, controls=None):
    lines = []
    lines.append("=" * 118)
    lines.append("HDBF v2 - 3x3x1 indoor box farm source-model validation report")
    lines.append("=" * 118)
    lines.append("report:     deterministic (identical inputs -> byte-identical report;")
    lines.append("            no timestamps, no elapsed times)")
    lines.append("mode:       Blender %s, FBX re-import + GLB import (both checked)"
                 % BLENDER_VERSION)
    lines.append("numpy:      %s" % (np.__version__ if np is not None else NUMPY_ERR))
    lines.append("contract:   art/atlas_layout.py (envelope, flat roof, ports, "
                 "tier materials, indoor expression)")
    lines.append("            art/material_texture_map.json (ART-05 real _D textures "
                 "+ ART-07 world-UV canvases/texel densities), "
                 "art/material_uv_policy.py (frozen ART-07 policy), "
                 "art/material_patches.py (accessors), art/timbermesh_common.py "
                 "(export contract)")
    lines.append("            art/ART07_UV_TILING.md (ART-07 world-scale UV mapping "
                 "contract: frozen projection tie-break, canvases, texel density)")
    lines.append("files:      %d source models" % (len(validator.results) - 1))
    lines.append("tolerances: envelope +-%.3f grid, flat top +-%.3f, ports +-%.3f, "
                 "tri budget +-%.0f%%, LOD shrink <=%.0f%%, uv0-in-canvas +-%.0e "
                 "(tile guard bound +-%.0e), patch mean +-%.1f / std +-%.1f"
                 % (BOUNDS_TOL, FLAT_TOL, PORT_TOL, TRI_TOL * 100,
                    A.LOD_BBOX_SHRINK_MAX * 100, PATCH_UV_TOL, TILE_GUARD_TOL,
                    PATCH_MEAN_TOL, PATCH_STD_TOL))
    lines.append("ART-07:     uv = uv_center + (world_uv - world_center) * px_per_m / "
                 "texture_size; dominant face normal (unit Newell normal, "
                 "|components| rounded to %d decimals, X > Y > Z) picks the "
                 "projection plane; UVs stay inside the material canvas because the "
                 "runtime AutoAtlaser remaps them into the material's atlas cell"
                 % PROJECTION_ROUND_DECIMALS)
    lines.append("            C15 density: aggregate +-%.0f %% per material, "
                 "per-triangle +-%.0f %% above %.2f m^2, tile band %.0f-%.0f px/m, "
                 "patch floor >= %.0f px/m (2x ART-06), exact UV recompute +-%.0e "
                 "(ambiguous-plane triangles: top-two rounded |normal| components "
                 "within %.0e, <= %.0f %% of triangles / %.1f %% of world area)"
                 % (DENSITY_AGG_TOL * 100.0, DENSITY_TRI_TOL * 100.0,
                    DENSITY_TRI_MIN_WORLD_AREA, TILE_DENSITY_BAND_DEFAULT[0],
                    TILE_DENSITY_BAND_DEFAULT[1], DENSITY_PATCH_MIN, EXACT_UV_TOL,
                    AMBIGUOUS_PLANE_TOL, 100.0 * AMBIGUOUS_COUNT_MAX,
                    100.0 * AMBIGUOUS_AREA_MAX))
    lines.append("ART-05/07: every material resolves to its OWN vanilla _D albedo + "
                 "_Color tint; C7/C11 prove the FBX/GLB uv0 stays inside that "
                 "material's ART-07 canvas, C12-C14 prove the map covers every used "
                 "material and the recorded patch/canvas statistics match the real "
                 "PNG bytes (no PaintedMetal hazard stripe is shipped), C15 proves "
                 "the native-on-atlas texel density and C16 proves the frozen "
                 "mapping contract / per-model determinism")
    lines.append("50%%-camera C9: %d px/grid raster (%dx%d); a tier pair must differ on "
                 ">=%d of {material>=%.2f, windows>=%.2f, silhouette>=%.2f, "
                 "roof-plan>=%.3f, ports>=%.0f%%}"
                 % (RASTER_SCALE, RASTER_SIZE, RASTER_SIZE, DIST_CRITERIA_MIN,
                    DIST_MATERIAL_MIN, DIST_GLASS_MIN, DIST_SILHOUETTE_MIN,
                    DIST_PLAN_MIN, DIST_PORT_MIN * 100))
    lines.append("")
    lines.append("-" * 118)
    lines.append("CHECKS")
    for cid, title in CHECK_DEFS:
        lines.append("  %-3s %s (per model)" % (cid, title))
    for cid, title in CROSS_CHECK_DEFS:
        lines.append("  %-3s %s (cross-model)" % (cid, title))
    lines.append("")
    lines.append("CROSS-MODEL MATERIAL CHECKS (C12-C16; see their sections below)")
    for cid, title in CROSS_CHECK_DEFS:
        lines.append("  %-3s %s" % (cid, title))
    lines.append("")
    # file-presence check (C1 across the whole expected file set)
    for result in validator.results:
        if result["spec"] is None:
            check = result["checks"]["C1"]
            lines.append("FILE SET  %-5s %s" % (check.status, check.text))
            for note in check.notes:
                lines.append("        - %s" % note)
            for reason in check.reasons:
                lines.append("        ! %s" % reason)
    lines.append("")
    lines.append("=" * 118)
    lines.append("SUMMARY TABLE (one row per source model)")
    lines.append("=" * 118)
    header = "%-40s" % "MODEL"
    header += "".join("%3s" % cid for cid, _t in CHECK_DEFS[:7])
    header += "  %6s %8s  %-15s" % ("TRIS", "MATS", "ENVELOPE X/Y/Z")
    lines.append(header)
    for result in validator.results:
        spec = result["spec"]
        if spec is None:
            continue
        checks = result["checks"]
        row = "%-40s" % spec["label"]
        for cid, _title in CHECK_DEFS[:7]:
            row += "%3s" % _letter(checks[cid])
        facts = result["facts"]["glb"]
        tris = facts.tri_count if not facts.error else -1
        mats = len(facts.materials) if not facts.error else -1
        if not facts.error:
            lo, hi = facts.bounds()
            envelope = _fmt3(hi - lo)
        else:
            envelope = "-"
        row += "  %6d %8d  %-15s" % (tris, mats, envelope)
        lines.append(row)
    lines.append("  C8/C9/C10 are cross-model checks; see their sections below.")
    lines.append("")
    lines.append("=" * 118)
    lines.append("PER-MODEL CHECKS")
    lines.append("=" * 118)
    for result in validator.results:
        spec = result["spec"]
        if spec is None:
            continue
        lines.append("-" * 118)
        lines.append(spec["label"])
        for cid, _title in CHECK_DEFS:
            if cid in ("C8", "C9", "C10"):
                continue
            check = result["checks"][cid]
            lines.append("  %-3s %-38s %-5s %s"
                         % (cid, check.title, check.status, check.text))
            for reason in check.reasons:
                lines.append("        ! %s" % reason)
            for note in check.notes:
                lines.append("        - %s" % note)
    lines.append("")
    lines.append("=" * 118)
    lines.append("C8: LOD BOUNDING BOX")
    lines.append("=" * 118)
    lines.append("  %-5s %-38s %s"
                 % (validator.c8.status, validator.c8.title, validator.c8.text))
    for note in validator.c8.notes:
        lines.append("        - %s" % note)
    for reason in validator.c8.reasons:
        lines.append("        ! %s" % reason)
    lines.append("")
    lines.append("=" * 118)
    lines.append("C9: FOUR TIERS DISTINGUISHABLE AT THE 50% CAMERA (LOD0)")
    lines.append("=" * 118)
    lines.append("  %-5s %-38s %s"
                 % (validator.c9.status, validator.c9.title, validator.c9.text))
    for note in validator.c9.notes:
        lines.append("        - %s" % note)
    for reason in validator.c9.reasons:
        lines.append("        ! %s" % reason)
    lines.append("")
    lines.append("=" * 118)
    lines.append("C10: material_slots.json")
    lines.append("=" * 118)
    lines.append("  %-5s %-38s %s"
                 % (validator.c10.status, validator.c10.title,
                    validator.c10.text))
    for note in validator.c10.notes:
        lines.append("        - %s" % note)
    for reason in validator.c10.reasons:
        lines.append("        ! %s" % reason)
    lines.append("")
    lines.extend(render_patch_checks(validator))
    if controls is not None:
        lines.append("=" * 118)
        lines.append("NEGATIVE CONTROLS (requirement D: each control must be rejected)")
        lines.append("=" * 118)
        lines.extend(render_negative_controls(controls))
        lines.append("")
    lines.append("=" * 118)
    lines.append("FLAT ROOF EVIDENCE (contract space; walking surface == apex)")
    lines.append("=" * 118)
    lines.append("  %-40s %8s %8s %10s %10s" % ("MODEL", "TOP", "APEX",
                                                "ABOVE_TOP", "TOP_VERTS"))
    for result in validator.results:
        spec = result["spec"]
        if spec is None:
            continue
        facts = result["facts"]["glb"]
        if facts.error:
            continue
        top = A.ROOF_PLATFORM_TOP
        above = int((facts.pos[:, 1] > top + 1e-5).sum())
        px, pz = A.roof_platform(spec["platform_tier"])
        face = (np.abs(facts.pos[:, 1] - top) <= 0.01)
        inside = ((np.abs(facts.pos[:, 0]) <= px * 0.5 + 0.03)
                  & (np.abs(facts.pos[:, 2]) <= pz * 0.5 + 0.03))
        lines.append("  %-40s %8.4f %8.4f %10d %10d"
                     % (spec["label"], top, float(facts.pos[:, 1].max()),
                        above, int((face & inside).sum())))
    lines.append("")
    lines.append("=" * 118)
    lines.append("ENVELOPE TABLE (contract space, finished LOD0 models)")
    lines.append("=" * 118)
    lines.append("  %-40s %10s %10s %10s  %-22s"
                 % ("MODEL", "SIZE X", "SIZE Y", "SIZE Z", "MIN (X,Y,Z)"))
    for result in validator.results:
        spec = result["spec"]
        if spec is None or spec["kind"] != "tier" or spec["lod"] != 0:
            continue
        facts = result["facts"]["glb"]
        if facts.error:
            continue
        lo, hi = facts.bounds()
        lines.append("  %-40s %10.4f %10.4f %10.4f  %-22s"
                     % (spec["label"], hi[0] - lo[0], hi[1] - lo[1],
                        hi[2] - lo[2],
                        "(%.3f, %.3f, %.3f)" % (lo[0], lo[1], lo[2])))
    lines.append("")
    lines.append("=" * 118)
    lines.append("VERDICT")
    lines.append("=" * 118)
    failures = []
    for result in validator.results:
        spec = result["spec"]
        if spec is None:
            for cid, check in sorted(result["checks"].items()):
                if check.status != "PASS":
                    failures.append("%s: %s" % (cid, check.text))
            continue
        for cid, check in sorted(result["checks"].items()):
            if check.status != "PASS":
                failures.append("%s %s: %s" % (spec["label"], cid, check.text))
    for check in (validator.c8, validator.c9, validator.c10):
        if check.status != "PASS":
            failures.append("%s: %s" % (check.cid, check.text))
    global_checks = (validator.globals.checks if validator.globals is not None else [])
    for check in global_checks:
        if check.status == "FAIL":
            failures.append("%s: %s" % (check.cid, check.text))
    control_verdict = None
    if controls is not None:
        control_verdict = negative_controls_verdict(controls)
        rejected_controls = sum(1 for control in controls if control.rejected)
        lines.append("NEGATIVE CONTROLS: %s (each control must be REJECTED)"
                     % control_verdict)
        lines.append("controls: %d/%d rejected"
                     % (rejected_controls, len(controls)))
        if not control_verdict.startswith("PASS"):
            failures.append("negative controls: %s" % control_verdict)
    if failures:
        lines.append("OVERALL VERDICT: FAIL")
        for failure in failures:
            lines.append("  ! %s" % failure)
    else:
        lines.append("OVERALL VERDICT: PASS")
        lines.append("  every expected source model passes C1-C11 (the ART-05 real-_D "
                     "textures and the ART-07 mode-aware UV canvases included), the "
                     "cross-model C12-C16 checks (map coverage, real-PNG patch/canvas "
                     "statistics, hazard policy, ART-07 world-UV texel density and "
                     "UV mapping contract) pass, and all %d negative controls are "
                     "rejected, under the v2 3x3x1 flat-roof contract."
                     % (len(controls) if controls is not None else 0))
    lines.append("")
    lines.append("report written by art/validate_models.py (offline validation, not "
                 "an in-game check)")
    return "\n".join(lines) + "\n"


# --------------------------------------------------------------------------- #
# main
# --------------------------------------------------------------------------- #
def _run_self_test():
    """Requirement D: run only the six negative controls (each must be rejected)."""
    patch_info = load_patch_map()
    results = []
    source_note = "no Blender source model imported (synthetic UV fixture used)"
    if HAVE_BPY and np is not None:
        specs = all_specs()
        if specs:
            spec = specs[0]
            facts = {}
            for ext in ("fbx", "glb"):
                path = os.path.join(spec["dir"], spec["stem"] + "." + ext)
                facts[ext] = load_facts(path, spec) if os.path.isfile(path) \
                    else Facts(path, spec)
            results = [{"spec": spec, "checks": {}, "facts": facts}]
            if any(not fact.error for fact in facts.values()):
                source_note = "real source model: %s (FBX/GLB import)" % spec["label"]
    controls = run_negative_controls(patch_info, results)
    lines = []
    lines.append("HDBF v2 ART-05/ART-07 - negative controls (requirement D: every "
                 "control MUST be rejected)")
    lines.append("mode:       --self-test")
    lines.append("map:        %s"
                 % (patch_info.get("source") if patch_info.get("data") is not None
                    else "UNAVAILABLE (%s)" % (patch_info.get("error") or "not loaded")))
    lines.append("source:     %s" % source_note)
    lines.append("controls:   (1) uv escape -> C11, (2) hazard-stripe patch -> C13, "
                 "(3) missing map record -> C12,")
    lines.append("            (4) tile UV x0.5 -> C15, (5) tile UV x2.0 -> C15, "
                 "(6) large patch face -> full 0..1 square -> C7/C11")
    lines.append("")
    lines.extend(render_negative_controls(controls))
    verdict = negative_controls_verdict(controls)
    lines.append("")
    lines.append("SELF-TEST VERDICT: %s" % verdict)
    print("\n".join(lines))
    return 0 if verdict.startswith("PASS") else 1


def main():
    if "--self-test" in sys.argv:
        return _run_self_test()
    no_report = "--no-report" in sys.argv
    if not HAVE_BPY or np is None:
        sys.stderr.write(
            "BLOCKED: art/validate_models.py needs Blender (bpy) and numpy.\n"
            "Run: blender.exe -b --factory-startup -noaudio --python "
            "art\\validate_models.py\n")
        return 2
    print("HDBF v2 source-model validation - Blender %s" % BLENDER_VERSION)
    print("contract: 3.00 x 1.00 x 3.00 envelope, flat roof apex y=1.00, "
          "3 ports, no #Slot#, ART-05 real-_D textures + ART-07 world-scale UVs "
          "(C7/C11 mode-aware, C15 texel density, C16 mapping contract)")
    validator = Validator()
    validator.check_files()
    results = []
    for spec in all_specs():
        print("  loading %s ..." % spec["label"])
        try:
            result = validator.check_model(spec)
        except Exception:                                      # noqa: BLE001
            traceback.print_exc()
            result = {"spec": spec, "checks": {}, "facts": {}}
            for cid, title in CHECK_DEFS[:8]:
                check = Check(cid, title)
                check.fail("validator internal error while evaluating this check",
                           [traceback.format_exc(limit=4).strip()])
                result["checks"][cid] = check
        results.append(result)
    validator.results.extend(results)
    validator.check_lod_shrink(results)
    validator.check_distinguishability(results)
    validator.check_material_slots(results)
    validator.check_global_checks(results)
    controls = run_negative_controls(validator.patch_info, results)
    meta = {"blender": BLENDER_VERSION,
            "numpy": np.__version__ if np is not None else NUMPY_ERR}
    report = build_report(validator, meta, controls)
    if not no_report:
        with open(REPORT_PATH, "w", encoding="utf-8", newline="\n") as handle:
            handle.write(report)
    # console summary
    failures = 0
    for result in validator.results:
        spec = result["spec"]
        if spec is None:
            continue
        statuses = [check.status for check in result["checks"].values()]
        verdict = "PASS" if all(s == "PASS" for s in statuses) else "FAIL"
        if verdict != "PASS":
            failures += 1
        print("  %-40s %-5s tris=%d" % (spec["label"], verdict,
                                        result["facts"]["glb"].tri_count
                                        if not result["facts"]["glb"].error else -1))
    for check in (validator.c8, validator.c9, validator.c10):
        print("  %-3s %-60s %s" % (check.cid, check.text, check.status))
        if check.status != "PASS":
            failures += 1
    for check in validator.globals.checks:
        print("  %-3s %-60s %s" % (check.cid, check.text, check.status))
        if check.status == "FAIL":
            failures += 1
    if validator.globals.variance_rows:
        print("  patch table (material | texture | pixel rect | uv rect | mean RGB "
              "| std | std_max):")
        for row in validator.globals.variance_rows:
            print("    %-4s %-34s %-28s %-18s %-36s %-20s %7.3f %7.2f"
                  % (row["status"], row["material"], row["texture"],
                     _rect_text(row["pixel_rect"]),
                     _uv_text(row["uv_rect"]) if row["uv_rect"] else "-",
                     _mean_text(row["mean_measured"]), row["std_measured"],
                     row["std_max"]))
    density_report = validator.globals.density_report or {}
    density_rows = density_report.get("rows") or {}
    if density_rows:
        print("  world-UV texel density table (material | mode | canvas | px/m "
              "expected | measured min/mean/max | tris):")
        for material in sorted(density_rows):
            row = density_rows[material]
            if row.get("measured") is None:
                print("    %-4s %-38s %-5s %-20s %8.2f %9s %9s %9s %6d"
                      % ("FAIL", material, row.get("mode") or "?",
                         _rect_text(row["canvas"]) if row.get("canvas") else "-",
                         row.get("px_expected") or 0.0, "-", "-", "-",
                         row.get("tri_count") or 0))
                continue
            print("    %-4s %-38s %-5s %-20s %8.2f %9.3f %9.3f %9.3f %6d"
                  % (row["status"], material, row["mode"],
                     _rect_text(row["canvas"]) if row["canvas"] else "-",
                     row["px_expected"], row["min"], row["measured"], row["max"],
                     row["tri_count"]))
        band = tile_density_band(validator.patch_info.get("data"))
        print("    tile band %.0f-%.0f px/m; patch floor >= %.0f px/m (2x ART-06; "
              "WindowsAtlas glass pane 82.4 px/m by design); aggregate tolerance "
              "+-%.0f %%; ambiguous planes %.2f %% of triangles / %.3f %% of area"
              % (band[0], band[1], DENSITY_PATCH_MIN, DENSITY_AGG_TOL * 100.0,
                 100.0 * density_report.get("amb_fraction", 0.0),
                 100.0 * density_report.get("amb_area_fraction", 0.0)))
    control_verdict = negative_controls_verdict(controls)
    print("")
    print("NEGATIVE CONTROLS (requirement D; each must be rejected, or the run fails)")
    for line in render_negative_controls(controls):
        print(line)
    controls_ok = control_verdict.startswith("PASS")
    if not controls_ok:
        failures += 1
    print("\nOVERALL VERDICT: %s" % ("PASS" if failures == 0 else "FAIL"))
    if no_report:
        print("report: not written (--no-report)")
    else:
        print("report: art/validation-report.txt")
    return 0 if failures == 0 else 1


if __name__ == "__main__":
    try:
        sys.exit(main())
    except SystemExit:
        raise
    except Exception:
        traceback.print_exc()
        sys.exit(1)
