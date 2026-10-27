# ART-07 ??native-texel-density world UV mapping (contract)

Status: **frozen for ART-07** (2026-10-27). This file is the shared contract for
`art/gen_models_v2.py`, `art/material_uv_policy.py`, `art/material_texture_analysis.py`,
`art/validate_models.py`, `art/validate_timbermesh.py`, the L1 C# contract tests and
the preview/icon renderers.

The user request: *"?芾?韐游?舀??啣?擃捶??嚗停?游末鈭?* ??the ART-06 per-material
real-texture **patch** mapping is correct but far too low resolution
(`PATCH_TEXELS_PER_GRID=32` ??a 3 m face samples 96 px of a 1024 px texture).
ART-07 replaces the small patch with a **world-scale planar projection** at the
highest texel density that is legal in the real game.

## 1. Hard in-game constraint: the runtime auto-atlas remaps UVs

Verified from the installed game's IL (`Timberborn.PrefabOptimization`,
`art/ART07_PROVENANCE.md` reproduces the quotes):

* `AutoAtlaserSpec` (`Configuration/AutoAtlaser.blueprint`, extracted from
  `resources.assets`) puts **every material we ship** into
  `FactionAtlas.Folktails` / `FactionAtlas.IronTeeth`.
* `PrefabOptimizationChain` order is `TimbermeshPrefabOptimizer` **then**
  `AutoAtlasingPrefabOptimizer`. `TimbermeshImporter`/`StaticMeshBuilder` creates
  plain `MeshRenderer`s, so our models are processed by the auto-atlas step too.
* `AutoAtlaser.GenerateAutoAtlasFragments` builds one atlas texture of
  `1024 * sizeMultiplier` and gives each fragment the **native 1024簡** cell
  (`CombineFragments` copies the fragment at native resolution) together with
  `UVScale = (1/sizeMultiplier, 1/sizeMultiplier)` and
  `UVOffset = (cellX/sizeMultiplier, cellY/sizeMultiplier)`;
  `AutoAtlasingPrefabOptimizer.OptimizeMeshRenderer` rewrites the mesh UVs as
  `uv_atlas = uv_mesh * UVScale + UVOffset`.
* Consequence: a mesh UV component **> 1** leaves the material's own cell and
  samples another material in the atlas. `Texture2D.m_WrapMode = 0` (Repeat) is
  real (texture *assets* are Repeat, `m_FilterMode=2`, `m_Aniso=16`), but Repeat
  is never reached because the UVs are remapped into an atlas cell first.

??**ART-07 rule: every shipped UV must stay inside its material's own canvas,
i.e. inside `[0, 1]` for tile materials and inside the canvas rect for patch
materials. No UV component > 1, no negative UV.** The hard ceiling for a
texture whose canvas must cover the whole 3 m model is therefore
`1024 / 3.0 = 341 px/m` (tile materials) or `canvas_px / 3.0` (patch materials).
This is ~10x (tile) / ~5x (patch) sharper than ART-06 and is the native
resolution of the game's own material atlases.

## 2. Mapping rule (one function, two canvas kinds)

`art/gen_models_v2.py::assign_world_tiled_uvs(mb, slot_materials)`

For every face of the model (works on the author-space mesh, Blender Z-up,
front +Y; the exporter later converts to game space):

1. Face normal ??dominant axis (Newell normal, normalised to unit length first,
   then each `|component|` rounded to **4 decimals** before the dominant test ??   this frozen tie-break X > Y > Z is required because 45簞 bevel faces have two
   or three equal components and the mesh round-trips through float32; the
   generator and every validator/test must use the same rule):
   * `簣X` ??`U = y`, `V = z`
   * `簣Y` ??`U = x`, `V = z`
   * `簣Z` ??`U = x`, `V = y`
2. World-space centre of the projection (frozen): `x = 0`, `y = 0`, `z = 0.5`.
3. Per vanilla material (from `art/material_uv_policy.py`, mirrored into
   `art/material_texture_map.json`):
   * `mode` = `"tile"` | `"patch"`
   * canvas: `"tile"` ??the whole `_D` texture `[0,1]簡` (with a 2 texel guard:
     usable = 1020 px); `"patch"` ??the curated `canvas_pixel_rect` inside the
     texture (usable = rect minus 2 texel guard).
   * `coverage_u_m`, `coverage_v_m`: the world span the canvas must cover
     (measured worst case over all shipped LOD0/1/2 models, see 禮3).
   * `px_per_m = min(nominal_px_per_m, usable_w / coverage_u_m,
     usable_h / coverage_v_m)` (one density per material, uniform over the model).
   * UV centre = centre of the usable canvas.
