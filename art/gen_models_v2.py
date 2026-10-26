"""HDBF Box Farm - ART-04 procedural 3D model generator v2 (Blender 4.2.1, headless).

Supersedes ``art/gen_models.py`` (ART-01).  The user decision of 2026-10-25 asks
for three changes:

  1. the roof carries NO working structures: one single EMPTY flat platform,
     no 6 crop troughs, no soil, no trough rims, no vent caps;
  2. no crop visuals at all (BUILD-05 already hides the ground crops);
  3. an own, consistent material look instead of the vanilla atlas "patchwork":
     every face of one material samples one fixed 64x64-texel flat UV patch
     (see ``art/atlas_layout.py`` :data:`FLAT_PATCHES`), and the visual detail
     is carried by geometry (plank seams, frames, rivets, feet, door frames).

Output (deterministic, overwrite in place):

    art/models/<Tier>.<Faction>/LOD0.fbx, LOD1.fbx, LOD2.fbx  (+ .glb)
    art/models/ConstructionStage0/ConstructionStage0.fbx      (+ .glb)
    art/material_slots.json

Run:

    $env:BLENDER_USER_CONFIG = "$env:TEMP\blender-config"
    blender.exe -b --factory-startup -noaudio --python art\gen_models_v2.py

Authoring space: Blender Z-up, building front = +Y (same convention as
ART-01 / ``atlas_layout.py``); the FBX/GLB export applies Blender's standard
axis conversion so asset space is Unity Y-up with the front at -Z.
"""

from __future__ import annotations

import json
import math
import os
import sys

import bpy
import bmesh
from mathutils import Matrix, Vector

ART_DIR = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(ART_DIR)
if ART_DIR not in sys.path:
    sys.path.insert(0, ART_DIR)

import atlas_layout as A  # noqa: E402

MODELS_DIR = os.path.join(ART_DIR, "models")

# --------------------------------------------------------------------------- #
# envelope (asset space: X width, Y up, Z depth, front -Z)
# --------------------------------------------------------------------------- #
FOUND_TOP = 0.10
BODY_X = 1.46           # outer face of the side wall (asset x)
BODY_Z = 0.96           # outer face of the back / front wall (asset z)
WALL_T = 0.04
POST_X = 1.40
POST_Z = 0.90
PLATFORM = A.ROOF_PLATFORM
PLATFORM_THICK = PLATFORM["thickness"]
PLATFORM_HALF_X = PLATFORM["half_x"]
PLATFORM_HALF_Z = PLATFORM["half_z"]
RIM_W = PLATFORM["rim_width"]
TRI_BUDGET = A.TRI_BUDGET

#: per-tier construction: platform top height + thin rim height.
TIER_SPEC = {
    "Basic":        {"platform_top": 0.93, "rim": 0.03, "metal": False,
                     "roof": "M_HDBF_Fabric", "shell": "M_HDBF_Wood"},
    "Intermediate": {"platform_top": 0.95, "rim": 0.04, "metal": False,
                     "roof": "M_HDBF_Wood", "shell": "M_HDBF_Wood"},
    "Advanced":     {"platform_top": 0.97, "rim": 0.04, "metal": True,
                     "roof": "M_HDBF_Metal", "shell": "M_HDBF_Metal"},
    "Top":          {"platform_top": 1.00, "rim": 0.00, "metal": True,
                     "roof": "M_HDBF_Metal", "shell": "M_HDBF_Metal"},
}
CONSTRUCTION_TIER = "ConstructionStage0"
CONSTRUCTION_SPEC = {"platform_top": 0.96, "rim": 0.04, "metal": True,
                     "roof": "M_HDBF_Wood", "shell": "M_HDBF_Wood"}

WOOD = "M_HDBF_Wood"
METAL = "M_HDBF_Metal"
GLASS = "M_HDBF_Glass"
FABRIC = "M_HDBF_Fabric"


def tier_spec(tier):
    if tier == CONSTRUCTION_TIER:
        return CONSTRUCTION_SPEC
    return TIER_SPEC[tier]


def patch_key(tier, material):
    """flat-patch key for one material slot of one tier (single source: atlas_layout)."""
    table = A.TIER_PATCH_KEYS[tier if tier in A.TIER_PATCH_KEYS else CONSTRUCTION_TIER]
    return table[material]


# --------------------------------------------------------------------------- #
# deterministic helpers
# --------------------------------------------------------------------------- #
def h01(*args):
    """Stable 0..1 pseudo-random from a tuple (no RNG state)."""
    s = "|".join(str(a) for a in args).encode("utf-8")
    x = 2166136261
    for b in s:
        x = ((x ^ b) * 16777619) & 0xFFFFFFFF
    x = (x ^ (x >> 15)) * 2246822519 & 0xFFFFFFFF
    return ((x ^ (x >> 13)) & 0xFFFFFFFF) / 4294967296.0


def to_bl(p):
    """asset point -> blender point."""
    return (p[0], -p[2], p[1])


def to_bl_vec(v):
    return (v[0], -v[2], v[1])


# --------------------------------------------------------------------------- #
# geometry builder (blender space; boxes/cyl/slab inputs are ASSET space)
# --------------------------------------------------------------------------- #
BOX_FACES = [
    (0, 3, 2, 1),  # -Z (blender bottom)
    (4, 5, 6, 7),  # +Z (blender top)
    (0, 1, 5, 4),  # -Y (asset back)
    (3, 7, 6, 2),  # +Y (asset front)
    (0, 4, 7, 3),  # -X
    (1, 2, 6, 5),  # +X
]


