#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""HDBF v2 - pure-Python .timbermesh validator (no Blender, no bpy).

Parses every expected ``art/timbermesh/BoxFarm.<Tier>.<Faction>[.ConstructionStage0].Model
.timbermesh`` with the vendored official Blender plugin's protobuf schema
(``art/tools/timbermesh_blender_plugin/model_pb2.py``) after zlib decompression,
then checks the frozen HDBF-v2 export contract from ``art/timbermesh_common.py``.

A ``.timbermesh`` file is ``zlib.compress(model_pb2.Model.SerializeToString())``:

    Model { version:int, name:string, nodes:[Node] }
    Node  { parent:int, name:string, position/rotation/scale, vertexCount:int,
            vertexProperties:[VertexProperty], meshes:[Mesh] }
    VertexProperty { name:string, scalarType:int, scalarTypeDimension:int, data:bytes }
    Mesh { indices:[int], material:string }

``VertexProperty.data`` is little-endian float32 packed, dimension =
``scalarTypeDimension`` (2 = uv0/uv1/uv2, 3 = position/normal, 4 = tangent/color)
and its length must be ``vertexCount * dim * 4``.

Checks per file (all eight must PASS):
  C1 parses: zlib.decompress + protobuf ParseFromString without exception.
  C2 identity: Model.name == file stem; Model.version == 0.
  C3 mesh bounds (export space, block-corner origin): size == (3.0, 1.0, 3.0)
     and min == (0, 0, 0), each +-0.02 (strict v2 envelope).
  C4 triangles: sum(len(mesh.indices)//3) <= 6000 (tier LOD0 budget).
  C5 materials: 1..4 distinct, every mesh.material in the faction whitelist.
  C6 empties + flat roof: exactly 3
     #Port#Power/#Port#Pipe/#Port#Logistics (vertexCount == 0, no meshes,
     node position within +-0.02 of the export-space contract), NO #Slot#
     node at all, and the roof is ONE flat empty platform: the WALKING SURFACE
     is the model apex at y == 1.00, NOTHING exists above y == 1.00 (the 30 cm
     above the surface is therefore empty), the top face exists inside the
     near-full platform plan, and the perimeter fascia never rises above 1.00.
  C7 vertex properties: names subset of {position, normal, tangent, color,
     uv0, uv1, uv2}; position/normal/tangent present on every node with
     vertexCount > 0; no NaN/Inf; FLOAT scalar type; document dimensions;
     data length == vertexCount*dim*4.
  C8 topology: indices in [0, vertexCount); node parent == -1 or < own index;
     exactly one root node whose name == file stem; uv0 in [0,1] +-0.001.
  C9 material canvas UV containment (ART-07): every uv0 value used by a
     material's faces lies inside that material's ART-07 canvas: tile materials
     inside the frozen 2-texel guard band (2/1024), patch materials inside
     ``uv_canvas.uv_rect`` from ``art/material_texture_map.json`` (tolerance
     1e-4); per-material uv0 min/max is reported and a single escaping vertex
     FAILS.

Cross-file checks (ART-05 material texture fix + ART-07 world-scale UVs,
2026-10-27; reported separately):
  C10 material patch map coverage: ``art/material_texture_map.json`` exists, its
      schema is ``hdbf.material_texture_map/v1``, every material used by a shipped
      model (and every material in ``timbermesh_common.MATERIAL_MAP``) has an
      entry, every patch ``pixel_rect`` fits ``texture_size`` and ``uv_rect``
      equals ``[x0/W, 1-y1/H, x1/W, 1-y0/H]`` within JSON rounding.
  C11 real _D texture variance re-verification: decode the real vanilla albedo
      PNG of every patch with ``art/png_stats.py``, re-compute the patch mean RGB
      and std (max channel std, 0..255), compare to the recorded values
      (mean +-0.5 / std +-0.5) and check ``std <= std_max`` (class-based, default
      18.0/255).  SKIP with a clear note only when the texture roots are absent.
  C12 hazard / large-shell policy: ``large_shell_policy.forbidden_materials`` must
      list the yellow-black hazard materials ``PaintedMetal.Folktails`` /
      ``PaintedMetal.IronTeeth``; no shipped/scheduled model may use one; a patch
      whose ``patch_class``/``reject_tags`` marks a hazard/logo/text region may
      not be used; usable patch rects must not overlap recorded exclusion rects.
  C15 ART-07 world-UV texel density: the shipped positions are converted back to
      the Blender author space (``author = (x-1.5, 1.5-z, y)``) and, per material
      of every shipped file, ``sum(uv_area)/sum(projected world_area)`` (weighted
      by ``texture_size``) must match the map ``px_per_m`` +-2 %, every triangle
      above 0.05 m^2 must be within +-25 %, tile materials must be in the REQ
      band 300-600 px/m, patch materials must stay >= 64 px/m (2x the retired
      ART-06 density; the WindowsAtlas glass pane is 82.4 px/m by design), no
      NaN/Inf is allowed and the exact UV recomputation (frozen projection
      tie-break, +-1e-3) must pass for every non-ambiguous triangle (ambiguous
      plane: top-two rounded |normal| components within 1e-3; <= 15 % of
      triangles / <= 2 % of world area).  The 8 ConstructionStage0 files share
      ONE baked Folktails UV set and are measured against the Folktails source
      material of the ``construction_shared_patches`` window.
  C16 ART-07 UV mapping contract / determinism: the map carries the sec.4 fields for
      every used material, ``px_per_m`` reproduces from the usable canvas and
      coverage, ``wrap``/``tile_meters``/``uv_canvas``/``patch`` are valid, the
      construction shared windows resolve both factions, and the same
      ``px_per_m``/canvas is used by every shipped file.

NEGATIVE CONTROLS (requirement D) run at the end of every normal run *and* under
``--self-test``: (1) a shipped uv0 shifted outside its ART-07 canvas must be
rejected by C9; (2) a synthetic patch record over the real yellow-black hazard
stripes of ``PaintedMetal_D.Folktails.png`` must be rejected by C11; (3) removing
one required material from an in-memory map copy must be rejected by C10; (4) a
tile material's UVs scaled x0.5 and (5) x2.0 must be rejected by C15; (6) one
large patch-mode face stretched to the full 0..1 UV square must be rejected by
C9.  The run exits nonzero if any control is NOT rejected.

The report (``art/timbermesh-validation.txt``) is deterministic: identical input
files produce a byte-identical report (no timestamps, no elapsed times).

Usage:
    python art/validate_timbermesh.py
    python art/validate_timbermesh.py --no-report --reference
    python art/validate_timbermesh.py --self-test
    python art/validate_timbermesh.py --probe art/timbermesh/axis-test/BoxFarm.AxisTest.Model.timbermesh

