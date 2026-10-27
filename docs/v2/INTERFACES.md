# HDBF v2 冻结接口与不变量

> **状态**：draft（阶段 1 冻结）｜**日期**：2026-10-26｜**适用版本**：2.2.0 / schema 2
> **变更摘要**：定义 v2 `HDBF.Core.Indoor` 的公开类型与不变量；各实现卡必须按此契约实现，
> 新增/修改接口必须先改本文件。所有公开类型/方法必须有 XML 文档注释（REQ-V2-READ-02）。

命名：统一术语 `slot / tank / coverage / cropPolicy / roof stacking`；魔法数字只能出现在
`IndoorConstants`、`TierCatalog`、配置里（REQ-V2-READ-02/MAGIC）。

## 1. 枚举与常量

```csharp
namespace HDBF.Core.Indoor
{
    public enum HdbfFaction { Folktails = 0, IronTeeth = 1 }
    public enum IndoorMode { Normal = 0, Dense = 1, Fallow = 2 }          // 复用 HdbfMode 也可
    public enum CropPolicy { Rotation = 0, Fixed = 1 }
    public enum SlotStage { Empty = 0, Growing = 1, Mature = 2 }
    public enum WaterSupplyKind { None = 0, Pipe = 1, Manual = 2 }
    public enum PowerConnection { NotRequired = 0, Grid = 1, OffGrid = 2 }

    [Flags] public enum IndoorWarningFlags
    {
        None = 0, NoWater = 1, LowWater = 2, MaintenanceDue = 4,
        MaintenanceStalled = 8, NoPower = 16, EntranceBlocked = 32,
        OutputFull = 64, NoWorkers = 128, MissingMaterial = 256,
    }

    public static class IndoorConstants
    {
        public const int SlotsPerFarmer = 38;
        public const int ActionsPerFarmerDay = 13;
        public const float WorkDaysPerAction = 0.08f;      // workCost = 0.08 × modeWorkFactor

        public const float DenseYieldMultiplier = 1.35f;
        public const float DenseWaterMultiplier = 1.50f;
        public const float DenseFatigueMultiplier = 1.50f;
        public const float DenseWorkMultiplier = 1.20f;
        public const float FallowWaterMultiplier = 0.30f;
        public const float FallowRecoveryPerDay = 0.03f;

        public const float FatiguePerHarvest = 0.08f;       // × 档位疲劳系数
        public const float FatigueYieldPenalty = 0.60f;     // G_fat = 1 − 0.60×F
        public const float RotationFatigueFactor = 0.40f;
        public const float RotationRecovery = 0.05f;

        public const float OffGridEfficiency = 0.60f;
        public const float OneCycleMissedEfficiency = 0.50f;
        public const int StalledAfterCycles = 2;

        public const int ManualRefillActions = 2;
        public const float ManualRefillTankFraction = 1f / 3f;  // 每趟补 1/3 罐
        public const int ManualRefillRadiusCells = 12;
        public const float ModeTransitionDays = 1f;
    }
}
```

## 2. 档位数据（`TierCatalog` / `TierProfile`）

【修改】v2 字段命名与语义：

| 字段 | 值（Basic/Intermediate/Advanced/Top） | 说明 |
|---|---|---|
| `SlotCapacity` | 60 / 150 / 300 / 600 | 原 `PlotCapacity` 重命名 |
| `MaxWorkers` | 2 / 4 / 8 / 16 | 不变 |
| `MinIrrigationCoverage` | 0.50 / 0.60 / 0.75 / 0.80 | `C_min` |
| `WaterPerSlotPerDay` | 0.020 / 0.024 / 0.028 / 0.032 | 原 `WaterPerPlotPerDay` 重命名 |
| `TankCapacityDays` | 3 / 3 / 4 / 4 | 不变 |
| `TankCapacity` | 3.6 / 10.8 / 33.6 / 76.8 | `WaterPerSlotPerDay × SlotCapacity × TankCapacityDays` |
| `SoilFatigueFactor` | 1.00 / 1.10 / 1.25 / 1.50 | 不改名（存档/配置兼容），文档称“档位疲劳系数” |
| `BuildWorkdays` | **2 / 3 / 5 / 8** | 【修改】v2.1 由 6/10/16/28 缩短 |
| `WorkRange` | 【作废】 | 阶段 3 删除；阶段 1 暂留字段不再读取 |

`TierCatalog.SlotsPerFarmer = 38`、`ActionsPerFarmerDay = 13` 保留常量。

**实现定稿说明（2026-10-26）**：`PlotCapacity` / `WaterPerPlotPerDay` 这两个 **JSON/存档字段名**
最终**没有重命名**（v1→v2 数据与蓝图兼容优先）；`TierProfile` 上的只读别名
`SlotCapacity` / `WaterPerSlotPerDay` / `SlotsPerFarmer` 是 v2 代码使用的正式名字，
XML 注释已改为 v2 语义。`WorkRange` 字段保留但 v2 不再读取（v1 地块链路已删除）。