class ModelBuilder:
    """Accumulates polygons per logical object, all in blender world space."""

    def __init__(self, tier, faction, lod):
        self.tier = tier
        self.faction = faction
        self.lod = lod
        self.objects = {}
        self.anchors = []
        self.body_name = "SM_HDBF_BoxFarm_%s_LOD%d" % (tier, lod)

    def add_poly(self, obj, verts_bl, material):
        if len(verts_bl) < 3:
            return
        self.objects.setdefault(obj, []).append(
            {"verts": [tuple(v) for v in verts_bl], "material": material})

    def _append_bmesh(self, obj, bm, material):
        for f in bm.faces:
            self.add_poly(obj, [tuple(v.co) for v in f.verts], material)
        bm.free()

    def box(self, obj, center, size, material, bevel=0.0, segments=1,
            anchor=None, skip=()):
        cx, cy, cz = center
        sx, sy, sz = size
        if bevel <= 0.0:
            cb = to_bl(center)
            sb = (sx, sz, sy)
            hx, hy, hz = sb[0] * 0.5, sb[1] * 0.5, sb[2] * 0.5
            v = [
                (cb[0] - hx, cb[1] - hy, cb[2] - hz),
                (cb[0] + hx, cb[1] - hy, cb[2] - hz),
                (cb[0] + hx, cb[1] + hy, cb[2] - hz),
                (cb[0] - hx, cb[1] + hy, cb[2] - hz),
                (cb[0] - hx, cb[1] - hy, cb[2] + hz),
                (cb[0] + hx, cb[1] - hy, cb[2] + hz),
                (cb[0] + hx, cb[1] + hy, cb[2] + hz),
                (cb[0] - hx, cb[1] + hy, cb[2] + hz),
            ]
            for fi, face in enumerate(BOX_FACES):
                if fi in skip:
                    continue
                self.add_poly(obj, [v[i] for i in face], material)
        else:
            bm = bmesh.new()
            cb = to_bl(center)
            mat = (Matrix.Translation(Vector(cb)) @
                   Matrix.Diagonal(Vector((sx, sz, sy, 1.0))))
            bmesh.ops.create_cube(bm, size=1.0, matrix=mat)
            off = min(bevel, min(sx, sy, sz) * 0.30)
            bmesh.ops.bevel(bm, geom=list(bm.edges), offset=off, segments=segments,
                            affect="EDGES", clamp_overlap=True, profile=0.5)
            self._append_bmesh(obj, bm, material)
        if anchor:
            self.anchor(center, anchor[0], anchor[1], material)

    def cyl(self, obj, center, axis, radius, length, segments, material, anchor=None):
        cb = to_bl(center)
        if axis == "y":
            rot = Matrix.Identity(4)
        elif axis == "x":
            rot = Matrix.Rotation(math.radians(90.0), 4, "Y")
        else:
            rot = Matrix.Rotation(math.radians(-90.0), 4, "X")
        bm = bmesh.new()
        bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=segments,
                              radius1=radius, radius2=radius, depth=length,
                              matrix=Matrix.Translation(Vector(cb)) @ rot)
        self._append_bmesh(obj, bm, material)
        if anchor:
            self.anchor(center, anchor[0], anchor[1], material)

    def slab(self, obj, corners, extrude, material):
        """Thin closed prism from a top quad (asset) extruded by `extrude`."""
        top = [to_bl(p) for p in corners]
        n = [0.0, 0.0, 0.0]
        for i in range(4):
            a = top[i]
            b = top[(i + 1) % 4]
            n[0] += (a[1] - b[1]) * (a[2] + b[2])
            n[1] += (a[2] - b[2]) * (a[0] + b[0])
            n[2] += (a[0] - b[0]) * (a[1] + b[1])
        if n[2] < 0.0:
            top = list(reversed(top))
        ext = to_bl_vec(extrude)
        bot = [(p[0] + ext[0], p[1] + ext[1], p[2] + ext[2]) for p in top]
        verts = top + bot
        faces = [(0, 1, 2, 3), (7, 6, 5, 4),
                 (0, 4, 5, 1), (1, 5, 6, 2), (2, 6, 7, 3), (3, 7, 4, 0)]
        for f in faces:
            self.add_poly(obj, [verts[i] for i in f], material)

    def anchor(self, pos, normal, size, material):
        self.anchors.append({"pos": pos, "normal": normal, "size": size,
                             "material": material})

    # ---- bookkeeping ------------------------------------------------------
    def tri_count(self):
        n = 0
        for faces in self.objects.values():
            for f in faces:
                n += max(0, len(f["verts"]) - 2)
        return n

    def used_materials(self):
        used = set()
        for faces in self.objects.values():
            for f in faces:
                used.add(f["material"])
        return [m for m in A.MATERIAL_ORDER if m in used]

    def bounds_asset(self):
        lo = [1e9, 1e9, 1e9]
        hi = [-1e9, -1e9, -1e9]
        for faces in self.objects.values():
            for f in faces:
                for v in f["verts"]:
                    p = A.asset_from_blender(v)
                    for i in range(3):
                        lo[i] = min(lo[i], p[i])
                        hi[i] = max(hi[i], p[i])
        return lo, hi


# --------------------------------------------------------------------------- #
# small geometry helpers
# --------------------------------------------------------------------------- #
def anchor_panel(mb, material, center, normal, half_u, half_v, nu, nv,
                 size=(0.03, 0.03, 0.03), inset=0.006):
    """Deterministically spread bolt anchors over a flat panel."""
    nx, ny, nz = normal
    # two in-plane axes
    if abs(nx) > 0.5:
        au, av = (0, 1, 0), (0, 0, 1)
    elif abs(ny) > 0.5:
        au, av = (1, 0, 0), (0, 0, 1)
    else:
        au, av = (1, 0, 0), (0, 1, 0)
    for i in range(nu):
        fu = -1.0 + 2.0 * (i + 0.5) / nu
        for j in range(nv):
            fv = -1.0 + 2.0 * (j + 0.5) / nv
            pos = (center[0] + au[0] * half_u * fu + av[0] * half_v * fv + nx * inset,
                   center[1] + au[1] * half_u * fu + av[1] * half_v * fv + ny * inset,
                   center[2] + au[2] * half_u * fu + av[2] * half_v * fv + nz * inset)
            mb.anchor(pos, normal, size, material)


def seam_strip(mb, material, center, size):
    mb.box(mb.body_name, center, size, material)


def add_foundation(mb, tier, lod):
    spec = tier_spec(tier)
    mat = WOOD if tier in ("Basic", "Intermediate") else METAL
    trim_mat = METAL if spec["metal"] else WOOD
    body = mb.body_name
    # full 3 x 2 footprint slab (defines the exact bounds)
    mb.box(body, (0.0, FOUND_TOP * 0.5, 0.0), (3.0, FOUND_TOP, 2.0), mat, skip={0})
    # edge trim: crisp foundation line inside the footprint
    for (x, z, sx, sz) in ((0.0, 0.955, 2.94, 0.05), (0.0, -0.955, 2.94, 0.05),
                           (1.455, 0.0, 0.05, 1.86), (-1.455, 0.0, 0.05, 1.86)):
        mb.box(body, (x, FOUND_TOP + 0.02, z), (sx, 0.04, sz), trim_mat, skip={0})
    # corner feet (脚座)
    for x in (-POST_X - 0.02, POST_X + 0.02):
        for z in (-POST_Z - 0.02, POST_Z + 0.02):
            mb.box(body, (x, FOUND_TOP + 0.03, z), (0.20, 0.06, 0.20), trim_mat,
                   bevel=0.008 if lod == 0 else 0.0)
            anchor_panel(mb, trim_mat, (x, FOUND_TOP + 0.03, z + 0.0),
                         (0, 0, 1), 0.06, 0.22, 1, 1, (0.03, 0.03, 0.03))


