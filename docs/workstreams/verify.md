# verify 工作流报告 — 测试验证与游戏内冒烟（task-4）

日期：2026-10-24 夜（Asia/Macau）。范围：`tests/**`、`tools/smoke/**`、`docs/VERIFICATION.md`、
本文件。**未改** `src/**`、`data/**`、`build.ps1`、`docs/REQ_TRACEABILITY.md`。

一句话状态：**离线门禁全绿（5 程序集 0 warning/0 error、L1 259/259、数据校验 RESULT: OK、暂存
303 KB 且与部署逐文件一致）；游戏内自动化冒烟在本机被证明不可行（batchmode 5.1 s 自行退出，未加载
模组）；当前 303 KB 包从未被游戏成功加载 —— 下一步只能靠用户白天的窗口化实机会话。**

---

## 1. 交付物

| 文件 | 作用 |
|---|---|
| `tests/HDBF.Tests/SaveRoundTripFaultTests.cs`（新，21 条） | 存档往返/迁移/损坏注入/隔离/确定性 fuzz |
| `tests/HDBF.Tests/SaveBudgetTests.cs`（新，2 条） | 100×600 的存档体积与 codec 热路径预算 |
| `tests/HDBF.Tests/UiContractTests.cs`（新，14 条） | 警告严重度契约、单一产出乘积、canonical 数字格式、劳动上限 |
| `tests/HDBF.Tests/FarmPlannerTests.cs`（新，22 条） | P1 `FarmWorkPlanner` 生产源码的排序/工时/劳动上限 |
| `tests/HDBF.Tests/Stubs/UnityEngineVector3IntStub.cs`（新） | 仅为把纯逻辑生产源文件编进测试程序集的结构占位 |
| `tests/HDBF.Tests/HDBF.Tests.csproj` | 新增 `<Compile Link>` 引用 `src/HDBF.Gameplay/Farming/FarmWorkPlanner.cs` |
| `tools/smoke/Invoke-GameSmoke.ps1`（新，v1.1） | 部署检查 + Player.log 分析 + 窗口化启动 + 安静 batchmode autostart |
| `tools/smoke/README.md`（新） | 用法、安全模型、batchmode IL/文档证据、能/不能证明的边界、证据格式 |
| `docs/VERIFICATION.md` §13（新增） | 本轮权威构建/测试/校验/冒烟证据与未验证项 |

既有 9 个套件 200 条未改；总测试 200 → **259**，套件 9 → 13。

---

## 2. 权威命令与结果

### 2.1 构建门禁（Lead 要求由本轮作为最终构建证据；未加 `-Install`）

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1
```

原样输出（完整 stdout：`%USERPROFILE%\.hdbf\smoke\build-gate-20261024-225329.log`）：

```
已成功生成。  (×5 项目)
    0 个警告
    0 个错误

HDBF L1 test run - 2026-10-24T14:53:55.5179137Z
[budget] 100 x Basic/60: estimated JSON bytes total=154,700 (0.15 MB); chapter 12.7 band 0.10-0.15 MB
[budget] 100 x Top/600: estimated JSON bytes total=1,270,700 (1.21 MB), per building min=12,707 max=12,707
[budget] chapter 12.7 bands: per building 10-14 KB, 100 buildings 1.0-1.4 MB; chapter 15.3 says <=256 KB for 100 buildings (contradiction, see docs)
[budget] size scaling 600/150 plots = 3.73x (linear reference 4.0x)
[budget] 100 buildings x 600 plots: sanitize+migrate+canonicalize best=15.1 ms; canonicalize-only best=12.8 ms; chapter-12.7 serializer budget=50 ms (proxy only, not the game serializer)

PASSED 259  FAILED 0

HDBF data validator
  game assemblies : 660
  spec types      : 506 (game + mod ComponentSpec subclasses)
  blueprints      : 10
WARNINGS: none
RESULT: OK - data/ is valid.

Staged mod package: C:\Users\user\.hdbf\work\stage\HDBF
  blueprints : 10
  size       : 303 KB
Package safety gate: workspace has no manifest and no stray DLLs.
```

部署核对：`Mods\HDBF` 与暂存包同为 29 files / 310,766 B；5 个 DLL SHA-256 逐一相同。
（历史章节的 266 KB / 199 tests 是上一轮数字。）

### 2.2 单独跑测试

```powershell
dotnet run --project tests\HDBF.Tests -c Release -m:1
# PASSED 259  FAILED 0  (exit 0)
```

### 2.3 单独跑数据校验

```powershell
dotnet run --project tools\validate -c Release -m:1 -- data `
  --managed "C:\Program Files (x86)\Steam\steamapps\common\Timberborn\Timberborn_Data\Managed" `
  --ref "$env:USERPROFILE\.hdbf\ref" --assemblies "$env:USERPROFILE\.hdbf\work\bin"
