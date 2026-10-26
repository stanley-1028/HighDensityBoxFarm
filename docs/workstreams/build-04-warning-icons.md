# BUILD-04 — world-space P0/P1 warning icons & cross-building aggregation (chapter 14.2 / 14.6)

Owner: `lead` (single build card). Date: 2026-10-25.
Requirement source: `高密度盒子农场模组需求文档.md` chapter 14.2 (状态与警告: P0/P1 head-top icons,
40-cell distance, ≤1 Hz blink, cross-building aggregation), chapter 14.5 (三重编码 / 闪烁), chapter
14.6 (头顶图标每 0.5 s 聚合、警告列表每 1 s 重排、只处理屏幕内建筑、聚合结果缓存、排序稳定),
REQ-UI-03/04/10/12.

> **Honest status: implemented + offline-verified only.**
> Core/Gameplay/UI/Tests build with **0 errors / 0 warnings**, the L1 suite is green
> (`PASSED 457 FAILED 0`) and the data validator passes, but **nothing in this card was run in
> Timberborn**. Every world-space behaviour (which sprite appears, the 40-cell cull, blink rate,
> row expansion, camera focus) is **待實機** and listed with concrete steps in §9. "Implemented"
> below never means "verified in game".

> Card constraints honoured: no deployment, no `build.ps1`, no game launch, no balance-number
> change, no `src/HDBF.Core/Balance` edit, `docs/VERIFICATION.md` and `docs/REQ_TRACEABILITY.md`
> untouched, no new static mutable state, no per-frame whole-map scan, UI reads only through Core
> contracts.

---

## 1. What was researched and how

Ground truth came from the installed game (1.1.2.8, Unity 6000.5), not from the requirement's
example type names. Tooling:

```powershell
$w = Join-Path $env:TEMP 'hdbf-card-work'
dotnet build tools\ApiIndex\ApiIndex.csproj -c Release -m:1 -p:HdbfWorkRoot=$w
$m = 'C:\Program Files (x86)\Steam\steamapps\common\Timberborn\Timberborn_Data\Managed'
$tool = "$w\bin\ApiIndex\Release\net9.0\ApiIndex.dll"
dotnet $tool index $m "$w\il\type-index.txt"
dotnet $tool dump  $m "$w\il\status-api.txt" '^Timberborn\.StatusSystem\.' '^Timberborn\.BuildingStatuses\.'
dotnet $tool il    $m "$w\il\status-icon-cycler.txt" 'StatusSystem\.StatusIconCycler' '.*'
dotnet $tool il    $m "$w\il\status-subject.txt" 'StatusSystem\.(StatusSubject|StatusInstance|StatusToggle)' '.*'
dotnet $tool il    $m "$w\il\status-configurator.txt" '(StatusSystemConfigurator|BuildingStatusesConfigurator)' '.*'
dotnet $tool il    $m "$w\il\building-status-icon.txt" 'BuildingStatusIcon(Offsetter|Updater)' '.*'
dotnet $tool il    $m "$w\il\status-initializers.txt" '(NoPowerStatus|NeedsWaterBuildingStatus|NoRecipeStatus|UnconnectedBuildingStatus|...)' '.*'
dotnet $tool il    $m "$w\il\camera-service-il.txt" '^Timberborn\.CameraSystem\.CameraService$' '(get_Transform|get_Target|IsInFront|Load)'
dotnet $tool dump  $m "$w\il\selection-api.txt" '^Timberborn\.SelectionSystem\.'
dotnet $tool dump  $m "$w\il\singleton-api.txt" '^Timberborn\.SingletonSystem\.'
```

Curated excerpts are saved as [`docs/api/il-status-icons.txt`](../api/il-status-icons.txt); the
general API table gained [`docs/API_FACTS.md` §12](../API_FACTS.md).

### 1.1 Selected path — reuse the vanilla status-icon service, draw nothing ourselves

| Candidate (verified type) | Verdict |
|---|---|
| `Timberborn.StatusSystem.StatusIconCycler` + `StatusSubject` + `StatusToggle` | **selected**. Every `BuildingSpec` entity is automatically decorated with this pipeline (`BuildingStatusesConfigurator.ProvideTemplateModule` IL), so the icon mesh/material/facing/scale/stacking are all vanilla. The mod registers one toggle per P0/P1 warning kind and activates only the highest-priority one. |
| `Timberborn.BuildingStatuses.BuildingStatusIconOffsetter` | used implicitly by the decorator; owns the icon root (`InitializeIcon(Transform, 0.5)`), vertical stacking and visibility. Not instantiated by the mod. |
| Custom `Graphics.DrawMesh` sprite billboards | **rejected**. The card explicitly asks to reuse the vanilla service; a custom sprite renderer would duplicate camera facing, stacking, material and LOD behaviour and add draw calls (chapter 14.6). |
| `StatusAggregator`/`DynamicStatusAggregator` alert list | not used as the primary surface. The mod also needs its own per-farm summary/aggregation with P0/P1-only semantics and a 0.5 s budget, which the vanilla aggregator does not provide; the toggles are still registered with it (they are the same `StatusSubject`). |
| `EntitySelectionService.SelectAndFocusOn` | selected for the "click a member row → locate" action (vanilla alert-panel jump path). |

