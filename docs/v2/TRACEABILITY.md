# HDBF v2 需求追溯矩阵

> **状态**：draft（阶段 1 初始矩阵）｜**日期**：2026-10-26｜**适用版本**：0.2.0
> **变更摘要**：`REQ-V2-*` ↔ 代码文件/类 ↔ L1/实机测试 的唯一矩阵。
> 状态：`pending`（未实现）/ `L1`（离线测试绿）/ `GAME`（实机已验）/ `n/a`。没有测试映射的需求不得标已实现。

路径约定：`C:` = `src/HDBF.Core/Indoor/`，`P:` = `src/HDBF.Persistence/Indoor/`，
`G:` = `src/HDBF.Gameplay/`，`U:` = `src/HDBF.UI/`，`I:` = `src/HDBF.Integration/`，
`T:` = `tests/HDBF.Tests/`，`D:` = `data/`。

## 1. 建筑（BLD）

| 需求 | 实现文件/类 | L1 测试 | 实机 | 状态 |
|---|---|---|---|---|
| REQ-V2-BLD-01 3×3×1 占地 | `D:Buildings/Food/BoxFarm/BoxFarm.*.blueprint.json`（BlockObjectSpec.Size 3×3×1） | `T:V2BlueprintContractTests` | TC-V2-01 | pending |
| REQ-V2-BLD-02 入口通路/堵塞暂停 | `G:Components/IndoorFarmController.*.cs`（入口检查） | `T:V2EntranceGateTests` | TC-V2-01 | pending |
| REQ-V2-BLD-03 平屋顶 | `art/gen_models_v2.py`、`D:*.timbermesh` | `T:ArtModelContractTests`（屋顶 30cm 无几何） | TC-V2-02 | pending |
| REQ-V2-BLD-04 屋顶可叠放 | 蓝图 `Stackable:BlockObject` + Top/Floor + GenerateFloors | `T:V2BlueprintContractTests` | TC-V2-02 | pending |
| REQ-V2-BLD-05 拆除语义 | `G:Components/IndoorFarmController.cs`（persistent 删除） | `T:V2DeleteIdempotencyTests` | TC-V2-01 | pending |
| REQ-V2-BLD-06 8 蓝图/端口/模型 | `D:Buildings/...`、`tools/validate` | `T:V2BlueprintContractTests` | TC-V2-10 | pending |
| REQ-V2-BLD-07 叠放门禁（报告用户） | `docs/v2/ADRS.md` ADR-V2-002、`docs/VERIFICATION.md` | —（流程门禁） | TC-V2-02 | pending |

## 2. 核心（CORE）

| 需求 | 实现文件/类 | L1 测试 | 状态 |
|---|---|---|---|
| REQ-V2-CORE-01 槽位容量/P_eff | `C:IndoorFarmRecord.cs`, `C:IndoorLaborMath.cs` | `T:V2SlotCapacityTests`, `T:V2LaborTests` | pending |
| REQ-V2-CORE-02 读档槽位补齐/截断 | `C:IndoorSaveValidator.cs` | `T:V2SaveRepairTests` | pending |
| REQ-V2-CORE-03 禁止世界作物/田块 API | `C` 无游戏引用；`T:V2ArchitectureTests` | `T:V2ArchitectureTests` | pending |
| REQ-V2-CORE-04 纯逻辑可测 | `C:` 全部（仅 BCL） | `T:V2ArchitectureTests` | pending |
| REQ-V2-CORE-05 单槽隔离/节流 | `C:IndoorDayTick.cs` | `T:V2SlotFaultIsolationTests` | pending |

## 3. 作物/生长（CORE + 平衡）

| 需求 | 实现文件/类 | L1 测试 | 状态 |
|---|---|---|---|
| §19.5 生长公式 | `C:IndoorGrowthMath.cs`, `C:IndoorDayTick.cs` | `T:V2GrowthTests` | pending |
| §19.5 轮作/疲劳 | `C:IndoorDayTick.cs`（fatigue 步骤） | `T:V2RotationFatigueTests` | pending |
| §19.5 模式倍率 | `C:IndoorConstants.cs`, `C:IndoorGrowthMath.cs` | `T:V2ModeTests` | pending |

## 4. 水（WATER）

| 需求 | 实现文件/类 | L1 测试 | 状态 |
|---|---|---|---|
| REQ-V2-WATER-01 覆盖率纯函数 | `C:IndoorWaterMath.cs` | `T:V2WaterTests`（含除零/负值） | pending |
| REQ-V2-WATER-02 每日一次扣水/有源补水 | `C:IndoorWaterMath.cs`, `C:IndoorDayTick.cs` | `T:V2WaterTests` | pending |
| REQ-V2-WATER-03 面板水字段 | `C:IndoorFarmSnapshot.cs`, `U:Panels/` | `T:V2PanelContractTests` | pending |
| REQ-V2-WATER-04 人工补水动作/可达 | `C:IndoorDayTick.cs`, `G:Components/IndoorFarmController.*.cs` | `T:V2ManualRefillTests` | pending |

