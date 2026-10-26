#!/usr/bin/env python3
"""HDBF (Timberborn High-Density Box Farm) art asset validator.

Validates every exported asset against the frozen art contract in
``art/atlas_layout.py`` and writes ``art/validation-report.txt``.

Modes
-----
FULL (default under Blender headless; ``bpy`` importable)
    GLB is parsed directly from the binary glTF container; FBX is re-imported
    into Blender and measured in authoring space.  Both representations are
    checked for bounds / triangle count / nodes / ports / materials / UV.

REDUCED GLB-only (plain ``python art/validate_models.py``, no ``bpy``)
    Only the GLB half of every check runs.  FBX re-import checks are reported
    as ``REDUCED`` (never silently skipped) and the verdict is labelled
    ``PASS (REDUCED)``.  A reduced run can never claim full validation.

Usage
-----
    blender.exe -b --factory-startup -noaudio --python art\\validate_models.py
    python art\\validate_models.py
    python art\\validate_models.py --root <workspace-root>   # self-test only

Exit code 0 only when every *executed* check is PASS.  Any FAIL or BLOCKED
check (missing/corrupt asset, unavailable data) exits with code 1.

Coordinate systems
------------------
asset space   = Unity Y-up, front -Z, pivot footprint bottom centre:
                X in [-1.5, 1.5], Y in [0, 1], Z in [-1, 1]
blender space = Z-up, front +Y: X in [-1.5, 1.5], Y in [-1, 1], Z in [0, 1]
                asset = (bx, bz, -by)   (atlas_layout.asset_from_blender)

The GLB container already stores the asset-space values.  FBX re-imported into
Blender restores authoring space, so its measurements are normalised through
the atlas_layout helpers before comparison.
"""

from __future__ import annotations

import base64
import json
import math
import os
import platform
import re
import struct
import sys
import traceback

HERE = os.path.dirname(os.path.abspath(__file__))
DEFAULT_ART = HERE
DEFAULT_ROOT = os.path.dirname(HERE)

# --------------------------------------------------------------------------- #
# optional numpy / bpy
# --------------------------------------------------------------------------- #
try:
    import numpy as np

    NUMPY_ERR = None
except Exception as exc:  # pragma: no cover - environment specific
    np = None
    NUMPY_ERR = "%s: %s" % (type(exc).__name__, exc)

try:
    import bpy  # noqa: F401  (Blender only)

    HAVE_BPY = True
    BLENDER_VERSION = bpy.app.version_string
except Exception:
    HAVE_BPY = False
    BLENDER_VERSION = None

# --------------------------------------------------------------------------- #
# contract import (art/atlas_layout.py is the single source of truth)
# --------------------------------------------------------------------------- #
if DEFAULT_ART not in sys.path:
    sys.path.insert(0, DEFAULT_ART)
try:
    import atlas_layout as C
except Exception:  # pragma: no cover
    C = None

# --------------------------------------------------------------------------- #
# thresholds (all documented in the report header)
# --------------------------------------------------------------------------- #
BOUNDS_TOL = 0.02          # asset-space envelope +-0.02 grid units
PIVOT_TOL = 0.02           # X/Z centre and min Y tolerance
UV_GRID = 512              # UV coverage raster resolution
UV_EPS = 1e-5              # "loops within [0,1]" tolerance
UV_DOUBLE_MAX = 0.002      # <=0.2% of covered texels may be double-covered
TOL_EPS = 1e-4             # float-noise epsilon; tolerances are inclusive
UV_PX_PER_UNIT = 64        # silhouette base raster density (then /2 = 50%)
SIL_DOWNSAMPLE = 2         # emulate 50% camera scale
SIL_IOU_MAX = 0.85         # area IoU fallback/one of the outline-rule disjuncts
OUTLINE_MEAN_MIN = 0.04    # outline rule: mean |dh(u)| in grid units (50% scale)
OUTLINE_MAX_MIN = 0.12     # outline rule: max |dh(u)| in grid units
PORT_PROXIMITY_RADIUS = 0.30  # heuristic for "recess geometry near port"
TRI_TOL = 0.20             # triangle budget +-20%
UV_DIAG_DOUBLE_MIN = 0.0001  # run UV epsilon diagnostics when double ratio > 0.01%

STATUS_RANK = {"PASS": 0, "INFO": 0, "WARN": 1, "REDUCED": 2,
               "BLOCKED": 3, "FAIL": 4}


# --------------------------------------------------------------------------- #
# contract-derived model list
# --------------------------------------------------------------------------- #
class ModelSpec(object):
    """One expected exported model (tier/faction/LOD or construction stage)."""

    def __init__(self, tier, faction=None, lod=None, construction=False):
        self.tier = tier
        self.faction = faction
        self.lod = lod
        self.construction = construction
        if construction:
            self.label = C.CONSTRUCTION_STAGE
            self.dir_rel = "art/models/%s" % C.CONSTRUCTION_STAGE
            self.stem = C.CONSTRUCTION_STAGE
            self.expected_body = "SM_HDBF_BoxFarm_ConstructionStage0"
            # Lead contract (2026-10-25): the shared greybox also ships six
            # trough nodes (no LOD suffix) and the same six port empties.
            # Node *presence* is checked, greybox coordinates are informational.
            self.expected_troughs = [
                "SM_HDBF_BoxFarm_ConstructionStage0_Trough_%02d" % n
                for n in range(1, 7)]
            self.budget = None
            self.materials = list(C.TIER_MATERIALS[C.CONSTRUCTION_STAGE])
            self.fbx_rel = self.dir_rel + "/" + self.stem + ".fbx"
            self.glb_rel = self.dir_rel + "/" + self.stem + ".glb"
        else:
            self.label = "%s.%s LOD%d" % (tier, faction, lod)
            self.dir_rel = "art/models/%s.%s" % (tier, faction)
            self.stem = "LOD%d" % lod
            self.expected_body = "SM_HDBF_BoxFarm_%s_LOD%d" % (tier, lod)
            self.expected_troughs = [
                "SM_HDBF_BoxFarm_%s_Trough_%02d_LOD%d" % (tier, n, lod)
                for n in range(1, 7)]
            self.budget = C.TRI_BUDGET[lod]
            self.materials = list(C.TIER_MATERIALS[tier])
            self.fbx_rel = self.dir_rel + "/" + self.stem + ".fbx"
            self.glb_rel = self.dir_rel + "/" + self.stem + ".glb"


def model_specs():
    specs = []
    for tier in C.TIERS:
        for faction in C.FACTIONS:
            for lod in C.LODS:
                specs.append(ModelSpec(tier, faction, lod))
    specs.append(ModelSpec(None, construction=True))
    return specs


def expected_files():
    files = []
    for spec in model_specs():
        files.append(spec.fbx_rel)
        files.append(spec.glb_rel)
    for key in sorted(C.ATLAS_FILES):
        files.append("art/" + C.ATLAS_FILES[key])
    files.append("art/material_slots.json")
    return files


# --------------------------------------------------------------------------- #
# small math helpers
# --------------------------------------------------------------------------- #
def _quat_to_mat3(q):
    x, y, z, w = q
    n = x * x + y * y + z * z + w * w
    if n <= 1e-20:
        return np.eye(3)
    s = 2.0 / n
    return np.array([
        [1.0 - s * (y * y + z * z), s * (x * y - w * z), s * (x * z + w * y)],
        [s * (x * y + w * z), 1.0 - s * (x * x + z * z), s * (y * z - w * x)],
        [s * (x * z - w * y), s * (y * z + w * x), 1.0 - s * (x * x + y * y)],
    ], dtype=np.float64)


def _mat4_local(node):
    if "matrix" in node and node["matrix"] is not None:
        m = np.array(node["matrix"], dtype=np.float64).reshape(4, 4).T
        return m
    t = node.get("translation", (0.0, 0.0, 0.0))
    r = node.get("rotation", (0.0, 0.0, 0.0, 1.0))
    s = node.get("scale", (1.0, 1.0, 1.0))
    m = np.eye(4, dtype=np.float64)
    rot = _quat_to_mat3(r)
    m[:3, :3] = rot * np.array(s, dtype=np.float64).reshape(1, 3)
    m[:3, 3] = np.array(t, dtype=np.float64)
    return m


def _rot3_of(m4):
    """Rotation part (columns normalised) of a world matrix + per-axis scale."""
    a = m4[:3, :3].astype(np.float64)
    scales = np.linalg.norm(a, axis=0)
    scales = np.where(scales < 1e-12, 1.0, scales)
    return a / scales.reshape(1, 3), scales


def _xform_points(m4, pts):
    return pts @ m4[:3, :3].T + m4[:3, 3]


def _asset_from_blender_point(p):
    return (float(p[0]), float(p[2]), -float(p[1]))


def _asset_from_blender_dir(p):
    return (float(p[0]), float(p[2]), -float(p[1]))


def _bbox6(points):
    lo = points.min(axis=0)
    hi = points.max(axis=0)
    return (float(lo[0]), float(lo[1]), float(lo[2]),
            float(hi[0]), float(hi[1]), float(hi[2]))


# --------------------------------------------------------------------------- #
# PNG header parsing (no PIL available anywhere)
# --------------------------------------------------------------------------- #
PNG_COLOR_TYPES = {0: "gray", 2: "RGB", 3: "palette", 4: "gray+alpha", 6: "RGBA"}


def read_png_header(path):
    with open(path, "rb") as f:
        head = f.read(33)
    if len(head) < 33 or head[:8] != b"\x89PNG\r\n\x1a\n":
        return {"error": "not a PNG (bad signature)"}
    length = struct.unpack(">I", head[8:12])[0]
    ctype = head[12:16]
    if ctype != b"IHDR":
        return {"error": "first chunk is %r, expected IHDR" % ctype}
    w, h = struct.unpack(">II", head[16:24])
    bit_depth = head[24]
    color_type = head[25]
    compression, filter_method, interlace = head[26], head[27], head[28]
    return {"width": w, "height": h, "bit_depth": bit_depth,
            "color_type": color_type, "color": PNG_COLOR_TYPES.get(color_type, "?"),
            "interlace": interlace, "compression": compression,
            "filter": filter_method, "ihdr_len": length}


# --------------------------------------------------------------------------- #
# GLB (binary glTF 2.0) parser
# --------------------------------------------------------------------------- #
class GlbError(Exception):
    pass


_GLB_MAGIC = 0x46546C67
_CHUNK_JSON = 0x4E4F534A
_CHUNK_BIN = 0x004E4942
_COMPONENT = {5120: np.int8, 5121: np.uint8, 5122: np.int16,
              5123: np.uint16, 5125: np.uint32, 5126: np.float32}
_NCOMP = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4,
          "MAT2": 4, "MAT3": 9, "MAT4": 16}


def load_glb(path):
    with open(path, "rb") as f:
        data = f.read()
    if len(data) < 12:
        raise GlbError("file too short (%d bytes)" % len(data))
    magic, version, length = struct.unpack_from("<III", data, 0)
    if magic != _GLB_MAGIC:
        raise GlbError("bad magic 0x%08X" % magic)
    if version != 2:
        raise GlbError("unsupported glTF version %d" % version)
    off = 12
    js = None
    bin_chunk = None
    while off + 8 <= len(data):
        clen, ctype = struct.unpack_from("<II", data, off)
        off += 8
        chunk = data[off: off + clen]
        off += clen
        if ctype == _CHUNK_JSON and js is None:
            js = json.loads(chunk.decode("utf-8").rstrip("\x00 \t\r\n"))
        elif ctype == _CHUNK_BIN and bin_chunk is None:
            bin_chunk = chunk
    if js is None:
        raise GlbError("no JSON chunk")
    buffers = []
    for i, b in enumerate(js.get("buffers", [])):
        uri = b.get("uri")
        if uri is None:
            if bin_chunk is None:
                raise GlbError("buffer %d has no uri and no BIN chunk" % i)
            buffers.append(bin_chunk)
        elif uri.startswith("data:"):
            buffers.append(base64.b64decode(uri.split(",", 1)[1]))
        else:
            rel = os.path.join(os.path.dirname(path), uri.replace("\\", "/"))
            if not os.path.isfile(rel):
                raise GlbError("external buffer missing: %r" % uri)
            with open(rel, "rb") as bf:
                buffers.append(bf.read())
    return js, buffers


def read_accessor(js, buffers, index):
    acc = js["accessors"][index]
    if "bufferView" not in acc:
        raise GlbError("accessor %d has no bufferView (sparse accessors unsupported)"
                       % index)
    bv = js["bufferViews"][acc["bufferView"]]
    ctype = acc["componentType"]
    if ctype not in _COMPONENT:
        raise GlbError("accessor %d unknown componentType %d" % (index, ctype))
    dtype = np.dtype(_COMPONENT[ctype])
    ncomp = _NCOMP[acc["type"]]
    count = int(acc["count"])
    item = dtype.itemsize * ncomp
    stride = int(bv.get("byteStride") or item)
    start = int(bv.get("byteOffset", 0)) + int(acc.get("byteOffset", 0))
    buf = buffers[bv["buffer"]]
    if stride == item:
        arr = np.frombuffer(buf, dtype=dtype, count=count * ncomp, offset=start)
        arr = arr.reshape(count, ncomp)
    else:
        raw = np.empty((count, ncomp), dtype=dtype)
        for i in range(count):
            raw[i] = np.frombuffer(buf, dtype=dtype, count=ncomp,
                                   offset=start + i * stride)
        arr = raw
    if dtype.kind == "f":
        out = arr.astype(np.float64)
    else:
        out = arr.astype(np.int64)
        if acc.get("normalized"):
            info = np.iinfo(dtype)
            out = out.astype(np.float64) / (float(info.max) if dtype.kind == "u"
                                            else float(-info.min))
    return out


