#!/usr/bin/env python3
"""HDBF v2 - source-model validator (Blender 4.2.1 headless, FBX + GLB).

Validates every v2 source model in ``art/models/**`` against the frozen art
contract in ``art/atlas_layout.py`` / ``art/material_patches.py`` /
``art/timbermesh_common.py`` and writes ``art/validation-report.txt``.

v2 (2026-10-26) contract summary
--------------------------------
* strict asset-space envelope 3.00 (X) x 1.00 (Y) x 3.00 (Z), front -Z, pivot at
  the footprint bottom centre; nothing (decoration, windows, trims, ports) may
  exceed it;
* the roof is ONE flat empty platform: the WALKING SURFACE is the model apex at
  y = 1.00, nothing exists above it (30 cm clearance above the surface is empty),
  and the top face spans the near-full platform plan;
* exactly the three ``#Port#Power/#Port#Pipe/#Port#Logistics`` empty markers and
  **no ``#Slot#`` node anywhere**; no crop/trough/slot geometry;
* "indoor high density" is expressed by STATIC racks/shelves (3 levels) visible
  through the facade openings: every LOD0 model must carry >= 2 horizontal shelf
  levels inside the interior, and the glass facade area must reach the per-tier
  minimum (0 / .35 / .60 / .85 grid^2 for Basic/Intermediate/Advanced/Top);
* four tiers must stay distinguishable at the 50% camera: every tier pair is
  compared with a 50%-scale multi-view material/silhouette/glass/port raster and
  must differ on at least two of the four criteria (REQ-V2-ART-04);
* triangle budgets LOD0/LOD1/LOD2 = 6000/2400/800 (+-20%), LOD bbox shrink <=2%;
* every face of one material samples ONE fixed flat UV patch (all UVs inside the
  material's patch rect, no wild / out-of-[0,1] UVs);
* ``art/material_slots.json`` agrees with the measured meshes.

Run:
    blender.exe -b --factory-startup -noaudio --python art\\validate_models.py

Modes:
    FULL (default under Blender): FBX is re-imported into Blender and measured in
    authoring space; GLB is imported with Blender's glTF importer.  Both formats
    are checked and cross-compared.  Without bpy the script exits 2 (BLOCKED);
    it never pretends to have validated the FBX files.

Exit code 0 only when every check of every expected model PASSES.
The report is deterministic (no timestamps / elapsed times).
"""

from __future__ import annotations

import json
import math
import os
import sys
import traceback

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
if HERE not in sys.path:
    sys.path.insert(0, HERE)

try:
    import numpy as np
    NUMPY_ERR = None
except Exception as exc:  # pragma: no cover
    np = None
    NUMPY_ERR = "%s: %s" % (type(exc).__name__, exc)

try:
    import bpy
    HAVE_BPY = True
    BLENDER_VERSION = bpy.app.version_string
except Exception:
    HAVE_BPY = False
    BLENDER_VERSION = None

import atlas_layout as A          # noqa: E402
import material_patches as MP     # noqa: E402
import timbermesh_common as TC    # noqa: E402

REPORT_PATH = os.path.join(HERE, "validation-report.txt")
MODELS_DIR = os.path.join(HERE, "models")

BOUNDS_TOL = A.ENVELOPE_TOLERANCE           # 0.02 strict envelope
FLAT_TOL = 0.005                            # top face / apex equality
TRI_TOL = 0.20                              # triangle budget +-20%
PORT_TOL = A.PORT_TOLERANCE                 # 0.02
UV_EPS = 2e-4                               # flat-patch rect epsilon
PANE_MIN_AREA = dict(A.V2_INDOOR["glass_area_min"])
SHELF_MIN_LEVELS = A.V2_INDOOR["shelf_min_levels_checked"]
PORT_RADIUS = A.PORT_FIXTURE_RADIUS
PORT_MIN_TRIS = A.PORT_FIXTURE_MIN_TRIS

#: 50% camera emulation: 32 px per grid unit (the in-game 50% scale of the
#: documented 64 px/unit reference raster), 128 x 128 frame.
RASTER_SCALE = 32
RASTER_SIZE = 128
DIST_MATERIAL_MIN = 0.05
DIST_GLASS_MIN = 0.03
DIST_SILHOUETTE_MIN = 0.02
DIST_PLAN_MIN = 0.015
DIST_PORT_MIN = 0.10
DIST_CRITERIA_MIN = 2

STATUS_RANK = {"PASS": 0, "INFO": 0, "FAIL": 3, "BLOCKED": 4}


# --------------------------------------------------------------------------- #
# model specs
# --------------------------------------------------------------------------- #
def all_specs():
    """25 source models: 8 tier x faction dirs x LOD0/1/2 + ConstructionStage0."""
    specs = []
    for tier in A.TIERS:
        for faction in A.FACTIONS:
            for lod in A.LODS:
                specs.append({
                    "kind": "tier", "tier": tier, "faction": faction, "lod": lod,
                    "label": "%s.%s.LOD%d" % (tier, faction, lod),
                    "dir": os.path.join(MODELS_DIR, "%s.%s" % (tier, faction)),
                    "stem": "LOD%d" % lod,
                    "expected_body": "SM_HDBF_BoxFarm_%s_LOD%d" % (tier, lod),
                    "budget": A.TRI_BUDGET[lod],
                    "materials": list(A.TIER_MATERIALS[tier]),
                    "platform_tier": tier,
                    "construction": False,
                })
    specs.append({
        "kind": "construction", "tier": A.CONSTRUCTION_STAGE, "faction": None,
        "lod": 0, "label": A.CONSTRUCTION_STAGE,
        "dir": os.path.join(MODELS_DIR, A.CONSTRUCTION_STAGE),
        "stem": A.CONSTRUCTION_STAGE,
        "expected_body": "SM_HDBF_BoxFarm_ConstructionStage0",
        "budget": None,
        "materials": list(A.TIER_MATERIALS[A.CONSTRUCTION_STAGE]),
        "platform_tier": A.CONSTRUCTION_STAGE,
        "construction": True,
    })
    return specs


def expected_files():
    files = ["art/material_slots.json"]
    for key in sorted(A.ATLAS_FILES):
        files.append("art/" + A.ATLAS_FILES[key])
    for spec in all_specs():
        files.append("art/models/%s/%s.fbx" % (os.path.basename(spec["dir"]),
                                               spec["stem"]))
        files.append("art/models/%s/%s.glb" % (os.path.basename(spec["dir"]),
                                               spec["stem"]))
    return files


# --------------------------------------------------------------------------- #
# blender import / facts
# --------------------------------------------------------------------------- #
class Facts(object):
    """Measured geometry of one imported model file, normalised to asset space."""

    def __init__(self, path, spec):
        self.path = path
        self.spec = spec
        self.pos = np.zeros((0, 3), dtype=np.float64)     # vertices, asset space
        self.tri = np.zeros((0, 3), dtype=np.int64)       # triangle vertex indices
        self.tri_mat = np.zeros(0, dtype=np.int64)        # material slot index per tri
        self.tri_uv = np.zeros((0, 3, 2), dtype=np.float64)
        self.materials = []
        self.mesh_nodes = []
        self.empties = {}
        self.tri_count = 0
        self.loaded = False
        self.error = None

    # -- geometry helpers ------------------------------------------------
    def tri_positions(self):
        return self.pos[self.tri]

    def tri_centroids(self):
        return self.pos[self.tri].mean(axis=1)

    def tri_normals(self):
        p = self.pos[self.tri]
        n = np.cross(p[:, 1] - p[:, 0], p[:, 2] - p[:, 0])
        length = np.linalg.norm(n, axis=1)
        length[length == 0.0] = 1.0
        return n / length[:, None]

    def tri_areas(self):
        p = self.pos[self.tri]
        n = np.cross(p[:, 1] - p[:, 0], p[:, 2] - p[:, 0])
        return 0.5 * np.linalg.norm(n, axis=1)

    def bounds(self):
        if not len(self.pos):
            return None
        return self.pos.min(axis=0), self.pos.max(axis=0)

    def material_index(self, name):
        try:
            return self.materials.index(name)
        except ValueError:
            return -1