## 5. 劳动力（LABOR）

| 需求 | 实现文件/类 | L1 测试 | 状态 |
|---|---|---|---|
| REQ-V2-LABOR-01 动作预算/收获优先 | `C:IndoorLaborMath.cs` | `T:V2LaborTests` | pending |
| REQ-V2-LABOR-02 无路径工时 | `C:IndoorLaborMath.cs` | `T:V2LaborTests` | pending |
| REQ-V2-LABOR-03 原版工作位门禁 | `G:Components/IndoorFarmController.*.cs` | `T:V2WorkplaceContractTests` | pending |
| REQ-V2-LABOR-04 室内工作/无外部农田 | `G:Components/IndoorFarmController.*.cs` | 实机 TC-V2-03 | pending |

## 6. 平衡/维护/动力（BAL）

| 需求 | 实现文件/类 | L1 测试 | 状态 |
|---|---|---|---|
| REQ-V2-BAL-01 单一汇总乘算 | `C:IndoorMultipliers`（唯一乘法点） | `T:V2BalanceTests` | pending |
| REQ-V2-BAL-02 停摆只停农事 | `C:IndoorDayTick.cs` | `T:V2MaintenanceTests` | pending |
| REQ-V2-BAL-03 世界警告图标/面板原因 | `G:Components/HdbfWarnings*.cs`(v2), `U:Panels/` | `T:V2WarningTests` | pending |

## 7. 存档（SAVE）

| 需求 | 实现文件/类 | L1 测试 | 状态 |
|---|---|---|---|
| REQ-V2-SAVE-01 schema v2 往返/修复 | `C:IndoorSaveCodec.cs`, `C:IndoorSaveValidator.cs`, `P:IndoorEntitySaveBridge.cs` | `T:V2SaveCodecTests` | pending |
| REQ-V2-SAVE-02 v1→v2 迁移 | `C:IndoorSaveMigrator.cs`, `P:IndoorEntitySaveBridge.cs` | `T:V2SaveMigrationTests` | pending |
| REQ-V2-SAVE-03 读档一致性/帧率无关 | `C:IndoorDayTick.cs` | `T:V2DeterminismTests` | pending |
| REQ-V2-SAVE-04 写前校验 | `C:IndoorSaveValidator.cs`, `P:IndoorEntitySaveBridge.cs` | `T:V2SaveValidationTests` | pending |
| REQ-V2-SAVE-05 迁移失败安全 | `C:IndoorSaveMigrator.cs` | `T:V2SaveMigrationTests` | pending |
| REQ-V2-SAVE-06 读档不因坏字段失败 | `P:IndoorEntitySaveBridge.cs` | `T:V2SaveFaultInjectionTests` | pending |
| REQ-V2-SAVE-07 损坏/迁移/往返/预算全绿 | 上述 + `T:V2SaveBudgetTests` | `T:V2SaveFaultInjectionTests`, `T:V2SaveBudgetTests` | pending |

## 8. UI（UI）

| 需求 | 实现文件/类 | L1 测试 | 状态 |
|---|---|---|---|
| REQ-V2-UI-01 面板 ≤4Hz/脏标记/无抖动 | `U:Panels/HdbfFarmPanelFragment.cs` | `T:V2PanelContractTests` | pending |
| REQ-V2-UI-02 失败原因具体 | `U:Panels/`, `G:Components/...Command*` | `T:V2PanelContractTests` | pending |
| REQ-V2-UI-03 三语本地化键齐全 | `D:Localizations/*.csv`, `U:Localization/HdbfUiLocKeys.cs` | `T:V2LocalizationTests` | pending |
| REQ-V2-UI-04 清除 v1 术语 | `D:Localizations/*.csv`, `U:` | `T:V2TerminologyTests` | pending |

## 9. 美术（ART）

| 需求 | 实现文件/类 | L1 测试 | 实机 | 状态 |
|---|---|---|---|---|
| REQ-V2-ART-01 外包络 ≤3×3×1/屋顶平 | `art/gen_models_v2.py`, `D:*.timbermesh` | `T:ArtModelContractTests` | TC-V2-02 | pending |
| REQ-V2-ART-02 16 个 timbermesh/无 slot 节点 | `D:Buildings/Food/BoxFarm/`, `art/validate_timbermesh.py` | `T:ArtModelContractTests` | TC-V2-10 | pending |
| REQ-V2-ART-03 施工灰盒 | `art/models/ConstructionStage0`, `D:*.timbermesh` | `T:ArtModelContractTests` | TC-V2-10 | pending |
| REQ-V2-ART-04 四档 50% 可辨 + 预览图 | `art/previews/` | `T:ArtModelContractTests` | TC-V2-10 | pending |

## 10. 兼容/性能/测试（COMPAT/PERF/TEST）

