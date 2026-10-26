"""HDBF Box Farm — procedural 3D model generator (Blender 4.2.1, headless).

**ART-04 revision (2026-10-25 user instruction).**  The six raised crop troughs
(#Slot#Crop_0..5), their soil/rim geometry, the roof vents/awning/gable/gantry
and every other roof object are GONE.  Each model now has:

  * ONE EMPTY FLAT ROOF PLATFORM — a single flat walking surface at
    ``ROOF_PLATFORM_TOP`` (0.96) with a thin 0.04 perimeter curb (apex y = 1.0,
    which is also the 3x1x2 envelope apex).  Nothing stands on the platform.
    Tiers differ by platform PLAN SIZE (2.05x1.25 Basic .. 2.96x1.96 Top), which
    is what keeps the four 50%-camera silhouettes distinguishable (C9).
  * No crop / soil / plant geometry at all (the game-side crop visuals stay
    hidden by BUILD-05; the model never shows crops).
  * Only the 3 ``#Port#Power/#Port#Pipe/#Port#Logistics`` marker empties.

**ART-04 materials.**  Every face of one material is mapped onto ONE fixed
``64x64..384x384`` texel flat patch of that material (``art/material_patches.py``)
instead of the old unique-per-face atlas packing.  Wood / metal / glass / roof /
plaster therefore read as a consistent colour+texture instead of a patchwork.
Geometric detail (plank seams, frames, rivets, feet, door frame) carries the
shape detail that the texture no longer has to.  Per-face vertex colours are
authored too (whether the game shader uses them is unknown -> in-game check).

Generates (deterministic, re-runnable):

    art/models/<Tier>.<Faction>/LOD0.fbx, LOD1.fbx, LOD2.fbx  (+ .glb)
    art/models/ConstructionStage0/ConstructionStage0.fbx      (+ .glb)
    art/material_slots.json

Run:
    $env:BLENDER_USER_CONFIG = "$env:TEMP\\blender-config"
    blender.exe -b --factory-startup -noaudio --python art/gen_models.py

Authoring space: Blender Z-up, building front = +Y.
Export: Blender standard axis conversion => asset space (Unity/Y-up, front -Z):
    asset = ( blender.x, blender.z, -blender.y )
so the requirement-13.6 port coordinates in atlas_layout.PORT_SPECS are exact.
"""

from __future__ import annotations

import json
import math
import os
import sys
import time

import bpy
import bmesh
from mathutils import Matrix, Vector

ART_DIR = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(ART_DIR)
if ART_DIR not in sys.path:
    sys.path.insert(0, ART_DIR)

import atlas_layout as A          # noqa: E402
import material_patches as MP     # noqa: E402
import timbermesh_common as TC    # noqa: E402

MODELS_DIR = os.path.join(ART_DIR, "models")

# --------------------------------------------------------------------------- #
# vertical / plan envelope (asset space: X width, Y up, Z depth, front -Z)
# --------------------------------------------------------------------------- #
FOUND_TOP = 0.10
WALL_TOP = 0.76
DECK_TOP = 0.80
BODY_X = 1.46           # outer face of side wall
BODY_Z = 0.96           # outer face of front/back wall
WALL_T = 0.04
POST_X = 1.40
POST_Z = 0.90

PLATFORM_TOP = A.ROOF_PLATFORM_TOP            # 0.96 (walking surface)
PLATFORM_THICK = A.ROOF_PLATFORM_THICKNESS    # 0.05 -> underside 0.91
PLATFORM_UNDER = PLATFORM_TOP - PLATFORM_THICK
CURB_H = A.ROOF_CURB_H                        # 0.04 -> apex 1.00
CURB_W = A.ROOF_CURB_W

WOOD = "M_HDBF_Wood"
METAL = "M_HDBF_Metal"
GLASS = "M_HDBF_Glass"
FABRIC = "M_HDBF_Fabric"

#: per-face vertex colour (ART-04 attempt; the game shader may ignore it, an
#: in-game check is required — the export pipeline reports whether it survived).
FACE_COLORS = {
    WOOD: (1.00, 0.94, 0.86, 1.0),
    METAL: (0.90, 0.93, 0.97, 1.0),
    GLASS: (0.80, 0.94, 1.00, 1.0),
    FABRIC: (1.00, 0.96, 0.82, 1.0),
}

CONSTRUCTION_TIER = "ConstructionStage0"


# --------------------------------------------------------------------------- #
# tier style (materials + front treatment + platform plan)
# --------------------------------------------------------------------------- #
def tier_style(tier):
    """Material slots / front treatment / platform plan for one finished tier.

    Every slot set stays exactly equal to ``atlas_layout.TIER_MATERIALS`` so the
    frozen 1->2->3->4 material progression is preserved:
      Basic        wood + roof (thatch/plank)            (2)
      Intermediate planks + plaster + glass              (3)
      Advanced     white/grey timber + metal + glass + detail plates (4)
      Top          metal + riveted plate + glass + timber deck      (4)
    """
    sx, sz = A.roof_platform(tier)
    if tier == "Basic":
        return dict(tier=tier, plan=(sx, sz), wall=WOOD, frame=WOOD,
                    foundation=WOOD, roof=FABRIC, deck=WOOD, front="open",
                    glass=0, panel=None, door=None)
    if tier == "Intermediate":
        return dict(tier=tier, plan=(sx, sz), wall=WOOD, frame=WOOD,
                    foundation=WOOD, roof=WOOD, deck=WOOD, front="door",
                    glass=3, panel=FABRIC, door=WOOD)
    if tier == "Advanced":
        return dict(tier=tier, plan=(sx, sz), wall=WOOD, frame=METAL,
                    foundation=WOOD, roof=METAL, deck=WOOD, front="door",
                    glass=6, panel=FABRIC, door=METAL)
    return dict(tier=tier, plan=(sx, sz), wall=METAL, frame=METAL,
                foundation=METAL, roof=FABRIC, deck=WOOD, front="door",
                glass=8, panel=FABRIC, door=FABRIC)


# --------------------------------------------------------------------------- #
# small deterministic helpers
# --------------------------------------------------------------------------- #
def h01(*args):
    """Stable 0..1 pseudo-random from a tuple (no RNG state, deterministic)."""
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
# geometry builder
# --------------------------------------------------------------------------- #
BOX_FACES = [
    (0, 3, 2, 1),  # -Z
    (4, 5, 6, 7),  # +Z
    (0, 1, 5, 4),  # -Y
    (3, 7, 6, 2),  # +Y
    (0, 4, 7, 3),  # -X
    (1, 2, 6, 5),  # +X
]


