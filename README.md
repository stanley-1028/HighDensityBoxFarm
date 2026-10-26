# 高密度盒子农场（HDBF）— Timberborn 模组

> 按《[高密度盒子农场模组需求文档](../../../deepseek-harness/default-workspace/high-density-box-farm/高密度盒子农场模组需求文档.md)》蓝图实现的 Timberborn 1.1.2.8 模组。
> 本工程不是文档的摘要，而是可构建、可测试、可安装的代码与数据。
>
> **v2 室内版设计（2026-10-26，v2.1-draft）**：需求文档 **§19** 与 [docs/REQ_V2_SECTION_19.md](docs/REQ_V2_SECTION_19.md) 已改为 **3×3×1 室内高密度农场 + 平屋顶可叠建筑**，并新增 §19.18 非功能需求（可读性 / 可维护性 / 可扩展性 / 安全性）。
> **注意：当前安装与部署的 `0.1.0` 仍是 v1 外部农田实现；v2 代码/模型尚未开始迁移，以需求文档 §19 为设计基线。**

| 项目 | 值 |
|---|---|
| 模组版本 | 0.1.0（v1 已实现） / 0.2.0-draft（v2 目标） |
| 目标游戏 | Timberborn 1.1.2.8（`1.1.2.8-ffc2de0-sw`，Unity 6000.5.11f1） |
| 存档 schema | 1（v1，随建筑实体持久化，见 ADR-001） / 2（v2 设计，§19.10） |
| 配置 schema | 1（v1） / 2（v2 设计） |
| v2 设计 | 3×3×1 室内槽位、无外部农田、平屋顶可叠建筑（[需求 §19](docs/REQ_V2_SECTION_19.md)） |
| 程序集 | `HDBF.Core` / `HDBF.Persistence` / `HDBF.Gameplay` / `HDBF.Integration` / `HDBF.UI` |
| Harmony 补丁 | 0 |

## 1. 这是什么

四档 3×2×1 的「盒子农场」建筑：用中后期科技把大量分散农田压缩进有限工作范围集中管理，
但必须持续支付**劳动力、灌溉、维护、土壤疲劳**四类代价。建筑本身不产作物，只改变
「哪些格被管理、以什么效率被管理」。

- 基础版 60 格 / 2 农民 / 11×11 范围 / 无材料维护
- 中级版 150 格 / 4 农民 / 15×15 / 每 10 日 8 木板
- 高级版 300 格 / 8 农民 / 19×19 / 每 10 日 6 金属
- 顶级版 600 格 / 16 农民 / 27×27 / 150 动力或离网 15 金属每 8 日

全部数值集中在 `src/HDBF.Core/Balance/TierCatalog.cs` 与八份（四档 × 两阵营）蓝图 JSON，改数值不需要改代码。

## 2. 目录

```
mod/
├── HDBF.sln                    # 7 个工程（5 个游戏程序集 + 测试 + API 索引工具）
├── src/
│   ├── HDBF.Core/              # 纯逻辑：公式、档位表、配置、日志、兼容契约、存档记录
│   ├── HDBF.Persistence/       # 建筑存档读写（Timberborn 实体持久化）
│   ├── HDBF.Gameplay/          # Spec、控制器组件、日 tick、模组入口、DI 注册
│   ├── HDBF.Integration/       # 五个软依赖适配器 + 检测缓存 + 反射代理
│   └── HDBF.UI/                # 建筑面板数据与状态警告
├── tests/HDBF.Tests/           # L1 测试（零 NuGet 依赖，自带 runner）
├── data/                       # 随包发布：manifest、8 份阵营蓝图、本地化、配置、图标、美术契约
├── tools/ApiIndex/             # 读取游戏程序集元数据/IL 的离线工具（本工程的事实来源）
├── tools/validate/             # 数据一致性校验器（蓝图/本地化/配置/manifest）
├── docs/                       # API 事实、接口契约、架构决策、验证报告
└── build.ps1                   # 构建 + 测试 + 校验 + 打包（产物在 %USERPROFILE%\.hdbf\work）
```

