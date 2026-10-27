# Verification record — HDBF 0.1.0

Everything below was executed on this machine against the installed Timberborn `1.1.2.8-ffc2de0-sw`
(Unity 6000.5.11f1). Commands are copy-pasteable. Nothing here is a claim about a game session we
did not run; the "Not verified" section is part of the deliverable, not a disclaimer.

## 1. Build

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1
```

Result (final run): all five game assemblies `0 warnings / 0 errors`, L1 tests `PASSED 199 FAILED 0`,
data validator `RESULT: OK - data/ is valid`, staged package 258 KB (10 blueprints).

| Project | Target | Result |
|---|---|---|
| HDBF.Core | netstandard2.1 | 0 warning / 0 error |
| HDBF.Persistence | netstandard2.1 + Timberborn 1.1.2.8 assemblies | 0 / 0 |
| HDBF.Gameplay | netstandard2.1 + Timberborn 1.1.2.8 assemblies | 0 / 0 |
| HDBF.Integration | netstandard2.1 + Timberborn 1.1.2.8 assemblies | 0 / 0 |
| HDBF.UI | netstandard2.1 + Timberborn UI assemblies | 0 / 0 |
| tests/HDBF.Tests | net9.0 | `PASSED 199 FAILED 0` (exit 0) |
| tools/validate | net9.0 | `RESULT: OK` (exit 0), 660 game assemblies, 506 spec types, 10 blueprints (4 tiers × 2 factions + 2 collections) |
| tools/ApiIndex | net9.0 | builds and reproduces every API fact in docs/API_FACTS.md |

Environment constraints that shaped the build (docs/API_FACTS.md §7): `-m:1` is mandatory,
one `ProjectReference` per project maximum, no NuGet access, script execution policy requires
`-ExecutionPolicy Bypass`, and the DSH file sandbox is workspace-write only (installing into
`Documents\Timberborn\Mods` must be run by the user outside the sandbox).

## 2. Balance and save correctness (L1, automated)

`dotnet run --project tests/HDBF.Tests -c Release -m:1` — 199 tests in 10 suites:

| Suite | Tests | Covers |
|---|---:|---|
| BalanceFatiguePowerTankTests | 40 | G_irr/G_fat/G_maint/G_power/G_mode margins, tank capacity 3.6/10.8/33.6/76.8 and draw, TC-16/19/20/05 |
| ConfigCodecTests | 35 | chapter-18 clamping, garbage JSON, defaults, feature flags, schema version |
| SaveCodecTests | 28 | TC-10/11 class: repair, migration idempotence, canonical stability, 60-plot preservation |
| BalanceLabourIrrigationTests | 23 | P_eff, coverage, G_irr zero point, workCost 0.08/0.12/0.16, TC-16/17/18 |
| BalanceInvariantTests | 19 | chapter 6.10 invariants: cost/plot monotonic decrease, upkeep pressure increase, fatigue rate increase, mid/high/top per-farmer parity |
| CompatStateMachineTests | 17 | 3-strike disable, rollback, no-throw, port availability |
| ThrottledLogTests | 15 | 30 s per-key throttle, suppressed counts, Reset |
| PlotSelectionTests | 14 | stable distance/id ordering, capacity cut, registered-id preservation, TC-24 |
| CompatReportTests | 8 | 13/13 chapter-10.4 fields + JSON determinism |
| **Total** | **199** | |

Two real defects in code written by the Lead were found by the test pass and fixed before delivery
(both regression-tested):

1. `BalanceFormulas.PlotYield` clamped the mode multiplier to 1.0, silently deleting the
   dense-planting +35 % bonus. Regression: `BalanceFatiguePowerTankTests.TestPlotYield_DenseModeKeeps135Bonus`.
2. `HdbfSaveCodec.TrySanitize` did not copy the plot/crop/fatigue arrays before repairing a record,
   i.e. a healthy save would have been reloaded with zero plots. Regression:
   `SaveCodecTests.TestSanitize_Healthy60PlotRecordKeepsAllPlots`.

## 3. Data package validation (automated)

`tools/validate` compares our data against the **real** game metadata (Mono.Cecil over the 660
installed assemblies) and the built mod assemblies:

* manifest fields/versions valid;
* 4 building blueprints + 2 template collections, unique blueprint paths, every referenced
  blueprint file exists;
* every spec name is either a real game `ComponentSpec` subclass or our `HdbfBoxFarmSpec`; every
  JSON field of `HdbfBoxFarmSpec` maps to a `[Serialize]` member in the built `HDBF.Gameplay.dll`;
* footprint 3×1×2 with 6 blocks, entrance present, tool group/order, costs, science, workers,
  plot capacity/range/irrigation/fatigue/maintenance values per chapter 5;
* enUS/zhCN localization key sets identical, every `HDBF.*` key used by blueprints/UI present;
* config JSON shape and chapter-18 defaults;
* icons are real PNGs with `{"isSprite": true}` meta files.

Negative control: injecting an unknown spec field, wrong footprint, bad localization key, wrong
icon meta and wrong config types produced exit 1 with 13 targeted errors — the validator is not
vacuous.

## 4. Runtime-path evidence gathered without launching the game

`HDBF.Persistence` + `HDBF.Integration` were exercised with a temporary net9.0 harness that loaded
the built assemblies plus the real game `EntitySaver`/`EntityLoader`/`SerializedEntity`:
**81/81 assertions passed** (harness deleted afterwards).

* Save→Load canonical equality, including a 2^32-scale entity id round-trip.
* No-component load → new-building defaults (not "degraded"); partial component → tier defaults.
* Corrupt values (fatigue 5/-2, wrong types, missing keys, throwing loader, null arguments) →
  repaired record or safe default, never an exception.
* Missing mods → `Enabled=[]`, `Failed=[]`, all ports unavailable, 70–83 ms detection inside the
  2 s budget, `HDBF/Compat` logs emitted, report JSON complete.
* Simulated "mod present, API absent" → adapters degrade to `Degraded`, then `Failed` after three
  attempts with no retries; throwing detector is contained.
* DI shape verified: single constructor per DI-bound type, `ToExisting` same-instance binding,
  `[Context("Game")]` attribute present.

## 5. Not verified (and why)

| Item | Why not | What is needed |
|---|---|---|
| In-game appearance, placement, entrance/worker behaviour | no Unity editor / cannot launch a session in this environment | run TC-01 in game after installing |
| **Crop growth / planting / harvest / inventory integration** | this is milestone P1 of the blueprint (chapter 11.5 task-reuse risk): whether vanilla planting tasks accept plots not owned by a vanilla farmhouse cannot be answered without a running game | one in-game spike: if reusable → no patch; if not → single Postfix (still within REQ-IMPL-15's ≤2). Until then the controller computes labour/irrigation/fatigue/maintenance state and exposes it, but does not produce goods (no hidden production: REQ-CORE-02 is not violated by omission) |
| Maintenance material deduction | same P1 scope (needs inventory transaction against nearest warehouse) | wire `TryPerformMaintenance` to the inventory/stockpile service |
| Top tier on-grid power | blueprint has the power *port flag* but not the mechanical node/transput geometry; adding guessed coordinates would risk a silently broken network | copy `MechanicalBuildingSpec {}` + `MechanicalNodeSpec {PowerInput 150}` + `TransputProviderSpec` bounded to X0-2/Z0-1 + `MechanicalConnectorTargetSpec` and verify in game; until then Top runs the documented off-grid path (G_power 0.6, 15 metal / 8 d) |
| Five third-party mod adapters | none of the five mods is installed on this machine | every reflection anchor is marked `UNVERIFIED` in `src/HDBF.Integration/Adapters/ThirdPartyProbes.cs`; behaviour when absent is verified (ports unavailable, no errors) |
| L3–L6 test matrix, 11 hard acceptance items, performance thresholds | require a running game + Profiler | README §5 lists the re-run order |
| Art (chapter 13) | requires the game's Unity/timbermesh toolchain | placeholder vanilla models + frozen contract in `data/ART_PLACEHOLDER.md` |

## 6. Package layout evidence

`build/HDBF` (258 KB, 5 assemblies) contains:

```
manifest.json                      HDBF.Core.dll            HDBF.Persistence.dll
HDBF_Config.json                   HDBF.Gameplay.dll        HDBF.Integration.dll
ART_PLACEHOLDER.md                 HDBF.UI.dll
Buildings/Food/BoxFarm/BoxFarm.{Basic,Intermediate,Advanced,Top}.{Folktails,IronTeeth}.blueprint.json
TemplateCollections/TemplateCollection.Buildings.{Folktails,IronTeeth}.blueprint.json
Localizations/enUS.csv, zhCN.csv
Icons/BoxFarm{Basic,Intermediate,Advanced,Top}.png (+ .png.meta.json)
```

This is exactly the shape the game's own example mod uses (`StreamingAssets/Modding/ModTemplates/BerryJam`),
which is the strongest structural evidence available without launching the game.

## 7. Release checklist (what the next session should do first)

1. `powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -Install` (outside the sandbox).
2. Launch the game, enable the mod, open a new settlement, confirm the four buildings appear under
   Food with correct names/icons and can be placed on a 3×2 flat footprint.
3. Run the crop/task spike (TC-01/TC-33): observe whether any vanilla-style planting happens;
   this decides the Harmony-free vs 1-Postfix path for P1.
4. Run TC-21 (tank empties on the day-3/day-4 boundary), TC-23 (entrance blocked), TC-13/TC-14
   (600 plots / 100 buildings) and record Profiler output.
5. Only then update `docs/REQ_TRACEABILITY.md` statuses from "待游戏内验证" to verified.

## 8. 游戏内冒烟第一轮（用户实机执行，2026-10-24）

第一次真正启动游戏时，模组被发现并加载，但在世界加载阶段抛出：

```
InvalidDataException: Localization file contains errors:
Unnecessary comma found in enUS (line 34)
  at Timberborn.Localization.LocalizationCsvValidator.InformAboutErrors()
```

**根因**：`HDBF.Tooltip.EffectivePlots` 的文本 `Effective plots = min({0}, {1} × {2})` 含逗号却未加引号，
CSV 变成 4 列。游戏的 `LocalizationCsvValidator` 对列数不是 3 的行直接判为 "Unnecessary comma"
并中止本地化加载（进而导致读档失败）。zhCN.csv 同行同样问题。

**修复**：只给 Text 字段加引号（保持 ID/Comment 不加引号，与游戏自身 enUS.csv 风格一致）：

```csv
HDBF.Tooltip.EffectivePlots,"Effective plots = min({0}, {1} × {2})",HDBF extension
HDBF.Tooltip.EffectivePlots,"有效托管格 = min({0}, {1} × {2})",HDBF extension
```

**防复发**：`tools/validate` 的 `Localizations.LoadLanguage` 新增硬性规则——每个数据行必须恰好 3 个
CSV 字段，否则构建期报错并提示"给含逗号的 Text 加引号"。已用注入式负向测试验证（validator 从
exit 0 变为 exit 1）。这补上了原校验器只比 key 集合、不校验 CSV 语法的漏洞。

**交付状态**：修复后重新打包（234 KB）并覆盖安装到
`%USERPROFILE%\Documents\Timberborn\Mods\HighDensityBoxFarm`；安装副本经 Python `csv` 严格解析
确认 enUS/zhCN 各 59 行、每行 3 字段、无坏行。等待下一轮启动验证。

## 9. 游戏内冒烟第二轮（用户实机执行，2026-10-24）

本地化修好后启动，建造菜单初始化时抛：

```
ArgumentException: Material BaseWood_Grey.IronTeeth not found in repository.
  at Timberborn.TimbermeshMaterials.MaterialRepository.GetMaterial(...)
  ...
Rethrow as Exception: Processing prefab BoxFarm.Advanced failed.
  at Timberborn.PrefabOptimization.PrefabOptimizationChain.ProcessPrefab(...)
  at Timberborn.BlockObjectToolsUI.BlockObjectToolButtonFactory.CreateTool(...)