4. `u = uc + (U_world - cx) * px_per_m / texture_w`
   `v = vc + (V_world - cz_or_cy) * px_per_m / texture_h`
   (texture 1024?1024; `v` increases with world `z`/`y`, matching the PNG's
   bottom-up UV convention).

Properties: deterministic (pure function of the frozen geometry + policy),
constant texel density over the whole model (no stretched small faces), no
per-face random offsets, coplanar faces of one material are continuous, all UVs
inside the material canvas (asserted by the generator), no NaN/Inf.

`assign_flat_patch_uvs` stays importable (legacy preview fallback / historical
tests) but is **no longer called** for shipped models.

## 3. Material policy table (frozen)

Densities are `px/m` at 1024簡 textures. `tile_meters` is the nominal world size
of one full texture for the class (REQ ranges); the *effective* world span of
the canvas is `1024/px_per_m` because the no-repeat constraint of 禮1 forces the
canvas to cover the whole model. `coverage_*` values are measured from the
ART-06 geometry (unchanged geometry in ART-07).

| vanilla material | mode | tile_meters (class) | canvas px rect | coverage u/v m | px/m |
|---|---|---:|---|---|---:|
| BaseWood_Brown.Folktails | patch | ??| 4,4,512,444 | 3.0 / 3.0 | 146.0 |
| BaseWood_DarkBrown.IronTeeth | patch | ??| 4,4,512,444 | 3.0 / 3.0 | 146.0 |
| BaseWood_Grey.IronTeeth | patch | ??| 4,4,512,444 | 3.0 / 3.0 | 146.0 |
| BaseWood_LightBrown.Folktails | patch | ??| 4,4,512,444 | 3.0 / 3.0 | 146.0 |
| BaseWood_White.Folktails | patch | ??| 4,4,512,444 | 3.0 / 3.0 | 146.0 |
| BaseMetal.Folktails | patch | ??| 8,8,504,504 | 3.0 / 3.0 | 164.7 |
| BaseMetal.IronTeeth | patch | ??| 8,8,504,504 | 3.0 / 3.0 | 164.7 |
| Details.Folktails | patch | ??| 4,516,508,1012 | 3.0 / 3.0 | 164.7 |
| Details.IronTeeth | patch | ??| 4,516,508,1012 | 3.0 / 3.0 | 164.7 |
| WindowsAtlas.Folktails | patch | ??| 772,772,1020,1020 | 2.956 / 2.956 | 83.2 |
| WindowsAtlas.IronTeeth | patch | ??| 772,772,1020,1020 | 2.956 / 2.956 | 83.2 |
| Plaster_White.Folktails | tile | 2.6 | full 1024簡 | 3.0 / 3.0 | 340.0 |
| Plaster_Orange.IronTeeth | tile | 2.6 | full 1024簡 | 3.0 / 3.0 | 340.0 |
| Moss.Folktails | tile | 2.2 | full 1024簡 | 2.98 / 2.98 | 342.3 |
| RoofPlanks.IronTeeth | tile | 2.2 | full 1024簡 | 2.84 / 2.84 | 359.2 |
| IrregularPlanks_Brown.Folktails | tile | 2.4 | full 1024簡 | 3.0 / 3.0 | 340.0 |
| IrregularPlanks_MetalForged.IronTeeth | tile | 2.2 | full 1024簡 | 3.0 / 3.0 | 340.0 |

Rationale per canvas:
* `tile` = the texture is a **full-bleed surface texture** (plaster, moss,
  planks, roof planks): the whole 1024簡 canvas can be sampled anywhere.
* `patch` = the texture is an **atlas sheet** (wood variants, sheet-metal
  variants, detail/window atlases): the canvas is the largest coherent region
  that is free of hazard stripes / faction banners / logos / text / blank
  canvas margins. The wood canvas is the vertical-plank field; the metal canvas
  is the plain worn sheet; the details canvas is the framed bottom-left plank
  block (dark timber panels); the window canvas is the flat glass pane.
* `BaseMetal` canvas is identical for both factions (the two sheet-metal
  textures have the same atlas layout - required by the shared construction
  UV set).
* `PaintedMetal.*` stay banned on large faces (`large_shell_policy`), unchanged.

`px_per_m` is computed with a 2 texel inset per canvas edge:
`usable = canvas_px - 4` (tile) / `canvas_px - 4` (patch).

### Construction stage

The construction stage shares one source mesh + one baked UV set for both
factions (unchanged design). It is now baked from the **Folktails construction
material set** (`BaseWood_Brown.Folktails`, `BaseMetal.Folktails`) with the very
same `assign_world_tiled_uvs`; the IronTeeth files reuse those UVs with
`RoofPlanks.IronTeeth` / `BaseMetal.IronTeeth`:
* `BaseMetal.Folktails` and `BaseMetal.IronTeeth` share the same canvas rect and
  density ??identical sampling for both factions.