Key consequence: the HDBF blueprints already contain `BuildingSpec`, so no blueprint/data change
was needed for the icon pipeline itself.

---

## 2. IL evidence (summary; full excerpts in `docs/api/il-status-icons.txt`)

1. `BuildingStatusesConfigurator.ProvideTemplateModule`:
   `AddDecorator<BuildingSpec, BuildingStatusIconOffsetter/StatusSubject/StatusIconCycler>`.
2. `StatusSystemConfigurator.ProvideTemplateModule`:
   `AddDecorator<StatusIconCycler, FacingCamera>` + `AddDecorator<BlockObject, StatusSlotOccupier>`
   → camera facing and screen-slot stacking are vanilla.
3. `BuildingStatusIconOffsetter.PreInitializeEntity` calls
   `StatusIconCycler.InitializeIcon(Transform, 0.5f)` → the icon root exists for every building.
4. `StatusSubject.RegisterStatus(StatusToggle)` → `StatusInstanceFactory.CreateStatus` →
   `StatusSpriteLoader.LoadSprite("Sprites/StatusIcons/" + name)` → `IAssetLoader.Load<Sprite>`.
5. `StatusToggle.CreatePriorityStatusWithFloatingIcon` / `CreateNormalStatusWithFloatingIcon`
   create a `StatusSpecification` with `ShowFloatingIcon = true` (`CreateWithIcon`).
6. `StatusIconCycler.CheckActiveStatuses/UpdateIcon/ShowIcon` show **one** active status with
   `ShowFloatingIcon && IsVisible()`; `StatusIconMaterials.SetMaterial` clones the renderer material
   and writes the sprite texture into `_BaseMap`; `UpdateScale` divides by the building scale;
   `ToggleStatusIcon` calls `FacingCamera.Enable`.
7. `StatusIconCyclerUpdater`: `UpdateStatusVisibility()` every frame, `IntervalUpdate()` every
   `UpdateInterval = 1.25 s` — the cycler itself is vanilla-timed; the mod's 0.5 s pass only decides
   which status is active.
8. `StatusVisibilityToggle.Hide()/Show()` (public, returned by public
   `StatusIconCycler.GetStatusVisibilityToggle()`) drives `StatusIconCycler.UpdateVisibility` →
   `ToggleRoot`, i.e. the floating icon can be hidden while the status stays active.
9. Vanilla sprite literals reused (all from IL `ldstr`): `NoPower`, `BuildingNeedsWater`,
   `UnspecifiedRecipe`, `UnconnectedBuilding`; the loader directory literal is
   `"Sprites/StatusIcons"`.
10. `CameraService.Transform` / `Target` (public) + `CameraServiceSpec.BaseDistance = 32`
    (blueprint) → measure "cells" from `Target`, not from the camera (see §10.4).
11. `EntitySelectionService.SelectAndFocusOn(BaseComponent)` (public).
12. `ILoadableSingleton.Load()`, `IUpdatableSingleton.UpdateSingleton()`,
    `IUnloadableSingleton.Unload()` — lifecycle names used by the new singletons.

---

## 3. Data contract (Core, game-free, L1-tested)

`src/HDBF.Core/Warnings/FarmWarnings.cs`

* `HdbfWarningKeys` (:17) — the nine warning localization keys, canonical in Core. Gameplay's
  `HdbfLocKeys` warning constants now alias it; UI keeps compile-time literals (localization
  validator requirement) and a test pins both spellings equal.
* `FarmWarningIconKind` (:35) — `None|Shutdown|NoWater|IrrigationLow|NoPower|AdapterFailed`, the
  game-free icon semantics. Gameplay maps a kind to a vanilla sprite name; Core never knows sprites.
* `FarmWarningCatalog` (:57) —
  * `IsWorldIconSeverity` = P0 (Critical) or P1 (High);
  * `IconKindOf(codeKey)` mapping;
  * `HasWorldIcon(severity,key)` (:91) requires **both** a P0/P1 severity and a mapped kind (a
    malformed P2+Shutdown pair cannot produce an icon);
  * `Compare` = severity then ordinal key (single ordering rule reused by UI + aggregator).
* `FarmWarningSubject` (:127) — immutable per-farm warning set, copied from the snapshot.
  `FromSnapshot` prefers the new `BoxFarmSnapshot.RuntimeEntityId` (locatable) and falls back to the
  save-id mapping only for old snapshots.
