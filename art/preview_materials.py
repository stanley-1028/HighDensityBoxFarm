"""HDBF v2 preview / icon material binding: real vanilla `_D` textures (bpy).

Why this module exists
----------------------
The 16 shipped ``.timbermesh`` models use vanilla timberborn
``MaterialCollection`` material asset names (``BaseWood_Brown.Folktails``,
``WindowsAtlas.Folktails``, ...).  In game each name resolves to a Material
asset whose albedo (``_MainTex``) is **one specific 1024x1024 ``_D`` PNG** plus
a material ``_Color`` tint; the model UVs are small patches inside that PNG.
The old offline previews/icons bound the self-made 2048x2048 ART-01 atlas
(``art/textures/atlas_albedo.png``), which is why they looked fine while the
game showed checkerboard / yellow-black hazard stripes: the offline images were
sampling a texture the game never uses.

This module is the single place that turns the *shipped* material mapping into
a Blender material, so ``art/render_previews.py`` and ``art/render_icons.py``
sample exactly the same bytes + tint + UVs the game does:

* slot -> vanilla material name: ``timbermesh_common.MATERIAL_MAP`` (frozen
  contract, shared with the exporter; construction uses its
  ``("Basic", <faction>, True)`` key like ``gen_models_v2.patch_roles_for``).
* vanilla material name -> real ``_D`` texture + ``_Color`` tint (+ optional
  ``_BumpMap`` normal map): ``art/material_texture_map.json``
  (schema ``hdbf.material_texture_map/v1``), which is generated from the
  AssetStudio material dump by the material-texture analysis step.  When that
  file is not on disk yet, a builtin ground-truth fallback table transcribed
  from the same dump is used and a warning is recorded (never a fabricated
  texture).
* binding: Image Texture (full real PNG, UVMap) * ``_Color`` tint -> Principled
  Base Color.  The FBX mesh UVs already live in the mapped material's real
  texture space, so the patch/canvas is sampled automatically - no UV is changed
  here.  ART-07 (2026-10-27): the real-texture image nodes use
  ``extension = "REPEAT"`` (interpolation stays ``"Linear"``) so the offline
  sampler matches the in-game sampler; the ART-07 world UVs stay inside the
  material canvas, so REPEAT only matches the game's half-texel edge filtering,
  never the mapping (see ``art/ART07_UV_TILING.md`` section 6).
  The vanilla ``_BumpMap`` normal maps are **OFF by default**: the extracted
  Unity ``_N`` assets are stored in Unity's normal-map packing, not as a
  Blender/OpenGL tangent-space RGB normal map, and sampling them directly turns
  flat surfaces into mottled black/cream blotches (measured 2026-10-27: with
  them on, Intermediate_iso reads as patchy black/cream; with them off the
  plaster/glass/wood patches read correctly).  ``HDBF_PREVIEW_NORMALMAPS=1``
  keeps the experiment available for a future correctly-decoded normal map.

Glass is bound **opaque** on purpose: the game's ``WindowsAtlas`` material has
cutout off, so the light-blue pane patch is what the player sees; a transparent
Blender glass would be a second lie.  Documented slot colours (same values the
renders always used) are applied only when a texture file cannot be found, and
that fallback is always recorded as a warning in the render report.

Textures are read **read-only** from the vanilla SDK texture roots (default
``%USERPROFILE%\\.hdbf\\sdk\\vanilla-tex-all`` / ``vanilla-tex-extra``); nothing
is copied into ``art/`` or ``data/``.  ``HDBF_VANILLA_TEX_ROOT`` (os.pathsep
separated) prepends portable roots, ``HDBF_MATERIAL_TEXTURE_MAP`` overrides the
map path, ``HDBF_PREVIEW_NORMALMAPS`` disables/enables the (default-off)
vanilla _BumpMap experiment.

The module is importable by a plain Python interpreter (no bpy at import time):
``python art/preview_materials.py`` prints the resolved material table for every
shipping model context and exits non-zero if a texture is missing.
"""

from __future__ import annotations

import json
import os
import re
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
if HERE not in sys.path:
    sys.path.insert(0, HERE)

import atlas_layout as A  # noqa: E402  (pure-Python shared art contract)
import timbermesh_common as TC  # noqa: E402  (frozen slot -> material contract)

#: real map produced by the material-texture analysis (Lead-owned input).
MAP_PATH = os.path.join(HERE, "material_texture_map.json")
MAP_SCHEMA = "hdbf.material_texture_map/v1"

#: env override for portable texture roots (os.pathsep separated, searched first)
ENV_TEX_ROOT = "HDBF_VANILLA_TEX_ROOT"
#: env override for an alternative map file (QA / smoke tests)
ENV_MAP_PATH = "HDBF_MATERIAL_TEXTURE_MAP"
#: debug knob: "0"/"false"/"off" disables the vanilla _BumpMap normal maps
ENV_NORMALMAPS = "HDBF_PREVIEW_NORMALMAPS"

#: default read-only texture roots (also the fallback when the map is absent).
DEFAULT_TEXTURE_ROOTS = {
    "vanilla-tex-all": r"C:\Users\user\.hdbf\sdk\vanilla-tex-all",
    "vanilla-tex-extra": r"C:\Users\user\.hdbf\sdk\vanilla-tex-extra",
}

#: per-root env overrides, same names the map generator
#: (art/material_texture_analysis.py) uses for portability.
ROOT_ENV_OVERRIDES = {
    "vanilla-tex-all": "HDBF_VANILLA_TEX_ALL",
    "vanilla-tex-extra": "HDBF_VANILLA_TEX_EXTRA",
}

#: glass renders opaque (the game material has _CutoutWithAlpha = 0).
GLASS_SLOT = "M_HDBF_Glass"

#: bind the vanilla ``_BumpMap`` as a Blender Normal Map by default?  FALSE:
#: the extracted Unity normal assets use Unity's normal-map packing and are not
#: valid Blender tangent-space RGB normal maps; binding them raw produces
#: mottled black/cream shading on flat faces (verified 2026-10-27).  The
#: albedo * _Color tint binding below is the honest, artifact-free look.
DEFAULT_NORMAL_MAPS = False

