"""HDBF Box Farm - v2 batch Timbermesh exporter (Blender 4.2.1, headless).

Takes the v2 LOD0 source meshes (art/models/<Tier>.<Faction>/LOD0.fbx and
art/models/ConstructionStage0/ConstructionStage0.fbx), re-authors them into the
game's block-object local space (block-corner origin, see
art/timbermesh_common.py for the full coordinate derivation), adds the exactly
THREE port marker empties (`#Port#Power/#Port#Pipe/#Port#Logistics`; the v2
models have no `#Slot#` nodes), assigns VANILLA material asset names (the game
looks materials up by name in the faction MaterialCollection), keeps the
flat-patch UV0 / per-face vertex colours, and exports one .timbermesh per model
through the official vendored plugin (art/tools/timbermesh_blender_plugin,
v1.2.0 MIT).

v2 (2026-10-26): strict 3 x 3 x 1 envelope; the roof is ONE flat empty platform
whose WALKING SURFACE is the model apex at y = 1.00 and nothing exists above it.

Run:
  & 'C:\\Users\\user\\.hdbf\\sdk\\blender-4.2.1-windows-x64\\blender.exe'
      -b --factory-startup -noaudio --python art\\export_timbermesh.py

Output:
  art/timbermesh/BoxFarm.<Tier>.<Faction>.Model.timbermesh                  (8)
  art/timbermesh/BoxFarm.<Tier>.<Faction>.ConstructionStage0.Model.timbermesh (8)
  art/timbermesh/export-manifest.json
"""

import hashlib
import json
import math
import os
import struct
import sys
import traceback
import zlib

import bpy
import mathutils

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)                       # timbermesh_common
TOOLS = os.path.join(HERE, "tools")
if TOOLS not in sys.path:
    sys.path.insert(0, TOOLS)                      # vendored official plugin

import timbermesh_common as C                      # noqa: E402

import timbermesh_blender_plugin                    # noqa: E402,F401  (appends plugin dir to sys.path)
from timbermesh_blender_plugin import timbermesh_exporter  # noqa: E402
import model_pb2                                     # noqa: E402

BLENDER_VERSION = bpy.app.version_string
EXPORT_DIR = C.TIMBERMESH_DIR

#: ART-01 authoring space -> Blender space handed to the plugin.
#:   bx' = -bx - 1.5 ; by' = by - 1.0 ; bz' = bz
#: (see timbermesh_common.source_to_export_blender for the derivation)
EXPORT_MATRIX = mathutils.Matrix((
    (-1.0, 0.0, 0.0, -C.SIZE_X / 2.0),
    (0.0, 1.0, 0.0, -C.SIZE_Z / 2.0),
    (0.0, 0.0, 1.0, 0.0),
    (0.0, 0.0, 0.0, 1.0),
))

COLOR_ATTRIBUTE_NAME = "Col"


# --------------------------------------------------------------------------- #
# scene helpers
# --------------------------------------------------------------------------- #

def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def import_fbx(path):
    if not os.path.isfile(path):
        raise FileNotFoundError(path)
    bpy.ops.import_scene.fbx(filepath=path)
    meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
    if not meshes:
        raise RuntimeError("no mesh objects imported from %s" % path)
    # ART-01 port empties are replaced by the ART-02 contract markers.
    for obj in list(bpy.context.scene.objects):
        if obj.type != 'MESH':
            bpy.data.objects.remove(obj, do_unlink=True)
    return meshes


def join_meshes(meshes, object_name):
    bpy.ops.object.select_all(action='DESELECT')
    for obj in meshes:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    if len(meshes) > 1:
        bpy.ops.object.join()
    obj = bpy.context.view_layer.objects.active
    obj.name = object_name
    obj.data.name = object_name
    # drop parenting but keep world placement, then bake transforms
    if obj.parent is not None:
        world = obj.matrix_world.copy()
        obj.parent = None
        obj.matrix_world = world
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    return obj