Exit code 0 only when all 16 expected files are present and every check of every
expected file passes, the cross-file material checks PASS (C11 may be SKIP only
when no vanilla texture root exists) and all three negative controls are
rejected.  ``--probe``/``--reference`` are diagnostic only and never affect the
exit code (a vanilla asset is expected to fail the contract checks; it is used to
prove the parser works and to show its diagnosis).  ``--self-test`` runs only the
negative controls and exits 0 only when all three are rejected.
"""

from __future__ import annotations

import argparse
import json
import math
import os
import platform
import struct
import sys
import traceback
import zlib

HERE = os.path.dirname(os.path.abspath(__file__))
WORKSPACE = os.path.dirname(HERE)

# contract module (single source of truth; pure python, no bpy)
sys.path.insert(0, HERE)
import timbermesh_common as C  # noqa: E402

# ART-05 (2026-10-27) material patch contract: the map accessor lives in
# material_patches.py (with a JSON fallback so the validator still works while
# the module is being rolled over); the PNG statistics decoder is png_stats.py.
try:
    import material_patches as MP  # noqa: E402
    MP_ERR = None
except Exception as _exc:  # pragma: no cover - defensive
    MP = None
    MP_ERR = "%s: %s" % (type(_exc).__name__, _exc)

try:
    import png_stats as PS  # noqa: E402
    PS_ERR = None
except Exception as _exc:  # pragma: no cover - defensive
    PS = None
    PS_ERR = "%s: %s" % (type(_exc).__name__, _exc)

# vendored official Timbermesh Blender plugin (MIT) - protobuf schema lives here
sys.path.insert(0, C.VENDORED_PLUGIN_DIR)
import model_pb2  # noqa: E402

UV_TOLERANCE = 0.001
SCALAR_TYPE_FLOAT = 4
ALLOWED_PROPERTY_NAMES = ("position", "normal", "tangent", "color", "uv0", "uv1", "uv2")
PROPERTY_DIMS = {"position": 3, "normal": 3, "tangent": 4, "color": 4,
                 "uv0": 2, "uv1": 2, "uv2": 2}
REQUIRED_PROPERTY_NAMES = ("position", "normal", "tangent")

# real vanilla asset from the official modding SDK - parser cross-check only
REFERENCE_PATH = os.path.join(
    r"C:\Users\user\.hdbf\sdk\sdk-repo\timberborn-modding-main",
    "Assets", "Mods", "ShantySpeaker", "Data", "Buildings", "Decorations",
    "ShantySpeaker", "ShantySpeaker.IronTeeth.Model.timbermesh")

# --------------------------------------------------------------------------- #
# ART-05 material texture patch contract (2026-10-27)
# --------------------------------------------------------------------------- #
# Why: the old pipeline assumed all vanilla materials shared one 2048 atlas.  In
# game each vanilla MaterialCollection material resolves to its OWN 1024x1024
# `_D` albedo texture plus a material `_Color` tint (verified by the Lead from
# game IL + an AssetStudio dump), so the atlas patchwork produced a checkerboard
# surface and the top tier sampled the yellow-black PaintedMetal hazard stripes.
# art/material_texture_map.json now records, per material, the real texture and
# one flat patch (pixel + uv rect + measured mean RGB/std + thresholds); the
# generator maps every face of a material into its material's patch.
MAP_SCHEMA = "hdbf.material_texture_map/v1"
MAP_PATH = os.path.join(HERE, "material_texture_map.json")

#: tolerance for uv0-in-patch containment.  float32 vertex data and the
#: 6-decimal uv_rect in the JSON both round below 5e-7, so 1e-4 is an order of
#: magnitude looser than the data and still far tighter than the smallest patch.
PATCH_UV_TOL = 1e-4
#: allowed |recorded - re-measured| for patch mean RGB / std (JSON rounds to 6 dp;
#: the PNG decode is bit-exact, so this only absorbs JSON rounding).
PATCH_MEAN_TOL = 0.5
PATCH_STD_TOL = 0.5
#: fallback flattening limit when the map has neither a per-patch std_max nor a
#: class threshold (18/255, the ART-05 uniformity requirement).
PATCH_STD_MAX_DEFAULT = 18.0
#: uv_rect formula check: 6-decimal JSON values round below 5e-7 per component.
UV_RECT_ROUND_TOL = 1.5e-6

#: forbidden large-shell / hazard materials: vanilla `PaintedMetal.*` is the
#: yellow-black hazard-stripe texture.  It must never appear on a shipped model.
HAZARD_MATERIALS = ("PaintedMetal.Folktails", "PaintedMetal.IronTeeth")
#: tag substrings used for reporting a patch/entry's avoid/exclusion tags.  NOTE:
#: in the Lead's map `patch.reject_tags` lists the hazards the patch AVOIDS (e.g.
#: "hazard-stripes", "faction-banner/logo", "text") on every patch, so those tags
#: alone never mark a patch as hazardous (see is_hazard_marked).
HAZARD_TAGS = ("hazard", "stripe", "logo", "text", "large_shell", "exclude")
#: a patch is unusable when its class says so, an explicit flag says so, or the
#: recorded hazard pixel fraction is above this small epsilon.
HAZARD_CLASSES = ("hazard", "hazard-region", "hazard-stripe", "hazard-stripes",
                  "forbidden", "excluded")
HAZARD_FRACTION_MIN = 0.02
#: 192x192 window over PaintedMetal_D.Folktails.png that is yellow+black hazard
#: stripes (measured: ~40% near-black and ~40% near-yellow pixels, patch std
#: ~60/255).  Used by negative control 2 to prove the variance checker rejects
#: a hazard region even when it is mislabelled as a normal metal patch.
HAZARD_CONTROL_TEXTURE = "PaintedMetal_D.Folktails.png"
HAZARD_CONTROL_PIXEL_RECT = (384, 416, 576, 608)
HAZARD_CONTROL_TEXTURE_SIZE = (1024, 1024)
#: vanilla texture roots of the reference machine; the map repeats them in
#: "texture_roots" and HDBF_VANILLA_TEX_ROOT overrides/adds a root directory.
DEFAULT_TEXTURE_ROOTS = [
    os.path.join(os.path.expanduser("~"), ".hdbf", "sdk", "vanilla-tex-all"),
    os.path.join(os.path.expanduser("~"), ".hdbf", "sdk", "vanilla-tex-extra"),
]

# --------------------------------------------------------------------------- #
# ART-07 (2026-10-27) world-scale UV mapping contract (art/ART07_UV_TILING.md)
#
# The shipped UVs are a world-scale planar projection into each material's own
# texture canvas (the game's runtime AutoAtlaser remaps every UV into the
# material's atlas cell, so a UV component > 1 would sample another material).
# The .timbermesh positions are in game/export space; the frozen mapping is
# defined in the Blender author space, so this validator converts every vertex
# back with export -> author = (x - 1.5, 1.5 - z, y) before re-deriving the UVs
# and the texel density.  C9 becomes mode-aware, C15/C16 are the new cross-file
# ART-07 checks (mirrored in art/validate_models.py).
# --------------------------------------------------------------------------- #
UV_POLICY_SCHEMA = "hdbf.world_uv/v1"
UV_WORLD_CENTER = (0.0, 0.0, 0.5)
UV_GUARD_TEXELS = 2
TILE_GUARD_TOL = 2e-6
DENSITY_AGG_TOL = 0.02
DENSITY_TRI_TOL = 0.25
DENSITY_TRI_MIN_WORLD_AREA = 0.05
#: C15 regression floor for patch materials: 2x the retired ART-06 density
#: (32 px/m) = 64 px/m.  sec.5 suggests 4x (128) but the WindowsAtlas glass pane is
#: an intentionally small canvas at 82.4 px/m; the exact px/m is still checked
#: against the map (+-2 %) and tile materials still need the 300-600 band.
DENSITY_PATCH_MIN = 64.0
TILE_DENSITY_BAND_DEFAULT = (300.0, 600.0)
TILE_METERS_BAND_FALLBACK = {
    "wood": (2.2, 2.6),
    "planks": (2.2, 2.6),
    "forged_planks": (2.2, 2.6),
    "plaster": (2.4, 2.8),
    "metal": (1.8, 2.2),
    "roof_planks": (2.0, 2.4),
    "moss": (2.0, 2.4),
}
PROJECTION_ROUND_DECIMALS = 4
AMBIGUOUS_PLANE_TOL = 1e-3
AMBIGUOUS_COUNT_MAX = 0.15
AMBIGUOUS_AREA_MAX = 0.02
EXACT_UV_TOL = 1e-3
UV_CENTER_TOL = 1e-6
PX_PER_M_ROUND = 0.101
UV_CANVAS_ROUND_TOL = 1.5e-9
#: export space -> Blender author space: export = (bx+1.5, bz, 1.5-by) (see
#: art/timbermesh_common.py), so author = (export.x-1.5, 1.5-export.z, export.y).
EXPORT_SIZE_HALF_X = 1.5
EXPORT_DEPTH_HALF_Z = 1.5

CHECK_DEFS = [
    ("C1", "parses (zlib + protobuf)"),
    ("C2", "model identity (name/version)"),
    ("C3", "mesh bounds (3x1x3 @ corner)"),
    ("C4", "triangles <= budget"),
    ("C5", "materials (whitelist + count)"),
    ("C6", "#Port# empties + flat empty roof"),
    ("C7", "vertex properties"),
    ("C8", "topology (indices/parents/uv0)"),
    ("C9", "material patch UV containment"),
]

CHECK_DESCRIPTIONS = {
    "C1": "zlib.decompress(file) then model_pb2.Model().ParseFromString(data), no exception",
    "C2": "Model.name == file stem (e.g. 'BoxFarm.Basic.Folktails.Model'), Model.version == 0",
    "C3": "mesh bounds: size == (%.1f, %.1f, %.1f) and min == (0, 0, 0), each +-%.2f "
          "(game model space is block-corner origin, NOT centred; strict v2 envelope)" % (
              C.SIZE_X, C.SIZE_Y, C.SIZE_Z, C.BOUNDS_TOLERANCE),
    "C4": "triangles = sum(len(mesh.indices)//3) over all nodes; must be <= %d "
          "(timbermesh_common.TRI_BUDGET_LOD0; index counts must be multiples of 3)" % C.TRI_BUDGET_LOD0,
    "C5": "1 <= distinct mesh.material count <= 4, and every material name is in the "
          "faction-specific vanilla whitelist from timbermesh_common.MATERIAL_WHITELIST",
    "C6": "exactly the 3 nodes #Port#Power/#Port#Pipe/#Port#Logistics (vertexCount == 0, no "
          "meshes, position within +-%.2f of the contract), NO #Slot# node at all, and the roof "
          "is ONE flat empty platform: walking surface == model apex y == %.2f inside the "
          "near-full plan, NOTHING above y == %.2f (30 cm clearance), top face present"
          % (C.POSITION_TOLERANCE, C.PLATFORM_TOP, C.PLATFORM_TOP),
    "C7": "vertex property names subset of {position, normal, tangent, color, uv0, uv1, uv2}; "
          "'position','normal','tangent' present on every node with vertexCount > 0; no NaN/Inf; "
          "scalarType FLOAT(4); dim 3/3/4/4/2/2/2; data length == vertexCount*dim*4",
    "C8": "all mesh indices in [0, vertexCount); every node parent is -1 or < its own index; "
          "exactly one root node (parent == -1) and its name == file stem; uv0 values in "
          "[0, 1] +-%.3f" % UV_TOLERANCE,
    "C9": "ART-07: for every material used by a mesh, every uv0 value referenced by that "
          "material's faces lies inside the material's ART-07 canvas: tile materials inside "
          "the frozen 2-texel guard band (2/1024), patch materials inside "
          "art/material_texture_map.json's uv_canvas.uv_rect (tolerance %.0e); per-material "
          "uv0 min/max is reported and ONE escaping vertex FAILS" % 1e-4,
}


# --------------------------------------------------------------------------- #
# small helpers
# --------------------------------------------------------------------------- #

def _rel(path):
    """Path relative to the workspace when possible, else the absolute path."""
    try:
        rel = os.path.relpath(path, WORKSPACE)
    except ValueError:
        return path
    return rel if not rel.startswith("..") else path


def _fmt3(vec):
    return "(%.4f, %.4f, %.4f)" % (vec[0], vec[1], vec[2])


def _num(value):
    if value is None:
        return "-"
    return "{:,}".format(int(value))


def _capped(items, limit=15):
    items = list(items)
    if len(items) <= limit:
        return items
    return items[:limit] + ["... and %d more" % (len(items) - limit)]


def _letter(check):
    if check.status == "PASS":
        return "P"
    if check.status == "FAIL":
        return "F"
    return "-"


# --------------------------------------------------------------------------- #
# analysis
# --------------------------------------------------------------------------- #

class Check(object):
    __slots__ = ("cid", "title", "status", "text", "reasons", "notes")

    def __init__(self, cid, title):
        self.cid = cid
        self.title = title
        self.status = "NOT RUN"
        self.text = ""
        self.reasons = []
        self.notes = []

    def pass_(self, text, notes=None):
        self.status = "PASS"
        self.text = text
        if notes:
            self.notes.extend(notes)

    def fail(self, text, reasons=None, notes=None):
        self.status = "FAIL"
        self.text = text
        if reasons:
            self.reasons.extend(reasons)
        if notes:
            self.notes.extend(notes)

    def skip(self, text):
        self.status = "NOT RUN"
        self.text = text


class PropFacts(object):
    __slots__ = ("name", "scalar_type", "dim", "raw_len", "values", "parse_error")

    def __init__(self):
        self.name = ""
        self.scalar_type = 0
        self.dim = 0
        self.raw_len = 0
        self.values = None
        self.parse_error = ""


class NodeFacts(object):
    __slots__ = ("index", "name", "parent", "vertex_count", "position",
                 "rotation", "scale", "prop_list", "props", "meshes")

    def __init__(self):
        self.index = 0
        self.name = ""
        self.parent = -1
        self.vertex_count = 0
        self.position = (0.0, 0.0, 0.0)
        self.rotation = (0.0, 0.0, 0.0, 1.0)
        self.scale = (1.0, 1.0, 1.0)
        self.prop_list = []
        self.props = {}
        self.meshes = []


def collect_facts(model):
    """Flatten the parsed protobuf into plain-python node/property/mesh facts."""
    facts = []
    for index, node in enumerate(model.nodes):
        fact = NodeFacts()
        fact.index = index
        fact.name = node.name
        fact.parent = int(node.parent)
        fact.vertex_count = int(node.vertexCount)
        fact.position = (float(node.position.x), float(node.position.y), float(node.position.z))
        fact.rotation = (float(node.rotation.x), float(node.rotation.y),
                         float(node.rotation.z), float(node.rotation.w))
        fact.scale = (float(node.scale.x), float(node.scale.y), float(node.scale.z))
        for vp in node.vertexProperties:
            prop = PropFacts()
            prop.name = vp.name
            prop.scalar_type = int(vp.scalarType)
            prop.dim = int(vp.scalarTypeDimension)
            raw = vp.data
            prop.raw_len = len(raw)
            if prop.dim <= 0:
                prop.parse_error = "scalarTypeDimension=%d is not a positive dimension" % prop.dim
            elif len(raw) % 4 != 0:
                prop.parse_error = "data length %d bytes is not a multiple of 4" % len(raw)
            else:
                try:
                    prop.values = struct.unpack("<%df" % (len(raw) // 4), raw)
                except struct.error as exc:                       # pragma: no cover - defensive
                    prop.parse_error = "struct.error: %s" % exc
            fact.prop_list.append(prop)
            fact.props[prop.name] = prop
        fact.meshes = [(mesh.material, tuple(mesh.indices)) for mesh in node.meshes]
        facts.append(fact)
    return facts


class Analysis(object):
    """Parse + validate one .timbermesh file against the ART-02 contract."""

    def __init__(self, spec, expected=True, patch_map=None):
        self.spec = spec
        self.path = spec.get("path", "")
        self.expected = expected
        self.patch_map = patch_map if patch_map is not None else {"data": None,
                                                                  "source": "-",
                                                                  "error": "not loaded"}
        self.checks = {}
        for cid, title in CHECK_DEFS:
            self.checks[cid] = Check(cid, title)
        self.present = False
        self.model = None
        self.facts = []
        self.raw_size = None
        self.decomp_size = None
        self.bounds = None
        self.tris = None
        self.budget = int(getattr(C, "TRI_BUDGET_LOD0", 6000))
        self.materials = []
        self.slots_present = 0
        self.ports_present = 0
        self.empty_devs = {"port": None}
        self.platform_ok = False
        self.platform_info = "-"
        self.prop_names = set()
        self.root_name = None

    def c(self, cid):
        return self.checks[cid]

    @property
    def verdict(self):
        return "PASS" if all(ch.status == "PASS" for ch in self.checks.values()) else "FAIL"

    # -- driver -------------------------------------------------------------

    def analyze(self):
        if not self._parse():
            self._skip_rest("not evaluated: C1 (parse) failed")
            return
        try:
            self.facts = collect_facts(self.model)
        except Exception as exc:                                  # pragma: no cover - defensive
            self._skip_rest("not evaluated: reading the parsed model failed: %s: %s"
                            % (type(exc).__name__, exc))
            return
        for cid, func in (("C2", self._check_identity),
                          ("C3", self._check_bounds),
                          ("C4", self._check_triangles),
                          ("C5", self._check_materials),
                          ("C6", self._check_empties),
                          ("C7", self._check_properties),
                          ("C8", self._check_topology),
                          ("C9", self._check_uv_containment)):
            try:
                func()
            except Exception:
                self.c(cid).fail("validator internal error while evaluating this check",
                                 [traceback.format_exc(limit=4).strip()])
        for cid, _title in CHECK_DEFS:
            if self.c(cid).status == "NOT RUN":
                self.c(cid).fail("check did not run", [])

    def _skip_rest(self, why):
        for cid, _title in CHECK_DEFS[1:]:
            self.c(cid).skip(why)

    # -- C1 ----------------------------------------------------------------

    def _parse(self):
        check = self.c("C1")
        self.present = os.path.isfile(self.path)
        if not self.present:
            check.fail("file not found", ["path does not exist: %s" % self.path])
            return False
        try:
            with open(self.path, "rb") as handle:
                raw = handle.read()
        except OSError as exc:
            check.fail("cannot read file", ["%s: %s" % (type(exc).__name__, exc)])
            return False
        self.raw_size = len(raw)
        if not raw:
            check.fail("empty file", ["0-byte .timbermesh file"])
            return False
        try:
            data = zlib.decompress(raw)
        except zlib.error as exc:
            check.fail("zlib.decompress failed",
                       ["%s: %s (a .timbermesh must be zlib-compressed protobuf)"
                        % (type(exc).__name__, exc)])
            return False
        self.decomp_size = len(data)
        if not data:
            check.fail("zlib stream decompressed to 0 bytes", [])
            return False
        try:
            model = model_pb2.Model()
            model.ParseFromString(data)
        except Exception as exc:
            check.fail("protobuf ParseFromString failed",
                       ["%s: %s" % (type(exc).__name__, exc)])
            return False
        self.model = model
        check.pass_("zlib ok: %s bytes compressed -> %s bytes protobuf; Model parsed; nodes=%d"
                    % (_num(self.raw_size), _num(self.decomp_size), len(model.nodes)))
        return True

    # -- C2 ----------------------------------------------------------------

    def _check_identity(self):
        check = self.c("C2")
        stem = self.spec["stem"]
        reasons = []
        name = self.model.name
        version = int(self.model.version)
        if name != stem:
            reasons.append("Model.name = %r, expected file stem %r" % (name, stem))
        if version != 0:
            reasons.append("Model.version = %d, expected 0" % version)
        roots = [fact for fact in self.facts if fact.parent == -1]
        if len(roots) == 1:
            self.root_name = roots[0].name
            root_info = "root node name=%r" % roots[0].name
        elif not roots:
            root_info = "no root node (no node with parent == -1)"
        else:
            root_info = "%d root nodes: %s" % (len(roots), [fact.name for fact in roots])
        text = "Model.name=%r version=%d; %s" % (name, version, root_info)
        if reasons:
            check.fail("model identity does not match the file name", reasons)
        else:
            check.pass_(text)

    # -- C3 ----------------------------------------------------------------

    def _check_bounds(self):
        check = self.c("C3")
        target = (C.SIZE_X, C.SIZE_Y, C.SIZE_Z)
        tolerance = C.BOUNDS_TOLERANCE
        lo = [math.inf, math.inf, math.inf]
        hi = [-math.inf, -math.inf, -math.inf]
        vertices = 0
        skipped = []
        non_finite = 0
        for fact in self.facts:
            if fact.vertex_count <= 0:
                continue
            prop = fact.props.get("position")
            if prop is None:
                skipped.append("%s: no 'position' property" % fact.name)
                continue
            if prop.values is None:
                skipped.append("%s: 'position' data unreadable (%s)" % (fact.name, prop.parse_error))
                continue
            if prop.dim != 3 or len(prop.values) % 3:
                skipped.append("%s: 'position' dim=%d with %d values"
                               % (fact.name, prop.dim, len(prop.values)))
                continue
            values = prop.values
            for k in range(0, len(values), 3):
                point = (values[k], values[k + 1], values[k + 2])
                if not all(math.isfinite(v) for v in point):
                    non_finite += 1
                    continue
                for axis in range(3):
                    if point[axis] < lo[axis]:
                        lo[axis] = point[axis]
                    if point[axis] > hi[axis]:
                        hi[axis] = point[axis]
                vertices += 1
        if vertices == 0:
            check.fail("no usable vertex position data",
                       _capped(skipped) or ["the model contains no mesh vertices"])
            return
        size = (hi[0] - lo[0], hi[1] - lo[1], hi[2] - lo[2])
        size_dev = max(abs(size[i] - target[i]) for i in range(3))
        min_dev = max(abs(lo[i]) for i in range(3))
        self.bounds = {"size": size, "min": tuple(lo), "max": tuple(hi),
                       "size_dev": size_dev, "min_dev": min_dev, "vertices": vertices}
        reasons = []
        for axis, label in enumerate("XYZ"):
            if abs(size[axis] - target[axis]) > tolerance:
                reasons.append("size.%s = %.4f, expected %.4f (deviation %.4f > tolerance %.4f)"
                               % (label, size[axis], target[axis],
                                  abs(size[axis] - target[axis]), tolerance))
            if abs(lo[axis]) > tolerance:
                reasons.append("min.%s = %.4f, expected 0.0000 (deviation %.4f > tolerance %.4f)"
                               % (label, lo[axis], abs(lo[axis]), tolerance))
        notes = []
        if skipped:
            notes.append("%d vertex node(s) without usable position data: %s"
                         % (len(skipped), "; ".join(skipped[:3])))
        if non_finite:
            notes.append("%d non-finite position value(s) ignored for bounds (reported by C7)"
                         % non_finite)
        text = ("size=%s min=%s max=%s; size_dev=%.4f min_dev=%.4f "
                "(tolerance %.3f, %d vertices)" % (_fmt3(size), _fmt3(lo), _fmt3(hi),
                                                   size_dev, min_dev, tolerance, vertices))
        if reasons:
            check.fail("mesh bounds do not match the strict 3x3x1 block-corner contract",
                       reasons, notes)
        else:
            check.pass_(text, notes)

    # -- C4 ----------------------------------------------------------------

    def _check_triangles(self):
        check = self.c("C4")
        budget = self.budget
        triangles = 0
        index_total = 0
        not_multiple_of_three = []
        per_node = []
        for fact in self.facts:
            node_triangles = 0
            for material, indices in fact.meshes:
                count = len(indices)
                index_total += count
                node_triangles += count // 3
                if count % 3:
                    not_multiple_of_three.append(
                        "node %r mesh %r: %d indices is not a multiple of 3"
                        % (fact.name, material, count))
            triangles += node_triangles
            if fact.meshes:
                per_node.append("%s=%d" % (fact.name, node_triangles))
        self.tris = triangles
        reasons = []
        if triangles > budget:
            reasons.append("%d triangles > budget %d (over by %d, %.1f%%)"
                           % (triangles, budget, triangles - budget,
                              100.0 * (triangles - budget) / budget))
        reasons.extend(not_multiple_of_three)
        text = ("%d triangles (budget %d, %+d spare); total index count %s"
                % (triangles, budget, budget - triangles, _num(index_total)))
        notes = []
        if per_node:
            joined = ", ".join(per_node[:8]) + ("..." if len(per_node) > 8 else "")
            notes.append("per-node triangles: %s" % joined)
        if reasons:
            check.fail("triangle budget / index topology violated", _capped(reasons), notes)
        else:
            check.pass_(text, notes)

    # -- C5 ----------------------------------------------------------------

    def _check_materials(self):
        check = self.c("C5")
        faction = self.spec.get("faction")
        materials = []
        mesh_count = 0
        for fact in self.facts:
            for material, _indices in fact.meshes:
                mesh_count += 1
                if material not in materials:
                    materials.append(material)
        self.materials = materials
        reasons = []
        notes = []
        if faction in C.MATERIAL_WHITELIST:
            whitelist = set(C.MATERIAL_WHITELIST[faction])
            whitelist_source = "%s whitelist" % faction
        elif not self.expected:
            whitelist = set()
            for names in C.MATERIAL_WHITELIST.values():
                whitelist.update(names)
            whitelist_source = "union of faction whitelists (probe: faction undetermined)"
        else:
            whitelist = set()
            whitelist_source = "n/a"
            reasons.append("no material whitelist for faction %r in "
                           "timbermesh_common.MATERIAL_WHITELIST" % (faction,))
        unknown = [material for material in materials if material not in whitelist]
        if unknown:
            reasons.append("%d material(s) outside the %s: %s"
                           % (len(unknown), whitelist_source, unknown))
        if not 1 <= len(materials) <= 4:
            reasons.append("%d distinct material(s), expected between 1 and 4"
                           % len(materials))
        text = ("%d distinct material(s) across %d mesh(es): %s; whitelist=%s"
                % (len(materials), mesh_count, materials, whitelist_source))
        tier = self.spec.get("tier")
        construction = bool(self.spec.get("construction"))
        expected_map = None
        if tier and faction:
            expected_map = C.MATERIAL_MAP.get((tier, faction, construction))
        if expected_map:
            expected = sorted(set(expected_map.values()))
            extra = sorted(set(materials) - set(expected))
            missing = sorted(set(expected) - set(materials))
            notes.append("INFO (not scored): MATERIAL_MAP[(%r, %r, construction=%s)] expects %s; "
                         "extra=%s missing=%s"
                         % (tier, faction, construction, expected, extra, missing))
        else:
            notes.append("INFO (not scored): no MATERIAL_MAP entry for tier=%r faction=%r "
                         "construction=%s" % (tier, faction, construction))
        if reasons:
            check.fail("material whitelist / count violated", reasons, notes)
        else:
            check.pass_(text, notes)

    # -- C6 ----------------------------------------------------------------

    def _check_empties(self):
        check = self.c("C6")
        tolerance = C.POSITION_TOLERANCE
        name_map = {}
        for fact in self.facts:
            name_map.setdefault(fact.name, []).append(fact)
        port_expect = list(C.port_export_positions().items())
        if len(port_expect) != 3:
            check.fail("contract: expected 3 ports, timbermesh_common defines %d"
                       % len(port_expect))
            return
        reasons = []
        notes = []
        present = 0
        max_dev = None
        expected_names = set(dict(port_expect))
        for name, expected_pos in port_expect:
            nodes = name_map.get(name, [])
            if not nodes:
                reasons.append("%s: missing empty node" % name)
                continue
            if len(nodes) > 1:
                reasons.append("%s: %d nodes with this name, expected exactly 1"
                               % (name, len(nodes)))
                continue
            present += 1
            fact = nodes[0]
            if fact.vertex_count != 0:
                reasons.append("%s: vertexCount = %d, expected 0"
                               % (name, fact.vertex_count))
            if fact.meshes:
                reasons.append("%s: %d mesh(es) attached, expected none"
                               % (name, len(fact.meshes)))
            if all(math.isfinite(v) for v in fact.position):
                dev = max(abs(fact.position[axis] - expected_pos[axis]) for axis in range(3))
                if max_dev is None or dev > max_dev:
                    max_dev = dev
                if dev > tolerance:
                    reasons.append("%s: position %s, expected %s (deviation %.4f > tolerance %.4f)"
                                   % (name, _fmt3(fact.position), _fmt3(expected_pos),
                                      dev, tolerance))
                notes.append("%s position=%s dev=%.4f" % (name, _fmt3(fact.position), dev))
            else:
                reasons.append("%s: non-finite node position %s"
                               % (name, _fmt3(fact.position)))
        extra_slots = [fact.name for fact in self.facts if fact.name.startswith("#Slot#")]
        if extra_slots:
            reasons.append("unexpected #Slot# marker node(s): %s" % extra_slots)
        extra_ports = [fact.name for fact in self.facts
                       if fact.name.startswith("#Port#") and fact.name not in expected_names]
        if extra_ports:
            reasons.append("unexpected #Port# marker node(s): %s" % extra_ports)
        # -- roof platform ---------------------------------------------------
        tier = self.spec.get("tier")
        if self.spec.get("construction"):
            tier = "ConstructionStage0"
        platform_ok = False
        platform_info = "-"
        try:
            x0, x1, z0, z1 = C.platform_export_rect(tier)
            top = C.platform_top_export()
            ceiling = C.platform_clear_ceiling()
        except Exception as exc:                                  # pragma: no cover - defensive
            reasons.append("platform contract unavailable for tier=%r (%s)" % (tier, exc))
            x0 = x1 = z0 = z1 = top = ceiling = None
        if x1 is not None:
            face_verts = 0
            above = []
            above_top = []
            top_x = [math.inf, -math.inf]
            top_z = [math.inf, -math.inf]
            apex = -math.inf
            near_top = []
            for fact in self.facts:
                prop = fact.props.get("position")
                if prop is None or prop.values is None:
                    continue
                values = prop.values
                for k in range(0, len(values) - 2, 3):
                    x, y, z = values[k], values[k + 1], values[k + 2]
                    if not (math.isfinite(x) and math.isfinite(y) and math.isfinite(z)):
                        continue
                    apex = max(apex, y)
                    if y > top + 1e-6:
                        above_top.append((round(x, 3), round(y, 3), round(z, 3)))
                    in_plan = (x0 - 0.02 <= x <= x1 + 0.02
                               and z0 - 0.02 <= z <= z1 + 0.02)
                    if in_plan:
                        if abs(y - top) <= 0.01:
                            face_verts += 1
                            top_x[0] = min(top_x[0], x)
                            top_x[1] = max(top_x[1], x)
                            top_z[0] = min(top_z[0], z)
                            top_z[1] = max(top_z[1], z)
                        if y > top - C.PLATFORM_CURB_H - 1e-6:
                            # everything in the top <=0.04 band must be the flat
                            # platform / its flush fascia, i.e. inside the plan
                            near_top.append((round(x, 3), round(y, 3), round(z, 3)))
                        if y > ceiling + 1e-6:
                            above.append((round(x, 3), round(y, 3), round(z, 3)))
            platform_ok = (face_verts > 0 and not above and not above_top
                           and abs(apex - top) <= 0.005
                           and (top_x[1] - top_x[0]) >= (x1 - x0) - 0.10
                           and (top_z[1] - top_z[0]) >= (z1 - z0) - 0.10)
            platform_info = ("face=%d above=0 apex=%.3f top=%.3f ext=%.2fx%.2f"
                             % (face_verts, top, top, top_x[1] - top_x[0],
                                top_z[1] - top_z[0]))
            if face_verts == 0:
                reasons.append("roof platform: no vertex at the walking surface %.3f "
                               "inside the platform plan (%s)" % (top, platform_info))
            if above_top:
                reasons.append("roof platform: %d vertex/vertices above the walking "
                               "surface %.3f (the top face must be the model apex; "
                               "e.g. %s)" % (len(above_top), top, above_top[:5]))
            if above:
                reasons.append("roof platform: %d vertex/vertices above the 30 cm "
                               "clearance ceiling %.3f (e.g. %s)"
                               % (len(above), ceiling, above[:5]))
            if abs(apex - top) > 0.005:
                reasons.append("roof platform: model apex y=%.4f != walking surface "
                               "%.3f (the flat top must be the highest face)"
                               % (apex, top))
            if (top_x[1] - top_x[0]) < (x1 - x0) - 0.10 or \
                    (top_z[1] - top_z[0]) < (z1 - z0) - 0.10:
                reasons.append("roof platform: flat top face does not span the "
                               "near-full plan (ext=%.2fx%.2f, plan=%.2fx%.2f)"
                               % (top_x[1] - top_x[0], top_z[1] - top_z[0],
                                  x1 - x0, z1 - z0))
            notes.append("flat roof: apex=%.4f top=%.4f above_top=%d above_clearance=%d "
                         "near_top_band_vertices=%d" % (apex, top, len(above_top),
                                                        len(above), len(near_top)))
        self.slots_present = 0
        self.ports_present = present
        self.empty_devs = {"port": max_dev}
        self.platform_ok = platform_ok
        self.platform_info = platform_info
        dev_txt = "n/a" if max_dev is None else "%.4f" % max_dev
        text = ("ports %d/3 present exactly once (vertexCount=0, no meshes, max position dev=%s, "
                "tolerance %.3f); no #Slot# nodes; empty roof platform: %s"
                % (present, dev_txt, tolerance, platform_info))
        if reasons:
            check.fail("marker/platform contract violated", _capped(reasons), notes)
        else:
            check.pass_(text, notes)

    # -- C7 ----------------------------------------------------------------

    def _check_properties(self):
        check = self.c("C7")
        allowed = set(ALLOWED_PROPERTY_NAMES)
        reasons = []
        notes = []
        names_all = set()
        vertex_nodes = 0
        non_finite = 0
        non_finite_samples = []
        bytes_checked = 0
        for fact in self.facts:
            names_all.update(prop.name for prop in fact.prop_list)
            if fact.vertex_count < 0:
                reasons.append("node %r: vertexCount = %d (negative)" % (fact.name, fact.vertex_count))
            if fact.vertex_count > 0:
                vertex_nodes += 1
                missing = [name for name in REQUIRED_PROPERTY_NAMES if name not in fact.props]
                if missing:
                    reasons.append("node %r (vertexCount=%d): missing required propert%s %s"
                                   % (fact.name, fact.vertex_count,
                                      "y" if len(missing) == 1 else "ies", missing))
            for prop in fact.prop_list:
                if prop.name not in allowed:
                    reasons.append("node %r: property name %r is not in the allowed set %s"
                                   % (fact.name, prop.name, sorted(allowed)))
                if prop.scalar_type != SCALAR_TYPE_FLOAT:
                    reasons.append("node %r property %r: scalarType=%d, expected "
                                   "SCALAR_TYPE_FLOAT(4)" % (fact.name, prop.name, prop.scalar_type))
                expected_dim = PROPERTY_DIMS.get(prop.name)
                if expected_dim is not None and prop.dim != expected_dim:
                    reasons.append("node %r property %r: scalarTypeDimension=%d, expected %d"
                                   % (fact.name, prop.name, prop.dim, expected_dim))
                if prop.values is None:
                    reasons.append("node %r property %r: %s"
                                   % (fact.name, prop.name, prop.parse_error))
                    continue
                expected_len = fact.vertex_count * prop.dim * 4
                if prop.raw_len != expected_len:
                    reasons.append("node %r property %r: data length %d bytes, expected "
                                   "vertexCount*dim*4 = %d"
                                   % (fact.name, prop.name, prop.raw_len, expected_len))
                bytes_checked += prop.raw_len
                for value in prop.values:
                    if not math.isfinite(value):
                        non_finite += 1
                        if len(non_finite_samples) < 5:
                            non_finite_samples.append((fact.name, prop.name, value))
        if non_finite:
            reasons.append("%d NaN/Inf value(s) in vertex property data; first: %s"
                           % (non_finite, non_finite_samples))
        self.prop_names = names_all
        if len(self.facts) <= 8:
            for fact in self.facts:
                notes.append("node %r vertexCount=%d properties=%s meshes=%d"
                             % (fact.name, fact.vertex_count,
                                [(prop.name, prop.dim) for prop in fact.prop_list],
                                len(fact.meshes)))
        else:
            notes.append("%d nodes; property names in use: %s"
                         % (len(self.facts), sorted(names_all)))
        text = ("property names %s; position/normal/tangent present on %d vertex node(s); "
                "%d NaN/Inf value(s) in %s bytes of float32 data; scalarType/dimension/length "
                "checked" % (sorted(names_all), vertex_nodes, non_finite, _num(bytes_checked)))
        if reasons:
            check.fail("vertex property data violates the schema contract", _capped(reasons, 20), notes)
        else:
            check.pass_(text, notes)

    # -- C8 ----------------------------------------------------------------

    def _check_topology(self):
        check = self.c("C8")
        stem = self.spec["stem"]
        reasons = []
        bad_indices = 0
        first_bad_index = None
        parent_errors = []
        roots = []
        index_total = 0
        uv_count = 0
        uv_bad = 0
        uv_min = [math.inf, math.inf]
        uv_max = [-math.inf, -math.inf]
        for index, fact in enumerate(self.facts):
            if fact.parent == -1:
                roots.append(fact)
            elif not 0 <= fact.parent < index:
                parent_errors.append("node %d %r: parent=%d, must be -1 or < %d"
                                     % (index, fact.name, fact.parent, index))
            for material, indices in fact.meshes:
                index_total += len(indices)
                for vertex_index in indices:
                    if vertex_index < 0 or vertex_index >= fact.vertex_count:
                        bad_indices += 1
                        if first_bad_index is None:
                            first_bad_index = ("node %r mesh %r: index %d outside [0, %d)"
                                               % (fact.name, material, vertex_index,
                                                  fact.vertex_count))
            prop = fact.props.get("uv0")
            if prop is None or prop.values is None or prop.dim != 2:
                continue
            values = prop.values
            for k in range(0, len(values) - 1, 2):
                u, v = values[k], values[k + 1]
                if not (math.isfinite(u) and math.isfinite(v)):
                    continue
                uv_count += 1
                if u < uv_min[0]:
                    uv_min[0] = u
                if u > uv_max[0]:
                    uv_max[0] = u
                if v < uv_min[1]:
                    uv_min[1] = v
                if v > uv_max[1]:
                    uv_max[1] = v
                if u < -UV_TOLERANCE or u > 1.0 + UV_TOLERANCE:
                    uv_bad += 1
                if v < -UV_TOLERANCE or v > 1.0 + UV_TOLERANCE:
                    uv_bad += 1
        if bad_indices:
            reasons.append("%d mesh index/indices out of range; first: %s"
                           % (bad_indices, first_bad_index))
        reasons.extend(parent_errors[:10])
        if len(roots) != 1:
            reasons.append("expected exactly 1 root node (parent == -1), found %d: %s"
                           % (len(roots), [fact.name for fact in roots]))
        else:
            self.root_name = roots[0].name
            if roots[0].name != stem:
                reasons.append("root node name %r, expected file stem %r"
                               % (roots[0].name, stem))
        if uv_bad:
            reasons.append("%d uv0 component(s) outside [0, 1] +-%.3f"
                           % (uv_bad, UV_TOLERANCE))
        if uv_count:
            text = ("all %s indices in [0, vertexCount); parent links valid; exactly 1 root "
                    "node (%r); uv0 range u[%.4f, %.4f] v[%.4f, %.4f] over %d values "
                    "(tolerance %.3f)"
                    % (_num(index_total), self.root_name, uv_min[0], uv_max[0],
                       uv_min[1], uv_max[1], uv_count, UV_TOLERANCE))
        else:
            text = ("all %s indices in [0, vertexCount); parent links valid; exactly 1 root "
                    "node (%r); no uv0 property present (not required)"
                    % (_num(index_total), self.root_name))
        notes = ["total index count: %s; uv0 values checked: %s"
                 % (_num(index_total), _num(uv_count))]
        if reasons:
            check.fail("topology / uv0 contract violated", _capped(reasons), notes)
        else:
            check.pass_(text, notes)

    # -- C9 ----------------------------------------------------------------

    def _check_uv_containment(self):
        """Mode-aware ART-07 uv0 bounds: every face inside its own canvas."""
        check = self.c("C9")
        map_data = self.patch_map.get("data")
        if map_data is None:
            check.fail("material texture map unavailable",
                       ["cannot test the ART-07 uv0 canvas containment: %s"
                        % (self.patch_map.get("error") or "art/material_texture_map.json "
                           "not loaded")],
                       ["C9 needs the ART-07 material_texture_map.json written by "
                        "the Lead's material analysis; it is absent or unreadable"])
            return
        uv_samples = {}
        nodes_with_uv = 0
        for fact in self.facts:
            prop = fact.props.get("uv0")
            if prop is None or prop.values is None or prop.dim != 2:
                continue
            values = prop.values
            nodes_with_uv += 1
            for material, indices in fact.meshes:
                samples = uv_samples.setdefault(material, [])
                for vertex_index in indices:
                    if 0 <= vertex_index < fact.vertex_count \
                            and 2 * vertex_index + 1 < len(values):
                        samples.append((fact.name, values[2 * vertex_index],
                                        values[2 * vertex_index + 1]))
        if not uv_samples:
            check.fail("no uv0 data on any material mesh",
                       ["the .timbermesh has no readable uv0 property, so the ART-07 "
                        "canvas containment cannot be verified"])
            return
        rows, reasons = containment_report(uv_samples, map_data)
        notes = []
        for row in rows:
            notes.append(container_row_text(row))
        bad_rows = [row for row in rows if row["status"] != "PASS"]
        total_values = sum(row["count"] for row in rows)
        tile_count = len([row for row in rows if row.get("mode") == "tile"])
        text = ("%d material(s) (%d tile / %d patch) / %s uv0 value(s) checked "
                "against their ART-07 canvases (tile: 2-texel guard band; patch: "
                "uv_canvas): %s"
                % (len(rows), tile_count, len(rows) - tile_count,
                   _num(total_values),
                   "all inside" if not bad_rows
                   else "%d material(s) ESCAPE their canvas" % len(bad_rows)))
        if reasons:
            check.fail("uv0 escapes the material's ART-07 canvas",
                       _capped(reasons), notes)
        else:
            check.pass_(text, notes)


# --------------------------------------------------------------------------- #
# specs
# --------------------------------------------------------------------------- #

def make_probe_spec(path):
    """Best-effort spec for an arbitrary .timbermesh (diagnostic mode only)."""
    name = os.path.basename(path)
    stem = name[:-len(".timbermesh")] if name.endswith(".timbermesh") else name
    faction = None
    for candidate in C.FACTIONS:
        if ("." + candidate) in stem:
            faction = candidate
            break
    tier = None
    for candidate in C.TIERS:
        if ("." + candidate + ".") in stem:
            tier = candidate
            break
    return {
        "stem": stem,
        "path": path,
        "tier": tier,
        "faction": faction,
        "construction": "ConstructionStage0" in stem,
        "materials": None,
    }


# --------------------------------------------------------------------------- #
# ART-05 material texture patch helpers (C9-C12 + negative controls)
# --------------------------------------------------------------------------- #
# Everything below is pure Python (no bpy, no numpy requirement): it is shared by
# the .timbermesh validator and mirrored in the Blender source-model validator.

def _is_number(value):
    return (isinstance(value, (int, float)) and not isinstance(value, bool)
            and math.isfinite(float(value)))


def _as_rect4(value, label, reasons):
    """Coerce a JSON [a,b,c,d] number list; append a reason and return None on error."""
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


# -- map loading / texture resolution --------------------------------------- #

def load_patch_map():
    """Load art/material_texture_map.json (via material_patches when it exposes it).

    Returns ``{"data": dict|None, "source": str, "error": str|None, "path": str}``.
    The map accessor is the ART-05 contract in art/material_patches.py
    (``load_material_texture_map()``); the direct JSON read is a fallback so the
    validator keeps working while that module is being rolled over.
    """
    info = {"data": None, "source": "-", "error": None, "path": MAP_PATH}
    loader = getattr(MP, "load_material_texture_map", None) if MP is not None else None
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
    path = getattr(MP, "MAP_PATH", None) if MP is not None else None
    if not isinstance(path, str) or not path:
        path = MAP_PATH
    info["path"] = path
    try:
        with open(path, "r", encoding="utf-8") as handle:
            data = json.load(handle)
    except Exception as exc:  # noqa: BLE001 - report, do not crash
        if info["error"] is None:
            info["error"] = "%s: %s (%s)" % (type(exc).__name__, exc, _rel(path))
        return info
    info["data"] = data
    info["source"] = "json: %s" % _rel(path)
    return info


def texture_roots(map_data):
    """Ordered [(label, root_dir)] candidates: env override, map roots, defaults."""
    candidates = []
    env = (os.environ.get("HDBF_VANILLA_TEX_ROOT") or "").strip()
    if env:
        candidates.append(("HDBF_VANILLA_TEX_ROOT", env))
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
    """Resolve the entry's real vanilla ``_D`` PNG: (path, root_label, tried_paths).

    Resolution order (ART-05 contract): an explicit HDBF_VANILLA_TEX_ROOT
    override directory, then the texture_path recorded in the map (when the file
    still exists), then map.texture_roots (recorded texture_source first), then
    the built-in default roots.
    """
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
    """True when at least one candidate texture root directory exists."""
    for _label, root in texture_roots(map_data):
        if os.path.isdir(root):
            return True
    return False


def textures_reachable(map_data):
    """True when the roots exist or any recorded texture_path still exists."""
    if texture_roots_available(map_data):
        return True
    for _section, _material, entry in all_patch_entries(map_data):
        recorded = entry.get("texture_path")
        if isinstance(recorded, str) and os.path.isfile(recorded):
            return True
    return False


def expected_uv_rect(pixel_rect, texture_size):
    """[x0/W, 1-y1/H, x1/W, 1-y0/H] for a top-left pixel rect (0..1 UV space)."""
    x0, y0, x1, y1 = [float(v) for v in pixel_rect]
    width, height = float(texture_size[0]), float(texture_size[1])
    return [x0 / width, 1.0 - y1 / height, x1 / width, 1.0 - y0 / height]


# -- map queries ------------------------------------------------------------ #

def patch_entry(map_data, material):
    """(entry, section) of a material in the map, or (None, None)."""
    if not isinstance(map_data, dict):
        return None, None
    for section in ("materials", "unused_materials"):
        entries = map_data.get(section)
        if isinstance(entries, dict) and material in entries:
            return entries[material], section
    return None, None


def patch_uv_rect(map_data, material):
    """uv_rect of the material's real-texture patch (map first, then material_patches)."""
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
    accessor = getattr(MP, "uv_rect_for", None) if MP is not None else None
    if callable(accessor):
        try:
            return tuple(float(v) for v in accessor(material))
        except Exception:  # noqa: BLE001 - unknown material / module still old
            return None
    return None