* `RoofPlanks.IronTeeth` is a `tile` material (canvas = full texture), so the
  wood canvas UVs of `BaseWood_Brown.Folktails` are inside its canvas too.
`art/material_texture_map.json ??construction_shared_patches` is updated to the
new canvas rects and records per-texture stats inside those windows (the
`construction_wood` group: BaseWood_1K_D_Brown.png + RoofPlanks_D.png;
`construction_metal`: BaseMetal_D1.png + BaseMetal_D2.png).

## 4. `material_texture_map.json` schema additions (schema stays `/v1`)

Every entry of `materials` gains:

```json
"mode": "tile",
"tile_meters": 2.6,
"texture_size": [1024, 1024],
"wrap": "repeat",
"uv_canvas": {
  "pixel_rect": [0, 0, 1024, 1024],
  "uv_rect": [0.0, 0.0, 1.0, 1.0],
  "size_px": [1024, 1024],
  "meaning": "tile: whole _D texture; patch: curated coherent region"
},
"projection": {
  "axes": {"-X": ["+y", "+z"], "+X": ["+y", "+z"],
           "-Y": ["+x", "+z"], "+Y": ["+x", "+z"],
           "-Z": ["+x", "+y"], "+Z": ["+x", "+y"]},
  "world_center": [0.0, 0.0, 0.5],
  "uv_center": [0.5, 0.5],
  "note": "uv = uv_center + (world_uv - world_center) * px_per_m / texture_size"
},
"px_per_m": 340.0,
"coverage_u_m": 3.0,
"coverage_v_m": 3.0,
"canvas_meters": 3.01,
"wrap_evidence": "Texture2D.m_WrapMode=0 (Repeat) in resources.assets; ignored in game because AutoAtlaser remaps uv into the material cell (see ART07 禮1)"
```

* `tile` ??`wrap: "repeat"`, `tile_meters` in the REQ class range, `patch: null`
  is **not** allowed (the legacy `patch` probe record is kept, see below).
* `patch` ??`wrap: "clamp"`, `tile_meters: null`, `uv_canvas.pixel_rect` = the
  curated large patch.
* Both keep the existing `patch` record (probe window + measured mean/std +
  `std_max`) so C12/C13 keep working unchanged: for `patch` materials it is the
  canvas itself; for `tile` materials it is the existing small uniform probe
  window (texture identity check) while `uv_canvas` is the full texture.
* New top-level block:

```json
"uv_mapping": {
  "schema": "hdbf.world_uv/v1",
  "generator": "art/material_uv_policy.py",
  "texel_density_target_px_per_m": [300, 600],
  "atlas_cell_safe": true,
  "no_uv_outside_canvas": true,
  "densities": {"<material>": {"px_per_m": 340.0, "canvas_meters": 3.01}}
}
```

`material_slots.json` (written by `gen_models_v2.py`) gains per model a
`uv_mapping` block: `{"mode": "world_tiled", "materials": {<material>:
{"px_per_m": ?? "canvas": ?并}}` and the top-level `uv_space` text is updated
(no more `patch_texels_per_grid` as the shipped density; keep
`legacy_patch_texels_per_grid` for reference).

## 5. Validators (`validate_models.py`, `validate_timbermesh.py`)

Keep **all** existing checks C1?14 and both negative-control families. Add /
upgrade (details are the validator task):

* **C7 (flat-patch UV0 bounds) upgrade**: mode-aware.
  * tile material: `0 + g <= u,v <= 1 - g` with `g = 2/1024` guard, all finite
    (no NaN/Inf; `math.isfinite`));
  * patch material: every UV corner inside the entry's `uv_canvas.uv_rect`
    (簣 `PATCH_UV_TOL`), all finite.
* **C11 (material patch UV containment) upgrade**: containment against
  `uv_canvas` (tile full texturing too) + construction models keep using
  `construction_shared_patches` (now the canvas rects) for both factions.
* **C15 (new) ??world-UV texel density**: for every material of every shipped
  LOD0/LOD1/LOD2 model:
  * `uv_density = |UV edge| / |projected world edge|` per triangle,
    aggregated per material as `sum(uv_area) / sum(world_area)` (texel weighting
    by `texture_size`), and checked against the map `px_per_m` (tolerance 簣2 %
    aggregate, 簣25 % per triangle for triangles with world area ??0.05 m簡);
  * tile materials must additionally be inside the REQ target band
    **300??00 px/m** (this is the point of the round: 1024/3.0 = 341 ??300);
  * patch materials must be ??4? the ART-06 density (32 px/m) ??regression guard;
  * no NaN/Inf UV or world coordinate.
  * exact recomputation (optional but recommended): expected
    `uv = center + (world-center)*px_per_m/size`, compared per corner with
    tolerance 1e-3; triangles whose **rounded** dominant |normal| components
    have the top two within 1e-3 are "ambiguous plane" (45簞 bevel/chamfer faces)
    and are skipped by the exact check (count them: must be < 15 % of triangles
    and < 2 % of world area; measured on the shipped meshes: 8.1 % of triangles
    / 0.08 % of area).  The ambiguous faces are still covered by C11
    containment and by the C15 area-aggregate density.
  * the frozen projection tie-break is part of the contract: unit Newell
    normal, `|component|` rounded to 4 decimals, dominant axis with X > Y > Z
    priority (禮2).
