# 2026-11-07 玩家回馈维修：农场输出库存「只做暂存」（2.5.9）

> **状态**：离线修复完成（L1 732/732、`tools/validate` OK、44 gates PASSED），实机验证 pending。
> **日期**：2026-11-07
> **适用版本**：2.5.9（上一发布 2.5.8）
> **范围**：`IndoorConstants.OutputCapacityFor` + 8 份蓝图 + `tools/validate` + 面板/警告共享规则 + L1 契约 + 文档；不改存档 schema、不改搬运/收获逻辑。

## 1. 玩家回馈（原文）

> 「高密度农场的仓库并没有做暂存，反而是像普通仓库一样储存，我想它的的仓库只做暂存，而不是像普通仓库一样储存。」

用户已选择验收口径：**一天产量级暂存**（每档容量 = 满员一天最大收获量 104/208/416/832），
而不是原版农家固定 50（会让整盒成熟时每天只送出约 50 个，即 2026-10-29 回馈的行为），
也不是「只改容量口径」（IgnorableCapacity）方案。

## 2. 根因（含证据）

### 2.1 现行实现的来源

2026-10-29 玩家回馈「600 玉米成熟后只有 50 个送进仓库」时，把四档
`SimpleOutputInventorySpec.Capacity` 从原版农家的 50 改为**整盒收成上限** `槽位上限 × 4`
（240/600/1200/2400，见 `docs/VERIFICATION.md` §29、`docs/REQ_V2_SECTION_19.md` §19.3 旧修正说明）。
这解决了「整盒一次收获放不下」，但把农场的输出库存做成了可囤积数日产量的仓库存量。

### 2.2 为什么大容量 = 像仓库一样储存（原版搬运机制，IL 复核）

用 Mono.Cecil 对 1.1.2.8 实装程序集反编译核对（本机
`C:\Program Files (x86)\Steam\steamapps\common\Timberborn\Timberborn_Data\Managed`）：

| 环节 | 类型/方法 | 事实 |
|---|---|---|
| 输出库存从哪来 | `Timberborn.SimpleOutputBuildings.SimpleOutputInventoryInitializer::Initialize` | `CreateWithUnlimitedCapacity`（`Inventory.Capacity = int.MaxValue`）+ `HasPublicOutput()`；按 `SimpleOutputInventorySpec.Capacity` 把每个允许货物设为 takeable 的**每货物上限**；`IgnorableCapacity` 只转调 `Inventory::SetIgnorableCapacity` |
| 清空输出建筑的权重 | `Timberborn.SimpleOutputBuildings.SimpleOutputInventoryHaulBehaviorProvider::GetWeightedBehaviors` | 权重 = `InventoryFillCalculator::GetInStockOutputFillPercentage(Inventory)`，即 `Σ在库量 / Σ(有库货物的每货物上限)`（`Mathf.Clamp01`）；空库存权重 0（不会顺路清空）；`Inventory.Enabled && BlockableObject.IsUnblocked` 才提供行为 |
| 填充率公式 | `Timberborn.InventorySystem.InventoryFillCalculator::GetInventoryFillPercentage` | 对每个有库存的允许货物求 `AmountInStock / LimitedAmount`；分母 = `Math.Min(Σ LimitedAmount, Inventory.Capacity)`；输出库存每货物上限 = `spec.Capacity`，故单作物 `权重 = 库存 / 容量`，多作物时分母为各货物上限之和 → 权重更低 |
| 搬运优先级 | `Timberborn.Hauling.DistrictHaulCandidates::GetWorkplaceBehaviorsOrdered` | 比较器 `b.Weight.CompareTo(a.Weight)` → **按 Weight 降序排序**后交给搬运工；`HaulWorkplaceBehavior.Decide` 顺序取第一个可执行行为 → 填充率低的农场排在空仓库之后，长期轮不到清空 |
| **清空必须有目的地** | `Timberborn.Emptying.EmptyOutputWorkplaceBehavior` → `EmptyingStarter.GetCarriableGood` → `CarrierInventoryFinder.GetClosestInventoryWithCapacity` → `DistrictInventoryRegistry.ActiveInventoriesWithCapacity` → `InventoryRegistry.UpdateRegistries` | **只有 `PublicInput` 库存会被放进「有容量」表**；`SimpleOutputInventory` 是 PublicOutput-only，永远不是目的地。⇒ **地区没有接受该货物的仓库时，任何容量/权重都不会让搬运工来清空** |
| `IgnorableCapacity` 的作用面 | `Timberborn.InventorySystem.Inventory::GetCapacity` + `Timberborn.ResourceCountingSystem.CapacityCounter::CountInventoryCapacity` | 仅让该库存**不计入地区库存容量统计**；不影响库存量、不影响搬运权重、不影响 `LimitedAmount`。因此它不是「暂存」开关（不采用） |
| 旧存档兼容 | `Inventory::Load` → `LoadFromNamedComponent` | 读档时直接把存档 `StorageKey` 的货物写入 `_storage`，**无容量校验/裁剪**；容量不在存档、来自蓝图 spec，读档实例化即生效。旧档超过新容量的库存保留，但 `UnreservedCapacity = 0` → 该货物**暂不再收**（槽位保持成熟、不丢货），有目的地时权重按 `Clamp01` = 1.0 最高优先被清出，低于新容量后恢复收获；面板进度条夹到 100% |
| 原版先例 | `FarmHouse.IronTeeth` 蓝图 | 2 工人 / 输出 50；`EfficientFarmHouse` 3/50、`AquaticFarmhouse` 2/50、`TappersShack` 1/50、`GathererFlag` 1/20。50 是固定小缓冲，**不能严格写成“一天产量”**（按本模组 13 动作/人/日模型，2 工人理论 78/日 > 50） |