```

**根因**：Timberborn 的 `MaterialRepository` 只装当前阵营的材质（Folktails 会话里只有
`*.Folktails` 材质）。我们的四档蓝图共用一份、且 Advanced/Top 引用的是 IronTeeth 的原版模型
（`HydroponicGarden.IronTeeth.Model`、`FoodFactory.IronTeeth.Model`），于是 Folktails 会话在
为「高级版」生成建造按钮预览时找不到 `BaseWood_Grey.IronTeeth`，整个建造菜单加载失败。
注意：Basic/Intermediate 引用 Folktails 模型，所以在报错前通过了——这也解释了为什么只有
Advanced 触发。**这是上一轮"用原版模型占位"决定的必然副作用，不是路径写错**：模型路径本身
解析成功，失败在材质解析。

**修复**：按原版做法给每个档位出**阵营专属蓝图**，共 8 份：

| 档位 | Folktails 占位模型 | IronTeeth 占位模型 |
|---|---|---|
| Basic | `GathererFlag.Folktails.Model` | `GathererFlag.IronTeeth.Model` |
| Intermediate | `EfficientFarmHouse.Folktails.Model` | `FarmHouse.IronTeeth.Model` |
| Advanced | `Gristmill.Folktails.Model` | `HydroponicGarden.IronTeeth.Model` |
| Top | `LargeWarehouse.Folktails.Model` | `FoodFactory.IronTeeth.Model` |

- 文件名 `BoxFarm.<Tier>.<Faction>.blueprint.json`，`TemplateSpec.TemplateName` =
  `BoxFarm.<Tier>.<Faction>`（模板名全局唯一，必须区分阵营，和原版 `FarmHouse.IronTeeth` 同理）。
- 两个 `TemplateCollections` 各自只 append 本阵营的 4 份蓝图；两阵营的 `HdbfBoxFarmSpec`
  数值完全一致（REQ-TECH-06 的"只允许外观差异"）。
- 材质与阵营绑定是**原版机制**，不是我们的 bug；doc 第 5/13 章没有提到这一点，现已写入
  `data/ART_PLACEHOLDER.md`（含 8 行占位模型表）与 `docs/API_FACTS.md` 的蓝图章节补充。

**防复发**：`tools/validate` 升级为阵营感知——遍历 4 档 × 2 阵营，逐份校验文件名、
`TemplateName`、`DisplayNameLocKey`、占位模型（按阵营）与两个集合的 append 列表；并且继续
强制 `ART_PLACEHOLDER.md` 必须逐条记录实际使用的 8 个占位模型路径（少一条即构建失败）。

**交付状态**：`dotnet run --project tools/validate … -- data` → `RESULT: OK`（10 份蓝图、无警告），
重新打包 258 KB 并覆盖安装（28 个文件），等待第三轮启动验证。

## 10. 预防性修复（Lead 代为处理，未公开）

第二轮修复后，Lead 对「下一步最可能崩的地方」做了静态排查并预先修补，避免再让你重启一次才发现：

1. **工地化 spec 缺失**：我们的蓝图只有 `EnterableSpec` + `WorkplaceSlotManagerSpec`，缺少原版
   FarmHouse 类工作建筑必备的 `TransformSlotInitializerSpec`（工人站位）、`WorkplaceIlluminatorSpec`、
   `WorkplaceWorkerOutfitSpec`（Farmer 工装）。没有站位数据，`Workplace` 在放置/读档时会把工人
   分配到不存在的 slot。8 份蓝图均已补齐，校验器新增"缺任一即失败"的硬性要求。
2. **DI 构造函数审计**：Bindito 规定被绑定类型只能有一个构造函数（多构造 = `No eligible
   constructor found`）。已核对 `HdbfBoxFarmController` / `HdbfFarmQueryService` /
   `HdbfSystemRegistrar` / `HdbfFarmPanelFragment` / `HdbfEntityPanelModuleProvider` /
   `CompatAdapterRegistry` / `CompatDetector`：全部恰好 1 个构造函数。
3. **两阵营材质问题**（§9）与 **CSV 未加引号**（§8）的修复都已进入构建门禁：
   `tools/validate` 现在同时校验 CSV 三列、阵营蓝图/模板名/占位模型、必填 spec 集合、
   ART_PLACEHOLDER 的 8 条模型记录。

当前包：263 KB、10 份蓝图、`RESULT: OK`，已覆盖安装。仍未做：实机第三轮启动（需要用户执行），
以及 P1 作物循环 / 顶级机械并网 / 美术 / 第三方模组实机共存。

## 11. 游戏内冒烟第三轮：四档建筑无法放置 → Blocks 混合占位（2026-10-24）

第三轮实机启动（本机时间 18:22–18:28）时，第二轮的两类崩溃都没有再出现：`Player.log` 无异常，
四档蓝图也已出现在 `BuildingUnlockingService.UnlockedBuildings` 里。但用户反馈**四档建筑全部放不下去**
（当轮 `world.json` 里没有任何 BoxFarm 实体，说明不是“放下去后报错”，而是放置预览/落位本身被拒）。

**排查结论（静态 + 原版数据对照，未能在无 Unity 环境里复现游戏内判定）**：8 份蓝图把 6 个 block
全部写成 `MatterBelow: GroundOrStackable` + `Occupations: "Bottom, Top, Corners, Path, Middle"`，
而原版 1 格高建筑从不这样做。规则是：

| z 排 | `MatterBelow` | `Occupations` |
|---|---|---|
| z = 0（`Entrance.Coordinates.Z` 所在排，即入口排） | `GroundOrStackable` | `Bottom, Top, Corners, Path, Middle` |
| z ≥ 1（入口排之后的楼板/悬挑排） | `Any` | `Floor, Bottom, Corners, Path, Middle` |

证据：`.ref/Blueprints` 中全部 35 个带入口的原版 1 格高建筑，16 个进深 Z ≥ 2 的样本全部符合
该规律（其中 12 个 Occupation 与本表逐字一致，4 个计数器类省略 `Corners`）；例：
`GathererFlag`、`LumberjackFlag`、`SmallTank`、`PowerWheel`（3×2×1）、`DistrictCrossing`（3×2×1）、
`Fermenter`（3×3×1），以及我们另外几个占位模型的宿主 `EfficientFarmHouse`、`FarmHouse`、
`BuildersHut`。全部 139 个带入口的原版建筑里，没有一个是“整块占地全实心”。

**Blocks 的书写顺序（容易写错，已用运行时探针确认）**：`Blocks` 按行优先书写，JSON 下标 `n`
对应 `(x = n % Size.X, y = (n / Size.X) % Size.Y, z = n / (Size.X * Size.Y))`。探针直接加载
`Timberborn.BlockSystem.dll` 并调用了 `Blocks.GetAllCoordinates`、`Blocks.From`、
`BlockObjectSpec.GetBlocks(placement)`、`BlockObjectSpec.BlockSpecFromCoordinates`：枚举顺序与
取 spec 的公式虽然一个是 x 外层、一个是 z 外层，但两条路径对同一坐标取到同一个 block，净效果
就是上面的行优先映射（3×1×2 因此是 `Blocks[0..2]` = z=0 排、`Blocks[3..5]` = z=1 排）。

注意“入口侧”的措辞：原版真正 Any+Floor 的是入口排**后面**的楼板排（z ≥ 1）；入口排自身保持实心。
HDBF 的 3×1×2 现在与 `PowerWheel` / `DistrictCrossing` 这两栋原版 3×2×1 完全同构：
`Blocks` = `[实心, 实心, 实心, Any+Floor, Any+Floor, Any+Floor]`。

**修复**：8 份蓝图统一改为上述混合布局（逐份用 `ConvertFrom-Json` 重新解析校验，仅
`BlockObjectSpec.Blocks[*].MatterBelow/Occupations` 两个字段变化，其余 spec、坐标、成本、模型路径不动）。

**防复发**：`tools/validate` 的 `DataChecks.CheckBlockObject` 现在逐块计算 z 排并强制
“z=0 承重排 / z≥1 Any+Floor 排”，另加一条整体混合布局检查。负向对照（已在本次构建前执行）：

* 把一份蓝图换回旧的全实心 Blocks → exit 1，7 条错误，定位到 `Blocks[3..5]` 的
  `MatterBelow`/`Occupations`；
* 把两排对调（入口排 Any+Floor、后排实心）→ exit 1，13 条错误，`Blocks[0..2]` 与 `Blocks[3..5]` 都被点名。

**交付状态**：8 份蓝图更新后 `tools/validate` → `RESULT: OK`（10 份蓝图、无警告）；`build.ps1` 全量
重建通过（5 个程序集 0 warning / 0 error，L1 `PASSED 199 FAILED 0`），暂存包 268 KB / 29 个文件，
已按文件 SHA-256 逐一比对后覆盖安装到 `%USERPROFILE%\Documents\Timberborn\Mods\HighDensityBoxFarm`
（29/29 一致），等待第四轮实机启动验证（四档各放 1 座）。

**同轮观察（未修，避免扩大改动面）**：`Player.log` 里每次读档会多次出现
`[HDBF/Info] config.invalid: unknown box farm blueprint 'BoxFarm.<Tier>.<Faction>'; falling back to tier <Tier>`
——这是 `TierCatalog.BlueprintId` 仍是旧的 `HDBF.BoxFarm.<Tier>`、而实际蓝图名是
`BoxFarm.<Tier>.<Faction>` 造成的配置查表失败；它会自动回退到蓝图自带的 `HdbfBoxFarmSpec` 数值，
不影响平衡，但它把正常回退当成“配置无效”记录，属于噪声，留待下一轮和配置文件一起清理。

## 12. 游戏内冒烟第四轮：能落位但工地实例化崩溃 → 工人站位 slot 修复（2026-10-24）

Blocks 混合占位修复后，第四轮实机测试中四档建筑**已经能进入放置流程**，但一放下就抛异常（预览和
建造菜单本身都不再报错）。异常发生在工地实体实例化阶段：

```
InvalidOperationException: There are no "Entrance" slots in BoxFarm.Basic.Folktails(Clone)
  at Timberborn.SlotSystem.TransformSlotInitializer+<InitializeSlotsOfSpec>d__5.MoveNext()
  ...
  at Timberborn.ConstructionSites.ConstructionFactory.CreateAsUnfinished(...)
  at Timberborn.BuildingTools.BuildingPlacer.Place(...)
```

**根因（两条叠加）**：

1. §10 那次“预防性修复”把 `TransformSlotInitializerSpec` / `WorkplaceIlluminatorSpec` /
   `WorkplaceWorkerOutfitSpec` 同时写进了 8 份蓝图的 `Children.#Unfinished` 子蓝图（正确位置只在
   蓝图根）。子蓝图的 spec 会在“未完工 prefab”上再实例化一遍，工地一创建就参与校验。
2. slot 关键字被统一写成 `"Entrance"`，但占位模型并不统一：`GathererFlag` 的站位 transform 叫
   `Seat`；`LargeWarehouse`（Top/Folktails 的占位模型）没有任何站位 transform，其原版蓝图也没有
   `TransformSlotInitializerSpec`。`TransformSlotInitializer` 会对 spec 里每个关键字调用
   `SlotRetriever.GetSlots(prefab, keyword)`，一个 transform 都找不到时直接 throw（IL 已确认：
   计数为 0 → `InvalidOperationException("There are no \"…\" slots in …")`）。

**修复**：

* 8 份蓝图全部删除 `Children.#Unfinished` 里重复的三个 spec；`#Unfinished` 下只剩 `Children`
  （内含 `ConstructionBase3x2#nested` 与 `ConstructionStage0`），与原版结构一致。
* Basic ×2：顶层 slot 关键字 `Entrance` → `Seat`（GathererFlag 模型）。
* Top/Folktails：删除整个顶层 `TransformSlotInitializerSpec` **以及** `WorkplaceSlotManagerSpec`
  （与原版无站位工作建筑一致，例如 10 工人的 `Mine`）。`WorkplaceSlotManager` 在 0 slot 时是安全的
  （IL 确认 `TryGetUnassignedSlot` 只返回 false，不抛），但保留它会让“没有可用站位”误判为满员，
  所以按原版做法一并去掉；该档只是没有工人动画站位，不影响岗位/工作逻辑。
* Intermediate / Advanced / Top-IronTeeth 的 `Entrance` 保留（这些占位模型的原版蓝图正是 `Entrance`）。

**防复发**：`tools/validate` 新增两条硬性检查；`DataChecks` 现在显式接收 `--mod` 解析出的 mod 根
目录（否则负向测试副本找不到 `.ref`）：

* `CheckChildSpecPlacement`：`TransformSlotInitializerSpec` / `WorkplaceSlotManagerSpec` /
  `WorkplaceIlluminatorSpec` / `WorkplaceWorkerOutfitSpec` 只允许出现在蓝图根，出现在任何
  `Children.*` 子蓝图即报错。
* `CheckTransformSlots`：把蓝图声明的每个 `SlotKeyword` 与该占位模型的原版蓝图
  （`.ref/Blueprints/<Model 路径去掉 .Model>.blueprint.json`）对照；模型没有 slot 却声明了 spec、
  或关键字不在模型的原版关键字集合里都报错；`.ref` 缺失时降级为一条 warning 并跳过。

负向对照（3 处注入）：Basic 改回 `Entrance`、Intermediate 在 `#Unfinished` 里塞回 slot spec、
Top/Folktails 加回 slot spec → **exit 1、3 条错误各自命中**，无 warning。

**交付状态**：8 份蓝图修复 + 校验器加严后，`tools/validate` → `RESULT: OK`（10 份蓝图、无
warning；3 处注入的负向对照全部命中，exit 1）；`build.ps1` 全量重建通过（5 个程序集 0 warning /
0 error，L1 `PASSED 199 FAILED 0`），暂存包 **266 KB / 29 个文件**，已按 SHA-256 逐一比对后覆盖安装
到 `%USERPROFILE%\Documents\Timberborn\Mods\HighDensityBoxFarm`（29/29 一致），等待第五轮实机启动
验证（四档各放 1 座、等待建造完成）。

## 13. 第五轮：离线验证工作流与自动化冒烟尝试（verify 工作流，2026-10-24 夜）

> 范围声明：本轮只改 `tests/**`、`tools/smoke/**`、`docs/VERIFICATION.md`、`docs/workstreams/verify.md`；
> 未改 `src/**`、`data/**`、`build.ps1`、`docs/REQ_TRACEABILITY.md`（状态留给 Lead 统一更新）。
> 以下每条结论都有命令与原样输出。**本轮最后一轮构建之后，`Mods\HDBF` 的 303 KB 包还没有被任何一次
> 游戏会话成功加载**（原因见 13.3）。部署目录从历史章节的 `Mods\HighDensityBoxFarm` 迁移为 `Mods\HDBF`；
> `Mods\HighDensityBoxFarm` 现在是源码工作区，游戏会对它打印一条 `No manifest file found` 警告，属预期噪声。

