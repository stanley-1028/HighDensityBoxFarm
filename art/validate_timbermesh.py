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
  C9 material patch UV containment (ART-05): every uv0 value used by a material's
     faces lies inside that material's real ``_D`` texture patch rect from
     ``art/material_texture_map.json`` (tolerance 1e-4); per-material uv0 min/max
     is reported and a single escaping vertex FAILS.

Cross-file checks (ART-05 material texture fix, 2026-10-27; reported separately):
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

NEGATIVE CONTROLS (requirement D) run at the end of every normal run *and* under
``--self-test``: (1) a shipped uv0 shifted outside its patch must be rejected by
C9; (2) a synthetic patch record over the real yellow-black hazard stripes of
``PaintedMetal_D.Folktails.png`` must be rejected by C11; (3) removing one
required material from an in-memory map copy must be rejected by C10.  The run
exits nonzero if any control is NOT rejected.

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
    "C9": "ART-05: for every material used by a mesh, every uv0 value referenced by that "
          "material's faces lies inside the material's real-_D texture patch uv_rect from "
          "art/material_texture_map.json (tolerance %.0e); per-material uv0 min/max is "
          "reported and ONE escaping vertex FAILS" % 1e-4,
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
        """Every uv0 used by a material's faces must sit in that material's patch."""
        check = self.c("C9")
        map_data = self.patch_map.get("data")
        if map_data is None:
            check.fail("material texture patch map unavailable",
                       ["cannot test uv0 patch containment: %s"
                        % (self.patch_map.get("error") or "art/material_texture_map.json "
                           "not loaded")],
                       ["C9 needs the ART-05 material_texture_map.json written by the "
                        "Lead's material analysis; it is absent or unreadable"])
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
                       ["the .timbermesh has no readable uv0 property, so the ART-05 "
                        "patch containment cannot be verified"])
            return
        rows, reasons = containment_report(uv_samples, map_data)
        notes = []
        for row in rows:
            notes.append(container_row_text(row))
        bad_rows = [row for row in rows if row["status"] != "PASS"]
        total_values = sum(row["count"] for row in rows)
        text = ("%d material(s) / %s uv0 value(s) checked against their real-_D texture "
                "patch (tolerance %.0e): %s"
                % (len(rows), _num(total_values), PATCH_UV_TOL,
                   "all inside" if not bad_rows
                   else "%d material(s) ESCAPE their patch" % len(bad_rows)))
        if reasons:
            check.fail("uv0 escapes the material's real-texture patch", _capped(reasons),
                       notes)
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


# -- UV containment (C9) ----------------------------------------------------- #

def containment_report(uv_samples, map_data):
    """(rows, reasons) for uv0-in-patch containment.

    ``uv_samples`` maps material -> [(source, u, v)] (source is a human label).
    A single sample outside the material's patch uv_rect +-PATCH_UV_TOL is a FAIL.
    """
    rows = []
    reasons = []
    for material in sorted(uv_samples):
        samples = uv_samples[material]
        rect = patch_uv_rect(map_data, material)
        row = {"material": material, "rect": tuple(rect) if rect else None,
               "count": 0, "min_u": None, "max_u": None, "min_v": None,
               "max_v": None, "bad_count": 0, "bad": [], "status": "PASS"}
        if rect is None:
            row["status"] = "FAIL"
            row["bad_count"] = len(samples)
            row["bad"] = [("no patch uv_rect in art/material_texture_map.json",
                           None, None)]
            reasons.append("material %r has no real-texture patch uv_rect in "
                           "art/material_texture_map.json" % material)
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
            if (u < u0 - PATCH_UV_TOL or u > u1 + PATCH_UV_TOL
                    or v < v0 - PATCH_UV_TOL or v > v1 + PATCH_UV_TOL):
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
            reasons.append("material %r: no finite uv0 values on its faces" % material)
        elif bad_count:
            row["status"] = "FAIL"
            reasons.append("material %r: %d uv0 value(s) lie outside its patch "
                           "uv_rect [%.6f, %.6f, %.6f, %.6f] +-%.0e (first: %s)"
                           % (material, bad_count, u0, v0, u1, v1, PATCH_UV_TOL,
                              bad[:3]))
        rows.append(row)
    return rows, reasons