# RESULT: OK - data/ is valid. (exit 0)
```

注意：validator 的 `--assemblies %USERPROFILE%\.hdbf\work\bin` 会把 `bin` 下所有程序集算进
“mod assemblies”（本次列出 7 个，包含 ApiIndex/HDBF.Tests）。不影响校验结论，但属于工具口径问题，
留给 Lead/工具维护者决定是否收紧为只认 5 个 HDBF 程序集。

---

## 3. 新增测试明细（59 条）

### 3.1 SaveRoundTripFaultTests（21）

| 测试 | 覆盖 |
|---|---|
| TestSaveRoundTripThreePassesIsFieldIdentical | sanitize→migrate 3 遍，逐字段 + canonical 一致（AC-06/12.4） |
| TestSaveRoundTripKeepsLargeEntityIdAndPorts / …FatigueAndExclusionsOrder | 2^32 级 entityId、三端口、adapter 字典、注册顺序 |
| TestMigrateV0ThreePassesIsFieldIdentical / …KeepsExistingDueDay / …NewerSchemaKeepsStamp | v0→v1 三次逐字段幂等；已有 DueDay 保留；新版本号只读不降级（REQ-TEST-06） |
| TestMigrateV0AfterSanitizeFillsDefaultsExactlyOnce | v0 缺 fatigue/excluded/adapters 数组：修复后空数组、0 疲劳、schema=1；**并记录 sanitize 先归一化 schema 导致 DueDay 不被补**（见 §5.1） |
| TestSanitizeTruncatedPlotIdsDropsOrphanCropAndFatigueEntries / …TruncatedCropsFillsMissingEntriesWithEmpty / …TruncatedFatigueFillsZero | TC-11 截断：保留存活 ids，孤儿 crop/fatigue 丢弃；短数组补空/补 0 |
| TestSanitizeFatigueOutOfRangeVariantsAreClamped | 5/-2/NaN/±Inf/1.0001 全部落回 [0,1] 且邻居不变 |
| TestSanitizeInvalidTierIsReplacedByProfileTier / …NonFiniteDueDayIsRescheduled / …NegativeCyclesMissedIsReset / …NegativeAndInfiniteTankIsReset | 非法枚举/非有限值修复 |
| TestSanitizeCombinedDamageRepairsEveryFieldWithoutThrowing | 多字段同时损坏仍不抛、逐项修复 |
| TestSanitizeNullCollectionsAreTreatedAsEmpty / …NullPlotsWithOrphanCropsDropsEverything / …DropsEmptyAdapterKeysAndKeepsNullValuesEmpty | 缺字段（null 集合）与 adapter 空 key |
| TestDamagedBuildingDoesNotAffectHealthyBuilding | 单建筑隔离 + 不修改调用方对象（REQ-TEST-07 第 3 点 / REQ-SAVE-09） |
| TestDeterministicDamageSweepAlwaysYieldsValidRecord | 固定 LCG 种子 200 轮：不抛、数组等长、id 唯一、fatigue∈[0,1]、tank∈[0,cap]、excluded 不在 ids、既有枚举/有限值、修复后二次 sanitize 必须干净且 canonical 幂等 |

### 3.2 SaveBudgetTests（2）

* `100 × Top/600`：逐栋估算 12,707 B（chapter 12.7 的 10–14 KB 带内）、总计 1,270,700 B = 1.21 MB
  （12.7 的 1.0–1.4 MB 带内）；600/150 体积比 3.73×（线性参考 4.0×，防“每格一个对象”）；
  反射断言 `PlotIds/CropIds/Fatigue/ExcludedPlotIds` 仍是并行数组；sanitize+migrate+canonicalize
  100 栋 600 格热路径 best 15.1 ms、canonicalize-only 12.8 ms（代理，不算 50 ms 实测）。
* `100 × Basic/60`：154,700 B（12.7 的 0.10–0.15 MB 带内）——这更像 15.3 的 256 KB 想描述的场景。

### 3.3 UiContractTests（14）

严重度数值契约（Critical=0..Info=4）与 ordinal 排序；`FarmMultipliers.Total` 五层乘积（0.105 /
1.35 / 任一层为 0）；`FarmWarning` 只带 loc key + 两个数字；canonical 数字（12.3456→`12.346`、
12345.5→`12345.500`、无千分位/无小数逗号、跨 culture 稳定）；`HdbfMode` 恰为 3 值；劳动上限
（0 工人=0 格/0 行动、1 工人=min(cap,38)/13、满编 P_eff/C_day、13×W 线性且 int 溢出饱和、
P_eff 永不超过容量与劳动力）。**没有伪造** warning 排序/面板格式化的实现替身（那两层在游戏程序集里，
见 §5.3）。

### 3.4 FarmPlannerTests（22）

针对 `src/HDBF.Gameplay/Farming/FarmWorkPlanner.cs`（farm-loop 本轮新增的 P1 规则）：

* 排序：Harvest(0) < Plant(1)，同级按实测路径升序、再按 plot id；未测量（-1）在同级排最后；
  `CompareMeasured` 是全序；`CompareEstimated`=优先级→入口距离→id。
* 工时：`ActionWorkCost` = 0.08×(1+path/20) × 模式乘数（Normal 0.08/0.12/0.16；Dense ×1.2；
  Fallow ×0.2；负路径按 0；可配置 denseWork）。
* 每日预算：13 行动 + 1.04 工日/工人；14 次被行动上限拒绝；path=100 时只能做 2 次；
  预算不足/0 工人返回不能；剩余工日被尊重。
* 派发：跳过已认领、未测量、付不起的候选；空/负预算返回 -1；null 入参安全。
* 配额：`MeasurementQuota` = 2×行动+16，cap=512；覆盖每个档位满编的行动预算。
* `SortMeasured` 对 null/单元素安全、重复调用稳定。

技术说明见 §5.2。

---

## 4. 游戏内冒烟工具（tools/smoke/Invoke-GameSmoke.ps1）

### 4.1 三种模式

| 模式 | 行为 | 启动游戏 |
|---|---|---|
| 默认 | 部署检查（manifest/5 DLL/数据/SHA-256 对暂存包/workspace 安全门）+ 当前 Player.log 分析 | 否 |
| `-BatchMode` | 上述 + 校验 batchmode autostart 的源存档并打印完整命令行 | 否 |
| `-Launch` | 窗口化启动，抓取增量 Player.log，结束只关自己启动的 PID | 是（有声有窗） |
| `-Launch -BatchMode` | 复制 `Saves\110000` → `Saves\HDBFTest`，`-batchmode` autostart 读副本，抓日志后删副本 | 是（无窗口） |

退出码 0/1/2/3/4（DryRun OK / DryRun FAIL / 前置缺失 / 启动 FAIL / 安全拒绝）。

### 4.2 安全承诺与实现

* 已有 `Timberborn` 进程、部署失败、源存档非法、`Saves\HDBFTest` 存在但无脚本所有权标记
  （`.hdbf-smoke-owned`）→ 拒绝启动（exit 4），绝不按进程名杀进程。
* 源存档目录运行前后做全文件 SHA-256 清单对比；历史 `Player.log` 也做前后 SHA-256；任一变化 →
  判 FAIL（这是本轮的硬安全检查，已验证 true/true）。
* batchmode 用 `Stop-Process -Id <本次 PID>`（无窗口可关）；窗口化先 `CloseMainWindow` 再兜底。
* 不点击、不读档 UI、不写真实存档；唯一写盘是 JSON/日志副本/临时副本目录。

### 4.3 batchmode 机制（IL 取证）

* `Timberborn.MainMenuScene.AutoStarter` 读 `saveName`/`settlementName`（`CommandLineArguments`
  自动加 `-`），条件 `!Application.isEditor && Has("saveName")`，然后
  `StartSaveGameInstantly(new SaveReference(saveName, new SettlementReference(settlementName,
  Path.Combine(UserDataFolder.Folder, "Saves"))))`。
* `GameSaveRepository.SaveNameToFileName` = `<SaveDirectory>\<settlement>\<saveName>.timber`，
  `SaveExtension=".timber"`，`GetSaves` 去掉扩展名 → `-saveName` 不带扩展名。
* `ExperimentalMode` 仅 `-experimental` 时改用 `ExperimentalSaves`；默认 `Saves`，所以临时目录是
  `Documents\Timberborn\Saves\HDBFTest`。
* Unity 文档：`-batchmode` 无显示无输入；`-nographics` 关闭输出日志 → 脚本不加 `-nographics`，
  否则没有证据。`UnityPlayer.dll` 含这些 flag 字符串；660 个游戏程序集都没有解析它们。

### 4.4 实际执行结论（Lead 执行，verify 未自行启动）

命令见 `docs/VERIFICATION.md` §13.3.3。结果：PID 12488，**5.1 s 后自行退出 exit 0**；45 行日志中
0 条 `[HDBF/Info]`、0 条 `Opening file:`；只有源码目录的 `No manifest file found` 警告。脚本判
FAIL(exit 3)，并确认源存档/历史日志未变、临时副本已删除。

**结论：本环境无法用命令行自动化完成游戏内验证**（batchmode 在 mod 加载前自行退出）。不重试。
窗口化人工会话是唯一路径。

---

## 5. 缺口、发现与建议

### 5.1 v0 迁移顺序（发现，未改 src）

`HdbfSaveCodec.TrySanitize` 先（因 schema<=0）把版本戳改成 1，再调用 `Migrate`，因此
“v0 DueDay=0 → 当前日+周期”的分支在 sanitize 路径被跳过，`DueDay` 保持 0 → 维护会在首次 tick
被判为逾期。直接 `Migrate(v0)` 路径正常。v0 原型存档从未发布，故不影响任何玩家；测试按实际行为
断言并注释。若未来要兼容 v0，建议把“v0 默认值补齐”移到 schema 归一化之前。

### 5.2 P1 逻辑的离线测试方式（建议保持）

`FarmWorkPlanner.cs` 位于 `HDBF.Gameplay`（游戏程序集），但除 `FarmWorkItem.Cell` 的
`UnityEngine.Vector3Int` 字段外是纯逻辑。测试工程用 `<Compile Link>` 编入**生产源文件** + 一个
最小结构占位，不引用任何游戏程序集（REQ-TEST-03）。“测试的是复制品”在这里不成立：被编译的是
`src/` 下的同一份文件；文件一旦真的依赖 Unity，测试工程编译失败而不是静默跳过。
更彻底的方案（下一轮可考虑）：把 planner/预算类拆到 `HDBF.Core`（`FarmWorkItem` 留在 Gameplay），
这样连 stub 都不需要；需要 farm-loop/Lead 决定，本轮未动 src。

### 5.3 仍无法离线验证（诚实清单）

| 项 | 原因 | 需要的证据 |
|---|---|---|
| 四档建筑放置/建造完成/入口与工人行为 | 需运行中的游戏 | 窗口化实机第五轮（TC-01） |
| 播种/收获/产出库存/作物生长 | P1 任务派发与游戏地块/任务系统耦合 | TC-01/TC-33 实机 + 产出统计 |
| 维护扣料、机械并网 | 需库存/机械网络服务 | TC-05/TC-20 实机 |
| HDBF 组件存档往返（真实游戏 JSON） | `HdbfEntitySaveBridge` 依赖 Timberborn Persistence | TC-10/AC-06 实机存档 |
| UI 面板排序/格式化/交互、本地化运行时 | HDBF.UI 依赖 Timberborn.Localization/UIElements | TC-30 + 面板截图 |
| 警告排序/数字格式（真实实现） | 见 §5.3 原因（REQ-TEST-03 排除 L1） | 面板截图 |
| 模式切换冷却（1 游戏日） | 读游戏天数，无离线接缝 | TC-22 实机 |
| 性能（600 格/100 栋、帧率/扫描/存档耗时） | 需 Profiler | TC-13/14、15.3 阈值 |
| 兼容矩阵（5 模组组合/缺失/版本不符） | 本机未安装那 5 个模组 | 独立环境快照 |
| 自动化游戏内冒烟 | batchmode 本机不可行（§4.4） | 人工窗口会话 |

### 5.4 给 Lead 的建议（不含越权改动）

1. 部署：本轮暂存包 303 KB 已与部署一致；若你随后又改 src/data，请再跑一次 `-Install`，并用
   `tools/smoke/Invoke-GameSmoke.ps1`（DryRun）的 `deploy.matchesStage` 复核。
2. `docs/REQ_TRACEABILITY.md` 状态建议维持“待游戏内验证”，直到窗口化实机第五轮通过。
3. 需求冲突（12.7 的 1.0–1.4 MB vs 15.3 的 ≤256 KB）建议在第 15 章验收口径里裁定；本轮测试
   同时打印两者，不替产品做选择。
4. 下次实机前删除/确认 `Saves\HDBFTest` 不存在（脚本只会删自己创建的副本）。
5. validator 的 `--assemblies` 口径建议收紧（见 §2.3），避免把测试/ApiIndex 程序集算进 mod 程序集。

---

## 6. 证据文件索引

| 文件 | 内容 |
|---|---|
| `%USERPROFILE%\.hdbf\smoke\build-gate-20261024-225329.log` | 本轮权威构建门禁完整 stdout（UTF-16LE） |
| `%USERPROFILE%\.hdbf\smoke\smoke-20261024-225429.json` | 最终部署状态的 DryRun（303 KB，SHA 全匹配） |
| `%USERPROFILE%\.hdbf\smoke\smoke-20261024-225324.json` | Lead 执行的 BatchModeLaunch 判定（FAIL，含安全隔离结果） |
| `%USERPROFILE%\.hdbf\smoke\batchmode-20261024-225324-Player.log` | 该次 batchmode 游戏日志副本（45 行） |
| `%USERPROFILE%\.hdbf\smoke\smoke-20261024-223956.json` | 首次 DryRun：保留第四轮 Player.log 的 HDBF 启动行与异常摘录 |
| `docs/VERIFICATION.md` §13 | 对外验证记录（命令、原样输出、未验证项） |