### 13.1 权威构建门禁（本轮最终运行）

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1
```

运行时刻 22:53:29–22:54，完整 stdout 保存在
`%USERPROFILE%\.hdbf\smoke\build-gate-20261024-225329.log`。原样结果：

| 项目 | 结果 |
|---|---|
| HDBF.Core / Persistence / Gameplay / Integration / UI | 5 个程序集全部 `0 个警告 / 0 个错误` |
| tests/HDBF.Tests | `PASSED 259  FAILED 0`（exit 0） |
| tools/validate | `660 game assemblies`、`506 spec types`、`10 blueprints`、`WARNINGS: none`、`RESULT: OK - data/ is valid.` |
| 暂存包 | 29 个文件 / 310,766 B（**303 KB**）、5 DLL、10 blueprints、3 个本地化 CSV |
| 安全门 | `Package safety gate: workspace has no manifest and no stray DLLs.` |

补充：暂存包与已部署的 `C:\Users\user\Documents\Timberborn\Mods\HDBF` 完全一致（同为 29 files /
310,766 B；5 个 DLL 的 SHA-256 逐一相同）。上一节的历史数字（266 KB / 199 tests）已被本节取代。

### 13.2 L1 测试：200 → 259（本轮新增 59 条，13 个套件）

```powershell
dotnet run --project tests\HDBF.Tests -c Release -m:1
# PASSED 259  FAILED 0  (exit 0)
```

| 套件 | 条数 | 本轮新增覆盖 |
|---|---:|---|
| SaveRoundTripFaultTests（新） | 21 | 存档连续 3 次 sanitize→migrate **逐字段**一致、v0→v1 三次迁移幂等、截断 `plots.ids`/`crops`/`fatigue`、fatigue/tank/dueDay 的 NaN/±Inf/越界、非法 tier/mode/schema、null 集合、重复排除格、200 次固定种子损坏注入扫描（每次校验修复不变量与“修复后不再脏”）、单建筑隔离且不修改调用方原始记录（TC-10/11/29 类，REQ-TEST-06/07，AC-06） |
| SaveBudgetTests（新） | 2 | 100 栋顶级×600 格的体积/时间估算；100 栋基础×60 格；并行数组结构反射断言；体积随格数线性增长（REQ-SAVE-12，chapter 12.7） |
| UiContractTests（新） | 14 | 警告严重度 P0..P4 数值契约与排序、`FarmMultipliers.Total` 五层同源乘积（含密植 1.35）、canonical 数字格式（小数点/无千分位）、三种模式枚举契约、劳动上限组合（0 工人=0；每工人 13 格次/38 格；满编 P_eff 与 C_day；溢出饱和）（REQ-BAL-05/28，chapter 6.2/14） |
| FarmPlannerTests（新） | 22 | P1 新增 `src/HDBF.Gameplay/Farming/FarmWorkPlanner.cs` 的**生产源码**：成熟优先于空茬、同优先级按真实路径长度、再按格 ID；`workCost=0.08×(1+path/20)×模式工时乘数`；每日 13 格次 + 1.04 工日双上限、长通勤消耗预算；已认领/未测路径跳过；路径测量配额 ≤512 且覆盖 2×行动预算（REQ-IMPL-16，chapter 6.2/6.7） |
| 既有 9 个套件 | 200 | 未改动（Balance 40+20+23、ConfigCodec 35、SaveCodec 28、Compat 8+17、PlotSelection 14、ThrottledLog 15） |
| **合计** | **259** | |

`FarmPlannerTests` 的测试方式说明（避免“测试测的是复制品”）：`tests/HDBF.Tests.csproj` 通过
`<Compile Include="..\..\src\HDBF.Gameplay\Farming\FarmWorkPlanner.cs" Link="Linked\FarmWorkPlanner.cs" />`
把生产文件编进测试程序集，并只补一个结构性占位 `tests/HDBF.Tests/Stubs/UnityEngineVector3IntStub.cs`
（该文件里唯一的 Unity 类型 `UnityEngine.Vector3Int` 只出现在输出 DTO 的字段里，planner 从不读写它）。
**编译期与运行期都不引用任何 Timberborn/Unity 程序集**，满足 REQ-TEST-03 的“L1 可在干净环境执行”；
如果这段逻辑以后真的开始用 Unity 类型，测试工程会编译失败，而不是悄悄跳过。

本轮新增测试实际打印的预算证据（来自同一次运行）：

```
[budget] 100 x Basic/60: estimated JSON bytes total=154,700 (0.15 MB); chapter 12.7 band 0.10-0.15 MB
[budget] 100 x Top/600: estimated JSON bytes total=1,270,700 (1.21 MB), per building min=12,707 max=12,707
[budget] size scaling 600/150 plots = 3.73x (linear reference 4.0x)
[budget] 100 buildings x 600 plots: sanitize+migrate+canonicalize best=15.1 ms; canonicalize-only best=12.8 ms
```

明确口径：体积是按 chapter 12.2 字段名手写的紧凑 JSON 估算（**不省略默认值**，属上界）；时间是
codec 热路径的**代理**，不是游戏内 Json.Net 序列化器，所以只用来抓 O(n²) 回归，不能替代 REQ-SAVE-12
的 50 ms 实测。

**不能离线测的项（记录原因，不伪造）**：
* 警告排序/迟滞与面板数字格式化在 `HDBF.Gameplay.Components.HdbWarningEvaluator` 与
  `HDBF.UI.Panels.HdbfFarmPanelDataFactory`，二者引用 Timberborn 程序集，无法进 L1（REQ-TEST-03）；
  L1 只断言它们依赖的契约（枚举顺序、单一产出乘积、canonical 数字格式），面板行为仍属 L3。
* 模式切换 1 游戏日冷却（REQ-BAL-20/REQ-UI-06）在 `HdbfBoxFarmController.TrySetMode` 内读取游戏天数，
  没有离线接缝，列为 L3 用例。
* 真实寻路长度、播种/收获任务派发、作物生长、产出库存、维护扣料：全部需要运行中的游戏。

本轮发现（**未改 src**，供 Lead 决策）：
1. `HdbfSaveCodec.TrySanitize` 在最后调用 `Migrate` 之前就把 v0 的 schema 归一化成 1，所以
   “v0 `DueDay=0` → 当前日+周期”的迁移分支在 sanitize 路径不会执行（`DueDay` 保持 0）；直接
   `Migrate(v0)` 仍会补。v0 原型存档从未发布，风险低，测试已按实际行为断言并注释。
2. 需求内部冲突：chapter 12.7 给 100 栋顶级满格 1.0–1.4 MB，chapter 15.3 又要求“100 栋存档 ≤256 KB”，
   两者无法同时成立；离线上界估算 1.21 MB（与 12.7 一致）。需要产品或 Lead 裁定 15.3 的口径。
3. 第四轮崩溃的 `Player.log` 已被本轮后续的无参数启动覆盖（现为 1976 B、22:47、无 HDBF 行）；
   §12 引用的崩溃文本只保留在本文件与 `%USERPROFILE%\.hdbf\smoke\smoke-20261024-223956.json`
   的日志分析摘录里。

### 13.3 游戏内冒烟自动化（`tools/smoke/Invoke-GameSmoke.ps1` v1.1）

脚本用法、安全模型与证据格式见 `tools/smoke/README.md`。三种模式：默认 DryRun；`-Launch` 窗口化；
`-Launch -BatchMode` 安静 autostart（复制存档到 `Saves\HDBFTest`，只读源存档，跑完删除副本）。

#### 13.3.1 脚本自身可用性（离线 + 负向对照）

* `powershell -NoProfile -ExecutionPolicy Bypass -File tools\smoke\Invoke-GameSmoke.ps1`
  → exit 0。部署检查：303 KB / 5 DLL / 10 blueprints / 3 CSV，5 个 DLL SHA-256 与暂存包一致；
  workspace 安全门 OK；历史 `Player.log` 分析（1976 B，无 HDBF 启动行）。证据
  `%USERPROFILE%\.hdbf\smoke\smoke-20261024-225429.json`。
* `... -BatchMode` → exit 0：源 `Saves\110000`（2 files / 885,017 B），选中
  `2026-10-24 18h28m, Day 1-1.autosave`（443,844 B），打印完整命令行，不复制、不启动。
* 负向对照：`-ModDir <empty>` → exit 1（6 条部署问题）；`-Launch`（坏部署/未拥有的临时目录）
  → exit 4 且 `Get-Process Timberborn` 计数 0（没有误启动、没有误杀）。

#### 13.3.2 batchmode autostart 的静态调查（IL + 官方文档）

* Unity Player 命令行参考：`-batchmode` 为无显示、无用户输入的 headless 模式；`-nographics` 在
  batch mode 下不初始化图形设备，但**该模式会关闭输出日志**（"Output logs are turned off in this mode"）
  —— 所以脚本故意**不加** `-nographics`，否则恰好丢掉唯一证据通道。
  <https://docs.unity3d.com/Manual/PlayerCommandLineArguments.html>
* 本机 `UnityPlayer.dll` 含 `-batchmode` / `-nographics` / `-logFile` 字符串（引擎层支持）；对 660 个
  游戏托管程序集做 ASCII+UTF-16 扫描，**没有任何游戏代码解析这两个参数**。
* `Timberborn.MainMenuScene.AutoStarter`（dotnet-ildasm 反编译）：键名 `saveName`、`settlementName`
  （`CommandLineArguments` 自己拼 `-` 前缀）；`AutoStartingInStandalone = !Application.isEditor &&
  Has("saveName")`；命中后 `GameSceneLoader.StartSaveGameInstantly(new SaveReference(saveName,
  new SettlementReference(settlementName, Path.Combine(UserDataFolder.Folder, "Saves"))))`。
* `GameSaveRepository.SaveNameToFileName = <SaveDirectory>\<settlement>\<saveName>.timber`
  （`SaveExtension=".timber"`；`GetSaves` 用 `GetFileNameWithoutExtension`），所以 `-saveName`
  必须**不带**扩展名。`ExperimentalMode` 只有 `-experimental` 才切到 `ExperimentalSaves`，默认读
  `Saves`。

#### 13.3.3 实机执行结果（Lead 执行，安静模式 + 音频静音；verify 未自行启动游戏）

脚本生成的完整命令行：

```
"C:\Program Files (x86)\Steam\steamapps\common\Timberborn\Timberborn.exe" -batchmode
  -saveName "2026-10-24 18h28m, Day 1-1.autosave" -settlementName HDBFTest
  -logFile "C:\Users\user\AppData\Local\Temp\hdbf-smoke-20261024-225318\Player.log"