* `FarmWarningSummary` (:207) — per-farm **highest priority warning + 影響條數**:
  `HighestSeverity`, `HighestWarningKey`, `HighestSeverityCount`, `TotalCount`, `IconKind`,
  `HasWorldIcon`, `BlinkAllowed` (P0 only). This is what the 0.5 s icon pass consumes.
* `FarmWarningMember` (:303) / `FarmWarningGroup` (:317) / `FarmWarningAggregator` (:336) —
  cross-building aggregation: one member per farm per key, members sorted by entity id, groups sorted
  severity → count desc → key, `FindGroup` for the panel row lookup.
  `BuildGroups(null)` / empty input returns an empty list and never throws.

`src/HDBF.Core/Abstractions/IBoxFarmWarningQuery.cs`

* `IBoxFarmWarningQuery.GetWarningGroups()` (:21) — the only UI → gameplay aggregation seam; returns
  the immutable cached list, never scans buildings in UI code.
* `IBoxFarmLocator.TryLocate(Guid)` (:38) — member-row action; gameplay implements it with the
  vanilla selection service.

`src/HDBF.Core/Abstractions/BoxFarmSnapshot.cs` (:62) — additive `RuntimeEntityId` so a cached
aggregate member can be selected in the world (the long save id cannot address an entity).

`src/HDBF.Core/Warnings/WorldIconPolicy.cs`

* constants: `DefaultMaxDistance = 40` (:14), `DefaultIconIntervalSeconds = 0.5` (:17),
  `DefaultWarningListIntervalSeconds = 1` (:20), `DefaultBlinkHz = 1` (:23),
  `DefaultFarmsPerPass = 24` (:30), `DefaultAggregateTtlSeconds = 3` (:37);
* `IsWorldIconVisible(isOnScreen, distance, maxDistance)` (:44) — screen inside **and**
  `distance <= 40`; NaN/Infinity/negative budget → false;
* `ShouldBlink(severity, blinkEnabled)` (:56) — P0 only, switch honoured;
* `IsBlinkOn(elapsedSeconds, blinkHz)` (:68) — 1 Hz = on `[0,0.5)`, off `[0.5,1)`; disabled → always on;
* `IsDue(now,next,interval)` (:94) — 0.5 s scheduling;
* `WarningHysteresis` (:118) + `Observe(slot, active, now, thresholdSeconds)` (:145) — the chapter-14.2
  2 s debounce, **wall-clock** based (see §5.1).

---

## 4. Gameplay implementation (chapter 14.2/14.6)

`src/HDBF.Gameplay/Components/HdbfWorldIconService.cs` (new, 625 lines)

* `Register(entityId, BaseComponent)` (:94) — called once when the farm becomes finished; resolves
  the already-present vanilla `StatusSubject`/`StatusIconCycler`, creates 5 toggles (P0 key kinds
  via `CreatePriorityStatusWithFloatingIcon`, P1 via `...Normal...`), registers them, stores the
  vanilla `StatusVisibilityToggle`. A missing pipeline only disables the icons for that farm; the
  farm still contributes to the cross-building aggregation (the entity is tracked either way).
  Re-entering the finished state reuses the toggles (a `StatusSubject` has no unregister API, so
  re-creating them would duplicate vanilla status entries).
* `Deactivate` (:179) / `Forget` (:197) — leaves the finished state (toggles off, icon hidden, kept
  for re-activation) vs demolition (drop every reference).
* `UpdateSingleton` (:260) — the budgeted pass:
  * reads `ui.*` config; the pass always runs because the cross-building aggregation is part of the
    warning list (chapter 14.2) and must keep working when the optional icons are switched off; with
    `ui.worldIcons = false` it still evaluates on-screen farms into the aggregation board but leaves
    every status toggle inactive (no icon is shown);
  * lazily resolves the `Camera` once from `CameraService.Transform` (no Find/GetComponent in the
    steady-state path; retried while the camera singleton is still loading);
  * runs at most every `ui.worldIconIntervalSeconds` (default 0.5 s);
  * for **every** registered farm does a cheap cull test: camera viewport
    (`Camera.WorldToViewportPoint`) + `Vector3.Distance(CameraService.Target, farm)` <=
    `ui.worldIconMaxDistance` (default 40). Off-screen/far farms get their icon hidden and are
    removed from the aggregation cache;
  * only visible farms consume the snapshot budget (`ui.worldIconFarmsPerTick`, default 24,
    round-robin cursor) — a screen full of farms cannot exceed the chapter-14.6 warning budget;
  * per farm snapshot → `FarmWarningSummary` → board report → activate only the highest-priority
    P0/P1 toggle, deactivate the others; compute blink visibility; apply the vanilla visibility
    toggle only when the hidden state actually changes (dirty flag).
