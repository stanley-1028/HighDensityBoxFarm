"""HDBF Box Farm — shared art contract (atlas layout, ports, slots, tiers).

**v2 revision (2026-10-26): four tiers are all 3 x 3 x 1.**  The envelope is
strictly 3.00 (X width) x 1.00 (Y height) x 3.00 (Z depth) in asset space, the
front face is -Z and the pivot is the footprint bottom centre.  The roof is ONE
flat empty platform whose WALKING SURFACE is the model apex (y = 1.00) and
nothing exists above it.  The four tiers are told apart by facade / opening /
material layering (indoor high-density shelves behind windows), not by a roof
silhouette.  A construction-stage greybox ships per tier x faction.

This module is the single source of truth shared by:
  * art/gen_textures.py    — paints the 2048x2048 atlas into these pixel rects
  * art/gen_models_v2.py   — builds the v2 source models + material_slots.json
  * art/validate_models.py — re-checks the exported source assets (FBX+GLB)
  * art/validate_timbermesh.py (via timbermesh_common) — checks the 16 shipped
    `.timbermesh` files

It is pure Python (no bpy / numpy) so it can be imported by any validator.

Coordinate systems
------------------
* "asset space" = Unity space required by the v2 building spec:
  X right, Y up, Z depth, building front faces -Z, pivot at footprint bottom
  centre: X in [-1.5, 1.5], Y in [0, 1], Z in [-1.5, 1.5].  Port coordinates in
  PORT_SPECS are given in this space.
* "blender space" = authoring space: Blender is Z-up, and the FBX/GLB export
  with Blender's standard axis conversion emits
      asset = ( blender.x, blender.z, -blender.y )
  so blender front is +Y and blender space bounds are
  X in [-1.5, 1.5], Y in [-1.5, 1.5] (front = +Y), Z in [0, 1] (up).

Atlas layout
------------
2048x2048, one shared atlas for all tiers, both factions and the construction
stage.  Pixel rects use a TOP-LEFT origin (row 0 = v 1.0).  Material bands follow
requirement 13.4: U 0.00-0.45 Wood, 0.45-0.70 Metal, 0.70-0.85 Glass,
0.85-1.00 Fabric.  Each band is split into semantic sub-regions so a model face
can be packed into the texture it belongs to while never overlapping another
face of the same model.
"""

from __future__ import annotations

# --------------------------------------------------------------------------- #
# atlas
# --------------------------------------------------------------------------- #

ATLAS_SIZE = 2048
#: gutter between two packed UV islands, in pixels (2 px half-margin per side).
PADDING = 4
#: target texel density, requirement 13.4 "about 256 px per grid unit".
TEXEL_DENSITY = 256
#: normal map convention used by the atlas.
NORMAL_CONVENTION = "OpenGL (+Y up)"

#: ordered material slots, requirement 13.4 / 13.7 (index 0..3 is fixed).
MATERIAL_ORDER = [
    "M_HDBF_Wood",    # slot 0
    "M_HDBF_Metal",   # slot 1
    "M_HDBF_Glass",   # slot 2
    "M_HDBF_Fabric",  # slot 3
]

#: material band edges in U.  Requirement 13.4 gives suggested bands (Wood
#: 0.00-0.45 / Metal 0.45-0.70 / Glass 0.70-0.85 / Fabric 0.85-1.00); those are
#: marked "initially adjustable", and the allocation below keeps the same
#: 0-Wood / 1-Metal / 2-Glass / 3-Fabric order while giving Wood the extra atlas
#: space that unique, non-overlapping per-face UVs need at the 6000-tri LOD0
#: budget.  See art/README.md "atlas allocation deviation".
MATERIAL_BANDS = {
    "M_HDBF_Wood": (0.0, 950 / 2048.0),
    "M_HDBF_Metal": (950 / 2048.0, 1730 / 2048.0),
    "M_HDBF_Glass": (1730 / 2048.0, 1846 / 2048.0),
    "M_HDBF_Fabric": (1846 / 2048.0, 1.0),
}

