"""HDBF Box Farm - v2 procedural model generator (Blender 4.2.1, headless).

**v2 revision (2026-10-26, REQ_V2_SECTION_19):** the four tiers are now all
strict **3 x 3 x 1** indoor high-density farms.  Compared with the ART-04
generator (``art/gen_models.py``, 3x2x1) this generator:

  * builds a strict 3.00 x 1.00 x 3.00 asset-space envelope (front -Z, pivot at
    footprint bottom centre), including decoration, windows, trims and ports;
  * puts ONE flat empty roof platform on top: the WALKING SURFACE is the model
    apex at **y = 1.00**, the perimeter "收边" is a flush 0.04 fascia whose top is
    exactly the walking surface, and **no geometry exists above y = 1.00**
    (the 30 cm volume above the walking surface is empty -> stacking safe);
  * expresses "indoor high density" with STATIC interior racks / shelves (3
    levels) visible through facade openings - never 600 dynamic slots.  Opening
    style, glass amount, framing and material layering differ per tier so the
    four tiers stay distinguishable at the 50% camera (REQ-V2-ART-04);
  * carries EXACTLY the three ``#Port#Power/#Port#Pipe/#Port#Logistics`` empty
    markers and **no ``#Slot#`` node** (REQ-V2-ART-02);
  * keeps the ART-04 flat-patch UV mapping and vanilla ``MaterialCollection``
    material asset names (no custom PBR / AssetBundle);
  * builds one shared construction-stage greybox, also a strict 3x3x1 with the
    same flat empty roof platform (REQ-V2-ART-03).

Output (deterministic, overwrite in place):

    art/models/<Tier>.<Faction>/LOD0.fbx, LOD1.fbx, LOD2.fbx  (+ .glb)
    art/models/ConstructionStage0/ConstructionStage0.fbx      (+ .glb)
    art/material_slots.json

Run:

    $env:BLENDER_USER_CONFIG = "$env:TEMP\blender-config"
    blender.exe -b --factory-startup -noaudio --python art\\gen_models_v2.py

Authoring space: Blender Z-up, building front = +Y.  The FBX/GLB export applies
Blender's standard axis conversion so asset space is Unity Y-up with the front
at -Z:

    asset = ( blender.x, blender.z, -blender.y )
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
# envelope (asset space: X width, Y up, Z depth, front -Z)
# --------------------------------------------------------------------------- #
FOUND_TOP = 0.10            # foundation slab 0.00 .. 0.10
WALL_TOP = 0.95             # walls reach the platform underside (flush)
BODY_X = 1.46               # outer face of the side walls
BODY_Z = 1.46               # outer face of the front/back walls
WALL_T = 0.04               # wall thickness
POST_X = 1.40               # corner post centre X
POST_Z = 1.40               # corner post centre Z

PLATFORM = A.ROOF_PLATFORM
PLATFORM_TOP = A.ROOF_PLATFORM_TOP                  # 1.00 == model apex
PLATFORM_THICK = A.ROOF_PLATFORM_THICKNESS          # 0.05 -> underside 0.95
PLATFORM_UNDER = PLATFORM_TOP - PLATFORM_THICK
CURB_H = A.ROOF_CURB_H                              # 0.04 flush fascia
CURB_W = A.ROOF_CURB_W
CLEAR_ABOVE = A.ROOF_CLEAR_ABOVE                    # 0.30 empty volume above the top
TRI_BUDGET = A.TRI_BUDGET

WOOD = "M_HDBF_Wood"
METAL = "M_HDBF_Metal"
GLASS = "M_HDBF_Glass"
FABRIC = "M_HDBF_Fabric"

#: per-face vertex colour (the game shader may ignore it; the export pipeline
#: records whether it survived, and the report never claims more than that).
FACE_COLORS = {
    WOOD: (1.00, 0.94, 0.86, 1.0),
    METAL: (0.90, 0.93, 0.97, 1.0),
    GLASS: (0.80, 0.94, 1.00, 1.0),
    FABRIC: (1.00, 0.96, 0.82, 1.0),
}

CONSTRUCTION_TIER = A.CONSTRUCTION_STAGE

#: facade plane table: axis + sign of the outward normal + the fixed coordinate
#: of the wall CENTRE plane (asset space).
PLANES = {
    "front": {"axis": "z", "sign": -1.0, "fixed": -(BODY_Z - WALL_T * 0.5)},
    "back": {"axis": "z", "sign": 1.0, "fixed": BODY_Z - WALL_T * 0.5},
    "left": {"axis": "x", "sign": -1.0, "fixed": -(BODY_X - WALL_T * 0.5)},
    "right": {"axis": "x", "sign": 1.0, "fixed": BODY_X - WALL_T * 0.5},
}

#: glass/pane plane: slightly recessed from the wall outer face (still inside
#: the envelope); the frame sits just outside it.
PANE_OFFSET = BODY_X - 0.008        # 1.452
FRAME_OFFSET = BODY_X + 0.002       # 1.462


# --------------------------------------------------------------------------- #
# tier style
# --------------------------------------------------------------------------- #
def tier_style(tier):
    """Materials / facade openings / framing per finished tier.

    Material slots always equal ``atlas_layout.TIER_MATERIALS`` so the frozen
    1 -> 2 -> 3 -> 4 material progression is preserved:
      Basic        wood + roof/thatch                       (2)
      Intermediate planks + plaster + glass                 (3)
      Advanced     white/grey timber + metal + glass + plates (4)
      Top          metal + riveted plates + glass + timber boards (4)
    The four tiers differ by opening layout (open bays vs 2 / 4 / 5 glazed
    bays), framing density, material layering and port fixtures: any two of the
    50%-camera criteria {silhouette, material, windows, ports} differ.
    """
    sx, sz = A.roof_platform(tier)
    if tier == "Basic":
        return dict(
            tier=tier, plan=(sx, sz), wall=WOOD, frame=WOOD, foundation=WOOD,
            roof=FABRIC, panel=None, door=None, shelf=WOOD, accent=FABRIC,
            metal_frame=False, beveled=True, shelf_levels=3,
            openings={
                "front": [
                    {"rect": (-1.35, -0.52, 0.22, 0.80), "kind": "open"},
                    {"rect": (-0.42, 0.42, 0.22, 0.80), "kind": "open"},
                    {"rect": (0.52, 1.35, 0.22, 0.80), "kind": "open"},
                ],
                "left": [{"rect": (-1.25, 1.25, 0.50, 0.88), "kind": "open"}],
                "right": [{"rect": (-1.25, 1.25, 0.50, 0.88), "kind": "open"}],
                "back": [],
            })
    if tier == "Intermediate":
        return dict(
            tier=tier, plan=(sx, sz), wall=WOOD, frame=WOOD, foundation=WOOD,
            roof=WOOD, panel=FABRIC, door=WOOD, shelf=WOOD, accent=FABRIC,
            metal_frame=False, beveled=True, shelf_levels=3,
            openings={
                "front": [
                    {"rect": (-0.31, 0.31, 0.10, 0.82), "kind": "door"},
                    {"rect": (-1.05, -0.45, 0.34, 0.70), "kind": "glass"},
                    {"rect": (0.45, 1.05, 0.34, 0.70), "kind": "glass"},
                ],
                "back": [{"rect": (-1.10, 1.10, 0.70, 0.86), "kind": "glass"}],
                "left": [{"rect": (-0.95, 0.95, 0.38, 0.66), "kind": "glass"}],
                "right": [{"rect": (-0.95, 0.95, 0.38, 0.66), "kind": "glass"}],
            })
    if tier == "Advanced":
        lower = [(-1.20, -0.70), (-0.57, -0.07), (0.07, 0.57), (0.70, 1.20)]
        front = []
        for (a0, a1) in lower:
            front.append({"rect": (a0, a1, 0.30, 0.70), "kind": "glass"})
            front.append({"rect": (a0, a1, 0.78, 0.90), "kind": "glass"})
        return dict(
            tier=tier, plan=(sx, sz), wall=WOOD, frame=METAL, foundation=METAL,
            roof=METAL, panel=FABRIC, door=METAL, shelf=WOOD, accent=METAL,
            metal_frame=True, beveled=True, shelf_levels=3,
            openings={
                "front": front,
                "back": [{"rect": (-1.10, 1.10, 0.74, 0.88), "kind": "glass"}],
                "left": [
                    {"rect": (-0.85, -0.25, 0.32, 0.68), "kind": "glass"},
                    {"rect": (0.25, 0.85, 0.32, 0.68), "kind": "glass"},
                ],
                "right": [
                    {"rect": (-0.85, -0.25, 0.32, 0.68), "kind": "glass"},
                    {"rect": (0.25, 0.85, 0.32, 0.68), "kind": "glass"},
                ],
            })
    # Top: five lower + five clerestory glazed bays, metal shell, louver band.
    centres = (-1.16, -0.58, 0.0, 0.58, 1.16)
    front = []
    for cx in centres:
        front.append({"rect": (cx - 0.23, cx + 0.23, 0.30, 0.62), "kind": "glass"})
        front.append({"rect": (cx - 0.23, cx + 0.23, 0.72, 0.90), "kind": "glass"})
    return dict(
        tier=tier, plan=(sx, sz), wall=METAL, frame=METAL, foundation=METAL,
        roof=METAL, panel=FABRIC, door=METAL, shelf=WOOD, accent=METAL,
        metal_frame=True, beveled=True, shelf_levels=3,
        openings={
            "front": front,
            "back": [
                {"rect": (-0.30, 0.30, 0.10, 0.80), "kind": "door"},
                {"rect": (-1.20, -0.70, 0.65, 0.85), "kind": "glass"},
                {"rect": (0.70, 1.20, 0.65, 0.85), "kind": "glass"},
            ],
            "left": [
                {"rect": (-0.85, -0.35, 0.36, 0.64), "kind": "glass"},
                {"rect": (0.35, 0.85, 0.36, 0.64), "kind": "glass"},
            ],
            "right": [
                {"rect": (-0.85, -0.35, 0.36, 0.64), "kind": "glass"},
                {"rect": (0.35, 0.85, 0.36, 0.64), "kind": "glass"},
            ],
        })


def font_style(tier):
    return tier_style(tier)


# --------------------------------------------------------------------------- #
# deterministic helpers
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
    (0, 3, 2, 1),  # -Z (blender bottom = asset bottom)
    (4, 5, 6, 7),  # +Z (blender top = asset top)
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
# facade helpers
# --------------------------------------------------------------------------- #
def facade_panels(a0, a1, y0, y1, openings):
    """Solid panels (a_center, a_size, y_center, y_size) = rectangle - openings."""
    edges = {a0, a1}
    for op in openings:
        oa0, oa1 = op["rect"][0], op["rect"][1]
        edges.add(max(a0, min(a1, oa0)))
        edges.add(max(a0, min(a1, oa1)))
    xs = sorted(edges)
    panels = []
    for i in range(len(xs) - 1):
        xa, xb = xs[i], xs[i + 1]
        if xb - xa < 1e-6:
            continue
        cover = sorted((op["rect"][2], op["rect"][3]) for op in openings
                       if op["rect"][0] <= xa + 1e-9 and xb <= op["rect"][1] + 1e-9)
        merged = []
        for (b, t) in cover:
            if merged and b <= merged[-1][1] + 1e-9:
                merged[-1][1] = max(merged[-1][1], t)
            else:
                merged.append([b, t])
        y = y0
        for (b, t) in merged:
            if b - y > 1e-6:
                panels.append(((xa + xb) * 0.5, xb - xa, (y + b) * 0.5, b - y))
            y = max(y, t)
        if y1 - y > 1e-6:
            panels.append(((xa + xb) * 0.5, xb - xa, (y + y1) * 0.5, y1 - y))
    return panels


def facade_box(mb, plane, ac, aw, yc, yh, depth, offset, material, bevel=0.0):
    """Place a box centred on a facade's horizontal coordinate `ac` at plane."""
    spec = PLANES[plane]
    if spec["axis"] == "z":
        center = (ac, yc, spec["sign"] * offset)
        size = (aw, yh, depth)
    else:
        center = (spec["sign"] * offset, yc, ac)
        size = (depth, yh, aw)
    mb.box(mb.body_name, center, size, material,
           bevel=bevel if bevel > 0.0 else 0.0)


