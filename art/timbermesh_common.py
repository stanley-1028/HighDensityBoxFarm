"""HDBF Box Farm - v2 Timbermesh export contract (pure Python, no bpy).

Single source of truth shared by:
  * art/export_timbermesh.py     (Blender headless exporter)
  * art/validate_timbermesh.py   (pure-Python validator, no Blender)
  * art/validate_models.py       (source FBX/GLB validator, Blender headless)

v2 (2026-10-26) changes the building from 3 x 2 x 1 to a strict **3 x 3 x 1**
indoor high-density farm.  The roof is ONE flat empty platform whose WALKING
SURFACE is the model apex at y = 1.00; the four tiers differ by facade /
openings / material layering, not by roof silhouette.  There are exactly three
`#Port#` empty markers and no `#Slot#` nodes.

Coordinate systems (all measured against the real game, see README-timbermesh.md)
--------------------------------------------------------------------------------
1. "contract space" = requirement space used by the source models:
   X right, Y up, Z depth, building front faces -Z, pivot at the footprint
   bottom CENTRE: X in [-1.5, 1.5], Y in [0, 1], Z in [-1.5, 1.5].

2. "source Blender authoring space" = what art/models/*/LOD*.fbx contain:
   Blender Z-up, front faces +Y, geometry spans X [-1.5, 1.5], Y [-1.5, 1.5]
   (front = +Y), Z [0, 1] (up).  Mapping to contract space is
   contract = (blender.x, blender.z, -blender.y).

3. "export space" = the coordinate space inside a .timbermesh file = the game's
   block-object local space.  MEASURED from the shipped example model
   ShantySpeaker.IronTeeth.Model.timbermesh (assets of the official modding SDK)
   and cross-checked against vanilla blueprints' CollidersSpec: it is
   BLOCK-CORNER origin, X 0..sizeX (width), Y 0..sizeY (height, UP),
   Z 0..sizeZ (depth), front at Z = 0 / -Z.
   For this 3 x 3 x 1 building:
       export = (contract.x + 1.5, contract.y, contract.z + 1.5)
   i.e. the model must span X [0, 3], Y [0, 1], Z [0, 3].  The requirement
   document assumed a centred pivot; the game does not, so the exporter shifts
   by half a footprint.  This is recorded as a deviation in the report.

The official Blender plugin writes
    export = (-blender.x, blender.z, -blender.y)
(see node_builder.__save_node_transform / __save_node_vertex_properties: it
mirrors X and Z; triangle winding is reversed to compensate, so authored
outward normals stay outward).  Therefore, to get a desired export coordinate
the exporter must author the Blender scene as
    blender = (-export.x, -export.z, export.y)
which for the v2 source models (contract = (blender.x, blender.z, -blender.y))
is
    blender_export = (-src.x - 1.5, src.y - 1.5, src.z)
"""

from __future__ import annotations

import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
WORKSPACE = os.path.dirname(HERE)
if HERE not in sys.path:
    sys.path.insert(0, HERE)

import atlas_layout as A  # noqa: E402  (pure python shared art contract)

#: vendored official plugin (MIT, v1.2.0) - the only copy the pipeline uses.
VENDORED_PLUGIN_DIR = os.path.join(HERE, "tools", "timbermesh_blender_plugin")
#: output directory for all .timbermesh files.
TIMBERMESH_DIR = os.path.join(HERE, "timbermesh")

TIERS = ["Basic", "Intermediate", "Advanced", "Top"]
FACTIONS = ["Folktails", "IronTeeth"]

#: block size of the building (v2: 3 x 3 x 1): X width, Y height, Z depth.
SIZE_X, SIZE_Y, SIZE_Z = 3.0, 1.0, 3.0
#: strict envelope tolerance used by both validators (grid units).
BOUNDS_TOLERANCE = 0.02
POSITION_TOLERANCE = 0.02