#: semantic atlas regions.  name -> (material, pixel rect x0,y0,x1,y1 top-left origin).
#: 4 px gutters separate regions so packed islands never bleed into a neighbour.
#: Models pack all metal faces into the single `metal` region (trim/fixture tiles
#: would have needed a larger metal band than the shared 2048 atlas allows).
REGIONS = {
    # ---- Wood band --------------------------------------------------------
    "wood":       ("M_HDBF_Wood",   (0,    0,    842,  2048)),
    "soil_full":  ("M_HDBF_Wood",   (846,  0,    942,  1024)),
    "soil_empty": ("M_HDBF_Wood",   (846,  1028, 942,  2048)),
    # ---- Metal band -------------------------------------------------------
    "metal":      ("M_HDBF_Metal",  (950,  0,    1729, 2048)),
    # ---- Glass band -------------------------------------------------------
    "glass":      ("M_HDBF_Glass",  (1730, 0,    1845, 2048)),
    # ---- Fabric band ------------------------------------------------------
    # v2.2 (2026-10-26, reference-image upgrade): the old canvas region was split
    # to give the Basic Folktails tier a dedicated MOSS patch (foliage / moss
    # accent geometry, vanilla `Moss.Folktails`).  The remaining upper part is the
    # cream PLASTER patch used by the reference "light flat plaster roof" and the
    # plaster facade bands.  The fabric band is otherwise unchanged.
    "plaster":    ("M_HDBF_Fabric", (1846, 0,    2048, 900)),
    "moss":       ("M_HDBF_Fabric", (1846, 904,  2048, 1250)),
    "thatch":     ("M_HDBF_Fabric", (1846, 1254, 2048, 2048)),
}

#: fallback region for tools that only know a material name.
MATERIAL_FALLBACK_REGION = {
    "M_HDBF_Wood": "wood",
    "M_HDBF_Metal": "metal",
    "M_HDBF_Glass": "glass",
    "M_HDBF_Fabric": "plaster",
}

#: per material physical response (requirement 13.4 parameter table).
MATERIAL_PARAMS = {
    "M_HDBF_Wood":   {"metallic": 0.00, "roughness": 0.72, "alpha": 1.00, "blend": "Opaque"},
    "M_HDBF_Metal":  {"metallic": 0.92, "roughness": 0.48, "alpha": 1.00, "blend": "Opaque"},
    "M_HDBF_Glass":  {"metallic": 0.00, "roughness": 0.08, "alpha": 0.62, "blend": "Blend"},
    "M_HDBF_Fabric": {"metallic": 0.00, "roughness": 0.86, "alpha": 1.00, "blend": "Opaque"},
}

#: textures produced by art/gen_textures.py (relative to art/).
ATLAS_FILES = {
    "albedo": "textures/atlas_albedo.png",
    "normal": "textures/atlas_normal.png",
    "mask": "textures/atlas_mask.png",
}

# --------------------------------------------------------------------------- #
# model identity
# --------------------------------------------------------------------------- #

TIERS = ["Basic", "Intermediate", "Advanced", "Top"]
FACTIONS = ["Folktails", "IronTeeth"]
LODS = [0, 1, 2]
#: triangle budgets (requirement 13.5) and allowed +-20%.
TRI_BUDGET = {0: 6000, 1: 2400, 2: 800}
TRI_TOLERANCE = 0.20
LOD_SWITCH_DISTANCE_GRID = {0: "0-18", 1: "18-45", 2: "45-90"}
LOD_CAST_SHADOW = {0: True, 1: True, 2: False}

#: construction stage is shared by all four tiers (one file).
CONSTRUCTION_STAGE = "ConstructionStage0"