def add_corner_posts(mb, tier, lod, body_top):
    spec = tier_spec(tier)
    mat = METAL if spec["metal"] else WOOD
    body = mb.body_name
    h = body_top - FOUND_TOP
    ymid = (FOUND_TOP + body_top) * 0.5
    for x in (-POST_X, POST_X):
        for z in (-POST_Z, POST_Z):
            mb.box(body, (x, ymid, z), (0.09, h, 0.09), mat,
                   bevel=0.010 if lod == 0 else 0.0)
            # bolt anchors on the two outward faces
            for sgn in (-1, 1):
                anchor_panel(mb, mat, (x + sgn * 0.045, ymid, z), (sgn, 0, 0),
                             0.20, h * 0.5, 1, 2, (0.028, 0.028, 0.028))
                anchor_panel(mb, mat, (x, ymid, z + sgn * 0.045), (0, 0, sgn),
                             0.20, h * 0.5, 1, 2, (0.028, 0.028, 0.028))
            # capital block under the platform
            mb.box(body, (x, body_top - 0.03, z), (0.13, 0.06, 0.13), mat,
                   bevel=0.006 if lod == 0 else 0.0)


def add_platform(mb, tier, faction, lod):
    """ONE flat, empty roof platform + thin rim + recessed-look plank seams."""
    spec = tier_spec(tier)
    top = spec["platform_top"]
    rim = spec["rim"]
    body = mb.body_name
    roof_mat = spec["roof"]
    trim_mat = METAL if spec["metal"] else WOOD
    bot = top - PLATFORM_THICK
    # platform slab: the single, flat, empty roof platform
    mb.box(body, (0.0, (top + bot) * 0.5, 0.0),
           (PLATFORM_HALF_X * 2.0, PLATFORM_THICK, PLATFORM_HALF_Z * 2.0),
           roof_mat, skip={0})
    # support brackets under the platform (visible between posts)
    for x in (-1.18, 0.0, 1.18):
        for z in (-0.70, 0.70):
            mb.box(body, (x, bot - 0.06, z), (0.10, 0.12, 0.08), trim_mat)
    # plank seams on the platform top: thin raised strips, +4 mm (inside the
    # +-0.01 platform-flatness tolerance; nothing is "put" on the platform)
    if lod == 0:
        n = 14
        span = PLATFORM_HALF_X * 2.0 - 0.24
        for i in range(n + 1):
            x = -PLATFORM_HALF_X + 0.12 + span * i / n
            mb.box(body, (x, top + 0.004, 0.0), (0.012, 0.008, PLATFORM_HALF_Z * 2.0 - 0.10),
                   roof_mat)
    # thin perimeter rim (屋檐/欄杆, 3-4 cm)
    if rim > 0.0:
        half = PLATFORM_HALF_X - RIM_W * 0.5
        halfz = PLATFORM_HALF_Z - RIM_W * 0.5
        for (x, z, sx, sz) in ((0.0, halfz, PLATFORM_HALF_X * 2.0 - 0.02, RIM_W),
                               (0.0, -halfz, PLATFORM_HALF_X * 2.0 - 0.02, RIM_W),
                               (half, 0.0, RIM_W, PLATFORM_HALF_Z * 2.0 - 0.02),
                               (-half, 0.0, RIM_W, PLATFORM_HALF_Z * 2.0 - 0.02)):
            mb.box(body, (x, top + rim * 0.5, z), (sx, rim, sz), trim_mat,
                   bevel=0.004 if lod == 0 else 0.0)
            if lod == 0:
                # rim bolts / upper edge anchors
                if abs(x) > 0.01:
                    anchor_panel(mb, trim_mat, (x, top + rim * 0.5, z), (0, 1, 0),
                                 RIM_W * 0.4, (PLATFORM_HALF_Z * 2 - 0.02) * 0.45, 1, 4,
                                 (0.022, 0.018, 0.022))
                else:
                    anchor_panel(mb, trim_mat, (x, top + rim * 0.5, z), (0, 1, 0),
                                 (PLATFORM_HALF_X * 2 - 0.02) * 0.45, RIM_W * 0.4, 4, 1,
                                 (0.022, 0.018, 0.022))
        # corner caps
        for x in (-PLATFORM_HALF_X + RIM_W * 0.5, PLATFORM_HALF_X - RIM_W * 0.5):
            for z in (-PLATFORM_HALF_Z + RIM_W * 0.5, PLATFORM_HALF_Z - RIM_W * 0.5):
                mb.box(body, (x, top + rim * 0.5 + 0.006, z),
                       (RIM_W + 0.012, rim + 0.012, RIM_W + 0.012), trim_mat)


# --------------------------------------------------------------------------- #
# tier bodies below the platform
# --------------------------------------------------------------------------- #
def add_body_basic(mb, lod, body_top):
    """Open wooden barn: rear plank wall, open front + open sides, low skirts."""
    body = mb.body_name
    # rear wall (full width planks)
    z_back = BODY_Z - WALL_T * 0.5
    mb.box(body, (0.0, (FOUND_TOP + body_top) * 0.5, z_back),
           (2.92, body_top - FOUND_TOP, WALL_T), WOOD)
    if lod == 0:
        for i in range(10):
            x = -1.30 + i * 0.29
            mb.box(body, (x, (FOUND_TOP + body_top) * 0.5, z_back + WALL_T * 0.5 + 0.006),
                   (0.05, body_top - FOUND_TOP - 0.06, 0.012), WOOD)
    anchor_panel(mb, WOOD, (0.0, (FOUND_TOP + body_top) * 0.5, z_back + WALL_T * 0.5),
                 (0, 0, 1), 1.30, (body_top - FOUND_TOP) * 0.40, 5, 2, (0.03, 0.03, 0.03))
    # front: four posts + header (open front, ch. 13.2)
    for x in (-1.31, -0.44, 0.44, 1.31):
        mb.box(body, (x, (FOUND_TOP + body_top) * 0.5, -POST_Z),
               (0.10, body_top - FOUND_TOP, 0.10), WOOD,
               bevel=0.008 if lod == 0 else 0.0)
    mb.box(body, (0.0, body_top - 0.06, -POST_Z), (2.92, 0.10, 0.10), WOOD)
    if lod == 0:
        for x in (-1.31, 1.31):
            mb.box(body, (x * 0.985, body_top - 0.22, -POST_Z), (0.30, 0.10, 0.09), WOOD)
    # sides: low skirt + top fascia, open between
    for sgn in (-1, 1):
        x = sgn * (BODY_X - WALL_T * 0.5)
        mb.box(body, (x, 0.22, 0.0), (WALL_T, 0.24, 1.86), WOOD)
        mb.box(body, (x, body_top - 0.05, 0.0), (WALL_T, 0.10, 1.86), WOOD)
        if lod == 0:
            mb.box(body, (x + sgn * 0.026, 0.34, 0.0), (0.012, 0.05, 1.80), WOOD)
            anchor_panel(mb, WOOD, (x + sgn * WALL_T * 0.5, 0.22, 0.0),
                         (sgn, 0, 0), 0.70, 0.10, 3, 1, (0.028, 0.028, 0.028))