#: neutral, contract-consistent albedo colours used ONLY when the real texture
#: file is missing; identical to the values both render scripts shipped before
#: this change, so a fallback render stays geometry-true and honest.
SLOT_FALLBACK_COLORS = {
    "M_HDBF_Wood": (0.31, 0.18, 0.09, 1.0),
    "M_HDBF_Metal": (0.50, 0.51, 0.52, 1.0),
    "M_HDBF_Glass": (0.42, 0.55, 0.52, 1.0),
    "M_HDBF_Fabric": (0.66, 0.55, 0.38, 1.0),
}

#: vanilla material -> real `_D` texture + `_Color` tint, transcribed from the
#: committed ground truth: see `art/material_texture_map.json` ->
#: `material_source_provenance` (AssetStudio dump of the game's
#: resources.assets Material assets, 2026-10-27).  Used only while the map file
#: is absent; the map always wins.
BUILTIN_MATERIAL_TEXTURES = {
    # ---- Folktails --------------------------------------------------------
    "BaseWood_Brown.Folktails": ("BaseWood_1K_D_Brown.png", (1.0, 1.0, 1.0)),
    "BaseWood_LightBrown.Folktails": ("BaseWood_1K_D_White.png",
                                      (0.6604, 0.5552, 0.4081)),
    "BaseWood_White.Folktails": ("BaseWood_1K_D_White.png",
                                 (0.8868, 0.8641, 0.8324)),
    "BaseWood_Yellow.Folktails": ("BaseWood_1K_D_White.png",
                                  (0.6981, 0.5676, 0.3458)),
    "IrregularPlanks_Brown.Folktails": ("IrregularPlanks_D_Brown.png",
                                        (1.0, 1.0, 1.0)),
    "IrregularPlanks_LightBrown.Folktails": ("IrregularPlanks_D_White.png",
                                             (0.6588, 0.5569, 0.4078)),
    "IrregularPlanks_White.Folktails": ("IrregularPlanks_D_White.png",
                                        (0.9151, 0.8834, 0.8590)),
    "IrregularPlanks_Yellow.Folktails": ("IrregularPlanks_D_White.png",
                                         (0.6902, 0.5686, 0.3098)),
    "IrregularPlanks_Mossy.Folktails": ("IrregularPlanks_D_Mossy.png",
                                        (1.0, 1.0, 1.0)),
    "PaintedMetal.Folktails": ("PaintedMetal_D.Folktails.png", (1.0, 1.0, 1.0)),
    "BaseMetal.Folktails": ("BaseMetal_D1.png", (1.0, 1.0, 1.0)),
    "WindowsAtlas.Folktails": ("WindowsAtlas_D.Folktails.png", (1.0, 1.0, 1.0)),
    "ThatchedRoof.Folktails": ("ThatchedRoof_D.png", (0.9245, 0.9245, 0.9245)),
    "Plaster_White.Folktails": ("Plaster_D_White.Folktails.png",
                                (1.0, 1.0, 1.0)),
    "Details.Folktails": ("Details_D.Folktails.png", (1.0, 1.0, 1.0)),
    "Moss.Folktails": ("Moss_D.Folktails.png", (1.0, 1.0, 1.0)),
    # ---- IronTeeth --------------------------------------------------------
    "BaseWood_DarkBrown.IronTeeth": ("BaseWood_1K_D_Grey.png",
                                     (0.3113, 0.2673, 0.2071)),
    "BaseWood_Grey.IronTeeth": ("BaseWood_1K_D_Grey.png",
                                (0.6226, 0.5796, 0.5257)),
    "BaseWood_Indigo.IronTeeth": ("BaseWood_1K_D_Indigo.png", (1.0, 1.0, 1.0)),
    "IrregularPlanks_DarkBrown.IronTeeth": ("IrregularPlanks_D_Grey.png",
                                            (0.3098, 0.2667, 0.2078)),
    "IrregularPlanks_Grey.IronTeeth": ("IrregularPlanks_D_Grey.png",
                                       (0.6235, 0.5804, 0.5255)),
    "IrregularPlanks_Indigo.IronTeeth": ("IrregularPlanks_D_Indigo.png",
                                         (1.0, 1.0, 1.0)),
    "IrregularPlanks_MetalForged.IronTeeth": ("PlanksWithMetal_D.png",
                                              (1.0, 1.0, 1.0)),
    "PaintedMetal.IronTeeth": ("PaintedMetal_D.IronTeeth.png", (1.0, 1.0, 1.0)),
    "BaseMetal.IronTeeth": ("BaseMetal_D2.png", (1.0, 1.0, 1.0)),
    "WindowsAtlas.IronTeeth": ("WindowsAtlas_D.IronTeeth.png", (1.0, 1.0, 1.0)),
    "RoofPlanks.IronTeeth": ("RoofPlanks_D.png", (1.0, 1.0, 1.0)),
    "Plaster_White.IronTeeth": ("Plaster_D_White.IronTeeth.png",
                                (1.0, 1.0, 1.0)),
    "Plaster_Orange.IronTeeth": ("Plaster_D_White.IronTeeth.png",
                                 (0.8353, 0.4824, 0.2588)),
    "Details.IronTeeth": ("Details_D.IronTeeth.png", (1.0, 1.0, 1.0)),
}

#: normal map overrides where the sibling-file rule does not hold or needs the
#: shared / split variant (verified in the AssetStudio material dump: e.g.
#: Moss.Folktails._BumpMap is Plaster_N.Folktails).
NORMAL_TEXTURE_OVERRIDES = {
    "Moss_D.Folktails.png": "Plaster_N.Folktails.png",
    "Details_D.Folktails.png": "Details_N.png",
    "Details_D.IronTeeth.png": "Details_N.png",
    "PaintedMetal_D.Folktails.png": "PaintedMetal_N.png",
    "PaintedMetal_D.IronTeeth.png": "PaintedMetal_N.png",
    "IrregularPlanks_D_Mossy.png": "IrregularPlanks_N_Mossy.png",
}

