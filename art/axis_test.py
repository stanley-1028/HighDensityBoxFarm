"""HDBF ART-02 - Timbermesh axis / unit test (Blender 4.2.1, headless).

Builds a 3 x 2 x 1 test block in the ART-01 authoring space, plus five small
asymmetric probe cubes and one marker empty, and pushes it through the exact
same code path as the real exporter (art/export_timbermesh.py).  The exported
protobuf is parsed back with the plugin's own model_pb2 and measured.

Two files are produced:
  * BoxFarm.AxisTest.Model.timbermesh            - with the ART-02 compensation
                                                   (block-corner origin, game
                                                   orientation) -> must measure
                                                   X 0..3, Y 0..1, Z 0..2.
  * BoxFarm.AxisTestNoCompensation.Model.timbermesh - negative control, raw
                                                   ART-01 authoring space.  It
                                                   documents what the official
                                                   plugin does on its own.

Run:
  & 'C:\\Users\\user\\.hdbf\\sdk\\blender-4.2.1-windows-x64\\blender.exe'
      -b --factory-startup -noaudio --python art\\axis_test.py

Writes art/timbermesh/axis-test/axis-test-report.txt
"""

import datetime
import json
import os
import sys

import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)
TOOLS = os.path.join(HERE, "tools")
if TOOLS not in sys.path:
    sys.path.insert(0, TOOLS)

import timbermesh_common as C                      # noqa: E402
import export_timbermesh as EX                     # noqa: E402  (imports the plugin)

OUT_DIR = os.path.join(C.TIMBERMESH_DIR, "axis-test")
PROBE_SIZE = 0.08

#: probes are given in CONTRACT space (requirement 13.3/13.6, centred pivot,
#: front -Z, Y up).  They verify orientation, not just size.
PROBES = [
    ("front", (0.00, 0.50, -0.95)),   # near the front (-Z) face
    ("back", (0.00, 0.50, +0.95)),    # near the back (+Z) face
    ("left", (-1.40, 0.50, 0.00)),    # requirement -X short side (pipe side)
    ("right", (+1.40, 0.50, 0.00)),   # requirement +X short side (power side)
    ("high", (0.75, 0.90, -0.60)),    # near the top (Y up)
]
EMPTY_CONTRACT = (0.25, 0.50, -0.50)


def contract_to_source_blender(point):
    """contract space -> ART-01 Blender authoring space (front +Y, Z up)."""
    x, y, z = point
    return (x, -z, y)


def box_geometry(center, size):
    cx, cy, cz = center
    sx, sy, sz = size
    verts = []
    for dz in (-0.5, 0.5):
        for dy in (-0.5, 0.5):
            for dx in (-0.5, 0.5):
                verts.append((cx + dx * sx, cy + dy * sy, cz + dz * sz))
    faces = [(0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1),
             (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)]
    return verts, faces