# --------------------------------------------------------------------------- #
# v2 envelope (3 x 3 x 1) + flat roof platform (no objects on top)
# --------------------------------------------------------------------------- #
#: v2 (2026-10-26): every finished model and every construction-stage greybox is
#: strictly 3.00 x 1.00 x 3.00 in asset space (X width, Y up, Z depth).  All
#: decoration, windows, port geometry and roof edges must stay inside it.
ENVELOPE = {
    "size_x": 3.0, "size_y": 1.0, "size_z": 3.0,
    "min_x": -1.5, "min_y": 0.0, "min_z": -1.5,
    "max_x": 1.5, "max_y": 1.0, "max_z": 1.5,
}
#: envelope tolerance used by both validators (grid units).
ENVELOPE_TOLERANCE = 0.02

#: The roof is ONE flat empty platform: the WALKING SURFACE is the model apex
#: (y = 1.00) in every tier / LOD / faction, and no geometry exists above it.
#: The perimeter "收边" is a flush fascia band (height <= 0.04, width 0.04)
#: whose top is exactly the walking surface, so it cannot block stacking.
ROOF_PLATFORM_TOP = 1.00        # walking surface of the ONE platform (model apex)
ROOF_PLATFORM_THICKNESS = 0.05  # slab thickness (underside y = 0.95)
ROOF_CURB_H = 0.04              # flush perimeter fascia band height (top = walking surface)
ROOF_CURB_W = 0.04              # curb / fascia width
#: no geometry is allowed in the 30 cm above the walking surface (REQ-V2-ART-01);
#: in practice nothing may exceed ROOF_PLATFORM_TOP at all.
ROOF_CLEAR_ABOVE = 0.30
#: The platform plan is near the full 3x3 footprint (so an upper building has a
#: real support surface); the plan shrinks slightly on the lower tiers and every
#: plan stays inside the 3.00 x 3.00 envelope.
ROOF_PLATFORM = {
    "Basic":        {"size_x": 2.84, "size_z": 2.84},
    "Intermediate": {"size_x": 2.92, "size_z": 2.92},
    "Advanced":     {"size_x": 2.96, "size_z": 2.96},
    "Top":          {"size_x": 2.98, "size_z": 2.98},
    "ConstructionStage0": {"size_x": 2.90, "size_z": 2.90},
}


def roof_platform(tier):
    """Plan size (x, z) of the single empty roof platform for one tier."""
    return ROOF_PLATFORM[tier]["size_x"], ROOF_PLATFORM[tier]["size_z"]


# --------------------------------------------------------------------------- #
# v2 indoor-high-density facade contract (REQ-V2-ART-04)
# --------------------------------------------------------------------------- #
#: Four tiers must be distinguishable at the 50% camera by at least two of
#: {silhouette, material, windows/openings, ports} (validator C9).  The model
#: expresses "indoor high density" with STATIC racks/shelves visible through the
#: facade openings (3 levels, interior only) — never with 600 dynamic slots.
V2_INDOOR = {
    # shelf levels are horizontal slabs visible behind the facade openings
    "shelf_levels": 3,
    "shelf_min_levels_checked": 2,
    # glass facade area (grid^2, front + side windows) expected per tier
    "glass_area_min": {
        "Basic": 0.00,
        "Intermediate": 0.35,
        "Advanced": 0.60,
        "Top": 0.85,
    },
    # open-front bay count (Basic is an open barn; other tiers are glazed)
    "open_front_bays": {
        "Basic": 3,
        "Intermediate": 0,
        "Advanced": 0,
        "Top": 0,
    },
}
#: a "port fixture" counts as present when >= this many triangles lie within
#: PORT_FIXTURE_RADIUS grid units of the port marker position (geometry differs
#: per tier: covered/blank on lower tiers, open flange/box on higher tiers).
PORT_FIXTURE_RADIUS = 0.22
PORT_FIXTURE_MIN_TRIS = 8