#: `_D` albedo file -> sibling `_N` normal map, by filename.  The first match
#: wins; exact exceptions are handled by NORMAL_TEXTURE_OVERRIDES above.
NORMAL_TEXTURE_PATTERNS = (
    # BaseWood_1K_D_Brown.png -> BaseWood_1K_N.png (tint-only variants share N)
    (re.compile(r"^BaseWood_1K_D(_[A-Za-z0-9]+)?\.png$"), "BaseWood_1K_N.png"),
    # IrregularPlanks_D_Brown.png -> IrregularPlanks_N.png
    (re.compile(r"^IrregularPlanks_D(_[A-Za-z0-9]+)?\.png$"),
     "IrregularPlanks_N.png"),
    # BaseMetal_D1/D2.png -> BaseMetal_N.png
    (re.compile(r"^BaseMetal_D[0-9]*\.png$"), "BaseMetal_N.png"),
    # <Name>_D_<Variant>.<Faction>.png -> <Name>_N.<Faction>.png
    (re.compile(r"^(.*)_D_[A-Za-z0-9]+(\.[A-Za-z]+)?\.png$"), None),
    # <Name>_D.<Faction>.png / <Name>_D.png -> <Name>_N[.<Faction>].png
    (re.compile(r"^(.*)_D(\.[A-Za-z]+)?\.png$"), None),
)


# --------------------------------------------------------------------------- #
# small pure-Python helpers
# --------------------------------------------------------------------------- #
def png_size(path):
    """(width, height) from the PNG IHDR, or None when not a readable PNG."""
    try:
        with open(path, "rb") as f:
            head = f.read(33)
    except OSError:
        return None
    if len(head) < 33 or head[:8] != b"\x89PNG\r\n\x1a\n":
        return None
    width = int.from_bytes(head[16:20], "big")
    height = int.from_bytes(head[20:24], "big")
    if width <= 0 or height <= 0:
        return None
    return width, height


def normal_maps_enabled():
    """Whether to bind the vanilla ``_BumpMap`` (default: off, see above)."""
    value = os.environ.get(ENV_NORMALMAPS)
    if value is None:
        return DEFAULT_NORMAL_MAPS
    return value.strip().lower() not in ("0", "false", "no", "off", "")


def _sibling_normal(texture_name):
    """Real `_N` normal map filename for a `_D` albedo filename (or None)."""
    if not texture_name:
        return None
    if texture_name in NORMAL_TEXTURE_OVERRIDES:
        return NORMAL_TEXTURE_OVERRIDES[texture_name]
    for pattern, replacement in NORMAL_TEXTURE_PATTERNS:
        match = pattern.match(texture_name)
        if not match:
            continue
        if replacement is not None:
            return replacement
        base = match.group(1)
        faction = match.group(2) or ""
        return "%s_N%s.png" % (base, faction)
    return None


def _format_tint(tint):
    if tint is None:
        return "-"
    return "(%.3f,%.3f,%.3f)" % tuple(tint[:3])


def _format_rect(rect):
    if not rect:
        return "-"
    return "[%s]" % ",".join(("%.4f" % v if isinstance(v, float) else str(v))
                             for v in rect)


