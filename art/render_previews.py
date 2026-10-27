#!/usr/bin/env python3
"""HDBF v2 orthographic three-view / 50%-camera preview renders (Blender only).

Renders 512x512 PNGs of each tier x faction LOD0 model (front / side / top),
a 50%-camera contact shot (``<Tier>[_<Faction>]_scale50.png``, the in-game "50%
zoom" readability view) and one iso view, using Cycles CPU (samples <= 24) with a
neutral background and simple sun + ambient lighting.  No text, annotations or
overlays are ever added to the images.

v2 (2026-10-26): the models are 3x3x1 indoor farms with ONE flat empty roof
platform (walking surface = model apex y = 1.00).  v2.2 (reference-image upgrade)
renders BOTH factions: Folktails keeps the legacy ``<Tier>_*.png`` names, the
IronTeeth set is ``<Tier>.IronTeeth_*.png``.  After the per-model renders the
50%-camera A/B comparison sheets are composed (no text, pure render panels):
    art/previews/tier_ab_basic_vs_intermediate_50.png   (2 panels, Folktails)
    art/previews/tier_ab_advanced_vs_top_50.png         (2 panels, Folktails)
    art/previews/tiers_scale50_grid.png                 (2x2, Folktails)
    art/previews/faction_ab_basic_50.png                (2 panels, FT vs IT)
    art/previews/faction_ab_top_50.png                  (2 panels, FT vs IT)
    art/previews/tiers_factions_scale50_grid.png        (4x2, all 8 models)

Usage (from the workspace root):
    blender.exe -b --factory-startup -noaudio --python art\\render_previews.py

Outputs:
    art/previews/<Tier>_front.png   (asset front = -Z; camera looks at the facade)
    art/previews/<Tier>_side.png    (left short side, pipe flange side)
    art/previews/<Tier>_top.png     (plan view)
    art/previews/<Tier>_scale50.png (front at 50% camera scale, contact shot)
    art/previews/<Tier>_iso.png     (3/4 iso view)
    art/previews/<Tier>.IronTeeth_*.png   (same five views, IronTeeth faction)
    art/previews/ConstructionStage0_front.png
    art/previews/ConstructionStage0_iso.png
    art/previews/*_50.png / *_scale50_grid.png (composed 50% A/B sheets)

Exit codes: 0 all previews rendered and verified; 1 render/verify failure;
2 blocked (bpy unavailable or required model files missing).
If Cycles is unavailable or fails headless the script retries with the
Blender Workbench engine and says so in its report; it never fabricates images.
"""

from __future__ import annotations

import math
import os
import struct
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
PREVIEW_DIR = os.path.join(HERE, "previews")

try:
    import bpy
    from mathutils import Vector

    HAVE_BPY = True
except Exception as exc:  # pragma: no cover - environment specific
    bpy = None
    Vector = None
    HAVE_BPY = False
    BPY_ERR = "%s: %s" % (type(exc).__name__, exc)

try:
    import numpy as np

    NUMPY_ERR = None
except Exception as exc:  # pragma: no cover
    np = None
    NUMPY_ERR = "%s: %s" % (type(exc).__name__, exc)

if os.path.join(HERE) not in sys.path:
    sys.path.insert(0, HERE)
try:
    import atlas_layout as A
except Exception as exc:  # pragma: no cover
    sys.stderr.write("BLOCKED: cannot import art/atlas_layout.py: %s\n" % exc)
    sys.exit(2)

RESOLUTION = 512
SAMPLES = 24
ORTHO_SCALE = 3.9          # 3x3 footprint + margin (facade fills the frame)
ORTHO_SCALE_50 = 8.0       # 50% camera scale (one 3-grid building fills 3/8 of frame)
ORTHO_SCALE_ISO = 5.2


def _sun_rotation(elevation_deg, azimuth_deg):
    """Euler for a sun light whose rays travel from (azimuth, elevation)."""
    el = math.radians(elevation_deg)
    az = math.radians(azimuth_deg)
    # ray direction (camera convention: local -Z tracks it), from front-right-above
    ray = Vector((math.sin(az) * math.cos(el),
                  -math.cos(az) * math.cos(el),
                  -math.sin(el)))
    return ray.to_track_quat("-Z", "Y").to_euler()
FALLBACK_COLORS = {
    # neutral, contract-consistent albedo fallbacks used only when an imported
    # material has no image texture node (renders stay geometry-true).
    "M_HDBF_Wood": (0.31, 0.18, 0.09, 1.0),
    "M_HDBF_Metal": (0.50, 0.51, 0.52, 1.0),
    "M_HDBF_Glass": (0.42, 0.55, 0.52, 0.62),
    "M_HDBF_Fabric": (0.66, 0.55, 0.38, 1.0),
}


