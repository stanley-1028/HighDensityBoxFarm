# HDBF v2 需求追溯矩阵

> **状态**：draft（阶段 4 门禁收敛）｜**日期**：2026-10-26｜**适用版本**：0.2.0 / 存档 schema 2 / 配置 schema 2
> **变更摘要**：`REQ-V2-*` ↔ 代码文件/类 ↔ L1/实机测试 的唯一矩阵；v1 地块链路已删除（§12）。
> 状态：`pending`（未实现）/ `L1`（离线自动化绿）/ `GAME`（实机已验）。没有测试映射的需求不得标已实现。

路径约定：`C:` = `src/HDBF.Core/Indoor/`，`P:` = `src/HDBF.Persistence/Indoor/`，
`G:` = `src/HDBF.Gameplay/`，`U:` = `src/HDBF.UI/`，`I:` = `src/HDBF.Integration/`，
`T:` = `tests/HDBF.Tests/`，`D:` = `data/`。

## 1. 建筑（BLD）

| 需求 | 实现文件/类 | L1 测试 | 实机 | 状态 |
|---|---|---|---|---|
| REQ-V2-BLD-01 3×3×1 占地/9 块 | `D:Buildings/Food/BoxFarm/*.blueprint.json`（Size 3×3×1，Blocks=9） | `T:V2BlueprintContractTests` | TC-V2-01 | L1 |
| REQ-V2-BLD-02 入口通路/堵塞暂停 | `G:Components/HdbfBoxFarmController.Activation.cs` / `.Tick.cs`（入口门禁） | `T:V2GameplayContractTests` | TC-V2-01 | L1 |
| REQ-V2-BLD-03 平屋顶 | `art/gen_models_v2.py`、`D:*.timbermesh`（平平台、顶面 y=1.00） | `T:ArtModelContractTests`（128 项美术校验） | TC-V2-02 | L1 |
| REQ-V2-BLD-04 屋顶可叠放 | 蓝图 `Stackable:BlockObject` + `BlockObjectNavMeshSettingsSpec.GenerateFloorsOnStackable` | `T:V2BlueprintContractTests` | TC-V2-02 | L1（A/B 待实机） |
| REQ-V2-BLD-05 拆除语义 | `G:Components/HdbfBoxFarmController.cs`（`DeleteEntity` 无地块副作用） | `T:V2GameplayContractTests` | TC-V2-01 | L1 |
| REQ-V2-BLD-06 8 蓝图/端口/模型 | `D:Buildings/Food/BoxFarm/`、`tools/validate` | `T:V2BlueprintContractTests` | TC-V2-10 | L1 |
| REQ-V2-BLD-07 叠放门禁（报告用户） | `docs/v2/ADRS.md` ADR-V2-002、`docs/VERIFICATION.md` | —（流程门禁） | TC-V2-02 | pending（待实机） |

## 2. 核心（CORE）

| 需求 | 实现文件/类 | L1 测试 | 状态 |
|---|---|---|---|
| REQ-V2-CORE-01 槽位容量/P_eff | `C:IndoorFarmRecord.cs`, `C:IndoorLaborMath.cs` | `T:V2LaborTests`, `T:V2BlueprintContractTests` | L1 |
| REQ-V2-CORE-02 读档补齐/截断 | `C:IndoorSaveValidator.cs`, `C:IndoorSlotRepair.cs` | `T:V2SaveRepairTests` | L1 |
| REQ-V2-CORE-03 禁止世界作物/田块 API | `C` 无游戏引用；`G` 无 PlantingService/NaturalResourceFactory/SetField | `T:V2GameplayContractTests`, `T:V2NfrCodeContractTests` | L1 |
| REQ-V2-CORE-04 纯逻辑可测 | `C:` 全部（仅 BCL） | `T:V2NfrCodeContractTests` | L1 |
| REQ-V2-CORE-05 单槽隔离/节流 | `C:IndoorDayTick.cs`（单槽 try/catch） | `T:V2SlotFaultIsolationTests` | L1 |

