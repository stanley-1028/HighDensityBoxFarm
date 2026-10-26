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

The report (``art/timbermesh-validation.txt``) is deterministic: identical input
files produce a byte-identical report (no timestamps, no elapsed times).

Usage:
    python art/validate_timbermesh.py
    python art/validate_timbermesh.py --no-report --reference
    python art/validate_timbermesh.py --probe art/timbermesh/axis-test/BoxFarm.AxisTest.Model.timbermesh

Exit code 0 only when all 16 expected files are present and every check of every
expected file passes.  ``--probe``/``--reference`` are diagnostic only and never
affect the exit code (a vanilla asset is expected to fail the contract checks;
it is used to prove the parser works and to show its diagnosis).
"""

from __future__ import annotations

import argparse
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

CHECK_DEFS = [
    ("C1", "parses (zlib + protobuf)"),
    ("C2", "model identity (name/version)"),
    ("C3", "mesh bounds (3x1x3 @ corner)"),
    ("C4", "triangles <= budget"),
    ("C5", "materials (whitelist + count)"),
    ("C6", "#Port# empties + flat empty roof"),
    ("C7", "vertex properties"),
    ("C8", "topology (indices/parents/uv0)"),
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

    def __init__(self, spec, expected=True):
        self.spec = spec
        self.path = spec.get("path", "")
        self.expected = expected
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
                          ("C8", self._check_topology)):
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
                 "+-%.3f, empty positions +-%.3f, uv0 [0,1] +-%.3f"
                 % (C.BOUNDS_TOLERANCE, C.BOUNDS_TOLERANCE,
                    C.POSITION_TOLERANCE, UV_TOLERANCE))
    lines.append("tri budget: <= %d per file (timbermesh_common.TRI_BUDGET_LOD0 = LOD0 tier "
                 "budget)" % meta["budget"])
    lines.append("roof:       ONE flat empty platform; walking surface == model apex y = %.2f; "
                 "nothing above it; %d cm clearance above the surface"
                 % (C.PLATFORM_TOP, int(round(C.PLATFORM_CLEAR_ABOVE * 100))))
    lines.append("")
    lines.append("-" * 104)
    lines.append("CHECKS (per file; all eight must PASS for the file to PASS)")
    for cid, _title in CHECK_DEFS:
        lines.append("  %-3s %s" % (cid, CHECK_DESCRIPTIONS[cid]))
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


def render_verdict(results, extras, meta):
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
    if failed:
        lines.append("FAILING FILES:")
        for result in results:
            if result.verdict != "PASS":
                bad = [cid for cid, _ in CHECK_DEFS if result.c(cid).status != "PASS"]
                lines.append("  %-50s failed: %s"
                             % (result.spec["stem"], ", ".join(bad) or "?"))
    overall = "PASS" if failed == 0 and present == len(results) else "FAIL"
    lines.append("OVERALL VERDICT: %s" % overall)
    if present < len(results):
        lines.append("  (reason: %d of the %d expected .timbermesh files are missing - "
                     "the v2 export is not complete)"
                     % (len(results) - present, len(results)))
    if meta.get("report_path"):
        lines.append("report: %s" % _rel(meta["report_path"]))
    return lines


def render_console(results, extras, probes, meta):
    lines = []
    lines.append("HDBF v2 3x3x1 timbermesh validation (deterministic report)")
    lines.append("mode: pure-python / Blender-free (vendored protobuf model_pb2 + zlib); "
                 "contract: art/timbermesh_common.py")
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
    lines.append("")
    lines.extend(render_verdict(results, extras, meta))
    for label, result in probes:
        lines.append("")
        lines.append("PARSER CROSS-CHECK (non-scoring): %s" % label)
        lines.extend(render_block(result, "  %s" % os.path.basename(result.path)))
    return lines


def build_report(results, extras, probes, meta):
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
    lines.extend(render_table(results))
    lines.append("")
    lines.extend(render_verdict(results, extras, meta))
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


def main(argv=None):
    args = parse_args(argv)
    specs = C.all_model_specs()
    results = []
    for spec in specs:
        analysis = Analysis(spec, expected=True)
        analysis.analyze()
        results.append(analysis)

    # any extra top-level .timbermesh (not in the contract) is reported but not scored
    extras = []
    known = set(os.path.basename(spec["path"]) for spec in specs)
    if os.path.isdir(C.TIMBERMESH_DIR):
        for name in sorted(os.listdir(C.TIMBERMESH_DIR)):
            path = os.path.join(C.TIMBERMESH_DIR, name)
            if name.endswith(".timbermesh") and os.path.isfile(path) and name not in known:
                analysis = Analysis(make_probe_spec(path), expected=False)
                analysis.analyze()
                extras.append(analysis)

    probes = []
    for path in args.probe:
        analysis = Analysis(make_probe_spec(path), expected=False)
        analysis.analyze()
        probes.append(("PROBE", analysis))
    if args.reference:
        analysis = Analysis(make_probe_spec(REFERENCE_PATH), expected=False)
        analysis.analyze()
        probes.append(("REFERENCE (vanilla SDK asset; contract checks expected to fail)", analysis))

    meta = {
        "python": "%s %s on %s" % (platform.python_implementation(),
                                   platform.python_version(), platform.system()),
        "workspace": WORKSPACE,
        "outdir": _rel(C.TIMBERMESH_DIR),
        "expected": len(specs),
        "present": sum(1 for result in results if result.present),
        "budget": C.TRI_BUDGET_LOD0,
        "report_path": None if args.no_report else os.path.abspath(args.report),
    }
    if not args.no_report:
        report = build_report(results, extras, probes, meta)
        with open(args.report, "w", encoding="utf-8", newline="\n") as handle:
            handle.write(report)
    _emit(render_console(results, extras, probes, meta))
    return 0 if all(result.verdict == "PASS" for result in results) else 1


if __name__ == "__main__":
    sys.exit(main())