# --------------------------------------------------------------------------- #
# scene setup
# --------------------------------------------------------------------------- #
def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def setup_world_and_light():
    scene = bpy.context.scene
    world = bpy.data.worlds.new("preview_neutral")
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    if bg is not None:
        # neutral studio ambient: lifts interiors/undersides so the atlas
        # albedo/normal detail and the indoor crop silhouette stay legible at 50%
        bg.inputs[0].default_value = (0.36, 0.37, 0.40, 1.0)
        bg.inputs[1].default_value = 0.95
    scene.world = world
    sun_data = bpy.data.lights.new("preview_sun", "SUN")
    sun_data.energy = 4.4
    sun_data.angle = math.radians(12.0)
    sun = bpy.data.objects.new("preview_sun", sun_data)
    scene.collection.objects.link(sun)
    # key light: ~50 deg elevation, ~30 deg azimuth, from the front-right
    sun.rotation_euler = _sun_rotation(52.0, 32.0)
    fill_data = bpy.data.lights.new("preview_fill", "SUN")
    fill_data.energy = 2.2
    fill_data.angle = math.radians(35.0)
    fill = bpy.data.objects.new("preview_fill", fill_data)
    scene.collection.objects.link(fill)
    # fill from the opposite (rear-left) side keeps shadow sides readable
    fill.rotation_euler = _sun_rotation(28.0, -145.0)
    rim_data = bpy.data.lights.new("preview_rim", "SUN")
    rim_data.energy = 1.1
    rim_data.angle = math.radians(20.0)
    rim = bpy.data.objects.new("preview_rim", rim_data)
    scene.collection.objects.link(rim)
    # low front-left rim light: separates the glass facade from the background
    rim.rotation_euler = _sun_rotation(14.0, -32.0)


def setup_cycles():
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = SAMPLES
    scene.cycles.max_bounces = 4
    scene.cycles.diffuse_bounces = 2
    scene.cycles.glossy_bounces = 2
    scene.cycles.transmission_bounces = 2
    scene.cycles.transparent_max_bounces = 2
    try:
        scene.cycles.use_denoising = True
    except Exception:
        pass
    scene.render.resolution_x = RESOLUTION
    scene.render.resolution_y = RESOLUTION
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGB"
    scene.render.image_settings.color_depth = "8"
    scene.render.film_transparent = False
    scene.render.use_file_extension = True
    try:
        scene.view_settings.view_transform = "Standard"
        scene.view_settings.look = "None"
        scene.view_settings.exposure = 0.75
        scene.view_settings.gamma = 1.0
    except Exception:
        pass
    scene.render.threads_mode = "AUTO"


def setup_workbench():
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.render.resolution_x = RESOLUTION
    scene.render.resolution_y = RESOLUTION
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGB"
    scene.render.image_settings.color_depth = "8"
    scene.render.film_transparent = False
    try:
        shading = scene.display.shading
        shading.light = "STUDIO"
        shading.color_type = "TEXTURE"
        shading.show_shadows = True
        shading.show_cavity = True
        shading.background_type = "VIEWPORT"
        shading.background_color = (0.18, 0.18, 0.19)
    except Exception:
        pass


def make_camera(name, location, direction=None, ortho_scale=ORTHO_SCALE):
    scene = bpy.context.scene
    cam_data = bpy.data.cameras.new(name)
    cam_data.type = "ORTHO"
    cam_data.ortho_scale = ortho_scale
    cam_data.clip_start = 0.05
    cam_data.clip_end = 100.0
    cam = bpy.data.objects.new(name, cam_data)
    scene.collection.objects.link(cam)
    cam.location = location
    if direction is None:
        # looking straight down (camera default -Z view direction, +Y image up)
        cam.rotation_euler = (0.0, 0.0, 0.0)
    else:
        cam.rotation_euler = Vector(direction).to_track_quat("-Z", "Y").to_euler()
    return cam


def make_cameras():
    """Blender authoring space: X right, Y front (+Y), Z up. Asset front = -Z."""
    iso_loc = (6.2, 6.2, 4.4)
    return {
        "front": make_camera("cam_front", (0.0, 8.0, 0.5), (0.0, -1.0, 0.0)),
        "side": make_camera("cam_side", (-8.0, 0.0, 0.5), (1.0, 0.0, 0.0)),
        "top": make_camera("cam_top", (0.0, 0.0, 8.0), None),
        "iso": make_camera("cam_iso", iso_loc,
                           (-iso_loc[0], -iso_loc[1], 0.5 - iso_loc[2]),
                           ORTHO_SCALE_ISO),
    }