#: per-LOD0 triangle budget (requirement 13.5).
TRI_BUDGET_LOD0 = 6000
TRI_BUDGET_LOD1 = 2400
TRI_BUDGET_LOD2 = 800

#: model file stem pattern: BoxFarm.<Tier>.<Faction>[.<Stage>].Model
def model_stem(tier: str, faction: str, construction: bool = False) -> str:
    if construction:
        return "BoxFarm.%s.%s.ConstructionStage0.Model" % (tier, faction)
    return "BoxFarm.%s.%s.Model" % (tier, faction)


def model_filename(tier: str, faction: str, construction: bool = False) -> str:
    return model_stem(tier, faction, construction) + ".timbermesh"


def model_path(tier: str, faction: str, construction: bool = False) -> str:
    return os.path.join(TIMBERMESH_DIR, model_filename(tier, faction, construction))


def source_fbx(tier: str, faction: str, construction: bool = False) -> str:
    if construction:
        return os.path.join(HERE, "models", "ConstructionStage0", "ConstructionStage0.fbx")
    return os.path.join(HERE, "models", "%s.%s" % (tier, faction), "LOD0.fbx")


def all_model_specs() -> list[dict]:
    """Every .timbermesh the ART-02 card requires (8 finished + 8 construction)."""
    specs = []
    for tier in TIERS:
        for faction in FACTIONS:
            specs.append({
                "tier": tier, "faction": faction, "construction": False,
                "stem": model_stem(tier, faction, False),
                "path": model_path(tier, faction, False),
                "source": source_fbx(tier, faction, False),
                "materials": MATERIAL_MAP[(tier, faction, False)],
            })
    for tier in TIERS:
        for faction in FACTIONS:
            specs.append({
                "tier": tier, "faction": faction, "construction": True,
                "stem": model_stem(tier, faction, True),
                "path": model_path(tier, faction, True),
                "source": source_fbx(tier, faction, True),
                "materials": MATERIAL_MAP[(tier, faction, True)],
            })
    return specs


# --------------------------------------------------------------------------- #
# vanilla material names (whitelist from the ART-02 card; names are the exact
# vanilla material asset names in the game's MaterialCollections)
# --------------------------------------------------------------------------- #

MATERIAL_WHITELIST = {
    "Folktails": [
        "BaseWood_Brown.Folktails",
        "BaseWood_LightBrown.Folktails",
        "BaseWood_White.Folktails",
        "IrregularPlanks_Brown.Folktails",
        "IrregularPlanks_LightBrown.Folktails",
        "PaintedMetal.Folktails",
        "BaseMetal.Folktails",
        "WindowsAtlas.Folktails",
        "ThatchedRoof.Folktails",
        "Plaster_White.Folktails",
        "Details.Folktails",
        # v2.2 reference-image upgrade: vanilla moss material (already part of
        # MaterialCollection.Folktails) for the Basic moss accents / crop silhouette.
        "Moss.Folktails",
    ],
    "IronTeeth": [
        "BaseWood_DarkBrown.IronTeeth",
        "BaseWood_Grey.IronTeeth",
        "IrregularPlanks_DarkBrown.IronTeeth",
        "IrregularPlanks_MetalForged.IronTeeth",
        "PaintedMetal.IronTeeth",
        "BaseMetal.IronTeeth",
        "WindowsAtlas.IronTeeth",
        "RoofPlanks.IronTeeth",
        "Plaster_Orange.IronTeeth",
        "Details.IronTeeth",
    ],
}

