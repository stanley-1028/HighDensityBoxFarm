# BUILD-02 — managed-plot highlight & box select (REQ-UI-05, chapter 14.3/14.6)

Owner: `lead` (Lead integration) with workstreams `core-rules` (Core rules + L1) and
`ui-loc` (panel + localization). Date: 2026-10-25.
Requirement source: `高密度盒子农场模组需求文档.md` chapter 14.3 (交互), 14.6 (UI 性能),
14.4/14.5 (localization/format), chapter 11.3 (plot binding, unreachable diagnostics),
REQ-UI-05, REQ-IMPL-10.

> **Honest status: implemented + offline-verified only.**
> Core/Gameplay/UI/Tests/validate compile with 0 errors, the L1 suite is green and the data
> validator passes, but nothing in this card has been run in a Timberborn build. Every
> game-facing behaviour (overlay visibility, colours, click/drag/Esc, panel auto-close, draw
> calls, 600-cell smoothness) is **待實機** and listed with concrete steps in §9. Do not read
> "implemented" below as "verified".

> Card constraints honoured: no deployment, no `build.ps1`, no game launch, no balance-number
> change, `docs/VERIFICATION.md` and `docs/REQ_TRACEABILITY.md` untouched, no `data/` change
> outside `data/Localizations/`, no new static mutable state, no per-frame whole-map scan,
> UI/render only queries through Core contracts.

---

## 1. What was researched and how

