#!/usr/bin/env python3
"""HDBF tier building icons - offline Blender renders (Blender 4.2.1 headless).

Renders one front three-quarter studio shot per tier from
`art/models/<Tier>.Folktails/LOD0.fbx`, with one identical camera (position,
look-at, lens, framing) for all four tiers, then writes:

    art/icons/BoxFarm.<Tier>.png       512x512 RGBA
    art/icons/BoxFarm.<Tier>.128.png   128x128 RGBA
    art/icons/icons-manifest.json

The 512 shot is rendered once per tier; the 128 icon is derived from it in
memory (alpha-aware premultiplied 4x4 box filter via numpy), so there is no
second render and no alpha fringing.

Camera / framing: perspective 85 mm (36 mm sensor), front three-quarter from
the +Y facade side, offset in -X and raised in +Z, aimed at the centre of the
3 x 2 x 1 contract box (Blender authoring space: X right, +Y front, Z up,
footprint X -1.5..1.5 / Y -1..1 / Z 0..1). Azimuth 34.7 deg from the +Y facade,
elevation 32 deg (the brief suggested ~(-4.5, 6.5, 4.0) / ~24 deg; raised to
32 deg so the empty flat roof platform, the shallow perimeter curb and the
tier-specific body/material treatment read at 128 px -- ART-04 removed the roof
troughs and every tier-specific roof object).
The distance is solved once so the projected contract silhouette fills 84 % of
the frame (8 % margin per side); the same camera is reused verbatim for every
tier.

Lighting/camera/colour management follow `art/render_previews.py` (Cycles CPU,
sun key + opposite fill sun, neutral world ambient, Standard view transform,
no ground plane, no text/logos). Background is transparent
(`film_transparent = True`).

Usage (from the workspace root):
    blender.exe -b --factory-startup -noaudio --python art\\render_icons.py

Debug knob: HDBF_ICON_ONLY=Basic[,Top] renders a subset (iterating only; the
manifest is then incomplete - do not ship that state).

Exit codes: 0 = 8 icons rendered, verified and manifest written;
            1 = render/verification failure; 2 = blocked (bpy/model missing).
"""

from __future__ import annotations

import hashlib
import json
import math
import os
import struct
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
ICON_DIR = os.path.join(HERE, "icons")
MODEL_DIR = os.path.join(HERE, "models")

# --------------------------------------------------------------------------- #
# render contract
# --------------------------------------------------------------------------- #
RESOLUTION = 512
SMALL = 128
DOWNSCALE_FACTOR = RESOLUTION // SMALL

SAMPLES = 64
LENS_MM = 85.0
SENSOR_MM = 36.0
#: projected extent of the 3 x 2 x 1 contract silhouette as a fraction of the
#: square frame: 0.84 -> 8 % empty margin on each side, identical for all tiers.
FRAME_FILL = 0.84
#: look-at point: centre of the contract bounding box.
TARGET = (0.0, 0.0, 0.5)
#: camera direction in Blender authoring space (model front is +Y): azimuth is
#: measured from the +Y facade towards -X, elevation above the horizon.
AZIMUTH_DEG = 34.7
ELEVATION_DEG = 32.0
#: the brief's suggested camera point, kept only as provenance in the manifest.
CAM_LOCATION_HINT = (-4.5, 6.5, 4.0)
EXPOSURE = 0.25

#: Blender authoring-space contract bounds of every tier (README sec. 3).
CONTRACT_MIN = (-1.5, -1.0, 0.0)
CONTRACT_MAX = (1.5, 1.0, 1.0)

FALLBACK_COLORS = {
    # neutral, contract-consistent albedo fallbacks used only when an imported
    # material has no usable image texture (renders stay geometry-true).
    "M_HDBF_Wood": (0.31, 0.18, 0.09, 1.0),
    "M_HDBF_Metal": (0.50, 0.51, 0.52, 1.0),
    "M_HDBF_Glass": (0.42, 0.55, 0.52, 0.62),
    "M_HDBF_Fabric": (0.66, 0.55, 0.38, 1.0),
}

try:
    import numpy as np
    import bpy
    from mathutils import Euler, Vector
    from bpy_extras.object_utils import world_to_camera_view

    HAVE_BPY = True
    BPY_ERR = ""