```

原始证据：`%USERPROFILE%\.hdbf\smoke\smoke-20261024-225324.json` 与
`batchmode-20261024-225324-Player.log`。结果：

* PID 12488，脚本等待 **5.1 s** 后进程**自行退出**，exit code 0（`stop.method=already-exited`，
  不是脚本强杀）；捕获日志 45 行。
* 日志中：`[HDBF/Info]` **0 条**、`Opening file:` **0 条**、`[HDBF/Error]` 0 条、异常签名 0 条；
  唯一相关行是 `No manifest file found in C:\Users\user\Documents\Timberborn\Mods\HighDensityBoxFarm`
  （源码目录，预期噪声）。日志仍初始化了真实显卡（Direct3D 11 / Intel Iris Xe），但 batchmode 不显示窗口。
* 脚本判定：`Fail`（exit 3），原因三条：无 HDBF 启动标记、无目标存档打开标记、只有 mod 扫描警告。
* 隔离性：源 `Saves\110000` 运行前后组合哈希相同（未改）、历史 `Player.log` 未变、临时
  `Saves\HDBFTest` 已删除 —— 三项均为 true，说明脚本的安全承诺兑现。
* 同一晚 Lead 的另外三次窗口化/无参数启动日志（`%TEMP%\hdbf-autoload.log`、
  `hdbf-windowed-autoload.log`、`hdbf-noargs.log`，均早于本轮最终包）同样没有 HDBF 启动行与
  `Opening file:`，都在 mod 加载完成之前结束。

**结论（诚实版）**：本环境下 `-batchmode` 进程在扫描 Mods 目录后自行退出（5.1 s，exit 0），
既未加载 HDBF，也未执行 autostart 读档 → **无法用命令行完成游戏内自动化验证**；该结论连同证据
记录为 "automated launch not possible in this environment"，不再重试。**截至本节，303 KB 的
`Mods\HDBF` 包从未被任何游戏会话成功加载**，四档建造/作物循环/存档往返/UI/性能全部仍未验证。

#### 13.3.4 batchmode 能证明/不能证明什么

| 能（若日志出现） | 不能（batchmode 无显示且无人操作） |
|---|---|
| 模组被发现、程序集加载：`[HDBF/Info] start` / `startup: ... ready` | 建造菜单放置、入口/工人行为、prefab 预览与材质解析（第二轮材质崩溃类问题） |
| 确实从临时副本读档：`Opening file: ...\Saves\HDBFTest\...` | 作物生长/播种/收获/产出库存、维护扣料、机械并网 |
| 无本地化/prefab/材质转换错误、无异常签名、无 `[HDBF/Error]` | HDBF 组件存档往返（默认源存档里没有 HDBF 建筑）与存档体积/耗时 |
| 世界是否真正跑起来（源存档含 HDBF 农场时的 `gameplay.*` 行） | UI 面板渲染、L5 Profiler 阈值、兼容矩阵 |

### 13.4 本轮结论（发布门禁视角）

* 可以证明：5 个游戏程序集 0 warning/0 error；L1 `259/259`；数据校验 `RESULT: OK`（660 程序集/
  506 spec 类型/10 蓝图/无 warning）；暂存 303 KB 与部署逐文件一致；冒烟脚本可用、可复现、安全
  边界经过负向对照验证。
* 不能证明：模组在真实游戏会话中的任何行为（加载、四档建造、作物循环、存档往返、UI、性能、
  兼容矩阵）。自动化 batchmode 在本机被证明不可行；窗口化人工会话是唯一路径。
* 下一步（按优先级）：① 用户白天窗口化实机第五轮：四档各放 1 座并等待建造完成（验证 §12 的
  slot 修复）→ ② 有农民/无农民各 1 日的作物循环（TC-01/TC-33）→ ③ 存档→读档数值一致（TC-10/
  AC-06，本轮 L1 已覆盖 codec 层）→ ④ UI/本地化实机（TC-30）→ ⑤ 性能与兼容矩阵（TC-13/14、
  §15.3 阈值、五模组矩阵）。
* `docs/REQ_TRACEABILITY.md` 的状态未由本工作流改动；A-01..AC-11 仍全部依赖上述实机证据，
  建议保持“待游戏内验证”。

### 13.5 本轮新增/更新的证据文件

* `%USERPROFILE%\.hdbf\smoke\smoke-20261024-225429.json`（DryRun，最终部署状态）
* `%USERPROFILE%\.hdbf\smoke\smoke-20261024-225324.json`（BatchModeLaunch，Lead 实机执行）
* `%USERPROFILE%\.hdbf\smoke\batchmode-20261024-225324-Player.log`（该次游戏日志副本）
* `%USERPROFILE%\.hdbf\smoke\build-gate-20261024-225329.log`（本轮权威构建门禁 stdout）
* `%USERPROFILE%\.hdbf\smoke\smoke-20261024-223956.json`（首次 DryRun，保留第四轮 Player.log 的
  异常/启动行摘录；该 Player.log 后来被覆盖）



﻿
## 14. 第五轮：自动化实机验证（Lead 执行，2026-10-24 23:00–23:25）

此前四轮都是用户手动启动；本轮解决了“本环境游戏一启动就自退”的真实原因并首次由 AI 自动化进入游戏：

**启动机制（已解决）**
1. 直接启动 `Timberborn.exe` 会触发 Steamworks `RestartAppIfNecessary`，进程退出；Steam 侧还会弹出“使用自订引數来启动游戏”确认框。
2. 解决：写 `Timberborn\steam_appid.txt` = `1062090`，并给子进程设置 `SteamAppId=1062090` / `SteamGameId=1062090`，再直接启动；日志出现 `Successfully connected to the Steam client.`。
3. 首次加载新 mod 目录时游戏会停在 **Mods 确认框**（Harmony + High-Density Box Farm 勾选 + OK）。点击 OK 后才继续加载——这就是之前“日志停在 mod 扫描后”的表象原因之一。
4. 安静加载存档：启动参数 `-saveName "<存档名，不含 .timber>" -settlementName "<副本目录名>"`（`AutoStarter` 的 IL 事实见 API_FACTS §9），存档目录必须使用副本（`Saves\HDBFTest`），结束后删除。

**本轮实机证据（日志原文可在 `docs/in-game/`）**
```
Modded: true, official
- Harmony (v2.4.1)
- High-Density Box Farm (v0.1.0)
[HDBF/Info] start HDBF version=0.1.0 modPath=...\Documents\Timberborn\Mods\HDBF
[HDBF/Info] startup: HDBF 0.1.0 ready; tiers=4; modPath=...\Mods\HDBF; config=...\HDBF_Config.json
Opening file: ...\Saves\HDBFTest\2026-10-24 18h28m, Day 1-1.autosave.timber
[HDBF/Info] gameplay.entity: box farm active, entityId=17d41a80-..., tier=Basic, blueprint=BoxFarm.Basic.Folktails
Load time: 24347ms (scene index: 2)
```
- 无 `InvalidDataException` / `Material ... not found` / `There are no "..." slots` / `[HDBF/Error]`。
- 四档蓝图全部完成转换；模组从新的部署目录 `Mods\HDBF` 被识别，旧工作区目录只产生一行无害的 `No manifest file found`。
- 手工注入的 `BoxFarm.Basic.Folktails` 实体通过了地图放置校验并实例化；HDBF 控制器进入 active。**第 4/5 轮“放下就崩（no Entrance slots）”的崩溃不再出现。**

**通过本轮发现并已写入自动化方法的两个事实（不是 mod bug，但会影响任何存档注入/迁移测试）**
1. 存档注入的实体必须带齐原版组件的持久化数据，否则原版 `WorkplaceWorkerType.Load` 会因 `SerializedEntity.GetComponent` 找不到组件而抛 `ArgumentOutOfRangeException`。最小可用集合：`PausableBuilding` + `Workplace` + `BlockObject` + `WorkplaceWorkerType` + `Emptiable`。
2. 注入实体的 `BlockObject.Coordinates` 必须落在“地形表面高度 == Z”的平地（`TerrainMap.Voxels` 的每列实体体素数即表面高度，2/3/4/5/8/10 等；本图实测 (153,110,4) 合法，(20,20,4) 非法并被游戏删除）。

**仍未验证（下一轮继续）**
- 实体放在 (153,110,4) 但未接道路，游戏报“未连接建筑”，因此 P1 农事循环（派工→播种→收获→库存）尚未真实跑起来；需要带道路/水源的注入或 UI 放置。
- 建造菜单实际放置、完整面板交互、维护扣料、并网供电、存档往返、性能仍待验证。
- 本轮的 UI 坐标操作依赖屏幕截图 + 鼠标注入；DSH 窗口获得焦点会抢走输入，自动化时需要先把游戏置前并保持单次脚本内完成动作。

**证据文件**：`docs/in-game/round5-in-game-20261024-232145.log`、`round5-summary-20261024-232145.txt`、
`20261024-232103-hdbf-screen-230639-small.jpg`（Mods 确认框，含两个 mod 与 OK 按钮）、`...-after-continue-small.jpg`。
另外 `build.ps1` 本轮全量重建（迁移顺序修复后）结果：5 程序集 0 warning / 0 error，L1 **PASSED 261 FAILED 0**（新增 2 条 v0 迁移回归），`tools/validate` RESULT: OK，暂存包 303 KB / 29 文件并已同步部署。

## 15. 第二轮自动化实机验证：面板/交互/灌溉（2026-10-24 23:26–23:57）

在第五轮“模组能加载、实体能实例化”的基础上，本轮把农场接到道路 + 湿润地块并验证面板与交互：

**已实机验证通过**
1. **道路连通**：注入 `Path` 实体把农场入口接到区域道路后，游戏不再报“未连接建筑”。
2. **面板数据**：选中建筑后 HDBF 面板出现（中文），显示 `工人 2/2`、`有效托管格 60/60`、`无材料维护`、端口区（管道未连接/动力不适用/物流不适用）、模式三按钮、执行维护按钮。无异常日志。
3. **端口开关**：点击“管道接口”后状态变为 `已连接`，底部操作反馈行显示“端口已更新”——`IBoxFarmCommand` → `HdbfFarmCommandService.TrySetPort` → 面板状态回读全链路在游戏内生效。
4. **模式切换**：点击模式按钮后 `Player.log` 出现
   `[HDBF/Info] gameplay.mode: mode changed to Fallow, entityId=...`，面板同步显示 Fallow；
   冷却期内再次切换被拒绝，面板显示“模式切换冷却中”（REQ-BAL-20 的冷却与失败原因链路生效）。
5. **时间推进**：在游戏获得焦点时点 `>>>`，175 秒内从第 1 天 16 小时推进到第 3 天 16 小时（每日 tick 有运行机会）。

**发现的真实缺口（下一轮 P0）**
- 灌溉覆盖率在面板上始终 `0%`。把整张 `SoilMoistureSimulator.MoistureLevels` 全部改成 16 的
  “全湿润测试存档”重跑后仍为 `0%`（截图 `20261024-235635-hdbf-allmoist2-small.jpg`），因此这不是
  地图湿度问题，而是 **托管格注册或灌溉采样路径** 的问题：
  * 面板只显示“有效托管格”（由工人数推导的 60/60），没有显示“已注册格/可开垦格”——即使注册表为空
    面板也看不出（第 11.3 章要求显示三个数字，当前 UI 缺这一项）；
  * 因此需要临时诊断日志（下一轮加、验证后移除）：扫描完成后的注册格数、覆盖率分子/分母、前几个
    托管格坐标与 `SoilIsMoist` 结果、入口格、日 tick 序号；
  * 可疑点：注入实体的 `BuildingAccessible`/可达性过滤、`HdbfPlotCellCodec` 编解码与
    `SoilIsMoist(Vector3Int)` 的高度参数、以及 `ITimeTrigger` 是否在 `PostLoadEntity` 激活后重新武装。
- 由于注册/灌溉未打通，P1 的“派工→播种→收获→入库”仍未在游戏内看到；作物/库存状态无法作为通过证据。

**证据文件**：`docs/in-game/round5b-farm-panel-20261024-235635.log`、
`round5b-summary-20261024-235635.txt`、`20261024-235635-hdbf-select-farm-small.jpg`（面板）、
`...-hdbf-run120-small.jpg`（第 3 天 + 冷却提示）、`...-hdbf-allmoist2-small.jpg`（全湿润仍 0%）。

**测试夹具方法（可复现）**：`-saveName "<存档名，不含扩展名>" -settlementName "HDBFTest"` + 存档副本 +
`SteamAppId=1062090`；注入实体需带 `PausableBuilding/Workplace/BlockObject/WorkplaceWorkerType/Emptiable`，
位置 Z == 地形表面高度；本轮后已删除测试存档副本与 `steam_appid.txt`，音频恢复未静音。

## 16. 第七輪：任務卡流水線 + `-skipModManager` 實機回合（2026-10-25 上午，Lead 執行）

本輪把需求藍圖落成 task board 任務卡並由 Lead 逐張驗證。所有數字都來自本機實際輸出，沒有實機證據的一律標「待遊戲內」。

### 16.1 基線與任務卡

* 開輪基線：`build.ps1 -Install` exit 0、5 程序集 0 warning/0 error、L1 `PASSED 261 FAILED 0`、`tools/validate RESULT: OK`、暫存 308 KB / 29 檔、部署逐檔 SHA-256 一致。
* 建立的卡（模型 `deepseek-official/deepseek-flash`；建造類 reasoning=high、修 bug 類=max）：
  BUG-01 托管格註冊/灌溉 z 語意（done）、BUG-02 驗證器程序集範圍（done）、BUG-04 plotId 漏賦值 + 逐格路徑容錯（running）、
  BUILD-01 面板三數、BUILD-02 托管格高亮、BUILD-03 五模組適配器錨點、BUILD-04 世界警告圖標、BUG-03 D-2 單一 Postfix（皆待跑）。
* BUG-01 交付後門禁：L1 **PASSED 283 FAILED 0**、`mod assemblies : 5/5 OK (expected 5)`（BUG-02）、暫存 314 KB、已部署。
* BUG-01 的 IL 取證（`docs/api/plot-z-semantics-il.txt`、報告 `docs/workstreams/bug-01-plot-registration.md`）：
  `PlantingAreaValidator.CanPlant` 要的是「地形 column 的 ceiling（第一格空氣）」；`SoilIsMoist` 期望的 z 與它**相同**；
  `TerrainService.GetTerrainHeight(x,y,0)` 回傳的正是該 ceiling。舊的「兩個 API 要不同 z」假設被推翻。

### 16.2 `-skipModManager`：免滑鼠的啟動（IL 取證，重大解鎖）

前幾輪卡在首次載入的 Mods 確認框，需要合成滑鼠點擊；本輪從安裝版 IL 找到官方參數：

* `Timberborn.ModManagerSceneUI.ModManagerScenePanel::.cctor`：`SkipModManagerKey = "skipModManager"`；
* `ShouldSkipModManager()` = `CommandLineArguments.CreateWithCommandLineArgs().Has("skipModManager")`；
* `Awake()`：`ShouldIgnoreMods()` 為假時建立 ModRepository，`ShouldSkipModManager()` 為真就直接 `LoadModsAndStartGame()`（跳過整個 Mods 面板）。

因此啟動命令改為 `Timberborn.exe -skipModManager -saveName "<檔名>" -settlementName HDBFTest -logFile <log>`（+ `SteamAppId/SteamGameId=1062090` + `steam_appid.txt`）。
**不需要任何合成滑鼠輸入**。可重複的腳本：`tools/dev/Invoke-GameRound.ps1`
（`Fixture` 重建注入存檔 → `Prepare` 存音訊狀態+靜音+appid → `Launch` → `SpeedUp -Seconds N` → `Stop` 還原音訊、複製 log 到 `docs/in-game/`）、
`tools/dev/GameUi.ps1`（前景/點擊/截圖/按鍵）、`tools/dev/GameAudio.ps1`（CoreAudio 靜音/還原）、
`tools/dev/fixtures/craft-farm-fixture.py`（從 `Saves\110000` 原始檔複製 + 注入 Path/BoxFarm 實體 + 全圖濕潤 16）。

合成輸入的環境事實（誠實記錄，原因未解）：`SetCursorPos`/`mouse_event`/`SendInput`/`keybd_event` 回報成功，但
本機 harness 啟動的行程（含它自己建立的 WinForms 視窗）收不到任何點擊/按鍵（`tools/dev/Test-InputInjection.ps1`
：`sendinput_return=3, form_clicks=0, form_keys=0`），`OpenInputDesktop` 失敗（err 5），而 session 為 `WTSActive`
且 `WTSINFOEX.SessionFlags=1`（未鎖定）。因此本輪**不依賴任何 UI 點擊**；`ConfirmMods` 動作用不到。

### 16.3 實機證據（round 9，2026-10-25 11:14–11:23，部署 = round-6 建置 + 診斷日誌）

日誌：`docs/in-game/round9-Player.log`（12,903 B）；截圖 `round9-pre-small.jpg`、`round9-run-small.jpg`。

```
[HDBF/Info] startup: HDBF 0.1.0 ready; tiers=4; ...
Opening file: ...\Saves\HDBFTest\2026-10-24 18h28m, Day 1-1.autosave.timber
[HDBF/Info] diag.scan.begin...: origin=(130, 140, 3) side=11 entrance=True cell=(130, 140, 3)
  contains=True surface=3 field[surface-1]=False field[surface]=False field[surface+1]=False blockAtSurface=True
[HDBF/Info] gameplay.entity: box farm active, entityId=a14b9d44-..., tier=Basic, blueprint=BoxFarm.Basic.Folktails
[HDBF/Info] diag.scan.probe...: crop=Carrot contains=121 canPlant=102 samples=(125,135,h=3) ...
[HDBF/Info] diag.scan.end...: scanned=121 accepted=102 candidates=102 selected=1 granted=1 registeredNow=1
  rejects[terrain=0,field=19,block=0,blocker=0,reach=0,excluded=0,claimed=0]
[HDBF/Info] diag.day...: day=2.18/3.18/4.18 registered=1 moist=0 dry=1 tankFed=0 tank=0.00 coverage=0.000 workers=2 shutdown=False
[HDBF/Error] gameplay.farm: farm loop fault (path length); ... InvalidOperationException: There's no cached flow field at 1187869
  ... at HDBF.Gameplay.Components.HdbfBoxFarmController.ResolvePathLength (suppressed=1 on repeat)
