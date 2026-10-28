# HDBF v2 架构

> **状态**：2.2.0 已实现（层/日 tick/数据流与代码一致；实机证据见 VERIFICATION §26）｜**日期**：2026-10-26｜**适用版本**：2.2.0 / schema 2
> **变更摘要**：v2 用室内槽位模型替换 v1 世界地块扫描；本文记录分层、数据流、日 tick 固定顺序与关键决策摘要。

## 1. 分层与依赖

```
┌────────────────────┐   ┌──────────────────┐   ┌────────────────────┐
│ HDBF.UI            │   │ HDBF.Gameplay    │   │ HDBF.Integration   │
│ 面板快照呈现/tooltip│   │ 控制器/日 tick    │   │ 管道/动力/物流适配器 │
└─────────┬──────────┘   └────────┬─────────┘   └─────────┬──────────┘
          │                       │                       │
          │              ┌────────▼────────┐              │
          │              │ HDBF.Persistence│              │
          │              │ 实体读写/迁移    │              │
          │              └────────┬────────┘              │
          └───────────────────────┼───────────────────────┘
                          ┌───────▼────────┐
                          │ HDBF.Core       │  纯逻辑：槽位/公式/存档/快照
                          │ Indoor/*        │  仅 BCL，禁止 Unity/Timberborn
                          └────────────────┘
```

规则（NFR-GATE-03 由架构测试强制）：

1. `HDBF.Core` 不引用 UnityEngine / Timberborn / 任何外部模组程序集。
2. 无循环依赖；`UI`/`Integration` 只向下依赖 `Core`。
3. 单文件 ≤300 行（软门槛，超标必须拆分并在 TRACEABILITY 记理由）。
4. 无静态可变状态；无每帧分配；缓存可显式清理；卸载/删除幂等。
5. `Gameplay` 只做游戏对象桥接与编排，**公式与状态迁移只在 Core**。

## 2. 运行时组成

1. 游戏加载 `HDBF.*.dll`；`HdbfModStarter` 记录版本，永不抛异常。
2. `HdbfGameConfigurator`（Context=Game）绑定：配置/档位注册表、`IndoorFarmController`
   （`AddDecorator<HdbfBoxFarmSpec, IndoorFarmController>`）、工作位 behavior、输出库存
   decorator（原版搬运）、快照查询服务、命令服务、`ICompatRegistry`。
3. 蓝图 JSON 携带 `HdbfBoxFarmSpec`；四档差异**只在数据**（`TierProfile` + 蓝图 + 配置）。
4. 控制器在自己的实体上实现 `IPersistentEntity`，通过 `IndoorEntitySaveBridge` 读写 v2 记录。

## 3. 日 tick 固定顺序（REQ-V2-MAINT-03）

控制器每个游戏日触发一次 `IndoorDayTick.Run(...)`，顺序**固定且可测**：

```
1. 入口/在岗门禁       → 入口堵塞则本日只停农事动作（生长不变）
2. 水位读取            → 水罐由游戏层按经过的游戏时间连续扣（IndoorWaterMath.DemandForElapsedDays
                        + Consume）；本 tick 只修复/读取实时水罐，算覆盖率 C 与 G_irr
3. 生长推进            → 每个非空未成熟槽位 progress += Δday × G_growth
4. 农事动作            → LaborMath：成熟优先收获（预算 = 在岗工人 × 13）→ 水罐有水时
                        空槽自动种满（不占预算，2026-10-28 玩家需求）
5. 疲劳/轮作           → 同作物累加、轮作衰减、休耕恢复
6. 维护/动力           → 到期判定、缺料标记、G_maint/G_power
7. 警示与脏标记        → IndoorWarningFlags 汇总；Changed=true 时置存档脏位
```

>`水耗连续化` 由 Gameplay 的 `HdbfWaterFlowService`（`IUpdatableSingleton`）每帧把
>`IDayNightCycle.PartialDayNumber` 的增量交给每个已完工农场：暂停不扣、倍速按比例、一整天恰好扣
>`DemandPerDay`（帧率无关）。每日 tick 不再一次性扣水，避免「一天扣一大格」的突兀表现。

`G_growth = G_irr × G_maint × G_power` 只由 `IndoorMultipliers` 汇总函数计算（REQ-V2-BAL-01）。

## 4. 数据流

* **存档**：实体 → `IndoorEntitySaveBridge.Save` → `IndoorSaveValidator` 校验 → 写入实体键。
  写前校验失败：不写该记录、保留上一份内存快照、`save.validation-failed` 日志、下次保存重试。
* **读档**：实体键 → 容错读取 → 默认值修复 → v1 记录迁移（尽力而为）→ 激活控制器。
* **面板**：控制器 → `IndoorSnapshotBuilder`（Core）→ `IndoorFarmSnapshot` → UI 呈现（≤4 Hz、脏标记）。
* **水**：`IWaterSource`（管道适配器 / 人工补水）→ `TryDraw` → 水罐；**不得无源补水**。
* **产物**：成熟槽位收获 → `IInventorySink.TryAdd(goodId, amount)` → 原版 `SimpleOutputInventory`
  → 原版搬运/传送带。库存满：不收获、槽位保持成熟、面板显示积压。

## 5. 已删除的 v1 机制（§19.2）

| v1 | v2 |
|---|---|
| 世界格扫描 / `PlotRegistry` / 托管格 | 删除；改为槽位数组，无坐标 |
| 工作范围（11×11…27×27） | 删除；建筑固定 3×3×1 |
| 土壤水分采样 / 外部灌溉 | 删除；内部水罐 + 覆盖率 |
| 原版种植/收获任务派发 | 删除；室内动作预算 |
| 托管格高亮/框选/世界地块图标 | 删除；只保留缺水/维护/断电/入口堵塞图标 |
| 托管作物可见性补丁 / Harmony 生长补丁 | 删除；v2 目标 **0 Harmony** |

## 6. 日志键登记（附录，§19.18.1-B）

| 前缀 | 用途 | 示例 key |
|---|---|---|
| `indoor.*` | 槽位/日 tick/动作 | `indoor.day`, `indoor.slot-fault`, `indoor.mode` |
| `water.*` | 水罐/补水/覆盖率 | `water.settle`, `water.refill-failed`, `water.low` |
| `save.*` | 读写/校验/迁移 | `save.write`, `save.validation-failed`, `save.migrated` |
| `ui.*` | 面板/命令 | `ui.command-failed`, `ui.snapshot` |
| `compat.*` | 适配器 | `compat.failed`, `compat.degraded` |
| `config.*` | 配置迁移/忽略键 | `config.migrated`, `config.ignored-key` |

同一 key 30 秒节流；日志带 entityId/槽位号，不带完整堆栈刷屏（`ThrottledLog`）。

## 7. 性能预算（§19.13）

| 项 | 预算 | 实现约束 |
|---|---|---|
| 100 栋 × 600 槽日 tick | ≤0.5 ms/建筑/日 | 纯数组遍历，无临时 list/字符串拼接 |
| 覆盖率重算 | 2 Hz/建筑，≤0.05 ms | O(activeSlots) |
| UI 刷新 | ≤4 Hz，≤0.10 ms | 脏标记 + 快照 DTO |
| 存档 | 100 栋 ≤1.5 MB | 槽位紧凑编码；序列化 ≤50 ms |
| 叠放 nav mesh | 放下后一次重建 | 不得每帧重建 |