## 3. 作物与生长（§19.5）

| 需求 | 实现文件/类 | L1 测试 | 状态 |
|---|---|---|---|
| §19.5 生长公式 | `C:IndoorGrowthMath.cs`, `C:IndoorDayTick.cs` | `T:V2GrowthTests` | L1 |
| §19.5 轮作/疲劳 | `C:IndoorLaborMath.cs`, `C:IndoorDayTick.cs` | `T:V2RotationFatigueTests` | L1 |
| §19.5 模式倍率 | `C:IndoorConstants.cs`, `C:IndoorGrowthMath.cs` | `T:V2ModeTests` | L1 |

## 4. 水（WATER）

| 需求 | 实现文件/类 | L1 测试 | 状态 |
|---|---|---|---|
| REQ-V2-WATER-01 覆盖率纯函数 | `C:IndoorWaterMath.cs` | `T:V2WaterTests`（含除零/负值） | L1 |
| REQ-V2-WATER-02 每日一次扣水/有源补水 | `C:IndoorWaterMath.cs`, `C:IndoorDayTick.cs` | `T:V2WaterTests`, `T:V2GameplayWiringTests` | L1 |
| REQ-V2-WATER-03 面板水字段 | `C:IndoorFarmSnapshot.cs`, `U:Panels/HdbfIndoorPanel*` | `T:V2PanelContractTests` | L1 |
| REQ-V2-WATER-04 人工补水动作/可达 | `C:IndoorDayTick.cs`, `G:Components/HdbfBoxFarmController.ManualRefill.cs` | `T:V2GameplayWiringTests` | L1 |

## 5. 劳动力（LABOR）

| 需求 | 实现文件/类 | L1 测试 | 状态 |
|---|---|---|---|
| REQ-V2-LABOR-01 动作预算/收获优先 | `C:IndoorLaborMath.cs` | `T:V2LaborTests` | L1 |
| REQ-V2-LABOR-02 无路径工时 | `C:IndoorLaborMath.cs`（`WorkDaysPerAction` 无路径项） | `T:V2LaborTests` | L1 |
| REQ-V2-LABOR-03 原版工作位门禁 | `G:Components/HdbfBoxFarmController.Tick.cs`, `G:Farming/IndoorFarmWorkplaceBehavior.cs` | `T:V2GameplayContractTests` | L1 |
| REQ-V2-LABOR-04 室内工作/无外部农田 | 同上；无地块扫描/种植派发 | `T:V2GameplayContractTests` | L1（实机 TC-V2-03 pending） |

## 6. 平衡/维护/动力（BAL）

| 需求 | 实现文件/类 | L1 测试 | 状态 |
|---|---|---|---|
| REQ-V2-BAL-01 单一汇总乘算 | `C:IndoorMultipliers.cs`（唯一乘法点） | `T:V2BalanceTests`, `T:V2GrowthTests` | L1 |
| REQ-V2-BAL-02 停摆只停农事 | `C:IndoorDayTick.cs`（生长继续，动作暂停） | `T:V2MaintenancePowerTests` | L1 |
| REQ-V2-BAL-03 世界警告图标/面板原因 | `G:Components/HdbfWarnings.cs`, `HdbfWorldIconService*.cs` | `T:V2WarningIconContractTests` | L1 |

## 7. 存档（SAVE）