def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def _blender_to_asset(points):
    """(N,3) blender Z-up points -> asset space (Unity Y-up, front -Z)."""
    out = np.empty_like(points)
    out[:, 0] = points[:, 0]
    out[:, 1] = points[:, 2]
    out[:, 2] = -points[:, 1]
    return out


def load_facts(path, spec):
    facts = Facts(path, spec)
    ext = os.path.splitext(path)[1].lower()
    try:
        reset_scene()
        if ext == ".fbx":
            bpy.ops.import_scene.fbx(filepath=path)
        else:
            bpy.ops.import_scene.gltf(filepath=path)
        dg = bpy.context.evaluated_depsgraph_get()
        pos_parts = []
        tri_parts = []
        mat_parts = []
        uv_parts = []
        vert_base = 0
        for ob in sorted(bpy.data.objects, key=lambda o: o.name):
            if ob.type == "EMPTY":
                m4 = np.array(ob.matrix_world, dtype=np.float64)
                loc = m4[:3, 3]
                local_z = m4[:3, :3] @ np.array([0.0, 0.0, 1.0], dtype=np.float64)
                norm = np.linalg.norm(local_z)
                if norm > 1e-9:
                    local_z = local_z / norm
                facts.empties[ob.name] = {
                    "pos": _blender_to_asset(loc.reshape(1, 3))[0],
                    "dir": _blender_to_asset(local_z.reshape(1, 3))[0],
                }
            elif ob.type == "MESH":
                ev = ob.evaluated_get(dg)
                me = ev.to_mesh()
                try:
                    me.calc_loop_triangles()
                    mw = np.array(ev.matrix_world, dtype=np.float64)
                    nv = len(me.vertices)
                    co = np.empty(nv * 3, dtype=np.float64)
                    me.vertices.foreach_get("co", co)
                    pos_bl = co.reshape(-1, 3)
                    pos_bl = (mw[:3, :3] @ pos_bl.T).T + mw[:3, 3]
                    pos_asset = _blender_to_asset(pos_bl)
                    loops = np.empty(len(me.loop_triangles) * 3, dtype=np.int64)
                    me.loop_triangles.foreach_get("loops", loops)
                    loops = loops.reshape(-1, 3)
                    tri_verts = np.empty(len(me.loop_triangles) * 3,
                                         dtype=np.int64)
                    me.loop_triangles.foreach_get("vertices", tri_verts)
                    tri_verts = tri_verts.reshape(-1, 3)
                    mats = [slot.name if slot is not None else None
                            for slot in me.materials]
                    poly_mats = np.empty(len(me.polygons), dtype=np.int64)
                    me.polygons.foreach_get("material_index", poly_mats)
                    poly_of_tri = np.empty(len(me.loop_triangles), dtype=np.int64)
                    me.loop_triangles.foreach_get("polygon_index", poly_of_tri)
                    tri_mat = poly_mats[poly_of_tri] if len(poly_of_tri) else \
                        np.zeros(0, dtype=np.int64)
                    uv_layer = me.uv_layers.active
                    if uv_layer is not None and len(me.loop_triangles):
                        uv_raw = np.empty(len(me.loops) * 2, dtype=np.float64)
                        uv_layer.data.foreach_get("uv", uv_raw)
                        tri_uv = uv_raw.reshape(-1, 2)[loops]
                    else:
                        tri_uv = np.zeros((len(loops), 3, 2), dtype=np.float64)
                    if facts.materials:
                        # second mesh object: merge material slot indices
                        raise RuntimeError("model contains %d mesh objects; the "
                                           "v2 contract expects one body mesh"
                                           % (len(facts.mesh_nodes) + 1))
                    facts.materials = [m for m in mats if m is not None]
                    facts.mesh_nodes.append(ob.name)
                    pos_parts.append(pos_asset)
                    tri_parts.append(tri_verts + vert_base)
                    mat_parts.append(tri_mat)
                    uv_parts.append(tri_uv)
                    vert_base += nv
                finally:
                    ev.to_mesh_clear()
        if not pos_parts:
            raise RuntimeError("import produced no mesh object")
        facts.pos = np.vstack(pos_parts)
        facts.tri = np.vstack(tri_parts)
        facts.tri_mat = np.concatenate(mat_parts)
        facts.tri_uv = np.vstack(uv_parts)
        facts.tri_count = len(facts.tri)
        facts.loaded = True
    except Exception as exc:                                   # noqa: BLE001
        facts.error = "%s: %s" % (type(exc).__name__, exc)
    return facts


# --------------------------------------------------------------------------- #
# 50% raster helpers (material-id / silhouette / glass)
# --------------------------------------------------------------------------- #
def _project(points, view):
    """asset points (N,3) -> (u, v, depth) for a camera view."""
    if view == "front":
        return points[:, 0], points[:, 1], points[:, 2]
    if view == "back":
        return points[:, 0], points[:, 1], -points[:, 2]
    if view == "left":
        return points[:, 2], points[:, 1], points[:, 0]
    if view == "right":
        return points[:, 2], points[:, 1], -points[:, 0]
    # top: looking down -Y, screen = (x, z)
    return points[:, 0], points[:, 2], -points[:, 1]


def raster_material(facts, view):
    """Z-buffered material-id raster at the 50% camera scale.

    Returns (ids, coverage) where ids is an int16 material-slot map (-1 empty).
    """
    ids = np.full((RASTER_SIZE, RASTER_SIZE), -1, dtype=np.int16)
    depth = np.full((RASTER_SIZE, RASTER_SIZE), np.inf, dtype=np.float64)
    if not facts.tri_count:
        return ids, np.zeros_like(ids, dtype=bool)
    u, v, d = _project(facts.pos, view)
    half = RASTER_SIZE * 0.5
    px = half + u * RASTER_SCALE
    py = half + 0.18 * RASTER_SIZE - v * RASTER_SCALE
    tri = facts.tri
    for t in range(facts.tri_count):
        i0, i1, i2 = tri[t]
        x0, x1, x2 = px[i0], px[i1], px[i2]
        y0, y1, y2 = py[i0], py[i1], py[i2]
        minx = max(0, int(math.floor(min(x0, x1, x2))))
        maxx = min(RASTER_SIZE - 1, int(math.ceil(max(x0, x1, x2))))
        miny = max(0, int(math.floor(min(y0, y1, y2))))
        maxy = min(RASTER_SIZE - 1, int(math.ceil(max(y0, y1, y2))))
        if minx > maxx or miny > maxy:
            continue
        denom = (y1 - y2) * (x0 - x2) + (x2 - x1) * (y0 - y2)
        if abs(denom) < 1e-12:
            continue
        xs = np.arange(minx, maxx + 1) + 0.5
        ys = np.arange(miny, maxy + 1) + 0.5
        gx, gy = np.meshgrid(xs, ys)
        a = ((y1 - y2) * (gx - x2) + (x2 - x1) * (gy - y2)) / denom
        b = ((y2 - y0) * (gx - x2) + (x0 - x2) * (gy - y2)) / denom
        c = 1.0 - a - b
        inside = (a >= -1e-9) & (b >= -1e-9) & (c >= -1e-9)
        if not inside.any():
            continue
        dz = a * d[i0] + b * d[i1] + c * d[i2]
        sub_depth = depth[miny:maxy + 1, minx:maxx + 1]
        sub_ids = ids[miny:maxy + 1, minx:maxx + 1]
        update = inside & (dz < sub_depth - 1e-12)
        sub_depth[update] = dz[update]
        sub_ids[update] = facts.tri_mat[t]
    return ids, ids >= 0