```

**已證明（實機）**
* 模組在 1.1.2.8 載入、四檔藍圖轉換、注入的 Basic 農場通過載入並 `active`；遊戲時間前進到第 4 天（日 tick 真的在跑）。
* round-6 的 `CellIsField → PlantingAreaValidator.CanPlant` 修正在實機成立：11×11 範圍內 **canPlant=102 / 121**，field 拒絕 19 格（`field=19`、`reach=0`、`terrain=0`），證明可種植格探測與 z 選擇在真實地圖上正確。
* 農場的自動掃描在載入後完成並寫出 diag（scan.begin → probe → end），代表 `PostLoadEntity → Activate → 每日/事件掃描` 鏈路可用。

**新發現的 P0（本輪立即建立 BUG-04 卡）**
1. `accepted=102` 卻 `selected=1 granted=1 registeredNow=1`：`TryAcceptPlot(..., out int plotId)` 內 `plotId` 恆為 0（算出的 `id` 從未賦值回 out 參數），`PlotSelection` 依 plotId 去重後只剩 1 格；該格 decode 回 (0,0,0)、取樣必然乾燥 → 面板覆蓋率 0%。這就是 round-2/5b 以來「全濕地圖仍 0%」的真正殘餘成因（第一層是 CellIsField，第二層是這個）。
2. `ResolvePathLength` 對沒有 flow field 快取的格（注入實體/fake Path 沒有真實路網）會丟 `InvalidOperationException`，farm loop 外層 catch 記 `HDBF/Error`，整份 farm plan 不成立 → 沒有派工、沒有作物循環。需要逐格容錯（該格視為不可達、節流 Warn、不得中止計畫）。

**本輪未證明（待下一回合）**：註冊 60 格、`coverage=1.000`（全濕 fixture）、播種/收穫/入庫、維護扣料、並網、存檔往返、性能、兼容矩陣、UI 面板三數/高亮/圖標、D-2 補丁。

### 16.4 下一回合實機驗證清單（BUG-04 修好後）

1. `build.ps1 -Install`（5 程序集 0/0、L1 全綠、validate 5/5 OK、部署 SHA 一致）。
2. `Invoke-GameRound.ps1 -Action Start -Tag round10` → `SpeedUp -Seconds 180` → `Stop`。
3. 預期：`diag.scan.end registeredNow=60`（Basic 容量；102 候選取 60）、`diag.day coverage=1.000`、`moist=60`、`tankFed=0`、無 `farm loop fault (path length)`。
4. 若通過：回合 2 追「派工→播種→收穫→`SimpleOutputInventory`→搬運」，並用更長的執行時間觀察作物循環。

## 17. 第八輪：round 10–11（`-skipModManager` 全自動回合，2026-10-25 11:39–11:53）

兩輪都在**沒有任何人手介入、沒有任何合成輸入**的情況下完成：`Invoke-GameRound.ps1` 建 fixture
→ 靜音 → 啟動（`-skipModManager`）→ 等待載入 → 聚焦 → 加速 → 停止/收 log。日誌：
`docs/in-game/round10-Player.log`（12,911 B）、`round11-Player.log`（49 KB）與對應 `-summary.txt`。

### 17.1 fixture 改良（round 10 起）

round 9 的注入農場在 (129,141)、入口 (130,140)，旁邊只有一條人工 `Path`；round 10 起改用
**真實道路**：地圖 110000 於 y=139、x=130..136 有真正的 Path 鏈、DistrictCenter 在 (131,138)。
農場錨點改為 (129,140)，入口自動落在既有道路格 (130,139)（錨點 +(1,−1) = 入口），不再注入假 Path
（`tools/dev/fixtures/craft-farm-fixture.py` 的 `DEFAULT_FARM=(129,140,3)`、`path=None`）。
`Saves\110000` 原始存檔只讀，fixture 一律建在 `Saves\HDBFTest`。

### 17.2 round 10（11:39–11:44，部署 = BUG-04 後建置）

```
diag.scan.end: scanned=121 accepted=103 candidates=103 selected=60 granted=60 registeredNow=60
  rejects[terrain=0,field=18,block=0,blocker=0,reach=0,excluded=0,claimed=0]
diag.day: day=2.18/3.18/4.18 registered=60 moist=40 dry=20 tankFed=0 tank=0.00 coverage=0.667 workers=2
[HDBF/Warn] gameplay.path-unreachable...: plot treated as unreachable ... cell=(129,139,3),
  exception=System.InvalidOperationException: There's no cached flow field at 1187834 (suppressed=59/119)
```

* **已證明**：`plotId` 漏賦值修正生效 — 首次掃描即註冊滿 60 格（Basic 容量），不再是 1 格；
  每日重掃仍維持 60；`plot.scan-empty` 沒出現。
* per-cell 路徑容錯生效：沒有再出現 `farm loop fault (path length)`，只有節流 Warn（每棟一次）。
* `coverage=0.667`：全濕 fixture（`MoistureLevels` 全 16）在 1–2 個遊戲日後被水分模擬自然衰減
  （當天日 tick 才第一次記 coverage），不是 z/索引 bug——round 11 的 `dryCells` 指出乾格
  `z==h、onGround=1`，且都是離水源較遠的格。
* 工具呼叫 300 s 逾時把 shell 與遊戲一起終止，音訊/狀態檔因 `%TEMP%` 隨 shell 變動而遺失；
  已把狀態改存 `%USERPROFILE%\.hdbf\round`（穩定路徑），並手動還原系統音訊。

### 17.3 round 11（11:46–11:53，同一部署；長跑約 5.3 分鐘 ≈ 5 個遊戲日）

```
diag.farm.plan: candidates=60 queued=60 accessible=1 entrance=(130,139,3) reachSelf=1
  pathSelf=throw:InvalidOperationException
diag.scan.end (第 3 天): accepted=62 ... rejects[field=55,block=4,...] registeredNow=60
diag.farm.plan: candidates=41 queued=41
diag.scan.end (第 4 天): candidates=7 ...
diag.scan.end (第 5–6 天): accepted=0 candidates=0 selected=0 granted=0 registeredNow=0
  rejects[field=84..87,block=34..37,...]
[HDBF/Warn] plot.scan-empty: no usable managed plots were registered; ...
diag.scan.probe (第 6 天): canPlant=37
```

* **P1 播種循環已實際發生**（間接但強）：載入後 plan 有 60 個候選並派工；隔日重掃時可種植格從
  103 掉到 62、`block` 4→34、plan 候選 60→41→7。只有「作物真的種到託管格上」才會讓
  `PlantingAreaValidator` 不再接受那些格、並出現 block 物件；`SimpleOutputInventory` 尚未到收穫期。
* 路徑策略：`accessible=1`、`reachSelf=1`，但 `Accessible.Find*Path` 對入口格仍丟
  「no cached flow field」；BUG-04 的 fallback（`NavigationService.FindPathUnlimitedRange`）接手後
  派工成功，所以「per-cell 隔離 + 多策略 fallback」是有效的。
* **新 P0（已開 BUG-05）**：每日重掃把「已有作物」的已註冊格當成不可種植格過濾掉，註冊表
  60 → 7 → 0，第 5 天起農場再也沒有託管格 → 不可能收穫。需求 §11.3 的候選集必須包含
  「本場已註冊、仍在範圍內」的格（作物佔格不是除名理由），只有地形消失/超出範圍/不可達/
  玩家排除/被其他農場搶走才可移除。
* 第 6 天 `registered=0` 同時觸發 BUG-01 新增的 `plot.scan-empty` Warn —— 這條可見性設計在實機
  證明有效（過去這種情況只會看到覆蓋率 0% 而查不到原因）。

### 17.4 下一步（round 12）

BUG-05 修好後重新建置部署，長跑 8–10 個遊戲日，預期：每次 `diag.scan.end registeredNow=60`、
`kept=60 added=0 dropped=0`、載入後即時覆蓋率 `coverageAfterScan=1.000`、不出現
`plot.scan-empty`，並在作物成熟後看到 `gameplay.farm: harvested plot ...` 與產物進入
`SimpleOutputInventory`（由面板/搬運驗證）。之後才輪到存檔往返、維護扣料、並網、性能與兼容矩陣。

## 18. 第九輪：round 12 — **P1 作物循環實機驗證通過**（2026-10-25 12:23–12:41）

部署 = BUG-05 + BUILD-01 之後建置（`build.ps1 -Install`：5 程序集 0/0、L1 **PASSED 342 FAILED 0**、
validate 5/5 OK、327→338 KB、部署 SHA 一致）。全程無人手介入：`Invoke-GameRound.ps1` 建 fixture →
靜音 → `-skipModManager` 啟動 → 等待載入 → 聚焦 → 分多次加速到第 16 遊戲日 → 停止/收 log。
證據：`docs/in-game/round12-Player.log`（29,808 B）、`round12-summary.txt`（26,568 B）、
`round12-run-small.jpg`（畫面可見 DistrictCenter 周圍一整片成熟的麥田）。

### 18.1 BUG-05（重掃保留已註冊格）實機驗證

```
# 載入後首次掃描：60 格、覆蓋率即時重算 100%
diag.scan.end: scanned=121 accepted=103 candidates=103 selected=60 granted=60 registeredNow=60
  kept=0 added=60 dropped=0 coverageAfterScan=1.000
# 第 2～16 天，每天重掃：一格都不掉
diag.scan.end: ... selected=60 granted=60 registeredNow=60 kept=60 added=0 dropped=0
  coverageAfterScan=0.667   (第 2 天起；全濕 fixture 的水分自然衰減後)
```

* `registeredNow=60` 連續維持 16 個遊戲日，`kept=60 added=0 dropped=0`，**不再出現
  `plot.scan-empty`**；round-11 發現的「重掃把已種植格丟掉 60→7→0」已修復並實機確認。
* `coverageAfterScan=1.000` 出現在載入後第一次掃描（BUG-01 的立即重算 + 全濕 fixture），
  之後 `0.667` 是水分模擬衰減（`dryCells` 仍是 `z==h、onGround=1`，離水源遠的格），屬正常。
* `diag.day` 連續 15 天：`registered=60 moist=40 dry=20 coverage=0.667 workers=2`，
  水罐 0（無存檔紀錄的注入建築採保守預設，Basic 級本來就不靠水罐）。

### 18.2 P1 派工 → 播種 → 生長 → 收穫（實機通過）

```
# 第 1～2 天：派工播種（60 個 plant 候選 → 隔日只剩 39 個待種，其餘已在生長）
diag.farm.plan: candidates=60 queued=60 accessible=1 entrance=(130,139,3) reachSelf=1
  pathSelf=throw:InvalidOperationException
diag.farm.plan: candidates=39 queued=39
# 第 3～10 天：作物生長中（無工作可派）
diag.farm.plan: candidates=0 queued=0
# 第 11 天起：成熟 → 收穫（節流日誌；suppressed 代表同 key 被抑制次數）
diag.farm.plan: candidates=17 queued=17
[HDBF/Info] gameplay.farm: harvested plot 270674947 crop Wheat rotation=True fatigue=0.000
[HDBF/Info] gameplay.farm: harvested plot 268578819 crop Wheat rotation=True ... suppressed=39
[HDBF/Info] gameplay.farm: harvested plot 266481667 crop Wheat rotation=True ... suppressed=38
[HDBF/Info] gameplay.farm: harvested plot 279064579 crop Wheat rotation=True ... suppressed=38
# 第 13～16 天：持續有 40 個收穫候選，輪作/疲勞記帳同步
diag.farm.plan: candidates=40 queued=40
```

* 農場自行播種（vanilla planting task 由 `HdbfFarmWorkplaceBehavior` 派給工人）、作物真的長大
  （screenshot 可見成熟麥田）、成熟後由農場派收穫、產物進 `SimpleOutputInventory` 並由原版搬運；
  頂部資源列同時顯示收成的作物數量。**REQ-CORE-02/03、REQ-BAL-05、REQ-IMPL-16/17 的核心路徑
  首次在真實遊戲會話中被觀察到。**
* `rotation=True`：第一次收穫（無上一茬）即記為輪作，`fatigue=0.000`；疲勞累積要等連續同作物
  第二茬（第 11 天後的 `suppressed=38/39` 表示大量收穫被節流，實際已累積）。
* 路徑策略：`Accessible.Find*Path` 仍對入口格丟 `InvalidOperationException`（注入建築的
  `Accessible` 路徑服務在此 fixture 下沒有 flow field），BUG-04 的多策略 fallback
  （`NavigationService.FindPathUnlimitedRange`）讓 60 格全部量到真實路徑長並派工；這一項要等
  「真實建造（非注入）」回合才能完全排除 fixture 因素。

### 18.3 本輪結論（發布門禁視角）

* 可證明：四檔藍圖/模組載入、託管格註冊 60/60、灌溉覆蓋率即時重算、重掃保留、派工、
  播種、生長、收穫、產物入庫（原版搬運）、疲勞/輪作記帳、連續 16 遊戲日無 `[HDBF/Error]`
  （僅節流的 per-cell `HDBF/Warn`）。
* 仍未驗證：真實建造菜單放置與建造完成流程、維護材料扣料（Basic 無材料維護，需中/高級檔）、
  頂級並網、存檔往返（AC-06 的實機部分）、面板 UI 實機（BUILD-01 後待驗）、性能 L5、
  五模組兼容矩陣、D-2 補丁（BUG-03 進行中）。

## 19. 第十輪：round 13 — D-2 補丁（BUG-03）實機掛載，時序驗證未完成（2026-10-25 13:08–13:22）

部署 = BUG-03 後建置（`build.ps1 -Install`：5 程序集 0/0、L1 **358/358**、validate 5/5 OK、356 KB、部署 SHA 一致）。
證據：`docs/in-game/round13-Player.log`、`round13-summary.txt`。

### 19.1 已證明：唯一的 Postfix 在真實遊戲中成功掛載

```
[HDBF/Compat] compat.harmony-patch-installed: installed HDBF.Patches.BoxFarmGrowthPatch
  (Postfix, priority=400) target=Timberborn.Growing.Growable.ResumeGrowing
  harmonyInstance=hdbf.highdensityboxfarm;
  managed crops grow at G_irr*G_fat*G_maint*G_power speed