#: ART-01 material slot -> vanilla material asset name, per tier/faction.
#: The ART-01 slot meanings are fixed (0 Wood, 1 Metal, 2 Glass, 3 Fabric);
#: each tier maps them onto the card's material strategy.  ART-05 (2026-10-27)
#: re-mapped the metal-heavy tiers after the user's in-game texture-bug report:
#:   Basic        = dark timber + moss / dark roof planks          (2 slots)
#:   Intermediate = light/honey timber + plaster + glass            (3 slots)
#:   Advanced     = painted/base metal shell + glass + wood patches (4 slots)
#:   Top          = dark heavy plated shell + glass + metal/copper accents
#:   Construction = raw wood + bare metal                           (2 slots)
#:
#: **PaintedMetal.* is banned from MATERIAL_MAP on purpose.**  Its whole albedo
#: PNG is a yellow/black (FT) resp. blue/black (IT) warning-stripe texture; using
#: it for a large shell is exactly the "yellow/black top tier" the user reported.
#: It stays in MATERIAL_WHITELIST so the whitelist stays a superset of the real
#: faction MaterialCollection, but it must never be assigned to a shipped model
#: (asserted below and re-checked by the art validators and L1 tests).
MATERIAL_MAP = {
    ("Basic", "Folktails", False):
        # v2.2 reference-image upgrade kept: dark-brown rough timber + vanilla
        # moss accents (the thatched roof was replaced by a dark timber roof, so
        # the second slot carries the moss/foliage material instead of thatch).
        {"M_HDBF_Wood": "BaseWood_Brown.Folktails",
         "M_HDBF_Fabric": "Moss.Folktails"},
    ("Basic", "IronTeeth", False):
        {"M_HDBF_Wood": "BaseWood_DarkBrown.IronTeeth",
         "M_HDBF_Fabric": "RoofPlanks.IronTeeth"},
    ("Intermediate", "Folktails", False):
        # honey timber (BaseWood_1K_D_White * warm _Color tint) + cream plaster.
        {"M_HDBF_Wood": "BaseWood_LightBrown.Folktails",
         "M_HDBF_Glass": "WindowsAtlas.Folktails",
         "M_HDBF_Fabric": "Plaster_White.Folktails"},
    ("Intermediate", "IronTeeth", False):
        # light-grey timber + the orange IronTeeth plaster tint.
        {"M_HDBF_Wood": "BaseWood_Grey.IronTeeth",
         "M_HDBF_Glass": "WindowsAtlas.IronTeeth",
         "M_HDBF_Fabric": "Plaster_Orange.IronTeeth"},
    ("Advanced", "Folktails", False):
        # ART-05: PaintedMetal (yellow/black hazard) -> BaseMetal flat painted
        # sheet metal; whitewashed timber patch plates + detail/rivet plates.
        {"M_HDBF_Wood": "BaseWood_White.Folktails",
         "M_HDBF_Metal": "BaseMetal.Folktails",
         "M_HDBF_Glass": "WindowsAtlas.Folktails",
         "M_HDBF_Fabric": "Details.Folktails"},
    ("Advanced", "IronTeeth", False):
        # ART-05: grey-blue BaseMetal steel shell + dark detail plates.
        {"M_HDBF_Wood": "IrregularPlanks_MetalForged.IronTeeth",
         "M_HDBF_Metal": "BaseMetal.IronTeeth",
         "M_HDBF_Glass": "WindowsAtlas.IronTeeth",
         "M_HDBF_Fabric": "Details.IronTeeth"},
    ("Top", "Folktails", False):
        # ART-05: dark heavy plated shell from the Details atlas (its banner /
        # logo / canvas regions are excluded from the patch) + steel accents.
        {"M_HDBF_Wood": "IrregularPlanks_Brown.Folktails",
         "M_HDBF_Metal": "Details.Folktails",
         "M_HDBF_Glass": "WindowsAtlas.Folktails",
         "M_HDBF_Fabric": "BaseMetal.Folktails"},
    ("Top", "IronTeeth", False):
        # ART-05: dark heavy plated shell + forged copper/steel accents ("brass"
        # accent language of the reference lineup) + very dark timber trim.
        {"M_HDBF_Wood": "BaseWood_DarkBrown.IronTeeth",
         "M_HDBF_Metal": "Details.IronTeeth",
         "M_HDBF_Glass": "WindowsAtlas.IronTeeth",
         "M_HDBF_Fabric": "IrregularPlanks_MetalForged.IronTeeth"},
}
for _tier in TIERS:
    MATERIAL_MAP[(_tier, "Folktails", True)] = {
        "M_HDBF_Wood": "BaseWood_Brown.Folktails",
        "M_HDBF_Metal": "BaseMetal.Folktails",
    }
    # ART-05: the construction stage is ONE shared source mesh (same geometry +
    # UVs) exported for both factions, so the wood slot must use materials whose
    # patches were searched JOINTLY and share the same UV window (see
    # `construction_shared_patches` in art/material_texture_map.json):
    #   BaseWood_Brown.Folktails (FT) <-> RoofPlanks.IronTeeth (IT)
    #   BaseMetal.Folktails (FT)      <-> BaseMetal.IronTeeth (IT)
    # RoofPlanks reads as raw dark construction timber for the IronTeeth greybox.
    MATERIAL_MAP[(_tier, "IronTeeth", True)] = {
        "M_HDBF_Wood": "RoofPlanks.IronTeeth",
        "M_HDBF_Metal": "BaseMetal.IronTeeth",
    }