def clear_custom_normals(obj):
    """Custom split normals from the FBX are stale after the X mirror; drop them.

    ART-01 LOD0 is 100% flat shaded (measured: 0 smooth polygons), so flat face
    normals after the mirror are exactly the authored look.
    """
    mesh = obj.data
    if not mesh.has_custom_normals:
        return
    try:
        bpy.ops.mesh.customdata_custom_splitnormals_clear()
    except Exception:
        attr = mesh.attributes.get("custom_normal")
        if attr is not None:
            mesh.attributes.remove(attr)
    mesh.update()


def assign_vanilla_materials(obj, material_map):
    """Replace ART-01 M_HDBF_* slots with vanilla material asset names."""
    mapping = dict(material_map)
    seen = []
    for slot in obj.material_slots:
        old = slot.material.name if slot.material is not None else None
        if old not in mapping:
            raise RuntimeError("ART-01 material %r has no ART-02 mapping" % (old,))
        new_name = mapping[old]
        mat = bpy.data.materials.get(new_name)
        if mat is None:
            mat = bpy.data.materials.new(name=new_name)
        mat.use_nodes = False
        slot.material = mat
        if new_name not in seen:
            seen.append(new_name)
    if not seen:
        raise RuntimeError("object %s has no material slots" % obj.name)
    if len(seen) > 4:
        raise RuntimeError("model uses %d materials (>4): %s" % (len(seen), seen))
    return seen


def ensure_vertex_colors(obj):
    """The plugin exports a 'color' vertex property only when the mesh has one.

    ART-04 authors ONE flat colour per face in `gen_models.py` (a ``Col`` layer).
    Blender's FBX round-trip may or may not preserve it; when it survived we keep
    it, otherwise a neutral white layer is written so the property still exists
    (the report records which case happened).
    """
    mesh = obj.data
    layer = mesh.color_attributes.get(COLOR_ATTRIBUTE_NAME)
    created = False
    if layer is None:
        layer = mesh.color_attributes.new(name=COLOR_ATTRIBUTE_NAME,
                                          type='BYTE_COLOR', domain='CORNER')
        created = True
        for entry in layer.data:
            entry.color = (1.0, 1.0, 1.0, 1.0)
    mesh.color_attributes.active_color = layer
    mesh.update()
    return created


def color_stats(obj):
    """Min/max of the corner colour layer (documents whether ART-04 colours survived)."""
    mesh = obj.data
    layer = mesh.color_attributes.get(COLOR_ATTRIBUTE_NAME)
    if layer is None:
        return None
    lo = [1.0, 1.0, 1.0]
    hi = [0.0, 0.0, 0.0]
    for entry in layer.data:
        for i in range(3):
            value = float(entry.color[i])
            lo[i] = min(lo[i], value)
            hi[i] = max(hi[i], value)
    return {"min": [round(v, 4) for v in lo], "max": [round(v, 4) for v in hi],
            "non_white": any(hi[i] - lo[i] > 1e-4 for i in range(3))}


def flip_winding(obj):
    mesh = obj.data
    try:
        mesh.flip_normals()
    except AttributeError:                      # pragma: no cover - version guard
        import bmesh
        bm = bmesh.new()
        bm.from_mesh(mesh)
        bmesh.ops.reverse_faces(bm, faces=bm.faces)
        bm.to_mesh(mesh)
        bm.free()
    mesh.update()


def reauthor_to_game_space(obj):
    """Mirror X, shift by half a footprint and flip winding (see module doc)."""
    mesh = obj.data
    mesh.transform(EXPORT_MATRIX)
    flip_winding(obj)
    for polygon in mesh.polygons:
        polygon.use_smooth = False
    mesh.update()


def move_to_collection(obj, collection):
    for other in list(obj.users_collection):
        other.objects.unlink(obj)
    collection.objects.link(obj)


def make_empty(name, location, collection):
    empty = bpy.data.objects.new(name, None)
    empty.empty_display_type = 'PLAIN_AXES'
    empty.empty_display_size = 0.25
    empty.location = location
    collection.objects.link(empty)
    return empty