class ModelBuilder:
    """Accumulates polygons per logical object, all in blender world space."""

    def __init__(self, tier, faction, lod):
        self.tier = tier
        self.faction = faction
        self.lod = lod
        self.objects = {}       # object name -> list of face dicts
        self.anchors = []       # rivet anchors: dict(pos, normal, size, material)

    # ---- low level --------------------------------------------------------
    def add_poly(self, obj, verts_bl, material):
        if len(verts_bl) < 3:
            return
        self.objects.setdefault(obj, []).append(
            {"verts": [tuple(v) for v in verts_bl], "material": material})

    def _append_bmesh(self, obj, bm, material):
        for f in bm.faces:
            self.add_poly(obj, [tuple(v.co) for v in f.verts], material)
        bm.free()

    # ---- primitives (all inputs in asset space) --------------------------
    def box(self, obj, center, size, material, bevel=0.0, segments=1,
            anchor=None, skip=()):
        """Axis-aligned box.  `skip` drops hidden faces (indices into BOX_FACES)."""
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

    def cyl(self, obj, center, axis, radius, length, segments, material,
            anchor=None):
        cb = to_bl(center)
        if axis == "y":
            rot = Matrix.Identity(4)
        elif axis == "x":
            rot = Matrix.Rotation(math.radians(90.0), 4, "Y")
        else:  # asset depth axis -> blender y
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
# geometry: foundation / frame / deck
# --------------------------------------------------------------------------- #
def add_foundation(mb, style, lod):
    body = mb.body_name
    # full 3x2 base slab; bottom face is against the ground -> culled
    mb.box(body, (0.0, FOUND_TOP * 0.5, 0.0), (3.0, FOUND_TOP, 2.0),
           style["foundation"], skip={0})
    # perimeter trim line at y 0.10..0.14
    for (x, z, sx, sz) in ((0.0, 0.955, 2.96, 0.05), (0.0, -0.955, 2.96, 0.05),
                           (1.455, 0.0, 0.05, 1.86), (-1.455, 0.0, 0.05, 1.86)):
        mb.box(body, (x, 0.12, z), (sx, 0.04, sz), style["frame"], skip={0})
    if lod == 0:
        # 4 feet / base pads (腳座) on the foundation corners
        for x in (-1.36, 1.36):
            for z in (-0.86, 0.86):
                mb.box(body, (x, 0.13, z), (0.20, 0.06, 0.20), style["frame"])


def add_corner_frame(mb, style, lod, bevel):
    body = mb.body_name
    frame = style["frame"]
    if lod <= 1:
        for x in (-POST_X, POST_X):
            for z in (-POST_Z, POST_Z):
                mb.box(body, (x, (FOUND_TOP + WALL_TOP) * 0.5, z),
                       (0.09, WALL_TOP - FOUND_TOP, 0.09), frame, bevel=bevel,
                       anchor=((1 if x > 0 else -1, 0, 0), (0.03, 0.03, 0.03)))
        # top perimeter beams
        for (x, z, sx, sz) in ((0.0, POST_Z, 2.86, 0.08),
                               (0.0, -POST_Z, 2.86, 0.08),
                               (POST_X, 0.0, 0.08, 1.78),
                               (-POST_X, 0.0, 0.08, 1.78)):
            mb.box(body, (x, 0.74, z), (sx, 0.08, sz), frame, bevel=bevel,
                   anchor=((0, 1, 0), (0.04, 0.03, 0.04)))
    else:  # LOD2 massing only
        for x in (-POST_X, POST_X):
            for z in (-POST_Z, POST_Z):
                mb.box(body, (x, (FOUND_TOP + WALL_TOP) * 0.5, z),
                       (0.09, WALL_TOP - FOUND_TOP, 0.09), frame)
        for (x, z, sx, sz) in ((0.0, POST_Z, 2.86, 0.08),
                               (0.0, -POST_Z, 2.86, 0.08),
                               (POST_X, 0.0, 0.08, 1.78),
                               (-POST_X, 0.0, 0.08, 1.78)):
            mb.box(body, (x, 0.74, z), (sx, 0.08, sz), frame)
    # deck slab: top face y = 0.80 (bottom hidden)
    mb.box(body, (0.0, 0.77, 0.0), (2.84, 0.06, 1.80), style["deck"], skip={0})


def add_deck_details(mb, style, lod):
    """Deck plank seams (板縫): geometric detail, never above the platform."""
    body = mb.body_name
    if lod == 0:
        for i in range(7):
            z = -0.72 + i * 0.24
            mb.box(body, (0.0, 0.804, z), (2.64, 0.008, 0.05),
                   style["deck"], skip={0})
        for i in range(3):
            x = -0.88 + i * 0.88
            mb.box(body, (x, 0.802, 0.0), (0.06, 0.008, 1.62),
                   style["deck"], skip={0})
    elif lod == 1:
        for i in range(3):
            z = -0.60 + i * 0.60
            mb.box(body, (0.0, 0.804, z), (2.56, 0.008, 0.05),
                   style["deck"], skip={0})


# --------------------------------------------------------------------------- #
# geometry: walls / door / windows / panels
# --------------------------------------------------------------------------- #
def add_walls(mb, style, lod, bevel):
    body = mb.body_name
    wall = style["wall"]
    frame = style["frame"]
    wall_h = WALL_TOP - FOUND_TOP
    wall_mid = (FOUND_TOP + WALL_TOP) * 0.5
    z_back = BODY_Z - WALL_T * 0.5
    z_front = -BODY_Z + WALL_T * 0.5
    x_side = BODY_X - WALL_T * 0.5

    # back wall (+Z) and side walls
    mb.box(body, (0.0, wall_mid, z_back), (2.92, wall_h, WALL_T), wall,
           bevel=bevel if lod == 0 else 0.0)
    for sgn in (-1, 1):
        mb.box(body, (sgn * x_side, wall_mid, 0.0), (WALL_T, wall_h, 1.88), wall,
               bevel=bevel if lod == 0 else 0.0)

    # front: Basic is fully open (posts + header); other tiers have the door bay
    if style["front"] == "open":
        for sgn in (-1, 1):
            mb.box(body, (sgn * 0.61, (FOUND_TOP + 0.74) * 0.5, z_front),
                   (0.10, 0.64, WALL_T), wall,
                   bevel=bevel if lod == 0 else 0.0)
        mb.box(body, (0.0, 0.785, z_front), (1.34, 0.09, WALL_T), wall)
        mb.box(body, (0.0, FOUND_TOP + 0.02, -0.955), (1.36, 0.04, 0.07), frame)
    else:
        for sgn in (-1, 1):
            mb.box(body, (sgn * 0.735, wall_mid, z_front),
                   (0.53, wall_h, WALL_T), wall,
                   bevel=bevel if lod == 0 else 0.0)
        mb.box(body, (0.0, 0.72, z_front), (1.20, 0.08, WALL_T), wall)
        # door frame (門框)
        for sgn in (-1, 1):
            mb.box(body, (sgn * 0.58, 0.43, -0.935), (0.07, 0.64, 0.07), frame,
                   bevel=bevel if lod == 0 else 0.0)
        mb.box(body, (0.0, 0.745, -0.935), (1.23, 0.06, 0.07), frame,
               bevel=bevel if lod == 0 else 0.0)

    # wall seams (板縫): thin proud strips; LOD0 full, LOD1 reduced
    if lod == 0:
        for i in range(8):
            x = -1.28 + i * 0.365
            mb.box(body, (x, wall_mid, z_back + WALL_T * 0.5 + 0.006),
                   (0.05, wall_h * 0.92, 0.012), wall, skip={3})
        for sgn in (-1, 1):
            for i in range(5):
                z = -0.80 + i * 0.40
                mb.box(body, (sgn * (x_side + WALL_T * 0.5 + 0.005), wall_mid, z),
                       (0.010, wall_h * 0.92, 0.05), wall, skip={3 if sgn < 0 else 2})
    elif lod == 1:
        for i in range(4):
            x = -1.2 + i * 0.8
            mb.box(body, (x, wall_mid, z_back + WALL_T * 0.5 + 0.006),
                   (0.05, wall_h * 0.90, 0.010), wall, skip={3})