Ground truth was taken from the installed game, not from the requirement document's example
names. Tooling (existing, read-only reference data `%USERPROFILE%\.hdbf\api\` is not modified):

```powershell
$w = Join-Path $env:TEMP 'hdbf-card-work'
dotnet build tools\ApiIndex\ApiIndex.csproj -c Release -m:1 -p:HdbfWorkRoot=$w
$m = 'C:\Program Files (x86)\Steam\steamapps\common\Timberborn\Timberborn_Data\Managed'
dotnet "$w\bin\ApiIndex\Release\net9.0\ApiIndex.dll" dump $m "$w\il\highlight-api.txt" '^Timberborn\.SelectionSystem\.' '^Timberborn\.InputSystem\.' ...
dotnet "$w\bin\ApiIndex\Release\net9.0\ApiIndex.dll" il   $m "$w\il\highlight-il.txt" 'AreaHighlightingService' '.*'
dotnet "$w\bin\ApiIndex\Release\net9.0\ApiIndex.dll" il   $m "$w\il\meshdrawer-il.txt" '^Timberborn\.Rendering\.(MeshDrawer|MarkerDrawerFactory)$' '.*'
dotnet "$w\bin\ApiIndex\Release\net9.0\ApiIndex.dll" il   $m "$w\il\input-il.txt" '^Timberborn\.InputSystem\.InputService$' '.*'
dotnet "$w\bin\ApiIndex\Release\net9.0\ApiIndex.dll" il   $m "$w\il\cursor-tool-il.txt" '^Timberborn\.CursorToolSystem\.CursorTool$' '.*'
```

The curated excerpt used for this card is also saved as `docs/api/il-plot-highlight.txt`
(generated evidence, same style as the existing `docs/api/il-*.txt`).

Game version: Timberborn **1.1.2.8**, Unity 6000.5 (660 managed assemblies).

### 1.1 Candidate APIs found

| Candidate (verified type) | Visibility | What it does | Verdict |
|---|---|---|---|
| `Timberborn.SelectionSystem.AreaHighlightingService.DrawTile(Vector3Int, Color)` | public singleton | the vanilla Fields-tool tile highlighter | **not used directly**: one `Graphics.DrawMesh` per tile, so 600 cells = 600 draw calls (violates 14.6 ≤4) |
| `Timberborn.Rendering.MeshDrawer.DrawMultipleInstanced(List<Matrix4x4>)` | public class | batches quads with `Graphics.DrawMeshInstanced` | **not used directly**: drops the per-tile material-property block, so four colours cannot be expressed |
| `Timberborn.Rendering.MarkerDrawerFactory.CreateTileDrawer(Color)` | public class, public method | returns a coloured `MeshDrawer` built from the vanilla marker tile mesh/material | **not used directly**: the mesh/material it captured are private fields and its backing spec type is internal; no public accessor |
| `Timberborn.Rendering.AreaTileDrawerFactory.Create(Color, GameObject)` | public | vanilla area mesh drawer (creates GameObjects/MeshRenderers, full `UpdateArea` rebuild) | considered; rejected because it rebuilds the whole mesh per update and draw calls scale with map tiles touched per state |
| `Timberborn.TerrainSystemRendering.TerrainHighlightingService` | public | terrain/level highlight, not managed-plot tiles | not applicable |
| `Timberborn.Rendering.AreaTileDrawer` (used via factory) | public | grouped mesh per map tile | fallback only; not selected |

### 1.2 Selected path (and why)

**Reuse the vanilla marker tile assets, replace only the per-tile submit with the same instanced
draw API vanilla itself uses.**

* mesh: `Markers/TileMarker`, material: `Markers/TileMarkerSmall` — exactly the asset paths the
  vanilla `MarkerDrawerFactorySpec` / `AreaTileDrawerFactorySpec` blueprints use (reference data:
  `%USERPROFILE%\.hdbf\ref\Blueprints\Configurations\MarkerDrawerFactory.blueprint.json` and
  `AreaTileDrawerFactory.blueprint.json`), loaded through the public `Timberborn.AssetSystem.IAssetLoader`
  (the same loader `AssetRef` deserialization uses, see §2);
* world placement / vertical offset: `CoordinateSystem.GridToWorldCentered(cell) + (0, 0.02, 0)`,
  the same `YOffset = 0.02` the vanilla area drawer and `DrawTile` use;
* render layer: `Timberborn.Rendering.Layers.UILayer`, the same layer `MeshDrawer.DrawAtPosition`
  passes to `Graphics.DrawMesh` for the vanilla highlight;
* submit: `Graphics.DrawMeshInstanced(mesh, 0, material, matrices, count, colourMpb, …)` with one
  material-property block per state (`_BaseColor`, the property name vanilla `MeshDrawer` uses),
  i.e. **≤4 instanced draw calls** for the whole overlay.

This is not a re-implementation of the highlight visual: mesh, material, layer, offset and colour
property are all vanilla. The only mod-owned part is the batching (required by the 14.6 budget)
and the per-state colour table mandated by 14.3. There is deliberately **no** fallback to
per-tile draws: if the two assets cannot be loaded the overlay hides itself and logs one warn
(REQ-SAFE-01) rather than silently breaking the draw-call budget.

Renderer: `src/HDBF.Gameplay/Components/HdbfPlotHighlightRenderer.cs`
(`Draw()` at :206, `Submit()` at :236, `ResolveAssets()` at :287, asset paths at :51/:54,
1023-instance chunking at :62/:225).

---

## 2. IL evidence

### 2.1 The vanilla per-cell highlighter is public but one draw call per tile

`Timberborn.SelectionSystem.AreaHighlightingService` (public, type index
`docs/api/game-type-index.txt:22512`; `ILoadableSingleton`):

```text
=== Timberborn.SelectionSystem.AreaHighlightingService::DrawTile   [Timberborn.SelectionSystem]
  IL_0000: ldarg.0
  IL_0001: ldfld Timberborn.Rendering.MeshDrawer ...::_meshDrawer
  IL_0006: ldarg.1
  IL_0007: ldc.r4 0.02
  IL_000c: ldarg.2
  IL_000d: callvirt System.Void Timberborn.Rendering.MeshDrawer::DrawAtCoordinates(Vector3Int,Single,Color)
  IL_0012: ret
```

`MeshDrawer.DrawAtCoordinates` → `DrawAtPosition` → `Graphics.DrawMesh`:

```text
=== Timberborn.Rendering.MeshDrawer::DrawAtPosition   [Timberborn.Rendering]
  IL_0000: ldarg.0
  IL_0001: ldfld UnityEngine.Mesh ...::_mesh
  IL_0006: ldarg.1
  IL_0007: ldarg.2
  IL_0008: ldarg.0
  IL_0009: ldfld UnityEngine.Material ...::_material
  IL_000e: ldsfld System.Int32 Timberborn.Rendering.Layers::UILayer
  IL_0013: ldnull
  IL_0014: ldc.i4.0
  IL_0015: ldarg.3
  IL_0019: call UnityEngine.Graphics::DrawMesh(UnityEngine.Mesh,UnityEngine.Vector3,UnityEngine.Quaternion,UnityEngine.Material,System.Int32,UnityEngine.Camera,System.Int32,UnityEngine.MaterialPropertyBlock,...)
```

Conclusion: the public vanilla service is exactly the right *visual*, but per-tile immediate
submission. Using it for 600 cells spends 600 draw calls/frame, against chapter 14.6
("≤4 draw call", "≤8 UI draw call delta including the highlight layer").

### 2.2 Vanilla instancing exists but drops the per-tile colour

`Timberborn.Rendering.MeshDrawer.DrawMultipleInstanced` (public):

```text
=== Timberborn.Rendering.MeshDrawer::DrawMultipleInstanced   [Timberborn.Rendering]
  IL_0000: ldarg.0
  IL_0001: ldfld UnityEngine.Mesh ...::_mesh
  IL_0006: ldc.i4.0
  IL_0007: ldarg.0
  IL_0008: ldfld UnityEngine.Material ...::_material
  IL_000d: ldarg.1
  IL_000e: call System.Void UnityEngine.Graphics::DrawMeshInstanced(Mesh,Int32,Material,List`1<Matrix4x4>)
```

It batches, but the `MaterialPropertyBlock` that carries the colour in `DrawAtPosition`
(`SetColor(MeshDrawer.ColorProperty, color)`, IL above) is not passed to the instanced call, so
one call would paint every state the same colour.

### 2.3 Reusing vanilla assets without touching internal types

* `MarkerDrawerFactory` is public (`game-type-index.txt:22330`) and has
  `CreateTileDrawer(Color)` / `CreateTileDrawer()`; its backing `MarkerDrawerFactorySpec` is
  **internal** (`game-type-index.txt:22331`, attributes `BeforeFieldInit` without `Public`), and
  the mesh/material are private `MeshDrawer` fields (`_mesh`, `_material` per IL above).
* The vanilla asset paths are visible in shipped blueprints (read-only reference data):
  `MarkerDrawerFactory.blueprint.json` → `"TileMesh": "Markers/TileMarker"`,
  `"TileMaterial": "Markers/TileMarkerSmall"` (same pair in `AreaTileDrawerFactory.blueprint.json`).
* `AssetRef` loading passes that exact string to the public loader:

```text
=== Timberborn.BlueprintSystem.AssetRefDeserializer+GenericDeserializer`1<T>::LoadAsset   [Timberborn.BlueprintSystem]
  IL_0009: ldfld Timberborn.AssetSystem.IAssetLoader ...::_assetLoader
  IL_000f: callvirt !!0 Timberborn.AssetSystem.IAssetLoader::Load<T>(System.String)
```

`IAssetLoader.Load<T>(string)` is public (`game-type-index.txt:17784`), so the mod loads the same
two vanilla assets by their vanilla paths — no reflection, no internal type access.

### 2.4 What a "tile" is (mesh size, offset, colour property)

`AreaTileDrawerFactory.Create` (public) is the vanilla per-cell area renderer; its IL confirms the
same assets, the same `_BaseColor` colour mechanism and the same vertical offset:

```text
=== Timberborn.Rendering.AreaTileDrawerFactory::Create   [Timberborn.Rendering]
  IL_0027: ldfld ...::_areaTileDrawerFactorySpec
  IL_002d: callvirt AssetRef`1<Material> ...::get_TileMaterial()
  IL_0032: callvirt Material AssetRef`1<Material>::get_Asset()
  IL_0037: newobj System.Void UnityEngine.Material::.ctor(UnityEngine.Material)   // clone
  IL_003e: ldsfld System.Int32 ...Factory::ColorProperty
  IL_0043: ldarg.1
  IL_0044: callvirt System.Void UnityEngine.Material::SetColor(Int32,Color)       // _BaseColor
  ...
  IL_007b: callvirt AssetRef`1<Mesh> ...::get_TileMesh()
  IL_0080: callvirt ...::get_Asset()
=== Timberborn.Rendering.AreaTileDrawer::.cctor   [Timberborn.Rendering]
  IL_0000: ldc.r4 0.02
  IL_0005: stsfld System.Single Timberborn.Rendering.AreaTileDrawer::YOffset
```

`AreaTileDrawer.UpdateArea` calls `Clear()` then `UpdateMeshBuilders(...)` then `UpdateTiles()`
(full mesh rebuild), which is why it is not used as the primary path (the renderer keeps cached
matrices and repaints only changed cells, §5). `Layers` is a public static class
(`game-type-index.txt:22325`) whose `UILayer` is the layer vanilla markers use.

---

## 3. Four-state data contract (Core, game-free, L1-tested)

`src/HDBF.Core/Abstractions/PlotHighlights.cs`

* `HdbfPlotHighlightState { Irrigated=0, Dry=1, Excluded=2, Unreachable=3 }` (:18) — the four
  chapter-14.3 states, single definition for UI legend, renderer and tests.
* `readonly struct PlotHighlightCell { PlotId, X, Y, Z, State }` (:41) with
  `FromPlotId(plotId, state)` that decodes the plant cell through `PlotCellCodec`, so the rendered
  quad cannot drift from the persisted plot id.
* `HdbfPlotHighlightPalette` (:111): `IrrigatedRgba 0x4CAF50A6` (green), `DryRgba 0xFFC107A6`
  (yellow), `ExcludedRgba 0x9E9E9EA6` (gray), `UnreachableRgba 0xF44336A6` (red),
  `LegendOrder` (:119) = the requirement's order, `Rgba(state)` (:128). Alpha ≈65% is a first
  visual constant (初版可调) — the exact tint needs the in-game check (§9).

`src/HDBF.Core/Plots/PlotHighlightRules.cs`

* `PlotHighlightClassifier.TryClassify(isRegistered, isExcluded, isIrrigated, isUnreachable, out state)`
  (:35) — precedence **Excluded > Unreachable > Irrigated/Dry**; returns false when the cell is
  neither registered, excluded nor unreachable (nothing to paint).
* `PlotBoxSelection.RegisteredCellsInBox(cells, cornerX, cornerY, oppositeX, oppositeY, z)` (:93) —
  rectangle normalized in any drag direction, inclusive edges, same z-level only, only
  `Irrigated`/`Dry` cells (already-excluded/unreachable cells are not re-excluded), distinct ids
  sorted ascending for deterministic batches.
* `PlotExclusionPolicy.Plan(registered, requested)` (:203) / `readonly struct PlotExclusionPlan`
  (:141) — requested ids intersected with registrations and de-duplicated; **the whole request is
  refused when fewer than 1 registered plot would remain** (`RefusedByMinimum`), so box-select can
  never partially break REQ-UI-05's "注册格不得少于 1 格"; unknown/empty request is a no-op.
* `HighlightFrameBudget` (:263): `CellsPerFrame = 200`, `MaxFrames = 3`,
  `FramesNeeded` (:280) (600→3, 601→4, 0→0), `BatchSize` (:300) — the 14.6 numbers live in one
  place instead of being re-typed in the renderer.
* `PlotHighlightDelta` (:322) / `PlotHighlightDiff.Diff` (:367) — added/changed/removed diff used
  by the overlay refresh so unchanged cells do not rebuild their transform.

Snapshot read model additions (`src/HDBF.Core/Abstractions/BoxFarmSnapshot.cs`):
`IsPlotHighlightActive` (:120) mirrors the gameplay session for the panel toggle;
`PlotActionFailureKey` (:129) is the last **world** action rejection key (click/box select) so the
panel can display "注册格不足" without guessing. Command contract:
`IBoxFarmCommand.TrySetPlotHighlight(Guid, bool, out string?)` (`IBoxFarmCommand.cs:56`).

Semantics of the four states, honestly:

| state | meaning | source today |
|---|---|---|
| Irrigated (green) | registered + `ISoilMoistureService.SoilIsMoist` true at the moisture-sample cell | implemented |
| Dry (yellow) | registered + moisture false (also the fallback if a moisture probe throws for one cell) | implemented |
| Excluded (gray) | id present in the persisted `excludedPlotIds` set (REQ-IMPL-10) | implemented |
| Unreachable (red) | optional chapter-11.3 diagnostic: plantable but unreachable, never registered | **contract + renderer bucket + legend only; current scan does not retain the rejected candidate list, so no red cell is produced yet** |

Note: plots currently watered from the internal tank show their *soil* state (yellow until the
soil moisture service reports moisture); the aggregate panel coverage still counts tank-fed plots.
The overlay intentionally reads the same per-cell moisture query the coverage sampler uses, and a
per-cell probe failure degrades to yellow (conservative), never green.

---

## 4. Panel button, command chain and mutual exclusion

### 4.1 Chain

```
UI HdbfFarmPanelFragment.OnHighlightClicked()                    (HdbfFarmPanelFragment.cs:370)
  target = !snapshot.IsPlotHighlightActive                        (authoritative, never optimistic)
  IBoxFarmCommand.TrySetPlotHighlight(entityId, target, out key)  (Core contract)
    -> HdbfFarmCommandService.TrySetPlotHighlight                  (HdbfFarmCommandService.cs:165)
       -> HdbfPlotHighlightService.Activate(entityId) / Deactivate()  (HdbfPlotHighlightService.cs:131/156)
          -> HdbfPlotHighlightSession (gameplay singleton)         (HdbfPlotHighlightSession.cs:19)
             -> controller snapshot reads IsPlotHighlightActive + PlotActionFailureKey
                                                                (HdbfBoxFarmController.Snapshot.cs:72-73)
```

* Button: assets-free UIElements `Button` in the action row (`HdbfFarmPanelFragment.cs:193`),
  focusable (Tab/Enter) like the existing controls; active state is bold text (same pattern as the
  shutdown maintenance button). While active the fragment shows the legend header, four legend
  rows (colour **and** text, chapter 14.5 triple encoding) and the Shift-drag hint
  (`RenderHighlight`, :647). `HdbfFarmPanelData` carries `HighlightActive`,
  `HighlightButtonTooltip`, `HighlightHint`, `HighlightFailure`, `HighlightLegend`
  (`HdbfFarmPanelData.cs:99-137`; factory :208-216, legend :472).
* Close paths, all through the same command:
  * panel close: `ClearFragment()` closes before clearing the entity id (`HdbfFarmPanelFragment.cs:267`);
  * building switch: `ShowFragment()` closes the previous farm's overlay (`:227`);
  * gameplay-side safety: if the selected entity changes or is deselected without the fragment
    running, `HdbfPlotHighlightService.UpdateSingleton` detects it and deactivates
    (`SelectionMatchesActiveFarm`, :512) — event-driven read of the selected entity, no `Find`.
* Failure reasons use the existing no-placeholder Core keys (`PlotMinimum`,
  `PlotNotRegistered`, …), whitelisted by `HdbfFarmPanelDataFactory.FormatCommandFailure`, so a raw
  key can never reach the player (chapter 14.4/REQ-UI-08).

### 4.2 Why the panel click does not select another building (input mutual exclusion)

Verified input dispatch (`Timberborn.InputSystem.InputService.CallInputProcessors`):

```text
  ... priority processors loop (IPriorityInputProcessor, cannot consume) ...
  IL_0039: ldfld ...::_inputProcessorsCopy
  IL_003e: callvirt ...::get_Count()
  IL_0043: ldc.i4.1
  IL_0044: sub                       // iterate from the END of the registration list
  ...
  IL_004f: callvirt !0 IInputProcessor::get_Item(Int32)
  IL_0054: callvirt System.Boolean Timberborn.InputSystem.IInputProcessor::ProcessInput()
  IL_0059: brfalse.s ...
  IL_005b: leave.s                   // first processor returning true ends the chain
```

`InputService.AddInputProcessor(IInputProcessor)` appends to `_inputProcessors`; the default
`CursorTool.Enter()` adds itself (`IL_0007: callvirt InputService::AddInputProcessor`) and is what
selects/unselects buildings on a main-mouse click:

```text
=== Timberborn.CursorToolSystem.CursorTool::ProcessSelectObject   [Timberborn.CursorToolSystem]
  IL_0006: callvirt System.Boolean InputService::get_MainMouseButtonDown()
  IL_0013: callvirt System.Boolean InputService::get_MouseOverUI()
  IL_0022: callvirt System.Boolean SelectableObjectRaycaster::TryHitSelectableObjectIncludeTerrainStump(...)
  IL_0030: callvirt System.Void EntitySelectionService::Select(BaseComponent)   // building selection!
```

`HdbfPlotHighlightService` adds itself as an ordinary `IInputProcessor` when the overlay opens —
i.e. **after** the cursor tool, so it runs first in the reverse loop — and returns `true` for the
click/drag gesture. The cursor tool never sees the click, so the selected farm stays selected
(chapter 14.3 "高亮模式与建筑选择互斥：进入高亮后首次点击用于格子操作，不再切换建筑").
A one-second re-registration guard (`MaintainInputPrecedence`, :300) keeps that precedence if a
tool was entered later through the bottom bar; clicks over the panel return `false` early
(`MouseOverUI`), so the panel button and every other UI control stay usable.

`Esc` is handled by the same processor: `InputService.Cancel` closes the overlay and is consumed,
so the game's next `Esc` can close the panel (chapter 14.3 "Esc 层退出，再次 Esc 关闭面板").
This two-layer behaviour is **待實機** because the panel-close Esc is handled by game UI events,
not by this input chain.

### 4.3 Click / box select

* Cell under the mouse: `CameraService.ScreenPointToRayInGridSpace(InputService.MousePosition)`
  → `TerrainPicker.PickTerrainCoordinates(ray)` (`TraversedCoordinates.Coordinates`)
  → `HdbfBoxFarmController.ResolvePlotCell` (the BUG-04 plant-cell z rule used by the scan), so a
  click maps to exactly the stored plot id. No Physics collider, no per-frame map traversal.
  IL: `CameraService::ScreenPointToRayInGridSpace`, `TerrainPicker::PickTerrainCoordinates(Ray)`,
  `TraversedCoordinates::get_Coordinates` (public types, `game-type-index.txt:18801/23232/20413`).
* Single click: registered → exclude; excluded → restore; empty cell → silent no-op.
  Controller path `TryTogglePlotExclusion` (`HdbfBoxFarmController.Highlights.cs:109`) enforces the
  last-plot rule and routes persistence through the existing `TryExcludePlot`/`TryIncludePlot`
  (`REQ-IMPL-10`: `excludedPlotIds`, `HdbfBoxFarmController.Commands.cs:107`). The existing
  single-plot exclude path now also sets `_saveDirty` (it already persisted
  `ExcludedPlotIds` but did not mark the record dirty; the restore path already did).
* Shift+drag: Shift read from the Unity Input System (`Keyboard.current.leftShiftKey/rightShiftKey`),
  the input stack the game itself runs on (`KeyboardListener` IL uses `UnityEngine.InputSystem`).
  On release the two plant cells are fed to `PlotBoxSelection.RegisteredCellsInBox` and the batch
  goes through `TryExcludePlots` (`HdbfBoxFarmController.Highlights.cs:153`): one plan validation,
  one registry update, one coverage resample. Already-excluded cells inside the box are **ignored**
  (not restored) — chapter 14.3 only mandates bulk exclusion; bulk restore is click-only.
  There is deliberately no drag-rectangle preview (not required by 14.3; listed as a nice-to-have).
* Harvest semantics untouched: exclusion uses the existing controller paths; no harvest code was
  changed. "已种植格仅停止新播种，在途收获继续" is whatever the current farming logic already does
  (BUG-05 round); this card does not change it.

---

## 5. Performance design (chapter 14.6)

| Requirement | Implementation |
|---|---|
| 600 cells rebuilt over ≤200 cells/frame, ≤3 frames | `HighlightFrameBudget.CellsPerFrame/MaxFrames` (Core) drive `StepRefreshCycle` (`HdbfPlotHighlightService.cs:327`): the overlay walks the plot-id list 200 ids per frame, so the 600-plot top tier paints in 3 frames; the initial open is progressive, not a one-frame spike |
| only dirty cells rebuilt | the refresh classifies ids in slices and calls `HdbfPlotHighlightRenderer.Offer`; a cell's transform is recomputed **only** when its state changes (`Offer`, renderer :124), and the per-state draw lists are rebuilt only when a cell changed (`_drawListsDirty`); a frame with no change does 4 draws and zero allocations |
| ≤4 draw calls | one `Graphics.DrawMeshInstanced` per non-empty state, max 4 (chunked at 1023 instances if a future capacity exceeds it); `Draw()` returns the exact count, renderer :206 |
| ≤4 ms rebuild for 600 cells | work is 600 moisture dictionary lookups + 600 matrix updates spread over 3 frames; all per-cell probes are the same cheap lookup the daily coverage sampler already uses |
| no per-frame whole-map scan | the overlay only iterates the farm's own registered/excluded id list (≤600), never the map |
| no `Find`/`GetComponent` in Update | all services are constructor-injected singletons; the selected entity is read from `EntitySelectionService.SelectedObject`; the highlight service only reads cached controller data |
| panel close stops everything | `ClearFragment`/selection change deactivates and unregisters the input processor; no draw calls happen while closed |
| no new static mutable state | session state lives in the `HdbfPlotHighlightSession` singleton; renderer buffers are instance fields; only `static readonly` constants (`Shader.PropertyToID`, palette values) |

Memory: per displayed cell one dictionary entry (`PlotHighlightCell` + one `Matrix4x4`), ≤600
cells ⇒ well under the 4 MB UI cache budget; no GameObjects/Materials/Meshes are created or leaked
by the overlay (only `Graphics.DrawMeshInstanced` submits).

---

## 6. Files changed

Core (Lead + `core-rules`):

| File | Lines | Change |
|---|---|---|
| `src/HDBF.Core/Abstractions/PlotHighlights.cs` | 1-140 (new) | four-state enum/DTO/palette/legend order |
| `src/HDBF.Core/Abstractions/BoxFarmSnapshot.cs` | 114-130 | `IsPlotHighlightActive`, `PlotActionFailureKey` read-model fields |
| `src/HDBF.Core/Abstractions/IBoxFarmCommand.cs` | 49-57 | `TrySetPlotHighlight` contract |
| `src/HDBF.Core/Plots/PlotHighlightRules.cs` | 1-412 (new, `core-rules`) | classifier, box selection, exclusion policy, frame budget, diff |
| `tests/HDBF.Tests/PlotHighlightTests.cs` | 1-543 (new, `core-rules`) | 37 L1 tests for the rules above |

Gameplay (Lead):

| File | Lines | Change |
|---|---|---|
| `src/HDBF.Gameplay/Components/HdbfPlotHighlightSession.cs` | 1-62 (new) | session-wide overlay state (`ActiveEntityId`, `FailureKey`) |
| `src/HDBF.Gameplay/Components/HdbfPlotHighlightService.cs` | 1-575 (new) | activate/deactivate, input processor (click/drag/Esc), selection auto-clear, 200/frame refresh, picking, batch actions |
| `src/HDBF.Gameplay/Components/HdbfPlotHighlightRenderer.cs` | 1-355 (new) | vanilla asset load, cached matrices, 4 instanced draws, dirty lists |
| `src/HDBF.Gameplay/Components/HdbfBoxFarmController.Highlights.cs` | 1-223 (new) | per-plot state source, plant-cell pick, toggle/batch exclusion |
| `src/HDBF.Gameplay/Components/HdbfBoxFarmController.cs` | 86, 145, 159 | inject `HdbfPlotHighlightSession` |
| `src/HDBF.Gameplay/Components/HdbfBoxFarmController.Snapshot.cs` | 72-75 | mirror highlight state/failure into the snapshot |
| `src/HDBF.Gameplay/Components/HdbfBoxFarmController.Commands.cs` | 107-137 | single-plot exclusion now marks the save dirty |
| `src/HDBF.Gameplay/Components/HdbfFarmCommandService.cs` | 32-40, 165 | `TrySetPlotHighlight` implementation |
| `src/HDBF.Gameplay/HdbfGameConfigurator.cs` | 58-62 | bind session + overlay service |
| `src/HDBF.Gameplay/HDBF.Gameplay.csproj` | 87-126 | refs: InputSystem, CameraSystem, TerrainQueryingSystem, GridTraversing, SelectionSystem, Rendering, AssetSystem, Coordinates, Unity.InputSystem |

UI (`ui-loc`):

| File | Lines | Change |
|---|---|---|
| `src/HDBF.UI/Panels/HdbfFarmPanelFragment.cs` | 92-221, 227-289, 370-402, 534-539, 647-693, 732-742, 854-859 | highlight button, legend, hint, failure line, close-on-clear/switch, fingerprint |
| `src/HDBF.UI/Panels/HdbfFarmPanelData.cs` | 99-137, 208-216, 472-515 | highlight data fields, legend rows, state→key/tooltip helpers |
| `src/HDBF.UI/Localization/HdbfUiLocKeys.cs` | 51-57, 87-88, 133-137 | 7 new constants + `ActionHighlightPlots` |
| `src/HDBF.UI/README.md` | interaction/known-gaps/manual-check sections | honest status update (was "no highlight layer") |
| `data/Localizations/enUS.csv`, `zhCN.csv`, `zhTW.csv` | 88-94 in each | 7 identical new keys, 3 CSV fields, no existing row changed |
| `tools/validate/Localizations.cs` | 36-40, 51, 62-63 | new required keys |
| `tests/HDBF.Tests/HighlightUiContractTests.cs` | 1-~300 (new) | 9 UI/loc L1 tests |

New localization keys (all three languages; zhTW keeps English for two pre-existing
buttons per the project's zhTW convention):

| key | enUS | zhCN |
|---|---|---|
| `HDBF.Panel.HighlightLegend` | Managed Plots | 托管格图例 |
| `HDBF.Panel.HighlightIrrigated` | Irrigated | 已灌溉 |
| `HDBF.Panel.HighlightDry` | Not Irrigated | 未灌溉 |
| `HDBF.Panel.HighlightExcluded` | Excluded | 已排除 |
| `HDBF.Panel.HighlightUnreachable` | Unreachable | 不可达 |
| `HDBF.Tooltip.HighlightPlots` | click/restore, Shift+drag, Esc hint | 点击托管格可排除或恢复；按住 Shift 拖拽框选；按 Esc 关闭高亮。 |
| `HDBF.Tooltip.HighlightBoxSelect` | Shift+drag to exclude or restore several plots at once. | 按住 Shift 拖拽可一次排除或恢复多个托管格。 |

(`HDBF.Action.HighlightPlots` already existed from chapter 14.4 and is now actually wired.)

---

## 7. Verification actually run (offline)

All builds use the sandbox output root, nothing is deployed and the game was not started:

```powershell
$w = Join-Path $env:TEMP 'hdbf-card-work'
dotnet build src\HDBF.Core\HDBF.Core.csproj        -c Release -m:1 -p:HdbfWorkRoot=$w
dotnet build src\HDBF.Gameplay\HDBF.Gameplay.csproj -c Release -m:1 -p:HdbfWorkRoot=$w
dotnet build src\HDBF.UI\HDBF.UI.csproj            -c Release -m:1 -p:HdbfWorkRoot=$w
dotnet build src\HDBF.Integration\HDBF.Integration.csproj -c Release -m:1 -p:HdbfWorkRoot=$w  # validator completeness only
dotnet build tools\validate\validate.csproj        -c Release -m:1 -p:HdbfWorkRoot=$w
dotnet build tests\HDBF.Tests\HDBF.Tests.csproj    -c Release -m:1 -p:HdbfWorkRoot=$w
dotnet "$w\bin\HDBF.Tests\Release\net9.0\HDBF.Tests.dll"
```

Results (final run, 2026-10-25):

* Core / Gameplay / UI / Integration / validate / Tests: **0 errors, 0 warnings**.
* L1 suite: **`PASSED 428  FAILED 0`**. Attribution: the card started from 358/0; this card adds
  37 Core-rule tests + 9 UI/localization contract tests; the remaining 24 are from the concurrent
  BUG-06 maintenance-default card that landed in the same shared workspace while this card was
  running (the suite is cumulative, every count above is green).
* Data validator (standalone, sandbox assemblies, no staging/deploy):
  `dotnet "$w\bin\validate\Release\net9.0\HdbfValidate.dll" data --managed "<Managed>" --assemblies "$w\bin" --ref "$env:USERPROFILE\.hdbf\ref"`
  → `mod assemblies : 5/5 OK`, `WARNINGS: none`, **`RESULT: OK - data/ is valid.`**
  (the first run failed only because `HDBF.Integration` had not been built in the sandbox; after a
  plain single-project build of that assembly the validator was clean).
* No `bin/`/`obj/` artefacts were left inside the workspace (build output is redirected by
  `Directory.Build.props`); no deployment, no game, no `build.ps1`.

What this proves: the contract compiles, the pure rules are correct at L1, UI/localization data
agree, and gameplay compiles against the exact installed game APIs (so every referenced
type/member exists in 1.1.2.8). It does **not** prove any runtime rendering/input behaviour.

---

## 8. Honest gaps and deviations

1. **Nothing was verified in game.** Rendering, colours, input, Esc layering and draw-call count
   are all `待實機`; see §9.
2. **Unreachable (red) is not populated.** The contract, renderer bucket and legend are complete,
   but the current scan does not retain unreachable candidates, so red cells will not appear. The
   card explicitly marked this state optional; populating it needs a scan-side diagnostic list
   (touch `HdbfBoxFarmController.Scan.cs` / BUG-05 territory) — deliberately not done here.
3. **Tank-fed plots can look dry.** Per-cell colour uses the soil-moisture service; the aggregate
   coverage also counts tank-fed plots. Until the tank water raises soil moisture the overlay can
   show yellow while the panel coverage is higher. This is honest sampling, not a second water
   simulation; the in-game check should confirm the expected lag.
4. **Box select only excludes**, it does not restore already-excluded cells (chapter 14.3 mandates
   bulk exclusion; single click restores). There is no drag rectangle preview.
5. **Esc two-layer close is unverified.** The overlay consumes `InputService.Cancel`; whether the
   game's panel Esc handler also fires on the same frame (closing the panel immediately) must be
   checked in game. If it does, the fix is to gate the game's panel close on overlay state (UI
   event hook) — not implemented because it cannot be verified offline.
6. **Asset-path dependency.** The overlay loads `Markers/TileMarker` + `Markers/TileMarkerSmall`
   by their vanilla blueprint paths. If a future game build moves those assets, `ResolveAssets`
   logs one warning and the overlay stays hidden (no per-cell fallback, to protect the draw-call
   budget). The constants are documented in the renderer header.
7. **Tint alpha / colours are first-version constants** (65 % alpha). They may need tuning after
   seeing the actual game lighting; the values are single-sourced in Core
   (`HdbfPlotHighlightPalette`) so UI legend and world overlay change together.
8. **Single-plot exclude now marks the save dirty.** Small correctness fix in the existing path
   (it persisted `ExcludedPlotIds` but did not request a save, unlike restore); no semantic change
   to exclusions or harvests.
9. **Escape/right-click:** `InputService.Cancel` may be bound to more than Esc; the overlay treats
   it as "close overlay". In-game check needed for the exact binding behaviour.
10. **Docs sweep pending for the verification docs.** Per the card, this workstream did not touch
    `docs/VERIFICATION.md` or `docs/REQ_TRACEABILITY.md`; other cards running in the same shared
    workspace do edit them, so their current content is not this card's evidence. After the
    in-game run, the verify workstream/Lead must update REQ-UI-05 status (or mark it 待實機).

---

## 9. 待實機驗證步驟 (in-game verification — not done here)

Do this in a **test save**, with the mod packaged by the normal `build.ps1 -Install` flow (this
card did not deploy). Steps and expected observations:

1. **Open:** build a Basic box farm, let the first scan finish (panel shows Registered Plots), then
   select it. The action row shows a "Show Managed Plots" button.
2. **Toggle on:** click it. Expected: the button becomes bold; the legend header + four rows
   (Irrigated / Not Irrigated / Excluded / Unreachable) and the Shift-drag hint appear; managed
   cells get a semi-transparent tint (green where the panel coverage counts moisture, yellow
   otherwise, gray for previously excluded plots). If nothing is drawn, check the Player.log for
   one `[HDBF/Warn] ... managed-plot overlay hidden; vanilla marker tile assets could not be
   loaded` line (asset path issue) — this is the failure signal we could not exercise offline.
3. **Single click exclude:** click a green/yellow cell. Expected: its tint turns gray immediately
   (≤0.5 s refresh), the panel's Registered Plots count drops by one within 250 ms, the cell is
   persisted (`excludedPlotIds` in the save), and after a rescan it stays out. The click must
   **not** change the selected building (the panel stays open on the same farm).
4. **Restore:** click a gray cell. Expected: it leaves the gray set; after the next scan it is
   registered again (green/yellow) and the registered count recovers.
5. **Last-plot rule:** with only one registered plot left, click it. Expected: the cell stays and
   the panel result line shows the localized "At least one managed plot must remain." message
   (from `PlotMinimum`), no persistence change. Same for a Shift-drag box that would exclude every
   registered plot (whole batch refused, no partial application).
6. **Box select:** hold Shift, drag a rectangle over several managed cells, release. Expected: all
   registered cells in the rectangle turn gray at once; already-gray cells inside are ignored; the
   count drops by the number of newly excluded cells. A very small Shift movement (<4 px) is a
   click, not a drag.
7. **Esc layering:** press Esc once. Expected: the overlay closes (tints disappear, panel stays
   open on the farm). Press Esc again: the panel closes. If the first Esc also closes the panel,
   record that as a finding (see §8.5).
8. **Auto-clear on panel close / building switch:** with the overlay active, select another
   building (or close the panel with Esc/toolbar). Expected: the tint disappears immediately and
   the button state resets; re-selecting the farm shows the button inactive (legend hidden).
9. **Building selection mutual exclusion:** while the overlay is open, click terrain / another
   building. Expected: the click performs a plot operation or does nothing; it never selects the
   other building and never closes the farm panel. Clicks on the panel itself must still work
   (buttons/toggles).
10. **Performance (Top tier, 600 plots):** open the overlay and watch the Frame Debugger/Profiler.
    Expected: at most 4 instanced draw calls for the overlay (one per non-empty state; 0 for an
    empty state), first paint spread over ≤3 frames (no single-frame hitch), no per-frame
    allocations visible in the Profiler while idle with the overlay open. Excluding cells must not
    rebuild the whole overlay. Close the overlay and confirm the extra draw calls disappear.
11. **Localization/visual encoding:** switch to zhCN and zhTW, reopen the panel and overlay —
    legend/hint/button/tooltip must be Chinese with no raw `HDBF.` text. Open the panel at 100 %,
    125 % and 150 % UI scale — the legend and buttons must not clip. Verify the four tints are
    distinguishable (and that a red-green colour-blind simulation still leaves the legend text
    readable; colour is never the only encoding).
12. **Keyboard:** Tab to the highlight button, Enter toggles it; the focus ring is visible; the
    game's Esc handling still works after the overlay is closed.

Everything above is **unverified** until someone runs it; this card only claims compile + L1 +
data-validator evidence.