def add_facade_walls(mb, style, lod, bevel):
    """Build the four facades with their openings cut out of solid wall panels."""
    body = mb.body_name
    material = style["wall"]
    for plane in ("front", "back", "left", "right"):
        openings = style["openings"].get(plane, [])
        horizontal = (-BODY_X + WALL_T * 0.5, BODY_X - WALL_T * 0.5) \
            if PLANES[plane]["axis"] == "x" else (
                -BODY_Z + WALL_T * 0.5, BODY_Z - WALL_T * 0.5)
        a0, a1 = horizontal
        if lod == 2:
            # LOD2 massing: one solid wall per facade; the openings are painted
            # by surface glass panes (see add_openings) instead of being cut out
            facade_box(mb, plane, (a0 + a1) * 0.5, a1 - a0,
                       (FOUND_TOP + WALL_TOP) * 0.5, WALL_TOP - FOUND_TOP,
                       WALL_T, 0.0, material)
            continue
        panels = facade_panels(a0, a1, FOUND_TOP, WALL_TOP, openings)
        for (ac, aw, yc, yh) in panels:
            facade_box(mb, plane, ac, aw, yc, yh, WALL_T, 0.0, material,
                       bevel=bevel)
        # corner trim on every wall corner (adds a crisp frame edge)
        for a_edge in (a0 + 0.02, a1 - 0.02):
            facade_box(mb, plane, a_edge, 0.04, (FOUND_TOP + WALL_TOP) * 0.5,
                       WALL_TOP - FOUND_TOP, 0.012, BODY_X + 0.006
                       if PLANES[plane]["axis"] == "x" else BODY_Z + 0.006,
                       style["frame"])
    # corner posts (vertical, on the four footprint corners)
    post_h = WALL_TOP - FOUND_TOP
    for x in (-POST_X, POST_X):
        for z in (-POST_Z, POST_Z):
            mb.box(body, (x, FOUND_TOP + post_h * 0.5, z),
                   (0.10, post_h, 0.10), style["frame"],
                   bevel=bevel if lod == 0 else 0.0,
                   anchor=((1 if x > 0 else -1, 0, 0), (0.03, 0.03, 0.03)))
    # top perimeter beam ring (below the roof platform)
    beam_y = WALL_TOP - 0.05
    for (x, z, sx, sz) in ((0.0, BODY_Z - 0.03, 2.88, 0.07),
                           (0.0, -(BODY_Z - 0.03), 2.88, 0.07),
                           (BODY_X - 0.03, 0.0, 0.07, 2.88),
                           (-(BODY_X - 0.03), 0.0, 0.07, 2.88)):
        mb.box(body, (x, beam_y, z), (sx, 0.07, sz), style["frame"],
               bevel=bevel if lod == 0 else 0.0,
               anchor=((0, 1, 0), (0.05, 0.03, 0.05)))