def container_row_text(row):
    """One report/notes line: per-material PASS/FAIL + uv0 min/max + patch uv_rect."""
    rect = row["rect"]
    if rect is None:
        return ("%-4s %-34s no real-texture patch uv_rect in the map (%d uv0 value(s))"
                % (row["status"], row["material"], row["count"]))
    if row["count"]:
        return ("%-4s %-34s patch uv=[%.6f, %.6f, %.6f, %.6f]  uv0 u=[%.6f, %.6f] "
                "v=[%.6f, %.6f]  values=%d%s"
                % (row["status"], row["material"], rect[0], rect[1], rect[2], rect[3],
                   row["min_u"], row["max_u"], row["min_v"], row["max_v"],
                   row["count"],
                   "" if not row["bad_count"] else "  ESCAPED=%d" % row["bad_count"]))
    return ("%-4s %-34s patch uv=[%.6f, %.6f, %.6f, %.6f]  no finite uv0"
            % (row["status"], row["material"], rect[0], rect[1], rect[2], rect[3]))


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
    """The three cross-file ART-05 checks (C10-C12) + their report data."""

    __slots__ = ("coverage", "variance", "hazard", "variance_rows",
                 "variance_skipped", "coverage_details", "map_source", "map_error")

    def __init__(self):
        self.coverage = None
        self.variance = None
        self.hazard = None
        self.variance_rows = []
        self.variance_skipped = []
        self.coverage_details = {}
        self.map_source = "-"
        self.map_error = None

    @property
    def checks(self):
        return [check for check in (self.coverage, self.variance, self.hazard)
                if check is not None]

    def failures(self):
        return [check for check in self.checks if check.status == "FAIL"]


