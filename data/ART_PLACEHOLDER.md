# Art contract — custom `.timbermesh` models (ART-04: empty flat roof platform + flat-patch material)

> **The file name is kept on purpose.** `data/ART_PLACEHOLDER.md` is referenced by
> `tools/validate`, `docs/REQ_TRACEABILITY.md`, `docs/ARCHITECTURE.md`, `docs/OPS.md` and older
> workstream reports; renaming it would create dangling references. The name is historical — the
> content is no longer about placeholder art: the eight BoxFarm blueprints reference **custom
> `.timbermesh` models** (16 files), and this file records the frozen facts and evidence for that art.
> As of **ART-04** (2026-10-25) the models were redesigned: one **empty flat roof platform**, no
> crop/soil visuals, and a **flat UV patch** per material for a consistent own-material look.

All measured ART-04 values below were generated offline by the Lead pipeline on 2026-10-25 and are
copied verbatim from `.tmp-art04/FACTS-ART04.md` / the machine-generated evidence it cites
(`art/timbermesh/export-manifest.json`, `art/timbermesh-validation.txt`, `art/material_slots.json`,
`art/icons/icons-manifest.json`), plus csharp-gate's final `tools/validate` / L1 gate result (§10).
The only unresolved item in this document is **`PENDING(deploy-evidence)`** (deployment, out of scope
for this card and **not done**).

## 0. Machine-readable contract (`ART_CONTRACT`)

The following exact lines are part of the data contract: `tools/validate` + the L1 tests assert them
(the `data/ART_PLACEHOLDER.md` contract is part of the build gate). Do not reformat them.

```text
ART_CONTRACT: roof=empty-flat-platform
ART_CONTRACT: slots=none
ART_CONTRACT: ports=#Port#Power,#Port#Pipe,#Port#Logistics
ART_CONTRACT: material=flat-patch
ART_CONTRACT: ground-crops-hidden=BUILD-05
```

## 1. Status: ART-04 redesign replaces the ART-01/02/03 art

v0.1.0 shipped **no custom models** (vanilla placeholder meshes). ART-03 wired in the ART-02 custom
models. **ART-04** follows the user instruction of 2026-10-25 (verbatim, kept in
[`docs/workstreams/art-04-flat-roof-own-material.md`](../docs/workstreams/art-04-flat-roof-own-material.md)):

> 「你还是自己造一个材質一样的吧,至於农作物的顯示就不用且沒必要了屋頂还是別放東西,並只有一個空平台吧。」

Interpreted as three model-contract changes:

1. **Own consistent material look:** keep the vanilla `MaterialCollection` material asset names
   (faction-scoped) but map **all faces of one material slot to one fixed UV patch** (flat patch)
   instead of per-face atlas packing. The measured patch size is **88–384 texels** at a
   **64 texels-per-grid** density (64 is the density, **not** the patch size; min `glass` 88×88,
   max wood/metal 384×384 — full table in §3). No Unity/AssetBundle is needed for this.
2. **Roof = exactly one empty flat platform:** nothing on top; no crop troughs, no soil, no rim
   frames, and **no `#Slot#Crop_0..5` empty nodes**. The 8 construction-stage greyboxes also use one
   empty flat platform. Only the 3 port markers stay.
3. **Crop visuals unnecessary:** the model carries no crop/soil visuals; BUILD-05's ground-crop
   hiding is unchanged.

`data/Buildings/Food/BoxFarm/` still contains **16 `.timbermesh` files** at the same paths:

* **8 finished models (LOD0)**: `BoxFarm.<Tier>.<Faction>.Model.timbermesh`
* **8 construction-stage models**: `BoxFarm.<Tier>.<Faction>.ConstructionStage0.Model.timbermesh`

with tiers `Basic`, `Intermediate`, `Advanced`, `Top` and factions `Folktails`, `IronTeeth`.

The `data/Buildings/Food/BoxFarm/` copies are the ART-04 exports: `Get-FileHash` verified **16/16
SHA-256 match** between `art/timbermesh/*.timbermesh` and `data/Buildings/Food/BoxFarm/*.timbermesh`
(2026-10-25). The old ART-02/03 files are no longer on disk.

Bytes / triangle / vertex counts are measured from the ART-04 pipeline (`art/gen_models.py` + export;
source `art/timbermesh/export-manifest.json`): **1,266,760 bytes / 50,344 tris / 121,366 vertices**
in total — finished 8 files **1,193,582 B / 47,560 tris**, construction 8 files **73,178 B / 2,784
tris**. The old ART-02/03 values (1,172,971 B / 50,828 tris) are **outdated** and must not be quoted
for the shipped files. The old 6 trough meshes, soil, rim frames, vents/awnings/gables/gantry roof
structures and the 6 `#Slot#` nodes are **removed by ART-04**; each new file has **4 nodes**
(1 root + 3 ports) instead of 10, and `slots` is empty in every file.

### Model inventory (ART-04 measured values; asset paths are frozen)

