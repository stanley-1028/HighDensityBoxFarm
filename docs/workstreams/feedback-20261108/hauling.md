# 2026-11-08 玩家回馈维修：普通搬运工不把高密度农场暂存作物搬进普通仓库（2.5.10）

> **状态**：根因已定位并修复；离线门禁全绿 + **本机实机验证通过**（货物 93→0 被原版搬运工搬进普通仓库，
> 4.1 个游戏日无异常）；**未发布** Steam Workshop（本机修复版已安装待玩家确认）。
> **日期**：2026-11-08
> **适用版本**：2.5.10（上一发布 2.5.9）
> **范围**：`HdbfGameConfigurator` 的模板装饰器链（只做加法：补 2 个原版行为组件）+ 契约测试 + 文档；
> 不改存档 schema、不改蓝图、不改容量/收获/搬运数值。

## 1. 玩家回馈（原文）

> 「我希望普通搬运工也能把在高密度农场暂存的农作物搬進普通仓库中。」

玩家存档（`1110 (6).timber`，Cycle 2 Day 17，00:10 存档、HDBF 2.5.9）证据：

| 事实 | 值 |
|---|---|
| Basic 农场输出库存 | `Inventory:SimpleOutputInventory` Carrot **93 / 104** |
| 同地区接收仓库 | `LargeWarehouse.Folktails` (125,147) `Inventory:Stockpile` Carrot **569**，蓝图允许 Carrot |
| 目的地距离 | `DistrictInventoryPicker.ClosestInventoryWithCapacity` → `Stockpile@5.0`（可达） |
| 搬运工 | 地区中心 4 名工人（普通搬运工），另有 2 名农民 |
| 玩家手动“优先搬运” | Basic 农场 `HaulPrioritizable.Prioritized=true`（玩家已尝试强制搬运） |
| 其它农场 | Intermediate/Advanced/Top 已完工、被玩家**暂停**并**标记清空**（`Emptiable.IsMarkedForEmptying=true`）；WaterPump 同样被标记 |

即：有目的地、有搬运工、填充率高（0.894）、还被手动提权，作物却一直躺在农场输出库存里——不是“仓库满/没仓库”。

## 2. 根因（IL + 实机双层证据）

### 2.1 原版搬运链的完整组件配对（1.1.2.8 IL）

原版 `SimpleOutputInventory` 建筑的“清空输出”链由三部分组成，**provider 与 behavior 必须成对出现在同一实体上**：

| 模块 | 注册 |
|---|---|
| `SimpleOutputBuildingsTemplateModuleProvider` | `SimpleOutputInventorySpec → SimpleOutputInventory` + dedicated `Inventory` + `InventoryNeedBehavior`（仅库存，不含搬运链） |
| `Timberborn.Fields.FieldsConfigurator`（FarmHouse）/ `ForestryConfigurator`（LumberjackFlagSpec） | `AutoEmptiable / Emptiable / HaulCandidate / SimpleOutputInventoryHaulBehaviorProvider / EmptyOutputWorkplaceBehavior` **+ `RemoveUnwantedStockWorkplaceBehavior` + `EmptyInventoriesWorkplaceBehavior`** |
| `Timberborn.Emptying.EmptyingConfigurator`（级联） | `Emptiable → HaulCandidate / EmptiableHaulBehaviorProvider / UnwantedStockHaulBehaviorProvider`；`AutoEmptiable → AutoEmptiableBlocker` |

`EmptiableHaulBehaviorProvider.Awake`：`_emptyInventoriesWorkplaceBehavior = GetComponent<EmptyInventoriesWorkplaceBehavior>()`；
`GetWeightedBehaviors`：若 `Emptiable.IsMarkedForEmptying`，直接
`new WeightedBehavior(0.51f, _emptyInventoriesWorkplaceBehavior)`（**无 null 检查**）。
`UnwantedStockHaulBehaviorProvider` 对 `RemoveUnwantedStockWorkplaceBehavior` 同构。
`HaulCandidate.GetWeightedBehaviors` 把这些条目原样交进地区候选表；`HaulWorkplaceBehavior.Decide`
逐条 `behavior.Decide(agent)`。