# --------------------------------------------------------------------------- #
# ART-07 (2026-10-27) world-scale UV mapping helpers (C9 upgrade + C15/C16)
# --------------------------------------------------------------------------- #

def material_uv_entry(map_data, material):
    """ART-07 map entry of a vanilla material (``materials``/``unused_materials``)."""
    entry, _section = patch_entry(map_data, material)
    return entry if isinstance(entry, dict) else None


def entry_mode(entry, reasons=None, where="entry"):
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
    size = entry_texture_size(entry) or [1024, 1024]
    guard_u = UV_GUARD_TEXELS / float(size[0])
    guard_v = UV_GUARD_TEXELS / float(size[1])
    return [guard_u, guard_v, 1.0 - guard_u, 1.0 - guard_v]


def containment_bounds_for(map_data, material):
    """(rect, tol, mode, reason) of the mode-aware ART-07 canvas bounds."""
    entry = material_uv_entry(map_data, material)
    if entry is None:
        return (None, PATCH_UV_TOL, None,
                "material %r has no entry in art/material_texture_map.json"
                % (material,))
    mode = entry_mode(entry)
    if mode is None:
        return (None, PATCH_UV_TOL, None,
                "material %r has no ART-07 mode in art/material_texture_map.json"
                % (material,))
    if mode == "tile":
        return tile_guard_uv(entry), TILE_GUARD_TOL, mode, None
    rect = entry_canvas_uv_rect(entry)
    if rect is None:
        return (None, PATCH_UV_TOL, mode,
                "material %r has no uv_canvas.uv_rect in "
                "art/material_texture_map.json" % (material,))
    return rect, PATCH_UV_TOL, mode, None