# --------------------------------------------------------------------------- #
# import + material handling
# --------------------------------------------------------------------------- #
def import_model(path):
    ext = os.path.splitext(path)[1].lower()
    if ext == ".fbx":
        bpy.ops.import_scene.fbx(filepath=path)
    elif ext in (".glb", ".gltf"):
        bpy.ops.import_scene.gltf(filepath=path)
    else:
        raise RuntimeError("unsupported model extension: %s" % ext)
    # keep only model content (no imported cameras/lights)
    for ob in list(bpy.data.objects):
        if ob.type in ("CAMERA", "LIGHT"):
            bpy.data.objects.remove(ob, do_unlink=True)


def _principled(mat):
    if not mat.use_nodes or mat.node_tree is None:
        return None
    for node in mat.node_tree.nodes:
        if node.type == "BSDF_PRINCIPLED":
            return node
    return None


def material_fallbacks():
    """Give untextured/broken-texture materials contract-consistent colours.

    Textures referenced by the FBX are loaded lazily by Blender; if an atlas PNG
    is missing or was being rewritten when imported, the image has no data and
    would render as flat magenta.  Such broken texture nodes are disconnected
    here and the material falls back to the documented contract albedo, so a
    preview always shows real geometry with honest (never fabricated) shading.
    """
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
            # a Normal Map node left without a colour input would shade wrongly
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
                bsdf.inputs["Base Color"].default_value = color
                params = A.MATERIAL_PARAMS[key]
                bsdf.inputs["Metallic"].default_value = params["metallic"]
                bsdf.inputs["Roughness"].default_value = params["roughness"]
                bsdf.inputs["Alpha"].default_value = params["alpha"]
            except Exception:
                pass
        changed.append(mat.name)
    return changed


# --------------------------------------------------------------------------- #
# rendering
# --------------------------------------------------------------------------- #
class Renderer(object):
    def __init__(self):
        self.engine = "CYCLES"
        self.fallback_note = None
        self.results = []   # (path, ok, bytes, detail)

    def render(self, camera, filepath, ortho_scale=None):
        scene = bpy.context.scene
        if ortho_scale is not None:
            camera.data.ortho_scale = ortho_scale
        scene.camera = camera
        scene.render.filepath = filepath
        t0 = time.time()
        try:
            bpy.ops.render.render(write_still=True)
            dt = time.time() - t0
            size = os.path.getsize(filepath) if os.path.isfile(filepath) else 0
            return True, size, "%.1fs %s" % (dt, self.engine), None
        except Exception as exc:
            if self.engine == "CYCLES":
                # documented fallback: retry this and all later views on Workbench
                self.fallback_note = (
                    "Cycles CPU render failed (%s: %s); fell back to "
                    "BLENDER_WORKBENCH for all previews" % (type(exc).__name__, exc))
                self.engine = "WORKBENCH"
                setup_workbench()
                try:
                    bpy.ops.render.render(write_still=True)
                    dt = time.time() - t0
                    size = (os.path.getsize(filepath)
                            if os.path.isfile(filepath) else 0)
                    return True, size, "%.1fs %s" % (dt, self.engine), None
                except Exception as exc2:
                    return False, 0, "", "%s: %s" % (type(exc2).__name__, exc2)
            return False, 0, "", "%s: %s" % (type(exc).__name__, exc)


def png_header(path):
    with open(path, "rb") as f:
        head = f.read(33)
    if len(head) < 33 or head[:8] != b"\x89PNG\r\n\x1a\n":
        return None
    w, h = struct.unpack(">II", head[16:24])
    return {"width": w, "height": h, "bit_depth": head[24],
            "color_type": head[25]}


# --------------------------------------------------------------------------- #
# 50%-camera A/B composition (no text; pure render panels)
# --------------------------------------------------------------------------- #
def _load_pixels(path):
    """Load a PNG as a bottom-up float RGBA numpy array (Blender convention)."""
    img = bpy.data.images.load(path, check_existing=False)
    try:
        w, h = img.size
        pixels = np.array(img.pixels[:], dtype=np.float64).reshape(h, w, 4)
    finally:
        bpy.data.images.remove(img)
    return pixels


