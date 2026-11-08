# 2026-11-08 玩家回馈维修：「即便没有搬运工，也不显示『需要搬运工才能运转』」（2.5.11）

> **状态**：根因已定位并落地为硬保证；离线门禁全绿 + **本机实机（玩家存档副本）验证**——农场实体从未挂载该原版状态组件，
> 新增守卫日志 `indoor.hauler-status` 确认「不存在即无需显示」；**已发布** Steam Workshop（ManifestID `2025374107308867942`）。
> **日期**：2026-11-08
> **适用版本**：2.5.11（上一发布 2.5.10）
> **范围**：`HdbfBoxFarmController` 新增一个 partial（守卫）+ 日志键 + 契约测试 + 文档；
> **不改** configurator 装饰器链（不动搬运链/数值/蓝图/存档 schema），无 Harmony、无反射、无新依赖。

## 1. 玩家回馈（原文）

> 「我希望即便是没有搬运工也好，也不会显示『没有搬运工就不能运作』。」

## 2. 考察：这条提示在原版里到底是什么、挂在谁身上

### 2.1 文案与图标（原版 1.1.2.8 本地化）

| 键 | enUS | zhCN | zhTW |
|---|---|---|---|
| `Status.Hauling.NoHaulingPost` | `Requires haulers to function.` | 「需要搬运工才能运转。」 | 「需要搬運工才能運轉。」 |

对应浮动图标 `OutOfHaulersRange`（`NoHaulingPostStatus.Awake` 的 `StatusToggle.CreateNormalStatusWithFloatingIcon("OutOfHaulersRange", …)`）。

### 2.2 原版只会给三类建筑挂这个状态（Mono.Cecil IL 证据）

全游戏只有 4 个程序集引用 `Timberborn.Hauling.NoHaulingPostStatus`：定义它的 `Timberborn.Hauling`，以及三个挂载方：

| 挂载点（IL） | 模板装饰器 | 实际生效条件 |
|---|---|---|
| `GoodConsumingBuildingSystemConfigurator.TemplateModuleProvider` | `AddDecorator<GoodConsumingBuildingStatusInitializer, NoHaulingPostStatus>` | 任何带 `GoodConsumingBuildingSpec` 的建筑；`OnEnterFinishedState` 里**只在建筑没有 `Workplace` 时**调用 `Initialize(() => true)` |
| `StockpilePrioritySystemConfigurator.ProvideTemplateModule` | `AddDecorator<GoodObtainerStatusInitializer, NoHaulingPostStatus>` | 设定「Obtain」的仓储（`Stockpile` + `GoodObtainer`） |
| `FireworkSystemConfigurator.TemplateModuleProvider` | `AddDecorator<FireworkLauncherStatusInitializer, NoHaulingPostStatus>` | 烟花发射器 |

**原版 FarmHouse / LumberjackFlag / 所有 `SimpleOutputInventory` 生产建筑都不挂它**（`Timberborn.Fields.dll` 与
`Timberborn.SimpleOutputBuildings.dll` 均不引用该类型）；HDBF 的模板装饰器链（`HdbfGameConfigurator`）也不挂。

### 2.3 玩家自己的存档里，盒子农场实体上没有这个组件

玩家 2026-11-08 21:51 存档（`Saves/3/…Day 4-6`）的 `BoxFarm.Basic.Folktails` 实体保存组件只有：

```
BlockObject, Workplace, HDBF.IndoorFarm, Inventory:Inventory:Water,
WorkplaceWorkerType, Inventory:ConstructionSite
```

没有 `GoodConsumingBuilding`、没有 `Stockpile`/`GoodObtainer`、没有 `NoHaulingPostStatus`（该组件本身也无存档状态）。
同一存档里真正挂着该状态的是**无工人的蒸汽引擎**（`SteamBoiler.Small.T1.Folktails` 带 `GoodConsumingBuilding`，见 §2.2 第一行）
以及其它模组建筑——不在 HDBF 的责任范围（对应模组各自修复）。

### 2.4 但 HDBF 要把「农场不需要搬运工也能工作」变成硬保证

盒子农场没有搬运工也能工作：

* 水：管道自动补水（Timber Pipes / 接网液体水塔）或面板「手动补水」按钮，不需要搬运工；
* 收获：先进入建筑内**一天产量**的输出暂存（104/208/416/832，2.5.9 起），搬运工只负责把暂存搬去仓库；
  没有搬运工时按设计回压并在面板显示「输出库存已满」，不会显示「需要搬运工才能运转」。

因此「requires haulers to function」对盒子农场是误导性提示。原版当前不会给农场挂它，但该状态是原版**公开类型**、
可被第三方/未来原版挂载；本版把它锁成**硬保证**，见 §3。