| 需求 | 实现文件/类 | L1 测试 | 实机 | 状态 |
|---|---|---|---|---|
| REQ-V2-COMPAT-01 适配器契约 | `I:Adapters/`, `C:Compat/` | `T:CompatAnchorTableTests`（沿用） | TC-V2-09 | pending |
| REQ-V2-COMPAT-02 缺模组隐藏/置灰 | `U:Panels/`, `I:Detection/` | `T:V2PanelContractTests` | TC-V2-09 | pending |
| REQ-V2-COMPAT-03 叠放+Hooman Stairs | `G:`, `docs/VERIFICATION.md` | 实机 | TC-V2-09 | pending |
| REQ-V2-PERF-01 100×600 稳定 | `C:IndoorDayTick.cs`（数组遍历） | `T:V2PerformanceTests` | TC-V2-08 | pending |
| REQ-V2-PERF-02 日 tick 无临时分配 | `C:IndoorDayTick.cs`, `C:IndoorLaborMath.cs` | `T:V2PerformanceTests` | — | pending |
| REQ-V2-PERF-03 叠放不抖动 | 蓝图/游戏行为 | 实机 | TC-V2-08 | pending |
| REQ-V2-TEST-01 L1 全自动零游戏依赖 | `T:` 全部 | 门禁 | — | pending |
| REQ-V2-TEST-02 实机证据留档 | `docs/VERIFICATION.md` | — | TC-V2-01..10 | pending |

## 11. NFR（READ/MAINT/EXT/SAFE/NFR）

| 需求 | 实现/证据 | 测试 | 状态 |
|---|---|---|---|
| REQ-V2-READ-01 文档集齐全 | `docs/v2/*.md` | NFR-GATE-01 检查脚本/卡 | pending |
| REQ-V2-READ-02 XML 注释/REQ 标记 | 全部新文件 | `T:V2NfrContractTests` | pending |
| REQ-V2-READ-03 日志键/术语/节流 | `C:IndoorLogKeys.cs` | `T:V2LogKeyTests` | pending |
| REQ-V2-READ-04 UI 失败原因/三语 | `D:Localizations/` | `T:V2LocalizationTests` | pending |
| REQ-V2-READ-05 CHANGELOG/ADR 同步 | `docs/v2/CHANGELOG.md`, `docs/v2/ADRS.md` | NFR-GATE-02 | pending |
| REQ-V2-MAINT-01 Core 不引用游戏/无环 | `src/*/*.csproj` | `T:V2ArchitectureTests` | pending |
| REQ-V2-MAINT-02 文件 ≤300 行 | 全部 | `T:V2NfrContractTests` | pending |
| REQ-V2-MAINT-03 日 tick 固定顺序/纯函数 | `C:IndoorDayTick.cs` | `T:V2TickOrderTests` | pending |
| REQ-V2-MAINT-04 无静态可变状态/幂等 | `P:`, `G:` | `T:V2DeleteIdempotencyTests` | pending |
| REQ-V2-MAINT-05 清理调试代码/TODO | 全部 + `build.ps1` G-08 | 门禁 | pending |
| REQ-V2-EXT-01 档位/作物数据驱动 | `C:TierCatalog.cs`, `C:FallbackCropCatalog.cs` | `T:V2CropCatalogTests` | pending |
| REQ-V2-EXT-02 接口扩展点 | `C:IWaterSource/IInventorySink` | `T:V2ExtensionPointTests` | pending |
| REQ-V2-EXT-03 迁移链可追加/默认值 | `C:IndoorSaveMigrator.cs` | `T:V2SaveMigrationTests` | pending |
| REQ-V2-EXT-04 配置 schema/范围/默认 | `C:Config/HdbfConfig*` | `T:ConfigCodecTests`（沿用） | pending |
| REQ-V2-SAFE-01..08 崩溃安全 | 见 `docs/v2/INTERFACES.md` §10 | `T:V2FaultInjectionTests` | pending |
| REQ-V2-NFR-01 阶段 1 先落 NFR 基线 | 本次文档集 + 卡 | NFR-GATE-01..03 | pending |
| REQ-V2-NFR-02 门禁失败不发布 | `docs/VERIFICATION.md` | 门禁 | pending |
| REQ-V2-NFR-03 Lead 阶段 4 汇总证据 | `docs/VERIFICATION.md` | 门禁 | pending |

## 12. 删除项（v1 → v2，验证“已删除”）

| v1 资产 | 处理 | 证据 |
|---|---|---|
| `src/HDBF.Core/Plots/**`（扫描/注册/高亮/保留） | 删除 | `T:V2ArchitectureTests` 断言文件不存在/无引用 |
| `src/HDBF.Gameplay/Components/HdbfPlotScanner*.cs` | 删除 | 同上 |
| `src/HDBF.Gameplay/Components/HdbfBoxFarmController.Scan.cs` | 删除 | 同上 |
| `src/HDBF.Integration/Harmony/**`（ADR-009 补丁） | 删除（0 Harmony） | `T:V2ArchitectureTests` + 门禁 G-08 patch 数=0 |
| 工作范围 / 托管格 / 开垦 本地化键与文案 | 删除/替换 | `T:V2TerminologyTests` |
| `docs/workstreams/*` v1 工作流文档 | 归档标注 | `docs/v2/CHANGELOG.md` |