def add_openings(mb, style, faction, lod):
    """Glass panes, door leaves, frames and louver slats in the wall openings."""
    body = mb.body_name
    frame = style["frame"]
    heavy = style["metal_frame"]
    bar = 0.036
    for plane in ("front", "back", "left", "right"):
        for op in style["openings"].get(plane, []):
            a0, a1, y0, y1 = op["rect"]
            kind = op["kind"]
            ac, aw = (a0 + a1) * 0.5, a1 - a0
            yc, yh = (y0 + y1) * 0.5, y1 - y0
            if lod == 2:
                # LOD2: surface panes only; windows are painted onto the massing
                if kind == "glass":
                    facade_box(mb, plane, ac, aw, yc, yh, 0.012,
                               PANE_OFFSET, GLASS)
                elif kind == "door":
                    facade_box(mb, plane, ac, aw - 0.02, yc, yh - 0.01, 0.024,
                               PANE_OFFSET + 0.006, style["door"])
                continue
            if kind == "glass":
                facade_box(mb, plane, ac, aw, yc, yh, 0.012, PANE_OFFSET,
                           GLASS)
                # two horizontal frame bars per opening (mullion/transom)
                for yy in (y0 + bar * 0.5, y1 - bar * 0.5):
                    facade_box(mb, plane, ac, aw, yy, bar, 0.020,
                               FRAME_OFFSET, frame)
                if lod == 0:
                    for aa in (a0 + bar * 0.5, a1 - bar * 0.5):
                        facade_box(mb, plane, aa, bar, yc, yh, 0.020,
                                   FRAME_OFFSET, frame)
            elif kind == "door":
                facade_box(mb, plane, ac, aw - 0.02, yc, yh - 0.01, 0.030,
                           PANE_OFFSET + 0.006, style["door"])
                if lod == 0:
                    if heavy:
                        for i in range(3):
                            facade_box(mb, plane, ac - aw * 0.5 + 0.08 + i * 0.16,
                                       0.03, yc, yh - 0.06, 0.012,
                                       FRAME_OFFSET + 0.012, style["panel"])
                    else:
                        for i in range(3):
                            facade_box(mb, plane, ac, aw - 0.10, y0 + 0.18 +
                                       i * 0.18, 0.022, 0.012,
                                       FRAME_OFFSET + 0.012, frame)
                    facade_box(mb, plane, ac + aw * 0.32, 0.10, yc, 0.06,
                               0.020, FRAME_OFFSET + 0.014, style["panel"]
                               if style["panel"] else frame)
            elif kind == "open":
                # open barn bay: timber trim around the opening only
                for yy in (y0 - 0.02, y1 + 0.02):
                    facade_box(mb, plane, ac, aw + 0.06, yy, 0.04, 0.030,
                               FRAME_OFFSET - 0.008, frame)
    # Top tier: horizontal metal louver slats on the lower front/back bands
    if style["tier"] == "Top" and lod == 0:
        for plane in ("front", "back"):
            for i in range(3):
                yy = 0.135 + i * 0.045
                facade_box(mb, plane, 0.0, 2.60, yy, 0.016, 0.016,
                           FRAME_OFFSET + 0.006, METAL)