def evaluate_global_checks(patch_info, results):
    """Run C10/C11/C12 over the whole shipped export."""
    globals_ = GlobalChecks()
    globals_.map_source = patch_info.get("source") or "-"
    globals_.map_error = patch_info.get("error")
    globals_.coverage, globals_.coverage_details = evaluate_coverage(
        patch_info.get("data"), patch_info.get("source"), results,
        patch_info.get("error"))
    globals_.variance, globals_.variance_rows, globals_.variance_skipped = \
        evaluate_variance(patch_info, results)
    globals_.hazard = evaluate_hazard(patch_info, results)
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
    return {
        "texture": "negative-control-synthetic.png",
        "texture_source": "synthetic",
        "texture_size": [1024, 1024],
        "patch_class": "wood",
        "patch": {
            "pixel_rect": [0, 0, 64, 64],
            "size_px": [64, 64],
            "uv_rect": list(patch_rect),
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
    """Negative control 1: shift one real shipped uv0 outside its patch -> C9 FAIL."""
    control = NegativeControl(1, "one shipped uv0 shifted outside its patch "
                                "(uv containment)")
    control.check_id = "C9"
    map_data = patch_info.get("data")
    sample = _first_shipped_uv(results)
    if sample is not None:
        source, material, u, v = sample
        real_uv = True
    else:
        source, material, u, v = "synthetic fixture (no shipped model)", "Fixture.Wood", 0.30, 0.40
        real_uv = False
    rect = patch_uv_rect(map_data, material) if map_data is not None else None
    inside = (rect is not None
              and rect[0] - PATCH_UV_TOL <= u <= rect[2] + PATCH_UV_TOL
              and rect[1] - PATCH_UV_TOL <= v <= rect[3] + PATCH_UV_TOL)
    if inside:
        probe_map = map_data
    else:
        rect = _rect_around(u, v, 0.05)
        probe_map = _map_with_synthetic_patch(map_data, material, rect)
    rect = tuple(float(value) for value in rect)
    width = rect[2] - rect[0]
    bad_u = rect[2] + width + 0.05
    bad_v = v
    samples = {material: [(source, bad_u, bad_v)]}
    rows, reasons = containment_report(samples, probe_map)
    rejected = bool(reasons)
    control.status = "REJECTED" if rejected else "NOT REJECTED"
    control.detail = ("source=%s material=%r uv0=(%.6f, %.6f) -> (%.6f, %.6f): moved "
                      "%.4f in u (> one patch width %.4f) outside patch uv=[%.6f, "
                      "%.6f, %.6f, %.6f]; containment checker %s (%s)"
                      % (source, material, u, v, bad_u, bad_v, bad_u - u, width,
                         rect[0], rect[1], rect[2], rect[3],
                         "REJECTED" if rejected else "ACCEPTED", rows[0]["status"]
                         if rows else "no row"))
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


def run_negative_controls(patch_info, results):
    """Run the three requirement-D negative controls; each MUST be rejected."""
    controls = [
        control_uv_escape(patch_info, results),
        control_hazard_patch(patch_info),
        control_missing_material(patch_info, results),
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
                 "+-%.3f, empty positions +-%.3f, uv0 [0,1] +-%.3f, uv0-in-material-patch "
                 "+-%.0e" % (C.BOUNDS_TOLERANCE, C.BOUNDS_TOLERANCE,
                             C.POSITION_TOLERANCE, UV_TOLERANCE, PATCH_UV_TOL))
    lines.append("tri budget: <= %d per file (timbermesh_common.TRI_BUDGET_LOD0 = LOD0 tier "
                 "budget)" % meta["budget"])
    lines.append("roof:       ONE flat empty platform; walking surface == model apex y = %.2f; "
                 "nothing above it; %d cm clearance above the surface"
                 % (C.PLATFORM_TOP, int(round(C.PLATFORM_CLEAR_ABOVE * 100))))
    lines.append("materials:  ART-05 per-material real-_D texture patches "
                 "(art/material_texture_map.json, schema %r); patch mean RGB / std "
                 "re-verified from the vanilla PNG bytes with art/png_stats.py "
                 "(mean +-%.1f, std +-%.1f, std <= std_max)"
                 % (MAP_SCHEMA, PATCH_MEAN_TOL, PATCH_STD_TOL))
    lines.append("")
    lines.append("-" * 104)
    lines.append("CHECKS (per file; all nine must PASS for the file to PASS)")
    for cid, _title in CHECK_DEFS:
        lines.append("  %-3s %s" % (cid, CHECK_DESCRIPTIONS[cid]))
    lines.append("")
    lines.append("CROSS-FILE MATERIAL CHECKS (C10-C12; see RESULT and their own sections)")
    lines.append("  C10 art/material_texture_map.json schema + coverage of every required "
                 "material")
    lines.append("  C11 real vanilla _D PNG patch mean RGB / std re-verification "
                 "(SKIP only when no texture root exists)")
    lines.append("  C12 hazard / large-shell policy (PaintedMetal.* forbidden, no hazard "
                 "patch shipped, exclusion rects respected)")
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
        lines.append("CROSS-FILE MATERIAL CHECKS (ART-05 real-texture patches):")
        for check in globals_.checks:
            lines.append("  %-3s %-58s %-6s %s"
                         % (check.cid, check.title, check.status, check.text))
            if check.status == "FAIL":
                global_failures.append(check)
    control_verdict = None
    if controls is not None:
        control_verdict = negative_controls_verdict(controls)
        lines.append("NEGATIVE CONTROLS: %s (each control must be REJECTED)"
                     % control_verdict)
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
    """Report/console sections for the ART-05 cross-file material checks."""
    lines = []
    lines.append("=" * 104)
    lines.append("C10-C12: MATERIAL TEXTURE PATCH CHECKS (ART-05, cross-file)")
    lines.append("=" * 104)
    lines.append("map:        %s" % globals_.map_source)
    if globals_.map_error:
        lines.append("map error:  %s" % globals_.map_error)
    lines.append("map schema: %r; texture roots: env HDBF_VANILLA_TEX_ROOT > "
                 "map.texture_roots > defaults" % MAP_SCHEMA)
    lines.append("patch stats: mean RGB + std (max channel std, 0..255) re-measured from "
                 "the real vanilla _D PNG with art/png_stats.py; recorded mean +-%.1f, "
                 "std +-%.1f, std <= std_max" % (PATCH_MEAN_TOL, PATCH_STD_TOL))
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
        lines.append("ART-05 MATERIAL PATCH CHECKS (C10-C12)")
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
                        help="run only the three requirement-D negative controls (UV escape, "
                             "hazard-stripe patch, missing map record); exit 0 only when all "
                             "three are rejected")
    return parser.parse_args(argv)


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
    """Requirement D: run ONLY the three negative controls; each must be rejected."""
    _specs, results = _run_specs(patch_info)
    controls = run_negative_controls(patch_info, results)
    lines = []
    lines.append("HDBF v2 ART-05 - negative controls (requirement D: every control "
                 "MUST be rejected)")
    lines.append("mode:       --self-test")
    lines.append("map:        %s" % _map_origin_text(patch_info))
    lines.append("controls:   (1) uv escape -> C9, (2) hazard-stripe patch -> C11, "
                 "(3) missing map record -> C10")
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