## 3. 槽位模型

```csharp
public sealed class IndoorSlot
{
    public string CropId { get; set; }          // 空串 = 空槽；必须属于本阵营作物目录
    public float GrowthDays { get; set; }       // 0..GrowthTimeInDays
    public float Fatigue { get; set; }          // 0..1
    public string LastCropId { get; set; }      // 轮作依据；空串 = 无
    public SlotStage Stage { get; set; }
    public byte Flags { get; set; }             // bit0 = reserved by worker, 其他保留
    public bool IsEmpty => Stage == SlotStage.Empty;
}
```

不变量：

* 槽位**不映射世界格**：没有坐标、PlotId、注册表（REQ-V2-CORE-03）。
* 下标 0..Capacity−1 稳定；新播种取编号最小的空槽。
* `P_eff = min(SlotCapacity, ActiveWorkers × 38)`；只有前 `P_eff` 个槽位参与调度（REQ-V2-CORE-01）。
* 单槽异常必须隔离：`IndoorDayTick` 内每槽 try/catch，连续 3 次失败禁用该子系统并告警（REQ-V2-SAFE-02）。

## 4. 记录（存档 / 运行时唯一状态）

```csharp
public sealed class IndoorFarmRecord
{
    public const int CurrentSchemaVersion = 2;
    public int SchemaVersion { get; set; }
    public string EntityId { get; set; }                 // 仅诊断，可为空
    public HdbfTier Tier { get; set; }
    public HdbfFaction Faction { get; set; }
    public IndoorMode Mode { get; set; }
    public CropPolicy CropPolicy { get; set; }
    public string FixedCropId { get; set; }              // CropPolicy=Fixed 时有效，否则空串
    public IndoorSlot[] Slots { get; set; }              // Length == TierProfile.SlotCapacity
    public float Tank { get; set; }
    public float MaintenanceDueDay { get; set; }
    public int MaintenanceCyclesMissed { get; set; }
    public bool PowerConnected { get; set; }
    public PowerConnection PowerMode { get; set; }
    public bool PipePort { get; set; }
    public bool LogisticsPort { get; set; }
    public bool PowerPort { get; set; }
    public string TimberPipesAdapter { get; set; }       // None/Ok/Degraded/Disabled/Failed

    public static IndoorFarmRecord Create(HdbfTier tier, HdbfFaction faction);
}
```

不变量：`Slots.Length` 恒等于档位容量；`Tank ∈ [0, TankCapacity]`；浮点必须有限；
枚举必须在白名单内；`FixedCropId` 必须属于本阵营或为空（REQ-V2-SAVE-04）。

## 5. 纯函数模块（Core，无游戏依赖）

```csharp
public readonly record struct IndoorMultipliers(float Irrigation, float Maintenance, float Power)
{
    public float Growth { get; } // = Irrigation * Maintenance * Power（唯一乘算点）
}

public static class IndoorGrowthMath
{
    public static float FatigueYieldMultiplier(float fatigue);            // 1 − 0.60×F
    public static float ModeYieldMultiplier(IndoorMode mode);             // 1.00 / 1.35 / 0（休耕不收获）
    public static float Yield(int baseYield, float fatigue, IndoorMode mode);
    public static void Advance(IndoorSlot slot, CropDefinition crop, float gameDays, float growthMultiplier);
}

public static class IndoorWaterMath
{
    public static float DemandPerDay(int activeSlots, float waterPerSlotPerDay, IndoorMode mode);
    public static float Coverage(float tank, float demandPerDay);         // clamp(tank/demand,0,1)；demand=0 → 1
    public static float IrrigationMultiplier(float coverage, float minCoverage)
        => Math.Clamp((coverage - 0.25f*minCoverage) / (0.75f*minCoverage), 0f, 1f);
    public static float SettleDay(IndoorFarmRecord record, TierProfile tier, int activeSlots); // 每日仅一次扣水，返回 C
    public static float RefillAmount(float tankCapacity);                 // capacity/3
    public static float TryRefill(IndoorFarmRecord record, float amount); // 返回实际补入，clamp 到容量
    public static float DaysRemaining(float tank, float demandPerDay);    // demand=0 → float.PositiveInfinity 由 UI 显示 “—”
}

public static class IndoorLaborMath
{
    public static int EffectiveSlots(TierProfile tier, int activeWorkers);
    public static int ActionBudget(int activeWorkers);                    // ×13
    public static float WorkDaysPerAction(IndoorMode mode);               // 0.08 / 0.096 / 0.08
    public static IndoorActionStats RunActions(IndoorFarmRecord record, TierProfile tier,
        ICropCatalog catalog, int activeWorkers, IInventorySink output, bool farmActionsAllowed);
}

public struct IndoorActionStats
{
    public int Harvested, Planted, ActionsUsed, BlockedByOutput;
    public bool Backlog;
}

public static class IndoorMaintenanceMath
{
    public static float Efficiency(int cyclesMissed);   // 0→1.0, 1→0.5, ≥2→0（停摆）
    public static bool IsStalled(int cyclesMissed);
    public static float MaintenanceMaterialMultiplier(bool powerConnected); // 断电 ×2
}

public static class IndoorPowerMath
{
    public static float Efficiency(bool needsPower, bool connected); // 无需动力/已接电→1.0，断电→0.6
}

public static class IndoorDayTick
{
    public static IndoorDayResult Run(IndoorFarmRecord record, TierProfile tier,
        ICropCatalog catalog, in IndoorDayContext context);
}

public sealed class IndoorDayContext
{
    public int ActiveWorkers { get; init; }
    public float CurrentDay { get; init; }
    public bool EntranceBlocked { get; init; }
    public IInventorySink? Output { get; init; }
    public IWaterSource? Water { get; init; }
    public IMaintenanceMaterialSource? Materials { get; init; }
    public bool HasPipeWater { get; init; }
    public bool ManualRefillRequested { get; init; }
}

public readonly struct IndoorDayResult
{
    public float Coverage { get; init; }
    public float GrowthMultiplier { get; init; }
    public float YieldMultiplier { get; init; }
    public IndoorActionStats Actions { get; init; }
    public IndoorWarningFlags Warnings { get; init; }
    public bool Changed { get; init; }
    public bool Stalled { get; init; }
}
```