class GlbModel(object):
    """Parsed GLB with node world transforms and per-mesh-node geometry."""

    def __init__(self, path, spec):
        self.path = path
        self.spec = spec
        self.js, self.buffers = load_glb(path)
        nodes = self.js.get("nodes", [])
        self.nodes = nodes
        self.node_order = self._traversal_order(nodes)
        self.world = self._world_matrices(nodes, self.node_order)
        self.mesh_nodes = []
        self.warnings = []
        self._triangles_cache = None
        self._build_mesh_nodes()

    # -- structure -------------------------------------------------------- #
    def _traversal_order(self, nodes):
        children_of = {}
        parent = {}
        for i, node in enumerate(nodes):
            for c in node.get("children", []) or []:
                children_of.setdefault(i, []).append(c)
                parent[c] = i
        roots = []
        for scene in self.js.get("scenes", []) or []:
            roots.extend(scene.get("nodes", []) or [])
        seen = set()
        order = []

        def visit(i):
            if i in seen or i < 0 or i >= len(nodes):
                return
            seen.add(i)
            order.append(i)
            for c in children_of.get(i, []):
                visit(c)

        for r in roots:
            visit(r)
        for i in range(len(nodes)):
            if i not in seen and i not in parent:
                visit(i)
        for i in range(len(nodes)):
            if i not in seen:  # orphan / cycle
                self.warnings.append("node %d unreachable, local transform only" % i)
                visit(i)
        return order

    def _world_matrices(self, nodes, order):
        world = {}

        def walk(i, parent_m):
            local = _mat4_local(nodes[i])
            m = parent_m @ local
            world[i] = m
            for c in nodes[i].get("children", []) or []:
                if c not in world:
                    walk(c, m)

        root_m = np.eye(4, dtype=np.float64)
        for i in order:
            if i in world:
                continue
            # only walk roots (no parent) with identity, children handled above
            parents = set()
            for node in nodes:
                parents.update(node.get("children", []) or [])
            if i in parents:
                continue
            walk(i, root_m)
        for i in order:
            if i not in world:
                walk(i, root_m)
        return world

    def _build_mesh_nodes(self):
        meshes = self.js.get("meshes", []) or []
        for idx in self.node_order:
            node = self.nodes[idx]
            if "mesh" not in node:
                continue
            mesh = meshes[node["mesh"]]
            prims = mesh.get("primitives", []) or []
            if not prims:
                self.warnings.append("node %r mesh has no primitives"
                                     % node.get("name"))
            pos_parts = []
            tri_parts = []
            uv_parts = []
            mat_ids = []
            tri_count = 0
            uv_ok = True
            mat_per_prim = []
            vert_base = 0
            for prim in prims:
                mode = int(prim.get("mode", 4))
                if mode != 4:
                    raise GlbError("node %r primitive mode %d (only TRIANGLES=4 "
                                   "supported)" % (node.get("name"), mode))
                attrs = prim.get("attributes", {}) or {}
                if "POSITION" not in attrs:
                    raise GlbError("node %r primitive without POSITION"
                                   % node.get("name"))
                pos = read_accessor(self.js, self.buffers, attrs["POSITION"])
                if "indices" in prim:
                    idxs = read_accessor(self.js, self.buffers,
                                         prim["indices"]).reshape(-1)
                    idxs = idxs.astype(np.int64)
                else:
                    idxs = np.arange(pos.shape[0], dtype=np.int64)
                if idxs.size % 3 != 0:
                    raise GlbError("node %r index count %d not divisible by 3"
                                   % (node.get("name"), idxs.size))
                # primitive indices are local to that primitive's POSITION
                # accessor; offset them into the concatenated vertex array
                tris = idxs.reshape(-1, 3) + vert_base
                vert_base += pos.shape[0]
                if attrs.get("TEXCOORD_0") is not None:
                    uv = read_accessor(self.js, self.buffers,
                                       attrs["TEXCOORD_0"]).reshape(-1, 2)
                else:
                    uv = None
                    uv_ok = False
                mat = prim.get("material")
                pos_parts.append(pos)
                tri_parts.append(tris)
                uv_parts.append(uv)
                mat_ids.append(np.full(tris.shape[0], -1 if mat is None else mat,
                                       dtype=np.int64))
                tri_count += tris.shape[0]
                mat_per_prim.append((mat, tris.shape[0]))
            pos_all = np.vstack(pos_parts) if pos_parts else np.zeros((0, 3))
            tri_all = np.vstack(tri_parts) if tri_parts else np.zeros((0, 3), np.int64)
            uv_all = None
            if uv_ok and all(u is not None for u in uv_parts):
                uv_all = np.vstack(uv_parts)
            m4 = self.world[idx]
            pos_asset = _xform_points(m4, pos_all) if pos_all.size else pos_all
            self.mesh_nodes.append({
                "index": idx,
                "name": node.get("name") or "",
                "pos": pos_asset,          # asset-space world vertices
                "tris": tri_all,           # local indices into pos_asset
                "uv": uv_all,
                "uv_missing": not uv_ok,
                "tri_count": tri_count,
                "mat_ids": (np.concatenate(mat_ids) if mat_ids
                            else np.zeros(0, np.int64)),
                "prim_materials": mat_per_prim,
                "world": m4,
            })

    # -- convenience ------------------------------------------------------ #
    def all_positions(self):
        if not self.mesh_nodes:
            raise GlbError("no mesh nodes")
        return np.vstack([n["pos"] for n in self.mesh_nodes if n["pos"].size])

    def bounds(self):
        return _bbox6(self.all_positions())

    def triangles_asset(self):
        if self._triangles_cache is None:
            parts = []
            for n in self.mesh_nodes:
                if n["tris"].size:
                    parts.append(n["pos"][n["tris"]])
            if not parts:
                raise GlbError("no triangles")
            self._triangles_cache = np.vstack(parts)
        return self._triangles_cache

    def tri_total(self):
        return int(sum(n["tri_count"] for n in self.mesh_nodes))

    def tri_per_node(self):
        return [(n["name"], int(n["tri_count"])) for n in self.mesh_nodes]

    def node_names(self):
        return [self.nodes[i].get("name") or "" for i in range(len(self.nodes))]

    def material_names(self):
        out = []
        for mat in self.js.get("materials", []) or []:
            out.append(mat.get("name") or "")
        return out

    def used_materials(self):
        """Ordered unique material names actually referenced by primitives."""
        names = self.material_names()
        used = []
        for n in self.mesh_nodes:
            for mat_idx, _cnt in n["prim_materials"]:
                if mat_idx is None:
                    continue
                if mat_idx < len(names) and names[mat_idx] not in used:
                    used.append(names[mat_idx])
        return used

    def material_tri_counts(self):
        names = self.material_names()
        counts = {}
        unknown = 0
        for n in self.mesh_nodes:
            for mat_idx, cnt in n["prim_materials"]:
                key = ("#%d" % mat_idx) if mat_idx is None else names[mat_idx]
                counts[key] = counts.get(key, 0) + cnt
                if mat_idx is None:
                    unknown += cnt
        return counts, unknown

    def texture_refs(self):
        refs = []
        js = self.js
        materials = js.get("materials", []) or []
        textures = js.get("textures", []) or []
        images = js.get("images", []) or []
        tex_keys = ("baseColorTexture", "metallicRoughnessTexture",
                    "normalTexture", "occlusionTexture", "emissiveTexture")
        for mi, mat in enumerate(materials):
            name = mat.get("name") or "#%d" % mi
            pbr = mat.get("pbrMetallicRoughness", {}) or {}
            entries = []
            for key in tex_keys:
                info = pbr.get(key) if key in pbr else mat.get(key)
                if isinstance(info, dict) and "index" in info:
                    entries.append((key, int(info["index"])))
            for key, ti in entries:
                if ti >= len(textures):
                    refs.append("%s:%s -> texture index %d OUT OF RANGE" % (name, key, ti))
                    continue
                src = textures[ti].get("source")
                if src is None:
                    refs.append("%s:%s -> texture %d (no source)" % (name, key, ti))
                elif src < len(images):
                    img = images[src]
                    if img.get("uri"):
                        refs.append("%s:%s -> %s" % (name, key, img["uri"]))
                    else:
                        refs.append("%s:%s -> embedded bufferView %s"
                                    % (name, key, img.get("bufferView")))
                else:
                    refs.append("%s:%s -> image index %d OUT OF RANGE" % (name, key, src))
        return refs


# --------------------------------------------------------------------------- #
# FBX re-import (Blender full mode only)
# --------------------------------------------------------------------------- #
class FbxModel(object):
    """FBX re-imported into Blender; every measurement normalised to asset space."""

    def __init__(self, path, spec):
        if not HAVE_BPY:
            raise RuntimeError("bpy unavailable")
        self.path = path
        self.spec = spec
        self.warnings = []
        self.mesh_nodes = []
        self.empties = {}
        self._triangles_cache = None
        self._load()

    def _load(self):
        bpy.ops.wm.read_factory_settings(use_empty=True)
        try:
            bpy.ops.import_scene.fbx(filepath=self.path)
        except Exception as exc:
            raise RuntimeError("FBX import failed: %s: %s"
                               % (type(exc).__name__, exc))
        dg = bpy.context.evaluated_depsgraph_get()
        objects = sorted(bpy.data.objects, key=lambda o: o.name)
        for ob in objects:
            if ob.type == "MESH":
                ev = ob.evaluated_get(dg)
                me = ev.to_mesh()
                try:
                    me.calc_loop_triangles()
                    mw = ev.matrix_world
                    m4 = np.array(mw, dtype=np.float64)
                    nv = len(me.vertices)
                    co = np.empty(nv * 3, dtype=np.float64)
                    me.vertices.foreach_get("co", co)
                    pos_bl = co.reshape(-1, 3)
                    pos_asset = np.empty_like(pos_bl)
                    pos_asset[:, 0] = pos_bl[:, 0]
                    pos_asset[:, 1] = pos_bl[:, 2]
                    pos_asset[:, 2] = -pos_bl[:, 1]
                    # world transform in blender space, then normalise
                    pos_world_bl = _xform_points(m4, pos_bl)
                    pos_world_asset = np.empty_like(pos_world_bl)
                    pos_world_asset[:, 0] = pos_world_bl[:, 0]
                    pos_world_asset[:, 1] = pos_world_bl[:, 2]
                    pos_world_asset[:, 2] = -pos_world_bl[:, 1]
                    lt = me.loop_triangles
                    ntri = len(lt)
                    loops = np.empty(ntri * 3, dtype=np.int64)
                    lt.foreach_get("loops", loops)
                    loops = loops.reshape(-1, 3)
                    uv_layer = me.uv_layers.active
                    if uv_layer is not None and ntri:
                        uv_raw = np.empty(len(me.loops) * 2, dtype=np.float64)
                        uv_layer.data.foreach_get("uv", uv_raw)
                        uv_loops = uv_raw.reshape(-1, 2)
                        tri_uv = uv_loops[loops]
                    else:
                        tri_uv = None
                    mats = []
                    for slot in me.materials:
                        mats.append(slot.name if slot is not None else None)
                    poly_mats = np.empty(len(me.polygons), dtype=np.int64)
                    me.polygons.foreach_get("material_index", poly_mats)
                    # expand per-polygon material index to per-triangle
                    poly_of_tri = np.empty(ntri, dtype=np.int64)
                    lt.foreach_get("polygon_index", poly_of_tri)
                    tri_mat = (poly_mats[poly_of_tri] if ntri
                               else np.zeros(0, np.int64))
                    origin_asset = np.array(_asset_from_blender_point(m4[:3, 3]))
                    self.mesh_nodes.append({
                        "name": ob.name,
                        "origin": origin_asset,
                        "pos": pos_world_asset,
                        "tris": loops,
                        "uv": tri_uv,
                        "uv_missing": tri_uv is None,
                        "tri_count": ntri,
                        "material_slots": mats,
                        "tri_mat": tri_mat,
                    })
                finally:
                    ev.to_mesh_clear()
            elif ob.type == "EMPTY":
                m4 = np.array(ob.matrix_world, dtype=np.float64)
                rot3, scales = _rot3_of(m4)
                loc_bl = m4[:3, 3]
                zdir_bl = rot3 @ np.array([0.0, 0.0, 1.0])
                self.empties[ob.name] = {
                    "pos": np.array(_asset_from_blender_point(loc_bl)),
                    "dir": np.array(_asset_from_blender_dir(zdir_bl)),
                    "scales": scales,
                }
        if not self.mesh_nodes:
            raise RuntimeError("FBX contains no mesh objects")

    # -- convenience ------------------------------------------------------ #
    def bounds(self):
        return _bbox6(np.vstack([n["pos"] for n in self.mesh_nodes
                                 if n["pos"].size]))

    def tri_total(self):
        return int(sum(n["tri_count"] for n in self.mesh_nodes))

    def tri_per_node(self):
        return [(n["name"], int(n["tri_count"])) for n in self.mesh_nodes]

    def node_names(self):
        out = [n["name"] for n in self.mesh_nodes]
        out.extend(self.empties.keys())
        return out

    def material_names(self):
        """Ordered unique slot names: body first if present, then others sorted."""
        body = self.spec.expected_body
        ordered = []
        rest = []
        for n in self.mesh_nodes:
            if n["name"] == body:
                ordered.append(n)
            else:
                rest.append(n)
        out = []
        for n in ordered + sorted(rest, key=lambda x: x["name"]):
            for name in n.get("material_slots", []):
                if name is not None and name not in out:
                    out.append(name)
        return out

    def texture_refs(self):
        refs = []
        if not HAVE_BPY:
            return refs
        for mat in sorted(bpy.data.materials, key=lambda m: m.name):
            if not mat.use_nodes or mat.node_tree is None:
                continue
            for node in mat.node_tree.nodes:
                if node.type == "TEX_IMAGE" and node.image is not None:
                    refs.append("%s -> %s" % (mat.name,
                                              node.image.filepath or node.image.name))
        return refs

    def triangles_asset(self):
        if self._triangles_cache is None:
            parts = []
            for n in self.mesh_nodes:
                if n["tris"].size:
                    parts.append(n["pos"][n["tris"]])
            self._triangles_cache = np.vstack(parts)
        return self._triangles_cache