def add_panels(mb, style, tier, faction, lod):
    """Plaster band (Intermediate) / riveted detail plates (Advanced, Top)."""
    body = mb.body_name
    panel = style["panel"]
    if not panel:
        return
    if tier == "Intermediate":
        # plaster bands above the windows / below the roof beam (front + back)
        for plane, rects in (
                ("front", [(-1.30, 1.30, 0.78, 0.94)]),
                ("back", [(-1.30, 1.30, 0.89, 0.94), (-1.30, 1.30, 0.30, 0.66)]),
                ("left", [(-1.30, 1.30, 0.72, 0.94), (-1.30, 1.30, 0.20, 0.32)]),
                ("right", [(-1.30, 1.30, 0.72, 0.94), (-1.30, 1.30, 0.20, 0.32)])):
            for (a0, a1, y0, y1) in rects:
                facade_box(mb, plane, (a0 + a1) * 0.5, a1 - a0,
                           (y0 + y1) * 0.5, y1 - y0, 0.018, FRAME_OFFSET + 0.004,
                           panel)
        if lod == 0:
            for plane in ("front", "back"):
                for i in range(5):
                    facade_box(mb, plane, -1.10 + i * 0.55, 0.03, 0.85, 0.13,
                               0.010, FRAME_OFFSET + 0.014, style["frame"])
    else:
        # riveted plate bands on the lower band + above the lower window row
        for plane, rects in (
                ("front", [(-1.32, 1.32, 0.115, 0.255), (-1.32, 1.32, 0.705, 0.725)]),
                ("back", [(-1.32, 1.32, 0.115, 0.255)]),
                ("left", [(-1.32, 1.32, 0.115, 0.255), (-1.32, 1.32, 0.705, 0.725)]),
                ("right", [(-1.32, 1.32, 0.115, 0.255), (-1.32, 1.32, 0.705, 0.725)])):
            for (a0, a1, y0, y1) in rects:
                facade_box(mb, plane, (a0 + a1) * 0.5, a1 - a0,
                           (y0 + y1) * 0.5, y1 - y0, 0.016, FRAME_OFFSET + 0.004,
                           panel)
        if lod == 0:
            for plane in ("front", "back"):
                for i in range(5):
                    facade_box(mb, plane, -1.10 + i * 0.55, 0.045, 0.185, 0.045,
                               0.012, FRAME_OFFSET + 0.016, style["accent"])
            for plane in ("left", "right"):
                for i in range(3):
                    facade_box(mb, plane, -0.90 + i * 0.90, 0.045, 0.185, 0.045,
                               0.012, FRAME_OFFSET + 0.016, style["accent"])


# --------------------------------------------------------------------------- #
# interior: static high-density racks (v2 "indoor" expression)
# --------------------------------------------------------------------------- #
def add_interior_shelves(mb, style, tier, lod):
    """Static 3-level racks visible through the facade openings.

    These are DECORATION only: no crop entities, no dynamic slot meshes, no
    clickable geometry; the simulation never reads this geometry (REQ-V2-ART-04).
    """
    body = mb.body_name
    levels = style["shelf_levels"]
    board = style["shelf"]
    frame = style["frame"]
    heavy = style["metal_frame"]
    # two rack banks inside the front half so the racks read as silhouettes
    # through the open bays / glass windows
    banks = ((-1.24, -0.08), (0.08, 1.24))
    z0, z1 = -1.32, 0.30
    for bi, (xa, xb) in enumerate(banks):
        xc, xw = (xa + xb) * 0.5, xb - xa
        if lod <= 1:
            for x in (xa, xb):
                mb.box(body, (x, FOUND_TOP + 0.34, (z0 + z1) * 0.5),
                       (0.06, 0.68, z1 - z0 + 0.06), frame)
        for li in range(levels):
            y = 0.26 + li * 0.20
            mb.box(body, (xc, y, (z0 + z1) * 0.5),
                   (xw - 0.06, 0.030, z1 - z0 - 0.04), board)
            if lod == 0 and not heavy:
                # shelf edge rail (silhouette detail)
                mb.box(body, (xc, y + 0.028, z1 - 0.01),
                       (xw - 0.06, 0.020, 0.030), frame)
            if lod == 0:
                # deterministic crates on the boards
                n_crates = 1 if heavy else 2
                for ci in range(n_crates):
                    if h01(tier, bi, li, ci) < 0.72:
                        cx = xa + 0.16 + (h01(tier, bi, li, ci, "x") *
                                          (xw - 0.34))
                        cz = z0 + 0.22 + h01(tier, bi, li, ci, "z") * 0.55
                        mb.box(body, (cx, y + 0.075, cz), (0.18, 0.12, 0.18),
                               board if h01(tier, bi, li, ci, "m") < 0.6 else frame)
    # rack end frames against the back wall (extra depth cue at 50% scale)
    if lod == 0:
        for x in (-1.28, 1.28):
            mb.box(body, (x, FOUND_TOP + 0.30, 1.30), (0.06, 0.60, 0.10), frame)


# --------------------------------------------------------------------------- #
# foundation / decor
# --------------------------------------------------------------------------- #
def add_foundation(mb, style, lod):
    body = mb.body_name
    # full 3 x 3 base slab (defines the exact X/Z bounds and min Y = 0)
    mb.box(body, (0.0, FOUND_TOP * 0.5, 0.0), (3.0, FOUND_TOP, 3.0),
           style["foundation"], skip={0})
    # perimeter trim ring (all inside the envelope)
    for (x, z, sx, sz) in ((0.0, 1.48, 2.94, 0.04), (0.0, -1.48, 2.94, 0.04),
                           (1.48, 0.0, 0.04, 2.94), (-1.48, 0.0, 0.04, 2.94)):
        mb.box(body, (x, 0.12, z), (sx, 0.04, sz), style["frame"], skip={0})
    if lod == 0:
        # corner feet / pads
        for x in (-1.36, 1.36):
            for z in (-1.36, 1.36):
                mb.box(body, (x, 0.13, z), (0.20, 0.06, 0.20), style["frame"],
                       bevel=0.008)


