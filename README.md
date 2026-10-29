# 高密度盒子农场（HDBF）— Timberborn 模组

> 按需求蓝图 §19（v2.1）实现的 Timberborn 1.1.2.8 模组：**3×3×1 室内高密度农场**。
> 本工程不是文档摘要，而是可构建、可测试、可安装的代码与数据。
>
> **当前版本 2.5.0（v2 室内版）**：四档 3×3×1 建筑，内部 60/150/300/600 个抽象槽位，
> 平屋顶可叠放，内部水罐 + 管道/人工补水；v1 的世界地块扫描、托管格、原版种植派发、
> 地块高亮、作物可见性补丁与 Harmony 补丁**已全部删除（0 Harmony）**。
> v2 设计入口：[docs/v2/README.md](docs/v2/README.md)、需求唯一来源 [§19](docs/REQ_V2_SECTION_19.md)。

| 项目 | 值 |
|---|---|
| 模组版本 | **2.5.0（v2）** |
| 目标游戏 | Timberborn 1.1.2.8（`1.1.2.8-ffc2de0-sw`，Unity 6000.5.11f1） |
| 存档 schema | **2**（每建筑一条原生持久化记录，含 v1→v2 迁移；见 ADR-V2-003） |
| 配置 schema | **2**（v1 配置可读并迁移；未知键忽略） |
| 程序集 | `HDBF.Core` / `HDBF.Persistence` / `HDBF.Gameplay` / `HDBF.Integration` / `HDBF.UI` |
| Harmony 补丁 | **0** |
| L1 测试 | **601**（零游戏依赖，`FAILED 0`） |
| 发布门禁 | **PASSED (44 checks)** + `NFR GATE MATRIX: PASSED` |

## 1. 这是什么

四档「盒子农场」建筑（基础/中级/高级/顶级），把大量分散农田压缩进一栋 **3×3×1** 的建筑内部：

- 基础版 60 槽 / 2 工人 / C_min 50% / 水罐 3.6
- 中级版 150 槽 / 4 工人 / 60% / 10.8
- 高级版 300 槽 / 8 工人 / 75% / 33.6
- 顶级版 600 槽 / 16 工人 / 80% / 76.8 + 150 动力（或离网 15 金属/8 日）

作物在建筑**内部槽位**模拟生长（读取原版作物 spec 的生长天数/产量/产物），
农民只占建筑工作位；水由**内部水罐**提供（管道自动补水或工人人工补水）；
产物进入建筑输出库存后由原版搬运/传送带送出。模型屋顶是**平的、可通行、可叠放建筑**。

- 面板新增 **「正在生长」** 卡（2.5.0）：每个当前占用的作物一行（作物名、生长中/待收槽数、
  平均生长进度条），**第一格种下就出现**并随面板 4 Hz 实时更新，不再等种植槽种满；无作物时显示空状态。

代价（防作弊门禁）：无工人不作业、无水不生长、无维护会减产/停摆、无动力顶级减产；
产物只能来自成熟槽位的模拟收获。

## 2. 目录

```
mod/
├── HDBF.sln                    # 5 个游戏程序集 + 测试 + API 索引工具
├── src/
│   ├── HDBF.Core/Indoor/       # v2 纯逻辑：槽位/生长/水/劳动力/维护/动力/存档/快照
│   ├── HDBF.Persistence/Indoor/# v2 实体持久化桥（写前校验/失败安全/v1 迁移）
│   ├── HDBF.Gameplay/          # 室内控制器、日 tick 编排、作物目录、工作位、仓库桥
│   ├── HDBF.Integration/       # 5 个软依赖适配器 + IIndoorAdapterHost（管道补水）
│   └── HDBF.UI/                # v2 面板（槽位/水罐/维护/动力/策略）+ 三语本地化
├── tests/HDBF.Tests/           # L1 测试（零 NuGet，自带 runner；含 NFR 门禁矩阵）
├── data/                       # 8 份蓝图、16 个 .timbermesh、三语系、配置、图标
├── art/                        # Blender 程序化模型生成器 + 验证/预览报告
├── tools/                      # ApiIndex / validate / release gates / 实机回合脚本
├── docs/v2/                    # v2 文档集（架构/接口/ADR/追溯/变更）
└── docs/VERIFICATION.md        # 实际验证记录（含 v2 §26 实机证据）
```