def compose_sheet(out_path, panel_paths, columns):
    """Compose rendered panels into one PNG grid (rows filled top-down)."""
    tiles = [_load_pixels(path) for path in panel_paths]
    height, width = tiles[0].shape[0], tiles[0].shape[1]
    rows = (len(tiles) + columns - 1) // columns
    canvas = np.zeros((rows * height, columns * width, 4), dtype=np.float64)
    canvas[..., 3] = 1.0
    for index, tile in enumerate(tiles):
        row = index // columns
        column = index % columns
        # numpy row 0 is the BOTTOM row of the image -> place rows top-down
        top_row = rows - 1 - row
        canvas[top_row * height:(top_row + 1) * height,
               column * width:(column + 1) * width] = tile
    img = bpy.data.images.new("preview_sheet", width=columns * width,
                              height=rows * height, alpha=True)
    try:
        img.pixels = canvas.reshape(-1).tolist()
        img.filepath_raw = out_path
        img.file_format = "PNG"
        img.save()
    finally:
        bpy.data.images.remove(img)
    return png_header(out_path)


def preview_prefix(tier, faction):
    """Folktails keeps the legacy ``<Tier>_*.png`` names; IronTeeth is suffixed."""
    return tier if faction == "Folktails" else "%s.%s" % (tier, faction)


def scale50_panel(tier, faction="Folktails"):
    return os.path.join(PREVIEW_DIR, "%s_scale50.png" % preview_prefix(tier, faction))


def build_ab_sheets(out):
    """Compose the 50%-camera A/B sheets from the rendered scale50 panels."""
    if np is None:
        out.append("ENGINE NOTE: numpy unavailable (%s); 50%% A/B sheets skipped"
                   % NUMPY_ERR)
        return []
    sheets = []
    sheet_jobs = (
        ("tier_ab_basic_vs_intermediate_50",
         [scale50_panel("Basic"), scale50_panel("Intermediate")], 2),
        ("tier_ab_advanced_vs_top_50",
         [scale50_panel("Advanced"), scale50_panel("Top")], 2),
        ("tiers_scale50_grid",
         [scale50_panel(t) for t in A.TIERS], 2),
        # v2.2 reference-image upgrade: faction A/B (same tier, two factions)
        ("faction_ab_basic_50",
         [scale50_panel("Basic"), scale50_panel("Basic", "IronTeeth")], 2),
        ("faction_ab_top_50",
         [scale50_panel("Top"), scale50_panel("Top", "IronTeeth")], 2),
        ("tiers_factions_scale50_grid",
         [scale50_panel(t, f) for f in A.FACTIONS for t in A.TIERS], 4),
    )
    for name, panels, columns in sheet_jobs:
        if not all(os.path.isfile(path) for path in panels):
            out.append("  %-34s SKIP (missing scale50 panel)" % name)
            continue
        dst = os.path.join(PREVIEW_DIR, name + ".png")
        try:
            header = compose_sheet(dst, panels, columns)
        except Exception as exc:                              # noqa: BLE001
            out.append("  %-32s FAILED to compose: %s: %s"
                       % (name, type(exc).__name__, exc))
            sheets.append((dst, False, 0, "%s: %s" % (type(exc).__name__, exc)))
            continue
        rows = (len(panels) + columns - 1) // columns
        ok = (header is not None
              and header["width"] == RESOLUTION * columns
              and header["height"] == RESOLUTION * rows
              and os.path.getsize(dst) > 4096)
        detail = ("%d panels %dx%d"
                  % (len(panels), RESOLUTION * columns, RESOLUTION * rows))
        out.append("  %-32s %s  %s"
                   % (name, "OK  " if ok else "FAIL", detail))
        sheets.append((dst, ok, os.path.getsize(dst) if os.path.isfile(dst) else 0,
                       detail))
    return sheets