* `Evaluate` (:446) / `ApplyVisibility` (:520) — blink uses `IsBlinkOn(now, 1 Hz)` and the same
  `StatusVisibilityToggle` as culling (`hidden = cullingHidden || blinkHidden`), so blinking and
  distance hiding cannot fight each other.
* No static mutable state; all maps/timers are instance fields; `Unload()` clears them.
* 0.5 s is the *mod* aggregation interval; the vanilla cycler's own 1.25 s interval stays untouched
  (it only refreshes icon visibility; state changes propagate immediately through the toggle event).

`src/HDBF.Gameplay/Components/HdbfWarningBoard.cs` (new, 161 lines)

* session singleton implementing `IBoxFarmWarningQuery` + `IUpdatableSingleton` +
  `IUnloadableSingleton`;
* `Report(subject)` (:53) stores the on-screen farm's warning subject with a timestamp;
  `Remove` drops it; `UpdateSingleton` (:76) rebuilds groups **at most once per second** and prunes
  entries older than `ui.warningAggregateTtlSeconds` (default 3 s) — chapter 14.6 "只有螢幕內建築 +
  聚合結果緩存 + 排序穩定";
* a rebuild failure keeps the previous group list and logs one throttled error.

`src/HDBF.Gameplay/Components/HdbfFarmLocator.cs` (new, 63 lines) — `IBoxFarmLocator` implementation;
resolves the live farm through `HdbfFarmQueryService.TryGetSource`, then
`EntitySelectionService.SelectAndFocusOn(BaseComponent)`; failures return false, never throw.

`src/HDBF.Gameplay/HdbfGameConfigurator.cs` (:68-72) — binds the board, its Core interface, the
icon service and the locator.

Controller integration:

* `HdbfBoxFarmController.cs` — new `HdbfWorldIconService` constructor dependency (:147-162), snapshot
  carries `RuntimeEntityId = _entityId` (`Snapshot.cs`), deletion drops icon refs (:256-259).
* `HdbfBoxFarmController.Activation.cs` (:40, :49, :77, :87, :101) — `Register` on activation,
  `Deactivate` on temporary exit, `Forget` on deletion; all isolated so icon failures never block the
  farm.
* `HdbfFarmQueryService.cs` (BuildSnapshot) now passes `Time.unscaledTime` into the evaluator.

---

## 5. Warning rule changes required by the icon pass

### 5.1 Hysteresis is now wall-clock, not sample-count

The old evaluator counted evaluation samples (`HysteresisSamples = round(2 s × uiRefreshHz)`), which
was correct only while the panel was the sole consumer. The icon pass adds a second consumer at a
different rate (and is the only consumer while the panel is closed), so the window would have shrunk
to ≈1.3 s or grown to 4 s depending on what is open. BUILD-04 moved the rule into Core
`WarningHysteresis` (time-based, L1-tested) and changed `HdbfWarningInputs.HysteresisSamples` to
`HysteresisSeconds`:

* `HdbfWarnings.cs` (:58, :64, `Confirm`);
* `HdbfGameplayContracts.cs` (`HysteresisSeconds`);
* `HdbfBoxFarmController.Snapshot.cs` (`HysteresisSeconds = HdbfWarningRules.HysteresisSeconds`,
  requirement constant 2 s — changing `uiRefreshHz` can no longer change the debounce);
* `HdbfFarmQueryService.BuildSnapshot` supplies the clock.

This is an intentional behaviour change (window is exactly 2 s at any sampling rate) and is covered
by the L1 hysteresis tests.

### 5.2 P0/P1-only world icons

`FarmWarningSummary.HasWorldIcon` is true only when the highest warning is P0/P1 **and** maps to an
icon kind. P2/P3/P4 warnings stay in the panel status area only, as the chapter-14.2 table requires
for those rows. The icon service activates at most one toggle, so the player sees the highest
priority warning, not a random member of a cycler list.

---

## 6. UI implementation — cross-building aggregate rows

`src/HDBF.UI/Panels/HdbfFarmPanelData.cs`

* `WarningAggregates` (:134) + `HdbfWarningAggregateRow` (:146) +
  `HdbfWarningAggregateMemberRow` (:163);
* `HdbfFarmPanelDataFactory.BuildWarningAggregates` (:359) — for every warning of the selected farm
  (P0→P4 order), finds the matching Core group, computes `others = group.Count − selected?1:0`,
  builds the row text `HDBF.Panel.WarningAggregate {0},{1}` ("另有 N 座建築：<warning text>") and a
  locatable member row per **other** farm (tier name). Cap 3 rows (`MaxWarningAggregates`, :199).
  Selected farm is never a member row; an empty/absent group produces no row.

