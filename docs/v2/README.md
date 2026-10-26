# HDBF v2 — 室内高密度盒子农场（v2.1）

> **状态**：阶段 1–3 完成；阶段 4 部分完成（离线门禁全绿，实机证据见 `docs/VERIFICATION.md` §26）
> **日期**：2026-10-26
> **适用版本**：模组 **0.2.0** / 游戏 Timberborn 1.1.2.8 / 存档 schema 2 / 配置 schema 2
> **变更摘要**：v2 以需求 `docs/REQ_V2_SECTION_19.md`（唯一事实来源）为蓝本，替换 v1 的外部农田实现；本文件是 v2 文档集的一页索引。

## 1. 目标（一句话）

四档 **3×3×1** 建筑，作物在**建筑内部的 60/150/300/600 个抽象槽位**里模拟生长，
农民只占建筑工作位；水面由**内部水罐 + 管道/人工补水**提供；**屋顶是平的、可叠放建筑**。
v2 **不扫描世界格、不注册地块、不生成原版作物实体、不新增 Harmony 补丁**。

## 2. 模块地图（5 个程序集，不新增）

| 程序集 | v2 目录 | 职责 | 允许引用 |
|---|---|---|---|
| `HDBF.Core` | `Indoor/` | 槽位状态机、生长/水/疲劳/维护/动力公式、作物目录、存档记录/校验/迁移、面板快照、纯函数 | 仅 BCL（**禁止** UnityEngine / Timberborn） |
| `HDBF.Persistence` | `Indoor/` | schema v2 实体读写、v1→v2 迁移、写前校验、失败安全 | Core + 游戏持久化接口 |
| `HDBF.Gameplay` | `Components/`, `Specs/` | `IndoorFarmController` 日 tick 编排、工人/水/库存/维护桥接、存档脏标记 | Core、Persistence、游戏程序集 |
| `HDBF.UI` | `Panels/` | 面板快照呈现、按钮命令、tooltip、三语本地化 | Core、UI 框架 |
| `HDBF.Integration` | `Adapters/` | 管道/动力/物流适配器（沿用 `ICompatAdapter`，`IWaterSource`） | Core、反射访问外部模组 |

依赖方向：`Core ← Persistence ← Gameplay`；`UI/Integration → Core`。禁止反向引用与循环（NFR-GATE-03）。

## 3. 构建 / 测试命令（本机沙箱约束）

工作区 `C:\Users\user\Documents\Timberborn\Mods\HighDensityBoxFarm` **不是** mod 安装目录；
构建产物必须放临时目录（`%TEMP%\hdbf-work`），**不要**写 `%USERPROFILE%\.hdbf\work`（当前沙箱拒绝写入）。
每个并行任务使用**独立** work root，避免 MSBuild 冲突。

```powershell
# 单工程编译（快）
dotnet build src/HDBF.Core/HDBF.Core.csproj -c Release -m:1 -p:HdbfWorkRoot="$env:TEMP\hdbf-work-<tag>"

# L1 测试（零 NuGet）
dotnet run --project tests/HDBF.Tests -c Release -m:1 -p:HdbfWorkRoot="$env:TEMP\hdbf-work-<tag>"

# 全量门禁（5 程序集 + L1 + validate + 打包 + 55 条 release gates）
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -WorkRoot "$env:TEMP\hdbf-work-gate"
```

**必须带 `-m:1`**（并行 MSBuild 在本机 restore 阶段静默失败）。不得声明 2 个以上 `ProjectReference`。

## 4. 阶段计划与自动化任务

| 阶段 | 内容 | 自动化任务卡 | 状态 |
|---|---|---|---|
| W1 | v2 Core 纯逻辑 + 存档 | `V2-CORE-01`, `V2-CORE-02` | **完成**（L1 全绿） |
| W2 | 3×3×1 美术模型 + 蓝图/规格/配置 | `V2-ART-01`, `V2-DATA-01` | **完成**（128 项美术校验 PASS） |
| W3 | 存档桥 + 室内控制器 + 净删 v1 地块链路 | `V2-SAVE-01`, `V2-GAMEPLAY-01/02`, `V2-CLEANUP-01` | **完成** |
| W4 | UI 面板 v2 + 三语本地化 + 世界图标 | `V2-UI-01` | **完成**（L1 契约） |
| W5 | 适配器（管道水/动力/物流） | `V2-INTEG-01` | **完成**（反射锚点 + 降级） |
| W6 | NFR 门禁 + 文档收敛 + 实机验证 | `V2-GATE-01`, Lead | **离线完成；实机部分见 VERIFICATION §26** |

最终门禁（本机实测）：5 程序集 0/0、**L1 PASSED 529 / FAILED 0**、`tools/validate RESULT: OK`、
**RELEASE GATES: PASSED (44 checks)**（0 Harmony）、`NFR GATE MATRIX: PASSED`。

## 5. 需求入口

* 需求唯一来源：[`../REQ_V2_SECTION_19.md`](../REQ_V2_SECTION_19.md)（含 §19.18 NFR）
* 架构与日 tick 顺序：[`ARCHITECTURE.md`](ARCHITECTURE.md)
* 冻结接口与不变量：[`INTERFACES.md`](INTERFACES.md)
* 架构决策：[`ADRS.md`](ADRS.md)
* 需求追溯：[`TRACEABILITY.md`](TRACEABILITY.md)
* 变更记录：[`CHANGELOG.md`](CHANGELOG.md)
* 实机证据：[`../VERIFICATION.md`](../VERIFICATION.md)

## 6. 文档纪律（§19.18.1）

* 每份文档头部写状态/日期/版本/变更摘要；文档间相对链接互引，同一事实只写一次。
* 变更使用 `【新增】/【修改】/【作废】/【待确认】` 标记。
* 无测试映射的需求不得标“已实现”；`TRACEABILITY.md` 是唯一矩阵。