# --------------------------------------------------------------------------- #
# material / texture book (pure Python)
# --------------------------------------------------------------------------- #
class MaterialTextureBook(object):
    """Resolved vanilla material -> real texture/tint table for one process."""

    def __init__(self, map_path=None):
        self.warnings = []
        self.map_path = (map_path or os.environ.get(ENV_MAP_PATH) or MAP_PATH)
        self.map_used = False
        self.map_schema = None
        self.roots = {}
        self.materials = {}
        self._resolved = {}
        self._load()

    # -- loading -------------------------------------------------------- #
    def _load(self):
        raw = None
        if os.path.isfile(self.map_path):
            raw = self._read_map_json()
        if raw is None:
            self.warnings.append(
                "material texture map not available (%s); using the builtin "
                "ground-truth table transcribed from the AssetStudio material "
                "dump (patch uv_rect QA disabled)" % self.map_path)
        else:
            self.map_used = True
            self.map_schema = raw.get("schema")
            if self.map_schema != MAP_SCHEMA:
                self.warnings.append(
                    "material texture map schema %r is not %r; reading it "
                    "anyway" % (self.map_schema, MAP_SCHEMA))
            roots = raw.get("texture_roots") or {}
            for name, path in roots.items():
                if isinstance(path, str) and path:
                    self.roots[str(name)] = path
            materials = raw.get("materials") or {}
            if not materials:
                self.warnings.append(
                    "material texture map %s has no 'materials' records; "
                    "falling back to the builtin table" % self.map_path)
            for name, rec in materials.items():
                self.materials[str(name)] = self._normalize_record(rec)
        if not self.materials:
            for name, value in BUILTIN_MATERIAL_TEXTURES.items():
                texture, tint = value
                self.materials[name] = {
                    "vanilla_material": name,
                    "texture": texture,
                    "texture_source": "vanilla-tex-all",
                    "texture_size": None,
                    "material_color_tint": list(tint),
                    "patch": None,
                    "source": "builtin-ground-truth",
                }
        if not self.roots:
            self.roots = dict(DEFAULT_TEXTURE_ROOTS)

    def _read_map_json(self):
        last_error = None
        for attempt in range(3):
            try:
                with open(self.map_path, encoding="utf-8") as f:
                    data = json.load(f)
                if not isinstance(data, dict):
                    raise ValueError("top-level JSON is not an object")
                return data
            except Exception as exc:  # noqa: BLE001 - report, never fabricate
                last_error = exc
                time.sleep(0.2 * (attempt + 1))
        self.warnings.append(
            "cannot parse material texture map %s (%s: %s); using the builtin "
            "ground-truth table" % (self.map_path, type(last_error).__name__,
                                    last_error))
        return None

    @staticmethod
    def _normalize_record(rec):
        out = {"source": "map"}
        if isinstance(rec, dict):
            out["texture"] = rec.get("texture")
            out["texture_source"] = rec.get("texture_source")
            size = rec.get("texture_size")
            out["texture_size"] = (list(size) if isinstance(size, (list, tuple))
                                   and len(size) == 2 else None)
            tint = rec.get("material_color_tint")
            if isinstance(tint, (list, tuple)) and len(tint) >= 3:
                out["material_color_tint"] = [float(v) for v in tint[:3]]
            else:
                out["material_color_tint"] = None
            patch = rec.get("patch")
            out["patch"] = patch if isinstance(patch, dict) else None
            canvas = rec.get("uv_canvas")
            out["uv_canvas"] = canvas if isinstance(canvas, dict) else None
            out["mode"] = rec.get("mode")
            out["px_per_m"] = rec.get("px_per_m")
            out["normal_texture"] = (rec.get("normal_texture")
                                     or rec.get("normal_texture_name"))
            out["normal_texture_source"] = rec.get("normal_texture_source")
            used = rec.get("used_in_material_map")
            out["used_in_material_map"] = (list(used) if isinstance(
                used, (list, tuple)) else [])
            out["patch_class"] = rec.get("patch_class")
        else:
            out["texture"] = None
        return out

    # -- lookup --------------------------------------------------------- #
    def texture_path(self, texture_name, source_hint=None):
        """Absolute path of a texture (searching the roots), or (None, None)."""
        if not texture_name:
            return None, None
        candidates = []
        env_roots = os.environ.get(ENV_TEX_ROOT, "")
        for chunk in env_roots.split(os.pathsep):
            chunk = chunk.strip()
            if chunk:
                candidates.append(("env:" + ENV_TEX_ROOT, chunk))
        if source_hint and source_hint in self.roots:
            candidates.append((source_hint,
                               self._root_path(source_hint)))
        for name in self.roots:
            if name != source_hint:
                candidates.append((name, self._root_path(name)))
        for name, path in DEFAULT_TEXTURE_ROOTS.items():
            if name not in self.roots:
                candidates.append((name,
                                   os.environ.get(ROOT_ENV_OVERRIDES.get(
                                       name, "")) or path))
        seen = set()
        for tag, path in candidates:
            key = os.path.normcase(os.path.abspath(path))
            if key in seen:
                continue
            seen.add(key)
            candidate = os.path.join(path, texture_name)
            if os.path.isfile(candidate):
                return candidate, tag
        return None, None

    def _root_path(self, name):
        """Root path with the per-root env override applied when set."""
        env_name = ROOT_ENV_OVERRIDES.get(name)
        if env_name:
            override = os.environ.get(env_name, "").strip()
            if override:
                return override
        return self.roots.get(name, DEFAULT_TEXTURE_ROOTS.get(name, ""))

    def record(self, vanilla_material):
        """Fully resolved record for one vanilla material asset name (cached)."""
        if vanilla_material in self._resolved:
            return self._resolved[vanilla_material]
        raw = self.materials.get(vanilla_material)
        if raw is None:
            result = {
                "vanilla_material": vanilla_material,
                "texture": None, "texture_path": None, "texture_source": None,
                "texture_size": None, "texture_size_declared": None,
                "material_color_tint": None, "patch": None,
                "mode": None, "px_per_m": None, "canvas_uv_rect": None,
                "normal_texture": None, "normal_path": None,
                "normal_source": None, "source": "missing",
                "status": "unknown-material",
            }
            self.warnings.append(
                "no texture mapping for vanilla material %r (not in %s and not "
                "in the builtin ground-truth table)" % (vanilla_material,
                                                        self.map_path))
        else:
            texture = raw.get("texture")
            path, source = self.texture_path(texture, raw.get("texture_source"))
            size = png_size(path) if path else None
            declared = raw.get("texture_size")
            tint = raw.get("material_color_tint")
            if tint is None:
                tint = [1.0, 1.0, 1.0]
            normal_name = raw.get("normal_texture")
            normal_path = None
            normal_source = None
            if not normal_name:
                normal_name = _sibling_normal(texture)
            if normal_name:
                normal_path, normal_source = self.texture_path(
                    normal_name, raw.get("normal_texture_source")
                    or raw.get("texture_source"))
            if path is None:
                status = "missing-texture"
                self.warnings.append(
                    "texture %r for vanilla material %r not found under %s"
                    % (texture, vanilla_material,
                       ", ".join(self.roots.values())))
            else:
                status = "bound"
                if declared and size and [size[0], size[1]] != list(declared):
                    self.warnings.append(
                        "texture %r is %dx%d but the map declares %r"
                        % (texture, size[0], size[1], list(declared)))
            result = {
                "vanilla_material": vanilla_material,
                "texture": texture,
                "texture_path": path,
                "texture_source": source,
                "texture_size": list(size) if size else None,
                "texture_size_declared": (list(declared) if declared else None),
                "material_color_tint": [float(v) for v in tint[:3]],
                "patch": raw.get("patch"),
                "mode": raw.get("mode"),
                "px_per_m": raw.get("px_per_m"),
                "canvas_uv_rect": self._canvas_uv_rect(vanilla_material,
                                                       raw.get("uv_canvas")),
                "normal_texture": normal_name,
                "normal_path": normal_path,
                "normal_source": normal_source,
                "source": raw.get("source", "map"),
                "status": status,
            }
            self._check_patch(vanilla_material, result)
        self._resolved[vanilla_material] = result
        return result

    def _canvas_uv_rect(self, vanilla_material, canvas):
        """ART-07 ``uv_canvas.uv_rect`` (ordered, inside [0,1]) or None.

        A *tile* material samples the whole texture (uv_rect 0,0,1,1); a *patch*
        material samples the curated coherent canvas rect.  Either way every
        shipped UV must stay inside this rect (ART07_UV_TILING.md sections 2
        and 5), so the preview QA checks containment against it instead of the
        out-of-date ART-06 small patch window.
        """
        if not isinstance(canvas, dict):
            return None
        uv_rect = canvas.get("uv_rect")
        if not (isinstance(uv_rect, (list, tuple)) and len(uv_rect) == 4):
            self.warnings.append("%s: uv_canvas has no usable uv_rect"
                                 % vanilla_material)
            return None
        values = [float(v) for v in uv_rect]
        if not (0.0 <= values[0] < values[2] <= 1.0
                and 0.0 <= values[1] < values[3] <= 1.0):
            self.warnings.append(
                "%s: uv_canvas uv_rect %r is not an ordered rect inside [0,1]"
                % (vanilla_material, values))
            return None
        return values

    def _check_patch(self, vanilla_material, record):
        """Sanity-check the informative patch rect against the texture size."""
        patch = record.get("patch")
        if not isinstance(patch, dict):
            return
        uv_rect = patch.get("uv_rect")
        if not (isinstance(uv_rect, (list, tuple)) and len(uv_rect) == 4):
            self.warnings.append("%s: patch has no usable uv_rect" %
                                 vanilla_material)
            record["patch_uv_rect_valid"] = False
            return
        if not (0.0 <= uv_rect[0] < uv_rect[2] <= 1.0
                and 0.0 <= uv_rect[1] < uv_rect[3] <= 1.0):
            self.warnings.append(
                "%s: patch uv_rect %r is not an ordered rect inside [0,1]"
                % (vanilla_material, list(uv_rect)))
            record["patch_uv_rect_valid"] = False
            return
        record["patch_uv_rect_valid"] = True
        size = record.get("texture_size")
        pixel_rect = patch.get("pixel_rect")
        if size and isinstance(pixel_rect, (list, tuple)) and len(pixel_rect) == 4:
            width, height = float(size[0]), float(size[1])
            expected = (pixel_rect[0] / width, 1.0 - pixel_rect[3] / height,
                        pixel_rect[2] / width, 1.0 - pixel_rect[1] / height)
            delta = max(abs(expected[i] - uv_rect[i]) for i in range(4))
            if delta > 1e-4:
                self.warnings.append(
                    "%s: patch pixel_rect %r does not match uv_rect %r "
                    "(max delta %.6f)" % (vanilla_material, list(pixel_rect),
                                          list(uv_rect), delta))

    # -- model context -------------------------------------------------- #
    def material_map_for(self, tier, faction, construction=False):
        """slot -> vanilla material name for one model context (contract map)."""
        key = (tier, faction, bool(construction))
        if key in TC.MATERIAL_MAP:
            return dict(TC.MATERIAL_MAP[key]), key
        # the construction-stage exports share ONE source mesh; the exporter keys
        # its patch roles on ("Basic", <faction>, True) (gen_models_v2.
        # patch_roles_for), which is the documented rule followed here silently.
        candidates = []
        if construction:
            candidates.append(("Basic", faction, True))
            candidates.append(("Basic", "Folktails", True))
            for (t, f, c) in TC.MATERIAL_MAP:
                if c and f == faction:
                    candidates.append((t, f, c))
            for (t, f, c) in TC.MATERIAL_MAP:
                if c:
                    candidates.append((t, f, c))
        else:
            for (t, f, c) in TC.MATERIAL_MAP:
                if not c:
                    candidates.append((t, f, c))
        for candidate in candidates:
            if candidate in TC.MATERIAL_MAP:
                if not construction:
                    self.warnings.append(
                        "no MATERIAL_MAP entry for context %r; using %r instead"
                        % (key, candidate))
                return dict(TC.MATERIAL_MAP[candidate]), candidate
        raise KeyError("no MATERIAL_MAP entry for model context %r" % (key,))

    def context_table(self, tier, faction, construction=False):
        """Resolved slot -> record table + context metadata for one model."""
        mapping, used_key = self.material_map_for(tier, faction, construction)
        slots = {}
        unknown = []
        for slot in A.MATERIAL_ORDER:
            if slot not in mapping:
                continue
            slots[slot] = self.record(mapping[slot])
        for slot in sorted(set(mapping) - set(A.MATERIAL_ORDER)):
            unknown.append({"slot": slot, "vanilla_material": mapping[slot]})
            self.warnings.append(
                "model context %r maps unknown material slot %r -> %r; the "
                "preview cannot bind that slot" % (used_key, slot,
                                                   mapping[slot]))
        return {
            "tier": tier, "faction": faction,
            "construction": bool(construction),
            "material_map_key": list(used_key),
            "slots": slots,
            "unknown_slots": unknown,
        }

    # -- reporting ------------------------------------------------------ #
    def describe(self):
        """One-line summary of where the table came from."""
        if self.map_used:
            return ("material texture map: %s (%d materials, schema %s)"
                    % (self.map_path, len(self.materials), self.map_schema))
        return ("material texture map: %s NOT FOUND -> builtin ground-truth "
                "table (%d materials)" % (self.map_path, len(self.materials)))