def add_base_details(mb, style, tier, faction, lod):
    """Structural seams, bolts and trim below the roof platform."""
    body = mb.body_name
    if lod > 1:
        return
    wall = style["wall"]
    frame = style["frame"]
    heavy = style["metal_frame"]
    bevel = 0.010 if faction == "Folktails" else 0.006
    # vertical plank seams on the back wall + the side walls (timber tiers only:
    # the glazed / metal shells do not carry plank seams on their facades)
    if lod == 0 and not heavy:
        for i in range(13):
            x = -1.30 + i * (2.60 / 12.0)
            mb.box(body, (x, 0.52, BODY_Z + 0.006),
                   (0.018, 0.76, 0.010), wall)
        for sgn in (-1, 1):
            for i in range(10):
                z = -1.28 + i * (2.56 / 9.0)
                mb.box(body, (sgn * (BODY_X + 0.006), 0.52, z),
                       (0.010, 0.76, 0.018), wall)
    # foundation bolt rows
    n_bolt = 7 if (heavy or lod == 1) else 13
    step = 2.64 / max(1, n_bolt - 1)
    for i in range(n_bolt):
        x = -1.32 + i * step
        for zsgn in (-1, 1):
            mb.box(body, (x, 0.115, zsgn * 1.494),
                   (0.030, 0.030, 0.012), frame)
    # roof beam bolt rows (below the walking surface)
    n_beam = 6 if (heavy or lod == 1) else 11
    step_b = 2.60 / max(1, n_beam - 1)
    for i in range(n_beam):
        x = -1.30 + i * step_b
        for zsgn in (-1, 1):
            mb.box(body, (x, 0.912, zsgn * 1.494),
                   (0.028, 0.028, 0.012), frame)
    # corner brackets
    for x in (-POST_X, POST_X):
        for z in (-POST_Z, POST_Z):
            heights = (0.16, 0.88) if lod == 0 else (0.88,)
            for yy in heights:
                mb.box(body, (x, yy, z), (0.14, 0.05, 0.14), frame,
                       bevel=bevel if lod == 0 else 0.0)
            mb.anchor((x + (0.05 if x > 0 else -0.05), 0.52, z),
                      (1 if x > 0 else -1, 0, 0), (0.030, 0.030, 0.030), frame)


# --------------------------------------------------------------------------- #
# ports: exactly the three #Port# fixtures
# --------------------------------------------------------------------------- #
def add_ports_geometry(mb, style, tier, faction, lod):
    body = mb.body_name
    frame = style["frame"]
    metal = style["metal_frame"]
    # ---------------- pipe port, left side x = -1.50 ----------------------
    cover = not metal
    mb.box(body, (-1.462, 0.40, 0.0), (0.020, 0.28, 0.28),
           style["wall"] if cover else METAL,
           skip={0})
    if lod == 0:
        if cover:
            mb.box(body, (-1.474, 0.40, 0.0), (0.010, 0.20, 0.20),
                   style["wall"])
        else:
            mb.cyl(body, (-1.474, 0.40, 0.0), "x", 0.090, 0.048, 12, METAL)
            mb.cyl(body, (-1.462, 0.40, 0.0), "x", 0.062, 0.020, 10, METAL)
    # ---------------- logistics port, back z = +1.50 ----------------------
    visible = tier != "Basic"
    lx, ly = -1.0, 0.35
    for sgn in (-1, 1):
        mb.box(body, (lx + sgn * 0.43, ly, 1.452), (0.06, 0.46, 0.070), frame)
        mb.box(body, (lx, ly + sgn * 0.23, 1.452), (0.86, 0.05, 0.070), frame)
    if visible:
        mb.box(body, (lx, ly, 1.438), (0.78, 0.38, 0.020),
               METAL if metal else style["wall"])
        if lod == 0:
            for i in range(4):
                mb.box(body, (lx, 0.17 + i * 0.11, 1.424),
                       (0.72, 0.055, 0.014), frame)
    else:
        mb.box(body, (lx, ly, 1.452), (0.84, 0.40, 0.020), style["wall"])
        mb.box(body, (lx, ly, 1.458), (0.30, 0.30, 0.012), frame)
    # ---------------- power port, back right x = 1.0, y = 0.85 -------------
    wx, wy = 1.0, 0.85
    mb.box(body, (wx, 0.84, 1.452), (0.18, 0.13, 0.030),
           METAL if metal else frame,
           anchor=((0, 0, 1), (0.03, 0.03, 0.02)))
    if lod == 0:
        mb.box(body, (wx, 0.84, 1.470), (0.10, 0.08, 0.014), frame)
        if tier == "Top":
            mb.cyl(body, (wx + 0.06, 0.84, 1.474), "z", 0.020, 0.030, 8, METAL)
    mb.box(body, (wx, 0.44, 1.464), (0.12, 0.72, 0.016), frame)
    # service riser under the junction box (below the roof platform)
    if lod == 0:
        mb.box(body, (wx, 0.70, 1.470), (0.07, 0.20, 0.014),
               METAL if metal else frame)


# --------------------------------------------------------------------------- #
# the ONE flat empty roof platform
# --------------------------------------------------------------------------- #
def add_roof_platform(mb, style, tier, faction, lod):
    """Flat empty platform: walking surface y = 1.00 == apex; nothing on top."""
    body = mb.body_name
    sx, sz = style["plan"]
    roof = style["roof"]
    frame = style["frame"]

    # platform slab: underside 0.95, walking surface 1.00
    mb.box(body, (0.0, (PLATFORM_TOP + PLATFORM_UNDER) * 0.5, 0.0),
           (sx, PLATFORM_THICK, sz), roof, skip={0})

    # flush perimeter fascia ("收边"): 0.04 tall, top == walking surface, so the
    # edge never rises above the top and cannot block an upper building.
    fy = PLATFORM_TOP - CURB_H * 0.5
    mb.box(body, (0.0, fy, sz * 0.5 - CURB_W * 0.5),
           (sx, CURB_H, CURB_W), frame)
    mb.box(body, (0.0, fy, -(sz * 0.5 - CURB_W * 0.5)),
           (sx, CURB_H, CURB_W), frame)
    mb.box(body, (sx * 0.5 - CURB_W * 0.5, fy, 0.0),
           (CURB_W, CURB_H, sz - 2.0 * CURB_W), frame)
    mb.box(body, (-(sx * 0.5 - CURB_W * 0.5), fy, 0.0),
           (CURB_W, CURB_H, sz - 2.0 * CURB_W), frame)

    # underside frame: perimeter ring + cross beams (all below the platform)
    by = PLATFORM_UNDER - 0.028
    mb.box(body, (0.0, by, sz * 0.5 - 0.080), (sx - 0.06, 0.055, 0.06), frame)
    mb.box(body, (0.0, by, -(sz * 0.5 - 0.080)), (sx - 0.06, 0.055, 0.06), frame)
    mb.box(body, (sx * 0.5 - 0.080, by, 0.0), (0.06, 0.055, sz - 0.06), frame)
    mb.box(body, (-(sx * 0.5 - 0.080), by, 0.0), (0.06, 0.055, sz - 0.06), frame)
    for i in range(5 if lod <= 1 else 2):
        z = -1.10 + i * (0.55 if lod <= 1 else 1.10)
        mb.box(body, (0.0, by, z), (sx - 0.16, 0.045, 0.05), frame)
    if lod == 0:
        for x in (-0.9, 0.0, 0.9):
            mb.box(body, (x, by, 0.0), (0.05, 0.045, sz - 0.16), frame)
    # exposed rafter ends (Basic reads as a timber barn roof edge)
    if tier == "Basic" and lod == 0:
        for i in range(7):
            x = -1.20 + i * 0.40
            for zsgn in (-1, 1):
                mb.box(body, (x, PLATFORM_UNDER - 0.010, zsgn * (sz * 0.5 + 0.018)),
                       (0.08, 0.06, 0.045), WOOD)
    # rivets on the advanced / top roof fascia (never above the walking surface)
    if lod == 0 and style["metal_frame"]:
        n = 7
        for i in range(n):
            x = -1.24 + i * (2.48 / (n - 1))
            for zsgn in (-1, 1):
                mb.box(body, (x, PLATFORM_TOP - 0.020, zsgn * (sz * 0.5 + 0.004)),
                       (0.030, 0.030, 0.012), METAL)
        for i in range(n):
            z = -1.24 + i * (2.48 / (n - 1))
            for xsgn in (-1, 1):
                mb.box(body, (xsgn * (sx * 0.5 + 0.004), PLATFORM_TOP - 0.020, z),
                       (0.012, 0.030, 0.030), METAL)