# --------------------------------------------------------------------------- #
# ports: exactly the three #Port# marker empties (REQ-V2-BLD-06 / ART-02)
# --------------------------------------------------------------------------- #
#: v2 (2026-10-26): the model ships EXACTLY three `#Port#` empty markers.  The
#: coordinates below are ASSET space (Unity Y-up, front -Z, pivot at the
#: footprint bottom centre) for the new 3 x 3 footprint; they keep the v1 port
#: heights/roles and only re-anchor the depth to +-1.50.  "rot_blender" is the
#: euler rotation that makes the empty's local +Z point outwards in Blender.
PORT_SPECS = [
    {"name": "Power", "object": "#Port#Power", "class": "power",
     "asset": (1.00, 0.85, 1.50), "outward": (0, 0, 1),
     "size": (0.12, 0.12, 0.06), "rot_blender": (90.0, 0.0, 0.0),
     "note": "rear right, junction box + insulator (exposed on Top only)"},
    {"name": "Pipe", "object": "#Port#Pipe", "class": "pipe",
     "asset": (-1.50, 0.40, 0.00), "outward": (-1, 0, 0),
     "size": (0.16, 0.16, 0.06), "rot_blender": (0.0, -90.0, 0.0),
     "note": "left short side centre, flange + cover on lower tiers"},
    {"name": "Logistics", "object": "#Port#Logistics", "class": "logistics",
     "asset": (-1.00, 0.35, 1.50), "outward": (0, 0, 1),
     "size": (0.80, 0.40, 0.06), "rot_blender": (90.0, 0.0, 0.0),
     "note": "rear left, conveyor opening; blank cover plate on Basic"},
]

#: port tolerance in grid units.
PORT_TOLERANCE = 0.02

#: ports whose *visible geometry* is absent per tier (marker empties still
#: exist, so an upgrade never shifts a port coordinate).
PORT_GEOMETRY_VISIBLE = {
    "Basic":        {"Pipe"},
    "Intermediate": {"Pipe", "Logistics"},
    "Advanced":     {"Pipe", "Logistics"},
    "Top":          {"Power", "Pipe", "Logistics"},
}

#: which materials each tier is expected to reference (subsequence of
#: MATERIAL_ORDER).  Basic = wood + canvas/thatch; Intermediate adds glass;
#: Advanced/Top use all four (ch. 13.2 "main material count 1->2->3->4" intent).
TIER_MATERIALS = {
    "Basic":        ["M_HDBF_Wood", "M_HDBF_Fabric"],
    "Intermediate": ["M_HDBF_Wood", "M_HDBF_Glass", "M_HDBF_Fabric"],
    "Advanced":     ["M_HDBF_Wood", "M_HDBF_Metal", "M_HDBF_Glass", "M_HDBF_Fabric"],
    "Top":          ["M_HDBF_Wood", "M_HDBF_Metal", "M_HDBF_Glass", "M_HDBF_Fabric"],
    "ConstructionStage0": ["M_HDBF_Wood", "M_HDBF_Metal"],
}

#: acceptable shrink of the LOD bounding box from LOD0 (requirement 13.5: <=2%).
LOD_BBOX_SHRINK_MAX = 0.02


# --------------------------------------------------------------------------- #
# helpers
# --------------------------------------------------------------------------- #

def blender_from_asset(p):
    """asset-space (Unity Y-up, front -Z) -> blender-space (Z-up, front +Y)."""
    x, y, z = p
    return (x, -z, y)


def asset_from_blender(p):
    """blender-space (Z-up, front +Y) -> asset-space (Unity Y-up, front -Z)."""
    x, y, z = p
    return (x, z, -y)


def px_rect_to_uv(rect, size=ATLAS_SIZE):
    """pixel rect (x0, y0, x1, y1 top-left origin) -> uv rect (u0, v0, u1, v1).

    UV v is flipped because image row 0 is v = 1.0 in Blender/GL.
    """
    x0, y0, x1, y1 = rect
    u0 = x0 / float(size)
    u1 = x1 / float(size)
    v0 = 1.0 - y1 / float(size)
    v1 = 1.0 - y0 / float(size)
    return (u0, v0, u1, v1)


def region_uv(name):
    """UV rect (u0, v0, u1, v1) of a semantic atlas region."""
    return px_rect_to_uv(REGIONS[name][1])


def region_pixel_size(name):
    x0, y0, x1, y1 = REGIONS[name][1]
    return (x1 - x0, y1 - y0)


def material_of_region(name):
    return REGIONS[name][0]