# --------------------------------------------------------------------------- #
# Blender binding (bpy is imported lazily so the table stays CI-checkable)
# --------------------------------------------------------------------------- #
def _bpy():
    try:
        import bpy  # noqa: PLC0415 - Blender-only import
    except Exception as exc:  # pragma: no cover - environment specific
        raise RuntimeError("preview_materials binding requires Blender (bpy "
                           "unavailable: %s: %s)"
                           % (type(exc).__name__, exc))
    return bpy


def _socket(sockets, identifier=None, name=None):
    for sock in sockets:
        if identifier is not None and getattr(sock, "identifier", None) == \
                identifier:
            return sock
    if name is not None:
        for sock in sockets:
            if sock.name == name:
                return sock
    raise KeyError("socket not found (identifier=%r name=%r)" %
                   (identifier, name))


def _load_image(path, colorspace):
    """Load a PNG into bpy.data.images; returns (image, error-string)."""
    bpy = _bpy()
    image = None
    try:
        image = bpy.data.images.load(path, check_existing=True)
    except Exception as exc:  # noqa: BLE001
        return None, "%s: %s" % (type(exc).__name__, exc)
    try:
        image.colorspace_settings.name = colorspace
    except Exception:  # noqa: BLE001 - non-critical
        pass
    if image.size[0] == 0 or image.size[1] == 0:
        try:
            image.reload()
        except Exception:  # noqa: BLE001
            pass
    if image.size[0] == 0 or image.size[1] == 0:
        try:
            bpy.data.images.remove(image)
        except Exception:  # noqa: BLE001
            pass
        return None, "image has no pixel data"
    return image, None


def _find_material(slot):
    bpy = _bpy()
    material = bpy.data.materials.get(slot)
    if material is not None:
        return material
    # tolerate import-side suffixes ("M_HDBF_Wood.001") but not other slots
    for mat in bpy.data.materials:
        if mat.name == slot or mat.name.startswith(slot + "."):
            return mat
    return None


def slot_of_material(name):
    """M_HDBF_* slot of a Blender material name, or None."""
    if name in A.MATERIAL_ORDER:
        return name
    for slot in A.MATERIAL_ORDER:
        if name.startswith(slot + "."):
            return slot
    return None