| Tier | Faction | Kind | `TimbermeshSpec.Model` asset path (no extension) | File under `data/Buildings/Food/BoxFarm/` | Bytes | Tris | Vertices |
| --- | --- | --- | --- | --- | ---: | ---: | ---: |
| Basic | Folktails | finished LOD0 | `Buildings/Food/BoxFarm/BoxFarm.Basic.Folktails.Model` | `BoxFarm.Basic.Folktails.Model.timbermesh` | 148999 | 5944 | 14380 |
| Basic | IronTeeth | finished LOD0 | `Buildings/Food/BoxFarm/BoxFarm.Basic.IronTeeth.Model` | `BoxFarm.Basic.IronTeeth.Model.timbermesh` | 148660 | 5944 | 14372 |
| Intermediate | Folktails | finished LOD0 | `Buildings/Food/BoxFarm/BoxFarm.Intermediate.Folktails.Model` | `BoxFarm.Intermediate.Folktails.Model.timbermesh` | 147832 | 5944 | 14326 |
| Intermediate | IronTeeth | finished LOD0 | `Buildings/Food/BoxFarm/BoxFarm.Intermediate.IronTeeth.Model` | `BoxFarm.Intermediate.IronTeeth.Model.timbermesh` | 146779 | 5944 | 14254 |
| Advanced | Folktails | finished LOD0 | `Buildings/Food/BoxFarm/BoxFarm.Advanced.Folktails.Model` | `BoxFarm.Advanced.Folktails.Model.timbermesh` | 150326 | 5944 | 14355 |
| Advanced | IronTeeth | finished LOD0 | `Buildings/Food/BoxFarm/BoxFarm.Advanced.IronTeeth.Model` | `BoxFarm.Advanced.IronTeeth.Model.timbermesh` | 149746 | 5944 | 14333 |
| Top | Folktails | finished LOD0 | `Buildings/Food/BoxFarm/BoxFarm.Top.Folktails.Model` | `BoxFarm.Top.Folktails.Model.timbermesh` | 150934 | 5948 | 14342 |
| Top | IronTeeth | finished LOD0 | `Buildings/Food/BoxFarm/BoxFarm.Top.IronTeeth.Model` | `BoxFarm.Top.IronTeeth.Model.timbermesh` | 150306 | 5948 | 14332 |
| Basic | Folktails | construction stage | `Buildings/Food/BoxFarm/BoxFarm.Basic.Folktails.ConstructionStage0.Model` | `BoxFarm.Basic.Folktails.ConstructionStage0.Model.timbermesh` | 9142 | 348 | 834 |
| Basic | IronTeeth | construction stage | `Buildings/Food/BoxFarm/BoxFarm.Basic.IronTeeth.ConstructionStage0.Model` | `BoxFarm.Basic.IronTeeth.ConstructionStage0.Model.timbermesh` | 9146 | 348 | 834 |
| Intermediate | Folktails | construction stage | `Buildings/Food/BoxFarm/BoxFarm.Intermediate.Folktails.ConstructionStage0.Model` | `BoxFarm.Intermediate.Folktails.ConstructionStage0.Model.timbermesh` | 9151 | 348 | 834 |
| Intermediate | IronTeeth | construction stage | `Buildings/Food/BoxFarm/BoxFarm.Intermediate.IronTeeth.ConstructionStage0.Model` | `BoxFarm.Intermediate.IronTeeth.ConstructionStage0.Model.timbermesh` | 9156 | 348 | 834 |
| Advanced | Folktails | construction stage | `Buildings/Food/BoxFarm/BoxFarm.Advanced.Folktails.ConstructionStage0.Model` | `BoxFarm.Advanced.Folktails.ConstructionStage0.Model.timbermesh` | 9147 | 348 | 834 |
| Advanced | IronTeeth | construction stage | `Buildings/Food/BoxFarm/BoxFarm.Advanced.IronTeeth.ConstructionStage0.Model` | `BoxFarm.Advanced.IronTeeth.ConstructionStage0.Model.timbermesh` | 9151 | 348 | 834 |
| Top | Folktails | construction stage | `Buildings/Food/BoxFarm/BoxFarm.Top.Folktails.ConstructionStage0.Model` | `BoxFarm.Top.Folktails.ConstructionStage0.Model.timbermesh` | 9141 | 348 | 834 |
| Top | IronTeeth | construction stage | `Buildings/Food/BoxFarm/BoxFarm.Top.IronTeeth.ConstructionStage0.Model` | `BoxFarm.Top.IronTeeth.ConstructionStage0.Model.timbermesh` | 9144 | 348 | 834 |

Totals: **1,266,760 bytes / 50,344 tris / 121,366 vertices** (finished 8: 1,193,582 B / 47,560 tris;
construction 8: 73,178 B / 2,784 tris). Per-file SHA-256 below; all 16 are byte-identical between
`art/timbermesh/` and `data/Buildings/Food/BoxFarm/` (`Get-FileHash`, 2026-10-25):

```text
BoxFarm.Basic.Folktails.Model                              f3213b70d452e02cbc35d9c35541c4d87bcb63bc4503af3d3585f3b66b4857a0
BoxFarm.Basic.IronTeeth.Model                              57e66a0aa93d1afab0fa0be464454cbb8a04b9400774fb2732275e843f116829
BoxFarm.Intermediate.Folktails.Model                       5b78cd896b554ad1277e23d4e478b3830813243e336f6cfd7490f1f913f3f9d7
BoxFarm.Intermediate.IronTeeth.Model                       7cb96ad49fa67538bd4109a7f23def3a45ab4c56c24cd43a5e38b41ce9caae86
BoxFarm.Advanced.Folktails.Model                           59982415ad3e67bc55b11103c380e1119024ca4049fcafe1e48d7e3c3a01886b
BoxFarm.Advanced.IronTeeth.Model                           e515e80a386e4fb761003ef2e2edd8f82bd8a4d0fd9a600fbce3f09da2579461
BoxFarm.Top.Folktails.Model                                cf202759c0291007b4616694399613102952b0a29ac4c21428d219a0a7716b9a
BoxFarm.Top.IronTeeth.Model                                9b1282e815f41e753aee9e50a1b69b51882d12c7deb41be8f8109b2e40372036
BoxFarm.Basic.Folktails.ConstructionStage0.Model           3ab11bc49f67ca95e314fc6f52180ea913e9f6080bb4a5e4c93e6f16c39b6697
BoxFarm.Basic.IronTeeth.ConstructionStage0.Model           399729d296b5bf9fad1bea5bf616e0c490ec033ae29f662a55ab72dfc3fdc3a9
BoxFarm.Intermediate.Folktails.ConstructionStage0.Model    053c9e8fed9e6e3f4e969d5cbddc1d975b7ab2d48a73071cb925fde5577411cc
BoxFarm.Intermediate.IronTeeth.ConstructionStage0.Model    15129d0cbd5eb7d158dad65cdb5dc12a58696202d848b552ab8a2ae8ccd56c42
BoxFarm.Advanced.Folktails.ConstructionStage0.Model        7b79d1787cf8ba209c4538fe7f951ebbdcab231be8bc9ab078564aedd9bc2513
BoxFarm.Advanced.IronTeeth.ConstructionStage0.Model        a79bcb89994633670839a021f96a3f27327bf7f7dacf4be280fb25bb111dbe8e
BoxFarm.Top.Folktails.ConstructionStage0.Model             ce3e1469dd8da02b765b92b50c0a0b0fca01a33d4063d3df6db320d75dac86ef
BoxFarm.Top.IronTeeth.ConstructionStage0.Model             8f28229c413082ad8febe5307d5a498771faf33ee8bc1f675a0a8240fd8f4753
```