| 需求 | 实现文件/类 | L1 测试 | 状态 |
|---|---|---|---|
| REQ-V2-SAVE-01 schema v2 往返/修复 | `C:IndoorSaveCodec.cs`, `C:IndoorSaveValidator.cs`, `P:IndoorEntitySaveBridge.cs` | `T:V2SaveCodecTests`, `T:V2PersistenceBridgeContractTests` | L1 |
| REQ-V2-SAVE-02 v1→v2 迁移 | `C:IndoorSaveMigrator.cs`, `P:IndoorEntitySaveBridge.Legacy.cs` | `T:V2SaveMigrationTests` | L1 |
| REQ-V2-SAVE-03 读档一致性/帧率无关 | `C:IndoorDayTick.cs`（日触发） | `T:V2NfrPerformanceTests` | L1 |
| REQ-V2-SAVE-04 写前校验 | `C:IndoorSaveValidator.cs`, `P:IndoorEntitySaveBridge.cs` | `T:V2SaveValidatorTests`, `T:V2SaveFaultInjectionTests` | L1 |
| REQ-V2-SAVE-05 迁移失败安全 | `C:IndoorSaveMigrator.cs` | `T:V2SaveMigrationTests` | L1 |
| REQ-V2-SAVE-06 读档不因坏字段失败 | `P:IndoorEntitySaveBridge.cs` | `T:V2SaveFaultInjectionTests` | L1 |
| REQ-V2-SAVE-07 损坏/迁移/往返/预算全绿 | 上述 + `T:V2SaveBudgetTests` | `T:V2SaveFaultInjectionTests`, `T:V2SaveBudgetTests` | L1 |

## 8. UI（UI）

| 需求 | 实现文件/类 | L1 测试 | 状态 |
|---|---|---|---|
| REQ-V2-UI-01 面板 ≤4Hz/脏标记 | `U:Panels/HdbfFarmPanelFragment*.cs` | `T:V2PanelContractTests` | L1 |
| REQ-V2-UI-02 失败原因具体 | `U:Panels/HdbfIndoorCommandBridge.cs`, `G:Components/HdbfIndoorCommandFailures.cs` | `T:V2PanelContractTests`, `T:V2GameplayWiringTests` | L1 |
| REQ-V2-UI-03 三语本地化键齐全 | `D:Localizations/*.csv`, `U:Localization/HdbfUiLocKeys.cs` | `T:V2LocalizationTests` | L1 |
| REQ-V2-UI-04 清除 v1 术语 | `D:Localizations/*.csv`, `U:` | `T:V2LocalizationTests`, `T:V2NfrReleaseGateMatrixTests` | L1 |

## 9. 美术（ART）

| 需求 | 实现文件/类 | L1 测试 | 实机 | 状态 |
|---|---|---|---|---|
| REQ-V2-ART-01 外包络 ≤3×3×1/屋顶平 | `art/gen_models_v2.py`, `D:*.timbermesh` | `T:ArtModelContractTests`（128 项校验全过） | TC-V2-02 | L1 |
| REQ-V2-ART-02 16 个 timbermesh/无 slot 节点 | `D:Buildings/Food/BoxFarm/`、`art/validate_timbermesh.py` | `T:ArtModelContractTests` | TC-V2-10 | L1 |
| REQ-V2-ART-03 施工灰盒 | `art/models/*/ConstructionStage0.*`, `D:*.ConstructionStage0.Model.timbermesh` | `T:ArtModelContractTests` | TC-V2-10 | L1 |
| REQ-V2-ART-04 四档 50% 可辨 + 预览图 | `art/previews/`（tier A/B、50% grid、iso 渲染） | `T:ArtModelContractTests` | TC-V2-10 | L1（用户确认 pending） |

## 10. 兼容/性能/测试（COMPAT/PERF/TEST）

