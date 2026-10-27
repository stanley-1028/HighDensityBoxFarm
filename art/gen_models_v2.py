"""HDBF Box Farm - v2 procedural model generator (Blender 4.2.1, headless).

**v2 revision (2026-10-26, REQ_V2_SECTION_19):** the four tiers are now all
strict **3 x 3 x 1** indoor high-density farms.  Compared with the ART-04
generator (``art/gen_models.py``, 3x2x1) this generator:

  * builds a strict 3.00 x 1.00 x 3.00 asset-space envelope (front -Z, pivot at
    footprint bottom centre), including decoration, windows, trims and ports;
  * puts ONE flat empty roof platform on top: the WALKING SURFACE is the model
    apex at **y = 1.00**, the perimeter "?嗉器" is a flush 0.04 fascia whose top is
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
import types

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
# v2.2 reference-image upgrade: facade zoning bands
# --------------------------------------------------------------------------- #
#: plinth band (stone/timber base) that sits on the foundation and wraps the
#: building; it defines the dark "base" line under the facade in the references.
PLINTH_LO = 0.10
PLINTH_HI = 0.15
PLINTH_OUT = 1.480
#: heavy roof edge beam ("roof casing") below the platform: this is the thick
#: horizontal band of the reference images.  Its TOP is 0.95 (the platform
#: underside), so it never rises above the flat walking surface y = 1.00.
BEAM_LO = 0.86
BEAM_HI = 0.95
BEAM_OUT = 1.488
#: flushed roof-deck border: a frame-material band coplanar with the walking
#: surface (top = 1.00) so the roof reads as a framed platform, exactly like the
#: references, without any geometry above y = 1.00.
ROOF_BORDER_W = 0.10
#: seam gap between roof planks / metal panels (shows the darker substrate).
ROOF_SEAM = 0.008
#: standard door (front, centred; flush threshold, inside step only).
DOOR_W = 0.62
DOOR_TOP = 0.82


# --------------------------------------------------------------------------- #
# tier style
# --------------------------------------------------------------------------- #
def tier_style(tier, faction="Folktails"):
    """Materials / facade zoning / framing per finished tier.

    v2.2 (2026-10-26, reference-image upgrade): the four tiers follow the seven
    Seedream reference images (`art/references/`), which are **not copied**
    literally where they conflict with the frozen v2 contract (no roof pipes, no
    external steps, no slanted glazing, no scaffold outside 3x3x1):

      * Basic        - dark rough timber barn: heavy wood frame + open bays
                       (no glass), two-span facade, timber plank roof, vanilla
                       moss accents (Folktails) / plank roof (IronTeeth).
      * Intermediate - honey wood frame + large greenhouse glazing (side walls
                       ~50% glass) + cream plaster flat roof.
      * Advanced     - painted-metal frame/shell + large mullioned glazing +
                       riveted metal bands + wood patch plates (steel reads as
                       the dominant material).
      * Top          - dark heavy steel: dense frame ribs, riveted panels,
                       soot/narrow glazing grid, flanges on the rear ports,
                       still a FLAT roof.

    Material slots always equal ``atlas_layout.TIER_MATERIALS`` so the frozen
    1 -> 2 -> 3 -> 4 material progression is preserved:
      Basic        wood + moss/roof-planks                   (2)
      Intermediate planks + plaster + glass                  (3)
      Advanced     timber + painted metal + glass + details  (4)
      Top          dark timber + base metal + glass + plates (4)

    The four tiers differ by opening layout / glazing ratio, framing density,
    material layering and port fixtures: any two of the 50%-camera criteria
    {silhouette, material, windows, ports} differ.
    """
    sx, sz = A.roof_platform(tier)
    style = dict(
        tier=tier, faction=faction, plan=(sx, sz),
        wall=WOOD, frame=WOOD, foundation=WOOD, roof=WOOD, door=WOOD,
        shelf=WOOD, panel=None, accent=WOOD, crop=WOOD, moss=None,
        metal_frame=False, heavy=False, shelf_levels=3,
        roof_style="planks", door_window=False, braces=False,
        rivets=0, plates=0, louver=False, backing=None, rafter_ends=False,
        trim_width=0.036,
    )

    if tier == "Basic":
        style.update(dict(
            metal_frame=False, heavy=False, roof_style="planks",
            door_window=False, braces=True, rivets=0, plates=0,
            louver=False, rafter_ends=True,
            opening_trim=0.040, trim_width=0.040,
            openings={
                "front": [
                    {"rect": (-0.31, 0.31, 0.10, 0.80), "kind": "door"},
                    {"rect": (-1.30, -0.42, 0.26, 0.80), "kind": "open"},
                    {"rect": (0.42, 1.30, 0.26, 0.80), "kind": "open"},
                ],
                "left": [
                    {"rect": (-1.28, -0.16, 0.30, 0.80), "kind": "open"},
                    {"rect": (0.16, 1.28, 0.30, 0.80), "kind": "open"},
                ],
                "right": [
                    {"rect": (-1.28, -0.16, 0.30, 0.80), "kind": "open"},
                    {"rect": (0.16, 1.28, 0.30, 0.80), "kind": "open"},
                ],
                "back": [],
            },
            bands=[
                ("front", -1.32, 1.32, 0.15, 0.20),
                ("back", -1.32, 1.32, 0.15, 0.20),
                ("left", -1.32, 1.32, 0.15, 0.20),
                ("right", -1.32, 1.32, 0.15, 0.20),
            ],
        ))
        if faction == "Folktails":
            # vanilla moss material: moss tufts + the crop silhouette behind the
            # open bays (the only tier with a dedicated green material slot).
            style.update(moss=FABRIC, crop=FABRIC, roof=WOOD,
                         foundation=WOOD)
        else:
            style.update(moss=None, crop=WOOD, roof=FABRIC, foundation=WOOD)
        return style

    if tier == "Intermediate":
        style.update(dict(
            wall=WOOD, frame=WOOD, foundation=WOOD, roof=FABRIC,
            door=WOOD, shelf=WOOD, panel=FABRIC, accent=WOOD,
            crop=WOOD, backing=FABRIC, metal_frame=False, heavy=False,
            roof_style="plaster", trim_width=0.034,
            openings={
                "front": [
                    {"rect": (-0.31, 0.31, 0.10, 0.82), "kind": "door"},
                    {"rect": (-1.32, -0.44, 0.22, 0.80), "kind": "glass",
                     "grid": (2, 2)},
                    {"rect": (0.44, 1.32, 0.22, 0.80), "kind": "glass",
                     "grid": (2, 2)},
                ],
                "left": [
                    {"rect": (-1.32, -0.18, 0.22, 0.80), "kind": "glass",
                     "grid": (2, 2)},
                    {"rect": (0.18, 1.32, 0.22, 0.80), "kind": "glass",
                     "grid": (2, 2)},
                ],
                "right": [
                    {"rect": (-1.32, -0.18, 0.22, 0.80), "kind": "glass",
                     "grid": (2, 2)},
                    {"rect": (0.18, 1.32, 0.22, 0.80), "kind": "glass",
                     "grid": (2, 2)},
                ],
                "back": [
                    {"rect": (-1.30, 0.70, 0.62, 0.80), "kind": "glass",
                     "grid": (3, 1)},
                ],
            },
            bands=[
                ("front", -1.32, 1.32, 0.15, 0.20),
                ("front", -1.32, 1.32, 0.82, 0.86),
                ("back", -1.32, 0.80, 0.15, 0.20),
                ("back", -1.32, 0.80, 0.82, 0.86),
                ("left", -1.32, 1.32, 0.15, 0.20),
                ("left", -1.32, 1.32, 0.82, 0.86),
                ("right", -1.32, 1.32, 0.15, 0.20),
                ("right", -1.32, 1.32, 0.82, 0.86),
            ],
        ))
        return style

    if tier == "Advanced":
        style.update(dict(
            wall=METAL, frame=METAL, foundation=METAL, roof=METAL,
            door=METAL, shelf=WOOD, panel=FABRIC, accent=WOOD, crop=WOOD,
            backing=FABRIC, metal_frame=True, heavy=False, roof_style="metal",
            door_window=True, rivets=1, plates=6, trim_width=0.030,
            openings={
                "front": [
                    {"rect": (-0.30, 0.30, 0.10, 0.82), "kind": "door"},
                    {"rect": (-1.30, -0.58, 0.20, 0.84), "kind": "glass",
                     "grid": (2, 2)},
                    {"rect": (0.58, 1.30, 0.20, 0.84), "kind": "glass",
                     "grid": (2, 2)},
                ],
                "left": [
                    {"rect": (-1.26, -0.24, 0.20, 0.84), "kind": "glass",
                     "grid": (2, 2)},
                    {"rect": (0.24, 1.26, 0.20, 0.84), "kind": "glass",
                     "grid": (2, 2)},
                ],
                "right": [
                    {"rect": (-1.26, -0.24, 0.20, 0.84), "kind": "glass",
                     "grid": (2, 2)},
                    {"rect": (0.24, 1.26, 0.20, 0.84), "kind": "glass",
                     "grid": (2, 2)},
                ],
                "back": [
                    {"rect": (-1.30, 0.72, 0.62, 0.80), "kind": "glass",
                     "grid": (3, 1)},
                ],
            },
            bands=[
                ("front", -1.32, 1.32, 0.15, 0.20),
                ("front", -1.32, 1.32, 0.84, 0.86),
                ("back", -1.32, 0.80, 0.15, 0.20),
                ("left", -1.32, 1.32, 0.15, 0.20),
                ("right", -1.32, 1.32, 0.15, 0.20),
            ],
            patch_plates=[
                ("front", -0.46, 0.30, 0.20, 0.16),
                ("front", 0.46, 0.30, 0.20, 0.16),
                ("left", 0.00, 0.34, 0.30, 0.18),
                ("right", 0.00, 0.34, 0.30, 0.18),
                ("back", 0.15, 0.28, 0.30, 0.18),
            ],
        ))
        return style

    # Top: dark heavy industrial steel, dense frame, riveted panels, flat roof.
    style.update(dict(
        wall=METAL, frame=METAL, foundation=METAL, roof=METAL,
        door=METAL, shelf=METAL, panel=FABRIC, accent=WOOD, crop=FABRIC,
        backing=WOOD, metal_frame=True, heavy=True, roof_style="heavy",
        door_window=True, rivets=2, plates=8, louver=True, trim_width=0.028,
        openings={
            "front": [
                {"rect": (-0.30, 0.30, 0.10, 0.82), "kind": "door"},
                {"rect": (-1.34, -0.52, 0.20, 0.72), "kind": "glass",
                 "grid": (3, 3)},
                {"rect": (0.52, 1.34, 0.20, 0.72), "kind": "glass",
                 "grid": (3, 3)},
            ],
            "left": [
                {"rect": (-1.34, -0.50, 0.20, 0.72), "kind": "glass",
                 "grid": (2, 3)},
                {"rect": (-0.42, 0.42, 0.20, 0.72), "kind": "glass",
                 "grid": (2, 3)},
                {"rect": (0.50, 1.34, 0.20, 0.72), "kind": "glass",
                 "grid": (2, 3)},
            ],
            "right": [
                {"rect": (-1.34, -0.50, 0.20, 0.72), "kind": "glass",
                 "grid": (2, 3)},
                {"rect": (-0.42, 0.42, 0.20, 0.72), "kind": "glass",
                 "grid": (2, 3)},
                {"rect": (0.50, 1.34, 0.20, 0.72), "kind": "glass",
                 "grid": (2, 3)},
            ],
            "back": [],
        },
        bands=[
            ("front", -1.32, 1.32, 0.15, 0.20),
            ("front", -1.32, 1.32, 0.74, 0.86),
            ("back", -1.32, 1.32, 0.15, 0.20),
            ("back", -1.32, 1.32, 0.74, 0.86),
            ("left", -1.32, 1.32, 0.15, 0.20),
            ("left", -1.32, 1.32, 0.74, 0.86),
            ("right", -1.32, 1.32, 0.15, 0.20),
            ("right", -1.32, 1.32, 0.74, 0.86),
        ],
        patch_plates=[
            ("front", -0.43, 0.26, 0.14, 0.14),
            ("front", 0.43, 0.26, 0.14, 0.14),
            ("back", 0.15, 0.26, 0.34, 0.16),
        ],
    ))
    return style


def font_style(tier, faction="Folktails"):
    return tier_style(tier, faction)


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


#: in-plane horizontal axis (asset space) and outward normal per facade.
PLANE_AXIS = {
    "front": (1.0, 0.0, 0.0),
    "back": (1.0, 0.0, 0.0),
    "left": (0.0, 0.0, 1.0),
    "right": (0.0, 0.0, 1.0),
}
PLANE_NORMAL = {
    "front": (0.0, 0.0, -1.0),
    "back": (0.0, 0.0, 1.0),
    "left": (-1.0, 0.0, 0.0),
    "right": (1.0, 0.0, 0.0),
}


def oriented_box(mb, center, basis_u, basis_v, basis_d, size, material):
    """A box with an arbitrary (right-handed) asset-space basis.

    ``size`` is (u_len, v_len, d_len) along the three basis vectors.  Used for
    the diagonal timber braces of the reference barn (an axis-aligned box cannot
    express them); the winding is built by bmesh so the outward normals stay
    outward after the export axis conversion.
    """
    bu, bv, bd = to_bl_vec(basis_u), to_bl_vec(basis_v), to_bl_vec(basis_d)
    rot = Matrix(((bu[0], bv[0], bd[0]),
                  (bu[1], bv[1], bd[1]),
                  (bu[2], bv[2], bd[2]))).to_quaternion().to_euler()
    bm = bmesh.new()
    mat = (Matrix.Translation(Vector(to_bl(center)))
           @ rot.to_matrix().to_4x4()
           @ Matrix.Diagonal(Vector((size[0], size[1], size[2], 1.0))))
    bmesh.ops.create_cube(bm, size=1.0, matrix=mat)
    mb._append_bmesh(mb.body_name, bm, material)


def diag_brace(mb, plane, a0, y0, a1, y1, thickness, depth, offset, material):
    """Diagonal brace inside a facade plane (reference-image timber bracing)."""
    e_a = PLANE_AXIS[plane]
    d = PLANE_NORMAL[plane]
    v = (0.0, 1.0, 0.0)
    u0 = (v[1] * d[2] - v[2] * d[1],
          v[2] * d[0] - v[0] * d[2],
          v[0] * d[1] - v[1] * d[0])          # u0 = v x d
    da, dy = a1 - a0, y1 - y0
    length = math.sqrt(da * da + dy * dy)
    if length < 1e-6:
        return
    dx = (e_a[0] * da, e_a[1] * da, e_a[2] * da)
    du = (dx[0] + 0.0, dx[1] + dy, dx[2] + 0.0)
    dl = math.sqrt(du[0] ** 2 + du[1] ** 2 + du[2] ** 2)
    du = (du[0] / dl, du[1] / dl, du[2] / dl)
    cos_t = du[0] * u0[0] + du[1] * u0[1] + du[2] * u0[2]
    sin_t = du[0] * v[0] + du[1] * v[1] + du[2] * v[2]
    u = (u0[0] * cos_t + v[0] * sin_t,
         u0[1] * cos_t + v[1] * sin_t,
         u0[2] * cos_t + v[2] * sin_t)
    vv = (-u0[0] * sin_t + v[0] * cos_t,
          -u0[1] * sin_t + v[1] * cos_t,
          -u0[2] * sin_t + v[2] * cos_t)
    ac, yc = (a0 + a1) * 0.5, (y0 + y1) * 0.5
    center = (e_a[0] * ac + d[0] * offset,
              yc + d[1] * offset,
              e_a[2] * ac + d[2] * offset)
    oriented_box(mb, center, u, vv, d, (length, thickness, depth), material)


# --------------------------------------------------------------------------- #
# reference-image facade vocabulary
# --------------------------------------------------------------------------- #
def opening_frame(mb, plane, rect, material, bar, depth, offset, lod):
    """Rectangular casing around one opening (4 bars)."""
    a0, a1, y0, y1 = rect
    ac, aw = (a0 + a1) * 0.5, a1 - a0
    yc, yh = (y0 + y1) * 0.5, y1 - y0
    facade_box(mb, plane, ac, aw, y0 + bar * 0.5, bar, depth, offset, material)
    facade_box(mb, plane, ac, aw, y1 - bar * 0.5, bar, depth, offset, material)
    facade_box(mb, plane, a0 + bar * 0.5, bar, yc, yh, depth, offset, material)
    facade_box(mb, plane, a1 - bar * 0.5, bar, yc, yh, depth, offset, material)


def add_glass_bay(mb, style, plane, rect, grid, lod):
    """Large glazed bay: pane + heavy casing + reference-style mullion grid.

    The mullion grid is what makes the greenhouse / industrial curtain-wall
    zoning of the reference images read at the 50% camera.
    """
    a0, a1, y0, y1 = rect
    ac, aw = (a0 + a1) * 0.5, a1 - a0
    yc, yh = (y0 + y1) * 0.5, y1 - y0
    frame = style["frame"]
    bar = style["trim_width"]
    facade_box(mb, plane, ac, max(0.02, aw - 0.012), yc,
               max(0.02, yh - 0.012), 0.012, PANE_OFFSET, GLASS)
    opening_frame(mb, plane, rect, frame, bar, 0.022, FRAME_OFFSET, lod)
    if lod > 0:
        return
    cols, rows = grid
    # vertical mullions
    for i in range(1, max(1, cols)):
        aa = a0 + (a1 - a0) * i / float(cols)
        facade_box(mb, plane, aa, bar * 0.72, yc, yh, 0.018,
                   FRAME_OFFSET + 0.004, frame)
    # horizontal transoms
    for i in range(1, max(1, rows)):
        yy = y0 + (y1 - y0) * i / float(rows)
        facade_box(mb, plane, ac, aw, yy, bar * 0.66, 0.018,
                   FRAME_OFFSET + 0.004, frame)


def add_wood_door(mb, style, plane, rect, lod):
    """Wooden plank door (Basic / Intermediate), flush threshold, inner step."""
    a0, a1, y0, y1 = rect
    ac, aw = (a0 + a1) * 0.5, a1 - a0
    yc, yh = (y0 + y1) * 0.5, y1 - y0
    frame, door = style["frame"], style["door"]
    casing = style["trim_width"] + 0.012
    opening_frame(mb, plane, rect, frame, casing, 0.030, FRAME_OFFSET, lod)
    # leaf
    facade_box(mb, plane, ac, aw - 0.03, yc, yh - 0.02, 0.030,
               PANE_OFFSET + 0.004, door)
    if lod == 0:
        # three horizontal boards + two stiles + handle + hinges
        for i in range(3):
            facade_box(mb, plane, ac, aw - 0.10, y0 + 0.14 + i * (yh - 0.16) / 2.6,
                       0.020, 0.012, FRAME_OFFSET + 0.012, frame)
        for sgn in (-1, 1):
            facade_box(mb, plane, ac + sgn * (aw * 0.5 - 0.09), 0.055,
                       yc, yh - 0.06, 0.014, FRAME_OFFSET + 0.010, door)
        facade_box(mb, plane, ac + aw * 0.30, 0.075, yc - 0.02, 0.045,
                   0.020, FRAME_OFFSET + 0.020, style["accent"])
        for yy in (y0 + 0.20, y1 - 0.22):
            facade_box(mb, plane, a0 + 0.045, 0.050, yy, 0.060, 0.014,
                       FRAME_OFFSET + 0.020, frame)


def add_metal_door(mb, style, plane, rect, lod):
    """Metal door (Advanced / Top) with riveted ribs and a small wired pane."""
    a0, a1, y0, y1 = rect
    ac, aw = (a0 + a1) * 0.5, a1 - a0
    yc, yh = (y0 + y1) * 0.5, y1 - y0
    frame, door = style["frame"], style["door"]
    casing = style["trim_width"] + 0.014
    opening_frame(mb, plane, rect, frame, casing, 0.034, FRAME_OFFSET, lod)
    facade_box(mb, plane, ac, aw - 0.03, yc, yh - 0.02, 0.034,
               PANE_OFFSET + 0.004, door)
    if lod == 0:
        # vertical press ribs
        for sgn in (-1, 1):
            facade_box(mb, plane, ac + sgn * (aw * 0.24), 0.045,
                       yc, yh - 0.05, 0.014, FRAME_OFFSET + 0.014, frame)
        facade_box(mb, plane, ac, aw - 0.12, y0 + 0.16, 0.030, 0.014,
                   FRAME_OFFSET + 0.014, frame)
        # rivets on the door edge rails
        for i in range(4):
            for sgn in (-1, 1):
                facade_box(mb, plane, ac + sgn * (aw * 0.5 - 0.055),
                           0.024, y0 + 0.12 + i * (yh - 0.24) / 3.0, 0.024,
                           0.010, FRAME_OFFSET + 0.024, frame)
        # handle / lock plate
        facade_box(mb, plane, ac + aw * 0.27, 0.06, yc - 0.02, 0.05,
                   0.020, FRAME_OFFSET + 0.024, style["accent"])
        if style["door_window"]:
            facade_box(mb, plane, ac, aw * 0.46, yc + yh * 0.22, yh * 0.30,
                       0.012, PANE_OFFSET + 0.020, GLASS)
            facade_box(mb, plane, ac, aw * 0.50, yc + yh * 0.22, 0.026,
                       0.016, FRAME_OFFSET + 0.020, frame)


def add_door_threshold(mb, style, plane, rect, lod):
    """Flush threshold + INSIDE step (never a step outside the 3x3 footprint)."""
    a0, a1, y0, y1 = rect
    if y0 > 0.11:
        return
    frame = style["frame"]
    spec = PLANES[plane]
    # flush sill plate on the foundation ledge (still inside +-1.50)
    if spec["axis"] == "z":
        mb.box(mb.body_name, ((a0 + a1) * 0.5, 0.112, spec["sign"] * 1.466),
               (a1 - a0 + 0.06, 0.024, 0.060), frame)
        # inside step (interior, behind the wall plane)
        mb.box(mb.body_name, ((a0 + a1) * 0.5, 0.132, spec["sign"] * 1.286),
               (a1 - a0 - 0.04, 0.064, 0.180), style["door"])
    else:
        mb.box(mb.body_name, (spec["sign"] * 1.466, 0.112, (a0 + a1) * 0.5),
               (0.060, 0.024, a1 - a0 + 0.06), frame)
        mb.box(mb.body_name, (spec["sign"] * 1.286, 0.132, (a0 + a1) * 0.5),
               (0.180, 0.064, a1 - a0 - 0.04), style["door"])


def add_open_bay(mb, style, plane, rect, lod):
    """Open (un-glazed) timber bay: casing, sill board, cross rail, brace."""
    a0, a1, y0, y1 = rect
    frame = style["frame"]
    if lod == 2:
        return
    opening_frame(mb, plane, rect, frame, style["trim_width"], 0.030,
                  FRAME_OFFSET, lod)
    if lod == 0:
        facade_box(mb, plane, (a0 + a1) * 0.5, a1 - a0 + 0.06, y0 - 0.034,
                   0.038, 0.055, FRAME_OFFSET + 0.002, frame)
        # one cross rail across the bay (barn window look)
        facade_box(mb, plane, (a0 + a1) * 0.5, a1 - a0 + 0.02,
                   (y0 + y1) * 0.22 + y0 * 0.78, 0.030, 0.024,
                   FRAME_OFFSET + 0.004, frame)


def add_facade_walls(mb, style, lod, bevel):
    """Build the four facades with their openings cut out of solid wall panels.

    v2.2 reference-image upgrade adds the three horizontal zoning lines the ref
    images are built from: the dark plinth band at the base, the heavy roof edge
    beam below the platform, and corner capitals at the roof corners.  All three
    stay strictly inside the 3x3x1 envelope and below the walking surface.
    """
    body = mb.body_name
    material = style["wall"]
    frame = style["frame"]
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
            # wall panels stay unbevelled: beveling every panel triplet in a
            # heavily-windowed facade blows the 6000-tri LOD0 budget
            facade_box(mb, plane, ac, aw, yc, yh, WALL_T, 0.0, material)
    # ---- plinth band: dark base line of the references ---- #
    ph = PLINTH_HI - PLINTH_LO
    py = PLINTH_LO + ph * 0.5
    for (x, z, sx, sz) in ((0.0, PLINTH_OUT - 0.025, 2.0 * PLINTH_OUT, 0.05),
                           (0.0, -(PLINTH_OUT - 0.025), 2.0 * PLINTH_OUT, 0.05),
                           (PLINTH_OUT - 0.025, 0.0, 0.05, 2.0 * PLINTH_OUT - 0.10),
                           (-(PLINTH_OUT - 0.025), 0.0, 0.05,
                            2.0 * PLINTH_OUT - 0.10)):
        mb.box(body, (x, py, z), (sx, ph, sz), frame, skip={0})
    # ---- roof edge beam: the thick horizontal band of the references ---- #
    bh = BEAM_HI - BEAM_LO
    by = BEAM_LO + bh * 0.5
    for (x, z, sx, sz) in ((0.0, BEAM_OUT - 0.028, 2.0 * BEAM_OUT, 0.056),
                           (0.0, -(BEAM_OUT - 0.028), 2.0 * BEAM_OUT, 0.056),
                           (BEAM_OUT - 0.028, 0.0, 0.056, 2.0 * BEAM_OUT - 0.112),
                           (-(BEAM_OUT - 0.028), 0.0, 0.056,
                            2.0 * BEAM_OUT - 0.112)):
        mb.box(body, (x, by, z), (sx, bh, sz), frame, skip={0})
    if lod == 0:
        # beam splines / joint blocks: short vertical strips on the beam face
        for plane in ("front", "back"):
            for i in range(4):
                aa = -1.20 + i * 0.72
                facade_box(mb, plane, aa, 0.045, by, bh * 0.82, 0.014,
                           BEAM_OUT + 0.004, style["panel"] or frame)
        for plane in ("left", "right"):
            for i in range(4):
                aa = -1.20 + i * 0.72
                facade_box(mb, plane, aa, 0.045, by, bh * 0.82, 0.014,
                           BEAM_OUT + 0.004, style["panel"] or frame)
    # corner posts (vertical, on the four footprint corners)
    post_h = WALL_TOP - FOUND_TOP
    for x in (-POST_X, POST_X):
        for z in (-POST_Z, POST_Z):
            mb.box(body, (x, FOUND_TOP + post_h * 0.5, z),
                   (0.12, post_h, 0.12), frame,
                   bevel=bevel if lod == 0 else 0.0,
                   anchor=((1 if x > 0 else -1, 0, 0), (0.03, 0.03, 0.03)))
    # corner capitals (roof corner blocks) - inside the envelope, below the top
    if lod == 0:
        cap = 0.17
        for x in (-1.395, 1.395):
            for z in (-1.395, 1.395):
                mb.box(body, (x, BEAM_LO + 0.045, z), (cap, 0.115, cap), frame)
    # top perimeter beam ring below the roof platform (structural read)
    beam_y = WALL_TOP - 0.05
    for (x, z, sx, sz) in ((0.0, BODY_Z - 0.03, 2.88, 0.07),
                           (0.0, -(BODY_Z - 0.03), 2.88, 0.07),
                           (BODY_X - 0.03, 0.0, 0.07, 2.88),
                           (-(BODY_X - 0.03), 0.0, 0.07, 2.88)):
        mb.box(body, (x, beam_y, z), (sx, 0.07, sz), frame,
               anchor=((0, 1, 0), (0.05, 0.03, 0.05)))


def add_openings(mb, style, faction, lod):
    """Glass panes, door leaves, frames, louvres and timber bracing per tier."""
    frame = style["frame"]
    for plane in ("front", "back", "left", "right"):
        for op in style["openings"].get(plane, []):
            rect = op["rect"]
            kind = op["kind"]
            if kind == "glass":
                if lod == 2:
                    a0, a1, y0, y1 = rect
                    facade_box(mb, plane, (a0 + a1) * 0.5, a1 - a0,
                               (y0 + y1) * 0.5, y1 - y0, 0.012, PANE_OFFSET,
                               GLASS)
                else:
                    add_glass_bay(mb, style, plane, rect,
                                  op.get("grid", (2, 1)), lod)
            elif kind == "door":
                if lod == 2:
                    a0, a1, y0, y1 = rect
                    facade_box(mb, plane, (a0 + a1) * 0.5, a1 - a0 - 0.02,
                               (y0 + y1) * 0.5, y1 - y0 - 0.01, 0.024,
                               PANE_OFFSET + 0.006, style["door"])
                else:
                    if style["metal_frame"]:
                        add_metal_door(mb, style, plane, rect, lod)
                    else:
                        add_wood_door(mb, style, plane, rect, lod)
                    add_door_threshold(mb, style, plane, rect, lod)
            elif kind == "open":
                add_open_bay(mb, style, plane, rect, lod)
    # Top tier: louver grille band on the rear wall (below the roofline, above
    # the port fixtures - the reference's industrial louvre bank).
    if style.get("louver") and lod == 0:
        for i in range(4):
            yy = 0.60 + i * 0.050
            facade_box(mb, "back", -0.02, 1.16, yy, 0.022, 0.022,
                       FRAME_OFFSET + 0.008, frame)
    # Basic barn: diagonal timber bracing at the open bays (reference language)
    if style.get("braces") and lod == 0:
        braces = [
            ("front", -1.26, 0.29, -0.52, 0.77),
            ("front", 1.26, 0.29, 0.52, 0.77),
            ("left", -1.24, 0.33, -0.24, 0.77),
            ("left", 1.24, 0.33, 0.24, 0.77),
            ("right", -1.24, 0.33, -0.24, 0.77),
            ("right", 1.24, 0.33, 0.24, 0.77),
        ]
        for (plane, a0, y0, a1, y1) in braces:
            diag_brace(mb, plane, a0, y0, a1, y1, 0.055, 0.020,
                       FRAME_OFFSET + 0.004, frame)


def solid_spans(style, plane, y0, y1, a_lo=-1.42, a_hi=1.42):
    """Horizontal spans of a facade that are SOLID in the y band [y0, y1].

    Used so base boards / bolt rows / auto-fill seams never run across a door or
    a window (the reference upgrade adds many large openings).
    """
    blocks = []
    for op in style["openings"].get(plane, []):
        oa0, oa1, oy0, oy1 = op["rect"]
        if oy1 > y0 + 1e-9 and oy0 < y1 - 1e-9:
            blocks.append((max(a_lo, oa0), min(a_hi, oa1)))
    blocks.sort()
    spans, pos = [], a_lo
    for b0, b1 in blocks:
        if b0 > pos + 1e-6:
            spans.append((pos, b0))
        pos = max(pos, b1)
    if a_hi > pos + 1e-6:
        spans.append((pos, a_hi))
    return spans


def add_panels(mb, style, tier, faction, lod):
    """Material-zoning bands + riveted wood patch plates (reference layering).

    Intermediate carries cream plaster bands (roof band + sill band); Advanced /
    Top carry riveted metal detail bands and *wood patch plates* bolted onto the
    metal shell - the reference-image "metal shell + wooden patches" layering.
    """
    frame = style["frame"]
    panel = style["panel"]
    if panel:
        for (plane, a0, a1, y0, y1) in style.get("bands", []):
            facade_box(mb, plane, (a0 + a1) * 0.5, a1 - a0,
                       (y0 + y1) * 0.5, y1 - y0, 0.018,
                       FRAME_OFFSET + 0.004, panel)
            if lod == 0 and style["metal_frame"]:
                n = max(3, int((a1 - a0) / 0.26))
                for i in range(n):
                    aa = a0 + 0.08 + i * (a1 - a0 - 0.16) / max(1, n - 1)
                    facade_box(mb, plane, aa, 0.020, (y0 + y1) * 0.5, 0.020,
                               0.010, FRAME_OFFSET + 0.016, frame)
    plates = style.get("patch_plates", [])
    if plates:
        max_plates = len(plates) if lod == 0 else min(3, len(plates))
        for (plane, ac, yc, pw, ph) in plates[:max_plates]:
            facade_box(mb, plane, ac, pw, yc, ph, 0.020,
                       FRAME_OFFSET + 0.006, style["accent"])
            if lod == 0:
                for (dx, dy) in ((-1, -1), (1, 1)):
                    facade_box(mb, plane, ac + dx * pw * 0.36, 0.018,
                               yc + dy * ph * 0.32, 0.018, 0.010,
                               FRAME_OFFSET + 0.020, frame)
                facade_box(mb, plane, ac, pw + 0.02, yc - ph * 0.5 - 0.010,
                           0.014, 0.012, FRAME_OFFSET + 0.014, frame)


# --------------------------------------------------------------------------- #
# interior: static high-density racks (v2 "indoor" expression)
# --------------------------------------------------------------------------- #
def add_interior_shelves(mb, style, tier, lod):
    """Static 3-level racks + crop silhouette visible through the openings.

    These are DECORATION only: no crop entities, no dynamic slot meshes, no
    clickable geometry; the simulation never reads this geometry (REQ-V2-ART-04).
    The reference-image interior cutaway is expressed as dense trays with rows of
    small crop tufts - a *silhouette* of "multi-level racks + crop rows", never
    600 individual slot models.
    """
    body = mb.body_name
    levels = style["shelf_levels"]
    board = style["shelf"]
    frame = style["frame"]
    crop = style["crop"]
    heavy = style["metal_frame"]
    # interior back panel: lifts the rack/crop silhouette behind the glazing
    if style.get("backing") and lod <= 1:
        mb.box(body, (0.0, 0.50, 1.385), (2.62, 0.70, 0.030), style["backing"])
    # two rack banks inside the front half so the racks read as silhouettes
    banks = ((-1.24, -0.08), (0.08, 1.24))
    z0, z1 = -1.32, 0.30
    if lod == 2:
        # LOD2 massing: one rack block (keeps the shelf material slot alive)
        mb.box(body, (0.0, FOUND_TOP + 0.34, (z0 + z1) * 0.5),
               (2.50, 0.54, z1 - z0 - 0.04), board)
        return
    for bi, (xa, xb) in enumerate(banks):
        xc, xw = (xa + xb) * 0.5, xb - xa
        if lod <= 1:
            for x in (xa, xb):
                mb.box(body, (x, FOUND_TOP + 0.34, (z0 + z1) * 0.5),
                       (0.06, 0.68, z1 - z0 + 0.06), frame)
        for li in range(levels):
            y = 0.235 + li * 0.19
            mb.box(body, (xc, y, (z0 + z1) * 0.5),
                   (xw - 0.06, 0.030, z1 - z0 - 0.04), board)
            if lod == 0:
                # shelf edge rail (silhouette detail)
                mb.box(body, (xc, y + 0.026, z1 - 0.01),
                       (xw - 0.06, 0.018, 0.028), frame)
                # deterministic crop rows: tray + leaf tuft(s)
                n = 3 if heavy else 2
                for ci in range(n):
                    cx = xa + 0.16 + ci * ((xw - 0.32) / max(1, n - 1))
                    cz = z0 + 0.16 + 0.42 * h01(tier, bi, li, ci, "z")
                    mb.box(body, (cx, y + 0.040, cz), (0.13, 0.045, 0.12),
                           board)
                    mb.box(body, (cx, y + 0.105, cz), (0.10, 0.090, 0.09),
                           crop)
                    if (ci + li) % 2 == 0:
                        mb.box(body, (cx + 0.030, y + 0.128, cz),
                               (0.045, 0.065, 0.055), crop)
                # crates between the crop rows
                if h01(tier, bi, li, "crate") < 0.45:
                    mb.box(body, (xc, y + 0.070, z0 + 0.16),
                           (0.16, 0.115, 0.16), board)
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
                mb.box(body, (x, 0.13, z), (0.20, 0.06, 0.20), style["frame"])


def add_base_details(mb, style, tier, faction, lod):
    """Base boards, plank seams, bolt rows, panel joints and corner brackets."""
    body = mb.body_name
    if lod > 1:
        return
    wall = style["wall"]
    frame = style["frame"]
    panel = style["panel"]
    heavy = style["metal_frame"]
    bevel = 0.010 if faction == "Folktails" else 0.006
    # base board line right above the plinth (skips the door bay)
    for plane in ("front", "back", "left", "right"):
        for (s0, s1) in solid_spans(style, plane, 0.15, 0.18):
            if s1 - s0 < 0.14:
                continue
            facade_box(mb, plane, (s0 + s1) * 0.5, s1 - s0 - 0.02,
                       0.162, 0.026, 0.018, FRAME_OFFSET - 0.010, frame)
    # vertical plank seams on the back wall + the side walls (timber tiers only:
    # the glazed / metal shells do not carry plank seams on their facades)
    if lod == 0 and not heavy:
        for i in range(8):
            x = -1.26 + i * (2.52 / 7.0)
            mb.box(body, (x, 0.52, BODY_Z + 0.006),
                   (0.018, 0.70, 0.010), wall)
        for sgn in (-1, 1):
            for i in range(6):
                z = -1.22 + i * (2.44 / 5.0)
                mb.box(body, (sgn * (BODY_X + 0.006), 0.52, z),
                       (0.010, 0.70, 0.018), wall)
    # metal shell: vertical joint strips on the solid piers
    if lod == 0 and heavy:
        for plane in ("front", "back"):
            for (s0, s1) in solid_spans(style, plane, 0.20, 0.84):
                if s1 - s0 < 0.10:
                    continue
                for k in range(1 + int((s1 - s0) // 0.34)):
                    aa = s0 + (s1 - s0) * (0.5 + (k - 1) * 0.34)
                    if aa < s0 + 0.02 or aa > s1 - 0.02:
                        continue
                    facade_box(mb, plane, aa, 0.020, 0.50, 0.66, 0.012,
                               FRAME_OFFSET + 0.002, panel or frame)
    # foundation bolt rows on the plinth face (never across an opening)
    n_bolt = 6 if lod == 0 else 4
    for plane in ("front", "back", "left", "right"):
        for (s0, s1) in solid_spans(style, plane, 0.10, 0.15):
            if s1 - s0 < 0.10:
                continue
            n = max(2, min(n_bolt, 1 + int((s1 - s0) / 0.30)))
            for i in range(n):
                aa = s0 + 0.06 + i * (s1 - s0 - 0.12) / max(1, n - 1)
                facade_box(mb, plane, aa, 0.030, 0.118, 0.030, 0.012,
                           PLINTH_OUT + 0.004, frame)
    # roof beam bolt / rivet rows
    n_beam = 6 if lod == 0 else 4
    for plane in ("front", "back", "left", "right"):
        for (s0, s1) in solid_spans(style, plane, 0.86, 0.95):
            if s1 - s0 < 0.10:
                continue
            n = max(2, min(n_beam, 1 + int((s1 - s0) / 0.30)))
            for i in range(n):
                aa = s0 + 0.06 + i * (s1 - s0 - 0.12) / max(1, n - 1)
                facade_box(mb, plane, aa, 0.028, 0.905, 0.028, 0.012,
                           BEAM_OUT + 0.004, frame)
    # corner brackets
    for x in (-POST_X, POST_X):
        for z in (-POST_Z, POST_Z):
            heights = (0.18, 0.80) if lod == 0 else (0.80,)
            for yy in heights:
                mb.box(body, (x, yy, z), (0.14, 0.05, 0.14), frame)
            mb.anchor((x + (0.05 if x > 0 else -0.05), 0.52, z),
                      (1 if x > 0 else -1, 0, 0), (0.030, 0.030, 0.030), frame)


# --------------------------------------------------------------------------- #
# ports: exactly the three #Port# fixtures
# --------------------------------------------------------------------------- #
def add_ports_geometry(mb, style, tier, faction, lod):
    """The three rear/side port fixtures (markers are frozen; fixtures vary)."""
    body = mb.body_name
    frame = style["frame"]
    metal = style["metal_frame"]
    heavy = style["heavy"]
    # ---------------- pipe port, left side x = -1.50 ----------------------
    cover = not metal
    mb.box(body, (-1.462, 0.40, 0.0), (0.026, 0.30, 0.30),
           style["wall"] if cover else METAL, skip={0})
    if lod == 0:
        if cover:
            mb.box(body, (-1.474, 0.40, 0.0), (0.014, 0.22, 0.22),
                   style["wall"])
            for i in range(2):
                mb.box(body, (-1.482, 0.40, -0.10 + i * 0.20),
                       (0.008, 0.26, 0.09), style["frame"])
        else:
            rad = 0.112 if heavy else 0.090
            mb.cyl(body, (-1.474, 0.40, 0.0), "x", rad, 0.050, 8, METAL)
            mb.cyl(body, (-1.460, 0.40, 0.0), "x", rad * 0.62, 0.020, 8,
                   METAL)
            bolt_r = rad * 0.74
            for i in range(6):
                ang = i * (2.0 * math.pi / 6.0)
                mb.box(body, (-1.486, 0.40 + math.cos(ang) * bolt_r,
                              math.sin(ang) * bolt_r),
                       (0.010, 0.026, 0.026), METAL)
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
            n_roll = 5 if heavy else 4
            for i in range(n_roll):
                mb.box(body, (lx - 0.34 + i * (0.68 / max(1, n_roll - 1)),
                              0.17, 1.424), (0.055, 0.055, 0.014),
                       METAL if metal else frame)
            # lip plate under the roller mouth
            mb.box(body, (lx, 0.34, 1.470), (0.84, 0.022, 0.020), frame)
    else:
        mb.box(body, (lx, ly, 1.452), (0.84, 0.40, 0.020), style["wall"])
        mb.box(body, (lx, ly, 1.458), (0.30, 0.30, 0.012), frame)
    # ---------------- power port, back right x = 1.0, y = 0.85 -------------
    wx, wy = 1.0, 0.80
    mb.box(body, (wx, wy, 1.454), (0.20, 0.14, 0.030),
           METAL if metal else frame,
           anchor=((0, 0, 1), (0.03, 0.03, 0.02)))
    if lod == 0:
        mb.box(body, (wx, wy, 1.472), (0.11, 0.08, 0.014), frame)
        # service riser under the junction box (below the roof beam)
        mb.box(body, (wx, 0.60, 1.462), (0.075, 0.34, 0.016),
               METAL if metal else frame)
        for sgn in (-1, 1):
            mb.box(body, (wx + sgn * 0.10, 0.60, 1.468),
                   (0.016, 0.30, 0.014), METAL if metal else frame)
        if tier == "Top":
            # heavy flange + cable glands (reference industrial language)
            mb.cyl(body, (wx, 0.80, 1.478), "z", 0.098, 0.030, 8, METAL)
            for i in range(3):
                mb.cyl(body, (wx - 0.055 + i * 0.055, 0.755, 1.482), "z",
                       0.022, 0.026, 6, METAL)
    mb.box(body, (wx, 0.42, 1.466), (0.13, 0.70, 0.016), frame)


# --------------------------------------------------------------------------- #
# the ONE flat empty roof platform
# --------------------------------------------------------------------------- #
def add_roof_platform(mb, style, tier, faction, lod):
    """ONE flat empty roof platform: framed deck, walking surface y = 1.00.

    v2.2 reference-image upgrade adds the framed roof border and the plank /
    panel seams of the references.  Every vertex stays at or below y = 1.00: the
    border band and the deck planks are coplanar with the walking surface
    (top = 1.00), the seams are gaps that expose a darker substrate BELOW 1.00,
    and the deep fascia beam sits under the platform (0.86..0.95).  NOTHING
    exists above the walking surface, and there is no equipment / crop / pipe on
    the roof (REQ-V2-BLD-03/04, REQ-V2-ART-01).
    """
    body = mb.body_name
    sx, sz = style["plan"]
    roof = style["roof"]
    frame = style["frame"]
    b = ROOF_BORDER_W
    inner_x, inner_z = sx - 2.0 * b, sz - 2.0 * b
    deck_top = PLATFORM_TOP              # 1.00 == walking surface
    plank_t = 0.010
    plank_y = deck_top - plank_t * 0.5   # top exactly at the walking surface
    # deck: LOD2 is one massing slab, LOD0/LOD1 get the reference plank /
    # panel seams (the seam gaps expose the darker substrate BELOW 1.00).
    kind = style["roof_style"]
    gap = ROOF_SEAM
    if lod == 2:
        mb.box(body, (0.0, 0.975, 0.0), (sx, 0.050, sz), roof, skip={0})
    else:
        mb.box(body, (0.0, 0.965, 0.0),
               (inner_x + 0.02, 0.050, inner_z + 0.02), roof, skip={0})
        if kind in ("metal", "heavy"):
            n = 3 if lod == 0 else 2
            pw = (inner_x - gap * (n - 1)) / n
            pd = (inner_z - gap * (n - 1)) / n
            for i in range(n):
                for j in range(n):
                    mb.box(body, (-inner_x * 0.5 + pw * 0.5 + i * (pw + gap),
                                  plank_y,
                                  -inner_z * 0.5 + pd * 0.5 + j * (pd + gap)),
                           (pw, plank_t, pd), roof)
        else:
            n = (7 if kind == "planks" else 4) if lod == 0 else 4
            pd = (inner_z - gap * (n - 1)) / n
            for i in range(n):
                mb.box(body, (0.0, plank_y,
                              -inner_z * 0.5 + pd * 0.5 + i * (pd + gap)),
                       (inner_x, plank_t, pd), roof)
    # framed border band (frame material, coplanar top = 1.00)
    by = deck_top - 0.030
    bh = 0.060
    mb.box(body, (0.0, by, sz * 0.5 - b * 0.5), (sx, bh, b), frame)
    mb.box(body, (0.0, by, -(sz * 0.5 - b * 0.5)), (sx, bh, b), frame)
    mb.box(body, (sx * 0.5 - b * 0.5, by, 0.0), (b, bh, sz - 2.0 * b), frame)
    mb.box(body, (-(sx * 0.5 - b * 0.5), by, 0.0), (b, bh, sz - 2.0 * b), frame)
    # flush perimeter fascia ("?嗉器"): 0.04 tall, top == walking surface, so the
    # edge never rises above the top and cannot block an upper building.
    fy = deck_top - CURB_H * 0.5
    for (x, z, cbx, cbz) in ((0.0, sz * 0.5 - 0.012, sx, 0.024),
                             (0.0, -(sz * 0.5 - 0.012), sx, 0.024),
                             (sx * 0.5 - 0.012, 0.0, 0.024, sz - 0.048),
                             (-(sx * 0.5 - 0.012), 0.0, 0.024, sz - 0.048)):
        mb.box(body, (x, fy, z), (cbx, CURB_H, cbz), frame)
    # underside frame: perimeter ring + cross beams (all below the platform)
    if lod <= 1:
        uy = PLATFORM_UNDER - 0.030
        mb.box(body, (0.0, uy, sz * 0.5 - 0.080), (sx - 0.06, 0.055, 0.06),
               frame)
        mb.box(body, (0.0, uy, -(sz * 0.5 - 0.080)), (sx - 0.06, 0.055, 0.06),
               frame)
        mb.box(body, (sx * 0.5 - 0.080, uy, 0.0), (0.06, 0.055, sz - 0.06),
               frame)
        mb.box(body, (-(sx * 0.5 - 0.080), uy, 0.0), (0.06, 0.055, sz - 0.06),
               frame)
        for i in range(3 if lod == 0 else 2):
            z = -1.10 + i * (1.10 if lod == 0 else 1.65)
            mb.box(body, (0.0, uy, z), (sx - 0.16, 0.045, 0.05), frame)
        if lod == 0:
            for x in (-0.9, 0.0, 0.9):
                mb.box(body, (x, uy, 0.0), (0.05, 0.045, sz - 0.16), frame)
    # exposed rafter ends on the roof-edge beam (Basic reads as a timber barn)
    if style.get("rafter_ends") and lod == 0:
        for i in range(5):
            x = -1.20 + i * 0.60
            for zsgn in (-1, 1):
                mb.box(body, (x, BEAM_LO + 0.045,
                              zsgn * (BEAM_OUT + 0.004)),
                       (0.080, 0.100, 0.012), WOOD)
    # metal tiers: rivets / panel plates on the roof border (all top <= 1.00)
    if lod == 0 and style["metal_frame"]:
        n = 3
        for i in range(n):
            x = -1.24 + i * (2.48 / (n - 1))
            for zsgn in (-1, 1):
                mb.box(body, (x, deck_top - 0.018,
                              zsgn * (sz * 0.5 + 0.004)),
                       (0.030, 0.030, 0.010), METAL)
        for i in range(n):
            z = -1.24 + i * (2.48 / (n - 1))
            for xsgn in (-1, 1):
                mb.box(body, (xsgn * (sx * 0.5 + 0.004), deck_top - 0.018, z),
                       (0.010, 0.030, 0.030), METAL)
    if style.get("heavy") and lod == 0:
        # corner armour plates, coplanar with the walking surface
        for xsgn in (-1, 1):
            for zsgn in (-1, 1):
                mb.box(body, (xsgn * (sx * 0.5 - 0.14), deck_top - 0.004,
                              zsgn * (sz * 0.5 - 0.14)),
                       (0.24, 0.008, 0.24), frame)


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
# moss accents (reference-image foliage layer)
# --------------------------------------------------------------------------- #
def add_moss(mb, style, tier, faction, lod):
    """Moss accents for the Basic Folktails barn (vanilla ``Moss.Folktails``).

    Placed on the plinth corners, the corner-post feet, the open-bay sills and the
    roof-border SIDES.  Never on the roof walking surface: the frozen v2 contract
    forbids any geometry above y = 1.00 (the reference image puts moss on the roof
    top; that is one of the documented "do not copy" conflicts).
    """
    if not style.get("moss"):
        return
    body = mb.body_name
    mat = style["moss"]
    plan_x, plan_z = style["plan"]
    if lod == 0:
        for (x, z) in ((-1.40, -1.40), (1.40, -1.40),
                       (-1.40, 1.40), (1.40, 1.40)):
            mb.box(body, (x, 0.168, z), (0.17, 0.038, 0.17), mat)
        face_clumps = [
            (-1.10, 0.158, -(PLINTH_OUT + 0.002), 0.20, 0.016),
            (0.95, 0.158, -(PLINTH_OUT + 0.002), 0.16, 0.016),
            (1.10, 0.158, (PLINTH_OUT + 0.002), 0.18, 0.016),
            (-(PLINTH_OUT + 0.002), 0.158, -0.90, 0.016, 0.18),
        ]
        for (x, y, z, sx, sz) in face_clumps:
            mb.box(body, (x, y, z), (sx, 0.045, sz), mat)
        for x in (-1.05, 1.05):
            mb.box(body, (x, 0.278, -1.452), (0.26, 0.026, 0.020), mat)
        for z in (-0.90, 0.95):
            for sgn in (-1, 1):
                mb.box(body, (sgn * 1.458, 0.322, z), (0.020, 0.026, 0.24),
                       mat)
        for i in range(4):
            t = -1.02 + i * 0.68
            for zsgn in (-1, 1):
                mb.box(body, (t, 0.962, zsgn * (plan_z * 0.5 + 0.006)),
                       (0.15, 0.050, 0.020), mat)
        for i in range(4):
            t = -0.90 + i * 0.60
            for xsgn in (-1, 1):
                mb.box(body, (xsgn * (plan_x * 0.5 + 0.006), 0.962, t),
                       (0.020, 0.050, 0.15), mat)
    elif lod == 1:
        for (x, z) in ((-1.40, -1.40), (1.40, 1.40)):
            mb.box(body, (x, 0.168, z), (0.16, 0.034, 0.16), mat)
        for i in range(3):
            t = -0.90 + i * 0.90
            mb.box(body, (t, 0.962, -(plan_z * 0.5 + 0.006)),
                   (0.16, 0.045, 0.020), mat)
    else:
        mb.box(body, (-1.40, 0.168, -1.40), (0.16, 0.034, 0.16), mat)


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
        if mb.tri_count() >= budget * 0.975 or detail_tris >= cap:
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
    # structural seam strips on the SOLID plinth / roof-beam bands: both bands
    # are solid on every tier and the candidate position is additionally tested
    # against the opening list, so the filler can never cover a window or cross
    # the roof top.
    seam_i = 0
    placed = 0
    guard = 0
    while mb.tri_count() < budget * 0.975 and guard < 4000:
        guard += 1
        i = seam_i
        seam_i += 1
        band = i % 2
        y = 0.125 if band == 0 else 0.905
        side = (i // 2) % 4
        plane = ("back", "front", "left", "right")[side]
        index = (i // 8) % 12
        aa = -1.32 + index * 0.24
        if not any(s0 - 0.02 <= aa <= s1 + 0.02
                   for (s0, s1) in solid_spans(style, plane, y - 0.04, y + 0.04)):
            continue
        placed += 1
        if band == 0:
            off = PLINTH_OUT + 0.004
        else:
            off = BEAM_OUT + 0.002
        if plane in ("front", "back"):
            zsgn = -1.0 if plane == "front" else 1.0
            mb.box(mb.body_name, (aa, y, zsgn * off),
                   (0.055, 0.030, 0.012), frame)
        else:
            xsgn = -1.0 if plane == "left" else 1.0
            mb.box(mb.body_name, (xsgn * off, y, aa),
                   (0.012, 0.030, 0.055), frame)
    return {"added_pads": added_pads, "added_seams": placed,
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


_FBX_TIME_PATCHED = False


def pin_fbx_timestamp():
    """Pin the FBX writer's CreationTimeStamp so re-generation is byte-stable.

    Blender's FBX exporter stamps ``datetime.datetime.now()`` into the file
    header, which made regenerated FBX files differ byte-wise even when the mesh
    was identical.  Pinning it (1970-01-01) keeps the whole source-model set
    reproducible; the mesh payload was already deterministic.
    """
    global _FBX_TIME_PATCHED
    if _FBX_TIME_PATCHED:
        return
    try:
        import datetime as _dt
        import io_scene_fbx.export_fbx_bin as _fbx_bin

        class _FixedDatetime(object):
            @staticmethod
            def now():
                return _dt.datetime(1970, 1, 1, 0, 0, 0, 0)

        _fbx_bin.datetime = types.SimpleNamespace(datetime=_FixedDatetime)
        _FBX_TIME_PATCHED = True
    except Exception as exc:            # pragma: no cover - exporter internals
        print("WARNING: cannot pin the FBX creation timestamp (%r); FBX files "
              "may differ byte-wise between runs while the mesh stays identical"
              % (exc,))


_FBX_UID_PATCHED = False


def pin_fbx_uids():
    """Make the FBX object UIDs deterministic (byte-stable source models).

    Blender's FBX exporter derives object UIDs from Python's builtin ``hash()``
    of keys that contain bpy structs, and CPython randomises string hashing per
    process -> two generations of the same mesh wrote different FBX bytes.  We
    replace the UID derivation with a SHA-256 over a *stable* key description
    (bpy structs by type+name, tuples recursively), keeping the exporter's own
    "shorten + uniquify" behaviour.  The .timbermesh export never reads these
    UIDs; only the FBX container bytes become reproducible.
    """
    global _FBX_UID_PATCHED
    if _FBX_UID_PATCHED:
        return
    try:
        import hashlib
        import io_scene_fbx.fbx_utils as _fbx_utils

        def _stable_desc(key):
            if key is None or isinstance(key, (str, int, float, bool, bytes)):
                return repr(key)
            if isinstance(key, (tuple, list)):
                return "(" + ",".join(_stable_desc(k) for k in key) + ")"
            name = getattr(key, "name", None)
            if isinstance(name, str) and name:
                return "%s:%s" % (type(key).__name__, name)
            return type(key).__name__

        def _key_to_uuid(uuids, key):
            digest = hashlib.sha256(
                ("HDBF|" + _stable_desc(key)).encode("utf-8")).digest()
            uuid = int.from_bytes(digest[:8], "big") & ((1 << 63) - 1)
            if uuid > int(1e9):
                t_uuid = uuid % int(1e9)
                if t_uuid not in uuids:
                    uuid = t_uuid
            if uuid in uuids:
                inc = 1 if uuid < 2 ** 62 else -1
                while uuid in uuids:
                    uuid += inc
                    if 0 > uuid >= 2 ** 63:
                        raise ValueError("cannot allocate a FBX UID for %r"
                                         % (_stable_desc(key),))
            return _fbx_utils.UUID(uuid)

        _fbx_utils._key_to_uuid = _key_to_uuid
        _FBX_UID_PATCHED = True
    except Exception as exc:            # pragma: no cover - exporter internals
        print("WARNING: cannot pin the FBX object UIDs (%r); FBX files may "
              "differ byte-wise between runs while the mesh is identical"
              % (exc,))


def export_model(mb, out_dir, basename, materials):
    pin_fbx_timestamp()
    pin_fbx_uids()
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


def facade_metrics(style):
    """Objective facade metrics for the delivery report (visible opening areas)."""
    wall_h = WALL_TOP - FOUND_TOP
    wall_w = 2.0 * (BODY_X - WALL_T * 0.5)
    area = wall_w * wall_h
    glass = {}
    openings = 0
    for plane in ("front", "back", "left", "right"):
        g = 0.0
        for op in style["openings"].get(plane, []):
            openings += 1
            if op["kind"] == "glass":
                a0, a1, y0, y1 = op["rect"]
                g += (a1 - a0) * (y1 - y0)
        glass[plane] = g
    side = glass["left"] + glass["right"]
    return {
        "openings": openings,
        "shelf_levels": style["shelf_levels"],
        "glass_area_hint": A.V2_INDOOR["glass_area_min"][style["tier"]],
        "wall_band_area": round(area, 4),
        "glass_area_visible": round(sum(glass.values()), 4),
        "glass_front": round(glass["front"], 4),
        "glass_back": round(glass["back"], 4),
        "glass_side_total": round(side, 4),
        "side_wall_glass_ratio": round(side / (2.0 * area), 4),
        "front_facade_glass_ratio": round(glass["front"] / area, 4),
    }


def _dbg(mb, label):
    """Optional per-stage triangle histogram (HDBF_DEBUG_TRIS=1)."""
    if os.environ.get("HDBF_DEBUG_TRIS"):
        print("    tris[%-22s] = %5d" % (label, mb.tri_count()))


def build_tier_model(tier, faction, lod):
    mb = make_builder(tier, faction, lod)
    style = tier_style(tier, faction)
    bevel = (0.012 if faction == "Folktails" else 0.007) if lod == 0 else 0.0
    add_foundation(mb, style, lod)
    _dbg(mb, "foundation")
    add_facade_walls(mb, style, lod, bevel)
    _dbg(mb, "facade_walls")
    add_openings(mb, style, faction, lod)
    _dbg(mb, "openings")
    add_panels(mb, style, tier, faction, lod)
    _dbg(mb, "panels")
    add_interior_shelves(mb, style, tier, lod)
    _dbg(mb, "shelves")
    add_ports_geometry(mb, style, tier, faction, lod)
    _dbg(mb, "ports")
    add_roof_platform(mb, style, tier, faction, lod)
    _dbg(mb, "roof")
    add_base_details(mb, style, tier, faction, lod)
    _dbg(mb, "base_details")
    add_moss(mb, style, tier, faction, lod)
    _dbg(mb, "moss")
    add_decor(mb, tier, faction, style, lod)
    _dbg(mb, "decor")
    fill = auto_fill(mb, tier, style, lod)
    _dbg(mb, "auto_fill")
    roles = patch_roles_for(tier, faction, construction=False)
    _check_patch_coverage(mb, roles, "%s.%s.LOD%d" % (tier, faction, lod))
    assign_flat_patch_uvs(mb, roles)
    lo, hi = _check_envelope(mb, "%s.%s.LOD%d" % (tier, faction, lod))
    facade = facade_metrics(style)
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
        "facade": facade,
        "fill": fill,
    }
    return mb, info


def build_construction_model():
    """Shared construction-stage greybox, following the reference greybox image.

    Reference language (adopted): exposed timber frame, scaffold standards /
    ledgers / braces and a flat concrete-style slab.  Conflicts NOT copied: the
    scaffold never leaves the 3x3 footprint, nothing rises above y = 1.00, and the
    port markers stay exactly where the frozen contract puts them.
    """
    tier = CONSTRUCTION_TIER
    mb = make_builder(tier, "shared", 0)
    wood, metal = WOOD, METAL
    sx, sz = A.roof_platform(tier)
    body = mb.body_name
    b = ROOF_BORDER_W
    # full 3 x 3 foundation slab (defines the exact envelope)
    mb.box(body, (0.0, FOUND_TOP * 0.5, 0.0), (3.0, FOUND_TOP, 3.0), wood,
           skip={0})
    # plinth ring (stone/metal base line)
    for (x, z, w, d) in ((0.0, 1.48, 2.94, 0.04), (0.0, -1.48, 2.94, 0.04),
                         (1.48, 0.0, 0.04, 2.94), (-1.48, 0.0, 0.04, 2.94)):
        mb.box(body, (x, 0.125, z), (w, 0.05, d), metal, skip={0})
    # corner frame posts (timber) up to the wall line
    for x in (-1.40, 1.40):
        for z in (-1.40, 1.40):
            mb.box(body, (x, 0.52, z), (0.11, 0.84, 0.11), wood)
    # half-built perimeter walls (all below 0.95)
    mb.box(body, (0.0, 0.34, 1.44), (2.90, 0.48, 0.05), wood)
    mb.box(body, (-1.44, 0.28, 0.0), (0.05, 0.36, 2.86), wood)
    for i in range(3):
        mb.box(body, (-0.90 + i * 0.90, 0.42, -1.44), (0.80, 0.64, 0.05), wood)
    # exposed timber top beams at the wall line
    for (x, z, w, d) in ((0.0, 1.44, 2.88, 0.07), (0.0, -1.44, 2.88, 0.07),
                         (1.44, 0.0, 0.07, 2.88), (-1.44, 0.0, 0.07, 2.88)):
        mb.box(body, (x, 0.80, z), (w, 0.08, d), wood)
    # interior scaffold (steel standards + ledgers), fully inside the footprint
    for x in (-1.12, 0.0, 1.12):
        for z in (-1.10, 0.72):
            mb.box(body, (x, 0.44, z), (0.045, 0.68, 0.045), metal)
    for yy in (0.30, 0.66):
        for z in (-1.10, 0.72):
            mb.box(body, (0.0, yy, z), (2.30, 0.035, 0.035), metal)
        for x in (-1.12, 0.0, 1.12):
            mb.box(body, (x, yy, -0.19), (0.035, 0.035, 1.86), metal)
    for (plane, a0, a1) in (("front", -0.80, 0.30), ("back", -0.30, 0.80)):
        diag_brace(mb, plane, a0, 0.26, a1, 0.70, 0.030, 0.016, 1.26, metal)
    # ONE flat empty roof platform (greybox): slab + panels + flush fascia
    inner_x, inner_z = sx - 2.0 * b, sz - 2.0 * b
    mb.box(body, (0.0, 0.965, 0.0), (inner_x + 0.02, 0.050, inner_z + 0.02),
           wood, skip={0})
    gap = ROOF_SEAM
    pw = (inner_x - gap * 3.0) / 4.0
    for i in range(4):
        for j in range(4):
            mb.box(body, (-inner_x * 0.5 + pw * 0.5 + i * (pw + gap), 0.995,
                          -inner_z * 0.5 + pw * 0.5 + j * (pw + gap)),
                   (pw, 0.010, pw), metal)
    for (x, z, w, d) in ((0.0, sz * 0.5 - b * 0.5, sx, b),
                         (0.0, -(sz * 0.5 - b * 0.5), sx, b),
                         (sx * 0.5 - b * 0.5, 0.0, b, sz - 2 * b),
                         (-(sx * 0.5 - b * 0.5), 0.0, b, sz - 2 * b)):
        mb.box(body, (x, 0.97, z), (w, 0.060, d), metal)
    fy = PLATFORM_TOP - CURB_H * 0.5
    for (x, z, cbx, cbz) in ((0.0, sz * 0.5 - 0.012, sx, 0.024),
                             (0.0, -(sz * 0.5 - 0.012), sx, 0.024),
                             (sx * 0.5 - 0.012, 0.0, 0.024, sz - 0.048),
                             (-(sx * 0.5 - 0.012), 0.0, 0.024, sz - 0.048)):
        mb.box(body, (x, fy, z), (cbx, CURB_H, cbz), metal)
    # platform supports / beams under the slab
    uy = PLATFORM_UNDER - 0.030
    for x in (-1.32, 0.0, 1.32):
        mb.box(body, (x, uy, 0.0), (0.08, 0.055, sz - 0.10), metal)
    mb.box(body, (0.0, uy, 1.32), (sx - 0.10, 0.055, 0.08), metal)
    mb.box(body, (0.0, uy, -1.32), (sx - 0.10, 0.055, 0.08), metal)
    # material stacks / pallets on the floor
    for i in range(4):
        mb.box(body, (-1.00 + i * 0.62, 0.16, -1.10), (0.42, 0.08, 0.30), wood)
    for i in range(3):
        mb.box(body, (0.88, 0.15, -0.30 + i * 0.34), (0.36, 0.06, 0.26), wood)
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