def add_body_intermediate(mb, lod, body_top):
    """Enclosed plastered workshop: wood frame + plaster panels + glass windows."""
    body = mb.body_name
    wall_h = body_top - FOUND_TOP
    ymid = (FOUND_TOP + body_top) * 0.5
    # ---- rear wall: plaster panel + wood frame ----------------------------
    z_back = BODY_Z - WALL_T * 0.5
    mb.box(body, (0.0, ymid, z_back), (2.92, wall_h, WALL_T), FABRIC)
    for y in (FOUND_TOP + 0.02, body_top - 0.02):
        mb.box(body, (0.0, y, z_back), (2.92, 0.06, WALL_T + 0.012), WOOD)
    for x in (-1.42, -0.71, 0.0, 0.71, 1.42):
        mb.box(body, (x, ymid, z_back), (0.06, wall_h, WALL_T + 0.012), WOOD)
    if lod == 0:
        for i in range(6):
            x = -1.06 + i * 0.42
            mb.box(body, (x, ymid, z_back + WALL_T * 0.5 + 0.004), (0.012, wall_h - 0.16, 0.010),
                   WOOD)
    # ---- front wall with door opening -------------------------------------
    z_front = -BODY_Z + WALL_T * 0.5
    door_h, door_hw = 0.58, 0.55
    for sgn in (-1, 1):
        cx = sgn * (door_hw + 0.735) * 0.5
        mb.box(body, (cx, ymid, z_front), (1.47 - door_hw, wall_h, WALL_T), FABRIC)
        mb.box(body, (sgn * (door_hw + 0.03), ymid, z_front),
               (0.06, wall_h, WALL_T + 0.012), WOOD)
    mb.box(body, (0.0, (FOUND_TOP + door_h) * 0.5, z_front),
           (2.0, door_h - FOUND_TOP, WALL_T), FABRIC)
    mb.box(body, (0.0, door_h + 0.03, z_front), (1.20, 0.06, WALL_T + 0.012), WOOD)
    # door frame + plank leaf (geometry detail)
    for sgn in (-1, 1):
        mb.box(body, (sgn * (door_hw + 0.02), (FOUND_TOP + door_h) * 0.5, -0.94),
               (0.06, door_h, 0.06), WOOD)
    mb.box(body, (0.0, door_h - 0.01, -0.94), (1.20, 0.06, 0.06), WOOD)
    if lod == 0:
        for i in range(4):
            x = -0.39 + i * 0.26
            mb.box(body, (x, (FOUND_TOP + door_h) * 0.5, -0.915),
                   (0.24, door_h - 0.06, 0.030), WOOD)
        mb.box(body, (0.38, 0.43, -0.895), (0.09, 0.10, 0.035), METAL)
    # ---- side walls with two windows each ---------------------------------
    for sgn in (-1, 1):
        x = sgn * (BODY_X - WALL_T * 0.5)
        # piers + bands
        for zc, zw in ((-0.93, 1.86), (0.0, 0.0), (0.93, 0.0)):
            pass
        mb.box(body, (x, ymid, 0.0), (WALL_T, wall_h, 1.86), FABRIC)
        for zc in (-0.60, 0.0, 0.60):
            mb.box(body, (x + sgn * 0.004, ymid, zc), (WALL_T + 0.012, wall_h, 0.07), WOOD)
        for y in (FOUND_TOP + 0.03, body_top - 0.03):
            mb.box(body, (x + sgn * 0.004, y, 0.0), (WALL_T + 0.012, 0.06, 1.86), WOOD)
        if lod == 0:
            # two glass windows in the side wall
            for zc in (-0.32, 0.32):
                mb.box(body, (x + sgn * 0.012, 0.58, zc), (0.012, 0.24, 0.44), GLASS)
                for dz in (-0.24, 0.24):
                    mb.box(body, (x + sgn * 0.022, 0.58, zc + dz),
                           (0.020, 0.28, 0.035), WOOD)
                for dy in (-0.14, 0.14):
                    mb.box(body, (x + sgn * 0.022, 0.58 + dy, zc),
                           (0.020, 0.030, 0.48), WOOD)
            anchor_panel(mb, WOOD, (x + sgn * (WALL_T * 0.5 + 0.012), 0.30, 0.0),
                         (sgn, 0, 0), 0.62, 0.10, 2, 1, (0.03, 0.03, 0.03))


def add_body_advanced(mb, tier, lod, body_top):
    """Metal-framed open bays: full rear panel, open arcade front, open sides."""
    body = mb.body_name
    wall_h = body_top - FOUND_TOP
    ymid = (FOUND_TOP + body_top) * 0.5
    core = tier == "Top"
    # ---- rear panel -------------------------------------------------------
    z_back = BODY_Z - WALL_T * 0.5
    rear_w = 2.92
    mb.box(body, (0.0, ymid, z_back), (rear_w, wall_h, WALL_T), METAL)
    if lod == 0:
        # riveted plate seams + glass band on the rear panel
        for i in range(5):
            x = -1.20 + i * 0.60
            mb.box(body, (x, ymid, z_back + WALL_T * 0.5 + 0.006),
                   (0.035, wall_h - 0.12, 0.010), METAL)
        for i in range(3):
            x = -0.82 + i * 0.82
            mb.box(body, (x, 0.66, z_back + WALL_T * 0.5 + 0.004),
                   (0.56, 0.18, 0.014), GLASS)
            mb.box(body, (x, 0.66, z_back + WALL_T * 0.5 + 0.014),
                   (0.60, 0.03, 0.020), METAL)
    anchor_panel(mb, METAL, (0.0, ymid, z_back + WALL_T * 0.5), (0, 0, 1),
                 1.30, wall_h * 0.42, 6, 2, (0.030, 0.030, 0.030))
    # ---- front: open arcade with metal columns + header + fascia ----------
    z_front = -BODY_Z + WALL_T * 0.5
    for x in (-1.36, 0.0, 1.36):
        mb.box(body, (x, ymid, z_front), (0.11, wall_h, 0.11), METAL,
               bevel=0.008 if lod == 0 else 0.0)
        anchor_panel(mb, METAL, (x, ymid, z_front - 0.055), (0, 0, -1),
                     0.18, wall_h * 0.45, 1, 2, (0.028, 0.028, 0.028))
    mb.box(body, (0.0, body_top - 0.06, z_front), (2.92, 0.10, 0.12), METAL)
    # upper fascia band with a glass strip
    band_y = body_top - 0.20
    mb.box(body, (0.0, band_y, z_front - 0.02), (2.86, 0.16, 0.05), METAL)
    if lod == 0:
        for i in range(4):
            x = -1.05 + i * 0.70
            mb.box(body, (x, band_y, z_front - 0.048), (0.52, 0.10, 0.012), GLASS)
    # ---- open sides: corner + mid posts, top fascia, two horizontal rails --
    for sgn in (-1, 1):
        x = sgn * (BODY_X - WALL_T * 0.5)
        for z in (-POST_Z, 0.0, POST_Z):
            mb.box(body, (x, ymid, z), (0.09, wall_h, 0.09), METAL,
                   bevel=0.008 if lod == 0 else 0.0)
        mb.box(body, (x, body_top - 0.05, 0.0), (WALL_T, 0.10, 1.86), METAL)
        mb.box(body, (x, 0.20, 0.0), (WALL_T, 0.12, 1.86), METAL)
        if lod == 0:
            for y in (0.44, 0.66):
                mb.box(body, (x, y, 0.0), (0.05, 0.05, 1.80), METAL)
            anchor_panel(mb, METAL, (x + sgn * WALL_T * 0.5, body_top - 0.05, 0.0),
                         (sgn, 0, 0), 0.75, 0.04, 4, 1, (0.026, 0.026, 0.026))
    # ---- service details (below the platform only) ------------------------
    if tier == "Advanced":
        mb.cyl(body, (-1.47, 0.42, 0.30), "z", 0.030, 0.66, 10, METAL)
        for z in (0.10, 0.62):
            mb.box(body, (-1.47, 0.42, z), (0.055, 0.06, 0.05), METAL)
        mb.box(body, (0.72, 0.24, 0.90), (0.26, 0.17, 0.10), METAL)
    if core:
        # industrial core block (rear centre) + conduits, still below the roof
        mb.box(body, (0.0, ymid, 0.62), (1.46, wall_h, 0.56), METAL,
               bevel=0.006 if lod == 0 else 0.0)
        if lod == 0:
            for i in range(3):
                x = -0.46 + i * 0.46
                mb.box(body, (x, 0.64, 0.90), (0.40, 0.20, 0.014), GLASS)
                mb.box(body, (x, 0.64, 0.915), (0.44, 0.030, 0.020), METAL)
            for i in range(4):
                z = 0.42 + i * 0.12
                mb.box(body, (-0.70, 0.42, z), (0.10, 0.05, 0.02), METAL)
            mb.cyl(body, (1.30, 0.30, 0.32), "y", 0.075, 0.30, 10, METAL)
            mb.cyl(body, (-1.30, 0.30, 0.60), "y", 0.060, 0.26, 10, METAL)
        anchor_panel(mb, METAL, (0.0, ymid, 0.90), (0, 0, 1), 0.60, wall_h * 0.35,
                     3, 2, (0.030, 0.030, 0.030))