# --------------------------------------------------------------------------- #
# decoration
# --------------------------------------------------------------------------- #
def add_decor(mb, tier, faction, style, lod):
    """Small exterior decoration; always below the platform and inside bounds."""
    if lod != 0:
        return
    body = mb.body_name
    if tier == "Basic":
        # water barrel + crate in the front working corner
        mb.cyl(body, (-1.16, 0.26, -1.05), "y", 0.105, 0.30, 10, WOOD)
        for yy in (0.15, 0.35):
            mb.cyl(body, (-1.16, yy, -1.05), "y", 0.112, 0.018, 10, WOOD)
        mb.box(body, (-0.76, 0.20, -1.12), (0.22, 0.20, 0.22), WOOD, bevel=0.010)
        if faction == "IronTeeth":
            mb.box(body, (-0.76, 0.31, -1.12), (0.24, 0.02, 0.24), style["accent"])
    elif tier == "Intermediate":
        mb.box(body, (0.98, 0.24, -1.08), (0.50, 0.18, 0.34), WOOD)
        for i in range(3):
            mb.box(body, (0.98, 0.16 + i * 0.06, -1.08), (0.52, 0.02, 0.36), WOOD)
        for sgn in (-1, 1):
            mb.box(body, (sgn * 1.472, 0.20, -0.98), (0.014, 0.16, 0.24), FABRIC)
    elif tier == "Advanced":
        mb.box(body, (1.02, 0.22, -1.06), (0.40, 0.20, 0.34), METAL)
        mb.box(body, (1.02, 0.33, -1.06), (0.42, 0.02, 0.36), METAL)
        mb.cyl(body, (-1.16, 0.22, -0.98), "y", 0.10, 0.24, 10, METAL)
    else:  # Top
        mb.box(body, (1.02, 0.23, -1.06), (0.44, 0.22, 0.34), METAL)
        mb.box(body, (1.02, 0.35, -1.06), (0.46, 0.02, 0.36), METAL)
        mb.cyl(body, (-1.18, 0.22, -1.02), "z", 0.09, 0.16, 10, METAL)


