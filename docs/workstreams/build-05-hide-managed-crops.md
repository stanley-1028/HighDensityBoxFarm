# BUILD-05 — 托管格上的農作物視覺完全隱藏（crop models hidden, vanilla crop still grown/harvested）

Owner: `lead` (single build card). Date: 2026-10-25.
Design source: **user design decision 2026-10-25** — "托管格上的農作物視覺完全隱藏，只留建築內部槽位".
Requirement context: chapter 10.5 / 14.6 (no per-frame whole-map scan, frame budgets), ADR-009
isolation rule (never touch crops that are not this farm's managed plots), REQ-SAFE-01/02
(isolate every per-cell failure, never throw into the game loop), REQ-TEST-03 (Core rules must be
game-free L1-testable).

> **Honest status: implemented + offline-verified only. Nothing in this card was run in
> Timberborn.** Five mod assemblies build with 0 errors / 0 warnings, the L1 suite is green
> (`PASSED 492 FAILED 0`) and the data-config validator compiles and accepts the new keys, but the
> renderer behaviour, the discovery latency and the restore paths are **待實機** — the concrete
> checklist for the Lead's in-game round is §8. No deploy, no `build.ps1`, no game launch.

> Card constraints honoured: no deployment / no game launch / no `build.ps1`; **0 Harmony patch
> changes** (`src/HDBF.Integration/Harmony/*` untouched, zero references to the new code); no new
> static mutable state (all new state is per-controller instance state); no per-frame whole-map
> scan and no single-pass 600-cell scan; balance numbers, `docs/VERIFICATION.md`,
> `docs/REQ_TRACEABILITY.md`, panel data, plot highlight (BUILD-02) and world icons (BUILD-04)
> untouched.

---

## 1. 摘要 (Chinese summary)

1. 托管格上的原版 `Crop`/`Growable` 實體照常種植、成長、收穫、產出；只把它們的
   `MeshRenderer.enabled` 關掉（與原版 `NaturalResourceModel.SetVisibility(false)` 完全相同的
   做法），所以模型從地面消失，但模擬完全不受影響。
2. 0 個 Harmony patch、0 個 static 可變狀態。只在 `Tick()` 裡做「本農場自己的
   `_record.PlotIds`」分片刷新（預設每 2 秒一輪、每幀最多 64 格，600 格 = 10 幀），不掃全圖。
   已隱藏集合以 `plotId → crop entityId` 快取，重複掃描 idempotent；換株會重新隱藏；收穫後
   （`hasCrop=false`）清快取。
3. 新增 `ui.hideManagedCrops`（預設 `true`）。`false` → 全部恢復可見（需重啟遊戲，config 只在
   session 開始時載入一次）。
4. 恢復路徑：取消註冊 / 玩家排除 / 重新掃描被剔除 / 建築離開 finished state / 拆除 / 存檔卸載
   （`Deactivate`/`DeleteEntity`）。未註冊格（原版農田、其他模組作物、觀賞植物）永遠不被碰。
5. L1：`462 → 492` 全綠（新增 30 條，全部 game-free），Core/Gameplay/Integration/UI 全部 0 error。

---

## 2. API ground truth (installed Timberborn 1.1.2.8, read with `tools/ApiIndex`, never guessed)

Tooling (outputs in `%TEMP%`, not shipped):

```powershell
$w = Join-Path $env:TEMP 'hdbf-card-work'
dotnet build tools\ApiIndex\ApiIndex.csproj -c Release -m:1 -p:HdbfWorkRoot=$w
$m  = 'C:\Program Files (x86)\Steam\steamapps\common\Timberborn\Timberborn_Data\Managed'
$t  = "$w\bin\ApiIndex\Release\net9.0\ApiIndex.dll"
dotnet $t dump $m "$w\api-model-hiding.txt" '^Timberborn\.NaturalResourcesModelSystem\.[A-Za-z]+$' '^Timberborn\.BlockObjectModelSystem\.[A-Za-z]+$' '^Timberborn\.Fields\.Crop$' '^Timberborn\.Growing\.Growable$'
dotnet $t il   $m "$w\il-natres-model.txt"  '^Timberborn\.NaturalResourcesModelSystem\.NaturalResourceModel$' '.*'
dotnet $t il   $m "$w\il-growable.txt"      '^Timberborn\.Growing\.Growable$' '.*'
dotnet $t il   $m "$w\il-blockmodel.txt"    '^Timberborn\.BlockObjectModelSystem\.(BlockObjectModel|GameObjectExtensions)$' '.*'
dotnet $t referrers $m "$w\ref-natresmodel-show.txt" 'NaturalResourceModel::Show'
dotnet $t referrers $m "$w\ref-natresmodel-hide.txt" 'NaturalResourceModel::Hide'
```

| # | Fact | IL evidence | Consequence for the design |
|---|---|---|---|
| 1 | Crops are ordinary vanilla `BlockObject` entities carrying `Timberborn.Fields.Crop` and rendered by `MeshRenderer`s under the entity root. | `Crop` type index; `NaturalResourceModel.SetVisibility` walks `GetComponentsInChildren<MeshRenderer>(true)`. | Find the crop per plot cell with the call the farm already uses: `_blockService.GetFirstObjectWithComponentAt<Crop>(cell)` (Scan.cs/Farming.cs). |
| 2 | **Renderer-only hide is the vanilla mechanism.** `NaturalResourceModel` (internal type) has public `Show()`/`Hide()` → `SetVisibility(bool)` → for every `MeshRenderer` under the entity: `renderer.enabled = bool`. Nothing else. | `NaturalResourceModel::SetVisibility`: `GetComponentsInChildren<MeshRenderer>(true)` + loop `set_enabled`. | Reproduce exactly that body on the crop `GameObject`; this is visual-only, colliders and simulation untouched. Cannot reference the type (internal), so we do not reflect it either. |
| 3 | Growth-stage model switching does **not** re-enable renderers. `ShowSeedlingModel`/`ShowMatureModel`/`HideModel` call `NaturalResourceLifecycleModel.Show/Hide`, which only `GameObject.SetActive` the `#Alive/#Dying/#Dead` model children. | `Growable::ShowSeedlingModel` / `ShowMatureModel` / `HideModel`; `NaturalResourceLifecycleModel::Show`/`Hide` = `SetActive(bool)`. | A disabled `renderer.enabled` persists across seedling → mature → dry/dead. No per-stage re-hide needed (the sweep would catch it anyway). |
| 4 | Nothing in the normal crop lifecycle calls `NaturalResourceModel.Show()` again. Referrers of `Show()`: `NaturalResourceModel.PostInitializeEntity` (entity init, before our first sweep), `NaturalResourcesModelToggler.ToggleNaturalResource` (dev/UI resource toggle), MapEditor. | `referrers NaturalResourceModel::Show`. | The hide is stable in normal gameplay; only an explicit resource-visibility toggle could undo it (see §9 limitation). |
| 5 | The vanilla level-visibility model-hiding pipeline does not own crops: `ModelHidingConfigurator.ProvideTemplateModule` decorates only `HidabilityPositionUpdaterSpec`, which crop blueprints (Carrot/Wheat/…) do not contain. | `ModelHidingConfigurator::ProvideTemplateModule`; vanilla crop blueprints in `Timberborn_Data/StreamingAssets/Modding/Blueprints.zip`. | `ModelHider`/`HidableModels` cannot re-show a crop we hid. |
| 6 | `GameObject.SetActive(false)` would stop the simulation: `Growable` is a `BaseComponent` whose growth is an `ITimeTrigger` ticked on the entity. | `Growable.Awake/InitializeEntity`; `GrowableSpec.GrowthTimeInDays`. | **Rejected** as the hide mechanism (would stop growth/harvest). |
| 7 | `BlockObjectModel.HideFullModelPermanently()` also disables **colliders**: `UpdateModelVisibility` → `GameObjectExtensions.ToggleModelVisibility` → `ToggleRenderers` **and** `ToggleColliders`. | `BlockObjectModel::HideFullModelPermanently`; `GameObjectExtensions::ToggleModelVisibility`. | **Rejected**: the card allows visual hiding only, so colliders are left alone. |
| 8 | A crop has `EntityComponent` (the vanilla randomizer itself reads it). | `NaturalResourcesModelSystem.NaturalResourceModelRandomizer::PreInitializeEntity` → `GetComponent<EntityComponent>().EntityId`. | `EntityId` is the cache identity, so a harvested + replanted crop on the same plot is detected as a new entity and hidden again. |

Chosen mechanism = **reproduce vanilla `NaturalResourceModel.SetVisibility`**:
`crop.GameObject.GetComponentsInChildren<MeshRenderer>(true)` → `renderer.enabled = false`
(restore is the same loop with `true`). No `SetActive`, no collider change, no reflection,
no `HideFullModelPermanently`.

---

## 3. Core contract (game-free, L1-tested) — `src/HDBF.Core/Plots/ManagedCropVisibility.cs` (new)

| Member | line | Semantics |
|---|---|---|
| `enum ManagedCropVisualAction {None, Hide, Restore}` | :17 | Renderer-only instruction the gameplay layer applies. |
| `ManagedCropVisibilityState` | :55 | Instance-scoped decision + cache machine; no static state; never throws. |
| `HideEnabled` (default `true`) | :77 | Last observed `ui.hideManagedCrops`. |
| `SetHideEnabled(bool) → int[]` | :93 | Turning off drains the cache and returns the plot ids to restore; on/unchanged → empty. |
| `SetRegisteredPlots(IReadOnlyList<int>?) → int[]` | :113 | Mirrors `_record.PlotIds`; returns the hidden plots that are no longer registered (immediate restore on exclusion/rescan/save load); order preserved for the sharded walk; null/empty safe. |
| `Observe(plotId, hasCrop, cropEntityId) → action` | :160 | Registered + crop + switch on → `Hide` once, then `None` while the same entity id is cached; different entity id → `Hide` again; `hasCrop=false`/unmanaged/switch-off after a hide → `Restore` exactly once and the cache entry is dropped. Idempotent, allocation-free in the steady state. |
| `IsHidden(plotId)` / `TryGetHiddenIdentity` | :190 / :199 | Cache introspection used by the controller's synthetic-identity fallback. |
| `Forget(plotId) → bool` | :211 | Drops one cached hide **without** restore: if the renderer toggle failed after the decision was cached, the next sweep retries instead of answering "already hidden" forever. |
| `RestoreAll() → int[]` | :221 | Drains the cache (switch off / deactivate / delete); sorted ids, never null. |
| `BeginRefreshCycle()` / `TakeRefreshShard(buffer, budget, out completed)` | :231 / :244 | The sharded "known cells" walk: copies at most `budget` registered plot ids per frame, wraps at the end (`completed`), never allocates, safe for null buffer / budget ≤ 0 / empty registration. |

The reason the cache key is `plotId → Guid cropEntityId` (not a bare `HashSet<plotId>`): a crop is
harvested and the same plot is replanted many times per game; a plot-id-only set would consider the
new crop "already hidden" and leave it visible forever. The identity makes the exactly-once work
per entity correct.

---

## 4. Gameplay integration (`HDBF.Gameplay`)

### 4.1 New files

| File | line | What |
|---|---|---|
| `src/HDBF.Gameplay/Components/HdbfManagedCropVisibility.cs` (new, 110 lines) | :30 | `internal static class HdbfCropVisuals` — renderer-only toggle helper. |
| | :36 / :45 | `TryHide` / `TryRestore` → `TrySetRenderersEnabled`; a destroyed `GameObject` is a successful no-op. |
| | :57 | `TryGetCropIdentity` (`EntityComponent.EntityId`; missing/empty → false, controller uses a synthetic id). |
| | :77 / :92 | The vanilla-equivalent body: `GetComponentsInChildren<MeshRenderer>(true)` → `renderer.enabled = enabled`, all inside `try/catch` returning `out Exception?`. |
| `src/HDBF.Gameplay/Components/HdbfBoxFarmController.CropVisibility.cs` (new, 442 lines) | :44–59 | Per-controller instance state: `ManagedCropVisibilityState`, one reusable shard buffer, cycle gate, failure counters, synthetic identity. No static state. |
| | :68 | `StartCropVisibility()` — activation entry; when the switch is off restores all and logs one Info line. |
| | :105 | `SyncCropVisibilityPlots()` — mirrors `_record.PlotIds`, restores plots that left the registry, requests an immediate cycle. |
| | :129 | `RestoreAllCropVisuals(reason)` — switch-off / deactivate / delete. |
| | :158 | `RestoreManagedCropPlots(int[])` — restores the current crop on each dropped plot, per-plot isolated. |
| | :200 | `StepCropVisibility()` — per-frame slice; interval gate on `Time.unscaledTime` (:234); `TakeRefreshShard` with the budget (:251); completion sets the next allowed start. |
| | :283 | `ProcessCropPlot(plotId)` — `Decode` → `GetFirstObjectWithComponentAt<Crop>` → identity → `Observe` → apply, its own `try/catch` so a broken cell never aborts the sweep. |
| | :306 | Hide-failure rollback: `_cropVisibility.Forget(plotId)` so the next sweep retries that cell. |
| | :334 / :351 | Identity resolution + synthetic `Guid` fallback (monotonic instance field). |
| | :378 | `LogCropVisibilityDaily()` — one `HDBF/Info` per game day, skipped when the farm hid nothing and had no failures. |
| | :411 | `WarnCropVisibilityFailed(...)` — one throttled `HDBF/Warn` per building, message has context/plotId/cell/exception type (no stack). |

### 4.2 Hook edits (minimal, no reformatting)

| File | line | Hook |
|---|---|---|
| `HdbfBoxFarmController.Scan.cs` | :71 | `StepCropVisibility();` at the end of `Tick()`, after `StepFarmPathMeasurements()` — runs even when the plot scan is idle. |
| `HdbfBoxFarmController.Scan.cs` | :529 | `SyncCropVisibilityPlots();` at the end of `ApplyPlotArrays()` — every registration change (scan completion, player exclusion, save-load claim). |
| `HdbfBoxFarmController.Activation.cs` | :44 | `StartCropVisibility();` in `Activate()` after the world-icon registration. |
| `HdbfBoxFarmController.Activation.cs` | :85 | `RestoreAllCropVisuals("deactivate");` in `Deactivate()` before `DisableComponent()`. |
| `HdbfBoxFarmController.cs` | :258 | `RestoreAllCropVisuals("delete");` in `DeleteEntity()` after the `Deactivate()` try/catch (covers a throwing Deactivate). |
| `HdbfBoxFarmController.Simulation.cs` | :110 | `LogCropVisibilityDaily();` in `RunDailyTick()` after `OnFarmDayTick(today)`. |

### 4.3 Refresh algorithm (timing/performance requirements)

* **Scope**: only `_record.PlotIds` (this farm's own registry). No world/map enumeration, no
  `_blockService.AnyObjectAt` sweep, no scan of unregistered cells. Unregistered crops (vanilla
  fields, other mods' crops, decorations) are never even looked at, which is the ADR-009 isolation
  guarantee.
* **Low frequency**: a new walk starts at most once per
  `perf.cropVisibilityIntervalSeconds` (default 2 s, clamp 1–10 s), gated on `Time.unscaledTime`.
  A registration change (new scan / exclusion) requests an immediate walk; otherwise the gate
  applies. Discovery latency bound = one interval + one walk.
* **Sharded, never one pass**: each frame copies at most
  `perf.cropVisibilityCellsPerFrame` ids (default 64, clamp 8–512) via the Core cursor. The 600-plot
  top tier is 600 / 64 = **10 frames** per walk, and the buffer is allocated once and reused.
* **Exactly-once work via cache**: a cell whose crop entity is already cached returns `None` from
  `Observe`, so the steady state is 600 cheap `GetFirstObjectWithComponentAt<Crop>` lookups spread
  across 10 frames every 2 s, with zero renderer writes.
* **Cache cleanup**: a harvested/removed crop (`hasCrop=false`) produces one `Restore` and drops the
  cache entry; a replanted crop has a new `EntityId` → `Hide` again. A failed hide calls `Forget`
  so the next walk retries.
* **Isolation**: every cell, every restore batch and the whole step is wrapped in `try/catch`; a
  failure is counted and reported through the per-building throttled `HDBF/Warn`, and the sweep /
  frame continues (REQ-SAFE-01/02).

### 4.4 Restore paths (card requirement 1)

| Trigger | Code path |
|---|---|
| Player excludes a plot (`TryExcludePlot`) | `ApplyPlotArrays` → :529 → `SetRegisteredPlots` returns the removed id → renderers enabled. |
| Rescan drops a plot (capacity cut / road replaced the crop) | same `ApplyPlotArrays` path. |
| Save load / claim drops plots already owned elsewhere | `ClaimLoadedPlots` → `ApplyPlotArrays` → same path. |
| `ui.hideManagedCrops` off | `StartCropVisibility` (:68) + defensive `StepCropVisibility` config-off branch (:210). |
| Building leaves finished state | `Deactivate` → :85. |
| Building demolished / world unload | `DeleteEntity` → :258 (also after a throwing Deactivate). |
| Crop was already harvested | `Observe(hasCrop=false)` → `Restore`, controller no-ops because the entity is gone. |

---

## 5. Config / data changes

| File | line | Change |
|---|---|---|
| `src/HDBF.Core/Config/HdbfConfig.cs` | :111–124 | `PerfSettings.CropVisibilityIntervalSeconds = 2f`, `PerfSettings.CropVisibilityCellsPerFrame = 64` (documented). |
| | :165–172 | `UiSettings.HideManagedCrops = true` with the user-design comment. Additive record properties: old config files and old code paths are unaffected. |
| `src/HDBF.Core/Config/HdbfConfigCodec.cs` | :101–110, :138 | `Parse`: reads/clamps `perf.cropVisibilityIntervalSeconds`, `perf.cropVisibilityCellsPerFrame`, `ui.hideManagedCrops`; a missing/wrong-typed key keeps the default and warns through the existing `config.invalid` path. |
| | :220–221, :234 | `Serialize`: writes the three keys, so `Parse(Serialize(Default))` round-trips (L1-tested). |
| `src/HDBF.Core/Config/HdbfConfigRanges.cs` | :116–119 | Interval 1–10 s, cells/frame 8–512 (named ranges shared by codec + tools). |
| `src/HDBF.Core/Logging/HdbfLogKeys.cs` | :79, :87 | `gameplay.crop-visibility` (daily Info) and `gameplay.crop-visibility-failed` (throttled Warn); gameplay appends `.<entityId>` for per-building throttle identity. |
| `data/HDBF_Config.json` | :22–23, :32 | Shipped file: `cropVisibilityIntervalSeconds: 2.0`, `cropVisibilityCellsPerFrame: 64`, `ui.hideManagedCrops: true`. |
| `tools/validate/ConfigValidator.cs` | :40–41, :58, :62–63, :252 | Whitelist/type checks for the two perf keys and the `ui.hideManagedCrops` boolean (boolean keys are now identified by name via `UiBooleanKeys` instead of the old two-name `if`). |

`configSchemaVersion` stays **1**: the new keys are additive with compiled-in defaults, exactly like
the BUILD-04 `ui.*` section (REQ-ARCH-10 / chapter 10.3). **No localization changes**: the switch is a
`HDBF_Config.json` key, not an in-game settings-screen label (same as `ui.worldIcons`, BUILD-04), so
there is nothing to add to `data/Localizations/*.csv` and no new key can leak into the panel.

How to undo the feature (also in §8): set `ui.hideManagedCrops` to `false` in the deployed mod's
`HDBF_Config.json` and restart the game (the config is loaded once per session by
`HdbfSystemRegistrar`; there is no hot reload).

---

## 6. L1 tests (game-free, REQ-TEST-03)

New file `tests/HDBF.Tests/ManagedCropVisibilityTests.cs` (new; **30** `Test*` methods, no existing
test file touched):

* **A. Observe decisions**: registered crop → `Hide`; repeat same `Guid` → `None` (idempotent);
  different `Guid` (replanted crop) → `Hide` again; `hasCrop=false` after a hide → `Restore` once
  then `None`; unregistered plot → `None` and never cached; `Guid.Empty` identity is still
  idempotent.
* **B. Registration cancellation**: `SetRegisteredPlots` drops the cache of removed plots and
  returns them sorted; `IsHidden`/`RegisteredCount` flip; null/empty lists safe; switch-off variant.
* **C. Switch**: default on; `SetHideEnabled(false)` returns the sorted hidden ids and drains;
  disabled from the start never hides; re-enable hides again; no-op returns an empty non-null array.
* **D. RestoreAll**: sorted, clears, idempotent, never null.
* **E. Sharded refresh**: 600 ids / 64 budget = exactly 10 shards covering every id exactly once,
  `completed` only on the last shard, cursor wraps to 0; buffer smaller than budget does not
  overrun; budget ≤ 0 / null buffer / empty registration safe and do not advance; cycle restart.
* **F. Config**: defaults `true / 2.0 / 64`; parse `{"ui":{"hideManagedCrops":false}}`; wrong type
  warns and keeps the default; clamp 0.2→1 s / 99→10 s and 1→8 / 9999→512 with the paths warned;
  shipped `data/HDBF_Config.json` (embedded resource) carries the keys with the documented values;
  `Serialize(Default)` writes them and the round-trip keeps them with 0 warnings; legacy files
  without the new keys get the defaults.
* **G/H. Safety edges**: duplicate registration ids no-throw + order/count asserted; `Forget` drops
  a cached hide so the next `Observe` hides again (hide-failure retry), unknown id is a benign
  `false`.

Result (same commands as §7): **`PASSED 492 FAILED 0`** — `462` pre-existing + `30` new. The
`tools/release/release-gates.json` L1 gate (`minTests 460`, `mustContain "FAILED 0"`) still holds.

---

## 7. Build & verification evidence (sandbox; no deploy)

```powershell
$w = Join-Path $env:TEMP 'hdbf-card-work'     # sandbox redirects TEMP; outputs stay outside the repo
dotnet build src\HDBF.Core\HDBF.Core.csproj        -c Release -m:1 -p:HdbfWorkRoot=$w   # 0 errors, 0 warnings
dotnet build src\HDBF.Gameplay\HDBF.Gameplay.csproj -c Release -m:1 -p:HdbfWorkRoot=$w  # 0 errors, 0 warnings
dotnet build src\HDBF.Integration\HDBF.Integration.csproj -c Release -m:1 -p:HdbfWorkRoot=$w  # 0 errors
dotnet build src\HDBF.UI\HDBF.UI.csproj             -c Release -m:1 -p:HdbfWorkRoot=$w  # 0 errors
dotnet build tests\HDBF.Tests\HDBF.Tests.csproj     -c Release -m:1 -p:HdbfWorkRoot=$w  # 0 errors
dotnet "$w\bin\HDBF.Tests\Release\net9.0\HDBF.Tests.dll"                                # PASSED 492  FAILED 0
```

Also checked:

* `dotnet build tools\validate\validate.csproj -c Release -m:1 -p:HdbfWorkRoot=$w` → 0 errors
  (the validator compiles with the new whitelist/boolean keys).
* Running the validator on `data` reports **no config error or warning for the new keys**; its run
  still ends `FAILED` only because this sandbox redirects `HdbfWorkRoot` to `%TEMP%`, so the
  validator cannot find the five built mod assemblies under `src\<Assembly>\<Cfg>\<tfm>` (pre-existing
  environment limitation, unrelated to this card; the Lead's normal `build.ps1 -Install` layout has
  them).
* **0 Harmony changes**: `src/HDBF.Integration/Harmony/*` untouched (timestamps pre-date the card,
  zero references to the new types). The single ADR-009 Postfix is unchanged.
* Forbidden docs untouched (`docs/VERIFICATION.md`, `docs/REQ_TRACEABILITY.md` timestamps unchanged),
  no balance/catalog edits, no `build.ps1` edit, no `data/Buildings/**` edit.
* Files changed by this card (timestamp audit): Core config/log/plots, Gameplay component partials,
  `data/HDBF_Config.json`, `tools/validate/ConfigValidator.cs`, the new test file — nothing else.

---

## 8. 實機預期 / in-game checklist for the Lead (not yet executed)

Expected player-visible result: after the next refresh (≤ ~2 s + one 10-frame sweep at the default
budget), **no crop model is visible on any of the farm's managed ground tiles**; the farm keeps
harvesting real crops into its output inventory, and the panel/highlight/icons behave exactly as
before. Only the building's own model remains visible — "只留建築內部槽位" needs the art (internal
slots) that is still placeholder (see §9).

1. Load an existing save with a farm that has crops on managed plots. Within ~2 s the ground crops
   disappear. Panel coverage/registered plots, BUILD-02 highlight and BUILD-04 icons unchanged.
2. Let a harvest happen: the crop entity is removed; nothing appears in its place; the output
   inventory still fills (harvest accounting/fatigue unchanged).
3. After a replant, the new crop is hidden within ≤ interval + one sweep (a brief visible window is
   expected, §9). Watch the daily line:
   `[HDBF/Info] gameplay.crop-visibility.<entityId>: crop visibility hidden=… registered=… hideFailures=… restoreFailures=…`
   (at most one per day; skipped when idle). At activation one line:
   `gameplay.crop-visibility.<entityId>.start: crop visibility active…`.
4. Exclude one plot from the panel (or let a rescan drop a plot): its crop model **reappears**.
   Re-register: hidden again after the next refresh.
5. Demolish the building: every crop the farm hid reappears (one restore line only when it actually
   restored plots; the entity tree teardown then removes them).
6. Set `ui.hideManagedCrops` to `false` in the **deployed** `HDBF_Config.json`, restart, load:
   crops are visible, log says `crop visibility inactive (ui.hideManagedCrops=false)`. Set back to
   `true` to ship the default.
7. Control group: untouched vanilla farm fields, other mods' crops and decoration plants must never
   change (the code only walks `_record.PlotIds`; if any unregistered crop disappears, that is a
   P0 isolation bug — report it).
8. Regression checks: growth time (dense mode), harvest yield/rotation/fatigue, irrigation
   coverage, maintenance/power, world icons, plot highlight click/exclude.
9. If a crop stays visible: note which case (newly planted / after growing to mature / after
   reload). The first two would point at the discovery sweep; the second would falsify the §2 fact 3
   renderer-persistence assumption.
10. Visual quality is **not** final: hiding the crop shows only the building model, so the intended
    "internal slots" look still needs real `.timbermesh` art (ART chapter is a separate placeholder
    contract). This card delivers the correct simulation/visibility split, not the final look.

---

## 9. Not done / limitations (honest)

* **Nothing was verified in game**: no deploy, no launch. Every behaviour above is compile- and
  IL-verified only.
* **Discovery latency**: worker-planted crops are found by polling (workers do not notify the mod).
  Worst case a newly planted crop is visible for ~one interval + one sweep (~2.2 s at the default
  60 fps / 600 plots; longer with a slower frame rate or a smaller budget). This is the accepted
  design (the card explicitly asked for the low-frequency sharded refresh).
* **Renderer persistence across growth stages is IL-based, not runtime-verified.** It follows from
  §2 fact 3 (`ShowMatureModel` only toggles `GameObject.SetActive`). If a future game build changes
  that, the sweep would not re-hide an already-cached crop; the mitigation would be re-applying the
  toggle (a deliberate cache-cost trade-off, not done here).
* If some vanilla/dev UI toggle calls `NaturalResourceModel.Show()` on a managed crop, the cached
  "already hidden" answer means the sweep will not re-hide it until the crop identity changes. Per
  IL referrers this call set is entity-init + the natural-resource visibility toggle + map editor,
  i.e. not normal farming; accepted and documented.
* A failed **hide** is retried (Core `Forget` on failure). A failed **restore** (deregistration
  path) is logged (throttled `HDBF/Warn`) but not retried; the only realistic cause is a destroyed
  entity, which is a no-op success rather than a failure.
* Crops without a readable `EntityComponent.EntityId` use a synthetic identity: repeating the same
  crop is still idempotent, but if such a crop is replaced without an empty-cell observation in
  between, the replacement may not be hidden. Normal crops have an `EntityId` (§2 fact 8).
* No config hot reload: `HdbfSystemRegistrar` loads `HDBF_Config.json` once per session, so the
  switch is read at activation and changing the file needs a restart (the code reads the live config
  object each sweep, so a future hot-reload would restore automatically).
* No new localization keys: the switch is a config-file switch, not an in-game settings row (same
  pattern as `ui.worldIcons`).
* The full `tools/validate` run cannot pass in this sandbox (assembly layout discovery), so the
  new keys are proven by the codec/validator build + the L1 config tests, not by a full validator
  run.
* No performance measurement in game (frame time with 100 farms × 600 plots is unmeasured; the
  per-frame work is bounded by `perf.cropVisibilityCellsPerFrame` lookups + at most 64 renderer
  writes per frame by construction).

---

## 10. Acceptance mapping (card → evidence)

| Card requirement | Evidence |
|---|---|
| Hide crop visuals on every registered plot (growing/mature/just planted/after harvest residue); renderer only, growth/yield/harvest untouched | §2 mechanism + §4.3; `HdbfCropVisuals.TrySetRenderersEnabled` (MeshRenderer.enabled only) |
| Never hide unregistered crops (ADR-009) | §4.3 scope = `_record.PlotIds` only; `Observe` returns `None` for unmanaged ids (L1 A) |
| Restore on cancel/exclude/demolish/save unload | §4.4 table + hooks :529/:85/:258 |
| Low-frequency, sharded, no per-frame whole-map / no single-pass 600-cell scan | §4.3 math + `TakeRefreshShard` budget (L1 E) |
| entityId cache, cleanup, idempotent | §3 cache + `Forget`; L1 A/D/H |
| try/catch, one-cell failure isolation, `HDBF/Warn` throttled | §4.1 `ProcessCropPlot`/`WarnCropVisibilityFailed` |
| `ui.hideManagedCrops` default true, schema sync, off = all visible | §5 (config/codec/ranges/data/validator) |
| One throttled `HDBF/Info`/diag per building per day with hidden/failure counts | §4.1 `LogCropVisibilityDaily` (:378) |
| Don't affect panel/highlight/icons/harvest/fatigue/irrigation/maintenance/power/vanilla crops | zero edits to those paths; only renderer bits + one log line |
| L1 game-free tests for hide/restore/ignore/idempotent/switch-off/harvested-null | §6, `PASSED 492 FAILED 0` |
| Build 0 error | §7 |