| 需求 | 实现文件/类 | L1 测试 | 实机 | 状态 |
|---|---|---|---|---|
| REQ-V2-COMPAT-01 适配器契约 | `I:Adapters/*`, `C:Compat/*` | `T:CompatAnchorTableTests`, `T:V2AdapterHostContractTests` | TC-V2-09 | L1 |
| REQ-V2-COMPAT-02 缺模组隐藏/置灰 | `I:Adapters/IndoorAdapterHost.cs`, `U:Panels/` | `T:V2AdapterHostContractTests`, `T:V2PanelContractTests` | TC-V2-09 | L1 |
| REQ-V2-COMPAT-03 叠放+Hooman Stairs | 蓝图叠放 + 适配器降级 | `T:V2BlueprintContractTests` | TC-V2-09 | L1（实机 pending） |
| REQ-V2-PERF-01 100×600 稳定 | `C:IndoorDayTick.cs`（纯数组遍历） | `T:V2PerformanceTests`, `T:V2NfrPerformanceTests` | TC-V2-08 | L1（实机 Profiler pending） |
| REQ-V2-PERF-02 日 tick 无临时分配 | `C:IndoorDayTick.cs`（0 bytes 实测） | `T:V2PerformanceTests` | — | L1 |
| REQ-V2-PERF-03 叠放不抖动 | 蓝图/游戏行为 | —（实机项） | TC-V2-08 | pending |
| REQ-V2-TEST-01 L1 全自动零游戏依赖 | `T:` 全部（501 条，tests 只引用 Core） | 门禁 `build.ps1 -G-01` | — | L1 |
| REQ-V2-TEST-02 实机证据留档 | `docs/VERIFICATION.md` | — | TC-V2-01..10 | pending |

## 11. NFR（READ/MAINT/EXT/SAFE/NFR）