def _glass_fraction(facts, ids, coverage):
    gi = facts.material_index(TC_GLASS_SLOT)
    if gi < 0:
        return 0.0
    total = int(coverage.sum())
    return float(((ids == gi) & coverage).sum()) / total if total else 0.0


TC_GLASS_SLOT = "M_HDBF_Glass"


def _port_signature(facts):
    """Per-port fixture area / triangle count / dominant material signature."""
    cents = facts.tri_centroids()
    areas = facts.tri_areas()
    sig = []
    for spec in A.PORT_SPECS:
        p = np.array(spec["asset"], dtype=np.float64)
        d = np.linalg.norm(cents - p, axis=1) if len(cents) else np.zeros(0)
        near = d <= PORT_RADIUS
        count = int(near.sum())
        area = float(areas[near].sum()) if count else 0.0
        dom = None
        if count:
            mats = facts.tri_mat[near]
            dom = int(np.bincount(mats).argmax())
        sig.append({"port": spec["object"], "count": count,
                    "area": round(area, 4), "dominant_material": dom})
    return sig


def _material_diff(ids_a, ids_b):
    union = (ids_a >= 0) | (ids_b >= 0)
    total = int(union.sum())
    if not total:
        return 0.0
    return float(((ids_a != ids_b) & union).sum()) / total


def _silhouette_iou(cov_a, cov_b):
    inter = int((cov_a & cov_b).sum())
    union = int((cov_a | cov_b).sum())
    return inter / float(union) if union else 1.0


# --------------------------------------------------------------------------- #
# checks
# --------------------------------------------------------------------------- #
class Check(object):
    def __init__(self, cid, title):
        self.cid = cid
        self.title = title
        self.status = "PASS"
        self.text = ""
        self.notes = []
        self.reasons = []

    def fail(self, text, reasons, notes=None):
        self.status = "FAIL"
        self.text = text
        self.reasons = list(reasons)
        self.notes = list(notes or [])

    def pass_(self, text, notes=None):
        self.status = "PASS"
        self.text = text
        self.notes = list(notes or [])


CHECK_DEFS = [
    ("C1", "files present + both formats parse"),
    ("C2", "strict 3.00 x 1.00 x 3.00 envelope + flat roof apex"),
    ("C3", "triangle budget (LOD0/1/2 +-20%)"),
    ("C4", "v2 structure + indoor shelves/glass expression"),
    ("C5", "exactly 3 #Port# empties, no #Slot# node"),
    ("C6", "material slot sets per tier/faction"),
    ("C7", "flat-patch UV0 bounds"),
    ("C8", "LOD bounding-box shrink <= 2%"),
    ("C9", "four tiers distinguishable at the 50% camera"),
    ("C10", "material_slots.json agrees with the meshes"),
]