`src/HDBF.UI/Panels/HdbfFarmPanelFragment.cs`

* injects `IBoxFarmWarningQuery` + `IBoxFarmLocator` (:67-68), no game component in UI;
* `Refresh` reads the cached groups and includes a separate aggregate fingerprint (:483-489) so a
  change on another farm re-renders even when this farm's snapshot is unchanged;
* `RenderWarningAggregates` (:601) rebuilds rows only when that fingerprint changed: a focusable
  `Button` per aggregate row (tooltip explains on-screen/cached semantics), clicking expands the
  member list, clicking a member (`OnLocateAggregateMember`, :674) calls `IBoxFarmLocator.TryLocate`
  → vanilla `SelectAndFocusOn`; a vanished building shows the generic localized action-failed line;
* expanded keys are kept across refreshes and cleared when the panel switches/closes (:255, :300).
* panel refresh remains ≤4 Hz (REQ-UI-12); the aggregation itself is rebuilt in gameplay 1×/s.

Localization (`data/Localizations/{enUS,zhCN,zhTW}.csv`, +6 identical keys each; validator list
updated in `tools/validate/Localizations.cs`):

| key | enUS | zhCN | zhTW |
|---|---|---|---|
| `HDBF.Panel.WarningAggregate` | `Also {0} buildings: {1}` | `另有 {0} 座建筑：{1}` | `另有 {0} 座建築：{1}` |
| `HDBF.Panel.WarningAggregateHint` | `Click a row to expand its buildings; click a building to select and focus it.` | `点击行展开建筑列表；点击建筑可选中并聚焦。` | `點擊列展開建築列表；點擊建築可選中並聚焦。` |
| `HDBF.Panel.SettingWorldIcons` | `World warning icons` | `世界警告图标` | `世界警告圖標` |
| `HDBF.Panel.SettingWorldIconBlink` | `Blink warning icons` | `警告图标闪烁` | `警告圖標閃爍` |
| `HDBF.Panel.SettingWorldIconDistance` | `Icon distance (cells)` | `图标可见距离（格）` | `圖標可見距離（格）` |
| `HDBF.Tooltip.WorldIcon` | `{0} (P0/P1 world icon; hidden beyond {1} cells)` | `{0}（P0/P1 世界图标；超过 {1} 格隐藏）` | `{0}（P0/P1 世界圖標；超過 {1} 格隱藏）` |

`HDBF.Tooltip.WorldIcon` is the vanilla status description (the icon hover text): `{0}` is the
localized P0/P1 warning text, `{1}` the configured distance. Settings labels are shipped for the
future settings card; the actual switches are `ui.*` in `HDBF_Config.json` (see §7).

---

## 7. Settings (no balance value touched)

`src/HDBF.Core/Config/HdbfConfig.cs` adds the **additive** `ui` section (`UiSettings`, :121);
existing `perf.*` / `features.*` keys keep their meaning. `HdbfConfigCodec` parses/serializes it,
`HdbfConfigRanges` clamps it, and `tools/validate/ConfigValidator.cs` validates the shape.
`data/HDBF_Config.json` ships the requirement defaults:

```json
"ui": {
  "worldIcons": true,
  "worldIconBlink": true,
  "worldIconMaxDistance": 40,
  "worldIconIntervalSeconds": 0.5,
  "worldIconFarmsPerTick": 24,
  "warningAggregateTtlSeconds": 3.0
}
```

* `worldIcons` — master switch (chapter 14.2 allows hiding the head-top icons; when off, the
  aggregation/panel rows still work, no status toggle is activated and no icon is shown);
* `worldIconBlink` — chapter 14.2/14.5 "闪烁可在设置中关闭" (P0 only when on);
* `worldIconMaxDistance` — 40 cells default ("初版可调");
* `worldIconIntervalSeconds` — 0.5 s default ("初版可调");
* `worldIconFarmsPerTick` — per-pass snapshot budget;
* `warningAggregateTtlSeconds` — on-screen aggregation cache TTL.

Old config files without `ui` parse to exactly these defaults (L1 test).

---

## 8. Files changed

Core:

| File | Change |
|---|---|
| `src/HDBF.Core/Warnings/FarmWarnings.cs` (new, 1-474) | warning keys, icon kinds, summary (highest + count), aggregation groups |
| `src/HDBF.Core/Warnings/WorldIconPolicy.cs` (new, 1-220) | 40-cell cull, 0.5 s schedule, 1 Hz blink, wall-clock hysteresis |
| `src/HDBF.Core/Abstractions/IBoxFarmWarningQuery.cs` (new, 1-43) | aggregation query + locator contracts |
| `src/HDBF.Core/Abstractions/BoxFarmSnapshot.cs` 56-62 | additive `RuntimeEntityId` |
| `src/HDBF.Core/Config/HdbfConfig.cs` 121-192 | `UiSettings` + `HdbfConfig.Ui` |
| `src/HDBF.Core/Config/HdbfConfigRanges.cs` 100-112 | `ui.*` clamp ranges |
| `src/HDBF.Core/Config/HdbfConfigCodec.cs` 49, 102-128, 209-218 | `ui` parse/serialize |
| `src/HDBF.Core/Logging/HdbfLogKeys.cs` 74-88 | `ui.world-icons`, `ui.world-icon-toggle` keys |