## 3. 构建

> **工作区不是安装目录**：仓库只放源码/数据/文档，构建产物与 staged 包写到工作根；
> 安装包部署到 `%USERPROFILE%\Documents\Timberborn\Mods\HDBF`。
>
> 本机沙箱下 `%USERPROFILE%\.hdbf\work` 不可写，使用 `%TEMP%\hdbf-<tag>` 作为工作根（ADR-V2-005）。

前置：.NET SDK 9（已验证 9.0.305）、游戏已安装（默认
`C:\Program Files (x86)\Steam\steamapps\common\Timberborn`）。

```powershell
# 全量门禁：5 程序集 → L1 601 → data 校验 → 打包 → 44 条 release gates
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -WorkRoot "$env:TEMP\hdbf-work"

# + 部署到 Documents\Timberborn\Mods\HDBF
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -WorkRoot "$env:TEMP\hdbf-work" -Install

# 逐工程（任何工程都不得声明 2 个以上 ProjectReference；必须 -m:1）
dotnet build src/HDBF.Core/HDBF.Core.csproj -c Release -m:1 -p:HdbfWorkRoot="$env:TEMP\hdbf-work"
dotnet run   --project tests/HDBF.Tests -c Release -m:1 -p:HdbfWorkRoot="$env:TEMP\hdbf-work"
```

> `-m:1` 是硬要求：本机并行 MSBuild 会在 NuGet restore 阶段静默失败（退出码 1、无错误信息）。

## 4. 安装与实机验证

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -WorkRoot "$env:TEMP\hdbf-work" -Install
```

游戏主菜单 → Mods 启用 **High Density Box Farm** 并重启；四档建筑在 **Food** 分类末尾。
自动化实机回合（无合成输入，`-skipModManager` + 存档副本）：

```powershell
.\tools\dev\Invoke-GameRound.ps1 -Action Prepare -Tag round
.\tools\dev\Invoke-GameRound.ps1 -Action Launch  -Tag round
.\tools\dev\Invoke-GameRound.ps1 -Action SpeedUp -Seconds 240 -Tag round
.\tools\dev\Invoke-GameRound.ps1 -Action Stop    -Tag round
```

> 本机执行策略会阻止未签名脚本，实际命令需用
> `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\dev\Invoke-GameRound.ps1 ...`。

v2 fixture（预植槽位 + schema-v2 payload）：

```powershell
dotnet run --project tools/dev/CraftIndoorFixture -c Release --property:HdbfWorkRoot="$env:TEMP\hdbf-fixture" -- `
  --tier Basic --crop Carrot --growth 3.9 --planted 60 --tank full --out "$env:USERPROFILE\.hdbf\round\indoor-payload.txt"
python .\tools\dev\fixtures\craft-farm-fixture.py --out "$env:USERPROFILE\Documents\Timberborn\Saves\HDBFTest" `
  --indoor-payload "$env:USERPROFILE\.hdbf\round\indoor-payload.txt" --tier BoxFarm.Basic.Folktails