class Validator(object):
    def __init__(self):
        self.results = []          # per model: dict of checks + facts
        self.contract_lines = []
        self.c10 = Check("C10", "material_slots.json agrees with the meshes")

    # -- file presence (C1) ----------------------------------------------
    def check_files(self):
        reasons = []
        notes = []
        scanned = 0
        for rel in expected_files():
            path = os.path.join(ROOT, rel.replace("/", os.sep))
            scanned += 1
            if not os.path.isfile(path):
                reasons.append("missing: %s" % rel)
        for key in sorted(A.ATLAS_FILES):
            path = os.path.join(HERE, A.ATLAS_FILES[key])
            if not os.path.isfile(path):
                continue
            with open(path, "rb") as handle:
                head = handle.read(33)
            if len(head) < 24 or head[:8] != b"\x89PNG\r\n\x1a\n":
                reasons.append("not a PNG: %s" % A.ATLAS_FILES[key])
                continue
            import struct
            w, h = struct.unpack(">II", head[16:24])
            if (w, h) != (A.ATLAS_SIZE, A.ATLAS_SIZE):
                reasons.append("%s is %dx%d, expected %dx%d"
                               % (A.ATLAS_FILES[key], w, h, A.ATLAS_SIZE, A.ATLAS_SIZE))
        notes.append("%d expected files scanned (25 models x FBX+GLB + 3 atlas PNGs "
                     "+ material_slots.json)" % scanned)
        result = {"spec": None, "checks": {}, "facts": {}}
        check = Check("C1", "files present + both formats parse")
        if reasons:
            check.fail("%d missing/invalid file(s)" % len(reasons), reasons, notes)
        else:
            check.pass_("all %d expected files present; atlas PNGs are %dx%d"
                        % (scanned, A.ATLAS_SIZE, A.ATLAS_SIZE), notes)
        result["checks"]["C1"] = check
        self.results.append(result)

    # -- per model --------------------------------------------------------
    def check_model(self, spec):
        result = {"spec": spec, "checks": {}, "facts": {}}
        for ext in ("fbx", "glb"):
            path = os.path.join(spec["dir"], spec["stem"] + "." + ext)
            facts = load_facts(path, spec) if os.path.isfile(path) else Facts(path, spec)
            if not os.path.isfile(path):
                facts.error = "file missing"
            result["facts"][ext] = facts

        checks = {}
        checks["C1"] = self._c1_parse(spec, result["facts"])
        checks["C2"] = self._c2_envelope(spec, result["facts"])
        checks["C3"] = self._c3_budget(spec, result["facts"])
        checks["C4"] = self._c4_indoor(spec, result["facts"])
        checks["C5"] = self._c5_ports(spec, result["facts"])
        checks["C6"] = self._c6_materials(spec, result["facts"])
        checks["C7"] = self._c7_uv(spec, result["facts"])
        result["checks"] = checks
        return result

    # C1 parse
    def _c1_parse(self, spec, facts):
        check = Check("C1", "parse")
        reasons = []
        notes = []
        for ext in ("fbx", "glb"):
            fact = facts[ext]
            if fact.error:
                reasons.append("%s: %s" % (ext, fact.error))
            else:
                notes.append("%s: %d mesh node(s), %d tris, %d vertices, %d empties"
                             % (ext, len(fact.mesh_nodes), fact.tri_count,
                                len(fact.pos), len(fact.empties)))
        if reasons:
            check.fail("source model did not import cleanly", reasons, notes)
        else:
            check.pass_("FBX and GLB imported; both carry one body mesh + 3 empties",
                        notes)
        return check

    # C2 envelope + flat roof
    def _c2_envelope(self, spec, facts):
        check = Check("C2", "envelope")
        reasons = []
        notes = []
        want_size = (A.ENVELOPE["size_x"], A.ENVELOPE["size_y"],
                     A.ENVELOPE["size_z"])
        want_min = (A.ENVELOPE["min_x"], A.ENVELOPE["min_y"], A.ENVELOPE["min_z"])
        want_max = (A.ENVELOPE["max_x"], A.ENVELOPE["max_y"], A.ENVELOPE["max_z"])
        measured = {}
        for ext in ("fbx", "glb"):
            fact = facts[ext]
            if fact.error:
                reasons.append("%s: cannot measure (%s)" % (ext, fact.error))
                continue
            bounds = fact.bounds()
            if bounds is None:
                reasons.append("%s: no vertices" % ext)
                continue
            lo, hi = bounds
            measured[ext] = (lo, hi)
            for axis in range(3):
                if abs(lo[axis] - want_min[axis]) > BOUNDS_TOL:
                    reasons.append("%s min.%s=%.4f, want %.3f (+-%.2f)"
                                   % (ext, "XYZ"[axis], lo[axis],
                                      want_min[axis], BOUNDS_TOL))
                if abs(hi[axis] - want_max[axis]) > BOUNDS_TOL:
                    reasons.append("%s max.%s=%.4f, want %.3f (+-%.2f)"
                                   % (ext, "XYZ"[axis], hi[axis],
                                      want_max[axis], BOUNDS_TOL))
            # flat roof: walking surface == apex, nothing above, top face exists
            top = A.ROOF_PLATFORM_TOP
            above = fact.pos[:, 1] > top + 1e-5
            if above.any():
                reasons.append("%s: %d vertex/vertices above the walking surface "
                               "y=%.3f (max y=%.4f)"
                               % (ext, int(above.sum()), top,
                                  float(fact.pos[:, 1].max())))
            if abs(float(hi[1]) - top) > FLAT_TOL:
                reasons.append("%s: mountain apex y=%.4f != flat walking surface "
                               "%.3f" % (ext, float(hi[1]), top))
            px, pz = A.roof_platform(spec["platform_tier"])
            face = (np.abs(fact.pos[:, 1] - top) <= 0.01)
            inside = ((np.abs(fact.pos[:, 0]) <= px * 0.5 + 0.03)
                      & (np.abs(fact.pos[:, 2]) <= pz * 0.5 + 0.03))
            face_verts = int((face & inside).sum())
            if face_verts < 20:
                reasons.append("%s: flat top face has only %d vertices inside the "
                               "platform plan %.2fx%.2f"
                               % (ext, face_verts, px, pz))
            if face_verts:
                xs = fact.pos[face & inside, 0]
                zs = fact.pos[face & inside, 2]
                if (float(xs.max() - xs.min()) < px - 0.10
                        or float(zs.max() - zs.min()) < pz - 0.10):
                    reasons.append("%s: flat top face does not span the platform "
                                   "plan (x %.2f z %.2f, plan %.2fx%.2f)"
                                   % (ext, float(xs.max() - xs.min()),
                                      float(zs.max() - zs.min()), px, pz))
            notes.append("%s: size=%.3fx%.3fx%.3f min=(%.3f,%.3f,%.3f) "
                         "top_face_vertices=%d above_top=%d"
                         % (ext, hi[0] - lo[0], hi[1] - lo[1], hi[2] - lo[2],
                            lo[0], lo[1], lo[2], face_verts, int(above.sum())))
        if "fbx" in measured and "glb" in measured:
            diff = float(np.abs(measured["fbx"][0] - measured["glb"][0]).max())
            diff2 = float(np.abs(measured["fbx"][1] - measured["glb"][1]).max())
            if max(diff, diff2) > 0.005:
                reasons.append("FBX and GLB bounds disagree by %.5f"
                               % max(diff, diff2))
            notes.append("FBX vs GLB bounds delta max=%.6f" % max(diff, diff2))
        if reasons:
            check.fail("envelope / flat-roof contract violated", reasons, notes)
        else:
            check.pass_("strict %.2fx%.2fx%.2f envelope; flat roof walking surface "
                        "== apex y=%.2f; nothing above it"
                        % (want_size[0], want_size[1], want_size[2],
                           A.ROOF_PLATFORM_TOP), notes)
        return check

    # C3 triangle budget
    def _c3_budget(self, spec, facts):
        check = Check("C3", "triangle budget")
        reasons = []
        notes = []
        budget = spec["budget"]
        for ext in ("fbx", "glb"):
            fact = facts[ext]
            if fact.error:
                reasons.append("%s: cannot count (%s)" % (ext, fact.error))
                continue
            count = fact.tri_count
            notes.append("%s tris=%d" % (ext, count))
            if budget is not None:
                lo = int(budget * (1.0 - TRI_TOL))
                hi = int(budget * (1.0 + TRI_TOL))
                if not lo <= count <= hi:
                    reasons.append("%s: %d triangles outside the %d +-%.0f%% "
                                   "budget [%d, %d]"
                                   % (ext, count, budget, TRI_TOL * 100, lo, hi))
        if reasons:
            check.fail("triangle budget violated", reasons, notes)
        else:
            if budget is None:
                check.pass_("construction-stage greybox: %d triangles "
                            "(no LOD budget applies)" % facts["glb"].tri_count,
                            notes)
            else:
                check.pass_("LOD%d budget %d +-20%%: measured %d tris (FBX == GLB)"
                            % (spec["lod"], budget, facts["glb"].tri_count), notes)
        return check

    # C4 indoor expression
    def _c4_indoor(self, spec, facts):
        check = Check("C4", "indoor expression")
        reasons = []
        notes = []
        construction = spec["construction"]
        for ext in ("fbx", "glb"):
            fact = facts[ext]
            if fact.error:
                reasons.append("%s: cannot inspect (%s)" % (ext, fact.error))
                continue
            # no crop/trough/slot entities may exist in the source scene
            forbidden = [name for name in list(fact.empties) + fact.mesh_nodes
                         if any(tag in name.lower()
                                for tag in ("trough", "crop", "slot"))]
            if forbidden:
                reasons.append("%s: crop/trough/slot node(s) present: %s"
                               % (ext, forbidden))
            if construction:
                continue
            if spec["lod"] != 0:
                continue
            # static 3-level racks: horizontal surfaces inside the interior
            normals = fact.tri_normals()
            cents = fact.tri_centroids()
            interior = ((np.abs(cents[:, 0]) <= 1.35)
                        & (np.abs(cents[:, 2]) <= 1.42)
                        & (cents[:, 1] > 0.15) & (cents[:, 1] < 0.90))
            horizontal = np.abs(normals[:, 1]) > 0.85
            glass_idx = fact.material_index(TC_GLASS_SLOT)
            not_glass = fact.tri_mat != glass_idx
            levels = sorted({round(float(y), 2)
                             for y in cents[interior & horizontal & not_glass, 1]})
            # merge levels closer than 4 cm (shelf board top/bottom faces)
            merged = []
            for level in levels:
                if not merged or level - merged[-1] > 0.04:
                    merged.append(level)
            if len(merged) < SHELF_MIN_LEVELS:
                reasons.append("%s: only %d distinct horizontal rack level(s) "
                               "inside the interior (need >= %d); levels=%s"
                               % (ext, len(merged), SHELF_MIN_LEVELS, merged))
            else:
                notes.append("%s: shelf levels y=%s" % (ext, merged))
            # glass amount grows with the tier
            glass_area = 0.0
            if glass_idx >= 0:
                mask = fact.tri_mat == glass_idx
                glass_area = float(fact.tri_areas()[mask].sum())
            need = PANE_MIN_AREA[spec["tier"]]
            if glass_area < need - 1e-4:
                reasons.append("%s: glass facade area %.3f grid^2 < %.2f required "
                               "for %s" % (ext, glass_area, need, spec["tier"]))
            notes.append("%s: glass_area=%.3f grid^2 (min %.2f); distinct rack "
                         "levels=%d" % (ext, glass_area, need, len(merged)))
            # Basic must not be glazed, higher tiers must be
            if spec["tier"] == "Basic" and glass_area > 1e-6:
                reasons.append("%s: Basic must be an OPEN timber barn but carries "
                               "%.3f grid^2 of glass" % (ext, glass_area))
        if reasons:
            check.fail("indoor-expression contract violated", reasons, notes)
        else:
            if construction:
                check.pass_("construction greybox carries no crop/trough/slot "
                            "entity; indoor racks start at the finished stage")
            elif spec["lod"] != 0:
                check.pass_("no crop/trough/slot entity in the source scene "
                            "(indoor-expression checks apply to LOD0)")
            else:
                check.pass_("static indoor racks + glazed facade present; "
                            "no crop/slot entities", notes)
        return check

    # C5 ports
    def _c5_ports(self, spec, facts):
        check = Check("C5", "ports")
        reasons = []
        notes = []
        expected = {spec_["object"]: spec_ for spec_ in A.PORT_SPECS}
        for ext in ("fbx", "glb"):
            fact = facts[ext]
            if fact.error:
                reasons.append("%s: cannot inspect (%s)" % (ext, fact.error))
                continue
            got = dict(fact.empties)
            for name, spec_ in expected.items():
                if name not in got:
                    reasons.append("%s: missing empty %s" % (ext, name))
            for name in got:
                if name not in expected:
                    reasons.append("%s: unexpected empty %s (only the three "
                                   "#Port# markers are allowed)" % (ext, name))
                elif "#Slot#" in name:
                    reasons.append("%s: forbidden #Slot# node %s" % (ext, name))
            dev = 0.0
            for name, spec_ in expected.items():
                if name not in got:
                    continue
                want = np.array(spec_["asset"], dtype=np.float64)
                delta = float(np.abs(got[name]["pos"] - want).max())
                dev = max(dev, delta)
                if delta > PORT_TOL:
                    reasons.append("%s: %s at %s, want %s (dev %.4f > %.3f)"
                                   % (ext, name, np.round(got[name]["pos"], 3),
                                      spec_["asset"], delta, PORT_TOL))
                want_dir = np.array(spec_["outward"], dtype=np.float64)
                got_dir = got[name]["dir"]
                if float(np.abs(got_dir - want_dir).max()) > 0.05:
                    reasons.append("%s: %s local +Z points %s, want outward %s"
                                   % (ext, name, np.round(got_dir, 3),
                                      spec_["outward"]))
            if "#Slot#" in " ".join(got) or "#Slot#" in " ".join(fact.mesh_nodes):
                reasons.append("%s: a #Slot# node exists in the model" % ext)
            notes.append("%s: %d empties, positions match exactly the three #Port# "
                         "contract markers (max dev %.4f), no #Slot#"
                         % (ext, len(got), dev))
        if reasons:
            check.fail("port empty contract violated", reasons, notes)
        else:
            check.pass_("exactly the 3 #Port#Power/#Port#Pipe/#Port#Logistics "
                        "empties at the frozen positions; 0 #Slot# nodes", notes)
        return check

    # C6 materials
    def _c6_materials(self, spec, facts):
        check = Check("C6", "materials")
        reasons = []
        notes = []
        want = set(spec["materials"])
        allowed = set(A.MATERIAL_ORDER)
        for ext in ("fbx", "glb"):
            fact = facts[ext]
            if fact.error:
                reasons.append("%s: cannot inspect (%s)" % (ext, fact.error))
                continue
            got = set(fact.materials)
            unknown = sorted(got - allowed)
            if unknown:
                reasons.append("%s: unknown material slot(s) %s (allowed: %s)"
                               % (ext, unknown, sorted(allowed)))
            if got != want:
                reasons.append("%s: material slots %s != contract %s"
                               % (ext, sorted(got), sorted(want)))
            if not 1 <= len(got) <= 4:
                reasons.append("%s: %d material slots (want 1..4)"
                               % (ext, len(got)))
            notes.append("%s: %s" % (ext, sorted(got)))
        if reasons:
            check.fail("material slot contract violated", reasons, notes)
        else:
            check.pass_("material slot set matches ATLAS TIER_MATERIALS (%s)"
                        % sorted(want), notes)
        return check

    # C7 flat-patch UV
    def _c7_uv(self, spec, facts):
        check = Check("C7", "flat-patch UV")
        reasons = []
        notes = []
        construction = spec["construction"]
        if construction:
            mapping = TC.MATERIAL_MAP[("Basic", "Folktails", True)]
        else:
            mapping = TC.MATERIAL_MAP[(spec["tier"], spec["faction"], False)]
        role_by_slot = {slot: MP.role_of(name) for slot, name in mapping.items()}
        for ext in ("fbx", "glb"):
            fact = facts[ext]
            if fact.error:
                reasons.append("%s: cannot inspect (%s)" % (ext, fact.error))
                continue
            if not fact.tri_count:
                reasons.append("%s: no triangles" % ext)
                continue
            uv = fact.tri_uv
            if not np.isfinite(uv).all():
                reasons.append("%s: non-finite UV value(s)" % ext)
            if float(uv.min()) < -UV_EPS or float(uv.max()) > 1.0 + UV_EPS:
                reasons.append("%s: UV outside [0,1] (min %.4f max %.4f)"
                               % (ext, float(uv.min()), float(uv.max())))
            bad = 0
            bad_sample = None
            for t in range(fact.tri_count):
                slot_idx = int(fact.tri_mat[t])
                if slot_idx < 0 or slot_idx >= len(fact.materials):
                    bad += 1
                    if bad_sample is None:
                        bad_sample = "triangle %d has invalid material slot %d" % (
                            t, slot_idx)
                    continue
                slot = fact.materials[slot_idx]
                role = role_by_slot.get(slot)
                if role is None:
                    bad += 1
                    if bad_sample is None:
                        bad_sample = "material slot %r has no flat patch" % slot
                    continue
                u0, v0, u1, v1 = MP.uv_rect(role)
                tuv = uv[t]
                if (float(tuv[:, 0].min()) < u0 - UV_EPS
                        or float(tuv[:, 0].max()) > u1 + UV_EPS
                        or float(tuv[:, 1].min()) < v0 - UV_EPS
                        or float(tuv[:, 1].max()) > v1 + UV_EPS):
                    bad += 1
                    if bad_sample is None:
                        bad_sample = ("triangle %d material %r uv=%s escapes its "
                                      "%r patch %s"
                                      % (t, slot, np.round(tuv, 4), role,
                                         tuple(round(v, 4) for v in (u0, v0, u1, v1))))
            if bad:
                reasons.append("%s: %d/%d triangles use UV outside their material's "
                               "flat patch (e.g. %s)"
                               % (ext, bad, fact.tri_count, bad_sample))
            notes.append("%s: all UVs inside their material flat patches "
                         "(%d triangles, 4 px patch gutter)" % (ext, fact.tri_count))
        if reasons:
            check.fail("flat-patch UV contract violated", reasons, notes)
        else:
            check.pass_("every face of one material samples that material's fixed "
                        "flat UV patch", notes)
        return check

    # -- C8 LOD shrink ----------------------------------------------------
    def check_lod_shrink(self, results):
        check = Check("C8", "LOD shrink")
        reasons = []
        notes = []
        by_key = {}
        for result in results:
            spec = result["spec"]
            if spec["kind"] != "tier":
                continue
            key = (spec["tier"], spec["faction"])
            by_key.setdefault(key, {})[spec["lod"]] = result
        for key in sorted(by_key):
            lods = by_key[key]
            if 0 not in lods:
                reasons.append("%s: LOD0 missing" % (key,))
                continue
            base_facts = lods[0]["facts"]["glb"]
            if base_facts.error:
                reasons.append("%s: LOD0 unreadable (%s)" % (key, base_facts.error))
                continue
            base_lo, base_hi = base_facts.bounds()
            base_size = base_hi - base_lo
            for lod in (1, 2):
                if lod not in lods:
                    reasons.append("%s: LOD%d missing" % (key, lod))
                    continue
                facts = lods[lod]["facts"]["glb"]
                if facts.error:
                    reasons.append("%s: LOD%d unreadable (%s)"
                                   % (key, lod, facts.error))
                    continue
                lo, hi = facts.bounds()
                shrink = float(np.max((base_size - (hi - lo)) / np.maximum(base_size, 1e-9)))
                notes.append("%s.%s LOD%d bbox shrink %.3f%%"
                             % (key[0], key[1], lod, shrink * 100.0))
                if shrink > A.LOD_BBOX_SHRINK_MAX + 1e-6:
                    reasons.append("%s: LOD%d bbox shrinks %.2f%% (max %.0f%%)"
                                   % (key, lod, shrink * 100.0,
                                      A.LOD_BBOX_SHRINK_MAX * 100.0))
        if reasons:
            check.fail("LOD bounding-box contract violated", reasons, notes)
        else:
            check.pass_("every LOD keeps the full 3x3x1 envelope (shrink <= 2%)",
                        notes)
        self.c8 = check

    # -- C9 50% distinguishability ---------------------------------------
    def check_distinguishability(self, results):
        check = Check("C9", "50% distinguishability")
        reasons = []
        notes = []
        by_key = {}
        for result in results:
            spec = result["spec"]
            if spec["kind"] == "tier" and spec["lod"] == 0:
                by_key[(spec["tier"], spec["faction"])] = result
        pairs = 0
        passed_pairs = 0
        for faction in A.FACTIONS:
            rasters = {}
            for tier in A.TIERS:
                result = by_key.get((tier, faction))
                if result is None:
                    reasons.append("%s.%s LOD0 missing for C9" % (tier, faction))
                    continue
                facts = result["facts"]["glb"]
                if facts.error:
                    reasons.append("%s.%s LOD0 unreadable (%s)"
                                   % (tier, faction, facts.error))
                    continue
                views = {}
                for view in ("front", "left", "top"):
                    ids, cov = raster_material(facts, view)
                    views[view] = {"ids": ids, "cov": cov,
                                   "glass": _glass_fraction(facts, ids, cov)}
                px, pz = A.roof_platform(tier)
                rasters[tier] = {"facts": facts, "views": views,
                                 "plan": (px, pz),
                                 "ports": _port_signature(facts)}
            tiers_here = [tier for tier in A.TIERS if tier in rasters]
            for i in range(len(tiers_here)):
                for j in range(i + 1, len(tiers_here)):
                    ta, tb = tiers_here[i], tiers_here[j]
                    ra, rb = rasters[ta], rasters[tb]
                    pairs += 1
                    mat_diff = max(_material_diff(ra["views"][v]["ids"],
                                                  rb["views"][v]["ids"])
                                   for v in ("front", "left"))
                    glass_diff = max(
                        abs(ra["views"][v]["glass"] - rb["views"][v]["glass"])
                        for v in ("front", "left"))
                    sil_iou = min(_silhouette_iou(ra["views"][v]["cov"],
                                                  rb["views"][v]["cov"])
                                  for v in ("front", "left", "top"))
                    sil_diff = 1.0 - sil_iou
                    plan_diff = max(abs(ra["plan"][0] - rb["plan"][0]),
                                    abs(ra["plan"][1] - rb["plan"][1]))
                    port_diff = 0.0
                    for pa, pb in zip(ra["ports"], rb["ports"]):
                        base = max(pa["area"], pb["area"], 1e-6)
                        port_diff = max(port_diff,
                                        abs(pa["area"] - pb["area"]) / base)
                    criteria = {
                        "material": mat_diff >= DIST_MATERIAL_MIN,
                        "windows": glass_diff >= DIST_GLASS_MIN,
                        "silhouette": sil_diff >= DIST_SILHOUETTE_MIN,
                        "roof_plan": plan_diff >= DIST_PLAN_MIN,
                        "ports": port_diff >= DIST_PORT_MIN,
                    }
                    category_material = criteria["material"]
                    category_windows = criteria["windows"]
                    category_contour = (criteria["silhouette"]
                                        or criteria["roof_plan"])
                    category_ports = criteria["ports"]
                    categories = sum(1 for value in
                                     (category_material, category_windows,
                                      category_contour, category_ports) if value)
                    ok = categories >= DIST_CRITERIA_MIN
                    line = ("%s vs %s [%s]: material=%.3f%s windows=%.3f%s "
                            "sil=%.3f%s plan=%.2f%s ports=%.2f%s -> %s "
                            "(%d/4 categories)"
                            % (ta, tb, faction, mat_diff,
                               "Y" if category_material else "n",
                               glass_diff, "Y" if category_windows else "n",
                               sil_diff, "Y" if criteria["silhouette"] else "n",
                               plan_diff, "Y" if criteria["roof_plan"] else "n",
                               port_diff, "Y" if category_ports else "n",
                               "PASS" if ok else "FAIL", categories))
                    notes.append(line)
                    if ok:
                        passed_pairs += 1
                    else:
                        reasons.append(line)
        if pairs != 12:
            reasons.append("expected 12 tier pairs (6 per faction), evaluated %d"
                           % pairs)
        if reasons:
            check.fail("tier pairs are not distinguishable by >= 2 of "
                       "{material, windows, silhouette/roof-plan, ports} at the "
                       "50%% camera", reasons, notes)
        else:
            check.pass_("all %d tier pairs differ on >= 2 of the 50%%-camera "
                        "criteria (material + windows/contour/ports)" % passed_pairs,
                        notes)
        self.c9 = check

    # -- C10 material_slots.json -----------------------------------------
    def check_material_slots(self, results):
        check = self.c10
        path = os.path.join(HERE, "material_slots.json")
        reasons = []
        notes = []
        try:
            with open(path, "r", encoding="utf-8") as handle:
                data = json.load(handle)
        except Exception as exc:
            check.fail("material_slots.json unreadable", ["%s: %s"
                                                          % (type(exc).__name__, exc)])
            return
        if "gen_models_v2" not in str(data.get("generated_by", "")):
            reasons.append("generated_by does not name art/gen_models_v2.py (%r)"
                           % data.get("generated_by"))
        env = data.get("envelope", {})
        if env.get("size") != [3.0, 1.0, 3.0]:
            reasons.append("envelope.size != [3.0, 1.0, 3.0] (%r)"
                           % (env.get("size"),))
        roof = data.get("roof_platform", {})
        if abs(float(roof.get("top", -1)) - A.ROOF_PLATFORM_TOP) > 1e-6:
            reasons.append("roof_platform.top != %.2f (%r)"
                           % (A.ROOF_PLATFORM_TOP, roof.get("top")))
        if roof.get("walking_surface_is_model_apex") is not True:
            reasons.append("roof_platform.walking_surface_is_model_apex is not true")
        if abs(float(roof.get("fascia_protrusion_above_top", 9)) - 0.0) > 1e-6:
            reasons.append("roof_platform.fascia_protrusion_above_top != 0")
        ports = data.get("ports", [])
        if sorted(p.get("name") for p in ports) != sorted(
                spec["object"] for spec in A.PORT_SPECS):
            reasons.append("ports list %r != the three #Port# markers"
                           % [p.get("name") for p in ports])
        if data.get("slots_markers") != []:
            reasons.append("slots_markers is not the empty list")
        measured = {}
        for result in results:
            spec = result["spec"]
            if spec["kind"] != "tier":
                if spec["kind"] == "construction":
                    key = A.CONSTRUCTION_STAGE
                else:
                    continue
            else:
                key = "%s.%s" % (spec["tier"], spec["faction"])
            facts = result["facts"]["glb"]
            if facts.error:
                continue
            measured.setdefault(key, {})["LOD%d" % spec["lod"]] = {
                "tris": facts.tri_count,
                "materials": list(facts.materials),
            }
        models = data.get("models", {})
        for key in sorted(measured):
            if key not in models:
                reasons.append("models[%r] missing from material_slots.json" % key)
                continue
            for lod_key in sorted(measured[key]):
                entry = models[key].get(lod_key)
                if entry is None:
                    reasons.append("models[%r][%r] missing" % (key, lod_key))
                    continue
                got = measured[key][lod_key]
                if entry.get("triangles") != got["tris"]:
                    reasons.append("models[%r][%r].triangles=%r, measured %d"
                                   % (key, lod_key, entry.get("triangles"),
                                      got["tris"]))
                if sorted(entry.get("materials", [])) != sorted(got["materials"]):
                    reasons.append("models[%r][%r].materials=%r, measured %r"
                                   % (key, lod_key, entry.get("materials"),
                                      got["materials"]))
                size = entry.get("bounds_size")
                if size != [3.0, 1.0, 3.0]:
                    reasons.append("models[%r][%r].bounds_size=%r != [3,1,3]"
                                   % (key, lod_key, size))
                plan = entry.get("roof_platform", {})
                want_plan = A.roof_platform(spec_tier_for(key, lod_key))
                if (abs(float(plan.get("size_x", -9)) - want_plan[0]) > 1e-6
                        or abs(float(plan.get("size_z", -9)) - want_plan[1]) > 1e-6):
                    reasons.append("models[%r][%r] platform plan %r != %r"
                                   % (key, lod_key, plan, want_plan))
        patches = data.get("material_patches", [])
        if len(patches) != len(MP.PATCH_PIXELS):
            reasons.append("%d patch records, expected %d"
                           % (len(patches), len(MP.PATCH_PIXELS)))
        notes.append("%d models, %d patch records validated against the meshes"
                     % (len(measured), len(patches)))
        if reasons:
            check.fail("material_slots.json does not agree with the models", reasons,
                       notes)
        else:
            check.pass_("material_slots.json (envelope, roof rule, ports, plans, "
                        "triangles, materials, %d flat patches) agrees with the "
                        "measured meshes" % len(patches), notes)