assert all(len(m) <= 4 for m in MATERIAL_MAP.values())
for _faction, _names in MATERIAL_WHITELIST.items():
    for _mapping in MATERIAL_MAP.values():
        for _target in _mapping.values():
            if _target.endswith("." + _faction):
                assert _target in _names, (_faction, _target)
# every mapping must be faction-correct
for (_t, _f, _c), _mapping in MATERIAL_MAP.items():
    for _target in _mapping.values():
        assert _target.endswith("." + _f), (_t, _f, _target)

# ART-05 hard rule: the warning-stripe paint materials must never be mapped to a
# shipped model (user bug 2026-10-27: yellow/black top tier).  Enforced again by
# art/validate_timbermesh.py, art/validate_models.py and the L1 contract tests.
BANNED_MATERIALS = ("PaintedMetal.Folktails", "PaintedMetal.IronTeeth")
for _mapping in MATERIAL_MAP.values():
    for _target in _mapping.values():
        assert _target not in BANNED_MATERIALS, (
            "BANNED_MATERIALS used in MATERIAL_MAP: %r" % (_target,))


# --------------------------------------------------------------------------- #
# empties: connection ports only (v2 keeps exactly the three #Port# markers)
# --------------------------------------------------------------------------- #
#: v2 (2026-10-26) port coordinates in CONTRACT space (centred pivot); the new
#: 3 x 3 footprint re-anchors the depth to +-1.50 while keeping the v1 heights
#: and roles.  There are exactly 3 ports and no `#Slot#` marker.
PORT_SPECS = [
    # name, contract (x, y, z), outward direction, requirement note
    ("#Port#Power", (1.00, 0.85, 1.50), (0.0, 0.0, 1.0), "back-right, power junction box"),
    ("#Port#Pipe", (-1.50, 0.40, 0.00), (-1.0, 0.0, 0.0), "left short side, pipe flange"),
    ("#Port#Logistics", (-1.00, 0.35, 1.50), (0.0, 0.0, 1.0), "back-left, conveyor opening"),
]

#: v2 (2026-10-26): the roof is ONE EMPTY FLAT PLATFORM.  Its WALKING SURFACE is
#: the model apex at y = 1.00; nothing may exist above it and a 30 cm clearance
#: cone above the surface is empty.  The perimeter "收边" is a flush fascia band
#: (height <= 0.04) whose top is the walking surface, so it cannot block
#: stacking.  These constants are the frozen contract the exporter and both
#: validators check.
PLATFORM_TOP = A.ROOF_PLATFORM_TOP                  # walking surface y (== apex)
PLATFORM_CURB_TOP = PLATFORM_TOP                    # flush: curb top == walking surface
PLATFORM_CURB_H = A.ROOF_CURB_H                     # flush fascia band height
PLATFORM_CURB_W = A.ROOF_CURB_W
PLATFORM_CLEAR_ABOVE = A.ROOF_CLEAR_ABOVE           # empty 30 cm above the surface
PLATFORM_PLAN = {tier: (spec["size_x"], spec["size_z"])
                 for tier, spec in A.ROOF_PLATFORM.items()}