# --------------------------------------------------------------------------- #
# rasterisation helpers
# --------------------------------------------------------------------------- #
def rasterize_triangles_2d(tris2d, width=None, height=None, inset_px=0.0):
    """Rasterise (F,3,2) pixel-space triangles; returns uint16 coverage grid.

    Pixel centres are at (i+0.5, j+0.5); the strict inside test (all three
    edge functions > 0) means a texel centre lying exactly on a shared edge is
    claimed by neither triangle, so shared edges do not double-count.  Explicit
    width/height keep grids comparable across models (silhouette views);
    geometry outside the grid is clipped.  ``inset_px`` shrinks every triangle by
    that many pixels (edge threshold = inset * edge length), which is the
    epsilon test used to classify UV double coverage as raster edge-rounding
    rather than a real island overlap.
    """
    if tris2d is None or tris2d.size == 0:
        return np.zeros((height or 1, width or 1), dtype=np.uint16)
    if width is None:
        width = max(1, int(math.ceil(float(tris2d[:, :, 0].max()))))
    if height is None:
        height = max(1, int(math.ceil(float(tris2d[:, :, 1].max()))))
    counts = np.zeros((height, width), dtype=np.uint16)
    for f in range(tris2d.shape[0]):
        a, b, c = tris2d[f, 0], tris2d[f, 1], tris2d[f, 2]
        area = (b[0] - a[0]) * (c[1] - a[1]) - (b[1] - a[1]) * (c[0] - a[0])
        if area == 0.0:
            continue
        if area < 0.0:
            b, c = c, b
        minx = max(0, int(math.floor(min(a[0], b[0], c[0]))))
        maxx = min(width - 1, int(math.ceil(max(a[0], b[0], c[0]))))
        miny = max(0, int(math.floor(min(a[1], b[1], c[1]))))
        maxy = min(height - 1, int(math.ceil(max(a[1], b[1], c[1]))))
        if maxx < minx or maxy < miny:
            continue
        xs = np.arange(minx, maxx + 1, dtype=np.float64) + 0.5
        ys = np.arange(miny, maxy + 1, dtype=np.float64) + 0.5
        X = xs.reshape(1, -1)
        Y = ys.reshape(-1, 1)
        e0 = (b[0] - a[0]) * (Y - a[1]) - (b[1] - a[1]) * (X - a[0])
        e1 = (c[0] - b[0]) * (Y - b[1]) - (c[1] - b[1]) * (X - b[0])
        e2 = (a[0] - c[0]) * (Y - c[1]) - (a[1] - c[1]) * (X - c[0])
        if inset_px:
            t0 = inset_px * math.hypot(b[0] - a[0], b[1] - a[1])
            t1 = inset_px * math.hypot(c[0] - b[0], c[1] - b[1])
            t2 = inset_px * math.hypot(a[0] - c[0], a[1] - c[1])
            inside = (e0 > t0) & (e1 > t1) & (e2 > t2)
        else:
            inside = (e0 > 0.0) & (e1 > 0.0) & (e2 > 0.0)
        if inside.any():
            counts[miny:maxy + 1, minx:maxx + 1] += inside.astype(np.uint16)
    return counts


def check_uv(uv, grid=UV_GRID, groups=None):
    """UV range + intra-model overlap statistics, with an epsilon diagnostic.

    ``groups`` optionally gives one integer group id per triangle (one id per
    mesh object); when present, double coverage between different objects is
    counted separately because the packer guarantees unique islands per model.

    The diagnostic answers the question "is double coverage raster edge
    rounding or a real overlap?" by re-rasterising (a) at twice the resolution
    and (b) with a 0.5 px triangle inset.  Rounding/adjacency artefacts shrink
    by at least ~40% under either test; real island overlap persists.
    """
    if uv is None or uv.size == 0:
        return {"error": "no UV layer / no UV data"}
    flat = uv.reshape(-1, 2)
    lo = flat.min(axis=0)
    hi = flat.max(axis=0)
    max_dev = float(max(max(0.0, -lo[0]), max(0.0, -lo[1]),
                        max(0.0, hi[0] - 1.0), max(0.0, hi[1] - 1.0)))
    oob_mask = ((flat < -UV_EPS) | (flat > 1.0 + UV_EPS))
    oob_loops = int(np.count_nonzero(oob_mask.any(axis=1)))
    tri_px = uv * float(grid)
    tri_px = np.clip(tri_px, -1.0, float(grid) + 1.0)
    counts = rasterize_triangles_2d(tri_px, grid, grid)
    covered = int(np.count_nonzero(counts > 0))
    double = int(np.count_nonzero(counts > 1))
    total = grid * grid
    stats = {
        "oob_loops": oob_loops,
        "max_dev": max_dev,
        "max_u": float(hi[0]),
        "max_v": float(hi[1]),
        "min_u": float(lo[0]),
        "min_v": float(lo[1]),
        "covered": covered,
        "double": double,
        "covered_ratio": covered / float(total),
        "double_ratio": (double / float(covered)) if covered else 0.0,
        "diag": None,
    }
    if not double or (double / float(covered) if covered else 0.0) <= UV_DIAG_DOUBLE_MIN:
        return stats
    # --- epsilon diagnostics -------------------------------------------------
    hi_grid = grid * 2
    tri_px_hi = np.clip(uv * float(hi_grid), -1.0, float(hi_grid) + 1.0)
    counts_hi = rasterize_triangles_2d(tri_px_hi, hi_grid, hi_grid)
    covered_hi = int(np.count_nonzero(counts_hi > 0))
    double_hi = int(np.count_nonzero(counts_hi > 1))
    counts_ins = rasterize_triangles_2d(tri_px, grid, grid, inset_px=0.5)
    covered_ins = int(np.count_nonzero(counts_ins > 0))
    double_ins = int(np.count_nonzero(counts_ins > 1))
    ratio = stats["double_ratio"]
    ratio_hi = (double_hi / float(covered_hi)) if covered_hi else 0.0
    ratio_ins = (double_ins / float(covered_ins)) if covered_ins else 0.0
    cross_object = 0
    group_bbox_pairs = []
    if groups is not None:
        uniq = np.unique(groups)
        group_masks = []
        boxes = []
        for g in uniq:
            sel = groups == g
            m = rasterize_triangles_2d(tri_px[sel], grid, grid) > 0
            group_masks.append(m)
            if sel.any():
                pts = tri_px[sel]
                boxes.append((g, float(pts[:, :, 0].min()),
                              float(pts[:, :, 1].min()),
                              float(pts[:, :, 0].max()),
                              float(pts[:, :, 1].max())))
        if len(group_masks) > 1:
            cross_object = int(np.count_nonzero(
                np.sum(np.stack(group_masks), axis=0) > 1))
        for i in range(len(boxes)):
            for j in range(i + 1, len(boxes)):
                g1, ax0, ay0, ax1, ay1 = boxes[i]
                g2, bx0, by0, bx1, by1 = boxes[j]
                if ax0 <= bx1 and bx0 <= ax1 and ay0 <= by1 and by0 <= ay1:
                    group_bbox_pairs.append((int(g1), int(g2)))
    rounding = (ratio_ins <= 0.25 * ratio) or (ratio_hi <= 0.60 * ratio)
    stats["diag"] = {
        "hi_grid": hi_grid,
        "double_hi": double_hi,
        "ratio_hi": ratio_hi,
        "double_inset": double_ins,
        "ratio_inset": ratio_ins,
        "cross_object": cross_object,
        "group_bbox_pairs": group_bbox_pairs[:6],
        "rounding": bool(rounding),
    }
    return stats


def downsample_binary(grid, factor=SIL_DOWNSAMPLE):
    h, w = grid.shape
    h2, w2 = h // factor, w // factor
    if h2 == 0 or w2 == 0:
        return np.zeros((max(1, h2), max(1, w2)), dtype=bool)
    return grid[:h2 * factor, :w2 * factor].reshape(
        h2, factor, w2, factor).any(axis=(1, 3))


def silhouette_views(tris_asset):
    """Rasterise front/side/top silhouettes at 50% scale (base 64 px/unit /2)."""
    scale = UV_PX_PER_UNIT
    views = {}
    # (name, horizontal axis, vertical axis, origin_h, origin_v, extent_h, extent_v)
    defs = [
        ("front", 0, 1, -1.5, 0.0, 3.0, 1.0),
        ("side", 2, 1, -1.0, 0.0, 2.0, 1.0),
        ("top", 0, 2, -1.5, -1.0, 3.0, 2.0),
    ]
    for name, ah, av, oh, ov, eh, ev in defs:
        w = int(round(eh * scale))
        h = int(round(ev * scale))
        pts = np.empty((tris_asset.shape[0], 3, 2), dtype=np.float64)
        pts[:, :, 0] = (tris_asset[:, :, ah] - oh) * scale
        pts[:, :, 1] = (tris_asset[:, :, av] - ov) * scale
        # fixed grid per view (contract extents) so tiers are comparable even if
        # a model slightly exceeds the envelope
        big = rasterize_triangles_2d(pts, w, h) > 0
        small = downsample_binary(big)
        views[name] = {
            "grid": small,
            "grid_full": big,
            "area": int(np.count_nonzero(small)),
            "pixels": int(small.size),
            "fill": float(np.count_nonzero(small)) / float(max(1, small.size)),
            "raw_pixels": int(big.size),
        }
    return views


def outline_profiles(views):
    """Outline top-edge height profile per view at 50% camera scale.

    h(u) = highest occupied row in column u, in grid units:
      * front / side: the skyline height y above the footprint base,
      * top: the highest depth z reached in that x column.
    Empty columns report 0.  Profiles are sampled at the same mask width for
    every model, so mean/max |dh(u)| compares outlines, not filled area.
    """
    px_per_unit = float(UV_PX_PER_UNIT) / float(SIL_DOWNSAMPLE)
    out = {}
    for name, view in views.items():
        grid = view["grid"]
        height, _width = grid.shape
        rows = np.where(grid, np.arange(height).reshape(-1, 1), -1)
        out[name] = (rows.max(axis=0) + 1).astype(np.float64) / px_per_unit
    return out


def iou(a, b):
    inter = int(np.count_nonzero(a & b))
    uni = int(np.count_nonzero(a | b))
    if uni == 0:
        return 0.0, inter, uni
    return inter / float(uni), inter, uni


# --------------------------------------------------------------------------- #
# checks / results
# --------------------------------------------------------------------------- #
class Check(object):
    def __init__(self, cid, title):
        self.cid = cid
        self.title = title
        self.entries = []

    def add(self, status, text):
        self.entries.append((status, text))

    def worst(self):
        ranks = [STATUS_RANK.get(s, 0) for s, _ in self.entries]
        if not ranks:
            return "BLOCKED"
        top = max(ranks)
        if top >= STATUS_RANK["FAIL"]:
            return "FAIL"
        if top >= STATUS_RANK["BLOCKED"]:
            return "BLOCKED"
        if top >= STATUS_RANK["REDUCED"]:
            return "PASS*"
        if top >= STATUS_RANK["WARN"]:
            return "PASS"       # WARN/INFO are notes, not failures
        return "PASS"

    def count(self, status):
        return sum(1 for s, _ in self.entries if s == status)


def _fmt6(b):
    if b is None:
        return "n/a"
    return "X[%+.3f,%+.3f] Y[%+.3f,%+.3f] Z[%+.3f,%+.3f]" % (
        b[0], b[3], b[1], b[4], b[2], b[5])


def _errs_or_ok(errs, ok_text):
    return errs if errs else [ok_text]