* **C16 (new) ??UV mapping contract / determinism**: the map must carry 禮4
  fields for every used material, `px_per_m` must reproduce from
  `usable/canvas ??coverage`, `wrap` must be `repeat` for tile and `clamp` for
  patch, `tile_meters` inside the REQ class band for tile materials, and
  `uv_canvas`/`patch` rects must be inside the texture. Cross-model: the same
  material must use the **same** `px_per_m`/canvas in every model (no per-LOD or
  per-tier drift).
* **Negative controls (must FAIL, machine-checked like the existing 3)**:
  1. scale one tile material's UVs by 0.5 ??C15 FAIL;
  2. scale one tile material's UVs by 2.0 ??C15 FAIL;
  3. stretch one large patch-mode face's UVs to the full 0..1 square ??C7/C11
     FAIL (or C15 density FAIL);
  4. (keep) uv escape ??C11; hazard patch ??C13; missing material ??C12.
  Controls are reported in the `validation-report.txt` summary line
  (`controls: 6/6 rejected`).
* `validate_timbermesh.py`: same C15/C16 density + containment checks on the
  shipped `.timbermesh` data (it already parses positions+uv0), plus the two
  scale negative controls on the parsed data; keep all existing C1?14.
* Both reports keep the existing structure and add:
  * a per-material density table (material, mode, canvas, px_per_m expected,
    measured min/mean/max, verdict);
  * the negative-control lines with `PASS (rejected)`.

## 6. Preview / icon renderers

* `art/preview_materials.py`: image texture node `extension = "REPEAT"` (the
  in-game sampler is Repeat; previews must not clamp at the canvas edge) for the
  real-texture nodes; keep `interpolation = "Linear"`.
* `art/render_previews.py` adds close-up groups that show the new quality:
  * per tier + faction: `iso_50` / `iso_100` / `front_50` / `front_100`
    (100 % = the face filling the frame, ~2.0 m across; 50 % = ~4.0 m across);
    written under `art/previews/closeups/` (existing previews unchanged);
  * a 4-tier one-frame lineup `art/previews/tiers_lineup_iso_100.png` (no text).
* `art/render_icons.py`: keep the 4 ? 128 px icons, same names; icons must be
  re-rendered from the new models (albedo/lighting unchanged except the real
  texture density).

## 7. Files / ownership (do not write outside your scope)

| owner | files |
|---|---|
| Lead | `art/material_uv_policy.py` (new), `art/material_texture_analysis.py`, `art/material_patches.py`, `art/gen_models_v2.py`, `art/models/**`, `art/timbermesh/**`, `data/Buildings/Food/BoxFarm/**`, `data/ART_PLACEHOLDER.md`, `art/validation-report.txt`, `art/timbermesh-validation.txt` |
| validator-dev | `art/validate_models.py`, `art/validate_timbermesh.py` |
| test-dev | `tests/HDBF.Tests/V2MaterialTilingContractTests.cs` (new) and, only if strictly needed, `tests/HDBF.Tests/V2MaterialPatchContractTests.cs` |
| preview-dev | `art/preview_materials.py`, `art/render_previews.py`, `art/render_icons.py`, `art/previews/**`, `art/icons/**`, `data/Icons/**` |

## 8. Verification commands (run from the workspace root, sandbox work roots)

```powershell
$blender = 'C:\Users\user\.hdbf\sdk\blender-4.2.1-windows-x64\blender.exe'
$env:BLENDER_USER_CONFIG = "$env:TEMP\blender-config"
& $blender -b --factory-startup -noaudio --python art\material_texture_analysis.py
& $blender -b --factory-startup -noaudio --python art\gen_models_v2.py
& $blender -b --factory-startup -noaudio --python art\validate_models.py
& $blender -b --factory-startup -noaudio --python art\export_timbermesh.py
& $blender -b --factory-startup -noaudio --python art\validate_timbermesh.py
& $blender -b --factory-startup -noaudio --python art\render_previews.py
& $blender -b --factory-startup -noaudio --python art\render_icons.py
```