def add_door(mb, style, lod, bevel):
    body = mb.body_name
    if style["front"] != "door":
        return
    mat = style["door"]
    z = -0.90
    if lod == 0:
        if mat == WOOD:
            for i in range(4):
                x = -0.39 + i * 0.26
                mb.box(body, (x, 0.43, z), (0.24, 0.58, 0.035), mat,
                       bevel=bevel if bevel else 0.0)
        else:  # metal panel / riveted shutter
            for i in range(6):
                x = -0.46 + i * 0.184
                mb.box(body, (x, 0.43, z), (0.17, 0.56, 0.022), mat)
        mb.box(body, (0.30, 0.45, z + 0.035), (0.10, 0.07, 0.05), style["frame"])
    else:
        mb.box(body, (0.0, 0.43, z), (1.02, 0.56, 0.03), mat)


def add_windows(mb, style, tier, lod):
    """Glass band(s); the glass count grows with the tier."""
    body = mb.body_name
    n = style["glass"]
    if not n:
        return
    frame = style["frame"]
    back_xs = (-0.85, 0.0, 0.85) if n <= 3 else (-1.05, -0.35, 0.35, 1.05)
    for x in back_xs:
        mb.box(body, (x, 0.555, 0.952), (0.50, 0.215, 0.012), GLASS)
        if lod == 0:
            for dy in (0.125, -0.125):
                mb.box(body, (x, 0.555 + dy, 0.956), (0.55, 0.028, 0.018), frame)
    if tier in ("Advanced", "Top") and lod <= 1:
        for sgn in (-1, 1):
            for y in (0.44, 0.66):
                mb.box(body, (sgn * 1.452, y, 0.28), (0.012, 0.17, 0.42), GLASS)
                if lod == 0:
                    for dz in (0.215, -0.215):
                        mb.box(body, (sgn * 1.456, y, 0.28 + dz),
                               (0.018, 0.20, 0.028), frame)


def add_panels(mb, style, tier, lod):
    """Plaster (Intermediate) or riveted detail plates (Advanced/Top)."""
    body = mb.body_name
    panel = style["panel"]
    if not panel:
        return
    if tier == "Intermediate":
        # plaster panel band on the back / side walls
        mb.box(body, (0.0, 0.30, 0.955), (2.80, 0.24, 0.014), panel)
        for sgn in (-1, 1):
            mb.box(body, (sgn * 1.452, 0.30, -0.20), (0.014, 0.24, 1.10), panel)
        if lod == 0:
            for i in range(5):
                x = -1.10 + i * 0.55
                mb.box(body, (x, 0.30, 0.962), (0.03, 0.24, 0.012), panel)
    else:
        # riveted plate band (鉚接板) above the wall line + on the side walls
        mb.box(body, (0.0, 0.815, 0.955), (2.84, 0.115, 0.022), panel)
        for sgn in (-1, 1):
            mb.box(body, (sgn * 1.452, 0.815, -0.30), (0.022, 0.115, 1.20), panel)
        if lod == 0:
            for i in range(11):
                x = -1.25 + i * 0.25
                mb.box(body, (x, 0.815, 0.968), (0.035, 0.035, 0.014), frame_col(style))
            for sgn in (-1, 1):
                for i in range(5):
                    z = -0.82 + i * 0.26
                    mb.box(body, (sgn * 1.466, 0.815, z),
                           (0.014, 0.035, 0.035), frame_col(style))


def frame_col(style):
    return style["frame"]


def add_surface_details(mb, style, tier, lod):
    """Plank seams / rivet rows / brackets / sills — ART-04 geometric detail.

    The flat-patch texture no longer carries panel lines, so the shape detail is
    authored as geometry.  Everything here stays below the platform top (the
    only exception are the curb-side rivets, which sit on the platform fascia at
    y <= 0.95, i.e. still below the walking surface).
    """
    body = mb.body_name
    if lod > 1:
        return
    wall = style["wall"]
    frame = style["frame"]
    sx, sz = style["plan"]
    wall_mid = 0.43

    def rivet(x, y, z, rx=0.030, ry=0.030, rz=0.014):
        mb.box(body, (x, y, z), (rx, ry, rz), frame)

    # vertical plank seams: back wall + both side walls
    for i in range(13):
        x = -1.30 + i * (2.60 / 12.0)
        mb.box(body, (x, wall_mid, 0.963), (0.014, 0.60, 0.010), wall)
    for sgn in (-1, 1):
        for i in range(8):
            z = -0.84 + i * (1.68 / 7.0)
            mb.box(body, (sgn * 1.463, wall_mid, z), (0.010, 0.60, 0.014), wall)
    # horizontal trim band at the wall top
    mb.box(body, (0.0, 0.735, 0.963), (2.92, 0.035, 0.020), frame)
    for sgn in (-1, 1):
        mb.box(body, (sgn * 1.463, 0.735, 0.0), (0.020, 0.035, 1.88), frame)
    if lod == 1:
        return

    # foundation trim rivet rows (front + back)
    for i in range(13):
        x = -1.32 + i * (2.64 / 12.0)
        rivet(x, 0.12, 0.982)
        rivet(x, 0.12, -0.982)
    # wall rivet rows along the top band
    for i in range(11):
        x = -1.30 + i * (2.60 / 10.0)
        rivet(x, 0.735, 0.984, 0.026, 0.026, 0.012)
    for sgn in (-1, 1):
        for i in range(7):
            z = -0.84 + i * (1.68 / 6.0)
            rivet(sgn * 1.486, 0.735, z, 0.012, 0.026, 0.026)
    # corner-post brackets (top + bottom)
    for x in (-POST_X, POST_X):
        for z in (-POST_Z, POST_Z):
            for yy in (0.135, 0.705):
                mb.box(body, (x, yy, z), (0.13, 0.055, 0.13), frame, bevel=0.004)
            rivet(x + (0.05 if x > 0 else -0.05), 0.43, z, 0.024, 0.024, 0.024)
    # window sills
    if style["glass"]:
        back_xs = (-0.85, 0.0, 0.85) if style["glass"] <= 3 else \
            (-1.05, -0.35, 0.35, 1.05)
        for x in back_xs:
            mb.box(body, (x, 0.442, 0.962), (0.56, 0.022, 0.030), frame)
            for dx in (-0.29, 0.29):
                rivet(x + dx, 0.555, 0.962, 0.014, 0.026, 0.014)
        if tier in ("Advanced", "Top"):
            for sgn in (-1, 1):
                for y in (0.44, 0.66):
                    rivet(sgn * 1.463, y, 0.062, 0.014, 0.026, 0.014)
                    rivet(sgn * 1.463, y, 0.498, 0.014, 0.026, 0.014)
    # platform curb / fascia rivet rows (below the platform top)
    for i in range(11):
        t = -1.0 + 2.0 * i / 10.0
        rivet(t * (sx * 0.5 - 0.08), 0.935, sz * 0.5 + 0.004, 0.03, 0.03, 0.012)
        rivet(t * (sx * 0.5 - 0.08), 0.935, -(sz * 0.5) - 0.004, 0.03, 0.03, 0.012)
    for i in range(9):
        t = -1.0 + 2.0 * i / 8.0
        rivet(sx * 0.5 + 0.004, 0.935, t * (sz * 0.5 - 0.08), 0.012, 0.03, 0.03)
        rivet(-(sx * 0.5) - 0.004, 0.935, t * (sz * 0.5 - 0.08), 0.012, 0.03, 0.03)
    # platform underside joists
    for i in range(5):
        x = -0.9 + i * 0.45
        mb.box(body, (x, PLATFORM_UNDER - 0.020, 0.0), (0.05, 0.035, sz - 0.10),
               frame)
    # platform support diagonal braces
    for sgn in (-1, 1):
        mb.box(body, (sgn * (sx * 0.5 - 0.20), 0.855, sgn * (sz * 0.5 - 0.16)),
               (0.04, 0.10, 0.04), frame, bevel=0.004)