def containment_report(uv_samples, map_data):
    """(rows, reasons) for the mode-aware uv0 containment (C9).

    ``uv_samples`` maps material -> [(source, u, v)].  A single sample outside the
    material's ART-07 canvas +-tol is a FAIL: tile materials must stay inside the
    frozen 2-texel guard band, patch materials inside their curated canvas.
    """
    rows = []
    reasons = []
    for material in sorted(uv_samples):
        samples = uv_samples[material]
        rect, tol, mode, why = containment_bounds_for(map_data, material)
        row = {"material": material, "rect": tuple(rect) if rect else None,
               "mode": mode, "tol": tol, "count": 0, "min_u": None,
               "max_u": None, "min_v": None, "max_v": None, "bad_count": 0,
               "bad": [], "status": "PASS"}
        if rect is None:
            row["status"] = "FAIL"
            row["bad_count"] = len(samples)
            row["bad"] = [(why or "no ART-07 canvas in the map", None, None)]
            reasons.append(why or ("material %r has no ART-07 canvas" % material))
            rows.append(row)
            continue
        u0, v0, u1, v1 = rect
        min_u = min_v = math.inf
        max_u = max_v = -math.inf
        count = 0
        non_finite = 0
        bad = []
        bad_count = 0
        for source, u, v in samples:
            if not (math.isfinite(u) and math.isfinite(v)):
                non_finite += 1
                continue
            count += 1
            if u < min_u:
                min_u = u
            if u > max_u:
                max_u = u
            if v < min_v:
                min_v = v
            if v > max_v:
                max_v = v
            if (u < u0 - tol or u > u1 + tol
                    or v < v0 - tol or v > v1 + tol):
                bad_count += 1
                if len(bad) < 5:
                    bad.append((source, round(u, 6), round(v, 6)))
        row.update(count=count, min_u=min_u if count else None,
                   max_u=max_u if count else None,
                   min_v=min_v if count else None,
                   max_v=max_v if count else None,
                   bad_count=bad_count, bad=bad)
        if non_finite:
            row["status"] = "FAIL"
            reasons.append("material %r: %d non-finite uv0 value(s)"
                           % (material, non_finite))
        if count == 0:
            row["status"] = "FAIL"
            reasons.append("material %r: no finite uv0 values on its faces"
                           % material)
        elif bad_count:
            row["status"] = "FAIL"
            what = ("2-texel guard band" if mode == "tile"
                    else "ART-07 uv_canvas")
            reasons.append("material %r: %d uv0 value(s) lie outside its %s "
                           "[%.6f, %.6f, %.6f, %.6f] +-%.0e (first: %s)"
                           % (material, bad_count, what, u0, v0, u1, v1, tol,
                              bad[:3]))
        rows.append(row)
    return rows, reasons


def container_row_text(row):
    """One report/console line: per-material PASS/FAIL + uv0 min/max + canvas."""
    rect = row["rect"]
    mode = row.get("mode")
    if rect is None:
        return ("%-4s %-34s no ART-07 canvas in the map (%d uv0 value(s))"
                % (row["status"], row["material"], row["count"]))
    what = "tile guard" if mode == "tile" else "uv_canvas"
    if row["count"]:
        return ("%-4s %-34s mode=%-5s %s uv=[%.6f, %.6f, %.6f, %.6f]  uv0 "
                "u=[%.6f, %.6f] v=[%.6f, %.6f]  values=%d%s"
                % (row["status"], row["material"], mode or "-", what,
                   rect[0], rect[1], rect[2], rect[3], row["min_u"], row["max_u"],
                   row["min_v"], row["max_v"], row["count"],
                   "" if not row["bad_count"]
                   else "  ESCAPED=%d" % row["bad_count"]))
    return ("%-4s %-34s mode=%-5s %s uv=[%.6f, %.6f, %.6f, %.6f]  no finite uv0"
            % (row["status"], row["material"], mode or "-", what, rect[0],
               rect[1], rect[2], rect[3]))


# -- ART-07 world-UV density / mapping contract (C15/C16) ------------------- #

def tile_density_band(map_data):
    block = (map_data or {}).get("uv_mapping") if isinstance(map_data, dict) else None
    value = block.get("texel_density_target_px_per_m") \
        if isinstance(block, dict) else None
    if (isinstance(value, (list, tuple)) and len(value) == 2
            and all(_is_number(v) for v in value)
            and float(value[0]) < float(value[1])):
        return (float(value[0]), float(value[1]))
    return TILE_DENSITY_BAND_DEFAULT


def tile_meters_band(map_data, material, entry):
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


def construction_groups(map_data):
    groups = (map_data or {}).get("construction_shared_patches") \
        if isinstance(map_data, dict) else None
    return groups if isinstance(groups, dict) else {}


def construction_group_for_material(map_data, material):
    """Shared construction-window group of a material (None when not one)."""
    for group_name in sorted(construction_groups(map_data)):
        group = construction_groups(map_data)[group_name]
        if not isinstance(group, dict):
            continue
        if material in (group.get("folktails_material"),
                        group.get("ironteeth_material")):
            return group
    entry = material_uv_entry(map_data, material)
    shared = entry.get("shared_uv_group") if isinstance(entry, dict) else None
    groups = construction_groups(map_data)
    if isinstance(shared, str) and isinstance(groups.get(shared), dict):
        return groups[shared]
    return None


def effective_density_material(map_data, material, construction):
    """Material whose ART-07 density the UVs were baked with (C15/C16).

    The construction stage shares ONE baked UV set (from the Folktails
    construction materials); the IronTeeth export only relabels the slots, so the
    density/exact-UV checks of a construction file must use the Folktails source
    material of the shared window, not the IronTeeth material that samples it.
    """
    if not construction:
        return material
    group = construction_group_for_material(map_data, material)
    if isinstance(group, dict):
        source = group.get("folktails_material")
        if isinstance(source, str) and source:
            return source
    return material


_WORLD_AXIS_INDEX = {"x": 0, "y": 1, "z": 2}
_CANONICAL_UV_AXES = {0: (1, 2), 1: (0, 2), 2: (0, 1)}


def _projection_axes_for(entry, axis_index):
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


def _author_point(values, index):
    """export-space vertex -> Blender author space (frozen inverse transform)."""
    base = 3 * int(index)
    return (float(values[base]) - EXPORT_SIZE_HALF_X,
            EXPORT_DEPTH_HALF_Z - float(values[base + 2]),
            float(values[base + 1]))


def density_model_inputs(results):
    """Per-file author-space geometry + construction flag for the C15 core."""
    inputs = []
    for result in results or []:
        if not getattr(result, "present", False):
            continue
        spec = getattr(result, "spec", None) or {}
        label = spec.get("stem") or "?"
        construction = bool(spec.get("construction"))
        nodes = []
        for fact in getattr(result, "facts", None) or []:
            if fact.vertex_count <= 0:
                continue
            pos = fact.props.get("position")
            uv = fact.props.get("uv0")
            if pos is None or pos.values is None or pos.dim != 3:
                continue
            if uv is None or uv.values is None or uv.dim != 2:
                continue
            verts = [_author_point(pos.values, index)
                     for index in range(fact.vertex_count)]
            nodes.append({"verts": verts, "uv": uv.values,
                          "meshes": list(fact.meshes)})
        if not nodes:
            inputs.append({"label": label, "construction": construction,
                           "error": "no usable position/uv0 vertex data"})
            continue
        inputs.append({"label": label, "construction": construction,
                       "nodes": nodes})
    return inputs