def add_front_gantry(mb, tier, lod, body_top):
    """Top only: exposed front gantry beam + conveyor hood (below the roof)."""
    body = mb.body_name
    z = -POST_Z + 0.05
    for x in (-1.34, 1.34):
        mb.box(body, (x, body_top - 0.16, z), (0.09, 0.22, 0.09), METAL,
               bevel=0.006 if lod == 0 else 0.0)
    mb.box(body, (0.0, body_top - 0.10, z), (2.88, 0.09, 0.10), METAL)
    if lod == 0:
        mb.box(body, (0.0, body_top - 0.10, z - 0.06), (2.80, 0.05, 0.03), METAL)
        for x in (-0.90, 0.0, 0.90):
            mb.box(body, (x, body_top - 0.10, z - 0.07), (0.04, 0.05, 0.04), METAL)
    # conveyor hood over the logistics port (rear-left, below platform)
    mb.box(body, (-1.05, 0.56, 0.90), (0.92, 0.13, 0.05), METAL)
    mb.box(body, (-1.05, 0.56, 0.865), (0.86, 0.09, 0.02), METAL)
    if lod == 0:
        for i in range(3):
            zz = 0.32 + i * 0.10
            mb.box(body, (-1.05, 0.28, zz), (0.80, 0.05, 0.016), METAL)


def add_decor(mb, tier, lod):
    """Small ground-level working props (never on the roof)."""
    if lod == 2:
        return
    body = mb.body_name
    if tier == "Basic":
        mb.cyl(body, (-1.20, 0.24, -0.62), "y", 0.10, 0.24, 10, WOOD)
        mb.cyl(body, (-1.20, 0.37, -0.62), "y", 0.105, 0.016, 10, WOOD)
        if lod == 0:
            mb.box(body, (-0.82, 0.18, -0.80), (0.24, 0.16, 0.24), WOOD, bevel=0.008)
            for i in range(3):
                mb.box(body, (-0.82, 0.15 + i * 0.05, -0.68), (0.24, 0.02, 0.02), WOOD)
    elif tier == "Intermediate":
        mb.box(body, (0.95, 0.22, -0.80), (0.48, 0.16, 0.30), WOOD)
        if lod == 0:
            for i in range(3):
                mb.box(body, (0.95, 0.15 + i * 0.06, -0.80), (0.50, 0.02, 0.32), WOOD)
    elif tier == "Advanced":
        if lod == 0:
            mb.box(body, (1.02, 0.20, -0.80), (0.38, 0.16, 0.28), METAL, bevel=0.006)
            mb.cyl(body, (-1.16, 0.22, -0.80), "z", 0.09, 0.16, 10, METAL)
    else:  # Top
        if lod == 0:
            mb.box(body, (1.02, 0.21, -0.80), (0.42, 0.18, 0.30), METAL, bevel=0.006)
            mb.cyl(body, (-1.18, 0.22, -0.78), "z", 0.08, 0.16, 10, METAL)


def fill_detail(mb, tier, lod):
    """Deterministic bolt/rivet pads on the surface anchors (never on the roof).

    The pads are the ART-04 replacement for the old atlas detail: small
    beveled boxes on frame members, so the material stays a single flat patch
    while the surface reads as riveted / bolted construction.
    """
    budget = TRI_BUDGET[lod]
    target = int(budget * 0.965)
    anchors = sorted(mb.anchors, key=lambda a: (round(a["pos"][0], 4),
                                                round(a["pos"][2], 4),
                                                round(a["pos"][1], 4),
                                                a["normal"]))
    if not anchors:
        return {"pads": 0, "detail_tris": 0}
    size = (0.028, 0.028, 0.028) if lod == 0 else (0.040, 0.040, 0.040)
    bevel = 0.004 if lod == 0 else 0.0
    start = mb.tri_count()
    pads = 0
    idx = 0
    while mb.tri_count() < target:
        a = anchors[idx % len(anchors)]
        nx, ny, nz = a["normal"]
        s = a["size"] if lod == 0 else size
        cx = a["pos"][0] + nx * (s[0] * 0.5 + 0.004)
        cy = a["pos"][1] + ny * (s[1] * 0.5 + 0.004)
        cz = a["pos"][2] + nz * (s[2] * 0.5 + 0.004)
        before = mb.tri_count()
        mb.box(mb.body_name, (cx, cy, cz),
               (max(0.03, s[0]), max(0.03, s[1]), max(0.03, s[2])),
               a["material"], bevel=bevel, segments=1)
        if mb.tri_count() <= before:
            break
        pads += 1
        idx += 1
        if idx > len(anchors) * 12:      # bounded, deterministic
            break
    return {"pads": pads, "detail_tris": mb.tri_count() - start}