def add_ports_geometry(mb, style, tier, lod):
    body = mb.body_name
    frame = style["frame"]
    metal = tier in ("Advanced", "Top")

    # ---------------- pipe port, left side x = -1.46 (contract -1.50) ------
    cover = not metal
    mb.box(body, (-1.462, 0.40, 0.0), (0.020, 0.26, 0.26),
           WOOD if cover else METAL)
    if lod == 0:
        if cover:
            mb.box(body, (-1.474, 0.40, 0.0), (0.010, 0.20, 0.20), WOOD)
        else:
            mb.cyl(body, (-1.475, 0.40, 0.0), "x", 0.085, 0.050, 12, METAL,
                   anchor=None)
            mb.cyl(body, (-1.462, 0.40, 0.0), "x", 0.060, 0.020, 10, METAL)

    # ---------------- logistics port, back z = +0.96 ----------------------
    visible = tier != "Basic"
    lx, ly = -1.0, 0.35
    for sgn in (-1, 1):
        mb.box(body, (lx + sgn * 0.43, ly, 0.955), (0.06, 0.46, 0.07), frame)
        mb.box(body, (lx, ly + sgn * 0.23, 0.955), (0.86, 0.05, 0.07), frame)
    if visible:
        mb.box(body, (lx, ly, 0.912), (0.78, 0.38, 0.020),
               METAL if metal else frame)
        if lod == 0:
            for i in range(4):
                mb.box(body, (lx, 0.17 + i * 0.11, 0.878), (0.72, 0.055, 0.014),
                       frame)
    else:
        mb.box(body, (lx, ly, 0.952), (0.84, 0.40, 0.020), WOOD)
        mb.box(body, (lx, ly, 0.958), (0.30, 0.30, 0.012), frame)

    # ---------------- power port, back z = +0.96, y = 0.85 ----------------
    wx, wy = 1.0, 0.85
    mb.box(body, (wx, 0.84, 0.962), (0.18, 0.13, 0.030),
           METAL if metal else frame,
           anchor=((0, 0, 1), (0.03, 0.03, 0.02)))
    if lod == 0:
        mb.box(body, (wx, 0.84, 0.978), (0.10, 0.08, 0.014), frame)
        if tier == "Top":
            mb.cyl(body, (wx + 0.06, 0.84, 0.98), "z", 0.020, 0.030, 8, METAL)
    # service mast / backplate behind the junction box (below the platform)
    mb.box(body, (wx, 0.44, 0.968), (0.12, 0.72, 0.016), frame)


# --------------------------------------------------------------------------- #
# geometry: the ONE empty flat roof platform (+ thin curb, + supports)
# --------------------------------------------------------------------------- #
def add_roof_platform(mb, style, tier, lod, bevel):
    """Single flat platform: uniform top 0.96, thin curb apex 1.00, NOTHING on top."""
    body = mb.body_name
    sx, sz = style["plan"]
    roof = style["roof"]
    frame = style["frame"]

    # platform slab: top face y = 0.96, underside y = 0.91
    mb.box(body, (0.0, (PLATFORM_TOP + PLATFORM_UNDER) * 0.5, 0.0),
           (sx, PLATFORM_THICK, sz), roof,
           bevel=bevel if lod == 0 else 0.0)

    # thin perimeter curb (top = 1.00 = envelope apex); the only thing above 0.96
    cy = (PLATFORM_TOP + 1.0) * 0.5
    mb.box(body, (0.0, cy, sz * 0.5 - CURB_W * 0.5), (sx, CURB_H, CURB_W), frame)
    mb.box(body, (0.0, cy, -(sz * 0.5 - CURB_W * 0.5)), (sx, CURB_H, CURB_W), frame)
    mb.box(body, (sx * 0.5 - CURB_W * 0.5, cy, 0.0),
           (CURB_W, CURB_H, sz - 2.0 * CURB_W), frame)
    mb.box(body, (-(sx * 0.5 - CURB_W * 0.5), cy, 0.0),
           (CURB_W, CURB_H, sz - 2.0 * CURB_W), frame)

    # platform fascia band (below the top, still <= platform top)
    if lod == 0:
        fy = PLATFORM_UNDER + PLATFORM_THICK * 0.5
        for sgn in (-1, 1):
            mb.box(body, (0.0, fy, sgn * (sz * 0.5 + 0.004)),
                   (sx, 0.020, 0.008), frame)
            mb.box(body, (sgn * (sx * 0.5 + 0.004), fy, 0.0),
                   (0.008, 0.020, sz), frame)

    # supports between deck (0.80) and platform underside (0.91)
    sup_y = (DECK_TOP + PLATFORM_UNDER) * 0.5
    sup_h = PLATFORM_UNDER - DECK_TOP
    for x in (-(sx * 0.5 - 0.07), sx * 0.5 - 0.07):
        for z in (-(sz * 0.5 - 0.07), sz * 0.5 - 0.07):
            mb.box(body, (x, sup_y, z), (0.07, sup_h, 0.07), frame,
                   bevel=bevel if lod == 0 else 0.0,
                   anchor=((1 if x > 0 else -1, 0, 0), (0.025, 0.025, 0.025)))
    if lod <= 1:
        n = 5 if lod == 0 else 2
        for i in range(n):
            t = -1.0 + 2.0 * i / max(1, n - 1)
            mb.box(body, (t * (sx * 0.5 - 0.10), sup_y, sz * 0.5 - 0.07),
                   (0.05, sup_h, 0.05), frame)
            mb.box(body, (t * (sx * 0.5 - 0.10), sup_y, -(sz * 0.5 - 0.07)),
                   (0.05, sup_h, 0.05), frame)
            if lod == 0:
                mb.box(body, (sx * 0.5 - 0.07, sup_y, t * (sz * 0.5 - 0.10)),
                       (0.05, sup_h, 0.05), frame)
                mb.box(body, (-(sx * 0.5 - 0.07), sup_y, t * (sz * 0.5 - 0.10)),
                       (0.05, sup_h, 0.05), frame)
    # perimeter beams just under the platform edge
    by = PLATFORM_UNDER - 0.028
    mb.box(body, (0.0, by, sz * 0.5 - 0.075), (sx - 0.06, 0.055, 0.06), frame)
    mb.box(body, (0.0, by, -(sz * 0.5 - 0.075)), (sx - 0.06, 0.055, 0.06), frame)
    mb.box(body, (sx * 0.5 - 0.075, by, 0.0), (0.06, 0.055, sz - 0.06), frame)
    mb.box(body, (-(sx * 0.5 - 0.075), by, 0.0), (0.06, 0.055, sz - 0.06), frame)

    # rivet row on the curb outer face (below the apex, never above the top)
    if lod == 0 and tier in ("Advanced", "Top"):
        for i in range(11):
            x = -1.25 + i * 0.25
            mb.box(body, (x, (PLATFORM_TOP + 1.0) * 0.5,
                          sz * 0.5 + 0.004), (0.030, 0.030, 0.010), METAL)