# --------------------------------------------------------------------------- #
# plugin export + post processing
# --------------------------------------------------------------------------- #

def export_collection(collection, path):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    settings = timbermesh_exporter.ExportSettings(
        bpy.context,
        merge_meshes=True,
        single_animation=False,
        use_vertex_animations=False,
    )
    timbermesh_exporter.Exporter.export_collection(collection, path, settings)


def read_model(path):
    model = model_pb2.Model()
    model.ParseFromString(zlib.decompress(open(path, 'rb').read()))
    return model


def write_model(model, path):
    payload = zlib.compress(model.SerializeToString())
    with open(path, 'wb') as handle:
        handle.write(payload)


def postprocess_model(path, stem):
    """Stamp Model.Name (official plugin leaves it empty, see README) and keep Version=0."""
    model = read_model(path)
    model.version = 0
    model.name = stem
    write_model(model, path)
    return read_model(path)


def _floats(property_message):
    dim = property_message.scalarTypeDimension
    count = len(property_message.data) // (4 * dim)
    return [struct.unpack_from('<%df' % dim, property_message.data, i * 4 * dim)
            for i in range(count)]


def model_stats(model):
    bounds = [[math.inf, math.inf, math.inf], [-math.inf, -math.inf, -math.inf]]
    triangles = 0
    materials = []
    nan = False
    slots = {}
    ports = {}
    nodes = []
    for node in model.nodes:
        nodes.append(node.name)
        for mesh in node.meshes:
            triangles += len(mesh.indices) // 3
            if mesh.material not in materials:
                materials.append(mesh.material)
        props = {prop.name: prop for prop in node.vertexProperties}
        if "position" in props and node.vertexCount:
            for point in _floats(props["position"]):
                for axis in range(3):
                    bounds[0][axis] = min(bounds[0][axis], point[axis])
                    bounds[1][axis] = max(bounds[1][axis], point[axis])
                    if math.isnan(point[axis]) or math.isinf(point[axis]):
                        nan = True
        for prop in node.vertexProperties:
            dim = max(prop.scalarTypeDimension, 1)
            if len(prop.data) % (4 * dim) != 0 or len(prop.data) != node.vertexCount * dim * 4:
                continue
            for value in _floats(prop):
                for component in value:
                    if math.isnan(component) or math.isinf(component):
                        nan = True
        if node.name.startswith("#Slot#"):
            slots[node.name] = (node.position.x, node.position.y, node.position.z)
        elif node.name.startswith("#Port#"):
            ports[node.name] = (node.position.x, node.position.y, node.position.z)
    return {
        "bounds_min": [round(v, 4) for v in bounds[0]],
        "bounds_max": [round(v, 4) for v in bounds[1]],
        "bounds_size": [round(bounds[1][i] - bounds[0][i], 4) for i in range(3)],
        "triangles": triangles,
        "materials": materials,
        "vertices": sum(node.vertexCount for node in model.nodes),
        "nodes": nodes,
        "slots": {k: [round(x, 4) for x in v] for k, v in slots.items()},
        "ports": {k: [round(x, 4) for x in v] for k, v in ports.items()},
        "nan": nan,
        "version": model.version,
        "name": model.name,
    }