### 2.2 HDBF 漏掉的两个行为组件

`src/HDBF.Gameplay/HdbfGameConfigurator.cs` 的模板装饰器只抄了 Vanilla FarmHouse 链的一部分：
`AutoEmptiable / Emptiable / HaulCandidate / SimpleOutputInventoryHaulBehaviorProvider / EmptyOutputWorkplaceBehavior`。
`Emptiable` 会**级联**带来 `EmptiableHaulBehaviorProvider` 与 `UnwantedStockHaulBehaviorProvider`，
但这两个 provider 所需的行为组件 `EmptyInventoriesWorkplaceBehavior` / `RemoveUnwantedStockWorkplaceBehavior`
从未被添加到 HDBF 实体上（原版由 FarmHouse/LumberjackFlag 的建筑模块负责添加）。

### 2.3 故障链（与实机崩溃逐帧吻合）

1. 玩家把 Intermediate/Advanced/Top 农场暂停并“清空” → 这些农场 `Emptiable.IsMarkedForEmptying=true`，
   且它们都是**已完工建筑**（实机探针确认 4 座农场全部 `OnEnterFinishedState` 激活）。
2. 地区搬运工的 `HaulWorkplaceBehavior.Decide` 遍历地区候选：遇到被标记农场时，
   `EmptiableHaulBehaviorProvider` 产出 `WeightedBehavior(0.51, NULL)`。
3. `HaulWorkplaceBehavior.Decide` 对 NULL 调 `Decide` → **NullReferenceException**（实机崩溃报告栈顶）：
   `NullReferenceException at Timberborn.Hauling.HaulWorkplaceBehavior.Decide … Rethrow as Exception:
   Exception thrown while ticking entity … 'BeaverAdult 奥佐布尔'`。
4. 后果：每次搬运工行为 tick 都在这一帧抛错，**整个地区的搬运任务无法完成**（普通游戏里 Unity 捕获
   Update 异常后继续，但搬运工永远不搬；本机 T3MP fast-tick 直接把异常重抛成崩溃）。Basic 农场的输出
   因此从不被搬走——这正是玩家看到的现象；“优先搬运”当然也无效。
5. 同一缺组件还会在另一入口复现：2.5.9 把输出库存每货物容量降到 104/208/416/832，旧存档中超过新容量的
   库存会让 `Inventory.HasUnwantedStock=true`，触发 `UnwantedStockHaulBehaviorProvider` 的 NULL behavior；
   修好组件配对后，这类“超容量旧货”也会由原版行为正常搬出（而不是报错）。

### 2.4 证据

* 存档分析：见 §1 表；4 座农场全部完工、Basic 93 件 Carrot 静置。
* IL：`Timberborn.SimpleOutputBuildings` / `Timberborn.Fields` / `Timberborn.Emptying` / `Timberborn.Hauling`
  反编译（ApiIndex/Mono.Cecil），事实见 §2.1。
* 修复前实机（probe 临时构建）：`docs/in-game/haulprobe2-Player.log`、`haulprobe2-crash-exception.txt`
  （NRE 栈，异常报告 `error-report-2026-11-08-01h00m43s.zip` 同内容）。
* 修复后实机（probe 临时构建）：`docs/in-game/haulprobe5-Player.log` / `haulprobe5-summary.txt` /
  `haulprobe5-round.log`：
  * `indoor.haulprobe.postload`（Basic）：`invEnabled=True invComponentEnabled=True`、
    `removeUnwantedBehavior=True emptyInventoriesBehavior=True`、
    `providers=…SimpleOutputInventoryHaulBehaviorProvider(0.89:EmptyOutputWorkplaceBehavior)…`、
    `enabledInventories=2[Inventory:Water,SimpleOutputInventory]`、`CarrotDest=Stockpile@5.0`；
  * 被标记农场：`EmptiableHaulBehaviorProvider(0.51:EmptyInventoriesWorkplaceBehavior)`（**非 NULL**）；
  * 库存储时间序列（game day 34.17 载入）：
    `34.17=93 → 35.15=93 → 35.94=89 → 36.17=89 → 36.72=0 → 37.17=0 → 37.51=0 → 38.17=0 → 38.29=0`；
  * 全程 4.1 个游戏日 **0 条 NullReferenceException / 0 条 ticking exception**；搬运工先把 3 座被标记
    农场的库存清空，随后在 Day 36.72 前把 Basic 的 93 件 Carrot 全部送进 `Stockpile`，之后随收随搬。