`IndoorDayTick.Run` 顺序固定：入口门禁 → 水耗/补水 → 生长 → 农事动作 → 疲劳/轮作 → 维护/动力 →
警示/脏标记。**任何单槽/单步骤异常不得中断其他槽位**（REQ-V2-CORE-05）。

## 6. 扩展接口（Core 定义，其它程序集实现）

```csharp
public interface ICropCatalog
{
    IReadOnlyList<CropDefinition> ForFaction(HdbfFaction faction);
    bool TryGet(HdbfFaction faction, string cropId, out CropDefinition crop);
}

public sealed record CropDefinition(
    string Id, float GrowthTimeInDays, string YieldItemId, int BaseYield);

public interface IWaterSource               // 管道 / 人工 / 未来第三来源
{
    WaterSupplyKind Kind { get; }
    bool IsAvailable { get; }
    bool TryDraw(float amount, out float actual);
}

public interface IInventorySink             // 原版库存 / 传送带 / 未来仓库
{
    bool TryAdd(string goodId, int amount);
}

public interface IMaintenanceMaterialSource // 最近仓库库存事务
{
    int Available(string itemId);
    bool TryConsume(string itemId, int count);
}

public interface IIndoorFarmState           // UI/测试只读快照
{
    IndoorFarmSnapshot Snapshot { get; }
}

public interface ISaveMigrator              // v1 → v2 → v3 迁移链
{
    bool TryMigrateV1(IndoorLegacyV1Record legacy, TierProfile tier, out IndoorFarmRecord migrated);
}

/// <summary>由 HDBF.Integration 的适配器注册表实现；Gameplay 只依赖该 Core 接口。</summary>
public interface IIndoorAdapterHost
{
    /// <summary>管道补水来源；模组缺失/未连接时为 null（降级为人工补水）。</summary>
    IWaterSource? PipeWaterSource { get; }

    bool IsPipeConnected { get; }

    bool IsLogisticsConnected { get; }

    /// <summary>适配器健康快照（None/Ok/Degraded/Disabled/Failed），用于面板与存档。</summary>
    HdbfAdapterHealth PipeHealth { get; }
}
```

依赖方向不变：`HDBF.Integration` 实现 `IIndoorAdapterHost`，`HDBF.Gameplay` 通过 DI 消费 Core 接口；
`Gameplay` 不得引用 `HDBF.Integration` 类型（ADR-002 的装配链限制）。缺失/未连接时 Gameplay 必须
降级为人工补水或离网 0.6，不得阻断建筑运行（REQ-V2-COMPAT-02/03）。

## 7. 作物目录

运行时从游戏 `FarmHouse` 可种作物集合读取 `GrowthTimeInDays` / `CuttableSpec.Yielder`；
`FallbackCropCatalog` 为内置回退表（缺失 spec 时用，记一次 `indoor.crop-spec-missing`）：