# --------------------------------------------------------------------------- #
# decoration (small, silhouette-neutral, all below the platform underside)
# --------------------------------------------------------------------------- #
def add_decor(mb, tier, style, lod):
    body = mb.body_name
    if lod != 0:
        return
    if tier == "Basic":
        # drip barrel + crate in the front working corner
        mb.cyl(body, (-1.18, 0.24, -0.62), "y", 0.105, 0.28, 10, WOOD)
        for yy in (0.145, 0.335):
            mb.cyl(body, (-1.18, yy, -0.62), "y", 0.112, 0.018, 10, WOOD)
        mb.box(body, (-0.80, 0.21, -0.80), (0.22, 0.22, 0.22), WOOD, bevel=0.010)
        mb.box(body, (-0.80, 0.33, -0.80), (0.24, 0.02, 0.24), WOOD)
    elif tier == "Intermediate":
        mb.box(body, (0.95, 0.25, -0.78), (0.50, 0.18, 0.30), WOOD)
        for i in range(3):
            mb.box(body, (0.95, 0.17 + i * 0.06, -0.78), (0.52, 0.02, 0.32), WOOD)
        for sgn in (-1, 1):
            mb.box(body, (sgn * 1.452, 0.20, -0.62), (0.014, 0.18, 0.24), FABRIC)
    elif tier == "Advanced":
        mb.box(body, (1.02, 0.22, -0.78), (0.40, 0.20, 0.30), METAL)
        mb.box(body, (1.02, 0.33, -0.78), (0.42, 0.02, 0.32), METAL)
        mb.cyl(body, (-1.18, 0.22, -0.62), "y", 0.10, 0.24, 10, METAL)
    else:  # Top
        mb.box(body, (1.02, 0.23, -0.80), (0.44, 0.22, 0.32), METAL)
        mb.box(body, (1.02, 0.35, -0.80), (0.46, 0.02, 0.34), METAL)
        mb.cyl(body, (-1.20, 0.22, -0.78), "z", 0.09, 0.16, 10, METAL)