def _density_analyze_inputs(inputs, map_data):
    """Run the C15 core over ``inputs`` (author-space .timbermesh models)."""
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
        construction = bool(model_input.get("construction"))
        for node in model_input.get("nodes") or []:
            verts = node["verts"]
            uv_values = node["uv"]
            for material, indices in node["meshes"]:
                material = str(material)
                effective = effective_density_material(map_data, material,
                                                       construction)
                entry = material_uv_entry(map_data, effective)
                if entry is None:
                    report["missing"].add(effective)
                    report["reasons"].append(
                        "%s: material %r has no entry in "
                        "art/material_texture_map.json" % (label, material))
                    continue
                mode = entry_mode(entry)
                size = entry_texture_size(entry)
                density_expected = entry_px_per_m(entry)
                if mode is None or size is None or density_expected is None:
                    report["reasons"].append(
                        "%s: material %r has no complete ART-07 record in the map"
                        % (label, material))
                    continue
                row = report["rows"].setdefault(str(effective), {
                    "material": str(effective), "mode": mode,
                    "canvas": entry_canvas_pixel_rect(entry),
                    "texture_size": size, "px_expected": density_expected,
                    "uv_area": 0.0, "proj_area": 0.0, "tri_count": 0,
                    "min": math.inf, "max": -math.inf, "large": 0,
                    "large_failed": 0, "exact_checked": 0, "exact_failed": 0,
                    "ambiguous": 0, "models": {}, "reasons": [],
                    "sources": set(),
                })
                row["sources"].add(material)
                center_uv, center_world = _projection_center(entry)
                texel = math.sqrt(float(size[0] * size[1]))
                model_acc = row["models"].setdefault(
                    label, [0.0, 0.0, 0, 0, 0])
                vertex_count = len(verts)
                uv_count = len(uv_values) // 2
                for tri_start in range(0, len(indices) - 2, 3):
                    i0 = int(indices[tri_start])
                    i1 = int(indices[tri_start + 1])
                    i2 = int(indices[tri_start + 2])
                    if not (0 <= i0 < vertex_count and 0 <= i1 < vertex_count
                            and 0 <= i2 < vertex_count):
                        continue
                    if not (0 <= i0 < uv_count and 0 <= i1 < uv_count
                            and 0 <= i2 < uv_count):
                        continue
                    p0, p1, p2 = verts[i0], verts[i1], verts[i2]
                    u0 = float(uv_values[2 * i0])
                    v0 = float(uv_values[2 * i0 + 1])
                    u1 = float(uv_values[2 * i1])
                    v1 = float(uv_values[2 * i1 + 1])
                    u2 = float(uv_values[2 * i2])
                    v2 = float(uv_values[2 * i2 + 1])
                    if not (math.isfinite(u0) and math.isfinite(v0)
                            and math.isfinite(u1) and math.isfinite(v1)
                            and math.isfinite(u2) and math.isfinite(v2)):
                        report["reasons"].append(
                            "%s: NaN/Inf UV0 value(s) on material %r"
                            % (label, material))
                        continue
                    e1 = (p1[0] - p0[0], p1[1] - p0[1], p1[2] - p0[2])
                    e2 = (p2[0] - p0[0], p2[1] - p0[1], p2[2] - p0[2])
                    normal = (e1[1] * e2[2] - e1[2] * e2[1],
                              e1[2] * e2[0] - e1[0] * e2[2],
                              e1[0] * e2[1] - e1[1] * e2[0])
                    length = math.sqrt(normal[0] * normal[0]
                                       + normal[1] * normal[1]
                                       + normal[2] * normal[2])
                    if length < 1e-12:
                        continue
                    absn = [round(abs(component) / length,
                                  PROJECTION_ROUND_DECIMALS)
                            for component in normal]
                    if absn[0] >= absn[1] and absn[0] >= absn[2]:
                        axis = 0
                    elif absn[1] >= absn[2]:
                        axis = 1
                    else:
                        axis = 2
                    ranked = sorted(absn, reverse=True)
                    ambiguous = (ranked[0] - ranked[1]) <= AMBIGUOUS_PLANE_TOL
                    world_area = 0.5 * length
                    proj_area = world_area * absn[axis]
                    if proj_area <= 0.0:
                        continue
                    uv_area = 0.5 * abs((u1 - u0) * (v2 - v0)
                                        - (u2 - u0) * (v1 - v0))
                    tri_density = math.sqrt(uv_area / proj_area) * texel
                    row["uv_area"] += uv_area
                    row["proj_area"] += proj_area
                    row["tri_count"] += 1
                    if tri_density < row["min"]:
                        row["min"] = tri_density
                    if tri_density > row["max"]:
                        row["max"] = tri_density
                    report["triangles"] += 1
                    report["total_area"] += world_area
                    model_stats["triangles"] += 1
                    model_stats["area"] += world_area
                    model_acc[0] += uv_area
                    model_acc[1] += proj_area
                    model_acc[2] += 1
                    if ambiguous:
                        row["ambiguous"] += 1
                        report["ambiguous_count"] += 1
                        report["ambiguous_area"] += world_area
                        model_stats["ambiguous"] += 1
                        model_stats["ambiguous_area"] += world_area
                    # -- per-triangle density (+-25 % above 0.05 m^2) ----------
                    if world_area >= DENSITY_TRI_MIN_WORLD_AREA:
                        row["large"] += 1
                        report["large_triangles"] += 1
                        relative = abs(tri_density - density_expected) \
                            / density_expected
                        if relative > DENSITY_TRI_TOL:
                            row["large_failed"] += 1
                            report["large_failed"] += 1
                            if len(row["reasons"]) < 8:
                                row["reasons"].append(
                                    "%s: triangle %d/%d has a texel density "
                                    "%.2f px/m vs the map %.1f (+-%.0f %% "
                                    "allowed; world area %.4f m^2)"
                                    % (label, row["large_failed"], row["large"],
                                       tri_density, density_expected,
                                       DENSITY_TRI_TOL * 100.0, world_area))
                    # -- exact UV recomputation ------------------------------
                    if ambiguous:
                        continue
                    u_axis, v_axis = _projection_axes_for(entry, axis)
                    report["exact_checked"] += 1
                    row["exact_checked"] += 1
                    model_stats["exact_checked"] += 1
                    delta = 0.0
                    for point, u, v in ((p0, u0, v0), (p1, u1, v1),
                                        (p2, u2, v2)):
                        expected_u = center_uv[0] + (
                            point[u_axis] - center_world[u_axis]
                        ) * density_expected / float(size[0])
                        expected_v = center_uv[1] + (
                            point[v_axis] - center_world[v_axis]
                        ) * density_expected / float(size[1])
                        delta = max(delta, abs(expected_u - u),
                                    abs(expected_v - v))
                    if delta > EXACT_UV_TOL:
                        row["exact_failed"] += 1
                        report["exact_failed"] += 1
                        model_stats["exact_failed"] += 1
                        if len(row["reasons"]) < 8:
                            row["reasons"].append(
                                "%s: exact UV recomputation differs by %.6f "
                                "(tolerance %.0e) on material %r triangle %d"
                                % (label, delta, EXACT_UV_TOL, material,
                                   tri_start // 3))
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
        texel = math.sqrt(float(row["texture_size"][0]
                                * row["texture_size"][1]))
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
                               "below the %.0f px/m regression floor (2x ART-06)"
                               % (material, measured, DENSITY_PATCH_MIN))
        row["status"] = "FAIL" if reasons else "PASS"
        row["sources"] = sorted(row["sources"])
        report["reasons"].extend(reasons)
    if report["triangles"]:
        amb_fraction = report["ambiguous_count"] / float(report["triangles"])
        area_fraction = (report["ambiguous_area"] / report["total_area"]
                         if report["total_area"] > 0.0 else 0.0)
        if amb_fraction > AMBIGUOUS_COUNT_MAX + 1e-12:
            report["reasons"].append(
                "ambiguous-plane triangles are %.2f %% of all triangles "
                "(> %.0f %% allowed)"
                % (100.0 * amb_fraction, 100.0 * AMBIGUOUS_COUNT_MAX))
        if area_fraction > AMBIGUOUS_AREA_MAX + 1e-12:
            report["reasons"].append(
                "ambiguous-plane triangles cover %.3f %% of the world area "
                "(> %.1f %% allowed)"
                % (100.0 * area_fraction, 100.0 * AMBIGUOUS_AREA_MAX))
    report["tile_band"] = band
    report["amb_fraction"] = (report["ambiguous_count"]
                              / float(report["triangles"])
                              if report["triangles"] else 0.0)
    report["amb_area_fraction"] = (
        report["ambiguous_area"] / report["total_area"]
        if report["total_area"] > 0.0 else 0.0)
    return report


def evaluate_world_uv_density(patch_info, results):
    """C15: ART-07 world-UV texel density of the shipped .timbermesh files."""
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
                 "be >= %.0f px/m (2x ART-06; the WindowsAtlas glass pane is 82.4 "
                 "px/m by design)"
                 % (DENSITY_AGG_TOL * 100.0, DENSITY_TRI_TOL * 100.0,
                    DENSITY_TRI_MIN_WORLD_AREA, report["large_failed"],
                    report["large_triangles"], list(band), DENSITY_PATCH_MIN))
    notes.append("exact UV recomputation (frozen projection tie-break; export -> "
                 "author space inverse): %d triangle(s) checked, %d mismatch(es) "
                 "> %.0e; ambiguous-plane triangles %d/%d = %.2f %% of triangles / "
                 "%.3f %% of world area (limits %.0f %% / %.1f %%)"
                 % (report["exact_checked"], report["exact_failed"],
                    EXACT_UV_TOL, report["ambiguous_count"],
                    report["triangles"], 100.0 * report["amb_fraction"],
                    100.0 * report["amb_area_fraction"],
                    100.0 * AMBIGUOUS_COUNT_MAX, 100.0 * AMBIGUOUS_AREA_MAX))
    notes.append("the 8 ConstructionStage0 files share ONE baked Folktails UV set: "
                 "their density/exact UVs are measured against the Folktails source "
                 "material of the shared window (the IronTeeth export samples that "
                 "same window by contract)")
    if reasons:
        check.fail("%d material(s)/file(s) violate the ART-07 texel-density "
                   "contract" % len(report["reasons"]), reasons, notes)
    elif not report["rows"]:
        check.fail("no material could be measured: no shipped .timbermesh file "
                   "is available for the world-UV density check", [], notes)
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