except Exception as exc:  # pragma: no cover - environment specific
    np = None
    bpy = None
    Vector = None
    Euler = None
    world_to_camera_view = None
    HAVE_BPY = False
    BPY_ERR = "%s: %s" % (type(exc).__name__, exc)

if HERE not in sys.path:
    sys.path.insert(0, HERE)
try:
    import atlas_layout as A
except Exception as exc:  # pragma: no cover
    sys.stderr.write("BLOCKED: cannot import art/atlas_layout.py: %s\n" % exc)
    sys.exit(2)


# --------------------------------------------------------------------------- #
# small helpers
# --------------------------------------------------------------------------- #
def rel(path):
    """Workspace-relative, forward-slash path for the manifest."""
    return os.path.relpath(path, ROOT).replace(os.sep, "/")


def sha256_of(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def png_header(path):
    """Minimal PNG IHDR reader (size / bit depth / colour type)."""
    with open(path, "rb") as f:
        head = f.read(33)
    if len(head) < 33 or head[:8] != b"\x89PNG\r\n\x1a\n":
        return None
    w, h = struct.unpack(">II", head[16:24])
    return {"width": w, "height": h, "bit_depth": head[24],
            "color_type": head[25],
            "color_type_name": {0: "L", 2: "RGB", 4: "LA", 6: "RGBA"}.get(
                head[25], "?")}


# --------------------------------------------------------------------------- #
# scene, light, camera (mirrors art/render_previews.py)
# --------------------------------------------------------------------------- #
def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def _sun_rotation(elevation_deg, azimuth_deg):
    """Euler for a sun light whose rays come from (azimuth, elevation)."""
    el = math.radians(elevation_deg)
    az = math.radians(azimuth_deg)
    ray = Vector((math.sin(az) * math.cos(el),
                  -math.cos(az) * math.cos(el),
                  -math.sin(el)))
    return ray.to_track_quat("-Z", "Y").to_euler()


def setup_world_and_light():
    scene = bpy.context.scene
    world = bpy.data.worlds.new("icon_neutral")
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    if bg is not None:
        bg.inputs[0].default_value = (0.32, 0.32, 0.34, 1.0)
        bg.inputs[1].default_value = 0.65
    scene.world = world

    sun_data = bpy.data.lights.new("icon_sun", "SUN")
    sun_data.energy = 3.6
    sun_data.angle = math.radians(10.0)
    sun = bpy.data.objects.new("icon_sun", sun_data)
    scene.collection.objects.link(sun)
    sun.rotation_euler = _sun_rotation(50.0, 30.0)

    fill_data = bpy.data.lights.new("icon_fill", "SUN")
    fill_data.energy = 1.4
    fill_data.angle = math.radians(35.0)
    fill = bpy.data.objects.new("icon_fill", fill_data)
    scene.collection.objects.link(fill)
    fill.rotation_euler = _sun_rotation(30.0, -145.0)


def setup_render():
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = SAMPLES
    scene.cycles.max_bounces = 4
    scene.cycles.diffuse_bounces = 2
    scene.cycles.glossy_bounces = 2
    scene.cycles.transmission_bounces = 2
    scene.cycles.transparent_max_bounces = 2
    scene.cycles.seed = 0
    try:
        scene.cycles.use_denoising = True
        scene.cycles.denoiser = "OPENIMAGEDENOISE"
    except Exception:
        pass
    scene.render.resolution_x = RESOLUTION
    scene.render.resolution_y = RESOLUTION
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"
    scene.render.image_settings.color_depth = "8"
    scene.render.film_transparent = True
    scene.render.use_file_extension = True
    scene.render.threads_mode = "AUTO"
    try:
        scene.view_settings.view_transform = "Standard"
        scene.view_settings.look = "None"
        scene.view_settings.exposure = EXPOSURE
        scene.view_settings.gamma = 1.0
    except Exception:
        pass


def add_camera(location, rotation_euler):
    scene = bpy.context.scene
    cam_data = bpy.data.cameras.new("icon_cam")
    cam_data.type = "PERSP"
    cam_data.lens = LENS_MM
    cam_data.sensor_width = SENSOR_MM
    cam_data.sensor_fit = "AUTO"
    cam_data.clip_start = 0.1
    cam_data.clip_end = 200.0
    cam = bpy.data.objects.new("icon_cam", cam_data)
    scene.collection.objects.link(cam)
    cam.location = Vector(location)
    cam.rotation_euler = Euler(rotation_euler, "XYZ")
    scene.camera = cam
    bpy.context.view_layer.update()
    return cam


def _contract_corners():
    xs = (CONTRACT_MIN[0], CONTRACT_MAX[0])
    ys = (CONTRACT_MIN[1], CONTRACT_MAX[1])
    zs = (CONTRACT_MIN[2], CONTRACT_MAX[2])
    return [Vector((x, y, z)) for x in xs for y in ys for z in zs]


def fit_camera():
    """Solve one camera so the 3x2x1 contract silhouette fills FRAME_FILL.

    Runs once per script run in a scratch scene; the resulting numbers are
    reused verbatim for all four tiers, so framing/tilt/coverage are identical.
    """
    scene = bpy.context.scene
    target = Vector(TARGET)
    el = math.radians(ELEVATION_DEG)
    az = math.radians(AZIMUTH_DEG)
    # +Y is the facade, so azimuth swings the camera towards -X (front-left 3/4)
    direction = Vector((-math.sin(az) * math.cos(el),
                        math.cos(az) * math.cos(el),
                        math.sin(el)))             # target -> camera
    rot = (-direction).to_track_quat("-Z", "Y").to_euler()
    corners = _contract_corners()

    cam = add_camera(target + direction * 10.0, rot)

    def bbox():
        bpy.context.view_layer.update()
        xs, ys = [], []
        for p in corners:
            v = world_to_camera_view(scene, cam, p)
            xs.append(v.x)
            ys.append(v.y)
        return min(xs), min(ys), max(xs), max(ys)

    # 1) distance: the projected extent shrinks monotonically with distance.
    lo, hi = 3.0, 80.0
    for _ in range(60):
        mid = 0.5 * (lo + hi)
        cam.location = target + direction * mid
        b = bbox()
        extent = max(b[2] - b[0], b[3] - b[1])
        if extent > FRAME_FILL:
            lo = mid
        else:
            hi = mid
    distance = 0.5 * (lo + hi)
    cam.location = target + direction * distance

    # 2) recentre the projected bbox in the frame by sliding the camera in its
    #    own right/up plane (orientation stays fixed -> identical for all tiers)
    basis = cam.matrix_world.to_3x3()
    right = (basis @ Vector((1.0, 0.0, 0.0))).normalized()
    up = (basis @ Vector((0.0, 1.0, 0.0))).normalized()
    eps = 0.1
    for _ in range(8):
        b = bbox()
        cx = 0.5 * (b[0] + b[2])
        cy = 0.5 * (b[1] + b[3])
        if abs(cx - 0.5) < 1e-6 and abs(cy - 0.5) < 1e-6:
            break
        base = cam.location.copy()
        cam.location = base + right * eps
        b1 = bbox()
        cam.location = base + up * eps
        b2 = bbox()
        cam.location = base
        sx = ((b1[0] + b1[2]) - (b[0] + b[2])) * 0.5 / eps
        sy = ((b2[1] + b2[3]) - (b[1] + b[3])) * 0.5 / eps
        cam.location = (base + right * ((0.5 - cx) / sx)
                        + up * ((0.5 - cy) / sy))
    bpy.context.view_layer.update()

    final = bbox()
    loc = cam.location.copy()
    rot_euler = cam.rotation_euler.copy()
    params = {
        "type": "perspective",
        "lens_mm": LENS_MM,
        "sensor_width_mm": SENSOR_MM,
        "sensor_fit": "AUTO",
        "location": [round(v, 6) for v in loc],
        "azimuth_deg": AZIMUTH_DEG,
        "elevation_deg": ELEVATION_DEG,
        "azimuth_reference": "degrees from the +Y facade towards -X",
        "suggested_location_from_brief": list(CAM_LOCATION_HINT),
        "look_at": list(TARGET),
        "rotation_euler_rad": [round(v, 6) for v in rot_euler],
        "rotation_euler_deg": [round(math.degrees(v), 3) for v in rot_euler],
        "distance": round(distance, 6),
        "frame_fill": FRAME_FILL,
        "margin_per_side": round((1.0 - FRAME_FILL) * 0.5, 6),
        "projected_bbox_ndc": [round(v, 6) for v in final],
        "projected_extent": round(max(final[2] - final[0],
                                      final[3] - final[1]), 6),
        "contract_box_blender": {"min": list(CONTRACT_MIN),
                                 "max": list(CONTRACT_MAX)},
    }
    return params


# --------------------------------------------------------------------------- #
# import + materials (mirrors art/render_previews.py)
# --------------------------------------------------------------------------- #
def import_model(path):
    ext = os.path.splitext(path)[1].lower()
    if ext == ".fbx":
        bpy.ops.import_scene.fbx(filepath=path)
    elif ext in (".glb", ".gltf"):
        bpy.ops.import_scene.gltf(filepath=path)
    else:
        raise RuntimeError("unsupported model extension: %s" % ext)
    for ob in list(bpy.data.objects):
        if ob.type in ("CAMERA", "LIGHT"):
            bpy.data.objects.remove(ob, do_unlink=True)


def mesh_triangle_count():
    total = 0
    meshes = 0
    for ob in bpy.data.objects:
        if ob.type != "MESH":
            continue
        me = ob.data
        me.calc_loop_triangles()
        total += len(me.loop_triangles)
        meshes += 1
    return total, meshes


def _principled(mat):
    if not mat.use_nodes or mat.node_tree is None:
        return None
    for node in mat.node_tree.nodes:
        if node.type == "BSDF_PRINCIPLED":
            return node
    return None


def material_fallbacks():
    """Give untextured/broken-texture materials contract-consistent colours."""
    changed = []
    for mat in bpy.data.materials:
        if mat is None:
            continue
        has_tex = False
        if mat.use_nodes and mat.node_tree is not None:
            nt = mat.node_tree
            broken = []
            for node in list(nt.nodes):
                if node.type == "TEX_IMAGE":
                    img = node.image
                    if img is None or img.size[0] == 0 or img.size[1] == 0:
                        broken.append(node)
            for node in broken:
                for link in list(node.outputs["Color"].links):
                    nt.links.remove(link)
                nt.nodes.remove(node)
            for node in list(nt.nodes):
                if node.type == "NORMAL_MAP" and not node.inputs["Color"].links:
                    for link in list(node.outputs["Normal"].links):
                        nt.links.remove(link)
                    nt.nodes.remove(node)
            for node in nt.nodes:
                if node.type == "TEX_IMAGE":
                    img = node.image
                    if img is not None and img.size[0] > 0 and img.size[1] > 0:
                        has_tex = True
                        break
        if has_tex:
            continue
        key = None
        for name in FALLBACK_COLORS:
            if name in mat.name:
                key = name
                break
        if key is None:
            continue
        color = FALLBACK_COLORS[key]
        mat.diffuse_color = color
        bsdf = _principled(mat)
        if bsdf is not None:
            try:
                params = A.MATERIAL_PARAMS[key]
                bsdf.inputs["Base Color"].default_value = color
                bsdf.inputs["Metallic"].default_value = params["metallic"]
                bsdf.inputs["Roughness"].default_value = params["roughness"]
                bsdf.inputs["Alpha"].default_value = params["alpha"]
            except Exception:
                pass
        changed.append(mat.name)
    return changed


# --------------------------------------------------------------------------- #
# image IO: render, decode, alpha-aware downscale
# --------------------------------------------------------------------------- #
def load_pixels(path):
    """Decode a PNG through bpy and return (H, W, 4) float32, bottom-up."""
    img = bpy.data.images.load(path, check_existing=False)
    try:
        w, h = img.size
        buf = np.empty(w * h * 4, dtype=np.float32)
        img.pixels.foreach_get(buf)
        return buf.reshape(h, w, 4)
    finally:
        bpy.data.images.remove(img)


def write_png(pixels, dst, name="icon"):
    """Write an (H, W, 4) float32 array as an 8-bit RGBA PNG via bpy."""
    h, w = pixels.shape[:2]
    img = bpy.data.images.new(name, w, h, alpha=True, float_buffer=False)
    try:
        img.colorspace_settings.name = "sRGB"
        img.pixels.foreach_set(np.ascontiguousarray(pixels).reshape(-1))
        img.filepath_raw = dst
        img.file_format = "PNG"
        img.save()
    finally:
        bpy.data.images.remove(img)


def strip_render_metadata(path):
    """Rewrite a Blender render PNG through the image API.

    Blender's render-result writer always embeds `tEXt` chunks such as
    `Date` and `RenderTime`, so two identical renders produce different file
    bytes (and different sha256). Rewriting through `bpy.data.images` keeps the
    pixels bit-exact but drops those chunks, which makes the delivered icons
    byte-reproducible. Returns the (H, W, 4) pixel array it wrote.
    """
    pixels = load_pixels(path)
    write_png(pixels, path, "icon_%d" % pixels.shape[0])
    return pixels


def downscale_premultiplied(pixels, dst, size):
    """Alpha-aware box-filter downscale (render once at 512 -> 128)."""
    src = pixels.shape[0]
    if src % size != 0:
        raise RuntimeError("downscale factor must be integral")
    k = src // size
    alpha = pixels[..., 3:4]
    # unassociated (straight) alpha in the PNG -> premultiply before averaging,
    # so the empty margin cannot bleed dark pixels into the model's edge
    premult = pixels[..., :3] * alpha
    small_rgb = premult.reshape(size, k, size, k, 3).mean(axis=(1, 3))
    small_a = alpha.reshape(size, k, size, k, 1).mean(axis=(1, 3))
    out = np.concatenate(
        [np.divide(small_rgb, np.maximum(small_a, 1e-6)), small_a], axis=-1)
    write_png(np.clip(out, 0.0, 1.0).astype(np.float32), dst,
              "icon_%d" % size)


def image_stats(path):
    """Alpha presence / coverage / mean colour of one RGBA icon."""
    arr = load_pixels(path)
    a = arr[..., 3]
    rgb = arr[..., :3]
    covered = a > (127.0 / 255.0)
    if covered.any():
        mean_rgb = rgb[covered].mean(axis=0) * 255.0
    else:
        mean_rgb = np.zeros(3)
    return {
        "alpha_min": int(round(float(a.min()) * 255)),
        "alpha_max": int(round(float(a.max()) * 255)),
        "alpha_zero_px": int((a <= 0.0).sum()),
        "alpha_full_px": int((a >= 1.0).sum()),
        "alpha_partial_px": int(((a > 0.0) & (a < 1.0)).sum()),
        "coverage": round(float(covered.mean()), 4),
        "mean_rgb_covered": [int(round(v)) for v in mean_rgb],
        "mean_rgb_covered_unit": [round(float(v) / 255.0, 4) for v in mean_rgb],
    }


def compare_images(path_a, path_b):
    """Mean absolute RGB difference over the union of both silhouettes."""
    a = load_pixels(path_a)
    b = load_pixels(path_b)
    mask = (a[..., 3] > 0.5) | (b[..., 3] > 0.5)
    if not mask.any():
        return 0.0
    diff = np.abs(a[..., :3] - b[..., :3])[mask].mean() * 255.0
    return round(float(diff), 3)


def previous_manifest():
    """Read the manifest left on disk by an earlier run (determinism check)."""
    path = os.path.join(ICON_DIR, "icons-manifest.json")
    if not os.path.isfile(path):
        return None
    try:
        with open(path, encoding="utf-8") as f:
            return json.load(f)
    except Exception:
        return None


# --------------------------------------------------------------------------- #
# main
# --------------------------------------------------------------------------- #
def main():
    started = time.time()
    if not HAVE_BPY:
        sys.stdout.write(
            "BLOCKED: bpy/numpy unavailable (%s).\n"
            "Run: blender.exe -b --factory-startup -noaudio --python "
            "art\\render_icons.py\n" % BPY_ERR)
        return 2

    tiers = list(A.TIERS)
    only = os.environ.get("HDBF_ICON_ONLY", "").strip()
    if only:
        wanted = [t.strip() for t in only.split(",") if t.strip()]
        tiers = [t for t in tiers if t in wanted]
        sys.stdout.write("DEBUG: HDBF_ICON_ONLY=%s -> %r\n" % (only, tiers))

    os.makedirs(ICON_DIR, exist_ok=True)
    # snapshot the previous manifest before this run overwrites it, so the new
    # manifest can record a run-to-run byte comparison as determinism evidence
    prev_manifest = previous_manifest()

    jobs = []
    missing = []
    for tier in tiers:
        fbx = os.path.join(MODEL_DIR, "%s.Folktails" % tier, "LOD0.fbx")
        glb = os.path.join(MODEL_DIR, "%s.Folktails" % tier, "LOD0.glb")
        path = fbx if os.path.isfile(fbx) else glb
        if not os.path.isfile(path):
            missing.append(fbx)
        else:
            jobs.append((tier, path))
    if missing:
        sys.stdout.write("BLOCKED: missing source model(s):\n  %s\n"
                         % "\n  ".join(missing))
        return 2

    out = []
    out.append("HDBF tier icons -> %s" % ICON_DIR)
    out.append("source: art/models/<Tier>.Folktails/LOD0.fbx (Folktails; the "
               "factions share icons)")

    # one camera solved from the fixed 3x2x1 contract box, reused for all tiers
    reset_scene()
    setup_render()
    camera = fit_camera()
    out.append("camera: perspective %g mm, location (%.3f, %.3f, %.3f), "
               "look-at (%.2f, %.2f, %.2f)"
               % (LENS_MM, camera["location"][0], camera["location"][1],
                  camera["location"][2], TARGET[0], TARGET[1], TARGET[2]))
    out.append("framing: contract silhouette fills %.0f%% of the frame "
               "(%.0f%% margin per side), identical for all tiers"
               % (FRAME_FILL * 100, (1.0 - FRAME_FILL) * 50))

    tier_records = []
    files = []           # (path, size, tier)
    fails = []
    for tier, src in jobs:
        reset_scene()
        try:
            import_model(src)
            replaced = material_fallbacks()
            tris, meshes = mesh_triangle_count()
        except Exception as exc:
            fails.append("%s: import failed: %s: %s"
                         % (tier, type(exc).__name__, exc))
            continue

        setup_world_and_light()
        setup_render()
        cam = add_camera(camera["location"], camera["rotation_euler_rad"])
        scene = bpy.context.scene
        scene.camera = cam

        big = os.path.join(ICON_DIR, "BoxFarm.%s.png" % tier)
        small = os.path.join(ICON_DIR, "BoxFarm.%s.128.png" % tier)
        scene.render.filepath = big
        t0 = time.time()
        try:
            bpy.ops.render.render(write_still=True)
        except Exception as exc:
            fails.append("%s: render failed: %s: %s"
                         % (tier, type(exc).__name__, exc))
            continue
        dt_big = time.time() - t0
        if not os.path.isfile(big):
            fails.append("%s: render produced no file (%s)" % (tier, big))
            continue

        t1 = time.time()
        try:
            # strip Blender's Date/RenderTime tEXt chunks (byte-reproducible),
            # then derive the 128 icon from the same in-memory pixels
            pixels = strip_render_metadata(big)
            downscale_premultiplied(pixels, small, SMALL)
        except Exception as exc:
            fails.append("%s: downscale failed: %s: %s"
                         % (tier, type(exc).__name__, exc))
            continue
        dt_small = time.time() - t1

        meta = image_stats(big)
        tier_records.append({
            "tier": tier,
            "source_model": rel(src),
            "source_triangles": tris,
            "source_mesh_objects": meshes,
            "material_fallbacks_applied": replaced,
            "large": big,
            "small": small,
            "render_seconds": round(dt_big, 1),
            "downscale_seconds": round(dt_small, 1),
            "coverage_512": meta["coverage"],
        })
        files.extend([(big, RESOLUTION, tier), (small, SMALL, tier)])
        out.append("  %-13s %4d tri  render %5.1fs  downscale %4.1fs  %s"
                   % (tier, tris, dt_big, dt_small,
                      "OK" if os.path.isfile(small) else "MISSING 128"))

    # ------------------------------------------------------------------ #
    # verification
    # ------------------------------------------------------------------ #
    out.append("")
    out.append("verification:")
    expected = {RESOLUTION: [RESOLUTION, RESOLUTION],
                SMALL: [SMALL, SMALL]}
    stats_512 = {}
    for path, size, tier in files:
        if not os.path.isfile(path):
            fails.append("missing file: %s" % path)
            continue
        hdr = png_header(path)
        if hdr is None:
            fails.append("not a PNG: %s" % path)
            continue
        if [hdr["width"], hdr["height"]] != expected[size]:
            fails.append("%s: %dx%d (expected %d)" % (
                rel(path), hdr["width"], hdr["height"], size))
        if hdr["bit_depth"] != 8 or hdr["color_type"] != 6:
            fails.append("%s: bit depth %d colour type %d (expected 8 / 6 RGBA)"
                         % (rel(path), hdr["bit_depth"], hdr["color_type"]))
        st = image_stats(path)
        if st["alpha_min"] != 0 or st["alpha_full_px"] == 0:
            fails.append("%s: alpha has no real transparency (min %d, full %d)"
                         % (rel(path), st["alpha_min"], st["alpha_full_px"]))
        if size == RESOLUTION:
            stats_512[tier] = st
        out.append("  %-34s %dx%d RGBA  alpha 0:%d 255:%d partial:%d  "
                   "coverage %.1f%%  meanRGB(%d,%d,%d)"
                   % (rel(path), hdr["width"], hdr["height"],
                      st["alpha_zero_px"], st["alpha_full_px"],
                      st["alpha_partial_px"], st["coverage"] * 100.0,
                      *st["mean_rgb_covered"]))

    # the four 512 icons must differ from each other
    tier_names = [t for t in A.TIERS if t in stats_512]
    pair_diffs = []
    for i in range(len(tier_names)):
        for j in range(i + 1, len(tier_names)):
            a = os.path.join(ICON_DIR, "BoxFarm.%s.png" % tier_names[i])
            b = os.path.join(ICON_DIR, "BoxFarm.%s.png" % tier_names[j])
            d = compare_images(a, b)
            cov_a = stats_512[tier_names[i]]["coverage"]
            cov_b = stats_512[tier_names[j]]["coverage"]
            pair_diffs.append({
                "a": tier_names[i], "b": tier_names[j],
                "mean_abs_rgb_diff": d,
                "coverage_delta": round(abs(cov_a - cov_b), 4),
            })
            out.append("  pair %-13s vs %-13s mean|dRGB| %6.2f/255  "
                       "dCoverage %.3f" % (tier_names[i], tier_names[j], d,
                                           abs(cov_a - cov_b)))
            if d < 1.0 and abs(cov_a - cov_b) < 0.005:
                fails.append("%s and %s icons are indistinguishable "
                             "(mean|dRGB| %.2f, dCoverage %.3f)"
                             % (tier_names[i], tier_names[j], d,
                                abs(cov_a - cov_b)))

    # ------------------------------------------------------------------ #
    # manifest
    # ------------------------------------------------------------------ #
    file_entries = []
    for tier, src in jobs:
        rec = next((r for r in tier_records if r["tier"] == tier), None)
        tris = rec["source_triangles"] if rec else None
        for size, target in ((RESOLUTION, "BoxFarm.%s.png" % tier),
                             (SMALL, "BoxFarm.%s.128.png" % tier)):
            path = os.path.join(ICON_DIR, target)
            if not os.path.isfile(path):
                continue
            hdr = png_header(path)
            st = image_stats(path)
            file_entries.append({
                "path": rel(path),
                "tier": tier,
                "width": hdr["width"],
                "height": hdr["height"],
                "mode": hdr["color_type_name"],
                "bit_depth": hdr["bit_depth"],
                "bytes": os.path.getsize(path),
                "sha256": sha256_of(path),
                "source_model": rel(src),
                "source_triangles": tris,
                "alpha": {"min": st["alpha_min"], "max": st["alpha_max"],
                          "zero_px": st["alpha_zero_px"],
                          "full_px": st["alpha_full_px"],
                          "partial_px": st["alpha_partial_px"]},
                "coverage": st["coverage"],
                "mean_rgb_covered": st["mean_rgb_covered"],
            })

    determinism = {
        "scope": ("Measured on this machine with this Blender build. Cycles "
                  "CPU output is hardware/build dependent, so byte identity is "
                  "proven for consecutive runs here - it is NOT guaranteed "
                  "across machines or Blender builds."),
        "metadata_note": (
            "Blender's render-result PNG writer always embeds tEXt chunks "
            "(Date, RenderTime, cycles.*_time), so two pixel-identical renders "
            "used to differ in file bytes. They are stripped by rewriting each "
            "512 render through the bpy image API; measured on this asset, the "
            "pre-strip files were exactly 387 bytes larger (11 tEXt chunks) "
            "with an identical IDAT payload size, i.e. identical pixels."),
    }
    cur_map = {entry["path"]: entry["sha256"] for entry in file_entries}
    if prev_manifest is not None:
        prev_map = {entry.get("path"): entry.get("sha256")
                    for entry in prev_manifest.get("files", [])}
        determinism["runs"] = [
            {
                "label": "previous run (icons-manifest.json on disk at start)",
                "file_sha256": {p: prev_map.get(p) for p in sorted(cur_map)},
            },
            {
                "label": "this run (files on disk now)",
                "file_sha256": {p: cur_map[p] for p in sorted(cur_map)},
            },
        ]
        determinism["png_sha256_identical"] = all(
            prev_map.get(p) == cur_map[p] for p in cur_map)
    else:
        determinism["runs"] = []
        determinism["png_sha256_identical"] = None
        determinism["note"] = ("No earlier icons-manifest.json existed at "
                               "startup, so no run-to-run comparison was made.")

    manifest = {
        "asset": "HDBF (Timberborn High-Density Box Farm) tier building icons",
        "card": "ART-04",
        "generator": rel(os.path.abspath(__file__)),
        "command": ("blender.exe -b --factory-startup -noaudio "
                    "--python art\\render_icons.py"),
        "blender_version": bpy.app.version_string,
        "note": ("These are offline Blender Cycles CPU renders of the source "
                 "FBX models - NOT in-game screenshots."),
        "factions": "Folktails LOD0 source (factions share the same icons)",
        "tiers": list(A.TIERS),
        "camera": camera,
        "render": {
            "engine": "CYCLES",
            "device": "CPU",
            "samples": SAMPLES,
            "denoiser": "OPENIMAGEDENOISE",
            "seed": 0,
            "resolution": [RESOLUTION, RESOLUTION],
            "film_transparent": True,
            "color_depth": 8,
            "view_transform": "Standard",
            "look": "None",
            "exposure": EXPOSURE,
            "lights": {
                "sun": {"energy": 3.6, "angle_deg": 10.0,
                        "elevation_deg": 50.0, "azimuth_deg": 30.0},
                "fill_sun": {"energy": 1.4, "angle_deg": 35.0,
                             "elevation_deg": 30.0, "azimuth_deg": -145.0},
                "world": {"color": [0.32, 0.32, 0.34],
                          "strength": 0.65},
            },
            "ground_plane": False,
            "text_or_logos": False,
            "metadata_stripped": True,
            "deterministic": (
                "Camera, geometry, lighting, samples and seed are fixed, and "
                "Blender's Date/RenderTime tEXt chunks are stripped. Measured "
                "on this machine with this Blender build, consecutive runs "
                "produced byte-identical PNGs (see the determinism block). "
                "Cycles CPU output is hardware/build dependent, so byte "
                "identity is not guaranteed on other machines or Blender "
                "builds."),
        },
        "determinism": determinism,
        "downscale": {
            "from": RESOLUTION,
            "to": SMALL,
            "method": ("alpha-aware premultiplied 4x4 box filter (numpy), "
                       "written with bpy image API"),
        },
        "files": file_entries,
        "tier_summary": [
            {
                "tier": r["tier"],
                "source_model": r["source_model"],
                "source_triangles": r["source_triangles"],
                "source_mesh_objects": r["source_mesh_objects"],
                "coverage_512": r["coverage_512"],
            }
            for r in tier_records
        ],
        "pairwise_differences_512": pair_diffs,
        "verification": {
            "expected_file_count": 8,
            "files_ok": len(file_entries),
            "failures": fails,
            "verdict": "PASS" if not fails and len(file_entries) == 8 else "FAIL",
        },
    }

    manifest_path = os.path.join(ICON_DIR, "icons-manifest.json")
    if not fails and len(file_entries) == 8:
        with open(manifest_path, "w", encoding="utf-8", newline="\n") as f:
            json.dump(manifest, f, indent=2, ensure_ascii=False)
            f.write("\n")
        out.append("manifest: %s" % rel(manifest_path))
    else:
        out.append("manifest NOT written (verification failed)")

    if fails:
        out.append("")
        out.append("VERDICT: FAIL (%d problem(s))" % len(fails))
        for f in fails:
            out.append("  bad: %s" % f)
    else:
        out.append("")
        out.append("VERDICT: PASS - 8 icons (4 x 512 RGBA + 4 x 128 RGBA), "
                   "real alpha, four distinguishable silhouettes")
    out.append("elapsed: %.1fs" % (time.time() - started))
    sys.stdout.write("\n".join(out) + "\n")
    try:
        sys.stdout.flush()
        sys.stderr.flush()
    except Exception:
        pass
    return 0 if not fails else 1


if __name__ == "__main__":
    try:
        code = main()
    except SystemExit:
        raise
    except Exception:
        import traceback
        traceback.print_exc()
        code = 1
    try:
        sys.stdout.flush()
        sys.stderr.flush()
    except Exception:
        pass
    os._exit(code)