def _apply_material(mat, slot, tint, image, normal_image):
    """Rebuild one Blender material as the game's material (texture * tint)."""
    nt = mat.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    out.location = (620, 0)
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    bsdf.location = (300, 0)
    nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    params = A.MATERIAL_PARAMS[slot]
    bsdf.inputs["Metallic"].default_value = float(params["metallic"])
    bsdf.inputs["Roughness"].default_value = float(params["roughness"])
    # the game's WindowsAtlas material has cutout off -> opaque pane in game
    alpha = 1.0 if slot == GLASS_SLOT else float(params["alpha"])
    bsdf.inputs["Alpha"].default_value = alpha
    try:
        mat.blend_method = "OPAQUE"
        mat.surface_render_method = "DITHERED"
    except Exception:  # noqa: BLE001 - Blender-version dependent
        pass
    if image is not None:
        tex = nt.nodes.new("ShaderNodeTexImage")
        tex.location = (-420, 220)
        tex.image = image
        tex.interpolation = "Linear"
        # ART-07: the in-game sampler is Repeat (Texture2D.m_WrapMode = 0); the
        # world UVs stay inside the material canvas, so this only matches the
        # game's half-texel edge filtering (see ART07_UV_TILING.md section 6).
        tex.extension = "REPEAT"
        tex.label = os.path.basename(image.filepath_raw or image.name)
        if tint is None or tuple(round(v, 6) for v in tint[:3]) == (1.0, 1.0,
                                                                    1.0):
            nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
        else:
            # albedo * material _Color tint (what the game multiplies by)
            mix = nt.nodes.new("ShaderNodeMix")
            mix.location = (0, 220)
            mix.data_type = "RGBA"
            mix.blend_type = "MULTIPLY"
            mix.clamp_result = False
            _socket(mix.inputs, identifier="Factor_Float").default_value = 1.0
            nt.links.new(tex.outputs["Color"],
                         _socket(mix.inputs, identifier="A_Color"))
            _socket(mix.inputs, identifier="B_Color").default_value = (
                tint[0], tint[1], tint[2], 1.0)
            nt.links.new(_socket(mix.outputs, identifier="Result_Color"),
                         bsdf.inputs["Base Color"])
    if normal_image is not None:
        ntex = nt.nodes.new("ShaderNodeTexImage")
        ntex.location = (-420, -160)
        ntex.image = normal_image
        ntex.interpolation = "Linear"
        ntex.extension = "REPEAT"
        ntex.label = os.path.basename(normal_image.filepath_raw or
                                      normal_image.name)
        nmap = nt.nodes.new("ShaderNodeNormalMap")
        nmap.location = (0, -160)
        nmap.inputs["Strength"].default_value = 1.0
        nt.links.new(ntex.outputs["Color"], nmap.inputs["Color"])
        nt.links.new(nmap.outputs["Normal"], bsdf.inputs["Normal"])
    mat.use_nodes = True
    mat.diffuse_color = SLOT_FALLBACK_COLORS.get(slot, (0.5, 0.5, 0.5, 1.0))


def _fallback_material(mat, slot, reason):
    """Apply the documented slot colour (never a fabricated texture)."""
    nt = mat.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    out.location = (620, 0)
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    bsdf.location = (300, 0)
    nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    color = SLOT_FALLBACK_COLORS.get(slot, (0.5, 0.5, 0.5, 1.0))
    params = A.MATERIAL_PARAMS.get(slot, {"metallic": 0.0, "roughness": 0.7,
                                          "alpha": 1.0})
    bsdf.inputs["Base Color"].default_value = color
    bsdf.inputs["Metallic"].default_value = float(params["metallic"])
    bsdf.inputs["Roughness"].default_value = float(params["roughness"])
    bsdf.inputs["Alpha"].default_value = (1.0 if slot == GLASS_SLOT
                                          else float(params["alpha"]))
    mat.diffuse_color = color
    try:
        mat.blend_method = "OPAQUE"
    except Exception:  # noqa: BLE001
        pass
    return reason


def _uv_bbox_by_slot():
    """Per-slot UV bbox of every mesh in the scene (QA: is the patch sampled?)."""
    bpy = _bpy()
    bboxes = {}
    for ob in bpy.data.objects:
        if ob.type != "MESH":
            continue
        mesh = ob.data
        uv_layers = mesh.uv_layers
        if not uv_layers:
            continue
        uv_data = (uv_layers.active or uv_layers[0]).data
        for poly in mesh.polygons:
            if poly.material_index >= len(mesh.materials):
                continue
            material = mesh.materials[poly.material_index]
            slot = slot_of_material(material.name) if material else None
            if slot is None:
                continue
            box = bboxes.get(slot)
            for li in poly.loop_indices:
                u, v = uv_data[li].uv[0], uv_data[li].uv[1]
                if box is None:
                    box = [u, v, u, v]
                    bboxes[slot] = box
                else:
                    box[0] = min(box[0], u)
                    box[1] = min(box[1], v)
                    box[2] = max(box[2], u)
                    box[3] = max(box[3], v)
    return bboxes


def _patch_containment(uv_box, uv_rect, eps=5e-5):
    if not uv_box or not uv_rect or len(uv_rect) != 4:
        return None
    return bool(uv_box[0] >= uv_rect[0] - eps and uv_box[1] >= uv_rect[1] - eps
                and uv_box[2] <= uv_rect[2] + eps
                and uv_box[3] <= uv_rect[3] + eps)


def _context_label(tier, faction, construction):
    """Human-readable model context label used in every report line."""
    if construction:
        return "%s [construction stage, %s materials]" % (tier, faction)
    return "%s.%s" % (tier, faction)