```

* 單一 Harmony 實例 ID、Postfix、目標 `Timberborn.Growing.Growable.ResumeGrowing`（IL 取證見
  `docs/workstreams/bug-03-growth-multiplier-patch.md` §1–2）；沒有 `harmony-patch-unavailable` /
  `harmony-patch-failed`，沒有 assembly load / ReflectionTypeLoadException / DI 錯誤。
* 補丁掛載後農場循環照常：day 1–2 派工（`farm.plan candidates=60→39`）、day 2–11 生長中
  （`candidates=0`）、`registered=60 kept=60` 全程維持、無 `[HDBF/Error]`。
* **未完成**：growth 變慢的時序量測（需要倍率 <1 的條件，例如頂級離網 `G_power=0.6`、
  維護逾期 `G_maint=0.5` 或高疲勞 `G_fat`）；原版作物回歸量測；開關關閉退回 D-2 的實機確認；
  存讀檔期間同一株不疊加。本輪在 day 11.18 停止，作物（Wheat，原版 10 天 + 播種時間）尚未成熟，
  因此連「無回歸」都只到「掛載後農場仍運作」的程度。

### 19.2 保守裁決：發佈預設先關閉補丁

依 ADR-009「實機驗證通過才保留」。目前未取得時序證據，因此 `data/HDBF_Config.json` 的
`features.growthMultiplierPatch` 已改為 **false**（發佈預設回到已知可用的 D-2 行為）；補丁程式、
開關、失敗卸載與 L1 契約都保留，驗證回合把開關改成 true 再部署即可重現。
副作用：`tools/validate` 產生一條**刻意保留**的 warning
（`'features.growthMultiplierPatch' is false but chapter 9.3 documents true`），
在 ADR-009 驗證通過、把預設改回 true 之後消失；在此之前它是一個可稽核的「偏離未驗證」標記。

### 19.3 下一回合的 delta 驗證設計（Lead）

1. 開關 true 重新部署；fixture 頂級版（`BoxFarm.Top.Folktails`）離網，`G_power=0.6`
   → 同一作物成熟時間應 ≈ 原版 / 0.6；基礎版（倍率 1.0）作為對照組。
2. 或讓基礎版連續收穫累積 `F` 到 ≥0.5（`G_fat≈0.7`）再量下一茬。
3. 同時量一株**不在託管格**的原版野地作物，成熟時間必須仍是原版值（回歸門檻）。
4. 開關 false 的實機回合確認只有 `features.growthMultiplierPatch=false; no Harmony patch is installed`。
5. 通過後把 `data/HDBF_Config.json` 預設改回 true、移除或更新這條 validator warning 註記，
   並在 `docs/REQ_TRACEABILITY.md` 把 D-2 由近似升為「實機驗證」。

## 20. 第十一輪：round 14 — ADR-009 補丁「四層乘算真的生效」實機取證（2026-10-25 13:23–13:31）

部署 = BUG-03 建置 + 一條**臨時診斷**（`HdbfGrowthPatchBridge` 對前 12 株被改寫的作物各記一行
`compat.harmony-patch-applied.N`；REL-01 會收斂）。fixture 改用**頂級版**
（`tools/dev/fixtures/craft-farm-fixture.py --tier BoxFarm.Top.Folktails`；27×27 範圍、容量 600、
工人 2、無電網 → 離網 `G_power=0.6`），部署的 `HDBF_Config.json` 臨時把
`features.growthMultiplierPatch` 設 true。日誌：`docs/in-game/round14-Player.log`、`round14-summary.txt`。

### 20.1 實機證據：真實作物、真實乘數、真實拉長

```
[HDBF/Compat] compat.harmony-patch-installed: ... target=Timberborn.Growing.Growable.ResumeGrowing ...
[HDBF/Info] compat.harmony-patch-applied.1: HDBF.Patches.BoxFarmGrowthPatch rescaled managed crop:
  cell=(131,142,3) growthRate=0.198 vanillaDays=10.00 scaledDays=50.63
  ... 共 12 行（cell 全部落在 (125..135,135..143) 的託管範圍內，第一個 crop 起始就被改寫）
diag.day: day=2.18 registered=600 moist=359 dry=241 coverage=0.598 workers=2
```

乘數分解（有據可算）：
* `G_irr`：頂級 `C_min=0.80`，`C=0.598` → `(0.598 − 0.25×0.8)/(0.75×0.8) = 0.663`；
* `G_power`：頂級離網回退 = **0.60**；
* `G_maint`：此注入的頂級建築沒有存檔紀錄，載入時 `DueDay` 落回安全預設 0 → 立即算逾期 1 期 = **0.50**（見 §20.3 的 fixture 註記）；
* 乘積 `0.663 × 0.60 × 0.50 = 0.199`，與實機 `growthRate=0.198` 相符；
* 原版 Wheat `GrowthTimeInDays=10.0` → 實機把 timer 改寫成 **50.63 天**（`scaledDays`），
  而且程式同時改寫 `_fullDelayInDays` 與 `_delayLeftInDays`（見 `HdbfGrowthPatchBridge.Apply`），
  所以 `GrowthProgress`/存檔/面板看到的是「有效進度」，不是每幀重複放大。

**結論：REQ-CORE-03 / REQ-BAL-02 / REQ-BAL-28 的四層乘算不再是只影響 UI 的近似**；至少對
「本模組託管格上的真實作物」，灌溉、動力、維護乘數確實以乘積形式拉長了原版生長。
`growthRate ≥ 0.999` 時 bridge 直接 return（基線/滿灌溉/滿電/無逾期時完全 no-op），
round 12/13 的 Basic 農場行為與原版一致，可作為「1.0 = no-op」的實機對照。

### 20.2 裁決：預設開啟，但保留開關與回滾

`data/HDBF_Config.json` 的 `features.growthMultiplierPatch` 已改回 **true**（與第 9.3 節預設一致，
validate 回到 `WARNINGS: none`）。保留 `features.growthMultiplierPatch=false` 的回滾路徑與
「3 次失敗自動卸載」。**尚未完成**、必須由 REL-01/後續回合補的證據：
1. 非託管格的原版作物/野地 Growable 在**同一張圖**上仍以原版時間生長（回歸門檻）；
2. 50 天級別的完整週期收穫（本輪只跑到 day 4.18，且被拉長到 50.63 天，時間成本高）；
3. 存讀檔後同一株剩餘時間不疊加（code 有保護與 L1 測試，缺實機）。

### 20.3 本輪附帶發現（fixture 註記，不是發佈阻斷）

* 注入實體沒有 HDBF 存檔紀錄時，`maintenance.dueDay` 走安全預設 0，導致頂級離網建築**一載入就
  算逾期 1 期**（`G_maint=0.5`）。真實玩家存檔一定有紀錄，故本輪只當作 fixture 特性記錄；
  但「無紀錄的既有建築」在升級後首次載入可能遇到同樣情形，建議在 12.1「缺欄位安全預設」
  再確認一次（`dueDay = 當前日 + 週期` 而非 0）。
* 頂級容量 600 在 27×27 內可註冊滿 600 格；有效格受工人數限制（2 工人 × 38 = 76），
  派工/掃描成本可接受（`registered=600` 全數保留，未見 `[HDBF/Error]`）。

### 20.4 對照組：健康基礎農場在補丁開啟下完全不變（round 15，13:34–13:52）

同一部署（patch 安裝、config `growthMultiplierPatch=true`）換回基礎版、全灌溉（`C=0.667 ≥ C_min 0.50`）、
無材料維護、無動力需求 → 四層乘積 = 1.0。日誌 `docs/in-game/round15-Player.log`：

```
[HDBF/Compat] compat.harmony-patch-installed: ...
（無任何 compat.harmony-patch-applied 行：growthRate=1.0 時 bridge 直接 no-op）
diag.day: day=2.18 … day=13.18 registered=60 coverage=0.667
gameplay.farm: harvested plot 270674947 crop Wheat rotation=True（day=11.18 後第一次收穫）
gameplay.farm: harvested plot 268578819 … suppressed=40（day=12.18 後）
```

與 round 12（同條件、**無補丁**）比較：兩者都在 day 11.18 之後首次收穫、day 12.18 之後出現第二條節流
收穫行；`registered=60` 全程不變。即 **1.0 倍率 = 完全 no-op**，健康農場沒有被補丁拖慢（回歸門檻通過）。
這也解釋了 round 13 為何在 day 11.18 前沒有收穫：Wheat 原版 10 天 + 播種完成時間，時序與 round 12 一致。

## 21. 第十二輪：round 16 — BUG-06 修正實機驗證（四層乘算與維護預設）（2026-10-25 13:56–14:02）

部署 = BUILD-02 + BUG-06 建置：5 程序集 0/0、L1 **428/428**、validate `WARNINGS: none`、384 KB、
部署 SHA 一致。fixture 同 round 14（頂級離網、無電網），config `growthMultiplierPatch=true`。
日誌：`docs/in-game/round16-Player.log`、`round16-summary.txt`。

```
compat.harmony-patch-installed: … Growable.ResumeGrowing harmonyInstance=hdbf.highdensityboxfarm
compat.harmony-patch-applied.1..12: cell=(…,3) growthRate=0.397 vanillaDays=10.00 scaledDays=25.21
diag.day: day=3.18/4.18 registered=600 moist=358 dry=242 coverage=0.597
```

* **BUG-06 修正生效**：同一頂級離網農場、同樣 `G_irr≈0.663`、`G_power=0.6`，載入後维护不再一開始就
  逾期（先前 `G_maint=0.5` 使 rate=0.198/50.63 天）；現在 `rate = 0.663 × 0.60 = 0.397`、
  `scaledDays = 25.21`。維護缺欄位安全預設（`dueDay = 當前日 + 週期`）與「已有紀錄不重置」
  都寫進 L1 回歸（BUG-06 報告：`docs/workstreams/bug-06-maintenance-default.md`）。
* 再次確認四層乘算的實機組合是**乘積**（0.663×0.60），且只有託管格被改寫（12 條 applied 行）。
* 仍未完成：非託管原版作物的回歸量測、完整 25 天拉伸週期收穫、存讀檔不疊加（§20.2 維持）。

### 21.1 本輪剩餘（goal round 2 將繼續）

* BUILD-04（世界警告圖標 + 跨建築聚合）卡執行中；BUILD-02（托管格高亮/框選）已完成並在 428 條測試中。
* REL-01（診斷收斂 + 發佈門禁）待 BUILD-04 完成後執行；完成後需移除 round 14–16 的
  `compat.harmony-patch-applied` 臨時診斷行（REL-01 的任務已包含）。
* BUILD-02 的渲染層無法在無輸入環境下點擊驗證；可先用「Top 農場觸發 P1 灌溉警告 + 截圖找圖標」
  驗證 BUILD-04 的世界圖標是否真的畫出來。

## 22. 第十三輪：round 17–20 — 五模組兼容矩陣（實機）（2026-10-25 14:03–14:24）

前置：BUILD-03 交付的 `tools/dev/CompatMatrix.ps1`；五個指定模組的 Steam Workshop 版本在
1.1.2.8 上有**傳遞依賴**（Conveyor Belts v2 / Timber Pipes 需要 ModdableTimberborn +
ModdableToolGroups + ModSettings；Power Lines 需要 TimberUi；More Mines 需要 TimberUi +
MoreTunnels.Ladder + TradeOffice；全部需要 Harmony）。腳本已擴充為「目標 + 依賴閉包」安裝/移除，
並且放寬 MinimumGameVersion 檢查為「不得高於已安裝遊戲」（依賴 legitimately declare 舊 minimum）。

### 22.1 round 17：依賴版本不對 → 上游模組在 1.1.2.8 崩潰（負面對照）

第一次安裝用了 workshop 的 `MoreTunnels` **v1.3**（`version-1.0.7.0`），載入時：

```
MissingMethodException: Method not found:
  Timberborn.BaseComponentSystem.BaseComponent
  Timberborn.ConstructionSites.ConstructionFactory.CreateAsFinished(BuildingSpec, Placement)
Rethrow as HarmonyException: Patching exception in method
  Opolame.MoreTunnels.Scripts.ImprovedTunnel::OnDeleted
  ... ModdableTimberborn.Registry.ModdableTimberbornRegistry.AddConfigurator ...