def main():
    started = time.time()
    if not HAVE_BPY:
        sys.stdout.write(
            "BLOCKED: previews require Blender (bpy unavailable: %s).\n"
            "Run: blender.exe -b --factory-startup -noaudio --python "
            "art\\render_previews.py\n" % BPY_ERR)
        return 2
    os.makedirs(PREVIEW_DIR, exist_ok=True)
    renderer = Renderer()
    missing = []
    jobs = []
    for tier in A.TIERS:
        for faction in A.FACTIONS:
            stem = "%s.%s" % (tier, faction)
            path = os.path.join(HERE, "models", stem, "LOD0.fbx")
            if not os.path.isfile(path):
                alt = os.path.join(HERE, "models", stem, "LOD0.glb")
                path = alt if os.path.isfile(alt) else path
            if not os.path.isfile(path):
                missing.append(path)
                continue
            jobs.append((tier, faction, path))
    construction = os.path.join(HERE, "models", A.CONSTRUCTION_STAGE,
                                A.CONSTRUCTION_STAGE + ".fbx")
    if not os.path.isfile(construction):
        alt = os.path.join(HERE, "models", A.CONSTRUCTION_STAGE,
                           A.CONSTRUCTION_STAGE + ".glb")
        construction = alt if os.path.isfile(alt) else construction
    if missing:
        sys.stdout.write("BLOCKED: missing LOD0 model(s):\n  %s\n"
                         % "\n  ".join(missing))
        return 2
    if not os.path.isfile(construction):
        construction = None
        sys.stdout.write("note: construction stage model missing; skipping its "
                         "optional preview\n")
    out = []
    out.append("HDBF preview render: %d model(s) (tier x faction) -> %s"
               % (len(jobs), PREVIEW_DIR))
    views = [("front", "front", ORTHO_SCALE),
             ("side", "side", ORTHO_SCALE),
             ("top", "top", ORTHO_SCALE),
             ("scale50", "front", ORTHO_SCALE_50),
             ("iso", "iso", ORTHO_SCALE_ISO)]
    for tier, faction, path in jobs:
        reset_scene()
        try:
            import_model(path)      # also strips any imported cameras/lights
            material_fallbacks()
        except Exception as exc:
            out.append("  %-14s FAILED to import %s: %s: %s"
                       % (tier + "." + faction, path, type(exc).__name__, exc))
            continue
        setup_world_and_light()
        setup_cycles()
        cams = make_cameras()
        prefix = preview_prefix(tier, faction)
        for view, cam_key, scale in views:
            dst = os.path.join(PREVIEW_DIR, "%s_%s.png" % (prefix, view))
            ok, size, detail, err = renderer.render(cams[cam_key], dst, scale)
            if ok:
                hdr = png_header(dst)
                if (hdr is None or hdr["width"] != RESOLUTION
                        or hdr["height"] != RESOLUTION
                        or hdr["bit_depth"] != 8 or size < 4096):
                    ok, err = False, ("rendered file failed verification: %r %d bytes"
                                      % (hdr, size))
            out.append("  %-14s %-7s %s  %s"
                       % (tier + "." + faction, view, "OK  " if ok else "FAIL",
                          detail if ok else err))
            renderer.results.append((dst, ok, size, detail if ok else err))
    if construction:
        reset_scene()
        try:
            import_model(construction)
            material_fallbacks()
        except Exception as exc:
            out.append("  Construction FAILED to import: %s: %s"
                       % (type(exc).__name__, exc))
        else:
            setup_world_and_light()
            setup_cycles()
            cams = make_cameras()
            for view, cam_key, scale in (("front", "front", ORTHO_SCALE),
                                         ("iso", "iso", ORTHO_SCALE_ISO)):
                dst = os.path.join(PREVIEW_DIR,
                                   "ConstructionStage0_%s.png" % view)
                ok, size, detail, err = renderer.render(cams[cam_key], dst,
                                                        scale)
                if ok:
                    hdr = png_header(dst)
                    if (hdr is None or hdr["width"] != RESOLUTION
                            or hdr["height"] != RESOLUTION or size < 4096):
                        ok, err = False, ("rendered file failed verification: %r"
                                          % (hdr,))
                out.append("  %-12s %-7s %s  %s"
                           % ("Construction", view, "OK  " if ok else "FAIL",
                              detail if ok else err))
                renderer.results.append((dst, ok, size, detail if ok else err))
    out.append("")
    out.append("50%-camera A/B sheets (composed from the scale50 panels above):")
    for dst, ok, size, detail in build_ab_sheets(out):
        renderer.results.append((dst, ok, size, detail))
    if renderer.fallback_note:
        out.append("ENGINE NOTE: %s" % renderer.fallback_note)
    fails = [r for r in renderer.results if not r[1]]
    out.append("rendered %d/%d previews with %s engine%s"
               % (len(renderer.results) - len(fails), len(renderer.results),
                  renderer.engine,
                  " (fallback)" if renderer.engine != "CYCLES" else ""))
    if fails:
        out.append("VERDICT: FAIL (%d preview(s) missing/invalid)" % len(fails))
        for path, _ok, _size, err in fails:
            out.append("  bad: %s (%s)" % (path, err))
    else:
        out.append("VERDICT: PASS - %d preview image(s) rendered and verified as "
                   "8-bit PNG" % len(renderer.results))
    out.append("elapsed: %.1fs" % (time.time() - started))
    text = "\n".join(out)
    sys.stdout.write(text + "\n")
    try:
        sys.stdout.flush()
        sys.stderr.flush()
    except Exception:
        pass
    return 1 if fails else 0


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