def check_stats(spec, stats):
    """Fail loudly if the exported file is outside the ART-02 contract."""
    problems = []
    if stats["name"] != spec["stem"]:
        problems.append("Model.Name %r != %r" % (stats["name"], spec["stem"]))
    if stats["nan"]:
        problems.append("NaN/Inf in vertex data")
    expected_size = [C.SIZE_X, C.SIZE_Y, C.SIZE_Z]
    for axis, want in enumerate(expected_size):
        got = stats["bounds_size"][axis]
        if abs(got - want) > C.BOUNDS_TOLERANCE:
            problems.append("bounds size axis %d = %.4f (want %.2f)" % (axis, got, want))
    for axis in range(3):
        if abs(stats["bounds_min"][axis]) > C.BOUNDS_TOLERANCE:
            problems.append("bounds min axis %d = %.4f (want 0)" % (axis, stats["bounds_min"][axis]))
    budget = C.TRI_BUDGET_LOD0
    if stats["triangles"] > budget:
        problems.append("triangles %d > budget %d" % (stats["triangles"], budget))
    whitelist = C.MATERIAL_WHITELIST[spec["faction"]]
    for material in stats["materials"]:
        if material not in whitelist:
            problems.append("material %r not in %s whitelist" % (material, spec["faction"]))
    if not 1 <= len(stats["materials"]) <= 4:
        problems.append("material slot count %d (want 1..4)" % len(stats["materials"]))
    expected_materials = set(spec["materials"].values())
    if set(stats["materials"]) != expected_materials:
        problems.append("materials %s != planned %s" % (
            sorted(stats["materials"]), sorted(expected_materials)))
    for name, expected in C.port_export_positions().items():
        got = stats["ports"].get(name)
        if got is None:
            problems.append("missing empty %s" % name)
            continue
        if max(abs(got[i] - expected[i]) for i in range(3)) > C.POSITION_TOLERANCE:
            problems.append("empty %s at %s, want %s" % (name, got, expected))
    # ART-04/v2: no crop-slot marker may survive; exactly the 3 port markers remain.
    if stats["slots"]:
        problems.append("crop-slot marker node(s) still present: %s"
                        % sorted(stats["slots"]))
    if len(stats["ports"]) != 3:
        problems.append("port count %d (want 3)" % len(stats["ports"]))
    # v2 flat roof: the walking surface is the model apex (y == PLATFORM_TOP)
    if abs(stats["bounds_max"][1] - C.PLATFORM_TOP) > 0.005:
        problems.append("roof apex y=%.4f (want the flat walking surface %.3f)"
                        % (stats["bounds_max"][1], C.PLATFORM_TOP))
    if problems:
        raise RuntimeError("%s: %s" % (spec["stem"], "; ".join(problems)))


# --------------------------------------------------------------------------- #
# one model
# --------------------------------------------------------------------------- #

def object_name_for(spec):
    if spec["construction"]:
        return "SM_HDBF_BoxFarm_ConstructionStage0"
    return "SM_HDBF_BoxFarm_%s_%s_LOD0" % (spec["tier"], spec["faction"])


def build_model_collection(spec):
    reset_scene()
    meshes = import_fbx(spec["source"])
    obj = join_meshes(meshes, object_name_for(spec))
    clear_custom_normals(obj)
    used = assign_vanilla_materials(obj, spec["materials"])
    color_created = ensure_vertex_colors(obj)
    colors = color_stats(obj)
    reauthor_to_game_space(obj)

    collection = bpy.data.collections.new(spec["stem"])
    bpy.context.scene.collection.children.link(collection)
    move_to_collection(obj, collection)
    for name, location in C.empty_specs_blender():
        make_empty(name, location, collection)
    return collection, used, {"created_white": color_created, "stats": colors}


def export_one(spec):
    collection, used, colors = build_model_collection(spec)
    export_collection(collection, spec["path"])
    model = postprocess_model(spec["path"], spec["stem"])
    stats = model_stats(model)
    stats["file"] = os.path.relpath(spec["path"], C.WORKSPACE).replace("\\", "/")
    stats["bytes"] = os.path.getsize(spec["path"])
    stats["sha256"] = hashlib.sha256(open(spec["path"], 'rb').read()).hexdigest()
    stats["source"] = os.path.relpath(spec["source"], C.WORKSPACE).replace("\\", "/")
    stats["object"] = object_name_for(spec)
    stats["material_slots_object"] = used
    stats["vertex_colors"] = colors
    check_stats(spec, stats)
    return stats


# --------------------------------------------------------------------------- #
# main
# --------------------------------------------------------------------------- #