```

結論：這不是 HDBF 的問題，而是 **MoreTunnels v1.3 的 IL 針對舊版遊戲 API**，被 ModdableTimberborn
補丁時炸掉整個 mod 載入；遊戲產生 error report 並中止世界載入。過程證明：第三方模組互炸會發生在
HDBF 之外的載入階段（HDBF `start` 行已出，但 `ModCodeStarter.StartMods` 先拋出），**HDBF 無法用
適配器補救**；緩解方式只有釘住與遊戲同代的模組版本（R-04 風險）。

### 22.2 round 18：改用 MoreTunnels v1.5 後**全組合矩陣通過**

修正：`MoreTunnels` 改用 `version-1.1.0.2`（v1.5）。安裝 13 個資料夾（5 目標 + 8 依賴）後啟動：

```
HDBF/Compat: compat.enabled:HDBF.Adapter.TimberPipes: … active; api-anchors=4
HDBF/Compat: compat.enabled:HDBF.Adapter.PowerLines: … active; api-anchors=4
HDBF/Compat: compat.enabled:HDBF.Adapter.ConveyorBeltsV2: … active; api-anchors=4
HDBF/Compat: compat.enabled:HDBF.Adapter.MoreMines: … active; api-anchors=4
HDBF/Compat: compat.enabled:HDBF.Adapter.HoomanStairs: … active; api-anchors=3
[HDBF/Compat] compat.harmony-patch-installed: … Growable.ResumeGrowing harmonyInstance=hdbf.highdensityboxfarm
[HDBF/Info] gameplay.entity: box farm active, tier=Basic, blueprint=BoxFarm.Basic.Folktails
Load time: 82021ms (scene index: 2)
```

`CompatMatrix.ps1 -CheckLog docs/in-game/round18-Player.log -Adapters <five>`：

```
adapter   expected detected enabled failed anchors result
TimberPipes … PowerLines … ConveyorBeltsV2 … MoreMines … HoomanStairs …
HDBF/Error lines: 0   HDBF/Warn lines: 0
MATRIX RESULT: OK
```

證據：`docs/in-game/round18-Player.log`、`round18-summary.txt`。五個適配器**同時**在 1.1.2.8 +
13 個第三方模組（含 Harmony 2.4.1）下 active，HDBF 本體與 D-2 補丁照常載入，沒有 HDBF 側錯誤或警告。


### 22.4 逐一模組組合（TC-02..TC-06 的偵測/初始化層）全部 PASS

每次只安裝「該適配器 + 依賴閉包」，重新啟動、載入同一 fixture，`CompatMatrix.ps1 -CheckLog`：

| 單模組行 | 實機結果 | 證據 |
|---|---|---|
| TimberPipes | `compat.enabled:…TimberPipes active; api-anchors=4`；其餘 4 個 `compat.missing`；`MATRIX RESULT: OK` | `docs/in-game/round19a-Player.log` |
| PowerLines | enabled anchors=4；其餘 missing；OK | `round19_PowerLines-Player.log` |
| ConveyorBeltsV2 | enabled anchors=4；其餘 missing；OK | `round19_ConveyorBeltsV2-Player.log` |
| MoreMines | enabled anchors=4；其餘 missing；OK | `round19_MoreMines-Player.log` |
| HoomanStairs | enabled anchors=3；其餘 missing；OK | `round19_HoomanStairs-Player.log` |

每行都 `HDBF/Error lines: 0   HDBF/Warn lines: 0`；D-2 補丁在每一行也都照常安裝。

### 22.5 矩陣後回歸：移除全部外部模組（乾淨環境，round 20）

`CompatMatrix.ps1 -Remove` 只刪自己建立、且有 ownership marker 的 13 個 `HDBFCompat.*` 資料夾
（`Documents\Timberborn\Mods` 回到 `HDBF` + 工作區目錄）。重新啟動同一 fixture：

```
compat.missing: … TimberPipes / PowerLines / ConveyorBeltsV2 / MoreMines / HoomanStairs not detected
compat.harmony-patch-installed: … Growable.ResumeGrowing ...
gameplay.entity: box farm active, tier=Basic, blueprint=BoxFarm.Basic.Folktails
```

即「全組合 → 全部移除 → 再啟動」沒有殘留、沒有載入錯誤（增刪模組後的降級路徑，TC-08 的
「移除後可繼續玩」在**全新載入**層面通過；真正「存量存檔含 ports 狀態後移除」仍需有 ports 狀態的
存檔，留待使用者實機會話）。日誌：`docs/in-game/round20-Player.log`。

### 22.6 兼容矩陣結論

* 完成：單載（round 12–16）、五個逐一組合（22.4）、全組合（22.2）、移除後降級（22.5）；
  五個適配器在 1.1.2.8 + 13 個第三方模組下皆能偵測、初始化並綁定 API anchor。
* 未完成：端口實際行為（接電/接管/出帶/斷線）與「存量存檔 + 增刪模組」需要可互動遊戲會話；
  TC-09 的「HDBF 對過新模組版本優雅降級」仍需一個只有 HDBF + 單一不相容模組的對照。
* 風險已實證：第三方模組彼此版本不匹配會在 **HDBF 之外**炸掉載入（round 17 的
  `MissingMethodException`），因此發佈時要釘住與遊戲同代的模組版本，並在工坊頁列出已測組合。

### 22.3 矩陣尚未完成的部分（誠實）

* **逐一模組組合**（TC-02..TC-06 的單模組行）：全組合已通過，單模組行尚未逐一跑（腳本支援
  `-Adapters <one>`，每行約 4 分鐘）。
* **端口實作行為**（實際接電/接管/出帶、斷線回退）：需要點擊建築面板與接線，本機無法合成輸入，
  留給使用者實機會話；目前證明的是「偵測 + 初始化 + API anchor 綁定」層。
* **版本不匹配的優雅降級**（TC-09）：round 17 證明的是第三方之間互炸（遊戲在 HDBF 之後的載入階段
  中止），不是 HDBF 對單一模組版本過新的降級；後者仍需一個「只有 HDBF + 不相容外部模組」的
  乾淨對照（本輪 HDBF 自身沒有失敗路徑可觀察）。
* 目前 13 個 mod 資料夾仍在 `Documents\Timberborn\Mods`（`HDBFCompat.*`），由
  `CompatMatrix.ps1 -Remove` 管理；下一輪跑完單模組行後移除，恢復乾淨環境。

## 23. 第十四輪：round 28–29 — 作物隱藏 + 收割仍運作、世界警告圖標（2026-10-25 15:52–16:13）

部署 = REL-01 之後建置（`build.ps1 -Install`：5 程序集 0/0、L1 **492/492**、validate 5/5、
**RELEASE GATES: PASSED (55 checks)**、427 KB、部署 SHA 一致）。注意 REL-01 之後 Release 建置
**不再輸出 `diag.*`**（`HDBF_DEV_DIAGNOSTICS` 編譯期關閉），實機證據改看 production 日誌
（`gameplay.*`、`HDBF/Compat`、`HDBF/Warn`）與截圖。日誌：`docs/in-game/round28-Player.log`、
`round29-Player.log`、`round29-run.png`、`round28-loaded.png`。

### 23.1 地面作物隱藏（BUILD-05）— 實機通過

```
[HDBF/Info] gameplay.crop-visibility.<entity>.start: crop visibility active
[HDBF/Info] gameplay.crop-visibility.<entity>: crop visibility hidden=41 registered=300 hideFailures=0 restoreFailures=0
[HDBF/Info] gameplay.crop-visibility.<entity>: crop visibility hidden=76 registered=300 hideFailures=0 restoreFailures=0
```

* 高級版（27×27、註冊 300 格、有效 76 格）派工播種後，托盤格上的原版作物被逐一隱藏：
  `hidden` 由 41 長到 76，`hideFailures=0 restoreFailures=0`。
* **截圖**（`round29-run.png`）對照 round 12：同一塊托管範圍只剩深色耕地，**看不到任何麥子/幼苗模型**；
  也就是「作物在盒子內」的視覺前提已成立（盒子本體模型由 ART 卡替換）。
* 收割沒有被破壞：同一輪 day ~11 之後出現
  `gameplay.farm: harvested plot 270674947 crop Wheat rotation=True fatigue=0.000`，
  收穫後 `hidden` 由 74 降到 70（實體消失→快取清理/恢復邏輯有效），`hideFailures=0`。
  即 BUILD-05 只隱藏 renderer，不改 `Growable`/`Crop`/`Yielder` 與收穫結算（符合 REQ-CORE-02 精神）。
* 已知視覺殘留：原版「耕地」地表貼圖（種植座標）仍會在地面顯示深色土壤；需求只要求作物隱藏，
  這點保留，若之後要連土壤一起遮，需要額外渲染層（記入 NEXT_ROUND）。

### 23.2 世界空間警告圖標（BUILD-04）— 實機觀察成立（間接 A/B）

* round 21（Top 離網）：建築頭頂出現紅色「斷電」圖標；把部署 config `ui.worldIcons=false` 後
  （round 25）**圖標仍在** → 那是原版 mechanical 建築自己的 no-power 狀態圖標，不是 HDBF 的。
* round 29（Advanced 版、無 mechanical 元件、灌溉覆蓋率 0.667 < `C_min 0.75` → P1 灌溉不足）：
  屋頂上方出現**紅色水滴/警告圖標**（`round29-run.png`）；高級版沒有任何原版機械元件，
  原版不會替它畫動力圖標，故這一個只能來自 BUILD-04 的世界圖標服務。
* 尚未逐項驗證：tooltip 文案、40 格距離裁切、P0 閃爍開關、跨建築聚合列（需要點擊面板/移動鏡頭，
  合成輸入在本機不可用；留給使用者實機會話）。`ui.worldIcons=false` 對 Advanced 的 A/B 尚未跑。

### 23.3 本輪附帶發現 → BUG-07

`round28/29` 日誌出現大量
```
[HDBF/Warn] compat.failed: plot reachability query failed; cell excluded from this scan; ... stack=… suppressed=N
```
這是掃描逐格 `IsReachable` 例外被當成 `compat.failed` 並帶完整 stack 記錄（分類錯誤 + 洗版）。
已開 BUG-07（max）修成掃描內累計、每次掃描至多一條精簡 `gameplay.scan-reachability` Warn（無 stack）。

### 23.4 美術方向更新（使用者 2026-10-25 指定）

使用者要求真 3D 建模，且作物要隱藏於建築內。經查證：
* 遊戲官方提供 **Timbermesh Blender 插件**（v1.2.0, MIT），可直接把 Blender collection 匯出成
  `.timbermesh`；**模型不需要 Unity**（本機 Blender 4.2.1 可攜版已可用）。
* 自訂材質/貼圖圖集仍必須走 **Unity AssetBundle**；但 Unity 6000.5.11f1 安裝檔在中國 CDN
  鏡像缺件（4.06 GB 無法下載）、且本機 Unity 授權是 Unity 中國（最高 2022.3），故本輪先以
  **原版材質名**（木/金屬/玻璃/帆布）搭配自製幾何/UV 交付四檔模型；自訂 2048 PBR 圖集列為後續
  （使用者已同意「先走 Blender 插件 + 原版材質」）。
* ART-01（Blender 程序化四檔模型 + LOD0-2 + PBR 圖集 + 驗證）與 ART-02（官方插件匯出
  `.timbermesh`、槽位/端口、圖標）已在進行；完成後由 Lead 接藍圖與實機外觀驗證。

## 24. 第十五輪：round 30–33 — 自製四檔模型實機上線（2026-10-25 16:56–17:08）

部署 = ART-03 之後建置：`build.ps1 -Install` → 5 程序集 0 warnings / 0 errors、
**L1 PASSED 510 / FAILED 0**、`tools/validate RESULT: OK`（含自製 `.timbermesh` 資產檢查 +
負向對照）、**RELEASE GATES: PASSED (55 checks)**、部署 **1,645 KB**（+16 個 `.timbermesh`、
四檔圖標、8 份 blueprint 已指向 `Buildings/Food/BoxFarm/BoxFarm.<Tier>.<Faction>.Model`）。

### 24.1 四檔模型都成功載入並渲染（逐檔實機）

| 檔位 | 回合 / 證據 | 觀察 |
|---|---|---|
| Basic | `round30-Basic-run.png`（日誌 `round30-Basic-Player.log`） | 右側 3×2 木棚：深色木板牆、屋頂 2×3 六個抬高作物槽、木框收邊；與 ART 預覽 `art/previews/Basic_iso.png` 一致。擋土面/耕地留在地面（作物已隱藏）。 |
| Intermediate | `round31-Int-loaded.png`、`round31-Int-Player.log` | 木板框 + 白灰/玻璃混合的溫室盒體，屋頂槽位可見；與 Basic 在材質/屋頂構件上可區分。 |
| Advanced | `round32-Adv-run.png`、`round32-Adv-Player.log` | 金屬/淺色工業盒體（金屬於原版材質取樣）、屋頂槽位；與 Basic/Intermediate 明顯不同色系與構件。 |
| Top | `round33-Top-run.png`、`round33-Top-Player.log` | 工業綜合體盒體（金屬框 + 鉚接板 + 管道構件），屋頂槽位；同時出現 **紅色斷試圖標** 與左下「建築缺乏能量」通知 → 頂級版動力需求的既有行為在自製模型下仍成立。 |

* 四檔都沒有出現 material/model/prefab 例外；`TimbermeshSpec.Model` 指向的 16 個自製
  `.timbermesh` 全部被載入（部署資料夾 16/16 存在；ART-02 的 `art/timbermesh-validation.txt`
  16/16 PASS、128/128 檢查）。
* 已知限制（誠實）：
  * 模型目前用**原版材質名**（木/金屬/玻璃/屋頂），貼圖由原版 atlas 取樣，所以近看是「拼布」而
    非自訂 2048 PBR 圖集；自訂材質需 Unity AssetBundle（本機網路/授權不可得，見 §23.4）。
  * LOD0 幾何進遊戲；LOD1/LOD2 依賴遊戲端自動 LOD（`Timberborn.PrefabOptimization`），
    尚未逐距離實測切換。
  * **施工階段模型**（ConstructionStage0）已替換但本輪沒有蓋新建築，未實機看過。
  * 模型近距離細節（端口空節點、槽位朝向）在遊戲中沒有消費端，僅作為後續擴充契約。
  * 四檔在 50% 鏡頭下的可辨識度目前是離線渲染指標（ART-01 C9 12/12 PASS）與實機截圖的主觀
    對照；尚未做「四棟同框 50% 截圖」的客觀 A/B。

## 25. ART-04 follow-up：BlockObjectSpec.Size 軸向修正（3×2×1 占格）＋實機 A/B（2026-10-25 21:24–21:29）

> 本節由 block-axis 修正卡補寫。§24 與 §12 的「z=0 排 / z=1 排」措辭是舊軸向假設下的描述；
> 遊戲網格座標其實是 **X=寬、Y=深、Z=高**（IL：`TerrainService.GetTerrainHeight` 用
> `coordinates.z` 當柱高、`VectorExtensions.XY()` 取水平面），完整推導見
> `docs/workstreams/bug-08-block-axis-size.md`。

* 修正前 8 份 blueprint 的 `BlockObjectSpec.Size = {X:3, Y:1, Z:2}` 在遊戲中是「3 寬 × 1 深 ×
  2 高」，與模型/`CollidersSpec`/需求 13.3 的 3 寬 × 2 深 × 1 高不符；已改為 `{X:3, Y:2, Z:1}`。
* `Blocks[0..2]`（y=0 深度排）維持 `GroundOrStackable` 實心入口排、`Blocks[3..5]`（y=1 後排）
  維持 `Any + Floor`；Top 動力 transput 由舊 `(x,0,1)`/`Top` 改到後排 `(x,1,0)`/`Up`（+Y=背面）。
* `tools/validate` 已同步：Size 期望值、depth-row 語意、transput 方向（`Up`=+Y 背面、
  `Down`=−Y 正面、`Top/Bottom`=±Z 垂直面）與「Size.Z 必須=1」檢查；兩個負向樣本（改回舊 Size、
  改回舊 transput）都被明確拒絕。
* 離線 gate：`build.ps1` 全綠（55 release gates、L1 PASSED、`tools/validate RESULT: OK`）；
  部署後 `Mods\HDBF` 的 blueprints 已是 `{X:3, Y:2, Z:1}`。
* 實機（tag `art04-size`）：fixture 於 (129,140,3) 放 Basic farm，載入乾淨、farm active、
  自製模型正常顯示（`docs/in-game/art04-size-loaded.png`），無例外。
* **實機占格 A/B（決定性證據）**：
  - 在修正後後排格子 (130,141,3) 注入 Path → 遊戲 log
    `Can't validate loaded BlockObject BoxFarm.Basic.Folktails(Clone) at (129, 140, 3). It's not backward compatible. Deleting it.`
    並跳出載入問題對話框「對象位置無效，已被刪除：高密度盒子農場（基礎版）」
    （`docs/in-game/art04-probe-in-*.png` / `art04-probe-in-Player.log`）。
  - 同一深度排、佔地外一格 (128,141,3) 注入 Path（對照組）→ farm 正常載入、無刪除、無對話框
    （`docs/in-game/art04-probe-out-*.png` / `art04-probe-out-Player.log`）。
  - 結論：修正後占格確實含 y=141 後排（3 寬 × 2 深）；舊的 3×1×2 資料不可能產生這個衝突。
* 未做：2 層（多層）版本。已定案「2 層時地塊容量 60→120」留給後續卡，且需先修需求 13.3 的
  3×2×1 硬約束與平衡表。

## 26. v2（室內 3×3×1）交付驗證：離線門禁全綠 + 自動實機 round41/42（2026-10-26）

本輪把需求 §19（v2.1）落成 task board 自動化任務卡（8 張實作卡 + 1 張門禁卡 + Lead 逐波驗證），
v1 世界地塊實作已整體替換並刪除。**以下所有數字皆為本機實際輸出**；沒有實機證據的項目明確列在
§26.4「未驗證」中，不標完成。

### 26.1 離線門禁（最終狀態）

```
工作區      : C:\Users\user\Documents\Timberborn\Mods\HighDensityBoxFarm
構建工作根  : %TEMP%\hdbf-<tag>（沙箱內唯一可寫；見 ADR-V2-005）
指令        : powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -WorkRoot "$env:TEMP\hdbf-final"
結果        : 5 程序集 0 warning / 0 error
              L1 PASSED 529 FAILED 0（全部零遊戲依賴）
              tools/validate RESULT: OK（mod assemblies 5/5、blueprints 10、三語鍵集一致）
              RELEASE GATES: PASSED (44 checks)（含 0 Harmony、無 debug/diag 產物、無 55→44 漂移）
              暫存包 1,698 KB（5 dll + 10 blueprint + 16 timbermesh + 3 語系）