Gameplay:

| File | Change |
|---|---|
| `HdbfWorldIconService.cs` (new, 1-625) | 0.5 s on-screen pass, vanilla status toggles, culling/blink, lifecycle |
| `HdbfWarningBoard.cs` (new, 1-161) | 1 s cached cross-building aggregation |
| `HdbfFarmLocator.cs` (new, 1-63) | `SelectAndFocusOn` behind the Core locator |
| `HdbfWarnings.cs` 33-75, 206-213 | Core wall-clock hysteresis; evaluator signature |
| `HdbfGameplayContracts.cs` 152-160 | `HysteresisSamples` → `HysteresisSeconds` |
| `HdbfFarmQueryService.cs` BuildSnapshot | passes `Time.unscaledTime` |
| `HdbfBoxFarmController.cs` | icon service dependency; deletion forget |
| `HdbfBoxFarmController.Snapshot.cs` | `RuntimeEntityId`, `HysteresisSeconds` |
| `HdbfBoxFarmController.Activation.cs` 40-110 | register/deactivate/forget hooks |
| `HdbfGameConfigurator.cs` 62-74 | DI bindings |
| `HdbfLocKeys.cs` | warning constants alias Core; `TooltipWorldIcon` |
| `HDBF.Gameplay.csproj` 119-136 | `Timberborn.StatusSystem`, `Timberborn.Localization` refs |

UI / data / tools / tests / docs:

| File | Change |
|---|---|
| `src/HDBF.UI/Panels/HdbfFarmPanelData.cs` 128-440 | aggregate row records + factory |
| `src/HDBF.UI/Panels/HdbfFarmPanelFragment.cs` 61-130, 480-700, 955-1030 | groups read, fingerprint, expand/locate rows |
| `src/HDBF.UI/Localization/HdbfUiLocKeys.cs` 104-115, 150-156 | 6 new keys + tooltip |
| `data/Localizations/enUS.csv`, `zhCN.csv`, `zhTW.csv` | +6 identical keys each |
| `data/HDBF_Config.json` 22-30 | `ui` section (defaults) |
| `tools/validate/Localizations.cs` 64-75 | required key list |
| `tools/validate/ConfigValidator.cs` 13-75, 220-290 | validate `ui` |
| `tests/HDBF.Tests/WarningIconContractTests.cs` (new, 1-673) | 29 L1 tests |
| `tests/HDBF.Tests/TestRunner.cs` | `Guid` assertion overload |
| `docs/api/il-status-icons.txt` (new) | curated IL evidence |
| `docs/API_FACTS.md` §12 | verified game facts |

No `data/Buildings/**`, no balance number, no `docs/VERIFICATION.md`, no
`docs/REQ_TRACEABILITY.md` was changed.

---

## 9. Verification actually run (offline)

```powershell
$w = Join-Path $env:TEMP 'hdbf-card-work'
dotnet build src\HDBF.Core\HDBF.Core.csproj        -c Release -m:1 -p:HdbfWorkRoot=$w
dotnet build src\HDBF.Gameplay\HDBF.Gameplay.csproj -c Release -m:1 -p:HdbfWorkRoot=$w
dotnet build src\HDBF.UI\HDBF.UI.csproj            -c Release -m:1 -p:HdbfWorkRoot=$w
dotnet build tests\HDBF.Tests\HDBF.Tests.csproj    -c Release -m:1 -p:HdbfWorkRoot=$w
dotnet "$w\bin\HDBF.Tests\Release\net9.0\HDBF.Tests.dll"
```

Result (2026-10-25):

* Core / Gameplay / UI / Tests: **0 errors, 0 warnings** each.
* L1 suite: **`PASSED 457  FAILED 0`**. The card adds 29 tests
  (`tests/HDBF.Tests/WarningIconContractTests.cs`): P0-over-P1 summary, highest-severity count,
  P0/P1-only icon rule (P2/P3/P4 and malformed pairs hidden), cross-building grouping + stable
  member/group order, 40-cell boundary (40 visible, 40.001 hidden), off-screen/NaN culling, 1 Hz blink
  phase + switch off, 0.5 s pass scheduling, wall-clock hysteresis appear/hide/reset/slot
  independence, `ui.*` defaults/clamps/round-trip/legacy files, and localization key/placeholder
  consistency (three CSVs + Core↔UI key equality).