def bind_scene_materials(tier, faction, construction=False, book=None,
                        normal_maps=None, force_fallback=False):
    """Bind the real vanilla `_D` textures to the scene's M_HDBF_* materials.

    Returns a JSON-ready report dict (slots, warnings, counts); never raises on
    a missing texture - it records the fallback + warning instead.
    """
    bpy = _bpy()
    book = book or MaterialTextureBook()
    normal_maps = normal_maps_enabled() if normal_maps is None else normal_maps
    table = book.context_table(tier, faction, construction)
    context_label = _context_label(tier, faction, construction)
    warnings = []
    slots = []
    bound = 0
    fallbacks = 0
    uv_boxes = _uv_bbox_by_slot()
    for slot, record in table["slots"].items():
        entry = {
            "slot": slot,
            "vanilla_material": record["vanilla_material"],
            "status": record["status"],
            "texture": record["texture"],
            "texture_path": record["texture_path"],
            "texture_source": record["texture_source"],
            "texture_size": record["texture_size"],
            "tint": record["material_color_tint"],
            "normal_texture": record["normal_texture"],
            "normal_path": (record["normal_path"] if normal_maps else None),
            "patch": record["patch"],
            "mode": record.get("mode"),
            "px_per_m": record.get("px_per_m"),
            "canvas_uv_rect": record.get("canvas_uv_rect"),
            "uv_bbox": uv_boxes.get(slot),
            "uv_inside_patch": None,
            "uv_inside_canvas": None,
            "table_source": record["source"],
        }
        material = _find_material(slot)
        if material is None:
            warnings.append("%s: imported model has no material slot %r"
                            % (context_label, slot))
            entry["status"] = "slot-not-in-model"
            slots.append(entry)
            continue
        image = None
        normal_image = None
        if force_fallback:
            entry["status"] = "forced-fallback"
        elif record["texture_path"] is None:
            entry["status"] = "fallback-missing-texture"
        else:
            image, error = _load_image(record["texture_path"], "sRGB")
            if image is None:
                warnings.append("%s: %s: cannot load texture %s (%s)"
                                % (context_label, slot,
                                   record["texture_path"], error))
                entry["status"] = "fallback-unreadable-texture"
                entry["texture_error"] = error
            elif normal_maps and record.get("normal_path"):
                normal_image, error = _load_image(record["normal_path"],
                                                  "Non-Color")
                if normal_image is None:
                    warnings.append("%s: %s: cannot load normal map %s (%s)"
                                    % (context_label, slot,
                                       record["normal_path"], error))
                    entry["normal_texture"] = None
                    entry["normal_path"] = None
        if image is not None:
            _apply_material(material, slot, record["material_color_tint"],
                            image, normal_image)
            entry["status"] = "bound"
            # ART-07: containment is checked against the material *canvas*
            # (full texture for tile materials / curated rect for patch
            # materials) when the map carries it; older maps fall back to the
            # informative ART-06 patch rect.
            canvas_rect = record.get("canvas_uv_rect")
            entry["uv_inside_patch"] = _patch_containment(
                entry["uv_bbox"],
                (record["patch"] or {}).get("uv_rect"))
            if canvas_rect is not None:
                entry["uv_inside_canvas"] = _patch_containment(
                    entry["uv_bbox"], canvas_rect)
                if entry["uv_inside_canvas"] is False:
                    warnings.append(
                        "%s: %s UVs %s lie outside the mapped %s uv_canvas %s "
                        "(ART-07 model UVs not regenerated yet?)"
                        % (context_label, slot, _format_rect(entry["uv_bbox"]),
                           record["vanilla_material"],
                           _format_rect(canvas_rect)))
            elif entry["uv_inside_patch"] is False:
                warnings.append(
                    "%s: %s UVs %s lie outside the mapped %s patch uv_rect %s "
                    "(model UVs not regenerated yet?)"
                    % (context_label, slot, _format_rect(entry["uv_bbox"]),
                       record["vanilla_material"],
                       _format_rect((record["patch"] or {}).get("uv_rect"))))
        else:
            reason = ("forced fallback (QA)" if force_fallback
                      else "texture unavailable: %r" % (record["texture"],))
            _fallback_material(material, slot, reason)
            fallbacks += 1
            if not force_fallback:
                warnings.append(
                    "%s: %s -> %s rendered with the documented slot colour "
                    "%s, NOT the real texture (%s)"
                    % (context_label, slot, record["vanilla_material"],
                       _format_tint(SLOT_FALLBACK_COLORS.get(
                           slot, (0.5, 0.5, 0.5))[:3]), reason))
        if force_fallback:
            entry["status"] = "forced-fallback"
        if entry["status"] == "bound":
            bound += 1
        slots.append(entry)
    # materials in the scene that the tier does not map at all
    mapped_slots = set(table["slots"])
    for material in bpy.data.materials:
        slot = slot_of_material(material.name)
        if slot is None or slot in mapped_slots or \
                slot in [s["slot"] for s in slots]:
            continue
        warnings.append("%s: material %r is not part of the tier's material "
                        "map; rendered with the documented slot colour"
                        % (context_label, material.name))
        _fallback_material(material, slot, "unmapped slot")
        fallbacks += 1
        slots.append({
            "slot": slot, "vanilla_material": None, "status": "unmapped-slot",
            "texture": None, "texture_path": None, "texture_source": None,
            "texture_size": None, "tint": None, "normal_texture": None,
            "normal_path": None, "patch": None, "uv_bbox": uv_boxes.get(slot),
            "uv_inside_patch": None, "uv_inside_canvas": None,
            "mode": None, "px_per_m": None, "canvas_uv_rect": None,
            "table_source": None,
        })
    return {
        "context": {
            "tier": tier, "faction": faction,
            "construction": bool(construction),
            "label": context_label,
            "material_map_key": table["material_map_key"],
        },
        "map": {
            "path": book.map_path,
            "used": book.map_used,
            "schema": book.map_schema,
            "materials": len(book.materials),
            "texture_roots": dict(book.roots),
        },
        "normal_maps": bool(normal_maps),
        "slots": slots,
        "warnings": warnings,
        "counts": {"slots": len(slots), "bound": bound,
                   "fallback": fallbacks},
    }


