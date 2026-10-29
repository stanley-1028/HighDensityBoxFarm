# HDBF v2 变更记录

> **状态**：2.5.1 已实现（离线门禁全绿；实机证据见 VERIFICATION §29、§30）
> **日期**：2026-10-29
> **适用版本**：2.5.1
> **变更摘要**：v2.1 需求引入室内农场方案；本文件记录 v1→v2 的行为/数据/工程变更与实现状态。

## [2.5.1] — 2026-10-29（两个玩家 bug 修复：叠层农场不播种、600 槽农场只送出 50 个作物）

### 修复 1：叠放/升高层的农场永远不播种，「正在生长」一直为 0

#### 变更（玩家 bug 回報：「生長中」只在种植槽种满时才显示正在生长的数量）

* 【修复】玩家回報「面板的『生長中』只有在種植槽種滿農作物時才會顯示正在生長的農作物數量」。
  顯示/資料鏈本身沒有「種滿才顯示」的條件（第一格種下就出行，見 REQ-V2-UI-05 與 VERIFICATION §28），
  真正原因是**入口判定錯誤**：`HdbfBoxFarmController.ReadEntranceBlocked` 原本要求門口那格上有
  **Path 方塊物件**，但這只對「門口就在路面層」的農場成立。屋頂是真正的樓層（REQ-V2-BLD-04），
  疊在另一座盒子農場屋頂上的農場，其門口前方是**屋頂格**而不是 Path，於是被永遠判定「入口堵塞」，
  每日農事（補種）完全不執行 → 槽位永遠空、面板永遠 0，而地面上那座（門口有 Path）正常顯示，
  玩家於是看到「只有種滿的那座才顯示」。
* 【修改】入口判定改用**遊戲原版定義**（`Timberborn.Buildings.BuildingBlockedAccessible.IsBlocked`）：
  `INavMeshService.AreConnected(門口格, 門前台階格)`。導航網格連通即視為可通行（屋頂/樓層也算），
  真正被牆堵住的入口仍然判定堵塞並照舊暫停農事、顯示「入口堵塞」；REQ-V2-BLD-02 的
  「入口前方至少 1 格通路」因此對所有樓層都成立。
* 【依賴】控制器不再注入 `IBlockService`（唯一用途就是上述錯誤判定），改注入 `INavMeshService`
  （遊戲單例，Bindito 已綁定）。
* 【測試】L1 新增 2 條回歸：`V2GameplayContractTests.TestEntranceProbeUsesNavMeshConnectivityNotAPathObject`
  釘住入口判定必須用導航網格連通、不得再要求門口 Path；`V2CropReportTests.TestEveryTierReportsCropsFromTheFirstPlantedSlot`
  對四檔各驗證「只種 1 格也立即產出作物列」。L1 606/606、發布門禁 PASSED。

### 修复 2：600 槽农场收获后只送出 50 个作物

#### 变更（玩家 bug 回報：Steam Workshop 留言）

* 【修复】玩家回報「600 個玉米成熟後只有 50 個送進倉庫，其餘完全沒有運出」。根因不是搬運鏈壞掉，而是
  建築輸出庫存沿用原版農家的固定容量 **50**：本模組是**每日批量收穫**，50 的緩衝只夠收約 25 槽，
  之後所有槽位卡在成熟、`blockedByOutput` 一直累積，玩家只看到 50 個作物離開農場。
* 【修改】四檔 `SimpleOutputInventorySpec.Capacity` 改為**整盒一次收成的上限** `槽位上限 × 4`：
  基礎 240、中級 600、高級 1200、頂級 2400（4 = 最高原版作物基礎產量 3 × 密植 1.35 四捨五入；
  疲勞與模式過渡只會降低產量，因此任何一槽最多 4 件）。一盒收成永遠放得下，瓶頸只剩搬運，
  不會再出現「收不了、送不出」的卡死。
* 【修改】面板「輸出」列的容量改為依快照槽位上限推算的每檔容量（`HdbfIndoorPanelLimits.OutputCapacity`），
  不再寫死 50；`HdbfIndoorPanelLimits.OutputFill` 同步接受槽位上限。