## 3. 修复（只做加法，原版对齐）

`src/HDBF.Gameplay/HdbfGameConfigurator.cs`：

```csharp
builder.AddDecorator<HdbfBoxFarmSpec, EmptyInventoriesWorkplaceBehavior>();
builder.AddDecorator<HdbfBoxFarmSpec, RemoveUnwantedStockWorkplaceBehavior>();
```

并加注释记录“provider ↔ behavior 必须成对”的原版依据与 2026-11-08 根因。

## 4. 验证

离线（`build.ps1`，本机）：

* 5 程序集 **0 warning / 0 error**；L1 **732 / 732 PASSED**（新增 2 条 configurator 契约 token）；
  `tools/validate` RESULT: OK（5/5 assemblies）；**RELEASE GATES PASSED (44 checks)**；
  `NFR GATE MATRIX: PASSED`。
* 新增回归：`V2GameplayWiringTests.TestGameplayWiringSourceContracts` 断言 configurator 必须包含
  `AddDecorator<HdbfBoxFarmSpec, EmptyInventoriesWorkplaceBehavior>` 与
  `…RemoveUnwantedStockWorkplaceBehavior>`（防再次漏配 provider↔behavior）。

实机（2026-11-08 09:47–09:55，`HDBFHaulProbe/haulprobe` 玩家存档副本，HDBF 2.5.10 probe 构建）：

* 载入 115.9 s；4 座农场全部激活；Basic 输出 93 件 Carrot 在 2.5 个游戏日内被原版搬运工搬空；
* 被标记农场此前会触发崩溃，本轮 4.1 游戏日无异常（修复前同存档 2.5 分钟内必现 NRE）；
* 证据文件见 §2.4。

## 5. 已知限制 / 未做

* **未发布**：本机 `Documents\Timberborn\Mods\HDBF` 已安装 2.5.10；未上传 Steam Workshop、未 push GitHub。
* 实机探针的“地区候选清单”转储因调用了 `GetComponentsAllocating<BaseComponent>()` 被游戏拒绝
  （`Use BaseComponent.AllComponents instead`），故未打印逐候选列表；但“搬运工确实把 93 件搬空且全程无异常”
  已直接证明候选/行为链在工作，且修复前后唯一差异就是这两个行为组件。
* 载入后 `HaulCandidate.IsInHaulingCenterRange=False`：这是加载顺序导致的候选侧 `_districtHaulCandidates`
  未重新绑定，只影响状态图标/面板提示（原版行为注册走 DistrictBuildingRegistry 事件，搬运本身不受影响，
  实机搬运成功已证明）；本修复不改它（避免扩大范围），另行开卡。
* 旧存档“超过 2.5.9 每货物容量”的库存：修复后由原版 `RemoveUnwantedStockWorkplaceBehavior` 正常搬出，
  不会再触发 NRE；本机玩家存档当时已回落到 104 以内，未单独构造超容量用例实机验证。

## 6. 改动文件

| 文件 | 改动 |
|---|---|
| `src/HDBF.Gameplay/HdbfGameConfigurator.cs` | +2 个行为组件装饰器（原版 FarmHouse/LumberjackFlag 对齐）+ 根因注释 |
| `tests/HDBF.Tests/V2GameplayWiringTests.cs` | +2 条 configurator 契约 token（防回归） |
| `data/manifest.json`、`tools/validate/DataChecks.cs` | 版本 2.5.9 → 2.5.10 |
| `docs/VERIFICATION.md` | 新增 §48（离线 + 实机证据） |
| `docs/v2/CHANGELOG.md`、`docs/v2/README.md`、`README.md` | 2.5.10 条目/版本/摘要 |
| `docs/in-game/haulprobe2-*`、`haulprobe5-*` | 修复前崩溃 + 修复后搬运的实机证据 |