def main():
    os.makedirs(EXPORT_DIR, exist_ok=True)
    specs = C.all_model_specs()
    print("=" * 78)
    print("HDBF ART-02 timbermesh export - Blender %s" % BLENDER_VERSION)
    print("export dir: %s" % EXPORT_DIR)
    print("models: %d" % len(specs))
    print("=" * 78)
    results = []
    failures = []
    for index, spec in enumerate(specs, start=1):
        print("\n[%02d/%02d] %s" % (index, len(specs), spec["stem"]))
        print("        source: %s" % os.path.relpath(spec["source"], C.WORKSPACE))
        try:
            stats = export_one(spec)
        except Exception as error:              # noqa: BLE001 - report and continue
            traceback.print_exc()
            failures.append({"stem": spec["stem"], "error": "%s: %s" % (type(error).__name__, error)})
            print("        FAILED: %s" % error)
            continue
        results.append(stats)
        print("        bytes=%d tris=%d vtx=%d" % (stats["bytes"], stats["triangles"], stats["vertices"]))
        print("        bounds min=%s max=%s" % (stats["bounds_min"], stats["bounds_max"]))
        print("        materials=%s" % stats["materials"])
        print("        nodes=%d  sha256=%s" % (len(stats["nodes"]), stats["sha256"][:16]))

    manifest = {
        "generated_by": "art/export_timbermesh.py",
        "art_revision": "HDBF-v2",
        "blender_version": BLENDER_VERSION,
        "plugin": {
            "name": "timbermesh_blender_plugin",
            "version": "1.2.0",
            "vendored_at": "art/tools/timbermesh_blender_plugin",
            "license": "MIT",
        },
        "coordinate_system": {
            "game_model_space": "block-corner origin: X [0,3] width, Y [0,1] height, Z [0,3] depth, front -Z",
            "export_matrix_blender": [[-1.0, 0.0, 0.0, -1.5],
                                      [0.0, 1.0, 0.0, -1.5],
                                      [0.0, 0.0, 1.0, 0.0],
                                      [0.0, 0.0, 0.0, 1.0]],
            "note": "art/models/* are centered-pivot (contract space); exporter mirrors X, shifts by half a footprint and flips winding so the plugin output is corner-origin game space",
        },
        "envelope_export_space": {
            "size": [C.SIZE_X, C.SIZE_Y, C.SIZE_Z],
            "min": [0.0, 0.0, 0.0],
            "max": [C.SIZE_X, C.SIZE_Y, C.SIZE_Z],
            "tolerance": C.BOUNDS_TOLERANCE,
        },
        "roof_platform": {
            "description": "ONE flat empty platform per model; walking surface == "
                           "model apex y = 1.00, nothing above it, 30 cm clearance "
                           "above the surface, flush <=0.04 perimeter fascia",
            "top": C.PLATFORM_TOP,
            "walking_surface_is_model_apex": True,
            "fascia_height": C.PLATFORM_CURB_H,
            "fascia_width": C.PLATFORM_CURB_W,
            "fascia_protrusion_above_top": 0.0,
            "clear_above": C.PLATFORM_CLEAR_ABOVE,
            "plans": {tier: {"size_x": plan[0], "size_z": plan[1]}
                      for tier, plan in C.PLATFORM_PLAN.items()},
        },
        "marker_empties": {
            "ports": sorted(C.port_export_positions()),
            "slot_markers": [],
        },
        "model_count": len(results),
        "models": results,
        "failures": failures,
    }
    manifest_path = os.path.join(EXPORT_DIR, "export-manifest.json")
    with open(manifest_path, "w", encoding="utf-8") as handle:
        json.dump(manifest, handle, indent=2, ensure_ascii=False)
        handle.write("\n")
    print("\nmanifest: %s" % manifest_path)
    print("=" * 78)
    if failures:
        print("EXPORT RESULT: FAIL (%d/%d models failed)" % (len(failures), len(specs)))
        for failure in failures:
            print("  - %s: %s" % (failure["stem"], failure["error"]))
        sys.exit(1)
    print("EXPORT RESULT: PASS (%d models)" % len(results))
    print("=" * 78)


if __name__ == "__main__":
    main()