* 【修改】`tools/validate` 的 inventory 檢查由「四檔同為 50、範圍 [20,100]」改為「每檔 == PlotCapacity × 4」；
  `V2BlueprintContractTests` 與 `V2PanelContractTests` 以同一條規則釘住資料/面板，資料再漂移會紅燈。
* 【測試】L1 契約更新（面板 12/600、藍圖容量 = 槽位 × 4）；全套 L1 **606/606 PASS**、`tools/validate` OK、
  `build.ps1` **RELEASE GATES: PASSED (44 checks)**。

## [2.4.2] — 2026-10-29（修复「正在生长」卡看不到 / 数量不够显眼）

### 变更（REQ-V2-UI-05）

* 【修复】玩家实机反馈「面板找不到正在生长卡、生長中的数量不会动态显示」：原因是该卡原本排在
  「槽位」卡之后，1080p 高度下 325 px 面板需要滚动才看得到。现在**卡片移动到「工人与效率」卡正下方**
  （位置 2），选中建筑即可直接看到。
* 【新增】卡片顶部新增**总览列**「已种植槽位 n / capacity」+ 平均生长进度条与百分比：数量与进度都由
  快照实时计算、4 Hz 脏标记刷新，种下第一格就出现；下方再列出每个作物一行。
* 【修改】本地化新增 2 个键（总览列标签/数值），三语 193 键集合一致。
* 【测试】`V2PanelContractTests` 更新卡片顺序与总览列断言；L1 601/601、44 条发布门禁 PASSED。

## [2.4.1] — 2026-10-29（面板「正在生长」实时作物卡）

### 变更（REQ-V2-UI-05）

* 【新增】面板新增「正在生长」卡片（排在 Slots 之后、Water 之前）：当前每个占用槽位的作物一行——
  作物名、生长中/待收槽数、平均生长进度条（成熟槽按 100 % 计入平均值）。卡片**从第一格种下就出现**，
  随既有 4 Hz 刷新实时递增，不再等种植槽种满才显示；无作物时显示明确的空状态行。
* 【新增】Core 纯函数 `IndoorCropReport.Build` + `IndoorCropStatus`：单趟扫描槽位聚合，按阵营作物表顺序
  稳定输出（未知作物 id 追加并按 ordinal 排序），非有限数值按 0、成熟按 100 %，坏 spec/坏 catalog 只降级
  该行，不影响其他行或面板（REQ-V2-SAFE-01）。
* 【修改】`IndoorFarmSnapshot` 增加 `Crops` 初始化属性（位置参数不变，既有构造点无需改动）；面板指纹纳入
  作物行数据（按 1 % 量化），数值变化才重绘，空闲面板仍不做格式化/写入。
* 【修改】三语新增 4 个本地化键：卡片标题、逐作物计数值、空状态、悬浮说明（键集三语一致）。
* 【测试】新增 `V2CropReportTests`（聚合、成熟=100 %、稳定排序、未知作物、损坏值、行数上限、确定性），
  更新 `V2PanelContractTests` 的卡片顺序与快照字段契约。L1 601/601、`tools/validate` RESULT: OK、
  `build.ps1` 44 条 release gates PASSED；实机 round57 证据见 `docs/VERIFICATION.md` §28。

## [2.4.0] — 2026-10-28（即时补水 + 农夫在室内工作）

### 变更（REQ-V2-WATER-02/04, REQ-V2-LABOR-03, REQ-V2-PERF-02）

* 【修改】补水即时生效：点击「补水」时不再排队等下一个每日 tick，而是在同一指令内从已校验的
  真实水源取水并立刻加进水罐（仍消耗当日 2 个农事动作，且计入当日预留动作，绝不过支预算）；
  面板提示从「已请求补水」改为「补水完成」。
* 【修改】搬运来的水立刻入罐：`ApplyHauledWater` 现在也由连续水流服务每帧调用，工人刚搬进
  `Inventory:Water` 的水货物当帧变成水罐水量，不再等每日 tick 或面板刷新。