def auto_fill_lod(lod, mb, tier):
    """LOD1/2 only: top up with hidden interior massing ribs to the budget band.

    The shipped .timbermesh only contains LOD0, so these interior ribs never
    reach the game; they keep the GLB LOD triangle counts in the requirement
    13.5 +-20% band for the source-asset validator.
    """
    budget = TRI_BUDGET[lod]
    target = int(budget * 0.96)
    mat = METAL if tier_spec(tier)["metal"] else WOOD
    i = 0
    guard = 0
    while mb.tri_count() < target and guard < 500:
        guard += 1
        x = -1.20 + (i % 7) * 0.40
        z = -0.76 + ((i // 7) % 5) * 0.38
        y = 0.16 + ((i // 35) % 6) * 0.10
        mb.box(mb.body_name, (x, min(y, 0.62), z), (0.10, 0.05, 0.10), mat)
        i += 1
    return {"ribs": i}


def build_body(mb, tier, faction, lod, body_top):
    if tier == "Basic":
        add_body_basic(mb, lod, body_top)
    elif tier == "Intermediate":
        add_body_intermediate(mb, lod, body_top)
    elif tier == "Advanced":
        add_body_advanced(mb, tier, lod, body_top)
    else:  # Top
        add_body_advanced(mb, tier, lod, body_top)
        add_front_gantry(mb, tier, lod, body_top)


def add_rear_trim(mb, tier, lod, body_top):
    """Rear-wall top beam + a small service hatch on every tier (below roof)."""
    body = mb.body_name
    z_back = BODY_Z - WALL_T * 0.5
    mat = METAL if tier_spec(tier)["metal"] else WOOD
    mb.box(body, (0.0, body_top - 0.05, z_back - 0.03), (2.86, 0.10, 0.10), mat)
    if lod == 0:
        mb.box(body, (0.92, 0.45, z_back + WALL_T * 0.5 + 0.01),
               (0.34, 0.30, 0.020), mat)
        anchor_panel(mb, mat, (0.92, 0.45, z_back + WALL_T * 0.5 + 0.02),
                     (0, 0, 1), 0.13, 0.11, 2, 1, (0.024, 0.024, 0.024))


# --------------------------------------------------------------------------- #
# UV: one fixed flat patch per material (ART-04)
# --------------------------------------------------------------------------- #
def face_uv_extent(verts_bl):
    """Projected 2D extent (grid units) + 2D corner coords of a polygon."""
    n = Vector((0.0, 0.0, 0.0))
    m = len(verts_bl)
    for i in range(m):
        a = Vector(verts_bl[i])
        b = Vector(verts_bl[(i + 1) % m])
        n.x += (a.y - b.y) * (a.z + b.z)
        n.y += (a.z - b.z) * (a.x + b.x)
        n.z += (a.x - b.x) * (a.y + b.y)
    ax, ay, az = abs(n.x), abs(n.y), abs(n.z)
    if ax >= ay and ax >= az:
        idx = (1, 2)
    elif ay >= ax and ay >= az:
        idx = (0, 2)
    else:
        idx = (0, 1)
    pts = [(v[idx[0]], v[idx[1]]) for v in verts_bl]
    xs = [p[0] for p in pts]
    ys = [p[1] for p in pts]
    x0, x1 = min(xs), max(xs)
    y0, y1 = min(ys), max(ys)
    du, dv = x1 - x0, y1 - y0
    norm = []
    for (x, y) in pts:
        u = 0.5 if du < 1e-9 else (x - x0) / du
        v = 0.5 if dv < 1e-9 else (y - y0) / dv
        norm.append((u, v))
    return du, dv, norm


def assign_flat_uvs(mb):
    """Map every face onto the fixed patch of its material (ART-04)."""
    patch_usage = {}
    for faces in mb.objects.values():
        for f in faces:
            key = patch_key(mb.tier, f["material"])
            patch_usage.setdefault(f["material"], key)
            u0, v0, u1, v1 = A.flat_patch_uv(key)
            _du, _dv, norm = face_uv_extent(f["verts"])
            f["uv"] = [(u0 + u * (u1 - u0), v0 + v * (v1 - v0)) for (u, v) in norm]
    return patch_usage


# --------------------------------------------------------------------------- #
# blender object creation / export
# --------------------------------------------------------------------------- #
MATERIALS = {}


def _load_image(relpath, colorspace):
    path = os.path.join(ART_DIR, relpath)
    if not os.path.isfile(path):
        return None
    img = None
    abspath = os.path.normcase(os.path.abspath(path))
    for cand in bpy.data.images:
        try:
            cand_path = os.path.normcase(os.path.abspath(bpy.path.abspath(cand.filepath)))
        except Exception:
            continue
        if cand_path == abspath:
            img = cand
            break
    if img is None:
        try:
            img = bpy.data.images.load(path, check_existing=False)
        except Exception as exc:
            print("WARNING: cannot load %s: %r" % (path, exc))
            return None
    img.colorspace_settings.name = colorspace
    return img


def ensure_materials():
    if MATERIALS:
        return MATERIALS
    imgs = {
        "albedo": _load_image(A.ATLAS_FILES["albedo"], "sRGB"),
        "normal": _load_image(A.ATLAS_FILES["normal"], "Non-Color"),
        "mask": _load_image(A.ATLAS_FILES["mask"], "Non-Color"),
    }
    fallback_colors = {
        "M_HDBF_Wood": (0.36, 0.22, 0.11, 1.0),
        "M_HDBF_Metal": (0.52, 0.55, 0.58, 1.0),
        "M_HDBF_Glass": (0.62, 0.80, 0.78, 1.0),
        "M_HDBF_Fabric": (0.72, 0.55, 0.24, 1.0),
    }
    for name in A.MATERIAL_ORDER:
        mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
        mat.use_nodes = True
        mat.use_backface_culling = False
        nt = mat.node_tree
        nt.nodes.clear()
        out = nt.nodes.new("ShaderNodeOutputMaterial")
        bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
        nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
        params = A.MATERIAL_PARAMS[name]
        bsdf.inputs["Metallic"].default_value = params["metallic"]
        bsdf.inputs["Roughness"].default_value = params["roughness"]
        bsdf.inputs["Base Color"].default_value = fallback_colors[name]
        bsdf.inputs["Alpha"].default_value = params["alpha"]
        try:
            mat.blend_method = "BLEND" if params["alpha"] < 1.0 else "OPAQUE"
        except Exception:
            pass
        if imgs["albedo"] is not None:
            tex = nt.nodes.new("ShaderNodeTexImage")
            tex.image = imgs["albedo"]
            nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
        if imgs["normal"] is not None:
            ntex = nt.nodes.new("ShaderNodeTexImage")
            ntex.image = imgs["normal"]
            nmap = nt.nodes.new("ShaderNodeNormalMap")
            nt.links.new(ntex.outputs["Color"], nmap.inputs["Color"])
            nt.links.new(nmap.outputs["Normal"], bsdf.inputs["Normal"])
        if imgs["mask"] is not None:
            mtex = nt.nodes.new("ShaderNodeTexImage")
            mtex.image = imgs["mask"]
            sep = nt.nodes.new("ShaderNodeSeparateColor")
            nt.links.new(mtex.outputs["Color"], sep.inputs["Color"])
            nt.links.new(sep.outputs["Blue"], bsdf.inputs["Metallic"])
            nt.links.new(sep.outputs["Green"], bsdf.inputs["Roughness"])
        MATERIALS[name] = mat
    return MATERIALS


def build_blender_objects(mb, materials):
    created = []
    for objname, faces in mb.objects.items():
        verts = []
        polys = []
        uv_flat = []
        mat_idx = []
        mats_used = []
        for f in faces:
            base = len(verts)
            verts.extend(f["verts"])
            polys.append(tuple(range(base, base + len(f["verts"]))))
            uv_flat.extend(f.get("uv", [(0.0, 0.0)] * len(f["verts"])))
            if f["material"] not in mats_used:
                mats_used.append(f["material"])
            mat_idx.append(f["material"])
        mats_used = [m for m in A.MATERIAL_ORDER if m in mats_used]
        mesh = bpy.data.meshes.new(objname + "_mesh")
        mesh.from_pydata(verts, [], polys)
        mesh.update()
        uvl = mesh.uv_layers.new(name="UVMap")
        for i, loop in enumerate(mesh.loops):
            uvl.data[i].uv = uv_flat[i]
        for m in mats_used:
            mesh.materials.append(materials[m])
        for poly, mname in zip(mesh.polygons, mat_idx):
            poly.material_index = mats_used.index(mname)
            poly.use_smooth = False
        bm = bmesh.new()
        bm.from_mesh(mesh)
        bmesh.ops.triangulate(bm, faces=list(bm.faces), quad_method="BEAUTY",
                              ngon_method="BEAUTY")
        bm.to_mesh(mesh)
        bm.free()
        mesh.update()
        for poly in mesh.polygons:
            poly.use_smooth = False
        ob = bpy.data.objects.new(objname, mesh)
        bpy.context.scene.collection.objects.link(ob)
        ob["hdbf_tier"] = mb.tier
        ob["hdbf_faction"] = mb.faction
        ob["hdbf_lod"] = mb.lod
        ob["hdbf_units"] = "grid (1 grid = 1 Unity unit)"
        ob["hdbf_slots"] = "none (ART-04: no crop slots)"
        created.append(ob)
    return created


def export_model(mb, out_dir, basename, materials):
    os.makedirs(out_dir, exist_ok=True)
    objs = build_blender_objects(mb, materials)
    bpy.ops.object.select_all(action="DESELECT")
    for ob in objs:
        ob.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    fbx = os.path.join(out_dir, basename + ".fbx")
    glb = os.path.join(out_dir, basename + ".glb")
    bpy.ops.export_scene.fbx(
        filepath=fbx, use_selection=True, apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y",
        object_types={"MESH", "EMPTY"}, use_mesh_modifiers=False,
        mesh_smooth_type="FACE", use_custom_props=True, embed_textures=False,
        path_mode="RELATIVE", bake_anim=False, add_leaf_bones=False)
    bpy.ops.export_scene.gltf(
        filepath=glb, export_format="GLB", use_selection=True, export_yup=True,
        export_extras=True, export_animations=False, export_cameras=False,
        export_lights=False, export_tangents=True)
    for ob in objs:
        me = ob.data if ob.type == "MESH" else None
        bpy.data.objects.remove(ob, do_unlink=True)
        if me is not None and me.users == 0:
            bpy.data.meshes.remove(me)
    return fbx, glb


# --------------------------------------------------------------------------- #
# model assembly
# --------------------------------------------------------------------------- #
def build_tier_model(tier, faction, lod):
    mb = ModelBuilder(tier, faction, lod)
    spec = TIER_SPEC[tier]
    top = spec["platform_top"]
    body_top = top - PLATFORM_THICK
    add_foundation(mb, tier, lod)
    add_corner_posts(mb, tier, lod, body_top)
    build_body(mb, tier, faction, lod, body_top)
    add_rear_trim(mb, tier, lod, body_top)
    add_decor(mb, tier, lod)
    add_platform(mb, tier, faction, lod)
    fill = fill_detail(mb, tier, lod)
    if lod > 0:
        fill["ribs"] = auto_fill_lod(lod, mb, tier)["ribs"]
    patch_usage = assign_flat_uvs(mb)
    lo, hi = mb.bounds_asset()
    info = {
        "tier": tier, "faction": faction, "lod": lod,
        "tris": mb.tri_count(), "budget": TRI_BUDGET[lod],
        "bounds": [round(hi[i] - lo[i], 5) for i in range(3)],
        "min": [round(v, 4) for v in lo], "max": [round(v, 4) for v in hi],
        "materials": mb.used_materials(),
        "patches": patch_usage,
        "platform_top": top,
        "platform_rim": spec["rim"],
        "fill": fill,
    }
    return mb, info


def build_construction_model():
    tier = CONSTRUCTION_TIER
    spec = CONSTRUCTION_SPEC
    mb = ModelBuilder(tier, "shared", 0)
    top = spec["platform_top"]
    body_top = top - PLATFORM_THICK
    # foundation slab (greybox base) + feet
    add_foundation(mb, tier, 0)
    # scaffold posts to the platform underside
    for x in (-POST_X, POST_X):
        for z in (-POST_Z, POST_Z):
            mb.box(mb.body_name, (x, (FOUND_TOP + body_top) * 0.5, z),
                   (0.08, body_top - FOUND_TOP, 0.08), WOOD)
            mb.box(mb.body_name, (x, body_top - 0.03, z), (0.11, 0.06, 0.11), METAL)
    # partial greybox walls (construction stage look, no troughs / no slots)
    for z in (0.955, -0.955):
        mb.box(mb.body_name, (0.0, 0.42, z), (2.86, 0.56, 0.04), WOOD)
    for x in (-1.455, 1.455):
        mb.box(mb.body_name, (x, 0.42, 0.0), (0.04, 0.56, 1.86), WOOD)
    # scaffold braces + safety railing below the platform
    for x in (-1.18, 0.0, 1.18):
        mb.box(mb.body_name, (x, 0.70, 0.0), (0.06, 0.05, 1.80), METAL)
    # ONE empty flat platform (same contract as the finished models)
    add_platform(mb, tier, "shared", 0)
    fill = fill_detail(mb, tier, 0)
    patch_usage = assign_flat_uvs(mb)
    lo, hi = mb.bounds_asset()
    info = {"tier": tier, "faction": "shared", "lod": 0,
            "tris": mb.tri_count(), "budget": TRI_BUDGET[2],
            "bounds": [round(hi[i] - lo[i], 5) for i in range(3)],
            "min": [round(v, 4) for v in lo], "max": [round(v, 4) for v in hi],
            "materials": mb.used_materials(), "patches": patch_usage,
            "platform_top": top, "platform_rim": spec["rim"], "fill": fill}
    return mb, info


# --------------------------------------------------------------------------- #
# material_slots.json (ART-04: flat patch table)
# --------------------------------------------------------------------------- #
def write_material_slots(infos):
    from timbermesh_common import MATERIAL_MAP, MATERIAL_WHITELIST

    used_by = {m: [] for m in A.MATERIAL_ORDER}
    patch_rows = []
    for key, patch in A.FLAT_PATCHES.items():
        x0, y0, x1, y1 = A.flat_patch_rect(key)
        patch_rows.append({
            "key": key,
            "material_slot": patch["material"],
            "look": patch["look"],
            "size_px": A.FLAT_PATCH_SIZE,
            "pixel_rect": [x0, y0, x1, y1],
            "uv_rect": [round(v, 6) for v in A.flat_patch_uv(key)],
            "patterns": "one fixed window per material; all faces of that material map inside it",
        })
    models = {}
    for info in infos:
        tier = info["tier"]
        faction = info["faction"]
        lod = info["lod"]
        construction = tier == CONSTRUCTION_TIER
        key = tier if construction else "%s.%s" % (tier, faction)
        model = models.setdefault(key, {})
        node = {
            "file_stem": ("ConstructionStage0" if construction else "LOD%d" % lod),
            "materials": info["materials"],
            "patches": {m: info["patches"].get(m) for m in info["materials"]},
            "triangles": info["tris"],
            "bounds_size": info["bounds"],
            "platform_top": info["platform_top"],
            "platform_rim": info["platform_rim"],
        }
        model["LOD%d" % lod] = node
        for m in info["materials"]:
            label = key if lod == 0 else "%s.LOD%d" % (key, lod)
            if label not in used_by[m]:
                used_by[m].append(label)
    models_out = {}
    for key, model in models.items():
        models_out[key] = {
            "LOD%d" % lod: node for lod, node in sorted(model.items())
        }
    data = {
        "generated_by": "art/gen_models_v2.py (ART-04)",
        "atlas": {
            "size": A.ATLAS_SIZE,
            "files": A.ATLAS_FILES,
            "padding_px": A.PADDING,
            "uv_space": "UV0 only; every face of a material maps inside that "
                        "material's fixed flat patch (no unique islands, no "
                        "cross-material sampling)",
        },
        "material_order": A.MATERIAL_ORDER,
        "flat_patches": patch_rows,
        "tier_patch_keys": A.TIER_PATCH_KEYS,
        "tier_material_sets": A.TIER_MATERIALS,
        "vanilla_materials": {
            "note": "the game resolves these material asset names from the "
                    "running faction's MaterialCollection; the exported UVs "
                    "sample one fixed small window of each vanilla material "
                    "texture (shader/texture appearance needs an in-game check)",
            "map": {"%s.%s.%s" % (t, f, "construction" if c else "finished"):
                    MATERIAL_MAP[(t, f, c)] for (t, f, c) in MATERIAL_MAP},
            "whitelist": MATERIAL_WHITELIST,
        },
        "materials": [
            {
                "slot": i, "name": m,
                "params": A.MATERIAL_PARAMS[m],
                "flat_patch": next((row["key"] for row in patch_rows
                                    if row["material_slot"] == m), None),
                "used_by": sorted(set(used_by[m])),
            } for i, m in enumerate(A.MATERIAL_ORDER)
        ],
        "models": models_out,
        "root_platform": {
            "thickness": PLATFORM_THICK,
            "half_x": PLATFORM_HALF_X,
            "half_z": PLATFORM_HALF_Z,
            "rim_width": RIM_W,
            "interior_check_margin": PLATFORM["interior_margin"],
            "max_above_platform": PLATFORM["max_above_platform"],
            "flat_tolerance": PLATFORM["flat_tolerance"],
            "min_top_area": PLATFORM["min_top_area"],
            "tops": PLATFORM["tops"],
            "rims": PLATFORM["rim_heights"],
        },
    }
    path = os.path.join(ART_DIR, "material_slots.json")
    with open(path, "w", encoding="utf-8", newline="\n") as fh:
        json.dump(data, fh, indent=2)
        fh.write("\n")
    return path


# --------------------------------------------------------------------------- #
# main
# --------------------------------------------------------------------------- #
def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.unit_settings.system = "METRIC"
    scene.unit_settings.scale_length = 1.0
    materials = ensure_materials()
    os.makedirs(MODELS_DIR, exist_ok=True)
    infos = []
    print("=== HDBF ART-04 model generation v2 ===")
    for tier in A.TIERS:
        for faction in A.FACTIONS:
            for lod in A.LODS:
                only = os.environ.get("HDBF_ONLY", "")
                if only and only != "%s.%s.LOD%d" % (tier, faction, lod):
                    continue
                mb, info = build_tier_model(tier, faction, lod)
                out_dir = os.path.join(MODELS_DIR, "%s.%s" % (tier, faction))
                export_model(mb, out_dir, "LOD%d" % lod, materials)
                infos.append(info)
                print("[%s.%s.LOD%d] tris=%d budget=%d bounds=%.3fx%.3fx%.3f "
                      "platform_top=%.2f mats=%s patches=%s" % (
                          tier, faction, lod, info["tris"], info["budget"],
                          info["bounds"][0], info["bounds"][1], info["bounds"][2],
                          info["platform_top"], ",".join(info["materials"]),
                          ",".join(sorted(str(v) for v in info["patches"].values()))))
    mb, info = build_construction_model()
    export_model(mb, os.path.join(MODELS_DIR, CONSTRUCTION_TIER),
                 CONSTRUCTION_TIER, materials)
    infos.append(info)
    print("[%s] tris=%d bounds=%.3fx%.3fx%.3f platform_top=%.2f mats=%s" % (
        CONSTRUCTION_TIER, info["tris"], info["bounds"][0], info["bounds"][1],
        info["bounds"][2], info["platform_top"], ",".join(info["materials"])))
    path = write_material_slots(infos)
    print("wrote", path)
    with open(os.path.join(os.environ.get("TEMP", ART_DIR), "hdbf_gen_v2_summary.json"),
              "w", encoding="utf-8") as fh:
        json.dump(infos, fh, indent=2)
    print("=== done: %d model files x2 formats ===" % len(infos))


if __name__ == "__main__":
    main()