| 阵营 | 作物 | 生长天数 | 产物 | 产量 |
|---|---|---:|---|---:|
| Folktails | Carrot | 4 | Carrot | 3 |
| Folktails | Potato | 6 | Potato | 1 |
| Folktails | Wheat | 10 | Wheat | 3 |
| Folktails | Sunflower | 5 | SunflowerSeeds | 2 |
| IronTeeth | Canola | 9 | CanolaSeeds | 3 |
| IronTeeth | Cassava | 5 | Cassava | 1 |
| IronTeeth | Corn | 10 | Corn | 2 |
| IronTeeth | Eggplant | 12 | Eggplant | 3 |
| IronTeeth | Kohlrabi | 3 | Kohlrabi | 2 |
| IronTeeth | Soybean | 8 | Soybean | 2 |

## 8. 存档编解码 / 校验 / 迁移

```csharp
public static class IndoorSaveValidator
{
    public static IndoorRepairResult ValidateAndRepair(
        IndoorFarmRecord record, TierProfile tier, ICropCatalog catalog, string? expectedEntityId);
}

public sealed class IndoorRepairResult
{
    public bool Repaired { get; }
    public int SlotsTrimmed { get; }
    public int SlotsPadded { get; }
    public int ValuesClamped { get; }
    public IReadOnlyList<string> Notes { get; }
}

public static class IndoorSaveCodec
{
    public const int SchemaVersion = 2;
    public static string Encode(IndoorFarmRecord record);                       // 确定性文本
    public static bool TryDecode(string text, out IndoorFarmRecord record, out string error);
    public static int MeasureBytes(IndoorFarmRecord record);                    // 预算测试
}

public static class IndoorSaveMigrator : ISaveMigrator
{
    public const int LegacySchemaVersion = 1;
    public static IndoorFarmRecord RepairDefaults(TierProfile tier, HdbfFaction faction); // 安全默认
}
```

v1→v2 映射（REQ-V2-SAVE-02）：v1 `plots.ids/crops/fatigue` 按顺序映射槽位，
数量取 `min(v1 槽数, Capacity)`；`tank`/维护/模式/端口/适配器保留；失败 → 空槽 + 满水罐 +
默认维护 + `save.migrated-failed` 警告；**不删除玩家原版地块/作物**。

**实体持久化落地（实际实现）**：每个建筑通过原生 `IPersistentEntity` 保存**一条**校验后的
schema-v2 文本负载（ComponentKey `HDBF.IndoorFarm`，属性 `Payload`），由
`HDBF.Persistence.Indoor.IndoorEntitySaveBridge` 负责「深拷贝 → 校验 → Encode → 单键写入」。
单键写入保证不会出现半条记录；读档走容错的 `TryDecode + ValidateAndRepair`；v1 的
`HDBF.BoxFarm` 组件键仍可读并走迁移。100 栋 × 600 槽 = 1.05 MB / 编码 34.8 ms（预算内）。

## 9. 面板快照 DTO（UI 不读游戏对象）

```csharp
public sealed record IndoorFarmSnapshot(
    HdbfTier Tier, HdbfFaction Faction,
    int Workers, int MaxWorkers, IndoorMode Mode, bool ModeTransitioning,
    int Capacity, int ConfiguredSlots, int EffectiveSlots,
    int EmptySlots, int GrowingSlots, int MatureSlots, int BacklogActions,
    float Tank, float TankCapacity, float TankDaysRemaining,
    float Coverage, float MinCoverage, WaterSupplyKind WaterSupply,
    float MaintenanceDueDay, int MaintenanceCyclesMissed, bool MaintenanceMaterialMissing,
    PowerConnection Power, bool PowerConnected,
    CropPolicy CropPolicy, string FixedCropId, string RotationSequence,
    int OutputOccupied, bool OutputPortConnected,
    bool PipePort, bool LogisticsPort, bool PowerPort,
    IndoorWarningFlags Warnings, string OutputInventoryPortState);
```

空数据用 `null` 或 `float.NaN` 表示，由 UI 渲染为“—”（REQ-V2-READ-04）。

## 10. 边界与安全契约

* **REQ-V2-SAFE-01**：日 tick / 存读档 / activate/deactivate/delete / UI 刷新 / 命令 / 适配器调用
  全部 try/catch；异常不外抛；节流 `HDBF/Warn`；不得静默吞掉。
* **REQ-V2-SAFE-02**：单槽/单建筑/单适配器隔离；连续 3 次失败只禁用该子系统。
* **REQ-V2-SAFE-04**：所有服务/实体/组件/spec/适配器结果都有 null 检查；实体销毁后 no-op。
* **REQ-V2-SAFE-05**：v2 **不新增 Harmony 补丁**；适配器只反射，失败降级。
* **REQ-V2-SAVE-04**：写入前必须校验；失败不写、保留旧值、`save.validation-failed`。
* **REQ-V2-SAVE-06**：读档不得因 HDBF 字段缺失/损坏失败；单栋隔离修复并记日志。