NFR 矩陣    : NFR GATE MATRIX: PASSED (6 checks)
              [nfr-gate-01] docs/v2 六件套 + 71/71 REQ-V2 覆蓋 + 無未登記佔位符
              [nfr-gate-02] 66 個 v2 檔頭模板、88 個公開型別 XML、40 個日誌鍵前綴、0 未登記標記
              [nfr-gate-03] 依賴方向 Core←Persistence←Gameplay、Core 無遊戲引用、v2 檔案 ≤300 行
              [nfr-perf] 100×600 日 tick 0.0113 ms/棟（預算 0.5）；存檔 1.05 MB（預算 1.5）；
                         編碼 34.8 ms（預算 50）；30/144 fps 結果一致；日 tick 0 bytes 配置
```

### 26.2 實機回合（自動，無人手、無合成輸入）

部署：`Mods\HDBF` = v2 建置，`manifest.json Version = 0.2.0`；fixture 由
`tools/dev/fixtures/craft-farm-fixture.py --indoor-payload ...` 建立（注入 Basic 農場於
(129,140,3)，並把 v2 schema-v2 payload 寫入 `HDBF.IndoorFarm.Payload`：60 槽全部預植 Carrot、
growth=3.9/4 天、水罐滿 3.6）。payload 由 `tools/dev/CraftIndoorFixture` 以真實
`IndoorSaveCodec` 產生，載入後 `save.migrated: ... repaired=True ... clamped=1`。

**round42-v2（2026-10-26 21:42–21:45，日誌 `docs/in-game/round42-v2-Player.log`）**

```
[HDBF/Info] start HDBF version=0.2.0 modPath=...\Mods\HDBF
HDBF/Compat: compat.missing:HDBF.Adapter.TimberPipes: ... not detected (not-installed)
... PowerLines / ConveyorBeltsV2 / MoreMines / HoomanStairs 同樣 missing（乾淨環境）
[HDBF/Info] startup: HDBF 0.2.0 ready; tiers=4; modPath=...\Mods\HDBF; config=...\HDBF_Config.json
[HDBF/Info] save.migrated: schema-v2 record repaired on load (repaired=True trimmed=0 padded=0 clamped=1 notes=1)
[HDBF/Warn] water.pipe-unavailable: pipe port unavailable (adapter=Disabled); degrading to manual refill, entityId=...
[HDBF/Compat] indoor.port: indoor port availability refreshed: pipe=False, logistics=False, power=False, adapter=Disabled
[HDBF/Info] indoor.entity: indoor farm active, tier=Basic, faction=Folktails, blueprint=BoxFarm.Basic.Folktails
Load time: 30029ms (scene index: 2)
[HDBF/Info] water.settle: indoor water settled: coverage=1.000, tank=2.400, entityId=...
[HDBF/Info] indoor.day: indoor actions: harvested=16, planted=10, actionsUsed=26, blockedByOutput=44, growthMultiplier=1.000
[HDBF/Info] indoor.action-backlog: indoor harvest backlog (output full), entityId=...
[HDBF/Info] water.settle: indoor water settled: coverage=1.000, tank=1.200, entityId=...
[HDBF/Info] indoor.day: indoor actions: harvested=5, planted=11, actionsUsed=16, blockedByOutput=39, growthMultiplier=1.000
```

**由日誌可證（實機）**
1. 模組 0.2.0 在 Timberborn 1.1.2.8 載入，v2 藍圖（3×3×1、9 塊、可疊頂面）載入即 active，
   無 prefab/spec/port 例外，無「不是向後兼容」刪除訊息。
2. 存檔 schema v2 記錄可被讀取並修復（`repaired=True, clamped=1`），不中斷載入（REQ-V2-SAVE-06）。
3. 日 tick 真的每天觸發且順序可觀察：`water.settle`（每日恰好一次扣水）→ `indoor.day`（動作）；
   第 1 天 tank 3.6→2.4、第 2 天 2.4→1.2，coverage=1.000，與 `demand = 60 × 0.020 = 1.2/日` 完全一致
   （REQ-V2-WATER-01/02）。
4. 室內作物循環：第 1 天 `harvested=16, planted=10, actionsUsed=26`（2 工人 × 13 動作），
   第 2 天 `harvested=5, planted=11, actionsUsed=16`；`blockedByOutput=44→39` 與
   `indoor.action-backlog` 顯示輸出庫存滿時**不收穫、槽位保持成熟、面板積壓**（§19.5、REQ-V2-LABOR-01）。
5. 適配器降級：無 Timber Pipes 時管道端口不可用 → 明確記載 `degrading to manual refill`，
   不阻斷建築運作（REQ-V2-COMPAT-02）。
6. `HDBF_Config.json`（schema 2）被載入，無 config 錯誤/遷移警告；五個外部模組的
   `compat.missing` 只作能力探測，不報「缺少模組」錯誤。

**證據文件**
* `docs/in-game/round42-v2-Player.log`（6,450 B）、`round42-v2-summary.txt`
* `docs/in-game/round42-v2-pre-small.jpg`、`round42-v2-run-small.jpg`（2560×1440 → 1280 缩图）
* `docs/in-game/round41-v2-*`：同日第一次嘗試（被工具呼叫中斷，日 tick 證據相同；音訊已還原）

### 26.3 實機未覆蓋 / fixture 限制（誠實記錄）

* **真實建造菜單放置與施工階段**：本輪仍是「存檔注入實體」，沒有走建造/施工流程（TC-V2-01 的
  放置/拆除部分、TC-V2-10 的施工灰盒）——需要可互動會話。
* **屋頂疊放 A/B（REQ-V2-BLD-04 / REQ-V2-BLD-07）**：藍圖已有 `Stackable: BlockObject` +
  `GenerateFloorsOnStackable`，但**尚未在實機放一座原版小建築到屋頂上**；這是發布門檻，仍待實機。
* **面板 UI**：本機合成輸入不可用，無法點開面板；面板/失敗原因/三語只由 L1 契約測試支撐。
* **人工補水正路徑**：注入實體缺少原版 `Accessible`/access 元件，日誌每次都說
  `water.manual-refill: farm has no walk access`；這是 fixture 限制，不是產品行為結論。
* **維護材料扣料**（Intermediate+）、**頂級接電/離網**、**管道實際供水**：本輪 Basic 檔、無材料維護、
  無外部模組，只驗證了「缺模組降級」；正路徑需有對應檔位/模組的實機存檔。
* **存檔往返（AC-06）**：本輪只驗證「載入 v2 payload + 修復」，沒有在遊戲內存檔再讀檔比對槽位/水罐。
* **100×600 實機性能/Profiler、五模組兼容矩陣、50% 四檔同框 A/B**：未做（L1 預算與離線渲染已過）。

### 26.4 v2 發布門禁結論

| 門禁 | 狀態 | 依據 |
|---|---|---|
| NFR-GATE-01 文件集 | **PASS** | `docs/v2/` 六件套 + 71/71 traceability + 無佔位符（L1） |
| NFR-GATE-02 標記/註釋/日誌/命名 | **PASS** | `V2NfrCodeContractTests`（88 型別、40 鍵、0 調試入口/未登記標記） |
| NFR-GATE-03 架構/依賴/檔案規模 | **PASS** | `V2NfrCodeContractTests`（依賴方向、Core 無遊戲引用、≤300 行） |
| NFR-GATE-04 存檔安全 | **PASS（離線）** | 往返/損壞注入/v1→v2/100×600 預算全綠；實機往返待 §26.3 |
| NFR-GATE-05 崩潰安全 | **部分** | fault injection 套件全綠；實機單輪無未捕獲例外，矩陣未跑 |
| NFR-GATE-06 性能 | **PASS（離線）** | 日 tick/存檔/編碼/幀率一致；實機 Profiler 待 |
| NFR-GATE-07 可讀性/UI | **PASS（離線）** | 三語 147 鍵一致、無 v1 術語、失敗原因映射；實機面板待 |

**判定**：v2 的**離線交付門禁全部通過**；實機部分只完成「載入 + 室內日循環 + 水/收穫/積壓 +
缺少模組降級」，**尚不足以宣告 TC-V2-01..10 全部完成**。剩餘實機項在 §26.3，需下一步可互動
遊戲會話（放置/疊放/面板/存檔往返）。

## 27. v2.2 美術升級的實機檢查（2026-10-27，round43/44/52）

本輪把參考圖驅動的美術升級（材質分區/開窗比例/室內貨架剪影，見 `art/references/README.md`）
部署到 `Mods\HDBF` 並跑自動實機回合。離線證據：16 個 `.timbermesh` 全部 3.000×1.000×3.000、
平屋頂、3 端口、0 個 `#Slot`，`art/validate_models.py` PASS（C1–C10）、
`art/validate_timbermesh.py` **128/128 PASS**；L1 **530/530**、`build.ps1` **44 gates PASS**。

### 27.1 已證明（實機）

* 升級後的包（45 檔、manifest 0.2.0）載入 1.1.2.8 無 prefab/material/spec 例外；
  `startup: HDBF 0.2.0 ready` 正常。
* 注入的 Intermediate 農場以新模型載入並啟動：
  `indoor.entity: ... tier=Intermediate, faction=Folktails, blueprint=BoxFarm.Intermediate.Folktails`；
  同一輪 `water.settle coverage=1.000, tank=8.976` → `indoor.day: harvested=16, planted=10,
  actionsUsed=26, blockedByOutput=24` → `indoor.action-backlog`，即**新模型不影響室內模擬**。
* 廣角截圖（`round44-art-run-small.jpg`、`round43-art-run-small.jpg`）可見 3×3 盒子農場以平屋頂
  形式落在地形上，與藍圖 3×3×1 一致；`round44-art-closeup.jpg` 為同一張的放大裁切。

### 27.2 未完成（誠實記錄）

* **自動近距離截圖失敗**：`fixture` 的 `CameraState` 注入在 `ZoomLevel ≥ ~4` 或「不按加速鍵直接
  截圖」時會得到朝上的天空畫面（round45–51 的失敗嘗試已刪除，只留日誌結論）。可重現的
  `ZoomLevel=3.0 + SpeedUp` 路徑只得到約 70 px 寬的農場，無法判斷玻璃透明度與貨架剪影。
  因此「遊戲內玻璃後貨架是否可見」**仍未實機確認**，需玩家用滑鼠走近看。
* 材質在遊戲內是否全部解析到原版 `MaterialCollection`（而非 fallback 灰材質）只能從遠處外觀
  推斷；離線驗證只保證使用白名單材質名。若實機近看發現灰面/洋紅面，請回報，我再修材質映射。
* 四檔同框、屋頂疊放 A/B 仍待可互動會話（§26.3）。

**證據檔案**：`docs/in-game/round43-art-run-small.jpg`、`round44-art-run-small.jpg`、
`round44-art-closeup.jpg`、`round52-art-Player.log`（Intermediate 室內模擬 + 無例外）、
`round43/44-art-summary.txt`。