def spec_tier_for(key, lod_key):
    if key == A.CONSTRUCTION_STAGE:
        return A.CONSTRUCTION_STAGE
    return key.split(".")[0]


# --------------------------------------------------------------------------- #
# report
# --------------------------------------------------------------------------- #
STATUS_LETTER = {"PASS": "P", "FAIL": "F", "BLOCKED": "B", "INFO": "I"}


def _letter(check):
    return STATUS_LETTER.get(check.status, "?")


def _fmt3(values):
    return "%.3f/%.3f/%.3f" % tuple(values)


def build_report(validator, meta):
    lines = []
    lines.append("=" * 118)
    lines.append("HDBF v2 - 3x3x1 indoor box farm source-model validation report")
    lines.append("=" * 118)
    lines.append("report:     deterministic (identical inputs -> byte-identical report;")
    lines.append("            no timestamps, no elapsed times)")
    lines.append("mode:       Blender %s, FBX re-import + GLB import (both checked)"
                 % BLENDER_VERSION)
    lines.append("numpy:      %s" % (np.__version__ if np is not None else NUMPY_ERR))
    lines.append("contract:   art/atlas_layout.py (envelope, flat roof, ports, "
                 "tier materials, indoor expression)")
    lines.append("            art/material_patches.py (flat-patch UV), "
                 "art/timbermesh_common.py (export contract)")
    lines.append("files:      %d source models (8 tier x faction x 3 LOD = 24 + "
                 "ConstructionStage0)" % (len(validator.results) - 1))
    lines.append("tolerances: envelope +-%.3f grid, flat top +-%.3f, ports +-%.3f, "
                 "tri budget +-%.0f%%, LOD shrink <=%.0f%%, UV patch +-%.4f"
                 % (BOUNDS_TOL, FLAT_TOL, PORT_TOL, TRI_TOL * 100,
                    A.LOD_BBOX_SHRINK_MAX * 100, UV_EPS))
    lines.append("50%%-camera C9: %d px/grid raster (%dx%d); a tier pair must differ on "
                 ">=%d of {material>=%.2f, windows>=%.2f, silhouette>=%.2f, "
                 "roof-plan>=%.3f, ports>=%.0f%%}"
                 % (RASTER_SCALE, RASTER_SIZE, RASTER_SIZE, DIST_CRITERIA_MIN,
                    DIST_MATERIAL_MIN, DIST_GLASS_MIN, DIST_SILHOUETTE_MIN,
                    DIST_PLAN_MIN, DIST_PORT_MIN * 100))
    lines.append("")
    lines.append("-" * 118)
    lines.append("CHECKS")
    for cid, title in CHECK_DEFS:
        lines.append("  %-3s %s" % (cid, title))
    lines.append("")
    # file-presence check (C1 across the whole expected file set)
    for result in validator.results:
        if result["spec"] is None:
            check = result["checks"]["C1"]
            lines.append("FILE SET  %-5s %s" % (check.status, check.text))
            for note in check.notes:
                lines.append("        - %s" % note)
            for reason in check.reasons:
                lines.append("        ! %s" % reason)
    lines.append("")
    lines.append("=" * 118)
    lines.append("SUMMARY TABLE (one row per source model)")
    lines.append("=" * 118)
    header = "%-40s" % "MODEL"
    header += "".join("%3s" % cid for cid, _t in CHECK_DEFS[:7])
    header += "  %6s %8s  %-15s" % ("TRIS", "MATS", "ENVELOPE X/Y/Z")
    lines.append(header)
    for result in validator.results:
        spec = result["spec"]
        if spec is None:
            continue
        checks = result["checks"]
        row = "%-40s" % spec["label"]
        for cid, _title in CHECK_DEFS[:7]:
            row += "%3s" % _letter(checks[cid])
        facts = result["facts"]["glb"]
        tris = facts.tri_count if not facts.error else -1
        mats = len(facts.materials) if not facts.error else -1
        if not facts.error:
            lo, hi = facts.bounds()
            envelope = _fmt3(hi - lo)
        else:
            envelope = "-"
        row += "  %6d %8d  %-15s" % (tris, mats, envelope)
        lines.append(row)
    lines.append("  C8/C9/C10 are cross-model checks; see their sections below.")
    lines.append("")
    lines.append("=" * 118)
    lines.append("PER-MODEL CHECKS")
    lines.append("=" * 118)
    for result in validator.results:
        spec = result["spec"]
        if spec is None:
            continue
        lines.append("-" * 118)
        lines.append(spec["label"])
        for cid, _title in CHECK_DEFS:
            if cid in ("C8", "C9", "C10"):
                continue
            check = result["checks"][cid]
            lines.append("  %-3s %-38s %-5s %s"
                         % (cid, check.title, check.status, check.text))
            for reason in check.reasons:
                lines.append("        ! %s" % reason)
            for note in check.notes:
                lines.append("        - %s" % note)
    lines.append("")
    lines.append("=" * 118)
    lines.append("C8: LOD BOUNDING BOX")
    lines.append("=" * 118)
    lines.append("  %-5s %-38s %s"
                 % (validator.c8.status, validator.c8.title, validator.c8.text))
    for note in validator.c8.notes:
        lines.append("        - %s" % note)
    for reason in validator.c8.reasons:
        lines.append("        ! %s" % reason)
    lines.append("")
    lines.append("=" * 118)
    lines.append("C9: FOUR TIERS DISTINGUISHABLE AT THE 50% CAMERA (LOD0)")
    lines.append("=" * 118)
    lines.append("  %-5s %-38s %s"
                 % (validator.c9.status, validator.c9.title, validator.c9.text))
    for note in validator.c9.notes:
        lines.append("        - %s" % note)
    for reason in validator.c9.reasons:
        lines.append("        ! %s" % reason)
    lines.append("")
    lines.append("=" * 118)
    lines.append("C10: material_slots.json")
    lines.append("=" * 118)
    lines.append("  %-5s %-38s %s"
                 % (validator.c10.status, validator.c10.title,
                    validator.c10.text))
    for note in validator.c10.notes:
        lines.append("        - %s" % note)
    for reason in validator.c10.reasons:
        lines.append("        ! %s" % reason)
    lines.append("")
    lines.append("=" * 118)
    lines.append("FLAT ROOF EVIDENCE (contract space; walking surface == apex)")
    lines.append("=" * 118)
    lines.append("  %-40s %8s %8s %10s %10s" % ("MODEL", "TOP", "APEX",
                                                "ABOVE_TOP", "TOP_VERTS"))
    for result in validator.results:
        spec = result["spec"]
        if spec is None:
            continue
        facts = result["facts"]["glb"]
        if facts.error:
            continue
        top = A.ROOF_PLATFORM_TOP
        above = int((facts.pos[:, 1] > top + 1e-5).sum())
        px, pz = A.roof_platform(spec["platform_tier"])
        face = (np.abs(facts.pos[:, 1] - top) <= 0.01)
        inside = ((np.abs(facts.pos[:, 0]) <= px * 0.5 + 0.03)
                  & (np.abs(facts.pos[:, 2]) <= pz * 0.5 + 0.03))
        lines.append("  %-40s %8.4f %8.4f %10d %10d"
                     % (spec["label"], top, float(facts.pos[:, 1].max()),
                        above, int((face & inside).sum())))
    lines.append("")
    lines.append("=" * 118)
    lines.append("ENVELOPE TABLE (contract space, finished LOD0 models)")
    lines.append("=" * 118)
    lines.append("  %-40s %10s %10s %10s  %-22s"
                 % ("MODEL", "SIZE X", "SIZE Y", "SIZE Z", "MIN (X,Y,Z)"))
    for result in validator.results:
        spec = result["spec"]
        if spec is None or spec["kind"] != "tier" or spec["lod"] != 0:
            continue
        facts = result["facts"]["glb"]
        if facts.error:
            continue
        lo, hi = facts.bounds()
        lines.append("  %-40s %10.4f %10.4f %10.4f  %-22s"
                     % (spec["label"], hi[0] - lo[0], hi[1] - lo[1],
                        hi[2] - lo[2],
                        "(%.3f, %.3f, %.3f)" % (lo[0], lo[1], lo[2])))
    lines.append("")
    lines.append("=" * 118)
    lines.append("VERDICT")
    lines.append("=" * 118)
    failures = []
    for result in validator.results:
        spec = result["spec"]
        if spec is None:
            for cid, check in sorted(result["checks"].items()):
                if check.status != "PASS":
                    failures.append("%s: %s" % (cid, check.text))
            continue
        for cid, check in sorted(result["checks"].items()):
            if check.status != "PASS":
                failures.append("%s %s: %s" % (spec["label"], cid, check.text))
    for check in (validator.c8, validator.c9, validator.c10):
        if check.status != "PASS":
            failures.append("%s: %s" % (check.cid, check.text))
    if failures:
        lines.append("OVERALL VERDICT: FAIL")
        for failure in failures:
            lines.append("  ! %s" % failure)
    else:
        lines.append("OVERALL VERDICT: PASS")
        lines.append("  every expected source model passes C1-C10 under the v2 "
                     "3x3x1 flat-roof contract.")
    lines.append("")
    lines.append("report written by art/validate_models.py (offline validation, not "
                 "an in-game check)")
    return "\n".join(lines) + "\n"