| 需求 | 实现/证据 | 测试 | 状态 |
|---|---|---|---|
| REQ-V2-READ-01 文档集齐全 | `docs/v2/*.md`（六件套 + 头部字段） | `T:V2NfrDocumentationTests` | L1 |
| REQ-V2-READ-02 XML 注释/REQ 标记 | 全部 v2 文件头 + 公开 API 注释 | `T:V2NfrCodeContractTests` | L1 |
| REQ-V2-READ-03 日志键/术语/节流 | `C:IndoorLogKeys.cs` + `ThrottledLog` | `T:V2NfrCodeContractTests` | L1 |
| REQ-V2-READ-04 UI 失败原因/三语 | `D:Localizations/`、`U:Panels/` | `T:V2LocalizationTests`, `T:V2PanelContractTests` | L1 |
| REQ-V2-READ-05 CHANGELOG/ADR 同步 | `docs/v2/CHANGELOG.md`, `docs/v2/ADRS.md` | `T:V2NfrDocumentationTests` | L1 |
| REQ-V2-MAINT-01 Core 不引用游戏/无环 | 5 个 csproj 依赖方向 | `T:V2NfrCodeContractTests` | L1 |
| REQ-V2-MAINT-02 文件 ≤300 行 | 全部 v2 文件；超限需理由注释 | `T:V2NfrCodeContractTests` | L1 |
| REQ-V2-MAINT-03 日 tick 固定顺序/纯函数 | `C:IndoorDayTick.cs` | `T:V2TickOrderTests` | L1 |
| REQ-V2-MAINT-04 无静态可变状态/幂等 | `P:`, `G:`（无 static mutable） | `T:V2NfrCodeContractTests`, `T:V2GameplayContractTests` | L1 |
| REQ-V2-MAINT-05 清理调试代码/未登记标记 | 全库扫描 | `T:V2NfrCodeContractTests` | L1 |
| REQ-V2-EXT-01 档位/作物数据驱动 | `C:TierCatalog.cs`, `C:FallbackCropCatalog.cs`, `G:Components/GameCropCatalog.cs` | `T:V2CropCatalogTests` | L1 |
| REQ-V2-EXT-02 接口扩展点 | `C:IWaterSource/IInventorySink/IIndoorAdapterHost` | `T:V2CropCatalogTests`, `T:V2AdapterHostContractTests` | L1 |
| REQ-V2-EXT-03 迁移链可追加/默认值 | `C:IndoorSaveMigrator.cs` | `T:V2SaveMigrationTests` | L1 |
| REQ-V2-EXT-04 配置 schema/范围/默认 | `C:Config/HdbfConfig*`（schema 2 + v1 迁移） | `T:ConfigCodecTests` | L1 |
| REQ-V2-SAFE-01 边界 try/catch/节流日志 | `C:IndoorDayTick.cs`, `P:IndoorEntitySaveBridge.cs`, `G:` 控制器 | `T:V2GameplayContractTests`, `T:V2SaveFaultInjectionTests` | L1 |
| REQ-V2-SAFE-02 单槽/单建筑/单适配器隔离 | `C:IndoorDayTick.cs`（单槽 try/catch）、`I:Adapters/IndoorAdapterHost.cs`（3 次失败禁用） | `T:V2SlotFaultIsolationTests`, `T:V2AdapterHostContractTests` | L1 |
| REQ-V2-SAFE-03 日 tick 无分配/有界循环/幂等 | `C:IndoorDayTick.cs`（0 bytes）、`G:` 删除幂等 | `T:V2PerformanceTests`, `T:V2GameplayContractTests` | L1 |
| REQ-V2-SAFE-04 null 检查/销毁 no-op | `C:`, `P:`, `G:` 全部边界 | `T:V2SlotFaultIsolationTests`, `T:V2GameplayContractTests` | L1 |
| REQ-V2-SAFE-05 0 Harmony/反射失败降级 | `src/HDBF.Integration/Harmony` 已删除；`I:Adapters/IndoorAdapterHost.cs` | `T:V2AdapterHostContractTests`, `build.ps1` 0-Harmony 门禁 | L1 |
| REQ-V2-SAFE-06 配置非法回退默认 | `C:Config/HdbfConfigCodec.cs`（schema 2 迁移/未知键忽略） | `T:ConfigCodecTests` | L1 |
| REQ-V2-SAFE-07 UI 异常/缺数据安全占位 | `U:Panels/HdbfIndoorPanelFormat.cs`（“—”占位） | `T:V2PanelContractTests` | L1 |
| REQ-V2-SAFE-08 fault injection 全绿 | `T:V2SaveFaultInjectionTests`, `T:V2SlotFaultIsolationTests`, `T:V2GameplayWiringTests` | `T:V2NfrReleaseGateMatrixTests` | L1 |
| REQ-V2-NFR-01 阶段 1 先落 NFR 基线 | 本文件 + 文档集 + Core 接口/校验/异常边界 | `T:V2NfrDocumentationTests` | L1 |
| REQ-V2-NFR-02 门禁失败不发布 | `build.ps1` 0-Harmony/文档/存档/性能门禁 | `T:V2NfrReleaseGateMatrixTests` | L1 |
| REQ-V2-NFR-03 Lead 阶段 4 汇总证据 | `docs/VERIFICATION.md` v2 段 | 门禁 + 实机记录 | pending（实机部分） |

## 12. 删除项（v1 → v2，验证“已删除”）

| v1 资产 | 处理 | 证据 |
|---|---|---|
| `src/HDBF.Core/Plots/**`（扫描/注册/高亮/保留） | **已删除** | `T:V2NfrCodeContractTests`（路径不存在） |
| `src/HDBF.Core/Save/**` + `HDBF.Persistence/HdbfEntitySaveBridge.cs` | **已删除**（v2 桥迁入 `P:Indoor/`） | `T:V2PersistenceBridgeContractTests` |
| `src/HDBF.Gameplay/Farming/FarmWorkPlanner.cs`、`HdbfPlotScanner.cs` | **已删除** | `T:V2GameplayContractTests` |
| `src/HDBF.Integration/Harmony/**`（ADR-009 补丁） | **已删除**（0 Harmony） | `build.ps1` 0-Harmony 门禁 + `T:V2NfrReleaseGateMatrixTests` |
| 工作范围/托管格/开垦/高亮 本地化键与文案 | **已删除/替换** | `T:V2LocalizationTests` |
| `docs/workstreams/*` v1 工作流文档 | 归档保留（历史证据） | `docs/v2/CHANGELOG.md` |