def new_mesh_object(name, verts, faces, collection=None):
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(verts, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    (collection or bpy.context.scene.collection).objects.link(obj)
    return obj


def build_scene(collection_name, compensate):
    EX.reset_scene()
    objects = []
    verts, faces = box_geometry((0.0, 0.0, 0.5), (C.SIZE_X, C.SIZE_Z, C.SIZE_Y))
    objects.append(new_mesh_object("SM_AxisTest_Block", verts, faces))
    for name, contract_point in PROBES:
        center = contract_to_source_blender(contract_point)
        verts, faces = box_geometry(center, (PROBE_SIZE, PROBE_SIZE, PROBE_SIZE))
        objects.append(new_mesh_object("SM_AxisTest_Probe_%s" % name, verts, faces))
    for obj in objects:
        material = bpy.data.materials.get("M_HDBF_Wood") or bpy.data.materials.new("M_HDBF_Wood")
        obj.data.materials.append(material)

    obj = EX.join_meshes(objects, "SM_HDBF_BoxFarm_AxisTest")
    EX.clear_custom_normals(obj)
    EX.assign_vanilla_materials(obj, {"M_HDBF_Wood": "BaseWood_Brown.Folktails"})
    EX.ensure_vertex_colors(obj)

    collection = bpy.data.collections.new(collection_name)
    bpy.context.scene.collection.children.link(collection)
    if compensate:
        EX.reauthor_to_game_space(obj)
    EX.move_to_collection(obj, collection)

    # marker empty: authored at the position that must end up at
    # contract_to_export(EMPTY_CONTRACT) in export space
    if compensate:
        location = C.export_to_blender(C.contract_to_export(EMPTY_CONTRACT))
    else:
        location = contract_to_source_blender(EMPTY_CONTRACT)
    EX.make_empty("#Slot#Probe", location, collection)
    return collection


def _near(points, target, tolerance=0.01):
    return any(max(abs(point[i] - target[i]) for i in range(3)) <= tolerance
               for point in points)


def expected_probe_export_positions():
    out = {}
    for name, contract_point in PROBES:
        out[name] = C.contract_to_export(contract_point)
    return out


def verify(stats, positions, points, empty_target, label):
    checks = []

    def check(name, ok, detail):
        checks.append({"check": name, "ok": bool(ok), "detail": detail})

    size_ok = all(abs(stats["bounds_size"][i] - want) <= C.BOUNDS_TOLERANCE
                  for i, want in enumerate([C.SIZE_X, C.SIZE_Y, C.SIZE_Z]))
    check("bounds size == (3, 1, 2) +/-0.05", size_ok,
          "measured %s" % (stats["bounds_size"],))

    min_ok = all(abs(v) <= C.BOUNDS_TOLERANCE for v in stats["bounds_min"])
    check("bounds min == (0, 0, 0) +/-0.05 (block-corner origin)", min_ok,
          "measured %s" % (stats["bounds_min"],))

    for name, center in positions.items():
        # the probe is a cube of side PROBE_SIZE, so every corner must be present
        # at center +/- PROBE_SIZE/2 in export space
        corners = []
        for dx in (-PROBE_SIZE / 2.0, PROBE_SIZE / 2.0):
            for dy in (-PROBE_SIZE / 2.0, PROBE_SIZE / 2.0):
                for dz in (-PROBE_SIZE / 2.0, PROBE_SIZE / 2.0):
                    corners.append((center[0] + dx, center[1] + dy, center[2] + dz))
        missing = [corner for corner in corners if not _near(points, corner)]
        check("probe %-5s cube vertex at export center %s" % (name, center),
              not missing,
              "8/8 corners found" if not missing else "%d/8 corners missing" % len(missing))

    empty_pos = stats["slots"].get("#Slot#Probe")
    empty_ok = empty_pos is not None and max(
        abs(empty_pos[i] - empty_target[i]) for i in range(3)) <= 1e-3
    check("empty '#Slot#Probe' position", empty_ok,
          "measured %s want %s" % (empty_pos, [round(v, 4) for v in empty_target]))

    check("Model.Name == file stem", stats["name"] == label, "measured %r" % (stats["name"],))
    check("vertex 'color' property present", "color" in stats.get("vertex_properties", []),
          "properties %s" % (stats.get("vertex_properties"),))
    return checks


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    contract_positions = expected_probe_export_positions()
    empty_export = C.contract_to_export(EMPTY_CONTRACT)

    # ---------------- 1) compensated export (the production path) ----------
    label = "BoxFarm.AxisTest.Model"
    collection = build_scene(label, compensate=True)
    path = os.path.join(OUT_DIR, label + ".timbermesh")
    EX.export_collection(collection, path)
    model = EX.postprocess_model(path, label)
    stats = EX.model_stats(model)
    points = EX._floats(next(prop for prop in model.nodes[0].vertexProperties
                             if prop.name == "position"))
    props = {prop.name for prop in model.nodes[0].vertexProperties}
    stats["vertex_properties"] = sorted(props)
    checks = verify(stats, contract_positions, points, empty_export, label)
    compensated_all_ok = all(c["ok"] for c in checks)

    # ---------------- 2) negative control: no compensation ------------------
    neg_label = "BoxFarm.AxisTestNoCompensation.Model"
    neg_collection = build_scene(neg_label, compensate=False)
    neg_path = os.path.join(OUT_DIR, neg_label + ".timbermesh")
    EX.export_collection(neg_collection, neg_path)
    neg_model = EX.postprocess_model(neg_path, neg_label)
    neg_stats = EX.model_stats(neg_model)

    # ---------------- report ------------------------------------------------
    lines = []
    add = lines.append
    add("HDBF Box Farm - ART-02 Timbermesh axis / unit test")
    add("=" * 72)
    add("generated (local): %s" % datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S"))
    add("blender: %s" % EX.BLENDER_VERSION)
    add("plugin: timbermesh_blender_plugin 1.2.0 (vendored at art/tools/)")
    add("test model: 3 x 2 x 1 block built in ART-01 authoring space + 5 asymmetric")
    add("            probe cubes (0.08) + 1 marker empty")
    add("")
    add("1) COMPENSATED EXPORT (production path)")
    add("-" * 72)
    add("file: %s" % os.path.relpath(path, C.WORKSPACE))
    add("compensation: blender_out = (-src.x - 1.5, src.y - 1.0, src.z)")
    add("bounds min         : %s" % (stats["bounds_min"],))
    add("bounds max         : %s" % (stats["bounds_max"],))
    add("bounds size        : %s   (want [3.0, 1.0, 2.0])" % (stats["bounds_size"],))
    add("triangles          : %d" % stats["triangles"])
    add("vertex properties  : %s" % (stats["vertex_properties"],))
    add("nodes              : %s" % (stats["nodes"],))
    add("slot positions     : %s" % json.dumps(stats["slots"]))
    add("")
    add("orientation probes (contract -> measured export):")
    for name, contract_point in PROBES:
        center = contract_positions[name]
        corners = [(center[0] + dx, center[1] + dy, center[2] + dz)
                   for dx in (-PROBE_SIZE / 2.0, PROBE_SIZE / 2.0)
                   for dy in (-PROBE_SIZE / 2.0, PROBE_SIZE / 2.0)
                   for dz in (-PROBE_SIZE / 2.0, PROBE_SIZE / 2.0)]
        missing = [corner for corner in corners if not _near(points, corner)]
        add("  %-5s contract=%-22s export=%-22s cube_vertices=%s" % (
            name, contract_point, tuple(round(v, 3) for v in center),
            "8/8 FOUND" if not missing else "%d/8 MISSING" % len(missing)))
    add("  empty contract=%s export=%s measured=%s" % (
        EMPTY_CONTRACT, tuple(round(v, 3) for v in empty_export),
        stats["slots"].get("#Slot#Probe")))
    add("")
    for check in checks:
        add("  [%s] %-52s %s" % ("PASS" if check["ok"] else "FAIL",
                                 check["check"], check["detail"]))
    add("")
    add("  RESULT: %s" % ("PASS" if compensated_all_ok else "FAIL"))
    add("")
    add("2) NEGATIVE CONTROL - raw ART-01 authoring space, no compensation")
    add("-" * 72)
    add("file: %s" % os.path.relpath(neg_path, C.WORKSPACE))
    add("This shows the official plugin's own axis mapping, empirically:")
    add("  bounds min : %s" % (neg_stats["bounds_min"],))
    add("  bounds max : %s" % (neg_stats["bounds_max"],))
    add("  bounds size: %s" % (neg_stats["bounds_size"],))
    add("")
    add("Interpretation of the two runs")
    add("-" * 72)
    add("* The official plugin DOES convert Blender Z-up -> game Y-up, and it also")
    add("  mirrors X and Z (node_builder writes export = (-bx, bz, -by) and reverses")
    add("  triangle winding to compensate).")
    add("* On raw ART-01 files the plugin therefore yields X [-1.5, 1.5], Y [0, 1],")
    add("  Z [-1, 1]: it keeps the centred pivot of the requirement document.")
    add("* The game's block-object local space is NOT centred. Measured from the")
    add("  shipped ShantySpeaker.IronTeeth.Model.timbermesh of the official modding")
    add("  SDK: a 1 x 1 x 2 decoration occupies X [0, 1], Z [0, 1] and is up to")
    add("  Y 1.87 tall; vanilla blueprint CollidersSpec centres agree")
    add("  (BuildersHut 3x2 box center (1.5, 0.5, 1.0); ShantySpeaker center")
    add("  (0.5, 0.25, 0.5)). So models are authored with the BLOCK CORNER at the")
    add("  origin, spanning [0, size] on X and Z, front at Z = 0.")
    add("* The exporter therefore mirrors X, shifts by half a footprint and shifts")
    add("  the depth by +1.0 so that requirement-13.6 contract coordinates map to")
    add("    export = (contract.x + 1.5, contract.y, contract.z + 1.0).")
    add("* Verified mapping used by the exporter:")
    add("    export = (-blender_out.x, blender_out.z, -blender_out.y)")
    add("    blender_out = (-src.x - 1.5, src.y - 1.0, src.z)")
    add("  i.e. export = (src.x + 1.5, src.z, -src.y + 1.0) = contract + offsets.")
    add("")
    add("OVERALL VERDICT: %s" % ("PASS" if compensated_all_ok else "FAIL"))
    report = "\n".join(lines) + "\n"
    report_path = os.path.join(OUT_DIR, "axis-test-report.txt")
    with open(report_path, "w", encoding="utf-8") as handle:
        handle.write(report)
    print(report)
    print("report:", report_path)
    if not compensated_all_ok:
        sys.exit(1)


if __name__ == "__main__":
    main()