def _contract_reproduce_px(entry):
    """(raw, fit, nominal) texel-density reproduction data of one map entry."""
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
    """C16: ART-07 ``uv_mapping`` contract / determinism over the exported files."""
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
                           "expected %r"
                           % (target, list(TILE_DENSITY_BAND_DEFAULT)))
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
    used = sorted(required_materials(results))
    field_names = ("mode", "tile_meters", "texture_size", "wrap", "uv_canvas",
                   "projection", "px_per_m", "coverage_u_m", "coverage_v_m")
    validated = 0
    for material in used:
        where = "materials[%r]" % material
        entry = material_uv_entry(map_data, material)
        if entry is None:
            reasons.append("%s has no entry in art/material_texture_map.json but "
                           "is used by a shipped/scheduled model" % where)
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
            reasons.append("%s: coverage_u_m/coverage_v_m must be numbers, got "
                           "%r/%r" % (where, entry.get("coverage_u_m"),
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
                        "canvas / coverage (fit %.4f, nominal %.4f -> raw %.4f)"
                        % (where, density_expected, fit, nominal, raw))
                if size is not None:
                    span_u = size[0] / density_expected
                    span_v = size[1] / density_expected
                    if span_u < coverage[0] - 1e-6:
                        reasons.append("%s: effective canvas span %.4f m < "
                                       "required coverage_u_m %.4f m"
                                       % (where, span_u, coverage[0]))
                    if span_v < coverage[1] - 1e-6:
                        reasons.append("%s: effective canvas span %.4f m < "
                                       "required coverage_v_m %.4f m"
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
                    reasons.append("%s: patch.pixel_rect %s != "
                                   "uv_canvas.pixel_rect %s for a patch material"
                                   % (where, _rect_text(prect),
                                      _rect_text(canvas_pixel)))
                if puv is not None:
                    want_uv = expected_uv_rect(prect, size)
                    for index, name in enumerate(("u0", "v0", "u1", "v1")):
                        if abs(puv[index] - want_uv[index]) > UV_RECT_ROUND_TOL:
                            reasons.append("%s: patch.uv_rect[%s] = %.9f, "
                                           "expected %.9f"
                                           % (where, name, puv[index],
                                              want_uv[index]))
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
                    reasons.append("uv_mapping.densities[%r].canvas_pixels = %r "
                                   "!= the entry uv_canvas size %r"
                                   % (material, recorded_pixels, want_pixels))
    # -- construction shared windows --------------------------------------- #
    groups = construction_groups(map_data)
    construction_checked = 0
    if not groups:
        reasons.append("art/material_texture_map.json has no "
                       "'construction_shared_patches' block")
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
                                   "texture" % (where, _rect_text(rect),
                                                size[0], size[1]))
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
                    it_rect, it_tol, _it_mode, _why = containment_bounds_for(
                        map_data, it_material)
                    if it_rect is not None and (
                            uv_rect[0] < it_rect[0] - it_tol
                            or uv_rect[1] < it_rect[1] - it_tol
                            or uv_rect[2] > it_rect[2] + it_tol
                            or uv_rect[3] > it_rect[3] + it_tol):
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
                        reasons.append("%s: per_texture[%r].std_max must be <= "
                                       "the hard cap 30.0, got %r"
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
    # -- cross-file determinism from the measured C15 density -------------- #
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
                        "cross-file drift: %s uses material %r at %.2f px/m "
                        "(%+.2f %%) != the map's single density %.1f px/m"
                        % (label, material, measured,
                           100.0 * (measured - expected) / expected, expected))
    notes.insert(0, "%d used material(s) checked against the sec.4 record; "
                    "construction shared window(s): %d; per-file density uses "
                    "cross-checked: %d (drift failures: %d)"
                    % (validated, construction_checked, drift_checked,
                       drift_failed))
    if reasons:
        check.fail("%d ART-07 UV mapping contract violation(s)" % len(reasons),
                   _capped(reasons, 25), notes)
    else:
        check.pass_("%d used material(s) carry the frozen sec.4 ART-07 record "
                    "(mode/tile_meters/texture_size/wrap/uv_canvas/projection/"
                    "px_per_m/coverage); px_per_m reproduces from the usable "
                    "canvas and coverage; wrap/tile_meters/uv_canvas/patch inside "
                    "the texture; %d construction shared window(s) resolve both "
                    "factions; the same density/canvas is used by every shipped file"
                    % (validated, construction_checked), notes)
    return check


def actual_materials(results):
    """Materials actually present in the parsed shipped .timbermesh files."""
    found = set()
    for result in results or []:
        for material in getattr(result, "materials", None) or []:
            found.add(str(material))
    return found


def required_materials(results):
    """Materials that must have a map entry: shipped models + MATERIAL_MAP targets."""
    required = set()
    for mapping in C.MATERIAL_MAP.values():
        for target in mapping.values():
            required.add(str(target))
    required.update(actual_materials(results))
    return required


def std_max_for(map_data, entry):
    """Patch std threshold: per-patch std_max, else class threshold, else default."""
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
    """Every tag/class string recorded on a patch/entry, lower-cased."""
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
    """True when the entry itself is a hazard/logo/text (unusable) region.

    ``patch.reject_tags`` in the Lead's map lists the hazards every good patch
    intentionally AVOIDS (e.g. "hazard-stripes", "faction-banner/logo", "text"),
    so a reject tag only marks the patch as hazardous when it names the region
    itself ("hazard", "hazard:..."), never for the avoid-list form.
    """
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
    """[(section, material, entry)] for every materials/unused entry carrying a patch."""
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


# -- C10 schema + coverage --------------------------------------------------- #

def entry_reasons(material, entry, require_patch=True):
    """Schema/integrity findings for one material map record (check C10)."""
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
                        "%s.patch.uv_rect[%s] = %.9f, expected [x0/W, 1-y1/H, x1/W, 1-y0/H] "
                        "= %.9f (delta %.2e > rounding tolerance %.1e)"
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
            reasons.append("%s.patch.mean_rgb must be 3 recorded numbers in 0..255, got %r"
                           % (where, measured))
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
    """Structural findings of art/material_texture_map.json (schema v1)."""
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
    """C10: every required material has a valid real-texture patch record."""
    check = Check("C10", "material patch map coverage")
    details = {"required": [], "missing": [], "used": [], "scheduled": [],
               "map_materials": 0, "map_unused": 0, "reference_only": []}
    notes = ["map source: %s" % (source or "-")]
    if map_data is None:
        check.fail("art/material_texture_map.json unavailable",
                   ["cannot verify material patch coverage: %s"
                    % (load_error or "map not loaded")],
                   notes)
        return check, details
    reasons = map_schema_reasons(map_data)
    materials = map_data.get("materials") or {}
    unused = map_data.get("unused_materials") or {}
    required = required_materials(results)
    used = actual_materials(results)
    scheduled = set()
    for mapping in C.MATERIAL_MAP.values():
        for target in mapping.values():
            scheduled.add(str(target))
    details.update(required=sorted(required), used=sorted(used),
                   scheduled=sorted(scheduled), map_materials=len(materials),
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
                 "%d required material name(s): %d used by shipped models, %d "
                 "MATERIAL_MAP target(s)"
                 % (map_data.get("schema"), len(materials), len(unused),
                    len(required), len(used), len(scheduled)))
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


# -- C11 real-texture variance re-verification ------------------------------- #

def evaluate_patch_entry(material, entry, map_data, decoded, texture_path=None):
    """(row, findings): re-measure one patch from the decoded real PNG (C11 core)."""
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
    uv = patch.get("uv_rect") if isinstance(patch, dict) else None
    row = {
        "material": material,
        "texture": os.path.basename(str(entry.get("texture", ""))) if entry else "",
        "texture_path": texture_path,
        "pixel_rect": rect,
        "size_px": [rect[2] - rect[0], rect[3] - rect[1]],
        "uv_rect": [round(float(v), 6) for v in uv] if isinstance(uv, (list, tuple))
                   and len(uv) == 4 else None,
        "mean_measured": computed_mean,
        "mean_recorded": [round(float(v), 4) for v in recorded_mean]
                         if isinstance(recorded_mean, (list, tuple)) and len(recorded_mean) == 3
                         else None,
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
    """One evidence line: recorded vs re-measured mean/std + std_max + verdict."""
    return ("%-4s %-34s %-30s px=%-20s uv=%-38s mean=%s std=%.3f std_max=%.2f"
            % (row["status"], row["material"], row["texture"],
               _rect_text(row["pixel_rect"]),
               _uv_text(row["uv_rect"]) if row["uv_rect"] else "-",
               _mean_text(row["mean_measured"]), row["std_measured"],
               row["std_max"]))


def evaluate_variance(patch_info, results):
    """C11: decode every real _D PNG and compare the recorded patch stats."""
    check = Check("C11", "real _D texture variance re-verification")
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
        if not isinstance(entry, dict):
            continue
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
                               % (section, material, _rel(path),
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
    if cache:
        notes.insert(0, "decoded %d unique vanilla _D PNG file(s) with art/png_stats.py "
                        "(decoder: %s); mean/std recomputed from the raw sRGB bytes"
                        % (len(cache), ", ".join(sorted(decoders))))
    else:
        notes.insert(0, "no vanilla _D PNG could be decoded")
    if roots_available:
        notes.append("texture root resolution: env HDBF_VANILLA_TEX_ROOT > "
                     "map.texture_roots > built-in default roots")
    if failed:
        check.fail("%d/%d patch record(s) do not match the real vanilla _D texture"
                   % (failed, len(entries)), _capped(reasons, 30), notes)
    elif not rows:
        check.skip("no patch record could be re-verified: %s" % (
            "; ".join("%s (%s)" % (m, why) for m, why in skipped[:4])
            or "no texture root available on this machine"))
    elif skipped_count:
        check.pass_("%d/%d patch record(s) re-verified from the real vanilla _D PNGs; "
                    "%d SKIPPED because no texture root is available on this machine "
                    "(not counted as PASS)" % (len(rows), len(entries), skipped_count),
                    notes)
    else:
        check.pass_("all %d patch record(s) re-verified from the real vanilla _D PNGs: "
                    "recorded mean RGB / std match the decoded pixels (mean +-%.1f, "
                    "std +-%.1f) and std <= std_max"
                    % (len(rows), PATCH_MEAN_TOL, PATCH_STD_TOL), notes)
    return check, rows, skipped


# -- C12 hazard / large-shell policy ----------------------------------------- #

def collect_exclusion_rects(map_data):
    """Recorded hazard/logo/text exclusion rects from any map location we support."""
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
            exclusions.append({
                "texture": os.path.basename(texture),
                "pixel_rect": [float(v) for v in rect],
                "reason": str(reason),
                "where": where,
            })

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
        # ART-05 reference-only hazard windows (used by the Lead's
        # `unused_materials` records): a recorded hazard region of a texture is
        # off limits for every usable patch of the same texture.
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
            exclusions.append({
                "texture": os.path.basename(texture),
                "pixel_rect": [float(v) for v in rect],
                "reason": str(reason),
                "where": "%s.%s" % (prefix, key),
            })

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
            texture = entry.get("texture")
            scan(entry, "unused_materials[%r]" % material, texture)
            # an unused entry that itself carries a hazard/reference patch is a
            # recorded exclusion region too (do not complain that it overlaps
            # itself; it is never usable).
            patch = entry.get("patch")
            if isinstance(patch, dict) and is_hazard_marked(entry):
                rect = patch.get("pixel_rect")
                if isinstance(rect, (list, tuple)) and len(rect) == 4 \
                        and all(_is_number(v) for v in rect) \
                        and isinstance(texture, str) and texture:
                    exclusions.append({
                        "texture": os.path.basename(texture),
                        "pixel_rect": [float(v) for v in rect],
                        "reason": "map hazard/reference patch of %r" % material,
                        "where": "unused_materials[%r]" % material,
                    })
    return exclusions


def rects_overlap(rect_a, rect_b):
    ax0, ay0, ax1, ay1 = [float(v) for v in rect_a]
    bx0, by0, bx1, by1 = [float(v) for v in rect_b]
    return ax0 < bx1 and bx0 < ax1 and ay0 < by1 and by0 < ay1


def evaluate_hazard(patch_info, results):
    """C12: no forbidden/hazard material may be used; exclusion rects respected."""
    check = Check("C12", "hazard / large-shell material policy")
    map_data = patch_info.get("data")
    reasons = []
    notes = []
    if map_data is None:
        check.fail("material texture map unavailable: hazard/large-shell policy "
                   "cannot be verified",
                   ["cannot verify large_shell_policy: %s"
                    % (patch_info.get("error") or "map not loaded")], notes)
        return check
    used = actual_materials(results)
    scheduled = set()
    for mapping in C.MATERIAL_MAP.values():
        for target in mapping.values():
            scheduled.add(str(target))
    required = used | scheduled
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
                                   "list the yellow-black hazard material %r (list: %r)"
                                   % (material, forbidden))
        notes.append("forbidden large-shell/hazard materials declared: %s"
                     % (forbidden or "-"))
    hits = sorted(set(forbidden) & required)
    if hits:
        origins = []
        if set(hits) & used:
            origins.append("shipped .timbermesh")
        if set(hits) & scheduled:
            origins.append("timbermesh_common.MATERIAL_MAP")
        reasons.append("%d forbidden hazard material(s) appear on %s: %s"
                       % (len(hits), " + ".join(origins), hits))
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
                               "(tags=%r) yet the material is used by a shipped or "
                               "scheduled model" % (section, material, tags))
            else:
                notes.append("%s[%r] is marked hazard/reference-only (tags=%r) and is "
                             "not used -> ok" % (section, material, tags))
    exclusions = collect_exclusion_rects(map_data)
    if exclusions:
        notes.append("%d recorded hazard/logo/text exclusion rect(s) checked against "
                     "the usable patches" % len(exclusions))
        overlapping = 0
        for section, material, entry in all_patch_entries(map_data):
            if is_hazard_marked(entry):
                continue  # an explicitly hazard-marked record is not usable by design
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
    notes.append("used by shipped .timbermesh: %s" % (sorted(used) or "-"))
    if reasons:
        check.fail("hazard / large-shell material policy violated",
                   _capped(reasons, 30), notes)
    else:
        check.pass_("%d forbidden yellow-black hazard material(s) declared and unused; "
                    "no hazard-tagged patch is shipped; %d exclusion rect(s) respected"
                    % (len(HAZARD_MATERIALS), len(exclusions)), notes)
    return check


class GlobalChecks(object):
    """The cross-file ART-05 checks (C10-C12) + ART-07 (C15/C16) + report data."""

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
    """Run C10/C11/C12 + the ART-07 C15/C16 checks over the shipped export."""
    globals_ = GlobalChecks()
    globals_.map_source = patch_info.get("source") or "-"
    globals_.map_error = patch_info.get("error")
    globals_.coverage, globals_.coverage_details = evaluate_coverage(
        patch_info.get("data"), patch_info.get("source"), results,
        patch_info.get("error"))
    globals_.variance, globals_.variance_rows, globals_.variance_skipped = \
        evaluate_variance(patch_info, results)
    globals_.hazard = evaluate_hazard(patch_info, results)
    globals_.density, globals_.density_report = evaluate_world_uv_density(
        patch_info, results)
    globals_.uv_contract = evaluate_uv_contract(patch_info, results,
                                                globals_.density_report)
    return globals_


# --------------------------------------------------------------------------- #
# negative controls (requirement D: each control must be rejected)
# --------------------------------------------------------------------------- #

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


def _first_shipped_uv(results):
    """First (source, material, u, v) uv0 sample from a parsed shipped model."""
    for result in results or []:
        if not getattr(result, "present", False):
            continue
        source = "shipped:%s" % result.spec.get("stem", "?")
        for fact in getattr(result, "facts", None) or []:
            prop = fact.props.get("uv0")
            if prop is None or prop.values is None or prop.dim != 2:
                continue
            values = prop.values
            for material, indices in fact.meshes:
                for vertex_index in indices:
                    if 0 <= vertex_index < fact.vertex_count \
                            and 2 * vertex_index + 1 < len(values):
                        u = values[2 * vertex_index]
                        v = values[2 * vertex_index + 1]
                        if math.isfinite(u) and math.isfinite(v):
                            return source, material, u, v
    return None


def _rect_around(u, v, radius):
    """Small synthetic patch around a sample point, clipped to [0,1]."""
    radius = max(0.02, float(radius))
    u0 = max(0.0, min(u - radius, 1.0 - 2.0 * radius))
    v0 = max(0.0, min(v - radius, 1.0 - 2.0 * radius))
    return [round(u0, 6), round(v0, 6), round(u0 + 2.0 * radius, 6),
            round(v0 + 2.0 * radius, 6)]


def _synthetic_entry_for_uv(material, patch_rect):
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
    """Deep-copied map (or a minimal one) with a synthetic entry for material."""
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
    materials[material] = _synthetic_entry_for_uv(material, patch_rect)
    return copy


def control_uv_escape(patch_info, results):
    """Negative control 1: shift one shipped uv0 outside its ART-07 canvas -> C9."""
    control = NegativeControl(1, "one shipped uv0 shifted outside its ART-07 canvas "
                                "(containment)")
    control.check_id = "C9"
    map_data = patch_info.get("data")
    sample = _first_shipped_uv(results)
    if sample is not None:
        source, material, u, v = sample
        real_uv = True
    else:
        source, material, u, v = ("synthetic fixture (no shipped model)",
                                  "Fixture.Wood", 0.30, 0.40)
        real_uv = False
    rect, tol, mode, _why = containment_bounds_for(map_data, material) \
        if map_data is not None else (None, PATCH_UV_TOL, None, None)
    inside = (rect is not None
              and rect[0] - tol <= u <= rect[2] + tol
              and rect[1] - tol <= v <= rect[3] + tol)
    if inside:
        probe_map = map_data
    else:
        rect = _rect_around(u, v, 0.05)
        probe_map = _map_with_synthetic_patch(map_data, material, rect)
        mode = "patch"
    rect = tuple(float(value) for value in rect)
    width = rect[2] - rect[0]
    bad_u = rect[2] + width + 0.05
    bad_v = v
    samples = {material: [(source, bad_u, bad_v)]}
    rows, reasons = containment_report(samples, probe_map)
    rejected = bool(reasons)
    control.status = "REJECTED" if rejected else "NOT REJECTED"
    control.detail = ("source=%s material=%r mode=%r uv0=(%.6f, %.6f) -> "
                      "(%.6f, %.6f): moved %.4f in u (> one canvas width %.4f) "
                      "outside the ART-07 canvas uv=[%.6f, %.6f, %.6f, %.6f]; "
                      "containment checker %s (%s)"
                      % (source, material, mode, u, v, bad_u, bad_v, bad_u - u,
                         width, rect[0], rect[1], rect[2], rect[3],
                         "REJECTED" if rejected else "ACCEPTED",
                         rows[0]["status"] if rows else "no row"))
    if rejected:
        first = reasons[0]
        control.note = "rejected by C9: %s" % first
    else:
        control.note = "BUG: the containment checker accepted an escaped uv0"
    if not real_uv:
        control.note += " [no shipped model UV available; synthetic fixture used]"
    return control


def _procedural_hazard_rgb(size):
    """Yellow/black vertical stripe raster (fallback when no texture root exists)."""
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
    """Negative control 2: a hazard-stripe patch record -> C11 must FAIL."""
    control = NegativeControl(2, "synthetic 192x192 yellow-black hazard-stripe "
                                "patch record (texture variance/hazard)")
    control.check_id = "C11"
    if PS is None:
        control.status = "NOT RUN"
        control.detail = ("art/png_stats.py unavailable (%s): cannot re-measure the "
                          "hazard window, so the variance checker cannot be exercised"
                          % PS_ERR)
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
    # Record exactly what was measured, so ONLY the std_max/variance rule can
    # reject the record (the negative control must exercise the variance check).
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
    control.detail = ("mode=%s; patch px=%s uv=%s; recorded std=%.3f; "
                      "std_max=%.3f -> %s"
                      % (mode, _rect_text(entry["patch"]["pixel_rect"]),
                         _uv_text(entry["patch"]["uv_rect"]), entry["patch"]["std"],
                         row["std_max"],
                         "REJECTED" if rejected else "ACCEPTED"))
    if rejected:
        control.note = "rejected by C11: %s" % "; ".join(findings)
    else:
        control.note = "BUG: the variance checker accepted a yellow-black hazard patch"
    return control


def control_missing_material(patch_info, results):
    """Negative control 3: drop one required material from a map copy -> C10 FAIL."""
    control = NegativeControl(3, "one required material removed from an in-memory "
                                "map copy (coverage)")
    control.check_id = "C10"
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
    control.detail = ("mode=%s; removed required material %r; coverage checker "
                      "-> %s" % (mode, material, check.status))
    if rejected:
        control.note = "rejected by C10: %s" % next(
            reason for reason in reasons if material in reason)
    else:
        control.note = "BUG: the coverage checker accepted a map without %r" % material
    return control


def _density_probe_input(results, map_data):
    """(model input, actual material, effective material) of a tile material."""
    for model_input in density_model_inputs(results):
        if model_input.get("error"):
            continue
        construction = bool(model_input.get("construction"))
        for node in model_input["nodes"]:
            for material, indices in node["meshes"]:
                material = str(material)
                effective = effective_density_material(map_data, material,
                                                       construction)
                entry = material_uv_entry(map_data, effective)
                if entry_mode(entry) != "tile":
                    continue
                if not indices:
                    continue
                return model_input, material, effective
    return None, None, None


def _density_fixture_input(map_data):
    """Synthetic 3x3x1 author-space box with exact ART-07 UVs (control fallback)."""
    if not isinstance(map_data, dict):
        return None, None
    material = None
    for candidate in sorted(map_data.get("materials") or {}):
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
    verts = [(-1.5, -1.5, 0.0), (1.5, -1.5, 0.0), (1.5, 1.5, 0.0),
             (-1.5, 1.5, 0.0), (-1.5, -1.5, 1.0), (1.5, -1.5, 1.0),
             (1.5, 1.5, 1.0), (-1.5, 1.5, 1.0)]
    quads = [(0, 1, 2, 3), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5),
             (2, 3, 7, 6), (3, 0, 4, 7)]
    tris = []
    for quad in quads:
        tris.append((quad[0], quad[1], quad[2]))
        tris.append((quad[0], quad[2], quad[3]))
    uv = [0.0] * (2 * len(verts))
    for i0, i1, i2 in tris:
        p0, p1, p2 = verts[i0], verts[i1], verts[i2]
        e1 = (p1[0] - p0[0], p1[1] - p0[1], p1[2] - p0[2])
        e2 = (p2[0] - p0[0], p2[1] - p0[1], p2[2] - p0[2])
        normal = (e1[1] * e2[2] - e1[2] * e2[1],
                  e1[2] * e2[0] - e1[0] * e2[2],
                  e1[0] * e2[1] - e1[1] * e2[0])
        length = math.sqrt(sum(component * component for component in normal))
        if length < 1e-12:
            continue
        absn = [round(abs(component) / length, PROJECTION_ROUND_DECIMALS)
                for component in normal]
        if absn[0] >= absn[1] and absn[0] >= absn[2]:
            axis = 0
        elif absn[1] >= absn[2]:
            axis = 1
        else:
            axis = 2
        u_axis, v_axis = _projection_axes_for(entry, axis)
        for index in (i0, i1, i2):
            point = verts[index]
            uv[2 * index] = center_uv[0] + (
                point[u_axis] - center_world[u_axis]) * density / float(size[0])
            uv[2 * index + 1] = center_uv[1] + (
                point[v_axis] - center_world[v_axis]) * density / float(size[1])
    model_input = {
        "label": "synthetic ART-07 control box", "construction": False,
        "nodes": [{"verts": verts, "uv": uv,
                   "meshes": [(material, [index for tri in tris for index in tri])]}],
    }
    return model_input, material