**两个叠加因素（不要混为一谈）**：

1. **无接收仓库** → 无论容量多大/权重多高，产物都不会被搬走（原版机制，见上表「清空必须有目的地」）。
   在旧的大容量下，农场于是**静默地**变成玩家唯一的仓库（用户存档正是这种局面）。
2. **有接收仓库时** → 清空任务按填充率降序排队；整盒容量 (240/600/1200/2400) 让单作物填充率长期很低
   （例：Top 有 500 件时仅 ~21%），清空任务排在空仓库之后，产物倾向滞留、优先被别的搬运任务挤掉。
   小容量会让填充率快速到高位，搬运工优先来清 —— 这就是原版农家「暂存」的机制。

### 2.3 用户实机存档证据（本机只读，未改动原存档）

`%USERPROFILE%\Documents\Timberborn\Saves\1110\`（Day 2-10 系列，2026-11-07 游玩 2.5.8）：

| 存档 | 游戏日 | Basic 输出 | Intermediate 输出 | Advanced 输出 | Top 输出 | 地区仓库 |
|---|---|---:|---:|---:|---:|---|
| `2026-11-07 17h31m, Day 2-9.autosave.timber` | 26 | Carrot 141 | Carrot 110 | Carrot 226 | —（未完工/无输出库存组件） | LargeWarehouse：GrilledPotato 418（`SingleGoodAllower=GrilledPotato`，不收 Carrot） |
| `2026-11-07 18h05m, Day 2-9.autosave.timber` | 26 | Carrot 141 | Carrot 110 | Carrot 226 | — | 同上 |
| `2026-11-07 18h10m, Day 2-10.autosave.timber` | 27 | Carrot 161 | Carrot 174 | Carrot 334 | — | 同上（无 Carrot 库存） |

- 作物产出 `DistrictGoodsBalance.ProductionRegistry` Carrot 210/日，地区没有接受 Carrot 的仓库；
  按 §2.2「清空必须有目的地」，这些 Carrot **不会被搬运**，在旧规则下农场输出库存（容量 240/600/1200）
  就是唯一可囤积处，一天内从 477 → 669 件且没有清运出口。
- 该快照证明「农场输出库存事实上被当作长期仓库使用」，而不是短暂中转；它同时体现「无目的地」
  与「大容量能静默吞下数日产量」两个因素。容量改小后，同一存档会**在一天左右回压**
  （OutputFull、槽位保持成熟），直到玩家建一个接受 Carrot 的仓库 —— 这才是「只做暂存」。
- **复现方法**：`.timber` 是 zip，解出 `world.json` 后按 `Entities[].Template == "BoxFarm.*"` 与
  `Components["Inventory:SimpleOutputInventory"].Storage.Goods` 读取农场输出库存；同地区仓库读
  `Inventory:Stockpile` / `SingleGoodAllower`。证据原档
  `2026-11-07 18h10m, Day 2-10.autosave.timber` = 460,710 B，SHA-256
  `2551DD85EC016D80F6465B42D789E64425D988A88153CBD3074829C75E3C4BA2`（只读，未修改）。

## 3. 修复设计（已由用户确认口径）

**新规则**：`OutputCapacityFor(slotCapacity)` = `ceil(slotCapacity / SlotsPerFarmer) × ActionsPerFarmerDay × MaxHarvestItemsPerSlot`
= `ceil(slots/38) × 13 × 4`：

| 档位 | 槽位 | 满员工人 | 一天最大收获动作 | 暂存容量 |
|---|---:|---:|---:|---:|
| Basic | 60 | 2 | 26 | **104** |
| Intermediate | 150 | 4 | 52 | **208** |
| Advanced | 300 | 8 | 104 | **416** |
| Top | 600 | 16 | 208 | **832** |

设计约束（同时满足两条）：

1. **不再像仓库**：农场最多只暂存满员一天产量（有接收仓库时，小容量让搬运工按填充率优先来清，
   不会再被整盒大容量长期降级）；一天以上的堆存必须由玩家仓库承担。
2. **不退回 2026-10-29 的积压 bug**：容量 ≥ 满员一天的最大收获量（208 动作 × 4 件 = 832），
   因此**空缓冲时**单次日 tick 的收获必装得下（若日初已有积压，则按设计回压：满则不收获、槽位保持
   成熟、面板 OutputFull/积压）；这是「暂存」的正确语义，不是丢货。

**前提（必须让玩家知道）**：清空输出库存必须有**接受该货物的仓库/公共输入库存**（原版
`CarrierInventoryFinder.GetClosestInventoryWithCapacity` 只认 `PublicInput`）。没有接收仓库时，
新规则不会把货「变」到别处：农场在约一天产量后**回压并亮 OutputFull**，玩家需要建对应仓库
（或调整现有仓库允许的货物）——这正是把「静默囤货」换成诚实的物流问题。

不改：`IgnorableCapacity`（保持 false，与原版农家一致，输出库存的容量仍计入地区库存容量）；
`SimpleOutputInventory`/搬运链、收获逻辑、存档 schema、`IInventorySink` 接口都不变。

## 4. 代码/数据改动清单

| 文件 | 改动 |
|---|---|
| `src/HDBF.Core/Indoor/IndoorConstants.cs` | `OutputCapacityFor` 改为满员一天产量规则；常量注释记录 2026-10-29 → 2026-11-07 的两次修正与原因（含「需有接收仓库」前提） |
| `data/Buildings/Food/BoxFarm/BoxFarm.*.blueprint.json`（8 份） | `SimpleOutputInventorySpec.Capacity` → 104/208/416/832 |
| `src/HDBF.UI/Panels/HdbfIndoorPanelLimits.cs`、`HdbfIndoorPanelFactory.Cards.Output.cs` | 只更新注释；容量由共享 Core 规则派生，数值自动跟随 |
| `src/HDBF.Core/Indoor/IndoorWarnings.cs` | 无需改代码（OutputFull 用同一 `OutputCapacityFor`，自动跟随） |
| `tools/validate/DataChecks.cs` | inventory 检查改为同一规则（`ExpectedOutputCapacity`），错误文案标注「一天暂存上限」；`ExpectedReleaseVersion` → 2.5.9 |
| `tests/HDBF.Tests/V2LaborTests.cs` | 1 条旧回归改为 3 条暂存回归（单日必装下 / 无搬运整盒回压 / 有搬运 200/日 整盒收完）+ dense 832 零余量边界 + 公式 104/208/416/832；`CappedSink` 支持搬运 Drain |
| `tests/HDBF.Tests/V2BlueprintContractTests.cs`、`V2LiveWarningContractTests.cs`、`V2PanelContractTests.cs`、`V2PanelVisualContractTests.cs` | 契约期望改为新公式/新数值；新增 validator↔Core 公式漂移源码契约 |
| `docs/workstreams/tech-data.md` | 追加 2026-11-07 补记声明整盒规则已被取代（历史段保留） |
| `data/manifest.json` | 版本 2.5.9 |
| 文档 | `README.md`、`docs/REQ_V2_SECTION_19.md`、`docs/v2/ARCHITECTURE.md`、`docs/v2/CHANGELOG.md`、`docs/v2/README.md`、`src/HDBF.UI/README.md`（含多货物口径/配置覆盖已知限制）、`docs/VERIFICATION.md` §46 |

## 5. 验证

离线（本轮已跑，见 `docs/VERIFICATION.md` §46）：

- `build.ps1`（最终全量重跑，含本文件/文档全部定稿后）：5 程序集 0 warning / 0 error、
  L1 **PASSED 732 / FAILED 0**、`tools/validate` **RESULT: OK / 5/5 assemblies**、
  **RELEASE GATES PASSED (44 checks)**、staged **48 files / 2,202,809 bytes / 2,151 KB**、manifest 2.5.9。
- `tools/validate` 负向对照：把某档容量改回 2400 → `[inventory] ... must be 104 (... found 2400)`、exit 1；
  还原后 RESULT: OK。
- 旧存档兼容论证见 §2.2 末行；实机加载旧存档（含超容量库存）的 A/B 留待实机回合。

实机（pending，不在本轮写「已实机」）：需要可交互会话观察——农场面板输出库存上限变为 104/208/416/832；
在有接收仓库时产物被搬运工搬走、库存回落到一天产量以内；无接收仓库时按设计回压（OutputFull 警告），
不再无限囤积。**本轮未启动游戏。**
