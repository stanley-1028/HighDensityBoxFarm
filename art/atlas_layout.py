"""HDBF Box Farm — shared art contract (atlas layout, ports, slots, tiers).

This module is the single source of truth shared by:
  * art/gen_textures.py   — paints the 2048x2048 PBR atlas into these pixel rects
  * art/gen_models.py     — packs every model face into one of these rects (UV)
  * art/validate_models.py — re-checks the exported assets against this contract

It is pure Python (no bpy / numpy) so it can be imported by any validator.

Coordinate systems
------------------
* "asset space" = Unity space required by requirement 13.3/13.6:
  X right, Y up, Z depth, building front faces -Z, pivot at footprint bottom
  centre: X in [-1.5, 1.5], Y in [0, 1], Z in [-1, 1].  Port coordinates in
  PORT_SPECS are given in this space (directly comparable to ch. 13.6 table).
* "blender space" = authoring space: Blender is Z-up, and the FBX/GLB export
  with Blender's standard axis conversion emits
      asset = ( blender.x, blender.z, -blender.y )
  so blender front is +Y and blender space bounds are
  X in [-1.5, 1.5], Y in [-1, 1] (front = +Y), Z in [0, 1] (up).

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
    "canvas":     ("M_HDBF_Fabric", (1846, 0,    2048, 1250)),
    "thatch":     ("M_HDBF_Fabric", (1846, 1254, 2048, 2048)),
}

#: fallback region for tools that only know a material name.
MATERIAL_FALLBACK_REGION = {
    "M_HDBF_Wood": "wood",
    "M_HDBF_Metal": "metal",
    "M_HDBF_Glass": "glass",
    "M_HDBF_Fabric": "canvas",
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
# ART-04 flat roof platform (no troughs, no roof objects)
# --------------------------------------------------------------------------- #
#: ART-04 (2026-10-25 user instruction) replaced the six raised crop troughs with
#: ONE empty flat platform.  The platform walking surface has the same height in
#: every tier / LOD / faction; only the plan size grows with the tier, which is
#: what keeps the four 50%-camera silhouettes distinguishable (validator C9).
#: No geometry may stand on the platform: only the thin perimeter curb (the
#: bounds apex at y = 1.0) is allowed outside the platform face.
#:
#: asset space (X width, Y up, Z depth; front -Z).  Platform is centred on the
#: footprint; the construction-stage greybox uses the same top and an almost
#: full-footprint plan so it reads as the same empty platform while half built.
ROOF_PLATFORM_TOP = 0.96        # walking surface of the ONE platform
ROOF_PLATFORM_THICKNESS = 0.05  # slab thickness (underside y = 0.91)
ROOF_CURB_H = 0.04              # thin perimeter edge, top = 1.00 (bounds apex)
ROOF_CURB_W = 0.04              # curb width
ROOF_PLATFORM = {
    "Basic":        {"size_x": 2.05, "size_z": 1.25},
    "Intermediate": {"size_x": 2.45, "size_z": 1.55},
    "Advanced":     {"size_x": 2.78, "size_z": 1.82},
    "Top":          {"size_x": 2.96, "size_z": 1.96},
    "ConstructionStage0": {"size_x": 2.86, "size_z": 1.86},
}


def roof_platform(tier):
    """Plan size (x, z) of the single empty roof platform for one tier."""
    return ROOF_PLATFORM[tier]["size_x"], ROOF_PLATFORM[tier]["size_z"]


# --------------------------------------------------------------------------- #
# ports: five port classes, six empty markers (requirement 13.6)
# --------------------------------------------------------------------------- #
#: coordinates are ASSET space (Unity Y-up, front -Z); "rot_blender" is the
#: euler rotation that makes the empty's local +Z point outwards in Blender.
PORT_SPECS = [
    {"name": "Entrance",  "object": "PORT_HDBF_Entrance",  "class": "entrance",
     "asset": (0.00, 0.00, -1.00),  "outward": (0, 0, -1), "size": (0.80, 0.60, 0.05),
     "rot_blender": (-90.0, 0.0, 0.0), "visible_from": "all",
     "note": "front facade centre, 0.8 x 0.6 clear opening"},
    {"name": "Power",     "object": "PORT_HDBF_Power",     "class": "power",
     "asset": (1.00, 0.85, 1.00),   "outward": (0, 0, 1),  "size": (0.12, 0.12, 0.06),
     "rot_blender": (90.0, 0.0, 0.0), "visible_from": "Top",
     "note": "rear right, junction box + insulator; cover plate on lower tiers"},
    {"name": "Pipe",      "object": "PORT_HDBF_Pipe",      "class": "pipe",
     "asset": (-1.50, 0.40, 0.00),  "outward": (-1, 0, 0), "size": (0.16, 0.16, 0.06),
     "rot_blender": (0.0, -90.0, 0.0), "visible_from": "all",
     "note": "left short side centre, flange + cover (Basic cover only)"},
    {"name": "Logistics", "object": "PORT_HDBF_Logistics", "class": "logistics",
     "asset": (-1.00, 0.35, 1.00),  "outward": (0, 0, 1),  "size": (0.80, 0.40, 0.06),
     "rot_blender": (90.0, 0.0, 0.0), "visible_from": "Intermediate+",
     "note": "rear left, conveyor opening; blank cover plate on Basic"},
    {"name": "Vent_01",   "object": "PORT_HDBF_Vent_01",   "class": "vent",
     "asset": (0.50, 1.00, -0.30),  "outward": (0, 1, 0),  "size": (0.30, 0.30, 0.05),
     "rot_blender": (0.0, 0.0, 0.0), "visible_from": "all",
     "note": "roof vent marker A; ART-04 removed the vent GEOMETRY (empty platform)"},
    {"name": "Vent_02",   "object": "PORT_HDBF_Vent_02",   "class": "vent",
     "asset": (-0.50, 1.00, 0.30),  "outward": (0, 1, 0),  "size": (0.30, 0.30, 0.05),
     "rot_blender": (0.0, 0.0, 0.0), "visible_from": "all",
     "note": "roof vent marker B; ART-04 removed the vent GEOMETRY (empty platform)"},
]

#: port tolerance in grid units (requirement 13.6).
PORT_TOLERANCE = 0.02

#: ports whose *visible geometry* is absent per tier (marker empties still exist,
#: so upgrade never shifts a port coordinate).  Requirement 13.6 / 13.2.
PORT_GEOMETRY_VISIBLE = {
    "Basic":        {"Entrance", "Pipe", "Vent_01", "Vent_02"},
    "Intermediate": {"Entrance", "Pipe", "Logistics", "Vent_01", "Vent_02"},
    "Advanced":     {"Entrance", "Pipe", "Logistics", "Vent_01", "Vent_02"},
    "Top":          {"Entrance", "Power", "Pipe", "Logistics", "Vent_01", "Vent_02"},
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
    assert len(PORT_SPECS) == 6
    # ART-04: one flat platform per tier, uniform top, curb apex == envelope top.
    assert abs(ROOF_PLATFORM_TOP + ROOF_CURB_H - 1.0) < 1e-9
    for tier, plan in ROOF_PLATFORM.items():
        assert tier in TIERS or tier == CONSTRUCTION_STAGE, tier
        assert 0.0 < plan["size_x"] <= 3.0 and 0.0 < plan["size_z"] <= 2.0, tier
        assert plan["size_x"] / 2.0 + 1e-9 < 1.5, tier
        assert plan["size_z"] / 2.0 + 1e-9 < 1.0, tier
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