# --------------------------------------------------------------------------- #
# main validator
# --------------------------------------------------------------------------- #
class Validator(object):
    def __init__(self, root):
        self.root = root
        self.art = os.path.join(root, "art")
        self.report_path = os.path.join(self.art, "validation-report.txt")
        self.full = HAVE_BPY
        self.specs = model_specs()
        self.files = expected_files()
        self.loaded = {}          # label -> {"glb": GlbModel|None, ...}
        self.evidence = {}        # label -> {"glb": dict, "fbx": dict}
        self.checks = {}
        for cid, title in [
            ("C1", "File set complete"),
            ("C2", "Asset-space bounds + pivot (GLB+FBX)"),
            ("C3", "Triangles per LOD vs budget"),
            ("C4", "Trough nodes (6/LOD) + LOD0 centres"),
            ("C5", "Port empties: coords + local +Z outward"),
            ("C6", "Materials + atlas PNGs"),
            ("C7", "UV range + intra-model overlap"),
            ("C8", "LOD bbox continuity (shrink <=2%)"),
            ("C9", "Cross-tier silhouette distinguishability (50%)"),
            ("C10", "material_slots.json cross-check"),
        ]:
            self.checks[cid] = Check(cid, title)
        self._uv_cache = {}

    # -- path helpers ----------------------------------------------------- #
    def abspath(self, rel):
        return os.path.join(self.root, rel.replace("/", os.sep))

    # -- loading ---------------------------------------------------------- #
    def load_all(self):
        for spec in self.specs:
            rec = {"glb": None, "glb_err": None, "fbx": None, "fbx_err": None}
            glb_path = self.abspath(spec.glb_rel)
            if os.path.isfile(glb_path):
                if np is None:
                    rec["glb_err"] = "numpy unavailable: %s" % NUMPY_ERR
                else:
                    try:
                        rec["glb"] = GlbModel(glb_path, spec)
                    except Exception as exc:
                        rec["glb_err"] = "%s: %s" % (type(exc).__name__, exc)
            else:
                rec["glb_err"] = "missing file"
            fbx_path = self.abspath(spec.fbx_rel)
            if not self.full:
                rec["fbx_err"] = "REDUCED"
            elif not os.path.isfile(fbx_path):
                rec["fbx_err"] = "missing file"
            else:
                try:
                    rec["fbx"] = FbxModel(fbx_path, spec)
                except Exception as exc:
                    rec["fbx_err"] = "%s: %s" % (type(exc).__name__, exc)
            self.loaded[spec.label] = rec
            self.evidence[spec.label] = {"glb": {}, "fbx": {}}
        # ensure evidence slots for construction stage too (in specs already)

    def source_models(self, spec):
        rec = self.loaded[spec.label]
        return rec

    def src_error_entries(self, check, spec, source, err):
        if err == "REDUCED":
            check.add("REDUCED", "%s %s: FBX re-import NOT EXECUTED "
                                 "(reduced GLB-only mode, bpy unavailable)"
                      % (spec.label, source))
        elif err == "missing file":
            check.add("BLOCKED", "%s %s: file missing -> check NOT EXECUTED"
                      % (spec.label, source))
        else:
            check.add("BLOCKED", "%s %s: cannot read (%s) -> check NOT EXECUTED"
                      % (spec.label, source, err))

    # ------------------------------------------------------------------ #
    # C1 file set
    # ------------------------------------------------------------------ #
    def check_c1(self):
        chk = self.checks["C1"]
        missing, empty, present = [], [], []
        for rel in self.files:
            p = self.abspath(rel)
            if not os.path.isfile(p):
                missing.append(rel)
            elif os.path.getsize(p) == 0:
                empty.append(rel)
            else:
                present.append(rel)
        n = len(self.files)
        if missing:
            chk.add("FAIL", "%d/%d expected files present; missing %d: %s"
                    % (len(present), n, len(missing), ", ".join(missing)))
        if empty:
            chk.add("FAIL", "%d zero-byte files: %s" % (len(empty), ", ".join(empty)))
        if not missing and not empty:
            chk.add("PASS", "%d/%d expected files present and non-empty"
                    % (len(present), n))
        # extras are informational only
        models_dir = os.path.join(self.art, "models")
        extra = []
        if os.path.isdir(models_dir):
            expected_abs = {self.abspath(r) for r in self.files}
            for dirpath, _dirnames, filenames in os.walk(models_dir):
                for fn in sorted(filenames):
                    full = os.path.join(dirpath, fn)
                    if full not in expected_abs:
                        extra.append(os.path.relpath(full, self.root))
        if extra:
            chk.add("WARN", "unexpected extra files under art/models: %s"
                    % ", ".join(sorted(extra)))
        return chk

    # ------------------------------------------------------------------ #
    # C2 bounds + pivot
    # ------------------------------------------------------------------ #
    def _check_bounds_values(self, spec, label, bounds):
        errs = []
        if bounds is None:
            return ["no geometry"]
        xmin, ymin, zmin, xmax, ymax, zmax = bounds
        ex, ey, ez = xmax - xmin, ymax - ymin, zmax - zmin
        cx, cz = (xmin + xmax) / 2.0, (zmin + zmax) / 2.0
        def chk(name, val, exp, tol):
            if abs(val - exp) > tol + TOL_EPS:
                errs.append("%s=%+.4f expected %+.4f (err %+.4f > %.3f)"
                            % (name, val, exp, val - exp, tol))
        chk("extentX", ex, 3.0, BOUNDS_TOL)
        chk("extentY(height)", ey, 1.0, BOUNDS_TOL)
        chk("extentZ(depth)", ez, 2.0, BOUNDS_TOL)
        chk("centreX", cx, 0.0, PIVOT_TOL)
        chk("centreZ", cz, 0.0, PIVOT_TOL)
        chk("minY(pivot)", ymin, 0.0, PIVOT_TOL)
        return errs

    def check_c2(self):
        chk = self.checks["C2"]
        for spec in self.specs:
            rec = self.loaded[spec.label]
            for source in ("glb", "fbx"):
                ev = self.evidence[spec.label][source]
                model = rec[source]
                err = rec[source + "_err"]
                if model is None:
                    ev["bounds"] = None
                    ev["bounds_status"] = "n/a"
                    self.src_error_entries(chk, spec, source.upper(), err)
                    continue
                try:
                    bounds = model.bounds()
                except Exception as exc:
                    chk.add("FAIL", "%s %s: bounds computation failed: %s: %s"
                            % (spec.label, source.upper(), type(exc).__name__, exc))
                    ev["bounds_status"] = "ERR"
                    continue
                ev["bounds"] = bounds
                errs = self._check_bounds_values(spec, spec.label, bounds)
                if errs:
                    ev["bounds_status"] = "FAIL"
                    chk.add("FAIL", "%s %s: %s | %s"
                            % (spec.label, source.upper(), "; ".join(errs),
                               _fmt6(bounds)))
                else:
                    ev["bounds_status"] = "ok"
        if chk.count("FAIL") == 0 and chk.count("BLOCKED") == 0:
            n = sum(1 for s in self.specs for src in ("glb", "fbx")
                    if self.evidence[s.label][src].get("bounds_status") == "ok")
            chk.add("PASS", "%d/%d model/source bounds+ pivot checks passed "
                            "(%d models x up to 2 sources)"
                    % (n, len(self.specs) * (2 if self.full else 1), len(self.specs)))
        return chk

    # ------------------------------------------------------------------ #
    # C3 triangles
    # ------------------------------------------------------------------ #
    def check_c3(self):
        chk = self.checks["C3"]
        for spec in self.specs:
            rec = self.loaded[spec.label]
            for source in ("glb", "fbx"):
                ev = self.evidence[spec.label][source]
                model = rec[source]
                if model is None:
                    ev["tri_total"] = None
                    self.src_error_entries(chk, spec, source.upper(),
                                           rec[source + "_err"])
                    continue
                try:
                    total = model.tri_total()
                    per_node = model.tri_per_node()
                except Exception as exc:
                    chk.add("FAIL", "%s %s: triangle count failed: %s: %s"
                            % (spec.label, source.upper(), type(exc).__name__, exc))
                    continue
                ev["tri_total"] = total
                ev["tri_per_node"] = per_node
                if spec.budget is None:
                    chk.add("PASS", "%s %s: %d tris (no budget for construction "
                                    "stage)" % (spec.label, source.upper(), total))
                    continue
                lo = spec.budget * (1.0 - TRI_TOL)
                hi = spec.budget * (1.0 + TRI_TOL)
                if total < lo or total > hi:
                    chk.add("FAIL", "%s %s: %d tris outside budget %d "
                                    "(allowed %d..%d, %.1f%% off) | per-node %s"
                            % (spec.label, source.upper(), total, spec.budget,
                               int(round(lo)), int(round(hi)),
                               100.0 * (total - spec.budget) / spec.budget,
                               ", ".join("%s=%d" % (n, c) for n, c in per_node)))
                else:
                    ev["tri_status"] = "ok"
        # cross-check FBX vs GLB triangle counts (same asset, two containers)
        if self.full:
            for spec in self.specs:
                g = self.evidence[spec.label]["glb"].get("tri_total")
                f = self.evidence[spec.label]["fbx"].get("tri_total")
                if g is not None and f is not None and g != f:
                    chk.add("FAIL", "%s: GLB %d tris != FBX %d tris "
                                    "(containers disagree)"
                            % (spec.label, g, f))
        if chk.count("FAIL") == 0 and chk.count("BLOCKED") == 0:
            glb_ok = [self.evidence[s.label]["glb"].get("tri_total")
                      for s in self.specs
                      if self.evidence[s.label]["glb"].get("tri_total") is not None]
            fbx_ok = [self.evidence[s.label]["fbx"].get("tri_total")
                      for s in self.specs
                      if self.evidence[s.label]["fbx"].get("tri_total") is not None]
            txt = "all GLB triangle budgets met"
            if glb_ok:
                txt += " (GLB totals %d..%d)" % (min(glb_ok), max(glb_ok))
            if self.full and fbx_ok:
                txt += "; FBX after triangulation %d..%d" % (min(fbx_ok), max(fbx_ok))
            chk.add("PASS", txt)
        return chk

    # ------------------------------------------------------------------ #
    # C4 troughs
    # ------------------------------------------------------------------ #
    def _trough_nodes(self, spec, model, source):
        """{node_name: {"origin": asset xyz | None, "centre": asset xyz, "tri": int}}."""
        out = {}
        for n in model.mesh_nodes:
            if source == "glb":
                origin = model.world[n["index"]][:3, 3].copy()
            else:
                origin = n.get("origin")
            out[n["name"]] = {
                "origin": origin,
                "centre": (n["pos"].min(axis=0) + n["pos"].max(axis=0)) / 2.0,
                "tri": n["tri_count"],
            }
        return out

    def check_c4(self):
        chk = self.checks["C4"]
        contract_rows = C.trough_centers()
        finished = [s for s in self.specs if not s.construction]
        for spec in self.specs:
            rec = self.loaded[spec.label]
            ev = self.evidence[spec.label]
            for source in ("glb", "fbx"):
                model = rec[source]
                if model is None:
                    self.src_error_entries(chk, spec, source.upper(),
                                           rec[source + "_err"])
                    continue
                try:
                    nodes = self._trough_nodes(spec, model, source)
                except Exception as exc:
                    chk.add("FAIL", "%s %s: trough node scan failed: %s: %s"
                            % (spec.label, source.upper(),
                               type(exc).__name__, exc))
                    continue
                if spec.construction:
                    # greybox shared asset: presence per Lead contract, coordinates
                    # informational only (the frozen atlas_layout contract names
                    # only the construction *body* node).
                    missing = [w for w in spec.expected_troughs
                               if w not in nodes]
                    extra_t = sorted(n for n in nodes if "_Trough_" in n
                                     and n not in spec.expected_troughs)
                    devs = []
                    for row in contract_rows:
                        want = spec.expected_troughs[row["index"] - 1]
                        if want not in nodes:
                            continue
                        c = nodes[want]["centre"]
                        devs.append(max(abs(float(c[0]) - row["x"]),
                                        abs(float(c[2]) - row["z"])))
                    if missing or extra_t:
                        chk.add("WARN", "%s %s: construction trough nodes: %s%s "
                                        "(greybox additions, non-failing)"
                                % (spec.label, source.upper(),
                                   ("missing " + ", ".join(missing))
                                   if missing else "",
                                   ("; unexpected " + ", ".join(extra_t))
                                   if extra_t else ""))
                    else:
                        chk.add("INFO", "%s %s: 6 construction trough nodes present"
                                % (spec.label, source.upper()))
                    if devs and max(devs) > C.PORT_TOLERANCE + TOL_EPS:
                        chk.add("WARN", "%s %s: construction trough centres deviate "
                                        "up to %.4f from TROUGH contract (informational)"
                                % (spec.label, source.upper(), max(devs)))
                    ev[source]["troughs"] = "n/a-greybox"
                    continue
                errs = []
                found_names = set(nodes.keys())
                for want in spec.expected_troughs:
                    if want not in found_names:
                        errs.append("missing node %s" % want)
                # unexpected trough nodes (wrong tier/lod/number)
                extra = sorted(n for n in found_names
                               if re.search(r"_Trough_\d{2}_LOD\d$", n)
                               and n not in spec.expected_troughs)
                if extra:
                    errs.append("unexpected trough nodes: %s" % ", ".join(extra))
                max_err = 0.0
                used_bbox = 0
                if spec.lod == 0 and not errs:
                    for row in contract_rows:
                        want = spec.expected_troughs[row["index"] - 1]
                        node = nodes[want]
                        origin = node["origin"]
                        ex = abs(float(origin[0]) - row["x"]) \
                            if origin is not None else 9e9
                        ez = abs(float(origin[2]) - row["z"]) \
                            if origin is not None else 9e9
                        if ex > C.PORT_TOLERANCE + TOL_EPS or ez > C.PORT_TOLERANCE + TOL_EPS:
                            c = node["centre"]
                            ex2 = abs(float(c[0]) - row["x"])
                            ez2 = abs(float(c[2]) - row["z"])
                            if ex2 <= C.PORT_TOLERANCE + TOL_EPS and ez2 <= C.PORT_TOLERANCE + TOL_EPS:
                                ex, ez = ex2, ez2
                                used_bbox += 1
                            else:
                                errs.append("%s centre x=%+.4f z=%+.4f expected "
                                            "x=%+.4f z=%+.4f (origin err %.4f/%.4f)"
                                            % (want, float(c[0]), float(c[2]),
                                               row["x"], row["z"], ex, ez))
                                continue
                        max_err = max(max_err, ex, ez)
                ev[source]["troughs"] = "FAIL" if errs else "ok"
                ev[source]["trough_max_err"] = max_err
                ev[source]["trough_bbox_fallback"] = used_bbox
                if errs:
                    chk.add("FAIL", "%s %s: %s" % (spec.label, source.upper(),
                                                   "; ".join(errs)))
        if chk.count("FAIL") == 0 and chk.count("BLOCKED") == 0:
            chk.add("PASS", "6 trough nodes present on %d finished models x %s; "
                            "LOD0 origins/bbox-centres within %.3f of TROUGH "
                            "rows_z/cols_x"
                    % (len(finished), "GLB+FBX" if self.full else "GLB",
                       C.PORT_TOLERANCE))
        return chk

    # ------------------------------------------------------------------ #
    # C5 ports
    # ------------------------------------------------------------------ #
    def _port_nodes(self, spec, model, source):
        out = {}
        if source == "glb":
            for idx in model.node_order:
                node = model.nodes[idx]
                name = node.get("name") or ""
                if name in (p["object"] for p in C.PORT_SPECS):
                    rot3, scales = _rot3_of(model.world[idx])
                    out[name] = {
                        "pos": model.world[idx][:3, 3].copy(),
                        "dir": rot3 @ np.array([0.0, 1.0, 0.0]),
                        "scales": scales,
                        "is_mesh": "mesh" in node,
                    }
        else:
            for name, info in model.empties.items():
                if name in (p["object"] for p in C.PORT_SPECS):
                    out[name] = {"pos": info["pos"], "dir": info["dir"],
                                 "scales": info["scales"], "is_mesh": False}
        return out

    def check_c5(self):
        chk = self.checks["C5"]
        spec_by_port = {p["name"]: p for p in C.PORT_SPECS}
        for spec in self.specs:
            rec = self.loaded[spec.label]
            ev = self.evidence[spec.label]
            for source in ("glb", "fbx"):
                model = rec[source]
                if model is None:
                    self.src_error_entries(chk, spec, source.upper(),
                                           rec[source + "_err"])
                    continue
                if spec.construction:
                    try:
                        found = self._port_nodes(spec, model, source)
                    except Exception:
                        found = {}
                    want_names = [p["object"] for p in C.PORT_SPECS]
                    missing = [n for n in want_names if n not in found]
                    if missing:
                        chk.add("WARN", "%s %s: construction greybox port empties "
                                        "missing: %s (additions beyond the frozen "
                                        "contract, non-failing)"
                                % (spec.label, source.upper(), ", ".join(missing)))
                    else:
                        chk.add("INFO", "%s %s: 6 construction port empties present"
                                % (spec.label, source.upper()))
                    devs = []
                    for port in C.PORT_SPECS:
                        if port["object"] not in found:
                            continue
                        info = found[port["object"]]
                        exp_pos = np.array(port["asset"], dtype=np.float64)
                        exp_dir = np.array(port["outward"], dtype=np.float64)
                        dp = float(np.abs(np.asarray(info["pos"]) - exp_pos).max())
                        dd = float(np.abs(np.asarray(info["dir"]) - exp_dir).max())
                        devs.append((port["name"], dp, dd))
                    bad = [(n, dp, dd) for n, dp, dd in devs
                           if dp > C.PORT_TOLERANCE + TOL_EPS or dd > C.PORT_TOLERANCE + TOL_EPS]
                    if bad:
                        chk.add("WARN", "%s %s: construction port deviations beyond "
                                        "%.3f (informational): %s"
                                % (spec.label, source.upper(),
                                   C.PORT_TOLERANCE,
                                   "; ".join("%s pos %.4f dir %.4f"
                                             % (n, dp, dd) for n, dp, dd in bad)))
                    if devs:
                        ev[source]["port_max_pos_err"] = max(d[1] for d in devs)
                        ev[source]["port_max_dir_err"] = max(d[2] for d in devs)
                    ev[source]["ports"] = "n/a-greybox"
                    continue
                try:
                    nodes = self._port_nodes(spec, model, source)
                except Exception as exc:
                    chk.add("FAIL", "%s %s: port scan failed: %s: %s"
                            % (spec.label, source.upper(), type(exc).__name__, exc))
                    continue
                errs = []
                details = []
                max_pos_err = 0.0
                max_dir_err = 0.0
                for port in C.PORT_SPECS:
                    name = port["object"]
                    if name not in nodes:
                        errs.append("missing empty %s" % name)
                        continue
                    info = nodes[name]
                    exp_pos = np.array(port["asset"], dtype=np.float64)
                    exp_dir = np.array(port["outward"], dtype=np.float64)
                    pos = np.asarray(info["pos"], dtype=np.float64)
                    dr = np.asarray(info["dir"], dtype=np.float64)
                    per_axis = np.abs(pos - exp_pos)
                    dir_axis = np.abs(dr - exp_dir)
                    max_pos_err = max(max_pos_err, float(per_axis.max()))
                    max_dir_err = max(max_dir_err, float(dir_axis.max()))
                    pe = []
                    if float(per_axis.max()) > C.PORT_TOLERANCE + TOL_EPS:
                        pe.append("pos actual(%+.4f,%+.4f,%+.4f) expected"
                                  "(%+.4f,%+.4f,%+.4f) err(%+.4f,%+.4f,%+.4f) > %.3f"
                                  % (pos[0], pos[1], pos[2],
                                     exp_pos[0], exp_pos[1], exp_pos[2],
                                     pos[0] - exp_pos[0], pos[1] - exp_pos[1],
                                     pos[2] - exp_pos[2], C.PORT_TOLERANCE))
                    if float(dir_axis.max()) > C.PORT_TOLERANCE + TOL_EPS:
                        pe.append("dir actual(%+.3f,%+.3f,%+.3f) expected"
                                  "(%+.3f,%+.3f,%+.3f) err(%+.3f,%+.3f,%+.3f) > %.3f"
                                  % (dr[0], dr[1], dr[2],
                                     exp_dir[0], exp_dir[1], exp_dir[2],
                                     dr[0] - exp_dir[0], dr[1] - exp_dir[1],
                                     dr[2] - exp_dir[2], C.PORT_TOLERANCE))
                    if pe:
                        errs.append("%s %s" % (name, "; ".join(pe)))
                    if info["is_mesh"]:
                        errs.append("%s is a mesh node, expected an empty" % name)
                    details.append((port["name"], pos, dr))
                extra = sorted(n for n in nodes
                               if n.startswith("PORT_") and
                               n not in [p["object"] for p in C.PORT_SPECS])
                if extra:
                    errs.append("unexpected PORT_* nodes: %s" % ", ".join(extra))
                ev[source]["ports"] = "FAIL" if errs else "ok"
                ev[source]["port_max_pos_err"] = max_pos_err
                ev[source]["port_max_dir_err"] = max_dir_err
                ev[source]["port_count"] = len([n for n in nodes if n.startswith("PORT_")])
                if errs:
                    chk.add("FAIL", "%s %s: %s"
                            % (spec.label, source.upper(), "; ".join(errs)))
        # port geometry visibility: informational heuristic only (never fails)
        self._port_visibility_info(chk)
        if chk.count("FAIL") == 0 and chk.count("BLOCKED") == 0:
            chk.add("PASS", "6 port empties present on all finished models x %s; "
                            "origins/directions within %.3f"
                    % ("GLB+FBX" if self.full else "GLB", C.PORT_TOLERANCE))
        return chk

    def _port_visibility_info(self, chk):
        radii = []
        for spec in self.specs:
            if spec.construction:
                continue
            glb = self.loaded[spec.label]["glb"]
            if glb is None:
                continue
            try:
                pos = glb.all_positions()
            except Exception:
                continue
            counts = {}
            for port in C.PORT_SPECS:
                exp = np.array(port["asset"], dtype=np.float64)
                d = np.linalg.norm(pos - exp.reshape(1, 3), axis=1)
                counts[port["name"]] = int(np.count_nonzero(
                    d <= PORT_PROXIMITY_RADIUS))
            visible = C.PORT_GEOMETRY_VISIBLE.get(spec.tier, set())
            missing = [n for n in visible if counts.get(n, 0) == 0]
            note = "nearby vertices (r=%.2f): %s" % (
                PORT_PROXIMITY_RADIUS,
                " ".join("%s=%d" % (p["name"], counts[p["name"]])
                         for p in C.PORT_SPECS))
            if missing:
                chk.add("WARN", "%s: expected-visible port geometry not detected "
                                "near %s (heuristic only, does not fail) | %s"
                        % (spec.label, ", ".join(missing), note))
            else:
                radii.append(note)
        if radii and chk.count("WARN") == 0:
            chk.add("INFO", "PORT_GEOMETRY_VISIBLE heuristic plausible on all "
                            "tiers; LOD0 vertex proximity to port origins: %s"
                    % (radii[0] if radii else "n/a"))

    # ------------------------------------------------------------------ #
    # C6 materials + atlas
    # ------------------------------------------------------------------ #
    def check_c6(self):
        chk = self.checks["C6"]
        canon = list(C.MATERIAL_ORDER)
        for spec in self.specs:
            rec = self.loaded[spec.label]
            ev = self.evidence[spec.label]
            for source in ("glb", "fbx"):
                model = rec[source]
                if model is None:
                    self.src_error_entries(chk, spec, source.upper(),
                                           rec[source + "_err"])
                    continue
                try:
                    if source == "glb":
                        slots = model.material_names()
                        used_raw = model.used_materials()
                        refs = model.texture_refs()
                        node_seqs = []
                        for n in model.mesh_nodes:
                            seq = []
                            for mat_idx, _cnt in n["prim_materials"]:
                                if mat_idx is None:
                                    seq.append(None)
                                elif mat_idx < len(slots):
                                    seq.append(slots[mat_idx])
                                else:
                                    seq.append("#%d" % mat_idx)
                            node_seqs.append((n["name"], seq))
                    else:
                        slots = model.material_names()   # ordered unique union
                        used_raw = slots
                        refs = model.texture_refs()
                        node_seqs = [(n["name"],
                                      list(n.get("material_slots", [])))
                                     for n in model.mesh_nodes]
                except Exception as exc:
                    chk.add("FAIL", "%s %s: material scan failed: %s: %s"
                            % (spec.label, source.upper(), type(exc).__name__, exc))
                    continue
                used_set = set(m for m in used_raw if m)
                canonical_used = [m for m in canon if m in used_set]
                ev[source]["materials"] = canonical_used
                ev[source]["material_slots"] = slots
                ev[source]["texture_refs"] = refs
                errs = []
                if len(slots) > 4:
                    errs.append("%d material slots > 4: %s" % (len(slots), slots))
                unknown = [m for m in slots if m not in canon]
                if unknown:
                    errs.append("unknown material names: %s" % ", ".join(unknown))
                unknown_used = sorted(m for m in used_set if m not in canon)
                if unknown_used:
                    errs.append("unknown used materials: %s"
                                % ", ".join(unknown_used))
                if canonical_used != spec.materials:
                    errs.append("model material union %s expected %s "
                                "(canonical slot order %s)"
                                % (canonical_used, spec.materials, canon))
                # per-object slot list must be a subsequence of MATERIAL_ORDER
                # (Lead contract: each mesh's material slots keep canonical order)
                for name, seq in node_seqs:
                    if any(m is None or m == "" for m in seq):
                        errs.append("%s: mesh object has an unassigned material slot"
                                    % (name or "<unnamed>"))
                    clean = []
                    for m in seq:
                        if m and m not in clean:
                            clean.append(m)
                    ordered = [m for m in canon if m in set(clean)]
                    if clean != ordered:
                        errs.append("%s: slots %s not in canonical subsequence "
                                    "order %s" % (name, clean, canon))
                    if not clean:
                        errs.append("%s: mesh object has no material assigned"
                                    % (name or "<unnamed>"))
                if errs:
                    chk.add("FAIL", "%s %s: %s"
                            % (spec.label, source.upper(), "; ".join(errs)))
                else:
                    ev[source]["mat_status"] = "ok"
        # texture image reference inventory (embedded GLB PNGs are expected/allowed)
        glb_refs, fbx_refs = [], []
        for spec in self.specs:
            glb_refs.extend(self.evidence[spec.label]["glb"].get(
                "texture_refs", []) or [])
            fbx_refs.extend(self.evidence[spec.label]["fbx"].get(
                "texture_refs", []) or [])
        embedded = sorted(set(r for r in glb_refs if "embedded bufferView" in r))
        if embedded:
            chk.add("INFO", "GLB embeds %d atlas image reference(s): %s "
                            "(embedded PNGs are expected for glTF; not a failure)"
                    % (len(embedded), "; ".join(embedded[:3])))
        ext = sorted(set(fbx_refs))
        if ext:
            chk.add("INFO", "FBX external texture reference(s) (%d unique): %s%s"
                    % (len(ext), "; ".join(ext[:8]),
                       " ..." if len(ext) > 8 else ""))
        elif not embedded and self.full:
            chk.add("INFO", "no texture image references found in GLB or FBX "
                            "materials (material_slots.json is the canonical atlas "
                            "table)")
        # atlas PNG checks (existence handled in C1; format/dimensions here)
        for key in sorted(C.ATLAS_FILES):
            rel = "art/" + C.ATLAS_FILES[key]
            p = self.abspath(rel)
            if not os.path.isfile(p):
                chk.add("BLOCKED", "atlas %s missing (%s) -> PNG format check NOT "
                                   "EXECUTED" % (key, rel))
                continue
            try:
                hdr = read_png_header(p)
            except Exception as exc:
                chk.add("FAIL", "atlas %s: PNG header read failed: %s: %s"
                        % (key, type(exc).__name__, exc))
                continue
            if "error" in hdr:
                chk.add("FAIL", "atlas %s (%s): %s" % (key, rel, hdr["error"]))
                continue
            errs = []
            if hdr["width"] != C.ATLAS_SIZE or hdr["height"] != C.ATLAS_SIZE:
                errs.append("size %dx%d expected %dx%d"
                            % (hdr["width"], hdr["height"],
                               C.ATLAS_SIZE, C.ATLAS_SIZE))
            if hdr["bit_depth"] != 8:
                errs.append("bit depth %d expected 8" % hdr["bit_depth"])
            if errs:
                chk.add("FAIL", "atlas %s (%s): %s" % (key, rel, "; ".join(errs)))
            else:
                chk.add("PASS", "atlas %s: %dx%d 8-bit %s PNG"
                        % (key, hdr["width"], hdr["height"], hdr["color"]))
        # external texture refs: report only (embedded GLB images and relative
        # FBX paths are both legitimate); warn if a ref is not one of the atlases
        known_basenames = {os.path.basename(v) for v in C.ATLAS_FILES.values()}
        outside = []
        for ref in sorted(set(ext)):
            target = ref.split(" -> ")[-1].replace("\\", "/")
            base = os.path.basename(target)
            if base not in known_basenames:
                outside.append(ref)
        if outside:
            chk.add("WARN", "external texture reference(s) outside the three atlas "
                            "files: %s" % "; ".join(outside[:8]))
        if chk.count("FAIL") == 0 and chk.count("BLOCKED") == 0:
            n = sum(1 for s in self.specs for src in ("glb", "fbx")
                    if self.evidence[s.label][src].get("mat_status") == "ok")
            chk.add("PASS", "materials <=4, per-object slots are canonical "
                            "subsequences, model unions match TIER_MATERIALS; "
                            "3 atlas PNGs 2048x2048 8-bit (%d model/source material "
                            "checks)" % n)
        return chk

    # ------------------------------------------------------------------ #
    # C7 UV
    # ------------------------------------------------------------------ #
    def _uv_for_model(self, model, source):
        """Returns (uv triangles (F,3,2), per-triangle group ids, error)."""
        if source == "glb":
            parts = []
            groups = []
            missing = False
            for gi, n in enumerate(model.mesh_nodes):
                if n["tris"].size == 0:
                    continue
                if n["uv"] is None:
                    missing = True
                    continue
                tri_uv = n["uv"][n["tris"]]        # per-node triangles -> (F,3,2)
                parts.append(tri_uv)
                groups.append(np.full(tri_uv.shape[0], gi, dtype=np.int64))
            if missing or not parts:
                return None, None, "some mesh nodes have no TEXCOORD_0"
            return np.vstack(parts), np.concatenate(groups), None
        else:
            parts = []
            groups = []
            missing = False
            for gi, n in enumerate(model.mesh_nodes):
                if n["tri_count"] == 0:
                    continue
                if n["uv"] is None:
                    missing = True
                    continue
                parts.append(n["uv"])              # already (F,3,2)
                groups.append(np.full(n["uv"].shape[0], gi, dtype=np.int64))
            if missing or not parts:
                return None, None, "some mesh nodes have no UV layer"
            return np.vstack(parts), np.concatenate(groups), None

    def check_c7(self):
        chk = self.checks["C7"]
        for spec in self.specs:
            rec = self.loaded[spec.label]
            ev = self.evidence[spec.label]
            for source in ("glb", "fbx"):
                model = rec[source]
                if model is None:
                    self.src_error_entries(chk, spec, source.upper(),
                                           rec[source + "_err"])
                    continue
                try:
                    looptris_uv, groups, err = self._uv_for_model(model, source)
                except Exception as exc:
                    chk.add("FAIL", "%s %s: UV extraction failed: %s: %s"
                            % (spec.label, source.upper(), type(exc).__name__, exc))
                    continue
                if err is not None:
                    chk.add("FAIL", "%s %s: %s" % (spec.label, source.upper(), err))
                    ev[source]["uv_status"] = "FAIL"
                    continue
                try:
                    stats = check_uv(looptris_uv, groups=groups)
                except Exception as exc:
                    chk.add("FAIL", "%s %s: UV rasterisation failed: %s: %s"
                            % (spec.label, source.upper(), type(exc).__name__, exc))
                    continue
                ev[source]["uv"] = stats
                node_names = [n.get("name", "") for n in model.mesh_nodes]

                def gname(gi):
                    try:
                        return node_names[int(gi)]
                    except Exception:
                        return "#%s" % gi

                oob_obj = ""
                if groups is not None and stats["oob_loops"] > 0:
                    mask = ((looptris_uv < -UV_EPS)
                            | (looptris_uv > 1.0 + UV_EPS))
                    per_loop = mask.any(axis=2).sum(axis=1)   # loops per triangle
                    counts = np.bincount(groups, weights=per_loop,
                                         minlength=len(node_names))
                    offenders = ["%s(%d loops)" % (gname(i), int(c))
                                 for i, c in enumerate(counts) if c > 0]
                    if offenders:
                        oob_obj = " in objects: " + ", ".join(offenders[:4])
                errs = []
                warns = []
                if stats["oob_loops"] > 0:
                    errs.append("%d loop UVs outside [0,1] (max deviation %.6f, "
                                "U[%.5f,%.5f] V[%.5f,%.5f])%s"
                                % (stats["oob_loops"], stats["max_dev"],
                                   stats["min_u"], stats["max_u"],
                                   stats["min_v"], stats["max_v"], oob_obj))
                diag = stats.get("diag")
                diag_txt = ""
                if diag:
                    grew = (diag["ratio_hi"] / stats["double_ratio"]
                            if stats["double_ratio"] else 0.0)
                    pair_txt = ""
                    if diag.get("group_bbox_pairs"):
                        pair_txt = ("; UV-bbox-overlapping object pairs: "
                                    + ", ".join("%s/%s" % (gname(a), gname(b))
                                                for a, b in diag["group_bbox_pairs"][:3]))
                    diag_txt = (" [epsilon-diag: 1024-grid double=%.3f%% "
                                "(%.2fx of 512), 0.5px-inset double=%.3f%%, "
                                "cross-object double=%d texels%s -> %s]"
                                % (100.0 * diag["ratio_hi"], grew,
                                   100.0 * diag["ratio_inset"],
                                   diag["cross_object"],
                                   pair_txt,
                                   "raster edge-rounding of adjacent islands "
                                   "(no real packer overlap)"
                                   if diag["rounding"] and
                                   diag["cross_object"] == 0
                                   else "PERSISTS at higher resolution/inset "
                                        "-> REAL overlap"))
                if stats["double_ratio"] > UV_DOUBLE_MAX:
                    msg = ("intra-model UV overlap: %.4f%% of covered texels "
                           "double-covered (%d of %d) > %.2f%% allowed%s"
                           % (100.0 * stats["double_ratio"], stats["double"],
                              stats["covered"], 100.0 * UV_DOUBLE_MAX, diag_txt))
                    if diag and diag["rounding"] and diag["cross_object"] == 0:
                        warns.append(msg)   # proven raster epsilon, documented
                    else:
                        errs.append(msg)
                elif stats["double_ratio"] > 0 and diag:
                    warns.append("double-covered %.4f%% within tolerance%s"
                                 % (100.0 * stats["double_ratio"], diag_txt))
                if diag and diag["cross_object"] > 0:
                    errs.append("UV islands of different mesh objects overlap "
                                "(%d texels) -> real packer bug"
                                % diag["cross_object"])
                ev[source]["uv_status"] = "FAIL" if errs else "ok"
                for w in warns:
                    chk.add("WARN", "%s %s: %s"
                            % (spec.label, source.upper(), w))
                if errs:
                    chk.add("FAIL", "%s %s: %s"
                            % (spec.label, source.upper(), "; ".join(errs)))
        if chk.count("FAIL") == 0 and chk.count("BLOCKED") == 0:
            cov = [e["uv"]["covered_ratio"] for label in self.evidence
                   for e in self.evidence[label].values()
                   if isinstance(e, dict) and e.get("uv")]
            dbl = [(e["uv"]["double_ratio"], e["uv"].get("diag"))
                   for label in self.evidence
                   for e in self.evidence[label].values()
                   if isinstance(e, dict) and e.get("uv")]
            max_dbl = max(d for d, _ in dbl)
            rounding_note = ""
            if any(d for d, _ in dbl):
                rounding_note = ("" if any(diag and not diag["rounding"]
                                           for _d, diag in dbl)
                                 else "; all double coverage proven to be raster "
                                      "edge-rounding (0.5px inset + 1024-grid "
                                      "tests), not a packer overlap")
            chk.add("PASS", "UVs in [0,1] and no real intra-model overlap on all "
                            "models; covered UV area %.1f%%..%.1f%% of the %dx%d "
                            "grid, max double-covered %.4f%% (limit %.2f%%)%s"
                    % (100.0 * min(cov), 100.0 * max(cov), UV_GRID, UV_GRID,
                       100.0 * max_dbl, 100.0 * UV_DOUBLE_MAX, rounding_note))
        return chk

    # ------------------------------------------------------------------ #
    # C8 LOD continuity
    # ------------------------------------------------------------------ #
    def check_c8(self):
        chk = self.checks["C8"]
        for tier in C.TIERS:
            for faction in C.FACTIONS:
                for source in ("glb", "fbx"):
                    base = self._bounds_of("%s.%s LOD0" % (tier, faction), source)
                    if base is None:
                        chk.add("BLOCKED", "%s.%s LOD0 %s: no bounds available -> "
                                            "LOD continuity NOT EXECUTED"
                                % (tier, faction, source.upper()))
                        continue
                    base_ext = np.array([base[3] - base[0], base[4] - base[1],
                                         base[5] - base[2]])
                    worst = 0.0
                    worst_desc = ""
                    failed = False
                    for lod in (1, 2):
                        b = self._bounds_of("%s.%s LOD%d" % (tier, faction, lod),
                                            source)
                        label = "%s.%s LOD%d" % (tier, faction, lod)
                        if b is None:
                            chk.add("BLOCKED", "%s %s: no bounds available -> LOD "
                                                "continuity NOT EXECUTED"
                                    % (label, source.upper()))
                            continue
                        ext = np.array([b[3] - b[0], b[4] - b[1], b[5] - b[2]])
                        shrink = np.zeros(3)
                        for i in range(3):
                            if base_ext[i] > 1e-9:
                                shrink[i] = (base_ext[i] - ext[i]) / base_ext[i]
                        axis = ["X", "Y", "Z"][int(np.argmax(shrink))]
                        smax = float(shrink.max())
                        if smax > worst:
                            worst = smax
                            worst_desc = "%s LOD%d %s %.2f%% vs LOD0" % (
                                label, lod, axis, 100.0 * smax)
                        if smax > C.LOD_BBOX_SHRINK_MAX + TOL_EPS:
                            failed = True
                            chk.add("FAIL", "%s %s: bbox shrink %s %.2f%% > %.2f%% "
                                            "allowed | LOD0 %s vs LOD%d %s"
                                    % (label, source.upper(), axis, 100.0 * smax,
                                       100.0 * C.LOD_BBOX_SHRINK_MAX,
                                       _fmt6(base), lod, _fmt6(b)))
                    if not failed:
                        chk.add("PASS", "%s.%s %s: LOD1/LOD2 shrink within %.2f%% "
                                        "(worst %s)"
                                % (tier, faction, source.upper(),
                                   100.0 * C.LOD_BBOX_SHRINK_MAX,
                                   worst_desc or "0.00%"))
        return chk

    def _bounds_of(self, label, source):
        ev = self.evidence.get(label, {}).get(source, {})
        return ev.get("bounds")

    # ------------------------------------------------------------------ #
    # C9 silhouette distinguishability
    # ------------------------------------------------------------------ #
    def check_c9(self):
        chk = self.checks["C9"]
        sil = {}  # (faction, lod, tier) -> views
        profiles = {}  # (faction, lod, tier) -> {view: heights}
        for tier in C.TIERS:
            for faction in C.FACTIONS:
                for lod in (0, 2):
                    label = "%s.%s LOD%d" % (tier, faction, lod)
                    rec = self.loaded.get(label)
                    if rec is None or rec["glb"] is None:
                        chk.add("BLOCKED", "%s: GLB unavailable -> silhouette NOT "
                                            "EXECUTED" % label)
                        continue
                    try:
                        tris = rec["glb"].triangles_asset()
                        views = silhouette_views(tris)
                        sil[(faction, lod, tier)] = views
                        profiles[(faction, lod, tier)] = outline_profiles(views)
                    except Exception as exc:
                        chk.add("FAIL", "%s: silhouette rasterisation failed: "
                                        "%s: %s"
                                % (label, type(exc).__name__, exc))
        # per tier/view silhouette area + fill ratio (evidence)
        for faction in C.FACTIONS:
            for tier in C.TIERS:
                for lod in (0, 2):
                    views = sil.get((faction, lod, tier))
                    if not views:
                        continue
                    chk.add("INFO", "%s.%s LOD%d silhouette: %s"
                            % (tier, faction, lod,
                               " ".join("%s area=%dpx fill=%.1f%%"
                                        % (v, views[v]["area"],
                                           100.0 * views[v]["fill"])
                                        for v in ("front", "side", "top"))))
        pairs = [(C.TIERS[i], C.TIERS[j])
                 for i in range(len(C.TIERS)) for j in range(i + 1, len(C.TIERS))]
        chk.add("INFO", "raw area-IoU matrices below are INFORMATIONAL ONLY: the "
                        "fixed 3x1x2 envelope forces filled-area overlap near 1.0, "
                        "so area IoU alone cannot discriminate tiers. The PASS rule "
                        "is the outline rule: mean|dh|>=%.2f grid or max|dh|>=%.2f "
                        "grid or area IoU<=%.2f, in at least one view at 50%% scale"
                % (OUTLINE_MEAN_MIN, OUTLINE_MAX_MIN, SIL_IOU_MAX))
        for faction in C.FACTIONS:
            for lod in (0, 2):
                for view in ("front", "side", "top"):
                    header = "%-14s" % (faction + " " + view)
                    for tier in C.TIERS:
                        header += " %8s" % tier[:8]
                    chk.add("INFO", "area-IoU LOD%d %s" % (lod, header))
                    for ta in C.TIERS:
                        row = "%-14s" % ta
                        for tb in C.TIERS:
                            va = sil.get((faction, lod, ta))
                            vb = sil.get((faction, lod, tb))
                            if not va or not vb:
                                row += " %8s" % "n/a"
                                continue
                            row += " %8.3f" % iou(va[view]["grid"],
                                                  vb[view]["grid"])[0]
                        chk.add("INFO", "area-IoU LOD%d %s" % (lod, row))
        # outline-distinguishability gate: LOD2 is the strict test (it must
        # survive silhouette simplification); LOD0 is reported for information.
        for lod in (2, 0):
            strict = (lod == 2)
            for faction in C.FACTIONS:
                for ta, tb in pairs:
                    pa = profiles.get((faction, lod, ta))
                    pb = profiles.get((faction, lod, tb))
                    if not pa or not pb:
                        chk.add("BLOCKED" if strict else "WARN",
                                "%s %s vs %s: LOD%d outline data unavailable"
                                % (faction, ta, tb, lod))
                        continue
                    parts = []
                    ok = False
                    deciding = []
                    for view in ("front", "side", "top"):
                        dh = np.abs(pa[view] - pb[view])
                        mean_dh = float(dh.mean())
                        max_dh = float(dh.max())
                        va = sil.get((faction, lod, ta))
                        vb = sil.get((faction, lod, tb))
                        aiou = iou(va[view]["grid"], vb[view]["grid"])[0]
                        ok_view = (mean_dh >= OUTLINE_MEAN_MIN
                                   or max_dh >= OUTLINE_MAX_MIN
                                   or aiou <= SIL_IOU_MAX)
                        if ok_view:
                            ok = True
                            deciding.append(view)
                        parts.append("%s m=%.3f x=%.3f iou=%.3f%s"
                                     % (view, mean_dh, max_dh, aiou,
                                        " *" if ok_view else ""))
                    text = ("%s %s vs %s: %s | %s"
                            % (faction, ta, tb,
                               ("PASS via " + ",".join(deciding)) if ok
                               else "no view meets outline rule",
                               "; ".join(parts)))
                    if strict:
                        chk.add("PASS" if ok else "FAIL", "[STRICT LOD2] " + text)
                    else:
                        chk.add("INFO" if ok else "WARN",
                                "[report LOD0] %s %s"
                                % ("PASS" if ok else "FAIL", text))
        if chk.count("FAIL") == 0 and chk.count("BLOCKED") == 0:
            n = len(C.FACTIONS) * len(pairs)
            chk.add("PASS", "%d/%d tier pairs distinguishable at 50%% scale under "
                            "the outline rule (LOD2 strict; mean|dh|>=%.2f or "
                            "max|dh|>=%.2f grid or area IoU<=%.2f)"
                    % (n, n, OUTLINE_MEAN_MIN, OUTLINE_MAX_MIN, SIL_IOU_MAX))
        return chk

    # ------------------------------------------------------------------ #
    # C10 material_slots.json
    # ------------------------------------------------------------------ #
    def check_c10(self):
        chk = self.checks["C10"]
        path = self.abspath("art/material_slots.json")
        if not os.path.isfile(path):
            chk.add("BLOCKED", "art/material_slots.json missing -> cross-check NOT "
                               "EXECUTED")
            return chk
        try:
            with open(path, "r", encoding="utf-8-sig") as f:
                data = json.load(f)
        except Exception as exc:
            chk.add("FAIL", "material_slots.json unreadable: %s: %s"
                    % (type(exc).__name__, exc))
            return chk
        if not isinstance(data, dict):
            chk.add("FAIL", "material_slots.json top level is %s, expected object"
                    % type(data).__name__)
            return chk

        # slot order ------------------------------------------------------
        order = data.get("material_order")
        if order is None:
            for key in ("slots_order", "slot_order", "material_slots_order"):
                if isinstance(data.get(key), list):
                    order = data[key]
                    break
        if not isinstance(order, list):
            chk.add("FAIL", "missing 'material_order' list (expected %s)"
                    % (C.MATERIAL_ORDER,))
        elif list(order) != C.MATERIAL_ORDER:
            chk.add("FAIL", "material_order = %s expected %s"
                    % (order, C.MATERIAL_ORDER))
        else:
            chk.add("PASS", "material_order %s" % (C.MATERIAL_ORDER,))

        # atlas table -----------------------------------------------------
        atlas = data.get("atlas")
        if not isinstance(atlas, dict):
            chk.add("FAIL", "missing 'atlas' object (expected files for %s)"
                    % (sorted(C.ATLAS_FILES.values()),))
        else:
            size = atlas.get("size")
            if size is not None and int(size) != C.ATLAS_SIZE:
                chk.add("FAIL", "atlas.size = %s expected %d"
                        % (size, C.ATLAS_SIZE))
            files = atlas.get("files")
            if not isinstance(files, dict):
                chk.add("FAIL", "atlas.files missing (expected %s)"
                        % (C.ATLAS_FILES,))
                files = {}
            for key in sorted(C.ATLAS_FILES):
                rel = files.get(key)
                if not isinstance(rel, str):
                    chk.add("FAIL", "atlas.files.%s missing (expected %r)"
                            % (key, C.ATLAS_FILES[key]))
                    continue
                want_base = os.path.basename(C.ATLAS_FILES[key])
                got_base = os.path.basename(rel.replace("\\", "/"))
                if got_base != want_base:
                    chk.add("FAIL", "atlas.files.%s = %r expected %r"
                            % (key, rel, C.ATLAS_FILES[key]))
                    continue
                resolved = None
                for cand in (os.path.join(self.art, rel.replace("/", os.sep)),
                             os.path.join(self.root, rel.replace("/", os.sep))):
                    if os.path.isfile(cand):
                        resolved = cand
                        break
                if resolved is None:
                    chk.add("FAIL", "atlas.files.%s = %r does not resolve to an "
                                    "existing file under the workspace"
                            % (key, rel))
                else:
                    chk.add("PASS", "atlas.files.%s -> %s" % (key, rel))

        # per-tier material sets ------------------------------------------
        tier_sets = data.get("tier_material_sets")
        if not isinstance(tier_sets, dict):
            alt = data.get("tiers")
            tier_sets = alt if isinstance(alt, dict) else {}
        if not tier_sets:
            # fall back to the per-faction 'slots' table (union across factions)
            slots_src = data.get("slots")
            derived = {}
            if isinstance(slots_src, dict):
                for tier in C.TIERS:
                    for faction in C.FACTIONS:
                        vals = slots_src.get("%s.%s" % (tier, faction))
                        if isinstance(vals, list):
                            derived.setdefault(tier, vals)
                cs = slots_src.get(C.CONSTRUCTION_STAGE)
                if isinstance(cs, list):
                    derived[C.CONSTRUCTION_STAGE] = cs
            if derived:
                tier_sets = derived
                chk.add("INFO", "tier_material_sets derived from 'slots' "
                                "(no 'tier_material_sets'/'tiers' object)")
        if not tier_sets:
            chk.add("FAIL", "missing 'tier_material_sets' (or 'tiers'/'slots') "
                            "per-tier material sets")
        for tier in list(C.TIERS) + [C.CONSTRUCTION_STAGE]:
            expected = list(C.TIER_MATERIALS[tier])
            got = tier_sets.get(tier)
            if got is None:
                chk.add("FAIL", "tier_material_sets.%s missing (expected %s)"
                        % (tier, expected))
            elif list(got) != expected:
                chk.add("FAIL", "tier_material_sets.%s = %s expected %s"
                        % (tier, got, expected))
            else:
                chk.add("PASS", "tier_material_sets.%s = %s" % (tier, expected))

        # per-slot material table (optional, cross-checked when present) ---
        mat_list = data.get("materials")
        if isinstance(mat_list, list) and mat_list:
            by_slot = {}
            for entry in mat_list:
                if not isinstance(entry, dict):
                    chk.add("FAIL", "materials[] entry is %s, expected object"
                            % type(entry).__name__)
                    continue
                name = entry.get("name")
                slot = entry.get("slot")
                if name not in C.MATERIAL_ORDER:
                    chk.add("FAIL", "materials[].name = %r is not one of %s"
                            % (name, C.MATERIAL_ORDER))
                    continue
                if not isinstance(slot, int) or not (0 <= slot < len(C.MATERIAL_ORDER)):
                    chk.add("FAIL", "materials[].slot = %r invalid for %r"
                            % (slot, name))
                    continue
                if C.MATERIAL_ORDER[slot] != name:
                    chk.add("FAIL", "materials[] slot %d is %r expected %r"
                            % (slot, name, C.MATERIAL_ORDER[slot]))
                by_slot[slot] = entry
                params = entry.get("params")
                if isinstance(params, dict):
                    exp = C.MATERIAL_PARAMS[name]
                    for pk in ("metallic", "roughness", "alpha"):
                        if pk in params and abs(float(params[pk]) - exp[pk]) > 1e-6:
                            chk.add("FAIL", "materials[%s].params.%s = %s expected %s"
                                    % (name, pk, params[pk], exp[pk]))
                    if "blend" in params and params["blend"] != exp["blend"]:
                        chk.add("FAIL", "materials[%s].params.blend = %r expected %r"
                                % (name, params["blend"], exp["blend"]))
                regions = entry.get("atlas_regions")
                if isinstance(regions, list):
                    for reg in regions:
                        if not isinstance(reg, dict):
                            continue
                        rname = reg.get("region")
                        if rname not in C.REGIONS:
                            chk.add("FAIL", "materials[%s] atlas region %r unknown"
                                    % (name, rname))
                            continue
                        rmat, rect = C.REGIONS[rname]
                        if rmat != name:
                            chk.add("FAIL", "atlas region %r belongs to %r, listed "
                                            "under %r" % (rname, rmat, name))
                        if "pixel_rect" in reg:
                            got = reg["pixel_rect"]
                            if list(got) != list(rect):
                                chk.add("FAIL", "atlas region %r pixel_rect = %s "
                                                "expected %s"
                                        % (rname, got, list(rect)))
                        if "uv_rect" in reg:
                            got = reg["uv_rect"]
                            want = C.region_uv(rname)
                            # material_slots.json rounds uv_rect to 5 decimals;
                            # 2e-5 covers that quantisation while still catching
                            # a real layout drift between atlas_layout.py and the
                            # shipped material table.
                            if len(got) != 4 or any(
                                    abs(float(got[i]) - want[i]) > 2e-5
                                    for i in range(4)):
                                chk.add("FAIL", "atlas region %r uv_rect = %s "
                                                "expected %s"
                                        % (rname, got,
                                           tuple(round(v, 6) for v in want)))
            for slot in range(len(C.MATERIAL_ORDER)):
                if slot not in by_slot:
                    chk.add("FAIL", "materials[] has no entry for slot %d (%s)"
                            % (slot, C.MATERIAL_ORDER[slot]))
        elif mat_list is not None:
            chk.add("FAIL", "'materials' is %s, expected list"
                    % type(mat_list).__name__)
        else:
            chk.add("INFO", "'materials' per-slot table absent (optional; "
                            "material_order covers slot names/order)")

        # 'slots' alias: Tier.Faction -> canonical material list -----------
        slots = data.get("slots")
        if isinstance(slots, dict) and slots:
            for tier in C.TIERS:
                for faction in C.FACTIONS:
                    key = "%s.%s" % (tier, faction)
                    expected = list(C.TIER_MATERIALS[tier])
                    if key not in slots:
                        chk.add("FAIL", "slots.%s missing (expected %s)"
                                % (key, expected))
                        continue
                    got = slots[key]
                    if not isinstance(got, list):
                        chk.add("FAIL", "slots.%s is %s, expected list"
                                % (key, type(got).__name__))
                    elif list(got) != expected:
                        chk.add("FAIL", "slots.%s = %s expected %s"
                                % (key, got, expected))
                    else:
                        chk.add("PASS", "slots.%s = %s" % (key, expected))
            if C.CONSTRUCTION_STAGE in slots:
                got = slots[C.CONSTRUCTION_STAGE]
                expected = list(C.TIER_MATERIALS[C.CONSTRUCTION_STAGE])
                if list(got) != expected:
                    chk.add("FAIL", "slots.%s = %s expected %s"
                            % (C.CONSTRUCTION_STAGE, got, expected))

        # 'models' per-LOD detail (optional, cross-checked when present) ---
        models = data.get("models")
        if isinstance(models, dict) and models:
            for tier in C.TIERS:
                for faction in C.FACTIONS:
                    key = "%s.%s" % (tier, faction)
                    entry = models.get(key)
                    if not isinstance(entry, dict):
                        # per-model detail is optional (Lead convenience table);
                        # missing detail must not block the art gate
                        chk.add("WARN", "models.%s absent from material_slots.json "
                                        "(optional per-model detail)" % key)
                        continue
                    expected = list(C.TIER_MATERIALS[tier])
                    for lod in C.LODS:
                        lk = "LOD%d" % lod
                        le = entry.get(lk)
                        if not isinstance(le, dict):
                            chk.add("INFO", "models.%s.%s absent (optional detail)"
                                    % (key, lk))
                            continue
                        if "materials" in le and isinstance(le["materials"], list):
                            if list(le["materials"]) != expected:
                                chk.add("FAIL", "models.%s.%s.materials = %s "
                                                "expected %s"
                                        % (key, lk, le["materials"], expected))
                        label = "%s.%s LOD%d" % (tier, faction, lod)
                        glb_tri = self.evidence.get(label, {}).get(
                            "glb", {}).get("tri_total")
                        if "triangles" in le and glb_tri is not None:
                            if int(le["triangles"]) != int(glb_tri):
                                chk.add("FAIL", "models.%s.%s.triangles = %s but "
                                                "exported GLB has %d triangles"
                                        % (key, lk, le["triangles"], glb_tri))
            ck = models.get(C.CONSTRUCTION_STAGE)
            if isinstance(ck, dict):
                expected = list(C.TIER_MATERIALS[C.CONSTRUCTION_STAGE])
                if "materials" in ck and isinstance(ck["materials"], list) \
                        and list(ck["materials"]) != expected:
                    chk.add("FAIL", "models.%s.materials = %s expected %s"
                            % (C.CONSTRUCTION_STAGE, ck["materials"], expected))
            else:
                chk.add("INFO", "models.%s absent (optional)"
                        % C.CONSTRUCTION_STAGE)
        elif models is not None:
            chk.add("FAIL", "'models' is %s, expected object"
                    % type(models).__name__)
        else:
            chk.add("INFO", "'models' per-model detail absent (optional)")

        if chk.count("FAIL") == 0 and chk.count("BLOCKED") == 0:
            chk.add("PASS", "material_slots.json cross-check: slot order/names, "
                            "atlas paths and per-tier material sets are consistent "
                            "with atlas_layout.py")
        return chk

    # ------------------------------------------------------------------ #
    # run + report
    # ------------------------------------------------------------------ #
    def run(self):
        checks = []
        if C is None:
            msg = "art/atlas_layout.py could not be imported (contract unavailable)"
            for cid, title in [("C1", "File set complete")]:
                pass
            return msg, None
        contract_ok = True
        contract_detail = "loaded"
        try:
            C.validate_contract()
        except Exception as exc:
            contract_ok = False
            contract_detail = "validate_contract() FAILED: %s: %s" % (
                type(exc).__name__, exc)
        checks.append(self.check_c1())
        if NUMPY_ERR is not None:
            # everything numeric is blocked; still write C1 evidence
            for cid in ("C2", "C3", "C4", "C5", "C6", "C7", "C8", "C9", "C10"):
                self.checks[cid].add("BLOCKED", "numpy unavailable (%s) -> check "
                                                "NOT EXECUTED" % NUMPY_ERR)
        else:
            self.load_all()
            checks.append(self.check_c2())
            checks.append(self.check_c3())
            checks.append(self.check_c4())
            checks.append(self.check_c5())
            checks.append(self.check_c6())
            checks.append(self.check_c7())
            checks.append(self.check_c8())
            checks.append(self.check_c9())
        checks.append(self.check_c10())
        return contract_ok, contract_detail

    # ------------------------------------------------------------------ #
    def build_report(self, contract_ok, contract_detail):
        L = []
        ap = L.append
        ap("HDBF ART VALIDATION REPORT")
        ap("=" * 78)
        ap("mode: %s" % ("FULL (Blender headless: GLB parse + FBX re-import)"
                         if self.full else
                         "REDUCED (GLB-only; FBX re-import NOT EXECUTED)"))
        ap("blender: %s" % (BLENDER_VERSION if BLENDER_VERSION else "not running "
                            "under Blender (bpy unavailable)"))
        ap("python: %s" % sys.version.split()[0])
        ap("numpy: %s" % (np.__version__ if np is not None else
                          "UNAVAILABLE (%s)" % NUMPY_ERR))
        ap("platform: %s" % platform.platform())
        ap("workspace: %s" % self.root)
        ap("assets dir: %s" % os.path.join(self.art, "models"))
        ap("contract: art/atlas_layout.py %s" % contract_detail)
        ap("thresholds: bounds 3.00x1.00x2.00 +-%.2f, pivot (X/Z centre, minY) "
           "+-%.2f, tri budget +-%.0f%%, LOD bbox shrink <=%.0f%%, UV grid %dx%d, "
           "double-covered <=%.2f%% of covered texels, ports +-%.2f"
           % (BOUNDS_TOL, PIVOT_TOL, 100.0 * TRI_TOL,
              100.0 * C.LOD_BBOX_SHRINK_MAX, UV_GRID, UV_GRID,
              100.0 * UV_DOUBLE_MAX, C.PORT_TOLERANCE))
        ap("C9 metric (amended, Lead 2026-10-25): outline-distinguishability at "
           "50%% scale, LOD2 strict / LOD0 reported; pair passes in a view if "
           "mean|dh|>=%.2f or max|dh|>=%.2f grid or area IoU<=%.2f. Raw area-IoU "
           "matrices are informational only (fixed envelope limitation)."
           % (OUTLINE_MEAN_MIN, OUTLINE_MAX_MIN, SIL_IOU_MAX))
        counts = [sum(1 for s in self.specs if s.lod == l) for l in C.LODS]
        ap("expected: %d finished models (%d per LOD) + 1 construction stage, "
           "%d model files + 3 atlas PNGs + material_slots.json = %d files"
           % (len(self.specs) - 1, counts[0],
              2 * len(self.specs), len(self.files)))
        ap("")
        # ---------------- check summary ----------------
        ap("CHECK SUMMARY")
        overall = "PASS"
        reduced = False
        for cid in ("C1", "C2", "C3", "C4", "C5", "C6", "C7", "C8", "C9", "C10"):
            chk = self.checks[cid]
            status = chk.worst()
            if status == "PASS*":
                reduced = True
            if status in ("FAIL", "BLOCKED"):
                overall = "FAIL"
            npass = chk.count("PASS")
            nfail = chk.count("FAIL") + chk.count("BLOCKED")
            nred = chk.count("REDUCED")
            note = ""
            if status == "PASS*":
                note = " (FBX checks REDUCED)"
            elif status == "BLOCKED":
                note = " (BLOCKED: check NOT EXECUTED)"
            elif nfail:
                note = " (%d failing)" % nfail
            ap("[%s] %-2s %-46s %s%s"
               % (status, cid, chk.title, status, note))
        verdict = overall
        if overall == "PASS" and reduced:
            verdict = "PASS (REDUCED: GLB-only, FBX re-import NOT EXECUTED)"
        elif overall == "PASS" and not reduced and not self.full:
            verdict = "PASS (REDUCED: GLB-only, FBX re-import NOT EXECUTED)"
        ap("")
        ap("OVERALL VERDICT: %s" % verdict)
        if not contract_ok:
            ap("CONTRACT ERROR: art/atlas_layout.py validate_contract() FAILED")
        ap("")
        # ---------------- per-model evidence ----------------
        if np is not None and self.loaded:
            ap("PER-MODEL EVIDENCE")
            hdr = "%-28s %-4s %-8s %-10s %-10s %-10s %s" % (
                "model", "src", "tris", "extentX", "extentY", "extentZ",
                "centreX centreZ minY")
            ap(hdr)
            ap("-" * len(hdr))
            for spec in self.specs:
                for source in ("glb", "fbx"):
                    ev = self.evidence[spec.label][source]
                    b = ev.get("bounds")
                    tri = ev.get("tri_total")
                    if b is None:
                        continue
                    ex, ey, ez = b[3] - b[0], b[4] - b[1], b[5] - b[2]
                    cx, cz = (b[0] + b[3]) / 2.0, (b[2] + b[5]) / 2.0
                    ap("%-28s %-4s %-8s %-10.4f %-10.4f %-10.4f %+.4f %+.4f %+.4f"
                       % (spec.label, source.upper(),
                          tri if tri is not None else "n/a", ex, ey, ez,
                          cx, cz, b[1]))
            ap("")
            # per-node triangle counts (GLB; FBX after triangulation if full)
            ap("PER-NODE TRIANGLE COUNTS")
            for spec in self.specs:
                for source in ("glb", "fbx"):
                    per = self.evidence[spec.label][source].get("tri_per_node")
                    if not per:
                        continue
                    body = [c for n, c in per if n == spec.expected_body]
                    ap("%-28s %-4s total=%-5d %s"
                       % (spec.label, source.upper(),
                          sum(c for _n, c in per),
                          " ".join("%s=%d" % (n.replace("SM_HDBF_BoxFarm_", "")
                                              .replace(spec.stem, ""), c)
                                   for n, c in per if n != spec.expected_body)
                          + (" body=%d" % body[0] if body else "")))
            ap("")
            # node / material / UV compact table
            hdr2 = ("%-28s %-4s %-7s %-7s %-26s %-7s %-8s %-8s"
                    % ("model", "src", "troughs", "ports", "materials",
                       "uv_oob", "uv_cov", "uv_dbl"))
            ap("NODES / MATERIALS / UV")
            ap(hdr2)
            ap("-" * len(hdr2))
            for spec in self.specs:
                for source in ("glb", "fbx"):
                    ev = self.evidence[spec.label][source]
                    troughs = ev.get("troughs", "-")
                    ports = ev.get("ports", "-")
                    mats = ev.get("materials", [])
                    mat_s = ",".join(m.replace("M_HDBF_", "") for m in mats)
                    uv = ev.get("uv") or {}
                    ap("%-28s %-4s %-7s %-7s %-26s %-7s %-8s %-8s"
                       % (spec.label, source.upper(),
                          ("6/6" if troughs == "ok" else troughs),
                          ("6/6" if ports == "ok" else ports),
                          mat_s if mat_s else "n/a",
                          uv.get("oob_loops", "n/a"),
                          ("%.1f%%" % (100.0 * uv["covered_ratio"])
                           if uv else "n/a"),
                          ("%.4f%%" % (100.0 * uv["double_ratio"])
                           if uv else "n/a")))
            # worst port/trough errors
            ap("")
            ap("PORT / TROUGH WORST ERRORS (per model, max over all ports/troughs)")
            for spec in self.specs:
                for source in ("glb", "fbx"):
                    ev = self.evidence[spec.label][source]
                    p_pos = ev.get("port_max_pos_err")
                    p_dir = ev.get("port_max_dir_err")
                    if p_pos is None:
                        continue
                    ap("%-28s %-4s port pos %.4f dir %.4f"
                       % (spec.label, source.upper(), p_pos, p_dir))
            ap("")
        # ---------------- failures ----------------
        ap("FAILURES (expected/actual)")
        any_fail = False
        for cid in ("C1", "C2", "C3", "C4", "C5", "C6", "C7", "C8", "C9", "C10"):
            chk = self.checks[cid]
            fails = [(s, t) for s, t in chk.entries if s in ("FAIL", "BLOCKED")]
            if not fails:
                continue
            any_fail = True
            ap("  %s %s:" % (cid, chk.title))
            for status, text in fails:
                ap("    [%s] %s" % (status, text))
        if not any_fail:
            ap("  none")
        ap("")
        # ---------------- notes / infos ----------------
        ap("WARNINGS / INFORMATION")
        any_note = False
        for cid in ("C1", "C2", "C3", "C4", "C5", "C6", "C7", "C8", "C9", "C10"):
            chk = self.checks[cid]
            notes = [(s, t) for s, t in chk.entries if s in ("WARN", "INFO")]
            if not notes:
                continue
            any_note = True
            ap("  %s %s:" % (cid, chk.title))
            for status, text in notes:
                ap("    [%s] %s" % (status, text))
        if not any_note:
            ap("  none")
        ap("")
        # ---------------- verbose detail for key checks ----------------
        ap("DETAIL")
        for cid in ("C7", "C8", "C9"):
            if cid == "C7":
                # per-model UV numbers already live in the tables; here only the
                # epsilon-diagnostic conclusions are repeated for clarity
                diag_entries = [(s, t) for s, t in self.checks[cid].entries
                                if s in ("WARN", "FAIL", "BLOCKED")]
                ap("  %s %s (epsilon diagnostics):" % (cid, self.checks[cid].title))
                for status, text in diag_entries:
                    ap("    [%s] %s" % (status, text))
                continue
            ap("  %s %s:" % (cid, self.checks[cid].title))
            for status, text in self.checks[cid].entries:
                ap("    [%s] %s" % (status, text))
        ap("  end of report")
        ap("=" * 78)
        return "\n".join(L) + "\n"