* 【修改】农夫在室内工作：农场模板新增原版 `WaitInsideIdlyWorkplaceBehavior`（简单工坊同款），
  在岗农夫会从入口进入建筑内部并在里面待岗，而不是在室外闲置游荡；室内种植/采收仍由模组的
  室内模拟完成（建筑为封闭 3×3 盒子，内部动作以模拟状态体现）。
* 【测试】`V2GameplayWiringTests` 增加即时补水/室内待岗/每帧入罐的源码契约；L1 全绿、
  44 条发布门禁 PASSED。

## [2.3.0] — 2026-10-28（水耗连续化 + 水罐有水时自动种满）

### 变更（REQ-V2-WATER-02, REQ-V2-LABOR-01, REQ-V2-PERF-02）

* 【修改】水耗由「每日 tick 一次性扣水」改为「按经过的游戏时间连续扣水」：
  `IndoorWaterMath.DemandForElapsedDays`（日需求 × 经过的游戏日）+ `Consume`（水罐不为负、
  修复损坏值）。Gameplay 新增 `HdbfWaterFlowService`（`IUpdatableSingleton`）每帧读取
  `IDayNightCycle.PartialDayNumber` 的增量并交给已完工农场；暂停不扣、倍速按比例、
  一整天恰好扣 `DemandPerDay`（帧率无关）。每日 tick 不再扣水，只修复/读取实时水罐并算覆盖率。
* 【修改】水罐有水时，空槽由工人自动种满、不再受每日 13 动作/人的播种预算限制（收获仍受预算
  限制，收获后同一 tick 即补种）；水罐没水则不播种。`IndoorLaborMath.RunActions` 新增
  `waterAvailable` 重载，旧的 6/8 参数重载保持原行为（向后兼容）。
* 【测试】更新 `V2WaterTests`（连续扣水/夹取/修复值）、`V2TickOrderTests`、`V2SlotFaultIsolationTests`
  （自动种满 37 槽）并新增连续扣水切片用例；L1 全绿、44 条发布门禁 PASSED。

## [0.2.1] — 2026-10-27（UI v2.4：按实机截图修正面板）

### 变更（REQ-V2-UI-01/02/03/04, REQ-V2-READ-04）

* 【删除】fragment 内的第二个头部（建筑图标+名称+档位·阵营+模式徽章）：原版 `EntityPanel` 已显示
  图标/名称/地区，重复头部连同其 NineSlice 空框与模式徽章一并删除；档位/阵营收进原版 header 下方的
  一行紧凑 subtitle row（`tier · faction`），不再重复建筑名。
* 【修复】棕色/米黄大底、深色文字、底部重叠：根因是 `AddToClassList("a b")` 只添加一个带空格的
  无效类名（面板因此退回默认深色文字/列布局），且透明 fragment 让 EntityPanel 的 tan 背景从卡片
  间隙与滚动条槽透出。现在所有多类名逐条添加（`HdbfPanelControls.AddClasses`）、卡片满宽且无间距、
  滚动条 `ScrollerVisibility.Hidden`、根容器透明。
* 【修复】全部面板文字统一暖白 `#F2EBDD`（`HdbfIndoorPanelStyle.LightTextRgba` +
  `HdbfPanelControls.ApplyLightText`；含原版 crop 下拉选中项与打开列表的重绘）；警告仍保留
  图标 + `▲/!` 文本标记 + 3 px 色条三编码。
* 【补回】水（水罐条/覆盖率 C 条 + C_min 标记/C_min/补水来源/缺水减产警告）、维护（到期日、逾期周期、
  材料需求行、缺料明细、停滞警告）、原版 RadioToggle 模式组 + 1 日过渡提示、输出占用原版 ProgressBar；
  输出端口与管道/物流/动力接口拆成独立行，全部文本可换行不再截断或重叠。
* 【修复】空缺工人 chip 使用原版 vacant 空头像（禁用、无警告三角）；⚠ 仅用于真实问题。
* 【新增】三语键 `HDBF.Panel.MaintenanceMaterialLabel` / `...MaterialValue`（enUS/zhCN/zhTW 同为 187 键）。
* 【测试】新增 `V2PanelVisualContractTests`（第二个 header/棕底/白色文字/ProgressBar/RadioToggle/
  按钮/端口分行/空缺 chip/三语键契约）；更新 `V2PanelContractTests`、`V2UiLiveWaterContractTests`、
  `V2LocalizationTests`。L1 `PASSED 569 / FAILED 0`；HDBF.UI/HDBF.Gameplay 0 warning / 0 error。