* Data validator (sandbox assemblies, no staging/deploy):
  `dotnet "$w\bin\validate\Release\net9.0\HdbfValidate.dll" data --managed "<Managed>" --assemblies "$w\bin" --ref "$env:USERPROFILE\.hdbf\ref"`
  → `mod assemblies : 5/5 OK`, `blueprints : 10`, `WARNINGS: none`, **`RESULT: OK - data/ is valid.`**
  (the new `ui` section and all 6 localization keys are accepted).
* No deployment, no game launch, no `build.ps1`; all outputs are under
  `%TEMP%\hdbf-card-work` (DSH sandbox redirects `%TEMP%`), nothing was written into the
  workspace `bin/`/`obj/`.

What this proves: the contract compiles against the exact installed 1.1.2.8 APIs, the pure rules
(priority, aggregation, culling, blink, hysteresis, config, localization) are correct at L1, and the
data/config shape stays valid. It does **not** prove any runtime icon, camera, timing or UI
behaviour.

---

## 10. Honest gaps / assumptions / deviations

1. **Nothing was verified in game** (see §11). This card only claims compile + L1 + data-validator
   evidence.
2. **Sprite mapping is a first version.** The requirement names custom glyphs (broken gear, slashed
   droplet, half droplet, broken lightning, broken plug); the game only ships a fixed status-icon
   set. The mod maps P0/P1 semantics to the closest verified vanilla sprites:
   Shutdown → `UnspecifiedRecipe`, NoWater/IrrigationLow → `BuildingNeedsWater`, NoPower → `NoPower`,
   AdapterFailed → `UnconnectedBuilding`. The actual visuals must be checked in game (§11 step 3);
   swapping a sprite is a one-line change in `HdbfWorldIconService` constants.
3. **§14.2 has an internal contradiction.** The table gives P1 irrigation "状态条 + 建筑头顶图标" and
   the explicit rule says "头顶世界图标仅显示 P0、P1", but the warning-fatigue mitigation paragraph
   says "P1 只进状态条". BUILD-04 follows the explicit table/rule (P1 icons exist; only P0 blinks;
   only P0 would get a sound, and this card adds no sound). The Lead should decide whether the
   mitigation sentence should instead win; the switch to change it is
   `FarmWarningSummary.HasWorldIcon` / the icon severity predicate (one place in Core).
4. **Distance origin is an assumption.** The 40-cell budget is measured from `CameraService.Target`
   (camera focus) because the camera's own distance is a zoom function (32 cells at default zoom;
   ~155 at max zoom-out), so camera distance would hide all icons when zoomed out. If the intended
   meaning is distance from the screen edge/screen centre in cells, only the culling call in
   `HdbfWorldIconService.UpdateSingleton` needs to change.
5. **Cross-building aggregation covers on-screen farms only.** Chapter 14.6 explicitly limits
   processing to on-screen buildings and requires a cached aggregate; the board therefore counts
   farms the icon pass has seen within `ui.warningAggregateTtlSeconds` (default 3 s). A row can
   therefore say "Also 2 buildings" even if more farms elsewhere have the same warning. If the
   requirement means a global count, the aggregation source (not the UI) must change.
6. **Settings UI does not exist.** The blink/distance/icon switches are `ui.*` in
   `HDBF_Config.json` (the card explicitly allows this path). The three `HDBF.Panel.Setting*` labels
   are shipped and validator/tests cover them, but no in-game settings card renders them yet.
   Switching `worldIcons` off only hides the head-top icons; the cross-building aggregation rows
   keep working (they are derived from the same on-screen pass, not from the icon toggles).
7. **Status description is localized at registration time** (same limitation as vanilla status
   components): switching language mid-session does not rewrite an already registered tooltip until
   the building is recreated. Vanilla behaves the same; no extra lifecycle was added.
8. **Vanilla cycler cadence.** `StatusIconCyclerUpdater.IntervalUpdate` is 1.25 s. Activating a
   status triggers an immediate `CheckActiveStatuses`/`UpdateIcon`, but if the game's update order
   ever shows a one-tick delay, the icon appears within 1.25 s. Verify in game; the mod's 0.5 s
   decision pass is unaffected.
9. **Blink uses unscaled time.** Icons blink while the game is paused (same as the vanilla status
   row blinker). If paused blinking is undesirable, gate on `IDayNightCycle`/game speed.
10. **Performance is budgeted, not measured.** 24 farm snapshots per 0.5 s pass, one aggregation
    rebuild per second, cheap transform/viewport culling for all registered farms. A farm snapshot
    itself computes fatigue over up to 600 plots, so 24 × 600 is the worst-case per pass; measure
    with 100 top-tier farms in the Profiler (§11 step 8) and lower
    `ui.worldIconFarmsPerTick` / raise `ui.worldIconIntervalSeconds` if needed.