# --------------------------------------------------------------------------- #
# entry point
# --------------------------------------------------------------------------- #
def _parse_root_arg(argv):
    for i, a in enumerate(argv):
        if a == "--root" and i + 1 < len(argv):
            return os.path.abspath(argv[i + 1])
    return None


def main():
    root = _parse_root_arg(sys.argv[1:]) or DEFAULT_ROOT
    started = None
    if np is not None:
        import time
        started = time.time()
    if C is None:
        out = ("HDBF ART VALIDATION REPORT\n"
               + "=" * 78 + "\n"
               + "OVERALL VERDICT: BLOCKED\n"
               + "reason: art/atlas_layout.py could not be imported from %s\n" % HERE)
        try:
            os.makedirs(os.path.join(root, "art"), exist_ok=True)
            with open(os.path.join(root, "art", "validation-report.txt"), "w",
                      encoding="utf-8", newline="\n") as f:
                f.write(out)
        except Exception:
            pass
        sys.stdout.write(out)
        return 2
    val = Validator(root)
    try:
        contract_ok, contract_detail = val.run()
    except Exception:
        contract_ok = False
        contract_detail = "validator crashed: %s" % traceback.format_exc()
        val.checks["C1"].add("BLOCKED", contract_detail)
    report = val.build_report(contract_ok, contract_detail)
    os.makedirs(val.art, exist_ok=True)
    with open(val.report_path, "w", encoding="utf-8", newline="\n") as f:
        f.write(report)
    # console summary: check statuses + failures (exact expected/actual)
    out = []
    out.append("HDBF validator: %s mode -> %s"
               % ("FULL" if val.full else "REDUCED (GLB-only)", val.report_path))
    overall_fail = False
    for cid in ("C1", "C2", "C3", "C4", "C5", "C6", "C7", "C8", "C9", "C10"):
        chk = val.checks[cid]
        status = chk.worst()
        if status in ("FAIL", "BLOCKED"):
            overall_fail = True
        out.append("  %-3s %-4s %-46s (%d pass / %d fail-or-blocked / %d reduced)"
                   % (cid, status, chk.title, chk.count("PASS"),
                      chk.count("FAIL") + chk.count("BLOCKED"),
                      chk.count("REDUCED")))
        if status in ("FAIL", "BLOCKED"):
            for st, text in chk.entries:
                if st in ("FAIL", "BLOCKED"):
                    out.append("    [%s] %s" % (st, text))
    if overall_fail:
        out.append("VERDICT: FAIL")
    elif val.full:
        out.append("VERDICT: PASS (FULL: GLB parsed + FBX re-imported)")
    else:
        out.append("VERDICT: PASS (REDUCED: FBX re-import checks NOT EXECUTED; "
                   "run under Blender for the full gate)")
    if started is not None:
        import time
        out.append("elapsed: %.1fs" % (time.time() - started))
    text = "\n".join(out)
    sys.stdout.write(text + "\n")
    try:
        sys.stdout.flush()
        sys.stderr.flush()
    except Exception:
        pass
    return 1 if overall_fail else 0


if __name__ == "__main__":
    try:
        code = main()
    except SystemExit:
        raise
    except Exception:
        traceback.print_exc()
        code = 1
    try:
        sys.stdout.flush()
        sys.stderr.flush()
    except Exception:
        pass
    os._exit(code)