* 【修复·审计】原版进度条色调类 `progress-bar--green/red` 曾被与基类拼成一个字符串传给
  `AddToClassList`（整串变成无效类名，填充色不生效）；现按单类逐个添加，条形恢复原版绿/红填充。
* 【修复·审计】原版 crop 下拉的选中项与打开的下拉列表 Label 自带 `.dropdown-item__text` 灰色
  （弹出列表由单例 `DropdownListDrawer` 持有，不在 Dropdown 子树内）：选中项在每次
  `SetItems`/`UpdateSelectedValue` 后重新着白，弹出列表在 `Showed` 后经 panel 树重新着白；
  固定 28px 的列表行保持原版 nowrap/ellipsis（面板行本身仍换行，D/H 才真正达标）。
* 【修复·审计】`WorkerView` 模板缺失时 fallback chip 的姓名 Label 补上 `name="Name"`。
* 【修复·审计】`HdbfIndoorCommandBridge` 复用 4 Hz 查询的 invoke 参数数组；README 修正“空闲零分配”
  表述并登记 gameplay 侧每次查询新建快照的已知边界。
* 【测试·审计】新增 5 项回归（多类名直传 `AddToClassList`、进度条单类色调、下拉重绘、fallback chip、
  含引号逗号的 CSV 解析完整性）。
## [2.2.0] — 2026-10-27（美术/UI 修订；与 0.2.0 行为一致）

* **版本号对齐**：此前 mod 版本为 `0.2.0`，而美术/UI 修订在工程内部用 `v2.2` 命名，造成混淆；
  自本版起 mod 版本统一为 **2.2.0**，与「需求 v2.1 + 美术/UI 修订 2.2」对齐。存档/配置 schema 不变（2）。
* 【修改】美术：参考图驱动的材质分区/开窗比例/室内货架剪影（16 个 `.timbermesh` 重导出，128/128 校验）；
  四档图标从新模型重渲染（`data/Icons/BoxFarm*.png`）。
* 【修改】面板：改为原版 UXML/USS 控件；删除重复头部与棕/米黄大底；文字统一暖白 `#F2EBDD`；
  水罐/覆盖率/维护/动力/模式/策略/输出分区与按钮补齐；实时覆盖率（满罐=100%，不再载入显示 0%）。
* 【修复】材质/贴图：原 flat-patch UV 假设所有材质共享同一张 2048 atlas，实机出现棋盘拼布与
  黄黑警示条。改为**逐材质使用其真实原版 `_D` 贴图**的低方差 patch（`art/material_texture_map.json`
  记录材质→贴图→patch→mean/std），预览/图标改用同一真实贴图；禁止 `PaintedMetal` 用于大面积外壳。
  16 个 `.timbermesh` 重导出，验证器新增 C11–C14 + 负向对照；L1 577/577、44 gates PASS。
* 【修复】贴图清晰度：大面（木/板/石膏/金属/屋面）改为**原生分辨率 world-scale 平铺** UV
  （贴图 Repeat，按 `tile_meters` 控制每米纹素密度），玻璃/细节等 atlas 材质继续 patch；
  预览/图标使用同源真实贴图并加 50%/100% 近景；验证器新增纹素密度/UV 契约与 6 个负向对照。
  L1 585/585、44 gates PASS。
* 门禁：L1 564/564、`build.ps1` 44 checks PASS。

## [0.2.0] — 2026-10-26（v2.1 实现完成；实机覆盖范围见 VERIFICATION §26.3）

### 变更（相对 v1.0.0 / 0.1.0）