## 3. 构建

> **工作区不是安装目录**：本仓库只放源码/数据/文档。`build.ps1` 把 obj/bin 与暂存包写到
> `%USERPROFILE%\.hdbf\work`，把安装包部署到 `%USERPROFILE%\Documents\Timberborn\Mods\HDBF`。
> 原因：游戏会递归加载 mod 目录下**所有** `*.dll` 与 `*.blueprint.json`，源码、原版参考蓝图和
> 构建产物一旦混进 mod 目录就会重复注册/重复加载。`build.ps1` 末尾有硬门禁检查这一点。

前置：.NET SDK 9（已验证 9.0.305）、游戏已安装（默认路径
`C:\Program Files (x86)\Steam\steamapps\common\Timberborn`，可用 `-TimberbornDir` 覆盖）。
原版参考蓝图/UI 解包在 `%USERPROFILE%\.hdbf\ref`（可用 `-RefDir` 覆盖），供校验器交叉核对，
不会随包发布。

```powershell
# 完整构建 + 测试 + 数据校验 + 打包（本机没有 pwsh，且执行策略默认阻止未签名脚本）
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1

# 构建 + 测试 + 校验 + 打包 + 部署到 Documents\Timberborn\Mods\HDBF
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -Install

# 或逐工程
dotnet build src/HDBF.Core/HDBF.Core.csproj -c Release -m:1
dotnet run   --project tests/HDBF.Tests -c Release -m:1
```

> **必须带 `-m:1`（单节点 MSBuild）**。这台机器上并行 MSBuild 会在 NuGet restore 阶段
> 静默失败（退出码 1、无任何错误信息），且任何工程不得声明 2 个以上 `ProjectReference`。
> 两个环境限制都记录在 [docs/API_FACTS.md](docs/API_FACTS.md) §6，装配链因此设计成
> `Core ← Persistence ← Gameplay`，`Integration`/`UI` 各自只引用 `Core`。