```

## 5. 验证边界（请务必读）

| 已验证（离线） | 方式 |
|---|---|
| 5 程序集对真实游戏 1.1.2.8 程序集编译 | `build.ps1`：0 warning / 0 error |
| v2 Core 公式/日 tick/存档/迁移/损坏注入/性能预算 | `tests/HDBF.Tests`：**601 条** L1 断言全绿 |
| 100×600 日 tick 0.0113 ms/栋、存档 1.05 MB、编码 34.8 ms、30/144 fps 一致 | `V2NfrPerformanceTests` |
| 3×3×1 蓝图（9 块、Stackable、建造 1/1.5/2.5/4 工日） | `V2BlueprintContractTests` + `tools/validate` |
| 16 个 `.timbermesh` 3.000×1.000×3.000、平屋顶、3 端口、无 slot 节点 | 128/128 美术校验 PASS + 契约测试 |
| 0 Harmony、Core 无游戏引用、依赖方向、文件规模、文档 71/71 追溯 | `V2Nfr*Tests` + 44 条发布门禁 |
| 三语 191 键一致、无 v1 术语、失败原因具体 | `V2LocalizationTests` / `V2PanelContractTests` |
| v2 实机：加载、室内日循环、水耗/覆盖率、收获/播种/积压、缺模组降级 | round41/42（`docs/VERIFICATION.md` §26） |

| 未验证（需可互动游戏会话） | 原因 |
|---|---|
| 真实建造菜单放置/施工阶段/拆除 | 本机无法合成 UI 输入（已记录原因） |
| **屋顶叠放 A/B（发布门槛）** | 蓝图已具备 Stackable 字段，但未在实机往屋顶放原版建筑 |
| 面板打开/按钮/失败原因/三语显示 | 需要点击建筑面板 |
| 人工补水正路径、维护材料扣料、顶级接电、管道实际供水 | 需对应档位/模组 + 可互动会话 |
| 游戏内存档往返（AC-06）、100 栋 Profiler、五模组矩阵、50% 四档同框 A/B | 需更长的实机会话 |
| 玩家 v1 存档实机迁移 | 已有 L1 迁移测试与 fixture payload 修复证据，缺真实旧档副本 |

> 「编译通过」不等于「实机验证」；未实机项在 `docs/v2/TRACEABILITY.md` 与 `docs/VERIFICATION.md` 中
> 明确标为 pending，不标完成。

## 6. 文档

| 文档 | 内容 |
|---|---|
| [docs/v2/README.md](docs/v2/README.md) | v2 一页索引：目标、模块地图、构建/测试、阶段与门禁 |
| [docs/v2/ARCHITECTURE.md](docs/v2/ARCHITECTURE.md) | 分层、日 tick 固定顺序、数据流、日志键/性能预算 |
| [docs/v2/INTERFACES.md](docs/v2/INTERFACES.md) | v2 Core 冻结接口、不变量、存档/安全契约 |
| [docs/v2/ADRS.md](docs/v2/ADRS.md) | ADR-V2-001 室内农场 / 002 平屋顶叠放 / 003 存档 v2 / 004 水模型 / 005 构建沙箱 |
| [docs/v2/TRACEABILITY.md](docs/v2/TRACEABILITY.md) | `REQ-V2-*` ↔ 代码 ↔ L1/实机测试 矩阵 |
| [docs/v2/CHANGELOG.md](docs/v2/CHANGELOG.md) | v1→v2 行为/数据/迁移变更 |
| [docs/VERIFICATION.md](docs/VERIFICATION.md) | 实际验证记录（§26 为 v2 实机证据与门禁结论） |
| [docs/API_FACTS.md](docs/API_FACTS.md) | 从真实程序集读出的 API/构建限制事实 |
| [docs/REQ_V2_SECTION_19.md](docs/REQ_V2_SECTION_19.md) | 需求唯一来源（§19 v2.1） |

## 7. 已知偏离（v2 摘要）

1. **存档落地**：每个建筑通过原生实体持久化保存一条 schema-v2 校验文本负载
   （`HDBF.IndoorFarm.Payload`），而非全局 `HDBF` 节点；原因与等价性见 ADR-V2-003。
2. **规格字段名**：`HdbfBoxFarmSpec` 的 JSON 字段沿用 v1 名称（`PlotCapacity` 等）以保证
   数据/存档兼容；v2 语义由 XML 注释与 `TierProfile` 别名表达。
3. **工作位表现**：v2 农事在建筑内部模拟，农民保持原版就业/工作位占用；不派发原版种植/收获任务，
   因此没有世界农田动画（原版「工作」动画由通用工作位行为承担，近似项）。
4. **人工补水的路径检查**：走原版可访问性组件；注入实体缺少这些组件时正路径无法验证
   （fixture 限制，见 §26.3）。
5. **构建工作根**：本机沙箱使用 `%TEMP%\hdbf-<tag>`（ADR-V2-005），不改变发布包结构。
6. **v1 存档**：尽力迁移（槽位/水罐/维护/模式/端口）；旧外部田块/作物保留为普通原版农田，不删除。

---

构建/测试/验证事实与偏离记录以 `docs/VERIFICATION.md`、`docs/v2/TRACEABILITY.md` 为准。