* 【作废】世界格扫描、托管格注册、工作范围（11×11…27×27）、地块高亮/框选、世界地块图标。
* 【作废】外部土壤水分采样与「灌溉覆盖率来自外部地块」的公式入口。
* 【作废】原版种植/收获任务派发与 `PlantingService` / `NaturalResourceFactory` 调用。
* 【作废】托管作物可见性补丁与 ADR-009 的 Harmony 生长补丁（v2 目标 **0 Harmony**，已达成）。
* 【修改】四档占地统一 **3×3×1**（原 3×2×1）；屋顶改为**平的、可通行、可叠放**。
* 【新增】室内槽位模型：60/150/300/600；`P_eff = min(Capacity, 在岗工人 × 38)`。
* 【新增】室内生长模拟：`G_growth = G_irr × G_maint × G_power`；疲劳/轮作/密植/休耕沿用 v1 数值。
* 【新增】内部水罐 + 管道/人工补水；覆盖率 `C` 与 `G_irr` 由纯函数计算，每日结算一次。
* 【新增】存档 schema v2（单条原生持久化负载 + 写前校验 + v1→v2 尽力迁移 + 失败安全默认）。
* 【修改】配置 schema **2**（v1 配置可读并迁移；未知键忽略并记日志）。
* 【修改】建造工时 **6/10/16/28 → 2/3/5/8 工日**（v2.1，只影响建造等待时间）。
* 【新增】§19.18 非功能需求：文档集、代码标记、日志键、模块化、扩展点、存档/崩溃安全门禁。
* 【新增】`docs/v2/` 文档集（README/ARCHITECTURE/INTERFACES/ADRS/TRACEABILITY/CHANGELOG）。

### 实现与门禁（本机实测，2026-10-26）

* 5 程序集 0 warning / 0 error；`L1 PASSED 529 / FAILED 0`；`tools/validate RESULT: OK`；
  `RELEASE GATES: PASSED (44 checks)`（0 Harmony）；`NFR GATE MATRIX: PASSED`。
* 100×600 日 tick 0.0113 ms/栋（预算 0.5）、0 bytes 配置；100×600 存档 1.05 MB（预算 1.5 MB）、
  编码 34.8 ms（预算 50 ms）；30/144 fps 结果一致。
* 3×3×1 蓝图（9 块、`Stackable: BlockObject`、`GenerateFloorsOnStackable`、建造 2/3/5/8 工日）；
  16 个 `.timbermesh` 全部 3.000×1.000×3.000、平屋顶、3 端口、无 slot 节点（128/128 校验 PASS）。
* 【修改】美术升级（参考图驱动）：四档材质分区（木框/玻璃分格/金属铆接/木补丁/苔藓）、开窗比例
  （Intermediate 侧墙玻璃约 45–55%）、室内静态货架剪影、齐平入口与后立面端口；16 个 `.timbermesh`
  重导出（LOD0 面数 5,850–5,918，预算内），128/128 美术校验 PASS，预览见 `art/previews/`。
* 实机（round41/42）：模组 0.2.0 加载、v2 记录读取/修复、室内日循环（每日一次水耗、覆盖率）、
  收获/播种/动作预算、输出满积压、缺 Timber Pipes 降级、配置 schema 2 加载。
* 未实机：真实建造/拆除、屋顶叠放 A/B、面板交互、人工补水正路径、维护材料扣料、顶级接电、
  游戏内存档往返、100 栋 Profiler、五模组矩阵、50% 四档同框 A/B（见 VERIFICATION §26.3）。

### 迁移说明（玩家）

* v1 存档：尽力迁移槽位/水罐/维护/模式/端口；**不删除**玩家原有地块与原版作物，
  旧农田恢复为普通原版农田；迁移失败时建筑以「空槽 + 满水罐 + 默认维护」安全载入并记警告。
* 已建 v1 农场建议拆除重建以获得 v2 行为（旧建筑不保证外观/端口一致）。

### 工程说明

* 构建工作根：本机沙箱下使用 `%TEMP%\hdbf-<tag>`（ADR-V2-005），不改变发布包结构。
* 发布门禁：`build.ps1`（5 程序集 0/0 → L1 全绿 → `tools/validate RESULT: OK` → 44 条 release gates）。

## 后续（未发布）

* 阶段 4 剩余实机证据（TC-V2-01 放置/拆除、TC-V2-02 屋顶叠放 A/B、TC-V2-05/06/07/08/09/10 的
  互动部分）逐条填入 `docs/VERIFICATION.md`；未实机项不得标「完成」。