def control_uv_scale_density(patch_info, results, index, factor):
    """Negative controls 4/5: scale a tile material's UVs -> C15 must FAIL."""
    control = NegativeControl(index, "tile material UVs scaled x%.1f (world-UV "
                                     "texel density)" % factor)
    control.check_id = "C15"
    map_data = patch_info.get("data")
    if not isinstance(map_data, dict):
        control.status = "NOT RUN"
        control.detail = "art/material_texture_map.json unavailable"
        control.note = "cannot exercise the C15 density checker"
        return control
    model_input, material, effective = _density_probe_input(results, map_data)
    source = "parsed shipped .timbermesh file"
    if model_input is None:
        model_input, effective = _density_fixture_input(map_data)
        material = effective
        source = "synthetic ART-07 control box (no shipped model parsed)"
    if model_input is None:
        control.status = "NOT RUN"
        control.detail = "no tile material available for the scale control"
        control.note = "cannot exercise the C15 density checker"
        return control
    baseline = _density_analyze_inputs([model_input], map_data)
    baseline_row = baseline["rows"].get(effective)
    baseline_density = baseline_row["measured"] if baseline_row else None
    baseline_ok = bool(baseline_row is not None
                       and baseline_row["status"] == "PASS")
    probe_nodes = []
    for node in model_input["nodes"]:
        uv = list(node["uv"])
        for mesh_material, indices in node["meshes"]:
            if str(mesh_material) != str(material):
                continue
            for vertex_index in set(int(value) for value in indices):
                position = 2 * vertex_index
                if 0 <= position and position + 1 < len(uv):
                    uv[position] = 0.5 + (uv[position] - 0.5) * factor
                    uv[position + 1] = 0.5 + (uv[position + 1] - 0.5) * factor
        probe_nodes.append({"verts": node["verts"], "uv": tuple(uv),
                            "meshes": node["meshes"]})
    probe = {"label": model_input.get("label", "?"),
             "construction": model_input.get("construction"), "nodes": probe_nodes}
    report = _density_analyze_inputs([probe], map_data)
    scaled_row = report["rows"].get(effective)
    scaled_density = scaled_row["measured"] if scaled_row else None
    rejected = bool(scaled_row is not None and scaled_row["status"] == "FAIL")
    control.status = "REJECTED" if rejected else "NOT REJECTED"
    expected = scaled_row["px_expected"] if scaled_row else None
    control.detail = (
        "source=%s material=%r (effective %r) UVs scaled x%.1f about uv=0.5; "
        "baseline density %s px/m (map %s) -> scaled density %s px/m; C15 "
        "aggregate tolerance +-%.0f%% -> %s"
        % (source, material, effective, factor,
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
                             "C9 would reject them too; the control is scored on "
                             "the C15 density checker]" % factor)
    else:
        control.note = ("BUG: the C15 density checker accepted a tile material "
                        "scaled x%.1f" % factor)
    return control


def control_patch_uv_stretch(patch_info, results):
    """Negative control 6: stretch one large patch face to the full 0..1 square."""
    control = NegativeControl(6, "one large patch-mode face stretched to the full "
                                 "0..1 UV square (mode-aware canvas containment)")
    control.check_id = "C9"
    map_data = patch_info.get("data")
    if not isinstance(map_data, dict):
        control.status = "NOT RUN"
        control.detail = "art/material_texture_map.json unavailable"
        control.note = "cannot exercise the C9 containment checker"
        return control
    sample = None
    for result in results or []:
        if not getattr(result, "present", False):
            continue
        for fact in getattr(result, "facts", None) or []:
            if fact.vertex_count <= 0:
                continue
            uv_prop = fact.props.get("uv0")
            pos_prop = fact.props.get("position")
            if uv_prop is None or uv_prop.values is None or uv_prop.dim != 2:
                continue
            if pos_prop is None or pos_prop.values is None:
                continue
            uv_values = uv_prop.values
            for material, indices in fact.meshes:
                material = str(material)
                entry = material_uv_entry(map_data, material)
                if entry_mode(entry) != "patch":
                    continue
                triangle = None
                best_area = -1.0
                for start in range(0, len(indices) - 2, 3):
                    i0 = int(indices[start])
                    i1 = int(indices[start + 1])
                    i2 = int(indices[start + 2])
                    if not (0 <= i0 < fact.vertex_count
                            and 0 <= i1 < fact.vertex_count
                            and 0 <= i2 < fact.vertex_count):
                        continue
                    p0 = _author_point(pos_prop.values, i0)
                    p1 = _author_point(pos_prop.values, i1)
                    p2 = _author_point(pos_prop.values, i2)
                    e1 = (p1[0] - p0[0], p1[1] - p0[1], p1[2] - p0[2])
                    e2 = (p2[0] - p0[0], p2[1] - p0[1], p2[2] - p0[2])
                    cross = (e1[1] * e2[2] - e1[2] * e2[1],
                             e1[2] * e2[0] - e1[0] * e2[2],
                             e1[0] * e2[1] - e1[1] * e2[0])
                    area = 0.5 * math.sqrt(sum(c * c for c in cross))
                    if area > best_area:
                        best_area = area
                        triangle = (i0, i1, i2)
                if triangle is None or best_area <= 0.0:
                    continue
                corners = set(triangle)
                samples = []
                for vertex_index in indices:
                    vertex_index = int(vertex_index)
                    if vertex_index in corners:
                        continue
                    if 0 <= vertex_index \
                            and 2 * vertex_index + 1 < len(uv_values):
                        samples.append((fact.name, uv_values[2 * vertex_index],
                                        uv_values[2 * vertex_index + 1]))
                samples.extend([(fact.name, 0.0, 0.0), (fact.name, 1.0, 0.0),
                                (fact.name, 0.0, 1.0)])
                sample = {"source": "parsed shipped .timbermesh file",
                          "stem": getattr(result, "spec", {}).get("stem", "?"),
                          "material": material, "triangle": triangle,
                          "world_area": best_area, "samples": samples}
                break
            if sample is not None:
                break
        if sample is not None:
            break
    if sample is None:
        material = None
        for candidate in sorted(map_data.get("materials") or {}):
            entry = material_uv_entry(map_data, candidate)
            if entry_mode(entry) == "patch":
                material = candidate
                break
        if material is None:
            control.status = "NOT RUN"
            control.detail = "no patch-mode material available"
            control.note = "cannot exercise the C9 containment checker"
            return control
        sample = {"source": "synthetic fixture (no shipped model parsed)",
                  "stem": "-", "material": material, "triangle": (0, 1, 2),
                  "world_area": None,
                  "samples": [(material, 0.0, 0.0), (material, 1.0, 0.0),
                              (material, 0.0, 1.0)]}
    rows, reasons = containment_report({sample["material"]: sample["samples"]},
                                       map_data)
    rejected = bool(reasons)
    row = rows[0] if rows else None
    control.status = "REJECTED" if rejected else "NOT REJECTED"
    control.detail = (
        "source=%s file=%s material=%r triangle=%s world_area=%s m^2; the face "
        "UVs are replaced by (0,0)/(1,0)/(0,1) -> its %s canvas is uv=[%s]; "
        "mode-aware containment %s"
        % (sample["source"], sample["stem"], sample["material"],
           str(sample["triangle"]), "n/a" if sample["world_area"] is None
           else "%.4f" % sample["world_area"],
           row["mode"] if row else "?",
           _uv_text(row["rect"]) if row and row["rect"] else "-",
           "REJECTED" if rejected else "ACCEPTED"))
    if rejected:
        control.note = "rejected by C9: %s" % reasons[0]
    else:
        control.note = ("BUG: the mode-aware canvas containment checker accepted "
                        "a full 0..1 stretch of a patch face")
    return control


def run_negative_controls(patch_info, results):
    """Run the six requirement-D negative controls; each MUST be rejected."""
    controls = [
        control_uv_escape(patch_info, results),
        control_hazard_patch(patch_info),
        control_missing_material(patch_info, results),
        control_uv_scale_density(patch_info, results, 4, 0.5),
        control_uv_scale_density(patch_info, results, 5, 2.0),
        control_patch_uv_stretch(patch_info, results),
    ]
    return controls


def negative_controls_verdict(controls):
    """'PASS n/n' only when every control was REJECTED, else 'FAIL ...'."""
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
# rendering
# --------------------------------------------------------------------------- #

def render_header(meta):
    lines = []
    lines.append("=" * 104)
    lines.append("HDBF v2 - 3x3x1 indoor box farm .timbermesh validation report")
    lines.append("=" * 104)
    lines.append("report:     deterministic (identical input files -> byte-identical report;")
    lines.append("            no timestamps, no elapsed times)")
    lines.append("mode:       pure-python / Blender-free (zlib + the vendored official plugin's")
    lines.append("            protobuf schema model_pb2; no bpy, no Blender, no Unity)")
    lines.append("contract:   art/timbermesh_common.py (single source of truth: tiers, factions,")
    lines.append("            file naming, vanilla material whitelists, export-space coordinates)")
    lines.append("schema:     art/tools/timbermesh_blender_plugin/model_pb2.py")
    lines.append("python:     %s" % meta["python"])
    lines.append("workspace:  %s" % meta["workspace"])
    lines.append("output dir: %s" % meta["outdir"])
    lines.append("files:      %d expected (8 finished LOD0 + 8 ConstructionStage0), %d present"
                 % (meta["expected"], meta["present"]))
    lines.append("tolerances: bounds +-%.3f (strict 3.00 x 1.00 x 3.00 envelope), min corner "
                 "+-%.3f, empty positions +-%.3f, uv0 [0,1] +-%.3f, mode-aware ART-07 "
                 "canvas +-%.0e (tile guard bound +-%.0e)"
                 % (C.BOUNDS_TOLERANCE, C.BOUNDS_TOLERANCE,
                    C.POSITION_TOLERANCE, UV_TOLERANCE, PATCH_UV_TOL,
                    TILE_GUARD_TOL))
    lines.append("tri budget: <= %d per file (timbermesh_common.TRI_BUDGET_LOD0 = LOD0 tier "
                 "budget)" % meta["budget"])
    lines.append("roof:       ONE flat empty platform; walking surface == model apex y = %.2f; "
                 "nothing above it; %d cm clearance above the surface"
                 % (C.PLATFORM_TOP, int(round(C.PLATFORM_CLEAR_ABOVE * 100))))
    lines.append("materials:  ART-05 per-material real-_D texture patches + ART-07 world-scale "
                 "UV mapping (art/material_texture_map.json, schema %r); patch mean RGB / "
                 "std re-verified from the vanilla PNG bytes with art/png_stats.py "
                 "(mean +-%.1f, std +-%.1f, std <= std_max)"
                 % (MAP_SCHEMA, PATCH_MEAN_TOL, PATCH_STD_TOL))
    lines.append("ART-07:     uv = uv_center + (world_uv - world_center) * px_per_m / "
                 "texture_size in the Blender author space (the .timbermesh export is "
                 "converted back with author = (x-%.1f, %.1f-z, y)); dominant face normal "
                 "(unit Newell normal, |components| rounded to %d decimals, X > Y > Z) "
                 "picks the projection plane; C15 density: aggregate +-%.0f %% per "
                 "material, per-triangle +-%.0f %% above %.2f m^2, tile band %.0f-%.0f "
                 "px/m, patch floor >= %.0f px/m (2x ART-06), exact UV recompute +-%.0e; "
                 "ambiguous-plane triangles (top-two rounded |normal| components within "
                 "%.0e) <= %.0f %% of triangles / %.1f %% of world area; C16 verifies the "
                 "frozen mapping contract + per-file determinism"
                 % (EXPORT_SIZE_HALF_X, EXPORT_DEPTH_HALF_Z,
                    PROJECTION_ROUND_DECIMALS, DENSITY_AGG_TOL * 100.0,
                    DENSITY_TRI_TOL * 100.0, DENSITY_TRI_MIN_WORLD_AREA,
                    TILE_DENSITY_BAND_DEFAULT[0], TILE_DENSITY_BAND_DEFAULT[1],
                    DENSITY_PATCH_MIN, EXACT_UV_TOL, AMBIGUOUS_PLANE_TOL,
                    100.0 * AMBIGUOUS_COUNT_MAX, 100.0 * AMBIGUOUS_AREA_MAX))
    lines.append("")
    lines.append("-" * 104)
    lines.append("CHECKS (per file; all nine must PASS for the file to PASS)")
    for cid, _title in CHECK_DEFS:
        lines.append("  %-3s %s" % (cid, CHECK_DESCRIPTIONS[cid]))
    lines.append("")
    lines.append("CROSS-FILE MATERIAL CHECKS (C10-C12 ART-05 + C15-C16 ART-07; see RESULT "
                 "and their own sections)")
    lines.append("  C10 art/material_texture_map.json schema + coverage of every required "
                 "material")
    lines.append("  C11 real vanilla _D PNG patch mean RGB / std re-verification "
                 "(SKIP only when no texture root exists)")
    lines.append("  C12 hazard / large-shell policy (PaintedMetal.* forbidden, no hazard "
                 "patch shipped, exclusion rects respected)")
    lines.append("  C15 ART-07 world-UV texel density: every material of every shipped "
                 "file must keep the map px_per_m (aggregate +-%.0f %%, per-triangle "
                 "+-%.0f %% above %.2f m^2, tile band %.0f-%.0f px/m, patch floor "
                 "%.0f px/m, exact UV recompute +-%.0e, ambiguous planes <= %.0f %% "
                 "of triangles / %.1f %% of area)"
                 % (DENSITY_AGG_TOL * 100.0, DENSITY_TRI_TOL * 100.0,
                    DENSITY_TRI_MIN_WORLD_AREA, TILE_DENSITY_BAND_DEFAULT[0],
                    TILE_DENSITY_BAND_DEFAULT[1], DENSITY_PATCH_MIN, EXACT_UV_TOL,
                    100.0 * AMBIGUOUS_COUNT_MAX, 100.0 * AMBIGUOUS_AREA_MAX))
    lines.append("  C16 ART-07 UV mapping contract / determinism: sec.4 map fields, "
                 "px_per_m reproduction from the usable canvas/coverage, wrap/tile_meters/"
                 "uv_canvas/patch validation, construction shared windows and the same "
                 "density/canvas in every shipped file")
    lines.append("NEGATIVE CONTROLS (requirement D) run after the checks; each must be "
                 "REJECTED or the run fails.")
    lines.append("")
    return lines


def render_block(result, label, index=None, total=None):
    lines = []
    spec = result.spec
    heading = label
    if index is not None and total is not None:
        heading = "[%2d/%d] %s" % (index, total, spec["stem"])
    lines.append("-" * 104)
    lines.append(heading)
    lines.append("  tier=%-12s faction=%-10s kind=%s"
                 % (spec.get("tier") or "-", spec.get("faction") or "-",
                    "construction-stage0" if spec.get("construction") else "finished (LOD0)"))
    lines.append("  file: %s" % _rel(result.path))
    if result.present:
        lines.append("  bytes: %s bytes (zlib) -> %s bytes (protobuf)"
                     % (_num(result.raw_size), _num(result.decomp_size)))
    else:
        lines.append("  bytes: - (file missing)")
    for cid, title in CHECK_DEFS:
        check = result.c(cid)
        lines.append("  %-3s %-33s %-7s %s" % (cid, title, check.status, check.text))
        for reason in check.reasons:
            lines.append("        ! %s" % reason)
        for note in check.notes:
            lines.append("        - %s" % note)
    lines.append("  RESULT: %s" % result.verdict)
    return lines


def render_table(results):
    lines = []
    lines.append("=" * 104)
    lines.append("SUMMARY TABLE")
    lines.append("=" * 104)
    header = "%-46s %-6s" % ("FILE", "RESULT")
    header += "".join("%3s" % ("C%d" % i) for i in range(1, len(CHECK_DEFS) + 1))
    header += "  %6s %3s %8s %5s  %-17s %-17s" % ("TRIS", "MAT", "PLATFORM", "PORT",
                                                  "SIZE X/Y/Z", "MIN X/Y/Z")
    lines.append(header)
    for result in results:
        codes = "".join("%3s" % _letter(result.c(cid)) for cid, _ in CHECK_DEFS)
        triangles = "-" if result.tris is None else "%d" % result.tris
        materials = "-" if not result.materials else "%d" % len(result.materials)
        platform = ("ok" if result.platform_ok else "FAIL") if result.present else "-"
        ports = "%d/3" % result.ports_present if result.present else "-"
        if result.bounds:
            size = "%.3f/%.3f/%.3f" % result.bounds["size"]
            minimum = "%.3f/%.3f/%.3f" % result.bounds["min"]
        else:
            size = "-"
            minimum = "-"
        lines.append("%-46s %-6s%s  %6s %3s %8s %5s  %-17s %-17s"
                     % (result.spec["stem"], result.verdict, codes, triangles, materials,
                        platform, ports, size, minimum))
    return lines


def render_verdict(results, extras, meta, globals_=None, controls=None):
    lines = []
    present = sum(1 for result in results if result.present)
    passed = sum(1 for result in results if result.verdict == "PASS")
    failed = len(results) - passed
    check_total = len(results) * len(CHECK_DEFS)
    check_pass = sum(1 for result in results for cid, _ in CHECK_DEFS
                     if result.c(cid).status == "PASS")
    check_fail = sum(1 for result in results for cid, _ in CHECK_DEFS
                     if result.c(cid).status == "FAIL")
    check_skip = sum(1 for result in results for cid, _ in CHECK_DEFS
                     if result.c(cid).status == "NOT RUN")
    lines.append("=" * 104)
    lines.append("RESULT")
    lines.append("=" * 104)
    lines.append("FILES:  %d/%d present; %d PASS, %d FAIL"
                 % (present, len(results), passed, failed))
    lines.append("CHECKS: %d PASS, %d FAIL, %d NOT RUN (of %d = %d files x %d checks)"
                 % (check_pass, check_fail, check_skip, check_total,
                    len(results), len(CHECK_DEFS)))
    if extras:
        lines.append("UNEXPECTED FILES in art/timbermesh (NOT part of the 16-file contract, "
                     "not scored): %d" % len(extras))
        for result in extras:
            lines.append("  %s -> %s" % (os.path.basename(result.path), result.verdict))
    global_failures = []
    if globals_ is not None:
        lines.append("CROSS-FILE MATERIAL CHECKS (C10-C12 ART-05 + C15-C16 ART-07):")
        for check in globals_.checks:
            lines.append("  %-3s %-58s %-6s %s"
                         % (check.cid, check.title, check.status, check.text))
            if check.status == "FAIL":
                global_failures.append(check)
    control_verdict = None
    if controls is not None:
        control_verdict = negative_controls_verdict(controls)
        rejected_controls = sum(1 for control in controls if control.rejected)
        lines.append("NEGATIVE CONTROLS: %s (each control must be REJECTED)"
                     % control_verdict)
        lines.append("controls: %d/%d rejected"
                     % (rejected_controls, len(controls)))
    if failed:
        lines.append("FAILING FILES:")
        for result in results:
            if result.verdict != "PASS":
                bad = [cid for cid, _ in CHECK_DEFS if result.c(cid).status != "PASS"]
                lines.append("  %-50s failed: %s"
                             % (result.spec["stem"], ", ".join(bad) or "?"))
    controls_ok = control_verdict is None or control_verdict.startswith("PASS")
    overall = "PASS" if failed == 0 and present == len(results) \
        and not global_failures and controls_ok else "FAIL"
    lines.append("OVERALL VERDICT: %s" % overall)
    if present < len(results):
        lines.append("  (reason: %d of the %d expected .timbermesh files are missing - "
                     "the v2 export is not complete)"
                     % (len(results) - present, len(results)))
    for check in global_failures:
        failures = check.reasons or [check.text]
        lines.append("  (reason: %s FAIL - %s)" % (check.cid, failures[0]))
    if control_verdict is not None and not controls_ok:
        lines.append("  (reason: negative control not rejected - requirement D not "
                     "demonstrated)")
    if meta.get("report_path"):
        lines.append("report: %s" % _rel(meta["report_path"]))
    return lines


def render_patch_checks(globals_):
    """Report sections for the ART-05 patch + ART-07 world-UV cross-file checks."""
    lines = []
    lines.append("=" * 104)
    lines.append("C10-C12 + C15-C16: MATERIAL TEXTURE PATCH + ART-07 WORLD-UV CHECKS "
                 "(cross-file)")
    lines.append("=" * 104)
    lines.append("map:        %s" % globals_.map_source)
    if globals_.map_error:
        lines.append("map error:  %s" % globals_.map_error)
    lines.append("map schema: %r; texture roots: env HDBF_VANILLA_TEX_ROOT > "
                 "map.texture_roots > defaults" % MAP_SCHEMA)
    lines.append("patch stats: mean RGB + std (max channel std, 0..255) re-measured from "
                 "the real vanilla _D PNG with art/png_stats.py; recorded mean +-%.1f, "
                 "std +-%.1f, std <= std_max" % (PATCH_MEAN_TOL, PATCH_STD_TOL))
    lines.append("ART-07:     world-scale planar projection at the native-on-atlas "
                 "texel density (art/ART07_UV_TILING.md); UVs inside the material "
                 "canvas because the game AutoAtlaser remaps them into the material's "
                 "own atlas cell (no UV>1 Repeat tiling)")
    lines.append("")
    for check in globals_.checks:
        lines.append("-" * 104)
        lines.append("%-3s %-56s %s" % (check.cid, check.title, check.status))
        lines.append("  %s" % check.text)
        for reason in check.reasons:
            lines.append("        ! %s" % reason)
        for note in check.notes:
            lines.append("        - %s" % note)
    lines.append("")
    lines.append("-" * 104)
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
    lines.append("-" * 104)
    lines.append("WORLD-UV TEXEL DENSITY TABLE (C15; per material of the 16 shipped "
                 ".timbermesh files, author space)")
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
                 "retired ART-06 density of 32 px/m; the WindowsAtlas glass pane is "
                 "82.4 px/m by design); aggregate tolerance +-%.0f %% vs the map "
                 "px_per_m"
                 % (band[0], band[1], DENSITY_PATCH_MIN, DENSITY_AGG_TOL * 100.0))
    if report.get("triangles"):
        lines.append("  per-triangle tolerance +-%.0f %% above %.2f m^2 world area "
                     "(%d/%d failed); exact UV recompute: %d triangle(s) checked, "
                     "%d mismatch(es) > %.0e; ambiguous planes %d/%d = %.2f %% of "
                     "triangles / %.3f %% of area (limits %.0f %% / %.1f %%)"
                     % (DENSITY_TRI_TOL * 100.0, DENSITY_TRI_MIN_WORLD_AREA,
                        report.get("large_failed", 0),
                        report.get("large_triangles", 0),
                        report.get("exact_checked", 0),
                        report.get("exact_failed", 0), EXACT_UV_TOL,
                        report.get("ambiguous_count", 0),
                        report.get("triangles", 0),
                        100.0 * report.get("amb_fraction", 0.0),
                        100.0 * report.get("amb_area_fraction", 0.0),
                        100.0 * AMBIGUOUS_COUNT_MAX,
                        100.0 * AMBIGUOUS_AREA_MAX))
    lines.append("  construction stage: the 8 ConstructionStage0 files share ONE baked "
                 "Folktails UV set; their density/exact UVs are measured against the "
                 "Folktails source material of the construction_shared_patches window")
    lines.append("")
    return lines