The construction stage is **one shared greybox geometry**; ART-02 exported it once per tier × faction
(8 files) so each blueprint can point at its own path. All 8 greybox files have identical
geometry/material names (**348 tris / 834 vertices / 2 materials each**, empty flat platform plan
**2.86 × 1.86**); only the file path and the per-file `Model.Name` differ. ART-04 keeps the same
arrangement but the greybox now shows one **empty flat platform** (no trough frames).

### Evidence (ART-04; offline only — measured)

| Claim | Evidence |
|---|---|
| 16/16 files present, parse, and match the ART-04 geometry/material/marker contract | [`art/timbermesh-validation.txt`](../art/timbermesh-validation.txt): all PASS — `FILES: 16/16 present; 16 PASS, 0 FAIL`, `CHECKS: 128 PASS, 0 FAIL, 0 NOT RUN (of 128 = 16 files x 8 checks)`, `OVERALL VERDICT: PASS`, exit code 0. Latest report: generated 2026-10-25T21:39:28+0800, pure Python / Blender-free, CPython 3.14.7 on Windows, elapsed 0.71 s (the report's title string still says ART-02; content is the ART-04 run) |
| Per-file bytes / triangles / vertices / material names / SHA-256 | [`art/timbermesh/export-manifest.json`](../art/timbermesh/export-manifest.json): `model_count: 16`, `failures: []`; per-file values in the inventory + SHA-256 block above |
| Bounds: **3.000 × 1.000 × 2.000**, min = (0, 0, 0), **block-corner origin, NOT centred**; front at Z = 0 | validation check C3: `size=(3.0000, 1.0000, 2.0000) min=(-0.0000, -0.0000, -0.0000)`, `size_dev=0.0000 min_dev=0.0000` on 16/16 files (tolerance ±0.05). The game model space is block-corner, so the export is `contract + (1.5, 0, 1.0)` — see ART-02 §3 |
| Empty nodes and empty roof platform: **0 × `#Slot#`**, **3 × `#Port#Power`/`#Port#Pipe`/`#Port#Logistics`** present exactly once, deviation **0.0000** (tolerance 0.02), vertexCount = 0/no mesh; platform face present at top ±0.01; **nothing above platform top + 0.06** inside the platform plan | validation check **C6** on 16/16 files: `ports 3/3 present exactly once`, `max position dev=0.0000`, `no #Slot# nodes`, `empty roof platform: face=<n> above=0`. Marker names are also enforced by `tools/validate` (§10) |
| The 16 files actually shipped in `data/` are byte-identical to the validated ART-04 outputs | `Get-FileHash` comparison: **16/16 SHA-256 match** between `art/timbermesh/*.timbermesh` and `data/Buildings/Food/BoxFarm/*.timbermesh` (2026-10-25; see the SHA-256 block above) |
| Re-runnable / deterministic | two consecutive full `art/export_timbermesh.py` runs on the same `art/models/` produced **16/16 identical SHA-256** (compared programmatically from `art/timbermesh/export-manifest.json`; run-1 = run-2) — [`art/timbermesh/export-determinism.txt`](../art/timbermesh/export-determinism.txt): `IDENTICAL: 16/16`, `RESULT: PASS - deterministic` |
| C9 50%-silhouette distinguishability (ART-01 metric, offline re-run) | `python .tmp-art04\c9_probe.py` imports the **unchanged** `art/validate_models.py` functions/thresholds (`OUTLINE_MEAN_MIN=0.04`, `OUTLINE_MAX_MIN=0.12`, `SIL_IOU_MAX=0.85`, 50% downsample) and runs them on `art/models/<Tier>.<Faction>/LOD{0,2}.glb`: **LOD2 (strict) 12/12 tier pairs PASS; LOD0 12/12 tier pairs PASS**, both factions, deciding views front+side — [`art/c9-art04-silhouette-probe.txt`](../art/c9-art04-silhouette-probe.txt). **Offline geometry evidence at 50% camera scale, NOT in-game**; the full ART-01 validator was NOT re-run (its C4 trough checks are stale for ART-04), so this is **not** "`validate_models.py` passes" — only the C9 metric re-run via probe. In-game 50% readability remains **待實機確認**. |
| Per-file node list / slots / LOD budget | exporter manifest: nodes always exactly 4 (`[root, #Port#Power, #Port#Pipe, #Port#Logistics]`), `slots` empty in every file, LOD0 budget 6,000 (max 5,948) |
| Vertex colours | exporter reports a non-white `color` attribute on **16/16** files (BYTE_COLOR survived the FBX round-trip); whether the game shader consumes it is **unknown / 待實機確認** — do not claim it works |

Re-run the offline check from the workspace root:

```powershell
python art\validate_timbermesh.py
# expected: 16/16 present, all checks PASS, OVERALL VERDICT: PASS (exit 0)
# exact FILES/CHECKS lines:
#   FILES:   16/16 present; 16 PASS, 0 FAIL
#   CHECKS:  128 PASS, 0 FAIL, 0 NOT RUN (of 128 = 16 files x 8 checks)
```

## 2. Path convention for `TimbermeshSpec.Model`

`TimbermeshSpec.Model` is the asset path **without extension**, **folder-relative to the mod root**
(the deployed mod's `Data/` folder — here the workspace `data/` folder):

```text
Buildings/Food/BoxFarm/BoxFarm.Basic.Folktails.Model
  -> <mod>/Buildings/Food/BoxFarm/BoxFarm.Basic.Folktails.Model.timbermesh
  -> data/Buildings/Food/BoxFarm/BoxFarm.Basic.Folktails.Model.timbermesh   (this repo)
```

Confirmed by the official modding SDK sample and by `docs/API_FACTS.md` §3.1:

* SDK sample `ShantySpeaker.IronTeeth.blueprint.json` declares
  `"Model": "Buildings/Decorations/ShantySpeaker/ShantySpeaker.IronTeeth.Model"`, and the sibling
  file is `.../Data/Buildings/Decorations/ShantySpeaker/ShantySpeaker.IronTeeth.Model.timbermesh`.
* `docs/API_FACTS.md` (`TimbermeshSpec` row): `Model` = path to the `.timbermesh` asset **without
  extension**, folder-relative, e.g.
  `Buildings/Food/EfficientFarmHouse/EfficientFarmHouse.Folktails.Model`.

Timberborn recursively converts every `*.timbermesh` below the mod folder, so placing the file next
to the blueprints is enough; no manifest entry is needed.

## 3. Materials: vanilla MaterialCollection names sampled through one flat UV patch

Every material name in the 16 files is still a **vanilla `MaterialCollection` asset name for the
matching faction** (the game resolves the name against the current faction's material repository; no
custom material asset is shipped). The ART-04 change is the **UV mapping**, not the names:

* each material slot uses **one fixed UV patch** and **every face of that slot** samples that same
  patch — no per-face atlas packing. Measured patch sizes are **88–384 texels** at a **64
  texels-per-grid** density (64 is the density, **not** the patch size);
* `art/gen_models.py` maps each face into a sub-rect of that patch at the fixed 64 px/grid density
  with a deterministic offset; faces stay inside the same patch (small tiling inside one patch is
  intended) and never sample a different material's region;
* consequently a material reads as **one consistent swatch** on every face of the model instead of
  the ART-02/03 vanilla-atlas quilt; the detail comes from the geometry (board seams, frames, rivets,
  feet, door frame).

**Authoritative flat-patch table:** [`art/material_slots.json`](../art/material_slots.json) and
[`art/material_patches.py`](../art/material_patches.py) (patch id → pixel rect on the ART-01 2048²
atlas → UV rect → texels → vanilla material names). ART-04 has **10 patch ids**; values measured
offline (FACTS §4) and quoted here so the two cannot drift:

| patch id | atlas region | pixel rect x0,y0,x1,y1 | UV rect (u0,v0,u1,v1) | texels | vanilla materials using it |
|---|---|---|---:|---:|---|
| `wood_plain` | wood | 96,352,480,736 | 0.046875,0.640625,0.234375,0.828125 | 384×384 | `BaseWood_Brown.Folktails`, `BaseWood_DarkBrown.IronTeeth` |
| `wood_light` | wood | 96,1056,480,1440 | 0.046875,0.296875,0.234375,0.484375 | 384×384 | `BaseWood_LightBrown.Folktails`, `BaseWood_White.Folktails`, `BaseWood_Grey.IronTeeth` |
| `planks_light` | wood | 96,1568,480,1952 | 0.046875,0.046875,0.234375,0.234375 | 384×384 | `IrregularPlanks_LightBrown.Folktails`, `IrregularPlanks_DarkBrown.IronTeeth` |
| `planks_brown` | wood | 528,352,784,608 | 0.257812,0.703125,0.382812,0.828125 | 256×256 | `IrregularPlanks_Brown.Folktails` |
| `metal_base` | metal | 1056,384,1440,768 | 0.515625,0.625,0.703125,0.8125 | 384×384 | `BaseMetal.Folktails`, `BaseMetal.IronTeeth` |
| `metal_painted` | metal | 1056,1088,1440,1472 | 0.515625,0.28125,0.703125,0.46875 | 384×384 | `PaintedMetal.Folktails`, `PaintedMetal.IronTeeth` |
| `metal_details` | metal | 1056,1568,1312,1824 | 0.515625,0.109375,0.640625,0.234375 | 256×256 | `Details.Folktails`, `Details.IronTeeth`, `IrregularPlanks_MetalForged.IronTeeth` |
| `glass` | glass | 1744,704,1832,792 | 0.851562,0.613281,0.894531,0.65625 | 88×88 | `WindowsAtlas.Folktails`, `WindowsAtlas.IronTeeth` |
| `plaster` | canvas | 1872,288,2000,416 | 0.914062,0.796875,0.976562,0.859375 | 128×128 | `Plaster_White.Folktails`, `Plaster_Orange.IronTeeth` |
| `roof` | thatch | 1872,1472,2000,1600 | 0.914062,0.21875,0.976562,0.28125 | 128×128 | `ThatchedRoof.Folktails`, `RoofPlanks.IronTeeth` |

Material sets per tier/faction (measured: **unchanged from ART-02**; validator C5 PASS on 16/16 files):

| Tier | Faction | # | Materials (vanilla asset names) |
|---|---|---:|---|
| Basic | Folktails | 2 | `BaseWood_Brown.Folktails`, `ThatchedRoof.Folktails` |
| Basic | IronTeeth | 2 | `BaseWood_DarkBrown.IronTeeth`, `RoofPlanks.IronTeeth` |
| Intermediate | Folktails | 3 | `IrregularPlanks_LightBrown.Folktails`, `Plaster_White.Folktails`, `WindowsAtlas.Folktails` |
| Intermediate | IronTeeth | 3 | `IrregularPlanks_DarkBrown.IronTeeth`, `Plaster_Orange.IronTeeth`, `WindowsAtlas.IronTeeth` |
| Advanced | Folktails | 4 | `BaseWood_White.Folktails`, `PaintedMetal.Folktails`, `Details.Folktails`, `WindowsAtlas.Folktails` |
| Advanced | IronTeeth | 4 | `BaseWood_Grey.IronTeeth`, `PaintedMetal.IronTeeth`, `Details.IronTeeth`, `WindowsAtlas.IronTeeth` |
| Top | Folktails | 4 | `BaseMetal.Folktails`, `IrregularPlanks_Brown.Folktails`, `Details.Folktails`, `WindowsAtlas.Folktails` |
| Top | IronTeeth | 4 | `BaseMetal.IronTeeth`, `BaseWood_DarkBrown.IronTeeth`, `IrregularPlanks_MetalForged.IronTeeth`, `WindowsAtlas.IronTeeth` |
| construction (all 8) | per faction | 2 | Folktails: `BaseWood_Brown.Folktails`, `BaseMetal.Folktails` / IronTeeth: `BaseWood_DarkBrown.IronTeeth`, `BaseMetal.IronTeeth` |

**Honest consequence:** the flat patch samples a small region of the **vanilla atlas**, so the material
reads as a uniform vanilla colour/pattern rather than a designed PBR texture. Consistency (no quilt)
is what ART-04 buys; a truly custom PBR material still needs a Unity material AssetBundle, which is
**not shipped**. The exact in-game appearance of the flat patches (blur/stretch, seams, mipmap
behaviour) is **待實機確認**.

## 4. Roof: exactly one empty flat platform (geometry contract)

Frozen geometry contract for ART-04 (enforced by `art/validate_timbermesh.py`; **C1–C8 only**, and the
empty-roof-platform check is part of **C6** — that script has **no separate C9**):

* **Envelope fixed:** bounds **3.000 × 1.000 × 2.000**, min (0, 0, 0), block-corner origin, front at
  Z = 0; export = contract + (1.5, 0, 1.0). Tolerance ±0.05.
* **One flat empty platform on top:** platform walking surface **Y = 0.96** (16/16 files,
  export == contract); thin perimeter curb height **0.04**, width **0.04**, apex **Y = 1.0**;
  **nothing above platform top + 0.06** (i.e. Y = 1.02) inside the platform plan — C6 result
  `face=<n> above=0` on all 16 files (no troughs, soil, rim frames, vents, awnings, gables, gantry,
  belt covers). Platform plan per tier (`size_x × size_z`): **Basic 2.05 × 1.25**,
  **Intermediate 2.45 × 1.55**, **Advanced 2.78 × 1.82**, **Top 2.96 × 1.96**,
  **ConstructionStage0 2.86 × 1.86**.
* **No crop/soil visuals:** no trough meshes, no soil material usage, no crop geometry.
* **No `#Slot#` markers:** exactly **0** nodes named `#Slot#Crop_*`; the 6 old crop-slot empties are
  removed.
* **Ports kept:** exactly 3 empty nodes `#Port#Power`, `#Port#Pipe`, `#Port#Logistics` at the ART-02
  positions (see §5); validation C6 measured deviation 0.0000 (tolerance 0.02).
* **Construction stage:** the 8 `*.ConstructionStage0.Model` files use the same one empty flat
  platform (greybox, plan 2.86 × 1.86), no trough frames.
* Per-file node count: **4** (1 root + 3 ports). Triangle budget LOD0 ≤ 6,000 (unchanged; measured
  max 5,948).
* **Tier distinguishability (C9 metric):** the ART-01 50%-silhouette metric was **re-run offline**
  with the unchanged ART-01 functions/thresholds (`art/validate_models.py`; `OUTLINE_MEAN_MIN=0.04`,
  `OUTLINE_MAX_MIN=0.12`, `SIL_IOU_MAX=0.85`, 50% downsample) on the new ART-04 `art/models/.../LOD{0,2}.glb`:
  **LOD2 (strict) 12/12 tier pairs PASS; LOD0 12/12 tier pairs PASS**, both factions, deciding views
  front+side — [`art/c9-art04-silhouette-probe.txt`](../art/c9-art04-silhouette-probe.txt). This is
  **offline geometry at 50% camera scale, NOT an in-game check**; the full ART-01 validator was NOT
  re-run (its C4 trough checks are stale), so do not claim "`validate_models.py` passes"; in-game 50%
  readability remains **待實機確認**.
* Tier distinction now comes from materials/body proportions/detail density — **not** from roof
  silhouette (all tiers share the empty flat roof).

## 5. Empty nodes: only the 3 ports (no slots)

All **8 blueprints have no `TransformSlotInitializerSpec` and no `WorkplaceSlotManagerSpec` at the
blueprint root** (and none inside `Children.*`). The custom models ship **no `Seat`/`Entrance`
transform slots** — the only empty nodes are the 3 ports:

| Empty node | contract (centred pivot) | export/game (corner origin) | Outward | 13.6 note |
|---|---|---|---|---|
| `#Port#Power` | (1.00, 0.85, 1.00) | (2.50, 0.85, 2.00) | +Z | back-right, Ø0.12 junction box |
| `#Port#Pipe` | (−1.50, 0.40, 0.00) | (0.00, 0.40, 1.00) | −X | left short-edge centre, Ø0.16 flange |
| `#Port#Logistics` | (−1.00, 0.35, 1.00) | (0.50, 0.35, 2.00) | +Z | back-left, 0.8×0.4 conveyor opening |

Measured on all 16 ART-04 files (validation **C6**): the 3 ports are present **exactly once** as
empty nodes (vertexCount = 0, no mesh) at these export coordinates, with **max position deviation
0.0000** (tolerance ±0.02), and there is **no `#Slot#` node** in any file.

Reason the specs are omitted: `TransformSlotInitializer` resolves each declared `SlotKeyword` with
`SlotRetriever.GetSlots(prefab, keyword)` and **throws when a keyword has no matching transform**
(smoke-test round 4, `docs/VERIFICATION.md` §12: `InvalidOperationException: There are no
"Entrance" slots`). The fix for a model without slot transforms is to **omit the spec** — the same
thing vanilla no-slot workplaces do (e.g. the 10-worker `Mine`).

Consequence: workers still work (workplace/job logic is unaffected), but they have **no seated
animation slots** on these buildings. Whether that looks acceptable in game **需實機確認**.

Also unchanged and still root-only (per `docs/VERIFICATION.md` §12 and `docs/API_FACTS.md`):
`WorkplaceIlluminatorSpec` / `WorkplaceWorkerOutfitSpec` may stay on the root; slot/workplace specs
must never be duplicated into `Children.#Unfinished` (their copies would be instantiated on the
unfinished prefab and can throw at placement).

## 6. Crop visuals: hidden by BUILD-05 (no crop/soil in the model)

The model carries **no crop and no soil visuals** (ART-04 removed the trough/soil meshes). Crop
*display* on managed plots is handled by the BUILD-05 runtime hiding (`gameplay.crop-visibility`),
which is unchanged and already verified in game in round 29
(`hidden=76 registered=300 hideFailures=0`, see `docs/NEXT_ROUND.md`; that evidence is for the
BUILD-05 hiding system, not for the ART-04 mesh). Crop growth/harvest logic is unchanged.

## 7. LOD: only LOD0 ships; lower LODs are generated by the game

Only the LOD0 `.timbermesh` files ship. There is no `art/timbermesh/lod/` folder and no LOD1/LOD2
`.timbermesh` in `data/`. The game generates lower LODs and auto-atlases at prefab-build time via
`Timberborn.PrefabOptimization`: the class names `TimbermeshPrefabOptimizer` and `AutoAtlaser` (and
the `AutoAtlas` spec/type names) are present in the installed assembly.

Evidence (run on this machine, 2026-10-25):

```powershell
Select-String -Path "C:\Program Files (x86)\Steam\steamapps\common\Timberborn\Timberborn_Data\Managed\*.dll" -Pattern 'TimbermeshPrefabOptimizer' -List
# -> Timberborn.PrefabOptimization.dll
Select-String -Path "C:\Program Files (x86)\Steam\steamapps\common\Timberborn\Timberborn_Data\Managed\*.dll" -Pattern 'AutoAtlaser' -List
# -> Timberborn.PrefabOptimization.dll
```

**待實機確認** — code-level fact from the installed DLLs, **not** in-game verification: the actual
LOD switch, its thresholds, and whether the generated LODs look acceptable at distance have not been
observed in a running game. Do not claim "LOD works" until that check happens.

## 8. Unchanged contracts

* **Colliders:** still a single axis-aligned box per model child —
  `Center { X: 1.5, Y: 0.5, Z: 1.0 }`, `Size { X: 3.0, Y: 1.0, Z: 2.0 }` — covering the full 3×2
  footprint. The custom meshes carry **no colliders of their own** (ART-01 §11 deviation 7); the
  blueprint collider is the collision contract. Do **not** retarget it at the model's own colliders.
* **Placement/gameplay:** `BlockObjectSpec.Size` is `{ X: 3, Y: 2, Z: 1 }`. Grid coordinates are
  X = width, Y = depth and Z = height, so this is the 3 wide x 2 deep x 1 high footprint. `Blocks`
  keeps the vanilla 1-high mixed layout (`Blocks[0..2]` = y=0 facade depth row solid /
  `GroundOrStackable`, `Blocks[3..5]` = the y=1 row behind it, `Any` + `Floor`),
  `Entrance.Coordinates` is `{ X: 1, Y: -1, Z: 0 }` (one depth step in front of the facade, height
  layer 0), and `HdbfBoxFarmSpec` (costs, plots, workers, range, irrigation, fatigue, maintenance)
  is untouched. The flat-roof own-material card changed the model content only; it did not alter
  balance data.
* **Block axis follow-up (2026-10-25):** the eight blueprints previously carried
  `Size { X: 3, Y: 1, Z: 2 }`, which the game reads as 3 wide x 1 deep x 2 HIGH (3x1x2 blocks, not
  the 3x2 footprint). They are now `{ X: 3, Y: 2, Z: 1 }`, and the Top power transputs moved from
  the top layer to the rear face of the ground layer (`Coordinates (x, 1, 0)`, direction `Up` = +Y;
  `Left`/`Right` for the side faces). Evidence and the in-game 3x2 A/B test are in
  `docs/workstreams/bug-08-block-axis-size.md`.
* **Construction stage:** `Children.#Unfinished` keeps `ConstructionBase3x2#nested`
  (`ConstructionBases/ConstructionBase3x2/ConstructionBase3x2.blueprint`), and
  `Children.#Unfinished.Children.ConstructionStage0` renders the custom greybox mesh
  (`*.ConstructionStage0.Model`) — now with the empty flat platform. Visual result **待實機確認**.
* **Why eight blueprints instead of four (still true):** Timberborn materials are
  **faction-scoped** — `MaterialRepository.GetMaterial` only knows the running faction's materials
  (`BaseWood_Grey.Folktails` vs `.IronTeeth`). A blueprint referencing the other faction's model
  makes prefab creation throw `Material <name> not found in repository` and breaks the build menu
  (confirmed in game, `docs/VERIFICATION.md` §9). Therefore each tier ships one blueprint per
  faction, each pointing at its own faction's model:
  * `BoxFarm.<Tier>.Folktails.blueprint.json` → appended to `Buildings.Folktails`
  * `BoxFarm.<Tier>.IronTeeth.blueprint.json` → appended to `Buildings.IronTeeth`

  Both factions keep **identical** costs/science/plots/workers/range/irrigation/fatigue/maintenance
  values (`HdbfBoxFarmSpec`), satisfying REQ-TECH-06. Only the model content and materials differ.
* **Template names:** `TemplateSpec.TemplateName` is `BoxFarm.<Tier>.<Faction>` — unique across
  factions because the game's template registry is global; saves reference the faction-specific
  template.

## 9. Icons: ART-04 offline renders ship in `data/Icons/`

`data/Icons/BoxFarm<Basic|Intermediate|Advanced|Top>.png` are now the **ART-04 128×128 production
renders** (Blender Cycles CPU, offline render — **not in-game screenshots**), re-rendered from the
ART-04 models and copied from `art/icons/BoxFarm.<Tier>.128.png`. Verified 2026-10-25:
`data/Icons/*.png` are byte-identical to `art/icons/BoxFarm.<Tier>.128.png` (**4/4 SHA-256 match**);
`art/icons/icons-manifest.json` reports `verification: PASS` (8/8 files ok; it also records the
512×512 renders):

| Tier | `data/Icons` file (128×128) | bytes | SHA-256 |
|---|---|---|---:|
| Basic | `BoxFarmBasic.png` | 14092 | `c611c815492587a80de3bca9f9f41febb97ed48a10e2766385ca1ddd784ad12d` |
| Intermediate | `BoxFarmIntermediate.png` | 13200 | `1eb50aeeaaa3cc4c458387c755551c373a23a340a73df58e868be1e60d7c3244` |
| Advanced | `BoxFarmAdvanced.png` | 14676 | `6d4b8012a7b2cbeeb9dab783eb83dcd8ee14fee40cead4632cf160ade4521bc7` |
| Top | `BoxFarmTop.png` | 13102 | `ce70af287b9e4c44e408e8eac375625cc82072515efec16c3036230ace6e70a2` |

The file names / `LabeledEntitySpec.Icon` (`Icons/BoxFarm<Tier>`) are unchanged. The sibling
`<name>.png.meta.json` (`{"isSprite": true}`, required by the game's `ModSpriteConverter`) is
unchanged. Icons are faction-independent, so all eight blueprints share the four tier icons.
`art/previews/*` were re-rendered from the ART-04 models too (offline). **Build-menu presentation is
待實機確認** — the renders are offline evidence, not in-game screenshots.

## 10. What `tools/validate` enforces (data contract)

The data validator is the gate for this contract. `dotnet run --project tools\validate -c Release -m:1 -- data`
(also run inside `build.ps1`) enforces at least:

1. **Exact model path:** each of the 8 blueprints' `Children.#Finished.TimbermeshSpec.Model` and
   `Children.#Unfinished.Children.ConstructionStage0.TimbermeshSpec.Model` must be the expected
   custom path `Buildings/Food/BoxFarm/BoxFarm.<Tier>.<Faction>[.ConstructionStage0].Model` (no
   extension). A vanilla path or any other string fails.
2. **Asset exists and is a valid `.timbermesh`:** the file must exist under `data/` (case/suffix
   exactly as the path implies), be non-empty, and be a complete, decodable **zlib stream**
   (`zlib(protobuf)`): valid zlib header, deflate stream decompresses to a non-empty payload, and the
   trailing 4 bytes match the payload's **Adler-32**. A missing, empty, header-invalid,
   mid-stream-truncated (Adler-32 mismatch) or otherwise undecodable file fails.
3. **Container-level marker check (ART-04):** for every custom model the decompressed payload must
   contain each of the 3 port marker names (`#Port#Power`, `#Port#Pipe`, `#Port#Logistics`) and must
   **not** contain the `#Slot#` marker substring — i.e. the shipped models really are slot-free.
4. **No slot specs on custom-model blueprints:** a blueprint using a custom model must not declare
   `TransformSlotInitializerSpec` or `WorkplaceSlotManagerSpec` at the root (the art ships no
   `Seat`/`Entrance` transforms, see §5). Root-only placement rules for the remaining workplace
   specs are also checked.
5. **This document lists every custom model path and keeps the `ART_CONTRACT` lines:** the 8 finished
   and 8 construction-stage model asset paths above, plus the five machine-readable
   `ART_CONTRACT:` lines in §0, must all appear in this file (L1 tests assert the lines), so the
   documented contract cannot drift from the shipped blueprints and models.

Negative controls used to prove the gate actually fires (validated by the validator workstream):

* point a blueprint at the old vanilla model (or any non-expected path) → **exit 1**;
* remove / truncate / corrupt a shipped `.timbermesh` (empty file, invalid zlib header,
  mid-stream truncation caught by the Adler-32 trailer, or an undecodable deflate stream) →
  **exit 1**;
* add back `TransformSlotInitializerSpec` to a custom-model blueprint → **exit 1**;
* inject a `#Slot#` marker into a shipped payload → **exit 1**;
* remove one of the five `ART_CONTRACT:` lines or a model path line from this document → **exit 1**.

**Final ART-04 gate evidence — verified offline 2026-10-25 21:43 by csharp-gate (canonical `build.ps1`
invocation; `$w = Join-Path $env:TEMP 'hdbf-card-work'`):**

* `dotnet build tools\validate\validate.csproj -c Release -m:1 -p:HdbfWorkRoot=$w` → **0 warnings /
  0 errors, exit 0**; the 5 mod assemblies (HDBF.Core / Gameplay / Integration / Persistence / UI)
  also each build **0 warnings / 0 errors**.
* `dotnet $w\bin\validate\Release\net9.0\HdbfValidate.dll data --assemblies $w\bin` → **exit 0**:
  game assemblies **660**, spec types **506**, mod assemblies **5/5 OK**, blueprints **10**,
  `WARNINGS: none`, **`RESULT: OK - data/ is valid.`**
  **Note:** the bare `-- data` form is **not** the `build.ps1` invocation — `build.ps1` passes
  `--assemblies $workRoot\bin`; without it the legacy assembly layout is missing and the validator
  exits 1 on assembly discovery, which is **not** an ART-04 data failure.
* **L1:** `dotnet build tests\HDBF.Tests\HDBF.Tests.csproj -c Release -m:1 -p:HdbfWorkRoot=$w` →
  0 warnings / 0 errors; `dotnet $w\bin\HDBF.Tests\Release\net9.0\HDBF.Tests.dll` →
  **`PASSED 512  FAILED 0`**, exit 0 (README baseline 510 + the 2 ART-04 tests).
* **Negative controls** (on a `$env:TEMP` copy; real `data/` untouched; **every one exits 1**):
  inject `#Slot#Crop_0` into the decompressed payload; remove a model file; half-truncate the stream
  (Adler-32 trailer mismatch); remove the last 4 trailer bytes (Adler-32 mismatch); rename
  `#Port#Logistics` (missing required port marker). The zlib/Adler-32 checks are intact and the
  ART-04 marker gate fires.
* **data ↔ art:** 16/16 SHA-256 identical; independent Python decompression: 16/16 payloads,
  `#Slot#` total **0**, and each file has `#Port#Power`/`#Port#Pipe`/`#Port#Logistics` exactly once
  (**3/3**).

Deployment (`build.ps1 -Install` / REL-01 / deploy==stage SHA) is out of scope for this card and
remains **`PENDING(deploy-evidence)`** (**not done**).

## 11. Changing or replacing the art later

1. Export the new model with the official Blender plugin; keep the same file name and the same
   asset path (`BoxFarm.<Tier>.<Faction>[...].Model`), so no blueprint edit is needed.
2. Keep the frozen ART-04 geometry contract: bounds **3.000 × 1.000 × 2.000**, min (0, 0, 0) in
   block-corner game space (front at Z = 0), **one empty flat roof platform** (platform top Y = 0.96,
   curb height 0.04 × width 0.04 with apex Y = 1.0, nothing above platform top + 0.06), **no `#Slot#`
   nodes**, exactly the 3 `#Port#` markers at their documented positions, and faction-valid vanilla
   material names with the flat-patch UV mapping described in `art/material_slots.json`.
3. Do not re-add slot specs unless the model actually ships the declared `Seat`/`Entrance`
   transforms. Do not re-add crop troughs or soil unless the user asks for roof contents again.
4. Run `python art\validate_timbermesh.py` (offline geometry/format), then
   `dotnet run --project tools\validate -c Release -m:1 -- data` (data contract), then
   `build.ps1 -Install`; update this file + `art/material_slots.json` + `art/README-timbermesh.md`
   in the same change.

## 12. 仍未做 / 待實機確認 (not done / needs in-game confirmation)

Nothing below is claimed as verified — these are explicit gaps:

| Item | Status |
|---|---|
| In-game appearance of the four tiers (silhouette, colours, orientation, no clipping) | **待實機確認**; only offline geometry/format validation (and ART-04 offline work by the Lead) exists |
| Flat-patch material look (blur/stretch, pattern repetition, seams, mipmaps, lighting) | **待實機確認**; the UV mechanism is offline, the look is not observed |
| Custom PBR material / atlas / shader AssetBundle | **not done** (needs Unity); the flat-patch vanilla-atlas sampling is the accepted interim look |
| "Own material" in the strict sense (own texture asset) | **not done**; ART-04 delivers a consistent look (+ no quilt) via flat UV patches on vanilla material names |
| Construction-stage look (empty-platform greybox over `ConstructionBase3x2`) and the switch to the finished mesh | **待實機確認** |
| Tier distinguishability at 50% camera distance (chapter 2.3 / 5.4) | **離線 C9 probe PASS**: LOD2 (strict) 12/12 and LOD0 12/12 tier pairs PASS with the unchanged ART-01 metric (deciding views front+side; [`art/c9-art04-silhouette-probe.txt`](../art/c9-art04-silhouette-probe.txt)). Offline geometry evidence at 50% camera scale, **not in-game**; in-game readability **待實機確認** |
| C1–C8 validator + C9 50% silhouette | **C1–C8: `128 PASS, 0 FAIL, 0 NOT RUN` (16 files × 8 checks), `OVERALL VERDICT: PASS`, exit 0**; C6 holds the empty-roof-platform check and `validate_timbermesh.py` has no separate C9. **C9 指標保留**: re-run offline with the unchanged ART-01 functions/thresholds → **LOD2 (strict) 12/12, LOD0 12/12 tier pairs PASS** (deciding views front+side; [`art/c9-art04-silhouette-probe.txt`](../art/c9-art04-silhouette-probe.txt)). This is offline geometry at 50% camera scale, **not** in-game; `art/validate_models.py` itself is still the ART-01 contract (with the removed C4 trough checks) and was not updated, so this is **not** "`validate_models.py` passes" — in-game 50% readability remains **待實機確認** |
| LOD switch smoothness / LOD thresholds / generated LOD quality | **待實機確認**; only the class names in `Timberborn.PrefabOptimization.dll` are verified |
| LOD1 / LOD2 `.timbermesh` files (manual LOD) | **not shipped**; relies on game-side auto-LOD (see §7) |
| Workers' seated animation slots | **not available by design** (no slot transforms); work logic unaffected, visuals **需實機確認** |
| In-game build-menu icon presentation | **待實機確認**; the ART-04 128×128 offline renders are in `data/Icons/` (4/4 SHA-256 match to `art/icons/*.128.png`), but they are **not** in-game screenshots |
| Vertex-colour (`color`) usage by the material shader | **unknown / unverified**; the exporter reports a non-white `color` attribute on **16/16** files (BYTE_COLOR survived the FBX round-trip), but whether the shader consumes it was not checked — do not rely on vertex colours; **待實機確認** |
| Port empty-node local +Z outward rotation (ART-01 contract 13.6) | **not done** — the 3 port empties are identity rotation; the outward direction is only recorded in the posture tables/docs, no consumer exists yet |
| `tools/validate` / L1 final gate result | **PASS (offline)**: `tools/validate` exit 0 with `RESULT: OK - data/ is valid.` (game assemblies 660, spec types 506, mod assemblies 5/5, blueprints 10, warnings none); L1 **`PASSED 512  FAILED 0`**, exit 0. Negative controls A–E all exit 1. See §10 |
| `build.ps1` / deployment / in-game check | **not done** — out of scope for this card; deployment evidence **`PENDING(deploy-evidence)`** |