## 3. 修复（防御式硬保证，原版公开 API，无补丁）

新增 partial `src/HDBF.Gameplay/Components/HdbfBoxFarmController.HaulerStatus.cs`：

1. **激活时 / 读档后**各探测一次实体上的 `Timberborn.Hauling.NoHaulingPostStatus`（`HasComponent` / `GetComponent`）；
2. 若存在 → 调用原版公开 opt-out **`NoHaulingPostStatus.Disable()`**（与 vanilla initializer 的
   `OnExitFinishedState` 用的是同一个调用：停用 toggle 并解除 `HaulCandidate.InHaulingCenterRangeChanged` 订阅）；
3. **每个 daily tick 再调一次**（成本 = 两次 null 检查）：即使之后被地区重指派等事件重新激活，也会在当日被压回去；
4. 不存在时写一条 info 诊断 `indoor.hauler-status`，把「农场上没有该状态」变成可核查的实机证据；
5. 每次激活/读档重新探测（`Deactivate`/激活失败时重置），不缓存跨生命周期的旧引用。

**不做**的事：不改 `HdbfGameConfigurator` 的装饰器链（搬运链保持原版对齐，搬运工仍会清空农场输出）、不改数值/蓝图/
存档 schema、不引入 Harmony/反射/新依赖。`HdbfGameConfigurator` 现在由契约测试锁定**不得**出现
`NoHaulingPostStatus` 装饰器（防止未来有人把误导性状态挂回农场）。

## 4. 验证

### 4.1 离线（`build.ps1 -Install`，本机）

* 5 程序集 **0 warning / 0 error**；L1 **732 / 732 PASSED**（`V2GameplayWiringTests` 新增守卫/反装饰器契约 token）；
* `tools/validate` RESULT: OK（5/5 assemblies）；**RELEASE GATES: PASSED**；`NFR GATE MATRIX: PASSED`
  （新增日志键 `indoor.hauler-status` 命中 `indoor.*` 注册前缀；新文件过 300 行门禁与头模板）。

### 4.2 实机（本机 batchmode 存档加载，玩家存档副本）

见 VERIFICATION §50：载入玩家 21:51 存档副本后，日志出现
`[HDBF/Info] indoor.hauler-status: no vanilla 'requires haulers' status on this farm …`，
农场正常激活并工作，全程无 `[HDBF/Error]`/异常——与 §2.3 的存档组件清单一致。

## 5. 已知限制 / 边界

* 玩家在游戏里看到的「需要搬运工才能运转」来自**原版 GoodConsuming 建筑（其无工人蒸汽引擎等）**，不是盒子农场
  （§2.2/§2.3）；本修复保证盒子农场永远不会显示它，并已把该保证写进契约测试。
* 2.5.10 记录的 `HaulCandidate.IsInHaulingCenterRange` 读档后为 `False` 的原版加载顺序限制**未改**
  （只影响状态图标/面板提示，不影响搬运；且本修复后盒子农场不再可能显示任何搬运范围状态）。
* 若第三方模组在农场**完工之后**才挂上 `NoHaulingPostStatus`，守卫会在下一个 daily tick 前压回（正常游戏中不存在该组件）。

## 6. 改动文件

| 文件 | 改动 |
|---|---|
| `src/HDBF.Gameplay/Components/HdbfBoxFarmController.HaulerStatus.cs` | **新增**：农场「需要搬运工才能运转」状态守卫（探测 + `Disable()` + 每日复检 + 诊断日志） |
| `src/HDBF.Core/Indoor/IndoorLogKeys.cs` | +`HaulerStatus = "indoor.hauler-status"` |
| `src/HDBF.Gameplay/Components/HdbfBoxFarmController.Activation.cs` | 激活/停用/激活失败时调用守卫/重置 |
| `src/HDBF.Gameplay/Components/HdbfBoxFarmController.cs` | `PostLoadEntity` 读档后再探测一次 |
| `src/HDBF.Gameplay/Components/HdbfBoxFarmController.Tick.cs` | daily tick 复检 |
| `tests/HDBF.Tests/V2GameplayWiringTests.cs` | 守卫契约 token + 「农场不得被装饰 `NoHaulingPostStatus`」负向契约 + 新文件尺寸/无逐帧门禁 |
| `data/manifest.json`、`tools/validate/DataChecks.cs` | 版本 2.5.10 → 2.5.11 |
| `docs/VERIFICATION.md` | 新增 §50（离线 + 实机）、§51（发布记录） |
| `docs/v2/CHANGELOG.md`、`docs/v2/README.md`、`README.md` | 2.5.11 条目/版本/摘要 |