assert len(PORT_SPECS) == 3
assert abs(PLATFORM_CURB_TOP - PLATFORM_TOP) < 1e-9
assert PLATFORM_CURB_H <= 0.04 + 1e-9
assert "ConstructionStage0" in PLATFORM_PLAN


def contract_to_export(p):
    """contract space (centred pivot) -> export space (block-corner origin)."""
    x, y, z = p
    return (x + SIZE_X / 2.0, y, z + SIZE_Z / 2.0)


def platform_export_rect(tier):
    """(x0, x1, z0, z1) of the single empty roof platform in export space."""
    size_x, size_z = PLATFORM_PLAN[tier]
    x0, x1 = SIZE_X / 2.0 - size_x / 2.0, SIZE_X / 2.0 + size_x / 2.0
    z0, z1 = SIZE_Z / 2.0 - size_z / 2.0, SIZE_Z / 2.0 + size_z / 2.0
    return (x0, x1, z0, z1)


def platform_top_export():
    """Platform walking-surface height (export space Y is contract Y)."""
    return PLATFORM_TOP


def platform_clear_ceiling():
    """No vertex may be above this Y (platform top + 0.06)."""
    return PLATFORM_TOP + PLATFORM_CLEAR_ABOVE


def export_to_blender(p):
    """export space -> Blender authoring space for the plugin (see module doc)."""
    x, y, z = p
    return (-x, -z, y)


def source_to_export_blender(p):
    """ART-01 Blender authoring space -> Blender space the plugin must receive.

    p is a point of art/models/*/LOD0.fbx.  Derived by composing
    source -> contract -> export -> blender(plugin):
        bx' = -bx - 1.5
        by' =  by - 1.0
        bz' =  bz
    (X is mirrored, because the plugin mirrors X; the mirror is undone for the
    net contract->export orientation, and the winding is flipped back in the
    exporter so outward normals stay outward.)
    """
    x, y, z = p
    return (-x - SIZE_X / 2.0, y - SIZE_Z / 2.0, z)


#: every empty as (name, export-space position) and Blender-space location.
def empty_specs_export():
    return [(name, contract_to_export(pos)) for name, pos, _o, _n in PORT_SPECS]


def empty_specs_blender():
    return [(name, export_to_blender(export_pos)) for name, export_pos in empty_specs_export()]


def empty_names():
    return [name for name, _ in empty_specs_export()]


def port_export_positions():
    """expected export-space positions of the 3 #Port#* markers."""
    return {name: contract_to_export(pos) for name, pos, _o, _n in PORT_SPECS}


#: transforms in matrix form are given in art/export_timbermesh.py (bpy types);
#: the numbers above are kept here so the pure-Python validator can check them.
MIRROR_X_AND_SHIFT = (-1.0, 0.0, 0.0, -SIZE_X / 2.0)   # bx' = -bx - 1.5
SHIFT_Y = (0.0, 1.0, 0.0, -SIZE_Z / 2.0)               # by' =  by - 1.0


if __name__ == "__main__":
    print("model specs: %d" % len(all_model_specs()))
    for spec in all_model_specs():
        print("  %-52s <- %s" % (spec["stem"], os.path.relpath(spec["source"], WORKSPACE)))
        print("      materials: %s" % sorted(set(spec["materials"].values())))
    print("\nempties (export space, block-corner origin):")
    for name, pos in empty_specs_export():
        bx, by, bz = export_to_blender(pos)
        print("  %-22s export=(%.2f, %.2f, %.2f)  blender=(%.2f, %.2f, %.2f)" % (
            name, pos[0], pos[1], pos[2], bx, by, bz))
    print("\nbounds: X [0, %.1f] Y [0, %.1f] Z [0, %.1f]" % (SIZE_X, SIZE_Y, SIZE_Z))