11. **Locating a row closes/rebinds the panel.** `SelectAndFocusOn` is the vanilla selection path,
    so the entity panel will switch to (or close on) the located building. This is intended
    "定位" behaviour but is 待實機.
12. **Aggregation row expansion is local UI state.** The expanded/collapsed set is not persisted
    (correct for a transient panel), and is cleared when the panel switches or closes.
13. **No sound.** Chapter 14.2 allows a single P0 alert sound; this card implemented no audio (not
    required by the task and unverifiable offline). The vanilla `StatusToggle` also supports
    `IsNotifying`; wiring it later must be checked against the "single sound per P0" requirement.

---

## 11. 待實機驗證步驟 (in-game verification — not done here)

Use a **test save**, packaged by the normal `build.ps1 -Install` flow (this card did not deploy or
launch the game). `HDBF_Config.json` must contain the new `ui` section (the shipped default does).

1. **Icon appears (P1):** build a Basic/Intermediate farm, let a scan register plots, then let
   coverage fall below `C_min` (e.g. a tank-fed farm with the tank not refilled, or block
   irrigation). After the 2 s hysteresis the building should show a **single** vanilla status icon
   above it (`BuildingNeedsWater` sprite expected). Hover the icon: the tooltip must be the
   localized warning text + "(P0/P1 world icon; hidden beyond 40 cells)" (no raw `HDBF.` key).
   Panel status rows still show the same warning.
2. **Highest priority wins (P0 over P1):** on the same farm, produce a shutdown (maintenance missed
   twice) while irrigation is also low. Expected: the icon switches to the P0
   `UnspecifiedRecipe` sprite and stops cycling between icons; it never shows two icons at once.
3. **Sprite sanity:** repeat for the top tier with no power (expected `NoPower` lightning) and with a
   failed adapter if one is installed (expected `UnconnectedBuilding`). Record which glyph actually
   appears; if a glyph reads wrong, change the sprite-name constant (one line) and re-check — that is
   the planned §10.2 remediation.
4. **Distance culling (40 cells):** keep the farm selected and pan/zoom the camera so the farm is
   farther than 40 cells from the current camera focus. Expected: the icon disappears at >40 cells
   and reappears inside 40 (allow for the 0.5 s pass). Also zoom fully out: icons inside 40 cells of
   the focus must stay visible (this is the check that the measure is focus distance, not camera
   distance). If the requirement instead means camera distance, record the observed difference.
5. **Off-screen:** move the camera so the farm is off-screen. Expected: no icon work is done for it
   (it disappears) and its row leaves the aggregation list after ~3 s.
6. **Blink (P0 only, ≤1 Hz, switchable):** with a P0 warning, the icon must blink on/off about once
   per second (not faster), and P1 icons must be static. Set `"worldIconBlink": false` in
   `HDBF_Config.json`, restart: the P0 icon must be steady. Also set `"worldIcons": false`: no icons
   at all, panel and cross-building aggregation rows unchanged (the aggregation is independent of
   the icon switch).
7. **Cross-building aggregate row:** have ≥2 on-screen farms with the same warning, open one farm's
   panel. Expected: a row "另有 N 座建築：<warning>" under the status rows (N excludes the selected
   farm, counts on-screen farms); clicking the row expands one locatable member row per other farm
   (tier name); clicking a member selects and focuses that farm. Check the row disappears when the
   other farms are off-screen for >3 s, and that expansion stays open across panel refreshes.
   Also confirm the row never shows the selected farm itself and shows at most 3 rows.
8. **Performance:** with 100 top-tier farms (or as many as the test map allows) and several on
   screen, open the Profiler. Expected: no frame spike at the 0.5 s icon pass, no per-frame
   allocation growth while idle, no `HDBF/Error`/`HDBF/Warn` lines about
   `ui.world-icons`/`ui.world-icon-toggle`. Note the CPU cost of one icon pass and compare with the
   chapter-14.6 warning re-sort budget (≤0.5 ms; the exact measurement needs the game).
9. **Localization:** switch to zhCN and zhTW and repeat steps 1/6/7: all new text must be Chinese,
   no raw keys, no `?` placeholders; the settings labels are not visible anywhere yet (expected,
   §10.6).
10. **Lifecycle:** demolish a flagged farm (icon disappears, no errors), enter/exit construction mode
    on a farm (icons hidden while unfinished, no duplicate icons after finishing), save/reload (no
    duplicate vanilla status entries in the debug/status list), and load a save made before this
    card (old config without `ui` → default 40/0.5/blink on).

Everything above is **unverified** until someone runs it; this card only claims compile + L1 +
data-validator evidence.