def region_of_material(material):
    return MATERIAL_FALLBACK_REGION[material]


def validate_contract():
    """Raise AssertionError if the contract itself is inconsistent."""
    assert len(MATERIAL_ORDER) == 4
    u_prev = 0.0
    for mat in MATERIAL_ORDER:
        u0, u1 = MATERIAL_BANDS[mat]
        assert abs(u0 - u_prev) < 1e-9, "material bands must be contiguous"
        u_prev = u1
    assert abs(u_prev - 1.0) < 1e-9
    used = set()
    for name, (mat, (x0, y0, x1, y1)) in REGIONS.items():
        assert mat in MATERIAL_ORDER, name
        assert 0 <= x0 < x1 <= ATLAS_SIZE and 0 <= y0 < y1 <= ATLAS_SIZE, name
        u0, v0, u1, v1 = px_rect_to_uv((x0, y0, x1, y1))
        bu0, bu1 = MATERIAL_BANDS[mat]
        assert bu0 - 1e-9 <= u0 and u1 <= bu1 + 1e-9, (
            "%s escapes material band %s: u %.4f..%.4f vs %.4f..%.4f"
            % (name, mat, u0, u1, bu0, bu1))
        for other in used:
            o = REGIONS[other][1]
            assert x1 <= o[0] or o[2] <= x0 or y1 <= o[1] or o[3] <= y0, (
                "regions overlap: %s %s" % (name, other))
        used.add(name)
    assert len(PORT_SPECS) == 3
    # v2: strict 3 x 3 x 1 envelope, flat roof apex at the walking surface.
    assert ENVELOPE["size_x"] == 3.0 and ENVELOPE["size_y"] == 1.0 \
        and ENVELOPE["size_z"] == 3.0
    assert ENVELOPE["min_x"] == -ENVELOPE["size_x"] / 2.0
    assert ENVELOPE["min_z"] == -ENVELOPE["size_z"] / 2.0
    assert abs(ROOF_PLATFORM_TOP - ENVELOPE["max_y"]) < 1e-9
    assert 0.0 < ROOF_CURB_H <= 0.04 + 1e-9
    assert ROOF_PLATFORM_TOP - ROOF_PLATFORM_THICKNESS > 0.0
    assert ROOF_CLEAR_ABOVE >= ROOF_CURB_H
    for tier, plan in ROOF_PLATFORM.items():
        assert tier in TIERS or tier == CONSTRUCTION_STAGE, tier
        assert 0.0 < plan["size_x"] <= ENVELOPE["size_x"], tier
        assert 0.0 < plan["size_z"] <= ENVELOPE["size_z"], tier
        # the flat top must be a real support surface, not a token strip
        assert plan["size_x"] >= 2.50 and plan["size_z"] >= 2.50, tier
        assert plan["size_x"] / 2.0 + 1e-9 < ENVELOPE["size_x"] / 2.0, tier
        assert plan["size_z"] / 2.0 + 1e-9 < ENVELOPE["size_z"] / 2.0, tier
    # indoor-expression contract: glass coverage must grow strictly with the tier
    order = ["Basic", "Intermediate", "Advanced", "Top"]
    mins = [V2_INDOOR["glass_area_min"][tier] for tier in order]
    assert mins == sorted(mins) and len(set(mins)) == len(mins), mins
    assert V2_INDOOR["shelf_levels"] >= V2_INDOOR["shelf_min_levels_checked"] >= 2
    return True


if __name__ == "__main__":
    validate_contract()
    print("atlas_layout contract OK: %d regions, %d ports, %d tiers x %d factions"
          % (len(REGIONS), len(PORT_SPECS), len(TIERS), len(FACTIONS)))
    for name in REGIONS:
        print("  %-11s %-12s px=%-22s uv=%s" % (
            name, REGIONS[name][0], REGIONS[name][1],
            tuple(round(v, 4) for v in region_uv(name))))
    for p in PORT_SPECS:
        print("  PORT %-11s asset=%s blender=%s" % (
            p["name"], p["asset"], blender_from_asset(p["asset"])))