def render_console(results, extras, probes, meta, globals_=None, controls=None):
    lines = []
    lines.append("HDBF v2 3x3x1 timbermesh validation (deterministic report)")
    lines.append("mode: pure-python / Blender-free (vendored protobuf model_pb2 + zlib); "
                 "contract: art/timbermesh_common.py + art/material_texture_map.json")
    lines.append("expected: %d files (8 finished + 8 ConstructionStage0); found: %d"
                 % (len(results), sum(1 for result in results if result.present)))
    lines.append("")
    for index, result in enumerate(results, 1):
        measured = []
        if result.bounds:
            measured.append("bounds=%s" % "x".join("%.3f" % value
                                                   for value in result.bounds["size"]))
        if result.tris is not None:
            measured.append("tris=%d" % result.tris)
        if result.materials:
            measured.append("mats=%d" % len(result.materials))
        lines.append("  [%2d/%d] %-50s %-5s %s"
                     % (index, len(results), result.spec["stem"], result.verdict,
                        "; ".join(measured)))
        for cid, _title in CHECK_DEFS:
            check = result.c(cid)
            if check.status == "FAIL":
                lines.append("           %s FAIL: %s" % (cid, check.text))
                for reason in check.reasons[:6]:
                    lines.append("             ! %s" % reason)
    lines.append("")
    lines.extend(render_table(results))
    if globals_ is not None:
        lines.append("")
        lines.append("CROSS-FILE CHECKS (C10-C12 ART-05 + C15-C16 ART-07)")
        for check in globals_.checks:
            lines.append("  %-3s %-52s %-6s %s"
                         % (check.cid, check.title, check.status, check.text))
            if check.status != "PASS":
                for reason in check.reasons[:6]:
                    lines.append("           ! %s" % reason)
        if globals_.variance_rows:
            lines.append("  patch table (material | texture | pixel rect | uv rect | "
                         "mean RGB | std | std_max):")
            for row in globals_.variance_rows:
                lines.append("    %-4s %-34s %-28s %-18s %-36s %-20s %7.3f %7.2f"
                             % (row["status"], row["material"], row["texture"],
                                _rect_text(row["pixel_rect"]),
                                _uv_text(row["uv_rect"]) if row["uv_rect"] else "-",
                                _mean_text(row["mean_measured"]),
                                row["std_measured"], row["std_max"]))
        density_report = globals_.density_report or {}
        density_rows = density_report.get("rows") or {}
        if density_rows:
            lines.append("  world-UV texel density table (material | mode | canvas | "
                         "px/m expected | measured min/mean/max | tris):")
            for material in sorted(density_rows):
                row = density_rows[material]
                if row.get("measured") is None:
                    lines.append("    %-4s %-38s %-5s %-20s %8.2f %9s %9s %9s %6d"
                                 % ("FAIL", material, row.get("mode") or "?",
                                    _rect_text(row["canvas"])
                                    if row.get("canvas") else "-",
                                    row.get("px_expected") or 0.0, "-", "-", "-",
                                    row.get("tri_count") or 0))
                    continue
                lines.append("    %-4s %-38s %-5s %-20s %8.2f %9.3f %9.3f %9.3f %6d"
                             % (row["status"], material, row["mode"],
                                _rect_text(row["canvas"]) if row["canvas"] else "-",
                                row["px_expected"], row["min"], row["measured"],
                                row["max"], row["tri_count"]))
            band = density_report.get("tile_band") or TILE_DENSITY_BAND_DEFAULT
            lines.append("    tile band %.0f-%.0f px/m; patch floor >= %.0f px/m "
                         "(2x ART-06; WindowsAtlas glass pane 82.4 px/m by design); "
                         "aggregate tolerance +-%.0f %%; ambiguous planes %.2f %% of "
                         "triangles / %.3f %% of area"
                         % (band[0], band[1], DENSITY_PATCH_MIN,
                            DENSITY_AGG_TOL * 100.0,
                            100.0 * density_report.get("amb_fraction", 0.0),
                            100.0 * density_report.get("amb_area_fraction", 0.0)))
    lines.append("")
    lines.extend(render_verdict(results, extras, meta, globals_, controls))
    if controls is not None:
        lines.append("")
        lines.append("NEGATIVE CONTROLS (requirement D; each must be rejected, or the "
                     "run fails)")
        lines.extend(render_negative_controls(controls))
    for label, result in probes:
        lines.append("")
        lines.append("PARSER CROSS-CHECK (non-scoring): %s" % label)
        lines.extend(render_block(result, "  %s" % os.path.basename(result.path)))
    return lines


def build_report(results, extras, probes, meta, globals_=None, controls=None):
    lines = render_header(meta)
    lines.append("PER-FILE RESULTS")
    lines.append("")
    total = len(results)
    for index, result in enumerate(results, 1):
        lines.extend(render_block(result, None, index, total))
    lines.append("")
    if extras:
        lines.append("=" * 104)
        lines.append("UNEXPECTED FILES IN art/timbermesh (not part of the 16-file contract; "
                     "not scored)")
        lines.append("=" * 104)
        for result in extras:
            lines.extend(render_block(result, "[UNEXPECTED] %s"
                                      % os.path.basename(result.path)))
        lines.append("")
    if globals_ is not None:
        lines.extend(render_patch_checks(globals_))
    lines.extend(render_table(results))
    lines.append("")
    lines.extend(render_verdict(results, extras, meta, globals_, controls))
    if controls is not None:
        lines.append("")
        lines.append("=" * 104)
        lines.append("NEGATIVE CONTROLS (requirement D: each control must be rejected)")
        lines.append("=" * 104)
        lines.extend(render_negative_controls(controls))
        lines.append("")
    if probes:
        lines.append("")
        lines.append("=" * 104)
        lines.append("APPENDIX - PARSER CROSS-CHECK (NOT SCORED)")
        lines.append("=" * 104)
        lines.append("The assets below are NOT part of the 16-file ART-02 contract.  Vanilla")
        lines.append("assets are expected to fail many contract checks; this appendix only proves")
        lines.append("that the zlib + protobuf parser works and shows the diagnosis it produces")
        lines.append("on a non-conforming file.  It never affects the overall verdict or exit code.")
        lines.append("")
        for label, result in probes:
            lines.extend(render_block(result, "%s: %s"
                                      % (label, os.path.basename(result.path))))
        lines.append("")
    return "\n".join(lines) + "\n"


# --------------------------------------------------------------------------- #
# main
# --------------------------------------------------------------------------- #

def _script_argv(argv=None):
    """Strip the host arguments when the script is launched by Blender.

    The frozen verification pipeline runs this pure-python validator as
    ``blender.exe -b --factory-startup -noaudio --python art\\validate_timbermesh.py``
    (art/ART07_UV_TILING.md sec.8).  Blender then exposes its OWN command line in
    ``sys.argv`` (and forwards script options after a ``--`` separator), so the
    argparse parser must only see the validator's own options.
    """
    args = list(sys.argv[1:]) if argv is None else list(argv)
    if "--" in args:
        return args[args.index("--") + 1:]
    known = {"--report", "--no-report", "--reference", "--probe", "--self-test"}
    host_markers = ("-b", "--background", "--factory-startup", "-noaudio",
                    "--python", "--python-exit-code", "-P")
    looks_like_host = ("blender" in os.path.basename(sys.argv[0]).lower()
                       or any(arg in host_markers for arg in args)
                       or any(arg.lower().endswith(".py") for arg in args))
    if not looks_like_host:
        return args
    filtered = []
    index = 0
    while index < len(args):
        arg = args[index]
        if arg in known:
            filtered.append(arg)
            index += 1
            if arg in ("--report", "--probe") and index < len(args):
                filtered.append(args[index])
                index += 1
        else:
            index += 1
    return filtered


def parse_args(argv=None):
    parser = argparse.ArgumentParser(
        description="Pure-Python ART-02 .timbermesh validator (no Blender).")
    parser.add_argument("--report", default=os.path.join(HERE, "timbermesh-validation.txt"),
                        help="report output path (default: art/timbermesh-validation.txt)")
    parser.add_argument("--no-report", action="store_true",
                        help="do not write the report file, print the summary only")
    parser.add_argument("--reference", action="store_true",
                        help="also parse the vanilla ShantySpeaker SDK asset as a non-scoring "
                             "parser cross-check (expected to fail contract checks)")
    parser.add_argument("--probe", action="append", default=[], metavar="PATH",
                        help="parse an arbitrary .timbermesh for diagnosis (non-scoring, "
                             "repeatable)")
    parser.add_argument("--self-test", action="store_true",
                        help="run only the six requirement-D negative controls (uv escape, "
                             "hazard-stripe patch, missing map record, tile UV scale x0.5, "
                             "tile UV scale x2.0, patch face stretched to the full 0..1 "
                             "square); exit 0 only when all six are rejected")
    return parser.parse_args(_script_argv(argv))


def _emit(lines):
    text = "\n".join(lines)
    try:
        print(text)
    except UnicodeEncodeError:                                    # pragma: no cover - console only
        stream = getattr(sys.stdout, "buffer", None)
        if stream is None:
            raise
        stream.write((text + "\n").encode("utf-8", "replace"))


def _run_specs(patch_info):
    """Parse + check every expected shipped .timbermesh (no extras/probes)."""
    specs = C.all_model_specs()
    results = []
    for spec in specs:
        analysis = Analysis(spec, expected=True, patch_map=patch_info)
        analysis.analyze()
        results.append(analysis)
    return specs, results


def _map_origin_text(patch_info):
    if patch_info.get("data") is not None:
        return patch_info.get("source") or "-"
    return "UNAVAILABLE (%s)" % (patch_info.get("error") or "not loaded")


def _self_test(patch_info):
    """Requirement D: run ONLY the six negative controls; each must be rejected."""
    _specs, results = _run_specs(patch_info)
    controls = run_negative_controls(patch_info, results)
    lines = []
    lines.append("HDBF v2 ART-05/ART-07 - negative controls (requirement D: every "
                 "control MUST be rejected)")
    lines.append("mode:       --self-test")
    lines.append("map:        %s" % _map_origin_text(patch_info))
    lines.append("controls:   (1) uv escape -> C9, (2) hazard-stripe patch -> C11, "
                 "(3) missing map record -> C10,")
    lines.append("            (4) tile UV x0.5 -> C15, (5) tile UV x2.0 -> C15, "
                 "(6) large patch face -> full 0..1 square -> C9")
    lines.append("")
    lines.extend(render_negative_controls(controls))
    verdict = negative_controls_verdict(controls)
    lines.append("")
    lines.append("SELF-TEST VERDICT: %s" % verdict)
    _emit(lines)
    return 0 if verdict.startswith("PASS") else 1


def main(argv=None):
    args = parse_args(argv)
    patch_info = load_patch_map()
    if args.self_test:
        return _self_test(patch_info)
    specs, results = _run_specs(patch_info)

    # any extra top-level .timbermesh (not in the contract) is reported but not scored
    extras = []
    known = set(os.path.basename(spec["path"]) for spec in specs)
    if os.path.isdir(C.TIMBERMESH_DIR):
        for name in sorted(os.listdir(C.TIMBERMESH_DIR)):
            path = os.path.join(C.TIMBERMESH_DIR, name)
            if name.endswith(".timbermesh") and os.path.isfile(path) and name not in known:
                analysis = Analysis(make_probe_spec(path), expected=False,
                                    patch_map=patch_info)
                analysis.analyze()
                extras.append(analysis)

    probes = []
    for path in args.probe:
        analysis = Analysis(make_probe_spec(path), expected=False,
                            patch_map=patch_info)
        analysis.analyze()
        probes.append(("PROBE", analysis))
    if args.reference:
        analysis = Analysis(make_probe_spec(REFERENCE_PATH), expected=False,
                            patch_map=patch_info)
        analysis.analyze()
        probes.append(("REFERENCE (vanilla SDK asset; contract checks expected to fail)", analysis))

    globals_ = evaluate_global_checks(patch_info, results)
    controls = run_negative_controls(patch_info, results)
    meta = {
        "python": "%s %s on %s" % (platform.python_implementation(),
                                   platform.python_version(), platform.system()),
        "workspace": WORKSPACE,
        "outdir": _rel(C.TIMBERMESH_DIR),
        "expected": len(specs),
        "present": sum(1 for result in results if result.present),
        "budget": C.TRI_BUDGET_LOD0,
        "report_path": None if args.no_report else os.path.abspath(args.report),
        "map_source": _map_origin_text(patch_info),
    }
    if not args.no_report:
        report = build_report(results, extras, probes, meta, globals_, controls)
        with open(args.report, "w", encoding="utf-8", newline="\n") as handle:
            handle.write(report)
    _emit(render_console(results, extras, probes, meta, globals_, controls))
    files_ok = all(result.verdict == "PASS" for result in results)
    globals_ok = not globals_.failures()
    controls_ok = all(control.rejected for control in controls)
    return 0 if (files_ok and globals_ok and controls_ok) else 1


if __name__ == "__main__":
    sys.exit(main())