# --------------------------------------------------------------------------- #
# auto detail: top up to the LOD triangle budget (stays below the platform)
# --------------------------------------------------------------------------- #
def auto_fill(mb, tier, style, lod):
    """Top up to the LOD triangle budget without ever touching the roof platform.

    Two deterministic supplies:
      1. beveled bolt/pad primitives on pre-registered anchors (decoration,
         capped at 25% of the budget, requirement 13.5);
      2. thin "seam" strips flush on the outer wall faces (structural panel
         detail, not decoration) if the anchors are not enough.
    Every placed primitive is checked to stay below ``PLATFORM_UNDER`` (~0.91)
    so the empty-roof contract cannot be broken by the filler.
    """
    budget = A.TRI_BUDGET[lod]
    cap = int(budget * 0.25)
    start = mb.tri_count()
    frame = style["frame"]

    def below_platform(cy, sy):
        return cy + sy * 0.5 <= PLATFORM_UNDER - 0.002

    anchors = sorted(mb.anchors, key=lambda a: (round(a["pos"][0], 4),
                                                round(a["pos"][2], 4),
                                                round(a["pos"][1], 4),
                                                a["normal"]))
    added_pads = 0
    detail_tris = 0

    def place_pad(a):
        size = a["size"]
        nx, ny, nz = a["normal"]
        x = a["pos"][0] + nx * (size[0] * 0.5 + 0.004)
        y = a["pos"][1] + ny * (size[1] * 0.5 + 0.004)
        z = a["pos"][2] + nz * (size[2] * 0.5 + 0.004)
        if not below_platform(y, size[1]):
            return None
        mb.box(mb.body_name, (x, y, z),
               (max(0.03, size[0]), max(0.03, size[1]), max(0.03, size[2])),
               a["material"], bevel=0.004, segments=1)

    for a in anchors:
        if mb.tri_count() >= budget * 0.99 or detail_tris >= cap:
            break
        before = mb.tri_count()
        place_pad(a)
        delta = mb.tri_count() - before
        if delta > 0:
            detail_tris += delta
            added_pads += 1
    # structural seam strips: flush on the four outer wall faces and the
    # foundation band; 12 tris each, below the platform by construction.
    seam_i = 0
    guard = 0
    while mb.tri_count() < budget * 0.99 and guard < 800:
        guard += 1
        i = seam_i
        seam_i += 1
        y = 0.20 + (i % 6) * 0.085
        if lod == 2:
            y = 0.22 + (i % 4) * 0.13
        y = min(y, 0.70)
        side = i % 4
        if side == 0:      # back wall
            x = -1.34 + ((i // 4) % 11) * 0.268
            mb.box(mb.body_name, (x, y, 0.966), (0.045, 0.055, 0.010), frame)
        elif side == 1:    # front wall / header band
            x = -1.34 + ((i // 4) % 11) * 0.268
            mb.box(mb.body_name, (x, y, -0.966), (0.045, 0.055, 0.010), frame)
        elif side == 2:    # left side wall
            z = -0.84 + ((i // 4) % 9) * 0.22
            mb.box(mb.body_name, (-1.466, y, z), (0.010, 0.055, 0.045), frame)
        else:              # right side wall
            z = -0.84 + ((i // 4) % 9) * 0.22
            mb.box(mb.body_name, (1.466, y, z), (0.010, 0.055, 0.045), frame)
    return {"added_pads": added_pads, "added_seams": seam_i,
            "detail_tris": detail_tris, "start_tris": start}


# --------------------------------------------------------------------------- #
# flat-patch UV mapping (ART-04)
# --------------------------------------------------------------------------- #
def face_uv_extent(verts_bl):
    """Projected 2D extent of a polygon: (du, dv, normalised 2D corners)."""
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
    du = x1 - x0
    dv = y1 - y0
    norm = []
    for (x, y) in pts:
        u = 0.5 if du < 1e-9 else (x - x0) / du
        v = 0.5 if dv < 1e-9 else (y - y0) / dv
        norm.append((u, v))
    return du, dv, norm


def patch_roles_for(tier, faction, construction=False):
    """ART-01 material slot -> flat-patch role, for one model context."""
    key = (tier, faction, construction) if not construction else \
        ("Basic", "Folktails", True)
    mapping = TC.MATERIAL_MAP[key]
    return {slot: MP.role_of(name) for slot, name in mapping.items()}


def assign_flat_patch_uvs(mb, roles):
    """Map every face of a material onto a deterministic sub-rect of its patch."""
    atlas = float(A.ATLAS_SIZE)
    for objname, faces in mb.objects.items():
        for fi, f in enumerate(faces):
            role = roles[f["material"]]
            x0, y0, x1, y1 = MP.PATCH_PIXELS[role][1]
            pw, ph = x1 - x0, y1 - y0
            du, dv, norm = face_uv_extent(f["verts"])
            w = max(2, int(round(du * MP.PATCH_TEXELS_PER_GRID)))
            h = max(2, int(round(dv * MP.PATCH_TEXELS_PER_GRID)))
            scale = min(1.0, (pw - 8.0) / max(1, w), (ph - 8.0) / max(1, h))
            w = max(2, int(round(w * scale)))
            h = max(2, int(round(h * scale)))
            key = (objname, fi, role)
            ox = 4 + int(h01(*key, "ox") * max(0, pw - w - 8))
            oy = 4 + int(h01(*key, "oy") * max(0, ph - h - 8))
            uvs = []
            for (u, v) in norm:
                px = x0 + ox + u * w
                py = y0 + oy + v * h
                uvs.append((px / atlas, 1.0 - py / atlas))
            f["uv"] = uvs


# --------------------------------------------------------------------------- #
# blender object creation / export
# --------------------------------------------------------------------------- #
MATERIALS = {}


def _load_image(relpath, colorspace):
    path = os.path.join(ART_DIR, relpath)
    if not os.path.isfile(path):
        return None
    abspath = os.path.normcase(os.path.abspath(path))
    img = None
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
        except Exception as exc:  # torn/partial PNG while another tool writes it
            print("WARNING: cannot load %s: %r" % (path, exc))
            return None
    img.colorspace_settings.name = colorspace
    for attempt in range(10):
        try:
            if img.size[0] > 0:
                _ = img.pixels[0]
                return img
        except Exception:
            pass
        time.sleep(0.2)
        try:
            img.reload()
        except Exception:
            pass
    print("WARNING: image %s has no pixel data (rerun after textures finish)" % path)
    return None


def ensure_materials():
    if MATERIALS:
        return MATERIALS
    imgs = {
        "albedo": _load_image(A.ATLAS_FILES["albedo"], "sRGB"),
        "normal": _load_image(A.ATLAS_FILES["normal"], "Non-Color"),
        "mask": _load_image(A.ATLAS_FILES["mask"], "Non-Color"),
    }
    fallback_colors = {
        WOOD: (0.36, 0.22, 0.11, 1.0),
        METAL: (0.52, 0.55, 0.58, 1.0),
        GLASS: (0.62, 0.80, 0.78, 1.0),
        FABRIC: (0.72, 0.55, 0.24, 1.0),
    }
    for name in A.MATERIAL_ORDER:
        mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
        mat.use_nodes = True
        mat.use_backface_culling = False
        nt = mat.node_tree
        nt.nodes.clear()
        out = nt.nodes.new("ShaderNodeOutputMaterial")
        out.location = (600, 0)
        bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
        bsdf.location = (250, 0)
        nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
        params = A.MATERIAL_PARAMS[name]
        bsdf.inputs["Metallic"].default_value = params["metallic"]
        bsdf.inputs["Roughness"].default_value = params["roughness"]
        bsdf.inputs["Base Color"].default_value = fallback_colors[name]
        bsdf.inputs["Alpha"].default_value = params["alpha"]
        try:
            mat.blend_method = "BLEND" if params["alpha"] < 1.0 else "OPAQUE"
            mat.surface_render_method = "BLENDED" if params["alpha"] < 1.0 else "DITHERED"
        except Exception:
            pass
        if imgs["albedo"] is not None:
            tex = nt.nodes.new("ShaderNodeTexImage")
            tex.location = (-300, 150)
            tex.image = imgs["albedo"]
            tex.interpolation = "Linear"
            nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
        if imgs["normal"] is not None:
            ntex = nt.nodes.new("ShaderNodeTexImage")
            ntex.location = (-300, -100)
            ntex.image = imgs["normal"]
            ntex.interpolation = "Linear"
            nmap = nt.nodes.new("ShaderNodeNormalMap")
            nmap.location = (0, -100)
            nt.links.new(ntex.outputs["Color"], nmap.inputs["Color"])
            nt.links.new(nmap.outputs["Normal"], bsdf.inputs["Normal"])
        if imgs["mask"] is not None:
            mtex = nt.nodes.new("ShaderNodeTexImage")
            mtex.location = (-300, -350)
            mtex.image = imgs["mask"]
            mtex.interpolation = "Linear"
            sep = nt.nodes.new("ShaderNodeSeparateColor")
            sep.location = (0, -350)
            nt.links.new(mtex.outputs["Color"], sep.inputs["Color"])
            nt.links.new(sep.outputs["Blue"], bsdf.inputs["Metallic"])
            nt.links.new(sep.outputs["Green"], bsdf.inputs["Roughness"])
        MATERIALS[name] = mat
    return MATERIALS


def build_blender_objects(mb, materials):
    """Create one Blender mesh object per logical object + the 6 port empties."""
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
        uvdata = uvl.data
        for i, loop in enumerate(mesh.loops):
            uvdata[i].uv = uv_flat[i]
        for m in mats_used:
            mesh.materials.append(materials[m])
        for poly, mname in zip(mesh.polygons, mat_idx):
            poly.material_index = mats_used.index(mname)
            poly.use_smooth = False
        # triangulate (deliverable is triangle-only).  Normals are correct by
        # construction; recalculating would flip open shells.
        bm = bmesh.new()
        bm.from_mesh(mesh)
        bmesh.ops.triangulate(bm, faces=list(bm.faces), quad_method="BEAUTY",
                              ngon_method="BEAUTY")
        bm.to_mesh(mesh)
        bm.free()
        mesh.update()
        for poly in mesh.polygons:
            poly.use_smooth = False
        # ART-04: one flat colour per face (vertex-colour attempt; whether the
        # game shader consumes it is unknown -> in-game check).
        col_layer = mesh.color_attributes.new(name="Col", type="BYTE_COLOR",
                                              domain="CORNER")
        mesh.color_attributes.active_color = col_layer
        color_of_slot = {i: FACE_COLORS[m] for i, m in enumerate(mats_used)}
        for poly in mesh.polygons:
            col = color_of_slot.get(poly.material_index, (1.0, 1.0, 1.0, 1.0))
            for loop_index in poly.loop_indices:
                col_layer.data[loop_index].color = col
        ob = bpy.data.objects.new(objname, mesh)
        bpy.context.scene.collection.objects.link(ob)
        ob["hdbf_tier"] = mb.tier
        ob["hdbf_faction"] = mb.faction
        ob["hdbf_lod"] = mb.lod
        ob["hdbf_units"] = "grid (1 grid = 1 Unity unit)"
        ob["hdbf_art"] = "ART-04 empty flat roof platform / flat-patch UV"
        created.append(ob)
    # ART-01 port empties (6; ART-02 exports only the 3 #Port# markers)
    for spec in A.PORT_SPECS:
        em = bpy.data.objects.new(spec["object"], None)
        em.empty_display_type = "PLAIN_AXES"
        em.empty_display_size = 0.08
        bl = A.blender_from_asset(spec["asset"])
        em.location = bl
        em.rotation_euler = tuple(math.radians(v) for v in spec["rot_blender"])
        em["hdbf_port_class"] = spec["class"]
        em["hdbf_port_outward"] = ",".join(str(v) for v in spec["outward"])
        em["hdbf_units"] = "grid"
        bpy.context.scene.collection.objects.link(em)
        created.append(em)
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
    # clean scene objects/meshes, materials stay for the next model
    for ob in objs:
        me = ob.data if ob.type == "MESH" else None
        bpy.data.objects.remove(ob, do_unlink=True)
        if me is not None and me.users == 0:
            bpy.data.meshes.remove(me)
    return fbx, glb


# --------------------------------------------------------------------------- #
# model assembly
# --------------------------------------------------------------------------- #
def make_builder(tier, faction, lod):
    mb = ModelBuilder(tier, faction, lod)
    if tier == CONSTRUCTION_TIER:
        mb.body_name = "SM_HDBF_BoxFarm_ConstructionStage0"
    else:
        mb.body_name = "SM_HDBF_BoxFarm_%s_LOD%d" % (tier, lod)
    return mb


def build_tier_model(tier, faction, lod):
    mb = make_builder(tier, faction, lod)
    style = tier_style(tier)
    bevel = 0.0
    if lod == 0:
        bevel = 0.014 if faction == "Folktails" else 0.009
    add_foundation(mb, style, lod)
    add_corner_frame(mb, style, lod, bevel)
    add_deck_details(mb, style, lod)
    add_walls(mb, style, lod, bevel)
    add_door(mb, style, lod, bevel)
    add_windows(mb, style, tier, lod)
    add_panels(mb, style, tier, lod)
    add_surface_details(mb, style, tier, lod)
    add_ports_geometry(mb, style, tier, lod)
    add_roof_platform(mb, style, tier, lod, bevel)
    add_decor(mb, tier, style, lod)
    fill = auto_fill(mb, tier, style, lod)
    roles = patch_roles_for(tier, faction, construction=False)
    assign_flat_patch_uvs(mb, roles)
    lo, hi = mb.bounds_asset()
    info = {
        "tier": tier, "faction": faction, "lod": lod,
        "tris": mb.tri_count(), "budget": A.TRI_BUDGET[lod],
        "bounds": [round(hi[i] - lo[i], 5) for i in range(3)],
        "min": [round(v, 4) for v in lo], "max": [round(v, 4) for v in hi],
        "materials": mb.used_materials(),
        "materials_to_patch": {slot: MP.role_of(name) for slot, name in
                               TC.MATERIAL_MAP[(tier, faction, False)].items()},
        "platform": {"size_x": style["plan"][0], "size_z": style["plan"][1],
                     "top": PLATFORM_TOP, "curb_top": 1.0},
        "fill": fill,
    }
    return mb, info


def build_construction_model():
    tier = CONSTRUCTION_TIER
    mb = make_builder(tier, "shared", 0)
    wood = WOOD
    metal = METAL
    sx, sz = A.roof_platform(tier)
    # full 3x2 foundation slab (greybox base)
    mb.box(mb.body_name, (0.0, FOUND_TOP * 0.5, 0.0), (3.0, FOUND_TOP, 2.0),
           wood, skip={0})
    for (x, z, w, d) in ((0.0, 0.955, 2.96, 0.05), (0.0, -0.955, 2.96, 0.05),
                         (1.455, 0.0, 0.05, 1.86), (-1.455, 0.0, 0.05, 1.86)):
        mb.box(mb.body_name, (x, 0.12, z), (w, 0.04, d), metal, skip={0})
    # scaffold posts to the wall line
    for x in (-1.40, 1.40):
        for z in (-0.90, 0.90):
            mb.box(mb.body_name, (x, 0.43, z), (0.08, 0.66, 0.08), wood)
    # half-built perimeter walls (construction stage look)
    mb.box(mb.body_name, (0.0, 0.36, 0.94), (2.90, 0.52, 0.04), wood)
    for x in (-1.44, 1.44):
        mb.box(mb.body_name, (x, 0.36, 0.0), (0.04, 0.52, 1.86), wood)
    # half-built deck
    mb.box(mb.body_name, (0.0, 0.77, 0.0), (2.84, 0.06, 1.80), wood, skip={0})
    for i in range(3):
        z = -0.60 + i * 0.60
        mb.box(mb.body_name, (0.0, 0.804, z), (2.60, 0.008, 0.05), wood)
    # the same ONE empty flat platform (greybox): slab + thin curb, nothing on it
    mb.box(mb.body_name, (0.0, (PLATFORM_TOP + PLATFORM_UNDER) * 0.5, 0.0),
           (sx, PLATFORM_THICK, sz), wood)
    cy = (PLATFORM_TOP + 1.0) * 0.5
    mb.box(mb.body_name, (0.0, cy, sz * 0.5 - CURB_W * 0.5),
           (sx, CURB_H, CURB_W), metal)
    mb.box(mb.body_name, (0.0, cy, -(sz * 0.5 - CURB_W * 0.5)),
           (sx, CURB_H, CURB_W), metal)
    mb.box(mb.body_name, (sx * 0.5 - CURB_W * 0.5, cy, 0.0),
           (CURB_W, CURB_H, sz - 2.0 * CURB_W), metal)
    mb.box(mb.body_name, (-(sx * 0.5 - CURB_W * 0.5), cy, 0.0),
           (CURB_W, CURB_H, sz - 2.0 * CURB_W), metal)
    # platform supports on the half deck
    sup_y = (DECK_TOP + PLATFORM_UNDER) * 0.5
    sup_h = PLATFORM_UNDER - DECK_TOP
    for x in (-(sx * 0.5 - 0.07), sx * 0.5 - 0.07):
        for z in (-(sz * 0.5 - 0.07), sz * 0.5 - 0.07):
            mb.box(mb.body_name, (x, sup_y, z), (0.07, sup_h, 0.07), metal)
    mb.box(mb.body_name, (0.0, PLATFORM_UNDER - 0.028, 0.0),
           (sx - 0.10, 0.055, 0.06), metal)
    # rim framing detail
    for x in (-1.40, 1.40):
        for z in (-0.90, 0.90):
            mb.box(mb.body_name, (x, 0.80, z), (0.10, 0.08, 0.10), metal)
    roles = patch_roles_for(tier, "shared", construction=True)
    assign_flat_patch_uvs(mb, roles)
    lo, hi = mb.bounds_asset()
    info = {
        "tier": tier, "faction": "shared", "lod": 0,
        "tris": mb.tri_count(), "budget": None,
        "bounds": [round(hi[i] - lo[i], 5) for i in range(3)],
        "min": [round(v, 4) for v in lo], "max": [round(v, 4) for v in hi],
        "materials": mb.used_materials(),
        "materials_to_patch": {slot: MP.role_of(name) for slot, name in
                               TC.MATERIAL_MAP[("Basic", "Folktails", True)].items()},
        "platform": {"size_x": sx, "size_z": sz, "top": PLATFORM_TOP,
                     "curb_top": 1.0},
        "fill": {"added_pads": 0, "added_seams": 0, "start_tris": mb.tri_count(),
                 "detail_tris": 0},
    }
    return mb, info


# --------------------------------------------------------------------------- #
# material_slots.json
# --------------------------------------------------------------------------- #
def write_material_slots(infos):
    regions_by_mat = {}
    for rname, (mat, px) in A.REGIONS.items():
        regions_by_mat.setdefault(mat, []).append({
            "region": rname, "pixel_rect": list(px),
            "uv_rect": [round(v, 5) for v in A.region_uv(rname)]})
    used_by = {m: [] for m in A.MATERIAL_ORDER}
    patches_used_by = {}
    for info in infos:
        key = info["tier"] if info["tier"] == CONSTRUCTION_TIER else \
            "%s.%s.LOD%d" % (info["tier"], info["faction"], info["lod"])
        for m in info["materials"]:
            used_by[m].append(key)
        for role in info["materials_to_patch"].values():
            patches_used_by.setdefault(role, []).append(key)
    patch_records = []
    for role in MP.PATCH_PIXELS:
        rec = MP.patch_record(role)
        rec["used_by_models"] = sorted(set(patches_used_by.get(role, [])))
        rec["materials"] = sorted(name for name, r in MP.ROLE_OF_MATERIAL.items()
                                  if r == role)
        patch_records.append(rec)
    data = {
        "generated_by": "art/gen_models.py (ART-04 flat-roof / flat-patch revision)",
        "art_revision": "ART-04",
        "roof_platform": {
            "description": "ONE empty flat platform per model; no crop slots, no "
                           "soil, no roof objects. Thin curb is the envelope apex.",
            "top_contract": PLATFORM_TOP,
            "curb_top_contract": 1.0,
            "curb_height": CURB_H,
            "curb_width": CURB_W,
            "clear_above": TC.PLATFORM_CLEAR_ABOVE,
            "plans": {tier: {"size_x": p["size_x"], "size_z": p["size_z"]}
                      for tier, p in A.ROOF_PLATFORM.items()},
        },
        "atlas": {
            "size": A.ATLAS_SIZE,
            "files": A.ATLAS_FILES,
            "channels": {
                "albedo": "sRGB RGB(A)",
                "normal": "linear RGB, OpenGL (+Y up)",
                "mask": "linear RGBA: R=metallic, G=roughness, A=smoothness, "
                        "B=metallic duplicate (glTF metallicRoughness packing)",
            },
            "padding_px": A.PADDING,
            "uv_space": "ART-04: every face of one material is mapped into that "
                        "material's fixed flat patch (no per-face unique packing); "
                        "all uv0 in [0,1]",
            "patch_texels_per_grid": MP.PATCH_TEXELS_PER_GRID,
        },
        "material_order": A.MATERIAL_ORDER,
        "material_patches": patch_records,
        "materials": [
            {
                "slot": i, "name": m, "atlas_regions": regions_by_mat.get(m, []),
                "params": A.MATERIAL_PARAMS[m],
                "used_by": sorted(set(used_by[m])),
            } for i, m in enumerate(A.MATERIAL_ORDER)
        ],
        "tier_material_sets": A.TIER_MATERIALS,
        "tiers": A.TIER_MATERIALS,
        "slots": {},
        "models": {},
    }
    for info in infos:
        key = info["tier"] if info["tier"] == CONSTRUCTION_TIER else \
            "%s.%s" % (info["tier"], info["faction"])
        construction = info["tier"] == CONSTRUCTION_TIER
        vanilla_map = TC.MATERIAL_MAP[("Basic", "Folktails", True)] if construction \
            else TC.MATERIAL_MAP[(info["tier"], info["faction"], False)]
        model_patches = {}
        for slot, role in info["materials_to_patch"].items():
            rec = MP.patch_record(role)
            rec["vanilla_material"] = vanilla_map[slot]
            model_patches[slot] = rec
        data["models"].setdefault(key, {})["LOD%d" % info["lod"]] = {
            "file_stem": ("ConstructionStage0" if construction else "LOD%d" % info["lod"]),
            "materials": info["materials"],
            "triangles": info["tris"],
            "material_patches": model_patches,
            "roof_platform": info["platform"],
        }
        slot_list = data["slots"].setdefault(key, [])
        for m in info["materials"]:
            if m not in slot_list:
                slot_list.append(m)
    for key in data["slots"]:
        data["slots"][key] = [m for m in A.MATERIAL_ORDER if m in data["slots"][key]]
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
    scene.render.fps = 24
    materials = ensure_materials()
    os.makedirs(MODELS_DIR, exist_ok=True)
    infos = []
    print("=== HDBF ART-04 model generation (empty flat roof platform) ===")
    print("textures present:", {k: os.path.isfile(os.path.join(ART_DIR, v))
                               for k, v in A.ATLAS_FILES.items()})
    print("platform plans:", {t: A.roof_platform(t) for t in A.TIERS})
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
                      "mats=%s platform=%.2fx%.2f pads=%d seams=%d" % (
                          tier, faction, lod, info["tris"], info["budget"],
                          info["bounds"][0], info["bounds"][1], info["bounds"][2],
                          ",".join(info["materials"]), info["platform"]["size_x"],
                          info["platform"]["size_z"], info["fill"]["added_pads"],
                          info["fill"]["added_seams"]))
    mb, info = build_construction_model()
    export_model(mb, os.path.join(MODELS_DIR, "ConstructionStage0"),
                 "ConstructionStage0", materials)
    infos.append(info)
    print("[ConstructionStage0] tris=%d bounds=%.3fx%.3fx%.3f mats=%s platform=%.2fx%.2f"
          % (info["tris"], info["bounds"][0], info["bounds"][1], info["bounds"][2],
             ",".join(info["materials"]), info["platform"]["size_x"],
             info["platform"]["size_z"]))
    path = write_material_slots(infos)
    print("wrote", path)
    with open(os.path.join(os.environ.get("TEMP", ART_DIR),
                           "hdbf_gen_summary.json"), "w", encoding="utf-8") as fh:
        json.dump(infos, fh, indent=2)
    print("=== done: %d model files x2 formats ===" % len(infos))


if __name__ == "__main__":
    main()