# --------------------------------------------------------------------------- #
# auto detail: top up to the LOD triangle budget (stays below the platform)
# --------------------------------------------------------------------------- #
def auto_fill(mb, tier, style, lod):
    """Top up to the LOD triangle budget without ever touching the roof top."""
    budget = TRI_BUDGET[lod]
    cap = int(budget * 0.25)
    start = mb.tri_count()
    frame = style["frame"]
    wall = style["wall"]

    def below_platform(cy, sy):
        return cy + sy * 0.5 <= PLATFORM_UNDER - 0.002

    anchors = sorted(mb.anchors, key=lambda a: (round(a["pos"][0], 4),
                                                round(a["pos"][2], 4),
                                                round(a["pos"][1], 4),
                                                a["normal"]))
    added_pads = 0
    detail_tris = 0

    for a in anchors:
        if mb.tri_count() >= budget * 0.99 or detail_tris >= cap:
            break
        size = a["size"]
        nx, ny, nz = a["normal"]
        x = a["pos"][0] + nx * (size[0] * 0.5 + 0.004)
        y = a["pos"][1] + ny * (size[1] * 0.5 + 0.004)
        z = a["pos"][2] + nz * (size[2] * 0.5 + 0.004)
        if not below_platform(y, size[1]):
            continue
        before = mb.tri_count()
        mb.box(mb.body_name, (x, y, z),
               (max(0.03, size[0]), max(0.03, size[1]), max(0.03, size[2])),
               a["material"], bevel=0.004, segments=1)
        delta = mb.tri_count() - before
        if delta > 0:
            detail_tris += delta
            added_pads += 1
    # structural seam strips: foundation band + top beam band are solid on every
    # tier, so the filler can never cover a window or cross the roof top.
    seam_i = 0
    guard = 0
    while mb.tri_count() < budget * 0.99 and guard < 2000:
        guard += 1
        i = seam_i
        seam_i += 1
        band = i % 2
        y = 0.118 if band == 0 else 0.918
        side = (i // 2) % 4
        index = (i // 8) % 12
        if side == 0:      # back
            mb.box(mb.body_name, (-1.32 + index * 0.24, y, BODY_Z + 0.010),
                   (0.055, 0.042, 0.012), frame)
        elif side == 1:    # front
            mb.box(mb.body_name, (-1.32 + index * 0.24, y, -(BODY_Z + 0.010)),
                   (0.055, 0.042, 0.012), frame)
        elif side == 2:    # left
            mb.box(mb.body_name, (-(BODY_X + 0.010), y, -1.32 + index * 0.24),
                   (0.012, 0.042, 0.055), frame)
        else:              # right
            mb.box(mb.body_name, (BODY_X + 0.010, y, -1.32 + index * 0.24),
                   (0.012, 0.042, 0.055), frame)
        if seam_i > 600:
            # last resort: vertical frame studs on the solid corner posts
            for x in (-POST_X, POST_X):
                for z in (-POST_Z, POST_Z):
                    mb.box(mb.body_name, (x, 0.30 + (i % 5) * 0.10, z),
                           (0.11, 0.05, 0.11), frame)
    return {"added_pads": added_pads, "added_seams": seam_i,
            "detail_tris": detail_tris, "start_tris": start}


# --------------------------------------------------------------------------- #
# flat-patch UV mapping
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
    """M_HDBF_* material slot -> flat-patch role for one model context."""
    key = (tier, faction, construction) if not construction else \
        ("Basic", "Folktails", True)
    mapping = TC.MATERIAL_MAP[key]
    return {slot: MP.role_of(name) for slot, name in mapping.items()}


def _check_patch_coverage(mb, roles, label):
    """Every used material slot must have a flat-patch role (fail loudly)."""
    missing = sorted(set(mb.used_materials()) - set(roles))
    if missing:
        raise RuntimeError("%s uses material slot(s) %s that its tier does not "
                           "declare (roles=%s)" % (label, missing,
                                                   sorted(roles)))


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
    for _attempt in range(10):
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
    """Create one Blender mesh object per logical object + the 3 port empties."""
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
        for i, _loop in enumerate(mesh.loops):
            uvdata[i].uv = uv_flat[i]
        for m in mats_used:
            mesh.materials.append(materials[m])
        for poly, mname in zip(mesh.polygons, mat_idx):
            poly.material_index = mats_used.index(mname)
            poly.use_smooth = False
        # triangulate (deliverable is triangle-only)
        bm = bmesh.new()
        bm.from_mesh(mesh)
        bmesh.ops.triangulate(bm, faces=list(bm.faces), quad_method="BEAUTY",
                              ngon_method="BEAUTY")
        bm.to_mesh(mesh)
        bm.free()
        mesh.update()
        for poly in mesh.polygons:
            poly.use_smooth = False
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
        ob["hdbf_art"] = "HDBF v2 3x3x1 indoor farm / empty flat roof platform"
        created.append(ob)
    # exactly the three #Port# marker empties (no #Slot# nodes anywhere)
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


def _check_envelope(mb, label):
    lo, hi = mb.bounds_asset()
    problems = []
    want_lo = (A.ENVELOPE["min_x"], A.ENVELOPE["min_y"], A.ENVELOPE["min_z"])
    want_hi = (A.ENVELOPE["max_x"], A.ENVELOPE["max_y"], A.ENVELOPE["max_z"])
    for i, axis in enumerate("XYZ"):
        if abs(lo[i] - want_lo[i]) > 1e-4:
            problems.append("min.%s=%.5f want %.3f" % (axis, lo[i], want_lo[i]))
        if abs(hi[i] - want_hi[i]) > 1e-4:
            problems.append("max.%s=%.5f want %.3f" % (axis, hi[i], want_hi[i]))
    if hi[1] > PLATFORM_TOP + 1e-6:
        problems.append("geometry above the roof walking surface: max.y=%.6f"
                        % hi[1])
    if problems:
        raise RuntimeError("%s envelope violated: %s" % (label, "; ".join(problems)))
    return lo, hi


def _frac(v):
    return round(v, 6)


def build_tier_model(tier, faction, lod):
    mb = make_builder(tier, faction, lod)
    style = tier_style(tier)
    bevel = (0.012 if faction == "Folktails" else 0.007) if lod == 0 else 0.0
    add_foundation(mb, style, lod)
    add_facade_walls(mb, style, lod, bevel)
    add_openings(mb, style, faction, lod)
    add_panels(mb, style, tier, faction, lod)
    add_interior_shelves(mb, style, tier, lod)
    add_ports_geometry(mb, style, tier, faction, lod)
    add_roof_platform(mb, style, tier, faction, lod)
    add_base_details(mb, style, tier, faction, lod)
    add_decor(mb, tier, faction, style, lod)
    fill = auto_fill(mb, tier, style, lod)
    roles = patch_roles_for(tier, faction, construction=False)
    _check_patch_coverage(mb, roles, "%s.%s.LOD%d" % (tier, faction, lod))
    assign_flat_patch_uvs(mb, roles)
    lo, hi = _check_envelope(mb, "%s.%s.LOD%d" % (tier, faction, lod))
    info = {
        "tier": tier, "faction": faction, "lod": lod,
        "tris": mb.tri_count(), "budget": TRI_BUDGET[lod],
        "bounds": [round(hi[i] - lo[i], 5) for i in range(3)],
        "min": [round(v, 4) for v in lo], "max": [round(v, 4) for v in hi],
        "materials": mb.used_materials(),
        "materials_to_patch": {slot: MP.role_of(name) for slot, name in
                               TC.MATERIAL_MAP[(tier, faction, False)].items()},
        "platform": {"size_x": style["plan"][0], "size_z": style["plan"][1],
                     "top": PLATFORM_TOP, "curb_h": CURB_H,
                     "clear_above": CLEAR_ABOVE,
                     "fascia_protrusion_above_top": 0.0},
        "facade": {"openings": sum(len(v) for v in style["openings"].values()),
                   "shelf_levels": style["shelf_levels"],
                   "glass_area_hint": A.V2_INDOOR["glass_area_min"][tier]},
        "fill": fill,
    }
    return mb, info


def build_construction_model():
    tier = CONSTRUCTION_TIER
    mb = make_builder(tier, "shared", 0)
    wood, metal = WOOD, METAL
    sx, sz = A.roof_platform(tier)
    body = mb.body_name
    # full 3 x 3 foundation slab (defines the exact envelope)
    mb.box(body, (0.0, FOUND_TOP * 0.5, 0.0), (3.0, FOUND_TOP, 3.0), wood, skip={0})
    for (x, z, w, d) in ((0.0, 1.48, 2.94, 0.04), (0.0, -1.48, 2.94, 0.04),
                         (1.48, 0.0, 0.04, 2.94), (-1.48, 0.0, 0.04, 2.94)):
        mb.box(body, (x, 0.12, z), (w, 0.04, d), metal, skip={0})
    # scaffold posts to the wall line
    for x in (-1.40, 1.40):
        for z in (-1.40, 1.40):
            mb.box(body, (x, 0.52, z), (0.09, 0.84, 0.09), wood)
    # half-built perimeter walls (construction-stage look, all below 0.95)
    mb.box(body, (0.0, 0.34, 1.44), (2.90, 0.48, 0.04), wood)
    mb.box(body, (-1.44, 0.28, 0.0), (0.04, 0.36, 2.86), wood)
    for i in range(3):
        mb.box(body, (-0.90 + i * 0.90, 0.42, -1.44), (0.80, 0.64, 0.04), wood)
    # scaffold cross braces
    for i in range(4):
        z = -1.05 + i * 0.70
        mb.box(body, (1.44, 0.45, z), (0.04, 0.06, 0.60), metal)
    # the same ONE flat empty platform (greybox): slab + flush 0.04 fascia
    mb.box(body, (0.0, (PLATFORM_TOP + PLATFORM_UNDER) * 0.5, 0.0),
           (sx, PLATFORM_THICK, sz), wood, skip={0})
    fy = PLATFORM_TOP - CURB_H * 0.5
    mb.box(body, (0.0, fy, sz * 0.5 - CURB_W * 0.5), (sx, CURB_H, CURB_W), metal)
    mb.box(body, (0.0, fy, -(sz * 0.5 - CURB_W * 0.5)), (sx, CURB_H, CURB_W), metal)
    mb.box(body, (sx * 0.5 - CURB_W * 0.5, fy, 0.0),
           (CURB_W, CURB_H, sz - 2.0 * CURB_W), metal)
    mb.box(body, (-(sx * 0.5 - CURB_W * 0.5), fy, 0.0),
           (CURB_W, CURB_H, sz - 2.0 * CURB_W), metal)
    # platform supports / beams under the slab
    by = PLATFORM_UNDER - 0.028
    for x in (-1.32, 0.0, 1.32):
        mb.box(body, (x, by, 0.0), (0.08, 0.055, sz - 0.10), metal)
    mb.box(body, (0.0, by, 1.32), (sx - 0.10, 0.055, 0.08), metal)
    mb.box(body, (0.0, by, -1.32), (sx - 0.10, 0.055, 0.08), metal)
    # scaffold detail: plank stacks + material pallets
    for i in range(4):
        mb.box(body, (-1.00 + i * 0.62, 0.16, -1.10), (0.42, 0.08, 0.30), wood)
    roles = patch_roles_for(tier, "shared", construction=True)
    _check_patch_coverage(mb, roles, "ConstructionStage0")
    assign_flat_patch_uvs(mb, roles)
    lo, hi = _check_envelope(mb, "ConstructionStage0")
    info = {
        "tier": tier, "faction": "shared", "lod": 0,
        "tris": mb.tri_count(), "budget": None,
        "bounds": [round(hi[i] - lo[i], 5) for i in range(3)],
        "min": [round(v, 4) for v in lo], "max": [round(v, 4) for v in hi],
        "materials": mb.used_materials(),
        "materials_to_patch": {slot: MP.role_of(name) for slot, name in
                               TC.MATERIAL_MAP[("Basic", "Folktails", True)].items()},
        "platform": {"size_x": sx, "size_z": sz, "top": PLATFORM_TOP,
                     "curb_h": CURB_H, "clear_above": CLEAR_ABOVE,
                     "fascia_protrusion_above_top": 0.0},
        "facade": {"openings": 0, "shelf_levels": 0, "glass_area_hint": 0.0},
        "fill": {"added_pads": 0, "added_seams": 0,
                 "start_tris": mb.tri_count(), "detail_tris": 0},
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
        "generated_by": "art/gen_models_v2.py (HDBF v2 3x3x1 indoor farm)",
        "art_revision": "v2",
        "envelope": {
            "description": "strict block-object envelope in asset space (X width, "
                           "Y up, Z depth; front -Z; pivot at footprint bottom centre)",
            "size": [A.ENVELOPE["size_x"], A.ENVELOPE["size_y"],
                     A.ENVELOPE["size_z"]],
            "min": [A.ENVELOPE["min_x"], A.ENVELOPE["min_y"],
                    A.ENVELOPE["min_z"]],
            "max": [A.ENVELOPE["max_x"], A.ENVELOPE["max_y"],
                    A.ENVELOPE["max_z"]],
            "tolerance": A.ENVELOPE_TOLERANCE,
        },
        "roof_platform": {
            "description": "ONE flat empty platform per model. The WALKING SURFACE "
                           "is the model apex at y = 1.00; nothing exists above it; "
                           "the 30 cm above it is empty (stacking safe). The "
                           "perimeter fascia is a flush 0.04 band whose top is the "
                           "walking surface (protrusion above the top = 0.00).",
            "top": PLATFORM_TOP,
            "walking_surface_is_model_apex": True,
            "fascia_height": CURB_H,
            "fascia_width": CURB_W,
            "fascia_protrusion_above_top": 0.0,
            "clear_above": CLEAR_ABOVE,
            "plans": {tier: {"size_x": p["size_x"], "size_z": p["size_z"]}
                      for tier, p in A.ROOF_PLATFORM.items()},
        },
        "indoor": dict(A.V2_INDOOR),
        "ports": [
            {"name": spec["object"], "asset_position": list(spec["asset"]),
             "outward": list(spec["outward"])}
            for spec in A.PORT_SPECS],
        "slots_markers": [],
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
            "uv_space": "flat patch: every face of one material is mapped into "
                        "that material's fixed patch (all uv0 in [0,1])",
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
            "file_stem": ("ConstructionStage0" if construction
                          else "LOD%d" % info["lod"]),
            "materials": info["materials"],
            "triangles": info["tris"],
            "bounds_size": info["bounds"],
            "bounds_min": info["min"],
            "bounds_max": info["max"],
            "material_patches": model_patches,
            "roof_platform": info["platform"],
            "facade": info["facade"],
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
    print("=== HDBF v2 model generation (3x3x1 indoor farm, flat empty roof) ===")
    print("textures present:", {k: os.path.isfile(os.path.join(ART_DIR, v))
                               for k, v in A.ATLAS_FILES.items()})
    print("envelope:", A.ENVELOPE)
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