# --------------------------------------------------------------------------- #
# main
# --------------------------------------------------------------------------- #
def main():
    if not HAVE_BPY or np is None:
        sys.stderr.write(
            "BLOCKED: art/validate_models.py needs Blender (bpy) and numpy.\n"
            "Run: blender.exe -b --factory-startup -noaudio --python "
            "art\\validate_models.py\n")
        return 2
    print("HDBF v2 source-model validation - Blender %s" % BLENDER_VERSION)
    print("contract: 3.00 x 1.00 x 3.00 envelope, flat roof apex y=1.00, "
          "3 ports, no #Slot#, flat-patch UV")
    validator = Validator()
    validator.check_files()
    results = []
    for spec in all_specs():
        print("  loading %s ..." % spec["label"])
        try:
            result = validator.check_model(spec)
        except Exception:                                      # noqa: BLE001
            traceback.print_exc()
            result = {"spec": spec, "checks": {}, "facts": {}}
            for cid, title in CHECK_DEFS[:8]:
                check = Check(cid, title)
                check.fail("validator internal error while evaluating this check",
                           [traceback.format_exc(limit=4).strip()])
                result["checks"][cid] = check
        results.append(result)
    validator.results.extend(results)
    validator.check_lod_shrink(results)
    validator.check_distinguishability(results)
    validator.check_material_slots(results)
    meta = {"blender": BLENDER_VERSION,
            "numpy": np.__version__ if np is not None else NUMPY_ERR}
    report = build_report(validator, meta)
    with open(REPORT_PATH, "w", encoding="utf-8", newline="\n") as handle:
        handle.write(report)
    # console summary
    failures = 0
    for result in validator.results:
        spec = result["spec"]
        if spec is None:
            continue
        statuses = [check.status for check in result["checks"].values()]
        verdict = "PASS" if all(s == "PASS" for s in statuses) else "FAIL"
        if verdict != "PASS":
            failures += 1
        print("  %-40s %-5s tris=%d" % (spec["label"], verdict,
                                        result["facts"]["glb"].tri_count
                                        if not result["facts"]["glb"].error else -1))
    for check in (validator.c8, validator.c9, validator.c10):
        print("  %-3s %-60s %s" % (check.cid, check.text, check.status))
        if check.status != "PASS":
            failures += 1
    print("\nOVERALL VERDICT: %s" % ("PASS" if failures == 0 else "FAIL"))
    print("report: art/validation-report.txt")
    return 0 if failures == 0 else 1


if __name__ == "__main__":
    try:
        sys.exit(main())
    except SystemExit:
        raise
    except Exception:
        traceback.print_exc()
        sys.exit(1)