def format_binding_report(report, indent="    "):
    """Human-readable lines for the render scripts' stdout reports."""
    ctx = report["context"]
    slots = report["slots"]
    counts = report["counts"]
    lines = ["%s%s: materials bound to real vanilla _D textures: %d/%d slots"
             " (%d fallback)%s"
             % (indent, ctx["label"], counts["bound"], counts["slots"],
                counts["fallback"],
                "" if report["normal_maps"] else " [normal maps off]")]
    for entry in slots:
        texture = entry["texture"] or "-"
        size = entry["texture_size"] or []
        size_text = ("%dx%d" % (size[0], size[1])) if len(size) == 2 else "?"
        uv_inside = entry.get("uv_inside_canvas")
        if uv_inside is None:
            uv_inside = entry["uv_inside_patch"]
        if entry["status"] == "bound":
            state = "OK"
        elif entry["status"] == "forced-fallback":
            state = "FALLBACK(forced QA)"
        elif entry["status"] == "slot-not-in-model":
            state = "SKIP(slot absent)"
        elif entry["status"] == "unmapped-slot":
            state = "FALLBACK(unmapped)"
        else:
            state = "FALLBACK"
        lines.append(
            "%s  %-13s %-34s %-28s %-11s tint=%s%s  uv=%s%s%s"
            % (indent, entry["slot"], str(entry["vanilla_material"]),
               texture, size_text, _format_tint(entry["tint"]),
               (" normal=%s" % entry["normal_texture"]
                if entry["normal_texture"] else ""),
               ("inside" if uv_inside else
                ("OUTSIDE" if uv_inside is False else "-")),
               (" %s %.1f px/m" % (entry["mode"], float(entry["px_per_m"]))
                if entry.get("mode") and entry.get("px_per_m") else ""),
               "  [%s]" % state if state != "OK" else ""))
    for warning in report["warnings"]:
        lines.append("%s  MATERIAL WARNING: %s" % (indent, warning))
    return lines


# --------------------------------------------------------------------------- #
# pure-Python self test / table dump
# --------------------------------------------------------------------------- #
def map_usage_cross_check(book):
    """Compare the map's ``used_in_material_map`` with the live MATERIAL_MAP.

    Detects drift between the Lead-generated map and
    ``timbermesh_common.MATERIAL_MAP`` (the same context strings the validators
    and the L1 material-patch tests use).  Returns (ok, problems).
    """
    if not book.map_used:
        return None, []
    declared = {}
    for name, raw in book.materials.items():
        for key in (raw.get("used_in_material_map") or []):
            declared.setdefault(name, set()).add(str(key))
    computed = {}
    for construction in (False, True):
        for faction in A.FACTIONS:
            for tier in A.TIERS:
                try:
                    table = book.context_table(tier, faction, construction)
                except KeyError:
                    continue
                for slot, record in table["slots"].items():
                    key = "%s.%s.%s%s" % (
                        tier, faction, slot,
                        ".Construction" if construction else "")
                    computed.setdefault(record["vanilla_material"],
                                        set()).add(key)
    problems = []
    for material in sorted(set(declared) | set(computed)):
        were = declared.get(material, set())
        now = computed.get(material, set())
        if were != now:
            problems.append(
                "%r: material_texture_map.json says used by %r, but "
                "timbermesh_common.MATERIAL_MAP now resolves %r"
                % (material, sorted(were), sorted(now)))
    return (not problems), problems


def map_contract_cross_check():
    """Run ``material_patches.validate_contract`` (map <-> MATERIAL_MAP contract).

    The UV-assignment module is the canonical accessor for the shipped patches
    (``art/material_patches.py``); the previews read the same JSON schema
    directly so they keep working when the map is absent, but a full preview
    run must agree with that contract.  Returns (ok, problems) or (None, []).
    """
    try:
        import material_patches as MP  # noqa: PLC0415 - Lead-owned accessor
    except Exception as exc:  # noqa: BLE001
        return None, ["material_patches contract check unavailable: %s: %s"
                      % (type(exc).__name__, exc)]
    try:
        MP.validate_contract()
        return True, []
    except AssertionError as exc:
        return False, ["material_patches.validate_contract() failed: %s" % exc]


def selftest(verbose=True):
    """Resolve every shipping model context; return (ok, lines, problems)."""
    book = MaterialTextureBook()
    lines = [book.describe()]
    problems = []
    contexts = []
    for construction in (False, True):
        for faction in A.FACTIONS:
            for tier in A.TIERS:
                contexts.append((tier, faction, construction))
    seen = set()
    labels = {}
    for tier, faction, construction in contexts:
        table = book.context_table(tier, faction, construction)
        label = _context_label(tier, faction, construction)
        labels.setdefault(label, 0)
        labels[label] += 1
        for slot, record in table["slots"].items():
            seen.add(record["vanilla_material"])
            if record["texture_path"] is None:
                problems.append("%s: %s missing texture %r"
                                % (label, slot, record["texture"]))
            if verbose and labels[label] <= 1:
                lines.append(
                    "  %-38s %-13s <- %-28s %s tint=%s"
                    % (label, slot, record["texture"],
                       "found" if record["texture_path"] else "MISSING",
                       _format_tint(record["material_color_tint"])))
    usage_ok, usage_problems = map_usage_cross_check(book)
    if usage_ok is not None:
        lines.append("material_texture_map.json <-> MATERIAL_MAP usage "
                     "cross-check: %s" % ("OK" if usage_ok else "MISMATCH"))
        problems.extend(usage_problems)
    contract_ok, contract_problems = map_contract_cross_check()
    if contract_ok is not None:
        lines.append("material_patches.validate_contract(): %s"
                     % ("OK" if contract_ok else "FAILED"))
        problems.extend(contract_problems)
    # every warning (load-time + resolution) once, after all records resolved
    for warn in book.warnings:
        lines.append("WARNING: %s" % warn)
    lines.append("resolved %d distinct vanilla materials across %d contexts"
                 % (len(seen), len(contexts)))
    if not book.map_used:
        lines.append("NOTE: art/material_texture_map.json missing; builtin "
                     "ground-truth table in use (no patch uv_rect QA)")
    return (not problems), lines, problems


def main(argv=None):
    ok, lines, problems = selftest(verbose=True)
    sys.stdout.write("\n".join(lines) + "\n")
    if problems:
        sys.stdout.write("VERDICT: FAIL - missing texture(s)\n")
        for problem in problems:
            sys.stdout.write("  %s\n" % problem)
        return 1
    sys.stdout.write("VERDICT: PASS - every shipping material resolves to a "
                     "real vanilla _D texture\n")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