## 4. 安装

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -Install
# 等价于复制 %USERPROFILE%\.hdbf\work\stage\HDBF\* 到
# %USERPROFILE%\Documents\Timberborn\Mods\HDBF\
```

然后在游戏主菜单 → Mods 启用 **High Density Box Farm**，重启游戏。
四档建筑出现在建造菜单 **Food（食物）** 分类末尾（ToolOrder 90–93）。

> 本工作区目录（`Documents\Timberborn\Mods\HighDensityBoxFarm`）没有 `manifest.json`，游戏会把它
> 当作普通目录忽略；真正加载的是同级 `Mods\HDBF`。这是刻意的：工作区里既有源码也有原版参考数据，
> 不能被当作 mod 递归加载。

## 5. 验证边界（请务必读）

本工程在**无 Unity 编辑器、无网络**的环境中完成；自 2026-10-25 起，窗口化游戏内验证已能**全自动**
执行（`-skipModManager` + 存档副本 + `Invoke-GameRound.ps1`，无需任何鼠标/键盘注入），
详见 `docs/VERIFICATION.md` §16–§18 与 `docs/NEXT_ROUND.md`。

| 已验证 | 方式 |
|---|---|
| 五个程序集能对真实游戏 1.1.2.8 程序集编译 | `build.ps1`：5 程序集 0 warning / 0 error |
| 平衡公式、存档、配置、兼容、劳动力计划、面板数据契约 | `tests/HDBF.Tests`：**510 条** L1 断言全绿 |
| 存档记录修复/迁移/幂等/损坏注入/单建筑隔离 | L1 测试（3 次往返、200 轮确定性损坏扫描） |
| 蓝图/本地化/配置/manifest/科技门禁/并网 spec/输出库存 | `tools/validate`（660 个游戏程序集元数据 + 506 个 spec 类型 + **5/5 模块程序集断言**） |
| 使用的游戏 API 名称与签名真实存在 | `tools/ApiIndex` 从程序集元数据/IL 读取，见 docs/API_FACTS.md |
| 打包产物结构、部署完整性与“工作区不是 mod”门禁 | `build.ps1` staging + 安全门禁（本机产物 338 KB / 29 文件 / 5 dll / 10 蓝图 / 3 本地化，部署 SHA 一致） |
| **托管格注册**：首次扫描 60/60、连续 16 游戏日重扫保留 | round12 实机：`diag.scan.end registeredNow=60 kept=60`（46 行日誌，`docs/in-game/round12-Player.log`） |
| **P1 作物循环**：派工→播种→生长→收穫→產物入庫/搬運 | round12 实机：`gameplay.farm: harvested plot … crop Wheat rotation=True`（含 `suppressed=38/39` 的大量收穫）、截圖可見成熟麥田與頂部資源列產物 |
| 灌溉覆蓋率即時重算 | round12：首次掃描 `coverageAfterScan=1.000`；之後隨水分衰減 `0.667` |
| 模組載入、四檔藍圖轉換、注入建築實例化 | round 5–12 多輪 `[HDBF/Info]` 日誌，無 prefab/material 錯誤 |

| 未验证（需真实建造/更多游戏会话或额外工具链） | 原因 | 下一步 |
|---|---|---|
| **真实建造菜单放置与建造完成** | 目前所有实机农場都是存档注入实体；入口/工作位的真实放置流程未走 | 用用户实机会话在建造菜单放置四档各 1 座（输入注入在本机不可用） |
| 维护材料扣除 | 已实现“最近仓库库存事务”，但 Basic 档无材料维护，需中/高级档实机 | 建仓库放木板/金属后点“执行维护”，核对库存与倒计时 |
| 顶级版并网供电 | 已补 mechanical spec，坐标/朝向仍可能需实机微调 | 顶级接电网：`G_power` 1.0；断电后 0.6 + 离网维护 |
| 五个第三方模组适配器（实机矩阵） | 五个模组已从 Steam Workshop 定位并完成离线锚点核对（BUILD-03）；实机握手需安装后跑 `tools/dev/CompatMatrix.ps1` | 单载 + 逐一组合 + 全组合 + 缺失读档 |
| 建筑实际外观 | **已实机观察**（round 30–33）：四档自定义 `.timbermesh` 均成功载入并渲染，3×2×1、屋顶 6 槽、四档材质/构型可辨（截图见 `docs/in-game/round30-Basic-run.png`、`round31-Int-loaded.png`、`round32-Adv-run.png`、`round33-Top-run.png`）；材质仍是原版 atlas 采样，自订 PBR 材质待 Unity | 施工阶段模型外观、LOD 切换距离、50% 同框 A/B、近看端口细节 |
| 面板运行时与托管格高亮/世界警告图标 | 面板三数数据与契约已补（BUILD-01，L1 绿）；高亮/图标卡（BUILD-02/04）仍在待办 | 实机开面板核对四档数字；高亮/图标完成后看渲染 |
| 存档往返（实机 AC-06）、100 建筑性能、30 日压力 | 需要真实存读档与 Profiler | TC-10/TC-13/TC-14 |

> 自動實機說明：`tools/dev/Invoke-GameRound.ps1`（`Fixture` → `Prepare` → `Launch` → `SpeedUp` →
> `Stop`）用 `-skipModManager` 跳過首次載入的 Mods 對話框、以存檔副本自動讀檔、靜音並在結束後
> 還原音訊、把 `Player.log` 與摘要寫進 `docs/in-game/`。本機的合成滑鼠/鍵盤輸入無法送達任何視窗
> （`tools/dev/Test-InputInjection.ps1` 的可重現證據），所以整條流程刻意**不依賴 UI 點擊**；
> 需要點擊的項目（建造菜單放置、面板操作）仍留給使用者實機會話。
> 歷史 `tools/smoke/Invoke-GameSmoke.ps1` 仍可用於部署完整性與 log 分析。

## 6. 文档

| 文档 | 内容 |
|---|---|
| [docs/API_FACTS.md](docs/API_FACTS.md) | 从真实程序集读出的模组加载/蓝图/组件/服务事实，以及本机两个构建限制 |
| [docs/INTERFACES.md](docs/INTERFACES.md) | 五程序集之间冻结的接口契约 |
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | 分层、依赖方向、七条架构决策（含与需求文档的偏离说明） |
| [docs/REQ_TRACEABILITY.md](docs/REQ_TRACEABILITY.md) | 需求编号 → 代码/测试/数据 追溯矩阵（含 7 条记录在案的偏离） |
| [docs/VERIFICATION.md](docs/VERIFICATION.md) | 本次交付的实际验证记录与证据（第 13 节为最终门禁） |
| [docs/IN_GAME_VERIFICATION.md](docs/IN_GAME_VERIFICATION.md) | 用户实机验证清单（装载、放置、P1 作物、维护、存档、冒烟命令） |
| [tools/smoke/README.md](tools/smoke/README.md) | 游戏冒烟脚本的三种模式、安全边界与 IL 取证 |
| [docs/workstreams/](docs/workstreams/README.md) | 本轮四条工作流的证据报告（farm-loop / tech-data / ui-panel / verify） |

## 7. 已知偏离（摘要）

1. **存档方式**：使用 Timberborn 原生实体持久化，而非需求文档 12.1 的独立 `HDBF` 节点。理由与等价性验证见 ADR-001。
2. **装配链**：`Gameplay → Persistence`（文档写的是 Gameplay 只依赖 Core），原因是构建环境限制 + 组件必须自己实现 `IPersistentEntity`。见 ADR-002。
3. **美术**：四档已随包交付自定义 `.timbermesh`（ART-02/03：8 个成品 LOD0 + 8 个施工阶段模型，位于 `data/Buildings/Food/BoxFarm/`，蓝图 `TimbermeshSpec.Model` 已指向自定义模型，四档图标已替换）；**round 30–33 实机确认四档都成功载入并渲染**（截图在 `docs/in-game/`）。路径/材质/LOD/slot spec/图标契约记录在 `data/ART_PLACEHOLDER.md`。**材质仍为原版 MaterialCollection 资产名**，由游戏以原版 atlas 采样 ART-01 的逐面 UV（远看材质类别正确、近看拼布），自订 PBR 材质/AssetBundle 待 Unity；施工阶段外观、LOD 切换与 50% 同框 A/B 仍待实机会话。见 ADR-003。
4. **工作区/部署分离**：工作区不是 mod 目录，`build.ps1 -Install` 部署到 `Documents\Timberborn\Mods\HDBF`。见 ADR-006。
5. **P1 作物循环（本轮接通）**：零 Harmony 补丁，自定义 workplace behavior 驱动原版 worker 侧种植/收获；产物进 `SimpleOutputInventory` 由原版搬运。**已知近似**：灌溉/疲劳/维护/动力四层以“派发门禁 + 生长通道”生效，密植 ×1.35 实现为真实生长加速；严惩“让原版作物变慢”需要私有 API 补丁，v0.1 不取（D-2/D-4）。见 ADR-004 与 `docs/workstreams/farm-loop.md`。
6. **科技前置条件**：1.1.2.8 无 research node/前置机制，四档只由 `ScienceCost` 200/600/1000/3000 解锁；第 7 章前置链记录为文档级偏离 D-3。见 ADR-007。
7. **构建命令**：本机没有 `pwsh`、且脚本执行策略默认阻止未签名脚本，请用
   `powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1`（README §3 的命令已按此修正）。
